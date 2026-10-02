using Nanook.GrindCore.ZStd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nanook.NKit.Container.ZArchive
{
    /// <summary>
    /// Reads ZArchive (.zar / .wua) files. ZArchive stores files in a filesystem tree with
    /// zstd-compressed 64 KiB blocks. The format index (footer, offset records, name table,
    /// file tree) lives at the END of the file, so a seekable stream is required.
    ///
    /// WUA specifics: the root directory contains one or more subdirectories named
    /// &lt;16-digit-titleId&gt;_v&lt;version&gt; (e.g. 000500001012d000_v0). Each title subfolder
    /// holds a decrypted WiiU CDN dump (code/, content/, meta/ and their .app / .tmd / .cetk
    /// files).
    ///
    /// Reference: https://github.com/Exzap/ZArchive
    /// </summary>
    internal class ZArchiveReader : IDisposable
    {
        // ── Format constants ────────────────────────────────────────────────

        public const int BlockSize = 0x10000;              // 64 KiB uncompressed block size
        public const int FooterSize = 144;                 // sizeof(Footer) from zarchivecommon.h
        public const int EntriesPerOffsetRecord = 16;      // blocks per CompressionOffsetRecord

        private const uint MagicValue   = 0x169F52D6u;
        private const uint VersionValue = 0x61BF3A01u;

        // ── Parsed footer ───────────────────────────────────────────────────

        private readonly Stream _stream;
        private long _compDataOffset;    // absolute file offset of the compressed data section
        private long _compDataSize;      // byte size of the compressed data section

        // ── Block index (offset records) ────────────────────────────────────

        // Each OffsetRecord covers EntriesPerOffsetRecord consecutive blocks.
        // baseOffset: absolute file offset of the first block in the record.
        // sizes[16]:  compressed size - 1 for each of the 16 blocks.
        private struct OffsetRecord
        {
            public long BaseOffset;
            public ushort[] Sizes; // [EntriesPerOffsetRecord]
        }
        private OffsetRecord[] _offsetRecords;

        // ── File tree ───────────────────────────────────────────────────────

        private struct Entry
        {
            public bool IsFile;
            public int  NameOffset;       // offset into _nameTable
            public string Name;           // resolved lazily

            // file fields
            public long FileOffset;       // byte offset within the concatenated uncompressed data
            public long FileSize;

            // directory fields
            public int ChildStartIndex;
            public int ChildCount;
        }
        private Entry[] _entries;
        private byte[]  _nameTable;

        // ── Block cache (LRU) ───────────────────────────────────────────────
        // Simple fixed-size circular cache: caches up to CacheCapacity decompressed 64KiB blocks.

        private const int CacheCapacity = 8;   // 8 × 64 KiB = 512 KiB
        private readonly long[] _cacheBlockIndex = new long[CacheCapacity];
        private readonly byte[][] _cacheData = new byte[CacheCapacity][];
        private readonly int[] _cacheLruOrder = new int[CacheCapacity]; // most-recent-use order, index 0 = MRU
        private int _cacheUsed;

        private readonly byte[] _decompBuffer = new byte[BlockSize];

        // ────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────

        private ZArchiveReader(Stream stream)
        {
            _stream = stream;
            for (int i = 0; i < CacheCapacity; i++)
            {
                _cacheBlockIndex[i] = -1;
                _cacheData[i] = new byte[BlockSize];
                _cacheLruOrder[i] = i;
            }
        }

        /// <summary>
        /// Opens a ZArchive from a seekable stream. Throws <see cref="HandledException"/> if the
        /// stream does not contain a valid ZArchive footer (wrong magic / version / size).
        /// Any seek failure (e.g. BufferStream size limit exceeded) propagates as-is.
        /// </summary>
        public static ZArchiveReader Open(Stream stream)
        {
            if (!stream.CanSeek)
                throw new HandledException("ZArchive requires a seekable stream (WUA files cannot be read from a non-seekable source).");

            long fileSize = stream.Length;
            if (fileSize < FooterSize)
                throw new HandledException("Stream is too small to be a valid ZArchive.");

            // Read footer (last 144 bytes)
            byte[] footer = new byte[FooterSize];
            stream.Seek(fileSize - FooterSize, SeekOrigin.Begin);
            readExact(stream, footer, 0, FooterSize);

            // footer layout (big-endian):
            //  [0..95]   6 × OffsetInfo (offset uint64 + size uint64)
            //  [96..127] SHA-256 hash (32 bytes, ignored here)
            //  [128..135] totalSize uint64
            //  [136..139] version uint32
            //  [140..143] magic uint32
            uint magic   = footer.ReadUInt32B(140);
            uint version = footer.ReadUInt32B(136);
            ulong total  = footer.ReadUInt64B(128);

            if (magic != MagicValue || version != VersionValue)
                throw new HandledException($"Not a valid ZArchive (magic=0x{magic:X8} version=0x{version:X8}).");
            if ((long)total != fileSize)
                throw new HandledException($"ZArchive totalSize mismatch (header says {total}, stream is {fileSize}).");

            // section offsets
            long compOff    = (long)footer.ReadUInt64B( 0);
            long compSize   = (long)footer.ReadUInt64B( 8);
            long recOff     = (long)footer.ReadUInt64B(16);
            long recSize    = (long)footer.ReadUInt64B(24);
            long namesOff   = (long)footer.ReadUInt64B(32);
            long namesSize  = (long)footer.ReadUInt64B(40);
            long treeOff    = (long)footer.ReadUInt64B(48);
            long treeSize   = (long)footer.ReadUInt64B(56);

            var reader = new ZArchiveReader(stream);
            reader._compDataOffset = compOff;
            reader._compDataSize   = compSize;

            // Read offset records
            int recCount = (int)(recSize / (8 + 2 * EntriesPerOffsetRecord)); // sizeof(CompressionOffsetRecord)
            reader._offsetRecords = new OffsetRecord[recCount];
            stream.Seek(recOff, SeekOrigin.Begin);
            byte[] recBuf = new byte[recSize];
            readExact(stream, recBuf, 0, (int)recSize);
            for (int i = 0; i < recCount; i++)
            {
                int off = i * (8 + 2 * EntriesPerOffsetRecord);
                reader._offsetRecords[i].BaseOffset = (long)recBuf.ReadUInt64B(off);
                reader._offsetRecords[i].Sizes = new ushort[EntriesPerOffsetRecord];
                for (int j = 0; j < EntriesPerOffsetRecord; j++)
                    reader._offsetRecords[i].Sizes[j] = recBuf.ReadUInt16B(off + 8 + j * 2);
            }

            // Read name table
            reader._nameTable = new byte[namesSize];
            stream.Seek(namesOff, SeekOrigin.Begin);
            readExact(stream, reader._nameTable, 0, (int)namesSize);

            // Read file tree (16 bytes per entry)
            int entryCount = (int)(treeSize / 16);
            reader._entries = new Entry[entryCount];
            byte[] treeBuf = new byte[treeSize];
            stream.Seek(treeOff, SeekOrigin.Begin);
            readExact(stream, treeBuf, 0, (int)treeSize);
            for (int i = 0; i < entryCount; i++)
            {
                int eOff = i * 16;
                uint nameAndType = treeBuf.ReadUInt32B(eOff);
                bool isFile = (nameAndType & 0x80000000u) != 0;
                int  nameOff = (int)(nameAndType & 0x7FFFFFFFu);
                reader._entries[i].IsFile     = isFile;
                reader._entries[i].NameOffset = nameOff;
                reader._entries[i].Name       = reader.GetName(nameOff);

                if (isFile)
                {
                    uint offLow  = treeBuf.ReadUInt32B(eOff + 4);
                    uint sizeLow = treeBuf.ReadUInt32B(eOff + 8);
                    uint high    = treeBuf.ReadUInt32B(eOff + 12);
                    reader._entries[i].FileOffset = (long)offLow  | (((long)(high & 0xFFFF)) << 32);
                    reader._entries[i].FileSize   = (long)sizeLow | (((long)((high >> 16) & 0xFFFF)) << 32);
                }
                else
                {
                    reader._entries[i].ChildStartIndex = (int)treeBuf.ReadUInt32B(eOff + 4);
                    reader._entries[i].ChildCount      = (int)treeBuf.ReadUInt32B(eOff + 8);
                }
            }

            return reader;
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Returns the names of all immediate child directories of the archive root.</summary>
        public IReadOnlyList<string> GetRootDirectories()
        {
            var result = new List<string>();
            if (_entries.Length == 0 || _entries[0].IsFile) return result;
            int start = _entries[0].ChildStartIndex;
            int count = _entries[0].ChildCount;
            for (int i = start; i < start + count; i++)
                if (!_entries[i].IsFile)
                    result.Add(_entries[i].Name);
            return result;
        }

        /// <summary>
        /// Enumerates all files recursively under <paramref name="directoryPath"/>.
        /// Returns (relativePath, uncompressedSize) pairs, where path uses '/' separators.
        /// </summary>
        public IEnumerable<(string path, long size)> EnumerateFiles(string directoryPath = "")
        {
            int dirIdx = FindEntry(directoryPath, false);
            if (dirIdx < 0) yield break;
            foreach (var item in enumerateFiles(dirIdx, ""))
                yield return item;
        }

        private IEnumerable<(string path, long size)> enumerateFiles(int dirIdx, string prefix)
        {
            Entry dir = _entries[dirIdx];
            for (int i = dir.ChildStartIndex; i < dir.ChildStartIndex + dir.ChildCount; i++)
            {
                Entry e = _entries[i];
                string p = prefix.Length == 0 ? e.Name : prefix + "/" + e.Name;
                if (e.IsFile)
                    yield return (p, e.FileSize);
                else
                    foreach (var item in enumerateFiles(i, p))
                        yield return item;
            }
        }

        /// <summary>Returns the uncompressed size of a file, or -1 if not found.</summary>
        public long GetFileSize(string path)
        {
            int idx = FindEntry(path, true);
            return idx >= 0 ? _entries[idx].FileSize : -1L;
        }

        /// <summary>Reads uncompressed bytes from a file within the archive.</summary>
        public int Read(string filePath, long fileOffset, byte[] buffer, int bufferOffset, int count)
        {
            int idx = FindEntry(filePath, true);
            if (idx < 0) throw new HandledException($"ZArchive: file not found '{filePath}'");
            return ReadByEntry(idx, fileOffset, buffer, bufferOffset, count);
        }

        /// <summary>
        /// Reads uncompressed bytes from a file entry identified by its index in the file tree.
        /// Used internally by WuaFileStream for direct index-based access (avoids path lookup per read).
        /// </summary>
        public int ReadByEntry(int entryIndex, long fileOffset, byte[] buffer, int bufferOffset, int count)
        {
            Entry entry = _entries[entryIndex];
            if (!entry.IsFile) throw new InvalidOperationException("Entry is not a file.");
            long fileSize = entry.FileSize;
            if (fileOffset >= fileSize || count <= 0) return 0;
            count = (int)Math.Min(count, fileSize - fileOffset);

            long rawOffset  = entry.FileOffset + fileOffset; // position in the uncompressed data space
            int  remaining  = count;
            int  written    = 0;

            while (remaining > 0)
            {
                long blockIdx   = rawOffset / BlockSize;
                int  blockOff   = (int)(rawOffset % BlockSize);
                int  available  = BlockSize - blockOff;
                int  toCopy     = Math.Min(remaining, available);

                byte[] block = getBlock(blockIdx);
                Array.Copy(block, blockOff, buffer, bufferOffset + written, toCopy);
                written   += toCopy;
                remaining -= toCopy;
                rawOffset += toCopy;
            }
            return written;
        }

        /// <summary>Opens a seekable <see cref="Stream"/> over a named file within the archive.</summary>
        public Stream OpenFile(string filePath)
        {
            int idx = FindEntry(filePath, true);
            if (idx < 0) throw new HandledException($"ZArchive: file not found '{filePath}'");
            return new WuaFileStream(this, idx, _entries[idx].FileSize);
        }

        /// <summary>Opens a seekable stream over a file entry by its tree index.</summary>
        public Stream OpenFileByIndex(int entryIndex)
        {
            if (entryIndex < 0 || entryIndex >= _entries.Length || !_entries[entryIndex].IsFile)
                throw new ArgumentOutOfRangeException(nameof(entryIndex));
            return new WuaFileStream(this, entryIndex, _entries[entryIndex].FileSize);
        }

        /// <summary>
        /// Finds a file or directory entry by path. Returns the index in _entries, or -1 if not found.
        /// Path components are separated by '/' or '\'. Case-insensitive for a-z.
        /// </summary>
        public int FindEntry(string path, bool expectFile)
        {
            if (string.IsNullOrEmpty(path))
                return expectFile ? -1 : 0; // root dir is index 0

            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            int currentIdx = 0;

            foreach (string part in parts)
            {
                Entry current = _entries[currentIdx];
                if (current.IsFile) return -1; // can't descend into a file
                int found = -1;
                for (int i = current.ChildStartIndex; i < current.ChildStartIndex + current.ChildCount; i++)
                {
                    if (CompareNameCI(part, _entries[i].Name))
                    {
                        found = i;
                        break;
                    }
                }
                if (found < 0) return -1;
                currentIdx = found;
            }

            Entry result = _entries[currentIdx];
            return (expectFile == result.IsFile) ? currentIdx : -1;
        }

        /// <summary>Gets the entry index for the given tree index (validates and returns it).</summary>
        internal bool IsFile(int index) => index >= 0 && index < _entries.Length && _entries[index].IsFile;
        internal long GetFileSize(int index) => _entries[index].FileSize;

        // ────────────────────────────────────────────────────────────────────
        // Block decompression + LRU cache
        // ────────────────────────────────────────────────────────────────────

        private byte[] getBlock(long blockIndex)
        {
            // Check cache
            for (int i = 0; i < _cacheUsed; i++)
            {
                int slot = _cacheLruOrder[i];
                if (_cacheBlockIndex[slot] == blockIndex)
                {
                    // Move to MRU position
                    if (i > 0)
                    {
                        int tmp = _cacheLruOrder[i];
                        for (int j = i; j > 0; j--) _cacheLruOrder[j] = _cacheLruOrder[j - 1];
                        _cacheLruOrder[0] = tmp;
                    }
                    return _cacheData[slot];
                }
            }

            // Not in cache — load it
            int evict = _cacheUsed < CacheCapacity ? _cacheUsed++ : _cacheLruOrder[CacheCapacity - 1];
            _cacheBlockIndex[evict] = blockIndex;
            loadBlock(blockIndex, _cacheData[evict]);

            // Make evicted slot MRU
            int pos = Array.IndexOf(_cacheLruOrder, evict);
            for (int j = pos; j > 0; j--) _cacheLruOrder[j] = _cacheLruOrder[j - 1];
            _cacheLruOrder[0] = evict;

            return _cacheData[evict];
        }

        private void loadBlock(long blockIndex, byte[] dest)
        {
            int  recIdx    = (int)(blockIndex / EntriesPerOffsetRecord);
            int  subIdx    = (int)(blockIndex % EntriesPerOffsetRecord);

            if (recIdx >= _offsetRecords.Length)
                throw new HandledException($"ZArchive: block index {blockIndex} out of range.");

            OffsetRecord rec = _offsetRecords[recIdx];

            // Compute absolute offset of this block within the file
            long blockFileOffset = rec.BaseOffset;
            for (int i = 0; i < subIdx; i++)
                blockFileOffset += (long)rec.Sizes[i] + 1;

            int compressedSize = (int)rec.Sizes[subIdx] + 1;

            blockFileOffset += _compDataOffset;

            if (compressedSize == BlockSize)
            {
                // Stored uncompressed — read directly into dest
                _stream.Seek(blockFileOffset, SeekOrigin.Begin);
                readExact(_stream, dest, 0, BlockSize);
            }
            else
            {
                // zstd compressed
                _stream.Seek(blockFileOffset, SeekOrigin.Begin);
                readExact(_stream, _decompBuffer, 0, compressedSize);

                int destLen = BlockSize;
                using ZStdBlock zs = new ZStdBlock(new GrindCore.CompressionOptions { BlockSize = BlockSize });
                zs.Decompress(_decompBuffer, 0, compressedSize, dest, 0, ref destLen);

                if (destLen != BlockSize)
                    throw new HandledException($"ZArchive: decompressed block {blockIndex} to {destLen} bytes, expected {BlockSize}.");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private string GetName(int nameOffset)
        {
            if (nameOffset == 0x7FFFFFFF || nameOffset >= _nameTable.Length) return "";
            int lenByte = _nameTable[nameOffset];
            int nameLen;
            int dataOff;
            if ((lenByte & 0x80) != 0)
            {
                if (nameOffset + 1 >= _nameTable.Length) return "";
                nameLen = (lenByte & 0x7F) | ((_nameTable[nameOffset + 1]) << 7);
                dataOff = nameOffset + 2;
            }
            else
            {
                nameLen = lenByte & 0x7F;
                dataOff = nameOffset + 1;
            }
            if (dataOff + nameLen > _nameTable.Length) return "";
            return Encoding.GetEncoding(1252).GetString(_nameTable, dataOff, nameLen);
        }

        private static bool CompareNameCI(string a, string b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                char ca = a[i], cb = b[i];
                if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
                if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
                if (ca != cb) return false;
            }
            return true;
        }

        private static void readExact(Stream s, byte[] buf, int off, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = s.Read(buf, off + total, count - total);
                if (r == 0) throw new EndOfStreamException($"ZArchive: unexpected end of stream reading {count} bytes.");
                total += r;
            }
        }


        public void Dispose() { /* stream is owned by the caller (WuaAsIso) */ }
    }
}

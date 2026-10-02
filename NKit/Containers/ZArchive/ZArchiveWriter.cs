using Nanook.GrindCore.ZStd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit.Container.ZArchive
{
    /// <summary>
    /// Writes ZArchive (.zar / .wua) files. The format is append-only:
    ///
    ///   [compressed data blocks]  (0..N × 64 KiB, sequential)
    ///   [OffsetRecords]           (one per 16 blocks, big-endian)
    ///   [name table]              (length-prefixed strings)
    ///   [file tree]               (16 bytes per entry, big-endian)
    ///   [meta directory]          (empty)
    ///   [meta data]               (empty)
    ///   [footer]                  (144 bytes, big-endian, magic at byte 140)
    ///
    /// Usage:
    ///   using var w = new ZArchiveWriter(outputStream);
    ///   w.MakeDir("titleId_v0");
    ///   w.MakeDir("titleId_v0/code");
    ///   w.StartFile("titleId_v0/code/app.xml");
    ///   w.Write(data, 0, data.Length);
    ///   w.FinalizeArchive();
    ///
    /// Reference: https://github.com/Exzap/ZArchive
    /// </summary>
    internal class ZArchiveWriter : IDisposable
    {
        // ── Format constants ─────────────────────────────────────────────────
        private const int  BlockSize           = ZArchiveReader.BlockSize;     // 64 KiB
        private const int  EntriesPerRecord    = ZArchiveReader.EntriesPerOffsetRecord; // 16
        private const uint MagicValue          = 0x169F52D6u;
        private const uint VersionValue        = 0x61BF3A01u;
        private const int  FooterSize          = ZArchiveReader.FooterSize;    // 144 bytes

        // ── Compression ──────────────────────────────────────────────────────
        private readonly ZStdBlock _compressor;
        private readonly byte[] _uncompressedBlock = new byte[BlockSize];
        private readonly byte[] _compressedBlock   = new byte[BlockSize + 4096]; // headroom
        private int _blockBufferFill; // bytes filled in _uncompressedBlock

        // ── Block offset tracking ─────────────────────────────────────────────
        // One OffsetRecord covers EntriesPerRecord blocks.
        // Each entry stores compressed size - 1 as a uint16 (big-endian).
        private readonly List<long>   _blockBaseOffsets = new List<long>(); // one per group of 16
        private readonly List<ushort> _blockSizes       = new List<ushort>(); // one per block

        // ── Output stream ─────────────────────────────────────────────────────
        private readonly Stream _out;
        private readonly IncrementalHash _sha256; // hashes every byte written to _out
        private long _compDataStart; // absolute stream position where compressed data begins
        private long _compWritePos;  // running write position

        // ── File tree ─────────────────────────────────────────────────────────
        private class Node
        {
            public string Name;
            public bool   IsFile;
            public long   FileOffset;  // byte offset within uncompressed data stream
            public long   FileSize;
            public List<Node> Children = new List<Node>(); // only for dirs
            public int ChildStartIndex; // assigned during tree serialisation
        }
        private readonly Node _root = new Node { Name = "", IsFile = false };
        private Node _currentFile; // file currently being appended to

        // Per-file: running uncompressed byte offset (across all files, sequential)
        private long _currentDataOffset;

        // ── Name table ───────────────────────────────────────────────────────
        private readonly List<byte[]> _nameBytes    = new List<byte[]>();
        private readonly Dictionary<string, int> _nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // ── SHA-256 (zeroed — Cemu doesn't verify) ────────────────────────────
        // We write 32 zero bytes in the footer's hash field.

        // ────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────

        public ZArchiveWriter(Stream output)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
            _compressor = new ZStdBlock(new GrindCore.CompressionOptions
            {
                BlockSize = BlockSize,
                Type = (GrindCore.CompressionType)19  // level 19 — maximum compression
            });
            _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _compDataStart = output.Position;
            _compWritePos  = _compDataStart;
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Creates a directory node. Call before adding files inside it.</summary>
        public void MakeDir(string path)
        {
            flushCurrentFile();
            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            Node cur = _root;
            foreach (string part in parts)
            {
                Node child = cur.Children.Find(n => !n.IsFile && string.Equals(n.Name, part, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new Node { Name = part, IsFile = false };
                    cur.Children.Add(child);
                    getOrCreateName(part); // pre-populate name table entry
                }
                cur = child;
            }
        }

        /// <summary>
        /// Starts a new file at the given path (directories must already exist).
        /// Subsequent <see cref="Write"/> calls append to this file.
        /// </summary>
        public void StartFile(string path)
        {
            flushCurrentFile();

            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) throw new ArgumentException("Empty path");

            // Navigate to parent directory
            Node dir = _root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                Node child = dir.Children.Find(n => !n.IsFile && string.Equals(n.Name, parts[i], StringComparison.OrdinalIgnoreCase));
                if (child == null) throw new HandledException($"ZArchiveWriter: directory '{parts[i]}' not found in path '{path}'");
                dir = child;
            }

            string fileName = parts[parts.Length - 1];
            var node = new Node { Name = fileName, IsFile = true, FileOffset = _currentDataOffset, FileSize = 0 };
            dir.Children.Add(node);
            getOrCreateName(fileName); // pre-populate name table entry
            _currentFile = node;
        }

        /// <summary>Appends bytes to the currently active file.</summary>
        public void Write(byte[] data, int offset, int count)
        {
            if (_currentFile == null) throw new InvalidOperationException("No file started. Call StartFile first.");

            int remaining = count;
            while (remaining > 0)
            {
                int space = BlockSize - _blockBufferFill;
                int toCopy = Math.Min(remaining, space);
                Array.Copy(data, offset + (count - remaining), _uncompressedBlock, _blockBufferFill, toCopy);
                _blockBufferFill += toCopy;
                remaining        -= toCopy;

                if (_blockBufferFill == BlockSize)
                    flushBlock();
            }
            _currentFile.FileSize += count;
            _currentDataOffset    += count;
        }

        /// <summary>Flushes any buffered data and writes the index tables + footer.</summary>
        public void FinalizeArchive()
        {
            flushCurrentFile();
            flushBlock(); // flush partial last block (may be empty → no-op inside flushBlock)

            long compDataEnd = _compWritePos - _compDataStart;

            // Write OffsetRecords
            long recStart = _compWritePos;
            writeOffsetRecords();
            long recEnd = _compWritePos;

            // Write name table
            long namesStart = _compWritePos;
            writeNameTable();
            long namesEnd = _compWritePos;

            // Write file tree
            long treeStart = _compWritePos;
            int entryCount = writeFileTree();
            long treeEnd = _compWritePos;

            // meta sections (empty)
            long metaDirStart = _compWritePos;
            long metaDataStart = _compWritePos;

            long totalSize = _compWritePos + FooterSize;

            // Build the footer with the SHA-256 hash field zeroed (required by spec —
            // the hash covers the whole archive including the footer with hash=0).
            byte[] footer = new byte[FooterSize];
            int fi = 0;
            // sectionCompressedData
            footer.WriteUInt64B(fi, (ulong)_compDataStart);             fi += 8;
            footer.WriteUInt64B(fi, (ulong)compDataEnd);                fi += 8;
            // sectionOffsetRecords
            footer.WriteUInt64B(fi, (ulong)recStart);                   fi += 8;
            footer.WriteUInt64B(fi, (ulong)(recEnd - recStart));        fi += 8;
            // sectionNames
            footer.WriteUInt64B(fi, (ulong)namesStart);                 fi += 8;
            footer.WriteUInt64B(fi, (ulong)(namesEnd - namesStart));    fi += 8;
            // sectionFileTree
            footer.WriteUInt64B(fi, (ulong)treeStart);                  fi += 8;
            footer.WriteUInt64B(fi, (ulong)(treeEnd - treeStart));      fi += 8;
            // sectionMetaDirectory
            footer.WriteUInt64B(fi, (ulong)metaDirStart);               fi += 8;
            footer.WriteUInt64B(fi, (ulong)0);                          fi += 8;
            // sectionMetaData
            footer.WriteUInt64B(fi, (ulong)metaDataStart);              fi += 8;
            footer.WriteUInt64B(fi, (ulong)0);                          fi += 8;
            // SHA-256 hash field — zeroed for hashing, filled in below (fi = 96)
            const int HashOffset = 96;
            fi += 32; // skip 32 zero bytes (already zero from new byte[])
            // totalSize
            footer.WriteUInt64B(fi, (ulong)totalSize);                  fi += 8;
            // version
            footer.WriteUInt32B(fi, VersionValue);                      fi += 4;
            // magic
            footer.WriteUInt32B(fi, MagicValue);                        fi += 4;

            // Feed the footer (with zeroed hash) into the SHA-256 so the hash covers
            // the complete archive. Then compute the final hash and embed it.
            _sha256.AppendData(footer, 0, FooterSize);
            byte[] hash = _sha256.GetHashAndReset();
            Array.Copy(hash, 0, footer, HashOffset, 32);

            // Write the footer with the real hash directly to _out (hash is now final).
            _out.Write(footer, 0, FooterSize);
        }

        public void Dispose()
        {
            _compressor?.Dispose();
            _sha256?.Dispose();
        }

        // ────────────────────────────────────────────────────────────────────
        // Private helpers
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes bytes to the output stream AND feeds them into the running SHA-256 hash.
        /// Every byte written to the archive (including the footer with hash field zeroed)
        /// must go through this helper so the final hash is correct.
        /// </summary>
        private void outputData(byte[] data, int offset, int count)
        {
            _out.Write(data, offset, count);
            _sha256.AppendData(data, offset, count);
            _compWritePos += count;
        }

        private void flushCurrentFile()
        {
            // no-op if no current file
            _currentFile = null;
        }

        private void flushBlock()
        {
            if (_blockBufferFill == 0) return;

            // Pad block to full size with zeros if partial
            if (_blockBufferFill < BlockSize)
                Array.Clear(_uncompressedBlock, _blockBufferFill, BlockSize - _blockBufferFill);

            // Track group start offset
            int blockIdx = _blockSizes.Count;
            if (blockIdx % EntriesPerRecord == 0)
                _blockBaseOffsets.Add(_compWritePos - _compDataStart);

            // Try to compress
            int compLen = _compressedBlock.Length;
            bool compressed = false;
            try
            {
                _compressor.Compress(_uncompressedBlock, 0, BlockSize, _compressedBlock, 0, ref compLen);
                compressed = compLen < BlockSize;
            }
            catch { compressed = false; }

            byte[] toWrite;
            int    toWriteLen;
            if (compressed)
            {
                toWrite    = _compressedBlock;
                toWriteLen = compLen;
            }
            else
            {
                toWrite    = _uncompressedBlock;
                toWriteLen = BlockSize;
            }

            if (toWriteLen > 0xFFFF + 1)
                throw new InvalidOperationException("Compressed block too large for uint16 size field.");

            _blockSizes.Add((ushort)(toWriteLen - 1));
            outputData(toWrite, 0, toWriteLen);
            _blockBufferFill = 0;
        }

        private void writeOffsetRecords()
        {
            // Each record: 8-byte base + 16 × uint16 (big-endian)
            int totalBlocks = _blockSizes.Count;
            int totalRecords = (totalBlocks + EntriesPerRecord - 1) / EntriesPerRecord;

            for (int r = 0; r < totalRecords; r++)
            {
                byte[] rec = new byte[8 + 2 * EntriesPerRecord];
                rec.WriteUInt64B(0, (ulong)_blockBaseOffsets[r]);
                for (int b = 0; b < EntriesPerRecord; b++)
                {
                    int idx = r * EntriesPerRecord + b;
                    ushort sz = idx < _blockSizes.Count ? _blockSizes[idx] : (ushort)0;
                    rec.WriteUInt16B(8 + b * 2, sz);
                }
                outputData(rec, 0, rec.Length);
            }
        }

        private void writeNameTable()
        {
            // Collect all names from the tree in DFS order to assign nameOffsets
            // Actually we build the table on the fly and assign indices during tree construction.
            // Here we write them in the order they appear in _nameBytes.
            using var ms = new MemoryStream();
            foreach (byte[] entry in _nameBytes)
            {
                ms.Write(entry, 0, entry.Length);
            }
            byte[] table = ms.ToArray();
            outputData(table, 0, table.Length);
        }

        private int writeFileTree()
        {
            // BFS (breadth-first) flat array — matches the C++ reference exactly.
            // All direct children of a directory are contiguous in the flat array so
            // ChildStartIndex + ChildCount correctly addresses them.
            //
            // Pass 1: BFS to compute ChildStartIndex for every directory.
            // Pass 2: BFS again in the same order to write entries.

            // Pass 1 — assign indices
            var queue = new Queue<Node>();
            queue.Enqueue(_root);
            int currentIndex = 1; // root occupies index 0

            while (queue.Count > 0)
            {
                Node node = queue.Dequeue();
                if (!node.IsFile && node.Children.Count > 0)
                {
                    node.ChildStartIndex = currentIndex;
                    currentIndex += node.Children.Count;
                    foreach (Node child in node.Children)
                        queue.Enqueue(child);
                }
            }

            // Pass 2 — serialize in the same BFS order
            queue.Enqueue(_root);
            int written = 0;

            while (queue.Count > 0)
            {
                Node n = queue.Dequeue();
                if (!n.IsFile)
                    foreach (Node child in n.Children)
                        queue.Enqueue(child);

                byte[] entry = new byte[16];
                // Root node uses the special name offset 0x7FFFFFFF (per spec)
                int nameOff = ReferenceEquals(n, _root) ? 0x7FFFFFFF : getOrCreateName(n.Name);
                uint nameAndType = (uint)nameOff;
                if (n.IsFile) nameAndType |= 0x80000000u;
                entry.WriteUInt32B(0, nameAndType);

                if (n.IsFile)
                {
                    uint offLow  = (uint)(n.FileOffset & 0xFFFFFFFF);
                    uint sizeLow = (uint)(n.FileSize   & 0xFFFFFFFF);
                    uint high    = (uint)(((n.FileOffset >> 32) & 0xFFFF) | (((n.FileSize >> 32) & 0xFFFF) << 16));
                    entry.WriteUInt32B( 4, offLow);
                    entry.WriteUInt32B( 8, sizeLow);
                    entry.WriteUInt32B(12, high);
                }
                else
                {
                    entry.WriteUInt32B( 4, (uint)n.ChildStartIndex);
                    entry.WriteUInt32B( 8, (uint)n.Children.Count);
                    entry.WriteUInt32B(12, 0);
                }
                outputData(entry, 0, 16);
                written++;
            }
            return written;
        }

        private int getOrCreateName(string name)
        {
            if (_nameIndex.TryGetValue(name, out int idx)) return idx;

            // Encode as Latin-1
            byte[] nameData = Encoding.GetEncoding(1252).GetBytes(name);

            // Write length prefix: single byte if length < 128, two bytes otherwise
            byte[] entry;
            int nameLen = nameData.Length;
            if (nameLen < 128)
            {
                entry = new byte[1 + nameLen];
                entry[0] = (byte)nameLen;
                Array.Copy(nameData, 0, entry, 1, nameLen);
            }
            else
            {
                entry = new byte[2 + nameLen];
                entry[0] = (byte)(nameLen & 0x7F);
                entry[1] = (byte)(nameLen >> 7);
                Array.Copy(nameData, 0, entry, 2, nameLen);
            }

            // The name offset is the current position in the name table byte stream
            int nameOffset = 0;
            foreach (byte[] e in _nameBytes) nameOffset += e.Length;

            idx = nameOffset;
            _nameIndex[name] = idx;
            _nameBytes.Add(entry);
            return idx;
        }

    }
}

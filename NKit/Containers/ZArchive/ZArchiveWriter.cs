using Nanook.GrindCore.ZStd;
using Nanook.NKit.Steps.Shared;
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
    ///   using var w = new ZArchiveWriter(outputStream, workers: 16);
    ///   w.MakeDir("titleId_v0");
    ///   w.StartFile("titleId_v0/code/app.rpx");
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

        // ── Thread-local compressor ───────────────────────────────────────────
        // One ZStdBlock per thread, created on first use and reused for its lifetime.
        // Each parallel worker thread gets its own context, so there is no lock contention
        // and no per-block native alloc/free churn (the CRT arena stays warm between calls).
        // This mirrors ConvertWiiGcRvzStep, which uses the same pattern with CircularSequenceQueue.
        [ThreadStatic]
        private static ZStdBlock _tlsCompressor;

        private static ZStdBlock getCompressor()
        {
            if (_tlsCompressor == null)
                _tlsCompressor = new ZStdBlock(new GrindCore.CompressionOptions
                {
                    BlockSize = BlockSize,
                    Type = (GrindCore.CompressionType)19  // level 19 — maximum compression
                });
            return _tlsCompressor;
        }

        // ── Parallel pipeline ─────────────────────────────────────────────────
        // Pool slot: (uncompressed, compressed) buffer pair.  No ZStdBlock here — the
        // thread-local compressor above is used by whichever worker thread processes the slot.
        private sealed class BlockBuffer
        {
            internal BlockBuffer()
            {
                this.Uncompressed = new byte[BlockSize];
                this.Compressed   = new byte[BlockSize + 4096]; // headroom
            }
            internal byte[] Uncompressed;
            internal byte[] Compressed;
            internal int    CompressedLen; // 0 = store uncompressed
        }

        private readonly CircularSequenceQueue<BlockBuffer> _queue;
        private int _blockBufferFill; // bytes in _queue.FillItem.Uncompressed
        private volatile Exception _writeException;

        // ── Block offset tracking ─────────────────────────────────────────────
        private readonly List<long>   _blockBaseOffsets = new List<long>();
        private readonly List<ushort> _blockSizes       = new List<ushort>();

        // ── Output stream ─────────────────────────────────────────────────────
        private readonly Stream _out;
        private readonly IncrementalHash _sha256;
        private long _compDataStart;
        private long _compWritePos;

        // ── File tree ─────────────────────────────────────────────────────────
        private class Node
        {
            public string Name;
            public bool   IsFile;
            public long   FileOffset;
            public long   FileSize;
            public List<Node> Children = new List<Node>();
            public int ChildStartIndex;
        }
        private readonly Node _root = new Node { Name = "", IsFile = false };
        private Node _currentFile;
        private long _currentDataOffset;

        // ── Name table ───────────────────────────────────────────────────────
        private readonly List<byte[]> _nameBytes    = new List<byte[]>();
        private readonly Dictionary<string, int> _nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // ────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new ZArchiveWriter.
        /// </summary>
        /// <param name="output">The stream to write the archive to.</param>
        /// <param name="workers">
        /// Number of parallel compression workers. 0 = use <see cref="Environment.ProcessorCount"/>.
        /// Mirrors the <c>wua:N</c> parallelism option (e.g. <c>wua:16</c>).
        /// </param>
        public ZArchiveWriter(Stream output, int workers = 0)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
            _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _compDataStart = output.Position;
            _compWritePos  = _compDataStart;

            if (workers <= 0)
                workers = Environment.ProcessorCount;

            // Pool: workers + 2 (one fill slot + one write-side slack), same sizing as RVZ.
            // CircularSequenceQueue takes the first item as the initial FillItem; the rest are
            // the circular work queue, so we need workers + 2 items total.
            BlockBuffer[] pool = new BlockBuffer[workers + 2];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = new BlockBuffer();

            _queue = new CircularSequenceQueue<BlockBuffer>(pool, compressBlock, writeBlock);
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────

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
                    getOrCreateName(part);
                }
                cur = child;
            }
        }

        public void StartFile(string path)
        {
            flushCurrentFile();

            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) throw new ArgumentException("Empty path");

            Node dir = _root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                Node child = dir.Children.Find(n => !n.IsFile && string.Equals(n.Name, parts[i], StringComparison.OrdinalIgnoreCase));
                if (child == null) throw new HandledException($"ZArchiveWriter: directory '{parts[i]}' not found in path '{path}'");
                dir = child;
            }

            string fileName = parts[parts.Length - 1];
            Node node = new Node { Name = fileName, IsFile = true, FileOffset = _currentDataOffset, FileSize = 0 };
            dir.Children.Add(node);
            getOrCreateName(fileName);
            _currentFile = node;
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (_currentFile == null) throw new InvalidOperationException("No file started. Call StartFile first.");
            checkException();

            int remaining = count;
            while (remaining > 0)
            {
                int space  = BlockSize - _blockBufferFill;
                int toCopy = Math.Min(remaining, space);
                Array.Copy(data, offset + (count - remaining), _queue.FillItem.Uncompressed, _blockBufferFill, toCopy);
                _blockBufferFill += toCopy;
                remaining        -= toCopy;

                if (_blockBufferFill == BlockSize)
                    submitBlock(full: true);
            }
            _currentFile.FileSize += count;
            _currentDataOffset    += count;
        }

        public void FinalizeArchive()
        {
            flushCurrentFile();
            submitBlock(full: false);
            _queue.Complete();
            checkException();

            long compDataEnd = _compWritePos - _compDataStart;

            long recStart = _compWritePos;
            writeOffsetRecords();
            long recEnd = _compWritePos;

            long namesStart = _compWritePos;
            writeNameTable();
            long namesEnd = _compWritePos;

            long treeStart = _compWritePos;
            writeFileTree();
            long treeEnd = _compWritePos;

            long metaDirStart  = _compWritePos;
            long metaDataStart = _compWritePos;
            long totalSize     = _compWritePos + FooterSize;

            byte[] footer = new byte[FooterSize];
            int fi = 0;
            footer.WriteUInt64B(fi, (ulong)_compDataStart);          fi += 8;
            footer.WriteUInt64B(fi, (ulong)compDataEnd);             fi += 8;
            footer.WriteUInt64B(fi, (ulong)recStart);                fi += 8;
            footer.WriteUInt64B(fi, (ulong)(recEnd - recStart));     fi += 8;
            footer.WriteUInt64B(fi, (ulong)namesStart);              fi += 8;
            footer.WriteUInt64B(fi, (ulong)(namesEnd - namesStart)); fi += 8;
            footer.WriteUInt64B(fi, (ulong)treeStart);               fi += 8;
            footer.WriteUInt64B(fi, (ulong)(treeEnd - treeStart));   fi += 8;
            footer.WriteUInt64B(fi, (ulong)metaDirStart);            fi += 8;
            footer.WriteUInt64B(fi, (ulong)0);                       fi += 8;
            footer.WriteUInt64B(fi, (ulong)metaDataStart);           fi += 8;
            footer.WriteUInt64B(fi, (ulong)0);                       fi += 8;
            const int HashOffset = 96;
            fi += 32;
            footer.WriteUInt64B(fi, (ulong)totalSize);               fi += 8;
            footer.WriteUInt32B(fi, VersionValue);                   fi += 4;
            footer.WriteUInt32B(fi, MagicValue);                     fi += 4;

            _sha256.AppendData(footer, 0, FooterSize);
            byte[] hash = _sha256.GetHashAndReset();
            Array.Copy(hash, 0, footer, HashOffset, 32);
            _out.Write(footer, 0, FooterSize);
        }

        public void Dispose()
        {
            _sha256?.Dispose();
            // Shut down the CircularSequenceQueue worker threads. Without this, each
            // ZArchiveWriter instance leaks ItemCount threads that block indefinitely
            // on slot.Ready.Wait() — across many test runs this exhausts thread limits.
            try { _queue?.Dispose(); } catch { }
            // _tlsCompressor is intentionally not disposed here — it lives for the thread's lifetime
            // and is reused across all ZArchiveWriter instances on the same thread.
        }

        // ────────────────────────────────────────────────────────────────────
        // Block pipeline
        // ────────────────────────────────────────────────────────────────────

        private void checkException()
        {
            if (_writeException != null)
                throw new HandledException(_writeException, $"ZArchiveWriter: {_writeException.Message}");
        }

        private void submitBlock(bool full)
        {
            if (!full && _blockBufferFill == 0)
                return;

            if (!full && _blockBufferFill < BlockSize)
                Array.Clear(_queue.FillItem.Uncompressed, _blockBufferFill, BlockSize - _blockBufferFill);

            _queue.ItemComplete();
            _blockBufferFill = 0;
        }

        // Called on a thread-pool worker thread by CircularSequenceQueue.
        private void compressBlock(BlockBuffer slot)
        {
            if (_writeException != null)
                return;
            int compLen = slot.Compressed.Length;
            bool ok = false;
            try
            {
                ok = getCompressor().Compress(slot.Uncompressed, 0, BlockSize, slot.Compressed, 0, ref compLen)
                     == GrindCore.CompressionResultCode.Success
                     && compLen < BlockSize;
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.CompareExchange(ref _writeException, ex, null);
                ok = false;
            }
            slot.CompressedLen = ok ? compLen : 0;
        }

        // Called serially in submission order by CircularSequenceQueue.
        private void writeBlock(BlockBuffer slot)
        {
            if (_writeException != null)
                return;
            try
            {
                int blockIdx = _blockSizes.Count;
                if (blockIdx % EntriesPerRecord == 0)
                    _blockBaseOffsets.Add(_compWritePos - _compDataStart);

                bool compressed = slot.CompressedLen > 0;
                byte[] toWrite    = compressed ? slot.Compressed   : slot.Uncompressed;
                int    toWriteLen = compressed ? slot.CompressedLen : BlockSize;

                if (toWriteLen > 0xFFFF + 1)
                    throw new InvalidOperationException("Compressed block too large for uint16 size field.");

                _blockSizes.Add((ushort)(toWriteLen - 1));
                outputData(toWrite, 0, toWriteLen);
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.CompareExchange(ref _writeException, ex, null);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Output / serialisation helpers
        // ────────────────────────────────────────────────────────────────────

        private void outputData(byte[] data, int offset, int count)
        {
            _out.Write(data, offset, count);
            _sha256.AppendData(data, offset, count);
            _compWritePos += count;
        }

        private void flushCurrentFile()
        {
            _currentFile = null;
        }

        private void writeOffsetRecords()
        {
            int totalRecords = (_blockSizes.Count + EntriesPerRecord - 1) / EntriesPerRecord;
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
            using var ms = new MemoryStream();
            foreach (byte[] entry in _nameBytes)
                ms.Write(entry, 0, entry.Length);
            byte[] table = ms.ToArray();
            outputData(table, 0, table.Length);
        }

        private void writeFileTree()
        {
            var queue = new Queue<Node>();
            queue.Enqueue(_root);
            int currentIndex = 1;

            // Pass 1: assign ChildStartIndex
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

            // Pass 2: serialize BFS
            queue.Enqueue(_root);
            while (queue.Count > 0)
            {
                Node n = queue.Dequeue();
                if (!n.IsFile)
                    foreach (Node child in n.Children)
                        queue.Enqueue(child);

                byte[] entry = new byte[16];
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
            }
        }

        private int getOrCreateName(string name)
        {
            if (_nameIndex.TryGetValue(name, out int idx)) return idx;

            byte[] nameData = Encoding.GetEncoding(1252).GetBytes(name);
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

            int nameOffset = 0;
            foreach (byte[] e in _nameBytes) nameOffset += e.Length;

            idx = nameOffset;
            _nameIndex[name] = idx;
            _nameBytes.Add(entry);
            return idx;
        }
    }
}

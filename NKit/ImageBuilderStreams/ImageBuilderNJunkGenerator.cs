using Nanook.NKit.Nintendo.WiiGc;
using NKitDataStore;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Nanook.NKit.ImageBuilderStreams
{
    /// <summary>
    /// Centralized NJunk generator used by ImageBuilder streams (Wii/GameCube).
    /// Encapsulates NJunk parameters, block caches and helpers to write padded junk into buffers.
    /// </summary>
    internal sealed class ImageBuilderNJunkGenerator
    {
        private byte[] _junkIdBytes;
        private byte[] _discJunkIdBytes;
        private int _discNo;

        private long _junkStartOffset; // leading nulls offset
        private long _junkTotalSize;   // aligned total size
        private long _junkBaseFsOffset; // base fs offset for partition-level generation

        private long _discJunkStartOffset;
        private long _discJunkTotalSize;
        private long _discJunkBaseFsOffset;
        private int _discJunkDiscNo;

        private readonly Dictionary<int, byte[]> _junkCache = new Dictionary<int, byte[]>();
        private readonly Queue<int> _junkCacheLru = new Queue<int>();
        private readonly Dictionary<int, byte[]> _discJunkCache = new Dictionary<int, byte[]>();
        private readonly Queue<int> _discJunkCacheLru = new Queue<int>();
        private readonly object _junkLock = new object();

        private readonly int _maxJunkCacheBlocks;
        private readonly int _maxDiscJunkCacheBlocks;

        private static readonly byte[] _ZeroJunkBlock = new byte[NJunk.JunkBlockSize];

        public ImageBuilderNJunkGenerator(int maxJunkCacheBlocks = 64, int maxDiscJunkCacheBlocks = 32)
        {
            _maxJunkCacheBlocks = Math.Max(1, maxJunkCacheBlocks);
            _maxDiscJunkCacheBlocks = Math.Max(1, maxDiscJunkCacheBlocks);
        }

        public void InitializePartition(string junkId, int discNo, long startOffset, long totalSize, long baseFsOffset)
        {
            _junkIdBytes = string.IsNullOrEmpty(junkId) ? null : Encoding.ASCII.GetBytes(junkId);
            _discNo = discNo;
            _junkStartOffset = startOffset;
            _junkTotalSize = Math.Max(0, totalSize);
            _junkBaseFsOffset = baseFsOffset;
            lock (_junkLock)
            {
                _junkCache.Clear();
                _junkCacheLru.Clear();
            }
            //Debug.WriteLine($"[NJunkGenerator] InitializePartition JunkId={(junkId ?? "(null)")}, DiscNo={discNo}, Start=0x{startOffset:X}, Total=0x{_junkTotalSize:X}, BaseFs=0x{baseFsOffset:X}");
        }

        public void InitializeDisc(string junkId, int discNo, long startOffset, long totalSize, long baseFsOffset)
        {
            _discJunkIdBytes = string.IsNullOrEmpty(junkId) ? null : Encoding.ASCII.GetBytes(junkId);
            _discJunkDiscNo = discNo;
            _discJunkStartOffset = startOffset;
            _discJunkTotalSize = Math.Max(0, totalSize);
            _discJunkBaseFsOffset = baseFsOffset;
            lock (_junkLock)
            {
                _discJunkCache.Clear();
                _discJunkCacheLru.Clear();
            }
            //Debug.WriteLine($"[NJunkGenerator] InitializeDisc JunkId={(junkId ?? "(null)")}, DiscNo={discNo}, Start=0x{startOffset:X}, Total=0x{_discJunkTotalSize:X}, BaseFs=0x{baseFsOffset:X}");
        }

        /// <summary>
        /// Copy generated junk into a buffer range, using the same semantics as the existing streams.
        /// The delegate writeToBuffer has signature (byte[] source, int sourceOffset, long cleanBufferOffset, int length)
        /// and is typically the protected ImageBuilder.WriteToBuffer method bound via method group.
        /// </summary>
        public void WriteJunkWithPadding(AreaRecord area, ref long offsetInGap, long cleanAreaOffset, ref long fragStart, ref int remaining, ref int destOffset, bool useAlignPadding, bool isFileSystem, Action<byte[], int, long, int> writeToBuffer)
        {
            if (remaining <= 0) return;
            long absFragStart = area.Offset + cleanAreaOffset;

            int alignBytes = 0;
            if (useAlignPadding)
                alignBytes = (int)((4 - (absFragStart % 4)) % 4);

            long totalPadding = alignBytes + WiiConsts.DataNullsCount;

            // Write padding only if this fragment hasn't already consumed it
            if (offsetInGap < totalPadding && offsetInGap >= 0)
            {
                long consumed = offsetInGap;
                long consumedFromAlign = Math.Min(consumed, alignBytes);
                long remainingAlign = alignBytes - consumedFromAlign;

                if (remainingAlign > 0 && remaining > 0)
                {
                    int toWrite = (int)Math.Min(remainingAlign, remaining);
                    byte[] zeros = new byte[toWrite];
                    writeToBuffer(zeros, 0, destOffset, toWrite);
                    remaining -= toWrite;
                    destOffset += toWrite;
                    fragStart += toWrite;
                    offsetInGap += toWrite;
                }

                long consumedFrom1C = Math.Max(0, offsetInGap - alignBytes);
                long remaining1C = WiiConsts.DataNullsCount - consumedFrom1C;
                if (remaining1C > 0 && remaining > 0)
                {
                    int toWrite1c = (int)Math.Min(remaining1C, remaining);
                    byte[] zeros1c = new byte[toWrite1c];
                    writeToBuffer(zeros1c, 0, destOffset, toWrite1c);
                    remaining -= toWrite1c;
                    destOffset += toWrite1c;
                    fragStart += toWrite1c;
                    offsetInGap += toWrite1c;
                }
            }

            if (remaining > 0)
            {
                long absFsStart = isFileSystem ? fragStart + _junkBaseFsOffset : (area.Offset + fragStart + _junkBaseFsOffset);
                int chunk = 0x100000; // 1MB chunk size
                ArrayPool<byte> pool = ArrayPool<byte>.Shared;
                while (remaining > 0)
                {
                    int toWrite = Math.Min(remaining, chunk);
                    byte[] tmp = pool.Rent(toWrite);
                    try
                    {
                        FillJunkIntoBuffer(tmp, 0, toWrite, absFsStart);
                        writeToBuffer(tmp, 0, destOffset, toWrite);
                    }
                    finally
                    {
                        pool.Return(tmp);
                    }
                    remaining -= toWrite;
                    destOffset += toWrite;
                    absFsStart += toWrite;
                    fragStart += toWrite;
                }
            }
        }

        /// <summary>
        /// Fill a buffer with generated junk starting at the provided absolute FS offset.
        /// Uses partition-level generator if available, otherwise disc-level generator, otherwise zeros.
        /// </summary>
        public void FillJunkIntoBuffer(byte[] buffer, int bufferOffset, int size, long absFsOffsetStart)
        {
            if (size <= 0) return;

            //Debug.WriteLine($"[NJunkGenerator] FillJunkIntoBuffer absStart=0x{absFsOffsetStart:X} size=0x{size:X} partitionConfigured={_junkIdBytes!=null} discConfigured={_discJunkIdBytes!=null}");

            int writtenTotal = 0;
            while (writtenTotal < size)
            {
                long curAbs = absFsOffsetStart + writtenTotal;

                // Partition-level
                if (_junkIdBytes != null)
                {
                    long relative = curAbs - _junkBaseFsOffset;
                    if (relative < 0) relative = 0;
                    int blockIndex = (int)(relative / NJunk.JunkBlockSize);
                    int offsetInBlock = (int)(relative % NJunk.JunkBlockSize);
                    byte[] blk = getOrGenerateJunkBlock(blockIndex) ?? _ZeroJunkBlock;
                    int toCopy = Math.Min(size - writtenTotal, NJunk.JunkBlockSize - offsetInBlock);
                    Array.Copy(blk, offsetInBlock, buffer, bufferOffset + writtenTotal, toCopy);
                    writtenTotal += toCopy;
                    continue;
                }

                // Disc-level
                if (_discJunkIdBytes != null)
                {
                    long relative = curAbs - _discJunkBaseFsOffset;
                    if (relative < 0) relative = 0;
                    int blockIndex = (int)(relative / NJunk.JunkBlockSize);
                    int offsetInBlock = (int)(relative % NJunk.JunkBlockSize);
                    byte[] blk = getOrGenerateDiscJunkBlock(blockIndex) ?? _ZeroJunkBlock;
                    int toCopy = Math.Min(size - writtenTotal, NJunk.JunkBlockSize - offsetInBlock);
                    Array.Copy(blk, offsetInBlock, buffer, bufferOffset + writtenTotal, toCopy);
                    writtenTotal += toCopy;
                    continue;
                }

                // Fallback to zeros
                int zeroFill = size - writtenTotal;
                Array.Clear(buffer, bufferOffset + writtenTotal, zeroFill);
                writtenTotal += zeroFill;
            }
        }

        private byte[] getOrGenerateJunkBlock(int blockIndex)
        {
            lock (_junkLock)
            {
                if (_junkIdBytes == null) return _ZeroJunkBlock;
                if (_junkCache.TryGetValue(blockIndex, out byte[] cached))
                {
                    _junkCacheLru.Enqueue(blockIndex);
                    return cached;
                }

                long absBlockFsOffset = _junkBaseFsOffset + (blockIndex * (long)NJunk.JunkBlockSize);
                if (_junkTotalSize > 0 && absBlockFsOffset >= _junkBaseFsOffset + _junkTotalSize)
                    return _ZeroJunkBlock;

                //Debug.WriteLine($"[NJunkGenerator] Generating partition blockIndex={blockIndex} absOffset=0x{absBlockFsOffset:X}");

                byte[] newBlock = new byte[NJunk.JunkBlockSize];
                try
                {
                    NJunk.Fill(_junkIdBytes, _discNo, _junkStartOffset, _junkTotalSize == 0 ? long.MaxValue : _junkTotalSize, absBlockFsOffset, newBlock);
                }
                catch
                {
                    return _ZeroJunkBlock;
                }

                _junkCache[blockIndex] = newBlock;
                _junkCacheLru.Enqueue(blockIndex);
                while (_junkCache.Count > _maxJunkCacheBlocks && _junkCacheLru.Count > 0)
                {
                    int oldest = _junkCacheLru.Dequeue();
                    if (_junkCache.ContainsKey(oldest) && _junkCache.Count > _maxJunkCacheBlocks)
                        _junkCache.Remove(oldest);
                }

                return newBlock;
            }
        }

        private byte[] getOrGenerateDiscJunkBlock(int blockIndex)
        {
            lock (_junkLock)
            {
                if (_discJunkIdBytes == null) return _ZeroJunkBlock;
                if (_discJunkCache.TryGetValue(blockIndex, out byte[] cached))
                {
                    _discJunkCacheLru.Enqueue(blockIndex);
                    return cached;
                }

                long absBlockFsOffset = _discJunkBaseFsOffset + (blockIndex * (long)NJunk.JunkBlockSize);
                if (_discJunkTotalSize > 0 && absBlockFsOffset >= _discJunkBaseFsOffset + _discJunkTotalSize)
                    return _ZeroJunkBlock;

                //Debug.WriteLine($"[NJunkGenerator] Generating disc blockIndex={blockIndex} absOffset=0x{absBlockFsOffset:X}");

                byte[] newBlock = new byte[NJunk.JunkBlockSize];
                try
                {
                    NJunk.Fill(_discJunkIdBytes, _discJunkDiscNo, _discJunkStartOffset, _discJunkTotalSize == 0 ? long.MaxValue : _discJunkTotalSize, absBlockFsOffset, newBlock);
                }
                catch
                {
                    return _ZeroJunkBlock;
                }

                _discJunkCache[blockIndex] = newBlock;
                _discJunkCacheLru.Enqueue(blockIndex);
                while (_discJunkCache.Count > _maxDiscJunkCacheBlocks && _discJunkCacheLru.Count > 0)
                {
                    int oldest = _discJunkCacheLru.Dequeue();
                    if (_discJunkCache.ContainsKey(oldest) && _discJunkCache.Count > _maxDiscJunkCacheBlocks)
                        _discJunkCache.Remove(oldest);
                }
                return newBlock;
            }
        }
    }
}
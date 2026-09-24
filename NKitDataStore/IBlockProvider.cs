using NKitDataStore.Interfaces;
using System.Diagnostics;

namespace NKitDataStore
{
    // Provides block access abstraction to decouple ImageBuilder from specific reader implementations.
    public interface IBlockProvider
    {
        BlockRecord? GetBlock(BlockKey key);
        Task<BlockRecord?> GetBlockAsync(BlockKey key);
        BlockRecord? GetBlock(OffsetRecord record, int blockIndex);
    }

    // Default implementation that wraps an IImageReader.
    internal class ReaderBlockProvider : IBlockProvider
    {
        private readonly IImageReader _reader;
        private readonly ImageReader? _imgReader;
        private OffsetRecord? _rawRunOffset;
        private int _rawRunStartBlockIdx;
        private int _rawRunBlockCount;
        private byte[]? _rawRunBuffer;
        private int[]? _rawRunLocalOffsets;
        private int[]? _rawRunLocalSizes;

        public ReaderBlockProvider(IImageReader reader)
        {
            _reader = reader;
            _imgReader = reader as ImageReader;
        }

        public BlockRecord? GetBlock(BlockKey key) =>
            // IImageReader is expected to expose GetBlock or equivalent; delegate to reader.
            _reader.GetBlock(key);

        public Task<BlockRecord?> GetBlockAsync(BlockKey key) =>
            // Simple Task-based wrapper for synchronous reader implementations.
            Task.Run(() => _reader.GetBlock(key));

        public BlockRecord? GetBlock(OffsetRecord record, int blockIndex)
        {
            if (_imgReader == null)
            {
                // Fallback for non-ImageReader implementations
                return _reader.GetBlock(record.GetBlockAt(blockIndex));
            }

            int blockSize = _imgReader.Info.BlockSize;
            int expectedBlockLength = (int)global::System.Math.Min(blockSize, record.Size - ((long)blockIndex * blockSize));
            BlockKey key = record.GetBlockAt(blockIndex);

            // 1) Ensure the run cache covers this block
            bool inRun = object.ReferenceEquals(_rawRunOffset, record)
                && blockIndex >= _rawRunStartBlockIdx
                && blockIndex < _rawRunStartBlockIdx + _rawRunBlockCount;

            if (!inRun)
            {
                (byte[]? runData, int runLength, int[]? runOffsets, int[]? runSizes) = _imgReader.ReadBlockRun(record, blockIndex);
                if (runData != null)
                {
                    _rawRunBuffer = runData;
                    _rawRunOffset = record;
                    _rawRunStartBlockIdx = blockIndex;
                    _rawRunBlockCount = runLength;
                    _rawRunLocalOffsets = runOffsets;
                    _rawRunLocalSizes = runSizes;
                    inRun = true;
                }
            }

            // 2) Get raw bytes
            byte[] rawSource;
            int srcOffset, srcLength;

            if (inRun)
            {
                int i = blockIndex - _rawRunStartBlockIdx;
                srcOffset = _rawRunLocalOffsets![i];
                srcLength = _rawRunLocalSizes![i];
                rawSource = _rawRunBuffer!;
            }
            else
            {
                byte[]? rawData = _imgReader.GetRawBlockData(key);
                if (rawData == null || rawData.Length == 0) return null;
                rawSource = rawData;
                srcOffset = 0;
                srcLength = rawSource.Length;
            }

            if (srcLength == 0) return null;

            global::NKitDataStore.CompressionType compressionType = srcLength == expectedBlockLength
                ? global::NKitDataStore.CompressionType.None
                : global::NKitDataStore.CompressionType.Zstd;

            byte[] destBuffer = new byte[expectedBlockLength];
            (byte[]? buffer, int outOffset, int outLength) = _imgReader.GetBlockDataInternal(expectedBlockLength, rawSource, srcOffset, srcLength, destBuffer);

            if (outLength != expectedBlockLength)
                Trace.WriteLine($"[BlockProvider] SIZE MISMATCH: expected={expectedBlockLength} got={outLength} srcLen={srcLength} key=({key.XxHash64:X},{key.Crc32:X}) blockIdx={blockIndex}");

            // If it returned a zero-copy slice (uncompressed), we still need to own the array for BlockRecord 
            // since ImageBuilder expects a fresh byte[] or an array that won't be mutated.
            byte[] finalData = buffer;
            if (!object.ReferenceEquals(buffer, destBuffer) || outOffset != 0 || outLength != destBuffer.Length)
            {
                finalData = new byte[outLength];
                global::System.Array.Copy(buffer, outOffset, finalData, 0, outLength);
            }

            return new BlockRecord(key, compressionType, finalData);
        }
    }
}
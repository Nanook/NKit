using Nanook.GrindCore.XXHash;
using NKitDataStore.Compression;
using NKitDataStore.Interfaces;
using System.Diagnostics;

namespace NKitDataStore
{
    /// <summary>
    /// Provides a concrete implementation for writing a single, new image.
    /// This class manages an atomic transaction for the entire write process.
    /// Uses a dedicated write session for resource management.
    /// </summary>
    public class ImageWriter : ImageReader, IImageWriter
    {
        private new readonly IDataStoreDataAccess _dataAccess;
        private readonly IDataStoreTransaction _transaction;
        private readonly IBlockCompressor _compressor;
        private readonly int _blockSize;
        private readonly int _maxOffsetBlocks;
        private bool _isFinalized = false;

        /// <inheritdoc />
        public bool AlreadyExists { get; private set; }

        private int _compressionParallelism = 8; // default

        /// <summary>
        /// Optional action invoked after Dispose completes (regardless of commit/rollback).
        /// Used by DataStore to re-pack the embedded database for shardSize=0 sets.
        /// This must run even on rollback because UnpackForWrite separates the single
        /// file into a 2-file layout that must be restored.
        /// </summary>
        private Action? _postDisposeAction;

        /// <summary>
        /// Internal constructor. Instances should be created via IDataStore.CreateImage.
        /// </summary>
        /// <param name="dataAccess">The low-level data access layer.</param>
        /// <param name="image">The metadata record of the new image.</param>
        /// <param name="transaction">The database transaction to use for all write operations.</param>
        /// <param name="compressor">The block compressor to use for compression/decompression.</param>
        /// <param name="postDisposeAction">Optional action to run after dispose (e.g., re-pack embedded DB).</param>
        internal ImageWriter(IDataStoreDataAccess dataAccess, ImageRecord image, IDataStoreTransaction transaction, IBlockCompressor compressor, Action? postDisposeAction = null)
            : base(dataAccess, image, compressor, ownsDataAccess: false, createReadSession: false)
        {
            _dataAccess = dataAccess;
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
            _blockSize = Info.BlockSize;
            _maxOffsetBlocks = Info.MaxOffsetBlocks;
            _postDisposeAction = postDisposeAction;

            // Register writer for resource tracking
            try { _dataAccess.RegisterWriter(Image.SetName); } catch { }
        }

        /// <summary>
        /// Controls the maximum parallel background compression operations the writer will allow.
        /// Default is 8 when not changed.
        /// </summary>
        public int CompressionParallelism
        {
            get => _compressionParallelism;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), "CompressionParallelism must be >= 1");
                _compressionParallelism = value;
            }
        }

        /// <summary>
        /// Begins writing data to the image at the specified offset using a streaming approach.
        /// Returns a Stream that automatically handles block chunking and deduplication.
        /// When the stream is disposed, offset records are created automatically.
        /// This is the recommended API for writing data as it prevents consumer errors.
        /// </summary>
        /// <param name="offset">The byte offset within the image where data will be written.</param>
        /// <param name="type">The type of data being written (default: File).</param>
        /// <param name="offsetStart">The starting offset for grouping. Defaults to offset if null.</param>
        /// <returns>A writable stream that handles all block management automatically.</returns>
        /// <example>
        /// <code>
        /// using (var stream = writer.BeginWriteStream(0, BlockType.File))
        /// {
        ///     sourceStream.CopyTo(stream);
        /// } // Automatically flushes blocks and creates offset records on dispose
        /// </code>
        /// </example>
        public Stream BeginWriteStream(long offset, BlockType type = BlockType.File, long? offsetStart = null, DataStride? stride = null, long? strideOriginOffset = null)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");

            long effectiveOffsetStart = offsetStart ?? offset;
            return new ImageWriteStream(this, offset, type, effectiveOffsetStart, _blockSize, stride, strideOriginOffset);
        }

        /// <summary>
        /// Internal method called by ImageWriteStream to store a block with deduplication and compression.
        /// Uses the injected compressor and checks database BEFORE compressing (performance optimization).
        /// </summary>
        internal void WriteBlock(BlockKey blockKey, byte[] blockData, int offset, int length)
        {
            // Use the new lazy compression method that checks existence BEFORE compressing
            _dataAccess.InsertBlockWithCompression(
                Image.SetName,
                _transaction,
                blockKey,
                blockData,
                offset,
                length,
                _compressor,
                _compressionParallelism
            );
        }

        /// <summary>
        /// Legacy overload for backward compatibility.
        /// </summary>
        internal void WriteBlock(BlockKey blockKey, byte[] blockData) => WriteBlock(blockKey, blockData, 0, blockData.Length);

        /// <summary>
        /// Internal method called by ImageWriteStream to create offset record(s) for written blocks.
        /// Automatically segments large writes across multiple offset rows based on max_offset_blocks.
        /// Supports optional stride parameter; when provided offsets are treated as physical positions
        /// and the stride is used to advance to the next physical offset. When stride is null,
        /// behavior is identical to the legacy non-strided logic.
        /// </summary>
        internal void CreateOffsetRecords(long startOffset, long totalSize, BlockType type, List<BlockKey> blockKeys, long offsetStart, DataStride? stride = null, long? strideOriginOffset = null)
        {
            if (blockKeys == null) // Allow 0 byte files || blockKeys.Count == 0)
                return;

            // Check if transaction is still valid (may have been disposed during cleanup)
            if (_transaction == null)
                return; // Silently skip - ImageWriter is being disposed

            int remainingBlocks = blockKeys.Count;
            int blockIndex = 0;
            long currentOffset = startOffset;
            long bytesProcessed = 0;
            bool force0ByteWrite = totalSize == 0 && blockKeys.Count == 0;

            while (remainingBlocks > 0 || force0ByteWrite)
            {
                int blocksInThisOffset = Math.Min(remainingBlocks, _maxOffsetBlocks);

                // Defensive bounds check to prevent GetRange from going out of bounds
                // This can happen if _maxOffsetBlocks is 0 or if there's a calculation error
                blocksInThisOffset = Math.Min(blocksInThisOffset, blockKeys.Count - blockIndex);

                if (blocksInThisOffset <= 0 && !force0ByteWrite)
                    break; // Safety check: prevent infinite loop
                force0ByteWrite = false; // only do this once if needed

                long offsetSize;
                if (blockIndex + blocksInThisOffset < blockKeys.Count)
                    offsetSize = (long)blocksInThisOffset * _blockSize;
                else
                    offsetSize = totalSize - bytesProcessed;

                List<BlockKey> blocksForThisOffset = blockKeys.GetRange(blockIndex, blocksInThisOffset);
                _dataAccess.InsertOffset(Image.SetName, _transaction, Image.Id, currentOffset,
                    offsetSize, type, blocksForThisOffset, offsetStart);

                blockIndex += blocksInThisOffset;
                remainingBlocks -= blocksInThisOffset;
                bytesProcessed += offsetSize;

                if (stride == null) // advance by clean data size
                    currentOffset += offsetSize;
                else
                {
                    long strideBaseOffset = strideOriginOffset ?? 0;
                    long relativeCurrentOffset = currentOffset - strideBaseOffset;
                    long cleanCurrentOffset = stride.OffsetToClean(relativeCurrentOffset);
                    currentOffset = strideBaseOffset + stride.CleanToOffset(cleanCurrentOffset + offsetSize, false);
                }
            }
        }

        public void WriteData(long offset, Stream data, long length, BlockType type = BlockType.File, DataStride? stride = null, long? offsetStart = null)
        {
            // Validate inputs
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
            if (length <= 0)
                throw new ArgumentException("Length must be greater than zero", nameof(length));

            if (stride != null)
            {
                // Validate stride parameters
                if (stride.SourceBlockSize <= 0)
                    throw new ArgumentException("SourceBlockSize must be greater than zero", nameof(stride));
                if (stride.DataOffset < 0 || stride.DataOffset >= stride.SourceBlockSize)
                    throw new ArgumentOutOfRangeException(nameof(stride), "DataOffset must be within SourceBlockSize");
                if (stride.DataLength <= 0 || stride.DataOffset + stride.DataLength > stride.SourceBlockSize)
                    throw new ArgumentOutOfRangeException(nameof(stride), "DataOffset + DataLength must be within SourceBlockSize");

                // Unified strided write: extract clean data, write blocks, then create offset records using the unified routine
                long totalSourceBlocks = length / stride.SourceBlockSize;
                List<BlockKey> blockKeys = new List<BlockKey>();
                byte[] cleanDataBuffer = new byte[_blockSize];
                int cleanBufferPosition = 0;
                long totalCleanDataWritten = 0;

                byte[] sourceBuffer = new byte[stride.SourceBlockSize];
                long sourceBlocksRemaining = totalSourceBlocks;

                while (sourceBlocksRemaining > 0)
                {
                    // Read one strided source block
                    int bytesRead = 0;
                    while (bytesRead < stride.SourceBlockSize)
                    {
                        int read = data.Read(sourceBuffer, bytesRead, stride.SourceBlockSize - bytesRead);
                        if (read == 0)
                            throw new IOException($"Unexpected end of stream reading source block. Expected {stride.SourceBlockSize - bytesRead} more bytes.");
                        bytesRead += read;
                    }

                    int cleanDataInBlock = stride.DataLength;
                    int sourceOffset = stride.DataOffset;

                    while (cleanDataInBlock > 0)
                    {
                        int spaceInBuffer = _blockSize - cleanBufferPosition;
                        int toCopy = Math.Min(cleanDataInBlock, spaceInBuffer);

                        Array.Copy(sourceBuffer, sourceOffset, cleanDataBuffer, cleanBufferPosition, toCopy);
                        cleanBufferPosition += toCopy;
                        sourceOffset += toCopy;
                        cleanDataInBlock -= toCopy;

                        if (cleanBufferPosition == _blockSize)
                        {
                            BlockKey blockKey = new BlockKey(
                                XXHash64.Compute(cleanDataBuffer, 0, _blockSize),
                                Crc.Compute(cleanDataBuffer, 0, _blockSize)
                            );
                            WriteBlock(blockKey, cleanDataBuffer, 0, _blockSize);
                            blockKeys.Add(blockKey);

                            totalCleanDataWritten += _blockSize;
                            cleanBufferPosition = 0;
                        }
                    }

                    sourceBlocksRemaining--;
                }

                // Flush remaining partial block
                if (cleanBufferPosition > 0)
                {
                    BlockKey blockKey = new BlockKey(
                        XXHash64.Compute(cleanDataBuffer, 0, cleanBufferPosition),
                        Crc.Compute(cleanDataBuffer, 0, cleanBufferPosition)
                    );
                    WriteBlock(blockKey, cleanDataBuffer, 0, cleanBufferPosition);
                    blockKeys.Add(blockKey);
                    totalCleanDataWritten += cleanBufferPosition;
                }

                // Create offset records with PHYSICAL offsets (accounting for stride padding)
                // Physical offset starts AFTER the first stride padding
                long startPhysicalOffset = offset + stride.DataOffset;

                CreateOffsetRecords(
                    startPhysicalOffset,
                    totalCleanDataWritten,
                    type,
                    blockKeys,
                    offsetStart ?? offset,
                    stride,
                    offset
                );

                return;
            }

            // Use the new streaming API internally for non-strided writes
            using (Stream writeStream = BeginWriteStream(offset, type, offsetStart))
            {
                // Copy from source to write stream, which handles chunking automatically
                byte[] buffer = new byte[Math.Min(81920, length)]; // 80KB buffer for copying
                long remaining = length;

                while (remaining > 0)
                {
                    int bytesToRead = (int)Math.Min(buffer.Length, remaining);
                    int bytesRead = data.Read(buffer, 0, bytesToRead);

                    if (bytesRead == 0)
                        throw new IOException($"Unexpected end of stream. Expected {remaining} more bytes.");

                    writeStream.Write(buffer, 0, bytesRead);
                    remaining -= bytesRead;
                }
            } // Stream dispose handles block flushing and offset creation
        }

        public void WriteData(long offset, byte[] data, BlockType type = BlockType.File, long? offsetStart = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0)
                throw new ArgumentException("Data cannot be empty", nameof(data));

            WriteData(offset, new MemoryStream(data), data.Length, type, null, offsetStart);
        }

        public void WriteData(long offset, byte[] data, int dataOffset, int length, BlockType type = BlockType.File, DataStride? stride = null, long? offsetStart = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (dataOffset < 0 || dataOffset >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(dataOffset));
            if (length <= 0)
                throw new ArgumentException("Length must be greater than zero", nameof(length));
            if (dataOffset + length > data.Length)
                throw new ArgumentException("Data offset + length exceeds array bounds");

            WriteData(offset, new MemoryStream(data, dataOffset, length), length, type, stride, offsetStart);
        }

        public void WriteVerifiableVirtualData(long offset, Stream data, long length, BlockType type) =>
            // Placeholder for implementation:
            // 1. Read the stream to calculate CRC32 and xxHash64 of the full 'length' of data.
            // 2. Call dataAccess.InsertOffset with null blockKey and the calculated checksums.
            throw new NotImplementedException();

        public void WriteVerifiableVirtualData(long offset, byte[] data, BlockType type) => WriteVerifiableVirtualData(offset, new MemoryStream(data), data.Length, type);

        /// <summary>
        /// Creates a new area record for a logical region of the disc image.
        /// This should be called after writing all blocks for the area.
        /// </summary>
        /// <param name="offset">The byte offset within the image where the area starts.</param>
        /// <param name="size">The size of the area in bytes.</param>
        /// <param name="crc32">The CRC32 hash of the area's data for validation.</param>
        /// <param name="xxhash64">The XXHash64 hash of the area's data for validation.</param>
        /// <param name="metadata">Optional metadata for the area.</param>
        /// <returns>The ID of the newly created area.</returns>
        public long CreateArea(long offset, long size, uint crc32, ulong xxhash64, int sectionSize, AreaMetadata? metadata = null)
        {
            // Validate inputs
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size cannot be negative");
            if (sectionSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(sectionSize), "Section size must be positive");

            // Note: We can't validate against Image.Size here because it's not set until FinalizeImage
            // The validation will happen naturally when trying to read beyond the image bounds

            return _dataAccess.InsertArea(Image.SetName, _transaction, Image.Id, offset, size, crc32, xxhash64, metadata, sectionSize: sectionSize);
        }

        /// <summary>
        /// Creates a new area record with stride information for reconstructing block structure with hashes.
        /// This is used for Wii/WiiU partitions where data is stored clean but must be reconstructed with hashes.
        /// </summary>
        /// <param name="offset">The byte offset where the area starts.</param>
        /// <param name="size">The size of the area in bytes (with stride applied).</param>
        /// <param name="crc32">The CRC32 hash of the area's data.</param>
        /// <param name="xxhash64">The XXHash64 hash of the area's data.</param>
        /// <param name="strideBlockSize">The size of each strided block (e.g., 0x8000 for Wii).</param>
        /// <param name="strideDataOffset">Offset within each block where clean data starts (e.g., 0x400 for Wii hash area).</param>
        /// <param name="strideDataLength">Length of clean data within each block (e.g., 0x7C00 for Wii data area).</param>
        /// <param name="metadata">Optional metadata for the area.</param>
        /// <returns>The ID of the newly created area.</returns>
        public long CreateArea(long offset, long size, uint crc32, ulong xxhash64,
            int strideBlockSize, int strideDataOffset, int strideDataLength,
            int sectionSize, AreaMetadata? metadata = null)
        {
            // Validate inputs
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size cannot be negative");
            if (strideBlockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(strideBlockSize), "Stride block size must be positive");
            if (strideDataOffset < 0 || strideDataOffset >= strideBlockSize)
                throw new ArgumentOutOfRangeException(nameof(strideDataOffset), "Stride data offset must be within block size");
            if (strideDataLength <= 0 || strideDataOffset + strideDataLength > strideBlockSize)
                throw new ArgumentOutOfRangeException(nameof(strideDataLength), "Stride data offset + length must be within block size");
            if (sectionSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(sectionSize), "Section size must be positive");

            // Call InsertArea with stride parameters
            return _dataAccess.InsertArea(Image.SetName, _transaction, Image.Id, offset, size, crc32, xxhash64,
                metadata, strideBlockSize, strideDataOffset, strideDataLength, sectionSize);
        }

        /// <summary>
        /// Creates a new area record for a logical region of the disc image with optional stride information.
        /// This should be called after writing all blocks for the area.
        /// </summary>
        /// <param name="offset">The byte offset within the image where the area starts.</param>
        /// <param name="size">The size of the area in bytes.</param>
        /// <param name="crc32">The CRC32 hash of the area's data for validation.</param>
        /// <param name="xxhash64">The XXHash64 hash of the area's data for validation.</param>
        /// <param name="metadata">Optional metadata for the area.</param>
        /// <param name="stride">Optional stride information for this area (e.g., CD Mode 1, Wii blocks).</param>
        /// <returns>The ID of the newly created area.</returns>
        public long CreateArea(long offset, long size, uint crc32, ulong xxhash64, AreaMetadata? metadata = null, DataStride? stride = null)
        {
            // Validate inputs
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size cannot be negative");

            // Extract stride parameters if provided
            int? strideBlockSize = stride?.SourceBlockSize;
            int? strideDataOffset = stride?.DataOffset;
            int? strideDataLength = stride?.DataLength;

            return _dataAccess.InsertArea(Image.SetName, _transaction, Image.Id, offset, size, crc32, xxhash64,
                metadata, strideBlockSize, strideDataOffset, strideDataLength);
        }

        public void FinalizeImage(long size, uint crc32, ulong xxhash64)
        {
            // Only check for duplicates if the hashes are meaningful (non-zero)
            // This allows test scenarios and incomplete images to finalize normally
            bool duplicateExists = false;
            if (crc32 != 0 && xxhash64 != 0)
            {
                duplicateExists = _dataAccess.ImageExists(Image.SetName, Image.Name, crc32, xxhash64);
            }

            if (duplicateExists)
            {
                // Don't finalize - this will cause rollback in Dispose. Flag it so callers can
                // distinguish an already-stored duplicate from a genuine failure.
                _isFinalized = false;
                AlreadyExists = true;
                return;
            }

            _dataAccess.UpdateImageMetadata(Image.SetName, _transaction, Image.Id, size, crc32, xxhash64);
            _isFinalized = true;
            AlreadyExists = false;
        }

        /// <summary>
        /// Disposes the ImageWriter with deterministic, ordered cleanup:
        /// 1. Wait for background compression tasks
        /// 2. Commit or rollback transaction based on finalization state
        /// 3. Dispose base reader resources (compressor, cache, read session)
        /// 4. Run post-dispose action (e.g., re-pack embedded DB for shardSize=0)
        /// 
        /// Note: Write session lifecycle is managed by DataStore.CreateImage, not here.
        /// This is synchronous - no disposal races.
        /// </summary>
        public new void Dispose()
        {
            try
            {
                // 1. Ensure any background compression tasks for this set have completed
                try { _dataAccess.WaitForCompressionTasks(Image.SetName, -1); } catch { }

                // 2. Commit or rollback transaction based on finalization state
                if (_isFinalized)
                {
                    _transaction.Commit();
                }
                else
                    _transaction.Rollback();
            }
            finally
            {
                try
                {
                    _transaction.Dispose();
                }
                finally
                {
                    try
                    {
                        // Unregister writer
                        try { _dataAccess.UnregisterWriter(Image.SetName); } catch { }
                    }
                    finally
                    {
                        try
                        {
                            // 3. Dispose base reader resources (compressor, cache, read session)
                            base.Dispose();
                        }
                        finally
                        {
                            // 4. Run post-dispose action after all resources are released.
                            // This always runs (commit or rollback) to ensure the embedded
                            // DB format is restored after UnpackForWrite separated it.
                            if (_postDisposeAction != null)
                            {
                                try { _postDisposeAction(); }
                                catch (Exception ex)
                                {
                                    Trace.TraceWarning(
                                        $"ImageWriter.Dispose: postDisposeAction failed for set '{Image.SetName}'. " +
                                        $"Exception: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
                                }
                            }
                        }
                    }
                }
            }
        }

        public void UpdateAreaMetadata(long areaId, AreaMetadata metadata) => _dataAccess.UpdateAreaMetadata(Image.SetName, _transaction, areaId, metadata);

        public void WriteFile(string name, byte[] data, bool isSystem = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("File name cannot be null or empty", nameof(name));
            if (data == null || data.Length == 0)
                throw new ArgumentException("File data cannot be null or empty", nameof(data));

            _dataAccess.InsertFile(Image.SetName, _transaction, Image.Id, name, data, isSystem);
        }
    }
}
using NKitDataStore.Binary.Serialization;
using NKitDataStore.Interfaces;
using System.Collections.Concurrent;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Implements IDataStoreTransaction for the binary index format.
    /// Buffers all changes in memory until Commit() triggers the append + atomic header update.
    /// </summary>
    internal class BinaryTransaction : IDataStoreTransaction
    {
        private readonly BinaryIndexFile _indexFile;
        private readonly InMemoryBlockIndex _blockIndex;
        private readonly ShardFileManager? _shardFileManager;
        private readonly Action? _postCommitAction;

        /// <summary>
        /// Gets the set name this transaction is associated with.
        /// </summary>
        public string SetName { get; }

        /// <summary>
        /// Gets whether the transaction has been completed (committed or rolled back).
        /// </summary>
        public bool IsCompleted { get; private set; }

        // Pending state accumulated during the transaction
        private ImageRecord? _pendingImage;
        private List<AreaRecord> _pendingAreas = new();
        private List<OffsetRecord> _pendingOffsets = new();
        private List<FileRecord> _pendingFiles = new();
        private List<BlockIndexEntry> _pendingNewBlocks = new();
        private ConcurrentDictionary<BlockKey, (int FileId, long Offset, int Size)> _pendingBlockLocations = new();
        private readonly object _pendingBlocksLock = new();

        /// <summary>
        /// Creates a new BinaryTransaction.
        /// </summary>
        /// <param name="setName">The set name this transaction is associated with.</param>
        /// <param name="indexFile">The binary index file to commit changes to.</param>
        /// <param name="blockIndex">The in-memory block index for deduplication lookups.</param>
        /// <param name="shardFileManager">Optional shard file manager to flush on commit.</param>
        /// <param name="postCommitAction">Optional action to execute after a successful commit (e.g., re-embed for embedded mode).</param>
        public BinaryTransaction(string setName, BinaryIndexFile indexFile, InMemoryBlockIndex blockIndex, ShardFileManager? shardFileManager = null, Action? postCommitAction = null)
        {
            SetName = setName;
            _indexFile = indexFile;
            _blockIndex = blockIndex;
            _shardFileManager = shardFileManager;
            _postCommitAction = postCommitAction;
        }

        /// <summary>
        /// Sets the pending image record for this transaction.
        /// </summary>
        /// <param name="image">The image record to set.</param>
        public void SetPendingImage(ImageRecord image) => _pendingImage = image;

        /// <summary>
        /// Adds a pending area record to this transaction.
        /// </summary>
        /// <param name="area">The area record to add.</param>
        public void AddPendingArea(AreaRecord area) => _pendingAreas.Add(area);

        /// <summary>
        /// Adds a pending offset record to this transaction.
        /// </summary>
        /// <param name="offset">The offset record to add.</param>
        public void AddPendingOffset(OffsetRecord offset) => _pendingOffsets.Add(offset);

        /// <summary>
        /// Adds a pending file record to this transaction.
        /// </summary>
        /// <param name="file">The file record to add.</param>
        public void AddPendingFile(FileRecord file) => _pendingFiles.Add(file);

        /// <summary>
        /// Adds a new block entry to the pending new blocks list.
        /// </summary>
        /// <param name="entry">The block index entry to add.</param>
        public void AddPendingBlock(BlockIndexEntry entry)
        {
            lock (_pendingBlocksLock)
            {
                _pendingNewBlocks.Add(entry);
            }
        }

        /// <summary>
        /// Adds a pending block location mapping.
        /// </summary>
        /// <param name="key">The block key.</param>
        /// <param name="fileId">The shard file ID.</param>
        /// <param name="offset">The byte offset within the shard file.</param>
        /// <param name="size">The compressed size of the block.</param>
        public void AddPendingBlockLocation(BlockKey key, int fileId, long offset, int size) => _pendingBlockLocations[key] = (fileId, offset, size);

        /// <summary>
        /// Gets the in-memory block index for deduplication lookups during the transaction.
        /// </summary>
        public InMemoryBlockIndex BlockIndex => _blockIndex;

        /// <summary>
        /// Gets the number of pending area records.
        /// </summary>
        public int PendingAreaCount => _pendingAreas.Count;

        /// <summary>
        /// Updates the pending image's metadata fields (Size, Crc32, XxHash64).
        /// </summary>
        /// <param name="imageId">The image ID to update (must match the pending image).</param>
        /// <param name="size">The total uncompressed size of the image.</param>
        /// <param name="crc32">The CRC32 checksum of the image.</param>
        /// <param name="xxhash64">The XXHash64 of the image.</param>
        public void UpdatePendingImageMetadata(long imageId, long size, uint crc32, ulong xxhash64)
        {
            if (_pendingImage == null || _pendingImage.Id != imageId)
                throw new InvalidOperationException($"No pending image with ID {imageId} exists in this transaction.");

            _pendingImage.Size = size;
            _pendingImage.Crc32 = crc32;
            _pendingImage.XxHash64 = xxhash64;
        }

        /// <summary>
        /// Updates the pending image's rollback checkpoint fields.
        /// </summary>
        /// <param name="imageId">The image ID to update (must match the pending image).</param>
        /// <param name="rollbackFileId">The shard file ID at the rollback point.</param>
        /// <param name="rollbackOffset">The shard file offset at the rollback point.</param>
        public void UpdatePendingImageRollback(long imageId, int rollbackFileId, long rollbackOffset)
        {
            if (_pendingImage == null || _pendingImage.Id != imageId)
                throw new InvalidOperationException($"No pending image with ID {imageId} exists in this transaction.");

            _pendingImage.RollbackFileId = rollbackFileId;
            _pendingImage.RollbackOffset = rollbackOffset;
        }

        /// <summary>
        /// Updates the metadata for a pending area record by its 1-based index (area ID).
        /// </summary>
        /// <param name="areaId">The 1-based area ID (index into pending areas).</param>
        /// <param name="metadata">The new metadata to set.</param>
        public void UpdatePendingAreaMetadata(long areaId, AreaMetadata metadata)
        {
            // areaId is 1-based (returned by InsertArea as count after adding)
            int index = (int)areaId - 1;
            if (index < 0 || index >= _pendingAreas.Count)
                throw new InvalidOperationException($"Area ID {areaId} does not exist in this transaction.");

            _pendingAreas[index].Metadata = metadata;
        }

        /// <summary>
        /// Commits all pending changes: serializes sections, appends to the binary index file,
        /// updates the directory, and performs an atomic commit.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the transaction has already been completed.</exception>
        /// <exception cref="InvalidOperationException">If no pending image has been set.</exception>
        public void Commit()
        {
            if (IsCompleted)
                throw new InvalidOperationException("Transaction has already been completed.");

            if (_pendingImage == null)
            {
                // No pending image — nothing to commit (e.g., UpdateImageName already committed directly)
                IsCompleted = true;
                return;
            }

            // Step 1: Serialize Image_Metadata_Section from pending areas + files
            (byte[]? metadataSection, int metadataUncompressedSize) = ImageMetadataSectionSerializer.Serialize(_pendingAreas, _pendingFiles);

            // Step 2: Serialize Image_BlockMap_Section from pending offsets + block locations
            (byte[]? blockMapSection, int blockMapUncompressedSize) = ImageBlockMapSectionSerializer.Serialize(_pendingOffsets, new Dictionary<BlockKey, (int FileId, long Offset, int Size)>(_pendingBlockLocations));

            // Step 3: Prepare new blocks array (sorted) for the delta
            BlockIndexEntry[]? newBlocks = null;
            if (_pendingNewBlocks.Count > 0)
            {
                newBlocks = _pendingNewBlocks.ToArray();
                Array.Sort(newBlocks);
            }

            // Step 4: Append image sections (and optional block index delta) to the file
            (long metadataOffset, int metadataSize, long blockMapOffset, int blockMapSize) =
                _indexFile.AppendImage(_pendingImage.Id, metadataSection, blockMapSection, newBlocks);

            // Step 5: Update the directory with the new image entry
            ImageDirectory directory = _indexFile.GetDirectory();
            ImageDirectoryEntry dirEntry = new ImageDirectoryEntry
            {
                ImageId = _pendingImage.Id,
                Name = _pendingImage.Name,
                Size = _pendingImage.Size,
                Crc32 = _pendingImage.Crc32,
                XxHash64 = _pendingImage.XxHash64,
                System = _pendingImage.System,
                Format = _pendingImage.Format,
                RollbackFileId = _pendingImage.RollbackFileId,
                RollbackOffset = _pendingImage.RollbackOffset,
                Removed = false,
                MetadataSectionOffset = metadataOffset,
                MetadataSectionCompressedSize = metadataSize,
                BlockMapSectionOffset = blockMapOffset,
                BlockMapSectionCompressedSize = blockMapSize,
                MetadataSectionUncompressedSize = metadataUncompressedSize,
                BlockMapSectionUncompressedSize = blockMapUncompressedSize
            };
            directory.AddOrUpdate(dirEntry);

            // Step 6: Append the updated directory to the file
            _indexFile.UpdateDirectory(directory);

            // Step 6.5: Flush shard write buffer so data is visible to readers before index commit
            _shardFileManager?.FlushWrite();

            // Step 7: Atomic commit — flush data, write secondary header, flush, write primary header, flush
            _indexFile.AtomicCommit();

            // Step 8: Update the in-memory block index with new blocks
            if (newBlocks != null)
            {
                foreach (BlockIndexEntry entry in newBlocks)
                {
                    _blockIndex.AddEntry(entry);
                }
            }

            IsCompleted = true;

            // Step 9: Execute post-commit action (e.g., re-embed for embedded mode)
            _postCommitAction?.Invoke();
        }

        /// <summary>
        /// Discards all pending state without modifying the binary index file.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the transaction has already been completed.</exception>
        public void Rollback()
        {
            if (IsCompleted)
                throw new InvalidOperationException("Transaction has already been completed.");

            // Discard all pending state
            _pendingImage = null;
            _pendingAreas.Clear();
            _pendingOffsets.Clear();
            _pendingFiles.Clear();
            _pendingNewBlocks.Clear();
            _pendingBlockLocations.Clear();

            IsCompleted = true;
        }

        /// <summary>
        /// Disposes the transaction. If not already completed, calls Rollback().
        /// </summary>
        public void Dispose()
        {
            if (!IsCompleted)
            {
                Rollback();
            }
        }
    }
}
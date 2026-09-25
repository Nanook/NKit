using NKitDataStore.Binary;
using NKitDataStore.Compression;

namespace NKitDataStore.Interfaces
{
    /// <summary>
    /// Defines the low-level contract for all direct database read and write operations.
    /// This interface abstracts the underlying database implementation (e.g., SQLite, SQL Server, MongoDB)
    /// and is used by the higher-level business logic classes.
    /// Methods in this interface are expected to participate in transactions managed by the caller.
    /// </summary>
    public interface IDataStoreDataAccess : IDisposable
    {
        /// <summary>
        /// Ensures that the database files and schema for a given set exist.
        /// Creates the directory, main DB, and sharded block DBs if they don't exist.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <param name="shardSize">The maximum size of each shard in bytes. This is used to determine how many shard DBs to create.</param>
        /// <param name="blockSize">Maximum size of each stored item in bytes (0 = use default 64KiB).</param>
        InfoRecord EnsureSetExists(string setName, long shardSize, int blockSize = 0);

        /// <summary>
        /// Begins a new transaction for write operations on the specified set.
        /// The transaction should be committed or rolled back explicitly by the caller.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <returns>A transaction that must be disposed by the caller.</returns>
        IDataStoreTransaction BeginTransaction(string setName);

        /// <summary>
        /// Gets the set metadata from the info table.
        /// Reads schema version, block size, shardSize, and max_offset_blocks in a single query.

        /// </summary>
        InfoRecord GetSetInfo(string setName);

        /// <summary>
        /// Analyzes the database and returns comprehensive statistics.
        /// Scans all blocks, offsets, and images to calculate storage metrics.
        /// </summary>
        DataStoreStatistics GetSetStatistics(string setName, bool includePerImageStats, Action<int, int>? progress = null, CancellationToken cancellationToken = default);

        // --- Image Operations ---

        /// <summary>
        /// Inserts a new image metadata record.
        /// </summary>
        /// <returns>The ID of the newly created image.</returns>
        long InsertImage(string setName, IDataStoreTransaction transaction, string imageName, string? system, ImageFormat format);

        /// <summary>
        /// Updates the metadata (size and hashes) for an existing image.
        /// </summary>
        void UpdateImageMetadata(string setName, IDataStoreTransaction transaction, long imageId, long size, uint crc32, ulong xxhash64);

        /// <summary>
        /// Updates the name for an existing image.
        /// </summary>
        void UpdateImageName(string setName, IDataStoreTransaction transaction, long imageId, string imageName);

        /// <summary>
        /// Updates the format for an existing image.
        /// </summary>
        void UpdateImageFormat(string setName, IDataStoreTransaction transaction, long imageId, ImageFormat format);

        /// <summary>
        /// Retrieves a single image record by its ID.
        /// </summary>
        ImageRecord? GetImage(string setName, long imageId);

        /// <summary>
        /// Retrieves all image records for a given set.
        /// </summary>
        IEnumerable<ImageRecord> GetAllImagesInSet(string setName);

        /// <summary>
        /// Retrieves all image records for a given set, including those marked as removed.
        /// </summary>
        IEnumerable<ImageRecord> GetAllImagesInSetIncludingRemoved(string setName);

        /// <summary>
        /// Rolls back the database and shards to the state of the specified image.
        /// </summary>
        void Rollback(string setName, long imageId, IProgress<(int Percentage, string Stage)>? progress = null);

        /// <summary>
        /// Checks if an image with the specified name, crc32, and xxhash64 already exists in the set.
        /// Used to detect duplicate images before finalizing a write operation.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <param name="imageName">The name of the image.</param>
        /// <param name="crc32">The CRC32 hash of the image.</param>
        /// <param name="xxhash64">The XXHash64 hash of the image.</param>
        /// <returns>True if an image with matching name, crc32, and xxhash64 exists; otherwise false.</returns>
        bool ImageExists(string setName, string imageName, uint crc32, ulong xxhash64);

        // --- Area Operations ---

        /// <summary>
        /// Inserts a new area record for an image with optional metadata and stride information.
        /// </summary>
        /// <param name="strideBlockSize">The size of each strided block in the source (null if not strided).</param>
        /// <param name="strideDataOffset">The offset where data starts within each block (null if not strided).</param>
        /// <param name="strideDataLength">The length of data within each block (null if not strided).</param>
        /// <param name="sectionSize">The section size used for offset management (null to use default).</param>
        /// <returns>The ID of the newly created area.</returns>
        long InsertArea(string setName, IDataStoreTransaction transaction, long imageId, long offset, long size, uint crc32, ulong xxhash64, AreaMetadata? metadata = null, int? strideBlockSize = null, int? strideDataOffset = null, int? strideDataLength = null, int? sectionSize = null);

        /// <summary>
        /// Updates the metadata for an existing area.
        /// </summary>
        void UpdateAreaMetadata(string setName, IDataStoreTransaction transaction, long areaId, AreaMetadata metadata);

        /// <summary>
        /// Retrieves all area records for a given image.
        /// </summary>
        IEnumerable<AreaRecord> GetAreasForImage(string setName, long imageId);

        /// <summary>
        /// Flags an image as removed from the datastore.
        /// </summary>
        void DeleteImage(string setName, long imageId);

        /// <summary>
        /// Restores an image that was previously flagged as removed.
        /// </summary>
        void RestoreImage(string setName, long imageId);

        /// <summary>
        /// Compacts the set by permanently deleting images marked as removed and their unreferenced blocks.
        /// </summary>
        void CompactSet(string setName, IProgress<(int Percentage, string Stage)>? progress = null);

        /// <summary>
        /// Inspects the file state of a set and reports its health status.
        /// This is a read-only operation that does not modify any files.
        /// </summary>
        RecoveryState CheckSetHealth(string setName);

        // --- Offset Operations ---

        /// <summary>
        /// Inserts a new offset record, mapping a data segment to an image (single block).
        /// </summary>
        /// <param name="offsetStart">The offset of the first chunk in this logical file. Defaults to offset if null.</param>
        void InsertOffset(string setName, IDataStoreTransaction transaction, long imageId, long offset, long size, BlockType type, BlockKey? blockKey, uint crc32, ulong xxhash64, long? offsetStart = null);

        /// <summary>
        /// Inserts a new offset record with multiple blocks.
        /// This overload is used by the streaming API when writing data that spans multiple blocks.
        /// </summary>
        /// <param name="offsetStart">The starting offset for grouping. Defaults to offset if not provided.</param>
        void InsertOffset(string setName, IDataStoreTransaction transaction, long imageId, long offset, long size, BlockType type, List<BlockKey> blockKeys, long? offsetStart = null);

        /// <summary>
        /// Retrieves all offset records for a given image.
        /// </summary>
        IEnumerable<OffsetRecord> GetOffsetsForImage(string setName, long imageId);

        /// <summary>
        /// Retrieves all offset records for a given image that belong to a specific logical file.
        /// All offset records with the same offsetStart value belong to the same file.
        /// </summary>
        IEnumerable<OffsetRecord> GetOffsetsForFile(string setName, long imageId, long offsetStart);

        /// <summary>
        /// Efficient batch fetch: returns a mapping of offset -> decoded block key list for all offsets
        /// in the image that have blocks stored. This allows callers to pre-warm block lists with
        /// a single roundtrip to the database.
        /// </summary>
        Dictionary<long, List<BlockKey>> GetOffsetBlockLists(string setName, long imageId);

        /// <summary>
        /// Retrieves offset records for an image that fall within a specific byte range.
        /// </summary>
        IEnumerable<OffsetRecord> GetOffsetsInRange(string setName, long imageId, long startOffset, long length);

        // --- Block Operations ---

        /// <summary>
        /// Checks if a block with the given key exists in the data store.
        /// </summary>
        bool BlockExists(string setName, BlockKey key);

        /// <summary>
        /// Retrieves a block's data and compression type by its key.
        /// </summary>
        (CompressionType compressionType, byte[]? data) GetBlockData(string setName, BlockKey key);

        /// <summary>
        /// Inserts a new block into the appropriate shard database.
        /// The data should be pre-compressed according to compression rules before calling this method.
        /// </summary>
        void InsertBlock(string setName, IDataStoreTransaction transaction, BlockKey key, byte[] data);

        /// <summary>
        /// Inserts a new block into the appropriate shard database from a span.
        /// This overload enables zero-copy insertion when the data is within a larger buffer.
        /// The data should be pre-compressed according to compression rules before calling this method.
        /// </summary>
        void InsertBlock(string setName, IDataStoreTransaction transaction, BlockKey key, ReadOnlySpan<byte> data);

        // --- File Operations ---

        /// <summary>
        /// Inserts a file record and writes compressed data to the shard files.
        /// The data is zstd compressed and written via ShardFileManager.
        /// </summary>
        /// <param name="setName">The set name.</param>
        /// <param name="transaction">The active transaction.</param>
        /// <param name="imageId">The image the file belongs to.</param>
        /// <param name="name">The logical name for the file.</param>
        /// <param name="data">The uncompressed file data.</param>
        void InsertFile(string setName, IDataStoreTransaction transaction, long imageId, string name, byte[] data, bool isSystem);

        /// <summary>
        /// Retrieves a file's metadata record by image ID and name.
        /// </summary>
        FileRecord? GetFile(string setName, long imageId, string name);

        /// <summary>
        /// Retrieves all file records for a given image.
        /// </summary>
        IEnumerable<FileRecord> GetFilesForImage(string setName, long imageId);

        /// <summary>
        /// Reads the compressed file data from shard files and decompresses it.
        /// </summary>
        /// <param name="setName">The set name.</param>
        /// <param name="record">The file record with shard location info.</param>
        /// <returns>The decompressed file data, or null if the data could not be read.</returns>
        byte[]? ReadFileData(string setName, FileRecord record);

        /// <summary>
        /// Inserts a new block with lazy compression optimization.
        /// Checks if the block already exists in the database BEFORE compressing.
        /// If the block exists, compression is skipped entirely (performance optimization).
        /// </summary>
        /// <param name="setName">The set name.</param>
        /// <param name="transaction">The active transaction.</param>
        /// <param name="key">The block key (hash-based identifier).</param>
        /// <param name="uncompressedData">The uncompressed block data.</param>
        /// <param name="offset">Offset in the uncompressed data buffer.</param>
        /// <param name="length">Length of data to compress and store.</param>
        /// <param name="compressor">The compressor to use if the block doesn't exist.</param>
        /// <param name="compressionParallelism">The level of parallelism for compression,controls number of threads.</param>
        void InsertBlockWithCompression(string setName, IDataStoreTransaction transaction, BlockKey key,
            byte[] uncompressedData, int offset, int length, IBlockCompressor compressor, int compressionParallelism);

        /// <summary>
        /// Waits for any background compression tasks for the specified set to complete.
        /// This allows callers (e.g., ImageWriter) to ensure blocks have been persisted to shard DBs
        /// before committing transactions that insert offsets referencing those blocks.
        /// </summary>
        /// <param name="setName">Name of the set to wait for.</param>
        /// <param name="timeoutMs">Timeout in milliseconds to wait; -1 to wait indefinitely.</param>
        void WaitForCompressionTasks(string setName, int timeoutMs = 30000);

        /// <summary>
        /// Register a reader opening the specified set so the data access can eagerly open shards.
        /// </summary>
        void RegisterReader(string setName);

        /// <summary>
        /// Unregister a previously registered reader for the specified set.
        /// </summary>
        void UnregisterReader(string setName);

        /// <summary>
        /// Register a writer opening the specified set. Only one writer may be registered at a time.
        /// </summary>
        void RegisterWriter(string setName);

        /// <summary>
        /// Unregister a previously registered writer for the specified set.
        /// </summary>
        void UnregisterWriter(string setName);
    }
}
using NKitDataStore.Binary;

namespace NKitDataStore.Interfaces
{
    /// <summary>
    /// Top-level entry point for managing NKit data sets in a directory.
    /// This interface is the factory for creating image readers and writers.
    /// </summary>
    public interface IDataStore : IDisposable
    {
        /// <summary>
        /// Opens a reader for a specific, existing image.
        /// </summary>
        /// <param name="key">The globally unique key of the image to read.</param>
        /// <returns>An interface for reading the specified image.</returns>
        IImageReader OpenImageReader(GlobalImageKey key);

        /// <summary>
        /// Adds a new image to an existing set and returns a writer for it.
        /// </summary>
        /// <param name="setName">The name of the existing set to contain the image.</param>
        /// <param name="imageName">The name of the new image.</param>
        /// <param name="system">Optional system metadata for the image.</param>
        /// <param name="format">Optional format metadata for the image.</param>
        /// <returns>An interface for writing to the new image.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the specified set does not exist.</exception>
        IImageWriter AddImage(string setName, string imageName, string? system = null, ImageFormat format = ImageFormat.Unknown);

        /// <summary>
        /// Renames an existing image.
        /// </summary>
        /// <param name="key">The globally unique key of the image to rename.</param>
        /// <param name="newImageName">The new image name.</param>
        /// <returns>The updated image record.</returns>
        ImageRecord RenameImage(GlobalImageKey key, string newImageName);

        /// <summary>
        /// Renames an image TITLE as one atomic unit, handling the WiiU multi-TMD case: renames the
        /// <see cref="ImageFormat.TmdAppFolder"/> umbrella to <paramref name="newBaseName"/> AND every
        /// "[tmd.N]" child to "{base} [tmd.N]" (preserving each index), so the mount recombine stays
        /// intact. A plain image is renamed as-is. <paramref name="key"/> may identify the umbrella or
        /// any child. Returns the number of image records renamed.
        /// </summary>
        int RenameImageTitle(GlobalImageKey key, string newBaseName);

        /// <summary>
        /// Updates the format of an existing image record.
        /// </summary>
        /// <param name="key">The globally unique key of the image to update.</param>
        /// <param name="format">The new image format.</param>
        void UpdateImageFormat(GlobalImageKey key, ImageFormat format);

        /// <summary>
        /// Scans all WiiU images and corrects any disc images (WUD/WUX/ISO) that were
        /// incorrectly stored with <see cref="ImageFormat.App"/> and updates them to
        /// <see cref="ImageFormat.Iso"/>.
        /// </summary>
        /// <returns>Number of records repaired.</returns>
        int RepairWiiUImageFormats();

        /// <summary>
        /// Permanently deletes an image from the datastore.
        /// Identifies orphaned blocks and marks them in the block_removed table for future compaction.
        /// </summary>
        /// <param name="key">The globally unique key of the image to delete.</param>
        void DeleteImage(GlobalImageKey key);

        /// <summary>
        /// Restores a previously removed image by resetting its removed flag to 0.
        /// </summary>
        /// <param name="key">The globally unique key of the image to restore.</param>
        void RestoreImage(GlobalImageKey key);

        /// <summary>
        /// Compacts the set by permanently deleting images marked as removed and their unreferenced blocks.
        /// </summary>
        /// <param name="setName">The name of the set to compact.</param>
        void CompactSet(string setName, IProgress<(int Percentage, string Stage)>? progress = null);

        /// <summary>
        /// Inspects the file state of a set and reports its health status.
        /// This is a read-only operation that does not modify any files.
        /// </summary>
        RecoveryState CheckSetHealth(string setName);

        /// <summary>
        /// Rolls back the database and shards to the state of the specified image.
        /// </summary>
        /// <param name="key">The globally unique key of the image to rollback to.</param>
        void RollbackImage(GlobalImageKey key, IProgress<(int Percentage, string Stage)>? progress = null);

        /// <summary>
        /// Creates a new, empty set and initializes its info table.
        /// </summary>
        /// <param name="setName">The set path/name relative to the datastore root.</param>
        /// <param name="shardSize">Maximum size of each shard data file in bytes. Use 0 for a single embedded database file.</param>
        /// <param name="blockSize">Maximum size of each stored item in bytes.</param>
        /// <returns>Information describing the created set.</returns>
        SetInfo CreateSet(string setName, long shardSize = 50L * 1024 * 1024 * 1024, int blockSize = 0);

        /// <summary>
        /// Lists the names of all available data sets.
        /// </summary>
        IEnumerable<string> ListSetNames();

        /// <summary>
        /// Lists all images across all loaded sets, with an optional filter.
        /// </summary>
        IEnumerable<ImageRecord> ListAllImages(Func<ImageRecord, bool>? predicate = null);

        /// <summary>
        /// Retrieves descriptive information for all available data sets.
        /// </summary>
        /// <returns>A collection of objects containing details about each set.</returns>
        IEnumerable<SetInfo> DescribeSets();

        /// <summary>
        /// Retrieves descriptive information and all images for all available data sets in a single pass.
        /// Avoiding multiple SQLite database extractions.
        /// </summary>
        /// <param name="filterSetName">Optional set path/name relative to the datastore root indicating a specific set to load.</param>
        /// <param name="includeFileSystemYaml">Whether to eagerly read and return the filesystem.yaml contents for each image.</param>
        /// <param name="maxFileSystemYamlSize">Optional maximum stored (compressed) size in bytes for eager filesystem.yaml loading.
        /// When specified, only filesystem.yaml files whose FileRecord.Size is less than or equal to this value are read.
        /// When null, all filesystem.yaml files are read (preserving backward compatibility).</param>
        /// <returns>A collection of tuples containing set info, associated images, and optionally pre-loaded filesystem.yaml data.</returns>
        IEnumerable<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> DescribeSetsWithImages(string? filterSetName = null, bool includeFileSystemYaml = false, long? maxFileSystemYamlSize = null);

        /// <summary>
        /// Retrieves descriptive information for a specific data set.
        /// Includes configuration from the info table: sharsSize, block size, and max offset blocks.
        /// </summary>
        /// <param name="setName">The name of the set to describe.</param>
        /// <returns>Information about the set, or null if the set does not exist.</returns>
        SetInfo? GetSetInfo(string setName, bool includeStats = false);

        /// <summary>
        /// Analyzes a data set and returns comprehensive statistics.
        /// This method scans all tables to calculate storage efficiency, deduplication ratios,
        /// compression effectiveness, and other metrics.
        /// </summary>
        /// <param name="setName">The name of the set to analyze.</param>
        /// <param name="includePerImageStats">Whether to include detailed per-image statistics.</param>
        /// <returns>Comprehensive statistics about the set, or null if the set does not exist.</returns>
        /// <remarks>
        /// This operation scans all blocks, offsets, and images in the set, so it may take
        /// a few seconds for large sets (millions of blocks). Use sparingly in production.
        /// </remarks>
        DataStoreStatistics? GetSetStatistics(string setName, bool includePerImageStats = false, Action<int, int>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a named file stored alongside an image without opening a full image reader.
        /// This is a lightweight operation that avoids the overhead of creating an ImageReader
        /// (sessions, areas, offsets, buffer caches).
        /// </summary>
        /// <param name="key">The globally unique key of the image.</param>
        /// <param name="name">The logical name of the file.</param>
        /// <returns>The decompressed file data, or null if the file does not exist.</returns>
        byte[]? ReadFile(GlobalImageKey key, string name);
    }
}
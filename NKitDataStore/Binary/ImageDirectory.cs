using NKitDataStore.Binary.Serialization;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// In-memory representation of the Image Directory.
    /// Loaded on file open, updated during writes.
    /// Maps image IDs to their metadata and file offsets for both
    /// Image_Metadata_Section and Image_BlockMap_Section.
    /// </summary>
    internal class ImageDirectory
    {
        private readonly Dictionary<long, ImageDirectoryEntry> _entries = new();

        /// <summary>
        /// Gets the number of entries in the directory (including removed).
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Gets the entry for the specified image ID, or null if not found.
        /// </summary>
        /// <param name="imageId">The image ID to look up.</param>
        /// <returns>The directory entry, or null if the image ID does not exist.</returns>
        public ImageDirectoryEntry? GetEntry(long imageId) => _entries.TryGetValue(imageId, out ImageDirectoryEntry? entry) ? entry : null;

        /// <summary>
        /// Gets all entries in the directory, including removed entries.
        /// </summary>
        /// <returns>All directory entries.</returns>
        public IEnumerable<ImageDirectoryEntry> GetAllEntries() => _entries.Values;

        /// <summary>
        /// Gets only entries that are not marked as removed.
        /// </summary>
        /// <returns>Non-removed directory entries.</returns>
        public IEnumerable<ImageDirectoryEntry> GetNonRemovedEntries() => _entries.Values.Where(e => !e.Removed);

        /// <summary>
        /// Adds a new entry or replaces an existing entry by ImageId.
        /// </summary>
        /// <param name="entry">The entry to add or update.</param>
        public void AddOrUpdate(ImageDirectoryEntry entry) => _entries[entry.ImageId] = entry;

        /// <summary>
        /// Sets the Removed flag on the entry with the specified image ID.
        /// </summary>
        /// <param name="imageId">The image ID to mark.</param>
        /// <param name="removed">True to mark as removed, false to restore.</param>
        /// <exception cref="KeyNotFoundException">Thrown if the image ID does not exist in the directory.</exception>
        public void MarkRemoved(long imageId, bool removed)
        {
            if (!_entries.TryGetValue(imageId, out ImageDirectoryEntry? entry))
                throw new KeyNotFoundException($"Image ID {imageId} not found in the directory.");

            entry.Removed = removed;
        }

        /// <summary>
        /// Permanently removes an entry from the directory. Used by rollback to physically
        /// delete images that were added after the rollback target.
        /// </summary>
        /// <param name="imageId">The image ID to remove.</param>
        /// <returns>True if the entry was found and removed, false if it did not exist.</returns>
        public bool RemoveEntry(long imageId) => _entries.Remove(imageId);

        /// <summary>
        /// Returns the next available image ID (max existing ID + 1, or 1 if empty).
        /// </summary>
        /// <returns>The next image ID to use.</returns>
        public long GetNextImageId()
        {
            if (_entries.Count == 0)
                return 1;

            return _entries.Keys.Max() + 1;
        }

        /// <summary>
        /// Serializes the directory to a byte array using <see cref="ImageDirectorySerializer"/>.
        /// </summary>
        /// <returns>The serialized byte array.</returns>
        public byte[] Serialize() => ImageDirectorySerializer.Serialize(GetAllEntries());

        /// <summary>
        /// Deserializes an Image_Directory from a byte span, building a new ImageDirectory instance.
        /// </summary>
        /// <param name="data">The raw bytes containing the serialized Image_Directory.</param>
        /// <returns>A new <see cref="ImageDirectory"/> populated with the deserialized entries.</returns>
        public static ImageDirectory Deserialize(ReadOnlySpan<byte> data)
        {
            List<ImageDirectoryEntry> entries = ImageDirectorySerializer.Deserialize(data);
            ImageDirectory directory = new ImageDirectory();
            foreach (ImageDirectoryEntry entry in entries)
            {
                directory.AddOrUpdate(entry);
            }
            return directory;
        }
    }
}
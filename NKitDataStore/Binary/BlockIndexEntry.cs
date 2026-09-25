namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents a single entry in the Block Index.
    /// Each entry maps a BlockKey to its physical location in a shard file.
    /// Entries are sorted by (XxHash64, Crc32) ascending for binary search.
    /// Entry size: 28 bytes.
    /// </summary>
    internal struct BlockIndexEntry : IComparable<BlockIndexEntry>
    {
        /// <summary>
        /// The content-addressed key identifying this block.
        /// </summary>
        public BlockKey Key;

        /// <summary>
        /// The shard file ID where this block is stored.
        /// </summary>
        public int FileId;

        /// <summary>
        /// The byte offset within the shard file where this block's data begins.
        /// </summary>
        public long Offset;

        /// <summary>
        /// The compressed size of this block in the shard file.
        /// </summary>
        public int Size;

        /// <summary>
        /// Compares entries by BlockKey: first by XxHash64, then by Crc32 (ascending).
        /// </summary>
        public int CompareTo(BlockIndexEntry other)
        {
            int hashCompare = Key.XxHash64.CompareTo(other.Key.XxHash64);
            if (hashCompare != 0)
                return hashCompare;

            return Key.Crc32.CompareTo(other.Key.Crc32);
        }
    }
}
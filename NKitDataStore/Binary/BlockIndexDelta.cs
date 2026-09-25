namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents a Block_Index_Delta — an append-only fragment containing
    /// newly added blocks from a single write operation.
    /// Deltas form a linked list via NextDeltaOffset, pointing to the previous delta.
    /// </summary>
    internal class BlockIndexDelta
    {
        /// <summary>
        /// The sorted array of block index entries in this delta.
        /// Sorted by (XxHash64, Crc32) ascending, same format as the main Block_Index.
        /// </summary>
        public BlockIndexEntry[] Entries { get; set; } = Array.Empty<BlockIndexEntry>();

        /// <summary>
        /// File offset of the previous (next-older) delta in the linked list.
        /// 0 indicates this is the last delta in the chain (no more deltas).
        /// </summary>
        public long NextDeltaOffset { get; set; }
    }
}
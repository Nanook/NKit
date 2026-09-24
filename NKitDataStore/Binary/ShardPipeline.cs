namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents a chunk of data in a shard file (block or file record).
    /// </summary>
    internal readonly struct DataChunk
    {
        public bool IsBlock { get; }
        public BlockKey BlockKey { get; }
        public int BlockEntryIndex { get; }
        public long ImageId { get; }
        public string FileName { get; }
        public int FileId { get; }
        public long Offset { get; }
        public long Size { get; }

        public DataChunk(bool IsBlock, BlockKey BlockKey, int BlockEntryIndex, long ImageId, string FileName, int FileId, long Offset, long Size)
        {
            this.IsBlock = IsBlock;
            this.BlockKey = BlockKey;
            this.BlockEntryIndex = BlockEntryIndex;
            this.ImageId = ImageId;
            this.FileName = FileName;
            this.FileId = FileId;
            this.Offset = Offset;
            this.Size = Size;
        }
    }

    /// <summary>
    /// Result of classifying chunks as referenced or unreferenced.
    /// </summary>
    internal class ChunkClassification
    {
        public List<DataChunk> ReferencedChunks { get; set; } = new();
        public List<int> UnreferencedBlockIndices { get; set; } = new();
    }

    /// <summary>
    /// Result of compacting a single shard.
    /// </summary>
    internal class ShardCompactResult
    {
        public int FileId { get; set; }
        public Dictionary<BlockKey, long> BlockOffsetUpdates { get; set; } = new();
        public Dictionary<(long ImageId, string FileName, int FileId), long> FileOffsetUpdates { get; set; } = new();
        public bool WasCompacted { get; set; }

        /// <summary>
        /// The final shard path (the original file that the temp will replace). Set when
        /// <see cref="WasCompacted"/> is true so the caller can promote the temp after the
        /// index has been committed.
        /// </summary>
        public string? ShardPath { get; set; }

        /// <summary>
        /// The path of the compacted temp file that has been fully written and flushed but
        /// NOT yet renamed over the original. The caller renames this into place ONLY after
        /// the block index has been durably committed, so a crash before commit leaves the
        /// originals authoritative and the temp is discarded on recovery.
        /// </summary>
        public string? TempShardPath { get; set; }
    }
}
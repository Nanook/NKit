namespace NKitDataStore
{
    /// <summary>
    /// Lightweight snapshot of an index file's health. All fields are read from the in-memory
    /// header - no disk I/O is performed. Returned by <see cref="DataStore.GetIndexHealth"/>.
    /// </summary>
    public class IndexHealthInfo
    {
        /// <summary>
        /// Number of Block_Index_Delta entries appended since the last compaction.
        /// Each image write appends one delta. High counts degrade cold-start time because
        /// <c>BeginTransaction</c> must traverse the entire delta chain on each new process start.
        /// </summary>
        public int DeltaCount { get; set; }

        /// <summary>
        /// True if any images in the set are marked as removed but have not yet been purged
        /// by a compact operation. Removed-image sections still occupy space in the index file.
        /// </summary>
        public bool HasRemovedImages { get; set; }

        /// <summary>
        /// Current on-disk size of the index file in bytes.
        /// </summary>
        public long IndexFileSizeBytes { get; set; }

        /// <summary>
        /// True when <see cref="DeltaCount"/> exceeds <see cref="CompactionThreshold"/> or
        /// <see cref="HasRemovedImages"/> is true. Callers should surface a recommendation
        /// to run <c>CompactSetFast</c> when this is true.
        /// </summary>
        public bool NeedsCompaction => this.DeltaCount > CompactionThreshold || this.HasRemovedImages;

        /// <summary>
        /// Number of deltas above which cold-start performance begins to degrade noticeably.
        /// Below this threshold the delta chain traversal is fast even on spinning disks.
        /// </summary>
        public const int CompactionThreshold = 20;
    }
}
namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents the health status of a data set.
    /// </summary>
    public enum SetHealthStatus
    {
        /// <summary>All files are consistent, no recovery needed.</summary>
        Clean,
        /// <summary>Intermediate files detected, automatic recovery is possible.</summary>
        NeedsRecovery,
        /// <summary>Critical files missing, recovery is not possible.</summary>
        Unrecoverable
    }

    /// <summary>
    /// Detailed recovery state returned by CheckSetHealth.
    /// </summary>
    public class RecoveryState
    {
        public SetHealthStatus Status { get; set; }
        public string Description { get; set; } = "";
        public List<string> DetectedIssues { get; set; } = new();
        public List<OrphanedShardInfo> OrphanedShards { get; set; } = new();
    }

    /// <summary>
    /// Information about orphaned tail data in a shard file.
    /// </summary>
    public class OrphanedShardInfo
    {
        public string ShardPath { get; set; } = "";
        public long ExpectedSize { get; set; }
        public long ActualSize { get; set; }
        public long OrphanedBytes => ActualSize - ExpectedSize;
    }
}
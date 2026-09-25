namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the storage statistics graphs dialog.
/// Provides aggregate set-level stats for the waterfall bar chart visualization.
/// Pipeline: Original → After Junk Removal → After Dedup → After Compression
/// </summary>
public class StatsGraphsViewModel : ViewModelBase
{
    // Set-level aggregate stats for the waterfall chart
    public long TotalImageSize { get; set; }
    public long AfterJunkRemovalSize { get; set; }  // TotalBlockRefs × BlockSize (data that needed storing, excluding recreatable junk/nulls)
    public long AfterDedupSize { get; set; }  // UniqueBlocks × BlockSize (logical unique size before compression)
    public long AfterCompressionSize { get; set; }  // TotalPhysicalBlockStorage (actual stored)

    // Block size for display
    public int BlockSize { get; set; }

    // Counts
    public long TotalBlockRefs { get; set; }
    public long UniqueBlocks { get; set; }
    public long CompressedBlocks { get; set; }
    public long UncompressedBlocks { get; set; }

    // Derived display values
    public string TotalImageSizeDisplay => FormatBytes(TotalImageSize);
    public string AfterJunkRemovalSizeDisplay => FormatBytes(AfterJunkRemovalSize);
    public string AfterDedupSizeDisplay => FormatBytes(AfterDedupSize);
    public string AfterCompressionSizeDisplay => FormatBytes(AfterCompressionSize);

    public string JunkRemovedDisplay => FormatBytes(TotalImageSize - AfterJunkRemovalSize);
    public string DedupSavingsDisplay => FormatBytes(AfterJunkRemovalSize - AfterDedupSize);
    public string CompressionSavingsDisplay => FormatBytes(AfterDedupSize - AfterCompressionSize);
    public string TotalSavingsDisplay => FormatBytes(TotalImageSize - AfterCompressionSize);

    public double JunkRemovalPct => TotalImageSize > 0 ? (1.0 - ((double)AfterJunkRemovalSize / TotalImageSize)) * 100 : 0;
    public double DedupReductionPct => AfterJunkRemovalSize > 0 ? (1.0 - ((double)AfterDedupSize / AfterJunkRemovalSize)) * 100 : 0;
    public double CompressionReductionPct => AfterDedupSize > 0 ? (1.0 - ((double)AfterCompressionSize / AfterDedupSize)) * 100 : 0;
    public double TotalReductionPct => TotalImageSize > 0 ? (1.0 - ((double)AfterCompressionSize / TotalImageSize)) * 100 : 0;

    /// <summary>
    /// Final ratio: stored size as a percentage of original size (e.g. "42.9%" means stored at 42.9% of original).
    /// </summary>
    public double FinalRatio => TotalImageSize > 0 ? (double)AfterCompressionSize / TotalImageSize * 100 : 0;
    public string FinalRatioDisplay => TotalImageSize > 0 ? $"{FinalRatio:F1}%" : "";

    public double CompressedBlocksPct => UniqueBlocks > 0 ? (double)CompressedBlocks / UniqueBlocks * 100 : 0;

    // Bar widths (normalized to max = 1.0 for the UI)
    public double OriginalBarWidth => 1.0;
    public double AfterJunkRemovalBarWidth => TotalImageSize > 0 ? (double)AfterJunkRemovalSize / TotalImageSize : 0;
    public double AfterDedupBarWidth => TotalImageSize > 0 ? (double)AfterDedupSize / TotalImageSize : 0;
    public double AfterCompressionBarWidth => TotalImageSize > 0 ? (double)AfterCompressionSize / TotalImageSize : 0;

    // Stacked bar segment widths (each as a fraction of the total, for Grid column proportions)
    // Segments from left to right: Stored | Compression Saved | Dedup Saved | Createable Removed
    public double StoredSegment => TotalImageSize > 0 ? (double)AfterCompressionSize / TotalImageSize : 0;
    public double CompressionSavedSegment => TotalImageSize > 0 ? (double)(AfterDedupSize - AfterCompressionSize) / TotalImageSize : 0;
    public double DedupSavedSegment => TotalImageSize > 0 ? (double)(AfterJunkRemovalSize - AfterDedupSize) / TotalImageSize : 0;
    public double CreateableRemovedSegment => TotalImageSize > 0 ? (double)(TotalImageSize - AfterJunkRemovalSize) / TotalImageSize : 0;

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }
}
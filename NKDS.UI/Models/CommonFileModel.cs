namespace NkdsUi.Models;

/// <summary>
/// A shared file group identified by common BlockKeys across multiple selected images.
/// </summary>
public class CommonFileModel
{
    /// <summary>
    /// The display name for this file group (AreaMetadata name if available, otherwise hex offset_start).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The starting offset of this file group within the image.
    /// </summary>
    public long OffsetStart { get; init; }

    /// <summary>
    /// The number of blocks shared across all selected images for this file group.
    /// </summary>
    public int SharedBlockCount { get; init; }

    /// <summary>
    /// The total size in bytes of this file group.
    /// </summary>
    public long TotalSizeBytes { get; init; }
}
using Nanook.NKit;

namespace NKDS.Models;

/// <summary>
/// Represents a single image discovered during the Pre-Scan phase.
/// Carries the minimum data needed to populate a Pending UI row.
/// </summary>
public sealed class CandidateImage
{
    /// <summary>Temporary negative ID assigned sequentially (starting at -1).</summary>
    public required long TempId { get; init; }

    /// <summary>Display name with disambiguation suffix (e.g., "game [tmd.0]").</summary>
    public required string DisambiguatedName { get; init; }

    /// <summary>File size in bytes (from SourceFile.Length).</summary>
    public required long Size { get; init; }

    /// <summary>The original SourceFile reference for pipeline execution.</summary>
    public required SourceFile Source { get; init; }

    /// <summary>Index within the scan result list (preserves insertion order).</summary>
    public required int Index { get; init; }
}
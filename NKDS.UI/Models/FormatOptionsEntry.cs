using System.Text.Json.Serialization;

namespace NkdsUi.Models;

/// <summary>
/// Represents persisted format-specific options for a single (System, SourceFormat, TargetFormat) combination.
/// </summary>
public sealed class FormatOptionsEntry
{
    /// <summary>RVZ encoding type: "none", "zstd", or "lzma". Null when not applicable.</summary>
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    /// <summary>RVZ compression level: 1–22 for ZStd, 1–9 for LZMA. Null for None encoding or non-RVZ formats.</summary>
    [JsonPropertyName("level")]
    public int? Level { get; set; }

    /// <summary>RVZ block size: "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb". Null when not applicable.</summary>
    [JsonPropertyName("blockSize")]
    public string? BlockSize { get; set; }

    /// <summary>CISO/WBFS lossless mode. Null when not applicable.</summary>
    [JsonPropertyName("lossless")]
    public bool? Lossless { get; set; }
}
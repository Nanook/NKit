using NkdsUi.Models;

namespace NkdsUi.Services;

public sealed class AppConfig
{
    public List<string> MountPathHistory { get; set; } = new();
    public List<string> DataStoreHistory { get; set; } = new();
    public List<string> CreateSetHistory { get; set; } = new();
    public List<string> ExportPathHistory { get; set; } = new();
    public List<string> OgmrYamlHistory { get; set; } = new();
    public List<string> OgmrOutputHistory { get; set; } = new();
    public List<string> DatPathHistory { get; set; } = new();
    public List<string> ImageBrowseHistory { get; set; } = new();
    public Dictionary<string, string> ExportFormatDefaults { get; set; } = new();

    /// <summary>
    /// Format-specific options keyed by "System|SourceFormat|TargetFormat".
    /// </summary>
    public Dictionary<string, FormatOptionsEntry> FormatOptions { get; set; } = new();

    /// <summary>
    /// Persisted DataGrid column widths keyed by column Tag name.
    /// </summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = new();

    public WindowStateConfig? WindowState { get; set; }

    /// <summary>
    /// Last-used mount UID override (Linux only). Empty string means not set.
    /// </summary>
    public string MountUid { get; set; } = "";

    /// <summary>
    /// Last-used mount GID override (Linux only). Empty string means not set.
    /// </summary>
    public string MountGid { get; set; } = "";

    /// <summary>
    /// Last-used mount AllowOther setting (Linux only).
    /// </summary>
    public bool MountAllowOther { get; set; }

    /// <summary>
    /// Window decoration mode for Linux. Null means default (client-side decorations).
    /// Stored as string for forward compatibility.
    /// </summary>
    public string? WindowDecorationMode { get; set; }

    /// <summary>
    /// Key and fix file path settings for NKit pipeline integration.
    /// </summary>
    public KeysAndFixPathsConfig? KeysAndFixPaths { get; set; }

    public static AppConfig CreateDefault() => new()
    {
        KeysAndFixPaths = KeysAndFixPathsConfig.CreateDefault()
    };
}
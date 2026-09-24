using NkdsUi.Models;

namespace NkdsUi.Services;

/// <summary>
/// Interface for unified configuration persistence operations.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Gets the most-recently-used mount path history (max 10, most recent first).
    /// </summary>
    IReadOnlyList<string> MountPathHistory { get; }

    /// <summary>
    /// Adds a mount path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddMountPath(string path);

    /// <summary>
    /// Gets the most-recently-used DataStore path history (max 10, most recent first).
    /// </summary>
    IReadOnlyList<string> DataStoreHistory { get; }

    /// <summary>
    /// Adds a DataStore path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddDataStorePath(string path);

    /// <summary>
    /// Gets the most-recently-used Create Set folder path history (max 10, most recent first).
    /// Separate from DataStoreHistory to avoid pre-populating with .nkds file paths.
    /// </summary>
    IReadOnlyList<string> CreateSetHistory { get; }

    /// <summary>
    /// Adds a Create Set folder path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddCreateSetPath(string path);

    /// <summary>
    /// Gets the preferred export format for a given system and source format combination.
    /// Returns null if no preference has been set.
    /// </summary>
    string? GetExportFormat(string system, string sourceFormat);

    /// <summary>
    /// Sets the preferred export format for a given system and source format combination.
    /// </summary>
    void SetExportFormat(string system, string sourceFormat, string targetFormat);

    /// <summary>
    /// Gets the persisted window state, or null if no state has been saved.
    /// </summary>
    WindowStateConfig? WindowState { get; }

    /// <summary>
    /// Gets the most-recently-used export output path history (max 10, most recent first).
    /// </summary>
    IReadOnlyList<string> ExportPathHistory { get; }

    /// <summary>
    /// Adds an export output path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddExportPath(string path);

    /// <summary>
    /// Persists the window position and size.
    /// </summary>
    void SetWindowState(int width, int height, int x, int y);

    /// <summary>
    /// Gets persisted format options for a (system, sourceFormat, targetFormat) combination.
    /// Returns null if no options have been persisted.
    /// </summary>
    FormatOptionsEntry? GetFormatOptions(string system, string sourceFormat, string targetFormat);

    /// <summary>
    /// Sets format options for a (system, sourceFormat, targetFormat) combination.
    /// </summary>
    void SetFormatOptions(string system, string sourceFormat, string targetFormat, FormatOptionsEntry options);

    /// <summary>
    /// Gets persisted DataGrid column widths, or null if none have been saved.
    /// </summary>
    IReadOnlyDictionary<string, double>? ColumnWidths { get; }

    /// <summary>
    /// Persists DataGrid column widths keyed by column Tag name.
    /// </summary>
    void SetColumnWidths(Dictionary<string, double> widths);

    /// <summary>
    /// Gets the most-recently-used 1GMR YAML path history (max 10, most recent first).
    /// </summary>
    IReadOnlyList<string> OgmrYamlHistory { get; }

    /// <summary>
    /// Adds a 1GMR YAML path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddOgmrYamlPath(string path);

    /// <summary>
    /// Gets the most-recently-used 1GMR output path history (max 10, most recent first).
    /// </summary>
    IReadOnlyList<string> OgmrOutputHistory { get; }

    /// <summary>
    /// Adds a 1GMR output path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddOgmrOutputPath(string path);

    /// <summary>
    /// Gets the most-recently-used dat file path history (max 10, most recent first).
    /// </summary>
    IReadOnlyList<string> DatPathHistory { get; }

    /// <summary>
    /// Adds a dat file path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddDatPath(string path);

    /// <summary>
    /// Gets the most-recently-used image browse path history (max 10, most recent first).
    /// Used by Add Images and 1GMR image pickers to remember the last browsed directory.
    /// </summary>
    IReadOnlyList<string> ImageBrowseHistory { get; }

    /// <summary>
    /// Adds an image browse path to the MRU history. Duplicates are moved to the front.
    /// </summary>
    void AddImageBrowsePath(string path);

    /// <summary>
    /// Gets the last-used mount UID override text (Linux only). Empty if not set.
    /// </summary>
    string MountUid { get; }

    /// <summary>
    /// Gets the last-used mount GID override text (Linux only). Empty if not set.
    /// </summary>
    string MountGid { get; }

    /// <summary>
    /// Gets the last-used mount AllowOther setting (Linux only).
    /// </summary>
    bool MountAllowOther { get; }

    /// <summary>
    /// Persists the mount Linux options (UID, GID, AllowOther).
    /// </summary>
    void SetMountLinuxOptions(string uid, string gid, bool allowOther);

    /// <summary>
    /// Gets the persisted window decoration mode. Returns ClientSideDecorations for null/unrecognized values.
    /// </summary>
    WindowDecorationMode GetWindowDecorationMode();

    /// <summary>
    /// Persists the window decoration mode to the configuration file.
    /// </summary>
    void SetWindowDecorationMode(WindowDecorationMode mode);

    /// <summary>
    /// Gets all key and fix file path settings.
    /// </summary>
    KeysAndFixPathsConfig KeysAndFixPaths { get; }

    /// <summary>
    /// Persists updated key and fix file path settings.
    /// </summary>
    void SetKeysAndFixPaths(KeysAndFixPathsConfig paths);

    /// <summary>
    /// Resets all key and fix file paths to their defaults.
    /// </summary>
    void ResetKeysAndFixPathsToDefaults();
}
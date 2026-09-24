using NkdsUi.Models;
using System.Text.Json;

namespace NkdsUi.Services;

/// <summary>
/// Unified configuration persistence service that consolidates all user preferences
/// into a single nkds-ui.json file stored next to the application executable.
/// </summary>
public sealed class ConfigService : IConfigService
{
    internal static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "nkds-ui.json");

    internal static readonly string LegacyPreferencesPath = Path.Combine(
        AppContext.BaseDirectory, "export-format-preferences.json");

    private readonly string _configPath;
    private readonly string _legacyPath;
    private AppConfig _config;
    private ErrorNotificationService? _errorNotification;

    public ConfigService()
        : this(ConfigPath, LegacyPreferencesPath)
    {
    }

    /// <summary>
    /// Internal constructor for testing with isolated file paths.
    /// </summary>
    internal ConfigService(string configPath, string legacyPath)
    {
        _configPath = configPath;
        _legacyPath = legacyPath;
        _config = Load();
        MigrateLegacyPreferences();
    }

    /// <summary>
    /// Sets the ErrorNotificationService for centralized error reporting.
    /// Called after construction since ConfigService is created before ErrorNotificationService in the registry.
    /// </summary>
    public void SetErrorNotification(ErrorNotificationService errorNotification) => _errorNotification = errorNotification;

    public IReadOnlyList<string> MountPathHistory => _config.MountPathHistory;

    public IReadOnlyList<string> DataStoreHistory => _config.DataStoreHistory;

    public IReadOnlyList<string> CreateSetHistory => _config.CreateSetHistory;

    public IReadOnlyList<string> ExportPathHistory => _config.ExportPathHistory;

    public IReadOnlyList<string> OgmrYamlHistory => _config.OgmrYamlHistory;

    public IReadOnlyList<string> OgmrOutputHistory => _config.OgmrOutputHistory;

    public IReadOnlyList<string> DatPathHistory => _config.DatPathHistory;

    public IReadOnlyList<string> ImageBrowseHistory => _config.ImageBrowseHistory;

    public WindowStateConfig? WindowState => _config.WindowState;

    public void AddMountPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.MountPathHistory, path, maxEntries: 10);
        Save();
    }

    public void AddDataStorePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.DataStoreHistory, path, maxEntries: 10);
        Save();
    }

    public void AddCreateSetPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.CreateSetHistory, path, maxEntries: 10);
        Save();
    }

    public void AddExportPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.ExportPathHistory, path, maxEntries: 10);
        Save();
    }

    public void AddOgmrYamlPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.OgmrYamlHistory, path, maxEntries: 10);
        Save();
    }

    public void AddOgmrOutputPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.OgmrOutputHistory, path, maxEntries: 10);
        Save();
    }

    public void AddDatPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.DatPathHistory, path, maxEntries: 10);
        Save();
    }

    public void AddImageBrowsePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AddToMruList(_config.ImageBrowseHistory, path, maxEntries: 10);
        Save();
    }

    public string? GetExportFormat(string system, string sourceFormat)
    {
        string key = BuildKey(system, sourceFormat);
        return _config.ExportFormatDefaults.GetValueOrDefault(key);
    }

    public void SetExportFormat(string system, string sourceFormat, string targetFormat)
    {
        string key = BuildKey(system, sourceFormat);
        _config.ExportFormatDefaults[key] = targetFormat;
        Save();
    }

    public void SetWindowState(int width, int height, int x, int y)
    {
        _config.WindowState = new WindowStateConfig { Width = width, Height = height, X = x, Y = y };
        Save();
    }

    public FormatOptionsEntry? GetFormatOptions(string system, string sourceFormat, string targetFormat)
    {
        string key = BuildFormatOptionsKey(system, sourceFormat, targetFormat);
        return _config.FormatOptions.GetValueOrDefault(key);
    }

    public void SetFormatOptions(string system, string sourceFormat, string targetFormat, FormatOptionsEntry options)
    {
        string key = BuildFormatOptionsKey(system, sourceFormat, targetFormat);
        _config.FormatOptions[key] = options;
        Save();
    }

    public IReadOnlyDictionary<string, double>? ColumnWidths =>
        _config.ColumnWidths.Count > 0 ? _config.ColumnWidths : null;

    public void SetColumnWidths(Dictionary<string, double> widths)
    {
        _config.ColumnWidths = widths;
        Save();
    }

    public string MountUid => _config.MountUid;

    public string MountGid => _config.MountGid;

    public bool MountAllowOther => _config.MountAllowOther;

    public void SetMountLinuxOptions(string uid, string gid, bool allowOther)
    {
        _config.MountUid = uid ?? "";
        _config.MountGid = gid ?? "";
        _config.MountAllowOther = allowOther;
        Save();
    }

    public WindowDecorationMode GetWindowDecorationMode()
    {
        string? value = _config.WindowDecorationMode;
        if (!string.IsNullOrWhiteSpace(value)
            && !char.IsDigit(value[0])
            && value[0] != '-'
            && Enum.TryParse<WindowDecorationMode>(value, out WindowDecorationMode mode)
            && Enum.IsDefined(mode))
            return mode;
        return WindowDecorationMode.ClientSideDecorations;
    }

    public void SetWindowDecorationMode(WindowDecorationMode mode)
    {
        _config.WindowDecorationMode = mode.ToString();
        Save();
    }

    public KeysAndFixPathsConfig KeysAndFixPaths => _config.KeysAndFixPaths ?? KeysAndFixPathsConfig.CreateDefault();

    public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths)
    {
        _config.KeysAndFixPaths = paths ?? KeysAndFixPathsConfig.CreateDefault();
        Save();
    }

    public void ResetKeysAndFixPathsToDefaults()
    {
        _config.KeysAndFixPaths = KeysAndFixPathsConfig.CreateDefault();
        Save();
    }

    // --- Internal helpers ---

    internal static string BuildKey(string system, string sourceFormat)
        => $"{system}|{sourceFormat}";

    internal static string BuildFormatOptionsKey(string system, string sourceFormat, string targetFormat)
        => $"{system}|{sourceFormat}|{targetFormat}";

    internal static List<string> AddToMruList(List<string> list, string entry, int maxEntries)
    {
        // Remove existing occurrence (case-insensitive for path comparison)
        list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
        // Insert at front
        list.Insert(0, entry);
        // Trim to capacity
        if (list.Count > maxEntries)
            list.RemoveRange(maxEntries, list.Count - maxEntries);
        return list;
    }

    private AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return AppConfig.CreateDefault();

        try
        {
            string json = File.ReadAllText(_configPath);
            AppConfig config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)
                ?? AppConfig.CreateDefault();

            // Fall back to defaults if KeysAndFixPaths is null or contains invalid entries
            if (config.KeysAndFixPaths == null || !IsValidKeysAndFixPaths(config.KeysAndFixPaths))
                config.KeysAndFixPaths = KeysAndFixPathsConfig.CreateDefault();

            return config;
        }
        catch (Exception ex)
        {
            _errorNotification?.PublishOperationError($"Failed to load config, using defaults: {ex.Message}");
            return AppConfig.CreateDefault();
        }
    }

    /// <summary>
    /// Validates that a KeysAndFixPathsConfig has no null entries.
    /// </summary>
    private static bool IsValidKeysAndFixPaths(KeysAndFixPathsConfig paths)
    {
        return paths.WiiUKeysPath != null
            && paths.Ps3KeysPath != null
            && paths.GameCubeFixInfoPath != null
            && paths.GameCubeFixFilesPath != null
            && paths.WiiFixInfoPath != null
            && paths.WiiFixFilesPath != null
            && paths.Ps3FixInfoPath != null
            && paths.Ps3FixFilesPath != null
            && paths.DreamcastFixInfoPath != null;
    }

    private void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, ConfigJsonContext.Default.AppConfig);
            string tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _errorNotification?.PublishOperationError($"Failed to save config: {ex.Message}");
        }
    }

    /// <summary>
    /// Migrates legacy export-format-preferences.json into the unified config.
    /// Merges entries without overwriting existing keys, saves, then deletes the legacy file.
    /// On any failure, logs the error and leaves the legacy file in place.
    /// </summary>
    private void MigrateLegacyPreferences()
    {
        if (!File.Exists(_legacyPath))
            return;

        try
        {
            string json = File.ReadAllText(_legacyPath);
            Dictionary<string, string>? legacy = JsonSerializer.Deserialize(json, FormatPreferencesJsonContext.Default.DictionaryStringString);
            if (legacy != null)
            {
                foreach (KeyValuePair<string, string> kvp in legacy)
                {
                    if (!_config.ExportFormatDefaults.ContainsKey(kvp.Key))
                        _config.ExportFormatDefaults[kvp.Key] = kvp.Value;
                }
                Save();
            }
            File.Delete(_legacyPath);
        }
        catch (Exception ex)
        {
            _errorNotification?.PublishOperationError($"Failed to migrate legacy preferences: {ex.Message}");
        }
    }
}
using NkdsUi.Models;
using NkdsUi.Services;

namespace NKitDataStore.Tests;

/// <summary>
/// A no-op IConfigService implementation for tests that don't need persistence behavior.
/// All get operations return null/empty defaults; all set operations are no-ops.
/// </summary>
internal sealed class NullConfigService : IConfigService
{
    public IReadOnlyList<string> MountPathHistory => [];
    public IReadOnlyList<string> DataStoreHistory => [];
    public IReadOnlyList<string> CreateSetHistory => [];
    public IReadOnlyList<string> ExportPathHistory => [];
    public IReadOnlyList<string> OgmrYamlHistory => [];
    public IReadOnlyList<string> OgmrOutputHistory => [];
    public IReadOnlyList<string> DatPathHistory => [];
    public IReadOnlyList<string> ImageBrowseHistory => [];
    public WindowStateConfig WindowState => null;
    public IReadOnlyDictionary<string, double> ColumnWidths => null;

    public void AddMountPath(string path) { }
    public void AddDataStorePath(string path) { }
    public void AddCreateSetPath(string path) { }
    public void AddExportPath(string path) { }
    public void AddOgmrYamlPath(string path) { }
    public void AddOgmrOutputPath(string path) { }
    public void AddDatPath(string path) { }
    public void AddImageBrowsePath(string path) { }
    public string GetExportFormat(string system, string sourceFormat) => null;
    public void SetExportFormat(string system, string sourceFormat, string targetFormat) { }
    public void SetWindowState(int width, int height, int x, int y) { }
    public FormatOptionsEntry GetFormatOptions(string system, string sourceFormat, string targetFormat) => null;
    public void SetFormatOptions(string system, string sourceFormat, string targetFormat, FormatOptionsEntry options) { }
    public void SetColumnWidths(Dictionary<string, double> widths) { }
    public string MountUid => "";
    public string MountGid => "";
    public bool MountAllowOther => false;
    public void SetMountLinuxOptions(string uid, string gid, bool allowOther) { }
    public WindowDecorationMode GetWindowDecorationMode() => WindowDecorationMode.ClientSideDecorations;
    public void SetWindowDecorationMode(WindowDecorationMode mode) { }
    public KeysAndFixPathsConfig KeysAndFixPaths => KeysAndFixPathsConfig.CreateDefault();
    public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths) { }
    public void ResetKeysAndFixPathsToDefaults() { }
}
using NkdsUi.Services;
using System.Text.Json;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for ConfigService load/save scenarios.
/// Validates: Requirements 7.1, 7.2, 7.3, 7.4, 6.1, 6.2
/// </summary>
public sealed class ConfigServiceLoadSaveTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _legacyPath;

    public ConfigServiceLoadSaveTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ConfigServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "nkds-ui.json");
        _legacyPath = Path.Combine(_tempDir, "export-format-preferences.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// Requirement 7.3: If the ConfigFile does not exist on disk, the ConfigService
    /// SHALL initialize with default values (empty lists and null window state).
    /// </summary>
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        // Arrange: no config file exists at _configPath

        // Act
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert
        Assert.Empty(service.MountPathHistory);
        Assert.Empty(service.DataStoreHistory);
        Assert.Null(service.WindowState);
        Assert.Null(service.GetExportFormat("Wii", "NKit"));
    }

    /// <summary>
    /// Requirements 7.1, 7.2: When the ConfigFile is successfully loaded, the ConfigService
    /// SHALL make all persisted preference values available.
    /// </summary>
    [Fact]
    public void Load_ValidFile_RestoresAllValues()
    {
        // Arrange: write a valid config file
        AppConfig config = new AppConfig
        {
            MountPathHistory = new List<string> { @"D:\Mount1", @"E:\Mount2" },
            DataStoreHistory = new List<string> { @"D:\Store1.nkds" },
            ExportFormatDefaults = new Dictionary<string, string>
            {
                ["Wii|NKit"] = "ISO",
                ["GameCube|ISO"] = "NKit"
            },
            WindowState = new WindowStateConfig { Width = 1400, Height = 900, X = 100, Y = 50 }
        };
        string json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, json);

        // Act
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert
        Assert.Equal(2, service.MountPathHistory.Count);
        Assert.Equal(@"D:\Mount1", service.MountPathHistory[0]);
        Assert.Equal(@"E:\Mount2", service.MountPathHistory[1]);

        Assert.Single(service.DataStoreHistory);
        Assert.Equal(@"D:\Store1.nkds", service.DataStoreHistory[0]);

        Assert.Equal("ISO", service.GetExportFormat("Wii", "NKit"));
        Assert.Equal("NKit", service.GetExportFormat("GameCube", "ISO"));

        Assert.NotNull(service.WindowState);
        Assert.Equal(1400, service.WindowState.Width);
        Assert.Equal(900, service.WindowState.Height);
        Assert.Equal(100, service.WindowState.X);
        Assert.Equal(50, service.WindowState.Y);
    }

    /// <summary>
    /// Requirements 7.3, 7.4: If the ConfigFile contains invalid JSON or cannot be deserialized,
    /// the ConfigService SHALL log a warning and initialize with default values.
    /// </summary>
    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        // Arrange: write corrupt/invalid JSON
        File.WriteAllText(_configPath, "this is not valid json {{{");

        // Act
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert: defaults
        Assert.Empty(service.MountPathHistory);
        Assert.Empty(service.DataStoreHistory);
        Assert.Null(service.WindowState);
        Assert.Null(service.GetExportFormat("Wii", "NKit"));
    }

    /// <summary>
    /// Requirement 6.2: Atomic write uses a .tmp file and renames it.
    /// After a successful save, no .tmp file should remain.
    /// </summary>
    [Fact]
    public void Save_AtomicWrite_NoTmpFileRemains()
    {
        // Arrange
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Act: trigger a save by mutating state
        service.AddMountPath(@"C:\TestPath");

        // Assert: config file exists, no .tmp file remains
        Assert.True(File.Exists(_configPath));
        Assert.False(File.Exists(_configPath + ".tmp"));
    }

    /// <summary>
    /// Requirements 6.1, 7.1, 7.2: Save persists data that can be loaded by a new instance.
    /// End-to-end round-trip: create service, mutate, create new service from same path, verify.
    /// </summary>
    [Fact]
    public void Save_PersistsData_LoadableByNewInstance()
    {
        // Arrange: create first instance and populate it
        ConfigService service1 = new ConfigService(_configPath, _legacyPath);
        service1.AddMountPath(@"D:\Games\Wii\Mount");
        service1.AddDataStorePath(@"D:\Games\Wii\DataStore");
        service1.SetExportFormat("Wii", "NKit", "ISO");
        service1.SetWindowState(1200, 800, 50, 25);

        // Act: create a second instance from the same file
        ConfigService service2 = new ConfigService(_configPath, _legacyPath);

        // Assert: all values persisted and restored
        Assert.Single(service2.MountPathHistory);
        Assert.Equal(@"D:\Games\Wii\Mount", service2.MountPathHistory[0]);

        Assert.Single(service2.DataStoreHistory);
        Assert.Equal(@"D:\Games\Wii\DataStore", service2.DataStoreHistory[0]);

        Assert.Equal("ISO", service2.GetExportFormat("Wii", "NKit"));

        Assert.NotNull(service2.WindowState);
        Assert.Equal(1200, service2.WindowState.Width);
        Assert.Equal(800, service2.WindowState.Height);
        Assert.Equal(50, service2.WindowState.X);
        Assert.Equal(25, service2.WindowState.Y);
    }
}
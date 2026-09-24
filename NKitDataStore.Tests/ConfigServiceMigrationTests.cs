using NkdsUi.Services;
using System.Text.Json;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for ConfigService legacy migration behavior.
/// Tests that export-format-preferences.json is correctly migrated into the unified config.
///
/// **Validates: Requirements 9.4**
/// </summary>
public class ConfigServiceMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _legacyPath;

    public ConfigServiceMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nkit-migration-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "nkds-ui.json");
        _legacyPath = Path.Combine(_tempDir, "export-format-preferences.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Migration_ValidLegacyFile_MergesIntoExportFormatDefaults()
    {
        // Arrange: write a valid legacy preferences file
        Dictionary<string, string> legacyPrefs = new Dictionary<string, string>
        {
            ["Wii|NKit"] = "ISO",
            ["GameCube|ISO"] = "NKit"
        };
        File.WriteAllText(_legacyPath, JsonSerializer.Serialize(legacyPrefs));

        // Act: construct ConfigService (triggers migration in constructor)
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert: legacy entries are available via GetExportFormat
        Assert.Equal("ISO", service.GetExportFormat("Wii", "NKit"));
        Assert.Equal("NKit", service.GetExportFormat("GameCube", "ISO"));
    }

    [Fact]
    public void Migration_ValidLegacyFile_DeletesLegacyFileAfterMigration()
    {
        // Arrange: write a valid legacy preferences file
        Dictionary<string, string> legacyPrefs = new Dictionary<string, string>
        {
            ["Wii|NKit"] = "ISO"
        };
        File.WriteAllText(_legacyPath, JsonSerializer.Serialize(legacyPrefs));

        // Act
        _ = new ConfigService(_configPath, _legacyPath);

        // Assert: legacy file is deleted
        Assert.False(File.Exists(_legacyPath));
    }

    [Fact]
    public void Migration_ExistingKeysInConfig_NotOverwrittenByLegacyData()
    {
        // Arrange: write a config file with an existing export format preference
        AppConfig existingConfig = new AppConfig
        {
            ExportFormatDefaults = new Dictionary<string, string>
            {
                ["Wii|NKit"] = "RVZ" // existing value
            }
        };
        string configJson = JsonSerializer.Serialize(existingConfig, ConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, configJson);

        // Write a legacy file that has the same key with a different value
        Dictionary<string, string> legacyPrefs = new Dictionary<string, string>
        {
            ["Wii|NKit"] = "ISO", // should NOT overwrite existing "RVZ"
            ["GameCube|ISO"] = "NKit" // new key, should be added
        };
        File.WriteAllText(_legacyPath, JsonSerializer.Serialize(legacyPrefs));

        // Act
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert: existing key retains its original value
        Assert.Equal("RVZ", service.GetExportFormat("Wii", "NKit"));
        // Assert: new key from legacy is added
        Assert.Equal("NKit", service.GetExportFormat("GameCube", "ISO"));
    }

    [Fact]
    public void Migration_CorruptLegacyFile_HandledGracefully_NoException()
    {
        // Arrange: write invalid JSON to the legacy file
        File.WriteAllText(_legacyPath, "this is not valid json {{{");

        // Act: should not throw
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert: service is functional with defaults, legacy file left in place
        Assert.Null(service.GetExportFormat("Wii", "NKit"));
        Assert.True(File.Exists(_legacyPath));
    }

    [Fact]
    public void Migration_NoLegacyFile_IsNoOp()
    {
        // Arrange: no legacy file exists (don't create one)
        // Optionally create a config with some data to verify it's untouched
        AppConfig existingConfig = new AppConfig
        {
            ExportFormatDefaults = new Dictionary<string, string>
            {
                ["Wii|NKit"] = "RVZ"
            }
        };
        string configJson = JsonSerializer.Serialize(existingConfig, ConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, configJson);

        // Act
        ConfigService service = new ConfigService(_configPath, _legacyPath);

        // Assert: existing config is unchanged, no legacy file created
        Assert.Equal("RVZ", service.GetExportFormat("Wii", "NKit"));
        Assert.False(File.Exists(_legacyPath));
    }
}
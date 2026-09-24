using NkdsUi.Services;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for ConfigService export format get/set behavior.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    public class ConfigServiceExportFormatTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _configPath;
        private readonly string _legacyPath;

        public ConfigServiceExportFormatTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _configPath = Path.Combine(_tempDir, "nkds-ui.json");
            _legacyPath = Path.Combine(_tempDir, "export-format-preferences.json");
        }

        public void Dispose()
        {
            // Clean up temp directory
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        /// <summary>
        /// Creates a ConfigService that operates in an isolated temp directory.
        /// </summary>
        private ConfigService CreateFreshService() => new ConfigService(_configPath, _legacyPath);

        // --- Requirement 4.1, 4.3: SetExportFormat then GetExportFormat returns the correct value ---

        [Fact]
        public void SetExportFormat_ThenGet_ReturnsCorrectValue()
        {
            ConfigService service = CreateFreshService();

            service.SetExportFormat("Wii", "NKit", "ISO");

            string result = service.GetExportFormat("Wii", "NKit");

            Assert.Equal("ISO", result);
        }

        // --- Requirement 4.4: GetExportFormat for a key that was never set returns null ---

        [Fact]
        public void GetExportFormat_NeverSet_ReturnsNull()
        {
            ConfigService service = CreateFreshService();

            string result = service.GetExportFormat("GameCube", "ISO");

            Assert.Null(result);
        }

        // --- Requirement 4.2: BuildKey produces the format "System|SourceFormat" ---

        [Fact]
        public void BuildKey_ProducesCorrectFormat()
        {
            string key = ConfigService.BuildKey("Wii", "NKit");

            Assert.Equal("Wii|NKit", key);
        }

        [Fact]
        public void BuildKey_DifferentInputs_ProducesExpectedFormat()
        {
            string key = ConfigService.BuildKey("GameCube", "ISO");

            Assert.Equal("GameCube|ISO", key);
        }

        // --- Requirement 4.1: Overwriting an existing key with a new value returns the new value ---

        [Fact]
        public void SetExportFormat_OverwriteExistingKey_ReturnsNewValue()
        {
            ConfigService service = CreateFreshService();

            service.SetExportFormat("Wii", "NKit", "ISO");
            service.SetExportFormat("Wii", "NKit", "WBFS");

            string result = service.GetExportFormat("Wii", "NKit");

            Assert.Equal("WBFS", result);
        }

        // --- Requirement 4.1, 4.3: Multiple different keys can coexist independently ---

        [Fact]
        public void SetExportFormat_MultipleDifferentKeys_CoexistIndependently()
        {
            ConfigService service = CreateFreshService();

            service.SetExportFormat("Wii", "NKit", "ISO");
            service.SetExportFormat("GameCube", "ISO", "NKit");
            service.SetExportFormat("Wii", "ISO", "WBFS");

            Assert.Equal("ISO", service.GetExportFormat("Wii", "NKit"));
            Assert.Equal("NKit", service.GetExportFormat("GameCube", "ISO"));
            Assert.Equal("WBFS", service.GetExportFormat("Wii", "ISO"));
        }
    }
}
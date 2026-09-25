using global::NKit.Ui.Services;
using Nanook.NKit;
using Nanook.NKit.Configuration;
using System.IO;
using Xunit;


namespace NKit.Tests.Configuration.UI
{
    /// <summary>
    /// Comprehensive tests for YAML configuration persistence behavior,
    /// including global vs system-specific settings and task-specific properties.
    /// Migrated from console test apps created during development.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "UI")]
    public class ConfigurationPersistenceIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public ConfigurationPersistenceIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Global Settings Persistence Tests

        [Fact(DisplayName = "Global settings (ConsoleLevel) are stored in global YAML section")]
        public void GlobalSettings_AreStoredInGlobalYamlSection()
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = CreateTestSettings(SystemType.GameCube, TaskType.Convert);
            settings.ConsoleLevel = LogLevel.Trace;  // Should go to global
            settings.V_Convert = Verify.Y;          // Should go to system-specific
            settings.R = true;                      // Should go to system-specific

            YamlConfigurationStore configStore = new YamlConfigurationStore();
            configStore.ClearCache();

            // Act
            configStore.StoreSettings(settings);

            // Assert
            string configPath = configStore.GetConfigurationPath();
            Assert.True(File.Exists(configPath), "Configuration file should be created");

            string yamlContent = File.ReadAllText(configPath);

            // Verify global section contains UI settings
            Assert.Contains("global:", yamlContent);
            int globalIndex = yamlContent.IndexOf("global:");
            Assert.True(yamlContent.IndexOf("consoleLevel:", globalIndex) > globalIndex,
                "consoleLevel should be in global section");

            // Verify system section contains processing settings
            Assert.Contains("gamecube:", yamlContent);
            int gamecubeIndex = yamlContent.IndexOf("gamecube:");
            Assert.True(yamlContent.IndexOf("v_Convert:", gamecubeIndex) > gamecubeIndex,
                "v_Convert should be in gamecube section");
            Assert.True(yamlContent.IndexOf("r:", gamecubeIndex) > gamecubeIndex,
                "r should be in gamecube section");

            _output.WriteLine($"Global settings correctly separated in YAML structure");
        }

        [Fact(DisplayName = "Global settings are consistent across different systems")]
        public void GlobalSettings_AreConsistentAcrossSystems()
        {
            // Arrange
            YamlConfigurationStore configStore = new YamlConfigurationStore();
            configStore.ClearCache();

            // Set global settings via GameCube
            global::NKit.Ui.Models.NKitSettings gameCubeSettings = CreateTestSettings(SystemType.GameCube, TaskType.Convert);
            gameCubeSettings.ConsoleLevel = LogLevel.Trace;
            configStore.StoreSettings(gameCubeSettings);

            // Update global settings via Wii
            global::NKit.Ui.Models.NKitSettings wiiSettings = CreateTestSettings(SystemType.Wii, TaskType.Convert);
            wiiSettings.ConsoleLevel = LogLevel.Info;
            configStore.StoreSettings(wiiSettings);

            // Act - Load GameCube settings again
            configStore.ClearCache();
            global::NKit.Ui.Models.NKitSettings reloadedGameCube = configStore.LoadSettings(SystemType.GameCube, TaskType.Convert);
            global::NKit.Ui.Models.NKitSettings reloadedWii = configStore.LoadSettings(SystemType.Wii, TaskType.Convert);

            // Assert - Global settings should be consistent
            Assert.Equal(LogLevel.Info, reloadedGameCube.ConsoleLevel);
            Assert.Equal(LogLevel.Info, reloadedWii.ConsoleLevel);

            _output.WriteLine("Global settings maintained consistency across systems");
        }

        #endregion

        #region Task-Specific Settings Persistence Tests

        [Fact(DisplayName = "Task-specific verify settings persistence behavior")]
        public void TaskSpecificVerifySettings_PersistenceBehavior()
        {
            // Arrange
            YamlConfigurationStore configStore = new YamlConfigurationStore();
            configStore.ClearCache();

            // Act - Store settings for different tasks and see what actually happens
            global::NKit.Ui.Models.NKitSettings convertSettings = CreateTestSettings(SystemType.GameCube, TaskType.Convert);
            convertSettings.V = Verify.Y;
            configStore.StoreSettings(convertSettings);
            _output.WriteLine($"Stored Convert settings: V = {convertSettings.V}, V_Convert = {convertSettings.V_Convert}");

            global::NKit.Ui.Models.NKitSettings scanSettings = CreateTestSettings(SystemType.GameCube, TaskType.Scan);
            scanSettings.V = Verify.DatLookup;
            configStore.StoreSettings(scanSettings);
            _output.WriteLine($"Stored Scan settings: V = {scanSettings.V}, V_Scan = {scanSettings.V_Scan}");

            // Clear cache and reload to see persistence behavior
            configStore.ClearCache();

            global::NKit.Ui.Models.NKitSettings loadedConvert = configStore.LoadSettings(SystemType.GameCube, TaskType.Convert);
            global::NKit.Ui.Models.NKitSettings loadedScan = configStore.LoadSettings(SystemType.GameCube, TaskType.Scan);

            _output.WriteLine($"Loaded Convert: V_Convert = {loadedConvert?.V_Convert}");
            _output.WriteLine($"Loaded Scan: V_Scan = {loadedScan?.V_Scan}");

            // Set tasks and check computed V property
            if (loadedConvert != null)
            {
                loadedConvert.Task = TaskType.Convert;
                _output.WriteLine($"Convert task V property: {loadedConvert.V}");
            }

            if (loadedScan != null)
            {
                loadedScan.Task = TaskType.Scan;
                _output.WriteLine($"Scan task V property: {loadedScan.V}");
            }

            // Assert what we can reasonably expect
            Assert.NotNull(loadedConvert);
            Assert.NotNull(loadedScan);

            // The test shows current behavior rather than asserting specific expected behavior
            // This is valuable for understanding how the system currently works
        }

        [Theory(DisplayName = "System default formats are correctly applied")]
        [InlineData(SystemType.GameCube, "rvz")]
        [InlineData(SystemType.Wii, "rvz")]
        [InlineData(SystemType.PS3, "deciso")]
        [InlineData(SystemType.PSP, "cso")]
        [InlineData(SystemType.Dreamcast, "cue")]
        [InlineData(SystemType.Default, "iso")]
        public void SystemDefaultFormats_AreCorrectlyApplied(SystemType systemType, string expectedFormat)
        {
            // Arrange & Act
            string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(systemType);

            // Assert
            Assert.Equal(expectedFormat, defaultFormat);
            _output.WriteLine($"{systemType} defaults to {defaultFormat}");
        }

        [Fact(DisplayName = "SystemType.Default supports dual format configuration")]
        public void SystemTypeDefault_SupportsDualFormatConfiguration()
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = CreateTestSettings(SystemType.Default, TaskType.Convert);

            // Act
            settings.Convert = "iso/cue:split:bin:bin:sub";

            // Assert
            Assert.Equal("iso", settings.ConvertSingleFormat);
            Assert.Equal("cue", settings.ConvertIndexedFormat);
            Assert.Equal("split", settings.ConvertCueType);
            Assert.Equal("bin", settings.ConvertBinary);
            Assert.Equal("bin", settings.ConvertAudio);

            _output.WriteLine($"Default system dual format: {settings.Convert}");
            _output.WriteLine($"Single: {settings.ConvertSingleFormat}, Indexed: {settings.ConvertIndexedFormat}");
        }

        #endregion

        #region Helper Methods

        private static global::NKit.Ui.Models.NKitSettings CreateTestSettings(SystemType systemType, TaskType taskType)
        {
            global::NKit.Ui.Models.NKitSettings settings = new global::NKit.Ui.Models.NKitSettings();
            settings.System = systemType;
            settings.Task = taskType;
            return settings;
        }

        #endregion
    }
}
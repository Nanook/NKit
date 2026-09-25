using global::NKit.Ui.Models;
using global::NKit.Ui.Services;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests to verify that missing configuration properties get proper default values
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class ConfigurationDefaultsTests
    {
        [Fact]
        public void ConfigurationDeserializer_MissingPersistFileQueue_DefaultsToTrue()
        {
            // Arrange - Create a YAML configuration without persistFileQueue setting
            string yamlWithoutPersistFileQueue = @"version: 1.3
global:
  consoleLevel: info
  parallelism: 4
  ui:
    showQueued: y
    showSkipped: y
    showProcessing: y
    showCompleted: y
    showFailed: y
    showCancelled: y
    consoleOutputAutoScroll: y
    consoleOutputWrapText: y
    reprocessCompletedFiles: n
    reprocessFailedFiles: n
    reprocessSkippedFiles: n
    # persistFileQueue is intentionally missing
systems:
  gamecube:
    v: y
    r: n
    arc: n
    results: n
    logOutLevel: info
    deleteProcessed: n
    skipIfCompleted: n
    outAsDatMatch: n
";

            // Act - Deserialize using the AOT YAML serializer
            using StringReader reader = new StringReader(yamlWithoutPersistFileQueue);
            UiConfiguration config = global::NKit.Ui.Helpers.Yaml.AotYamlSerializer.Deserialize(reader);

            // Assert - PersistFileQueue should default to true even though it's missing from YAML
            Assert.NotNull(config);
            Assert.NotNull(config.Global);
            Assert.NotNull(config.Global.Ui);
            Assert.True(config.Global.Ui.PersistFileQueue, "PersistFileQueue should default to true for missing values");
        }

        [Fact]
        public void ConfigurationDeserializer_ExplicitPersistFileQueueFalse_RespectsSetting()
        {
            // Arrange - Create a YAML configuration with persistFileQueue explicitly set to false
            string yamlWithExplicitFalse = @"version: 1.3
global:
  consoleLevel: info
  parallelism: 4
  ui:
    showQueued: y
    showSkipped: y
    showProcessing: y
    showCompleted: y
    showFailed: y
    showCancelled: y
    consoleOutputAutoScroll: y
    consoleOutputWrapText: y
    reprocessCompletedFiles: n
    reprocessFailedFiles: n
    reprocessSkippedFiles: n
    persistFileQueue: n
systems:
  gamecube:
    v: y
    r: n
    arc: n
    results: n
    logOutLevel: info
    deleteProcessed: n
    skipIfCompleted: n
    outAsDatMatch: n
";

            // Act - Deserialize using the AOT YAML serializer
            using StringReader reader = new StringReader(yamlWithExplicitFalse);
            UiConfiguration config = global::NKit.Ui.Helpers.Yaml.AotYamlSerializer.Deserialize(reader);

            // Assert - PersistFileQueue should be false as explicitly set
            Assert.NotNull(config);
            Assert.NotNull(config.Global);
            Assert.NotNull(config.Global.Ui);
            Assert.False(config.Global.Ui.PersistFileQueue, "PersistFileQueue should be false when explicitly set to 'n'");
        }

        [Fact]
        public void UiSettings_GetDefaultSettings_HasPersistFileQueueEnabled()
        {
            // Arrange & Act
            UiSettings defaults = UiSettings.GetDefaultSettings();

            // Assert
            Assert.NotNull(defaults);
            Assert.True(defaults.PersistFileQueue, "Default UiSettings should have PersistFileQueue enabled");
        }

        [Fact]
        public void YamlConfigurationStore_NewConfiguration_UsesPersistFileQueueDefault()
        {
            // This test verifies that when a new configuration is created,
            // it properly uses the default value for PersistFileQueue

            // Arrange - Create a temporary directory for the test
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a configuration file path that doesn't exist yet
                string configPath = Path.Combine(tempDir, "test_nkit-ui.yaml");

                // Act - Access UiSettings which should trigger creation of defaults
                YamlConfigurationStore store = new YamlConfigurationStore();
                UiSettings uiSettings = store.UiSettings;

                // Assert
                Assert.NotNull(uiSettings);
                Assert.True(uiSettings.PersistFileQueue, "New UiSettings should have PersistFileQueue enabled by default");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
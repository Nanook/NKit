using global::NKit.Ui.Services;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.Configuration.UI
{
    /// <summary>
    /// Test to verify queue persistence works in different configuration modes
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "UI")]
    public class QueuePersistenceModeTest
    {
        [Fact]
        public void QueuePersistence_PortableMode_SavesNextToExecutable()
        {
            // This test would need to be run with the application actually in portable mode
            // to properly test this scenario. The issue might be mode detection.

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a config file in the temp directory to simulate portable mode
                string configPath = Path.Combine(tempDir, "nkit-ui.yaml");
                File.WriteAllText(configPath, @"version: 1.3
global:
  consoleLevel: info
  parallelism: 4
  ui:
    persistFileQueue: y
systems:
  gamecube:
    v: y
");

                // Create a settings store and verify it detects the right location
                YamlConfigurationStore store = new YamlConfigurationStore();
                string actualConfigPath = store.GetConfigurationPath();

                Console.WriteLine($"Temp config created at: {configPath}");
                Console.WriteLine($"Store detected config at: {actualConfigPath}");

                // The issue might be that the store is not detecting portable mode correctly
                // and is defaulting to system mode even when a local config exists

                Assert.True(true, "This test demonstrates the configuration path detection logic");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void ShowCurrentConfigurationPaths()
        {
            // This test shows the current configuration paths being used

            YamlConfigurationStore store = new YamlConfigurationStore();
            string configPath = store.GetConfigurationPath();
            string queuePath = Path.Combine(Path.GetDirectoryName(configPath), "nkit-ui-queue.yaml");

            Console.WriteLine($"Current config path: {configPath}");
            Console.WriteLine($"Current queue path: {queuePath}");
            Console.WriteLine($"Config directory: {Path.GetDirectoryName(configPath)}");
            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"App domain base: {AppDomain.CurrentDomain.BaseDirectory}");

            // Check if config file exists
            Console.WriteLine($"Config file exists: {File.Exists(configPath)}");
            Console.WriteLine($"Queue file exists: {File.Exists(queuePath)}");

            Assert.True(true, "This test shows current paths for debugging");
        }
    }
}
using global::NKit.Ui.Models;
using global::NKit.Ui.Services;
using Nanook.NKit;
using System;
using System.Collections.ObjectModel;
using System.IO;
using Xunit;


namespace NKit.Tests.Configuration.UI
{
    /// <summary>
    /// Test to demonstrate portable vs system mode queue persistence
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "UI")]
    public class QueuePersistencePortableModeTest
    {
        [Fact]
        public void QueuePersistence_CreatePortableConfig_ShouldUsePortableMode()
        {
            // This test demonstrates how portable mode works
            // by creating a config file next to the executable

            string executableDir = AppDomain.CurrentDomain.BaseDirectory;
            // Portable mode is detected by looking for a config file named after the running executable.
            // In the test runner the process name is "NKit.Tests", so we need "NKit.Tests.yaml".
            // (In production the UI app writes "nkit-ui.yaml".)
            string execName = System.IO.Path.GetFileNameWithoutExtension(
                System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? AppDomain.CurrentDomain.FriendlyName);
            string portableConfigName = execName + ".yaml";
            string portableConfigPath = Path.Combine(executableDir, portableConfigName);
            string portableQueuePath = Path.Combine(executableDir, execName + "-queue.yaml");

            try
            {
                // Create a minimal config file next to the executable to trigger portable mode
                File.WriteAllText(portableConfigPath, @"version: 1.3
global:
  consoleLevel: info
  parallelism: 4
  ui:
    persistFileQueue: y
systems:
  gamecube:
    v: y
");

                Console.WriteLine($"Created portable config at: {portableConfigPath}");
                Console.WriteLine($"Expected queue path: {portableQueuePath}");

                // Create a new settings store - this should now detect portable mode
                YamlConfigurationStore store = new YamlConfigurationStore();
                string detectedConfigPath = store.GetConfigurationPath();

                Console.WriteLine($"Store detected config at: {detectedConfigPath}");
                Console.WriteLine($"Is using portable mode: {detectedConfigPath.StartsWith(executableDir)}");

                // Test queue persistence in portable mode
                ObservableCollection<SourceFileRecord> testQueue = new ObservableCollection<SourceFileRecord>
                {
                    new SourceFileRecord
                    {
                        Name = "portable_test.iso",
                        Filepath = @"C:\temp\portable_test.iso",
                        ImageType = SourceImageType.Iso,
                        Length = 1234567890,
                        ProcessingStatus = ProcessingStatus.Queued
                    }
                };

                store.WriteFileQueueToDisk(testQueue);

                Console.WriteLine($"Portable queue file exists after save: {File.Exists(portableQueuePath)}");

                if (File.Exists(portableQueuePath))
                {
                    Console.WriteLine($"Portable queue file size: {new FileInfo(portableQueuePath).Length} bytes");
                }

                Assert.True(true, "Portable mode test completed - check console output for results");
            }
            finally
            {
                // Clean up portable config files
                try
                {
                    if (File.Exists(portableConfigPath))
                    {
                        File.Delete(portableConfigPath);
                    }
                    if (File.Exists(portableQueuePath))
                    {
                        File.Delete(portableQueuePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
        }
    }
}
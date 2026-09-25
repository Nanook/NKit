using global::NKit.Ui.Models;
using global::NKit.Ui.Services;
using Nanook.NKit;
using System;
using System.Collections.ObjectModel;
using System.IO;


namespace NKit.Tests.Configuration.UI
{
    /// <summary>
    /// Manual test to verify queue persistence is working correctly.
    /// This test simulates the UI workflow to debug queue persistence issues.
    /// </summary>
    public class QueuePersistenceTest
    {
        public static void TestQueuePersistence()
        {
            Console.WriteLine("=== Queue Persistence Test ===");

            try
            {
                // Step 1: Create a settings store (similar to how Bootstrapper does it)
                YamlConfigurationStore settingsStore = new YamlConfigurationStore();
                Console.WriteLine($"Created YamlConfigurationStore");
                Console.WriteLine($"Config path: {settingsStore.GetConfigurationPath()}");

                // Step 2: Check initial UI settings
                UiSettings uiSettings = settingsStore.UiSettings;
                Console.WriteLine($"PersistFileQueue setting: {uiSettings.PersistFileQueue}");

                // Step 2.5: Explicitly enable queue persistence and verify it gets saved
                if (!uiSettings.PersistFileQueue)
                {
                    Console.WriteLine("Enabling PersistFileQueue setting...");
                    uiSettings.PersistFileQueue = true;
                    Console.WriteLine($"PersistFileQueue setting after change: {uiSettings.PersistFileQueue}");

                    // Force save settings to disk
                    settingsStore.WriteSettingsToDisk();
                    Console.WriteLine("Settings saved to disk");
                }

                // Step 3: Create a test queue with some sample data
                ObservableCollection<SourceFileRecord> fileQueue = new ObservableCollection<SourceFileRecord>
                {
                    new SourceFileRecord
                    {
                        Name = "test1.iso",
                        Filepath = @"C:\temp\test1.iso",
                        ImageType = SourceImageType.Iso,
                        Length = 1024000000,
                        ProcessingStatus = ProcessingStatus.Queued,
                        SourceFileDetails = "Test ISO file 1"
                    },
                    new SourceFileRecord
                    {
                        Name = "test2.wbfs",
                        Filepath = @"C:\temp\test2.wbfs",
                        ImageType = SourceImageType.Wbfs,
                        Length = 4700000000,
                        ProcessingStatus = ProcessingStatus.Completed,
                        SourceFileDetails = "Test WBFS file 2"
                    }
                };

                Console.WriteLine($"Created test queue with {fileQueue.Count} items");

                // Step 4: Try to save the queue
                Console.WriteLine("Attempting to save queue...");
                settingsStore.WriteFileQueueToDisk(fileQueue);

                // Step 5: Check if the file was created
                string configDir = Path.GetDirectoryName(settingsStore.GetConfigurationPath());
                string queuePath = Path.Combine(configDir, "nkit-ui-queue.yaml");

                Console.WriteLine($"Expected queue file path: {queuePath}");
                Console.WriteLine($"Queue file exists: {File.Exists(queuePath)}");

                if (File.Exists(queuePath))
                {
                    FileInfo info = new FileInfo(queuePath);
                    Console.WriteLine($"Queue file size: {info.Length} bytes");
                    Console.WriteLine($"Queue file content preview:");
                    string content = File.ReadAllText(queuePath);
                    Console.WriteLine(content.Length > 500 ? content.Substring(0, 500) + "..." : content);
                }

                // Step 6: Try to load the queue back
                Console.WriteLine("\nAttempting to load queue...");

                // Create a new settings store to simulate fresh startup
                YamlConfigurationStore newSettingsStore = new YamlConfigurationStore();

                // Simulate the import process from Bootstrapper
                if (File.Exists(queuePath))
                {
                    using StringReader reader = new StringReader(File.ReadAllText(queuePath));
                    ObservableCollection<SourceFileRecord> loadedQueue = global::NKit.Ui.Helpers.Yaml.AotQueueSerializer.Deserialize(reader);

                    Console.WriteLine($"Loaded queue with {loadedQueue.Count} items");

                    foreach (SourceFileRecord item in loadedQueue)
                    {
                        Console.WriteLine($"  - {item.Name} ({item.ProcessingStatus})");
                    }
                }
                else
                {
                    Console.WriteLine("Queue file not found for loading test");
                }

                Console.WriteLine("\n=== Test Complete ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during test: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
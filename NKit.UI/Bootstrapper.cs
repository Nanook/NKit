using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Helpers.Yaml;
using NKit.Ui.Models;
using NKit.Ui.Services;
using Splat;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace NKit.Ui
{
    public static class Bootstrapper
    {
        private static string _userQueueName = "nkit-ui-queue.yaml"; // Match YamlConfigurationStore

        public static string UserQueuePath { get; private set; }
        public static string UserSettingsPath { get; private set; }

        /// <summary>
        /// Registers services with default configuration manager
        /// </summary>
        public static void Register() => Register(configurationManager: null, forceMode: null);

        /// <summary>
        /// Registers services with explicit configuration manager and optional forced mode (for testing)
        /// </summary>
        /// <param name="configurationManager">Optional configuration manager for dependency injection</param>
        /// <param name="forceMode">Optional forced configuration mode for testing</param>
        public static void Register(IConfigurationManager configurationManager = null, bool? forceMode = null)
        {
            NKitSettings settings = new();
            ISettingsStore settingsStore;
            ObservableCollection<SourceFileRecord> fileQueue = new();

            try
            {
                // Use new YAML configuration store that creates concise configuration files
                settingsStore = new YamlConfigurationStore();

                // Get configuration paths from the store
                YamlConfigurationStore yamlStore = settingsStore as YamlConfigurationStore;
                UserSettingsPath = yamlStore?.GetConfigurationPath() ?? ConfigSettingsConstants.ConfigFileNameUI;
                UserQueuePath = Path.Combine(Path.GetDirectoryName(UserSettingsPath), _userQueueName);

                // Restore last used system and task from persisted configuration
                (SystemType lastSystem, TaskType lastTask) = yamlStore?.GetLastUsedSystemAndTask() ?? (SystemType.NotSet, TaskType.NotSet);
                if (lastSystem == SystemType.NotSet) lastSystem = SystemType.GameCube;
                if (lastTask == TaskType.NotSet) lastTask = TaskType.Convert;

                // Load settings for the last used system/task (or defaults)
                NKitSettings loadedSettings = settingsStore.GetSettings(lastSystem, lastTask);
                if (loadedSettings != null)
                    settings = loadedSettings;
                else
                {
                    settings = NKitSettings.GetDefaultSettings(lastSystem, lastTask);
                    settingsStore.StoreSettings(settings);
                }

                // Ensure settings are properly initialized with correct system/task
                if (settings.System == SystemType.NotSet)
                    settings.System = lastSystem;
                if (settings.Task == TaskType.NotSet)
                    settings.Task = lastTask;

                // Load file queue if enabled
                if (settingsStore.UiSettings?.PersistFileQueue == true && File.Exists(UserQueuePath))
                    fileQueue = ImportFileQueue();
            }
            catch (Exception ex)
            {
                // Log error but continue with minimal setup
                try
                {
                    string debugInfo = $"Bootstrapper Register failed: {ex.Message}, using minimal setup";
                    string debugPath = Path.Combine(Path.GetDirectoryName(UserSettingsPath) ?? Directory.GetCurrentDirectory(), "nkit_debug.log");
                    File.AppendAllText(debugPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {debugInfo}\n");
                }
                catch { }

                // Minimal setup with defaults
                settings = NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);
                settingsStore = new YamlConfigurationStore();
                fileQueue = new ObservableCollection<SourceFileRecord>();

                // Ensure we have a valid UserSettingsPath
                if (string.IsNullOrEmpty(UserSettingsPath))
                {
                    try
                    {
                        using ConfigurationManager fallbackConfigManager = new ConfigurationManager();
                        ConfigurationInfo fallbackInfo = fallbackConfigManager.GetConfigurationInfo();
                        UserSettingsPath = Path.Combine(fallbackInfo.ConfigDirectory, ConfigSettingsConstants.ConfigFileNameUI);
                    }
                    catch
                    {
                        UserSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigSettingsConstants.ConfigFileNameUI);
                    }
                }
            }

            Locator.CurrentMutable.RegisterConstant(settings, typeof(NKitSettings));
            Locator.CurrentMutable.RegisterConstant(settingsStore, typeof(ISettingsStore));
            Locator.CurrentMutable.RegisterConstant(fileQueue, typeof(ObservableCollection<SourceFileRecord>));

            ConsoleOutput consoleOutput = new(); // Needs ISettingsStore registered.
            Locator.CurrentMutable.RegisterConstant(consoleOutput, typeof(ConsoleOutput));
        }

        private static ObservableCollection<SourceFileRecord> ImportFileQueue()
        {
            try
            {
                if (!File.Exists(UserQueuePath))
                    return new ObservableCollection<SourceFileRecord>();

                // Use AOT-compatible emit-based deserializer for file queue
                using StringReader reader = new StringReader(File.ReadAllText(UserQueuePath));
                ObservableCollection<SourceFileRecord> fileQueue = AotQueueSerializer.Deserialize(reader);

                // Return sorted by name for consistent ordering
                return new ObservableCollection<SourceFileRecord>(fileQueue.OrderBy(x => x.Name));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to import file queue: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new ObservableCollection<SourceFileRecord>();
            }
        }
    }
}
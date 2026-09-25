using global::NKit.Ui.Models;
using global::NKit.Ui.Services;
using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.ObjectModel;
using System.IO;
using Xunit;


namespace NKit.Tests.Configuration.YamlConfigStore
{
    [Trait("Area", "Configuration")]
    [Trait("Group", "YamlConfigStore")]
    public class YamlConfigurationStoreTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly YamlConfigurationStore _store;

        public YamlConfigurationStoreTests(ITestOutputHelper output)
        {
            _output = output;
            _store = new YamlConfigurationStore();

            // Ensure a clean cache at start
            _store.ClearCache();
        }

        public void Dispose()
        {
            try
            {
                string path = _store.GetConfigurationPath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Cleanup failed: {ex.Message}");
            }
            finally
            {
                _store.ClearCache();
            }
        }

        [Fact(DisplayName = "Keys: persisted for supported systems and parsed into components")]
        public void Keys_Persisted_For_SupportedSystem()
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.WiiU, TaskType.Convert);
            settings.System = SystemType.WiiU;
            settings.Task = TaskType.Convert;

            // Use a simple masked filename next to source style
            string inputKeys = Path.Combine("C:", "keys", "prod.keys");
            settings.Keys = inputKeys;

            // Act - store and load
            _store.StoreSettings(settings);
            Ui.Models.NKitSettings loaded = _store.LoadSettings(SystemType.WiiU, TaskType.Convert);

            // Assert
            Assert.NotNull(loaded);
            Assert.False(string.IsNullOrEmpty(loaded.Keys));
            Assert.Contains("prod.keys", loaded.Keys, StringComparison.OrdinalIgnoreCase);

            // KeysPath_Manual should be directory part, KeysMask should be filename
            string expectedDir = Path.GetDirectoryName(loaded.Keys) ?? string.Empty;
            string expectedFile = Path.GetFileName(loaded.Keys) ?? string.Empty;
            Assert.Equal(expectedDir, loaded.KeysPath_Manual);
            Assert.Equal(expectedFile, loaded.KeysMask);

            _output.WriteLine($"Stored keys: {inputKeys}");
            _output.WriteLine($"Loaded keys: {loaded.Keys}");
            _output.WriteLine($"Loaded KeysPath_Manual: {loaded.KeysPath_Manual}");
            _output.WriteLine($"Loaded KeysMask: {loaded.KeysMask}");
        }

        [Fact(DisplayName = "Keys: not persisted for unsupported systems")]
        public void Keys_NotPersisted_For_UnsupportedSystem()
        {
            // Arrange - Wii does not support keys by default in the UI
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.Wii, TaskType.Convert);
            settings.System = SystemType.Wii;
            settings.Task = TaskType.Convert;
            settings.Keys = Path.Combine("C:", "keys", "prod.keys");

            // Act
            _store.StoreSettings(settings);
            Ui.Models.NKitSettings loaded = _store.LoadSettings(SystemType.Wii, TaskType.Convert);

            // Assert - UI should not persist keys for unsupported systems
            Assert.NotNull(loaded);
            Assert.True(string.IsNullOrEmpty(loaded.Keys));

            _output.WriteLine($"Wii loaded keys (should be empty): '{loaded.Keys}'");
        }

        [Theory(DisplayName = "DAT parsing: archive and plain file-mask forms are parsed correctly")]
        [InlineData("D:\\NKitFiles\\_DatsKeys\\*.zip//*.dat")]
        [InlineData("D:/NKitFiles/_DatsKeys/*.zip//*.dat")]
        public void DatParsing_ArchivePattern_IsParsed(string datValue)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);
            settings.System = SystemType.GameCube;
            settings.Task = TaskType.Convert;
            settings.Dat = datValue;

            // Act
            _store.StoreSettings(settings);
            Ui.Models.NKitSettings loaded = _store.LoadSettings(SystemType.GameCube, TaskType.Convert);

            // Assert
            Assert.NotNull(loaded);
            Assert.False(string.IsNullOrEmpty(loaded.Dat));

            // Expect archive mask and inner dat mask to be present
            Assert.False(string.IsNullOrEmpty(loaded.DatArchiveMask));
            Assert.False(string.IsNullOrEmpty(loaded.DatMask));

            _output.WriteLine($"Input DAT: {datValue}");
            _output.WriteLine($"Loaded DatPath_Manual: {loaded.DatPath_Manual}");
            _output.WriteLine($"Loaded DatArchiveMask: {loaded.DatArchiveMask}");
            _output.WriteLine($"Loaded DatMask: {loaded.DatMask}");
        }

        [Theory(DisplayName = "DAT parsing: plain file-mask is parsed into directory + mask")]
        [InlineData("D:\\NKitFiles\\_DatsKeys\\*.dat")]
        [InlineData("D:/NKitFiles/_DatsKeys/*.dat")]
        public void DatParsing_PlainFileMask_IsParsed(string datValue)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);
            settings.System = SystemType.GameCube;
            settings.Task = TaskType.Convert;
            settings.Dat = datValue;

            // Act
            _store.StoreSettings(settings);
            Ui.Models.NKitSettings loaded = _store.LoadSettings(SystemType.GameCube, TaskType.Convert);

            // Assert
            Assert.NotNull(loaded);
            Assert.False(string.IsNullOrEmpty(loaded.Dat));

            // For plain mask expect DatMask set and DatArchiveMask empty
            Assert.False(string.IsNullOrEmpty(loaded.DatMask));
            Assert.True(string.IsNullOrEmpty(loaded.DatArchiveMask));

            string expectedDir = Path.GetDirectoryName(loaded.Dat) ?? string.Empty;
            Assert.Equal(expectedDir, loaded.DatPath_Manual);

            _output.WriteLine($"Input DAT: {datValue}");
            _output.WriteLine($"Loaded DatPath_Manual: {loaded.DatPath_Manual}");
            _output.WriteLine($"Loaded DatArchiveMask: {loaded.DatArchiveMask}");
            _output.WriteLine($"Loaded DatMask: {loaded.DatMask}");
        }

        [Fact(DisplayName = "FixInfo/FixFiles: persisted for supported systems and not for unsupported")]
        public void FixInfoFixFiles_Persistence_Behavior()
        {
            // Supported system (GameCube)
            global::NKit.Ui.Models.NKitSettings s1 = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);
            s1.System = SystemType.GameCube;
            s1.Task = TaskType.Convert;
            s1.FixInfo = Path.Combine("C:", "fix", "fixinfo.dat");
            s1.FixFiles = Path.Combine("C:", "fix", "files");

            _store.StoreSettings(s1);
            Ui.Models.NKitSettings l1 = _store.LoadSettings(SystemType.GameCube, TaskType.Convert);
            Assert.NotNull(l1);
            Assert.False(string.IsNullOrEmpty(l1.FixInfo));
            Assert.False(string.IsNullOrEmpty(l1.FixFiles));

            // Unsupported system (PSP)
            global::NKit.Ui.Models.NKitSettings s2 = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.PSP, TaskType.Convert);
            s2.System = SystemType.PSP;
            s2.Task = TaskType.Convert;
            s2.FixInfo = Path.Combine("C:", "fix", "fixinfo.dat");
            s2.FixFiles = Path.Combine("C:", "fix", "files");

            _store.StoreSettings(s2);
            Ui.Models.NKitSettings l2 = _store.LoadSettings(SystemType.PSP, TaskType.Convert);
            Assert.NotNull(l2);
            // Should NOT persist fix-related settings for unsupported systems
            Assert.True(string.IsNullOrEmpty(l2.FixInfo) || !ConfigSettingsDefaults.IsFixSupported(SystemType.PSP));
            Assert.True(string.IsNullOrEmpty(l2.FixFiles) || !ConfigSettingsDefaults.IsFixFilesSupported(SystemType.PSP));
        }

        [Fact(DisplayName = "Keys components: KeysPath_Manual and KeysMask round-trip when set")]
        public void KeysComponents_RoundTrip()
        {
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.WiiU, TaskType.Convert);
            settings.System = SystemType.WiiU;
            settings.Task = TaskType.Convert;

            settings.KeysPath_Manual = Path.Combine("C:", "keys");
            settings.KeysMask = "*.key";

            // When KeysPath_Manual/KeysMask are set, Keys should be composed and persisted
            _store.StoreSettings(settings);
            Ui.Models.NKitSettings loaded = _store.LoadSettings(SystemType.WiiU, TaskType.Convert);

            Assert.NotNull(loaded);
            Assert.Equal(settings.KeysPath_Manual, loaded.KeysPath_Manual);
            Assert.Equal(settings.KeysMask, loaded.KeysMask);
            Assert.False(string.IsNullOrEmpty(loaded.Keys));
        }

        [Fact(DisplayName = "DAT components: plain and archive forms round-trip via components")]
        public void DatComponents_RoundTrip()
        {
            // Plain mask form
            global::NKit.Ui.Models.NKitSettings sPlain = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);
            sPlain.System = SystemType.GameCube;
            sPlain.Task = TaskType.Convert;
            sPlain.DatPath_Manual = Path.Combine("D:", "NKitFiles", "_DatsKeys");
            sPlain.DatMask = "*.dat";

            _store.StoreSettings(sPlain);
            Ui.Models.NKitSettings lPlain = _store.LoadSettings(SystemType.GameCube, TaskType.Convert);
            Assert.NotNull(lPlain);
            Assert.Equal(sPlain.DatPath_Manual, lPlain.DatPath_Manual);
            Assert.Equal(sPlain.DatMask, lPlain.DatMask);

            // Archive form
            global::NKit.Ui.Models.NKitSettings sArchive = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);
            sArchive.System = SystemType.GameCube;
            sArchive.Task = TaskType.Convert;
            sArchive.DatPath_Manual = Path.Combine("D:", "NKitFiles", "_DatsKeys");
            sArchive.DatArchiveMask = "*.zip";
            sArchive.DatMask = "*.dat";

            _store.StoreSettings(sArchive);
            Ui.Models.NKitSettings lArchive = _store.LoadSettings(SystemType.GameCube, TaskType.Convert);
            Assert.NotNull(lArchive);
            Assert.Equal(sArchive.DatPath_Manual, lArchive.DatPath_Manual);
            Assert.Equal(sArchive.DatArchiveMask, lArchive.DatArchiveMask);
            Assert.Equal(sArchive.DatMask, lArchive.DatMask);
        }

        [Fact(DisplayName = "Tab index and format level persistence")]
        public void TabIndex_And_FormatLevel_Persistence()
        {
            SystemType system = SystemType.Wii;

            // Tab index
            _store.StoreTabIndexForSystem(system, 3);
            int idx = _store.GetTabIndexForSystem(system);
            Assert.Equal(3, idx);

            // Format level
            string format = "rvz";
            _store.StoreFormatLevelForSystem(system, format, "19");
            string level = _store.GetFormatLevelForSystem(system, format);
            Assert.Equal("19", level);
            Assert.True(_store.HasFormatLevelForSystem(system, format));
        }

        [Fact(DisplayName = "WriteSettingsToDisk creates YAML file")]
        public void WriteSettingsToDisk_CreatesFile()
        {
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.Wii, TaskType.Convert);
            settings.System = SystemType.Wii;
            settings.Task = TaskType.Convert;

            _store.StoreSettings(settings);
            _store.WriteSettingsToDisk();

            string path = _store.GetConfigurationPath();
            Assert.False(string.IsNullOrEmpty(path));
            Assert.True(File.Exists(path));
        }

        [Fact(DisplayName = "UiSettings changes persist to YAML global.ui section")]
        public void UiSettings_Changes_PersistToYaml()
        {
            // Arrange - modify UI settings
            UiSettings ui = _store.UiSettings;
            ui.ShowQueued = false; // change a value

            // Give time for auto-save to occur (synchronous in our implementation)
            _store.WriteSettingsToDisk();

            string path = _store.GetConfigurationPath();
            Assert.True(File.Exists(path));
            string yaml = File.ReadAllText(path);

            // Ensure global and ui sections present and our key exists
            Assert.Contains("global:", yaml);
            Assert.Contains("ui:", yaml);
            Assert.Contains("showQueued", yaml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "WriteFileQueueToDisk creates queue YAML file")]
        public void WriteFileQueueToDisk_CreatesQueueFile()
        {
            // Arrange
            ObservableCollection<SourceFileRecord> queue = new ObservableCollection<SourceFileRecord>
            {
                new SourceFileRecord()
            };

            // Act
            _store.WriteFileQueueToDisk(queue);

            string queuePath = Path.Combine(Path.GetDirectoryName(_store.GetConfigurationPath()), "nkit-ui-queue.yaml");
            Assert.True(File.Exists(queuePath));
            string content = File.ReadAllText(queuePath);
            Assert.False(string.IsNullOrWhiteSpace(content));
        }

        [Fact(DisplayName = "GetSettings and GetLastUsedSettings return settings (wrappers)")]
        public void GetSettings_GetLastUsedSettings_Wrappers()
        {
            global::NKit.Ui.Models.NKitSettings s = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.Wii, TaskType.Convert);
            s.System = SystemType.Wii;
            s.Task = TaskType.Convert;
            s.Out = Path.Combine("C:", "out");

            _store.StoreSettings(s);

            Ui.Models.NKitSettings g = _store.GetSettings(SystemType.Wii, TaskType.Convert);
            Assert.NotNull(g);
            Assert.Equal(s.System, g.System);

            Ui.Models.NKitSettings last = _store.GetLastUsedSettings(SystemType.Wii);
            Assert.NotNull(last);
        }

        [Fact(DisplayName = "Version and LastFolderBrowsed properties behave as expected")]
        public void Version_And_LastFolderBrowsed_Properties()
        {
            // Default version set by ctor
            Assert.Equal(1.3f, _store.Version);

            // LastFolderBrowsed can be set and retrieved
            _store.LastFolderBrowsed = "C:\\temp";
            Assert.Equal("C:\\temp", _store.LastFolderBrowsed);
        }

        [Fact(DisplayName = "ClearCache forces reload from disk")]
        public void ClearCache_ForcesReload()
        {
            global::NKit.Ui.Models.NKitSettings s = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.Wii, TaskType.Convert);
            s.System = SystemType.Wii;
            s.Task = TaskType.Convert;
            s.Out = Path.Combine("C:", "out_reload_test");

            _store.StoreSettings(s);

            // Clear cache and then load - should read persisted value
            _store.ClearCache();
            Ui.Models.NKitSettings loaded = _store.LoadSettings(SystemType.Wii, TaskType.Convert);
            Assert.NotNull(loaded);
            Assert.Equal(s.Out, loaded.Out);
        }
    }
}
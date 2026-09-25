using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using static Nanook.NKit.Configuration.ConfigSettingsConstants;

namespace NKit.Ui.ViewModels.TaskSettings
{
    public class ConvertSettingsViewModel : TaskSettingsViewModelBase
    {
        private bool _isSystemChanging = false;
        private bool _isSavePending = false;

        // ==================== UI COLLECTIONS ====================

        public ObservableCollection<string> AvailableFormats { get; } = new();
        public ObservableCollection<string> AvailableIndexFormats { get; } = new();
        public ObservableCollection<RvzEncodingType> AvailableEncodings { get; } = new();
        // ViewModel's own collections for stable ComboBox binding
        public ObservableCollection<string> AvailableLevels { get; } = new();
        public ObservableCollection<string> AvailableBlockSizes { get; } = new();
        public ObservableCollection<string> AvailableParallelisms { get; } = new();
        public ObservableCollection<string> AvailableCueTypes { get; } = new();
        public ObservableCollection<string> AvailableBinaryExtensions { get; } = new();
        public ObservableCollection<string> AvailableAudioExtensions { get; } = new();

        public ConvertSettingsViewModel() : base(Locator.Current.GetService<NKitSettings>())
        {
            InitializeStaticCollections();
            MainWindowViewModel.SystemOrTaskUpdatedEvent += OnSystemChanged;
            Settings.PropertyChanged += OnSettingsPropertyChanged;

            LoadSettingsForCurrentSystem();
        }

        private void InitializeStaticCollections()
        {
            // Load static collections once
            foreach (RvzEncodingType encoding in ConfigSettingsRanges.GetRvzEncodingTypes())
                AvailableEncodings.Add(encoding);

            foreach (string type in ConfigSettingsRanges.GetCueTypes())
                AvailableCueTypes.Add(type);

            foreach (string ext in ConfigSettingsRanges.GetBinaryExtensions())
                AvailableBinaryExtensions.Add(ext);

            foreach (string ext in ConfigSettingsRanges.GetAudioExtensions())
                AvailableAudioExtensions.Add(ext);
        }

        private void OnSystemChanged(object sender, SystemOrTaskChangedEventArgs e)
        {
            if (e?.IsSystemChange == true)
            {
                _isSystemChanging = true;
                LoadSettingsForCurrentSystem();
                _isSystemChanging = false;
            }
        }

        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Always handle available options changes, even during system changes
            if (e.PropertyName == nameof(Settings.AvailableLevels) ||
                e.PropertyName == nameof(Settings.AvailableBlockSizes) ||
                e.PropertyName == nameof(Settings.AvailableParallelisms))
            {
                NotifyDynamicCollectionProperties();
                return;
            }

            if (_isSystemChanging) return;

            switch (e.PropertyName)
            {
                case nameof(Settings.ConvertSingleFormat):
                    NotifyVisibilityProperties();
                    NotifyDynamicCollectionProperties();
                    // Force UI update for all selections after format changes with delay
                    Task.Run(async () =>
                    {
                        await Task.Delay(1);
                        this.RaisePropertyChanged(nameof(SelectedLevel));
                        this.RaisePropertyChanged(nameof(SelectedBlockSize));
                        this.RaisePropertyChanged(nameof(SelectedParallelism));
                        this.RaisePropertyChanged(nameof(IsLosslessSelected));
                    });
                    break;

                case nameof(Settings.ConvertEncoding):
                    NotifyVisibilityProperties();
                    // Force UI update for level selection after encoding changes with delay
                    Task.Run(async () =>
                    {
                        await Task.Delay(1);
                        this.RaisePropertyChanged(nameof(SelectedLevel));
                    });
                    break;
            }
        }

        private void LoadSettingsForCurrentSystem()
        {
            YamlConfigurationStore configStore = Locator.Current.GetService<ISettingsStore>() as YamlConfigurationStore;
            NKitSettings persistedSettings = configStore?.LoadSettings(Settings.System, Settings.Task);

            if (persistedSettings != null)
            {
                // Load all persisted settings using UpdateFromSettings
                Settings.UpdateFromSettings(persistedSettings);
            }
            else
            {
                // Apply defaults for new system
                Settings.BeginBulkUpdate();
                try
                {
                    Settings.Convert = ConfigSettingsDefaults.GetFullDefaultFormat(Settings.System);
                }
                finally
                {
                    Settings.EndBulkUpdate();
                }
            }

            UpdateFormatCollections();
            NotifyAllPropertiesAfterSystemChange();
            SaveSettings(); // Save on system change
        }

        private void UpdateFormatCollections()
        {
            SystemType system = Settings.System;

            // Single formats
            if (ConfigSettingsDefaults.IsSingleFormatSupported(system))
            {
                IEnumerable<string> formats = ConfigSettingsRanges.GetSupportedDualSingleFormats(system)
                    .Select(f => ConfigSettingsConstants.FormatToDisplay(f, system));
                UpdateCollection(AvailableFormats, formats);
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] AvailableFormats: [{string.Join(", ", AvailableFormats)}]");
            }
            else
            {
                AvailableFormats.Clear();
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] AvailableFormats cleared (single format not supported)");
            }

            // Index formats
            if (ConfigSettingsDefaults.IsIndexedFormatSupported(system))
            {
                IReadOnlyList<string> indexFormats = ConfigSettingsDefaults.IsSingleFormatSupported(system)
                    ? ConfigSettingsRanges.GetSupportedDualIndexFormats(system) // Dual format system
                    : ConfigSettingsRanges.GetSupportedFormats(system); // Index-only system

                IEnumerable<string> displayFormats = indexFormats.Select(f => ConfigSettingsConstants.FormatToDisplay(f, system));
                UpdateCollection(AvailableIndexFormats, displayFormats);
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] AvailableIndexFormats: [{string.Join(", ", AvailableIndexFormats)}]");

                // For index-only systems, also populate single formats for UI binding
                if (!ConfigSettingsDefaults.IsSingleFormatSupported(system))
                {
                    UpdateCollection(AvailableFormats, displayFormats);
                    // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] AvailableFormats populated for index-only system");
                }
            }
            else
            {
                AvailableIndexFormats.Clear();
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] AvailableIndexFormats cleared (indexed format not supported)");
            }
        }



        private void UpdateCollection<T>(ObservableCollection<T> collection, System.Collections.Generic.IEnumerable<T> newItems)
        {
            List<T> newList = newItems.ToList();

            if (collection.Count != newList.Count || !collection.SequenceEqual(newList))
            {
                collection.Clear();
                foreach (T item in newList)
                    collection.Add(item);
            }
        }

        private void NotifyVisibilityProperties()
        {
            this.RaisePropertyChanged(nameof(IsSingleFormatVisible));
            this.RaisePropertyChanged(nameof(IsIndexedFormatVisible));
            this.RaisePropertyChanged(nameof(IsDualFormatVisible));
            this.RaisePropertyChanged(nameof(IsEncodingVisible));
            this.RaisePropertyChanged(nameof(IsLevelVisible));
            this.RaisePropertyChanged(nameof(IsBlockSizeVisible));
            this.RaisePropertyChanged(nameof(IsParallelismVisible));
            this.RaisePropertyChanged(nameof(IsLosslessVisible));
            this.RaisePropertyChanged(nameof(IsCueTypeVisible));
            this.RaisePropertyChanged(nameof(IsBinaryExtensionVisible));
            this.RaisePropertyChanged(nameof(IsAudioExtensionVisible));
        }

        private void NotifyDynamicCollectionProperties()
        {
            UpdateCollection(AvailableLevels, Settings.AvailableLevels);
            UpdateCollection(AvailableBlockSizes, Settings.AvailableBlockSizes);
            UpdateCollection(AvailableParallelisms, Settings.AvailableParallelisms);
        }

        private async void NotifyAllPropertiesAfterSystemChange()
        {
            // Notify collections first
            this.RaisePropertyChanged(nameof(AvailableFormats));
            this.RaisePropertyChanged(nameof(AvailableIndexFormats));
            NotifyDynamicCollectionProperties();

            // Notify visibility
            NotifyVisibilityProperties();

            // Ensure UI thread processes collection updates before selection updates
            await Task.Delay(1);

            // Notify selected values so ComboBoxes can re-evaluate their selections
            this.RaisePropertyChanged(nameof(SelectedSingleFormat));
            this.RaisePropertyChanged(nameof(SelectedIndexFormat));
            this.RaisePropertyChanged(nameof(SelectedEncoding));
            this.RaisePropertyChanged(nameof(SelectedLevel));
            this.RaisePropertyChanged(nameof(SelectedBlockSize));
            this.RaisePropertyChanged(nameof(SelectedParallelism));
            this.RaisePropertyChanged(nameof(SelectedCueType));
            this.RaisePropertyChanged(nameof(SelectedBinaryExtension));
            this.RaisePropertyChanged(nameof(SelectedAudioExtension));

            // Notify verify properties
            this.RaisePropertyChanged(nameof(IsDynamicVerifySelected));
            this.RaisePropertyChanged(nameof(IsDatLookupVerifySelected));
            this.RaisePropertyChanged(nameof(IsNoneVerifySelected));
        }





        private async void SaveSettings()
        {
            if (_isSystemChanging || _isSavePending) return;

            _isSavePending = true;

            // Debounce saves to prevent excessive disk writes
            await Task.Delay(1);

            try
            {
                YamlConfigurationStore configStore = Locator.Current.GetService<ISettingsStore>() as YamlConfigurationStore;
                configStore?.StoreSettings(Settings);
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
            finally
            {
                _isSavePending = false;
            }
        }

        // ==================== UI BINDING PROPERTIES ====================

        public string SelectedSingleFormat
        {
            get
            {
                // During system changes, return null to clear ComboBox selection temporarily
                if (_isSystemChanging)
                    return null;

                string result = ConfigSettingsConstants.FormatToDisplay(Settings.ConvertSingleFormat, Settings.System);
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] SelectedSingleFormat GET - System: {Settings.System}, Raw: '{Settings.ConvertSingleFormat}', Display: '{result}'");
                return result;
            }
            set
            {
                if (_isSystemChanging)
                {
                    // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] SelectedSingleFormat SET BLOCKED (system changing) - Value: '{value}'");
                    return;
                }
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] SelectedSingleFormat SET - System: {Settings.System}, Display: '{value}', Raw: '{DisplayToFormat(value)}'");
                Settings.ConvertSingleFormat = ConfigSettingsConstants.DisplayToFormat(value);
            }
        }

        public string SelectedIndexFormat
        {
            get
            {
                // During system changes, return null to clear ComboBox selection temporarily
                if (_isSystemChanging)
                    return null;

                string result = ConfigSettingsConstants.FormatToDisplay(Settings.ConvertIndexedFormat, Settings.System);
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] SelectedIndexFormat GET - System: {Settings.System}, Raw: '{Settings.ConvertIndexedFormat}', Display: '{result}'");
                return result;
            }
            set
            {
                if (_isSystemChanging)
                {
                    // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] SelectedIndexFormat SET BLOCKED (system changing) - Value: '{value}'");
                    return;
                }
                // System.Diagnostics.Debug.WriteLine($"[ConvertSettingsViewModel] SelectedIndexFormat SET - System: {Settings.System}, Display: '{value}', Raw: '{DisplayToFormat(value)}'");
                Settings.ConvertIndexedFormat = ConfigSettingsConstants.DisplayToFormat(value);
            }
        }

        public RvzEncodingType SelectedEncoding
        {
            get => Enum.TryParse<RvzEncodingType>(Settings.ConvertEncoding, true, out RvzEncodingType enc) ? enc : RvzEncodingType.ZStd;
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertEncoding = value.ToString().ToLowerInvariant();
            }
        }

        public string SelectedLevel
        {
            get
            {
                // During system changes, return null to clear ComboBox selection temporarily
                if (_isSystemChanging)
                    return null;
                return Settings.ConvertLevel;
            }
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertLevel = value;
            }
        }

        public string SelectedBlockSize
        {
            get => Settings.ConvertBlockSize;
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertBlockSize = value;
            }
        }

        public string SelectedParallelism
        {
            get => Settings.ConvertParallelism;
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertParallelism = value;
            }
        }

        public string SelectedCueType
        {
            get
            {
                // During system changes, return null to clear ComboBox selection temporarily
                if (_isSystemChanging)
                    return null;
                return Settings.ConvertCueType;
            }
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertCueType = value;
            }
        }

        public string SelectedBinaryExtension
        {
            get
            {
                // During system changes, return null to clear ComboBox selection temporarily
                if (_isSystemChanging)
                    return null;
                return Settings.ConvertBinary;
            }
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertBinary = value;
            }
        }

        public string SelectedAudioExtension
        {
            get
            {
                // During system changes, return null to clear ComboBox selection temporarily
                if (_isSystemChanging)
                    return null;
                return Settings.ConvertAudio;
            }
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertAudio = value;
            }
        }

        public bool IsLosslessSelected
        {
            get => Settings.ConvertLossless;
            set
            {
                if (_isSystemChanging) return;
                Settings.ConvertLossless = value;
            }
        }

        // ==================== VISIBILITY PROPERTIES ====================

        public bool IsSingleFormatVisible => ConfigSettingsDefaults.IsSingleFormatSupported(Settings.System);
        public bool IsIndexedFormatVisible => ConfigSettingsDefaults.IsIndexedFormatSupported(Settings.System);
        public bool IsDualFormatVisible => IsSingleFormatVisible && IsIndexedFormatVisible;

        public bool IsEncodingVisible => ConfigSettingsDefaults.IsEncodingTypeSupported(Settings.System, Settings.ConvertSingleFormat);
        public bool IsLevelVisible => ConfigSettingsDefaults.IsLevelsSupported(Settings.ConvertSingleFormat, Settings.ConvertEncoding) && Settings.ConvertEncoding?.ToLowerInvariant() != EncodingNone;
        public bool IsBlockSizeVisible => ConfigSettingsDefaults.IsBlockSizesSupported(Settings.ConvertSingleFormat);
        public bool IsParallelismVisible => ConfigSettingsDefaults.IsParallelismsSupported(Settings.ConvertSingleFormat);
        public bool IsLosslessVisible => ConfigSettingsDefaults.IsLosslessSupported(Settings.ConvertSingleFormat);

        public bool IsCueTypeVisible => ConfigSettingsDefaults.IsCueTypesSupported(Settings.ConvertIndexedFormat);
        public bool IsBinaryExtensionVisible => ConfigSettingsDefaults.IsBinaryExtensionsSupported(Settings.ConvertIndexedFormat);
        public bool IsAudioExtensionVisible => ConfigSettingsDefaults.IsAudioExtensionsSupported(Settings.ConvertIndexedFormat);

        // ==================== ADDITIONAL SETTINGS ====================

        public bool IsSkipIfCompletedSelected
        {
            get => Settings.SkipIfCompleted;
            set
            {
                if (Settings.SkipIfCompleted != value)
                {
                    Settings.SkipIfCompleted = value;
                    SaveSettings();
                }
            }
        }

        public bool IsOutAsDatMatchSelected
        {
            get => Settings.OutAsDatMatch;
            set
            {
                if (Settings.OutAsDatMatch != value)
                {
                    Settings.OutAsDatMatch = value;
                    SaveSettings();
                }
            }
        }

        public new bool IsDynamicVerifySelected
        {
            get => Settings.V == Verify.Y;
            set
            {
                if (value && Settings.V != Verify.Y)
                {
                    Settings.V = Verify.Y;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(IsDatLookupVerifySelected));
                    this.RaisePropertyChanged(nameof(IsNoneVerifySelected));
                    SaveSettings();
                }
            }
        }

        public new bool IsDatLookupVerifySelected
        {
            get => Settings.V == Verify.DatLookup;
            set
            {
                if (value && Settings.V != Verify.DatLookup)
                {
                    Settings.V = Verify.DatLookup;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(IsDynamicVerifySelected));
                    this.RaisePropertyChanged(nameof(IsNoneVerifySelected));
                    SaveSettings();
                }
            }
        }

        public new bool IsNoneVerifySelected
        {
            get => Settings.V == Verify.N;
            set
            {
                if (value && Settings.V != Verify.N)
                {
                    Settings.V = Verify.N;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(IsDynamicVerifySelected));
                    this.RaisePropertyChanged(nameof(IsDatLookupVerifySelected));
                    SaveSettings();
                }
            }
        }
    }
}
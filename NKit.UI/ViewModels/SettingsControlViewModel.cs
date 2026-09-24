using Nanook.NKit;
using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using Splat;
using System;
using System.Threading.Tasks;

namespace NKit.Ui.ViewModels
{
    public class SettingsControlViewModel : ReactiveObject
    {
        public NKitSettings Settings { get; set; }
        private ISettingsStore SettingsStore { get; set; }
        private DateTime _lastManualSelection = DateTime.MinValue;

        public SettingsControlViewModel() : base()
        {
            Settings = Locator.Current.GetService<NKitSettings>();
            SettingsStore = Locator.Current.GetService<ISettingsStore>();

            IsConvertSelected = false;
            IsScanSelected = false;
            IsDedupeSelected = false;
            IsFixSelected = false;
            IsVerifySelected = false;
            IsExtractSelected = false;
            IsExpandSelected = false;
            IsGameCubeWiiConvertSelected = false;
            IsPs3ConvertSelected = false;
            IsPspConvertSelected = false;
            IsWiiUConvertSelected = false;

            // Subscribe to events AFTER initializing properties
            MainWindowViewModel.SystemOrTaskUpdatedEvent += UpdateTaskSettingsVisibility;

            // Set task visibility first
            IsConvertSelected = Settings.Task is TaskType.Convert or TaskType.Expand;
            IsScanSelected = Settings.Task is TaskType.Scan;
            IsDedupeSelected = Settings.Task is TaskType.Dedupe;
            IsFixSelected = Settings.Task is TaskType.Fix;
            IsExtractSelected = Settings.Task is TaskType.Extract;
            IsExpandSelected = Settings.Task is TaskType.Expand;
            IsVerifySelected = Settings.Task is TaskType.Verify;

            IsGameCubeWiiConvertSelected = IsConvertSelected && (Settings.System is SystemType.GameCube or SystemType.Wii);
            IsWiiUConvertSelected = IsConvertSelected && (Settings.System is SystemType.WiiU);
            IsPs3ConvertSelected = IsConvertSelected && (Settings.System is SystemType.PS3);
            IsPspConvertSelected = IsConvertSelected && (Settings.System is SystemType.PSP);

            // Force switch to the appropriate task tab on startup
            int taskTabIndex = Settings.Task switch
            {
                TaskType.Convert or TaskType.Expand => 3, // Convert tab
                TaskType.Scan => 0,
                TaskType.Dedupe => 1,
                TaskType.Fix => 2,
                TaskType.Extract => 4,
                TaskType.Verify => 5,
                _ => 3 // Default to Convert tab
            };

            // Use property setter to ensure proper binding synchronization
            SelectedTabIndex = taskTabIndex;
        }

        private void UpdateTaskSettingsVisibility(object sender, SystemOrTaskChangedEventArgs e)
        {
            // Store current tab index before visibility changes (but only for system changes)
            if (e.IsSystemChange)
            {
                StoreSelectedTabIndexForCurrentSystem();
            }

            // Update task visibility flags
            IsConvertSelected = Settings.Task is TaskType.Convert or TaskType.Expand;
            IsScanSelected = Settings.Task is TaskType.Scan;
            IsDedupeSelected = Settings.Task is TaskType.Dedupe;
            IsFixSelected = Settings.Task is TaskType.Fix;
            IsExtractSelected = Settings.Task is TaskType.Extract;
            IsExpandSelected = Settings.Task is TaskType.Expand;
            IsVerifySelected = Settings.Task is TaskType.Verify;

            IsGameCubeWiiConvertSelected = IsConvertSelected && (Settings.System is SystemType.GameCube or SystemType.Wii);
            IsWiiUConvertSelected = IsConvertSelected && (Settings.System is SystemType.WiiU);
            IsPs3ConvertSelected = IsConvertSelected && (Settings.System is SystemType.PS3);
            IsPspConvertSelected = IsConvertSelected && (Settings.System is SystemType.PSP);

            // Handle tab switching logic based on change type
            if (e?.IsSystemChange == true)
            {
                // System changed - preserve current tab to allow comparing system options
                // No automatic tab switching on system change
            }
            else if (e?.IsTaskChange == true)
            {
                // Task changed - switch to appropriate task tab with delay to avoid overriding manual selection
                _ = Task.Delay(100).ContinueWith(_ =>
                {
                    // Only switch if user hasn't manually selected a tab in the last 500ms
                    if (DateTime.Now.Subtract(_lastManualSelection).TotalMilliseconds > 500)
                        SwitchToTaskSpecificTab(Settings.Task);
                });
            }
        }

        /// <summary>
        /// Switches to the appropriate tab based on the selected task
        /// </summary>
        /// <param name="task">The task that was selected</param>
        private void SwitchToTaskSpecificTab(TaskType task)
        {
            int targetTabIndex = task switch
            {
                TaskType.Scan => GetTabIndexByName("Scan"),
                TaskType.Dedupe => GetTabIndexByName("Dedupe"),
                TaskType.Fix => GetTabIndexByName("Fix"),
                TaskType.Convert or TaskType.Expand => GetTabIndexByName("Convert"),
                TaskType.Extract => GetTabIndexByName("Extract"),
                TaskType.Verify => GetTabIndexByName("Verify"),
                _ => -1 // No specific task tab
            };

            // If the specific task tab isn't available, find the first visible task tab
            if (targetTabIndex == -1)
            {
                targetTabIndex = GetFirstVisibleTaskTab();
            }

            // Use property setter to ensure proper binding synchronization
            SelectedTabIndex = targetTabIndex;
        }

        /// <summary>
        /// Gets the tab index by tab name based on the XAML structure
        /// Returns -1 if tab is not found or not visible for current system/task
        /// </summary>
        /// <param name="tabName">Name of the tab to find</param>
        /// <returns>Tab index or -1 if not found/visible</returns>
        private int GetTabIndexByName(string tabName)
        {
            // Based on the XAML structure in SettingsControl.axaml
            // Note: This maps to the actual tab item positions in the TabControl
            return tabName switch
            {
                "Scan" when IsScanSelected => 0,
                "Dedupe" when IsDedupeSelected => 1,
                "Fix" when IsFixSelected => 2,
                "Convert" when IsConvertSelected => 3,
                "Extract" when IsExtractSelected => 4,
                "Verify" when IsVerifySelected => 5,
                "Processing" => 6, // Always visible
                "Paths" => 7,      // Always visible
                "UI" => 8,         // Always visible
                _ => -1 // Tab not found or not visible
            };
        }

        /// <summary>
        /// Gets the first visible task tab (task settings), prioritizing over process options
        /// </summary>
        /// <returns>Tab index of first visible task tab, or Convert tab if none found</returns>
        private int GetFirstVisibleTaskTab()
        {
            // Check task tabs in order of preference
            if (IsConvertSelected) return GetTabIndexByName("Convert");
            if (IsScanSelected) return GetTabIndexByName("Scan");
            if (IsExtractSelected) return GetTabIndexByName("Extract");
            if (IsVerifySelected) return GetTabIndexByName("Verify");
            if (IsDedupeSelected) return GetTabIndexByName("Dedupe");
            if (IsFixSelected) return GetTabIndexByName("Fix");

            // Default to Convert tab (index 3) instead of first tab
            return 3;
        }

        private int _selectedTabIndex = 0;

        /// <summary>
        /// Gets or sets the currently selected tab index, persisted per system
        /// </summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    this.RaisePropertyChanged(nameof(SelectedTabIndex));

                    // Record timestamp of manual selection
                    _lastManualSelection = DateTime.Now;

                    // Store immediately when user changes tabs
                    StoreSelectedTabIndexForCurrentSystem();
                }
            }
        }

        /// <summary>
        /// Stores the current tab index for the current system
        /// </summary>
        private void StoreSelectedTabIndexForCurrentSystem()
        {
            if (Settings?.System == SystemType.NotSet || Settings?.Task == TaskType.NotSet)
                return;

            try
            {
                // We need to store this in the system configuration
                // Since we're using YamlConfigurationStore, we need to access it directly
                if (SettingsStore is YamlConfigurationStore yamlStore)
                {
                    yamlStore.StoreTabIndexForSystem(Settings.System, SelectedTabIndex);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to store tab index for system {Settings.System}: {ex.Message}");
            }
        }

        private bool _isExtractSelected;

        public bool IsExtractSelected
        {
            get => _isExtractSelected;
            set
            {
                _isExtractSelected = value;
                this.RaisePropertyChanged(nameof(IsExtractSelected));
            }
        }

        private bool _isExpandSelected;

        public bool IsExpandSelected
        {
            get => _isExpandSelected;
            set
            {
                _isExpandSelected = value;
                this.RaisePropertyChanged(nameof(IsExpandSelected));
            }
        }

        private bool _isConvertSelected;

        public bool IsConvertSelected
        {
            get => _isConvertSelected;
            set
            {
                _isConvertSelected = value;
                this.RaisePropertyChanged(nameof(IsConvertSelected));
            }
        }

        private bool _isVerifySelected;

        public bool IsVerifySelected
        {
            get => _isVerifySelected;
            set
            {
                _isVerifySelected = value;
                this.RaisePropertyChanged(nameof(IsVerifySelected));
            }
        }

        private bool _isScanSelected;

        public bool IsScanSelected
        {
            get => _isScanSelected;
            set
            {
                _isScanSelected = value;
                this.RaisePropertyChanged(nameof(IsScanSelected));
            }
        }

        private bool _isFixSelected;

        public bool IsFixSelected
        {
            get => _isFixSelected;
            set
            {
                _isFixSelected = value;
                this.RaisePropertyChanged(nameof(IsFixSelected));
            }
        }

        private bool _isDedupeSelected;

        public bool IsDedupeSelected
        {
            get => _isDedupeSelected;
            set
            {
                _isDedupeSelected = value;
                this.RaisePropertyChanged(nameof(IsDedupeSelected));
            }
        }

        private bool _isGameCubeWiiConvertSelected;

        public bool IsGameCubeWiiConvertSelected
        {
            get => _isGameCubeWiiConvertSelected;
            set
            {
                _isGameCubeWiiConvertSelected = value;
                this.RaisePropertyChanged(nameof(IsGameCubeWiiConvertSelected));
            }
        }

        private bool _isPs3ConvertSelected;

        public bool IsPs3ConvertSelected
        {
            get => _isPs3ConvertSelected;
            set
            {
                _isPs3ConvertSelected = value;
                this.RaisePropertyChanged(nameof(IsPs3ConvertSelected));
            }
        }

        private bool _isPspConvertSelected;

        public bool IsPspConvertSelected
        {
            get => _isPspConvertSelected;
            set
            {
                _isPspConvertSelected = value;
                this.RaisePropertyChanged(nameof(IsPspConvertSelected));
            }
        }

        private bool _isWiiUConvertSelected;

        public bool IsWiiUConvertSelected
        {
            get => _isWiiUConvertSelected;
            set
            {
                _isWiiUConvertSelected = value;
                this.RaisePropertyChanged(nameof(IsWiiUConvertSelected));
            }
        }
    }
}
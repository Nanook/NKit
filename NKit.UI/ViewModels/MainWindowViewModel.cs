using Nanook.NKit;
using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using Splat;
using System;
using System.Collections.ObjectModel;

namespace NKit.Ui.ViewModels
{
    /// <summary>
    /// Event args that indicate what type of change occurred (system or task change)
    /// </summary>
    public class SystemOrTaskChangedEventArgs : EventArgs
    {
        public bool IsSystemChange { get; set; }
        public bool IsTaskChange { get; set; }
        public SystemType System { get; set; }
        public TaskType Task { get; set; }
    }

    public class MainWindowViewModel : ViewModelBase
    {
        public static EventHandler<SystemOrTaskChangedEventArgs> SystemOrTaskUpdatedEvent;

        private readonly MainWindowService _service;
        // When true we suppress automatic saving triggered by programmatic task/system updates
        private bool _suppressAutoStore = false;

        public NKitSettings Settings { get; set; }
        public ISettingsStore SettingsStore { get; set; }

        public string NKitVersion { get; } = $"NKit v{Nanook.NKit.AppSettings.GetVersion()}";

        public MainWindowViewModel() : base()
        {
            _service = new MainWindowService();
            Settings = Locator.Current.GetService<NKitSettings>();
            SettingsStore = Locator.Current.GetService<ISettingsStore>();

            SystemTypes = _service.GetAllSystemTypes();
            TaskTypes = _service.GetTaskTypes(Settings.System);

            SelectedSystem = Settings.System;
            SelectedTask = Settings.Task;

            FilterTextColour = "White";
        }

        private ObservableCollection<SystemType> _systemTypes;
        public ObservableCollection<SystemType> SystemTypes
        {
            get => _systemTypes;
            set => this.RaiseAndSetIfChanged(ref _systemTypes, value);
        }

        private ObservableCollection<TaskType> _taskTypes;
        public ObservableCollection<TaskType> TaskTypes
        {
            get => _taskTypes;
            set => this.RaiseAndSetIfChanged(ref _taskTypes, value);
        }



        public (SystemType, ObservableCollection<SystemType>) GetFilteredSystemTypes(string systemFilter, ObservableCollection<SystemType> systems)
        {
            (SystemType newSystem, ObservableCollection<SystemType> newSystemTypes) = _service.GetFilteredSystemTypes(systemFilter, systems, SelectedSystem);

            FilterTextColour = string.IsNullOrWhiteSpace(systemFilter) || newSystemTypes.Count > 0 ? "White" : "Red";

            return (newSystem, newSystemTypes);
        }

        private void setTasksFromSystem(SystemType systemType) => TaskTypes = _service.GetTaskTypes(systemType);

        public void SetTasksFromSystem(SystemType systemType) => setTasksFromSystem(systemType);



        public bool DoesSystemHaveTask(SystemType systemType, TaskType taskType) => _service.DoesSystemHaveTask(systemType, taskType);

        private string _filterTextColour;
        public string FilterTextColour
        {
            get => _filterTextColour;
            set => this.RaiseAndSetIfChanged(ref _filterTextColour, value);
        }


        public SystemType SelectedSystem
        {
            get => Settings.System;
            set
            {
                if (value == Settings.System || value == SystemType.NotSet)
                    return;

                if (value != SystemType.NotSet)
                {
                    SystemType currentSystem = Settings.System;
                    TaskType currentTask = Settings.Task;
                    // System.Diagnostics.Debug.WriteLine($"SYSTEM CHANGE: {currentSystem}");
                    if (currentSystem != SystemType.NotSet)
                        SettingsStore.StoreSettings(Settings);

                    // Suppress saves triggered by the subsequent programmatic task change
                    _suppressAutoStore = true;

                    Settings.System = value;
                    setTasksFromSystem(value);

                    updateSettingsFromStoreWithTaskPreference(value, currentTask);

                    // Re-enable automatic storing after programmatic update is complete
                    _suppressAutoStore = false;

                    SystemOrTaskUpdatedEvent?.Invoke(this, new SystemOrTaskChangedEventArgs
                    {
                        IsSystemChange = true,
                        IsTaskChange = false,
                        System = value,
                        Task = Settings.Task
                    });
                }
            }
        }

        public TaskType SelectedTask
        {
            get => Settings.Task;
            set
            {
                if (value == Settings.Task || value == TaskType.NotSet)
                    return;

                if (value != TaskType.NotSet)
                {
                    SystemType currentSystem = Settings.System;
                    TaskType currentTask = Settings.Task;
                    // System.Diagnostics.Debug.WriteLine($"TASK CHANGE: {currentSystem}");
                    // Only store when this is a user-initiated change (not suppressed)
                    if (!_suppressAutoStore && currentSystem != SystemType.NotSet && currentTask != TaskType.NotSet)
                        SettingsStore.StoreSettings(Settings);


                    Settings.Task = value;
                    updateSettingsFromStore(SelectedSystem, value);

                    SystemOrTaskUpdatedEvent?.Invoke(this, new SystemOrTaskChangedEventArgs
                    {
                        IsSystemChange = false,
                        IsTaskChange = true,
                        System = SelectedSystem,
                        Task = value
                    });
                }
            }
        }

        public void UpdateSettingsFromStore(SystemType systemType)
        {
            TaskType defaultTaskForSystem = _service.GetDefaultTaskForSystem(systemType);
            updateSettingsFromStore(systemType, defaultTaskForSystem);
        }

        private void updateSettingsFromStoreWithTaskPreference(SystemType systemType, TaskType preferredTask)
        {
            TaskType taskToUse = _service.DetermineTaskWithPreference(systemType, preferredTask);
            updateSettingsFromStore(systemType, taskToUse);
        }

        private void updateSettingsFromStore(SystemType systemType, TaskType taskType)
        {
            NKitSettings loadedSettings = SettingsStore.GetSettings(systemType, taskType);

            if (loadedSettings != null)
            {
                Settings.UpdateFromSettings(loadedSettings);
                Settings.Task = taskType;
            }
            else
            {
                NKitSettings newSettings = NKitSettings.GetDefaultSettings(systemType, taskType, Settings);
                Settings.UpdateFromSettings(newSettings);
                Settings.Task = taskType;
                // Note: Don't save here - let the ViewModel handle saving when user makes changes
            }
        }

        public void RefreshSystemsAndTasks(SystemType? system)
        {
            this.RaisePropertyChanged(nameof(SystemTypes));

            if (system.HasValue && system != SelectedSystem)
                this.RaisePropertyChanged(nameof(SelectedSystem));

            this.RaisePropertyChanged(nameof(TaskTypes));
            this.RaisePropertyChanged(nameof(SelectedTask));
        }


    }
}
using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Models;
using ReactiveUI;
using Splat;
using System;

namespace NKit.Ui.ViewModels
{
    public class PathsControlViewModel : ReactiveObject
    {
        public NKitSettings Settings { get; set; }
        public ISettingsStore SettingsStore { get; set; }

        public string UserPath
        {
            get
            {
                try
                {
                    using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
                    ConfigurationInfo configInfo = configManager.GetConfigurationInfo();
                    return configInfo.UserDataDirectory;
                }
                catch
                {
                    return "[Error getting user path]";
                }
            }
        }

        public PathsControlViewModel() : base()
        {
            Settings = Locator.Current.GetService<NKitSettings>();
            SettingsStore = Locator.Current.GetService<ISettingsStore>();

            MainWindowViewModel.SystemOrTaskUpdatedEvent += UpdatePathEnablement;

            // Call once when initalising
            UpdatePathEnablement(null, null);
        }

        public void UpdatePathEnablement(Object sender, EventArgs e)
        {
            // Show FixFiles path for systems that support FixFiles (GameCube, Wii, PS3)
            IsFixFilesPathEnabled = ConfigSettingsDefaults.IsFixFilesSupported(Settings.System);

            // Show FixInfo path for systems that support it:
            // - GameCube/Wii systems (for all tasks - Fix data is used by Convert, Scan, Fix, etc.)
            // - PS3 system (always, for IRD files)
            // - Dreamcast system when using Convert or Expand tasks
            IsFixInfoPathEnabled = ConfigSettingsDefaults.IsFixSupported(Settings.System) &&
                (Settings.System is SystemType.GameCube or SystemType.Wii  // GameCube/Wii support fix for all tasks
                || Settings.System is SystemType.PS3  // PS3 always supports fix (IRD files)
                || (Settings.System is SystemType.Dreamcast && Settings.Task is TaskType.Convert or TaskType.Expand)); // Dreamcast only for specific tasks

            IsKeysPathEnabled = Settings.System is SystemType.WiiU or SystemType.PS3;

            // Dat path: enable by default for all systems (UI exposes dat configuration widely)
            // This ensures the dat bindings are visible in the UI. If needed, make this conditional later.
            IsDatPathEnabled = true;
        }

        private bool _isDatPathEnabled;

        public bool IsDatPathEnabled
        {
            get => _isDatPathEnabled; set => this.RaiseAndSetIfChanged(ref _isDatPathEnabled, value);
        }

        private bool _isFixInfoPathEnabled;

        public bool IsFixInfoPathEnabled
        {
            get => _isFixInfoPathEnabled; set => this.RaiseAndSetIfChanged(ref _isFixInfoPathEnabled, value);
        }

        private bool _isFixFilesPathEnabled;

        public bool IsFixFilesPathEnabled
        {
            get => _isFixFilesPathEnabled; set => this.RaiseAndSetIfChanged(ref _isFixFilesPathEnabled, value);
        }

        private bool _isKeysPathEnabled;

        public bool IsKeysPathEnabled
        {
            get => _isKeysPathEnabled; set => this.RaiseAndSetIfChanged(ref _isKeysPathEnabled, value);
        }

    }
}
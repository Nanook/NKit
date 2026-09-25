using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Models;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LogLevel = Nanook.NKit.LogLevel;

namespace NKit.Ui.ViewModels
{
    internal class ProcessingOptionsViewModel : ReactiveObject
    {
        public ObservableCollection<LogLevel> LogLevels { get; set; } = GetLogLevels();
        public ObservableCollection<Verify> Verifies { get; set; } = GetVerifyValues();

        public NKitSettings Settings { get; set; }
        public ISettingsStore SettingsStore { get; set; }

        public ProcessingOptionsViewModel() : base()
        {
            Settings = Locator.Current.GetService<NKitSettings>();
            SettingsStore = Locator.Current.GetService<ISettingsStore>();
            MainWindowViewModel.SystemOrTaskUpdatedEvent += UpdateProcessingOptionsEnablement;

            // Subscribe to LogOutLevel changes to update IsFileLogEnabled
            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Settings.LogOutLevel))
                {
                    this.RaisePropertyChanged(nameof(IsFileLogEnabled));
                }
            };
        }

        public bool IsFileLogEnabled => Settings?.LogOutLevel != LogLevel.None;

        // Expose the user configuration path for binding in the view
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

        private void UpdateProcessingOptionsEnablement(object sender, EventArgs e) => this.RaisePropertyChanged(nameof(IsFileLogEnabled));

        private static ObservableCollection<LogLevel> GetLogLevels() =>
            new ObservableCollection<LogLevel>(new List<LogLevel>
            {
                LogLevel.None,
                LogLevel.Error,
                LogLevel.Warning,
                LogLevel.Info,
                LogLevel.Detail,
                LogLevel.Trace,
            });

        private static ObservableCollection<Verify> GetVerifyValues() => new ObservableCollection<Verify>(Enum.GetValues<Verify>().ToList());
    }
}
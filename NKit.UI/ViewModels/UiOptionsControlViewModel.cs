using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using Splat;
using System;

namespace NKit.Ui.ViewModels
{
    public class UiOptionsControlViewModel : ReactiveObject
    {
        private bool _useNativeTitleBar;

        public UiSettings UiSettings { get; set; }

        /// <summary>
        /// Whether to use native OS title bar on Linux (requires application restart).
        /// </summary>
        public bool UseNativeTitleBar
        {
            get => _useNativeTitleBar;
            set
            {
                this.RaiseAndSetIfChanged(ref _useNativeTitleBar, value);
                // Persist the setting immediately
                YamlConfigurationStore store = Locator.Current.GetService<ISettingsStore>() as YamlConfigurationStore;
                store?.SetWindowDecorationMode(value ? "native" : "csd");
            }
        }

        public UiOptionsControlViewModel()
        {
            ISettingsStore settingsStore = Locator.Current.GetService<ISettingsStore>();
            UiSettings = settingsStore.UiSettings;

            // Initialize from persisted setting
            YamlConfigurationStore yamlStore = settingsStore as YamlConfigurationStore;
            string mode = yamlStore?.GetWindowDecorationMode() ?? "csd";
            _useNativeTitleBar = string.Equals(mode, "native", StringComparison.OrdinalIgnoreCase);
        }
    }
}
using Nanook.NKit;
using NKit.Ui.Helpers;
using NKit.Ui.Models;
using ReactiveUI;
using Splat;
using System.ComponentModel;

namespace NKit.Ui.ViewModels.TaskSettings
{
    public class TaskSettingsViewModelBase : ReactiveObject
    {
        private readonly NKitSettings _settings;
        private string _helpText = string.Empty;
        private bool _isHelpExpanded = false;
        private bool _enableTooltips = false;
        private bool _showTooltips = false;
        private ISettingsStore _settingsStore;

        public NKitSettings Settings => _settings;

        public string HelpText { get => _helpText; protected set => this.RaiseAndSetIfChanged(ref _helpText, value); }

        public bool IsHelpExpanded
        {
            get => _isHelpExpanded;
            set => this.RaiseAndSetIfChanged(ref _isHelpExpanded, value);
        }

        public bool EnableTooltips
        {
            get => _enableTooltips;
            set => this.RaiseAndSetIfChanged(ref _enableTooltips, value);
        }

        public bool ShowTooltips
        {
            get => _showTooltips;
            private set => this.RaiseAndSetIfChanged(ref _showTooltips, value);
        }

        public TaskSettingsViewModelBase(NKitSettings settings)
        {
            _settings = settings;

            RefreshVerifySettings();

            // Initialize settings store and observe global UiSettings.ShowTooltips
            _settingsStore = Locator.Current.GetService<ISettingsStore>();
            if (_settingsStore?.UiSettings != null)
            {
                ShowTooltips = _settingsStore.UiSettings.ShowTooltips;
                _settingsStore.UiSettings.PropertyChanged += UiSettings_PropertyChanged;
            }
        }

        private void UiSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UiSettings.ShowTooltips))
            {
                ShowTooltips = _settingsStore?.UiSettings?.ShowTooltips ?? false;
            }
        }

        public void RefreshVerifySettings()
        {
            (IsDynamicVerifyEnabled, IsDatLookupVerifyEnabled, IsNoneVerifyEnabled) = VerifyHelper.GetEnabledVerificationOptions(Settings.Task);

            IsDynamicVerifySelected = _settings.V == Verify.Y;
            IsDatLookupVerifySelected = _settings.V == Verify.DatLookup;
            IsNoneVerifySelected = _settings.V == Verify.N;
        }

        // ======= IsSelected =======
        private bool _isDynamicVerifySelected;

        public bool IsDynamicVerifySelected
        {
            get => _isDynamicVerifySelected;
            set
            {
                if (value)
                    _settings.V = Verify.Y;

                _isDynamicVerifySelected = value;
                this.RaisePropertyChanged(nameof(IsDynamicVerifySelected));
            }
        }

        private bool _isDatLookupVerifySelected;

        public bool IsDatLookupVerifySelected
        {
            get => _isDatLookupVerifySelected;
            set
            {
                if (value)
                    _settings.V = Verify.DatLookup;

                _isDatLookupVerifySelected = value;
                this.RaisePropertyChanged(nameof(IsDatLookupVerifySelected));
            }
        }

        private bool _isNoneVerifySelected;

        public bool IsNoneVerifySelected
        {
            get => _isNoneVerifySelected;
            set
            {
                if (value)
                    _settings.V = Verify.N;

                _isNoneVerifySelected = value;
                this.RaisePropertyChanged(nameof(IsNoneVerifySelected));
            }
        }

        // ======= End IsSelected =======

        // ======= IsRadioButtonEnabled =======
        private bool _isDynamicVerifyEnabled;

        public bool IsDynamicVerifyEnabled
        {
            get => _isDynamicVerifyEnabled; set => this.RaiseAndSetIfChanged(ref _isDynamicVerifyEnabled, value);
        }

        private bool _isDatLookupVerifyEnabled;

        public bool IsDatLookupVerifyEnabled
        {
            get => _isDatLookupVerifyEnabled; set => this.RaiseAndSetIfChanged(ref _isDatLookupVerifyEnabled, value);
        }

        private bool _isNoneVerifyEnabled;

        public bool IsNoneVerifyEnabled
        {
            get => _isNoneVerifyEnabled; set => this.RaiseAndSetIfChanged(ref _isNoneVerifyEnabled, value);
        }
        // ======= End IsRadioButtonEnabled =======
    }
}
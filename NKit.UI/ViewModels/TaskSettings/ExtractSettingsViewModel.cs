using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using Splat;
using System;

namespace NKit.Ui.ViewModels.TaskSettings
{
    public class ExtractSettingsViewModel : TaskSettingsViewModelBase
    {
        private readonly ExtractSettingsService _service = new();

        public ExtractSettingsViewModel() : base(Locator.Current.GetService<NKitSettings>())
        {
            MainWindowViewModel.SystemOrTaskUpdatedEvent += SystemOrTaskChangedHandler;
            ensureValidDefaults();
        }

        public void SystemOrTaskChangedHandler(Object sender, EventArgs e)
        {
            if (Settings.Task is TaskType.Extract or TaskType.FixExtract)
            {
                ensureValidDefaults();
                updateForensicModeAvailability();
                UpdateEnablement();
            }
        }

        private void updateForensicModeAvailability()
        {
            IsExtractionModeVisible = _service.IsFixFilesSupportedSystem(Settings.System);

            this.RaisePropertyChanged(nameof(IsAllFilesModeSelected));
            this.RaisePropertyChanged(nameof(IsFixFilesModeSelected));
            this.RaisePropertyChanged(nameof(IsForensicModeSelected));
        }

        public void UpdateEnablement()
        {
            updateExtractTypeSelection();
            updateSearchTermVisibility();
            this.RaisePropertyChanged(nameof(SearchTerm));
        }

        private void updateExtractTypeSelection()
        {
            (bool isMaskSelected, bool isRegexSelected) selection = _service.GetExtractTypeSelection(Settings.ExtractType);
            _isMaskSelected = selection.isMaskSelected;
            _isRegexSelected = selection.isRegexSelected;

            if (!selection.isMaskSelected && !selection.isRegexSelected)
                Settings.ExtractType = ConfigSettingsConstants.ExtractFlagMaskToRegex;

            this.RaisePropertyChanged(nameof(IsMaskSelected));
            this.RaisePropertyChanged(nameof(IsRegexSelected));
        }

        private void updateSearchTermVisibility()
        {
            IsSearchPatternVisible = !Settings.ExtractForensic && Settings.ExtractMode == ExtractMode.AllFiles;
            IsSearchTermEnabled = IsSearchPatternVisible;

            if (Settings.ExtractForensic && string.IsNullOrEmpty(Settings.ExtractSearchTerm))
                Settings.ExtractSearchTerm = ConfigSettingsDefaults.GetDefaultExtractSearchTerm();
        }

        public bool IsAllFilesModeSelected
        {
            get => !Settings.ExtractForensic && Settings.ExtractMode == ExtractMode.AllFiles;
            set
            {
                if (value)
                {
                    Settings.ExtractMode = ExtractMode.AllFiles;
                    Settings.ExtractForensic = false;
                    this.RaisePropertyChanged(nameof(IsForensicModeSelected));
                }
                this.RaisePropertyChanged(nameof(IsAllFilesModeSelected));
                UpdateEnablement();
            }
        }

        public bool IsFixFilesModeSelected
        {
            get => !Settings.ExtractForensic && Settings.ExtractMode == ExtractMode.FixFiles;
            set
            {
                if (value)
                {
                    Settings.ExtractMode = ExtractMode.FixFiles;
                    Settings.ExtractForensic = false;
                    this.RaisePropertyChanged(nameof(IsForensicModeSelected));
                }
                this.RaisePropertyChanged(nameof(IsFixFilesModeSelected));
                UpdateEnablement();
            }
        }

        private bool _displayInvalidRegexWarning;

        public bool DisplayInvalidRegexWarning
        {
            get => _displayInvalidRegexWarning;
            set => this.RaiseAndSetIfChanged(ref _displayInvalidRegexWarning, value);
        }

        private string _regexValidationMessage;

        public string RegexValidationMessage
        {
            get => _regexValidationMessage;
            set => this.RaiseAndSetIfChanged(ref _regexValidationMessage, value);
        }

        public void CheckRegex()
        {
            (bool isRegexValid, string validationMessage) result = _service.ValidateRegexPattern(SearchTerm, IsRegexSelected, Settings.ExtractForensic);
            RegexValidationMessage = result.validationMessage;
            DisplayInvalidRegexWarning = !result.isRegexValid;
        }

        private bool _isMaskSelected;

        public bool IsMaskSelected
        {
            get => _isMaskSelected;
            set
            {
                this.RaiseAndSetIfChanged(ref _isMaskSelected, value);
                if (value)
                {
                    Settings.ExtractType = ConfigSettingsConstants.ExtractFlagMaskToRegex;
                    IsRegexSelected = false;
                }
                CheckRegex();
            }
        }

        private bool _isRegexSelected;

        public bool IsRegexSelected
        {
            get => _isRegexSelected;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRegexSelected, value);
                if (value)
                {
                    Settings.ExtractType = ConfigSettingsConstants.ExtractTypeRegex;
                    IsMaskSelected = false;
                }
                CheckRegex();
            }
        }

        public bool IsForensicModeSelected
        {
            get => Settings.ExtractForensic;
            set
            {
                if (Settings.ExtractForensic != value)
                {
                    Settings.ExtractForensic = value;
                    this.RaisePropertyChanged(nameof(IsForensicModeSelected));

                    if (value)
                    {
                        Settings.ExtractMode = ExtractMode.AllFiles;
                        this.RaisePropertyChanged(nameof(IsAllFilesModeSelected));
                        this.RaisePropertyChanged(nameof(IsFixFilesModeSelected));
                    }

                    updateSearchTermVisibility();
                    CheckRegex();
                }
            }
        }

        public string SearchTerm
        {
            get => Settings.ExtractSearchTerm;
            set
            {
                Settings.ExtractSearchTerm = value;
                this.RaisePropertyChanged(nameof(SearchTerm));
                CheckRegex();
            }
        }

        private bool _isExtractionModeVisible;

        public bool IsExtractionModeVisible
        {
            get => _isExtractionModeVisible;
            set => this.RaiseAndSetIfChanged(ref _isExtractionModeVisible, value);
        }

        private bool _isSearchTermEnabled;

        public bool IsSearchTermEnabled
        {
            get => _isSearchTermEnabled;
            set => this.RaiseAndSetIfChanged(ref _isSearchTermEnabled, value);
        }

        private bool _isSearchPatternVisible;

        public bool IsSearchPatternVisible
        {
            get => _isSearchPatternVisible;
            set => this.RaiseAndSetIfChanged(ref _isSearchPatternVisible, value);
        }

        private void ensureValidDefaults() => _service.EnsureValidDefaults(Settings);
    }
}
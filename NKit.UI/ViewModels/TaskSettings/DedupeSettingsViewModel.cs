using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Nanook.NKit;
using Nanook.NKit.Ogmr;
using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NKit.Ui.ViewModels.TaskSettings;

public class DedupeSettingsViewModel : TaskSettingsViewModelBase
{
    private bool _isSystemChanging = false;
    private bool _isSavePending = false;

    // ==================== UI COLLECTIONS ====================

    public ObservableCollection<string> AvailableShardSizes { get; } = new();
    public ObservableCollection<string> AvailableBlockSizes { get; } = new();

    // ==================== CONSTANTS ====================

    private const string _DefaultShardSizeDisplay = "Default (50 GiB)";
    private const string _DefaultBlockSizeDisplay = "Default (64 KiB)";
    private const string _SingleFileDisplay = "Single File";

    public DedupeSettingsViewModel() : base(Locator.Current.GetService<NKitSettings>())
    {
        BrowseOgmrYamlCommand = ReactiveUI.ReactiveCommand.CreateFromTask(BrowseOgmrYaml);
        initializeCollections();
        MainWindowViewModel.SystemOrTaskUpdatedEvent += onSystemOrTaskChanged;
        Settings.PropertyChanged += onSettingsPropertyChanged;

        loadSettingsForCurrentSystem();
    }

    private void initializeCollections()
    {
        AvailableShardSizes.Add(_DefaultShardSizeDisplay);
        AvailableShardSizes.Add("10g");
        AvailableShardSizes.Add("25g");
        AvailableShardSizes.Add("50g");
        AvailableShardSizes.Add("100g");
        AvailableShardSizes.Add("200g");
        AvailableShardSizes.Add(_SingleFileDisplay);

        AvailableBlockSizes.Add(_DefaultBlockSizeDisplay);
        AvailableBlockSizes.Add("32k");
        AvailableBlockSizes.Add("64k");
        AvailableBlockSizes.Add("128k");
        AvailableBlockSizes.Add("256k");
        AvailableBlockSizes.Add("512k");
    }

    // ==================== EVENT HANDLERS ====================

    private void onSystemOrTaskChanged(object sender, SystemOrTaskChangedEventArgs e)
    {
        if (Settings.Task == TaskType.Dedupe)
            RefreshVerifySettings();

        if (e?.IsSystemChange == true)
        {
            _isSystemChanging = true;
            loadSettingsForCurrentSystem();
            _isSystemChanging = false;
        }
    }

    private void onSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isSystemChanging) return;

        switch (e.PropertyName)
        {
            case nameof(Settings.DedupeSetName):
            case nameof(Settings.DedupeShardSize):
            case nameof(Settings.DedupeBlockSize):
                saveSettings();
                break;
        }
    }

    private void loadSettingsForCurrentSystem()
    {
        YamlConfigurationStore configStore = Locator.Current.GetService<ISettingsStore>() as YamlConfigurationStore;
        NKitSettings persistedSettings = configStore?.LoadSettings(Settings.System, Settings.Task);

        if (persistedSettings != null)
            Settings.UpdateFromSettings(persistedSettings);

        notifyAllProperties();
        saveSettings();
    }

    private async void notifyAllProperties()
    {
        // Allow UI thread to process
        await Task.Delay(1);

        // Sync local fields from Settings model
        _auxMode = Settings.AuxMode;
        _ogmrYamlPath = Settings.OgmrYamlPath;

        // Update 1GMR status if a path is configured
        if (!string.IsNullOrWhiteSpace(_ogmrYamlPath))
        {
            try
            {
                if (File.Exists(_ogmrYamlPath))
                {
                    List<GameEntry> entries = OgmrYamlParser.Parse(_ogmrYamlPath);
                    OgmrStatus = $"Loaded {entries.Count} games";
                }
                else
                {
                    OgmrStatus = "File not found";
                }
            }
            catch (Exception ex)
            {
                OgmrStatus = ex.Message;
            }
        }
        else
        {
            OgmrStatus = string.Empty;
        }

        this.RaisePropertyChanged(nameof(SelectedShardSize));
        this.RaisePropertyChanged(nameof(SelectedBlockSize));
        this.RaisePropertyChanged(nameof(SetName));
        this.RaisePropertyChanged(nameof(AuxMode));
        this.RaisePropertyChanged(nameof(OgmrYamlPath));

        // Verify properties
        this.RaisePropertyChanged(nameof(IsDynamicVerifySelected));
        this.RaisePropertyChanged(nameof(IsDatLookupVerifySelected));
        this.RaisePropertyChanged(nameof(IsNoneVerifySelected));
    }

    private async void saveSettings()
    {
        if (_isSystemChanging || _isSavePending) return;

        _isSavePending = true;

        // Debounce saves
        await Task.Delay(1);

        try
        {
            YamlConfigurationStore configStore = Locator.Current.GetService<ISettingsStore>() as YamlConfigurationStore;
            configStore?.StoreSettings(Settings);
        }
        catch (Exception)
        {
            // Swallow save errors silently
        }
        finally
        {
            _isSavePending = false;
        }
    }

    // ==================== UI BINDING PROPERTIES ====================

    private bool _auxMode;
    public bool AuxMode
    {
        get => _auxMode;
        set
        {
            if (_isSystemChanging) return;
            this.RaiseAndSetIfChanged(ref _auxMode, value);
            Settings.AuxMode = value;
            saveSettings();
        }
    }

    private string _ogmrYamlPath;
    public string OgmrYamlPath
    {
        get => _ogmrYamlPath;
        set
        {
            if (_isSystemChanging) return;
            this.RaiseAndSetIfChanged(ref _ogmrYamlPath, value);
            Settings.OgmrYamlPath = value;
            saveSettings();
        }
    }

    private string _ogmrStatus;
    public string OgmrStatus
    {
        get => _ogmrStatus;
        set => this.RaiseAndSetIfChanged(ref _ogmrStatus, value);
    }

    public ReactiveUI.ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> BrowseOgmrYamlCommand { get; }

    private async Task BrowseOgmrYaml()
    {
        Window window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null)
            return;

        IStorageProvider storageProvider = window.StorageProvider;

        FilePickerFileType yamlFileType = new FilePickerFileType("YAML files") { Patterns = new[] { "*.yaml", "*.yml" } };

        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select 1GMR YAML File",
            AllowMultiple = false,
            FileTypeFilter = new[] { yamlFileType, FilePickerFileTypes.All },
            SuggestedStartLocation = !string.IsNullOrEmpty(_ogmrYamlPath) && File.Exists(_ogmrYamlPath)
                ? await storageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_ogmrYamlPath))
                : null
        });

        if (files == null || files.Count == 0)
            return;

        string selectedPath = files.First().Path.LocalPath;

        try
        {
            List<GameEntry> entries = OgmrYamlParser.Parse(selectedPath);
            OgmrYamlPath = selectedPath;
            OgmrStatus = $"Loaded {entries.Count} games";
        }
        catch (Exception ex)
        {
            OgmrStatus = ex.Message;
            OgmrYamlPath = null;
        }
    }

    public string SetName
    {
        get => Settings.DedupeSetName;
        set
        {
            if (_isSystemChanging) return;
            Settings.DedupeSetName = value ?? string.Empty;
        }
    }

    public string SelectedShardSize
    {
        get
        {
            if (_isSystemChanging) return null;
            string raw = Settings.DedupeShardSize;
            if (string.IsNullOrEmpty(raw)) return _DefaultShardSizeDisplay;
            if (raw == "0") return _SingleFileDisplay;
            return raw;
        }
        set
        {
            if (_isSystemChanging) return;
            if (value == _DefaultShardSizeDisplay || value == null)
                Settings.DedupeShardSize = string.Empty;
            else if (value == _SingleFileDisplay)
                Settings.DedupeShardSize = "0";
            else
                Settings.DedupeShardSize = value;
        }
    }

    public string SelectedBlockSize
    {
        get
        {
            if (_isSystemChanging) return null;
            string raw = Settings.DedupeBlockSize;
            if (string.IsNullOrEmpty(raw)) return _DefaultBlockSizeDisplay;
            return raw;
        }
        set
        {
            if (_isSystemChanging) return;
            if (value == _DefaultBlockSizeDisplay || value == null)
                Settings.DedupeBlockSize = string.Empty;
            else
                Settings.DedupeBlockSize = value;
        }
    }

    // ==================== VERIFY OVERRIDES (with save) ====================

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
                saveSettings();
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
                saveSettings();
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
                saveSettings();
            }
        }
    }
}
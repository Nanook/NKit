using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Helpers;
using NKit.Ui.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using LogLevel = Nanook.NKit.LogLevel;

namespace NKit.Ui.Models;

public class NKitSettings : ReactiveObject
{
    private readonly List<string> _in;

    // Core fields
    private SystemType _system;
    private TaskType _task;
    private bool _suppressPropertyChangeEvents = false;

    // Path fields
    private string _out;
    private string _scanIn;
    private string _scanOut;
    private string _tmp;
    private string _logOut;
    private string _resultsOut;
    private string _baseInPath;
    private string _fixInfo;
    private string _fixFiles;
    private string _dat;
    private string _keys;

    // Dat collection paths
    private string _redumpDatsPath;
    private string _noIntroDatsPath;
    private string _tosecDatsPath;

    // Dat components
    private string _datPath;
    private string _datArchiveMask;
    private string _datMask;
    private bool _updatingDat = false;

    // Keys components
    private string _keysPath;
    private string _keysMask;
    private bool _updatingKeys = false;

    // General settings
    private bool _r;
    private bool _arc;
    private Verify _v;
    private LogLevel _consoleLevel;
    private LogLevel _logOutLevel;
    private bool _log;
    private bool _results;
    private bool _skipIfCompleted;
    private bool _deleteProcessed;
    private bool _outAsDatMatch;

    // Conversion settings - all format strings generated on-demand from core components
    private string _convertSingleFormat;
    private string _convertIndexedFormat;
    private string _convertEncoding;
    private string _convertLevel;
    private string _convertBlockSize;
    private string _convertParallelism;
    private bool _convertLossless;
    private string _convertCueType;
    private string _convertBinary;
    private string _convertAudio;

    // Available options for UI binding
    private List<string> _availableLevels = new();
    private List<string> _availableBlockSizes = new();
    private List<string> _availableParallelisms = new();

    // Extraction settings
    private string _extractType;
    private bool _extractMatchCase;
    private bool _extractForensic;
    private string _extractSearchTerm;

    // Dedupe settings
    private string _dedupeSetName = string.Empty;
    private string _dedupeShardSize = string.Empty;
    private string _dedupeBlockSize = string.Empty;
    private bool _auxMode;
    private string _ogmrYamlPath;

    // Mode settings
    private ConvertMode _convertMode;
    private ExtractMode _extractMode;

    // Results/Log components (path + filename/mask)
    private string _resultsOutPathManual = string.Empty;
    private string _resultsOutMask = string.Empty;
    private string _logOutPathManual = string.Empty;
    private string _logOutMask = string.Empty;

    public NKitSettings()
    {
        _in = new List<string>();
    }

    // ======= Static Factory Methods =======

    public static NKitSettings GetDefaultSettings(SystemType system, TaskType task, NKitSettings currentSettings = null)
    {
        using ConfigurationManager configManager = new ConfigurationManager();
        CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(system);
        bool shouldExpandPaths = system != SystemType.NotSet && system != SystemType.Default;
        string systemString = system.ToString().ToLowerInvariant();

        NKitSettings settings = new NKitSettings
        {
            System = system,
            Task = task,
            R = uiDefaults.General.R,
            Arc = uiDefaults.General.Arc,
            BaseInPath = string.Empty,
            Out = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.Out, systemString) : uiDefaults.Paths.Out,
            Tmp = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.Tmp, systemString) : uiDefaults.Paths.Tmp,
            ScanOut = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.ScanOut, systemString) : uiDefaults.Paths.ScanOut,
            ScanIn = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.ScanIn, systemString) : uiDefaults.Paths.ScanIn,
            LogOut = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.LogOut, systemString) : uiDefaults.Paths.LogOut,
            ResultsOut = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.ResultsOut, systemString) : uiDefaults.Paths.ResultsOut,
            FixFiles = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.FixFiles, systemString) : uiDefaults.Paths.FixFiles,
            FixInfo = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.FixInfo, systemString) : uiDefaults.Paths.FixInfo,
            Keys = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.Keys, systemString) : uiDefaults.Paths.Keys,
            Dat = shouldExpandPaths ? configManager.ExpandConfigPathSystemOnly(uiDefaults.Paths.Dat, systemString) : uiDefaults.Paths.Dat,

            // Initialize dat collection paths
            RedumpDatsPath = string.Empty,
            NoIntroDatsPath = string.Empty,
            TosecDatsPath = string.Empty,

            // Initialize dat/keys component fields
            DatPath_Manual = string.Empty,
            DatArchiveMask = string.Empty,
            DatMask = "*.dat",
            KeysPath_Manual = string.Empty,
            KeysMask = string.Empty,

            // General settings
            Log = true,
            LogOutLevel = uiDefaults.General.LogOutLevel,
            ConsoleLevel = uiDefaults.General.ConsoleLevel,

            // Initialize task-specific verify settings with defaults
            V_Convert = GetVerifyForTask(TaskType.Convert),
            V_Scan = GetVerifyForTask(TaskType.Scan),
            V_Fix = GetVerifyForTask(TaskType.Fix),
            V_Verify = GetVerifyForTask(TaskType.Verify),
            V_Dedupe = GetVerifyForTask(TaskType.Dedupe),

            Results = uiDefaults.General.Results,
            DeleteProcessed = uiDefaults.General.DeleteProcessed,
            SkipIfCompleted = uiDefaults.General.SkipIfCompleted,
            OutAsDatMatch = uiDefaults.General.OutAsDatMatch,

            // Mode settings
            ConvertMode = ConvertMode.Direct,
            ExtractMode = ExtractMode.AllFiles,

            // Extraction settings
            ExtractType = uiDefaults.Extraction.Type,
            ExtractSearchTerm = uiDefaults.Extraction.SearchTerm,
            ExtractMatchCase = uiDefaults.Extraction.MatchCase,
            ExtractForensic = uiDefaults.Extraction.Forensic
        };

        // Initialize conversion settings through ConfigurationMappingService
        // Use bulk update mode to prevent excessive property change events
        settings.BeginBulkUpdate();
        try
        {
            // This populates all the individual conversion components from the default format string
            ConfigurationMappingService.MapFromConvertFormat(uiDefaults.Conversion.Format, settings, suppressEvents: true);
        }
        finally
        {
            settings.EndBulkUpdate();
        }

        // Apply current settings overrides if provided
        if (currentSettings != null)
        {
            settings.Arc = currentSettings.Arc;
            settings.R = currentSettings.R;
            settings.OutAsDatMatch = currentSettings.OutAsDatMatch;
            settings.Results = currentSettings.Results;
            settings.ResultsOut = currentSettings.ResultsOut;
            settings.Log = currentSettings.Log;
            settings.LogOut = currentSettings.LogOut;

            // Copy task-specific verify settings
            settings.V_Convert = currentSettings.V_Convert;
            settings.V_Scan = currentSettings.V_Scan;
            settings.V_Fix = currentSettings.V_Fix;
            settings.V_Verify = currentSettings.V_Verify;
            settings.V_Dedupe = currentSettings.V_Dedupe;

            settings.LogOutLevel = currentSettings.LogOutLevel;
            settings.ConsoleLevel = currentSettings.ConsoleLevel;
            settings.SkipIfCompleted = currentSettings.SkipIfCompleted;
        }

        return settings;
    }

    private static Verify GetVerifyForTask(TaskType task) => task switch
    {
        TaskType.Scan or TaskType.Convert or TaskType.Fix or TaskType.Dedupe or TaskType.Verify => Verify.Y,
        TaskType.FixExtract or TaskType.Extract => Verify.N,
        _ => Verify.Y
    };

    // ======= End Static Factory Methods =======

    // ======= System and Task Properties =======

    public SystemType System
    {
        get => _system;
        set
        {
            SystemType oldSystem = _system;
            this.RaiseAndSetIfChanged(ref _system, value);

            if (oldSystem != value && value != SystemType.NotSet && oldSystem != SystemType.NotSet)
            {
                ReExpandPathsForNewSystem(value);

                // CRITICAL: Reset conversion formats to new system defaults
                ResetConversionFormatsForNewSystem(value);
            }
        }
    }

    public TaskType Task
    {
        get => _task;
        set
        {
            TaskType oldTask = _task;
            this.RaiseAndSetIfChanged(ref _task, value);

            if (oldTask != value && value != TaskType.NotSet && oldTask != TaskType.NotSet && System != SystemType.NotSet)
                ReExpandPathsForNewSystem(System);
        }
    }

    // ======= End System and Task Properties =======

    // ======= Core Properties =======

    public List<string> In => _in;

    // ======= End Core Properties =======

    // ======= Path Properties =======

    public string Out
    {
        get => _out;
        set
        {
            this.RaiseAndSetIfChanged(ref _out, value);
            this.RaisePropertyChanged(nameof(OutPath));
        }
    }

    public string ScanIn
    {
        get => _scanIn;
        set
        {
            this.RaiseAndSetIfChanged(ref _scanIn, value);
            this.RaisePropertyChanged(nameof(ScanInPath));
        }
    }

    public string ScanOut
    {
        get => _scanOut;
        set
        {
            this.RaiseAndSetIfChanged(ref _scanOut, value);
            this.RaisePropertyChanged(nameof(ScanOutPath));
        }
    }

    public string Tmp
    {
        get => _tmp;
        set
        {
            this.RaiseAndSetIfChanged(ref _tmp, value);
            this.RaisePropertyChanged(nameof(TmpPath));
        }
    }

    public string BaseInPath
    {
        get => _baseInPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _baseInPath, value);
            this.RaisePropertyChanged(nameof(BaseInPath_Display));
        }
    }

    public string FixInfo
    {
        get => _fixInfo;
        set
        {
            this.RaiseAndSetIfChanged(ref _fixInfo, value);
            this.RaisePropertyChanged(nameof(FixInfoPath));
        }
    }

    public string FixFiles
    {
        get => _fixFiles;
        set
        {
            this.RaiseAndSetIfChanged(ref _fixFiles, value);
            this.RaisePropertyChanged(nameof(FixFilesPath));
        }
    }

    // ======= End Path Properties =======

    // ======= Display Path Properties =======

    public string OutPath
    {
        get => PathsHelper.ToDisplayPath(Out);
        set => Out = PathsHelper.ToAbsolutePath(value);
    }

    public string ScanInPath
    {
        get => PathsHelper.ToDisplayPath(ScanIn);
        set => ScanIn = PathsHelper.ToAbsolutePath(value);
    }

    public string ScanOutPath
    {
        get => PathsHelper.ToDisplayPath(ScanOut);
        set => ScanOut = PathsHelper.ToAbsolutePath(value);
    }

    public string TmpPath
    {
        get => PathsHelper.ToDisplayPath(Tmp);
        set => Tmp = PathsHelper.ToAbsolutePath(value);
    }

    public string LogOutPath
    {
        get => PathsHelper.ToDisplayPath(LogOut);
        set => LogOut = PathsHelper.ToAbsolutePath(value);
    }

    public string ResultsOutPath
    {
        get => PathsHelper.ToDisplayPath(ResultsOut);
        set => ResultsOut = PathsHelper.ToAbsolutePath(value);
    }

    public string BaseInPath_Display
    {
        get => PathsHelper.ToDisplayPath(BaseInPath);
        set => BaseInPath = PathsHelper.ToAbsolutePath(value);
    }

    public string FixInfoPath
    {
        get => PathsHelper.ToDisplayPath(FixInfo);
        set => FixInfo = PathsHelper.ToAbsolutePath(value);
    }

    public string FixFilesPath
    {
        get => PathsHelper.ToDisplayPath(FixFiles);
        set => FixFiles = PathsHelper.ToAbsolutePath(value);
    }

    // ======= End Display Path Properties =======

    // ======= DAT Properties =======

    public string Dat
    {
        get => _dat;
        set
        {
            this.RaiseAndSetIfChanged(ref _dat, value);
            this.RaisePropertyChanged(nameof(DatPath));
            ParseDatIntoComponents();
        }
    }

    public string DatPath_Manual
    {
        get => _datPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _datPath, value);
            this.RaisePropertyChanged(nameof(DatPath_Manual_Display));
            UpdateDatFromComponents();
        }
    }

    public string DatArchiveMask
    {
        get => _datArchiveMask;
        set
        {
            this.RaiseAndSetIfChanged(ref _datArchiveMask, value);
            UpdateDatFromComponents();
        }
    }

    public string DatMask
    {
        get => _datMask;
        set
        {
            this.RaiseAndSetIfChanged(ref _datMask, value);
            UpdateDatFromComponents();
        }
    }

    public string RedumpDatsPath
    {
        get => _redumpDatsPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _redumpDatsPath, value);
            this.RaisePropertyChanged(nameof(RedumpDatsPath_Display));
        }
    }

    public string NoIntroDatsPath
    {
        get => _noIntroDatsPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _noIntroDatsPath, value);
            this.RaisePropertyChanged(nameof(NoIntroDatsPath_Display));
        }
    }

    public string TosecDatsPath
    {
        get => _tosecDatsPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _tosecDatsPath, value);
            this.RaisePropertyChanged(nameof(TosecDatsPath_Display));
        }
    }

    public string DatPath
    {
        get => PathsHelper.ToDisplayPath(Dat);
        set => Dat = PathsHelper.ToAbsolutePath(value);
    }

    public string DatPath_Manual_Display
    {
        get => PathsHelper.ToDisplayPath(DatPath_Manual);
        set => DatPath_Manual = PathsHelper.ToAbsolutePath(value);
    }

    public string RedumpDatsPath_Display
    {
        get => PathsHelper.ToDisplayPath(RedumpDatsPath);
        set => RedumpDatsPath = PathsHelper.ToAbsolutePath(value);
    }

    public string NoIntroDatsPath_Display
    {
        get => PathsHelper.ToDisplayPath(NoIntroDatsPath);
        set => NoIntroDatsPath = PathsHelper.ToAbsolutePath(value);
    }

    public string TosecDatsPath_Display
    {
        get => PathsHelper.ToDisplayPath(TosecDatsPath);
        set => TosecDatsPath = PathsHelper.ToAbsolutePath(value);
    }

    // ======= End DAT Properties =======

    // ======= Keys Properties =======

    public string Keys
    {
        get => _keys;
        set
        {
            this.RaiseAndSetIfChanged(ref _keys, value);
            this.RaisePropertyChanged(nameof(KeysPath));
            ParseKeysIntoComponents();
        }
    }

    public string KeysPath_Manual
    {
        get => _keysPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _keysPath, value);
            this.RaisePropertyChanged(nameof(KeysPath_Manual_Display));
            UpdateKeysFromComponents();
        }
    }

    public string KeysMask
    {
        get => _keysMask;
        set
        {
            this.RaiseAndSetIfChanged(ref _keysMask, value);
            UpdateKeysFromComponents();
        }
    }

    public string KeysPath
    {
        get => PathsHelper.ToDisplayPath(Keys);
        set => Keys = PathsHelper.ToAbsolutePath(value);
    }

    public string KeysPath_Manual_Display
    {
        get => PathsHelper.ToDisplayPath(KeysPath_Manual);
        set => KeysPath_Manual = PathsHelper.ToAbsolutePath(value);
    }

    // ======= End Keys Properties =======

    // ======= General Settings =======

    public bool R
    {
        get => _r;
        set => this.RaiseAndSetIfChanged(ref _r, value);
    }

    public bool Arc
    {
        get => _arc;
        set => this.RaiseAndSetIfChanged(ref _arc, value);
    }

    // Task-specific verify fields
    private Verify _vConvert;
    private Verify _vScan;
    private Verify _vFix;
    private Verify _vVerify;
    private Verify _vDedupe;

    /// <summary>
    /// Gets or sets the verify setting for the current task.
    /// This automatically maps to the appropriate task-specific verify setting.
    /// </summary>
    public Verify V
    {
        get => GetVerifyForCurrentTask();
        set => SetVerifyForCurrentTask(value);
    }

    /// <summary>
    /// Task-specific verify setting for Convert task
    /// </summary>
    public Verify V_Convert
    {
        get => _vConvert;
        set => this.RaiseAndSetIfChanged(ref _vConvert, value);
    }

    /// <summary>
    /// Task-specific verify setting for Scan task
    /// </summary>
    public Verify V_Scan
    {
        get => _vScan;
        set => this.RaiseAndSetIfChanged(ref _vScan, value);
    }

    /// <summary>
    /// Task-specific verify setting for Fix task
    /// </summary>
    public Verify V_Fix
    {
        get => _vFix;
        set => this.RaiseAndSetIfChanged(ref _vFix, value);
    }

    /// <summary>
    /// Task-specific verify setting for Verify task
    /// </summary>
    public Verify V_Verify
    {
        get => _vVerify;
        set => this.RaiseAndSetIfChanged(ref _vVerify, value);
    }

    /// <summary>
    /// Task-specific verify setting for Dedupe task
    /// </summary>
    public Verify V_Dedupe
    {
        get => _vDedupe;
        set => this.RaiseAndSetIfChanged(ref _vDedupe, value);
    }

    /// <summary>
    /// Gets the verify setting for the current task
    /// Note: Expand task is not supported in the UI.
    /// </summary>
    private Verify GetVerifyForCurrentTask()
    {
        return Task switch
        {
            TaskType.Convert => V_Convert,
            TaskType.Scan => V_Scan,
            TaskType.Fix => V_Fix,
            TaskType.Verify => V_Verify,
            TaskType.Dedupe => V_Dedupe,
            TaskType.Extract => Verify.N, // Extract tasks typically don't use verify
            TaskType.FixExtract => Verify.N, // FixExtract tasks typically don't use verify
            _ => GetVerifyForTask(Task) // Fallback to the original logic for other tasks
        };
    }

    /// <summary>
    /// Sets the verify setting for the current task
    /// Note: Expand task is not supported in the UI.
    /// </summary>
    private void SetVerifyForCurrentTask(Verify value)
    {
        switch (Task)
        {
            case TaskType.Convert:
                V_Convert = value;
                break;
            case TaskType.Scan:
                V_Scan = value;
                break;
            case TaskType.Fix:
                V_Fix = value;
                break;
            case TaskType.Verify:
                V_Verify = value;
                break;
            case TaskType.Dedupe:
                V_Dedupe = value;
                break;
            case TaskType.Extract:
            case TaskType.FixExtract:
                // These tasks typically don't use verify, but store anyway
                _v = value;
                break;
            default:
                _v = value; // Fallback for other unsupported tasks
                break;
        }
    }

    public LogLevel ConsoleLevel
    {
        get => _consoleLevel;
        set => this.RaiseAndSetIfChanged(ref _consoleLevel, value);
    }

    public LogLevel LogOutLevel
    {
        get => _logOutLevel;
        set => this.RaiseAndSetIfChanged(ref _logOutLevel, value);
    }

    public bool Log
    {
        get => _log;
        set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    public bool Results
    {
        get => _results;
        set => this.RaiseAndSetIfChanged(ref _results, value);
    }

    public bool OutAsDatMatch
    {
        get => _outAsDatMatch;
        set => this.RaiseAndSetIfChanged(ref _outAsDatMatch, value);
    }

    public bool DeleteProcessed
    {
        get => _deleteProcessed;
        set => this.RaiseAndSetIfChanged(ref _deleteProcessed, value);
    }

    public bool SkipIfCompleted
    {
        get => _skipIfCompleted;
        set => this.RaiseAndSetIfChanged(ref _skipIfCompleted, value);
    }

    // ======= End General Settings =======

    // ======= Conversion Properties =======

    public string ConvertSingleFormat
    {
        get => _convertSingleFormat;
        set
        {
            if (_convertSingleFormat != value)
            {
                string oldFormat = _convertSingleFormat;
                this.RaiseAndSetIfChanged(ref _convertSingleFormat, value);

                // Update available options and apply format-specific defaults if not during bulk update
                if (!_suppressPropertyChangeEvents && !string.IsNullOrEmpty(value) && oldFormat != value)
                {
                    // Update options first, then apply defaults that depend on those options
                    UpdateAvailableOptions();
                    ApplyDefaultsForFormat(value);
                }
            }
        }
    }

    public string ConvertIndexedFormat
    {
        get => _convertIndexedFormat;
        set => this.RaiseAndSetIfChanged(ref _convertIndexedFormat, value);
    }

    public string ConvertEncoding
    {
        get => _convertEncoding;
        set
        {
            if (_convertEncoding != value)
            {
                string oldEncoding = _convertEncoding;
                this.RaiseAndSetIfChanged(ref _convertEncoding, value);

                // Update available options and apply defaults if not during bulk update
                if (!_suppressPropertyChangeEvents && !string.IsNullOrEmpty(value) && oldEncoding != value)
                {
                    // Update options first, then apply defaults that depend on those options
                    UpdateAvailableOptions();
                    ApplyDefaultLevelForEncoding(value);
                }
            }
        }
    }

    public List<string> AvailableLevels => _availableLevels;

    public List<string> AvailableBlockSizes => _availableBlockSizes;

    public List<string> AvailableParallelisms => _availableParallelisms;

    public string ConvertLevel
    {
        get => _convertLevel;
        set => this.RaiseAndSetIfChanged(ref _convertLevel, value);
    }

    public string ConvertBlockSize
    {
        get => _convertBlockSize;
        set => this.RaiseAndSetIfChanged(ref _convertBlockSize, value);
    }

    public string ConvertParallelism
    {
        get => _convertParallelism;
        set => this.RaiseAndSetIfChanged(ref _convertParallelism, value);
    }

    public bool ConvertLossless
    {
        get => _convertLossless;
        set => this.RaiseAndSetIfChanged(ref _convertLossless, value);
    }

    public string ConvertCueType
    {
        get => _convertCueType;
        set => this.RaiseAndSetIfChanged(ref _convertCueType, value);
    }

    public string ConvertBinary
    {
        get => _convertBinary;
        set => this.RaiseAndSetIfChanged(ref _convertBinary, value);
    }

    public string ConvertAudio
    {
        get => _convertAudio;
        set => this.RaiseAndSetIfChanged(ref _convertAudio, value);
    }

    /// <summary>
    /// Gets or sets the complete format string for conversion tasks.
    /// Generated on-demand from individual format components or parsed into components when set.
    /// Uses the ConfigurationMappingService to handle all format string operations.
    /// </summary>
    public string Convert
    {
        get =>
            // Generate the complete format string from components
            ConfigurationMappingService.MapToConvertFormat(this);
        set
        {
            // During bulk updates (like loading from config), always parse to ensure all properties are set
            // Otherwise, avoid circular updates - only set if actually different
            bool shouldParse = _suppressPropertyChangeEvents; // Always parse during bulk updates
            if (!shouldParse)
            {
                string currentValue = ConfigurationMappingService.MapToConvertFormat(this);
                shouldParse = value != currentValue;
            }

            if (shouldParse)
            {
                // Debug.WriteLine($"[NKitSettings] Convert property SET: '{value}'");

                // Suppress events during parsing to avoid excessive notifications
                bool wasSupressed = _suppressPropertyChangeEvents;
                _suppressPropertyChangeEvents = true;

                try
                {
                    // Use ConfigurationMappingService to parse and populate all convert properties
                    ConfigurationMappingService.MapFromConvertFormat(value, this, suppressEvents: true);

                    // Update available options after parsing, even during bulk updates
                    UpdateAvailableOptions();
                }
                finally
                {
                    _suppressPropertyChangeEvents = wasSupressed;
                }

                // Notify all related format properties once at the end
                if (!_suppressPropertyChangeEvents)
                {
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(ConvertSingleFormat));
                    this.RaisePropertyChanged(nameof(ConvertIndexedFormat));
                }
            }
        }
    }

    /// <summary>
    /// Temporarily suppresses property change events during bulk updates
    /// </summary>
    public void BeginBulkUpdate() => _suppressPropertyChangeEvents = true;

    /// <summary>
    /// Re-enables property change events and raises a bulk notification
    /// </summary>
    public void EndBulkUpdate()
    {
        // Debug.WriteLine($"[NKitSettings] EndBulkUpdate - System: {System}, Format: '{ConvertSingleFormat}', Encoding: '{ConvertEncoding}', Level: '{ConvertLevel}'");
        // Debug.WriteLine($"[NKitSettings] EndBulkUpdate - AvailableLevels count: {_availableLevels.Count}, Items: [{string.Join(", ", _availableLevels)}]");

        _suppressPropertyChangeEvents = false;

        // Raise a bulk notification for conversion properties
        this.RaisePropertyChanged(nameof(Convert));
        this.RaisePropertyChanged(nameof(ConvertSingleFormat));
        this.RaisePropertyChanged(nameof(ConvertIndexedFormat));

        // Notify V property for verify settings
        this.RaisePropertyChanged(nameof(V));

        // Always notify available options after bulk update to ensure UI is synchronized
        // Debug.WriteLine($"[NKitSettings] EndBulkUpdate - Raising property change notifications for available options");
        this.RaisePropertyChanged(nameof(AvailableLevels));
        this.RaisePropertyChanged(nameof(AvailableBlockSizes));
        this.RaisePropertyChanged(nameof(AvailableParallelisms));

        // Debug.WriteLine($"[NKitSettings] EndBulkUpdate - Complete");
    }

    // ======= End Conversion Properties =======

    // ======= Extraction Properties =======

    public string ExtractType
    {
        get => _extractType;
        set => this.RaiseAndSetIfChanged(ref _extractType, value);
    }

    public bool ExtractMatchCase
    {
        get => _extractMatchCase;
        set => this.RaiseAndSetIfChanged(ref _extractMatchCase, value);
    }

    public bool ExtractForensic
    {
        get => _extractForensic;
        set => this.RaiseAndSetIfChanged(ref _extractForensic, value);
    }

    public string ExtractSearchTerm
    {
        get => _extractSearchTerm;
        set => this.RaiseAndSetIfChanged(ref _extractSearchTerm, value);
    }

    /// <summary>
    /// Gets the proper format string for extraction tasks.
    /// Uses the centralized ConfigSettingsFormatGenerator to build the correct format strings.
    /// </summary>
    public string Extract
    {
        get
        {
            try
            {
                return ConfigSettingsFormatGenerator.GenerateExtractFormatString(
                    ExtractType ?? ConfigSettingsConstants.ExtractTypeMatch,
                    ExtractMatchCase,
                    ExtractForensic,
                    ExtractSearchTerm ?? "*");
            }
            catch
            {
                // Fallback to basic extract format
                return "m:*";
            }
        }
    }

    // ======= End Extraction Properties =======

    // ======= Dedupe Properties =======

    public string DedupeSetName
    {
        get => _dedupeSetName;
        set
        {
            this.RaiseAndSetIfChanged(ref _dedupeSetName, value ?? string.Empty);
            this.RaisePropertyChanged(nameof(Dedupe));
        }
    }

    public string DedupeShardSize
    {
        get => _dedupeShardSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _dedupeShardSize, value ?? string.Empty);
            this.RaisePropertyChanged(nameof(Dedupe));
        }
    }

    public string DedupeBlockSize
    {
        get => _dedupeBlockSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _dedupeBlockSize, value ?? string.Empty);
            this.RaisePropertyChanged(nameof(Dedupe));
        }
    }

    public bool AuxMode
    {
        get => _auxMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _auxMode, value);
            this.RaisePropertyChanged(nameof(Dedupe));
        }
    }

    public string OgmrYamlPath
    {
        get => _ogmrYamlPath;
        set => this.RaiseAndSetIfChanged(ref _ogmrYamlPath, value);
    }

    /// <summary>
    /// Assembled dedupe configuration string: setName:shardSize:blockSize:autoCreateAux
    /// Setting this parses components; getting this assembles them.
    /// </summary>
    public string Dedupe
    {
        get => ConfigSettingsFormatGenerator.GenerateDedupeFormatString(
                string.IsNullOrWhiteSpace(_dedupeSetName) ? null : _dedupeSetName,
                string.IsNullOrWhiteSpace(_dedupeShardSize) ? null : _dedupeShardSize,
                string.IsNullOrWhiteSpace(_dedupeBlockSize) ? null : _dedupeBlockSize,
                _auxMode ? "y" : null);
        set
        {
            DedupeConfiguration config = ConfigSettingsFormatParser.ParseDedupeConfiguration(value);

            bool wasSuppressed = _suppressPropertyChangeEvents;
            _suppressPropertyChangeEvents = true;
            try
            {
                DedupeSetName = config.SetName ?? string.Empty;
                DedupeShardSize = config.ShardSize == Nanook.NKit.Configuration.DedupeConfiguration.DefaultShardSize
                    ? string.Empty
                    : config.ShardSize == 0 ? "0" : FormatDedupeSize(config.ShardSize);
                DedupeBlockSize = config.BlockSize == 0
                    ? string.Empty
                    : FormatDedupeSize(config.BlockSize);
                _auxMode = config.AutoCreateAux;
            }
            finally
            {
                _suppressPropertyChangeEvents = wasSuppressed;
            }

            if (!_suppressPropertyChangeEvents)
            {
                this.RaisePropertyChanged(nameof(DedupeSetName));
                this.RaisePropertyChanged(nameof(DedupeShardSize));
                this.RaisePropertyChanged(nameof(DedupeBlockSize));
                this.RaisePropertyChanged(nameof(AuxMode));
                this.RaisePropertyChanged(nameof(Dedupe));
            }
        }
    }

    private static string FormatDedupeSize(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        if (bytes % (1024L * 1024L * 1024L) == 0) return $"{bytes / (1024L * 1024L * 1024L)}g";
        if (bytes % (1024L * 1024L) == 0) return $"{bytes / (1024L * 1024L)}m";
        if (bytes % 1024L == 0) return $"{bytes / 1024L}k";
        return bytes.ToString();
    }

    // ======= End Dedupe Properties =======

    // ======= Mode Properties =======

    public ConvertMode ConvertMode
    {
        get => _convertMode;
        set => this.RaiseAndSetIfChanged(ref _convertMode, value);
    }

    public ExtractMode ExtractMode
    {
        get => _extractMode;
        set => this.RaiseAndSetIfChanged(ref _extractMode, value);
    }

    // ======= End Mode Properties =======

    // ======= Update Methods =======

    public void UpdateFromSettings(NKitSettings source)
    {
        if (source == null)
            return;

        // Use bulk update mode to prevent excessive property change events
        BeginBulkUpdate();
        try
        {
            System = source.System;
            Out = source.Out;
            ScanIn = source.ScanIn;
            ScanOut = source.ScanOut;
            Tmp = source.Tmp;
            R = source.R;
            Arc = source.Arc;

            // Copy all task-specific verify settings
            V_Convert = source.V_Convert;
            V_Scan = source.V_Scan;
            V_Fix = source.V_Fix;
            V_Verify = source.V_Verify;
            V_Dedupe = source.V_Dedupe;

            ConsoleLevel = source.ConsoleLevel;
            LogOutLevel = source.LogOutLevel;
            Log = source.Log;
            LogOut = source.LogOut;
            Results = source.Results;
            ResultsOut = source.ResultsOut;
            BaseInPath = source.BaseInPath;
            FixInfo = source.FixInfo;
            FixFiles = source.FixFiles;
            Dat = source.Dat;
            Keys = source.Keys;

            // Preserve dat collections (they're global, not system-specific)
            RedumpDatsPath = source.RedumpDatsPath;
            NoIntroDatsPath = source.NoIntroDatsPath;
            TosecDatsPath = source.TosecDatsPath;

            KeysPath_Manual = source.KeysPath_Manual;
            KeysMask = source.KeysMask;

            // Copy conversion settings using the new approach
            Convert = source.Convert;  // This will parse and set all individual components automatically

            ExtractType = source.ExtractType;
            ExtractMatchCase = source.ExtractMatchCase;
            ExtractForensic = source.ExtractForensic;
            ExtractSearchTerm = source.ExtractSearchTerm;
            Dedupe = source.Dedupe;
            OgmrYamlPath = source.OgmrYamlPath;
            OutAsDatMatch = source.OutAsDatMatch;
            DeleteProcessed = source.DeleteProcessed;
            SkipIfCompleted = source.SkipIfCompleted;
            ConvertMode = source.ConvertMode;
            ExtractMode = source.ExtractMode;
        }
        finally
        {
            EndBulkUpdate();
        }
    }

    public SystemPresetSettings ToSystemPresetSettings() => new SystemPresetSettings
    {
        System = System,
        Out = Out,
        ScanIn = ScanIn,
        ScanOut = ScanOut,
        Tmp = Tmp,
        R = R,
        Arc = Arc,
        V = V, // This now automatically returns the task-specific verify setting
        ConsoleLevel = ConsoleLevel,
        LogOutLevel = LogOutLevel,
        LogOut = LogOut,
        Results = Results,
        ResultsOut = ResultsOut,
        BaseInPath = BaseInPath,
        FixInfo = FixInfo,
        FixFiles = FixFiles,
        Dat = Dat,
        Keys = Keys,
        RedumpDatsPath = RedumpDatsPath,
        NoIntroDatsPath = NoIntroDatsPath,
        TosecDatsPath = TosecDatsPath,
        Convert = Convert,
        Extract = Extract,
        Dedupe = Dedupe,
        OgmrYamlPath = OgmrYamlPath,
        OutAsDatMatch = OutAsDatMatch,
        DeleteProcessed = DeleteProcessed,
        SkipIfCompleted = SkipIfCompleted,
        Task = Task == TaskType.Extract && ExtractMode == ExtractMode.FixFiles ? TaskType.FixExtract : Task
    };

    // ======= End Update Methods =======

    // ======= Conversion Helper Methods =======

    private void ApplyDefaultsForFormat(string format)
    {
        if (string.IsNullOrEmpty(format) || System == SystemType.NotSet)
            return;

        try
        {
            // Get the default format string for this format and system
            string defaultFormatString = ConfigSettingsDefaults.GetFullDefaultFormat(System, format);
            if (!string.IsNullOrEmpty(defaultFormatString))
            {
                // Use ConfigurationMappingService to apply all the defaults
                ConfigurationMappingService.MapFromConvertFormat(defaultFormatString, this, suppressEvents: true);
            }
        }
        catch
        {
            // Ignore errors in default application
        }
    }

    private void UpdateAvailableOptions()
    {
        // Debug.WriteLine($"[NKitSettings] UpdateAvailableOptions - System: {System}, Format: '{ConvertSingleFormat}', Encoding: '{ConvertEncoding}', Level: '{ConvertLevel}', Suppress: {_suppressPropertyChangeEvents}");

        if (string.IsNullOrEmpty(ConvertSingleFormat) || System == SystemType.NotSet)
        {
            // Debug.WriteLine($"[NKitSettings] Clearing available options - invalid format or system");
            _availableLevels.Clear();
            _availableBlockSizes.Clear();
            _availableParallelisms.Clear();

            if (!_suppressPropertyChangeEvents)
            {
                this.RaisePropertyChanged(nameof(AvailableLevels));
                this.RaisePropertyChanged(nameof(AvailableBlockSizes));
                this.RaisePropertyChanged(nameof(AvailableParallelisms));
            }
            return;
        }

        try
        {
            RvzEncodingType enc = Enum.TryParse<RvzEncodingType>(ConvertEncoding, true, out RvzEncodingType e) ? e : RvzEncodingType.ZStd;
            // Debug.WriteLine($"[NKitSettings] Parsed encoding: {enc}");

            // Update available levels
            List<string> levels = ConfigSettingsRanges.GetCompressionLevels(ConvertSingleFormat, enc)
                .Where(l => l > 0)
                .Select(l => l.ToString())
                .ToList();
            // Debug.WriteLine($"[NKitSettings] Available levels for {ConvertSingleFormat}/{enc}: [{string.Join(", ", levels)}]");

            // Update available block sizes
            List<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(ConvertSingleFormat).ToList();
            // Debug.WriteLine($"[NKitSettings] Available block sizes: [{string.Join(", ", blockSizes)}]");

            // Update available parallelisms
            List<string> parallelisms = ConfigSettingsRanges.GetParallelismValues(System)
                .Select(p => p.ToString())
                .ToList();
            // Debug.WriteLine($"[NKitSettings] Available parallelisms: [{string.Join(", ", parallelisms)}]");

            // Only update if collections actually changed
            bool levelsChanged = !_availableLevels.SequenceEqual(levels);
            bool blockSizesChanged = !_availableBlockSizes.SequenceEqual(blockSizes);
            bool parallelismsChanged = !_availableParallelisms.SequenceEqual(parallelisms);

            // Debug.WriteLine($"[NKitSettings] Changes - Levels: {levelsChanged}, BlockSizes: {blockSizesChanged}, Parallelisms: {parallelismsChanged}");

            if (levelsChanged)
            {
                _availableLevels.Clear();
                _availableLevels.AddRange(levels);
                // Debug.WriteLine($"[NKitSettings] Updated AvailableLevels: [{string.Join(", ", _availableLevels)}]");
                if (!_suppressPropertyChangeEvents)
                    this.RaisePropertyChanged(nameof(AvailableLevels));
            }

            if (blockSizesChanged)
            {
                _availableBlockSizes.Clear();
                _availableBlockSizes.AddRange(blockSizes);
                if (!_suppressPropertyChangeEvents)
                    this.RaisePropertyChanged(nameof(AvailableBlockSizes));
            }

            if (parallelismsChanged)
            {
                _availableParallelisms.Clear();
                _availableParallelisms.AddRange(parallelisms);
                if (!_suppressPropertyChangeEvents)
                    this.RaisePropertyChanged(nameof(AvailableParallelisms));
            }
        }
        catch // (Exception ex)
        {
            // Debug.WriteLine($"[NKitSettings] Error in UpdateAvailableOptions: {ex.Message}");
        }
    }

    private void ApplyDefaultLevelForEncoding(string encoding)
    {
        // Debug.WriteLine($"[NKitSettings] ApplyDefaultLevelForEncoding - Encoding: '{encoding}', Current Level: '{ConvertLevel}'");

        if (string.IsNullOrEmpty(ConvertSingleFormat) || string.IsNullOrEmpty(encoding))
        {
            // Debug.WriteLine($"[NKitSettings] Skipping default level - missing format or encoding");
            return;
        }

        try
        {
            if (Enum.TryParse<RvzEncodingType>(encoding, true, out RvzEncodingType encodingType))
            {
                // Debug.WriteLine($"[NKitSettings] Parsed encoding type: {encodingType}");

                // For "none" encoding, clear the level since no compression is used
                if (encodingType == RvzEncodingType.None)
                {
                    // Debug.WriteLine($"[NKitSettings] Clearing level for 'none' encoding");
                    ConvertLevel = string.Empty;
                }
                else
                {
                    string defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(encodingType).ToString();
                    // Debug.WriteLine($"[NKitSettings] Default level for {encodingType}: '{defaultLevel}', Available levels: [{string.Join(", ", _availableLevels)}]");

                    // Only set if different and the level is available (options should be updated by now)
                    if (ConvertLevel != defaultLevel && _availableLevels.Contains(defaultLevel))
                    {
                        // Debug.WriteLine($"[NKitSettings] Setting level from '{ConvertLevel}' to '{defaultLevel}'");
                        ConvertLevel = defaultLevel;
                    }
                    else
                    {
                        // Debug.WriteLine($"[NKitSettings] Not changing level - same as current or not available");
                    }
                }
            }
        }
        catch // (Exception ex)
        {
            // Debug.WriteLine($"[NKitSettings] Error in ApplyDefaultLevelForEncoding: {ex.Message}");
        }
    }

    // ======= End Conversion Helper Methods =======

    // ======= Private Helper Methods =======

    private void ReExpandPathsForNewSystem(SystemType newSystem)
    {
        using ConfigurationManager configManager = new ConfigurationManager();
        string systemString = newSystem.ToString().ToLowerInvariant();

        if (!string.IsNullOrEmpty(Out) && Out.Contains("$"))
            Out = configManager.ExpandConfigPathSystemOnly(Out, systemString);

        if (!string.IsNullOrEmpty(ScanIn) && ScanIn.Contains("$"))
            ScanIn = configManager.ExpandConfigPathSystemOnly(ScanIn, systemString);

        if (!string.IsNullOrEmpty(ScanOut) && ScanOut.Contains("$"))
            ScanOut = configManager.ExpandConfigPathSystemOnly(ScanOut, systemString);

        if (!string.IsNullOrEmpty(Tmp) && Tmp.Contains("$"))
            Tmp = configManager.ExpandConfigPathSystemOnly(Tmp, systemString);

        if (!string.IsNullOrEmpty(LogOut) && LogOut.Contains("$"))
            LogOut = configManager.ExpandConfigPathSystemOnly(LogOut, systemString);

        if (!string.IsNullOrEmpty(ResultsOut) && ResultsOut.Contains("$"))
            ResultsOut = configManager.ExpandConfigPathSystemOnly(ResultsOut, systemString);

        if (!string.IsNullOrEmpty(BaseInPath) && BaseInPath.Contains("$"))
            BaseInPath = configManager.ExpandConfigPathSystemOnly(BaseInPath, systemString);

        if (!string.IsNullOrEmpty(FixInfo) && FixInfo.Contains("$"))
            FixInfo = configManager.ExpandConfigPathSystemOnly(FixInfo, systemString);

        if (!string.IsNullOrEmpty(FixFiles) && FixFiles.Contains("$"))
            FixFiles = configManager.ExpandConfigPathSystemOnly(FixFiles, systemString);

        if (!string.IsNullOrEmpty(Dat) && Dat.Contains("$"))
            Dat = configManager.ExpandConfigPathSystemOnly(Dat, systemString);

        if (!string.IsNullOrEmpty(Keys) && Keys.Contains("$"))
            Keys = configManager.ExpandConfigPathSystemOnly(Keys, systemString);

        if (!string.IsNullOrEmpty(RedumpDatsPath) && RedumpDatsPath.Contains("$"))
            RedumpDatsPath = configManager.ExpandConfigPathSystemOnly(RedumpDatsPath, systemString);

        if (!string.IsNullOrEmpty(NoIntroDatsPath) && NoIntroDatsPath.Contains("$"))
            NoIntroDatsPath = configManager.ExpandConfigPathSystemOnly(NoIntroDatsPath, systemString);

        if (!string.IsNullOrEmpty(TosecDatsPath) && TosecDatsPath.Contains("$"))
            TosecDatsPath = configManager.ExpandConfigPathSystemOnly(TosecDatsPath, systemString);

        if (!string.IsNullOrEmpty(KeysPath_Manual) && KeysPath_Manual.Contains("$"))
            KeysPath_Manual = configManager.ExpandConfigPathSystemOnly(KeysPath_Manual, systemString);
    }

    /// <summary>
    /// Resets conversion formats to the new system's defaults when system changes
    /// </summary>
    private void ResetConversionFormatsForNewSystem(SystemType newSystem)
    {
        try
        {
            using ConfigurationManager configManager = new ConfigurationManager();
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(newSystem);

            // Use bulk update to prevent excessive events
            BeginBulkUpdate();
            try
            {
                // Reset all conversion formats to new system defaults
                ConfigurationMappingService.MapFromConvertFormat(uiDefaults.Conversion.Format, this, suppressEvents: true);
            }
            finally
            {
                EndBulkUpdate();
            }
        }
        catch // (Exception ex)
        {
            // Debug.WriteLine($"Failed to reset conversion formats for system {newSystem}: {ex.Message}");
        }
    }

    private void UpdateDatFromComponents()
    {
        if (_updatingDat) return;

        if (string.IsNullOrEmpty(DatPath_Manual) || string.IsNullOrEmpty(DatMask))
        {
            _updatingDat = true;
            _dat = string.Empty;
            this.RaisePropertyChanged(nameof(Dat));
            this.RaisePropertyChanged(nameof(DatPath));
            _updatingDat = false;
            return;
        }

        string combinedDat;

        if (!string.IsNullOrEmpty(DatArchiveMask))
        {
            // Archive format: path\archivemask//mask (e.g., "c:\path\*.zip//*.dat")
            string pathSeparator = DatPath_Manual.Contains('\\') ? "\\" : "/";
            combinedDat = DatPath_Manual.TrimEnd('/', '\\') + pathSeparator + DatArchiveMask + "//" + DatMask;
        }
        else
        {
            // Simple format: path\mask (e.g., "c:\path\*.dat") 
            string pathSeparator = DatPath_Manual.Contains('\\') ? "\\" : "/";
            combinedDat = DatPath_Manual.TrimEnd('/', '\\') + pathSeparator + DatMask;
        }

        _updatingDat = true;
        _dat = combinedDat;
        this.RaisePropertyChanged(nameof(Dat));
        this.RaisePropertyChanged(nameof(DatPath));
        _updatingDat = false;
    }

    private void ParseDatIntoComponents()
    {
        if (_updatingDat) return;

        if (string.IsNullOrEmpty(Dat))
        {
            _datPath = string.Empty;
            _datArchiveMask = string.Empty;
            _datMask = string.Empty;
            this.RaisePropertyChanged(nameof(DatPath_Manual));
            this.RaisePropertyChanged(nameof(DatPath_Manual_Display));
            this.RaisePropertyChanged(nameof(DatArchiveMask));
            this.RaisePropertyChanged(nameof(DatMask));
            return;
        }

        // Check for archive separator "//" which indicates archive format
        int archiveIndex = Dat.IndexOf("//");
        if (archiveIndex != -1)
        {
            // Archive format: c:\path\*.zip//*.dat
            string beforeArchive = Dat.Substring(0, archiveIndex);
            _datMask = Dat.Substring(archiveIndex + 2);

            int lastSeparator = Math.Max(beforeArchive.LastIndexOf('/'), beforeArchive.LastIndexOf('\\'));

            if (lastSeparator != -1)
            {
                _datPath = beforeArchive.Substring(0, lastSeparator);
                _datArchiveMask = beforeArchive.Substring(lastSeparator + 1);
            }
            else
            {
                _datPath = string.Empty;
                _datArchiveMask = beforeArchive;
            }
        }
        else
        {
            // Simple format: c:\path\*.dat
            int lastSeparator = Math.Max(Dat.LastIndexOf('/'), Dat.LastIndexOf('\\'));

            if (lastSeparator != -1)
            {
                _datPath = Dat.Substring(0, lastSeparator);
                _datMask = Dat.Substring(lastSeparator + 1);
            }
            else
            {
                _datPath = string.Empty;
                _datMask = Dat;
            }
            _datArchiveMask = string.Empty;
        }

        this.RaisePropertyChanged(nameof(DatPath_Manual));
        this.RaisePropertyChanged(nameof(DatPath_Manual_Display));
        this.RaisePropertyChanged(nameof(DatArchiveMask));
        this.RaisePropertyChanged(nameof(DatMask));
    }

    private void UpdateKeysFromComponents()
    {
        if (_updatingKeys) return;

        if (string.IsNullOrEmpty(KeysPath_Manual))
        {
            _updatingKeys = true;
            _keys = string.Empty;
            this.RaisePropertyChanged(nameof(Keys));
            this.RaisePropertyChanged(nameof(KeysPath));
            _updatingKeys = false;
            return;
        }

        string combinedKeys;

        if (string.IsNullOrWhiteSpace(KeysMask))
        {
            // Folder mode: just the path
            combinedKeys = KeysPath_Manual.TrimEnd('/', '\\');
        }
        else
        {
            // File mask mode: path with mask
            string pathSeparator = KeysPath_Manual.Contains('\\') ? "\\" : "/";
            combinedKeys = KeysPath_Manual.TrimEnd('/', '\\') + pathSeparator + KeysMask;
        }

        _updatingKeys = true;
        _keys = combinedKeys;
        this.RaisePropertyChanged(nameof(Keys));
        this.RaisePropertyChanged(nameof(KeysPath));
        _updatingKeys = false;
    }

    private void ParseKeysIntoComponents()
    {
        if (_updatingKeys) return;

        if (string.IsNullOrEmpty(Keys))
        {
            _keysPath = string.Empty;
            _keysMask = string.Empty;
            this.RaisePropertyChanged(nameof(KeysPath_Manual));
            this.RaisePropertyChanged(nameof(KeysPath_Manual_Display));
            this.RaisePropertyChanged(nameof(KeysMask));
            return;
        }

        int lastSeparator = Keys.LastIndexOfAny(new char[] { '\\', '/' });

        if (lastSeparator != -1)
        {
            string potentialPath = Keys.Substring(0, lastSeparator);
            string potentialMask = Keys.Substring(lastSeparator + 1);

            // Decide whether the trailing part looks like a file/mask or a folder name.
            // If it contains wildcards or a dot in the filename extension (within last 5 chars) we treat it as a file/mask.
            bool hasWildcard = potentialMask.Contains('*') || potentialMask.Contains('?');
            int lastDot = potentialMask.LastIndexOf('.');
            bool hasDotNearEnd = lastDot != -1 && lastDot >= potentialMask.Length - 5;
            bool looksLikeMask = hasWildcard || hasDotNearEnd;

            if (looksLikeMask)
            {
                _keysPath = potentialPath;
                _keysMask = potentialMask;
            }
            else
            {
                // No mask and trailing token doesn't look like a filename -> treat full value as a folder path
                _keysPath = Keys;
                _keysMask = string.Empty;
            }
        }
        else
        {
            if (Keys.Contains('*') || Keys.Contains('?'))
            {
                _keysPath = string.Empty;
                _keysMask = Keys;
            }
            else
            {
                _keysPath = Keys;
                _keysMask = string.Empty;
            }
        }

        this.RaisePropertyChanged(nameof(KeysPath_Manual));
        this.RaisePropertyChanged(nameof(KeysPath_Manual_Display));
        this.RaisePropertyChanged(nameof(KeysMask));
    }

    // ======= Results/Log Properties =======

    public string ResultsOutPath_Manual
    {
        get => _resultsOutPathManual;
        set
        {
            this.RaiseAndSetIfChanged(ref _resultsOutPathManual, value);
            this.RaisePropertyChanged(nameof(ResultsOutPath_Manual_Display));
            UpdateResultsFromComponents();
        }
    }

    public string ResultsOutMask
    {
        get => _resultsOutMask;
        set
        {
            this.RaiseAndSetIfChanged(ref _resultsOutMask, value);
            UpdateResultsFromComponents();
        }
    }

    public string ResultsOutPath_Manual_Display
    {
        get => PathsHelper.ToDisplayPath(ResultsOutPath_Manual);
        set => ResultsOutPath_Manual = PathsHelper.ToAbsolutePath(value);
    }

    public string LogOutPath_Manual
    {
        get => _logOutPathManual;
        set
        {
            this.RaiseAndSetIfChanged(ref _logOutPathManual, value);
            this.RaisePropertyChanged(nameof(LogOutPath_Manual_Display));
            UpdateLogFromComponents();
        }
    }

    public string LogOutMask
    {
        get => _logOutMask;
        set
        {
            this.RaiseAndSetIfChanged(ref _logOutMask, value);
            UpdateLogFromComponents();
        }
    }

    public string LogOutPath_Manual_Display
    {
        get => PathsHelper.ToDisplayPath(LogOutPath_Manual);
        set => LogOutPath_Manual = PathsHelper.ToAbsolutePath(value);
    }

    // Ensure ResultsOut and LogOut parsing when set externally
    public string LogOut
    {
        get => _logOut;
        set
        {
            this.RaiseAndSetIfChanged(ref _logOut, value);
            this.RaisePropertyChanged(nameof(LogOutPath));
            // keep component fields in sync
            ParseLogIntoComponents();
        }
    }

    public string ResultsOut
    {
        get => _resultsOut;
        set
        {
            this.RaiseAndSetIfChanged(ref _resultsOut, value);
            this.RaisePropertyChanged(nameof(ResultsOutPath));
            // keep component fields in sync
            ParseResultsIntoComponents();
        }
    }

    // Compose ResultsOut from manual components
    private void UpdateResultsFromComponents()
    {
        if (string.IsNullOrEmpty(ResultsOutPath_Manual) && string.IsNullOrEmpty(ResultsOutMask))
            return;

        string combined;
        if (string.IsNullOrWhiteSpace(ResultsOutMask))
        {
            combined = ResultsOutPath_Manual?.TrimEnd('/', '\\');
        }
        else
        {
            string pathSeparator = ResultsOutPath_Manual?.Contains('\\') == true ? "\\" : "/";
            string path = (ResultsOutPath_Manual ?? string.Empty).TrimEnd('/', '\\');
            combined = string.IsNullOrEmpty(path) ? ResultsOutMask : path + pathSeparator + ResultsOutMask;
        }

        _resultsOut = combined;
        this.RaisePropertyChanged(nameof(ResultsOut));
        this.RaisePropertyChanged(nameof(ResultsOutPath));
    }

    private void ParseResultsIntoComponents()
    {
        if (string.IsNullOrEmpty(ResultsOut))
        {
            _resultsOutPathManual = string.Empty;
            _resultsOutMask = string.Empty;
            this.RaisePropertyChanged(nameof(ResultsOutPath_Manual));
            this.RaisePropertyChanged(nameof(ResultsOutPath_Manual_Display));
            this.RaisePropertyChanged(nameof(ResultsOutMask));
            return;
        }

        int lastSeparator = ResultsOut.LastIndexOfAny(new char[] { '\\', '/' });
        if (lastSeparator != -1)
        {
            _resultsOutPathManual = ResultsOut.Substring(0, lastSeparator);
            _resultsOutMask = ResultsOut.Substring(lastSeparator + 1);
        }
        else
        {
            _resultsOutPathManual = string.Empty;
            _resultsOutMask = ResultsOut;
        }

        this.RaisePropertyChanged(nameof(ResultsOutPath_Manual));
        this.RaisePropertyChanged(nameof(ResultsOutPath_Manual_Display));
        this.RaisePropertyChanged(nameof(ResultsOutMask));
    }

    // Compose LogOut from manual components
    private void UpdateLogFromComponents()
    {
        if (string.IsNullOrEmpty(LogOutPath_Manual) && string.IsNullOrEmpty(LogOutMask))
            return;

        string combined;
        if (string.IsNullOrWhiteSpace(LogOutMask))
        {
            combined = LogOutPath_Manual?.TrimEnd('/', '\\');
        }
        else
        {
            string pathSeparator = LogOutPath_Manual?.Contains('\\') == true ? "\\" : "/";
            string path = (LogOutPath_Manual ?? string.Empty).TrimEnd('/', '\\');
            combined = string.IsNullOrEmpty(path) ? LogOutMask : path + pathSeparator + LogOutMask;
        }

        _logOut = combined;
        this.RaisePropertyChanged(nameof(LogOut));
        this.RaisePropertyChanged(nameof(LogOutPath));
    }

    private void ParseLogIntoComponents()
    {
        if (string.IsNullOrEmpty(LogOut))
        {
            _logOutPathManual = string.Empty;
            _logOutMask = string.Empty;
            this.RaisePropertyChanged(nameof(LogOutPath_Manual));
            this.RaisePropertyChanged(nameof(LogOutPath_Manual_Display));
            this.RaisePropertyChanged(nameof(LogOutMask));
            return;
        }

        int lastSeparator = LogOut.LastIndexOfAny(new char[] { '\\', '/' });
        if (lastSeparator != -1)
        {
            _logOutPathManual = LogOut.Substring(0, lastSeparator);
            _logOutMask = LogOut.Substring(lastSeparator + 1);
        }
        else
        {
            _logOutPathManual = string.Empty;
            _logOutMask = LogOut;
        }

        this.RaisePropertyChanged(nameof(LogOutPath_Manual));
        this.RaisePropertyChanged(nameof(LogOutPath_Manual_Display));
        this.RaisePropertyChanged(nameof(LogOutMask));
    }

    // ======= End Private Helper Methods =======
}
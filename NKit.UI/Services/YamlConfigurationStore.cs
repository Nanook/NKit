using Nanook.NKit;
using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using Nanook.NKit.Configuration.Services;
using NKit.Ui.Helpers.Yaml;
using NKit.Ui.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace NKit.Ui.Services
{
    /// <summary>
    /// Provides centralized YAML-based configuration storage for the UI.
    /// Stores settings in a concise, hierarchical format similar to the CLI's nkit.yaml.
    /// Implements ISettingsStore for backward compatibility.
    /// </summary>
    public class YamlConfigurationStore : ISettingsStore
    {
        private readonly string _configFilePath;
        private readonly object _lock = new object();
        private UiConfiguration _cachedConfig;
        private UiSettings _uiSettings;

        public YamlConfigurationStore()
        {
            // Use the same configuration detection as the main ConfigurationManager
            string configDir = GetConfigurationDirectory();
            _configFilePath = Path.Combine(configDir, ConfigSettingsConstants.ConfigFileNameUI);
        }

        // ISettingsStore implementation for backward compatibility
        public float Version { get; set; } = 1.3f;
        public string LastFolderBrowsed { get; set; }
        public UiSettings UiSettings
        {
            get
            {
                // Return UI settings from the loaded configuration (global.ui section)
                // This ensures UI options are restored from the main config file
                if (_uiSettings == null)
                {
                    // Trigger loading of the configuration if not already loaded
                    UiConfiguration config = LoadOrCreateConfiguration();

                    // If still no UI settings after loading, create defaults
                    if (_uiSettings == null)
                    {
                        _uiSettings = UiSettings.GetDefaultSettings();

                        // Store the defaults in the configuration for next time
                        UpdateUiSettingsInConfiguration();
                    }

                    // Subscribe to property changes to auto-save when UI settings change
                    _uiSettings.PropertyChanged += OnUiSettingsChanged;
                }
                return _uiSettings;
            }
            set
            {
                // Unsubscribe from old instance if exists
                if (_uiSettings != null)
                {
                    _uiSettings.PropertyChanged -= OnUiSettingsChanged;
                }

                _uiSettings = value;

                // Subscribe to new instance
                if (_uiSettings != null)
                {
                    _uiSettings.PropertyChanged += OnUiSettingsChanged;
                }

                // Update the configuration when UI settings change
                UpdateUiSettingsInConfiguration();
            }
        }

        /// <summary>
        /// Event handler for UI settings property changes - automatically saves when settings are modified
        /// </summary>
        private void OnUiSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            // Auto-save UI settings when any property changes
            // System.Diagnostics.Debug.WriteLine($"UI setting changed: {e.PropertyName}");
            UpdateUiSettingsInConfiguration();

        /// <summary>
        /// Gets the persisted window decoration mode for Linux.
        /// Returns "csd" (client-side decorations) or "native" (OS title bar).
        /// </summary>
        public string GetWindowDecorationMode()
        {
            UiConfiguration config = LoadOrCreateConfiguration();
            return config?.Global?.Ui?.WindowDecorationMode ?? "csd";
        }

        /// <summary>
        /// Persists the window decoration mode for Linux.
        /// </summary>
        public void SetWindowDecorationMode(string mode)
        {
            lock (_lock)
            {
                UiConfiguration config = LoadOrCreateConfiguration();
                config.Global ??= new UiConfiguration.GlobalSettings();
                config.Global.Ui ??= new UiConfiguration.UiOnlySettings();
                config.Global.Ui.WindowDecorationMode = mode;
                SaveConfiguration(config);
                _cachedConfig = config;
            }
        }



        /// <summary>
        /// Gets the appropriate configuration directory using the same logic as ConfigurationManager
        /// </summary>
        private string GetConfigurationDirectory()
        {
            try
            {
                using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
                ConfigurationContext context = configManager.Context;
                return context.ConfigDirectory;
            }
            catch
            {
                // Fallback to executable directory
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        /// <summary>
        /// Stores NKitSettings using the restructured YAML format:
        /// - Processing settings go per-system with expanded paths
        /// - UI-only settings go in global section
        /// </summary>
        public void StoreSettings(NKitSettings settings)
        {
            if (settings?.System == SystemType.NotSet || settings?.Task == TaskType.NotSet)
                return;

            lock (_lock)
            {
                try
                {
                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] === StoreSettings START for {settings.System} ===");
                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] Input Convert: '{settings.Convert}'");
                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] Input ConvertLevel: '{settings.ConvertLevel}'");

                    // Load existing configuration or create new
                    UiConfiguration config = LoadOrCreateConfiguration();

                    if (config == null)
                    {
                        // System.Diagnostics.Debug.WriteLine("StoreSettings: LoadOrCreateConfiguration returned null, creating emergency default");
                        config = CreateDefaultConfiguration();
                    }

                    // Ensure paths are expanded for the current system before storing
                    ExpandPathsForCurrentSystem(settings);

                    // Check if this is a new system that needs defaults
                    string systemKey = settings.System.ToString().ToLowerInvariant();
                    bool systemExistedBefore = config.Systems?.ContainsKey(systemKey) == true;

                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] System '{systemKey}' existed before: {systemExistedBefore}")

                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] System '{systemKey}' existed before: {systemExistedBefore}");

                    // CRITICAL: Update user settings AFTER defaults (this ensures user settings take precedence)
                    UpdateSystemProcessingSettings(config, settings);
                    UpdateGlobalUiSettings(config, settings);

                    // CRITICAL: Check what's being stored after user settings are applied
                    if (config.Systems?.ContainsKey(systemKey) == true)
                    {
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] Final Convert in config: '{config.Systems[systemKey].Convert}'");
                    }

                    // Save to YAML
                    SaveConfiguration(config);

                    // Update cache
                    _cachedConfig = config;

                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] === StoreSettings COMPLETE for {settings.System} ===");
                }
                catch // (Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to store settings: {ex.Message}");
                    // System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Ensures all paths in settings are properly expanded for the current system before storage
        /// </summary>
        private void ExpandPathsForCurrentSystem(NKitSettings settings)
        {
            try
            {
                using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
                string systemString = settings.System.ToString().ToLowerInvariant();

                // Expand all paths that might contain variables
                if (!string.IsNullOrEmpty(settings.Out))
                    settings.Out = configManager.ExpandConfigPathSystemOnly(settings.Out, systemString);

                if (!string.IsNullOrEmpty(settings.ScanIn))
                    settings.ScanIn = configManager.ExpandConfigPathSystemOnly(settings.ScanIn, systemString);

                if (!string.IsNullOrEmpty(settings.ScanOut))
                    settings.ScanOut = configManager.ExpandConfigPathSystemOnly(settings.ScanOut, systemString);

                if (!string.IsNullOrEmpty(settings.Tmp))
                    settings.Tmp = configManager.ExpandConfigPathSystemOnly(settings.Tmp, systemString);

                // For LogOut and ResultsOut, expand only the directory portion and keep filename/mask intact
                if (!string.IsNullOrEmpty(settings.LogOut))
                {
                    settings.LogOut = ExpandSystemOnlyPathKeepFilename(settings.LogOut, configManager, systemString);
                }

                if (!string.IsNullOrEmpty(settings.ResultsOut))
                {
                    settings.ResultsOut = ExpandSystemOnlyPathKeepFilename(settings.ResultsOut, configManager, systemString);
                }

                if (!string.IsNullOrEmpty(settings.BaseInPath))
                    settings.BaseInPath = configManager.ExpandConfigPathSystemOnly(settings.BaseInPath, systemString);

                // IMPORTANT: Only expand FixInfo path if the system supports it
                if (ConfigSettingsDefaults.IsFixSupported(settings.System) && !string.IsNullOrEmpty(settings.FixInfo))
                    settings.FixInfo = configManager.ExpandConfigPathSystemOnly(settings.FixInfo, systemString);

                // IMPORTANT: Only expand FixFiles path if the system supports it
                if (ConfigSettingsDefaults.IsFixFilesSupported(settings.System) && !string.IsNullOrEmpty(settings.FixFiles))
                    settings.FixFiles = configManager.ExpandConfigPathSystemOnly(settings.FixFiles, systemString);

                if (!string.IsNullOrEmpty(settings.Dat))
                    settings.Dat = configManager.ExpandConfigPathSystemOnly(settings.Dat, systemString);

                // Only expand keys for WiiU and PS3 systems
                if ((settings.System == SystemType.WiiU || settings.System == SystemType.PS3) && !string.IsNullOrEmpty(settings.Keys))
                    settings.Keys = configManager.ExpandConfigPathSystemOnly(settings.Keys, systemString);

                if (!string.IsNullOrEmpty(settings.KeysPath_Manual) && settings.KeysPath_Manual.Contains("$"))
                    settings.KeysPath_Manual = configManager.ExpandConfigPathSystemOnly(settings.KeysPath_Manual, systemString);
            }
            catch // (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"Failed to expand paths for system {settings.System}: {ex.Message}");
                // Continue with unexpanded paths if expansion fails
            }
        }

        /// <summary>
        /// Expand path variables for stored values but preserve the filename/mask portion unexpanded.
        /// Uses ExpandConfigPathSystemOnly (system-only expansion) for storage phase.
        /// </summary>
        private string ExpandSystemOnlyPathKeepFilename(string value, ConfigurationManager configManager, string systemString)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            int lastSep = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            if (lastSep == -1)
            {
                // No directory part - do not expand filename
                return value;
            }

            string dirPart = value.Substring(0, lastSep);
            string filePart = value.Substring(lastSep + 1);

            string expandedDir = configManager.ExpandConfigPathSystemOnly(dirPart, systemString);

            // Choose separator from original
            char sep = value[lastSep];
            return expandedDir.TrimEnd('/', '\\') + sep + filePart;
        }

        /// <summary>
        /// Expand full path (task + system expansion) but keep filename/mask unexpanded.
        /// Used when loading settings for UI display.
        /// </summary>
        private string ExpandFullPathKeepFilename(string value, ConfigurationManager configManager, string taskString, string systemString)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            int lastSep = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            if (lastSep == -1)
            {
                // No directory part - do not expand filename
                return value;
            }

            string dirPart = value.Substring(0, lastSep);
            string filePart = value.Substring(lastSep + 1);

            string expandedDir = configManager.ExpandConfigPath(dirPart, taskString, systemString);

            char sep = value[lastSep];
            return expandedDir.TrimEnd('/', '\\') + sep + filePart;
        }

        /// <summary>
        /// Updates system-specific processing settings with expanded paths
        /// </summary>
        private void UpdateSystemProcessingSettings(UiConfiguration config, NKitSettings settings)
        {
            if (config == null)
            {
                // System.Diagnostics.Debug.WriteLine("UpdateSystemProcessingSettings: config is null");
                return;
            }

            string systemKey = settings.System.ToString().ToLowerInvariant();

            config.Systems ??= new Dictionary<string, UiConfiguration.SystemConfiguration>();

            if (!config.Systems.ContainsKey(systemKey))
            {
                config.Systems[systemKey] = new UiConfiguration.SystemConfiguration();
            }

            UiConfiguration.SystemConfiguration systemConfig = config.Systems[systemKey];

            // Task options - CRITICAL: Ensure the format string is appropriate for the target system
            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] === FORMAT STORAGE ANALYSIS ===");
            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Target System: {settings.System}");
            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Settings.Convert: '{settings.Convert}'");

            // CRITICAL FIX: Validate format appropriateness for the target system
            string convertFormatToStore = settings.Convert;

            // Check system capabilities
            bool targetSupportsIndex = ConfigSettingsDefaults.IsIndexedFormatSupported(settings.System);
            bool targetSupportsSingle = ConfigSettingsDefaults.IsSingleFormatSupported(settings.System);

            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Target system capabilities - Single: {targetSupportsSingle}, Indexed: {targetSupportsIndex}");

            // Fix incompatible format strings
            if (!string.IsNullOrEmpty(convertFormatToStore))
            {
                bool formatIsDual = convertFormatToStore.Contains('/');

                if (formatIsDual)
                {
                    // We have a dual format string like "iso/cue:split:bin:bin:sub"
                    string[] parts = convertFormatToStore.Split('/');
                    string singlePart = parts[0]?.Trim();
                    string indexedPart = parts[1]?.Trim();

                    if (!targetSupportsIndex && targetSupportsSingle)
                    {
                        // Target is single-only (like Xbox) - use only the single part
                        convertFormatToStore = singlePart;
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Fixed dual-format for single-only system: '{singlePart}'");
                    }
                    else if (targetSupportsIndex && !targetSupportsSingle)
                    {
                        // Target is index-only (like Dreamcast) - use only the indexed part  
                        convertFormatToStore = indexedPart;
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Fixed dual-format for index-only system: '{indexedPart}'");
                    }
                    // If target supports both, keep the dual format as-is
                }
                else
                {
                    // We have a single format string like "iso"
                    if (!targetSupportsSingle && targetSupportsIndex)
                    {
                        // Target is index-only but we have single format - need to add appropriate indexed format
                        string indexedDefault = ConfigSettingsDefaults.GetDefaultIndexedFormat(settings.System);
                        convertFormatToStore = $"{convertFormatToStore}/{indexedDefault}";
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Added indexed format for index-supporting system: '{convertFormatToStore}'");
                    }
                    else if (targetSupportsSingle && targetSupportsIndex)
                    {
                        // Target supports both but we only have single - add default indexed
                        string indexedDefault = ConfigSettingsDefaults.GetDefaultIndexedFormat(settings.System);
                        convertFormatToStore = $"{convertFormatToStore}/{indexedDefault}";
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Added default indexed format for dual-format system: '{convertFormatToStore}'");
                    }
                    // If target is single-only, keep single format as-is
                }

                // Final validation - ensure the format is valid for the target system
                ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(settings.System, convertFormatToStore);
                if (!validationResult.IsValid)
                {
                    // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Format validation failed: {validationResult.ErrorMessage}");
                    // Fall back to system default
                    convertFormatToStore = ConfigSettingsDefaults.GetFullDefaultFormat(settings.System);
                    // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Using system default format: '{convertFormatToStore}'");
                }
            }

            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Final format to store for {systemKey}: '{convertFormatToStore}'");
            systemConfig.Convert = convertFormatToStore;
            systemConfig.Extract = settings.Extract;

            // Process options - CRITICAL: Ensure the format string is appropriate for the target system
            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] === PROCESS OPTIONS STORAGE ANALYSIS ===");
            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Target System: {settings.System}");
            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Settings.Convert: '{settings.Convert}'");

            // CRITICAL FIX: Validate format appropriateness for the target system
            string convertFormatForProcessOptions = settings.Convert;

            // Check system capabilities
            bool targetSupportsIndexForProcessOptions = ConfigSettingsDefaults.IsIndexedFormatSupported(settings.System);
            bool targetSupportsSingleForProcessOptions = ConfigSettingsDefaults.IsSingleFormatSupported(settings.System);

            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Target system capabilities - Single: {targetSupportsSingleForProcessOptions}, Indexed: {targetSupportsIndexForProcessOptions}");

            // Fix incompatible format strings
            if (!string.IsNullOrEmpty(convertFormatForProcessOptions))
            {
                bool formatIsDual = convertFormatForProcessOptions.Contains('/');

                if (formatIsDual)
                {
                    // We have a dual format string like "iso/cue:split:bin:bin:sub"
                    string[] parts = convertFormatForProcessOptions.Split('/');
                    string singlePart = parts[0]?.Trim();
                    string indexedPart = parts[1]?.Trim();

                    if (!targetSupportsIndexForProcessOptions && targetSupportsSingleForProcessOptions)
                    {
                        // Target is single-only (like Xbox) - use only the single part
                        convertFormatForProcessOptions = singlePart;
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Fixed dual-format for single-only system: '{singlePart}'");
                    }
                    else if (targetSupportsIndexForProcessOptions && !targetSupportsSingleForProcessOptions)
                    {
                        // Target is index-only (like Dreamcast) - use only the indexed part  
                        convertFormatForProcessOptions = indexedPart;
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Fixed dual-format for index-only system: '{indexedPart}'");
                    }
                    // If target supports both, keep the dual format as-is
                }
                else
                {
                    // We have a single format string like "iso"
                    if (!targetSupportsSingleForProcessOptions && targetSupportsIndexForProcessOptions)
                    {
                        // Target is index-only but we have single format - need to add appropriate indexed format
                        string indexedDefault = ConfigSettingsDefaults.GetDefaultIndexedFormat(settings.System);
                        convertFormatForProcessOptions = $"{convertFormatForProcessOptions}/{indexedDefault}";
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Added indexed format for index-supporting system: '{convertFormatForProcessOptions}'");
                    }
                    else if (targetSupportsSingleForProcessOptions && targetSupportsIndexForProcessOptions)
                    {
                        // Target supports both but we only have single - add default indexed
                        string indexedDefault = ConfigSettingsDefaults.GetDefaultIndexedFormat(settings.System);
                        convertFormatForProcessOptions = $"{convertFormatForProcessOptions}/{indexedDefault}";
                        // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Added default indexed format for dual-format system: '{convertFormatForProcessOptions}'");
                    }
                    // If target is single-only, keep single format as-is
                }

                // Final validation - ensure the format is valid for the target system
                ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(settings.System, convertFormatForProcessOptions);
                if (!validationResult.IsValid)
                {
                    // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Process options format validation failed: {validationResult.ErrorMessage}");
                    // Fall back to system default
                    convertFormatForProcessOptions = ConfigSettingsDefaults.GetFullDefaultFormat(settings.System);
                    // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Using system default format for process options: '{convertFormatForProcessOptions}'");
                }
            }

            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Final process options format to store for {systemKey}: '{convertFormatForProcessOptions}'");
            systemConfig.Convert = convertFormatForProcessOptions;
            systemConfig.Extract = settings.Extract;
            systemConfig.Dedupe = settings.Dedupe;
            systemConfig.OgmrYamlPath = settings.OgmrYamlPath;

            // Process options - store both legacy V and task-specific verify settings
            systemConfig.V = settings.V.ToString().ToLowerInvariant();
            systemConfig.V_Convert = settings.V_Convert.ToString().ToLowerInvariant();
            systemConfig.V_Scan = settings.V_Scan.ToString().ToLowerInvariant();
            systemConfig.V_Fix = settings.V_Fix.ToString().ToLowerInvariant();
            systemConfig.V_Verify = settings.V_Verify.ToString().ToLowerInvariant();
            systemConfig.V_Dedupe = settings.V_Dedupe.ToString().ToLowerInvariant();

            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Storing V settings for {systemKey}:");
            // System.Diagnostics.Debug.WriteLine($"  V: '{settings.V}' -> '{systemConfig.V}'");
            // System.Diagnostics.Debug.WriteLine($"  V_Convert: '{settings.V_Convert}' -> '{systemConfig.V_Convert}'");
            // System.Diagnostics.Debug.WriteLine($"  V_Scan: '{settings.V_Scan}' -> '{systemConfig.V_Scan}'");
            // System.Diagnostics.Debug.WriteLine($"  V_Fix: '{settings.V_Fix}' -> '{systemConfig.V_Fix}'");
            // System.Diagnostics.Debug.WriteLine($"  V_Verify: '{settings.V_Verify}' -> '{systemConfig.V_Verify}'");
            // System.Diagnostics.Debug.WriteLine($"  V_Dedupe: '{settings.V_Dedupe}' -> '{systemConfig.V_Dedupe}'");

            systemConfig.R = settings.R;
            systemConfig.Arc = settings.Arc;
            systemConfig.Results = settings.Results;
            systemConfig.LogOutLevel = settings.LogOutLevel.ToString().ToLowerInvariant();
            systemConfig.DeleteProcessed = settings.DeleteProcessed;
            systemConfig.SkipIfCompleted = settings.SkipIfCompleted;
            systemConfig.OutAsDatMatch = settings.OutAsDatMatch;

            // UI state - preserve existing tab index if not specified
            // (SelectedTabIndex will be updated separately via StoreTabIndexForSystem method)

            // System paths (already expanded by ExpandPathsForCurrentSystem)
            systemConfig.Out = settings.Out;
            systemConfig.Tmp = settings.Tmp;
            systemConfig.ScanOut = settings.ScanOut;
            systemConfig.ScanIn = settings.ScanIn;
            systemConfig.LogOut = settings.LogOut;
            systemConfig.ResultsOut = settings.ResultsOut;
            systemConfig.BaseInPath = settings.BaseInPath;
            systemConfig.Dat = settings.Dat;

            // IMPORTANT: Only store FixInfo path if the system supports it
            if (ConfigSettingsDefaults.IsFixSupported(settings.System))
            {
                systemConfig.FixInfo = settings.FixInfo;
                // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Stored FixInfo path for {systemKey}: '{settings.FixInfo}'");
            }
            else
            {
                systemConfig.FixInfo = null; // Clear FixInfo for systems that don't support it
                // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Cleared FixInfo path for unsupported system: {systemKey}");
            }

            // IMPORTANT: Only store FixFiles path if the system supports it
            if (ConfigSettingsDefaults.IsFixFilesSupported(settings.System))
            {
                systemConfig.FixFiles = settings.FixFiles;
                // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Stored FixFiles path for {systemKey}: '{settings.FixFiles}'");
            }
            else
            {
                systemConfig.FixFiles = null; // Clear FixFiles for systems that don't support it
                // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] Cleared FixFiles path for unsupported system: {systemKey}");
            }

            // Only persist keys for systems that support keys
            if (ConfigSettingsDefaults.IsKeysSupported(settings.System))
            {
                systemConfig.Keys = settings.Keys;
            }
            else
            {
                systemConfig.Keys = null; // Clear keys for systems that don't support them
            }

            // System.Diagnostics.Debug.WriteLine($"[UpdateSystemProcessingSettings] === END FORMAT STORAGE ANALYSIS ===");
        }

        /// <summary>
        /// Updates global UI-level settings (not processing-related)
        /// </summary>
        private void UpdateGlobalUiSettings(UiConfiguration config, NKitSettings settings)
        {
            if (config == null)
            {
                // System.Diagnostics.Debug.WriteLine("UpdateGlobalUiSettings: config is null");
                return;
            }

            config.Global ??= new UiConfiguration.GlobalSettings();

            // Update UI-level settings in global (these apply across all systems)
            config.Global.ConsoleLevel = settings.ConsoleLevel.ToString().ToLowerInvariant();
            // NOTE: global "parallelism" is retired (worker count is autoscaled). The YAML field is
            // retained for back-compat deserialization but is no longer written from or read into the UI.

            // Persist last selected system and task so the UI can restore them on restart
            if (settings.System != SystemType.NotSet)
                config.Global.LastSystem = settings.System.ToString().ToLowerInvariant();
            if (settings.Task != TaskType.NotSet)
                config.Global.LastTask = settings.Task.ToString().ToLowerInvariant();

            // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings] Storing global UI settings:");
            // System.Diagnostics.Debug.WriteLine($"  ConsoleLevel: '{settings.ConsoleLevel}' -> '{config.Global.ConsoleLevel}'");

            // Update dat collections in dats structure (global for all systems)
            // IMPORTANT: Only update DAT collections if they have non-empty values to avoid overwriting existing paths
            config.Global.Dats ??= new UiConfiguration.DatCollections();

            if (!string.IsNullOrEmpty(settings.RedumpDatsPath))
            {
                config.Global.Dats.Redump = settings.RedumpDatsPath;
                // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings]   Updated RedumpDatsPath: '{settings.RedumpDatsPath}'");
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings]   Preserving existing RedumpDatsPath: '{config.Global.Dats.Redump}'");
            }

            if (!string.IsNullOrEmpty(settings.NoIntroDatsPath))
            {
                config.Global.Dats.NoIntro = settings.NoIntroDatsPath;
                // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings]   Updated NoIntroDatsPath: '{settings.NoIntroDatsPath}'");
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings]   Preserving existing NoIntroDatsPath: '{config.Global.Dats.NoIntro}'");
            }

            if (!string.IsNullOrEmpty(settings.TosecDatsPath))
            {
                config.Global.Dats.Tosec = settings.TosecDatsPath;
                // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings]   Updated TosecDatsPath: '{settings.TosecDatsPath}'");
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine($"[UpdateGlobalUiSettings]   Preserving existing TosecDatsPath: '{config.Global.Dats.Tosec}'");
            }

            // Integrate UiSettings into the global.ui section
            if (_uiSettings != null)
            {
                config.Global.Ui = new UiConfiguration.UiOnlySettings
                {
                    ShowQueued = _uiSettings.ShowQueued,
                    ShowSkipped = _uiSettings.ShowSkipped,
                    ShowProcessing = _uiSettings.ShowProcessing,
                    ShowCompleted = _uiSettings.ShowCompleted,
                    ShowFailed = _uiSettings.ShowFailed,
                    ShowCancelled = _uiSettings.ShowCancelled,
                    ConsoleOutputAutoScroll = _uiSettings.ConsoleOutputAutoScroll,
                    ConsoleOutputBuffer = _uiSettings.ConsoleOutputBuffer,
                    ConsoleOutputWrapText = _uiSettings.ConsoleOutputWrapText,
                    ReprocessCompletedFiles = _uiSettings.ReprocessCompletedFiles,
                    ReprocessFailedFiles = _uiSettings.ReprocessFailedFiles,
                    ReprocessSkippedFiles = _uiSettings.ReprocessSkippedFiles,
                    PersistFileQueue = _uiSettings.PersistFileQueue,
                    ShowTooltips = _uiSettings.ShowTooltips,
                };
            }
        }

        /// <summary>
        /// Loads NKitSettings for the specified system and task from system-specific section
        /// </summary>
        public NKitSettings LoadSettings(SystemType system, TaskType task)
        {
            lock (_lock)
            {
                try
                {
                    UiConfiguration config = LoadOrCreateConfiguration();

                    // Check if we actually found system-specific settings
                    string systemKey = system.ToString().ToLowerInvariant();
                    bool hasSystemConfig = config.Systems?.ContainsKey(systemKey) == true;

                    if (hasSystemConfig)
                    {
                        string convertFormat = config.Systems[systemKey].Convert;
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] LoadSettings DEBUG:");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   System: {system}");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Raw Convert from config: '{convertFormat}'");

                        // CRITICAL FIX: Validate format compatibility with target system
                        if (!string.IsNullOrEmpty(convertFormat))
                        {
                            bool systemSupportsIndex = ConfigSettingsDefaults.IsIndexedFormatSupported(system);
                            bool systemSupportsSingle = ConfigSettingsDefaults.IsSingleFormatSupported(system);
                            bool formatIsDual = convertFormat.Contains('/');

                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   System capabilities - Single: {systemSupportsSingle}, Indexed: {systemSupportsIndex}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Format is dual: {formatIsDual}");

                            // Fix incompatible stored formats
                            if (formatIsDual)
                            {
                                string[] parts = convertFormat.Split('/');
                                if (parts.Length == 2)
                                {
                                    if (!systemSupportsIndex && systemSupportsSingle)
                                    {
                                        // Single-only system with dual format - extract single part
                                        convertFormat = parts[0].Trim();
                                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Fixed dual format for single-only system: '{convertFormat}'");
                                    }
                                    else if (systemSupportsIndex && !systemSupportsSingle)
                                    {
                                        // Index-only system with dual format - extract indexed part
                                        convertFormat = parts[1].Trim();
                                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Fixed dual format for index-only system: '{convertFormat}'");
                                    }
                                    // Dual-format systems keep the dual format as-is
                                }
                            }


                            // Final validation
                            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(system, convertFormat);
                            if (!validationResult.IsValid)
                            {
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Format validation failed: {validationResult.ErrorMessage}");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Using system default format instead");
                                convertFormat = ConfigSettingsDefaults.GetFullDefaultFormat(system);
                            }
                        }

                        // Load settings from configuration
                        NKitSettings settings = NKitSettings.GetDefaultSettings(system, task);
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Default settings Convert: '{settings.Convert}'");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Default settings ConvertSingleFormat: '{settings.ConvertSingleFormat}'");

                        // Load global UI settings first
                        if (config.Global != null)
                        {
                            if (!string.IsNullOrEmpty(config.Global.ConsoleLevel) && Enum.TryParse<LogLevel>(config.Global.ConsoleLevel, true, out LogLevel consoleLevel))
                            {
                                settings.ConsoleLevel = consoleLevel;
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Loaded global ConsoleLevel: {consoleLevel}");
                            }


                            // Load DAT collections from global.dats section
                            if (config.Global.Dats != null)
                            {
                                if (!string.IsNullOrEmpty(config.Global.Dats.Redump))
                                {
                                    settings.RedumpDatsPath = config.Global.Dats.Redump;
                                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Loaded global RedumpDatsPath: {config.Global.Dats.Redump}");
                                }

                                if (!string.IsNullOrEmpty(config.Global.Dats.NoIntro))
                                {
                                    settings.NoIntroDatsPath = config.Global.Dats.NoIntro;
                                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Loaded global NoIntroDatsPath: {config.Global.Dats.NoIntro}");
                                }

                                if (!string.IsNullOrEmpty(config.Global.Dats.Tosec))
                                {
                                    settings.TosecDatsPath = config.Global.Dats.Tosec;
                                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Loaded global TosecDatsPath: {config.Global.Dats.Tosec}");
                                }
                            }
                        }

                        // Use NKitSettings encapsulation with bulk update to prevent smart defaults during loading
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Setting Convert property to: '{convertFormat}'");
                        settings.BeginBulkUpdate();
                        try
                        {
                            UiConfiguration.SystemConfiguration systemConfig = config.Systems[systemKey];

                            // Load conversion settings
                            settings.Convert = convertFormat;

                            // Load dedupe settings
                            if (!string.IsNullOrEmpty(systemConfig.Dedupe))
                                settings.Dedupe = systemConfig.Dedupe;

                            // Load 1GMR YAML path
                            if (!string.IsNullOrEmpty(systemConfig.OgmrYamlPath))
                                settings.OgmrYamlPath = systemConfig.OgmrYamlPath;

                            // Load extract settings � parse the stored format string back into components
                            if (!string.IsNullOrEmpty(systemConfig.Extract))
                            {
                                ExtractConfiguration extractConfig = ConfigSettingsFormatParser.ParseExtractConfiguration(systemConfig.Extract);
                                settings.ExtractForensic = extractConfig.IsForensic;
                                settings.ExtractMatchCase = !extractConfig.IsCaseInsensitive;
                                settings.ExtractSearchTerm = extractConfig.Pattern;
                                if (extractConfig.IsForensic)
                                    settings.ExtractType = ConfigSettingsConstants.ExtractFlagForensic;
                                else if (extractConfig.IsMaskToRegex)
                                    settings.ExtractType = ConfigSettingsConstants.ExtractFlagMaskToRegex;
                                else if (extractConfig.IsRecursive)
                                    settings.ExtractType = ConfigSettingsConstants.ExtractTypeRegex;
                            }

                            // Load system-specific process settings
                            settings.R = systemConfig.R;
                            settings.Arc = systemConfig.Arc;
                            settings.Results = systemConfig.Results;
                            settings.ResultsOut = systemConfig.ResultsOut ?? string.Empty;
                            settings.DeleteProcessed = systemConfig.DeleteProcessed;
                            settings.SkipIfCompleted = systemConfig.SkipIfCompleted;
                            settings.OutAsDatMatch = systemConfig.OutAsDatMatch;

                            if (!string.IsNullOrEmpty(systemConfig.LogOutLevel) && Enum.TryParse<LogLevel>(systemConfig.LogOutLevel, true, out LogLevel logOutLevel))
                            {
                                settings.LogOutLevel = logOutLevel;
                            }

                            // Load system paths and EXPAND ALL VARIABLES for UI display
                            try
                            {
                                using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
                                string systemString = system.ToString().ToLowerInvariant();
                                string taskString = task.ToString().ToLowerInvariant();

                                // Load and expand all paths with FULL variable expansion (including $date$, $task$, etc.)
                                if (!string.IsNullOrEmpty(systemConfig.Out))
                                    settings.Out = configManager.ExpandConfigPath(systemConfig.Out, taskString, systemString);
                                if (!string.IsNullOrEmpty(systemConfig.Tmp))
                                    settings.Tmp = configManager.ExpandConfigPath(systemConfig.Tmp, taskString, systemString);
                                if (!string.IsNullOrEmpty(systemConfig.ScanOut))
                                    settings.ScanOut = configManager.ExpandConfigPath(systemConfig.ScanOut, taskString, systemString);
                                if (!string.IsNullOrEmpty(systemConfig.ScanIn))
                                    settings.ScanIn = configManager.ExpandConfigPath(systemConfig.ScanIn, taskString, systemString);
                                // For LogOut and ResultsOut, expand only the directory portion and keep filename/mask intact
                                if (!string.IsNullOrEmpty(systemConfig.LogOut))
                                {
                                    settings.LogOut = ExpandFullPathKeepFilename(systemConfig.LogOut, configManager, taskString, systemString);
                                }

                                if (!string.IsNullOrEmpty(systemConfig.ResultsOut))
                                {
                                    settings.ResultsOut = ExpandFullPathKeepFilename(systemConfig.ResultsOut, configManager, taskString, systemString);
                                }

                                // Apply additional system paths that affect UI bindings: BaseInPath, FixInfo, FixFiles, Dat, Keys
                                if (!string.IsNullOrEmpty(systemConfig.BaseInPath))
                                    settings.BaseInPath = configManager.ExpandConfigPath(systemConfig.BaseInPath, taskString, systemString);

                                // Only apply FixInfo/FixFiles when supported for the current system
                                if (ConfigSettingsDefaults.IsFixSupported(system) && !string.IsNullOrEmpty(systemConfig.FixInfo))
                                    settings.FixInfo = configManager.ExpandConfigPath(systemConfig.FixInfo, taskString, systemString);

                                if (ConfigSettingsDefaults.IsFixFilesSupported(system) && !string.IsNullOrEmpty(systemConfig.FixFiles))
                                    settings.FixFiles = configManager.ExpandConfigPath(systemConfig.FixFiles, taskString, systemString);

                                // DAT path: assign raw stored value and parse components via DefaultPathProvider
                                if (!string.IsNullOrEmpty(systemConfig.Dat))
                                {
                                    settings.Dat = systemConfig.Dat;
                                    try
                                    {
                                        DefaultPathProvider parser = new DefaultPathProvider();
                                        DefaultPathProvider.DatWildcardComponents comp = parser.ParseDatWildcardPath(systemConfig.Dat);

                                        // Archive pattern (dir + archiveMask + inner file mask)
                                        if (!string.IsNullOrEmpty(comp.ArchiveMask) || !string.IsNullOrEmpty(comp.FileMask))
                                        {
                                            settings.DatPath_Manual = comp.Directory ?? string.Empty;
                                            settings.DatArchiveMask = comp.ArchiveMask ?? string.Empty;
                                            settings.DatMask = comp.FileMask ?? string.Empty;
                                        }
                                        else
                                        {
                                            // No archive detected. Determine whether value is a plain file-mask (e.g. C:\path\*.dat)
                                            string raw = systemConfig.Dat.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
                                            string last = Path.GetFileName(raw);
                                            if (!string.IsNullOrEmpty(last) && (last.Contains('*') || last.Contains('?')))
                                            {
                                                settings.DatMask = last;
                                                settings.DatPath_Manual = Path.GetDirectoryName(raw) ?? string.Empty;
                                                settings.DatArchiveMask = string.Empty;
                                            }
                                            else
                                            {
                                                // Treat as directory-only
                                                settings.DatPath_Manual = comp.Directory ?? raw;
                                                settings.DatArchiveMask = string.Empty;
                                                settings.DatMask = string.Empty;
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // ignore parse errors and fall back to raw assignment
                                    }
                                }

                                // Keys (supports <path> or <path>/<filemask>). Expand then populate components.
                                if (!string.IsNullOrEmpty(systemConfig.Keys))
                                {
                                    string expandedKeys = configManager.ExpandConfigPath(systemConfig.Keys, taskString, systemString);
                                    string rawKeys = expandedKeys.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();

                                    if (rawKeys.EndsWith(Path.DirectorySeparatorChar))
                                    {
                                        settings.KeysPath_Manual = rawKeys.TrimEnd(Path.DirectorySeparatorChar);
                                        settings.KeysMask = string.Empty;
                                    }
                                    else
                                    {
                                        settings.KeysPath_Manual = Path.GetDirectoryName(rawKeys) ?? string.Empty;
                                        settings.KeysMask = Path.GetFileName(rawKeys) ?? string.Empty;
                                    }

                                    settings.Keys = rawKeys;
                                }
                            }
                            catch // (Exception ex)
                            {
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Failed to expand paths: {ex.Message}");
                                // Fall back to unexpanded paths if expansion fails
                                if (!string.IsNullOrEmpty(systemConfig.Out))
                                    settings.Out = systemConfig.Out;
                                if (!string.IsNullOrEmpty(systemConfig.Tmp))
                                    settings.Tmp = systemConfig.Tmp;
                                if (!string.IsNullOrEmpty(systemConfig.ScanOut))
                                    settings.ScanOut = systemConfig.ScanOut;
                                if (!string.IsNullOrEmpty(systemConfig.ScanIn))
                                    settings.ScanIn = systemConfig.ScanIn;
                                if (!string.IsNullOrEmpty(systemConfig.LogOut))
                                    settings.LogOut = systemConfig.LogOut;
                                if (!string.IsNullOrEmpty(systemConfig.BaseInPath))
                                    settings.BaseInPath = systemConfig.BaseInPath;
                                if (ConfigSettingsDefaults.IsFixSupported(system) && !string.IsNullOrEmpty(systemConfig.FixInfo))
                                    settings.FixInfo = systemConfig.FixInfo;
                                if (ConfigSettingsDefaults.IsFixFilesSupported(system) && !string.IsNullOrEmpty(systemConfig.FixFiles))
                                    settings.FixFiles = systemConfig.FixFiles;
                                if (!string.IsNullOrEmpty(systemConfig.Dat))
                                    settings.Dat = systemConfig.Dat;
                                if (ConfigSettingsDefaults.IsKeysSupported(system) && !string.IsNullOrEmpty(systemConfig.Keys))
                                    settings.Keys = systemConfig.Keys;
                                if (!string.IsNullOrEmpty(systemConfig.ResultsOut))
                                    settings.ResultsOut = systemConfig.ResultsOut;
                            }

                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Loading system-specific settings:");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     Results: {systemConfig.Results} -> {settings.Results}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ResultsOut: '{systemConfig.ResultsOut}' -> '{settings.ResultsOut}'");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     R: {systemConfig.R} -> {settings.R}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     Arc: {systemConfig.Arc} -> {settings.Arc}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     DeleteProcessed: {systemConfig.DeleteProcessed} -> {settings.DeleteProcessed}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     SkipIfCompleted: {systemConfig.SkipIfCompleted} -> {settings.SkipIfCompleted}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     OutAsDatMatch: {systemConfig.OutAsDatMatch} -> {settings.OutAsDatMatch}");

                            // Load general verify setting (for backward compatibility)
                            // Note: We can't set the private _v field directly, so we'll rely on the task-specific settings

                            // Load task-specific verify settings (with fallback to general V)
                            if (!string.IsNullOrEmpty(systemConfig.V_Convert) && Enum.TryParse<Verify>(systemConfig.V_Convert, true, out Verify vConvert))
                            {
                                settings.V_Convert = vConvert;
                            }
                            else if (!string.IsNullOrEmpty(systemConfig.V) && Enum.TryParse<Verify>(systemConfig.V, true, out Verify fallbackConvert))
                            {
                                settings.V_Convert = fallbackConvert;
                            }

                            if (!string.IsNullOrEmpty(systemConfig.V_Scan) && Enum.TryParse<Verify>(systemConfig.V_Scan, true, out Verify vScan))
                            {
                                settings.V_Scan = vScan;
                            }
                            else if (!string.IsNullOrEmpty(systemConfig.V) && Enum.TryParse<Verify>(systemConfig.V, true, out Verify fallbackScan))
                            {
                                settings.V_Scan = fallbackScan;
                            }

                            if (!string.IsNullOrEmpty(systemConfig.V_Fix) && Enum.TryParse<Verify>(systemConfig.V_Fix, true, out Verify vFix))
                            {
                                settings.V_Fix = vFix;
                            }
                            else if (!string.IsNullOrEmpty(systemConfig.V) && Enum.TryParse<Verify>(systemConfig.V, true, out Verify fallbackFix))
                            {
                                settings.V_Fix = fallbackFix;
                            }

                            if (!string.IsNullOrEmpty(systemConfig.V_Verify) && Enum.TryParse<Verify>(systemConfig.V_Verify, true, out Verify vVerify))
                            {
                                settings.V_Verify = vVerify;
                            }
                            else if (!string.IsNullOrEmpty(systemConfig.V) && Enum.TryParse<Verify>(systemConfig.V, true, out Verify fallbackVerify))
                            {
                                settings.V_Verify = fallbackVerify;
                            }

                            if (!string.IsNullOrEmpty(systemConfig.V_Dedupe) && Enum.TryParse<Verify>(systemConfig.V_Dedupe, true, out Verify vDedupe))
                            {
                                settings.V_Dedupe = vDedupe;
                            }
                            else if (!string.IsNullOrEmpty(systemConfig.V) && Enum.TryParse<Verify>(systemConfig.V, true, out Verify fallbackDedupe))
                            {
                                settings.V_Dedupe = fallbackDedupe;
                            }

                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Successfully loaded task-specific verify settings:");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     V_Convert: {settings.V_Convert}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     V_Scan: {settings.V_Scan}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     V_Fix: {settings.V_Fix}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     V_Verify: {settings.V_Verify}");
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     V_Dedupe: {settings.V_Dedupe}");
                        }
                        finally
                        {
                            settings.EndBulkUpdate();
                        }

                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   After setting Convert property:");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     Convert: '{settings.Convert}'");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ConvertSingleFormat: '{settings.ConvertSingleFormat}'");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ConvertLevel: '{settings.ConvertLevel}'");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ConvertBlockSize: '{settings.ConvertBlockSize}'");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ConvertParallelism: '{settings.ConvertParallelism}'");
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Loaded persisted settings for {system}/{task}");

                        return settings;
                    }
                    else
                    {
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] No persisted settings found for {system}/{task}, applying defaults");

                        // CRITICAL: Apply default paths when no persisted configuration exists
                        try
                        {
                            using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
                            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(system);

                            // Create default settings and apply the UI defaults
                            NKitSettings settings = NKitSettings.GetDefaultSettings(system, task);

                            // IMPORTANT: Load existing global settings FIRST to preserve them
                            if (config.Global != null)
                            {
                                if (!string.IsNullOrEmpty(config.Global.ConsoleLevel) && Enum.TryParse<LogLevel>(config.Global.ConsoleLevel, true, out LogLevel consoleLevel))
                                {
                                    settings.ConsoleLevel = consoleLevel;
                                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Preserved global ConsoleLevel: {consoleLevel}");
                                }

                                // CRITICAL: Preserve existing DAT collection paths - don't reset them to empty
                                if (config.Global.Dats != null)
                                {
                                    if (!string.IsNullOrEmpty(config.Global.Dats.Redump))
                                    {
                                        settings.RedumpDatsPath = config.Global.Dats.Redump;
                                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Preserved global RedumpDatsPath: {config.Global.Dats.Redump}");
                                    }

                                    if (!string.IsNullOrEmpty(config.Global.Dats.NoIntro))
                                    {
                                        settings.NoIntroDatsPath = config.Global.Dats.NoIntro;
                                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Preserved global NoIntroDatsPath: {config.Global.Dats.NoIntro}");
                                    }

                                    if (!string.IsNullOrEmpty(config.Global.Dats.Tosec))
                                    {
                                        settings.TosecDatsPath = config.Global.Dats.Tosec;
                                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Preserved global TosecDatsPath: {config.Global.Dats.Tosec}");
                                    }
                                }
                            }

                            // Apply default paths from configuration manager (these will be properly expanded)
                            if (uiDefaults?.Paths != null)
                            {
                                string systemString = system.ToString().ToLowerInvariant();
                                string taskString = task.ToString().ToLowerInvariant();

                                // Set default paths and expand them
                                if (!string.IsNullOrEmpty(uiDefaults.Paths.Out))
                                    settings.Out = configManager.ExpandConfigPath(uiDefaults.Paths.Out, taskString, systemString);
                                if (!string.IsNullOrEmpty(uiDefaults.Paths.Tmp))
                                    settings.Tmp = configManager.ExpandConfigPath(uiDefaults.Paths.Tmp, taskString, systemString);
                                if (!string.IsNullOrEmpty(uiDefaults.Paths.ScanOut))
                                    settings.ScanOut = configManager.ExpandConfigPath(uiDefaults.Paths.ScanOut, taskString, systemString);
                                if (!string.IsNullOrEmpty(uiDefaults.Paths.ScanIn))
                                    settings.ScanIn = configManager.ExpandConfigPath(uiDefaults.Paths.ScanIn, taskString, systemString);
                                // Expand directory portion but keep filename/mask tokens (e.g. $system$, $task$) unexpanded
                                if (!string.IsNullOrEmpty(uiDefaults.Paths.LogOut))
                                    settings.LogOut = ExpandFullPathKeepFilename(uiDefaults.Paths.LogOut, configManager, taskString, systemString);
                                if (!string.IsNullOrEmpty(uiDefaults.Paths.ResultsOut))
                                    settings.ResultsOut = ExpandFullPathKeepFilename(uiDefaults.Paths.ResultsOut, configManager, taskString, systemString);

                                // IMPORTANT: Only set FixInfo path if not already preserved from global config
                                if (ConfigSettingsDefaults.IsFixSupported(system) && !string.IsNullOrEmpty(uiDefaults.Paths.FixInfo))
                                    settings.FixInfo = configManager.ExpandConfigPath(uiDefaults.Paths.FixInfo, taskString, systemString);

                                // IMPORTANT: Only set FixFiles path if not already preserved from global config
                                if (ConfigSettingsDefaults.IsFixFilesSupported(system) && !string.IsNullOrEmpty(uiDefaults.Paths.FixFiles))
                                    settings.FixFiles = configManager.ExpandConfigPath(uiDefaults.Paths.FixFiles, taskString, systemString);

                                // IMPORTANT: Only set DAT path if not already preserved from global config
                                if (string.IsNullOrEmpty(settings.Dat) && !string.IsNullOrEmpty(uiDefaults.Paths.Dat))
                                    settings.Dat = configManager.ExpandConfigPath(uiDefaults.Paths.Dat, taskString, systemString);

                                if (!string.IsNullOrEmpty(uiDefaults.Paths.Keys))
                                    settings.Keys = configManager.ExpandConfigPath(uiDefaults.Paths.Keys, taskString, systemString);

                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]   Applied default paths for {system}:");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     Out: '{settings.Out}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ScanOut: '{settings.ScanOut}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     ScanIn: '{settings.ScanIn}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     LogOut: '{settings.LogOut}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     FixInfo: '{settings.FixInfo}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     FixFiles: '{settings.FixFiles}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     Dat: '{settings.Dat}'");
                                // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore]     Keys: '{settings.Keys}'");
                            }

                            return settings;
                        }
                        catch //(Exception ex)
                        {
                            // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] Failed to apply default paths: {ex.Message}");
                            // Fall back to basic defaults if configuration manager fails
                            return NKitSettings.GetDefaultSettings(system, task);
                        }
                    }
                }
                catch // (Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to load settings for {system}/{task}: {ex.Message}");
                    return null;
                }
            }
        }

        // ISettingsStore interface implementations for backward compatibility
        public void WriteSettingsToDisk()
        {
            lock (_lock)
            {
                try
                {
                    // Always load or create configuration before saving
                    UiConfiguration config = _cachedConfig ?? LoadOrCreateConfiguration();

                    // System.Diagnostics.Debug.WriteLine($"WriteSettingsToDisk: Config is null: {config == null}");

                    if (config != null)
                    {
                        SaveConfiguration(config);
                        _cachedConfig = config;
                    }
                    else
                    {
                        // System.Diagnostics.Debug.WriteLine("WriteSettingsToDisk: No configuration to save");
                    }
                }
                catch //(Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to write settings to disk: {ex.Message}");
                    // System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        public void WriteFileQueueToDisk(ObservableCollection<SourceFileRecord> fileQueue)
        {
            try
            {
                string queuePath = Path.Combine(Path.GetDirectoryName(_configFilePath), "nkit-ui-queue.yaml");

                // Ensure directory exists
                string directory = Path.GetDirectoryName(queuePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use AOT-compatible emit-based serializer for file queue
                using StringWriter writer = new StringWriter();

                // Add header comment
                writer.WriteLine("# NKit UI File Queue");
                writer.WriteLine($"# Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine("# This file contains the persisted file processing queue");
                writer.WriteLine("# Files in this queue will be restored when the application restarts");
                writer.WriteLine();

                // Serialize the queue using emit-based serializer
                AotQueueSerializer.Serialize(writer, fileQueue);

                string yaml = writer.ToString();
                File.WriteAllText(queuePath, yaml);
            }
            catch // (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"Failed to write file queue: {ex.Message}");
                // System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public NKitSettings GetSettings(SystemType systemType, TaskType taskType) => LoadSettings(systemType, taskType);

        /// <summary>
        /// Reads the last selected system and task from the persisted global configuration.
        /// Returns (SystemType.NotSet, TaskType.NotSet) if nothing is persisted.
        /// </summary>
        public (SystemType system, TaskType task) GetLastUsedSystemAndTask()
        {
            try
            {
                UiConfiguration config = LoadOrCreateConfiguration();
                SystemType system = SystemType.NotSet;
                TaskType task = TaskType.NotSet;

                if (config?.Global != null)
                {
                    if (!string.IsNullOrEmpty(config.Global.LastSystem) &&
                        Enum.TryParse<SystemType>(config.Global.LastSystem, true, out SystemType parsedSystem))
                    {
                        system = parsedSystem;
                    }
                    if (!string.IsNullOrEmpty(config.Global.LastTask) &&
                        Enum.TryParse<TaskType>(config.Global.LastTask, true, out TaskType parsedTask))
                    {
                        task = parsedTask;
                    }
                }

                return (system, task);
            }
            catch
            {
                return (SystemType.NotSet, TaskType.NotSet);
            }
        }

        public NKitSettings GetLastUsedSettings(SystemType system) => LoadSettings(system, TaskType.Convert);

        /// <summary>
        /// Updates the UI settings in the cached configuration
        /// </summary>
        private void UpdateUiSettingsInConfiguration()
        {
            if (_uiSettings == null)
                return;

            try
            {
                UiConfiguration config = LoadOrCreateConfiguration();
                config.Global ??= new UiConfiguration.GlobalSettings();
                config.Global.Ui = new UiConfiguration.UiOnlySettings
                {
                    ShowQueued = _uiSettings.ShowQueued,
                    ShowSkipped = _uiSettings.ShowSkipped,
                    ShowProcessing = _uiSettings.ShowProcessing,
                    ShowCompleted = _uiSettings.ShowCompleted,
                    ShowFailed = _uiSettings.ShowFailed,
                    ShowCancelled = _uiSettings.ShowCancelled,
                    ConsoleOutputAutoScroll = _uiSettings.ConsoleOutputAutoScroll,
                    ConsoleOutputBuffer = _uiSettings.ConsoleOutputBuffer,
                    ConsoleOutputWrapText = _uiSettings.ConsoleOutputWrapText,
                    ReprocessCompletedFiles = _uiSettings.ReprocessCompletedFiles,
                    ReprocessFailedFiles = _uiSettings.ReprocessFailedFiles,
                    ReprocessSkippedFiles = _uiSettings.ReprocessSkippedFiles,
                    PersistFileQueue = _uiSettings.PersistFileQueue,
                    ShowTooltips = _uiSettings.ShowTooltips,
                };

                // Save the updated configuration
                SaveConfiguration(config);
                _cachedConfig = config;
            }
            catch //(Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"Failed to update UI settings in configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads existing configuration or creates a new one with defaults.
        /// Also loads UI settings from global.ui section if available.
        /// </summary>
        private UiConfiguration LoadOrCreateConfiguration()
        {
            // System.Diagnostics.Debug.WriteLine($"LoadOrCreateConfiguration: _cachedConfig is null: {_cachedConfig == null}");

            if (_cachedConfig != null)
                return _cachedConfig;

            // System.Diagnostics.Debug.WriteLine($"LoadOrCreateConfiguration: Config file exists: {File.Exists(_configFilePath)}");

            if (File.Exists(_configFilePath))
            {
                try
                {
                    string yaml = File.ReadAllText(_configFilePath);
                    // System.Diagnostics.Debug.WriteLine($"LoadOrCreateConfiguration: YAML content length: {yaml.Length}");

                    // Skip deserialization if file only contains header
                    if (yaml.Length < 200 || (!yaml.Contains("version:") && !yaml.Contains("global:") && !yaml.Contains("systems:")))
                    {
                        // System.Diagnostics.Debug.WriteLine("LoadOrCreateConfiguration: YAML file contains only header, creating new config");
                        _cachedConfig = CreateDefaultConfiguration();
                        return _cachedConfig;
                    }

                    _cachedConfig = YamlSerlialiserFactory.Deserialize(yaml);
                    // System.Diagnostics.Debug.WriteLine($"LoadOrCreateConfiguration: Deserialized config is null: {_cachedConfig == null}");

                    // Ensure we have a valid configuration even if deserialization returns null
                    if (_cachedConfig == null)
                    {
                        // System.Diagnostics.Debug.WriteLine("LoadOrCreateConfiguration: Deserialization returned null, creating default");
                        _cachedConfig = CreateDefaultConfiguration();
                        return _cachedConfig;
                    }

                    // Load UI settings from global.ui section if available
                    if (_cachedConfig?.Global?.Ui != null)
                    {
                        UiConfiguration.UiOnlySettings globalUi = _cachedConfig.Global.Ui;

                        // Create new UiSettings and populate from YAML
                        UiSettings loadedSettings = new UiSettings();

                        // Set all properties without triggering change notifications yet
                        loadedSettings.ShowQueued = globalUi.ShowQueued;
                        loadedSettings.ShowSkipped = globalUi.ShowSkipped;
                        loadedSettings.ShowProcessing = globalUi.ShowProcessing;
                        loadedSettings.ShowCompleted = globalUi.ShowCompleted;
                        loadedSettings.ShowFailed = globalUi.ShowFailed;
                        loadedSettings.ShowCancelled = globalUi.ShowCancelled;
                        loadedSettings.ConsoleOutputAutoScroll = globalUi.ConsoleOutputAutoScroll;
                        loadedSettings.ConsoleOutputBuffer = globalUi.ConsoleOutputBuffer;
                        loadedSettings.ConsoleOutputWrapText = globalUi.ConsoleOutputWrapText;
                        loadedSettings.ReprocessCompletedFiles = globalUi.ReprocessCompletedFiles;
                        loadedSettings.ReprocessFailedFiles = globalUi.ReprocessFailedFiles;
                        loadedSettings.ReprocessSkippedFiles = globalUi.ReprocessSkippedFiles;
                        loadedSettings.PersistFileQueue = globalUi.PersistFileQueue;
                        // If YAML explicitly contains the showTooltips key, use it; otherwise preserve UiSettings default (true)
                        bool yamlHasShowTooltips = !string.IsNullOrEmpty(yaml) && yaml.IndexOf("showTooltips", StringComparison.OrdinalIgnoreCase) >= 0;
                        loadedSettings.ShowTooltips = yamlHasShowTooltips ? globalUi.ShowTooltips : UiSettings.GetDefaultSettings().ShowTooltips;

                        // Now set the UI settings (this will set up property change subscription)
                        if (_uiSettings != null)
                        {
                            _uiSettings.PropertyChanged -= OnUiSettingsChanged;
                        }

                        _uiSettings = loadedSettings;
                        _uiSettings.PropertyChanged += OnUiSettingsChanged;

                        // System.Diagnostics.Debug.WriteLine("UI settings loaded from YAML configuration");
                    }
                    else
                    {
                        // No UI settings found in configuration, create defaults
                        if (_uiSettings == null)
                        {
                            _uiSettings = UiSettings.GetDefaultSettings();
                            _uiSettings.PropertyChanged += OnUiSettingsChanged;
                        }
                    }

                    return _cachedConfig;
                }
                catch //(Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to load configuration: {ex.Message}");
                    // System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    // Fall through to create default configuration
                }
            }

            // Create new configuration with defaults
            // System.Diagnostics.Debug.WriteLine("LoadOrCreateConfiguration: Creating new configuration with defaults");
            _cachedConfig = CreateDefaultConfiguration();

            // System.Diagnostics.Debug.WriteLine($"LoadOrCreateConfiguration: Returning config, is null: {_cachedConfig == null}");
            return _cachedConfig;
        }

        /// <summary>
        /// Creates a default configuration with proper initialization
        /// </summary>
        private UiConfiguration CreateDefaultConfiguration()
        {
            UiConfiguration config = new UiConfiguration
            {
                Version = "1.4",
                Global = new UiConfiguration.GlobalSettings
                {
                    ConsoleLevel = "info",
                    Dats = new UiConfiguration.DatCollections()
                },
                Systems = new Dictionary<string, UiConfiguration.SystemConfiguration>()
            };

            // Initialize UI settings with defaults if not already done
            if (_uiSettings == null)
            {
                _uiSettings = UiSettings.GetDefaultSettings();
                _uiSettings.PropertyChanged += OnUiSettingsChanged;
            }

            return config;
        }

        /// <summary>
        /// Saves configuration to YAML file
        /// </summary>
        private void SaveConfiguration(UiConfiguration config)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine($"SaveConfiguration: Starting save to {_configFilePath}");

                // Ensure directory exists
                string directory = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    // System.Diagnostics.Debug.WriteLine($"SaveConfiguration: Created directory {directory}");
                }

                // Create YAML with proper formatting using custom AOT serializer
                string serializedConfig = YamlSerlialiserFactory.Serialize(config);

                string yaml = CreateYamlWithHeader() + serializedConfig;

                File.WriteAllText(_configFilePath, yaml);

                // System.Diagnostics.Debug.WriteLine("Configuration saved to YAML file successfully");
            }
            catch //(Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
                // System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }


        /// <summary>
        /// Creates a YAML header that documents the restructured configuration format
        /// </summary>
        private string CreateYamlWithHeader()
        {
            return $@"# NKit UI Configuration File
# Generated by NKit UI v{Nanook.NKit.AppSettings.GetVersion()}
# Please read - https://github.com/Nanook/NKit/wiki/Usage for the most up to date documentation

# CONFIGURATION STRUCTURE:
# ========================
# 
# GLOBAL SECTION: Contains UI-only options that don't affect processing
#   - UI preferences (console output, file filters, etc.)
#   - Global parallelism and logging levels
#   - Last selected task (for UI state)
#
# SYSTEMS SECTION: Contains ALL processing-related settings per system
#   - Task options (convert formats, extract settings)
#   - Process options (verify, recursive, delete processed, etc.)  
#   - System paths (all expanded with system-specific variables)
#
# This structure ensures that:
# - UI behavior is consistent across systems (global)
# - Processing behavior is specific to each system (per-system)
# - Paths are correctly expanded for each system's context

# PATH EXPANSION:
# ===============
# Unlike the CLI, the UI stores EXPANDED paths per system since it processes
# one system at a time rather than batch processing multiple systems.
# 
# Examples of stored paths:
#   CLI format:  dat: ""$configPath$/dats/$system$/*.zip//*.dat"" 
#   UI format:   dat: ""C:/Users/user/nkit/config/dats/gamecube/*.zip//*.dat"" 

# CONFIGURATION MODES:
# ===================
# - PORTABLE MODE (preferred): Config file next to executable
# - SYSTEM MODE (fallback): Config in system directories

# Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

";
        }

        /// <summary>
        /// Clears the cached configuration (useful for testing)
        /// </summary>
        public void ClearCache()
        {
            lock (_lock)
            {
                _cachedConfig = null;
            }
        }

        /// <summary>
        /// Gets the path to the configuration file
        /// </summary>
        public string GetConfigurationPath() => _configFilePath;

        /// <summary>
        /// Stores the selected tab index for a specific system
        /// </summary>
        /// <param name="system">The system to store the tab index for</param>
        /// <param name="tabIndex">The tab index to store</param>
        public void StoreTabIndexForSystem(SystemType system, int tabIndex)
        {
            if (system == SystemType.NotSet)
                return;

            lock (_lock)
            {
                try
                {
                    UiConfiguration config = LoadOrCreateConfiguration();
                    string systemKey = system.ToString().ToLowerInvariant();

                    config.Systems ??= new Dictionary<string, UiConfiguration.SystemConfiguration>();

                    if (!config.Systems.ContainsKey(systemKey))
                    {
                        config.Systems[systemKey] = new UiConfiguration.SystemConfiguration();
                    }

                    config.Systems[systemKey].SelectedTabIndex = tabIndex;

                    // Save to YAML immediately to persist the change
                    SaveConfiguration(config);
                    _cachedConfig = config;
                }
                catch //(Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to store tab index for system {system}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the selected tab index for a specific system
        /// </summary>
        /// <param name="system">The system to get the tab index for</param>
        /// <returns>The stored tab index, or 0 if not found</returns>
        public int GetTabIndexForSystem(SystemType system)
        {
            if (system == SystemType.NotSet)
                return 0;

            lock (_lock)
            {
                try
                {
                    UiConfiguration config = LoadOrCreateConfiguration();
                    string systemKey = system.ToString().ToLowerInvariant();

                    if (config.Systems?.ContainsKey(systemKey) == true)
                    {
                        return config.Systems[systemKey].SelectedTabIndex;
                    }
                }
                catch //(Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to get tab index for system {system}: {ex.Message}");
                }
            }

            return 0; // Default to first tab
        }

        /// <summary>
        /// Stores format-specific level preference for a system
        /// This allows preserving user level choices when switching between formats
        /// </summary>
        public void StoreFormatLevelForSystem(SystemType system, string format, string level)
        {
            if (system == SystemType.NotSet || string.IsNullOrEmpty(format) || string.IsNullOrEmpty(level))
                return;

            lock (_lock)
            {
                try
                {
                    UiConfiguration config = LoadOrCreateConfiguration();
                    string systemKey = system.ToString().ToLowerInvariant();

                    config.Systems ??= new Dictionary<string, UiConfiguration.SystemConfiguration>();

                    if (!config.Systems.ContainsKey(systemKey))
                    {
                        config.Systems[systemKey] = new UiConfiguration.SystemConfiguration();
                    }

                    // Initialize format-specific level storage if it doesn't exist
                    config.Systems[systemKey].FormatLevels ??= new Dictionary<string, string>();

                    // Extract base format name for storage key
                    string baseFormat = format.Split(':')[0].ToLowerInvariant();
                    if (format.Contains('/'))
                    {
                        // For dual format, extract the compressed format part
                        string[] parts = format.Split('/');
                        baseFormat = parts[0].Split(':')[0].ToLowerInvariant();
                    }

                    config.Systems[systemKey].FormatLevels[baseFormat] = level;

                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] Stored format level for {system}/{baseFormat}: '{level}'");

                    // Save to YAML immediately to persist the change
                    SaveConfiguration(config);
                    _cachedConfig = config;
                }
                catch //(Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to store format level for system {system}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Retrieves format-specific level preference for a system
        /// Returns the stored level for the format, or null if none exists
        /// </summary>
        public string GetFormatLevelForSystem(SystemType system, string format)
        {
            if (system == SystemType.NotSet || string.IsNullOrEmpty(format))
                return null;

            lock (_lock)
            {
                try
                {
                    UiConfiguration config = LoadOrCreateConfiguration();
                    string systemKey = system.ToString().ToLowerInvariant();

                    if (config.Systems?.ContainsKey(systemKey) != true)
                        return null;

                    if (config.Systems[systemKey].FormatLevels == null)
                        return null;

                    // Extract base format name for lookup key
                    string baseFormat = format.Split(':')[0].ToLowerInvariant();
                    if (format.Contains('/'))
                    {
                        // For dual format, extract the compressed format part
                        string[] parts = format.Split('/');
                        baseFormat = parts[0].Split(':')[0].ToLowerInvariant();
                    }

                    if (config.Systems[systemKey].FormatLevels.TryGetValue(baseFormat, out string storedLevel))
                    {
                        // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] Retrieved format level for {system}/{baseFormat}: '{storedLevel}'");
                        return storedLevel;
                    }

                    // System.Diagnostics.Debug.WriteLine($"[YamlConfigurationStore] No stored level found for {system}/{baseFormat}");
                    return null;
                }
                catch //(Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"Failed to get format level for system {system}: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Checks if a format level has been previously stored for a system
        /// This helps determine whether to use stored value or apply default
        /// </summary>
        public bool HasFormatLevelForSystem(SystemType system, string format) => !string.IsNullOrEmpty(GetFormatLevelForSystem(system, format));
    }
}
using Nanook.NKit;
using Nanook.NKit.Configuration;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace NKit.Ui.Models
{
    /// <summary>
    /// Represents the UI's YAML configuration structure with proper separation:
    /// - Global: UI-only options (not processing-related)
    /// - Systems: All task options, process options, and system paths per system
    /// 
    /// IMPORTANT: Unlike the CLI, the UI expands path variables ($system$, $task$, etc.) immediately
    /// and stores fully resolved paths per system. This is because the UI processes one system at a time
    /// rather than batch processing multiple systems like the CLI.
    /// </summary>
    public class UiConfiguration
    {
        /// <summary>
        /// Version of the configuration format for future compatibility
        /// </summary>
        [YamlMember(Alias = "version")]
        public string Version { get; set; } = "1.4";

        /// <summary>
        /// Global configuration settings - UI-ONLY options that don't affect processing.
        /// This ensures UI options are restored from the main config file
        /// </summary>
        [YamlMember(Alias = "global")]
        public GlobalSettings Global { get; set; } = new();

        /// <summary>
        /// System-specific configurations mapped by system name (lowercase).
        /// Each system stores:
        /// - All task options (convert formats, extract settings)
        /// - All process options (verify, recursive, delete processed, etc.)
        /// - All system paths (expanded with system-specific variables)
        /// </summary>
        [YamlMember(Alias = "systems")]
        public Dictionary<string, SystemConfiguration> Systems { get; set; } = new();

        /// <summary>
        /// Global configuration settings - UI-ONLY options that don't affect processing
        /// </summary>
        public class GlobalSettings
        {
            [YamlMember(Alias = ConfigSettingsConstants.ParamConsoleLevel)]
            public string ConsoleLevel { get; set; } = "info";

            /// <summary>
            /// Last selected system type, persisted so the UI can restore it on restart
            /// </summary>
            [YamlMember(Alias = "lastSystem")]
            public string LastSystem { get; set; }

            /// <summary>
            /// Last selected task type, persisted so the UI can restore it on restart
            /// </summary>
            [YamlMember(Alias = "lastTask")]
            public string LastTask { get; set; }

            [YamlMember(Alias = "dats")]
            public DatCollections Dats { get; set; } = new();

            [YamlMember(Alias = "ui")]
            public UiOnlySettings Ui { get; set; } = new();
        }

        /// <summary>
        /// DAT collections configuration that matches the CLI structure
        /// </summary>
        public class DatCollections
        {
            [YamlMember(Alias = "redump")]
            public string Redump { get; set; }

            [YamlMember(Alias = "nointro")]
            public string NoIntro { get; set; }

            [YamlMember(Alias = "tosec")]
            public string Tosec { get; set; }
        }

        /// <summary>
        /// UI-only settings that control presentation and behavior but don't affect processing
        /// </summary>
        public class UiOnlySettings
        {
            [YamlMember(Alias = "showQueued")]
            public bool ShowQueued { get; set; } = true;

            [YamlMember(Alias = "showSkipped")]
            public bool ShowSkipped { get; set; } = true;

            [YamlMember(Alias = "showProcessing")]
            public bool ShowProcessing { get; set; } = true;

            [YamlMember(Alias = "showCompleted")]
            public bool ShowCompleted { get; set; } = true;

            [YamlMember(Alias = "showFailed")]
            public bool ShowFailed { get; set; } = true;

            [YamlMember(Alias = "showCancelled")]
            public bool ShowCancelled { get; set; } = true;

            [YamlMember(Alias = "consoleOutputAutoScroll")]
            public bool ConsoleOutputAutoScroll { get; set; } = true;

            [YamlMember(Alias = "consoleOutputBuffer")]
            public int ConsoleOutputBuffer { get; set; } = 100;

            [YamlMember(Alias = "consoleOutputWrapText")]
            public bool ConsoleOutputWrapText { get; set; } = true;

            [YamlMember(Alias = "reprocessCompletedFiles")]
            public bool ReprocessCompletedFiles { get; set; } = false;

            [YamlMember(Alias = "reprocessFailedFiles")]
            public bool ReprocessFailedFiles { get; set; } = false;

            [YamlMember(Alias = "reprocessSkippedFiles")]
            public bool ReprocessSkippedFiles { get; set; } = false;

            [YamlMember(Alias = "persistFileQueue")]
            public bool PersistFileQueue { get; set; } = true;

            /// <summary>
            /// Persist file queue between UI sessions
            /// </summary>
            [YamlMember(Alias = "showTooltips")]
            public bool ShowTooltips { get; set; } = true;

            /// <summary>
            /// Window decoration mode for Linux. "native" uses OS title bar, "csd" uses client-side decorations.
            /// Only affects Linux — Windows/macOS always use their native decorations.
            /// </summary>
            [YamlMember(Alias = "windowDecorationMode")]
            public string WindowDecorationMode { get; set; } = "csd";
        }

        /// <summary>
        /// System-specific configuration that stores ALL processing-related settings:
        /// - Task options (convert formats, extract settings)
        /// - Process options (verify, recursive, delete processed, etc.)
        /// - System paths (all expanded with system-specific variables)
        /// - UI state per system (selected tab index)
        /// 
        /// This mirrors the CLI's sys:systemname structure but with expanded paths.
        /// </summary>
        public class SystemConfiguration
        {
            // ===== TASK OPTIONS (Convert, Extract, etc.) =====

            /// <summary>
            /// Convert format string generated by ConfigSettingsFormatGenerator
            /// Examples: "rvz:zstd:19:128k:16", "cso:9:2k:4", "cue:split:bin:bin:sub"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamConvert)]
            public string Convert { get; set; }

            /// <summary>
            /// Extract format string generated by ConfigSettingsFormatGenerator
            /// Examples: "mi:*", "ri:^(.*)$", "fi:*"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamExtract)]
            public string Extract { get; set; }

            /// <summary>
            /// Dedupe configuration string: setName:shardSize:blockSize:persistFs
            /// All parts optional. Examples: "mySet:50g:64k:y", ":100g:128k:n"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamDedupe)]
            public string Dedupe { get; set; }

            /// <summary>
            /// 1GMR YAML file path for per-file routing during dedupe processing.
            /// When set, enables per-file routing mode.
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.Param1Gmr)]
            public string OgmrYamlPath { get; set; }

            /// <summary>
            /// Format-specific level preferences for this system.
            /// Stores user's compression level choices per format (e.g., "cso" -> "2", "zso" -> "7").
            /// This allows preserving user selections when switching between formats.
            /// </summary>
            [YamlMember(Alias = "formatLevels")]
            public Dictionary<string, string> FormatLevels { get; set; }

            // ===== PROCESS OPTIONS =====

            /// <summary>
            /// Verification setting for this system (backward compatibility - will be migrated to task-specific settings)
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamV)]
            public string V { get; set; } = ConfigSettingsConstants.LosslessTrue;

            /// <summary>
            /// Task-specific verification settings for Convert task
            /// </summary>
            [YamlMember(Alias = "v_Convert")]
            public string V_Convert { get; set; }

            /// <summary>
            /// Task-specific verification settings for Scan task
            /// </summary>
            [YamlMember(Alias = "v_Scan")]
            public string V_Scan { get; set; }

            /// <summary>
            /// Task-specific verification settings for Fix task
            /// </summary>
            [YamlMember(Alias = "v_Fix")]
            public string V_Fix { get; set; }

            /// <summary>
            /// Task-specific verification settings for Verify task
            /// </summary>
            [YamlMember(Alias = "v_Verify")]
            public string V_Verify { get; set; }

            /// <summary>
            /// Task-specific verification settings for Dedupe task
            /// </summary>
            [YamlMember(Alias = "v_Dedupe")]
            public string V_Dedupe { get; set; }

            /// <summary>
            /// Recursive directory processing for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamR)]
            public bool R { get; set; } = true;

            /// <summary>
            /// Archive scanning for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamArc)]
            public bool Arc { get; set; } = true;

            /// <summary>
            /// Delete processed files for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamDeleteProcessed)]
            public bool DeleteProcessed { get; set; }

            /// <summary>
            /// Skip if completed for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamSkipIfCompleted)]
            public bool SkipIfCompleted { get; set; }

            /// <summary>
            /// Output as DAT match for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamOutAsDatMatch)]
            public bool OutAsDatMatch { get; set; }

            /// <summary>
            /// Save results to a summary file for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamResults)]
            public bool Results { get; set; } = true;

            /// <summary>
            /// File log output level for this system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamLogOutLevel)]
            public string LogOutLevel { get; set; } = "info";

            // ===== UI STATE PER SYSTEM =====

            /// <summary>
            /// Last selected tab index for this system's settings.
            /// This preserves which tab (Convert Options, Process Options, System Paths, etc.) 
            /// was selected when the user switches between systems.
            /// </summary>
            [YamlMember(Alias = "selectedTabIndex")]
            public int SelectedTabIndex { get; set; } = 0;

            // ===== SYSTEM PATHS (All expanded for this specific system) =====

            /// <summary>
            /// Output directory - expanded for this specific system
            /// e.g., "C:/users/user/nkit/out/gamecube" instead of "$userPath$/nkit/out/$system$"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamOut)]
            public string Out { get; set; }

            /// <summary>
            /// Temporary directory - expanded for this specific system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamTmp)]
            public string Tmp { get; set; }

            /// <summary>
            /// Scan output directory - expanded for this specific system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamScanOut)]
            public string ScanOut { get; set; }

            /// <summary>
            /// Scan input directory - expanded for this specific system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamScanIn)]
            public string ScanIn { get; set; }

            /// <summary>
            /// Log output file - expanded for this specific system and with date variables
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamLogOut)]
            public string LogOut { get; set; }

            /// <summary>
            /// Results output file - expanded for this specific system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamResultsOut)]
            public string ResultsOut { get; set; }

            /// <summary>
            /// Base input path - expanded for this specific system
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamBaseInPath)]
            public string BaseInPath { get; set; }

            /// <summary>
            /// Keys path - expanded for this specific system
            /// e.g., "/config/keys/ps3/*.zip" instead of "/config/keys/$system$/*.zip"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamKeys)]
            public string Keys { get; set; }

            /// <summary>
            /// DAT file path - expanded for this specific system
            /// e.g., "/config/dats/wii/*.zip//*.dat" instead of "/config/dats/$system$/*.zip//*.dat"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamDat)]
            public string Dat { get; set; }

            /// <summary>
            /// Fix info file path - expanded for this specific system
            /// e.g., "/config/fix/fix_gamecube.yaml" instead of "/config/fix/fix_$system$.yaml"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamFixInfo)]
            public string FixInfo { get; set; }

            /// <summary>
            /// Fix files directory - expanded for this specific system
            /// e.g., "/config/fix/ps3" instead of "/config/fix/$system$"
            /// </summary>
            [YamlMember(Alias = ConfigSettingsConstants.ParamFixFiles)]
            public string FixFiles { get; set; }

            /// <summary>
            /// Gets the verify setting for a specific task, with fallback to general V setting.
            /// Note: Expand task is not supported in the UI.
            /// </summary>
            public string GetVerifyForTask(TaskType task)
            {
                return task switch
                {
                    TaskType.Convert => V_Convert ?? V,
                    TaskType.Scan => V_Scan ?? V,
                    TaskType.Fix => V_Fix ?? V,
                    TaskType.Verify => V_Verify ?? V,
                    TaskType.Dedupe => V_Dedupe ?? V,
                    _ => V // Fallback for all other tasks including Expand (not supported in UI)
                };
            }

            /// <summary>
            /// Sets the verify setting for a specific task.
            /// Note: Expand task is not supported in the UI.
            /// </summary>
            public void SetVerifyForTask(TaskType task, string value)
            {
                switch (task)
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
                    default:
                        V = value; // Fallback for unsupported tasks including Expand
                        break;
                }
            }
        }
    }
}
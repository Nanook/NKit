using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NKit.Ui.Extensions
{
    /// <summary>
    /// Extension methods and helper classes that integrate NKitConfigurationProvider
    /// with the existing UI ViewModels. This demonstrates how to migrate from
    /// hardcoded values to the centralized configuration ConfigSettingsRanges.
    /// </summary>
    public static class ConfigurationProviderExtensions
    {
        // ======= Convert Settings Extensions =======

        /// <summary>
        /// Gets block sizes for the current system using the configuration provider
        /// </summary>
        public static ObservableCollection<string> GetBlockSizes(this SystemType systemType) => new ObservableCollection<string>(ConfigSettingsRanges.GetBlockSizes(systemType));

        /// <summary>
        /// Gets parallelism values for the current system using the configuration provider
        /// </summary>
        public static ObservableCollection<string> GetParallelisms(this SystemType systemType)
        {
            IReadOnlyList<int> values = ConfigSettingsRanges.GetParallelismValues(systemType);
            return new ObservableCollection<string>(values.Select(v => v.ToString()));
        }

        /// <summary>
        /// Gets compression levels for the specified encoding type
        /// </summary>
        public static ObservableCollection<string> GetCompressionLevels(this RvzEncodingType encoding)
        {
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(ConfigSettingsConstants.FormatRvz, encoding);
            return new ObservableCollection<string>(levels.Select(l => l.ToString()));
        }

        /// <summary>
        /// Gets compression levels for the specified format (CSO/ZSO formats)
        /// </summary>
        public static ObservableCollection<string> GetCompressionLevels(string format)
        {
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);
            return new ObservableCollection<string>(levels.Select(l => l.ToString()));
        }

        /// <summary>
        /// Gets ZLib compression levels as observable collection (for CSO/CSO2 formats)
        /// </summary>
        public static ObservableCollection<string> GetZlibLevels() => new ObservableCollection<string>(ConfigSettingsRanges.GetZlibLevels().Select(l => l.ToString()));

        /// <summary>
        /// Gets LZ4 compression levels as observable collection (for ZSO format)
        /// </summary>
        public static ObservableCollection<string> GetLz4Levels() => new ObservableCollection<string>(ConfigSettingsRanges.GetLz4Levels().Select(l => l.ToString()));

        /// <summary>
        /// Gets RVZ encoding types as observable collection
        /// </summary>
        public static ObservableCollection<RvzEncodingType> GetRvzEncodingTypes() => new ObservableCollection<RvzEncodingType>(ConfigSettingsRanges.GetRvzEncodingTypes());

        /// <summary>
        /// Gets CUE types as observable collection
        /// </summary>
        public static ObservableCollection<string> GetCueTypes() => new ObservableCollection<string>(ConfigSettingsRanges.GetCueTypes());

        /// <summary>
        /// Gets binary extensions as observable collection
        /// </summary>
        public static ObservableCollection<string> GetBinaryExtensions() => new ObservableCollection<string>(ConfigSettingsRanges.GetBinaryExtensions());

        /// <summary>
        /// Gets audio extensions as observable collection
        /// </summary>
        public static ObservableCollection<string> GetAudioExtensions() => new ObservableCollection<string>(ConfigSettingsRanges.GetAudioExtensions());

        // ======= End Convert Settings Extensions =======

        // ======= Processing Options Extensions =======

        /// <summary>
        /// Gets log levels as observable collection
        /// </summary>
        public static ObservableCollection<LogLevel> GetLogLevels() => new ObservableCollection<LogLevel>(ConfigSettingsRanges.GetLogLevels());

        // ======= End Processing Options Extensions =======

        // ======= Validation Extensions =======

        /// <summary>
        /// Validates the current convert settings and returns any issues
        /// </summary>
        public static ValidationResult ValidateConvertSettings(SystemType systemType, string format,
            string encoding, string level, string blockSize, string parallelism)
        {
            if (format == "rvz")
            {
                string formatString = $"{format}:{encoding}:{level}:{blockSize}:{parallelism}";
                return ConfigSettingsFormatValidator.ValidateRvzFormat(formatString);
            }
            else if (format == "cue")
            {
                // For CUE, we need cueType, binary, audio instead of encoding/level
                // This would need to be adapted based on how CUE settings are stored
                return ValidationResult.Success(); // Placeholder
            }
            else if (format == "cso" || format == "cso2" || format == "zso")
            {
                string formatString = $"{format}:{level}:{blockSize}:{parallelism}";
                return ConfigSettingsFormatValidator.ValidateCsoFormat(formatString, format);
            }

            return ValidationResult.Success(); // Other formats don't need validation
        }

        /// <summary>
        /// Gets configuration warnings for the current settings
        /// </summary>
        public static IEnumerable<string> GetSettingsWarnings(SystemType systemType, string format,
            string level, string blockSize) => ConfigSettingsFormatValidator.GetConfigurationWarnings(systemType, format, level, blockSize);

        // ======= End Validation Extensions =======

        // ======= Default Value Extensions =======

        /// <summary>
        /// Gets safe default values for a system/task combination
        /// </summary>
        public static (string format, string encoding, string level, string blockSize, string parallelism)
            GetDefaultConvertSettings(SystemType systemType, TaskType taskType)
        {
            string format = ConfigSettingsDefaults.GetDefaultFormat(systemType);

            if (format == "rvz")
            {
                RvzEncodingType encoding = RvzEncodingType.ZStd;
                return (
                    format: format,
                    encoding: encoding.ToString().ToLower(),
                    level: ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding).ToString(),
                    blockSize: ConfigSettingsDefaults.GetDefaultBlockSize(systemType),
                    parallelism: ConfigSettingsDefaults.GetDefaultParallelism(systemType).ToString()
                );
            }
            else if (format == "cso" || format == "cso2")
            {
                return (
                    format: format,
                    encoding: "", // Not used for CSO
                    level: ConfigSettingsDefaults.GetDefaultCompressionLevel(format).ToString(), // Now uses ZLib default (9)
                    blockSize: ConfigSettingsDefaults.GetDefaultBlockSize(systemType),
                    parallelism: ConfigSettingsDefaults.GetDefaultParallelism(systemType).ToString()
                );
            }
            else if (format == "zso")
            {
                return (
                    format: format,
                    encoding: "", // Not used for ZSO
                    level: ConfigSettingsDefaults.GetDefaultCompressionLevel(format).ToString(), // Uses LZ4 default (12)
                    blockSize: ConfigSettingsDefaults.GetDefaultBlockSize(systemType),
                    parallelism: ConfigSettingsDefaults.GetDefaultParallelism(systemType).ToString()
                );
            }
            else
            {
                return (
                    format: format,
                    encoding: "",
                    level: "",
                    blockSize: ConfigSettingsDefaults.GetDefaultBlockSize(systemType),
                    parallelism: ConfigSettingsDefaults.GetDefaultParallelism(systemType).ToString()
                );
            }
        }

        /// <summary>
        /// Gets safe default extract settings
        /// </summary>
        public static (string type, string searchTerm, bool matchCase, bool forensic)
            GetDefaultExtractSettings()
        {
            return (
                type: ConfigSettingsDefaults.GetDefaultExtractType(),
                searchTerm: ConfigSettingsDefaults.GetDefaultExtractSearchTerm(),
                matchCase: false,
                forensic: false
            );
        }

        // ======= End Default Value Extensions =======

        // ======= Format Generation Extensions =======

        /// <summary>
        /// Generates a complete format string for the given parameters
        /// </summary>
        public static string GenerateFormatString(string format, SystemType systemType,
            string encoding = null, string level = null, string blockSize = null, string parallelism = null,
            string cueType = null, string binary = null, string audio = null)
        {
            return format.ToLowerInvariant() switch
            {
                "rvz" => GenerateRvzFormatString(encoding, level, blockSize, parallelism),
                "cue" => ConfigSettingsFormatGenerator.GenerateCueFormatString(cueType, binary, audio),
                "cso" or "cso2" or "zso" => GenerateCsoFormatString(format, level, blockSize, parallelism),
                _ => format
            };
        }

        private static string GenerateRvzFormatString(string encoding, string level, string blockSize, string parallelism)
        {
            if (!Enum.TryParse<RvzEncodingType>(encoding, true, out RvzEncodingType encodingType))
                encodingType = RvzEncodingType.ZStd;

            int? levelInt = int.TryParse(level, out int l) ? l : null;
            int? parallelismInt = int.TryParse(parallelism, out int p) ? p : null;

            return ConfigSettingsFormatGenerator.GenerateRvzFormatString(encodingType, levelInt, blockSize, parallelismInt);
        }

        private static string GenerateCsoFormatString(string format, string level, string blockSize, string parallelism) => $"{format}:{level}:{blockSize}:{parallelism}";

        // ======= End Format Generation Extensions =======
    }

    // ======= Configuration-Aware Helper Classes =======

    /// <summary>
    /// A configuration-aware replacement for hardcoded constants in ViewModels
    /// </summary>
    public static class ConfigurationConstants
    {
        // Dynamic constants that adapt to system types
        public static int GetMaxParallelism(SystemType systemType) => systemType switch
        {
            SystemType.GameCube or SystemType.Wii => ConfigSettingsConstants.MaxNintendoParallelism,
            SystemType.PS3 or SystemType.PSP => ConfigSettingsConstants.MaxSonyParallelism,
            _ => ConfigSettingsConstants.MaxNintendoParallelism
        };

        public static int GetMaxCompressionLevel(RvzEncodingType encoding) =>
            ConfigSettingsRanges.GetCompressionLevels(ConfigSettingsConstants.FormatRvz, encoding).Max();

        public static string GetDefaultFormat(SystemType systemType) =>
            ConfigSettingsDefaults.GetDefaultFormat(systemType);

        // UI-specific constants
        public const int FormatSettingHeight = 47;
    }

    /// <summary>
    /// Configuration-aware format validator that can be used throughout the application
    /// </summary>
    public static class ConfigurationValidator
    {
        /// <summary>
        /// Validates any format configuration string
        /// </summary>
        public static ValidationResult ValidateFormat(string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');
            string format = parts[0].ToLowerInvariant();

            return format switch
            {
                "rvz" => ConfigSettingsFormatValidator.ValidateRvzFormat(formatString),
                "cue" => ConfigSettingsFormatValidator.ValidateCueFormat(formatString),
                "cso" or "cso2" or "zso" => ConfigSettingsFormatValidator.ValidateCsoFormat(formatString, format),
                _ => ValidationResult.Success() // Other formats are assumed valid
            };
        }

        /// <summary>
        /// Validates extract configuration string
        /// </summary>
        public static ValidationResult ValidateExtractFormat(string extractString)
        {
            if (string.IsNullOrWhiteSpace(extractString))
                return ValidationResult.Error("Extract string cannot be empty");

            // Use the provider's extract validation instead of parsing manually
            return ConfigSettingsFormatValidator.ValidateExtractFormat(extractString);
        }
    }

    // ======= End Configuration-Aware Helper Classes =======
}
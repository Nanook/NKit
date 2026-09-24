using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Validates format configuration strings and provides configuration warnings.
    /// </summary>
    public static class ConfigSettingsFormatValidator
    {
        // ======= Configuration Warnings =======

        /// <summary>
        /// Gets validation warnings for potentially problematic configurations
        /// </summary>
        public static IEnumerable<string> GetConfigurationWarnings(SystemType systemType, string format, string level, string blockSize)
        {
            List<string> warnings = new List<string>();

            // ZStd ultra level warnings
            if (format == ConfigSettingsConstants.FormatRvz && int.TryParse(level, out int lvl) && lvl >= ConfigSettingsConstants.ZStdUltraLevelThreshold)
            {
                warnings.Add($"ZStd ultra levels {ConfigSettingsConstants.ZStdUltraLevelThreshold}+ require more memory to compress and decompress");
            }

            // Block size warnings for CSO formats
            if ((format == ConfigSettingsConstants.FormatCso || format == ConfigSettingsConstants.FormatCso2 || format == ConfigSettingsConstants.FormatZso) && blockSize != null &&
                !blockSize.Equals(ConfigSettingsConstants.BlockSize2kb, StringComparison.OrdinalIgnoreCase) &&
                !blockSize.Equals(ConfigSettingsConstants.BlockSize2k, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Some tools/apps may not support block sizes other than {ConfigSettingsConstants.BlockSize2kb}");
            }

            return warnings;
        }

        // ======= End Configuration Warnings =======
        // ======= RVZ Validation =======

        /// <summary>
        /// Validates RVZ format configuration string
        /// </summary>
        public static ValidationResult ValidateRvzFormat(string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');

            if (parts.Length < 2)
                return validateZStdRvzFormat(new[] { parts[0], ConfigSettingsConstants.EncodingZStd });

            if (!Enum.TryParse<RvzEncodingType>(parts[1], true, out RvzEncodingType encoding))
                return ValidationResult.Error($"Invalid encoding '{parts[1]}'. Supported: {ConfigSettingsConstants.EncodingNone}, {ConfigSettingsConstants.EncodingZStd}, {ConfigSettingsConstants.EncodingLzma}");

            return encoding switch
            {
                RvzEncodingType.ZStd => validateZStdRvzFormat(parts),
                RvzEncodingType.Lzma => validateLzmaRvzFormat(parts),
                RvzEncodingType.None => validateNoneRvzFormat(parts),
                _ => ValidationResult.Error($"Unsupported encoding type: {encoding}")
            };
        }

        private static ValidationResult validateZStdRvzFormat(string[] parts)
        {
            string level = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : ConfigSettingsConstants.DefaultZStdLevel.ToString();
            string blockSize = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : ConfigSettingsConstants.BlockSize128kb;
            string parallelism = parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : ConfigSettingsConstants.DefaultNintendoParallelism.ToString();

            if (!int.TryParse(level, out int levelInt) || levelInt < ConfigSettingsConstants.MinZStdLevel || levelInt > ConfigSettingsConstants.MaxZStdLevel)
                return ValidationResult.Error($"ZStd level must be {ConfigSettingsConstants.MinZStdLevel}-{ConfigSettingsConstants.MaxZStdLevel}");

            if (!Regex.IsMatch(blockSize, ConfigSettingsConstants.BlockSizePatternRvz, RegexOptions.IgnoreCase))
                return ValidationResult.Error(ConfigSettingsConstants.BlockSizeErrorRvz);

            if (!int.TryParse(parallelism, out int parallelismInt) || parallelismInt < 0 || parallelismInt > ConfigSettingsConstants.MaxParallelismValue)
                return ValidationResult.Error($"Parallelism must be 0-{ConfigSettingsConstants.MaxParallelismValue}");

            return ValidationResult.Success();
        }

        private static ValidationResult validateLzmaRvzFormat(string[] parts)
        {
            string level = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : ConfigSettingsConstants.DefaultLzmaLevel.ToString();
            string blockSize = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : ConfigSettingsConstants.BlockSize128kb;
            string parallelism = parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : ConfigSettingsConstants.DefaultNintendoParallelism.ToString();

            if (!int.TryParse(level, out int levelInt) || levelInt < ConfigSettingsConstants.MinLzmaLevel || levelInt > ConfigSettingsConstants.MaxLzmaLevel)
                return ValidationResult.Error($"LZMA compression level must be {ConfigSettingsConstants.MinLzmaLevel}-{ConfigSettingsConstants.MaxLzmaLevel}");

            if (!Regex.IsMatch(blockSize, ConfigSettingsConstants.BlockSizePatternRvz, RegexOptions.IgnoreCase))
                return ValidationResult.Error(ConfigSettingsConstants.BlockSizeErrorRvz);

            if (!int.TryParse(parallelism, out int parallelismInt) || parallelismInt < 0 || parallelismInt > ConfigSettingsConstants.MaxParallelismValue)
                return ValidationResult.Error($"Parallelism must be 0-{ConfigSettingsConstants.MaxParallelismValue}");

            return ValidationResult.Success();
        }

        private static ValidationResult validateNoneRvzFormat(string[] parts)
        {
            string blockSize = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : ConfigSettingsConstants.BlockSize128kb;
            string parallelism = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : ConfigSettingsConstants.DefaultCoreParallelism.ToString();

            if (!Regex.IsMatch(blockSize, ConfigSettingsConstants.BlockSizePatternRvz, RegexOptions.IgnoreCase))
                return ValidationResult.Error(ConfigSettingsConstants.BlockSizeErrorRvz);

            if (!int.TryParse(parallelism, out int parallelismInt) || parallelismInt < 0 || parallelismInt > ConfigSettingsConstants.MaxParallelismValue)
                return ValidationResult.Error($"Parallelism must be 0-{ConfigSettingsConstants.MaxParallelismValue}");

            return ValidationResult.Success();
        }

        // ======= End RVZ Validation =======
        // ======= CSO/ZSO Validation =======

        /// <summary>
        /// Validates CSO/ZSO format configuration
        /// </summary>
        public static ValidationResult ValidateCsoFormat(string formatString, string formatType = null)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');
            formatType ??= parts[0].ToLowerInvariant();

            string level = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : ConfigSettingsConstants.MaxZlibLevel.ToString();
            string blockSize = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : ConfigSettingsConstants.BlockSize2kb;
            string parallelism = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : ConfigSettingsConstants.DefaultSonyParallelism.ToString();

            if (formatType == ConfigSettingsConstants.FormatZso && string.IsNullOrWhiteSpace(level))
                level = "1";

            if (formatType != ConfigSettingsConstants.FormatZso)
            {
                if (!int.TryParse(level, out int levelInt) || levelInt < ConfigSettingsConstants.MinZlibLevel || levelInt > ConfigSettingsConstants.MaxZlibLevel)
                    return ValidationResult.Error($"{formatType.ToUpper()} level must be {ConfigSettingsConstants.MinZlibLevel}-{ConfigSettingsConstants.MaxZlibLevel}");
            }

            if (!Regex.IsMatch(blockSize, ConfigSettingsConstants.BlockSizePatternCso, RegexOptions.IgnoreCase))
                return ValidationResult.Error(ConfigSettingsConstants.BlockSizeErrorCso);

            if (!int.TryParse(parallelism, out int parallelismInt) || parallelismInt < 0 || parallelismInt > ConfigSettingsConstants.MaxParallelismValue)
                return ValidationResult.Error($"Parallelism must be 0-{ConfigSettingsConstants.MaxParallelismValue}");

            return ValidationResult.Success();
        }

        // ======= End CSO/ZSO Validation =======
        // ======= CUE Validation =======

        /// <summary>
        /// Validates CUE format configuration
        /// </summary>
        public static ValidationResult ValidateCueFormat(string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');

            string cueType = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : ConfigSettingsConstants.CueTypeSplit;
            string binary = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : ConfigSettingsConstants.BinaryExtensionBin;
            string audio = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : ConfigSettingsConstants.AudioExtensionBin;

            if (!Regex.IsMatch(cueType, $@"^({ConfigSettingsConstants.CueTypeSplit}|{ConfigSettingsConstants.CueTypeJoined})$", RegexOptions.IgnoreCase))
                return ValidationResult.Error($"CUE type must be {ConfigSettingsConstants.CueTypeSplit}|{ConfigSettingsConstants.CueTypeJoined}");

            if (!Regex.IsMatch(binary, $@"^({ConfigSettingsConstants.BinaryExtensionBin}|{ConfigSettingsConstants.BinaryExtensionImg}|{ConfigSettingsConstants.BinaryExtensionIso})$", RegexOptions.IgnoreCase))
                return ValidationResult.Error($"Binary type must be {ConfigSettingsConstants.BinaryExtensionBin}|{ConfigSettingsConstants.BinaryExtensionImg}|{ConfigSettingsConstants.BinaryExtensionIso}");

            if (!Regex.IsMatch(audio, $@"^({ConfigSettingsConstants.AudioExtensionBin}|{ConfigSettingsConstants.AudioExtensionWav}|{ConfigSettingsConstants.AudioExtensionFlac})$", RegexOptions.IgnoreCase))
                return ValidationResult.Error($"Audio type must be {ConfigSettingsConstants.AudioExtensionBin}|{ConfigSettingsConstants.AudioExtensionWav}|{ConfigSettingsConstants.AudioExtensionFlac}");

            return ValidationResult.Success();
        }

        // ======= End CUE Validation =======
        // ======= CISO/WBFS Validation =======

        /// <summary>
        /// Validates CISO format configuration
        /// </summary>
        public static ValidationResult ValidateCisoFormat(string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');

            if (parts.Length == 1)
                return ValidationResult.Success();

            if (parts.Length == 2)
            {
                string lossless = parts[1].ToLowerInvariant();
                if (lossless != ConfigSettingsConstants.LosslessTrue && lossless != ConfigSettingsConstants.LosslessFalse)
                {
                    return ValidationResult.Error($"CISO lossless parameter must be {ConfigSettingsConstants.LosslessTrue}|{ConfigSettingsConstants.LosslessFalse}");
                }
                return ValidationResult.Success();
            }

            return ValidationResult.Error("CISO format should be 'ciso' or 'ciso:lossless'");
        }

        /// <summary>
        /// Validates WBFS format configuration
        /// </summary>
        public static ValidationResult ValidateWbfsFormat(string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');

            if (parts.Length == 1)
                return ValidationResult.Success();

            if (parts.Length == 2)
            {
                string lossless = parts[1].ToLowerInvariant();
                if (lossless != ConfigSettingsConstants.LosslessTrue && lossless != ConfigSettingsConstants.LosslessFalse)
                {
                    return ValidationResult.Error($"WBFS lossless parameter must be {ConfigSettingsConstants.LosslessTrue}|{ConfigSettingsConstants.LosslessFalse}");
                }
                return ValidationResult.Success();
            }

            return ValidationResult.Error("WBFS format should be 'wbfs' or 'wbfs:lossless'");
        }

        // ======= End CISO/WBFS Validation =======
        // ======= Extract Validation =======

        /// <summary>
        /// Validates extract configuration format
        /// </summary>
        public static ValidationResult ValidateExtractFormat(string extractConfig)
        {
            if (string.IsNullOrWhiteSpace(extractConfig))
                return ValidationResult.Success();

            Match match = Regex.Match(extractConfig, @"^([fimr]*):(.*)$");
            if (!match.Success)
                return ValidationResult.Success();

            string flags = match.Groups[1].Value;
            string pattern = match.Groups[2].Value;

            foreach (char flag in flags)
            {
                if (!"fimr".Contains(flag))
                    return ValidationResult.Error($"Extract flag '{flag}' is not recognized. Valid flags are: f (forensic), i (case-insensitive), m (mask-to-regex), r (recursive)");
            }

            if (string.IsNullOrWhiteSpace(pattern))
                return ValidationResult.Error("Extract pattern cannot be empty");

            return ValidationResult.Success();
        }

        // ======= End Extract Validation =======
        // ======= Generic Format Validation =======

        /// <summary>
        /// Validates any format configuration string by automatically determining the format type
        /// </summary>
        public static ValidationResult ValidateFormatString(string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            string[] parts = formatString.Split(':');
            string format = parts[0].ToLowerInvariant();

            return format switch
            {
                ConfigSettingsConstants.FormatRvz => ValidateRvzFormat(formatString),
                ConfigSettingsConstants.FormatCue => ValidateCueFormat(formatString),
                ConfigSettingsConstants.FormatCso or ConfigSettingsConstants.FormatCso2 or ConfigSettingsConstants.FormatZso => ValidateCsoFormat(formatString, format),
                ConfigSettingsConstants.FormatWbfs => ValidateWbfsFormat(formatString),
                ConfigSettingsConstants.FormatCiso => ValidateCisoFormat(formatString),
                ConfigSettingsConstants.FormatIso or ConfigSettingsConstants.FormatApp or ConfigSettingsConstants.FormatTmd or
                ConfigSettingsConstants.FormatWux or ConfigSettingsConstants.FormatGdi or ConfigSettingsConstants.FormatDecIso => ValidationResult.Success(),
                _ => ValidationResult.Error($"Unknown format '{format}'. Supported formats: {string.Join(", ", ConfigSettingsRanges.GetAllSupportedFormats())}")
            };
        }

        /// <summary>
        /// Validates format configuration for a specific system and format combination
        /// </summary>
        public static ValidationResult ValidateFormatString(SystemType systemType, string formatString)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return ValidationResult.Error("Format string cannot be empty");

            // CRITICAL FIX: Handle dual format strings (e.g., "cso:9:2kb:4/cue:split:bin:bin")
            if (formatString.Contains('/'))
            {
                string[] parts = formatString.Split('/');
                if (parts.Length != 2)
                    return ValidationResult.Error("Dual format string must have exactly two parts separated by '/'");

                // Validate single format part
                ValidationResult singleFormatResult = ValidateFormatString(parts[0]);
                if (!singleFormatResult.IsValid)
                    return ValidationResult.Error($"Invalid single format: {singleFormatResult.ErrorMessage}");

                // Validate indexed format part  
                ValidationResult indexedFormatResult = ValidateFormatString(parts[1]);
                if (!indexedFormatResult.IsValid)
                    return ValidationResult.Error($"Invalid indexed format: {indexedFormatResult.ErrorMessage}");

                // Validate that both formats are supported by the system
                IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

                string singleFormat = parts[0].Split(':')[0].ToLowerInvariant();
                string indexedFormat = parts[1].Split(':')[0].ToLowerInvariant();

                if (!supportedFormats.Contains(singleFormat, StringComparer.OrdinalIgnoreCase))
                {
                    return ValidationResult.Error($"Single format '{singleFormat}' is not supported by system '{systemType}'. Supported formats: {string.Join(", ", supportedFormats)}");
                }

                if (!supportedFormats.Contains(indexedFormat, StringComparer.OrdinalIgnoreCase))
                {
                    return ValidationResult.Error($"Indexed format '{indexedFormat}' is not supported by system '{systemType}'. Supported formats: {string.Join(", ", supportedFormats)}");
                }

                return ValidationResult.Success();
            }

            // Handle single format strings (existing logic)
            ValidationResult formatResult = ValidateFormatString(formatString);
            if (!formatResult.IsValid)
                return formatResult;

            IReadOnlyList<string> supportedSingleFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);
            string format = formatString.Split(':')[0].ToLowerInvariant();

            if (!supportedSingleFormats.Contains(format, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.Error($"Format '{format}' is not supported by system '{systemType}'. Supported formats: {string.Join(", ", supportedSingleFormats)}");
            }

            return ValidationResult.Success();
        }

        // ======= End Generic Format Validation =======
    }
}
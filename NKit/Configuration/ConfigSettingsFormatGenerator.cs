using System;
using System.Text;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Generates format configuration strings for YAML files.
    /// Examples: "rvz:zstd:19:128k:16", "cso:9:2k:4", "cue:split:bin:bin"
    /// </summary>
    public static class ConfigSettingsFormatGenerator
    {
        // ======= RVZ Format Generation =======

        /// <summary>
        /// Generates a properly formatted RVZ configuration string with defaults
        /// </summary>
        public static string GenerateRvzFormatString(RvzEncodingType encoding, int? level = null, string blockSize = null, int? parallelism = null)
        {
            level ??= ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding);
            blockSize ??= ConfigSettingsDefaults.GetDefaultBlockSize(SystemType.GameCube);
            parallelism ??= ConfigSettingsDefaults.GetDefaultParallelism(SystemType.GameCube);

            return encoding switch
            {
                RvzEncodingType.ZStd => $"{ConfigSettingsConstants.FormatRvz}:{ConfigSettingsConstants.EncodingZStd}:{level}:{blockSize}:{parallelism}",
                RvzEncodingType.Lzma => $"{ConfigSettingsConstants.FormatRvz}:{ConfigSettingsConstants.EncodingLzma}:{level}:{blockSize}:{parallelism}",
                RvzEncodingType.None => $"{ConfigSettingsConstants.FormatRvz}:{ConfigSettingsConstants.EncodingNone}:{blockSize}:{parallelism}",
                _ => throw new ArgumentException($"Unsupported encoding: {encoding}")
            };
        }

        // ======= End RVZ Format Generation =======

        // ======= WBFS/CISO Format Generation =======

        /// <summary>
        /// Generates a properly formatted WBFS configuration string with defaults
        /// </summary>
        public static string GenerateWbfsFormatString(bool? lossless = null)
        {
            lossless ??= true; // WBFS defaults to lossless
            string losslessValue = lossless.Value ? ConfigSettingsConstants.LosslessTrue : ConfigSettingsConstants.LosslessFalse;
            return $"{ConfigSettingsConstants.FormatWbfs}:{losslessValue}";
        }

        /// <summary>
        /// Generates a properly formatted CISO configuration string with defaults
        /// </summary>
        public static string GenerateCisoFormatString(bool? lossless = null)
        {
            lossless ??= true; // CISO defaults to lossless
            string losslessValue = lossless.Value ? ConfigSettingsConstants.LosslessTrue : ConfigSettingsConstants.LosslessFalse;
            return $"{ConfigSettingsConstants.FormatCiso}:{losslessValue}";
        }

        // ======= End WBFS/CISO Format Generation =======

        // ======= CSO/CSO2/ZSO Format Generation =======

        /// <summary>
        /// Generates a properly formatted CSO configuration string with defaults
        /// </summary>
        public static string GenerateCsoFormatString(string level = null, string blockSize = null, string parallelism = null)
        {
            level ??= ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatCso).ToString();
            blockSize ??= ConfigSettingsDefaults.GetDefaultBlockSize(SystemType.PS2, ConfigSettingsConstants.FormatCso);
            parallelism ??= ConfigSettingsDefaults.GetDefaultParallelism(SystemType.PS2).ToString();

            return $"{ConfigSettingsConstants.FormatCso}:{level}:{blockSize}:{parallelism}";
        }

        /// <summary>
        /// Generates a properly formatted CSO2 configuration string with defaults
        /// </summary>
        public static string GenerateCso2FormatString(string level = null, string blockSize = null, string parallelism = null)
        {
            level ??= ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatCso2).ToString();
            blockSize ??= ConfigSettingsDefaults.GetDefaultBlockSize(SystemType.PS2, ConfigSettingsConstants.FormatCso2);
            parallelism ??= ConfigSettingsDefaults.GetDefaultParallelism(SystemType.PS2).ToString();

            return $"{ConfigSettingsConstants.FormatCso2}:{level}:{blockSize}:{parallelism}";
        }

        /// <summary>
        /// Generates a properly formatted ZSO configuration string with defaults
        /// </summary>
        public static string GenerateZsoFormatString(string level = null, string blockSize = null, string parallelism = null)
        {
            // FIXED: Include level parameter for proper UI binding and persistence
            level ??= ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatZso).ToString();
            blockSize ??= ConfigSettingsDefaults.GetDefaultBlockSize(SystemType.PS2, ConfigSettingsConstants.FormatZso);
            parallelism ??= ConfigSettingsDefaults.GetDefaultParallelism(SystemType.PS2).ToString();

            // FIXED: Include the level in the format string (was missing before)
            return $"{ConfigSettingsConstants.FormatZso}:{level}:{blockSize}:{parallelism}";
        }

        // ======= End CSO/CSO2/ZSO Format Generation =======

        // ======= CUE Format Generation =======

        /// <summary>
        /// Generates a properly formatted CUE configuration string with defaults
        /// </summary>
        public static string GenerateCueFormatString(string cueType = null, string binary = null, string audio = null, string sub = null)
        {
            cueType ??= ConfigSettingsDefaults.GetDefaultCueType();
            binary ??= ConfigSettingsDefaults.GetDefaultBinaryExtension();
            audio ??= ConfigSettingsDefaults.GetDefaultAudioExtension();
            sub ??= ConfigSettingsDefaults.GetDefaultSubExtension();

            // Generate 5 parts: cue:type:binary:audio:sub
            return $"{ConfigSettingsConstants.FormatCue}:{cueType}:{binary}:{audio}:{sub}";
        }

        // ======= End CUE Format Generation =======

        // ======= Extract Configuration Generation =======

        /// <summary>
        /// Generates extract configuration string from components
        /// </summary>
        public static string GenerateExtractConfiguration(ExtractConfiguration config)
        {
            StringBuilder flags = new StringBuilder();

            if (config.IsForensic) flags.Append(ConfigSettingsConstants.ExtractFlagForensic);
            if (config.IsCaseInsensitive) flags.Append(ConfigSettingsConstants.ExtractFlagCaseInsensitive);
            if (config.IsMaskToRegex) flags.Append(ConfigSettingsConstants.ExtractFlagMaskToRegex);
            if (config.IsRecursive) flags.Append(ConfigSettingsConstants.ExtractFlagRecursive);

            return $"{flags}:{config.Pattern}";
        }

        /// <summary>
        /// Generates a properly formatted extract configuration string with defaults
        /// </summary>
        public static string GenerateExtractFormatString(string type = null, bool matchCase = false, bool forensic = false, string searchTerm = null)
        {
            if (forensic)
                return ConfigSettingsConstants.ExtractFlagForensic;

            type ??= ConfigSettingsDefaults.GetDefaultExtractType();
            searchTerm ??= ConfigSettingsDefaults.GetDefaultExtractSearchTerm();

            string flags = $"{type}{(matchCase ? "" : ConfigSettingsConstants.ExtractFlagCaseInsensitive)}";
            return $"{flags}:{searchTerm}";
        }

        // ======= End Extract Configuration Generation =======

        // ======= Dedupe Configuration Generation =======

        /// <summary>
        /// Generates a dedupe configuration string from component values.
        /// Format: setName:shardSize:blockSize:autoCreateAux — trailing empty parts are trimmed.
        /// </summary>
        public static string GenerateDedupeFormatString(string setName = null, string shardSize = null, string blockSize = null, string autoCreateAux = null)
        {
            string[] parts = new string[4];
            parts[0] = setName ?? string.Empty;
            parts[1] = shardSize ?? string.Empty;
            parts[2] = blockSize ?? string.Empty;
            parts[3] = autoCreateAux ?? string.Empty;

            // Trim trailing empty parts
            int last = parts.Length - 1;
            while (last >= 0 && string.IsNullOrEmpty(parts[last]))
                last--;

            if (last < 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i <= last; i++)
            {
                if (i > 0)
                    sb.Append(':');
                sb.Append(parts[i]);
            }

            return sb.ToString();
        }

        // ======= End Dedupe Configuration Generation =======
    }
}
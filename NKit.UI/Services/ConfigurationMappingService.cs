using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NKit.Ui.Services
{
    /// <summary>
    /// Maps between the core configuration format strings and UI model properties
    /// using the centralized ConfigSettings classes for proper parsing and generation.
    /// Streamlined to only include methods actually used by NKitSettings.
    /// </summary>
    public static class ConfigurationMappingService
    {
        /// <summary>
        /// Maps from core convert format string to UI model properties
        /// Handles both single formats and dual formats (single/indexed)
        /// </summary>
        public static void MapFromConvertFormat(string convertFormat, NKitSettings settings, bool suppressEvents = false)
        {
            if (string.IsNullOrEmpty(convertFormat))
            {
                rebuildFromSystemDefaults(settings, suppressEvents);
                return;
            }

            // Validate format compatibility with system BEFORE processing
            if (!IsFormatCompatibleWithSystem(convertFormat, settings.System))
            {
                // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Format '{convertFormat}' incompatible with system {settings.System}, using system defaults");
                rebuildFromSystemDefaults(settings, suppressEvents);
                return;
            }

            // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Processing '{convertFormat}' for {settings.System}");

            // Check if this is a dual format (contains '/')
            if (convertFormat.Contains('/'))
            {
                string[] parts = convertFormat.Split('/');
                if (parts.Length == 2)
                {
                    // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Dual format detected - Single: '{parts[0]}', Indexed: '{parts[1]}'");

                    // Parse each part FIRST to set individual properties
                    mapSingleFormat(parts[0], settings, suppressEvents);
                    mapIndexedFormat(parts[1], settings, suppressEvents);

                    // THEN set the format name properties
                    settings.ConvertSingleFormat = parts[0].Split(':')[0];
                    settings.ConvertIndexedFormat = parts[1].Split(':')[0];
                }
                else
                {
                    // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Invalid dual format '{convertFormat}', rebuilding from defaults");
                    rebuildFromSystemDefaults(settings, suppressEvents);
                }
            }
            else
            {
                string formatName = convertFormat.Split(':')[0];
                // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Single format detected: '{formatName}'");

                // For index-only systems (like Dreamcast), treat the format as indexed, not single
                if (!ConfigSettingsDefaults.IsSingleFormatSupported(settings.System) &&
                    ConfigSettingsDefaults.IsIndexedFormatSupported(settings.System))
                {
                    // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Index-only system detected, treating as indexed format");
                    mapIndexedFormat(convertFormat, settings, suppressEvents);
                    settings.ConvertSingleFormat = string.Empty;
                    settings.ConvertIndexedFormat = formatName;
                }
                else
                {
                    // Single format system or dual format system using single format only
                    mapSingleFormat(convertFormat, settings, suppressEvents);
                    settings.ConvertSingleFormat = formatName;

                    // For dual format systems, preserve indexed format if it exists
                    if (!ConfigSettingsDefaults.IsIndexedFormatSupported(settings.System))
                    {
                        settings.ConvertIndexedFormat = string.Empty;
                    }
                    else if (string.IsNullOrEmpty(settings.ConvertIndexedFormat))
                    {
                        // Set default indexed format for dual systems
                        string defaultIndexed = ConfigSettingsDefaults.GetDefaultIndexedFormat(settings.System);
                        settings.ConvertIndexedFormat = defaultIndexed;
                        // System.Diagnostics.Debug.WriteLine($"MapFromConvertFormat: Set default indexed format '{defaultIndexed}' for dual system");
                    }
                }
            }
        }

        /// <summary>
        /// Maps from UI model properties to core convert format string
        /// Joins single and indexed formats with /
        /// </summary>
        public static string MapToConvertFormat(NKitSettings settings)
        {
            // Handle index-only systems (like Dreamcast) that have no single format
            if (string.IsNullOrEmpty(settings.ConvertSingleFormat))
            {
                // Check if this is an index-only system with indexed format
                if (!string.IsNullOrEmpty(settings.ConvertIndexedFormat) &&
                    !ConfigSettingsDefaults.IsSingleFormatSupported(settings.System) &&
                    ConfigSettingsDefaults.IsIndexedFormatSupported(settings.System))
                {
                    // Return just the indexed format string for index-only systems
                    return generateIndexedFormat(settings);
                }

                return string.Empty;
            }

            if (!string.IsNullOrEmpty(settings.ConvertIndexedFormat))
                return $"{generateSingleFormat(settings)}/{generateIndexedFormat(settings)}";

            return generateSingleFormat(settings);
        }

        // ======= Private Helper Methods =======

        private static void mapSingleFormat(string format, NKitSettings settings, bool suppressEvents = false)
        {
            // Clear only single format properties to prevent bleed between formats
            clearSingleFormatProperties(settings);

            try
            {
                object config = ConfigSettingsFormatParser.ParseFormatConfiguration(format, settings.System);

                switch (config)
                {
                    case RvzFormatConfiguration rvz:
                        settings.ConvertEncoding = rvz.Encoding.ToString().ToLower();

                        // Only set compression level if encoding actually uses compression
                        if (rvz.Encoding != RvzEncodingType.None)
                        {
                            settings.ConvertLevel = rvz.CompressionLevel.ToString();
                        }

                        settings.ConvertBlockSize = ConfigSettingsFormatParser.ParseBlockSizeToString(rvz.BlockSizeBytes);
                        settings.ConvertParallelism = rvz.Parallelism.ToString();
                        break;

                    case CsoFormatConfiguration cso:
                        string level = cso.ContainerTypeString.ToLowerInvariant() == ConfigSettingsConstants.FormatZso
                            ? cso.Lz4Level.ToString()
                            : cso.DeflateLevel.ToString();
                        settings.ConvertLevel = level;
                        settings.ConvertBlockSize = ConfigSettingsFormatParser.ParseBlockSizeToString(cso.BlockSizeBytes);
                        settings.ConvertParallelism = cso.Parallelism.ToString();
                        break;

                    case WbfsFormatConfiguration wbfs:
                        settings.ConvertLossless = wbfs.Lossless;
                        break;

                    case CisoFormatConfiguration ciso:
                        settings.ConvertLossless = ciso.Lossless;
                        break;

                    case CueFormatConfiguration cue:
                        settings.ConvertCueType = cue.CueType;
                        settings.ConvertBinary = cue.BinaryExtension;
                        settings.ConvertAudio = cue.AudioExtension;
                        break;
                }
            }
            catch (Exception)
            {
                // If parsing fails, properties are already cleared above
            }
        }

        private static void mapIndexedFormat(string format, NKitSettings settings, bool suppressEvents = false)
        {
            // Clear only indexed format properties
            clearIndexedFormatProperties(settings);

            try
            {
                object config = ConfigSettingsFormatParser.ParseFormatConfiguration(format, settings.System);

                if (config is CueFormatConfiguration cue)
                {
                    settings.ConvertCueType = cue.CueType;
                    settings.ConvertBinary = cue.BinaryExtension;
                    settings.ConvertAudio = cue.AudioExtension;
                }
            }
            catch (Exception)
            {
                // If parsing fails for indexed format, properties are already cleared above
            }
        }

        /// <summary>
        /// Clears only single format specific properties, preserving indexed format properties
        /// </summary>
        private static void clearSingleFormatProperties(NKitSettings settings)
        {
            settings.ConvertEncoding = string.Empty;
            settings.ConvertLevel = string.Empty;
            settings.ConvertBlockSize = string.Empty;
            settings.ConvertParallelism = string.Empty;
            settings.ConvertLossless = false;
        }

        /// <summary>
        /// Clears only indexed format specific properties, preserving single format properties
        /// </summary>
        private static void clearIndexedFormatProperties(NKitSettings settings)
        {
            settings.ConvertCueType = string.Empty;
            settings.ConvertBinary = string.Empty;
            settings.ConvertAudio = string.Empty;
        }

        private static string generateSingleFormat(NKitSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ConvertSingleFormat))
                return string.Empty;

            string baseFormat = settings.ConvertSingleFormat.Split(':')[0].ToLower();

            return baseFormat switch
            {
                ConfigSettingsConstants.FormatRvz => ConfigSettingsFormatGenerator.GenerateRvzFormatString(
                    Enum.TryParse<RvzEncodingType>(settings.ConvertEncoding, true, out RvzEncodingType enc) ? enc : RvzEncodingType.ZStd,
                    int.TryParse(settings.ConvertLevel, out int lvl) ? lvl : null,
                    settings.ConvertBlockSize,
                    int.TryParse(settings.ConvertParallelism, out int par) ? par : null),

                ConfigSettingsConstants.FormatCso => ConfigSettingsFormatGenerator.GenerateCsoFormatString(
                    settings.ConvertLevel,
                    settings.ConvertBlockSize,
                    settings.ConvertParallelism),

                ConfigSettingsConstants.FormatCso2 => ConfigSettingsFormatGenerator.GenerateCso2FormatString(
                    settings.ConvertLevel,
                    settings.ConvertBlockSize,
                    settings.ConvertParallelism),

                ConfigSettingsConstants.FormatZso => ConfigSettingsFormatGenerator.GenerateZsoFormatString(
                    settings.ConvertLevel,
                    settings.ConvertBlockSize,
                    settings.ConvertParallelism),

                ConfigSettingsConstants.FormatWbfs => ConfigSettingsFormatGenerator.GenerateWbfsFormatString(
                    settings.ConvertLossless),

                ConfigSettingsConstants.FormatCiso => ConfigSettingsFormatGenerator.GenerateCisoFormatString(
                    settings.ConvertLossless),

                // For simple formats without parameters, return just the format name
                ConfigSettingsConstants.FormatIso or
                ConfigSettingsConstants.FormatApp or
                ConfigSettingsConstants.FormatWux or
                ConfigSettingsConstants.FormatDecIso or
                ConfigSettingsConstants.FormatGdi => baseFormat,

                _ => settings.ConvertSingleFormat // Fallback for unknown formats
            };
        }

        private static string generateIndexedFormat(NKitSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ConvertIndexedFormat))
                return string.Empty;

            string baseFormat = settings.ConvertIndexedFormat.Split(':')[0].ToLower();

            if (baseFormat == ConfigSettingsConstants.FormatCue)
            {
                return ConfigSettingsFormatGenerator.GenerateCueFormatString(
                    settings.ConvertCueType,
                    settings.ConvertBinary,
                    settings.ConvertAudio,
                    ConfigSettingsDefaults.GetDefaultSubExtension());
            }

            return settings.ConvertIndexedFormat;
        }

        /// <summary>
        /// Rebuilds format settings from system defaults instead of clearing them
        /// This prevents persistent blank states that affect all dual systems
        /// </summary>
        private static void rebuildFromSystemDefaults(NKitSettings settings, bool suppressEvents)
        {
            // System.Diagnostics.Debug.WriteLine($"rebuildFromSystemDefaults: Rebuilding for {settings.System}");

            try
            {
                // Get the full default format for this system
                string defaultFullFormat = ConfigSettingsDefaults.GetFullDefaultFormat(settings.System);
                // System.Diagnostics.Debug.WriteLine($"rebuildFromSystemDefaults: Using default format '{defaultFullFormat}'");

                // Recursively call MapFromConvertFormat with the default format
                MapFromConvertFormat(defaultFullFormat, settings, suppressEvents);
            }
            catch // (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"rebuildFromSystemDefaults: Error rebuilding from defaults: {ex.Message}");

                // Last resort: Set minimal valid defaults
                if (ConfigSettingsDefaults.IsSingleFormatSupported(settings.System))
                {
                    string defaultSingle = ConfigSettingsDefaults.GetDefaultSingleFormat(settings.System);
                    settings.ConvertSingleFormat = defaultSingle;
                }

                if (ConfigSettingsDefaults.IsIndexedFormatSupported(settings.System))
                {
                    string defaultIndexed = ConfigSettingsDefaults.GetDefaultIndexedFormat(settings.System);
                    settings.ConvertIndexedFormat = defaultIndexed;
                }

                // Set basic defaults for format-specific properties
                settings.ConvertBlockSize = ConfigSettingsDefaults.GetDefaultBlockSize(settings.System);
                settings.ConvertParallelism = ConfigSettingsDefaults.GetDefaultParallelism(settings.System).ToString();
                settings.ConvertCueType = ConfigSettingsDefaults.GetDefaultCueType();
                settings.ConvertBinary = ConfigSettingsDefaults.GetDefaultBinaryExtension();
                settings.ConvertAudio = ConfigSettingsDefaults.GetDefaultAudioExtension();
            }
        }

        /// <summary>
        /// Checks if a format string is compatible with the given system's capabilities
        /// </summary>
        private static bool IsFormatCompatibleWithSystem(string formatString, SystemType system)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                return false;

            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Handle dual format strings (e.g., "cso:9:2kb:4/cue:split:bin:bin")
            if (formatString.Contains('/'))
            {
                string[] parts = formatString.Split('/');
                if (parts.Length != 2) return false;

                string singlePart = parts[0].Trim();
                string indexedPart = parts[1].Trim();

                // Check single format part
                if (!string.IsNullOrEmpty(singlePart))
                {
                    string singleFormatName = singlePart.Split(':')[0].ToLowerInvariant();
                    if (!supportedFormats.Contains(singleFormatName))
                    {
                        // System.Diagnostics.Debug.WriteLine($"IsFormatCompatibleWithSystem: Single format '{singleFormatName}' not supported by {system}");
                        return false;
                    }
                }

                // Check indexed format part
                if (!string.IsNullOrEmpty(indexedPart))
                {
                    string indexedFormatName = indexedPart.Split(':')[0].ToLowerInvariant();
                    if (!supportedFormats.Contains(indexedFormatName) || !ConfigSettingsDefaults.IsIndexedFormat(indexedFormatName))
                    {
                        // System.Diagnostics.Debug.WriteLine($"IsFormatCompatibleWithSystem: Indexed format '{indexedFormatName}' not supported by {system}");
                        return false;
                    }
                }

                return true;
            }
            else
            {
                // Handle single format strings (e.g., "rvz:zstd:19:128kb:16")
                string formatName = formatString.Split(':')[0].ToLowerInvariant();
                bool isSupported = supportedFormats.Contains(formatName);

                if (!isSupported)
                {
                    // System.Diagnostics.Debug.WriteLine($"IsFormatCompatibleWithSystem: Format '{formatName}' not supported by {system}");
                }

                return isSupported;
            }
        }

        // ======= End Private Helper Methods =======
    }
}
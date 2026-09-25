using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Provides lists and ranges of valid configuration values for UI binding.
    /// Returns read-only collections of supported formats, compression levels, block sizes, etc.
    /// </summary>
    public static class ConfigSettingsRanges
    {
        // ======= Format Support =======

        /// <summary>
        /// Gets all Dual Index supported conversion formats for a given system
        /// </summary>
        public static IReadOnlyList<string> GetSupportedDualIndexFormats(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.PS3 or SystemType.PS1 or SystemType.PS2 or SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD or SystemType.Default => new[]
                {
                    ConfigSettingsConstants.FormatCue,
                },

                SystemType.Dreamcast => new[]
                {
                    ConfigSettingsConstants.FormatCue,
                    ConfigSettingsConstants.FormatGdi
                },

                _ => Array.Empty<string>()
            };
        }

        /// <summary>
        /// Gets all Dual Single supported conversion formats for a given system
        /// </summary>
        public static IReadOnlyList<string> GetSupportedDualSingleFormats(SystemType systemType)
        {
            // get all supported formats then remove those that exist as dual index formats
            List<string> allFormats = GetSupportedFormats(systemType).ToList();
            IReadOnlyList<string> dualIndexFormats = GetSupportedDualIndexFormats(systemType);
            foreach (string format in dualIndexFormats)
                allFormats.Remove(format);

            return allFormats;
        }

        /// <summary>
        /// Gets all supported conversion formats for a given system
        /// </summary>
        public static IReadOnlyList<string> GetSupportedFormats(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => new[]
                {
                    ConfigSettingsConstants.FormatIso,
                    ConfigSettingsConstants.FormatRvz,
                    ConfigSettingsConstants.FormatWbfs,
                    ConfigSettingsConstants.FormatCiso
                },

                SystemType.PS1 or SystemType.PS2 or SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD or SystemType.Default => new[]
                {
                    ConfigSettingsConstants.FormatCue,
                    ConfigSettingsConstants.FormatCso,
                    ConfigSettingsConstants.FormatCso2,
                    ConfigSettingsConstants.FormatZso,
                    ConfigSettingsConstants.FormatIso
                },

                SystemType.PS3 => new[]
                {
                    ConfigSettingsConstants.FormatCue,
                    ConfigSettingsConstants.FormatCso,
                    ConfigSettingsConstants.FormatCso2,
                    ConfigSettingsConstants.FormatZso,
                    ConfigSettingsConstants.FormatDecIso,
                    ConfigSettingsConstants.FormatIso
                },

                SystemType.PSP => new[]
                {
                    ConfigSettingsConstants.FormatCso,
                    ConfigSettingsConstants.FormatCso2,
                    ConfigSettingsConstants.FormatZso,
                    ConfigSettingsConstants.FormatIso
                },

                SystemType.XBox or SystemType.XBox360 => new[]
                {
                    ConfigSettingsConstants.FormatIso,
                    ConfigSettingsConstants.FormatCso,
                    ConfigSettingsConstants.FormatCso2,
                    ConfigSettingsConstants.FormatZso
                },

                SystemType.WiiU => new[]
                {
                    ConfigSettingsConstants.FormatApp,
                    ConfigSettingsConstants.FormatTmd,
                    ConfigSettingsConstants.FormatIso,
                    ConfigSettingsConstants.FormatWux
                },

                SystemType.Dreamcast => new[]
                {
                    ConfigSettingsConstants.FormatCue,
                    ConfigSettingsConstants.FormatGdi
                },

                _ => Array.Empty<string>()
            };
        }

        /// <summary>
        /// Gets all supported format names across all systems
        /// </summary>
        public static IReadOnlyList<string> GetAllSupportedFormats()
        {
            HashSet<string> allFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SystemType systemType in Enum.GetValues(typeof(SystemType)))
            {
                IReadOnlyList<string> formats = GetSupportedFormats(systemType);
                foreach (string format in formats)
                {
                    allFormats.Add(format);
                }
            }

            return allFormats.OrderBy(f => f).ToList();
        }

        // ======= End Format Support =======

        // ======= Block Size Ranges =======

        // ======= Block Sizes =======

        /// <summary>
        /// Gets supported block sizes for a specific format
        /// </summary>
        public static IReadOnlyList<string> GetBlockSizes(string format)
        {
            return format?.ToLowerInvariant() switch
            {
                ConfigSettingsConstants.FormatRvz => GetRvzBlockSizes(),
                ConfigSettingsConstants.FormatWbfs => GetWbfsBlockSizes(),
                ConfigSettingsConstants.FormatCiso => GetCisoBlockSizes(),
                ConfigSettingsConstants.FormatCso or
                ConfigSettingsConstants.FormatCso2 or
                ConfigSettingsConstants.FormatZso => GetCsoBlockSizes(),

                // Formats that don't use block sizes (uncompressed formats)
                ConfigSettingsConstants.FormatCue or
                ConfigSettingsConstants.FormatGdi or
                ConfigSettingsConstants.FormatIso or
                ConfigSettingsConstants.FormatDecIso or
                ConfigSettingsConstants.FormatApp or
                ConfigSettingsConstants.FormatWux => Array.Empty<string>(),

                _ => GetRvzBlockSizes() // Default fallback for unknown formats
            };
        }

        /// <summary>
        /// Gets supported block sizes for a specific system (legacy method)
        /// Uses the system's default format to determine appropriate block sizes
        /// </summary>
        public static IReadOnlyList<string> GetBlockSizes(SystemType systemType)
        {
            // Get the default format for this system to determine block size requirements
            string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(systemType);

            // Use format-specific block sizes if possible
            IReadOnlyList<string> formatBlockSizes = GetBlockSizes(defaultFormat);
            if (formatBlockSizes.Count > 0 && !formatBlockSizes.Contains("32kb")) // If it's not just the RVZ fallback
            {
                return formatBlockSizes;
            }

            // Fall back to system-specific logic for edge cases
            return systemType switch
            {
                // Systems that primarily use uncompressed formats but might have compression options
                SystemType.XBox or SystemType.XBox360 or SystemType.PS3 or SystemType.PSP or SystemType.PcEngine or SystemType.CDi or
                SystemType.Saturn or SystemType.SegaCD or SystemType.PS1 or SystemType.PS2 => GetCsoBlockSizes(), // 2KB minimum for any compression

                _ => GetRvzBlockSizes() // Safe default for unknown systems
            };
        }

        /// <summary>
        /// Gets supported block sizes for RVZ format (32KB minimum)
        /// </summary>
        public static IReadOnlyList<string> GetRvzBlockSizes()
        {
            return new[]
            {
                ConfigSettingsConstants.BlockSize32kb,
                ConfigSettingsConstants.BlockSize64kb,
                ConfigSettingsConstants.BlockSize128kb,
                ConfigSettingsConstants.BlockSize256kb,
                ConfigSettingsConstants.BlockSize512kb,
                ConfigSettingsConstants.BlockSize1mb,
                ConfigSettingsConstants.BlockSize2mb
            };
        }

        /// <summary>
        /// Gets supported block sizes for WBFS format
        /// </summary>
        public static IReadOnlyList<string> GetWbfsBlockSizes() => GetRvzBlockSizes();

        /// <summary>
        /// Gets supported block sizes for CISO format
        /// </summary>
        public static IReadOnlyList<string> GetCisoBlockSizes() => GetRvzBlockSizes();

        /// <summary>
        /// Gets supported block sizes for CSO/CSO2/ZSO formats (2KB minimum)
        /// </summary>
        public static IReadOnlyList<string> GetCsoBlockSizes()
        {
            return new[]
            {
                ConfigSettingsConstants.BlockSize2kb,
                ConfigSettingsConstants.BlockSize4kb,
                ConfigSettingsConstants.BlockSize8kb,
                ConfigSettingsConstants.BlockSize16kb,
                ConfigSettingsConstants.BlockSize32kb,
                ConfigSettingsConstants.BlockSize64kb,
                ConfigSettingsConstants.BlockSize128kb,
                ConfigSettingsConstants.BlockSize256kb,
                ConfigSettingsConstants.BlockSize512kb,
                ConfigSettingsConstants.BlockSize1mb,
                ConfigSettingsConstants.BlockSize2mb
            };
        }

        // ======= End Block Sizes =======

        // ======= Compression Levels =======

        /// <summary>
        /// Gets compression levels for ZStd encoding
        /// </summary>
        public static IReadOnlyList<int> GetZStdLevels()
        {
            return Enumerable.Range(
                ConfigSettingsConstants.MinZStdLevel,
                ConfigSettingsConstants.MaxZStdLevel
            ).ToList();
        }

        /// <summary>
        /// Gets compression levels for LZMA encoding
        /// </summary>
        public static IReadOnlyList<int> GetLzmaLevels()
        {
            return Enumerable.Range(
                ConfigSettingsConstants.MinLzmaLevel,
                ConfigSettingsConstants.MaxLzmaLevel
            ).ToList();
        }

        /// <summary>
        /// Gets compression levels for ZLib encoding
        /// </summary>
        public static IReadOnlyList<int> GetZlibLevels()
        {
            return Enumerable.Range(
                ConfigSettingsConstants.MinZlibLevel,
                ConfigSettingsConstants.MaxZlibLevel
            ).ToList();
        }

        /// <summary>
        /// Gets compression levels for LZ4 encoding
        /// </summary>
        public static IReadOnlyList<int> GetLz4Levels()
        {
            return Enumerable.Range(
                ConfigSettingsConstants.MinLz4Level,
                ConfigSettingsConstants.MaxLz4Level
            ).ToList();
        }

        /// <summary>
        /// Gets compression levels for a specific format and optional encoding type.
        /// For RVZ format, encoding parameter is required. For CSO/ZSO formats, encoding is ignored.
        /// Returns a single-element list containing 0 for formats that don't support compression.
        /// </summary>
        /// <param name="format">The format (e.g., "rvz", "cso", "zso", "cue", "iso")</param>
        /// <param name="encoding">Optional RVZ encoding type (required for RVZ format)</param>
        /// <returns>List of valid compression levels for the format/encoding combination, or [0] for non-compressed formats</returns>
        public static IReadOnlyList<int> GetCompressionLevels(string format, RvzEncodingType encoding = RvzEncodingType.None)
        {
            // Handle format-based compression levels
            if (!string.IsNullOrEmpty(format))
            {
                switch (format.ToLowerInvariant())
                {
                    case ConfigSettingsConstants.FormatRvz:
                        // For RVZ, use encoding to determine levels
                        return encoding switch
                        {
                            RvzEncodingType.ZStd => GetZStdLevels(),
                            RvzEncodingType.Lzma => GetLzmaLevels(),
                            _ => new[] { 0 } // None encoding has no levels
                        };

                    case ConfigSettingsConstants.FormatZso:
                        return GetLz4Levels();

                    case ConfigSettingsConstants.FormatCso:
                    case ConfigSettingsConstants.FormatCso2:
                        return GetZlibLevels();

                    // Formats that don't support compression
                    case ConfigSettingsConstants.FormatIso:
                    case ConfigSettingsConstants.FormatCue:
                    case ConfigSettingsConstants.FormatGdi:
                    case ConfigSettingsConstants.FormatWbfs:
                    case ConfigSettingsConstants.FormatCiso:
                    case ConfigSettingsConstants.FormatDecIso:
                    case ConfigSettingsConstants.FormatApp:
                    case ConfigSettingsConstants.FormatWux:
                        return new[] { 0 };
                }
            }

            // Fallback: if no format specified but encoding is, use encoding
            if (encoding != RvzEncodingType.None)
            {
                return encoding switch
                {
                    RvzEncodingType.ZStd => GetZStdLevels(),
                    RvzEncodingType.Lzma => GetLzmaLevels(),
                    _ => new[] { 0 }
                };
            }

            // Default fallback for unknown formats - no compression
            return new[] { 0 };
        }

        // ======= End Compression Levels =======

        // ======= RVZ Encoding Types =======

        /// <summary>
        /// Gets supported RVZ encoding types
        /// </summary>
        public static IReadOnlyList<RvzEncodingType> GetRvzEncodingTypes() => new[] { RvzEncodingType.None, RvzEncodingType.ZStd, RvzEncodingType.Lzma };

        // ======= End RVZ Encoding Types =======

        // ======= Parallelism =======

        /// <summary>
        /// Gets supported parallelism values for a system
        /// </summary>
        public static IReadOnlyList<int> GetParallelismValues(SystemType systemType)
        {
            int maxParallelism = systemType switch
            {
                SystemType.GameCube or SystemType.Wii => ConfigSettingsConstants.MaxNintendoParallelism,
                SystemType.PS3 or SystemType.PSP => ConfigSettingsConstants.MaxSonyParallelism,
                SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.MaxSonyParallelism, // Use Sony parallelism range for Xbox systems
                _ => ConfigSettingsConstants.MaxNintendoParallelism
            };

            return Enumerable.Range(1, maxParallelism).ToList();
        }

        // ======= End Parallelism =======

        // ======= CUE Configuration =======

        /// <summary>
        /// Gets supported CUE types
        /// </summary>
        public static IReadOnlyList<string> GetCueTypes()
        {
            return new[]
            {
                ConfigSettingsConstants.CueTypeSplit,
                ConfigSettingsConstants.CueTypeJoined
            };
        }

        /// <summary>
        /// Gets supported binary extensions
        /// </summary>
        public static IReadOnlyList<string> GetBinaryExtensions()
        {
            return new[]
            {
                ConfigSettingsConstants.BinaryExtensionBin,
                ConfigSettingsConstants.BinaryExtensionImg,
                ConfigSettingsConstants.BinaryExtensionIso
            };
        }

        /// <summary>
        /// Gets supported audio extensions
        /// </summary>
        public static IReadOnlyList<string> GetAudioExtensions()
        {
            return new[]
            {
                ConfigSettingsConstants.AudioExtensionBin,
                ConfigSettingsConstants.AudioExtensionFlac,
                ConfigSettingsConstants.AudioExtensionWav,
                ConfigSettingsConstants.AudioExtensionRaw  // Add the missing 'raw' extension used by GDI
            };
        }

        // ======= End CUE Configuration =======

        // ======= Extract Configuration =======

        /// <summary>
        /// Gets supported extract types
        /// </summary>
        public static IReadOnlyList<string> GetExtractTypes()
        {
            return new[]
            {
                ConfigSettingsConstants.ExtractTypeMatch,
                ConfigSettingsConstants.ExtractTypeFile,
                ConfigSettingsConstants.ExtractTypeImage
            };
        }

        // ======= End Extract Configuration =======

        // ======= Log Levels =======

        /// <summary>
        /// Gets supported log levels
        /// </summary>
        public static IReadOnlyList<LogLevel> GetLogLevels()
        {
            return new[]
            {
                LogLevel.None,
                LogLevel.Error,
                LogLevel.Warning,
                LogLevel.Info,
                LogLevel.Detail,
                LogLevel.Trace
            };
        }

        // ======= End Log Levels =======

        // ======= End Block Size Ranges =======
    }
}
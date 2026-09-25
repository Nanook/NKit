using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Parses format configuration strings from YAML files.
    /// Examples: "rvz:zstd:19:128k:16", "cso:9:2k:4", "cue:split:bin:bin"
    /// </summary>
    public static class ConfigSettingsFormatParser
    {
        // ======= Block Size Parsing =======

        /// <summary>
        /// Converts a block size string to bytes
        /// </summary>
        public static int ParseBlockSizeToBytes(string blockSize, SystemType systemType)
        {
            // Get system-appropriate default
            int defaultSize = systemType switch
            {
                SystemType.GameCube or SystemType.Wii => 0x20000, // 128KB
                _ => 0x800 // 2KB for Sony systems
            };

            if (string.IsNullOrWhiteSpace(blockSize))
                return defaultSize;

            Match match = Regex.Match(blockSize.ToLower(), @"^([0-9]+)(kb?|mb?)?$");
            if (!match.Success)
                return defaultSize;

            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value;

            return value * (unit == "" ? 1 : (unit[0] == 'k' ? 0x400 : (0x400 * 0x400)));
        }

        /// <summary>
        /// Converts block size bytes back to string format (uses shorter forms like "2k" instead of "2kb")
        /// </summary>
        public static string ParseBlockSizeToString(int blockSizeBytes)
        {
            return blockSizeBytes switch
            {
                0x800 => "2kb",
                0x1000 => "4kb",
                0x2000 => "8kb",
                0x4000 => "16kb",
                0x8000 => "32kb",
                0x10000 => "64kb",
                0x20000 => "128kb",
                0x40000 => "256kb",
                0x80000 => "512kb",
                0x100000 => "1mb",
                0x200000 => "2mb",
                _ => "128kb"
            };
        }

        /// <summary>
        /// Converts block size bytes back to normalized string format (uses full forms like "2kb")
        /// </summary>
        public static string ParseBlockSizeToNormalizedString(int blockSizeBytes)
        {
            return blockSizeBytes switch
            {
                0x800 => ConfigSettingsConstants.BlockSize2kb,
                0x1000 => ConfigSettingsConstants.BlockSize4kb,
                0x2000 => ConfigSettingsConstants.BlockSize8kb,
                0x4000 => ConfigSettingsConstants.BlockSize16kb,
                0x8000 => ConfigSettingsConstants.BlockSize32kb,
                0x10000 => ConfigSettingsConstants.BlockSize64kb,
                0x20000 => ConfigSettingsConstants.BlockSize128kb,
                0x40000 => ConfigSettingsConstants.BlockSize256kb,
                0x80000 => ConfigSettingsConstants.BlockSize512kb,
                0x100000 => ConfigSettingsConstants.BlockSize1mb,
                0x200000 => ConfigSettingsConstants.BlockSize2mb,
                _ => ConfigSettingsConstants.BlockSize128kb
            };
        }

        // ======= End Block Size Parsing =======

        // ======= Helper Methods =======

        private static int parseLevel(string levelStr, int defaultLevel)
        {
            return !string.IsNullOrWhiteSpace(levelStr) && int.TryParse(levelStr, out int level)
                ? level
                : defaultLevel;
        }

        private static int parseParallelism(string parallelismStr, int defaultParallelism)
        {
            return !string.IsNullOrWhiteSpace(parallelismStr) && int.TryParse(parallelismStr, out int parallelism)
                ? parallelism
                : defaultParallelism;
        }

        private static string getRvzParameterByIndex(string[] parts, RvzEncodingType encoding, int desiredIndex)
        {
            // For "none" encoding, there's no compression level parameter, so indices shift
            if (encoding == RvzEncodingType.None && desiredIndex > 2)
            {
                int adjustedIndex = desiredIndex - 1;
                return parts.Length > adjustedIndex && !string.IsNullOrWhiteSpace(parts[adjustedIndex])
                    ? parts[adjustedIndex]
                    : "";
            }

            return parts.Length > desiredIndex && !string.IsNullOrWhiteSpace(parts[desiredIndex])
                ? parts[desiredIndex]
                : "";
        }

        // ======= End Helper Methods =======

        // ======= RVZ Format Parsing =======

        /// <summary>
        /// Parses RVZ format configuration (e.g., "rvz:zstd:19:128k:16")
        /// </summary>
        public static RvzFormatConfiguration ParseRvzFormat(string formatString, SystemType systemType)
        {
            string[] parts = formatString.Split(':');

            // Determine encoding type - default to ZStd if not specified
            RvzEncodingType encoding;
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                encoding = RvzEncodingType.ZStd;
            }
            else if (!Enum.TryParse<RvzEncodingType>(parts[1], true, out encoding))
            {
                throw new ArgumentException($"Invalid RVZ encoding '{parts[1]}'. Supported: {ConfigSettingsConstants.EncodingNone}, {ConfigSettingsConstants.EncodingZStd}, {ConfigSettingsConstants.EncodingLzma}");
            }

            int compressionLevel, parallelism, blockSizeBytes;
            List<string> warnings = new List<string>();

            if (parts.Length == 1 || (parts.Length == 2 && string.IsNullOrWhiteSpace(parts[1])))
            {
                // Use defaults
                compressionLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding);
                blockSizeBytes = ParseBlockSizeToBytes(ConfigSettingsDefaults.GetDefaultBlockSize(systemType, ConfigSettingsConstants.FormatRvz), systemType);
                parallelism = ConfigSettingsDefaults.GetDefaultParallelism(systemType);
            }
            else
            {
                // Parse explicit parameters
                string levelStr = getRvzParameterByIndex(parts, encoding, 2);
                string blockSizeStr = getRvzParameterByIndex(parts, encoding, 3);
                string parallelismStr = getRvzParameterByIndex(parts, encoding, 4);

                compressionLevel = parseLevel(levelStr, ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding));
                blockSizeBytes = ParseBlockSizeToBytes(!string.IsNullOrWhiteSpace(blockSizeStr)
                    ? blockSizeStr
                    : ConfigSettingsDefaults.GetDefaultBlockSize(systemType, ConfigSettingsConstants.FormatRvz), systemType);
                parallelism = parseParallelism(parallelismStr, ConfigSettingsDefaults.GetDefaultParallelism(systemType));

                // Validate
                if (encoding == RvzEncodingType.ZStd && (compressionLevel < ConfigSettingsConstants.MinZStdLevel || compressionLevel > ConfigSettingsConstants.MaxZStdLevel))
                    throw new ArgumentException($"ZStd compression level must be {ConfigSettingsConstants.MinZStdLevel}-{ConfigSettingsConstants.MaxZStdLevel}, got {compressionLevel}");

                if (encoding == RvzEncodingType.Lzma && (compressionLevel < ConfigSettingsConstants.MinLzmaLevel || compressionLevel > ConfigSettingsConstants.MaxLzmaLevel))
                    throw new ArgumentException($"LZMA compression level must be {ConfigSettingsConstants.MinLzmaLevel}-{ConfigSettingsConstants.MaxLzmaLevel}, got {compressionLevel}");

                if (!string.IsNullOrWhiteSpace(blockSizeStr) && !Regex.IsMatch(blockSizeStr, ConfigSettingsConstants.BlockSizePatternRvz, RegexOptions.IgnoreCase))
                    throw new ArgumentException($"Invalid RVZ block size '{blockSizeStr}'. {ConfigSettingsConstants.BlockSizeErrorRvz}");

                if (parallelism < 0 || parallelism > ConfigSettingsConstants.MaxParallelismValue)
                    throw new ArgumentException($"Parallelism must be 0-{ConfigSettingsConstants.MaxParallelismValue}, got {parallelism}");

                // Get warnings
                warnings.AddRange(ConfigSettingsFormatValidator.GetConfigurationWarnings(systemType, ConfigSettingsConstants.FormatRvz, levelStr, blockSizeStr));
            }

            return new RvzFormatConfiguration
            {
                Encoding = encoding,
                CompressionLevel = compressionLevel,
                BlockSizeBytes = blockSizeBytes,
                Parallelism = parallelism,
                Warnings = warnings
            };
        }

        // ======= End RVZ Format Parsing =======

        // ======= CSO/ZSO Format Parsing =======

        /// <summary>
        /// Parses CSO/ZSO format configuration (e.g., "cso:9:2k:4")
        /// </summary>
        public static CsoFormatConfiguration ParseCsoFormat(string formatString, SystemType systemType)
        {
            string[] parts = formatString.Split(':');
            string format = parts[0].ToLowerInvariant();

            string containerTypeString = string.Compare(parts[0], ConfigSettingsConstants.FormatZso, true) == 0
                ? ConfigSettingsConstants.FormatZso
                : ConfigSettingsConstants.FormatCso;
            int version = parts[0].EndsWith("2") ? 2 : 1;

            int deflateLevel, lz4Level, parallelism, blockSizeBytes;
            List<string> warnings = new List<string>();

            if (parts.Length == 1)
            {
                // Use defaults
                deflateLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatCso);
                lz4Level = ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatZso);
                blockSizeBytes = ParseBlockSizeToBytes(ConfigSettingsDefaults.GetDefaultBlockSize(systemType), systemType);
                parallelism = ConfigSettingsDefaults.GetDefaultParallelism(systemType);
            }
            else
            {
                // Parse explicit parameters
                string levelStr = parts.Length >= 2 ? parts[1] : "";
                string blockSizeStr = parts.Length >= 3 ? parts[2] : "";
                string parallelismStr = parts.Length >= 4 ? parts[3] : "";

                deflateLevel = parseLevel(levelStr, ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatCso));
                lz4Level = parseLevel(levelStr, ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatZso));
                blockSizeBytes = ParseBlockSizeToBytes(!string.IsNullOrWhiteSpace(blockSizeStr)
                    ? blockSizeStr
                    : ConfigSettingsDefaults.GetDefaultBlockSize(systemType), systemType);
                parallelism = parseParallelism(parallelismStr, ConfigSettingsDefaults.GetDefaultParallelism(systemType));

                // For CSO2, map the deflate level (1-9) to LZ4 range (4-12) by adding 3
                if (version == 2)
                    lz4Level = deflateLevel + 3;

                warnings.AddRange(ConfigSettingsFormatValidator.GetConfigurationWarnings(systemType, format, levelStr, blockSizeStr));
            }

            return new CsoFormatConfiguration
            {
                ContainerTypeString = containerTypeString,
                Version = version,
                DeflateLevel = deflateLevel,
                Lz4Level = lz4Level,
                BlockSizeBytes = blockSizeBytes,
                Parallelism = parallelism,
                Warnings = warnings
            };
        }

        // ======= End CSO/ZSO Format Parsing =======

        // ======= CUE Format Parsing =======

        /// <summary>
        /// Parses CUE format configuration (e.g., "cue:split:bin:bin")
        /// </summary>
        public static CueFormatConfiguration ParseCueFormat(string formatString, SystemType systemType)
        {
            string[] parts = formatString.Split(':');
            List<string> warnings = new List<string>();

            // Parse with provider defaults as fallback
            string cueType = parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1])
                ? parts[1]
                : ConfigSettingsDefaults.GetDefaultCueType();

            string binaryExtension = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
                ? parts[2]
                : ConfigSettingsDefaults.GetDefaultBinaryExtension();

            string audioExtension = parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3])
                ? parts[3]
                : ConfigSettingsDefaults.GetDefaultAudioExtension();

            string subType = parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4])
                ? parts[4]
                : ConfigSettingsConstants.DefaultCueSubType;

            string dataSize = parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5])
                ? parts[5]
                : "";

            // CRITICAL FIX: Special handling for Dreamcast should only apply defaults when parameters are NOT explicitly provided
            // This preserves parsed values like "joined" cueType or "img" binary extension
            if (systemType == SystemType.Dreamcast)
            {
                // Only override with Dreamcast defaults if the values were not explicitly provided in the format string
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    cueType = ConfigSettingsConstants.CueTypeSplit;

                if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2]))
                    binaryExtension = ConfigSettingsConstants.BinaryExtensionBin;

                if (parts.Length < 4 || string.IsNullOrWhiteSpace(parts[3]))
                    audioExtension = ConfigSettingsConstants.BinaryExtensionBin;

                // Always override these for Dreamcast regardless of input
                subType = "";
                dataSize = "";
            }

            warnings.AddRange(ConfigSettingsFormatValidator.GetConfigurationWarnings(systemType, parts[0], null, null));

            return new CueFormatConfiguration
            {
                FormatType = parts[0].ToLowerInvariant(),
                CueType = cueType,
                BinaryExtension = binaryExtension,
                AudioExtension = audioExtension,
                SubType = subType,
                DataSize = dataSize,
                Warnings = warnings
            };
        }

        // ======= End CUE Format Parsing =======

        // ======= Extract Configuration Parsing =======

        /// <summary>
        /// Parses extract configuration into its components (e.g., "mi:*")
        /// </summary>
        public static ExtractConfiguration ParseExtractConfiguration(string extractConfig)
        {
            if (string.IsNullOrWhiteSpace(extractConfig))
            {
                return new ExtractConfiguration
                {
                    IsForensic = false,
                    IsCaseInsensitive = true,
                    IsMaskToRegex = false,
                    IsRecursive = true,
                    Pattern = ConfigSettingsConstants.DefaultExtractPattern
                };
            }

            Match match = Regex.Match(extractConfig, @"^([fimr]*):(.*)$");
            if (!match.Success)
            {
                // Handle legacy format
                bool allValidFlags = extractConfig.All(c => $"{ConfigSettingsConstants.ExtractFlagForensic}{ConfigSettingsConstants.ExtractFlagCaseInsensitive}{ConfigSettingsConstants.ExtractFlagMaskToRegex}{ConfigSettingsConstants.ExtractFlagRecursive}".Contains(c));

                if (allValidFlags)
                {
                    return new ExtractConfiguration
                    {
                        IsForensic = extractConfig.Contains(ConfigSettingsConstants.ExtractFlagForensic),
                        IsCaseInsensitive = extractConfig.Contains(ConfigSettingsConstants.ExtractFlagCaseInsensitive),
                        IsMaskToRegex = extractConfig.Contains(ConfigSettingsConstants.ExtractFlagMaskToRegex),
                        IsRecursive = extractConfig.Contains(ConfigSettingsConstants.ExtractFlagRecursive),
                        Pattern = ConfigSettingsConstants.DefaultExtractPattern
                    };
                }
                else
                {
                    return new ExtractConfiguration
                    {
                        IsForensic = false,
                        IsCaseInsensitive = true,
                        IsMaskToRegex = false,
                        IsRecursive = true,
                        Pattern = extractConfig
                    };
                }
            }

            string flags = match.Groups[1].Value;
            string pattern = match.Groups[2].Value;

            return new ExtractConfiguration
            {
                IsForensic = flags.Contains(ConfigSettingsConstants.ExtractFlagForensic),
                IsCaseInsensitive = flags.Contains(ConfigSettingsConstants.ExtractFlagCaseInsensitive),
                IsMaskToRegex = flags.Contains(ConfigSettingsConstants.ExtractFlagMaskToRegex),
                IsRecursive = flags.Contains(ConfigSettingsConstants.ExtractFlagRecursive),
                Pattern = string.IsNullOrWhiteSpace(pattern) ? ConfigSettingsConstants.DefaultExtractPattern : pattern
            };
        }

        // ======= End Extract Configuration Parsing =======

        // ======= WBFS/CISO Format Parsing =======

        /// <summary>
        /// Parses WBFS format configuration (e.g., "wbfs:y" or "wbfs")
        /// </summary>
        public static WbfsFormatConfiguration ParseWbfsFormat(string formatString)
        {
            string[] parts = formatString.Split(':');

            bool lossless = true; // Default to lossless
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                string losslessStr = parts[1].ToLowerInvariant();
                lossless = losslessStr == ConfigSettingsConstants.LosslessTrue;
            }

            return new WbfsFormatConfiguration
            {
                Lossless = lossless
            };
        }

        /// <summary>
        /// Parses CISO format configuration (e.g., "ciso:n" or "ciso")
        /// </summary>
        public static CisoFormatConfiguration ParseCisoFormat(string formatString)
        {
            string[] parts = formatString.Split(':');

            bool lossless = true; // Default to lossless
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                string losslessStr = parts[1].ToLowerInvariant();
                lossless = losslessStr == ConfigSettingsConstants.LosslessTrue;
            }

            return new CisoFormatConfiguration
            {
                Lossless = lossless
            };
        }

        // ======= End WBFS/CISO Format Parsing =======

        // ======= Generic Format Parsing =======

        /// <summary>
        /// Parses any supported format configuration string and returns format-specific configuration
        /// </summary>
        public static object ParseFormatConfiguration(string formatString, SystemType systemType)
        {
            if (string.IsNullOrWhiteSpace(formatString))
                throw new ArgumentException("Format string cannot be empty");

            string formatType = formatString.Split(':')[0].ToLowerInvariant();

            return formatType switch
            {
                ConfigSettingsConstants.FormatRvz => ParseRvzFormat(formatString, systemType),
                ConfigSettingsConstants.FormatCue or ConfigSettingsConstants.FormatToc => ParseCueFormat(formatString, systemType),
                ConfigSettingsConstants.FormatCso or ConfigSettingsConstants.FormatCso2 or ConfigSettingsConstants.FormatZso => ParseCsoFormat(formatString, systemType),
                ConfigSettingsConstants.FormatWbfs => ParseWbfsFormat(formatString),
                ConfigSettingsConstants.FormatCiso => ParseCisoFormat(formatString),
                ConfigSettingsConstants.FormatGdi => new GdiFormatConfiguration { FormatType = formatType },
                ConfigSettingsConstants.FormatIso or ConfigSettingsConstants.FormatApp or ConfigSettingsConstants.FormatTmd or
                ConfigSettingsConstants.FormatWux or ConfigSettingsConstants.FormatDecIso => new SimpleFormatConfiguration { FormatType = formatType },
                _ => throw new ArgumentException($"Unknown or unsupported format '{formatType}'")
            };
        }

        // ======= End Generic Format Parsing =======

        // ======= Dedupe Configuration Parsing =======

        /// <summary>
        /// Parses a size string with unit suffix to bytes.
        /// Supports: B, KB/K/KiB, MB/M/MiB, GB/G/GiB, TB/T/TiB (case-insensitive).
        /// Returns 0 if the string is null/empty.
        /// </summary>
        public static long ParseSizeToBytes(string sizeText)
        {
            if (string.IsNullOrWhiteSpace(sizeText))
                return 0;

            string normalized = sizeText.Trim().ToLowerInvariant().Replace(" ", "");

            // Normalize iB suffixes and single-char suffixes
            normalized = normalized switch
            {
                var s when s.EndsWith("kib") => s.Substring(0, s.Length - 3) + "kb",
                var s when s.EndsWith("mib") => s.Substring(0, s.Length - 3) + "mb",
                var s when s.EndsWith("gib") => s.Substring(0, s.Length - 3) + "gb",
                var s when s.EndsWith("tib") => s.Substring(0, s.Length - 3) + "tb",
                var s when s.EndsWith("k") && !s.EndsWith("kb") => s.Substring(0, s.Length - 1) + "kb",
                var s when s.EndsWith("m") && !s.EndsWith("mb") => s.Substring(0, s.Length - 1) + "mb",
                var s when s.EndsWith("g") && !s.EndsWith("gb") => s.Substring(0, s.Length - 1) + "gb",
                var s when s.EndsWith("t") && !s.EndsWith("tb") => s.Substring(0, s.Length - 1) + "tb",
                _ => normalized
            };

            int unitStart = 0;
            while (unitStart < normalized.Length && (char.IsDigit(normalized[unitStart]) || normalized[unitStart] == '.'))
                unitStart++;

            if (unitStart == 0 || !long.TryParse(normalized.Substring(0, unitStart), out long value) || value < 0)
                return 0;

            string unit = normalized.Substring(unitStart);
            return unit switch
            {
                "" or "b" => value,
                "kb" => value * 1024L,
                "mb" => value * 1024L * 1024L,
                "gb" => value * 1024L * 1024L * 1024L,
                "tb" => value * 1024L * 1024L * 1024L * 1024L,
                _ => 0
            };
        }

        /// <summary>
        /// Returns true if <paramref name="sizeText"/> is a syntactically valid size token
        /// (i.e. a non-negative integer optionally followed by a recognised unit suffix).
        /// Used to distinguish an intentional zero ("0", "0g") from a garbage value.
        /// </summary>
        public static bool IsValidSizeToken(string sizeText)
        {
            if (string.IsNullOrWhiteSpace(sizeText))
                return false;

            string normalized = sizeText.Trim().ToLowerInvariant().Replace(" ", "");
            normalized = normalized switch
            {
                var s when s.EndsWith("kib") => s.Substring(0, s.Length - 3) + "kb",
                var s when s.EndsWith("mib") => s.Substring(0, s.Length - 3) + "mb",
                var s when s.EndsWith("gib") => s.Substring(0, s.Length - 3) + "gb",
                var s when s.EndsWith("tib") => s.Substring(0, s.Length - 3) + "tb",
                var s when s.EndsWith("k") && !s.EndsWith("kb") => s.Substring(0, s.Length - 1) + "kb",
                var s when s.EndsWith("m") && !s.EndsWith("mb") => s.Substring(0, s.Length - 1) + "mb",
                var s when s.EndsWith("g") && !s.EndsWith("gb") => s.Substring(0, s.Length - 1) + "gb",
                var s when s.EndsWith("t") && !s.EndsWith("tb") => s.Substring(0, s.Length - 1) + "tb",
                _ => normalized
            };

            int unitStart = 0;
            while (unitStart < normalized.Length && char.IsDigit(normalized[unitStart]))
                unitStart++;

            if (unitStart == 0 || !long.TryParse(normalized.Substring(0, unitStart), out long value) || value < 0)
                return false;

            string unit = normalized.Substring(unitStart);
            return unit is "" or "b" or "kb" or "mb" or "gb" or "tb";
        }

        /// <summary>
        /// Parses dedupe configuration string.
        /// Format: &lt;setName&gt;:&lt;shardSize&gt;:&lt;blockSize&gt;
        /// All parts are optional. A legacy 4th part (persistFs) is tolerated but ignored.
        /// Examples:
        ///   ""                     → all defaults
        ///   "mySet"                → setName=mySet, rest defaults
        ///   ":50g:64k"             → default setName, shardSize=50GiB, blockSize=64KiB
        ///   "mySet:100g:128k"      → all specified
        ///   "mySet:50g:64k:n"      → legacy 4th part silently discarded
        /// </summary>
        public static DedupeConfiguration ParseDedupeConfiguration(string formatString)
        {
            DedupeConfiguration config = new DedupeConfiguration();

            if (string.IsNullOrWhiteSpace(formatString))
                return config;

            string[] parts = formatString.Split(':');

            // Part 0: setName (optional)
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                config.SetName = parts[0].Trim();

            // Part 1: shardSize (optional, e.g. "50g", "100GiB", "0", "0g")
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                string shardPart = parts[1].Trim();
                long parsed = ParseSizeToBytes(shardPart);
                if (parsed > 0)
                    config.ShardSize = parsed;
                else if (parsed == 0 && IsValidSizeToken(shardPart))
                    config.ShardSize = 0; // Explicit zero = single embedded DB
                else if (parsed == 0 && !IsValidSizeToken(shardPart))
                    config.Warnings.Add($"Invalid shard size '{shardPart}', using default ({DedupeConfiguration.DefaultShardSize / (1024L * 1024L * 1024L)} GiB).");
            }

            // Part 2: blockSize (optional, e.g. "64k", "128kb")
            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                long parsed = ParseSizeToBytes(parts[2]);
                if (parsed > 0 && parsed <= int.MaxValue)
                    config.BlockSize = (int)parsed;
            }

            // Part 3: AutoCreateAux (repurposed from legacy persistFs)
            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                config.AutoCreateAux = parts[3].Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
            }

            // Part 4: AuxFilename (optional, user-supplied aux store name)
            if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
            {
                config.AuxFilename = parts[4].Trim();
            }

            return config;
        }

        // ======= End Dedupe Configuration Parsing =======
    }
}
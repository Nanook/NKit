namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Provides default values for NKit configuration settings.
    /// Matches defaults specified in defaults/nkit.yaml
    /// </summary>
    public static class ConfigSettingsDefaults
    {
        // ======= Format Defaults =======
        /// <summary>
        /// Gets the default format for a given system
        /// </summary>
        public static string GetDefaultFormat(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => ConfigSettingsConstants.FormatRvz,
                SystemType.PS3 => ConfigSettingsConstants.FormatDecIso,
                SystemType.PSP => ConfigSettingsConstants.FormatCso,
                SystemType.PS1 or SystemType.PS2 => ConfigSettingsConstants.FormatCso,
                SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.FormatIso,
                SystemType.WiiU => ConfigSettingsConstants.FormatWux,
                SystemType.Dreamcast => ConfigSettingsConstants.FormatCue, // Index-only system
                SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD => ConfigSettingsConstants.FormatIso, // Dual format systems with ISO primary
                _ => ConfigSettingsConstants.FormatIso
            };
        }

        /// <summary>
        /// Gets the full default format string with all parameters for a given system
        /// </summary>
        public static string GetFullDefaultFormat(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => "rvz:zstd:19:128kb:16",
                SystemType.PS3 => "deciso/cue:split:bin:bin:sub",
                SystemType.PSP => "cso:9:2kb:4",
                SystemType.PS1 or SystemType.PS2 => "cso:9:2kb:4/cue:split:bin:bin:sub",
                SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.FormatIso,
                SystemType.WiiU => ConfigSettingsConstants.FormatWux,
                SystemType.Dreamcast => "cue:split:bin:bin:sub", // Index-only system - single format string, not dual
                SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD => "iso/cue:split:bin:bin:sub", // Dual format systems
                _ => "iso/cue:split:bin:bin:sub"
            };
        }

        /// <summary>
        /// Gets the full default format string with all parameters for a specific format
        /// </summary>
        public static string GetFullDefaultFormat(SystemType systemType, string format)
        {
            if (string.IsNullOrEmpty(format))
                return GetFullDefaultFormat(systemType);

            // Format-specific defaults based on SystemCapabilitiesTestData
            return format.ToLowerInvariant() switch
            {
                "rvz" => "rvz:zstd:19:128kb:16",
                "cso" => "cso:9:2kb:4",
                "cso2" => "cso2:9:2kb:4",
                "zso" => "zso:12:2kb:4",
                "iso" => "iso",
                "deciso" => "deciso",
                "wbfs" => "wbfs",
                "ciso" => "ciso",
                "app" => "app",
                "wux" => "wux",
                "gdi" => "gdi",
                "cue" => "cue:split:bin:bin:sub",
                _ => GetFullDefaultFormat(systemType)
            };
        }

        // ======= End Format Defaults =======
        // ======= Block Size Defaults =======

        /// <summary>
        /// Gets default block size for a system and optional format
        /// </summary>
        public static string GetDefaultBlockSize(SystemType systemType, string format = null)
        {
            // If format is specified, use format-specific default
            if (!string.IsNullOrEmpty(format))
            {
                return format.ToLowerInvariant() switch
                {
                    ConfigSettingsConstants.FormatRvz or
                    ConfigSettingsConstants.FormatWbfs => ConfigSettingsConstants.BlockSize128kb,

                    ConfigSettingsConstants.FormatCiso => ConfigSettingsConstants.BlockSize128kb,

                    ConfigSettingsConstants.FormatCso or
                    ConfigSettingsConstants.FormatCso2 or
                    ConfigSettingsConstants.FormatZso => systemType switch
                    {
                        SystemType.PS3 => ConfigSettingsConstants.BlockSize16kb,
                        SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.BlockSize16kb, // Xbox systems use 16KB
                        _ => ConfigSettingsConstants.BlockSize2kb
                    },

                    // ISO format doesn't use block sizes, but return system default for consistency
                    ConfigSettingsConstants.FormatIso => GetSystemDefaultBlockSize(systemType),

                    _ => GetSystemDefaultBlockSize(systemType)
                };
            }

            // Fall back to system-based default
            return GetSystemDefaultBlockSize(systemType);
        }

        /// <summary>
        /// Gets default block size based on system type
        /// </summary>
        private static string GetSystemDefaultBlockSize(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => ConfigSettingsConstants.BlockSize128kb,
                SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.BlockSize16kb, // Xbox systems default is 16KB
                _ => ConfigSettingsConstants.BlockSize2kb,
            };
        }

        // ======= End Block Size Defaults =======
        // ======= Compression Level Defaults =======

        /// <summary>
        /// Gets default compression level for an RVZ encoding type
        /// </summary>
        public static int GetDefaultCompressionLevel(RvzEncodingType encoding)
        {
            return encoding switch
            {
                RvzEncodingType.ZStd => ConfigSettingsConstants.DefaultZStdLevel,
                RvzEncodingType.Lzma => ConfigSettingsConstants.DefaultLzmaLevel,
                _ => 0
            };
        }

        /// <summary>
        /// Gets default compression level for CSO/ZSO format types
        /// </summary>
        public static int GetDefaultCompressionLevel(string format)
        {
            return format?.ToLowerInvariant() switch
            {
                ConfigSettingsConstants.FormatZso => ConfigSettingsConstants.DefaultLz4Level,
                ConfigSettingsConstants.FormatCso or ConfigSettingsConstants.FormatCso2 => ConfigSettingsConstants.DefaultZLibLevel,
                _ => ConfigSettingsConstants.DefaultLzmaLevel
            };
        }

        // ======= End Compression Level Defaults =======
        // ======= Parallelism Defaults =======

        /// <summary>
        /// Gets default parallelism for a system
        /// </summary>
        public static int GetDefaultParallelism(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => ConfigSettingsConstants.DefaultNintendoParallelism,
                SystemType.PS3 or SystemType.PSP => ConfigSettingsConstants.DefaultSonyParallelism,
                SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.DefaultSonyParallelism, // Xbox systems use Sony parallelism settings
                _ => ConfigSettingsConstants.DefaultCoreParallelism
            };
        }

        // ======= End Parallelism Defaults =======
        // ======= Indexed Format Defaults =======

        /// <summary>
        /// Gets the default indexed format for systems that support indexed formats
        /// </summary>
        public static string GetDefaultIndexedFormat(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.PS1 or SystemType.PS2 or SystemType.PS3 or
                SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or
                SystemType.SegaCD or SystemType.Default => ConfigSettingsConstants.FormatCue,

                SystemType.Dreamcast => ConfigSettingsConstants.FormatCue, // Dreamcast can use both CUE and GDI, CUE is more common

                _ => ConfigSettingsConstants.FormatCue // Safe fallback
            };
        }

        /// <summary>
        /// Gets the default single format for systems that support single formats
        /// </summary>
        public static string GetDefaultSingleFormat(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => ConfigSettingsConstants.FormatRvz,
                SystemType.PS3 => ConfigSettingsConstants.FormatDecIso,
                SystemType.PSP => ConfigSettingsConstants.FormatCso,
                SystemType.PS1 or SystemType.PS2 => ConfigSettingsConstants.FormatCso,
                SystemType.XBox or SystemType.XBox360 => ConfigSettingsConstants.FormatIso,
                SystemType.WiiU => ConfigSettingsConstants.FormatWux,
                SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD => ConfigSettingsConstants.FormatIso, // These dual format systems default to ISO
                _ => ConfigSettingsConstants.FormatIso // Safe fallback
            };
        }

        // ======= End Indexed Format Defaults =======
        // ======= CUE/Audio Defaults =======

        /// <summary>
        /// Gets default CUE type
        /// </summary>
        public static string GetDefaultCueType() => ConfigSettingsConstants.CueTypeSplit;

        /// <summary>
        /// Gets default binary extension
        /// </summary>
        public static string GetDefaultBinaryExtension() => ConfigSettingsConstants.BinaryExtensionBin;

        /// <summary>
        /// Gets default audio extension
        /// </summary>
        public static string GetDefaultAudioExtension() => ConfigSettingsConstants.AudioExtensionBin;

        /// <summary>
        /// Gets default sub extension
        /// </summary>
        public static string GetDefaultSubExtension() => "sub";

        // ======= End CUE/Audio Defaults =======
        // ======= Extract Defaults =======

        /// <summary>
        /// Gets default extract type
        /// </summary>
        public static string GetDefaultExtractType() => ConfigSettingsConstants.ExtractTypeMatch;

        /// <summary>
        /// Gets default extract search term
        /// </summary>
        public static string GetDefaultExtractSearchTerm() => ConfigSettingsConstants.DefaultExtractSearchTerm;

        // ======= End Extract Defaults =======
        // ======= Log Level Defaults =======

        /// <summary>
        /// Gets default log level
        /// </summary>
        public static LogLevel GetDefaultLogLevel() => LogLevel.Info;

        // ======= End Log Level Defaults =======
        // ======= UI Defaults =======

        public static bool IsParallelismsSupported(string format) => IsBlockSizesSupported(format);

        public static bool IsFixSupported(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii or SystemType.Dreamcast or SystemType.PS3 => true,
                _ => false
            };
        }

        public static bool IsKeysSupported(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.PS3 or SystemType.WiiU => true,
                _ => false
            };
        }

        public static bool IsFixFilesSupported(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii or SystemType.PS3 => true,
                _ => false
            };
        }

        public static bool IsBlockSizesSupported(string format)
        {
            return format switch
            {
                ConfigSettingsConstants.FormatRvz or ConfigSettingsConstants.FormatCso or ConfigSettingsConstants.FormatCso2 or ConfigSettingsConstants.FormatZso => true,
                _ => false
            };
        }
        public static bool IsLevelsSupported(string format, string encoding)
        {
            return format switch
            {
                ConfigSettingsConstants.FormatRvz => encoding == ConfigSettingsConstants.EncodingZStd || encoding == ConfigSettingsConstants.EncodingLzma,
                ConfigSettingsConstants.FormatCso or ConfigSettingsConstants.FormatCso2 or ConfigSettingsConstants.FormatZso => true,
                _ => false
            };
        }

        public static bool IsEncodingTypeSupported(SystemType systemType, string format)
        {
            return systemType switch
            {
                SystemType.GameCube or SystemType.Wii => format == ConfigSettingsConstants.FormatRvz,
                _ => false
            };
        }

        public static bool IsLosslessSupported(string format) => format == ConfigSettingsConstants.FormatWbfs || format == ConfigSettingsConstants.FormatCiso;

        public static bool IsCueTypesSupported(string format) => format == ConfigSettingsConstants.FormatCue;

        public static bool IsBinaryExtensionsSupported(string format) => IsCueTypesSupported(format);

        public static bool IsAudioExtensionsSupported(string format) => IsCueTypesSupported(format);

        //public static bool IsDualFormatSupported(SystemType systemType)
        //{
        //    return systemType switch
        //    {
        //        SystemType.PS1 or SystemType.PS2 or SystemType.PS3 or SystemType.PcEngine or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD or SystemType.Default => true,
        //        _ => false
        //    };
        //}

        public static bool IsSingleFormatSupported(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.Dreamcast => false,
                _ => true
            };
        }

        public static bool IsIndexedFormatSupported(SystemType systemType)
        {
            return systemType switch
            {
                SystemType.PS1 or SystemType.PS2 or SystemType.PS3 or SystemType.PcEngine or SystemType.Dreamcast or SystemType.CDi or SystemType.Saturn or SystemType.SegaCD or SystemType.Default => true,
                _ => false
            };
        }

        public static bool IsIndexedFormat(string format)
        {
            return format switch
            {
                ConfigSettingsConstants.FormatCue or ConfigSettingsConstants.FormatGdi => true,
                _ => false
            };
        }

        public static bool IsSingleFormat(string format) => !IsIndexedFormat(format);

        /// <summary>
        /// Gets UI defaults for general settings when no system configuration is available.
        /// Matches defaults specified in defaults/nkit.yaml
        /// </summary>
        public static UiGeneralDefaults GetUiGeneralDefaults()
        {
            return new UiGeneralDefaults
            {
                R = true,
                Arc = true,
                V = Verify.Y,
                ConsoleLevel = LogLevel.Info,
                LogOutLevel = LogLevel.Info,
                Results = true,
                OutAsDatMatch = false,
                DeleteProcessed = false,
                SkipIfCompleted = false
            };
        }

        /// <summary>
        /// Gets UI defaults for conversion settings when no system configuration is available.
        /// Matches defaults from defaults/nkit.yaml
        /// </summary>
        public static UiConversionDefaults GetUiConversionDefaults(SystemType systemType)
        {
            string defaultFormat = GetDefaultFormat(systemType);

            // Create format-specific defaults
            string encoding;
            string level;

            switch (defaultFormat)
            {
                case ConfigSettingsConstants.FormatRvz:
                    encoding = ConfigSettingsConstants.EncodingZStd;
                    level = GetDefaultCompressionLevel(RvzEncodingType.ZStd).ToString();
                    break;

                case ConfigSettingsConstants.FormatCso:
                case ConfigSettingsConstants.FormatCso2:
                case ConfigSettingsConstants.FormatZso:
                    encoding = "";
                    level = GetDefaultCompressionLevel(defaultFormat).ToString();
                    break;

                case ConfigSettingsConstants.FormatCiso:
                    // CISO format uses lossless compression with 128KB block size
                    encoding = "";
                    level = "";
                    break;

                case ConfigSettingsConstants.FormatIso:
                case ConfigSettingsConstants.FormatCue:
                case ConfigSettingsConstants.FormatGdi:
                case ConfigSettingsConstants.FormatDecIso:
                default:
                    encoding = "";
                    level = "";
                    break;
            }

            return new UiConversionDefaults
            {
                Format = defaultFormat,
                Encoding = encoding,
                Level = level,
                BlockSize = GetDefaultBlockSize(systemType, defaultFormat),
                Parallelism = GetDefaultParallelism(systemType).ToString(),
                Lossless = defaultFormat == ConfigSettingsConstants.FormatCiso || defaultFormat == ConfigSettingsConstants.FormatWbfs, // RVZ doesn't use lossless flag
                CueType = GetDefaultCueType(),
                Binary = GetDefaultBinaryExtension(),
                Audio = GetDefaultAudioExtension()
            };
        }

        /// <summary>
        /// Gets UI defaults for extraction settings when no system configuration is available.
        /// Matches defaults from defaults/nkit.yaml (extract: mi:*)
        /// </summary>
        public static UiExtractionDefaults GetUiExtractionDefaults()
        {
            return new UiExtractionDefaults
            {
                Type = ConfigSettingsConstants.ExtractFlagMaskToRegex,
                MatchCase = false,
                Forensic = false,
                SearchTerm = "*"
            };
        }

        public static bool GetDefaultLossless(string format) => true;

        // ======= End UI Defaults =======
    }
}
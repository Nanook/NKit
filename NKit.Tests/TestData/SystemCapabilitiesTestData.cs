using Nanook.NKit;
using System.Linq;

namespace NKit.Tests.TestData
{
    /// <summary>
    /// Comprehensive test data model capturing the definitive system capabilities, format support,
    /// and expected behaviors for all NKit systems. This serves as the single source of truth
    /// for testing system switching, format validation, and UI behavior.
    /// </summary>
    public static class SystemCapabilitiesTestData
    {
        #region System Definitions

        /// <summary>
        /// Complete system capability definitions from the specification tables
        /// </summary>
        public static readonly SystemCapability[] SystemCapabilities = new[]
        {
            // Single-Format Systems (no indexed format support)
            new SystemCapability
            {
                System = SystemType.GameCube,
                Category = SystemCategory.SingleFormat,
                SupportedFormats = new[] { "iso", "rvz", "wbfs", "ciso" },
                SingleFormats = new[] { "iso", "rvz", "wbfs", "ciso" },
                IndexedFormats = new string[0],
                DefaultFormat = "rvz:zstd:19:128kb:16",
                DefaultSingleFormat = "rvz",
                DefaultIndexedFormat = null,
                MaxThreads = 32,
                DefaultThreads = 16
            },

            new SystemCapability
            {
                System = SystemType.Wii,
                Category = SystemCategory.SingleFormat,
                SupportedFormats = new[] { "iso", "rvz", "wbfs", "ciso" },
                SingleFormats = new[] { "iso", "rvz", "wbfs", "ciso" },
                IndexedFormats = new string[0],
                DefaultFormat = "rvz:zstd:19:128kb:16",
                DefaultSingleFormat = "rvz",
                DefaultIndexedFormat = null,
                MaxThreads = 32,
                DefaultThreads = 16
            },

            new SystemCapability
            {
                System = SystemType.PSP,
                Category = SystemCategory.SingleFormat,
                SupportedFormats = new[] { "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new string[0],
                DefaultFormat = "cso:9:2kb:4",
                DefaultSingleFormat = "cso",
                DefaultIndexedFormat = null,
                MaxThreads = 32,
                DefaultThreads = 4
            },

            new SystemCapability
            {
                System = SystemType.XBox,
                Category = SystemCategory.SingleFormat,
                SupportedFormats = new[] { "iso", "cso", "cso2", "zso" },
                SingleFormats = new[] { "iso", "cso", "cso2", "zso" },
                IndexedFormats = new string[0],
                DefaultFormat = "iso",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = null,
                MaxThreads = 32,
                DefaultThreads = 4
            },

            new SystemCapability
            {
                System = SystemType.XBox360,
                Category = SystemCategory.SingleFormat,
                SupportedFormats = new[] { "iso", "cso", "cso2", "zso" },
                SingleFormats = new[] { "iso", "cso", "cso2", "zso" },
                IndexedFormats = new string[0],
                DefaultFormat = "iso",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = null,
                MaxThreads = 32,
                DefaultThreads = 4
            },

            new SystemCapability
            {
                System = SystemType.WiiU,
                Category = SystemCategory.SingleFormat,
                SupportedFormats = new[] { "app", "iso", "wux" },
                SingleFormats = new[] { "app", "iso", "wux" },
                IndexedFormats = new string[0],
                DefaultFormat = "wux",
                DefaultSingleFormat = "wux",
                DefaultIndexedFormat = null,
                MaxThreads = 32,
                DefaultThreads = 16
            },
            
            // Index-Only Systems (no single format support)
            new SystemCapability
            {
                System = SystemType.Dreamcast,
                Category = SystemCategory.IndexOnly,
                SupportedFormats = new[] { "cue", "gdi" },
                SingleFormats = new string[0],
                IndexedFormats = new[] { "cue", "gdi" },
                DefaultFormat = "cue:split:bin:bin:sub",
                DefaultSingleFormat = null,
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 16
            },
            
            // Dual-Format Systems (both single and indexed support)
            new SystemCapability
            {
                System = SystemType.PS3,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "deciso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "deciso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "deciso/cue:split:bin:bin:sub", // PS3 is dual-format
                DefaultSingleFormat = "deciso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 4
            },

            new SystemCapability
            {
                System = SystemType.PS1,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "cso:9:2kb:4/cue:split:bin:bin:sub",
                DefaultSingleFormat = "cso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 4
            },

            new SystemCapability
            {
                System = SystemType.PS2,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "cso:9:2kb:4/cue:split:bin:bin:sub",
                DefaultSingleFormat = "cso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 4
            },

            new SystemCapability
            {
                System = SystemType.PcEngine,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "iso/cue:split:bin:bin:sub",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 16
            },

            new SystemCapability
            {
                System = SystemType.CDi,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "iso/cue:split:bin:bin:sub",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 16
            },

            new SystemCapability
            {
                System = SystemType.Saturn,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "iso/cue:split:bin:bin:sub",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 16
            },

            new SystemCapability
            {
                System = SystemType.SegaCD,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "iso/cue:split:bin:bin:sub",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 16
            },

            new SystemCapability
            {
                System = SystemType.Default,
                Category = SystemCategory.DualFormat,
                SupportedFormats = new[] { "cue", "cso", "cso2", "zso", "iso" },
                SingleFormats = new[] { "cso", "cso2", "zso", "iso" },
                IndexedFormats = new[] { "cue" },
                DefaultFormat = "iso/cue:split:bin:bin:sub",
                DefaultSingleFormat = "iso",
                DefaultIndexedFormat = "cue",
                MaxThreads = 32,
                DefaultThreads = 16
            }
        };

        #endregion

        #region Format Definitions

        /// <summary>
        /// Format capability definitions from the specification tables
        /// </summary>
        public static readonly FormatCapability[] FormatCapabilities = new[]
        {
            // RVZ format variations
            new FormatCapability
            {
                Format = "rvz",
                CompressionType = "zstd",
                LevelRange = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 },
                DefaultLevel = "19",
                BlockSizes = new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" },
                DefaultBlockSize = "128kb",
                DefaultParallelism = "16",
                HasEncoding = true,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "rvz",
                CompressionType = "lzma",
                LevelRange = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
                DefaultLevel = "5",
                BlockSizes = new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" },
                DefaultBlockSize = "128kb",
                DefaultParallelism = "16",
                HasEncoding = true,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "rvz",
                CompressionType = "none",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" },
                DefaultBlockSize = "128kb",
                DefaultParallelism = "16",
                HasEncoding = true,
                HasLossless = false,
                HasCueOptions = false
            },
            
            // CSO format variations
            new FormatCapability
            {
                Format = "cso",
                CompressionType = "zlib",
                LevelRange = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
                DefaultLevel = "9",
                BlockSizes = new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" },
                DefaultBlockSize = "2kb",
                DefaultParallelism = "4",
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "cso2",
                CompressionType = "zlib",
                LevelRange = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
                DefaultLevel = "9",
                BlockSizes = new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" },
                DefaultBlockSize = "2kb",
                DefaultParallelism = "4",
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "zso",
                CompressionType = "lz4",
                LevelRange = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 },
                DefaultLevel = "12",
                BlockSizes = new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" },
                DefaultBlockSize = "2kb",
                DefaultParallelism = "4",
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },
            
            // Simple formats with lossless flags
            new FormatCapability
            {
                Format = "wbfs",
                CompressionType = "lossless",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0], // WBFS block sizes are not configurable in NKit
                DefaultBlockSize = null,
                DefaultParallelism = null, // WBFS does not support parallelism
                HasEncoding = false,
                HasLossless = true,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "ciso",
                CompressionType = "lossless",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0], // CISO block sizes are not configurable in NKit
                DefaultBlockSize = null,
                DefaultParallelism = null, // CISO does not support parallelism
                HasEncoding = false,
                HasLossless = true,
                HasCueOptions = false
            },
            
            // Uncompressed formats
            new FormatCapability
            {
                Format = "iso",
                CompressionType = "none",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0],
                DefaultBlockSize = null,
                DefaultParallelism = null,
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "deciso",
                CompressionType = "none",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0],
                DefaultBlockSize = null,
                DefaultParallelism = null,
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "app",
                CompressionType = "none",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0],
                DefaultBlockSize = null,
                DefaultParallelism = null,
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "wux",
                CompressionType = "format specific",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0],
                DefaultBlockSize = null,
                DefaultParallelism = null,
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },

            new FormatCapability
            {
                Format = "gdi",
                CompressionType = "none",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0],
                DefaultBlockSize = null,
                DefaultParallelism = null,
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = false
            },
            
            // CUE format (indexed format with special options)
            new FormatCapability
            {
                Format = "cue",
                CompressionType = "none",
                LevelRange = new int[0],
                DefaultLevel = null,
                BlockSizes = new string[0],
                DefaultBlockSize = null,
                DefaultParallelism = null,
                HasEncoding = false,
                HasLossless = false,
                HasCueOptions = true
            }
        };

        #endregion

        #region CUE Format Options

        /// <summary>
        /// CUE format specific options from the specification
        /// </summary>
        public static readonly CueFormatOptions CueOptions = new CueFormatOptions
        {
            CueTypes = new[] { "split", "joined" },
            DefaultCueType = "split",
            BinaryExtensions = new[] { "bin", "img", "iso" },
            DefaultBinaryExtension = "bin",
            AudioExtensions = new[] { "bin", "wav", "flac", "raw" },
            DefaultAudioExtension = "bin",
            DefaultSubExtension = "sub"
        };

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get system capability by SystemType
        /// </summary>
        public static SystemCapability GetSystemCapability(SystemType system) => SystemCapabilities.FirstOrDefault(s => s.System == system);

        /// <summary>
        /// Get format capability by format name
        /// </summary>
        public static FormatCapability[] GetFormatCapabilities(string format) => FormatCapabilities.Where(f => f.Format.Equals(format, System.StringComparison.OrdinalIgnoreCase)).ToArray();

        /// <summary>
        /// Check if system supports single formats
        /// </summary>
        public static bool IsSingleFormatSupported(SystemType system)
        {
            SystemCapability capability = GetSystemCapability(system);
            return capability?.SingleFormats?.Length > 0;
        }

        /// <summary>
        /// Check if system supports indexed formats  
        /// </summary>
        public static bool IsIndexedFormatSupported(SystemType system)
        {
            SystemCapability capability = GetSystemCapability(system);
            return capability?.IndexedFormats?.Length > 0;
        }

        /// <summary>
        /// Get all single formats for a system
        /// </summary>
        public static string[] GetSingleFormats(SystemType system)
        {
            SystemCapability capability = GetSystemCapability(system);
            return capability?.SingleFormats ?? new string[0];
        }

        /// <summary>
        /// Get all indexed formats for a system
        /// </summary>
        public static string[] GetIndexedFormats(SystemType system)
        {
            SystemCapability capability = GetSystemCapability(system);
            return capability?.IndexedFormats ?? new string[0];
        }

        /// <summary>
        /// Get block sizes for a format
        /// </summary>
        public static string[] GetBlockSizes(string format)
        {
            FormatCapability[] capabilities = GetFormatCapabilities(format);
            return capabilities.FirstOrDefault()?.BlockSizes ?? new string[0];
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Represents the complete capability profile for a system
    /// </summary>
    public class SystemCapability
    {
        public SystemType System { get; set; }
        public SystemCategory Category { get; set; }
        public string[] SupportedFormats { get; set; }
        public string[] SingleFormats { get; set; }
        public string[] IndexedFormats { get; set; }
        public string DefaultFormat { get; set; }
        public string DefaultSingleFormat { get; set; }
        public string DefaultIndexedFormat { get; set; }
        public int MaxThreads { get; set; }
        public int DefaultThreads { get; set; }
    }

    /// <summary>
    /// Represents the capability profile for a format
    /// </summary>
    public class FormatCapability
    {
        public string Format { get; set; }
        public string CompressionType { get; set; }
        public int[] LevelRange { get; set; }
        public string DefaultLevel { get; set; }
        public string[] BlockSizes { get; set; }
        public string DefaultBlockSize { get; set; }
        public string DefaultParallelism { get; set; }
        public bool HasEncoding { get; set; }
        public bool HasLossless { get; set; }
        public bool HasCueOptions { get; set; }
    }

    /// <summary>
    /// CUE format specific options
    /// </summary>
    public class CueFormatOptions
    {
        public string[] CueTypes { get; set; }
        public string DefaultCueType { get; set; }
        public string[] BinaryExtensions { get; set; }
        public string DefaultBinaryExtension { get; set; }
        public string[] AudioExtensions { get; set; }
        public string DefaultAudioExtension { get; set; }
        public string DefaultSubExtension { get; set; }
    }

    /// <summary>
    /// System categorization for behavior grouping
    /// </summary>
    public enum SystemCategory
    {
        SingleFormat,   // Only supports single formats (GameCube, Wii, PSP, Xbox, etc.)
        IndexOnly,      // Only supports indexed formats (Dreamcast)
        DualFormat      // Supports both single and indexed formats (PS1, PS2, PS3, Saturn, etc.)
    }

    #endregion
}
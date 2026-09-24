using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Result of configuration setup operations
    /// </summary>
    internal class ConfigurationSetupResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int DirectoriesCreated { get; set; }
        public bool ConfigFileCreated { get; set; }
        public int FixFilesCopied { get; set; }
        public int PlaceholderFilesCreated { get; set; }
        public bool ConfigSymbolicLinkCreated { get; set; }
        public bool UserReadmeCreated { get; set; }

        public bool HasChanges => DirectoriesCreated > 0 || ConfigFileCreated || FixFilesCopied > 0 ||
                                 PlaceholderFilesCreated > 0 || ConfigSymbolicLinkCreated || UserReadmeCreated;
    }

    /// <summary>
    /// Result of configuration validation
    /// </summary>
    internal class ConfigurationValidationResult
    {
        public bool Success { get; set; }
        public bool IsValid { get; set; }
        public bool ConfigDirectoryExists { get; set; }
        public bool UserDataDirectoryExists { get; set; }
        public bool ConfigFileExists { get; set; }
        public bool ConfigDirectoryWritable { get; set; }
        public bool UserDataDirectoryWritable { get; set; }
        public string ValidationError { get; set; }
        public List<string> Errors { get; private set; } = new List<string>();
        public List<string> Warnings { get; private set; } = new List<string>();

        public void AddError(string error) => Errors.Add(error);
        public void AddWarning(string warning) => Warnings.Add(warning);
        public bool HasIssues => Errors.Any() || Warnings.Any();
    }

    /// <summary>
    /// Information about the current configuration
    /// </summary>
    public class ConfigurationInfo
    {
        public string ExecutableDirectory { get; set; }
        public string ConfigDirectory { get; set; }
        public string UserDataDirectory { get; set; }
        public bool IsPortableMode { get; set; }
        public ConfigSource ConfigSource { get; set; }
        public string ConfigFile { get; set; }
        public string ConfigFileName { get; set; }
        public bool HasValidConfiguration => ConfigSource != ConfigSource.None;
    }

    /// <summary>
    /// Source of configuration
    /// </summary>
    public enum ConfigSource
    {
        None,
        App,
        UserConfig,
        External,
        CommandLine
    }

    /// <summary>
    /// UI default path configuration when no system settings are available
    /// </summary>
    public class UiPathDefaults
    {
        public string Out { get; set; }
        public string ScanIn { get; set; }
        public string ScanOut { get; set; }
        public string Tmp { get; set; }
        public string LogOut { get; set; }
        public string ResultsOut { get; set; }
        public string FixInfo { get; set; }
        public string FixFiles { get; set; }
        public string Dat { get; set; }
        public string Keys { get; set; }
    }

    /// <summary>
    /// UI default general configuration when no system settings are available
    /// </summary>
    public class UiGeneralDefaults
    {
        public bool R { get; set; }
        public bool Arc { get; set; }
        public Verify V { get; set; }
        public LogLevel ConsoleLevel { get; set; }
        public LogLevel LogOutLevel { get; set; }
        public bool Results { get; set; }
        public bool OutAsDatMatch { get; set; }
        public bool DeleteProcessed { get; set; }
        public bool SkipIfCompleted { get; set; }
    }

    /// <summary>
    /// UI default conversion configuration when no system settings are available
    /// </summary>
    public class UiConversionDefaults
    {
        public string Format { get; set; }
        public string Encoding { get; set; }
        public string Level { get; set; }
        public string BlockSize { get; set; }
        public string Parallelism { get; set; }
        public bool Lossless { get; set; }
        public string CueType { get; set; }
        public string Binary { get; set; }
        public string Audio { get; set; }
    }

    /// <summary>
    /// UI default extraction configuration when no system settings are available
    /// </summary>
    public class UiExtractionDefaults
    {
        public string Type { get; set; }
        public bool MatchCase { get; set; }
        public bool Forensic { get; set; }
        public string SearchTerm { get; set; }
    }

    /// <summary>
    /// Complete UI defaults when no system settings are available
    /// </summary>
    public class CompleteUiDefaults
    {
        public SystemType SystemType { get; set; }
        public TaskType TaskType { get; set; }
        public UiPathDefaults Paths { get; set; }
        public UiGeneralDefaults General { get; set; }
        public UiConversionDefaults Conversion { get; set; }
        public UiExtractionDefaults Extraction { get; set; }
    }

    /// <summary>
    /// Configuration for RVZ format
    /// </summary>
    public class RvzFormatConfiguration
    {
        public RvzEncodingType Encoding { get; set; }
        public int CompressionLevel { get; set; }
        public int BlockSizeBytes { get; set; }
        public int Parallelism { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for CSO/ZSO format
    /// </summary>
    public class CsoFormatConfiguration
    {
        public string ContainerTypeString { get; set; }
        public int Version { get; set; }
        public int DeflateLevel { get; set; }
        public int Lz4Level { get; set; }
        public int BlockSizeBytes { get; set; }
        public int Parallelism { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public bool IsZso => ContainerTypeString == ConfigSettingsConstants.FormatZso;
        public bool IsCso2 => ContainerTypeString == ConfigSettingsConstants.FormatCso && Version == 2;
    }

    /// <summary>
    /// Configuration for CUE format
    /// </summary>
    public class CueFormatConfiguration
    {
        public string FormatType { get; set; }
        public string CueType { get; set; }
        public string BinaryExtension { get; set; }
        public string AudioExtension { get; set; }
        public string SubType { get; set; }
        public string DataSize { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for WBFS format
    /// </summary>
    public class WbfsFormatConfiguration
    {
        public bool Lossless { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for CISO format
    /// </summary>
    public class CisoFormatConfiguration
    {
        public bool Lossless { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for simple formats (ISO, APP, WUX, etc.)
    /// </summary>
    public class SimpleFormatConfiguration
    {
        public string FormatType { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for GDI format
    /// </summary>
    public class GdiFormatConfiguration
    {
        public string FormatType { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for extract operations
    /// </summary>
    public class ExtractConfiguration
    {
        public bool IsForensic { get; set; }
        public bool IsCaseInsensitive { get; set; }
        public bool IsMaskToRegex { get; set; }
        public bool IsRecursive { get; set; }
        public string Pattern { get; set; }
    }

    /// <summary>
    /// Configuration for dedupe operations.
    /// Parsed from format: dedupe: &lt;setName&gt;:&lt;shardSize&gt;:&lt;blockSize&gt;
    /// All parts are optional with sensible defaults.
    /// </summary>
    public class DedupeConfiguration
    {
        public const long DefaultShardSize = 50L * 1024L * 1024L * 1024L;

        /// <summary>
        /// Set name within the datastore. Null/empty means use system type name.
        /// </summary>
        public string SetName { get; set; }

        /// <summary>
        /// Maximum shard data file size in bytes. Default: 50 GiB.
        /// </summary>
        public long ShardSize { get; set; } = DefaultShardSize;

        /// <summary>
        /// Maximum stored block size in bytes. 0 means use default (64 KiB).
        /// </summary>
        public int BlockSize { get; set; }

        /// <summary>
        /// When true, the pipeline auto-creates an aux store if one does not exist.
        /// Triggered by the 4th part of the dedupe format string being "y".
        /// </summary>
        public bool AutoCreateAux { get; set; } = false;

        /// <summary>
        /// Optional user-supplied aux filename (without extension/suffix).
        /// For Wii/WiiU: used as the shared aux store name when provided (e.g., "MyCollection" → "MyCollection.aux.nkds").
        /// For Xbox/Xbox360: ignored — split filename is always derived from the game set name.
        /// For other systems: ignored — no aux store is created.
        /// Null or empty means use system-type-based defaults.
        /// </summary>
        public string AuxFilename { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }
}

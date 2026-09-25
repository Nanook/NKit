namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Constants used throughout the NKit configuration system.
    /// Contains all string and numeric constants for formats, paths, parameters, and validation.
    /// </summary>
    public static class ConfigSettingsConstants
    {
        // ======= Config File Names =======

        public const string ConfigFileNameCLI = "nkit.yaml";
        public const string ConfigFileNameUI = "nkit-ui.yaml";
        public const string ConfigFileExtension = ".yaml";

        // ======= Directory Names =======

        public const string DirectoryNameDats = "dats";
        public const string DirectoryNameKeys = "keys";
        public const string DirectoryNameFix = "fix";
        public const string DirectoryNameScans = "scans";
        public const string DirectoryNameOut = "out";
        public const string DirectoryNameLogs = "logs";
        public const string DirectoryNameTemp = "temp";
        public const string DirectoryNameDedupe = "dedupe";
        public const string DirectoryNameDefaults = "defaults";
        public const string ApplicationDirectoryName = "nkit";

        // ======= Configuration Parameter Names =======

        public const string ParamIn = "in";
        public const string ParamOut = "out";
        public const string ParamCfg = "cfg";
        public const string ParamTask = "task";
        public const string ParamSystem = "system";
        public const string ParamScanIn = "scanIn";
        public const string ParamScanOut = "scanOut";
        public const string ParamTmp = "tmp";
        public const string ParamR = "r";
        public const string ParamArc = "arc";
        public const string ParamV = "v";
        public const string ParamConsoleLevel = "consoleLevel";
        public const string ParamLogOutLevel = "logOutLevel";
        public const string ParamLogOut = "logOut";
        public const string ParamResults = "results";
        public const string ParamResultsOut = "resultsOut";
        public const string ParamBaseInPath = "baseInPath";
        public const string ParamFixInfo = "fixInfo";
        public const string ParamFixFiles = "fixFiles";
        public const string ParamDat = "dat";
        public const string ParamKeys = "keys";
        public const string ParamConvert = "convert";
        public const string ParamExtract = "extract";
        public const string ParamOutAsDatMatch = "outAsDatMatch";
        public const string ParamDeleteProcessed = "deleteProcessed";
        public const string ParamSkipIfCompleted = "skipIfCompleted";
        public const string ParamDedupe = "dedupe";
        public const string Param1Gmr = "1gmr";

        // ======= Path Variables =======

        public const string PathVariableConfig = "$configPath$";
        public const string PathVariableUser = "$userPath$";
        public const string PathVariableApp = "$appPath$";
        public const string PathVariableSystem = "$system$";
        public const string PathVariableTask = "$task$";
        public const string PathVariableDate = "$date$";
        public const string PathVariableTimestamp = "$timestamp$";

        // ======= Format Names =======

        public const string FormatIso = "iso";
        public const string FormatRvz = "rvz";
        public const string FormatWbfs = "wbfs";
        public const string FormatCiso = "ciso";
        public const string FormatCue = "cue";
        public const string FormatGdi = "gdi";
        public const string FormatCso = "cso";
        public const string FormatCso2 = "cso2";
        public const string FormatZso = "zso";
        public const string FormatDecIso = "deciso";
        public const string FormatApp = "app";
        public const string FormatTmd = "tmd";
        public const string FormatWux = "wux";
        public const string FormatToc = "toc";

        // ======= Display Format Names =======

        public const string DisplayFormatIso = "ISO";
        public const string DisplayFormatIsoDecrypted = "ISO (Decrypted)";
        public const string DisplayFormatIsoEncrypted = "ISO (Encrypted)";
        public const string DisplayFormatIsoWud = "ISO (WUD)";
        public const string DisplayFormatRvz = "RVZ";
        public const string DisplayFormatWbfs = "WBFS";
        public const string DisplayFormatCiso = "CISO";
        public const string DisplayFormatCso = "CSO";
        public const string DisplayFormatCso2 = "CSO2";
        public const string DisplayFormatZso = "ZSO";
        public const string DisplayFormatApp = "APP";
        public const string DisplayFormatTmd = "TMD";
        public const string DisplayFormatWux = "WUX";
        public const string DisplayFormatCue = "CUE";
        public const string DisplayFormatGdi = "GDI";

        // ======= Encoding Types =======

        public const string EncodingNone = "none";
        public const string EncodingZStd = "zstd";
        public const string EncodingLzma = "lzma";

        // ======= Block Sizes =======

        public const string BlockSize2kb = "2kb";
        public const string BlockSize2k = "2k";
        public const string BlockSize4kb = "4kb";
        public const string BlockSize8kb = "8kb";
        public const string BlockSize16kb = "16kb";
        public const string BlockSize32kb = "32kb";
        public const string BlockSize64kb = "64kb";
        public const string BlockSize128kb = "128kb";
        public const string BlockSize256kb = "256kb";
        public const string BlockSize512kb = "512kb";
        public const string BlockSize1mb = "1mb";
        public const string BlockSize2mb = "2mb";

        // ======= CUE Configuration =======

        public const string CueTypeSplit = "split";
        public const string CueTypeJoined = "joined";
        public const string DefaultCueSubType = "sub";

        // ======= Binary Extensions =======

        public const string BinaryExtensionBin = "bin";
        public const string BinaryExtensionImg = "img";
        public const string BinaryExtensionIso = "iso";

        // ======= Audio Extensions =======

        public const string AudioExtensionBin = "bin";
        public const string AudioExtensionWav = "wav";
        public const string AudioExtensionFlac = "flac";
        public const string AudioExtensionRaw = "raw";

        // ======= Extract Configuration =======

        public const string ExtractFlagForensic = "f";
        public const string ExtractFlagCaseInsensitive = "i";
        public const string ExtractFlagMaskToRegex = "m";
        public const string ExtractFlagRecursive = "r";
        public const string ExtractTypeMatch = "m";
        public const string ExtractTypeRegex = "r";
        public const string ExtractTypeFile = "f";
        public const string ExtractTypeImage = "i";
        public const string DefaultExtractSearchTerm = "*";
        public const string DefaultExtractPattern = ".*";

        // ======= Lossless Configuration =======

        public const string LosslessTrue = "y";
        public const string LosslessFalse = "n";

        // ======= Validation Patterns =======

        public const string BlockSizePatternCso = @"^(2kb?|4kb?|8kb?|16kb?|32kb?|64kb?|128kb?|256kb?|512kb?|1mb?|2mb?)$";
        public const string BlockSizePatternRvz = @"^((32|64|128|256|512)kb?|[12]mb?)$";

        // ======= Error Messages =======

        public const string BlockSizeErrorCso = "Block size must be 2k|2kb|4k|4kb|8k|8kb|16k|16kb|32k|32kb|64k|64kb|128k|128kb|256k|256kb|512k|512kb|1m|1mb|2m|2mb";
        public const string BlockSizeErrorRvz = "Block size must be 32k|32kb|64k|64kb|128k|128kb|256k|256kb|512k|512kb|1m|1mb|2m|2mb";

        // ======= Numeric Constants - Parallelism =======

        public const int MaxNintendoParallelism = 32;
        public const int MaxSonyParallelism = 32;
        public const int MaxParallelismValue = 99;
        public const int DefaultNintendoParallelism = 16;
        public const int DefaultSonyParallelism = 4;
        public const int DefaultCoreParallelism = 4;

        // ======= Numeric Constants - Compression Levels =======

        // ZStd
        public const int MinZStdLevel = 1;
        public const int MaxZStdLevel = 22;
        public const int DefaultZStdLevel = 19;
        public const int ZStdUltraLevelThreshold = 20;

        // LZMA
        public const int MinLzmaLevel = 1;
        public const int MaxLzmaLevel = 9;
        public const int DefaultLzmaLevel = 5;

        // ZLib
        public const int MinZlibLevel = 1;
        public const int MaxZlibLevel = 9;
        public const int DefaultZLibLevel = 9;

        // LZ4
        public const int MinLz4Level = 1;
        public const int MaxLz4Level = 12;
        public const int DefaultLz4Level = 12;

        // ======= Format Display Conversion Methods =======

        public static string FormatToDisplay(string format, SystemType system) => format?.ToLowerInvariant() switch
        {
            FormatIso => system == SystemType.WiiU ? DisplayFormatIsoWud : (system == SystemType.PS3 ? DisplayFormatIsoEncrypted : DisplayFormatIso),
            FormatDecIso => DisplayFormatIsoDecrypted,
            FormatRvz => DisplayFormatRvz,
            FormatWbfs => DisplayFormatWbfs,
            FormatCiso => DisplayFormatCiso,
            FormatCso => DisplayFormatCso,
            FormatCso2 => DisplayFormatCso2,
            FormatZso => DisplayFormatZso,
            FormatApp => DisplayFormatApp,
            FormatWux => DisplayFormatWux,
            FormatCue => DisplayFormatCue,
            FormatGdi => DisplayFormatGdi,
            _ => format?.ToUpper() ?? ""
        };

        public static string DisplayToFormat(string display) => display switch
        {
            DisplayFormatIso => FormatIso,
            DisplayFormatIsoDecrypted => FormatDecIso,
            DisplayFormatIsoEncrypted => FormatIso,
            DisplayFormatIsoWud => FormatIso,
            DisplayFormatRvz => FormatRvz,
            DisplayFormatWbfs => FormatWbfs,
            DisplayFormatCiso => FormatCiso,
            DisplayFormatCso => FormatCso,
            DisplayFormatCso2 => FormatCso2,
            DisplayFormatZso => FormatZso,
            DisplayFormatApp => FormatApp,
            DisplayFormatWux => FormatWux,
            DisplayFormatCue => FormatCue,
            DisplayFormatGdi => FormatGdi,
            _ => display?.ToLower() ?? ""
        };

    }
}
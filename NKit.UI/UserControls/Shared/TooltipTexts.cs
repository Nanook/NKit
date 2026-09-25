namespace NKit.Ui.UserControls.Shared
{
    /// <summary>
    /// Centralized tooltip text constants. Prefixes use the pattern: {Task}_{Control}_{Name}
    /// e.g. Convert_Label_FormatSingle, Convert_Check_DeleteProcessed
    /// This is intentionally simple C# constants so it is easy to reference from XAML via x:Static
    /// and to be AOT / linker friendly.
    /// </summary>
    public static class TooltipTexts
    {
        // Convert - Format selectors
        public const string Convert_Label_FormatSingle = "Select the Single file based image format - iso, rvz, wbfs, cso, zso etc";
        public const string Convert_Combo_FormatSingle = Convert_Label_FormatSingle;

        // Convert - Encoding
        public const string Convert_Label_Encoding = "Select the encoding method";
        public const string Convert_Combo_Encoding = Convert_Label_Encoding;

        // Convert - Level
        public const string Convert_Label_Level = "Select the compression level";
        public const string Convert_Combo_Level = Convert_Label_Level;

        // Convert - Block size
        public const string Convert_Label_BlockSize = "Select the block size for encoding";
        public const string Convert_Combo_BlockSize = Convert_Label_BlockSize;
        // Convert - Parallelism
        public const string Convert_Label_Parallelism = "Select the level of parallelism for processing";
        public const string Convert_Combo_Parallelism = Convert_Label_Parallelism;

        // Convert - Lossless
        public const string Convert_Label_Lossless = "Select to enable lossless encoding. NKit will preserve all non-recreatable data. Readable with other tools and converts back source 1:1 with NKit";
        public const string Convert_Check_Lossless = Convert_Label_Lossless;

        // Convert - Indexed format (dual-format systems)
        public const string Convert_Label_FormatIndexed = "Select the Indexed based format - cue/bin, gdi. Cue will be enhanced to support more options in future";
        public const string Convert_Combo_FormatIndexed = Convert_Label_FormatIndexed;
        // Convert - Cue/index options
        public const string Convert_Label_CueType = @"Select the type output:-
- Split - separates tracks into individual files
- Joined - combines all tracks into a single file
Note: Not full implemented. Split will be used in most cases";
        public const string Convert_Combo_CueType = Convert_Label_CueType;

        public const string Convert_Label_BinaryExtension = "Conversion is not currently implemented. The input will be used as the default";
        public const string Convert_Combo_BinaryExtension = Convert_Label_BinaryExtension;

        public const string Convert_Label_AudioExtension = "Conversion is not currently implemented. The input will be used as the default";
        public const string Convert_Combo_AudioExtension = Convert_Label_AudioExtension;

        // Convert - Verification radios
        public const string Convert_Radio_DynamicVerify = @"Verify conversions by using the best available method in order of:-

- InChecksum - Header checksums in the input image are used to verify the full image

- InScanCompare - Lossless conversions can scan the input image and compare it against the expanded output image

- ScanCompare - If the 'inScan' path is set and a scan matching the name of the input file is found it will be used for comparison

- DatLookup - Reads the expanded output image and checks it against DAT hashes (requires a DAT to be set)

- NoVerify - No verification available";
        public const string Convert_Radio_DatLookupVerify = "Force DatLookup verification by checking the expanded output image hashes. Use with the results log for cataloging";
        public const string Convert_Radio_NoneVerify = Verify_Radio_NoneVerify;

        // Convert - Output preferences
        public const string Convert_Check_DeleteProcessed = "Deletes source file on successful processing and verification, does not apply to archives";
        public const string Convert_Check_SkipIfCompleted = "If the output file exists skip processing";
        public const string Convert_Check_OutAsDatMatch = "Rename output images to a matched dat entry";

        // Extract - UI
        public const string Extract_Radio_AllFiles = "Extract files that match the search pattern. Masks support '*.txt' style masks. Regex mode supports full regex patterns";
        public const string Extract_Radio_FixFiles = "Extract files from good images that can be used by the fix task to repair/recover images to match DAT entries etc";
        public const string Extract_Radio_Forensic = "Extract all files disc areas to a structured filesystem that mirrors the disc layout. Useful for forensic analysis";
        public const string Extract_Label_SearchPattern = @"Enter a filename mask or regex to match files to extract, e.g:
- Match Mode: '*' for everything or '*.txt' for text files
- Regex Mode: '.*' for everything or '.*\.txt$' for text files";
        public const string Extract_Check_CaseSensitive = "Enable case sensitive matching for the search pattern";
        public const string Extract_Icon_InvalidRegex = "Invalid Regex";

        // Fix task specific (reuse verify texts)
        public const string Fix_Radio_DynamicVerify = @"Verify by using the best available method in order of:-

- DatLookup - Reads the expanded output image and checks it against DAT hashes (requires a DAT to be set)

- NoVerify - No verification available";
        public const string Fix_Radio_DatLookupVerify = "Force DatLookup verification by checking hashing the output image. Use with the results log for cataloging. May require the paths for fixInfo / fixFiles to be set correctly to be set";
        public const string Fix_Radio_NoneVerify = Verify_Radio_NoneVerify;

        // Scan task specific
        public const string Scan_Radio_DynamicVerify = Verify_Radio_DynamicVerify;
        public const string Scan_Radio_DatLookupVerify = Verify_Radio_DatLookupVerify;
        public const string Scan_Radio_NoneVerify = Verify_Radio_NoneVerify;

        // Verify / Extract / Fix / Dedupe / Scan - shared verification radios
        public const string Verify_Radio_DynamicVerify = @"Verify using the best available method based on task type and configuration:-
        
- InChecksum - Header checksums in the input image are used to verify the full image

- DatMatch - No header checksums, if there's a dat then lookup the input filename for a match to verify against

- ScanCompare - No header checksums or datMatch. Load an input filename matched NKit Scan to verify against

- DatLookup - The last resort, read the input image calculating MD5 or SHA1 plus CRC32 to lookup a dat entry

- NoVerify - No verification available";
        public const string Verify_Radio_DatLookupVerify = "Force DatLookup verification by checking the expanded output image hashes. Use with the results log for cataloging";
        public const string Verify_Radio_NoneVerify = "Turn off verification";

        // Dedupe task specific
        public const string Dedupe_Radio_DynamicVerify = "Verify against the scan created when deduping the image";
        public const string Dedupe_Radio_DatLookupVerify = Verify_Radio_DatLookupVerify;
        public const string Dedupe_Radio_NoneVerify = Verify_Radio_NoneVerify;

        // Dedupe - DataStore settings
        public const string Dedupe_Label_ShardSize = "Maximum size of each shard data file. Blank for default (50 GiB). Use 0 for a single database file";
        public const string Dedupe_Combo_ShardSize = Dedupe_Label_ShardSize;
        public const string Dedupe_Label_BlockSize = "Block size for deduplication storage. Blank for default (64 KiB). Larger blocks reduce index overhead; smaller blocks improve dedup ratio";
        public const string Dedupe_Combo_BlockSize = Dedupe_Label_BlockSize;
        public const string Dedupe_Label_Filesystem = "Store filesystem.yaml for each image in the datastore, enabling folder navigation when mounted";
        public const string Dedupe_Check_Filesystem = Dedupe_Label_Filesystem;
        public const string Dedupe_Label_SetName = "Custom set name within the datastore. Blank uses the system type name (e.g. gamecube, wii)";
        public const string Dedupe_Check_AuxMode = "Automatically create an aux store (.aux.nkds) for update partitions if one does not exist. Uses the primary set's block size when available.";
        public const string Dedupe_Label_OgmrYaml = "1GMR (1 Game, Many ROMs) YAML file for per-file routing. Each input filename is matched against game entry regex patterns to route it to a per-game set.";

        // General / future placeholders
        public const string General_ShowTooltips = "Toggle display of inline tooltips across the UI.";

        // Paths - textboxes and labels
        public const string Paths_Text_RootPath = "Base path for UI paths in this UI. Paths starting with the root path are shortened for aesthetic purposes";
        public const string Paths_Text_BaseInPath = "Base input path, all images in this path are processed as the current system. Useful for systems that have undetectable demos etc";
        public const string Paths_Text_OutPath = "Output path where processed files will be written, if blank the input image path is used";
        public const string Paths_Text_TmpPath = "Temporary working path (not for extract), if blank the input image path is used";
        public const string Paths_Text_ScanInPath = "Path to lookup scans for verifying 'in' files of the same name";
        public const string Paths_Text_ScanOutPath = @"# Output path to save full image scans
- Used for saving optional scans during tasks convert, expand, extract, and fix
- If the scan task does not have a specified output path, then the scanOut path is used
- The dedupe task requires a scanOut path defined";
        public const string Paths_Text_FixInfoPath = "Path to a yaml for systems that support the Fix task";
        public const string Paths_Text_FixFilesPath = "Path to NKit fix files. Known as recovery files by nkit v1";
        public const string Paths_Text_KeysPath = @"Path to keys. Supported formats: *.key, *.dkey, *.keys and keys.txt

Examples:
- keys/wiiu          Folder scanned for key files
- keys/wiiu/*.zip    All zip archives searched for key files inside
- keys/wiiu/k.zip    Specific archive searched for key files inside

PS3 and WiiU will try all loaded keys to find a match";
        public const string Paths_Text_KeysMask = @"A mask of archives to load keys from. zip/rar/7z/gzip are supported. If blank, keys are looked for in the specified path";
        public const string Paths_Text_RedumpDatsPath = "Path to the Redump DAT collection. NKit will apply system based filters to load the correct DATs";
        public const string Paths_Text_NoIntroDatsPath = "Path to the No-Intro DAT collection. NKit will apply system based filters to load the correct DATs";
        public const string Paths_Text_TosecDatsPath = "Path to the TOSEC DAT collection. NKit will apply system based filters to load the correct DATs";
        public const string Paths_Text_DatPath = "Path to DAT files used for lookups";
        public const string Paths_Text_DatArchiveMask = "A mask of archives to load dats from - zip/rar/7z/gzip are supported. If blank, dats are looked for in the specified path";
        public const string Paths_Text_DatMask = "Mask used to find DAT files within a path or archive if the archive mask is set";

        // Paths - Buttons (used in XAML)
        public const string Paths_Button_SelectBaseIn = "Select the base input folder";
        public const string Paths_Button_SelectOutput = "Select the output folder";
        public const string Paths_Button_SelectTemp = "Select the temporary working folder";
        public const string Paths_Button_SelectScanIn = "Select the Scan In folder";
        public const string Paths_Button_SelectScanOut = "Select the Scan Out folder";
        public const string Paths_Button_SelectDedupeIn = "Select the Dedupe In folder";
        public const string Paths_Button_SelectFixInfo = "Select the Fix Info file";
        public const string Paths_Button_SelectFixFiles = "Select the Fix Files folder";
        public const string Paths_Button_SelectKeys = "Select the Keys folder";
        public const string Paths_Button_SelectKeysArchive = "Select a keys archive (zip, 7z, rar)";
        public const string Paths_Button_SelectRedumpDats = "Select the Redump DATs folder";
        public const string Paths_Button_SelectNoIntroDats = "Select the No-Intro DATs folder";
        public const string Paths_Button_SelectTosecDats = "Select the TOSEC DATs folder";
        public const string Paths_Button_SelectDatPath = "Select the DAT path";

        // Process Buttons (used in ProcessFilesControl)
        public const string Process_Button_AddFiles = "Add files to the processing queue";
        public const string Process_Button_AddFolder = "Add a folder to the processing queue";
        public const string Process_Button_Clear = "Clear the processing queue";
        public const string Process_Button_Start = "Start processing queued files";
        public const string Process_Button_Cancel = "Cancel processing";

        // Process Options
        public const string ProcessOptions_Check_ScanArchives = "Include archives when scanning for images";
        public const string ProcessOptions_Check_Recursive = "Recursively scan subdirectories for images";
        public const string ProcessOptions_Check_SaveResults = "Enable summary results file loggins";
        public const string ProcessOptions_Text_ResultsOutPath = "Path to save the summary results file";
        public const string ProcessOptions_Text_ResultsOutMask = @"File name / mask to use when writing results:-

- $system$ - Name of system being processed
- $task$ - Task bring processed
- $date$ - YYYYMMDD date
- $timestamp$ - YYYYMMDDHHMMSS timestamp";
        public const string ProcessOptions_Button_SelectResults = "Select the folder for saving summary results";
        public const string ProcessOptions_Label_ConsoleLogLevel = "Logging verbosity for console output";
        public const string ProcessOptions_Label_FileLogLevel = "Logging verbosity for file output";
        public const string ProcessOptions_Text_LogOutPath = "File path for detailed log output";
        public const string ProcessOptions_Text_LogOutMask = @"File name / mask to use when writing logs:-

- $system$ - Name of system being processed
- $task$ - Task bring processed
- $date$ - YYYYMMDD date
- $timestamp$ - YYYYMMDDHHMMSS timestamp";
        public const string ProcessOptions_Button_SelectLog = "Select the path where log files will be written";
        // UI Options / Settings tab
        public const string Ui_ShowQueued = "Show queued items in the processing list";
        public const string Ui_ShowProcessing = "Show currently processing items in the processing list";
        public const string Ui_ShowCancelled = "Show cancelled items in the processing list";
        public const string Ui_ShowSkipped = "Show skipped items in the processing list";
        public const string Ui_ShowFailed = "Show failed items in the processing list";
        public const string Ui_ShowCompleted = "Show completed items in the processing list";

        public const string Ui_ReprocessSkippedFiles = "Allow reprocessing of skipped files when rebuilding the queue";
        public const string Ui_ReprocessFailedFiles = "Allow reprocessing of failed files when rebuilding the queue";
        public const string Ui_ReprocessCompletedFiles = "Allow reprocessing of completed files when rebuilding the queue";
        public const string Ui_PersistFileQueue = "Persist the file queue across application restarts";

        public const string Ui_ConsoleOutputAutoScroll = "Automatically scroll console output to show latest messages";
        public const string Ui_ConsoleOutputWrapText = "Wrap console output text lines for readability";

        // Process status / progress
        public const string Process_Status_Completed = "Number of completed items";
        public const string Process_Status_Skipped = "Number of skipped items";
        public const string Process_Status_Failed = "Number of failed items";
        public const string Process_Progress = "Overall processing progress";
    }
}
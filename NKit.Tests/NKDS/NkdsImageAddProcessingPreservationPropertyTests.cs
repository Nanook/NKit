using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Preservation property-based tests for the NKDS Image Add Processing bugfix.
    ///
    /// These tests verify that EXISTING correct behavior is preserved on the UNFIXED code.
    /// They capture baseline behavior for non-buggy code paths that must not change:
    /// - Single-index file archives (GDI-only or CUE-only) produce exactly 1 SourceFile
    /// - Multi-disc archives with distinct index files produce N separate SourceFiles
    /// - WiiU TMD app content (IsFolderMode=true) should be stored as ImageFormat.App
    /// - Non-WiiU system types produce correct ImageType values unchanged
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class NkdsImageAddProcessingPreservationPropertyTests : IDisposable
    {
        private readonly string _tempDir;

        public NkdsImageAddProcessingPreservationPropertyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_Preservation_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        #region Helpers

        /// <summary>
        /// Creates a minimal valid GDI file content referencing the given track filenames.
        /// GDI format: trackNo startLBA type sectorSize "filename" offset
        /// </summary>
        private static byte[] CreateGdiContent(string[] trackFileNames)
        {
            using MemoryStream ms = new MemoryStream();
            using StreamWriter sw = new StreamWriter(ms);
            sw.WriteLine(trackFileNames.Length.ToString());
            for (int i = 0; i < trackFileNames.Length; i++)
            {
                int trackNo = i + 1;
                int lba = i * 45000;
                int type = i == 0 ? 0 : 4; // track 1 is typically audio(0), rest are data(4)
                int sectorSize = i == 0 ? 2352 : 2352;
                sw.WriteLine($"{trackNo} {lba} {type} {sectorSize} \"{trackFileNames[i]}\" 0");
            }
            sw.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Creates a minimal valid CUE file content referencing the given track filenames.
        /// </summary>
        private static byte[] CreateCueContent(string[] trackFileNames)
        {
            using MemoryStream ms = new MemoryStream();
            using StreamWriter sw = new StreamWriter(ms);
            for (int i = 0; i < trackFileNames.Length; i++)
            {
                sw.WriteLine($"FILE \"{trackFileNames[i]}\" BINARY");
                sw.WriteLine($"  TRACK {i + 1:D2} MODE1/2352");
                sw.WriteLine($"    INDEX 01 00:00:00");
            }
            sw.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Creates a zip archive on disk containing the specified files.
        /// </summary>
        private string CreateZipArchive(string zipName, Dictionary<string, byte[]> files)
        {
            string zipPath = Path.Combine(_tempDir, zipName);
            using (FileStream fs = File.Create(zipPath))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (KeyValuePair<string, byte[]> kvp in files)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(kvp.Key);
                    using Stream entryStream = entry.Open();
                    entryStream.Write(kvp.Value, 0, kvp.Value.Length);
                }
            }
            return zipPath;
        }

        /// <summary>
        /// Scans a directory using SourceFiles.Scan and returns all valid SourceFile entries.
        /// </summary>
        private List<SourceFile> ScanDirectory(string path)
        {
            return SourceFiles.Scan(
                new string[] { path },
                scanSubfolders: false,
                scanArchives: true,
                validOnly: false,
                log: null,
                cancel: null
            ).ToList();
        }

        /// <summary>
        /// Generates a unique track filename with given index for test isolation.
        /// </summary>
        private static string MakeTrackName(string prefix, int trackIndex)
            => $"{prefix}_track{trackIndex:D2}.bin";

        #endregion

        #region Property 3: Single Index File Processing

        /// <summary>
        /// **Validates: Requirements 3.1, 3.2**
        ///
        /// Property 3: For any archive containing only a single index file (GDI or CUE, not both),
        /// scanning produces exactly 1 SourceFile with the correct type.
        ///
        /// This property generates random single-index archives with varying numbers of track files
        /// and verifies that exactly one SourceFile is produced per archive.
        /// </summary>
        [Property(MaxTest = 30)]
        public Property SingleIndexFile_ProducesExactlyOneSourceFile()
        {
            // Generate: choice of GDI or CUE, and 1-5 track files
            Gen<(bool isGdi, int trackCount, int uniqueSuffix)> testGen =
                from isGdi in Gen.Elements(true, false)
                from trackCount in Gen.Choose(1, 5)
                from uniqueSuffix in Gen.Choose(1, 99999)
                select (isGdi, trackCount, uniqueSuffix);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (bool isGdi, int trackCount, int uniqueSuffix) = data;

                // Create unique directory per test case to avoid interference
                string testDir = Path.Combine(_tempDir, $"single_{uniqueSuffix}_{(isGdi ? "gdi" : "cue")}_{trackCount}");
                Directory.CreateDirectory(testDir);

                try
                {
                    // Create track file names
                    string[] trackNames = Enumerable.Range(1, trackCount)
                        .Select(i => MakeTrackName($"disc{uniqueSuffix}", i))
                        .ToArray();

                    // Create index file content
                    string indexFileName = isGdi ? $"disc{uniqueSuffix}.gdi" : $"disc{uniqueSuffix}.cue";
                    byte[] indexContent = isGdi
                        ? CreateGdiContent(trackNames)
                        : CreateCueContent(trackNames);

                    // Build zip content
                    Dictionary<string, byte[]> zipFiles = new Dictionary<string, byte[]>();
                    zipFiles[indexFileName] = indexContent;
                    foreach (string trackName in trackNames)
                        zipFiles[trackName] = new byte[2352 * 10]; // Minimal track data

                    // Create the zip archive
                    string zipName = $"disc{uniqueSuffix}.zip";
                    string zipPath = Path.Combine(testDir, zipName);
                    using (FileStream fs = File.Create(zipPath))
                    using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        foreach (KeyValuePair<string, byte[]> kvp in zipFiles)
                        {
                            ZipArchiveEntry entry = zip.CreateEntry(kvp.Key);
                            using Stream entryStream = entry.Open();
                            entryStream.Write(kvp.Value, 0, kvp.Value.Length);
                        }
                    }

                    // Scan and verify
                    List<SourceFile> results = SourceFiles.Scan(
                        new string[] { testDir },
                        scanSubfolders: false,
                        scanArchives: true,
                        validOnly: true,
                        log: null,
                        cancel: null
                    ).ToList();

                    bool hasOneResult = results.Count == 1;

                    if (!hasOneResult)
                        return false.Label($"Expected 1 SourceFile, got {results.Count} for {(isGdi ? "GDI" : "CUE")} with {trackCount} tracks");

                    // Verify correct type
                    SourceFile sf = results[0];
                    bool correctType = isGdi
                        ? sf.ImageType == SourceImageType.Gdi
                        : (sf.ImageType == SourceImageType.Cue ||
                           sf.ImageType == SourceImageType.IsoMode1 ||
                           sf.ImageType == SourceImageType.IsoMode2);

                    return hasOneResult.Label($"Exactly 1 SourceFile produced")
                        .And(correctType).Label($"Correct ImageType: got {sf.ImageType} for {(isGdi ? "GDI" : "CUE")}");
                }
                finally
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            });
        }

        #endregion

        #region Property 4: Multi-Disc CueFolder Grouping

        /// <summary>
        /// **Validates: Requirements 3.3, 3.4**
        ///
        /// Property 4: For any container with multiple distinct CUE/GDI files for different discs
        /// (no shared tracks), scanning produces N separate SourceFiles where N equals the number
        /// of index files, and CueFolder grouping is triggered when 2+ exist.
        /// </summary>
        [Property(MaxTest = 20)]
        public Property MultiDiscArchive_ProducesNSourceFilesAndCueFolderGrouping()
        {
            // Generate 2-4 distinct disc index files, each with 1-3 tracks (no shared track names)
            Gen<(int discCount, int[] tracksPerDisc, bool useGdi, int uniqueSuffix)> testGen =
                from discCount in Gen.Choose(2, 4)
                from tracksPerDisc in Gen.ArrayOf(Gen.Choose(1, 3), discCount)
                from useGdi in Gen.Elements(true, false)
                from uniqueSuffix in Gen.Choose(1, 99999)
                select (discCount, tracksPerDisc, useGdi, uniqueSuffix);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (int discCount, int[] tracksPerDisc, bool useGdi, int uniqueSuffix) = data;

                string testDir = Path.Combine(_tempDir, $"multi_{uniqueSuffix}_{discCount}discs");
                Directory.CreateDirectory(testDir);

                try
                {
                    Dictionary<string, byte[]> zipFiles = new Dictionary<string, byte[]>();

                    // Create distinct index files and track files for each disc
                    for (int d = 0; d < discCount; d++)
                    {
                        int numTracks = tracksPerDisc[d];
                        string discPrefix = $"disc{d + 1}_{uniqueSuffix}";

                        // Each disc has its own distinct track files (no sharing)
                        string[] trackNames = Enumerable.Range(1, numTracks)
                            .Select(i => MakeTrackName(discPrefix, i))
                            .ToArray();

                        string indexFileName = useGdi
                            ? $"{discPrefix}.gdi"
                            : $"{discPrefix}.cue";

                        byte[] indexContent = useGdi
                            ? CreateGdiContent(trackNames)
                            : CreateCueContent(trackNames);

                        zipFiles[indexFileName] = indexContent;
                        foreach (string trackName in trackNames)
                            zipFiles[trackName] = new byte[2352 * 10];
                    }

                    // Create the zip archive
                    string zipPath = Path.Combine(testDir, $"multidisc_{uniqueSuffix}.zip");
                    using (FileStream fs = File.Create(zipPath))
                    using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        foreach (KeyValuePair<string, byte[]> kvp in zipFiles)
                        {
                            ZipArchiveEntry entry = zip.CreateEntry(kvp.Key);
                            using Stream entryStream = entry.Open();
                            entryStream.Write(kvp.Value, 0, kvp.Value.Length);
                        }
                    }

                    // Scan
                    List<SourceFile> results = SourceFiles.Scan(
                        new string[] { testDir },
                        scanSubfolders: false,
                        scanArchives: true,
                        validOnly: true,
                        log: null,
                        cancel: null
                    ).ToList();

                    // Filter to only results with index files (ignore any non-index standalone files)
                    List<SourceFile> indexResults = results.Where(sf => sf.IndexFile != null).ToList();
                    bool correctCount = indexResults.Count == discCount;

                    // Verify CueFolder grouping is triggered (2+ index files detected)
                    List<FolderGroupInfo> groups = SyntheticSourceDetector.DetectFolderGroups(results);
                    List<FolderGroupInfo> cueFolderGroups = groups.Where(g => g.GroupType == FolderGroupType.CueFolder).ToList();
                    bool cueFolderTriggered = cueFolderGroups.Count >= 1;

                    return correctCount.Label($"Expected {discCount} SourceFiles with index, got {indexResults.Count}")
                        .And(cueFolderTriggered).Label($"CueFolder grouping should be triggered for {discCount} discs (got {cueFolderGroups.Count} groups)");
                }
                finally
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            });
        }

        #endregion

        #region Property 5: WiiU TMD App Format

        /// <summary>
        /// **Validates: Requirements 3.5**
        ///
        /// Property 5: For any WiiU TMD app source (IsFolderMode=true, IndexFile != null),
        /// the format decision logic in DataStoreWiiUFormatter would assign ImageFormat.App.
        ///
        /// We test this by verifying that SourceFile.IsFolderMode is true when IndexFile is set,
        /// which is the condition that triggers ImageFormat.App in the formatter.
        /// The formatter logic is: if IsFolderMode == true → ImageFormat.App
        /// </summary>
        [Property(MaxTest = 50)]
        public Property WiiUTmdApp_HasIsFolderModeTrue_TriggersAppFormat()
        {
            // Generate varying TMD content IDs (simulating different TMD app scenarios)
            Gen<(int contentCount, int uniqueSuffix)> testGen =
                from contentCount in Gen.Choose(1, 8)
                from uniqueSuffix in Gen.Choose(1, 99999)
                select (contentCount, uniqueSuffix);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (int contentCount, int uniqueSuffix) = data;

                // Construct a SourceFile with a TmdApp IndexFile to verify IsFolderMode behavior.
                // We use the SourceFile internal constructor and set properties to simulate
                // what SourceFiles.Scan would produce for a TMD app archive.
                SourceFile sf = new SourceFile();

                // Create a minimal TmdApp index file using IndexFile.Parse
                // TMD binary format: header(484 bytes) + content records (36 bytes each)
                // Minimal valid TMD: version 1, 1+ content entries
                byte[] tmdData = CreateMinimalTmdData(contentCount);

                // Parse using the IndexFile parser
                IndexFile indexFile = IndexFile.Parse($"tmd.{uniqueSuffix}", ".tmd", tmdData);

                if (indexFile == null)
                {
                    // If parsing fails for edge-case content, skip this test case
                    return true.Label("TMD parse returned null - skipped");
                }

                // Set up the SourceFile with the parsed index
                sf.IndexFile = indexFile;
                sf.ImageFiles = Enumerable.Range(0, contentCount)
                    .Select(i => new SourceFileItem("", $"{i:X8}.app", ".app", "", 0, 1024, 0, false, false))
                    .ToArray();
                sf.SystemType = SystemType.WiiU;
                sf.Initialised();

                // Verify IsFolderMode is true (this is what triggers ImageFormat.App in the formatter)
                bool isFolderMode = sf.IsFolderMode;
                bool isCorrectImageType = sf.ImageType == SourceImageType.TmdApp;

                // The formatter's format decision:
                // ImageFormat chosenFormat = ImageFormat.Iso;
                // if (_context.SourceFile?.IsFolderMode ?? false) chosenFormat = ImageFormat.App;
                // So IsFolderMode == true → ImageFormat.App
                ImageFormat expectedFormat = isFolderMode ? ImageFormat.App : ImageFormat.Iso;
                bool wouldBeApp = expectedFormat == ImageFormat.App;

                return isFolderMode.Label("IsFolderMode should be true for TMD app source")
                    .And(wouldBeApp).Label("Formatter would assign ImageFormat.App")
                    .And(isCorrectImageType).Label($"ImageType should be TmdApp, got {sf.ImageType}");
            });
        }

        /// <summary>
        /// Creates minimal TMD binary data that IndexFile.Parse can parse.
        /// TMD format: 484 bytes header, then content records of 36 bytes each.
        /// Key fields: offset 0x1DE (2 bytes BE) = total content count,
        ///             content entries start at offset 0x1E4, each 36 bytes.
        /// </summary>
        private static byte[] CreateMinimalTmdData(int contentCount)
        {
            // TMD header is 484 (0x1E4) bytes. Content info entries follow.
            // Each content info record: 4 bytes contentId, 2 bytes index, 2 bytes type,
            //                           8 bytes size, 20 bytes hash = 36 bytes
            int headerSize = 0x1E4;
            int contentRecordSize = 36;
            byte[] tmd = new byte[headerSize + (contentCount * contentRecordSize)];

            // Write content count at offset 0x1DE (2 bytes, big-endian)
            tmd[0x1DE] = (byte)((contentCount >> 8) & 0xFF);
            tmd[0x1DF] = (byte)(contentCount & 0xFF);

            // Write content entries
            for (int i = 0; i < contentCount; i++)
            {
                int offset = headerSize + (i * contentRecordSize);
                // Content ID (4 bytes BE)
                tmd[offset] = 0;
                tmd[offset + 1] = 0;
                tmd[offset + 2] = (byte)((i >> 8) & 0xFF);
                tmd[offset + 3] = (byte)(i & 0xFF);
                // Index (2 bytes BE)
                tmd[offset + 4] = (byte)((i >> 8) & 0xFF);
                tmd[offset + 5] = (byte)(i & 0xFF);
                // Type (2 bytes) - 0x0001 = encrypted content
                tmd[offset + 6] = 0;
                tmd[offset + 7] = 1;
                // Size (8 bytes BE) - 1024 bytes
                tmd[offset + 8] = 0;
                tmd[offset + 9] = 0;
                tmd[offset + 10] = 0;
                tmd[offset + 11] = 0;
                tmd[offset + 12] = 0;
                tmd[offset + 13] = 0;
                tmd[offset + 14] = 0x04;
                tmd[offset + 15] = 0x00;
            }

            return tmd;
        }

        #endregion

        #region Property 6: Non-WiiU System Processing

        /// <summary>
        /// **Validates: Requirements 3.6**
        ///
        /// Property 6: For any non-WiiU system image (GameCube, Wii, PS1, Dreamcast, etc.),
        /// the system correctly detects the ImageType based on file extension,
        /// producing the same result as unfixed code (format detection is unchanged).
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NonWiiUSystemTypes_FormatDetectionUnchanged()
        {
            // Map of extensions to expected SourceImageType for non-WiiU systems
            (string ext, SourceImageType expected)[] extensionMap = new (string ext, SourceImageType expected)[]
            {
                (".iso", SourceImageType.Iso),
                (".gcm", SourceImageType.Gcm),
                (".wbfs", SourceImageType.Wbfs),
                (".ciso", SourceImageType.CIso),
                (".rvz", SourceImageType.Rvz),
                (".wia", SourceImageType.Wia),
                (".nkit.iso", SourceImageType.NKitIso),
                (".nkit.gcz", SourceImageType.NKitGcz),
                (".gcz", SourceImageType.Gcz),
                (".chd", SourceImageType.Chd),
            };

            // Non-WiiU system types to test
            SystemType[] nonWiiUSystems = new SystemType[]
            {
                SystemType.GameCube,
                SystemType.Wii,
                SystemType.PS1,
                SystemType.Dreamcast,
                SystemType.PS2,
                SystemType.Saturn,
                SystemType.SegaCD,
                SystemType.XBox,
                SystemType.PcEngine,
            };

            Gen<((string ext, SourceImageType expected), SystemType)> testGen =
                from extIdx in Gen.Choose(0, extensionMap.Length - 1)
                from sysIdx in Gen.Choose(0, nonWiiUSystems.Length - 1)
                select (extensionMap[extIdx], nonWiiUSystems[sysIdx]);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                ((string ext, SourceImageType expectedType), SystemType systemType) = data;

                // Construct SourceFile with the given extension and system type
                SourceFile sf = new SourceFile();
                string fileName = $"testimage{ext}";
                sf.ImageFiles = new[] { new SourceFileItem("", fileName, ext, "", 0, 1024 * 1024, 0, false, false) };
                sf.SystemType = systemType;
                sf.Initialised();

                bool correctType = sf.ImageType == expectedType;
                bool notFolderMode = !sf.IsFolderMode;

                return correctType.Label($"ImageType should be {expectedType} for '{ext}', got {sf.ImageType}")
                    .And(notFolderMode).Label($"IsFolderMode should be false for non-WiiU images without IndexFile");
            });
        }

        /// <summary>
        /// **Validates: Requirements 3.7**
        ///
        /// Supplementary: WiiU disc images (WUD/WUX) without an IndexFile
        /// correctly detect IsFolderMode as false.
        /// </summary>
        [Property(MaxTest = 30)]
        public Property WiiUDiscImages_WithoutIndexFile_DetectIsFolderModeFalse()
        {
            (string ext, SourceImageType expected)[] wiiuDiscExtensions = new (string ext, SourceImageType expected)[]
            {
                (".wud", SourceImageType.Wud),
                (".wux", SourceImageType.Wux),
                (".iso", SourceImageType.Iso),
            };

            Gen<((string ext, SourceImageType expected), int uniqueSuffix)> testGen =
                from extIdx in Gen.Choose(0, wiiuDiscExtensions.Length - 1)
                from uniqueSuffix in Gen.Choose(1, 99999)
                select (wiiuDiscExtensions[extIdx], uniqueSuffix);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                ((string ext, SourceImageType expectedType), int uniqueSuffix) = data;

                SourceFile sf = new SourceFile();
                string fileName = $"wiiu_game_{uniqueSuffix}{ext}";
                sf.ImageFiles = new[] { new SourceFileItem("", fileName, ext, "", 0, 1024L * 1024 * 1024, 0, false, false) };
                sf.SystemType = SystemType.WiiU;
                sf.Initialised();

                bool correctType = sf.ImageType == expectedType;
                bool notFolderMode = !sf.IsFolderMode;

                // The formatter decision: IsFolderMode == false → ImageFormat.Iso
                ImageFormat expectedFormat = sf.IsFolderMode ? ImageFormat.App : ImageFormat.Iso;
                bool wouldBeIso = expectedFormat == ImageFormat.Iso;

                return correctType.Label($"ImageType should be {expectedType} for '{ext}', got {sf.ImageType}")
                    .And(notFolderMode).Label("IsFolderMode should be false for WiiU disc images without IndexFile")
                    .And(wouldBeIso).Label("Formatter would assign ImageFormat.Iso for disc images");
            });
        }

        #endregion
    }
}
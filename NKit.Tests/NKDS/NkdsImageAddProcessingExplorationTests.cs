using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Bug condition exploration tests for the NKDS Image Add Processing bugfix.
    ///
    /// These tests encode the EXPECTED (correct) behavior and are designed to FAIL
    /// on unfixed code, confirming the bugs exist.
    ///
    /// Bug 1: Dreamcast GDI/CUE Duplicate Processing — a zip archive containing both
    /// a .gdi and .cue file referencing the same track files should produce only 1
    /// SourceFile (preferring CUE), but currently produces 2.
    ///
    /// Bug 2: WiiU Format Misassignment — WiiU disc images (WUD/WUX/ISO) should be
    /// stored as ImageFormat.Iso but are incorrectly stored as ImageFormat.App when
    /// a stale IsFolderMode=true context persists from a prior TMD app.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 2.1, 2.2, 2.3, 2.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class NkdsImageAddProcessingExplorationTests : IDisposable
    {
        private readonly string _tempDir;

        public NkdsImageAddProcessingExplorationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NkdsImageAddProcessing_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        #region Helper Methods

        /// <summary>
        /// Invokes the private static process() method on SourceFiles via reflection.
        /// </summary>
        private static void InvokeProcess(List<FileItem> items)
        {
            MethodInfo method = typeof(SourceFiles).GetMethod("process", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new object[] { items, null });
        }

        /// <summary>
        /// Creates a FileItem representing a track file in an archive.
        /// </summary>
        private static FileItem CreateTrackFileItem(string path, string fileName)
        {
            FileItem fi = new FileItem(path + fileName);
            fi.Parent = path;
            fi.IsMatch = true;
            fi.Type = FileItemType.File;
            fi.Populate();
            return fi;
        }

        /// <summary>
        /// Creates a GDI content string that references the given track files.
        /// Format: trackNum startOffset type blockSize "filename" 0
        /// </summary>
        private static byte[] CreateGdiContent(string[] trackFiles)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(trackFiles.Length.ToString());
            for (int i = 0; i < trackFiles.Length; i++)
            {
                int trackNum = i + 1;
                int offset = i * 45000;
                int type = i == 0 ? 0 : 4; // first track audio, rest data
                int blockSize = i == 0 ? 2352 : 2352;
                sb.AppendLine($"{trackNum} {offset} {type} {blockSize} \"{trackFiles[i]}\" 0");
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Creates a CUE content string that references the given track files.
        /// </summary>
        private static byte[] CreateCueContent(string[] trackFiles)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < trackFiles.Length; i++)
            {
                sb.AppendLine($"FILE \"{trackFiles[i]}\" BINARY");
                sb.AppendLine($"  TRACK {i + 1:D2} MODE1/2352");
                sb.AppendLine($"    INDEX 01 00:00:00");
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Creates a FileItem with an IndexFile parsed from GDI content, and populates
        /// its Parts with the provided track FileItems.
        /// </summary>
        private static FileItem CreateGdiIndexFileItem(string path, string gdiFileName, string[] trackFileNames, List<FileItem> trackFileItems)
        {
            FileItem fi = new FileItem(path + gdiFileName);
            fi.Parent = path;
            fi.IsMatch = true;
            fi.Type = FileItemType.Index;
            fi.Populate();

            byte[] content = CreateGdiContent(trackFileNames);
            fi.IndexFile = IndexFile.Parse(path, gdiFileName, ".gdi", ".gdi", content, false, false, null);

            // Populate Parts by matching track filenames (same as consolidateIndexFiles does)
            foreach (string trackName in fi.IndexFile.Items.Select(a => a.FileName).Distinct())
            {
                FileItem found = trackFileItems.FirstOrDefault(a =>
                    string.Compare(a.FileName, trackName, StringComparison.OrdinalIgnoreCase) == 0);
                if (found != null)
                    fi.Parts.Add(found);
            }

            return fi;
        }

        /// <summary>
        /// Creates a FileItem with an IndexFile parsed from CUE content, and populates
        /// its Parts with the provided track FileItems.
        /// </summary>
        private static FileItem CreateCueIndexFileItem(string path, string cueFileName, string[] trackFileNames, List<FileItem> trackFileItems)
        {
            FileItem fi = new FileItem(path + cueFileName);
            fi.Parent = path;
            fi.IsMatch = true;
            fi.Type = FileItemType.Index;
            fi.Populate();

            byte[] content = CreateCueContent(trackFileNames);
            fi.IndexFile = IndexFile.Parse(path, cueFileName, ".cue", ".cue", content, false, false, null);

            // Populate Parts by matching track filenames
            foreach (string trackName in fi.IndexFile.Items.Select(a => a.FileName).Distinct())
            {
                FileItem found = trackFileItems.FirstOrDefault(a =>
                    string.Compare(a.FileName, trackName, StringComparison.OrdinalIgnoreCase) == 0);
                if (found != null)
                    fi.Parts.Add(found);
            }

            return fi;
        }

        /// <summary>
        /// Creates a SourceFile with WiiU disc image characteristics.
        /// In the real pipeline, WUX/WUD disc images scanned standalone have no IndexFile
        /// (IsFolderMode = false). The bug manifests when the formatter's format decision
        /// is based on IsFolderMode alone — this test verifies the IndexFile-based check
        /// correctly identifies disc images (no TmdApp IndexFile → Iso).
        /// </summary>
        private static SourceFile CreateWiiUDiscSourceFile(string extension)
        {
            SourceFile sf = new SourceFile();
            sf.ImageFiles = new[] { new SourceFileItem("", $"game{extension}", extension, extension, 0, 1024 * 1024, 0, false, false) };
            sf.SystemType = SystemType.WiiU;
            // No IndexFile — WUX/WUD disc images are standalone files without a TMD index
            sf.Initialised();
            return sf;
        }

        /// <summary>
        /// Creates minimal fake TMD content that will parse as a valid TmdApp IndexFile.
        /// The TMD format requires specific header bytes to parse correctly.
        /// </summary>
        private static byte[] CreateFakeTmdContent()
        {
            // Minimal TMD structure:
            // - Header with signature type (at offset 0)
            // - Total contents field
            // We use a minimal valid structure that TmdInfo can parse
            byte[] tmd = new byte[0xB04 + 0x30]; // header + 1 content entry
            // Signature type = RSA_2048 (0x00010001) at offset 0 — big-endian
            tmd[0] = 0x00; tmd[1] = 0x01; tmd[2] = 0x00; tmd[3] = 0x01;
            // Number of contents at offset 0x9E (relative to cert start at 0x140+0x1C4=0x304 ?)
            // Actually TMD structure: after 0x140 cert chain comes the signed data
            // Let's just use a 2-content value at the correct offset
            // TmdInfo reads TotalContents at header+0x1DE big-endian
            int totalContentsOffset = 0x1DE;
            tmd[totalContentsOffset] = 0x00;
            tmd[totalContentsOffset + 1] = 0x01; // 1 content
            // Content[0] at 0x1E4: ContentId (4 bytes big-endian)
            int contentOffset = 0x1E4;
            tmd[contentOffset] = 0x00;
            tmd[contentOffset + 1] = 0x00;
            tmd[contentOffset + 2] = 0x00;
            tmd[contentOffset + 3] = 0x01; // content id = 1
            return tmd;
        }

        /// <summary>
        /// Minimal IStepContext for testing DataStoreWiiUFormatter.
        /// </summary>
        private class TestWiiUStepContext : IStepContext
        {
            public TestWiiUStepContext(SourceFile sourceFile)
            {
                SourceFile = sourceFile;
                SystemType = SystemType.WiiU;
            }

            public SourceFile SourceFile { get; }
            public byte[] Key => null;
            public Scan Scan => null;
            public NKitStepResult Result => null;
            public string WritePath => null;
            public int Index => 0;

            public IImageInfo ImageInfo => null;
            public SystemType SystemType { get; }
            public IStepInfo StepInfo => null;
            public IStep Step => null;
            public string SourceImageName => "TestWiiUImage";
            public byte[] HeaderData => null;
            public string StepConfig => null;
            public ILogScope Log => null;
            public Nanook.NKit.Dats.DatManager DatManager => null;
            public long ImageSize => 0;
            public IDataProvider Settings => null;
            public int ThreadCount => 1;

            public void AddSettingsInfo(string name, string value) { }
            public void SkipBlockTaskEnable() { }
            public void SkipToImageOffsetSet(long imageOffset) { }
        }

        #endregion

        #region Bug 1: Dreamcast GDI/CUE Duplicate Processing

        /// <summary>
        /// Bug 1 Exploration: A zip archive containing both game.gdi and game.cue
        /// referencing the same track files (track01.bin, track02.raw) should produce
        /// only 1 SourceFile entry after processing, preferring CUE format.
        ///
        /// EXPECTED TO FAIL on unfixed code: both index files survive process(),
        /// producing 2 SourceFiles instead of 1.
        ///
        /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
        /// </summary>
        [Fact]
        public void DreamcastDuplicate_GdiAndCueWithSharedTracks_ShouldProduceOneSourceFile()
        {
            // Arrange: simulate archive contents after readArchive + readIndexAndAdditionalFiles
            string path = "/archive/";
            string[] trackFiles = new[] { "track01.bin", "track02.raw" };

            // Create track FileItems
            List<FileItem> trackFileItems = trackFiles.Select(t => CreateTrackFileItem(path, t)).ToList();

            // Create GDI and CUE index FileItems with Parts referencing same tracks
            FileItem gdiItem = CreateGdiIndexFileItem(path, "game.gdi", trackFiles, trackFileItems);
            FileItem cueItem = CreateCueIndexFileItem(path, "game.cue", trackFiles, trackFileItems);

            // Build the items list as it would be after readIndexAndAdditionalFiles
            List<FileItem> items = new List<FileItem> { gdiItem, cueItem };
            items.AddRange(trackFileItems);

            // Act: invoke process() which calls cleanUpIndexFiles + consolidateMultipartFiles + filter
            InvokeProcess(items);

            // Assert: only 1 index item should remain (CUE preferred over GDI)
            List<FileItem> remainingIndexItems = items.Where(f => f.Type == FileItemType.Index).ToList();

            Assert.Single(remainingIndexItems); // EXPECTED TO FAIL: will have 2 items
            Assert.Equal(IndexFileType.Cue, remainingIndexItems[0].IndexFile.FileType);
        }

        /// <summary>
        /// Bug 1 Exploration (multiple tracks): GDI + CUE both referencing track01.bin,
        /// track02.bin, track03.raw — verify deduplication works with larger track sets.
        ///
        /// EXPECTED TO FAIL on unfixed code.
        ///
        /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
        /// </summary>
        [Fact]
        public void DreamcastDuplicate_GdiAndCueWithMultipleSharedTracks_ShouldProduceOneSourceFile()
        {
            // Arrange
            string path = "/archive/";
            string[] trackFiles = new[] { "track01.bin", "track02.bin", "track03.raw" };

            List<FileItem> trackFileItems = trackFiles.Select(t => CreateTrackFileItem(path, t)).ToList();
            FileItem gdiItem = CreateGdiIndexFileItem(path, "game.gdi", trackFiles, trackFileItems);
            FileItem cueItem = CreateCueIndexFileItem(path, "game.cue", trackFiles, trackFileItems);

            List<FileItem> items = new List<FileItem> { gdiItem, cueItem };
            items.AddRange(trackFileItems);

            // Act
            InvokeProcess(items);

            // Assert: only 1 index item should remain
            List<FileItem> remainingIndexItems = items.Where(f => f.Type == FileItemType.Index).ToList();

            Assert.Single(remainingIndexItems); // EXPECTED TO FAIL
            Assert.Equal(IndexFileType.Cue, remainingIndexItems[0].IndexFile.FileType);
        }

        /// <summary>
        /// Property-based exploration for Bug 1: For any set of 1-5 shared track files,
        /// a GDI+CUE pair referencing those tracks should deduplicate to 1 SourceFile.
        ///
        /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property DreamcastDuplicate_AnySharedTrackSet_ShouldDeduplicateToOne()
        {
            Gen<string[]> testGen =
                from trackCount in Gen.Choose(1, 5)
                from trackNames in Gen.ArrayOf(
                    Gen.Elements("track01.bin", "track02.bin", "track03.raw", "track04.bin", "track05.raw"),
                    trackCount)
                select trackNames.Distinct().ToArray();

            return Prop.ForAll(testGen.Where(t => t.Length >= 1).ToArbitrary(), trackFiles =>
            {
                string path = "/archive/";
                List<FileItem> trackFileItems = trackFiles.Select(t => CreateTrackFileItem(path, t)).ToList();
                FileItem gdiItem = CreateGdiIndexFileItem(path, "game.gdi", trackFiles, trackFileItems);
                FileItem cueItem = CreateCueIndexFileItem(path, "game.cue", trackFiles, trackFileItems);

                List<FileItem> items = new List<FileItem> { gdiItem, cueItem };
                items.AddRange(trackFileItems);

                InvokeProcess(items);

                List<FileItem> remainingIndexItems = items.Where(f => f.Type == FileItemType.Index).ToList();

                return (remainingIndexItems.Count == 1)
                    .Label($"Expected 1 index item after dedup, got {remainingIndexItems.Count}")
                    .And(remainingIndexItems.Count == 1 && remainingIndexItems[0].IndexFile.FileType == IndexFileType.Cue)
                    .Label("Remaining index should be CUE (preferred over GDI)");
            });
        }

        #endregion

        #region Bug 2: WiiU Format Misassignment

        /// <summary>
        /// Bug 2 Exploration: A WiiU WUD source image (no IndexFile, no TMD) should
        /// be stored as ImageFormat.Iso. The formatter checks IndexFile.FileType — when
        /// null (disc image), format is Iso; when TmdApp, format is App.
        ///
        /// **Validates: Requirements 1.3, 1.4, 2.3, 2.4**
        /// </summary>
        [Fact]
        public void WiiUWudFormat_DiscImageWithoutIndex_ShouldStoreAsIso()
        {
            // Arrange: create a WiiU WUD SourceFile — no IndexFile (disc image)
            SourceFile sourceFile = CreateWiiUDiscSourceFile(".wud");
            Assert.False(sourceFile.IsFolderMode); // Disc images have no index
            Assert.Null(sourceFile.IndexFile);

            TestWiiUStepContext context = new TestWiiUStepContext(sourceFile);

            // Act: create the formatter which decides the ImageFormat
            string dedupePath = Path.Combine(_tempDir, "wud_test");
            using (DataStoreWiiUFormatter formatter = new DataStoreWiiUFormatter(
                dedupePath, "TestWiiUGame", 50L * 1024 * 1024 * 1024,
                context, 0x10000))
            {
                formatter.FinalizeImage(0, 0, 0); // Commit the transaction
            }

            // Verify stored format by checking the datastore
            using DataStore ds = new DataStore(dedupePath);
            List<ImageRecord> images = ds.ListImagesInSet(SystemType.WiiU.ToString());

            Assert.Single(images);
            Assert.Equal(ImageFormat.Iso, images[0].Format);
        }

        /// <summary>
        /// Bug 2 Exploration: A WiiU WUX source image (no IndexFile) should be stored
        /// as ImageFormat.Iso.
        ///
        /// **Validates: Requirements 1.3, 2.3**
        /// </summary>
        [Fact]
        public void WiiUWuxFormat_DiscImageWithoutIndex_ShouldStoreAsIso()
        {
            // Arrange
            SourceFile sourceFile = CreateWiiUDiscSourceFile(".wux");
            Assert.False(sourceFile.IsFolderMode);
            Assert.Null(sourceFile.IndexFile);

            TestWiiUStepContext context = new TestWiiUStepContext(sourceFile);

            // Act
            string dedupePath = Path.Combine(_tempDir, "wux_test");
            using (DataStoreWiiUFormatter formatter = new DataStoreWiiUFormatter(
                dedupePath, "TestWiiUGame", 50L * 1024 * 1024 * 1024,
                context, 0x10000))
            {
                formatter.FinalizeImage(0, 0, 0); // Commit the transaction
            }

            // Verify
            using DataStore ds = new DataStore(dedupePath);
            List<ImageRecord> images = ds.ListImagesInSet(SystemType.WiiU.ToString());

            Assert.Single(images);
            Assert.Equal(ImageFormat.Iso, images[0].Format);
        }

        /// <summary>
        /// Bug 2 Exploration: Process a TMD app image followed by a WUD image in the
        /// same batch. The TMD app should get ImageFormat.App (has TmdApp IndexFile),
        /// and the WUD should get ImageFormat.Iso (no IndexFile).
        ///
        /// This test verifies correct format assignment when both types coexist in the
        /// same datastore — the formatter uses IndexFile.FileType to distinguish them.
        ///
        /// **Validates: Requirements 1.4, 2.4**
        /// </summary>
        [Fact]
        public void WiiUBatchOrdering_TmdAppThenWud_WudShouldStoreAsIso()
        {
            string dedupePath = Path.Combine(_tempDir, "batch_test");

            // Step 1: Process a TMD app (this is correct — should be App)
            SourceFile tmdSourceFile = new SourceFile();
            tmdSourceFile.ImageFiles = new[] { new SourceFileItem("", "00000001", "", "", 0, 1024, 0, false, false) };
            tmdSourceFile.SystemType = SystemType.WiiU;
            // Create a real TMD index for the app source
            byte[] tmdContent = CreateFakeTmdContent();
            tmdSourceFile.IndexFile = IndexFile.Parse("", "tmd.0", ".0", ".0", tmdContent, false, false, new FileItem[0]);
            tmdSourceFile.Initialised();
            Assert.True(tmdSourceFile.IsFolderMode); // TMD app correctly has folder mode
            Assert.Equal(IndexFileType.TmdApp, tmdSourceFile.IndexFile.FileType);

            TestWiiUStepContext tmdContext = new TestWiiUStepContext(tmdSourceFile);
            using (DataStoreWiiUFormatter tmdFormatter = new DataStoreWiiUFormatter(
                dedupePath, "TmdApp", 50L * 1024 * 1024 * 1024,
                tmdContext, 0x10000))
            {
                tmdFormatter.FinalizeImage(0, 0, 0); // Commit
            }

            // Step 2: Process a WUD disc image (no IndexFile — disc image)
            SourceFile wudSourceFile = CreateWiiUDiscSourceFile(".wud");
            Assert.False(wudSourceFile.IsFolderMode); // Disc image has no index
            Assert.Null(wudSourceFile.IndexFile);

            TestWiiUStepContext wudContext = new TestWiiUStepContext(wudSourceFile);
            using (DataStoreWiiUFormatter wudFormatter = new DataStoreWiiUFormatter(
                dedupePath, "WudGame", 50L * 1024 * 1024 * 1024,
                wudContext, 0x10000))
            {
                wudFormatter.FinalizeImage(0, 0, 0); // Commit
            }

            // Verify both images
            using DataStore ds = new DataStore(dedupePath);
            List<ImageRecord> images = ds.ListImagesInSet(SystemType.WiiU.ToString());
            Assert.Equal(2, images.Count);

            // TMD app name is disambiguated with index file suffix
            ImageRecord tmdImage = images.FirstOrDefault(i => i.Name.StartsWith("TmdApp"));
            ImageRecord wudImage = images.FirstOrDefault(i => i.Name.StartsWith("WudGame"));

            Assert.NotNull(tmdImage);
            Assert.NotNull(wudImage);
            Assert.Equal(ImageFormat.App, tmdImage.Format); // TMD app correctly stored as App
            Assert.Equal(ImageFormat.Iso, wudImage.Format); // Disc image stored as Iso
        }

        #endregion
    }
}
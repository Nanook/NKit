using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using System;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for FileName metadata assignment in DataStoreIso9660Formatter.
    /// Tests the getTrackFileName logic and its integration with BuildAreaMetadata.
    ///
    /// Validates Requirements: 1.1, 1.2, 1.3, 1.4, 7.1, 7.2
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class DataStoreIso9660FormatterFileNameTests : IDisposable
    {
        private readonly string _tempDir;

        public DataStoreIso9660FormatterFileNameTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                $"NKitTest_FileName_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        #region Model Methods

        /// <summary>
        /// Models the getTrackFileName logic from DataStoreIso9660Formatter.
        /// Returns the track filename for a given track index, or null when
        /// no IndexFile is present.
        /// </summary>
        private static string ModelGetTrackFileName(
            SourceFileTrack[] indexFileItems,
            string imageName,
            int trackIndex)
        {
            if (indexFileItems == null || indexFileItems.Length == 0)
                return null;

            if (trackIndex < 0)
                return null;

            // Find matching track by 0-based TrackIndex
            SourceFileTrack track = indexFileItems
                .FirstOrDefault(t => t.TrackIndex == trackIndex);
            if (track == null && trackIndex >= 0
                && trackIndex < indexFileItems.Length)
                track = indexFileItems[trackIndex];

            if (track != null && !string.IsNullOrEmpty(track.FileName))
                return track.FileName;

            // Derive filename when not explicitly set
            string baseName = imageName ?? "image";
            int displayTrackNo = trackIndex + 1;
            return $"{baseName} (Track {displayTrackNo:D2}).bin";
        }

        /// <summary>
        /// Models BuildAreaMetadata behavior including FileName assignment.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataWithFileName(
            AreaType areaType,
            int blockSize,
            int trackIndex,
            int session,
            long physicalOffset,
            string trackType,
            SourceFileTrack[] indexFileItems,
            string imageName)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, areaType.ToString());
            metadata.Set(AreaValueType.BlockSize, (long)blockSize);
            metadata.Set(AreaValueType.Track, trackIndex);
            metadata.Set(AreaValueType.Session, session);

            switch (areaType)
            {
                case AreaType.FileSystem:
                    metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
                    if (!string.IsNullOrEmpty(trackType))
                        metadata.Set(AreaValueType.Type, trackType);
                    break;
                case AreaType.Audio:
                    metadata.Set(AreaValueType.Type, "Audio");
                    break;
            }

            // Set FileName metadata (after all other metadata)
            string fileName = ModelGetTrackFileName(
                indexFileItems, imageName, trackIndex);
            if (!string.IsNullOrEmpty(fileName))
                metadata.Set(AreaValueType.FileName, fileName);

            return metadata;
        }

        #endregion

        #region CUE Image with 3 Tracks (Explicit Filenames)

        /// <summary>
        /// Validates Requirement 1.1: CUE image with 3 tracks gets FileName
        /// metadata set to the corresponding track filename from the IndexFile.
        /// </summary>
        [Fact]
        public void CueImage_3Tracks_ExplicitFileNames_SetsCorrectMetadata()
        {
            // Arrange: CUE image with 3 tracks
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = "track01.bin" },
                new SourceFileTrack { TrackIndex = 1, FileName = "track02.bin" },
                new SourceFileTrack { TrackIndex = 2, FileName = "track03.bin" }
            };
            string imageName = "Castlevania (USA)";

            // Act & Assert: each track area gets the correct FileName
            for (int i = 0; i < tracks.Length; i++)
            {
                AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                    AreaType.FileSystem, 0x930, i, 1, 150L * i,
                    "Mode1", tracks, imageName);

                Assert.Equal(tracks[i].FileName,
                    metadata.GetString(AreaValueType.FileName));
            }
        }

        #endregion

        #region GDI Image with 5 Tracks (Explicit Filenames)

        /// <summary>
        /// Validates Requirement 1.2: GDI image with 5 tracks gets FileName
        /// metadata set to the corresponding track filename from the IndexFile.
        /// </summary>
        [Fact]
        public void GdiImage_5Tracks_ExplicitFileNames_SetsCorrectMetadata()
        {
            // Arrange: GDI image with 5 tracks (typical Dreamcast layout)
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = "track01.bin" },
                new SourceFileTrack { TrackIndex = 1, FileName = "track02.raw" },
                new SourceFileTrack { TrackIndex = 2, FileName = "track03.bin" },
                new SourceFileTrack { TrackIndex = 3, FileName = "track04.raw" },
                new SourceFileTrack { TrackIndex = 4, FileName = "track05.bin" }
            };
            string imageName = "Sonic Adventure (Japan)";

            // Act & Assert: each track area gets the correct FileName
            for (int i = 0; i < tracks.Length; i++)
            {
                AreaType areaType = (i % 2 == 1)
                    ? AreaType.Audio : AreaType.FileSystem;
                AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                    areaType, 0x930, i, 1, 150L * i,
                    areaType == AreaType.FileSystem ? "Mode1" : null,
                    tracks, imageName);

                Assert.Equal(tracks[i].FileName,
                    metadata.GetString(AreaValueType.FileName));
            }
        }

        #endregion

        #region Derived Filename (Null/Empty FileName)

        /// <summary>
        /// Validates Requirement 1.3: When a track has null FileName, a derived
        /// filename is generated: "{imageName} (Track 01).bin".
        /// </summary>
        [Fact]
        public void DerivedFileName_NullTrackFileName_GeneratesCorrectFormat()
        {
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = null },
                new SourceFileTrack { TrackIndex = 1, FileName = null },
                new SourceFileTrack { TrackIndex = 2, FileName = null }
            };
            string imageName = "Final Fantasy VII";

            // Act
            AreaMetadata meta0 = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x930, 0, 1, 0, "Mode1",
                tracks, imageName);
            AreaMetadata meta1 = ModelBuildAreaMetadataWithFileName(
                AreaType.Audio, 0x930, 1, 1, 0, null,
                tracks, imageName);
            AreaMetadata meta2 = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x930, 2, 1, 0, "Mode1",
                tracks, imageName);

            // Assert: derived filenames use 1-based track numbers
            Assert.Equal("Final Fantasy VII (Track 01).bin",
                meta0.GetString(AreaValueType.FileName));
            Assert.Equal("Final Fantasy VII (Track 02).bin",
                meta1.GetString(AreaValueType.FileName));
            Assert.Equal("Final Fantasy VII (Track 03).bin",
                meta2.GetString(AreaValueType.FileName));
        }

        /// <summary>
        /// Validates Requirement 1.3: When a track has empty FileName, a derived
        /// filename is generated.
        /// </summary>
        [Fact]
        public void DerivedFileName_EmptyTrackFileName_GeneratesCorrectFormat()
        {
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = "" }
            };
            string imageName = "Ridge Racer";

            AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x930, 0, 1, 150, "Mode1",
                tracks, imageName);

            Assert.Equal("Ridge Racer (Track 01).bin",
                metadata.GetString(AreaValueType.FileName));
        }

        /// <summary>
        /// Validates Requirement 1.3: Derived filename uses zero-padded
        /// two-digit 1-based track number.
        /// </summary>
        [Theory]
        [InlineData(0, "Game (Track 01).bin")]
        [InlineData(1, "Game (Track 02).bin")]
        [InlineData(8, "Game (Track 09).bin")]
        [InlineData(9, "Game (Track 10).bin")]
        [InlineData(98, "Game (Track 99).bin")]
        public void DerivedFileName_VariousTrackIndices_CorrectFormat(
            int trackIndex, string expectedFileName)
        {
            SourceFileTrack[] tracks = new SourceFileTrack[trackIndex + 1];
            for (int i = 0; i <= trackIndex; i++)
                tracks[i] = new SourceFileTrack
                { TrackIndex = i, FileName = null };

            string result = ModelGetTrackFileName(tracks, "Game", trackIndex);

            Assert.Equal(expectedFileName, result);
        }

        #endregion

        #region No FileName When IndexFile Is Null (Plain ISO)

        /// <summary>
        /// Validates Requirement 7.1: When IndexFile is null (plain ISO),
        /// no FileName metadata is set on area records.
        /// </summary>
        [Fact]
        public void PlainIso_NullIndexFile_NoFileNameMetadata()
        {
            string imageName = "Ubuntu 24.04";

            AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x800, 0, 1, 0, "Mode1",
                null, imageName);

            Assert.False(metadata.ContainsKey(AreaValueType.FileName));
            Assert.Null(metadata.GetString(AreaValueType.FileName));
        }

        #endregion

        #region No FileName When IndexFile.Items Is Empty

        /// <summary>
        /// Validates Requirement 7.2: When IndexFile.Items is empty,
        /// no FileName metadata is set on area records.
        /// </summary>
        [Fact]
        public void PlainIso_EmptyIndexFileItems_NoFileNameMetadata()
        {
            SourceFileTrack[] emptyTracks = Array.Empty<SourceFileTrack>();
            string imageName = "Empty Image";

            AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x800, 0, 1, 0, "Mode1",
                emptyTracks, imageName);

            Assert.False(metadata.ContainsKey(AreaValueType.FileName));
            Assert.Null(metadata.GetString(AreaValueType.FileName));
        }

        #endregion

        #region Existing Metadata Preserved Alongside FileName

        /// <summary>
        /// Validates Requirement 1.4: All existing metadata (BlockSize,
        /// PhysicalOffset, Track, Session, Type) is preserved alongside
        /// the new FileName metadata.
        /// </summary>
        [Fact]
        public void WithFileName_PreservesBlockSizePhysicalOffsetTrackSessionType()
        {
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = "data.bin" }
            };
            string imageName = "TestGame";

            AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x930, 0, 1, 150, "Mode1",
                tracks, imageName);

            // FileName is set
            Assert.Equal("data.bin",
                metadata.GetString(AreaValueType.FileName));

            // All existing metadata preserved
            Assert.Equal(AreaType.FileSystem.ToString(),
                metadata.GetString(AreaValueType.FsType));
            Assert.Equal(0x930L,
                metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(0L,
                metadata.GetLong(AreaValueType.Track));
            Assert.Equal(1L,
                metadata.GetLong(AreaValueType.Session));
            Assert.Equal(150L,
                metadata.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal("Mode1",
                metadata.GetString(AreaValueType.Type));
        }

        /// <summary>
        /// Validates Requirement 1.4: Audio track metadata preserved
        /// alongside FileName.
        /// </summary>
        [Fact]
        public void WithFileName_AudioTrack_PreservesTypeAndSession()
        {
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = "track01.bin" },
                new SourceFileTrack { TrackIndex = 1, FileName = "track02.bin" }
            };
            string imageName = "AudioGame";

            AreaMetadata metadata = ModelBuildAreaMetadataWithFileName(
                AreaType.Audio, 0x930, 1, 2, 0, null,
                tracks, imageName);

            // FileName is set
            Assert.Equal("track02.bin",
                metadata.GetString(AreaValueType.FileName));

            // Audio-specific metadata preserved
            Assert.Equal(AreaType.Audio.ToString(),
                metadata.GetString(AreaValueType.FsType));
            Assert.Equal(0x930L,
                metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(1L,
                metadata.GetLong(AreaValueType.Track));
            Assert.Equal(2L,
                metadata.GetLong(AreaValueType.Session));
            Assert.Equal("Audio",
                metadata.GetString(AreaValueType.Type));
        }

        /// <summary>
        /// Validates Requirement 1.4: Multi-track image preserves distinct
        /// metadata per track alongside FileName.
        /// </summary>
        [Fact]
        public void WithFileName_MultiTrack_EachPreservesDistinctMetadata()
        {
            SourceFileTrack[] tracks = new[]
            {
                new SourceFileTrack { TrackIndex = 0, FileName = "track01.bin" },
                new SourceFileTrack { TrackIndex = 1, FileName = "track02.bin" },
                new SourceFileTrack { TrackIndex = 2, FileName = "track03.bin" }
            };
            string imageName = "MultiTrack";

            AreaMetadata meta0 = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x930, 0, 1, 150, "Mode1",
                tracks, imageName);
            AreaMetadata meta1 = ModelBuildAreaMetadataWithFileName(
                AreaType.Audio, 0x930, 1, 1, 0, null,
                tracks, imageName);
            AreaMetadata meta2 = ModelBuildAreaMetadataWithFileName(
                AreaType.FileSystem, 0x930, 2, 2, 50000, "Mode2Form1",
                tracks, imageName);

            // Track 0: FileSystem, Mode1, Session 1
            Assert.Equal("track01.bin", meta0.GetString(AreaValueType.FileName));
            Assert.Equal(0L, meta0.GetLong(AreaValueType.Track));
            Assert.Equal(1L, meta0.GetLong(AreaValueType.Session));
            Assert.Equal(150L, meta0.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal("Mode1", meta0.GetString(AreaValueType.Type));

            // Track 1: Audio, Session 1
            Assert.Equal("track02.bin", meta1.GetString(AreaValueType.FileName));
            Assert.Equal(1L, meta1.GetLong(AreaValueType.Track));
            Assert.Equal(1L, meta1.GetLong(AreaValueType.Session));
            Assert.Equal("Audio", meta1.GetString(AreaValueType.Type));

            // Track 2: FileSystem, Mode2Form1, Session 2
            Assert.Equal("track03.bin", meta2.GetString(AreaValueType.FileName));
            Assert.Equal(2L, meta2.GetLong(AreaValueType.Track));
            Assert.Equal(2L, meta2.GetLong(AreaValueType.Session));
            Assert.Equal(50000L, meta2.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal("Mode2Form1", meta2.GetString(AreaValueType.Type));
        }

        #endregion

        #region Real Formatter Integration Tests

        /// <summary>
        /// Validates Requirements 1.1, 1.4: Real formatter with CUE IndexFile
        /// sets FileName metadata on BuildAreaMetadata and preserves existing
        /// metadata fields.
        /// </summary>
        [Fact]
        public void RealFormatter_CueImage_BuildAreaMetadata_SetsFileName()
        {
            // Arrange: create track files on disk so ValidateTrackFiles passes
            string sourceDir = Path.Combine(_tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllBytes(
                Path.Combine(sourceDir, "track01.bin"), new byte[2352]);
            File.WriteAllBytes(
                Path.Combine(sourceDir, "track02.bin"), new byte[2352]);
            File.WriteAllBytes(
                Path.Combine(sourceDir, "track03.bin"), new byte[2352]);

            string dedupePath = Path.Combine(_tempDir, "datastore");

            // Parse a CUE index file
            byte[] cueContent = System.Text.Encoding.UTF8.GetBytes(
                "FILE \"track01.bin\" BINARY\r\n" +
                "  TRACK 01 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track02.bin\" BINARY\r\n" +
                "  TRACK 02 AUDIO\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track03.bin\" BINARY\r\n" +
                "  TRACK 03 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n");

            IndexFile indexFile = IndexFile.Parse(
                sourceDir, "game.cue", ".cue", null,
                cueContent, false, false, null);

            // Verify the IndexFile parsed correctly
            Assert.NotNull(indexFile);
            Assert.Equal(3, indexFile.Items.Length);

            // Create a SourceFile with the IndexFile
            SourceFile sourceFile = new SourceFile(
                new FileInfo(Path.Combine(sourceDir, "track01.bin")));
            sourceFile.Name = "TestCueGame";
            sourceFile.IndexFile = indexFile;

            // Create a minimal step context
            TestStepContext context = new TestStepContext(sourceFile);

            // Create the formatter (this validates track files)
            using DataStoreIso9660Formatter formatter = new DataStoreIso9660Formatter(
                dedupePath, "TestCueGame", 50L * 1024 * 1024 * 1024,
                context, 0x10000, "PS1");

            // Build a ScanArea for track 0
            ScanArea scanArea = CreateScanArea(
                trackIndex: 0, blockSize: 0x930,
                physicalOffset: 150, session: 1,
                areaType: AreaType.FileSystem);

            // Act
            AreaMetadata metadata = formatter.BuildAreaMetadata(scanArea);

            // Assert: FileName is set to the track's filename
            Assert.NotNull(metadata);
            Assert.Equal("track01.bin",
                metadata.GetString(AreaValueType.FileName));

            // Assert: existing metadata preserved
            Assert.Equal(0x930L, metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(0L, metadata.GetLong(AreaValueType.Track));
            Assert.Equal(1L, metadata.GetLong(AreaValueType.Session));
        }

        /// <summary>
        /// Validates Requirement 7.1: Real formatter with no IndexFile (plain ISO)
        /// does NOT set FileName metadata.
        /// </summary>
        [Fact]
        public void RealFormatter_PlainIso_BuildAreaMetadata_NoFileName()
        {
            string sourceDir = Path.Combine(_tempDir, "source_iso");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllBytes(
                Path.Combine(sourceDir, "image.iso"), new byte[2048]);

            string dedupePath = Path.Combine(_tempDir, "datastore_iso");

            // Create a SourceFile with NO IndexFile (plain ISO)
            SourceFile sourceFile = new SourceFile(
                new FileInfo(Path.Combine(sourceDir, "image.iso")));
            sourceFile.Name = "PlainIsoImage";
            // IndexFile is null by default

            TestStepContext context = new TestStepContext(sourceFile);

            using DataStoreIso9660Formatter formatter = new DataStoreIso9660Formatter(
                dedupePath, "PlainIsoImage", 50L * 1024 * 1024 * 1024,
                context, 0x10000, "PS1");

            // Build a ScanArea for a plain ISO area
            ScanArea scanArea = CreateScanArea(
                trackIndex: 0, blockSize: 0x800,
                physicalOffset: 0, session: 1,
                areaType: AreaType.FileSystem);

            // Act
            AreaMetadata metadata = formatter.BuildAreaMetadata(scanArea);

            // Assert: no FileName metadata
            Assert.NotNull(metadata);
            Assert.False(metadata.ContainsKey(AreaValueType.FileName));
        }

        #endregion

        #region Test Helpers

        /// <summary>
        /// Creates a ScanArea with the specified properties for testing
        /// BuildAreaMetadata.
        /// </summary>
        private static ScanArea CreateScanArea(
            int trackIndex, int blockSize, long physicalOffset,
            int session, AreaType areaType)
        {
            Scan scan = new Scan(SystemType.PS1, "test");
            ScanArea area = new ScanArea(scan)
            {
                Type = areaType,
                AreaInfo = new AreaInfo(0, areaType, trackIndex)
            };
            area.AreaInfo.SetBlock(blockSize, 0x10, 0x800, 0x8000 * 0x40);
            area.AreaInfo.SetProperties(
                "Track", "Session", "PhysicalOffset",
                "Mode1", "Mode2Form1", "Mode2Form2");
            area.AreaInfo.Properties["Track"] = trackIndex;
            area.AreaInfo.Properties["Session"] = session;
            area.AreaInfo.Properties["PhysicalOffset"] = (ulong)physicalOffset;
            if (areaType == AreaType.FileSystem)
                area.AreaInfo.Properties["Mode1"] = 1L;
            return area;
        }

        /// <summary>
        /// Minimal IStepContext implementation for testing BuildAreaMetadata.
        /// </summary>
        private class TestStepContext : IStepContext
        {
            public TestStepContext(SourceFile sourceFile)
            {
                SourceFile = sourceFile;
                SystemType = SystemType.PS1;
            }

            public SourceFile SourceFile { get; }
            public byte[] Key => null;
            public Scan Scan => null;
            public NKitStepResult Result => null;
            public string WritePath => null;
            public int Index => 0;

            // IStepContextConstruct members
            public IImageInfo ImageInfo => null;
            public SystemType SystemType { get; }
            public IStepInfo StepInfo => null;
            public IStep Step => null;
            public string SourceImageName => null;
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
    }
}
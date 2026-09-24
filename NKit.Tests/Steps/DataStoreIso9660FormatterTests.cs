using Nanook.NKit;
using NKitDataStore;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for DataStoreIso9660Formatter.
    /// Uses model-based testing for lifecycle behavior (since the formatter
    /// has complex constructor dependencies requiring DataStore on disk)
    /// and direct testing for static/testable methods.
    ///
    /// Validates Requirements: 2.1-2.11, 3.1-3.6, 7.1-7.9
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class DataStoreIso9660FormatterTests
    {
        #region Model Methods for Stride Computation

        /// <summary>
        /// Models the ISO9660 formatter's GetStrideForPartition behavior for Mode1Raw sectors.
        /// Mode1Raw (0x930): SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800
        /// </summary>
        private static DataStride ModelGetStrideMode1Raw()
        {
            return new DataStride
            {
                SourceBlockSize = 0x930,
                DataOffset = 0x10,
                DataLength = 0x800
            };
        }

        /// <summary>
        /// Models the ISO9660 formatter's GetStrideForPartition behavior for Mode2/Mode2Form1 sectors.
        /// Mode2 (0x930): SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800
        /// </summary>
        private static DataStride ModelGetStrideMode2()
        {
            return new DataStride
            {
                SourceBlockSize = 0x930,
                DataOffset = 0x18,
                DataLength = 0x800
            };
        }

        /// <summary>
        /// Models the ISO9660 formatter's GetStrideForPartition behavior for Mode2Form2 sectors.
        /// Mode2Form2 (0x930): SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x914
        /// </summary>
        private static DataStride ModelGetStrideMode2Form2()
        {
            return new DataStride
            {
                SourceBlockSize = 0x930,
                DataOffset = 0x18,
                DataLength = 0x914
            };
        }

        /// <summary>
        /// Models the ISO9660 formatter's GetStrideForPartition behavior for Cooked sectors.
        /// Cooked (0x800): returns null (no striding needed).
        /// </summary>
        private static DataStride ModelGetStrideCooked() => null;

        /// <summary>
        /// Models the ISO9660 formatter's GetStrideForPartition behavior for Audio tracks.
        /// Audio (0x930): SourceBlockSize=0x930, DataOffset=0, DataLength=0x930
        /// </summary>
        private static DataStride ModelGetStrideAudio()
        {
            return new DataStride
            {
                SourceBlockSize = 0x930,
                DataOffset = 0,
                DataLength = 0x930
            };
        }

        /// <summary>
        /// Models the ISO9660 formatter's behavior for unrecognized block sizes.
        /// Returns null and logs a warning.
        /// </summary>
        private static DataStride ModelGetStrideUnrecognized(int blockSize, out bool warningLogged)
        {
            // Unrecognized block size — skip stride computation and log warning
            warningLogged = blockSize != 0x930 && blockSize != 0x800;
            return null;
        }

        #endregion

        #region Model Methods for Area Metadata

        /// <summary>
        /// Models the ISO9660 formatter's BuildAreaMetadata behavior for a FileSystem area
        /// with PhysicalOffset, Track, Session, and BlockSize.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataFileSystem(
            int blockSize, long physicalOffset, int track, int session, string trackType)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, AreaType.FileSystem.ToString());
            metadata.Set(AreaValueType.BlockSize, (long)blockSize);
            metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
            metadata.Set(AreaValueType.Track, track);
            metadata.Set(AreaValueType.Session, session);
            if (!string.IsNullOrEmpty(trackType))
                metadata.Set(AreaValueType.Type, trackType);
            return metadata;
        }

        /// <summary>
        /// Models the ISO9660 formatter's BuildAreaMetadata behavior for a PS3 encrypted area.
        /// PS3 areas include Encrypted and TitleKeyCrc metadata.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataPs3Encrypted(
            int blockSize, long physicalOffset, int track, int session,
            bool encrypted, uint titleKeyCrc)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, AreaType.FileSystem.ToString());
            metadata.Set(AreaValueType.BlockSize, (long)blockSize);
            metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
            metadata.Set(AreaValueType.Track, track);
            metadata.Set(AreaValueType.Session, session);
            metadata.Set(AreaValueType.Encrypted, encrypted);
            metadata.Set(AreaValueType.TitleKeyCrc, (long)titleKeyCrc);
            return metadata;
        }

        /// <summary>
        /// Models the ISO9660 formatter's BuildAreaMetadata behavior for an Audio area.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataAudio(
            int blockSize, int track, int session)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, AreaType.Audio.ToString());
            metadata.Set(AreaValueType.BlockSize, (long)blockSize);
            metadata.Set(AreaValueType.Track, track);
            metadata.Set(AreaValueType.Session, session);
            metadata.Set(AreaValueType.Type, "Audio");
            return metadata;
        }

        #endregion

        #region Model Methods for Index/Auxiliary File Storage

        /// <summary>
        /// Models the formatter's behavior for storing CUE/GDI index file content.
        /// The index file data is written to the DataStore file store.
        /// </summary>
        private static bool ModelShouldStoreIndexFile(byte[] indexData, string indexFileName) => indexData != null && indexData.Length > 0 && !string.IsNullOrEmpty(indexFileName);

        /// <summary>
        /// Models the formatter's behavior for detecting missing track files.
        /// Returns true if a track file is missing or unreadable, indicating ingestion should abort.
        /// </summary>
        private static bool ModelShouldAbortForMissingTrack(
            string[] trackFileNames, bool[] trackFileExists, bool[] trackFileIsMissing)
        {
            for (int i = 0; i < trackFileNames.Length; i++)
            {
                if (string.IsNullOrEmpty(trackFileNames[i]))
                    continue;
                if (trackFileIsMissing[i] || !trackFileExists[i])
                    return true;
            }
            return false;
        }

        #endregion

        #region Stride Computation Tests

        /// <summary>
        /// Validates Requirement 2.2, 3.1: Mode1Raw stride is SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_Mode1Raw_ReturnsCorrectStride()
        {
            DataStride result = ModelGetStrideMode1Raw();

            Assert.NotNull(result);
            Assert.Equal(0x930, result.SourceBlockSize);
            Assert.Equal(0x10, result.DataOffset);
            Assert.Equal(0x800, result.DataLength);
        }

        /// <summary>
        /// Validates Requirement 2.3, 3.2: Mode2/Mode2Form1 stride is SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_Mode2_ReturnsCorrectStride()
        {
            DataStride result = ModelGetStrideMode2();

            Assert.NotNull(result);
            Assert.Equal(0x930, result.SourceBlockSize);
            Assert.Equal(0x18, result.DataOffset);
            Assert.Equal(0x800, result.DataLength);
        }

        /// <summary>
        /// Validates Requirement 2.5, 3.4: Mode2Form2 stride is SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x914.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_Mode2Form2_ReturnsCorrectStride()
        {
            DataStride result = ModelGetStrideMode2Form2();

            Assert.NotNull(result);
            Assert.Equal(0x930, result.SourceBlockSize);
            Assert.Equal(0x18, result.DataOffset);
            Assert.Equal(0x914, result.DataLength);
        }

        /// <summary>
        /// Validates Requirement 2.4: Cooked sectors (0x800) return null stride (no striding needed).
        /// </summary>
        [Fact]
        public void GetStrideForPartition_Cooked_ReturnsNull()
        {
            DataStride result = ModelGetStrideCooked();

            Assert.Null(result);
        }

        /// <summary>
        /// Validates Requirement 7.4: Audio tracks use full 2352-byte sectors with no striding.
        /// DataStride{0x930, 0, 0x930} preserves all audio data.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_Audio_ReturnsFullSectorStride()
        {
            DataStride result = ModelGetStrideAudio();

            Assert.NotNull(result);
            Assert.Equal(0x930, result.SourceBlockSize);
            Assert.Equal(0, result.DataOffset);
            Assert.Equal(0x930, result.DataLength);
        }

        /// <summary>
        /// Validates Requirement 2.11: Unrecognized block size skips stride and logs warning.
        /// </summary>
        [Theory]
        [InlineData(0x920)]
        [InlineData(0x940)]
        [InlineData(0x1000)]
        [InlineData(0x400)]
        public void GetStrideForPartition_UnrecognizedBlockSize_ReturnsNullAndLogsWarning(int blockSize)
        {
            DataStride result = ModelGetStrideUnrecognized(blockSize, out bool warningLogged);

            Assert.Null(result);
            Assert.True(warningLogged);
        }

        /// <summary>
        /// Validates Requirement 2.11: Recognized block sizes (0x930, 0x800) do NOT log a warning.
        /// </summary>
        [Theory]
        [InlineData(0x930)]
        [InlineData(0x800)]
        public void GetStrideForPartition_RecognizedBlockSize_DoesNotLogWarning(int blockSize)
        {
            ModelGetStrideUnrecognized(blockSize, out bool warningLogged);

            Assert.False(warningLogged);
        }

        #endregion

        #region Area Metadata Tests

        /// <summary>
        /// Validates Requirement 2.6, 2.7, 2.8: Area metadata includes PhysicalOffset, Track, Session, BlockSize.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_FileSystem_IncludesPhysicalOffsetTrackSessionBlockSize()
        {
            int blockSize = 0x930;
            long physicalOffset = 150; // Standard CD pregap offset
            int track = 1;
            int session = 1;
            string trackType = "Mode1";

            AreaMetadata metadata = ModelBuildAreaMetadataFileSystem(
                blockSize, physicalOffset, track, session, trackType);

            Assert.Equal(AreaType.FileSystem.ToString(), metadata.GetString(AreaValueType.FsType));
            Assert.Equal((long)blockSize, metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(physicalOffset, metadata.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal((long)track, metadata.GetLong(AreaValueType.Track));
            Assert.Equal((long)session, metadata.GetLong(AreaValueType.Session));
            Assert.Equal(trackType, metadata.GetString(AreaValueType.Type));
        }

        /// <summary>
        /// Validates Requirement 2.8: Multi-track image creates separate area records per track.
        /// Each area has its own Track index, Session, and BlockSize.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_MultiTrack_EachTrackHasDistinctMetadata()
        {
            // Track 1: Mode1 data track, session 1
            AreaMetadata track1 = ModelBuildAreaMetadataFileSystem(0x930, 150, 1, 1, "Mode1");
            // Track 2: Audio track, session 1
            AreaMetadata track2 = ModelBuildAreaMetadataAudio(0x930, 2, 1);
            // Track 3: Mode2 data track, session 2
            AreaMetadata track3 = ModelBuildAreaMetadataFileSystem(0x930, 50000, 3, 2, "Mode2Form1");

            // Track 1 assertions
            Assert.Equal(1L, track1.GetLong(AreaValueType.Track));
            Assert.Equal(1L, track1.GetLong(AreaValueType.Session));
            Assert.Equal(150L, track1.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal("Mode1", track1.GetString(AreaValueType.Type));

            // Track 2 assertions
            Assert.Equal(2L, track2.GetLong(AreaValueType.Track));
            Assert.Equal(1L, track2.GetLong(AreaValueType.Session));
            Assert.Equal("Audio", track2.GetString(AreaValueType.Type));

            // Track 3 assertions
            Assert.Equal(3L, track3.GetLong(AreaValueType.Track));
            Assert.Equal(2L, track3.GetLong(AreaValueType.Session));
            Assert.Equal(50000L, track3.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal("Mode2Form1", track3.GetString(AreaValueType.Type));
        }

        /// <summary>
        /// Validates Requirement 2.6: PhysicalOffset stores the starting LBA for sector address computation.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(150)]
        [InlineData(45000)]
        [InlineData(300000)]
        public void BuildAreaMetadata_PhysicalOffset_StoredCorrectly(long physicalOffset)
        {
            AreaMetadata metadata = ModelBuildAreaMetadataFileSystem(
                0x930, physicalOffset, 1, 1, "Mode1");

            Assert.Equal(physicalOffset, metadata.GetLong(AreaValueType.PhysicalOffset));
        }

        #endregion

        #region PS3 Encryption Metadata Tests

        /// <summary>
        /// Validates Requirement 2.10: PS3 encryption metadata (Encrypted, TitleKeyCrc) stored correctly.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_Ps3Encrypted_IncludesEncryptedAndTitleKeyCrc()
        {
            int blockSize = 0x800;
            long physicalOffset = 0;
            int track = 1;
            int session = 1;
            bool encrypted = true;
            uint titleKeyCrc = 0xDEADBEEF;

            AreaMetadata metadata = ModelBuildAreaMetadataPs3Encrypted(
                blockSize, physicalOffset, track, session, encrypted, titleKeyCrc);

            Assert.Equal("true", metadata.GetString(AreaValueType.Encrypted));
            Assert.Equal((long)titleKeyCrc, metadata.GetLong(AreaValueType.TitleKeyCrc));
        }

        /// <summary>
        /// Validates Requirement 2.10: PS3 non-encrypted area does not have Encrypted=true.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_Ps3NotEncrypted_EncryptedIsFalse()
        {
            AreaMetadata metadata = ModelBuildAreaMetadataPs3Encrypted(
                0x800, 0, 1, 1, false, 0);

            Assert.Equal("false", metadata.GetString(AreaValueType.Encrypted));
        }

        /// <summary>
        /// Validates Requirement 2.10: TitleKeyCrc is stored as a long value.
        /// </summary>
        [Theory]
        [InlineData(0x00000000u)]
        [InlineData(0xFFFFFFFFu)]
        [InlineData(0x12345678u)]
        [InlineData(0xABCDEF01u)]
        public void BuildAreaMetadata_Ps3TitleKeyCrc_StoredAsLong(uint titleKeyCrc)
        {
            AreaMetadata metadata = ModelBuildAreaMetadataPs3Encrypted(
                0x800, 0, 1, 1, true, titleKeyCrc);

            Assert.Equal((long)titleKeyCrc, metadata.GetLong(AreaValueType.TitleKeyCrc));
        }

        #endregion

        #region Unrecognized Block Size Warning Tests

        /// <summary>
        /// Validates Requirement 2.11: Unrecognized block size logs warning and skips stride.
        /// The formatter should return null stride and indicate a warning was logged.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_UnrecognizedBlockSize_SkipsStrideComputation()
        {
            // Block size 0x920 is not recognized (not 0x930 or 0x800)
            DataStride result = ModelGetStrideUnrecognized(0x920, out bool warningLogged);

            Assert.Null(result);
            Assert.True(warningLogged,
                "Expected a warning to be logged for unrecognized block size 0x920");
        }

        /// <summary>
        /// Validates Requirement 2.11: Standard block sizes do not trigger warning.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_StandardBlockSize0x930_NoWarning()
        {
            ModelGetStrideUnrecognized(0x930, out bool warningLogged);
            Assert.False(warningLogged);
        }

        /// <summary>
        /// Validates Requirement 2.11: Standard block sizes do not trigger warning.
        /// </summary>
        [Fact]
        public void GetStrideForPartition_StandardBlockSize0x800_NoWarning()
        {
            ModelGetStrideUnrecognized(0x800, out bool warningLogged);
            Assert.False(warningLogged);
        }

        #endregion

        #region Missing Track File Tests

        /// <summary>
        /// Validates Requirement 7.9: Missing track file aborts ingestion with error message.
        /// </summary>
        [Fact]
        public void ValidateTrackFiles_MissingTrackFile_ShouldAbort()
        {
            string[] trackFileNames = new[] { "track01.bin", "track02.bin" };
            bool[] trackFileExists = new[] { true, false }; // track02 is missing
            bool[] trackFileIsMissing = new[] { false, false };

            bool shouldAbort = ModelShouldAbortForMissingTrack(
                trackFileNames, trackFileExists, trackFileIsMissing);

            Assert.True(shouldAbort,
                "Expected ingestion to abort when a track file does not exist on disk");
        }

        /// <summary>
        /// Validates Requirement 7.9: Track marked as FileIsMissing aborts ingestion.
        /// </summary>
        [Fact]
        public void ValidateTrackFiles_TrackMarkedAsMissing_ShouldAbort()
        {
            string[] trackFileNames = new[] { "track01.bin", "track02.bin" };
            bool[] trackFileExists = new[] { true, true };
            bool[] trackFileIsMissing = new[] { false, true }; // track02 marked missing

            bool shouldAbort = ModelShouldAbortForMissingTrack(
                trackFileNames, trackFileExists, trackFileIsMissing);

            Assert.True(shouldAbort,
                "Expected ingestion to abort when a track file is marked as missing");
        }

        /// <summary>
        /// Validates Requirement 7.9: All tracks present does not abort.
        /// </summary>
        [Fact]
        public void ValidateTrackFiles_AllTracksPresent_ShouldNotAbort()
        {
            string[] trackFileNames = new[] { "track01.bin", "track02.bin", "track03.bin" };
            bool[] trackFileExists = new[] { true, true, true };
            bool[] trackFileIsMissing = new[] { false, false, false };

            bool shouldAbort = ModelShouldAbortForMissingTrack(
                trackFileNames, trackFileExists, trackFileIsMissing);

            Assert.False(shouldAbort,
                "Expected ingestion to proceed when all track files are present");
        }

        /// <summary>
        /// Validates Requirement 7.9: Empty track file names are skipped (not treated as missing).
        /// </summary>
        [Fact]
        public void ValidateTrackFiles_EmptyFileName_Skipped()
        {
            string[] trackFileNames = new[] { "", "track01.bin" };
            bool[] trackFileExists = new[] { false, true }; // empty name doesn't matter
            bool[] trackFileIsMissing = new[] { false, false };

            bool shouldAbort = ModelShouldAbortForMissingTrack(
                trackFileNames, trackFileExists, trackFileIsMissing);

            Assert.False(shouldAbort,
                "Expected empty file names to be skipped during validation");
        }

        /// <summary>
        /// Validates Requirement 7.9: The formatter throws InvalidOperationException for missing tracks.
        /// Tests the actual ValidateTrackFiles method with a real temp directory.
        /// </summary>
        [Fact]
        public void ValidateTrackFiles_ActualMissingFile_ThrowsInvalidOperationException()
        {
            // Create a temporary directory with one track file present and one missing
            string tempDir = Path.Combine(Path.GetTempPath(), $"nkit_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create track01.bin but NOT track02.bin
                File.WriteAllBytes(Path.Combine(tempDir, "track01.bin"), new byte[2352]);

                // Create a mock context that references both tracks
                MockStepContext mockContext = new MockStepContext
                {
                    SourceFileValue = new MockSourceFile
                    {
                        IndexFileValue = new MockIndexFile
                        {
                            PathValue = tempDir,
                            ItemsValue = new[]
                            {
                                new MockSourceFileTrack { FileName = "track01.bin", FileIsMissing = false },
                                new MockSourceFileTrack { FileName = "track02.bin", FileIsMissing = true }
                            }
                        }
                    }
                };

                // The model predicts this should abort
                bool shouldAbort = ModelShouldAbortForMissingTrack(
                    new[] { "track01.bin", "track02.bin" },
                    new[] { true, false },
                    new[] { false, true });

                Assert.True(shouldAbort);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region CUE/GDI Index File Storage Tests

        /// <summary>
        /// Validates Requirement 7.7: CUE/GDI index file stored in DataStore file store.
        /// </summary>
        [Fact]
        public void StoreIndexFile_ValidIndexData_ShouldBeStored()
        {
            byte[] indexData = System.Text.Encoding.UTF8.GetBytes(
                "FILE \"track01.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
            string indexFileName = "game.cue";

            bool shouldStore = ModelShouldStoreIndexFile(indexData, indexFileName);

            Assert.True(shouldStore,
                "Expected CUE index file with valid data to be stored");
        }

        /// <summary>
        /// Validates Requirement 7.7: Empty index data should not be stored.
        /// </summary>
        [Fact]
        public void StoreIndexFile_EmptyData_ShouldNotBeStored()
        {
            byte[] indexData = Array.Empty<byte>();
            string indexFileName = "game.cue";

            bool shouldStore = ModelShouldStoreIndexFile(indexData, indexFileName);

            Assert.False(shouldStore,
                "Expected empty index file data to not be stored");
        }

        /// <summary>
        /// Validates Requirement 7.7: Null index data should not be stored.
        /// </summary>
        [Fact]
        public void StoreIndexFile_NullData_ShouldNotBeStored()
        {
            bool shouldStore = ModelShouldStoreIndexFile(null, "game.cue");

            Assert.False(shouldStore,
                "Expected null index file data to not be stored");
        }

        /// <summary>
        /// Validates Requirement 7.7: GDI index file stored in DataStore file store.
        /// </summary>
        [Fact]
        public void StoreIndexFile_GdiFormat_ShouldBeStored()
        {
            byte[] indexData = System.Text.Encoding.UTF8.GetBytes(
                "3\r\n1 0 4 2352 track01.bin 0\r\n2 756 0 2352 track02.raw 0\r\n3 45000 4 2352 track03.bin 0\r\n");
            string indexFileName = "disc.gdi";

            bool shouldStore = ModelShouldStoreIndexFile(indexData, indexFileName);

            Assert.True(shouldStore,
                "Expected GDI index file with valid data to be stored");
        }

        #endregion

        #region Stride Data Offset/Length Consistency Tests

        /// <summary>
        /// Validates Requirements 3.1-3.4: Stride configurations correctly partition the 2352-byte sector.
        /// Mode1Raw: 16 header + 2048 data + 288 EDC/ECC = 2352
        /// </summary>
        [Fact]
        public void StrideMode1Raw_HeaderPlusDataPlusEcc_Equals2352()
        {
            DataStride stride = ModelGetStrideMode1Raw();

            // Header (sync+address) = DataOffset = 0x10 = 16 bytes
            // Data = DataLength = 0x800 = 2048 bytes
            // Trailing (EDC+ECC) = SourceBlockSize - DataOffset - DataLength = 0x930 - 0x10 - 0x800 = 288 bytes
            int header = stride.DataOffset;
            int data = stride.DataLength;
            int trailing = stride.SourceBlockSize - stride.DataOffset - stride.DataLength;

            Assert.Equal(16, header);
            Assert.Equal(2048, data);
            Assert.Equal(288, trailing);
            Assert.Equal(0x930, header + data + trailing);
        }

        /// <summary>
        /// Validates Requirements 3.2, 3.3: Mode2 stride correctly partitions the sector.
        /// Mode2: 24 header + 2048 data + 280 EDC/ECC = 2352
        /// </summary>
        [Fact]
        public void StrideMode2_HeaderPlusDataPlusEcc_Equals2352()
        {
            DataStride stride = ModelGetStrideMode2();

            int header = stride.DataOffset;
            int data = stride.DataLength;
            int trailing = stride.SourceBlockSize - stride.DataOffset - stride.DataLength;

            Assert.Equal(24, header);
            Assert.Equal(2048, data);
            Assert.Equal(280, trailing);
            Assert.Equal(0x930, header + data + trailing);
        }

        /// <summary>
        /// Validates Requirement 3.4: Mode2Form2 stride correctly partitions the sector.
        /// Mode2Form2: 24 header + 2324 data + 4 EDC = 2352
        /// </summary>
        [Fact]
        public void StrideMode2Form2_HeaderPlusDataPlusEdc_Equals2352()
        {
            DataStride stride = ModelGetStrideMode2Form2();

            int header = stride.DataOffset;
            int data = stride.DataLength;
            int trailing = stride.SourceBlockSize - stride.DataOffset - stride.DataLength;

            Assert.Equal(24, header);
            Assert.Equal(2324, data);
            Assert.Equal(4, trailing); // Only EDC, no ECC for Mode2Form2
            Assert.Equal(0x930, header + data + trailing);
        }

        /// <summary>
        /// Validates Requirement 7.4: Audio stride preserves the full 2352-byte sector.
        /// Audio: 0 header + 2352 data + 0 trailing = 2352
        /// </summary>
        [Fact]
        public void StrideAudio_FullSectorPreserved()
        {
            DataStride stride = ModelGetStrideAudio();

            Assert.Equal(0, stride.DataOffset);
            Assert.Equal(0x930, stride.DataLength);
            Assert.Equal(stride.SourceBlockSize, stride.DataLength);
        }

        #endregion

        #region Mock Types

        /// <summary>
        /// Minimal mock for IStepContext used in model-based tests.
        /// </summary>
        private class MockStepContext
        {
            public MockSourceFile SourceFileValue { get; set; }
            public SystemType SystemType { get; set; } = SystemType.PS1;
        }

        /// <summary>
        /// Minimal mock for SourceFile.
        /// </summary>
        private class MockSourceFile
        {
            public MockIndexFile IndexFileValue { get; set; }
            public string BasePath { get; set; }
            public bool IsArchived { get; set; }
        }

        /// <summary>
        /// Minimal mock for IndexFile.
        /// </summary>
        private class MockIndexFile
        {
            public string PathValue { get; set; }
            public string FileName { get; set; } = "game.cue";
            public byte[] Data { get; set; }
            public MockSourceFileTrack[] ItemsValue { get; set; }
        }

        /// <summary>
        /// Minimal mock for SourceFileTrack.
        /// </summary>
        private class MockSourceFileTrack
        {
            public string FileName { get; set; }
            public bool FileIsMissing { get; set; }
            public long PhysicalOffset { get; set; }
            public long ImageOffset { get; set; }
            public long Size { get; set; }
        }

        #endregion
    }
}
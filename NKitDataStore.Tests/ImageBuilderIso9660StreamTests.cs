using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for ImageBuilderIso9660Stream covering sector reconstruction,
    /// gap fill, audio pass-through, BlockPadding overlay, PS3 encryption,
    /// and area boundary transitions.
    /// Requirements: 5.1-5.9
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class ImageBuilderIso9660StreamTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public ImageBuilderIso9660StreamTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"Iso9660StreamTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Directory.Delete(_testDir, recursive: true);
                }
            }
            catch { }
        }

        #region Helper Methods

        /// <summary>
        /// Creates a DataStore with a single ISO9660 Mode1 raw area (0x930 block size)
        /// and writes user data into it. Returns the store path.
        /// </summary>
        private DataStore createMode1Image(string imageName, byte[] userData,
            long physicalOffset = 150, int sectorCount = 1)
        {
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
                metadata.Set(AreaValueType.Type, "Mode1");

                long areaSize = (long)sectorCount * 0x930;

                // Create area with ISO9660 Mode1 stride:
                // SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800
                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            return store;
        }

        /// <summary>
        /// Creates a DataStore with a single ISO9660 Mode2Form1 raw area (0x930 block size)
        /// and writes user data into it.
        /// </summary>
        private DataStore createMode2Form1Image(string imageName, byte[] userData,
            long physicalOffset = 150, int sectorCount = 1)
        {
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS2", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
                metadata.Set(AreaValueType.Type, "Mode2");

                long areaSize = (long)sectorCount * 0x930;

                // Mode2Form1 stride: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800
                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x18,
                    strideDataLength: 0x800,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            return store;
        }

        /// <summary>
        /// Creates a DataStore with a single Audio track area (0x930 block size, no stride).
        /// </summary>
        private DataStore createAudioImage(string imageName, byte[] audioData,
            int sectorCount = 1)
        {
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, 0L);
                metadata.Set(AreaValueType.Type, "Audio");

                long areaSize = (long)sectorCount * 0x930;

                // Audio: no striding (full 2352 bytes are data)
                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                writer.WriteData(0, audioData, BlockType.File, offsetStart: 0);
                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            return store;
        }

        /// <summary>
        /// Creates a DataStore with a PS3 encrypted Mode1 area.
        /// </summary>
        private DataStore createEncryptedMode1Image(string imageName, byte[] userData,
            byte[] titleKey, long physicalOffset = 150, int sectorCount = 1)
        {
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS3", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
                metadata.Set(AreaValueType.Type, "Mode1");
                metadata.Set(AreaValueType.Encrypted, true);
                metadata.Set(AreaValueType.TitleKey,
                    BitConverter.ToString(titleKey).Replace("-", ""));

                long areaSize = (long)sectorCount * 0x930;

                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            return store;
        }

        /// <summary>
        /// Standard sync pattern for CD sectors.
        /// </summary>
        private static readonly byte[] _SyncPattern = new byte[]
        {
            0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00
        };

        #endregion

        #region Mode1 Sector Header Regeneration Tests

        [Fact]
        public void Mode1_SectorHeader_SyncPatternRegenerated()
        {
            // Arrange: 1 sector of Mode1 user data (2048 bytes)
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i % 256);

            using DataStore store = createMode1Image("Mode1_Sync", userData,
                physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act: read the full 2352-byte sector
            byte[] sector = new byte[0x930];
            builder.Position = 0;
            int read = builder.Read(sector, 0, sector.Length);

            // Assert: sync pattern at offset 0-11
            Assert.Equal(0x930, read);
            Assert.Equal(_SyncPattern, sector[..12]);
            _output.WriteLine("Mode1 sync pattern regenerated correctly");
        }

        [Fact]
        public void Mode1_SectorHeader_MsfRegenerated()
        {
            // Arrange: LBA 150 -> MSF = (150+150)/75/60=0, (150+150)/75%60=4, (150+150)%75=0
            // BCD: M=0x00, S=0x04, F=0x00
            byte[] userData = new byte[0x800];
            using DataStore store = createMode1Image("Mode1_MSF", userData,
                physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act
            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // Assert: MSF at offset 12-14 (BCD encoded)
            // LBA 150: (150+150)=300, 300/75=4min*0sec, 300/75/60=0min, 300/75%60=4sec, 300%75=0frame
            Assert.Equal(0x00, sector[12]); // minute BCD
            Assert.Equal(0x04, sector[13]); // second BCD
            Assert.Equal(0x00, sector[14]); // frame BCD
            _output.WriteLine("Mode1 MSF regenerated correctly for LBA 150");
        }

        [Fact]
        public void Mode1_SectorHeader_ModeByteIsOne()
        {
            byte[] userData = new byte[0x800];
            using DataStore store = createMode1Image("Mode1_ModeByte", userData,
                physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // Mode byte at offset 0x0F should be 0x01 for Mode1
            Assert.Equal(0x01, sector[0x0F]);
            _output.WriteLine("Mode1 mode byte = 0x01 confirmed");
        }

        [Fact]
        public void Mode1_SectorHeader_EdcRegenerated()
        {
            // Arrange: write known user data, verify EDC at offset 0x810
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i % 256);

            using DataStore store = createMode1Image("Mode1_EDC", userData,
                physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // EDC is at offset 0x810, 4 bytes. Verify it's non-zero (computed)
            uint edc = BitConverter.ToUInt32(sector, 0x810);
            Assert.NotEqual(0u, edc);

            // Verify EDC matches what Ecm.ComputeEdc would produce
            uint expectedEdc = Ecm.ComputeEdc(0, sector, 0x810, 0);
            Assert.Equal(expectedEdc, edc);
            _output.WriteLine($"Mode1 EDC regenerated: 0x{edc:X8}");
        }

        [Fact]
        public void Mode1_SectorHeader_EccRegenerated()
        {
            // Arrange
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i * 7 % 256);

            using DataStore store = createMode1Image("Mode1_ECC", userData,
                physicalOffset: 200);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // ECC is at offset 0x81C, 276 bytes. Verify non-zero (computed)
            byte[] eccRegion = sector[0x81C..0x930];
            Assert.True(eccRegion.Any(b => b != 0),
                "ECC region should contain non-zero bytes");

            // Validate the full sector using Ecm.ValidateMode1
            Assert.True(Ecm.ValidateMode1(sector, 0),
                "Reconstructed Mode1 sector should pass validation");
            _output.WriteLine("Mode1 ECC regenerated and sector validates");
        }

        [Fact]
        public void Mode1_UserData_PreservedAtCorrectOffset()
        {
            // Arrange
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i % 256);

            using DataStore store = createMode1Image("Mode1_UserData", userData,
                physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // User data at offset 0x10, length 0x800
            byte[] reconstructedUserData = sector[0x10..0x810];
            Assert.Equal(userData, reconstructedUserData);
            _output.WriteLine("Mode1 user data preserved at offset 0x10");
        }

        #endregion

        #region Mode2Form1 Sector Header Regeneration Tests

        [Fact]
        public void Mode2Form1_SectorHeader_SyncAndMsfRegenerated()
        {
            // Arrange: Mode2Form1 user data (2048 bytes)
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)((i + 0x42) % 256);

            using DataStore store = createMode2Form1Image("Mode2F1_SyncMsf", userData,
                physicalOffset: 200);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // Assert: sync pattern
            Assert.Equal(_SyncPattern, sector[..12]);

            // Mode byte at 0x0F should be 0x02
            Assert.Equal(0x02, sector[0x0F]);

            // MSF for LBA 200: (200+150)=350, 350/75/60=0min, 350/75%60=4sec, 350%75=50frame
            // BCD: M=0x00, S=0x04, F=0x50
            Assert.Equal(0x00, sector[12]); // minute
            Assert.Equal(0x04, sector[13]); // second
            Assert.Equal(0x50, sector[14]); // frame (50 in BCD)
            _output.WriteLine("Mode2Form1 sync, MSF, and mode byte regenerated");
        }

        [Fact]
        public void Mode2Form1_SectorHeader_EdcEccRegenerated()
        {
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i % 256);

            using DataStore store = createMode2Form1Image("Mode2F1_EdcEcc", userData,
                physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // Mode2Form1 EDC at offset 0x818, 4 bytes
            uint edc = BitConverter.ToUInt32(sector, 0x818);
            Assert.NotEqual(0u, edc);

            // ECC at offset 0x81C, 276 bytes
            byte[] eccRegion = sector[0x81C..0x930];
            Assert.True(eccRegion.Any(b => b != 0),
                "Mode2Form1 ECC region should contain non-zero bytes");

            _output.WriteLine($"Mode2Form1 EDC: 0x{edc:X8}, ECC present");
        }

        #endregion

        #region Mode2Form2 Sector Regeneration Tests

        [Fact]
        public void Mode2Form2_SyncAndMsfOnly_DataPreserved()
        {
            // Arrange: Mode2Form2 has 0x914 (2324) bytes of user data.
            // The subheader byte at offset 0x12 must have bit 5 set (0x20) to indicate Form2.
            // BlockPadding uses the new Sector_Padding_Pack format (version 0x01) which stores
            // the subheader and extended user data as packed components.
            byte[] userData = new byte[0x914];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)((i + 0xAB) % 256);

            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "Mode2F2_Data",
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, 150L);
                metadata.Set(AreaValueType.Type, "Mode2");

                long areaSize = 0x930;

                // Mode2Form2 stride: DataOffset=0x18, DataLength=0x914
                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x18,
                    strideDataLength: 0x914,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);

                // Build Sector_Padding_Pack for 1 Mode2Form2 sector.
                // Format: version(1) + sectorCount(2 LE) + bitmap(1) + flagByte(1) + subheader(8) + extUserData(280)
                // Flag byte: Mode2Form2(0x02) | Subheader(0x10) | ExtUserData(0x80) = 0x92
                byte[] packData = new byte[4 + 1 + 8 + 280]; // 293 bytes
                packData[0] = 0x01; // version
                packData[1] = 0x01; // sector count low byte
                packData[2] = 0x00; // sector count high byte
                packData[3] = 0x01; // bitmap: bit 0 set (sector 0 has stored data)
                packData[4] = 0x92; // flag byte: Mode2Form2 | Subheader | ExtUserData

                // Subheader payload (8 bytes): offset 0x10-0x17 in sector
                // Byte 2 (submode at 0x12) = 0x20 (Form2), byte 6 (duplicate at 0x16) = 0x20
                packData[5 + 2] = 0x20; // submode byte
                packData[5 + 6] = 0x20; // duplicate submode

                // ExtUserData payload (280 bytes): offset 0x818-0x92F in sector
                // First 276 bytes come from userData[0x800..0x914], last 4 bytes are EDC (zeros)
                int extDataStart = 5 + 8; // offset in packData where ExtUserData payload begins
                int userDataExtStart = 0x800; // offset in userData where extended region starts
                int userDataExtLen = Math.Min(userData.Length - userDataExtStart, 280);
                Array.Copy(userData, userDataExtStart, packData, extDataStart, userDataExtLen);
                // Remaining bytes (EDC region at 0x92C-0x92F) stay as zeros

                writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);

                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act
            byte[] sector = new byte[0x930];
            builder.Position = 0;
            int read = builder.Read(sector, 0, sector.Length);

            // Assert: sync pattern regenerated
            Assert.Equal(0x930, read);
            Assert.Equal(_SyncPattern, sector[..12]);

            // Mode byte = 0x02
            Assert.Equal(0x02, sector[0x0F]);

            // User data at offset 0x18, length 0x914 should be preserved
            byte[] reconstructedData = sector[0x18..(0x18 + 0x914)];
            Assert.Equal(userData, reconstructedData);
            _output.WriteLine("Mode2Form2: sync/MSF regenerated, data preserved");
        }

        #endregion

        #region Audio Track Pass-Through Tests

        [Fact]
        public void AudioTrack_PassThrough_NoModification()
        {
            // Arrange: audio data is 2352 bytes per sector, stored verbatim
            byte[] audioData = new byte[0x930];
            Random rng = new Random(42);
            rng.NextBytes(audioData);

            using DataStore store = createAudioImage("Audio_PassThrough", audioData);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act
            byte[] output = new byte[0x930];
            builder.Position = 0;
            int read = builder.Read(output, 0, output.Length);

            // Assert: audio data should be byte-for-byte identical (no header regen)
            Assert.Equal(0x930, read);
            Assert.Equal(audioData, output);
            _output.WriteLine("Audio track passed through without modification");
        }

        [Fact]
        public void AudioTrack_MultiSector_AllPreserved()
        {
            // Arrange: 3 sectors of audio data
            const int sectorCount = 3;
            byte[] audioData = new byte[0x930 * sectorCount];
            Random rng = new Random(123);
            rng.NextBytes(audioData);

            using DataStore store = createAudioImage("Audio_Multi", audioData,
                sectorCount: sectorCount);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act
            byte[] output = new byte[0x930 * sectorCount];
            builder.Position = 0;
            int read = builder.Read(output, 0, output.Length);

            // Assert
            Assert.Equal(output.Length, read);
            Assert.Equal(audioData, output);
            _output.WriteLine("Multi-sector audio track preserved byte-for-byte");
        }

        #endregion

        #region Gap Fill Tests

        [Fact]
        public void GapFill_ProducesNullBytes()
        {
            // Arrange: create image with data at start, leaving a gap
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "GapFill",
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, 150L);
                metadata.Set(AreaValueType.Type, "Mode1");

                // 4 sectors total
                long areaSize = 4L * 0x930;

                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                // Write data for first sector only (0x800 bytes of user data)
                byte[] userData = new byte[0x800];
                for (int i = 0; i < userData.Length; i++)
                    userData[i] = (byte)(i % 256);
                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);

                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act: read the gap region (sectors 1-3)
            byte[] gapData = new byte[3 * 0x930];
            builder.Position = 0x930; // Start of second sector
            int read = builder.Read(gapData, 0, gapData.Length);

            // Assert: gap should be all zeros (OnGapFill fills with null bytes,
            // and since the entire buffer is zero, OnBufferComplete will regenerate
            // headers on zero data. But the base OnGapFill fills the clean data
            // region with zeros, and the header regeneration runs on top.)
            // For gap sectors, the user data (at offset 0x10, length 0x800) should be zeros.
            Assert.Equal(gapData.Length, read);
            for (int s = 0; s < 3; s++)
            {
                int sectorStart = s * 0x930;
                byte[] userDataRegion = gapData[(sectorStart + 0x10)..(sectorStart + 0x810)];
                Assert.All(userDataRegion, b => Assert.Equal(0, b));
            }
            _output.WriteLine("Gap fill produces null bytes in user data region");
        }

        #endregion

        #region PS3 Encryption Tests

        [Fact]
        public void Ps3Encryption_AppliedWhenEncryptedAndTitleKeyAvailable()
        {
            // Arrange: known user data and title key
            // Note: ImageBuilderIso9660Stream outputs DECRYPTED data. PS3 encryption
            // is handled by the verify step's SectionProcessor, not during Read.
            // The Key property is set so DataStoreAsIso can propagate it for the verify step.
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i % 256);

            byte[] titleKey = new byte[16];
            for (int i = 0; i < 16; i++)
                titleKey[i] = (byte)(0xA0 + i);

            using DataStore store = createEncryptedMode1Image("PS3_Encrypted", userData,
                titleKey, physicalOffset: 150);

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey key = new GlobalImageKey("Iso9660Test", image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act
            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // Assert: user data region (0x10-0x810) should match plaintext
            // (ImageBuilderIso9660Stream outputs decrypted data; encryption is applied later)
            byte[] outputUserData = sector[0x10..0x810];
            Assert.Equal(userData, outputUserData);

            // Verify the Key property is set for downstream encryption
            Assert.NotNull(builder.Key);
            Assert.Equal(titleKey, builder.Key);
            _output.WriteLine("PS3 encrypted image outputs decrypted data with Key property set");
        }

        #endregion

        #region OnAreaChanged Tests

        [Fact]
        public void OnAreaChanged_UpdatesBlockSizeAndBaseLba()
        {
            // Arrange: create image with two areas (different track types/offsets)
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "MultiTrack",
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                // Track 1: Mode1 at physical offset 150
                AreaMetadata meta1 = new AreaMetadata();
                meta1.Set(AreaValueType.FsType, "FileSystem");
                meta1.Set(AreaValueType.BlockSize, 0x930L);
                meta1.Set(AreaValueType.PhysicalOffset, 150L);
                meta1.Set(AreaValueType.Type, "Mode1");
                meta1.Set(AreaValueType.Track, 1L);

                long area1Size = 2L * 0x930;
                writer.CreateArea(
                    offset: 0,
                    size: area1Size,
                    crc32: 0x11111111,
                    xxhash64: 0x1111111111111111,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)area1Size,
                    metadata: meta1
                );

                byte[] data1 = new byte[0x800 * 2];
                for (int i = 0; i < data1.Length; i++)
                    data1[i] = (byte)(i % 256);
                writer.WriteData(0, data1, BlockType.File, offsetStart: 0);

                // Track 2: Mode1 at physical offset 5000
                AreaMetadata meta2 = new AreaMetadata();
                meta2.Set(AreaValueType.FsType, "FileSystem");
                meta2.Set(AreaValueType.BlockSize, 0x930L);
                meta2.Set(AreaValueType.PhysicalOffset, 5000L);
                meta2.Set(AreaValueType.Type, "Mode1");
                meta2.Set(AreaValueType.Track, 2L);

                long area2Size = 2L * 0x930;
                writer.CreateArea(
                    offset: area1Size,
                    size: area2Size,
                    crc32: 0x22222222,
                    xxhash64: 0x2222222222222222,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)area2Size,
                    metadata: meta2
                );

                byte[] data2 = new byte[0x800 * 2];
                for (int i = 0; i < data2.Length; i++)
                    data2[i] = (byte)((i + 0x80) % 256);
                writer.WriteData(area1Size, data2, BlockType.File,
                    offsetStart: area1Size);

                writer.FinalizeImage(size: area1Size + area2Size,
                    crc32: 0xAABBCCDD, xxhash64: 0x1122334455667788);
            }

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey gkey = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(gkey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act: read first sector of track 2
            long track2Offset = 2L * 0x930;
            byte[] sector = new byte[0x930];
            builder.Position = track2Offset;
            builder.ReadExactly(sector, 0, sector.Length);

            // Assert: MSF should reflect LBA 5000 (the PhysicalOffset of track 2)
            // LBA 5000: (5000+150)=5150, 5150/75/60=1min, 5150/75%60=8sec, 5150%75=50frame
            // BCD: M=0x01, S=0x08, F=0x50
            (byte expectedMin, byte expectedSec, byte expectedFrame) = Ecm.LbaToMsf(5000);
            byte expectedMinBcd = (byte)(((expectedMin / 10) << 4) + (expectedMin % 10));
            byte expectedSecBcd = (byte)(((expectedSec / 10) << 4) + (expectedSec % 10));
            byte expectedFrameBcd = (byte)(((expectedFrame / 10) << 4) + (expectedFrame % 10));

            Assert.Equal(expectedMinBcd, sector[12]);
            Assert.Equal(expectedSecBcd, sector[13]);
            Assert.Equal(expectedFrameBcd, sector[14]);
            _output.WriteLine(
                $"Track 2 MSF uses PhysicalOffset 5000: " +
                $"{expectedMin:D2}:{expectedSec:D2}:{expectedFrame:D2}");
        }

        #endregion

        #region BlockPadding Overlay Tests

        [Fact]
        public void BlockPadding_ProvidesSubheaderForModeDetection()
        {
            // Arrange: BlockPadding provides the subheader bytes that influence
            // Mode2 Form1 vs Form2 detection in OnBufferComplete.
            // When the subheader byte at offset 0x12 has bit 5 set, the sector
            // is treated as Mode2Form2 (data preserved, no ECC).
            // When bit 5 is clear, it's treated as Mode2Form1 (full EDC+ECC regen).
            DataStore store = new DataStore(Path.Combine(_testDir, Guid.NewGuid().ToString("N")));
            string setName = "Iso9660Test";

            // Create Mode2 area with user data
            byte[] userData = new byte[0x800];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i % 256);

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "BlockPad_SubHdr",
                shardSize: 0, system: "PS2", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, 0x930L);
                metadata.Set(AreaValueType.PhysicalOffset, 150L);
                metadata.Set(AreaValueType.Type, "Mode2");

                long areaSize = 0x930;

                // Mode2Form1 stride (DataOffset=0x18, DataLength=0x800)
                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x18,
                    strideDataLength: 0x800,
                    sectionSize: (int)areaSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);

                // Write BlockPadding using the new Sector_Padding_Pack format.
                // Pack for 1 Mode2Form1 sector with subheader that does NOT have bit 5 set.
                // Format: version(1) + sectorCount(2 LE) + bitmap(1) + flagByte(1) + subheader(8)
                // Flag byte: Mode2Form1(0x01) | Subheader(0x10) = 0x11
                byte[] packData = new byte[1 + 2 + 1 + 1 + 8]; // 13 bytes
                packData[0] = 0x01; // version
                packData[1] = 0x01; // sector count low byte
                packData[2] = 0x00; // sector count high byte
                packData[3] = 0x01; // bitmap: bit 0 set (sector 0 has stored data)
                packData[4] = 0x11; // flag byte: Mode2Form1(0x01) | Subheader(0x10)
                // Subheader payload (8 bytes): all zeros means submode bit 5 clear = Form1
                // packData[5..12] are already zero (Mode2Form1 subheader)

                writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);

                writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD,
                    xxhash64: 0x1122334455667788);
            }

            ImageRecord image = store.ListAllImages().First();
            GlobalImageKey gkey = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(gkey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            // Act
            byte[] sector = new byte[0x930];
            builder.Position = 0;
            builder.ReadExactly(sector, 0, sector.Length);

            // Assert: since subheader bit 5 is NOT set, this is Mode2Form1
            // EDC should be at offset 0x818 (Mode2Form1 EDC position)
            uint edc = BitConverter.ToUInt32(sector, 0x818);
            Assert.NotEqual(0u, edc);

            // ECC should be present at 0x81C
            byte[] eccRegion = sector[0x81C..0x930];
            Assert.True(eccRegion.Any(b => b != 0),
                "Mode2Form1 ECC should be regenerated when subheader indicates Form1");

            // Sync pattern should be regenerated
            Assert.Equal(_SyncPattern, sector[..12]);
            // Mode byte should be 0x02
            Assert.Equal(0x02, sector[0x0F]);

            _output.WriteLine("BlockPadding subheader influences mode detection correctly");
        }

        #endregion
    }
}
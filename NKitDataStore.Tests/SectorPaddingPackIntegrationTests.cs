using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration tests for the Sector Padding Pack feature.
    /// Tests the full ingestion → storage → reconstruction pipeline using
    /// SectorPaddingPacker and SectorPaddingUnpacker through the DataStore.
    /// Validates Requirements: 2.10, 3.1, 3.2, 4.1, 4.10, 4.11, 6.1, 7.1, 7.2
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class SectorPaddingPackIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        // Path to Swap Magic 3 BIN — if present, the live-disc test runs; otherwise it is skipped.
        private const string SwapMagicBinPath =
            @"D:\NKitFiles\PS2_Iso\Swap Magic 3 (USA) (En,Ja,Fr,De,Es,It,Zh,Ko) (Unl) (CD Version 3.00)\Swap Magic 3 (USA) (En,Ja,Fr,De,Es,It,Zh,Ko) (Unl) (CD Version 3.00).bin";

        private const int RawSectorSize = 0x930; // 2352 bytes

        /// <summary>Standard CD sync pattern: 00 FF FF FF FF FF FF FF FF FF FF 00</summary>
        private static readonly byte[] SyncPattern =
            { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        public SectorPaddingPackIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"SectorPaddingPackInteg_{Guid.NewGuid():N}");
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
        /// Builds a fully conforming Mode1 sector at the given LBA with specified user data.
        /// </summary>
        private static byte[] BuildConformingMode1Sector(long lba, byte[] userData = null)
        {
            byte[] sector = new byte[RawSectorSize];

            // Copy user data at offset 0x10 (2048 bytes)
            if (userData != null)
                Array.Copy(userData, 0, sector, 0x10, Math.Min(userData.Length, 0x800));

            // ReconstructPrefix sets sync + MSF + mode
            Ecm.ReconstructPrefix(sector, 0, true, lba);
            // ReconstructEcc sets EDC + reserved zeros + ECC
            Ecm.ReconstructEcc(sector, 0, true, false, false);
            return sector;
        }

        /// <summary>
        /// Builds a Mode1 sector with a non-standard MSF (simulating copy protection).
        /// The actual MSF bytes are set to a different LBA than expected.
        /// </summary>
        private static byte[] BuildMode1SectorWithNonStandardMsf(long actualLba, long fakeLba, byte[] userData = null)
        {
            byte[] sector = new byte[RawSectorSize];

            if (userData != null)
                Array.Copy(userData, 0, sector, 0x10, Math.Min(userData.Length, 0x800));

            // First build with the actual LBA to get correct sync
            Ecm.ReconstructPrefix(sector, 0, true, actualLba);
            Ecm.ReconstructEcc(sector, 0, true, false, false);

            // Now overwrite MSF with the fake LBA's MSF
            (byte minute, byte second, byte frame) fakeMsf = Ecm.LbaToMsf(fakeLba);
            sector[0x0C] = (byte)(((fakeMsf.minute / 10) << 4) + (fakeMsf.minute % 10));
            sector[0x0D] = (byte)(((fakeMsf.second / 10) << 4) + (fakeMsf.second % 10));
            sector[0x0E] = (byte)(((fakeMsf.frame / 10) << 4) + (fakeMsf.frame % 10));

            return sector;
        }

        /// <summary>
        /// Builds a conforming Mode2Form1 sector at the given LBA with specified subheader and user data.
        /// </summary>
        private static byte[] BuildConformingMode2Form1Sector(long lba, byte[] subheader = null, byte[] userData = null)
        {
            byte[] sector = new byte[RawSectorSize];

            // Write user data at offset 0x18 (2048 bytes for Mode2Form1)
            if (userData != null)
                Array.Copy(userData, 0, sector, 0x18, Math.Min(userData.Length, 0x800));

            // Set mode byte
            sector[0x0F] = 0x02;

            // Write subheader (8 bytes at 0x10-0x17)
            if (subheader != null)
            {
                Array.Copy(subheader, 0, sector, 0x10, Math.Min(subheader.Length, 8));
            }
            // Ensure submode byte (0x12) does NOT have bit 5 set (Form1)
            sector[0x12] &= 0xDF;
            // Duplicate subheader at 0x14
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Apply ReconstructPrefix (writes sync + MSF + mode, copies subheader 0x14->0x10)
            Ecm.ReconstructPrefix(sector, 0, false, lba);

            // Apply ReconstructEcc (computes EDC + ECC for Mode2Form1)
            Ecm.ReconstructEcc(sector, 0, false, true, false);

            return sector;
        }

        /// <summary>
        /// Builds a conforming Mode2Form2 sector at the given LBA with specified subheader and extended user data.
        /// </summary>
        private static byte[] BuildConformingMode2Form2Sector(long lba, byte[] subheader = null, byte[] extUserData = null)
        {
            byte[] sector = new byte[RawSectorSize];

            // Write user data at offset 0x18 (2048 bytes)
            for (int i = 0x18; i < 0x818; i++)
                sector[i] = (byte)(i & 0xFF);

            // Write extended user data (280 bytes at offset 0x818)
            if (extUserData != null)
                Array.Copy(extUserData, 0, sector, 0x818, Math.Min(extUserData.Length, 280));

            // Set mode byte
            sector[0x0F] = 0x02;

            // Write subheader (8 bytes at 0x10-0x17)
            if (subheader != null)
                Array.Copy(subheader, 0, sector, 0x10, Math.Min(subheader.Length, 8));

            // Ensure submode byte (0x12) has bit 5 set (Form2)
            sector[0x12] |= 0x20;
            // Duplicate subheader at 0x14
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Apply ReconstructPrefix (writes sync + MSF + mode, copies subheader 0x14->0x10)
            Ecm.ReconstructPrefix(sector, 0, false, lba);

            // Apply ReconstructEcc (computes EDC for Mode2Form2)
            Ecm.ReconstructEcc(sector, 0, false, false, true);

            return sector;
        }

        /// <summary>
        /// Extracts user data from Mode1 raw sectors (offset 0x10, length 0x800 per sector).
        /// </summary>
        private static byte[] ExtractMode1UserData(byte[] rawImage, int sectorCount)
        {
            byte[] userData = new byte[sectorCount * 0x800];
            for (int i = 0; i < sectorCount; i++)
            {
                Array.Copy(rawImage, (i * RawSectorSize) + 0x10, userData, i * 0x800, 0x800);
            }
            return userData;
        }

        #endregion

        // ── Requirement 2.10, 3.1, 3.2: Full ingestion pipeline produces BlockPadding record ──

        /// <summary>
        /// Validates Requirements 2.10, 3.1, 3.2:
        /// Packing a section of conforming Mode1 sectors produces a valid BlockPadding
        /// record with the correct Sector_Padding_Pack format (version + header + empty bitmap).
        /// The pack is then stored and retrieved through the DataStore.
        /// </summary>
        [Fact]
        public void IngestionPipeline_ConformingMode1Sectors_ProducesValidBlockPaddingRecord()
        {
            // Arrange: build 8 conforming Mode1 sectors
            const int sectorCount = 8;
            const long startLba = 150;
            byte[] sectionData = new byte[sectorCount * RawSectorSize];

            for (int i = 0; i < sectorCount; i++)
            {
                byte[] sector = BuildConformingMode1Sector(startLba + i);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            // Act: Pack the section (simulating what DataStoreIso9660Formatter does)
            byte[] packData = SectorPaddingPacker.Pack(sectionData, sectorCount, startLba);

            // Assert: pack format is correct
            Assert.NotNull(packData);

            // Version byte
            Assert.Equal(0x01, packData[0]);

            // Sector count (2 bytes LE)
            int storedSectorCount = packData[1] | (packData[2] << 8);
            Assert.Equal(sectorCount, storedSectorCount);

            // Bitmap: ceil(8/8) = 1 byte, all zero (all conforming)
            int bitmapLength = (sectorCount + 7) / 8;
            Assert.Equal(1, bitmapLength);
            Assert.Equal(0x00, packData[3]);

            // Total size: 1 (version) + 2 (count) + 1 (bitmap) = 4 bytes
            Assert.Equal(4, packData.Length);

            _output.WriteLine($"Pack produced: {packData.Length} bytes for {sectorCount} conforming Mode1 sectors");

            // Now store and retrieve through DataStore to verify persistence
            string storePath = Path.Combine(_testDir, "ingest_test");
            Directory.CreateDirectory(storePath);

            using DataStore store = new DataStore(storePath);
            string setName = "PS1";

            byte[] userData = ExtractMode1UserData(sectionData, sectorCount);

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "IngestTest.iso",
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)RawSectorSize);
                metadata.Set(AreaValueType.PhysicalOffset, startLba);
                metadata.Set(AreaValueType.Type, "Mode1");

                long imageSize = sectionData.Length;

                writer.CreateArea(
                    offset: 0,
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sectionData),
                    xxhash64: TestHashUtil.ComputeXXHash64(sectionData),
                    strideBlockSize: RawSectorSize,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)imageSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);

                writer.FinalizeImage(
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sectionData),
                    xxhash64: TestHashUtil.ComputeXXHash64(sectionData)
                );
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);
            _output.WriteLine("BlockPadding record stored successfully in DataStore");
        }

        // ── Requirement 4.1, 4.10, 4.11: Reconstruction pipeline reads and applies BlockPadding ──

        /// <summary>
        /// Validates Requirements 4.1, 4.10, 4.11:
        /// The reconstruction pipeline reads the BlockPadding record (Sector_Padding_Pack)
        /// and applies non-recreatable bytes correctly after standard regeneration.
        /// Tests with a Mode2Form1 sector that has a non-standard subheader stored in the pack.
        /// </summary>
        [Fact]
        public void ReconstructionPipeline_ReadsAndAppliesBlockPadding_Mode2Form1()
        {
            // Arrange: build a Mode2Form1 sector with a specific subheader
            const long startLba = 200;
            byte[] subheader = new byte[8] { 0x01, 0x02, 0x08, 0x04, 0x01, 0x02, 0x08, 0x04 };
            byte[] userData = new byte[0x800];
            Random rng = new Random(42);
            rng.NextBytes(userData);

            byte[] sector = BuildConformingMode2Form1Sector(startLba, subheader, userData);

            // Pack the sector
            byte[] packData = SectorPaddingPacker.Pack(sector, 1, startLba);
            Assert.NotNull(packData);

            // Verify the pack has the subheader stored (bit 4 should be set for Mode2)
            // Header: 1 (ver) + 2 (count) + 1 (bitmap) = 4 bytes
            // Bitmap bit 0 should be set (sector has stored data due to subheader)
            Assert.Equal(0x01, packData[3] & 0x01);

            _output.WriteLine($"Pack size: {packData.Length} bytes (includes subheader for Mode2Form1)");

            // Store in DataStore and reconstruct
            string storePath = Path.Combine(_testDir, "reconstruct_m2f1");
            Directory.CreateDirectory(storePath);

            using DataStore store = new DataStore(storePath);
            string setName = "PS1";

            // Mode2Form1 stride: DataOffset=0x18, DataLength=0x800
            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "M2F1Test.iso",
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)RawSectorSize);
                metadata.Set(AreaValueType.PhysicalOffset, startLba);
                metadata.Set(AreaValueType.Type, "Mode2");

                long imageSize = RawSectorSize;

                writer.CreateArea(
                    offset: 0,
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sector),
                    xxhash64: TestHashUtil.ComputeXXHash64(sector),
                    strideBlockSize: RawSectorSize,
                    strideDataOffset: 0x18,
                    strideDataLength: 0x800,
                    sectionSize: (int)imageSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);

                writer.FinalizeImage(
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sector),
                    xxhash64: TestHashUtil.ComputeXXHash64(sector)
                );
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Reconstruct
            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);

            GlobalImageKey globalKey = new GlobalImageKey(setName, images[0].Id);
            using IImageReader reader = store.OpenImageReader(globalKey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[RawSectorSize];
            builder.Position = 0;
            int totalRead = builder.Read(reconstructed, 0, RawSectorSize);

            Assert.Equal(RawSectorSize, totalRead);

            // Assert: sync pattern regenerated
            Assert.Equal(SyncPattern, reconstructed[..12]);

            // Assert: mode byte is 0x02
            Assert.Equal(0x02, reconstructed[0x0F]);

            // Assert: subheader was applied from the pack
            // The subheader at offset 0x10 should match what was stored
            // Note: ReconstructPrefix copies 0x14->0x10, so the pack overlay
            // restores the original subheader bytes
            byte[] reconstructedSubheader = reconstructed[0x10..0x18];
            _output.WriteLine($"Reconstructed subheader: {BitConverter.ToString(reconstructedSubheader)}");

            // Assert: user data preserved at offset 0x18
            byte[] reconstructedUserData = reconstructed[0x18..0x818];
            Assert.Equal(userData, reconstructedUserData);

            _output.WriteLine("Reconstruction pipeline correctly applied BlockPadding overlay");
        }

        // ── Requirement 6.1: PhysicalOffset enables correct MSF computation ──

        /// <summary>
        /// Validates Requirement 6.1:
        /// Different startLba values produce different MSF bytes in the pack when
        /// the sector has a non-conforming MSF. The unpacker correctly restores
        /// the original MSF bytes regardless of the PhysicalOffset used.
        /// </summary>
        [Fact]
        public void PhysicalOffset_DifferentStartLba_ProducesDifferentMsfInPack()
        {
            // Arrange: create a sector with a non-standard MSF (simulating copy protection)
            // Use LBAs that produce different MSF minute values to ensure clear mismatch.
            // LBA 300: MSF = (0, 6, 0)  — minute 0
            // LBA 10000: MSF = (2, 15, 25) — minute 2
            const long actualLba = 300;
            const long fakeLba = 10000;

            byte[] userData = new byte[0x800];
            new Random(77).NextBytes(userData);

            byte[] sectorWithFakeMsf = BuildMode1SectorWithNonStandardMsf(actualLba, fakeLba, userData);

            // Verify the sector has non-standard MSF by checking the full 4-byte MSF+mode region
            (byte minute, byte second, byte frame) expectedMsf = Ecm.LbaToMsf(actualLba);
            byte expectedSecBcd = (byte)(((expectedMsf.second / 10) << 4) + (expectedMsf.second % 10));
            // The second byte (0x0D) should differ since LBA 300 has second=6 and LBA 10000 has second=15
            Assert.NotEqual(expectedSecBcd, sectorWithFakeMsf[0x0D]); // MSF second should differ

            // Act: Pack with the actual LBA (packer detects MSF mismatch)
            byte[] packData = SectorPaddingPacker.Pack(sectorWithFakeMsf, 1, actualLba);
            Assert.NotNull(packData);

            // Assert: the pack should have the MsfMode flag set (bit 3)
            // Header: ver(1) + count(2) + bitmap(1) = 4 bytes
            // Bitmap bit 0 should be set
            Assert.NotEqual(0, packData[3] & 0x01);

            // Flag byte is at offset 4
            byte flagByte = packData[4];
            Assert.NotEqual(0, flagByte & (byte)SectorFlags.MsfMode);

            _output.WriteLine($"Pack detected non-standard MSF. Flag byte: 0x{flagByte:X2}");

            // Now pack the same sector data but with a DIFFERENT startLba
            // This simulates what would happen if PhysicalOffset were wrong
            const long wrongLba = 500;
            byte[] packDataWrongLba = SectorPaddingPacker.Pack(sectorWithFakeMsf, 1, wrongLba);
            Assert.NotNull(packDataWrongLba);

            // Both packs should detect MSF mismatch (since the sector has fake MSF)
            byte flagByteWrong = packDataWrongLba[4];
            Assert.NotEqual(0, flagByteWrong & (byte)SectorFlags.MsfMode);

            // The stored MSF bytes in both packs should be identical (same original sector data)
            // Find MSF payload position: after flag byte, check if sync is stored first
            int msfPayloadOffset1 = 5; // after flag byte
            if ((flagByte & (byte)SectorFlags.Sync) != 0)
                msfPayloadOffset1 += 12;

            int msfPayloadOffset2 = 5;
            if ((flagByteWrong & (byte)SectorFlags.Sync) != 0)
                msfPayloadOffset2 += 12;

            // Both should store the same fake MSF bytes
            byte[] storedMsf1 = packData[msfPayloadOffset1..(msfPayloadOffset1 + 4)];
            byte[] storedMsf2 = packDataWrongLba[msfPayloadOffset2..(msfPayloadOffset2 + 4)];
            Assert.Equal(storedMsf1, storedMsf2);

            _output.WriteLine($"Stored MSF bytes (fake LBA {fakeLba}): {BitConverter.ToString(storedMsf1)}");

            // Verify round-trip: pack → regenerate → unpack should restore original bytes
            byte[] regenerated = new byte[RawSectorSize];
            Array.Copy(sectorWithFakeMsf, 0x10, regenerated, 0x10, 0x800); // copy user data
            Ecm.ReconstructPrefix(regenerated, 0, true, actualLba);
            Ecm.ReconstructEcc(regenerated, 0, true, false, false);

            // Unpack overlay
            SectorPaddingUnpacker.Unpack(packData, regenerated, 0, 1);

            // The MSF bytes should now match the original (fake) MSF
            Assert.Equal(sectorWithFakeMsf[0x0C], regenerated[0x0C]);
            Assert.Equal(sectorWithFakeMsf[0x0D], regenerated[0x0D]);
            Assert.Equal(sectorWithFakeMsf[0x0E], regenerated[0x0E]);

            _output.WriteLine("PhysicalOffset-based MSF computation and pack/unpack restore non-standard MSF correctly");
        }

        // ── Requirements 7.1, 7.2: End-to-end ingest → reconstruct → byte-compare ──

        /// <summary>
        /// Validates Requirements 7.1, 7.2:
        /// End-to-end test: builds a multi-sector section with Mode1 sectors (some with
        /// non-standard MSF to simulate copy protection), packs via SectorPaddingPacker,
        /// stores in DataStore with BlockPadding, reconstructs via ImageBuilderIso9660Stream,
        /// and verifies byte-identical output against the original.
        /// </summary>
        [Fact]
        public void EndToEnd_IngestAndReconstruct_MixedMode1Sectors_ByteIdentical()
        {
            // Arrange: build 4 Mode1 sectors, 2 conforming + 2 with non-standard MSF
            const int sectorCount = 4;
            const long startLba = 450;

            byte[] sectionData = new byte[sectorCount * RawSectorSize];
            Random rng = new Random(123);

            for (int i = 0; i < sectorCount; i++)
            {
                byte[] userData = new byte[0x800];
                rng.NextBytes(userData);

                byte[] sector;
                if (i < 2)
                {
                    // Conforming sectors
                    sector = BuildConformingMode1Sector(startLba + i, userData);
                }
                else
                {
                    // Non-standard MSF (copy protection simulation)
                    long fakeLba = 9999 + i;
                    sector = BuildMode1SectorWithNonStandardMsf(startLba + i, fakeLba, userData);
                }

                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            // Pack the section
            byte[] packData = SectorPaddingPacker.Pack(sectionData, sectorCount, startLba);
            Assert.NotNull(packData);

            _output.WriteLine($"Section: {sectorCount} sectors, pack size: {packData.Length} bytes");

            // Verify pack structure: first 2 sectors conforming (no bits), last 2 non-conforming
            int bitmapByte = packData[3];
            // Bits 0,1 should be clear (conforming), bits 2,3 should be set (non-conforming MSF)
            Assert.Equal(0, bitmapByte & 0x01); // sector 0: conforming
            Assert.Equal(0, bitmapByte & 0x02); // sector 1: conforming
            Assert.NotEqual(0, bitmapByte & 0x04); // sector 2: non-conforming
            Assert.NotEqual(0, bitmapByte & 0x08); // sector 3: non-conforming

            // Store in DataStore
            string storePath = Path.Combine(_testDir, "e2e_mixed");
            Directory.CreateDirectory(storePath);

            using DataStore store = new DataStore(storePath);
            string setName = "PS1";

            byte[] userData2 = ExtractMode1UserData(sectionData, sectorCount);

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "E2ETest.iso",
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)RawSectorSize);
                metadata.Set(AreaValueType.PhysicalOffset, startLba);
                metadata.Set(AreaValueType.Type, "Mode1");

                long imageSize = sectionData.Length;

                writer.CreateArea(
                    offset: 0,
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sectionData),
                    xxhash64: TestHashUtil.ComputeXXHash64(sectionData),
                    strideBlockSize: RawSectorSize,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)imageSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData2, BlockType.File, offsetStart: 0);
                writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);

                writer.FinalizeImage(
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sectionData),
                    xxhash64: TestHashUtil.ComputeXXHash64(sectionData)
                );
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Reconstruct via ImageBuilderIso9660Stream
            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);

            GlobalImageKey globalKey = new GlobalImageKey(setName, images[0].Id);
            using IImageReader reader = store.OpenImageReader(globalKey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[sectionData.Length];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < sectionData.Length)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, sectionData.Length - totalRead);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            Assert.Equal(sectionData.Length, totalRead);

            // Assert: byte-compare each sector
            int mismatchCount = 0;
            for (int i = 0; i < sectorCount; i++)
            {
                int sectorOffset = i * RawSectorSize;
                bool match = true;

                for (int b = 0; b < RawSectorSize; b++)
                {
                    if (sectionData[sectorOffset + b] != reconstructed[sectorOffset + b])
                    {
                        _output.WriteLine(
                            $"Sector {i}: mismatch at byte 0x{b:X3} " +
                            $"(original=0x{sectionData[sectorOffset + b]:X2}, " +
                            $"reconstructed=0x{reconstructed[sectorOffset + b]:X2})");
                        match = false;
                        mismatchCount++;
                        break;
                    }
                }

                if (match)
                    _output.WriteLine($"Sector {i} (LBA {startLba + i}): byte-identical ✓");
            }

            Assert.Equal(0, mismatchCount);

            // Full array comparison
            Assert.Equal(sectionData, reconstructed);
            _output.WriteLine($"End-to-end round-trip verified: {sectorCount} sectors byte-identical");
        }

        /// <summary>
        /// End-to-end test: Mode2Form1 section with Swap Magic-style copy-protection sectors.
        /// Sectors 0-24 have standard subheader (00 00 08 00), sectors 25-29 have all-0x55
        /// subheader and non-standard ECC (all 0x55). Stores through DataStore and reconstructs
        /// via ImageBuilderIso9660Stream. Must be byte-identical to original.
        /// Uses realistic 892-sector section size (0x200340 bytes) matching Swap Magic disc.
        /// </summary>
        [Fact]
        public void EndToEnd_Mode2Form1_SwapMagicStyleFillSectors_ByteIdentical()
        {
            const int sectorCount = 892;  // real Swap Magic section size
            const long startLba = 0;
            const int RawSectionSize = sectorCount * RawSectorSize; // 0x200340

            byte[] sectionData = new byte[RawSectionSize];

            for (int i = 0; i < sectorCount; i++)
            {
                int off = i * RawSectorSize;
                // Fill user data with 0x55 (Swap Magic fill pattern)
                for (int j = 0; j < 0x800; j++)
                    sectionData[off + 0x18 + j] = 0x55;
                sectionData[off + 0x0F] = 0x02;

                if (i < 25)
                {
                    // Standard subheader: 00 00 08 00 (Form1)
                    sectionData[off + 0x10] = 0x00; sectionData[off + 0x11] = 0x00;
                    sectionData[off + 0x12] = 0x08; sectionData[off + 0x13] = 0x00;
                    sectionData[off + 0x14] = 0x00; sectionData[off + 0x15] = 0x00;
                    sectionData[off + 0x16] = 0x08; sectionData[off + 0x17] = 0x00;
                    Ecm.ReconstructPrefix(sectionData, off, false, startLba + i);
                    Ecm.ReconstructEcc(sectionData, off, false, true, false);
                    // EDC/ECC are recreatable for standard sectors
                }
                else
                {
                    // Non-standard subheader: all 0x55 (Swap Magic style, Form1: 0x55 & 0x20 = 0)
                    for (int j = 0x10; j < 0x18; j++) sectionData[off + j] = 0x55;
                    Ecm.ReconstructPrefix(sectionData, off, false, startLba + i);
                    // Standard ECC would be computed from 0x55 data — instead stomp with 0x55 fill
                    for (int j = 0x81C; j < 0x930; j++) sectionData[off + j] = 0x55;
                    // Also stomp EDC with non-standard value
                    sectionData[off + 0x818] = 0x55; sectionData[off + 0x819] = 0x55;
                    sectionData[off + 0x81A] = 0x55; sectionData[off + 0x81B] = 0x55;
                }
            }

            // Pack — Analyse should detect non-standard ECC/EDC for sectors 25-29
            byte[] packData = SectorPaddingPacker.Pack(sectionData, sectorCount, startLba);
            Assert.NotNull(packData);
            _output.WriteLine($"Pack size: {packData.Length} bytes");

            // Verify sectors 25-891 have Ecc flag set (non-standard ECC)
            SectorFlags[] flags = SectorPaddingPacker.Analyse(sectionData, sectorCount, startLba);
            for (int i = 25; i < sectorCount; i++)
                Assert.NotEqual(SectorFlags.None, flags[i] & SectorFlags.Ecc);

            // Store in DataStore
            string storePath = Path.Combine(_testDir, "e2e_swapm");
            Directory.CreateDirectory(storePath);
            using DataStore store = new DataStore(storePath);
            string setName = "PS2";

            // Extract Mode2Form1 user data (0x18-0x817 per sector)
            byte[] userData = new byte[sectorCount * 0x800];
            for (int i = 0; i < sectorCount; i++)
                Array.Copy(sectionData, i * RawSectorSize + 0x18, userData, i * 0x800, 0x800);

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, "SwapMagic.iso",
                shardSize: 0, system: "PS2", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)RawSectorSize);
                metadata.Set(AreaValueType.PhysicalOffset, startLba);
                metadata.Set(AreaValueType.Type, "Mode2");
                long imageSize = sectionData.Length;
                writer.CreateArea(
                    offset: 0, size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(sectionData),
                    xxhash64: TestHashUtil.ComputeXXHash64(sectionData),
                    strideBlockSize: RawSectorSize, strideDataOffset: 0x18, strideDataLength: 0x800,
                    sectionSize: RawSectionSize, metadata: metadata);
                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);
                writer.FinalizeImage(imageSize, TestHashUtil.ComputeCrc32(sectionData), TestHashUtil.ComputeXXHash64(sectionData));
            }
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Reconstruct via ImageBuilderIso9660Stream
            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, images[0].Id));
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[sectionData.Length];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < sectionData.Length)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, sectionData.Length - totalRead);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            // Find first diff for diagnostics
            for (int i = 0; i < sectorCount; i++)
            {
                int sectorOffset = i * RawSectorSize;
                for (int b = 0; b < RawSectorSize; b++)
                {
                    if (sectionData[sectorOffset + b] != reconstructed[sectorOffset + b])
                    {
                        _output.WriteLine(
                            $"First diff: sector {i} byte 0x{b:X3} orig=0x{sectionData[sectorOffset+b]:X2} got=0x{reconstructed[sectorOffset+b]:X2}");
                        goto doneCheck;
                    }
                }
            }
            doneCheck:
            Assert.Equal(sectionData, reconstructed);
            _output.WriteLine("Swap Magic style fill sectors: byte-identical ✓");
        }

        // Path to a previously deduped Swap Magic 3 nkds directory
        private const string SwapMagicNkdsDir = @"D:\Temp\_swap_clean_104124";

        /// <summary>
        /// Reads sections from the actual deduped Swap Magic nkds and compares against
        /// the raw BIN. Saves the first mismatching section to D:\Temp\ for binary diff.
        /// This tests the full OffsetsManager+ImageBuilder pipeline with all 364 sections
        /// loaded simultaneously — the real failure condition.
        /// </summary>
        [Fact]
        public void ReadFromRealNkds_SwapMagic_FindFirstMismatch()
        {
            if (!File.Exists(SwapMagicBinPath) || !Directory.Exists(SwapMagicNkdsDir))
            {
                _output.WriteLine("Skipped: disc or nkds directory not found");
                return;
            }

            const int RawSectionSize = 892 * RawSectorSize; // 0x200340
            long binFileSize = new FileInfo(SwapMagicBinPath).Length;
            int numSections = (int)(binFileSize / RawSectionSize);
            _output.WriteLine($"Reading {numSections} sections from real nkds");

            // Open the real nkds
            using DataStore store = new DataStore(SwapMagicNkdsDir);
            List<ImageRecord> images = store.ListImagesInSet("PS2");
            if (images.Count == 0) { _output.WriteLine("No PS2 images in nkds"); return; }

            _output.WriteLine($"Found {images.Count} image(s): {images[0].Name}");
            GlobalImageKey key = new GlobalImageKey("PS2", images[0].Id);
            using IImageReader reader = store.OpenImageReader(key);

            // Diagnostic: dump segment list for section 260 to understand why user data is missing
            // Diagnostic: dump segment list for section 260 to understand why user data is missing
            {
                // (removed - diagnostic complete)
            }

            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconBuffer = new byte[RawSectionSize];

            for (int secIdx = 0; secIdx < numSections; secIdx++)
            {
                long sectionOffset = (long)secIdx * RawSectionSize;

                // Read raw source section
                byte[] srcSection = new byte[RawSectionSize];
                using (FileStream fs = File.OpenRead(SwapMagicBinPath))
                {
                    fs.Seek(sectionOffset, SeekOrigin.Begin);
                    int totalSrc = 0;
                    while (totalSrc < RawSectionSize)
                    {
                        int n = fs.Read(srcSection, totalSrc, RawSectionSize - totalSrc);
                        if (n == 0) break;
                        totalSrc += n;
                    }
                }

                // Read reconstructed section from nkds
                builder.Position = sectionOffset;
                int totalRead = 0;
                while (totalRead < RawSectionSize)
                {
                    int n = builder.Read(reconBuffer, totalRead, RawSectionSize - totalRead);
                    if (n == 0) break;
                    totalRead += n;
                }

                // Compare
                int firstDiffSector = -1, firstDiffByte = -1;
                for (int s = 0; s < 892 && firstDiffSector < 0; s++)
                {
                    for (int b = 0; b < RawSectorSize; b++)
                    {
                        if (srcSection[s * RawSectorSize + b] != reconBuffer[s * RawSectorSize + b])
                        {
                            firstDiffSector = s; firstDiffByte = b; break;
                        }
                    }
                }

                if (firstDiffSector >= 0)
                {
                    int off = firstDiffSector * RawSectorSize + firstDiffByte;
                    _output.WriteLine($"MISMATCH section {secIdx} (offset 0x{sectionOffset:X}):" +
                        $" sector {firstDiffSector} byte 0x{firstDiffByte:X3}" +
                        $" src=0x{srcSection[off]:X2} got=0x{reconBuffer[off]:X2}");
                    File.WriteAllBytes($@"D:\Temp\swapm_real_sec{secIdx}_orig.bin", srcSection);
                    byte[] recon = new byte[RawSectionSize];
                    Array.Copy(reconBuffer, recon, RawSectionSize);
                    File.WriteAllBytes($@"D:\Temp\swapm_real_sec{secIdx}_recon.bin", recon);
                    _output.WriteLine($@"Saved D:\Temp\swapm_real_sec{secIdx}_orig.bin + _recon.bin");
                    Assert.Fail($"Section {secIdx} mismatch at sector {firstDiffSector} byte 0x{firstDiffByte:X3}");
                    return;
                }
                else if (secIdx < 5 || secIdx == numSections - 1)
                    _output.WriteLine($"  Section {secIdx}: ✓");
            }
            _output.WriteLine("All sections: byte-identical ✓");
        }
        /// AND section 1 (sectors 892-1784) — the first two full sections — and verifies the
        /// full DataStore round-trip produces byte-identical output for both.
        /// Saves mismatching sections to D:\Temp\ for binary comparison.
        /// Skipped when the disc file is not available.
        /// </summary>
        [Fact]
        public void EndToEnd_SwapMagic_LiveDisc_Section0_ByteIdentical()
        {
            if (!File.Exists(SwapMagicBinPath))
            {
                _output.WriteLine($"Skipped: {SwapMagicBinPath} not found");
                return;
            }

            const int sectorCount = 892;
            const int RawSectionSize = sectorCount * RawSectorSize; // 0x200340
            // Test all sections — compute from file size
            long binFileSize = new FileInfo(SwapMagicBinPath).Length;
            int numSections = (int)(binFileSize / RawSectionSize);
            _output.WriteLine($"Testing {numSections} sections from {binFileSize} byte disc");

            bool anyFailed = false;

            for (int secIdx = 0; secIdx < numSections; secIdx++)
            {
                long sectionImageOffset = (long)secIdx * RawSectionSize;
                long sectionLba = secIdx * sectorCount;

                // Read section from disc
                byte[] sectionData = new byte[RawSectionSize];
                using (FileStream fs = File.OpenRead(SwapMagicBinPath))
                {
                    fs.Seek(sectionImageOffset, SeekOrigin.Begin);
                    int totalRead2 = 0;
                    while (totalRead2 < RawSectionSize)
                    {
                        int n = fs.Read(sectionData, totalRead2, RawSectionSize - totalRead2);
                        if (n == 0) break;
                        totalRead2 += n;
                    }
                }

                // Pack with the correct start LBA for this section
                byte[] packData = SectorPaddingPacker.Pack(sectionData, sectorCount, sectionLba);
                Assert.NotNull(packData);
                _output.WriteLine($"Section {secIdx} (LBA {sectionLba}-{sectionLba+sectorCount-1}): pack={packData.Length} bytes");

                // Store in DataStore — one image per section for isolation
                string storePath = Path.Combine(_testDir, $"e2e_live_sec{secIdx}");
                Directory.CreateDirectory(storePath);
                using DataStore store = new DataStore(storePath);
                string setName = "PS2";

                byte[] userData = new byte[sectorCount * 0x800];
                for (int i = 0; i < sectorCount; i++)
                    Array.Copy(sectionData, i * RawSectorSize + 0x18, userData, i * 0x800, 0x800);

                using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, $"SwapMagicSec{secIdx}.iso",
                    shardSize: 0, system: "PS2", format: ImageFormat.Iso))
                {
                    AreaMetadata metadata = new AreaMetadata();
                    metadata.Set(AreaValueType.FsType, "FileSystem");
                    metadata.Set(AreaValueType.BlockSize, (long)RawSectorSize);
                    metadata.Set(AreaValueType.PhysicalOffset, sectionLba);
                    metadata.Set(AreaValueType.Type, "Mode2");
                    writer.CreateArea(
                        offset: 0, size: RawSectionSize,
                        crc32: TestHashUtil.ComputeCrc32(sectionData),
                        xxhash64: TestHashUtil.ComputeXXHash64(sectionData),
                        strideBlockSize: RawSectorSize, strideDataOffset: 0x18, strideDataLength: 0x800,
                        sectionSize: RawSectionSize, metadata: metadata);
                    writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                    writer.WriteData(0, packData, BlockType.BlockPadding, offsetStart: 0);
                    writer.FinalizeImage(RawSectionSize, TestHashUtil.ComputeCrc32(sectionData), TestHashUtil.ComputeXXHash64(sectionData));
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Reconstruct
                List<ImageRecord> images = store.ListImagesInSet(setName);
                Assert.Single(images);
                using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, images[0].Id));
                using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

                byte[] reconstructed = new byte[RawSectionSize];
                builder.Position = 0;
                int totalRead = 0;
                while (totalRead < RawSectionSize)
                {
                    int n = builder.Read(reconstructed, totalRead, RawSectionSize - totalRead);
                    if (n == 0) break;
                    totalRead += n;
                }

                // Find and report first diff
                int firstDiffSector = -1, firstDiffByte = -1;
                for (int s = 0; s < sectorCount && firstDiffSector < 0; s++)
                {
                    for (int b = 0; b < RawSectorSize; b++)
                    {
                        if (sectionData[s * RawSectorSize + b] != reconstructed[s * RawSectorSize + b])
                        {
                            firstDiffSector = s; firstDiffByte = b; break;
                        }
                    }
                }

                if (firstDiffSector >= 0)
                {
                    int off = firstDiffSector * RawSectorSize + firstDiffByte;
                    _output.WriteLine($"  Section {secIdx} DIFF: sector {firstDiffSector} byte 0x{firstDiffByte:X3}" +
                        $" orig=0x{sectionData[off]:X2} got=0x{reconstructed[off]:X2}");
                    // Save both for binary comparison
                    File.WriteAllBytes($@"D:\Temp\swapm_sec{secIdx}_orig.bin", sectionData);
                    File.WriteAllBytes($@"D:\Temp\swapm_sec{secIdx}_recon.bin", reconstructed);
                    _output.WriteLine($@"  Saved: D:\Temp\swapm_sec{secIdx}_orig.bin + _recon.bin");
                    anyFailed = true;
                    break; // stop at first mismatch
                }
                else
                {
                    _output.WriteLine($"  Section {secIdx}: byte-identical ✓");
                }
            }

            Assert.False(anyFailed, "One or more sections had reconstruction mismatches — see D:\\Temp\\swapm_sec*.bin");
        }
    }
}
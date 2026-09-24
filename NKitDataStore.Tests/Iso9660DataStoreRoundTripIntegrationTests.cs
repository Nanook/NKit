using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for ISO9660 DataStore round-trip.
    /// Property 11: DataStore Verify Round-Trip (Integration)
    /// 
    /// Creates a synthetic ISO9660 image with Mode1 raw sectors (2352 bytes each
    /// with proper sync, MSF, mode byte, user data, EDC, ECC), ingests it into a
    /// real DataStore using the ISO9660 formatter pattern (which strips headers/ECC
    /// via stride), then reconstructs it using ImageBuilderIso9660Stream (which
    /// regenerates headers/ECC) and verifies the output matches the original.
    ///
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class Iso9660DataStoreRoundTripIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public Iso9660DataStoreRoundTripIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"Iso9660RoundTrip_{Guid.NewGuid():N}");
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
        /// Builds a complete Mode1 raw sector (2352 bytes) from 2048 bytes of user data
        /// and a given LBA. Generates sync pattern, MSF, mode byte, EDC, and ECC.
        /// </summary>
        private static byte[] BuildMode1Sector(byte[] userData, long lba)
        {
            if (userData.Length != 0x800)
                throw new ArgumentException("User data must be exactly 2048 bytes", nameof(userData));

            byte[] sector = new byte[0x930]; // 2352 bytes

            // Copy user data at offset 0x10
            Array.Copy(userData, 0, sector, 0x10, 0x800);

            // Regenerate sync pattern, MSF, and mode byte using Ecm
            Ecm.ReconstructPrefix(sector, 0, mode1: true, lba);

            // Regenerate EDC and ECC
            Ecm.ReconstructEcc(sector, 0, mode1: true, mode2Form1: false, mode2Form2: false);

            return sector;
        }

        /// <summary>
        /// Builds a synthetic ISO9660 image consisting of multiple Mode1 raw sectors.
        /// Each sector has unique user data derived from its index.
        /// </summary>
        private static byte[] BuildSyntheticIso9660Image(int sectorCount, long baseLba)
        {
            byte[] image = new byte[sectorCount * 0x930];
            Random rng = new Random(42); // Deterministic seed for reproducibility

            for (int i = 0; i < sectorCount; i++)
            {
                // Generate unique user data for each sector
                byte[] userData = new byte[0x800];
                rng.NextBytes(userData);

                // Build the complete sector with headers and ECC
                byte[] sector = BuildMode1Sector(userData, baseLba + i);

                // Copy into the image buffer
                Array.Copy(sector, 0, image, i * 0x930, 0x930);
            }

            return image;
        }

        /// <summary>
        /// Extracts only the user data portions from a raw sector image.
        /// For Mode1: user data is at offset 0x10, length 0x800 within each 0x930 sector.
        /// </summary>
        private static byte[] ExtractUserData(byte[] rawImage, int sectorCount)
        {
            byte[] userData = new byte[sectorCount * 0x800];
            for (int i = 0; i < sectorCount; i++)
            {
                Array.Copy(rawImage, (i * 0x930) + 0x10, userData, i * 0x800, 0x800);
            }
            return userData;
        }

        #endregion

        /// <summary>
        /// Full round-trip integration test:
        /// 1. Create a synthetic ISO9660 image with Mode1 raw sectors
        /// 2. Ingest the user data into a DataStore (simulating what the formatter does with stride)
        /// 3. Reconstruct via ImageBuilderIso9660Stream
        /// 4. Verify per-sector the reconstructed output matches the original raw image byte-for-byte
        /// </summary>
        [Fact]
        public void Iso9660Mode1_IngestAndReconstruct_ProducesByteIdenticalOutput()
        {
            // ── Arrange: build synthetic ISO9660 image ────────────────────
            const int sectorCount = 16;
            const long baseLba = 150; // Standard CD start LBA (2 seconds)
            byte[] originalImage = BuildSyntheticIso9660Image(sectorCount, baseLba);
            long imageSize = originalImage.Length; // 16 * 2352 = 37632 bytes

            _output.WriteLine($"Built synthetic ISO9660 image: {sectorCount} sectors, {imageSize} bytes, base LBA={baseLba}");

            // Verify the original image has valid sectors
            for (int i = 0; i < sectorCount; i++)
            {
                Assert.True(Ecm.ValidateMode1(originalImage, i * 0x930),
                    $"Original sector {i} should be valid Mode1");
            }

            // ── Act: Ingest into DataStore (strip headers via stride) ─────
            // The formatter stores only the 2048-byte user data per sector,
            // using DataStride { SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800 }
            byte[] userData = ExtractUserData(originalImage, sectorCount);

            string storePath = Path.Combine(_testDir, "iso_roundtrip_store");
            Directory.CreateDirectory(storePath);

            using DataStore store = new DataStore(storePath);
            string setName = "PS1";
            string imageName = "RoundTripTest.iso";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)0x930);
                metadata.Set(AreaValueType.PhysicalOffset, baseLba);
                metadata.Set(AreaValueType.Type, "Mode1");

                // Create area with Mode1 stride configuration
                writer.CreateArea(
                    offset: 0,
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(originalImage),
                    xxhash64: TestHashUtil.ComputeXXHash64(originalImage),
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)imageSize,
                    metadata: metadata
                );

                // Write the user data (what the formatter would write after striding)
                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);

                writer.FinalizeImage(
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(originalImage),
                    xxhash64: TestHashUtil.ComputeXXHash64(originalImage)
                );
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);
            _output.WriteLine("Ingested image into DataStore with Mode1 stride");

            // ── Act: Reconstruct via ImageBuilderIso9660Stream ─────────────
            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);

            ImageRecord imageRecord = images[0];
            GlobalImageKey globalKey = new GlobalImageKey(setName, imageRecord.Id);
            using IImageReader reader = store.OpenImageReader(globalKey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[imageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < imageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, (int)(imageSize - totalRead));
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            Assert.Equal((int)imageSize, totalRead);
            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // ── Assert: Verify per-sector byte-identical output ────────────
            int mismatchCount = 0;
            for (int i = 0; i < sectorCount; i++)
            {
                int sectorOffset = i * 0x930;
                byte[] originalSector = originalImage[sectorOffset..(sectorOffset + 0x930)];
                byte[] reconstructedSector = reconstructed[sectorOffset..(sectorOffset + 0x930)];

                if (!originalSector.SequenceEqual(reconstructedSector))
                {
                    mismatchCount++;
                    // Find first differing byte for diagnostics
                    for (int b = 0; b < 0x930; b++)
                    {
                        if (originalSector[b] != reconstructedSector[b])
                        {
                            _output.WriteLine(
                                $"Sector {i}: first mismatch at byte 0x{b:X3} " +
                                $"(original=0x{originalSector[b]:X2}, reconstructed=0x{reconstructedSector[b]:X2})");
                            break;
                        }
                    }
                }
                else
                {
                    // Validate the reconstructed sector is valid Mode1
                    Assert.True(Ecm.ValidateMode1(reconstructed, sectorOffset),
                        $"Reconstructed sector {i} should pass Mode1 validation");
                }
            }

            Assert.Equal(0, mismatchCount);
            _output.WriteLine("All sectors match byte-for-byte — round-trip verified!");

            // ── Assert: Verify per-block hashes match ─────────────────────
            // Compute hashes over the full reconstructed output and compare
            // against the original image hashes (stored during ingestion)
            uint reconstructedCrc = TestHashUtil.ComputeCrc32(reconstructed);
            ulong reconstructedXxHash = TestHashUtil.ComputeXXHash64(reconstructed);
            uint originalCrc = TestHashUtil.ComputeCrc32(originalImage);
            ulong originalXxHash = TestHashUtil.ComputeXXHash64(originalImage);

            Assert.Equal(originalCrc, reconstructedCrc);
            Assert.Equal(originalXxHash, reconstructedXxHash);
            _output.WriteLine($"Hash verification passed: CRC32=0x{reconstructedCrc:X8}, XXHash64=0x{reconstructedXxHash:X16}");
        }

        /// <summary>
        /// Round-trip with multiple sectors and varying user data patterns.
        /// Verifies that sector headers (sync, MSF, mode) and EDC/ECC are
        /// correctly regenerated for each sector position.
        /// </summary>
        [Fact]
        public void Iso9660Mode1_MultipleSectors_MsfIncrements_RoundTrip()
        {
            // Use a non-standard base LBA to verify MSF computation works for various positions
            const int sectorCount = 32;
            const long baseLba = 4500; // Arbitrary LBA in the middle of a disc

            byte[] originalImage = BuildSyntheticIso9660Image(sectorCount, baseLba);
            long imageSize = originalImage.Length;

            _output.WriteLine($"Testing with {sectorCount} sectors at base LBA {baseLba}");

            // Ingest
            string storePath = Path.Combine(_testDir, "iso_msf_store");
            Directory.CreateDirectory(storePath);

            using DataStore store = new DataStore(storePath);
            string setName = "PS2";
            string imageName = "MsfTest.iso";

            byte[] userData = ExtractUserData(originalImage, sectorCount);

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS2", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)0x930);
                metadata.Set(AreaValueType.PhysicalOffset, baseLba);
                metadata.Set(AreaValueType.Type, "Mode1");

                writer.CreateArea(
                    offset: 0,
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(originalImage),
                    xxhash64: TestHashUtil.ComputeXXHash64(originalImage),
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)imageSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.FinalizeImage(
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(originalImage),
                    xxhash64: TestHashUtil.ComputeXXHash64(originalImage)
                );
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Reconstruct
            List<ImageRecord> images = store.ListImagesInSet(setName);
            GlobalImageKey globalKey = new GlobalImageKey(setName, images[0].Id);
            using IImageReader reader = store.OpenImageReader(globalKey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[imageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < imageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, (int)(imageSize - totalRead));
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            // Verify each sector
            for (int i = 0; i < sectorCount; i++)
            {
                int sectorOffset = i * 0x930;

                // Verify sync pattern
                Assert.Equal(originalImage[sectorOffset..(sectorOffset + 12)],
                    reconstructed[sectorOffset..(sectorOffset + 12)]);

                // Verify MSF bytes match (these depend on LBA = baseLba + i)
                Assert.Equal(originalImage[sectorOffset + 12], reconstructed[sectorOffset + 12]); // minute
                Assert.Equal(originalImage[sectorOffset + 13], reconstructed[sectorOffset + 13]); // second
                Assert.Equal(originalImage[sectorOffset + 14], reconstructed[sectorOffset + 14]); // frame

                // Verify mode byte
                Assert.Equal(0x01, reconstructed[sectorOffset + 0x0F]);

                // Verify user data preserved
                Assert.Equal(
                    originalImage[(sectorOffset + 0x10)..(sectorOffset + 0x810)],
                    reconstructed[(sectorOffset + 0x10)..(sectorOffset + 0x810)]);

                // Verify EDC
                Assert.Equal(
                    originalImage[(sectorOffset + 0x810)..(sectorOffset + 0x814)],
                    reconstructed[(sectorOffset + 0x810)..(sectorOffset + 0x814)]);

                // Verify ECC
                Assert.Equal(
                    originalImage[(sectorOffset + 0x81C)..(sectorOffset + 0x930)],
                    reconstructed[(sectorOffset + 0x81C)..(sectorOffset + 0x930)]);

                // Full sector validation
                Assert.True(Ecm.ValidateMode1(reconstructed, sectorOffset),
                    $"Sector {i} (LBA {baseLba + i}) should pass Mode1 validation");
            }

            // Full image byte comparison
            Assert.Equal(originalImage, reconstructed);
            _output.WriteLine($"All {sectorCount} sectors at LBA {baseLba}-{baseLba + sectorCount - 1} round-trip correctly");
        }

        /// <summary>
        /// Verifies that the round-trip works when the image spans a minute boundary
        /// in MSF addressing (e.g., LBA crosses from second 59 to minute+1 second 0).
        /// This tests the BCD encoding of MSF values at boundary conditions.
        /// </summary>
        [Fact]
        public void Iso9660Mode1_MsfMinuteBoundary_RoundTrip()
        {
            // LBA that crosses a minute boundary:
            // LBA 4349 -> MSF: (4349+150)/75/60 = 0 min, (4349+150)/75%60 = 59 sec, (4349+150)%75 = 74 frame
            // LBA 4350 -> MSF: (4350+150)/75/60 = 1 min, (4350+150)/75%60 = 0 sec, (4350+150)%75 = 0 frame
            const int sectorCount = 4;
            const long baseLba = 4349; // Crosses minute boundary at sector index 1

            byte[] originalImage = BuildSyntheticIso9660Image(sectorCount, baseLba);
            long imageSize = originalImage.Length;

            _output.WriteLine($"Testing MSF minute boundary crossing at LBA {baseLba}");

            string storePath = Path.Combine(_testDir, "iso_boundary_store");
            Directory.CreateDirectory(storePath);

            using DataStore store = new DataStore(storePath);
            string setName = "SegaCD";
            string imageName = "BoundaryTest.iso";

            byte[] userData = ExtractUserData(originalImage, sectorCount);

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "SegaCD", format: ImageFormat.Iso))
            {
                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FsType, "FileSystem");
                metadata.Set(AreaValueType.BlockSize, (long)0x930);
                metadata.Set(AreaValueType.PhysicalOffset, baseLba);
                metadata.Set(AreaValueType.Type, "Mode1");

                writer.CreateArea(
                    offset: 0,
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(originalImage),
                    xxhash64: TestHashUtil.ComputeXXHash64(originalImage),
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)imageSize,
                    metadata: metadata
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.FinalizeImage(
                    size: imageSize,
                    crc32: TestHashUtil.ComputeCrc32(originalImage),
                    xxhash64: TestHashUtil.ComputeXXHash64(originalImage)
                );
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Reconstruct
            List<ImageRecord> images = store.ListImagesInSet(setName);
            GlobalImageKey globalKey = new GlobalImageKey(setName, images[0].Id);
            using IImageReader reader = store.OpenImageReader(globalKey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[imageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < imageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, (int)(imageSize - totalRead));
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            // Verify the minute boundary crossing
            // Sector 0: LBA 4349 -> MSF 0:59:74
            (byte minute, byte second, byte frame) msf0 = Ecm.LbaToMsf(4349);
            Assert.Equal(0, msf0.minute);
            Assert.Equal(59, msf0.second);
            Assert.Equal(74, msf0.frame);

            // Sector 1: LBA 4350 -> MSF 1:00:00
            (byte minute, byte second, byte frame) msf1 = Ecm.LbaToMsf(4350);
            Assert.Equal(1, msf1.minute);
            Assert.Equal(0, msf1.second);
            Assert.Equal(0, msf1.frame);

            // Full byte comparison
            Assert.Equal(originalImage, reconstructed);
            _output.WriteLine("MSF minute boundary crossing round-trips correctly");
        }
    }
}
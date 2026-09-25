using Nanook.NKit;
using NKDS;
using NKDS.Models;
using NKitDataStore.Interfaces;
using System.Text;
using SysBuffer = System.Buffer;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for CUE verify round-trip through the NKDSApp pipeline.
    ///
    /// Exercises the full verify pipeline:
    /// 1. Create synthetic multi-track CUE image with data and audio tracks
    /// 2. Ingest into DataStore with FileName metadata
    /// 3. Verify via NkdsOperations.VerifyAsync (same path as NKDSApp verify command)
    /// 4. Confirm VerifySuccess
    ///
    /// This tests the complete routing chain:
    ///   NkdsOperations.VerifyAsync → NKitProcessor → NKitTaskContext.CreateSteps
    ///   → CalculateConfig → StepsDefs lookup → Verify-Image step → DataStore verification
    ///
    /// Feature: full-format-processing-parity
    /// **Validates: Requirements 10.2, 10.4**
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class CueVerifyRoundTripIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public CueVerifyRoundTripIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"CueVerifyRoundTrip_{Guid.NewGuid():N}");
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

        /// <summary>
        /// Builds a complete Mode1 raw sector (2352 bytes) from user data and LBA.
        /// </summary>
        private static byte[] BuildMode1Sector(byte[] userData, long lba)
        {
            if (userData.Length != 0x800)
                throw new ArgumentException("Mode1 user data must be 2048 bytes");

            byte[] sector = new byte[0x930];
            SysBuffer.BlockCopy(userData, 0, sector, 0x10, 0x800);
            Ecm.ReconstructPrefix(sector, 0, mode1: true, lba);
            Ecm.ReconstructEcc(sector, 0, mode1: true,
                mode2Form1: false, mode2Form2: false);
            return sector;
        }

        /// <summary>
        /// Generates deterministic pseudo-random data for a given seed and size.
        /// </summary>
        private static byte[] GenerateData(int seed, int size)
        {
            byte[] data = new byte[size];
            Random rng = new Random(seed);
            rng.NextBytes(data);
            return data;
        }

        /// <summary>
        /// Full round-trip integration test for CUE verify:
        /// 1. Create synthetic multi-track CUE image (Mode1 data track + Audio track)
        /// 2. Ingest into DataStore as CUE with FileName metadata
        /// 3. Verify via NkdsOperations.VerifyAsync
        /// 4. Confirm VerifySuccess (no errors)
        ///
        /// This confirms that the CalculateConfig fix and StepsDefs routing
        /// correctly resolve to the Verify-Image step for CUE folderindex sources,
        /// and that block hash comparison succeeds.
        ///
        /// **Validates: Requirements 10.2, 10.4**
        /// </summary>
        [Fact]
        public async Task CueImage_VerifyViaOperations_ReportsVerifySuccess()
        {
            // ── Arrange: build synthetic multi-track CUE image ────────────

            const int dataTrackSectors = 20; // Must be >= 16 for NKit validation
            const int audioTrackSectors = 4;
            const long dataTrackPhysicalOffset = 150; // Standard CD pregap
            const long audioTrackPhysicalOffset = 170; // After data track

            const string track1FileName = "VerifyTest (Track 01).bin";
            const string track2FileName = "VerifyTest (Track 02).bin";
            const string imageName = "VerifyTest";

            // Generate user data for Mode1 data track
            byte[] dataTrackUserData = new byte[dataTrackSectors * 0x800];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 5000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, dataTrackUserData,
                    i * 0x800, 0x800);
            }

            // Build full raw sectors for the data track
            byte[] dataTrackRawSectors = new byte[dataTrackSectors * 0x930];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(dataTrackUserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, dataTrackPhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, dataTrackRawSectors,
                    i * 0x930, 0x930);
            }

            // Generate audio track data (full raw sectors, no headers/ECC)
            byte[] audioTrackData = new byte[audioTrackSectors * 0x930];
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 6000 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, audioTrackData,
                    i * 0x930, 0x930);
            }

            long dataTrackAreaSize = (long)dataTrackSectors * 0x930;
            long audioTrackAreaSize = (long)audioTrackSectors * 0x930;
            long totalImageSize = dataTrackAreaSize + audioTrackAreaSize;

            // Compute hashes for each track
            uint dataTrackCrc = TestHashUtil.ComputeCrc32(dataTrackRawSectors);
            ulong dataTrackXxHash = TestHashUtil.ComputeXXHash64(dataTrackRawSectors);
            uint audioTrackCrc = TestHashUtil.ComputeCrc32(audioTrackData);
            ulong audioTrackXxHash = TestHashUtil.ComputeXXHash64(audioTrackData);

            // Compute full image hash (concatenation of all tracks)
            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(dataTrackRawSectors, 0, fullImage, 0,
                dataTrackRawSectors.Length);
            SysBuffer.BlockCopy(audioTrackData, 0, fullImage,
                dataTrackRawSectors.Length, audioTrackData.Length);
            uint imageCrc = TestHashUtil.ComputeCrc32(fullImage);
            ulong imageXxHash = TestHashUtil.ComputeXXHash64(fullImage);

            // Build CUE sheet content
            string cueContent = string.Join("\n", new[]
            {
                $"FILE \"{track1FileName}\" BINARY",
                "  TRACK 01 MODE1/2352",
                "    INDEX 01 00:00:00",
                $"FILE \"{track2FileName}\" BINARY",
                "  TRACK 02 AUDIO",
                "    INDEX 01 00:00:00"
            });
            byte[] cueBytes = Encoding.UTF8.GetBytes(cueContent);

            _output.WriteLine($"Synthetic CUE image: {totalImageSize} bytes");
            _output.WriteLine($"  Data track: {dataTrackSectors} sectors, " +
                $"{dataTrackAreaSize} bytes, file={track1FileName}");
            _output.WriteLine($"  Audio track: {audioTrackSectors} sectors, " +
                $"{audioTrackAreaSize} bytes, file={track2FileName}");
            _output.WriteLine($"  Image CRC32: 0x{imageCrc:X8}, XXHash64: 0x{imageXxHash:X16}");

            // ── Act: ingest into DataStore ────────────────────────────────

            string storePath = Path.Combine(_testDir, "datastore");
            Directory.CreateDirectory(storePath);
            DataStore store = new DataStore(storePath);
            string setName = "Default";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "Default", format: ImageFormat.Cue))
            {
                // Track 1: Mode1 data track with stride and FileName metadata
                AreaMetadata meta1 = new AreaMetadata();
                meta1.Set(AreaValueType.FsType, "FileSystem");
                meta1.Set(AreaValueType.BlockSize, 0x930L);
                meta1.Set(AreaValueType.PhysicalOffset, dataTrackPhysicalOffset);
                meta1.Set(AreaValueType.Type, "Mode1");
                meta1.Set(AreaValueType.Track, 1L);
                meta1.Set(AreaValueType.Session, 1L);
                meta1.Set(AreaValueType.FileName, track1FileName);

                writer.CreateArea(
                    offset: 0,
                    size: dataTrackAreaSize,
                    crc32: dataTrackCrc,
                    xxhash64: dataTrackXxHash,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)dataTrackAreaSize,
                    metadata: meta1
                );

                writer.WriteData(0, dataTrackUserData, BlockType.File, offsetStart: 0);

                // Track 2: Audio track with FileName metadata (no stride - raw audio)
                AreaMetadata meta2 = new AreaMetadata();
                meta2.Set(AreaValueType.FsType, "FileSystem");
                meta2.Set(AreaValueType.BlockSize, 0x930L);
                meta2.Set(AreaValueType.PhysicalOffset, audioTrackPhysicalOffset);
                meta2.Set(AreaValueType.Type, "Audio");
                meta2.Set(AreaValueType.Track, 2L);
                meta2.Set(AreaValueType.Session, 1L);
                meta2.Set(AreaValueType.FileName, track2FileName);

                writer.CreateArea(
                    offset: dataTrackAreaSize,
                    size: audioTrackAreaSize,
                    crc32: audioTrackCrc,
                    xxhash64: audioTrackXxHash,
                    sectionSize: (int)audioTrackAreaSize,
                    metadata: meta2
                );

                writer.WriteData(dataTrackAreaSize, audioTrackData, BlockType.File,
                    offsetStart: dataTrackAreaSize);

                // Store the CUE index file as a loose file
                writer.WriteFile($"{imageName}.cue", cueBytes);

                writer.FinalizeImage(size: totalImageSize,
                    crc32: imageCrc, xxhash64: imageXxHash);
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);
            _output.WriteLine("CUE image ingested into DataStore");

            // Get the image ID for verification
            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);
            ImageRecord imageRecord = images[0];
            Assert.Equal(ImageFormat.Cue, imageRecord.Format);
            Assert.Equal(imageName, imageRecord.Name);

            long imageId = imageRecord.Id;
            _output.WriteLine($"Image ID: {imageId}, Name: '{imageRecord.Name}', " +
                $"Format: {imageRecord.Format}");

            store.Dispose();

            // ── Act: verify via NkdsOperations.VerifyAsync ────────────────
            // This exercises the same code path as the NKDSApp verify command:
            //   NkdsOperations → NKitProcessor → NKitTaskContext routing → Verify-Image step

            bool? verifiedResult = null;
            string verifyOutput = "";

            using NkdsOperations ops = new NkdsOperations();
            OperationResult verifyResult = await ops.VerifyAsync(
                dataStorePath: storePath,
                setName: setName,
                imageIds: new[] { imageId },
                onImageVerified: (id, success) => verifiedResult = success,
                onImageOutput: (set, id, msg) =>
                {
                    verifyOutput += msg;
                    _output.WriteLine($"[Verify] {msg?.TrimEnd()}");
                });

            // ── Assert: verification succeeded ────────────────────────────

            _output.WriteLine($"Verify result: Success={verifyResult.Success}, " +
                $"ItemsFailed={verifyResult.ItemsFailed}, " +
                $"Errors=[{string.Join("; ", verifyResult.Errors.Select(e => $"{e.ItemName}: {e.Reason}"))}]");

            Assert.True(verifyResult.Success,
                $"VerifyAsync should succeed. Errors: " +
                $"{string.Join("; ", verifyResult.Errors.Select(e => $"{e.ItemName}: {e.Reason}"))}");
            Assert.Equal(0, verifyResult.ItemsFailed);
            Assert.True(verifiedResult == true,
                "onImageVerified should have been called with success=true");

            _output.WriteLine("PASS: CUE verify round-trip confirmed VerifySuccess");
        }

        /// <summary>
        /// Verifies that a single-track Mode1 CUE image also verifies successfully.
        /// This is a simpler case (no audio track) that exercises the same routing
        /// but with a single FileName-tagged area.
        ///
        /// **Validates: Requirements 10.2, 10.4**
        /// </summary>
        [Fact]
        public async Task CueImage_SingleTrack_VerifyViaOperations_ReportsVerifySuccess()
        {
            // ── Arrange: build single-track Mode1 CUE image ──────────────

            const int sectorCount = 16;
            const long physicalOffset = 150;
            const string trackFileName = "SingleTrack (Track 01).bin";
            const string imageName = "SingleTrack";

            // Generate Mode1 sectors
            byte[] userData = new byte[sectorCount * 0x800];
            for (int i = 0; i < sectorCount; i++)
            {
                byte[] sectorData = GenerateData(seed: 7000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, userData, i * 0x800, 0x800);
            }

            byte[] rawSectors = new byte[sectorCount * 0x930];
            for (int i = 0; i < sectorCount; i++)
            {
                byte[] sectorUserData = new byte[0x800];
                SysBuffer.BlockCopy(userData, i * 0x800, sectorUserData, 0, 0x800);
                byte[] sector = BuildMode1Sector(sectorUserData, physicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, rawSectors, i * 0x930, 0x930);
            }

            long areaSize = (long)sectorCount * 0x930;
            uint areaCrc = TestHashUtil.ComputeCrc32(rawSectors);
            ulong areaXxHash = TestHashUtil.ComputeXXHash64(rawSectors);

            string cueContent =
                $"FILE \"{trackFileName}\" BINARY\n" +
                "  TRACK 01 MODE1/2352\n" +
                "    INDEX 01 00:00:00\n";
            byte[] cueBytes = Encoding.UTF8.GetBytes(cueContent);

            _output.WriteLine($"Single-track CUE image: {areaSize} bytes, {sectorCount} sectors");

            // ── Act: ingest into DataStore ────────────────────────────────

            string storePath = Path.Combine(_testDir, "datastore_single");
            Directory.CreateDirectory(storePath);
            DataStore store = new DataStore(storePath);
            string setName = "Default";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "Default", format: ImageFormat.Cue))
            {
                AreaMetadata meta = new AreaMetadata();
                meta.Set(AreaValueType.FsType, "FileSystem");
                meta.Set(AreaValueType.BlockSize, 0x930L);
                meta.Set(AreaValueType.PhysicalOffset, physicalOffset);
                meta.Set(AreaValueType.Type, "Mode1");
                meta.Set(AreaValueType.Track, 1L);
                meta.Set(AreaValueType.Session, 1L);
                meta.Set(AreaValueType.FileName, trackFileName);

                writer.CreateArea(
                    offset: 0,
                    size: areaSize,
                    crc32: areaCrc,
                    xxhash64: areaXxHash,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)areaSize,
                    metadata: meta
                );

                writer.WriteData(0, userData, BlockType.File, offsetStart: 0);
                writer.WriteFile($"{imageName}.cue", cueBytes);
                writer.FinalizeImage(size: areaSize, crc32: areaCrc, xxhash64: areaXxHash);
            }

            TestDataStoreHelper.WaitForSetIdle(store, setName);

            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);
            long imageId = images[0].Id;
            _output.WriteLine($"Image ingested: ID={imageId}");

            store.Dispose();

            // ── Act: verify via NkdsOperations.VerifyAsync ────────────────

            bool? verifiedResult = null;

            using NkdsOperations ops = new NkdsOperations();
            OperationResult verifyResult = await ops.VerifyAsync(
                dataStorePath: storePath,
                setName: setName,
                imageIds: new[] { imageId },
                onImageVerified: (id, success) => verifiedResult = success,
                onImageOutput: (set, id, msg) =>
                {
                    _output.WriteLine($"[Verify] {msg?.TrimEnd()}");
                });

            // ── Assert: verification succeeded ────────────────────────────

            Assert.True(verifyResult.Success,
                $"VerifyAsync should succeed. Errors: " +
                $"{string.Join("; ", verifyResult.Errors.Select(e => $"{e.ItemName}: {e.Reason}"))}");
            Assert.Equal(0, verifyResult.ItemsFailed);
            Assert.True(verifiedResult == true,
                "onImageVerified should have been called with success=true");

            _output.WriteLine("PASS: Single-track CUE verify confirmed VerifySuccess");
        }
    }
}
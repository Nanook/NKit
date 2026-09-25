using Nanook.NKit;
using NKitDataStore.Interfaces;
using SysBuffer = System.Buffer;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for multi-track CUE image round-trip through the DataStore.
    /// Exercises the full ingestion → reconstruction pipeline for a synthetic multi-track
    /// CUE image with at least one Mode1 data track and one audio track.
    ///
    /// Feature: nkds-iso-xbox-support, Property 11: DataStore Verify Round-Trip (Integration)
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class MultiTrackCueRoundTripIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public MultiTrackCueRoundTripIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"CueRoundTrip_{Guid.NewGuid():N}");
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
        /// Standard sync pattern for CD sectors (12 bytes).
        /// </summary>
        private static readonly byte[] SyncPattern = new byte[]
        {
            0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00
        };

        /// <summary>
        /// Builds a complete Mode1 raw sector (2352 bytes) from user data and LBA.
        /// Generates sync pattern, MSF address, mode byte, EDC, and ECC.
        /// </summary>
        private static byte[] BuildMode1Sector(byte[] userData, long lba)
        {
            if (userData.Length != 0x800)
                throw new ArgumentException("Mode1 user data must be 2048 bytes");

            byte[] sector = new byte[0x930];

            // Place user data at offset 0x10
            SysBuffer.BlockCopy(userData, 0, sector, 0x10, 0x800);

            // Reconstruct prefix (sync, MSF, mode byte)
            Ecm.ReconstructPrefix(sector, 0, mode1: true, lba);

            // Reconstruct EDC and ECC
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
        /// Full round-trip integration test for a multi-track CUE image with one Mode1
        /// data track and one audio track.
        ///
        /// Steps:
        /// 1. Create synthetic multi-track image data (Mode1 data track + Audio track)
        /// 2. Ingest into DataStore via IImageWriter (simulating DataStoreIso9660Formatter)
        /// 3. Reconstruct via ImageBuilderIso9660Stream
        /// 4. Verify per-block hashes for each track area match the original ingested data
        ///
        /// **Validates: Requirements 9.3**
        /// </summary>
        [Fact]
        public void MultiTrackCue_DataAndAudioTracks_RoundTripHashesMatch()
        {
            // ── Arrange: build synthetic multi-track image ────────────────

            const int dataTrackSectors = 4;
            const int audioTrackSectors = 3;
            const long dataTrackPhysicalOffset = 150; // Standard CD pregap
            const long audioTrackPhysicalOffset = 154; // After data track

            // Generate user data for Mode1 data track (4 sectors × 2048 bytes)
            byte[] dataTrackUserData = new byte[dataTrackSectors * 0x800];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 100 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, dataTrackUserData,
                    i * 0x800, 0x800);
            }

            // Build full raw sectors for the data track (for verification)
            byte[] dataTrackRawSectors = new byte[dataTrackSectors * 0x930];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(dataTrackUserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, dataTrackPhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, dataTrackRawSectors,
                    i * 0x930, 0x930);
            }

            // Generate audio track data (3 sectors × 2352 bytes, full sector)
            byte[] audioTrackData = new byte[audioTrackSectors * 0x930];
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 200 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, audioTrackData,
                    i * 0x930, 0x930);
            }

            long dataTrackAreaSize = (long)dataTrackSectors * 0x930;
            long audioTrackAreaSize = (long)audioTrackSectors * 0x930;
            long totalImageSize = dataTrackAreaSize + audioTrackAreaSize;

            // Compute hashes for area and image finalization
            uint dataTrackCrc = TestHashUtil.ComputeCrc32(dataTrackRawSectors);
            ulong dataTrackXxHash = TestHashUtil.ComputeXXHash64(dataTrackRawSectors);
            uint audioTrackCrc = TestHashUtil.ComputeCrc32(audioTrackData);
            ulong audioTrackXxHash = TestHashUtil.ComputeXXHash64(audioTrackData);

            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(dataTrackRawSectors, 0, fullImage, 0,
                dataTrackRawSectors.Length);
            SysBuffer.BlockCopy(audioTrackData, 0, fullImage,
                dataTrackRawSectors.Length, audioTrackData.Length);
            uint imageCrc = TestHashUtil.ComputeCrc32(fullImage);
            ulong imageXxHash = TestHashUtil.ComputeXXHash64(fullImage);

            _output.WriteLine($"Data track: {dataTrackSectors} sectors, " +
                $"{dataTrackAreaSize} bytes");
            _output.WriteLine($"Audio track: {audioTrackSectors} sectors, " +
                $"{audioTrackAreaSize} bytes");
            _output.WriteLine($"Total image: {totalImageSize} bytes");

            // ── Act: ingest into DataStore ────────────────────────────────

            string storePath = Path.Combine(_testDir, Guid.NewGuid().ToString("N"));
            DataStore store = new DataStore(storePath);
            string setName = "Iso9660Test";
            string imageName = "MultiTrackCue.bin";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                // Track 1: Mode1 data track with stride
                AreaMetadata meta1 = new AreaMetadata();
                meta1.Set(AreaValueType.FsType, "FileSystem");
                meta1.Set(AreaValueType.BlockSize, 0x930L);
                meta1.Set(AreaValueType.PhysicalOffset, dataTrackPhysicalOffset);
                meta1.Set(AreaValueType.Type, "Mode1");
                meta1.Set(AreaValueType.Track, 1L);
                meta1.Set(AreaValueType.Session, 1L);

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

                // Write the clean user data (stride strips headers/ECC)
                writer.WriteData(0, dataTrackUserData, BlockType.File, offsetStart: 0);

                // Track 2: Audio track with no stride (full 2352 bytes stored)
                AreaMetadata meta2 = new AreaMetadata();
                meta2.Set(AreaValueType.FsType, "FileSystem");
                meta2.Set(AreaValueType.BlockSize, 0x930L);
                meta2.Set(AreaValueType.PhysicalOffset, audioTrackPhysicalOffset);
                meta2.Set(AreaValueType.Type, "Audio");
                meta2.Set(AreaValueType.Track, 2L);
                meta2.Set(AreaValueType.Session, 1L);

                writer.CreateArea(
                    offset: dataTrackAreaSize,
                    size: audioTrackAreaSize,
                    crc32: audioTrackCrc,
                    xxhash64: audioTrackXxHash,
                    sectionSize: (int)audioTrackAreaSize,
                    metadata: meta2
                );

                // Write audio data verbatim (no stride for audio tracks)
                writer.WriteData(dataTrackAreaSize, audioTrackData, BlockType.File,
                    offsetStart: dataTrackAreaSize);

                writer.FinalizeImage(size: totalImageSize,
                    crc32: imageCrc, xxhash64: imageXxHash);
            }

            _output.WriteLine("Image ingested into DataStore successfully");

            // ── Act: reconstruct via ImageBuilderIso9660Stream ────────────

            ImageRecord image = store.ListImagesInSet(setName).First();
            GlobalImageKey key = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[totalImageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead,
                    (int)(totalImageSize - totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            Assert.Equal((int)totalImageSize, totalRead);
            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // ── Assert: verify per-track area hashes match ───────────────

            // Verify data track (Track 1) - raw sectors with regenerated headers
            byte[] reconDataTrack = new byte[dataTrackAreaSize];
            SysBuffer.BlockCopy(reconstructed, 0, reconDataTrack, 0,
                (int)dataTrackAreaSize);

            uint reconDataCrc = TestHashUtil.ComputeCrc32(reconDataTrack);
            ulong reconDataXxHash = TestHashUtil.ComputeXXHash64(reconDataTrack);

            _output.WriteLine($"Data track - Original CRC: 0x{dataTrackCrc:X8}, " +
                $"Reconstructed CRC: 0x{reconDataCrc:X8}");
            _output.WriteLine($"Data track - Original XXH: 0x{dataTrackXxHash:X16}, " +
                $"Reconstructed XXH: 0x{reconDataXxHash:X16}");

            Assert.Equal(dataTrackCrc, reconDataCrc);
            Assert.Equal(dataTrackXxHash, reconDataXxHash);

            // Verify each sector in the data track individually
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] originalSector = dataTrackRawSectors
                    .AsSpan(i * 0x930, 0x930).ToArray();
                byte[] reconSector = reconDataTrack
                    .AsSpan(i * 0x930, 0x930).ToArray();

                // Verify sync pattern
                Assert.Equal(SyncPattern, reconSector[..12]);

                // Verify MSF address
                (byte m, byte s, byte f) = Ecm.LbaToMsf(dataTrackPhysicalOffset + i);
                byte mBcd = (byte)(((m / 10) << 4) + (m % 10));
                byte sBcd = (byte)(((s / 10) << 4) + (s % 10));
                byte fBcd = (byte)(((f / 10) << 4) + (f % 10));
                Assert.Equal(mBcd, reconSector[12]);
                Assert.Equal(sBcd, reconSector[13]);
                Assert.Equal(fBcd, reconSector[14]);

                // Verify mode byte
                Assert.Equal(0x01, reconSector[15]);

                // Verify user data at offset 0x10, length 0x800
                Assert.Equal(
                    originalSector.AsSpan(0x10, 0x800).ToArray(),
                    reconSector.AsSpan(0x10, 0x800).ToArray());

                // Verify full sector byte-for-byte (includes EDC/ECC)
                Assert.Equal(originalSector, reconSector);
            }

            _output.WriteLine("Data track: all sectors verified " +
                "(sync, MSF, mode, user data, EDC, ECC)");

            // Verify audio track (Track 2) - byte-for-byte identical
            byte[] reconAudioTrack = new byte[audioTrackAreaSize];
            SysBuffer.BlockCopy(reconstructed, (int)dataTrackAreaSize,
                reconAudioTrack, 0, (int)audioTrackAreaSize);

            uint reconAudioCrc = TestHashUtil.ComputeCrc32(reconAudioTrack);
            ulong reconAudioXxHash = TestHashUtil.ComputeXXHash64(reconAudioTrack);

            _output.WriteLine($"Audio track - Original CRC: 0x{audioTrackCrc:X8}, " +
                $"Reconstructed CRC: 0x{reconAudioCrc:X8}");
            _output.WriteLine($"Audio track - Original XXH: 0x{audioTrackXxHash:X16}, " +
                $"Reconstructed XXH: 0x{reconAudioXxHash:X16}");

            Assert.Equal(audioTrackCrc, reconAudioCrc);
            Assert.Equal(audioTrackXxHash, reconAudioXxHash);

            // Verify each audio sector byte-for-byte
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] originalSector = audioTrackData
                    .AsSpan(i * 0x930, 0x930).ToArray();
                byte[] reconSector = reconAudioTrack
                    .AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(originalSector, reconSector);
            }

            _output.WriteLine("Audio track: all sectors verified byte-for-byte");

            // Verify full image hash
            uint fullReconCrc = TestHashUtil.ComputeCrc32(reconstructed);
            ulong fullReconXxHash = TestHashUtil.ComputeXXHash64(reconstructed);

            Assert.Equal(imageCrc, fullReconCrc);
            Assert.Equal(imageXxHash, fullReconXxHash);

            _output.WriteLine("PASS: Multi-track CUE round-trip verified - " +
                "all hashes match");
        }

        /// <summary>
        /// Round-trip test with three tracks: Mode1 data, Audio, and a second Mode1
        /// data track at a different physical offset. Verifies that area boundary
        /// transitions work correctly across multiple track types and that each
        /// track area's hashes match after reconstruction.
        ///
        /// **Validates: Requirements 9.3**
        /// </summary>
        [Fact]
        public void MultiTrackCue_ThreeTracks_DataAudioData_RoundTripHashesMatch()
        {
            // ── Arrange: build synthetic 3-track image ────────────────────

            const int track1Sectors = 3; // Mode1 data
            const int track2Sectors = 2; // Audio
            const int track3Sectors = 3; // Mode1 data
            const long track1PhysicalOffset = 150;
            const long track2PhysicalOffset = 153; // After track 1
            const long track3PhysicalOffset = 200; // Different session/offset

            // Track 1: Mode1 data
            byte[] track1UserData = new byte[track1Sectors * 0x800];
            for (int i = 0; i < track1Sectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 300 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, track1UserData,
                    i * 0x800, 0x800);
            }

            byte[] track1RawSectors = new byte[track1Sectors * 0x930];
            for (int i = 0; i < track1Sectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(track1UserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, track1PhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, track1RawSectors,
                    i * 0x930, 0x930);
            }

            // Track 2: Audio
            byte[] track2AudioData = new byte[track2Sectors * 0x930];
            for (int i = 0; i < track2Sectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 400 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, track2AudioData,
                    i * 0x930, 0x930);
            }

            // Track 3: Mode1 data (different physical offset)
            byte[] track3UserData = new byte[track3Sectors * 0x800];
            for (int i = 0; i < track3Sectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 500 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, track3UserData,
                    i * 0x800, 0x800);
            }

            byte[] track3RawSectors = new byte[track3Sectors * 0x930];
            for (int i = 0; i < track3Sectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(track3UserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, track3PhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, track3RawSectors,
                    i * 0x930, 0x930);
            }

            long track1AreaSize = (long)track1Sectors * 0x930;
            long track2AreaSize = (long)track2Sectors * 0x930;
            long track3AreaSize = (long)track3Sectors * 0x930;
            long totalImageSize = track1AreaSize + track2AreaSize + track3AreaSize;

            // Compute hashes
            uint track1Crc = TestHashUtil.ComputeCrc32(track1RawSectors);
            ulong track1XxHash = TestHashUtil.ComputeXXHash64(track1RawSectors);
            uint track2Crc = TestHashUtil.ComputeCrc32(track2AudioData);
            ulong track2XxHash = TestHashUtil.ComputeXXHash64(track2AudioData);
            uint track3Crc = TestHashUtil.ComputeCrc32(track3RawSectors);
            ulong track3XxHash = TestHashUtil.ComputeXXHash64(track3RawSectors);

            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(track1RawSectors, 0, fullImage, 0,
                track1RawSectors.Length);
            SysBuffer.BlockCopy(track2AudioData, 0, fullImage,
                (int)track1AreaSize, track2AudioData.Length);
            SysBuffer.BlockCopy(track3RawSectors, 0, fullImage,
                (int)(track1AreaSize + track2AreaSize), track3RawSectors.Length);
            uint imageCrc = TestHashUtil.ComputeCrc32(fullImage);
            ulong imageXxHash = TestHashUtil.ComputeXXHash64(fullImage);

            // ── Act: ingest into DataStore ────────────────────────────────

            string storePath = Path.Combine(_testDir, Guid.NewGuid().ToString("N"));
            DataStore store = new DataStore(storePath);
            string setName = "Iso9660Test";
            string imageName = "ThreeTrackCue.bin";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS1", format: ImageFormat.Iso))
            {
                // Track 1: Mode1 data
                AreaMetadata meta1 = new AreaMetadata();
                meta1.Set(AreaValueType.FsType, "FileSystem");
                meta1.Set(AreaValueType.BlockSize, 0x930L);
                meta1.Set(AreaValueType.PhysicalOffset, track1PhysicalOffset);
                meta1.Set(AreaValueType.Type, "Mode1");
                meta1.Set(AreaValueType.Track, 1L);
                meta1.Set(AreaValueType.Session, 1L);

                writer.CreateArea(
                    offset: 0,
                    size: track1AreaSize,
                    crc32: track1Crc,
                    xxhash64: track1XxHash,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)track1AreaSize,
                    metadata: meta1
                );
                writer.WriteData(0, track1UserData, BlockType.File, offsetStart: 0);

                // Track 2: Audio
                long track2Offset = track1AreaSize;
                AreaMetadata meta2 = new AreaMetadata();
                meta2.Set(AreaValueType.FsType, "FileSystem");
                meta2.Set(AreaValueType.BlockSize, 0x930L);
                meta2.Set(AreaValueType.PhysicalOffset, track2PhysicalOffset);
                meta2.Set(AreaValueType.Type, "Audio");
                meta2.Set(AreaValueType.Track, 2L);
                meta2.Set(AreaValueType.Session, 1L);

                writer.CreateArea(
                    offset: track2Offset,
                    size: track2AreaSize,
                    crc32: track2Crc,
                    xxhash64: track2XxHash,
                    sectionSize: (int)track2AreaSize,
                    metadata: meta2
                );
                writer.WriteData(track2Offset, track2AudioData, BlockType.File,
                    offsetStart: track2Offset);

                // Track 3: Mode1 data (different physical offset)
                long track3Offset = track1AreaSize + track2AreaSize;
                AreaMetadata meta3 = new AreaMetadata();
                meta3.Set(AreaValueType.FsType, "FileSystem");
                meta3.Set(AreaValueType.BlockSize, 0x930L);
                meta3.Set(AreaValueType.PhysicalOffset, track3PhysicalOffset);
                meta3.Set(AreaValueType.Type, "Mode1");
                meta3.Set(AreaValueType.Track, 3L);
                meta3.Set(AreaValueType.Session, 2L);

                writer.CreateArea(
                    offset: track3Offset,
                    size: track3AreaSize,
                    crc32: track3Crc,
                    xxhash64: track3XxHash,
                    strideBlockSize: 0x930,
                    strideDataOffset: 0x10,
                    strideDataLength: 0x800,
                    sectionSize: (int)track3AreaSize,
                    metadata: meta3
                );
                writer.WriteData(track3Offset, track3UserData, BlockType.File,
                    offsetStart: track3Offset);

                writer.FinalizeImage(size: totalImageSize,
                    crc32: imageCrc, xxhash64: imageXxHash);
            }

            _output.WriteLine("3-track image ingested into DataStore");

            // ── Act: reconstruct via ImageBuilderIso9660Stream ────────────

            ImageRecord image = store.ListImagesInSet(setName).First();
            GlobalImageKey gkey = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(gkey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[totalImageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead,
                    (int)(totalImageSize - totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            Assert.Equal((int)totalImageSize, totalRead);

            // ── Assert: verify per-track area hashes ─────────────────────

            // Track 1: Mode1 data - verify hash
            byte[] reconTrack1 = reconstructed.AsSpan(0, (int)track1AreaSize).ToArray();
            Assert.Equal(track1Crc, TestHashUtil.ComputeCrc32(reconTrack1));
            Assert.Equal(track1XxHash, TestHashUtil.ComputeXXHash64(reconTrack1));
            _output.WriteLine("Track 1 (Mode1 data): hashes match");

            // Verify Track 1 sector details
            for (int i = 0; i < track1Sectors; i++)
            {
                byte[] reconSector = reconTrack1.AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(SyncPattern, reconSector[..12]);
                Assert.Equal(0x01, reconSector[15]); // Mode1

                (byte m, byte s, byte f) = Ecm.LbaToMsf(track1PhysicalOffset + i);
                Assert.Equal((byte)(((m / 10) << 4) + (m % 10)), reconSector[12]);
                Assert.Equal((byte)(((s / 10) << 4) + (s % 10)), reconSector[13]);
                Assert.Equal((byte)(((f / 10) << 4) + (f % 10)), reconSector[14]);
            }

            // Track 2: Audio - verify byte-for-byte
            byte[] reconTrack2 = reconstructed
                .AsSpan((int)track1AreaSize, (int)track2AreaSize).ToArray();
            Assert.Equal(track2Crc, TestHashUtil.ComputeCrc32(reconTrack2));
            Assert.Equal(track2XxHash, TestHashUtil.ComputeXXHash64(reconTrack2));
            Assert.Equal(track2AudioData, reconTrack2);
            _output.WriteLine("Track 2 (Audio): hashes match, byte-for-byte");

            // Track 3: Mode1 data - verify hash and MSF uses different offset
            byte[] reconTrack3 = reconstructed
                .AsSpan((int)(track1AreaSize + track2AreaSize), (int)track3AreaSize)
                .ToArray();
            Assert.Equal(track3Crc, TestHashUtil.ComputeCrc32(reconTrack3));
            Assert.Equal(track3XxHash, TestHashUtil.ComputeXXHash64(reconTrack3));
            _output.WriteLine("Track 3 (Mode1 data): hashes match");

            // Verify Track 3 uses its own PhysicalOffset for MSF
            for (int i = 0; i < track3Sectors; i++)
            {
                byte[] reconSector = reconTrack3.AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(SyncPattern, reconSector[..12]);
                Assert.Equal(0x01, reconSector[15]); // Mode1

                (byte m, byte s, byte f) = Ecm.LbaToMsf(track3PhysicalOffset + i);
                Assert.Equal((byte)(((m / 10) << 4) + (m % 10)), reconSector[12]);
                Assert.Equal((byte)(((s / 10) << 4) + (s % 10)), reconSector[13]);
                Assert.Equal((byte)(((f / 10) << 4) + (f % 10)), reconSector[14]);
            }

            // Verify full image hash
            uint fullReconCrc = TestHashUtil.ComputeCrc32(reconstructed);
            ulong fullReconXxHash = TestHashUtil.ComputeXXHash64(reconstructed);
            Assert.Equal(imageCrc, fullReconCrc);
            Assert.Equal(imageXxHash, fullReconXxHash);

            _output.WriteLine("PASS: 3-track CUE round-trip verified - " +
                "all track area hashes match");
        }
    }
}
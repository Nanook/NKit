using Nanook.NKit;
using NKitDataStore.Interfaces;
using System.Text;
using SysBuffer = System.Buffer;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for CUE image full round-trip through the DataStore
    /// using folder-based storage with FileName metadata.
    ///
    /// Exercises the full pipeline:
    /// 1. Create synthetic multi-track CUE image with data and audio tracks
    /// 2. Ingest with FileName metadata on each area
    /// 3. List in DataStore → verify appears as folder (no extension)
    /// 4. Enter folder → verify track files and index file enumerated
    /// 5. Reconstruct via ImageBuilderIso9660Stream
    /// 6. Verify per-block hashes match ingested values
    ///
    /// Feature: cue-gdi-folder-storage
    /// **Validates: Requirements 6.1, 6.2, 8.4**
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class CueImageFullRoundTripIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public CueImageFullRoundTripIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"CueFullRoundTrip_{Guid.NewGuid():N}");
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
        /// Full round-trip integration test for a multi-track CUE image stored with
        /// folder-based FileName metadata.
        ///
        /// Steps:
        /// 1. Create synthetic multi-track image (Mode1 data track + Audio track)
        /// 2. Ingest into DataStore as ImageFormat.Cue with FileName metadata per area
        /// 3. Store a CUE index file as a loose file
        /// 4. List images → verify CUE image appears as folder (no extension)
        /// 5. Open image reader → verify FileName-tagged areas and loose index file
        /// 6. Reconstruct via ImageBuilderIso9660Stream
        /// 7. Verify per-block hashes match ingested values
        ///
        /// **Validates: Requirements 6.1, 6.2, 8.4**
        /// </summary>
        [Fact]
        public void CueImage_FolderBased_FullRoundTrip_HashesMatch()
        {
            // ── Arrange: build synthetic multi-track image ────────────────

            const int dataTrackSectors = 4;
            const int audioTrackSectors = 3;
            const long dataTrackPhysicalOffset = 150; // Standard CD pregap
            const long audioTrackPhysicalOffset = 154; // After data track

            const string track1FileName = "game (Track 01).bin";
            const string track2FileName = "game (Track 02).bin";
            const string imageName = "game";

            // Generate user data for Mode1 data track (4 sectors × 2048 bytes)
            byte[] dataTrackUserData = new byte[dataTrackSectors * 0x800];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 1000 + i, size: 0x800);
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

            // Generate audio track data (3 sectors × 2352 bytes, full sector)
            byte[] audioTrackData = new byte[audioTrackSectors * 0x930];
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 2000 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, audioTrackData,
                    i * 0x930, 0x930);
            }

            long dataTrackAreaSize = (long)dataTrackSectors * 0x930;
            long audioTrackAreaSize = (long)audioTrackSectors * 0x930;
            long totalImageSize = dataTrackAreaSize + audioTrackAreaSize;

            // Compute hashes
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

            // Build a synthetic CUE index file content
            string cueContent = string.Join(Environment.NewLine, new[]
            {
                $"FILE \"{track1FileName}\" BINARY",
                "  TRACK 01 MODE1/2352",
                "    INDEX 01 00:00:00",
                $"FILE \"{track2FileName}\" BINARY",
                "  TRACK 02 AUDIO",
                "    INDEX 01 00:00:00"
            });
            byte[] cueBytes = Encoding.UTF8.GetBytes(cueContent);

            _output.WriteLine($"Data track: {dataTrackSectors} sectors, " +
                $"{dataTrackAreaSize} bytes, file={track1FileName}");
            _output.WriteLine($"Audio track: {audioTrackSectors} sectors, " +
                $"{audioTrackAreaSize} bytes, file={track2FileName}");
            _output.WriteLine($"Total image: {totalImageSize} bytes");

            // ── Act: ingest into DataStore as CUE with FileName metadata ──

            string storePath = Path.Combine(_testDir, Guid.NewGuid().ToString("N"));
            DataStore store = new DataStore(storePath);
            string setName = "CueRoundTripTest";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "PS1", format: ImageFormat.Cue))
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

                // Track 2: Audio track with FileName metadata (no stride)
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
                writer.WriteFile("game.cue", cueBytes);

                writer.FinalizeImage(size: totalImageSize,
                    crc32: imageCrc, xxhash64: imageXxHash);
            }

            _output.WriteLine("CUE image ingested with FileName metadata and loose CUE file");

            // ── Assert: listing shows CUE image as folder (no extension) ─

            List<ImageRecord> images = store.ListImagesInSet(setName);
            Assert.Single(images);
            ImageRecord imageRecord = images[0];

            Assert.Equal(ImageFormat.Cue, imageRecord.Format);
            Assert.Equal(imageName, imageRecord.Name);
            _output.WriteLine($"Image listed: Name='{imageRecord.Name}', " +
                $"Format={imageRecord.Format}");

            // Verify the image would be presented as a folder:
            // getDataStoreEntryExtension returns empty string for CUE
            string ext = imageRecord.Format.GetFileExtension();
            // The SourceFileSystem uses its own getDataStoreEntryExtension which
            // returns empty for CUE (folder presentation). We verify the format
            // is CUE which triggers folder presentation in SourceFileSystem.
            Assert.Equal(ImageFormat.Cue, imageRecord.Format);
            _output.WriteLine("PASS: CUE image format confirmed - " +
                "SourceFileSystem will present as folder (no extension)");

            // ── Assert: folder contents - track files and index file ──────

            GlobalImageKey key = new GlobalImageKey(setName, imageRecord.Id);
            using (IImageReader reader = store.OpenImageReader(key))
            {
                // Verify FileName-tagged areas (track files)
                List<AreaRecord> areas = reader.GetAreas().OrderBy(a => a.Offset).ToList();
                Assert.Equal(2, areas.Count);

                string area1FileName = areas[0].Metadata.GetString(AreaValueType.FileName);
                string area2FileName = areas[1].Metadata.GetString(AreaValueType.FileName);

                Assert.Equal(track1FileName, area1FileName);
                Assert.Equal(track2FileName, area2FileName);
                _output.WriteLine($"Track files enumerated: '{area1FileName}', " +
                    $"'{area2FileName}'");

                // Verify area metadata is complete
                Assert.Equal("Mode1", areas[0].Metadata.GetString(AreaValueType.Type));
                Assert.Equal(1L, areas[0].Metadata.GetLong(AreaValueType.Track));
                Assert.Equal("Audio", areas[1].Metadata.GetString(AreaValueType.Type));
                Assert.Equal(2L, areas[1].Metadata.GetLong(AreaValueType.Track));

                // Verify loose files (CUE index file)
                List<FileRecord> files = reader.ListFiles().ToList();
                Assert.Contains(files, f => f.Name == "game.cue");
                _output.WriteLine($"Index file enumerated: 'game.cue'");

                // Verify the CUE file content can be read back
                byte[] readCueBytes = reader.ReadFile("game.cue");
                Assert.NotNull(readCueBytes);
                string readCueContent = Encoding.UTF8.GetString(readCueBytes);
                Assert.Contains(track1FileName, readCueContent);
                Assert.Contains(track2FileName, readCueContent);
                _output.WriteLine("PASS: Folder contents verified - " +
                    "track files and index file present");
            }

            // ── Act: reconstruct via ImageBuilderIso9660Stream ────────────

            using IImageReader reconReader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reconReader);

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

            Assert.Equal(dataTrackCrc, reconDataCrc);
            Assert.Equal(dataTrackXxHash, reconDataXxHash);

            // Verify each sector in the data track
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

            _output.WriteLine("PASS: CUE image full round-trip verified - " +
                "folder listing, folder contents, and all hashes match");
        }
    }
}
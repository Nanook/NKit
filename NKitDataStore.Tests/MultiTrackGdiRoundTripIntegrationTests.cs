using Nanook.NKit;
using NKitDataStore.Interfaces;
using SysBuffer = System.Buffer;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for multi-track GDI image round-trip through the DataStore.
    /// Exercises the full ingestion and reconstruction pipeline for a synthetic multi-track
    /// GDI image (Dreamcast format) with data and audio tracks.
    ///
    /// Feature: cue-gdi-folder-storage, Task 13.4: GDI Image Full Round-Trip
    /// **Validates: Requirements 6.3, 6.4, 8.5**
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class MultiTrackGdiRoundTripIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public MultiTrackGdiRoundTripIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(), $"GdiRoundTrip_{Guid.NewGuid():N}");
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

        private static readonly byte[] SyncPattern = new byte[]
        {
            0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00
        };

        private static byte[] BuildMode1Sector(byte[] userData, long lba)
        {
            if (userData.Length != 0x800)
                throw new ArgumentException("Mode1 user data must be 2048 bytes");
            byte[] sector = new byte[0x930];
            SysBuffer.BlockCopy(userData, 0, sector, 0x10, 0x800);
            Ecm.ReconstructPrefix(sector, 0, mode1: true, lba);
            Ecm.ReconstructEcc(sector, 0, mode1: true, mode2Form1: false, mode2Form2: false);
            return sector;
        }

        private static byte[] GenerateData(int seed, int size)
        {
            byte[] data = new byte[size];
            Random rng = new Random(seed);
            rng.NextBytes(data);
            return data;
        }

        /// <summary>
        /// Full round-trip for a 2-track GDI image (Mode1 data + Audio).
        /// **Validates: Requirements 6.3, 6.4, 8.5**
        /// </summary>
        [Fact]
        public void MultiTrackGdi_DataAndAudioTracks_RoundTripHashesMatch()
        {
            const int dataTrackSectors = 4;
            const int audioTrackSectors = 3;
            const long dataTrackPhysicalOffset = 0;
            const long audioTrackPhysicalOffset = 450;

            byte[] dataTrackUserData = new byte[dataTrackSectors * 0x800];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 1000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, dataTrackUserData, i * 0x800, 0x800);
            }

            byte[] dataTrackRawSectors = new byte[dataTrackSectors * 0x930];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(dataTrackUserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, dataTrackPhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, dataTrackRawSectors, i * 0x930, 0x930);
            }

            byte[] audioTrackData = new byte[audioTrackSectors * 0x930];
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 2000 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, audioTrackData, i * 0x930, 0x930);
            }

            long dataTrackAreaSize = (long)dataTrackSectors * 0x930;
            long audioTrackAreaSize = (long)audioTrackSectors * 0x930;
            long totalImageSize = dataTrackAreaSize + audioTrackAreaSize;

            uint dataTrackCrc = TestHashUtil.ComputeCrc32(dataTrackRawSectors);
            ulong dataTrackXxHash = TestHashUtil.ComputeXXHash64(dataTrackRawSectors);
            uint audioTrackCrc = TestHashUtil.ComputeCrc32(audioTrackData);
            ulong audioTrackXxHash = TestHashUtil.ComputeXXHash64(audioTrackData);

            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(dataTrackRawSectors, 0, fullImage, 0, dataTrackRawSectors.Length);
            SysBuffer.BlockCopy(audioTrackData, 0, fullImage, dataTrackRawSectors.Length, audioTrackData.Length);
            uint imageCrc = TestHashUtil.ComputeCrc32(fullImage);
            ulong imageXxHash = TestHashUtil.ComputeXXHash64(fullImage);

            _output.WriteLine($"Data track: {dataTrackSectors} sectors, {dataTrackAreaSize} bytes");
            _output.WriteLine($"Audio track: {audioTrackSectors} sectors, {audioTrackAreaSize} bytes");
            _output.WriteLine($"Total image: {totalImageSize} bytes");

            // Ingest into DataStore
            string storePath = Path.Combine(_testDir, Guid.NewGuid().ToString("N"));
            DataStore store = new DataStore(storePath);
            string setName = "Iso9660Test";
            string imageName = "DreamcastGame.bin";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "Dreamcast", format: ImageFormat.Gdi))
            {
                AreaMetadata meta1 = new AreaMetadata();
                meta1.Set(AreaValueType.FsType, "FileSystem");
                meta1.Set(AreaValueType.BlockSize, 0x930L);
                meta1.Set(AreaValueType.PhysicalOffset, dataTrackPhysicalOffset);
                meta1.Set(AreaValueType.Type, "Mode1");
                meta1.Set(AreaValueType.Track, 1L);
                meta1.Set(AreaValueType.Session, 1L);
                writer.CreateArea(offset: 0, size: dataTrackAreaSize, crc32: dataTrackCrc,
                    xxhash64: dataTrackXxHash, strideBlockSize: 0x930, strideDataOffset: 0x10,
                    strideDataLength: 0x800, sectionSize: (int)dataTrackAreaSize, metadata: meta1);
                writer.WriteData(0, dataTrackUserData, BlockType.File, offsetStart: 0);

                AreaMetadata meta2 = new AreaMetadata();
                meta2.Set(AreaValueType.FsType, "FileSystem");
                meta2.Set(AreaValueType.BlockSize, 0x930L);
                meta2.Set(AreaValueType.PhysicalOffset, audioTrackPhysicalOffset);
                meta2.Set(AreaValueType.Type, "Audio");
                meta2.Set(AreaValueType.Track, 2L);
                meta2.Set(AreaValueType.Session, 1L);
                writer.CreateArea(offset: dataTrackAreaSize, size: audioTrackAreaSize,
                    crc32: audioTrackCrc, xxhash64: audioTrackXxHash,
                    sectionSize: (int)audioTrackAreaSize, metadata: meta2);
                writer.WriteData(dataTrackAreaSize, audioTrackData, BlockType.File,
                    offsetStart: dataTrackAreaSize);

                writer.FinalizeImage(size: totalImageSize, crc32: imageCrc, xxhash64: imageXxHash);
            }
            _output.WriteLine("GDI image ingested into DataStore successfully");

            // Reconstruct via ImageBuilderIso9660Stream
            ImageRecord image = store.ListImagesInSet(setName).First();
            GlobalImageKey key = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[totalImageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, (int)(totalImageSize - totalRead));
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }
            Assert.Equal((int)totalImageSize, totalRead);
            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // Verify data track (Track 1)
            byte[] reconDataTrack = new byte[dataTrackAreaSize];
            SysBuffer.BlockCopy(reconstructed, 0, reconDataTrack, 0, (int)dataTrackAreaSize);
            Assert.Equal(dataTrackCrc, TestHashUtil.ComputeCrc32(reconDataTrack));
            Assert.Equal(dataTrackXxHash, TestHashUtil.ComputeXXHash64(reconDataTrack));
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] originalSector = dataTrackRawSectors.AsSpan(i * 0x930, 0x930).ToArray();
                byte[] reconSector = reconDataTrack.AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(SyncPattern, reconSector[..12]);
                (byte m, byte s, byte f) = Ecm.LbaToMsf(dataTrackPhysicalOffset + i);
                Assert.Equal((byte)(((m / 10) << 4) + (m % 10)), reconSector[12]);
                Assert.Equal((byte)(((s / 10) << 4) + (s % 10)), reconSector[13]);
                Assert.Equal((byte)(((f / 10) << 4) + (f % 10)), reconSector[14]);
                Assert.Equal(0x01, reconSector[15]);
                Assert.Equal(originalSector, reconSector);
            }
            _output.WriteLine("Data track: all sectors verified");

            // Verify audio track (Track 2) - byte-for-byte identical
            byte[] reconAudioTrack = new byte[audioTrackAreaSize];
            SysBuffer.BlockCopy(reconstructed, (int)dataTrackAreaSize, reconAudioTrack, 0, (int)audioTrackAreaSize);
            Assert.Equal(audioTrackCrc, TestHashUtil.ComputeCrc32(reconAudioTrack));
            Assert.Equal(audioTrackXxHash, TestHashUtil.ComputeXXHash64(reconAudioTrack));
            for (int i = 0; i < audioTrackSectors; i++)
            {
                Assert.Equal(
                    audioTrackData.AsSpan(i * 0x930, 0x930).ToArray(),
                    reconAudioTrack.AsSpan(i * 0x930, 0x930).ToArray());
            }
            _output.WriteLine("Audio track: all sectors verified byte-for-byte");

            // Verify full image hash
            Assert.Equal(imageCrc, TestHashUtil.ComputeCrc32(reconstructed));
            Assert.Equal(imageXxHash, TestHashUtil.ComputeXXHash64(reconstructed));
            _output.WriteLine("PASS: Multi-track GDI round-trip verified");
        }

        /// <summary>
        /// Round-trip test with three tracks typical of a Dreamcast GDI layout:
        ///   Track 1: Low-density Mode1 data (LBA 0)
        ///   Track 2: Audio track (LBA 450)
        ///   Track 3: High-density Mode1 data (LBA 45000)
        /// **Validates: Requirements 6.3, 6.4, 8.5**
        /// </summary>
        [Fact]
        public void MultiTrackGdi_ThreeTracks_DataAudioData_RoundTripHashesMatch()
        {
            const int track1Sectors = 3;
            const int track2Sectors = 2;
            const int track3Sectors = 4;
            const long track1PhysicalOffset = 0;
            const long track2PhysicalOffset = 450;
            const long track3PhysicalOffset = 45000;

            // Track 1: Low-density Mode1 data
            byte[] track1UserData = new byte[track1Sectors * 0x800];
            for (int i = 0; i < track1Sectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 3000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, track1UserData, i * 0x800, 0x800);
            }
            byte[] track1RawSectors = new byte[track1Sectors * 0x930];
            for (int i = 0; i < track1Sectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(track1UserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, track1PhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, track1RawSectors, i * 0x930, 0x930);
            }

            // Track 2: Audio
            byte[] track2AudioData = new byte[track2Sectors * 0x930];
            for (int i = 0; i < track2Sectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 4000 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, track2AudioData, i * 0x930, 0x930);
            }

            // Track 3: High-density Mode1 data
            byte[] track3UserData = new byte[track3Sectors * 0x800];
            for (int i = 0; i < track3Sectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 5000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, track3UserData, i * 0x800, 0x800);
            }
            byte[] track3RawSectors = new byte[track3Sectors * 0x930];
            for (int i = 0; i < track3Sectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(track3UserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, track3PhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, track3RawSectors, i * 0x930, 0x930);
            }

            long track1AreaSize = (long)track1Sectors * 0x930;
            long track2AreaSize = (long)track2Sectors * 0x930;
            long track3AreaSize = (long)track3Sectors * 0x930;
            long totalImageSize = track1AreaSize + track2AreaSize + track3AreaSize;

            uint track1Crc = TestHashUtil.ComputeCrc32(track1RawSectors);
            ulong track1XxHash = TestHashUtil.ComputeXXHash64(track1RawSectors);
            uint track2Crc = TestHashUtil.ComputeCrc32(track2AudioData);
            ulong track2XxHash = TestHashUtil.ComputeXXHash64(track2AudioData);
            uint track3Crc = TestHashUtil.ComputeCrc32(track3RawSectors);
            ulong track3XxHash = TestHashUtil.ComputeXXHash64(track3RawSectors);

            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(track1RawSectors, 0, fullImage, 0, track1RawSectors.Length);
            SysBuffer.BlockCopy(track2AudioData, 0, fullImage, (int)track1AreaSize, track2AudioData.Length);
            SysBuffer.BlockCopy(track3RawSectors, 0, fullImage, (int)(track1AreaSize + track2AreaSize), track3RawSectors.Length);
            uint imageCrc = TestHashUtil.ComputeCrc32(fullImage);
            ulong imageXxHash = TestHashUtil.ComputeXXHash64(fullImage);

            // Ingest into DataStore
            string storePath = Path.Combine(_testDir, Guid.NewGuid().ToString("N"));
            DataStore store = new DataStore(storePath);
            string setName = "Iso9660Test";
            string imageName = "SonicAdventure.bin";

            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName,
                shardSize: 0, system: "Dreamcast", format: ImageFormat.Gdi))
            {
                AreaMetadata meta1 = new AreaMetadata();
                meta1.Set(AreaValueType.FsType, "FileSystem");
                meta1.Set(AreaValueType.BlockSize, 0x930L);
                meta1.Set(AreaValueType.PhysicalOffset, track1PhysicalOffset);
                meta1.Set(AreaValueType.Type, "Mode1");
                meta1.Set(AreaValueType.Track, 1L);
                meta1.Set(AreaValueType.Session, 1L);
                writer.CreateArea(offset: 0, size: track1AreaSize, crc32: track1Crc,
                    xxhash64: track1XxHash, strideBlockSize: 0x930, strideDataOffset: 0x10,
                    strideDataLength: 0x800, sectionSize: (int)track1AreaSize, metadata: meta1);
                writer.WriteData(0, track1UserData, BlockType.File, offsetStart: 0);

                long track2Offset = track1AreaSize;
                AreaMetadata meta2 = new AreaMetadata();
                meta2.Set(AreaValueType.FsType, "FileSystem");
                meta2.Set(AreaValueType.BlockSize, 0x930L);
                meta2.Set(AreaValueType.PhysicalOffset, track2PhysicalOffset);
                meta2.Set(AreaValueType.Type, "Audio");
                meta2.Set(AreaValueType.Track, 2L);
                meta2.Set(AreaValueType.Session, 1L);
                writer.CreateArea(offset: track2Offset, size: track2AreaSize,
                    crc32: track2Crc, xxhash64: track2XxHash,
                    sectionSize: (int)track2AreaSize, metadata: meta2);
                writer.WriteData(track2Offset, track2AudioData, BlockType.File,
                    offsetStart: track2Offset);

                long track3Offset = track1AreaSize + track2AreaSize;
                AreaMetadata meta3 = new AreaMetadata();
                meta3.Set(AreaValueType.FsType, "FileSystem");
                meta3.Set(AreaValueType.BlockSize, 0x930L);
                meta3.Set(AreaValueType.PhysicalOffset, track3PhysicalOffset);
                meta3.Set(AreaValueType.Type, "Mode1");
                meta3.Set(AreaValueType.Track, 3L);
                meta3.Set(AreaValueType.Session, 2L);
                writer.CreateArea(offset: track3Offset, size: track3AreaSize,
                    crc32: track3Crc, xxhash64: track3XxHash, strideBlockSize: 0x930,
                    strideDataOffset: 0x10, strideDataLength: 0x800,
                    sectionSize: (int)track3AreaSize, metadata: meta3);
                writer.WriteData(track3Offset, track3UserData, BlockType.File,
                    offsetStart: track3Offset);

                writer.FinalizeImage(size: totalImageSize, crc32: imageCrc, xxhash64: imageXxHash);
            }
            _output.WriteLine("3-track GDI image ingested into DataStore");

            // Reconstruct via ImageBuilderIso9660Stream
            ImageRecord image = store.ListImagesInSet(setName).First();
            GlobalImageKey gkey = new GlobalImageKey(setName, image.Id);
            using IImageReader reader = store.OpenImageReader(gkey);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[totalImageSize];
            builder.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = builder.Read(reconstructed, totalRead, (int)(totalImageSize - totalRead));
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }
            Assert.Equal((int)totalImageSize, totalRead);

            // Verify Track 1: Low-density Mode1 data
            byte[] reconTrack1 = reconstructed.AsSpan(0, (int)track1AreaSize).ToArray();
            Assert.Equal(track1Crc, TestHashUtil.ComputeCrc32(reconTrack1));
            Assert.Equal(track1XxHash, TestHashUtil.ComputeXXHash64(reconTrack1));
            for (int i = 0; i < track1Sectors; i++)
            {
                byte[] reconSector = reconTrack1.AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(SyncPattern, reconSector[..12]);
                Assert.Equal(0x01, reconSector[15]);
                (byte m, byte s, byte f) = Ecm.LbaToMsf(track1PhysicalOffset + i);
                Assert.Equal((byte)(((m / 10) << 4) + (m % 10)), reconSector[12]);
                Assert.Equal((byte)(((s / 10) << 4) + (s % 10)), reconSector[13]);
                Assert.Equal((byte)(((f / 10) << 4) + (f % 10)), reconSector[14]);
            }
            _output.WriteLine("Track 1 (Low-density Mode1): hashes match");

            // Verify Track 2: Audio - byte-for-byte
            byte[] reconTrack2 = reconstructed.AsSpan((int)track1AreaSize, (int)track2AreaSize).ToArray();
            Assert.Equal(track2Crc, TestHashUtil.ComputeCrc32(reconTrack2));
            Assert.Equal(track2XxHash, TestHashUtil.ComputeXXHash64(reconTrack2));
            Assert.Equal(track2AudioData, reconTrack2);
            _output.WriteLine("Track 2 (Audio): byte-for-byte match");

            // Verify Track 3: High-density Mode1 data with PhysicalOffset 45000
            byte[] reconTrack3 = reconstructed
                .AsSpan((int)(track1AreaSize + track2AreaSize), (int)track3AreaSize).ToArray();
            Assert.Equal(track3Crc, TestHashUtil.ComputeCrc32(reconTrack3));
            Assert.Equal(track3XxHash, TestHashUtil.ComputeXXHash64(reconTrack3));
            for (int i = 0; i < track3Sectors; i++)
            {
                byte[] reconSector = reconTrack3.AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(SyncPattern, reconSector[..12]);
                Assert.Equal(0x01, reconSector[15]);
                (byte m, byte s, byte f) = Ecm.LbaToMsf(track3PhysicalOffset + i);
                Assert.Equal((byte)(((m / 10) << 4) + (m % 10)), reconSector[12]);
                Assert.Equal((byte)(((s / 10) << 4) + (s % 10)), reconSector[13]);
                Assert.Equal((byte)(((f / 10) << 4) + (f % 10)), reconSector[14]);
            }
            _output.WriteLine("Track 3 (High-density Mode1): hashes match");

            // Verify full image hash
            Assert.Equal(imageCrc, TestHashUtil.ComputeCrc32(reconstructed));
            Assert.Equal(imageXxHash, TestHashUtil.ComputeXXHash64(reconstructed));
            _output.WriteLine("PASS: 3-track Dreamcast GDI round-trip verified");
        }
    }
}
#nullable enable
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using SysBuffer = System.Buffer;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Integration test for CUE export round-trip.
    /// Exercises the full pipeline:
    /// 1. Create synthetic CUE image (Mode1 data track + Audio track)
    /// 2. Ingest into DataStore with FileName metadata per area and loose CUE file
    /// 3. Export via reconstruction (simulating NKDSApp export with no format)
    /// 4. Verify CUE/BIN output matches original
    /// 5. Verify block hashes match ingested values
    ///
    /// Feature: full-format-processing-parity
    /// **Validates: Requirements 10.1, 10.3, 11.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class CueExportRoundTripIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDir;

        public CueExportRoundTripIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(),
                $"CueExportRoundTrip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        #region Helpers

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
        /// Computes CRC32 for a byte array using the NKit Crc class.
        /// </summary>
        private static uint ComputeCrc32(byte[] data) => Nanook.NKit.Crc.Compute(data);

        /// <summary>
        /// Computes CRC32 for a segment of a byte array.
        /// </summary>
        private static uint ComputeCrc32(byte[] data, int offset, int length) => Nanook.NKit.Crc.Compute(data, offset, length);

        /// <summary>
        /// Computes XXHash64 for a byte array.
        /// </summary>
        private static ulong ComputeXxHash64(byte[] data) => Nanook.NKit.XXHash64.Compute(data, 0, data.Length);

        /// <summary>
        /// Computes XXHash64 for a segment of a byte array.
        /// </summary>
        private static ulong ComputeXxHash64(byte[] data, int offset, int length) => Nanook.NKit.XXHash64.Compute(data, offset, length);

        /// <summary>
        /// Reads exactly the requested number of bytes from a stream, handling partial reads.
        /// </summary>
        private static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    break;
                totalRead += read;
            }
            return totalRead;
        }

        #endregion

        /// <summary>
        /// Full round-trip integration test for CUE export with no format specified:
        /// 1. Creates a synthetic multi-track CUE image (Mode1 data track + Audio track)
        /// 2. Ingests it into DataStore with FileName metadata per track area and a loose CUE file
        /// 3. Reconstructs via ImageBuilderIso9660Stream (the Expand-Iso-CueToc path)
        /// 4. Verifies the reconstructed output matches the original byte-for-byte
        /// 5. Verifies per-block hashes match ingested values
        ///
        /// This simulates NKDSApp export with no --format flag for a CUE image,
        /// which expands the image to its native CUE/BIN multi-file format.
        ///
        /// **Validates: Requirements 10.1, 10.3, 11.4**
        /// </summary>
        [Fact]
        public void CueExport_NoFormat_IngestAndReconstruct_OutputMatchesOriginal()
        {
            // ── Arrange: build synthetic multi-track CUE image ────────────

            const int dataTrackSectors = 8;
            const int audioTrackSectors = 6;
            const long dataTrackPhysicalOffset = 150; // Standard CD pregap (LBA 150)
            const long audioTrackPhysicalOffset = 158; // After data track

            const string track1FileName = "game (Track 01).bin";
            const string track2FileName = "game (Track 02).bin";
            const string imageName = "game";
            const string setName = "CueExportTest";

            // Generate user data for Mode1 data track (8 sectors × 2048 bytes)
            byte[] dataTrackUserData = new byte[dataTrackSectors * 0x800];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 3000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, dataTrackUserData,
                    i * 0x800, 0x800);
            }

            // Build full raw sectors for the data track (2352 bytes each)
            byte[] dataTrackRawSectors = new byte[dataTrackSectors * 0x930];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(dataTrackUserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, dataTrackPhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, dataTrackRawSectors,
                    i * 0x930, 0x930);
            }

            // Generate audio track data (6 sectors × 2352 bytes, full sector, no headers)
            byte[] audioTrackData = new byte[audioTrackSectors * 0x930];
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 4000 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, audioTrackData,
                    i * 0x930, 0x930);
            }

            long dataTrackAreaSize = (long)dataTrackSectors * 0x930;
            long audioTrackAreaSize = (long)audioTrackSectors * 0x930;
            long totalImageSize = dataTrackAreaSize + audioTrackAreaSize;

            // Compute per-track hashes
            uint dataTrackCrc = ComputeCrc32(dataTrackRawSectors);
            ulong dataTrackXxHash = ComputeXxHash64(dataTrackRawSectors);
            uint audioTrackCrc = ComputeCrc32(audioTrackData);
            ulong audioTrackXxHash = ComputeXxHash64(audioTrackData);

            // Build the full expected image (data track + audio track)
            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(dataTrackRawSectors, 0, fullImage, 0,
                dataTrackRawSectors.Length);
            SysBuffer.BlockCopy(audioTrackData, 0, fullImage,
                dataTrackRawSectors.Length, audioTrackData.Length);
            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            // Build synthetic CUE index file
            string cueContent = string.Join("\r\n", new[]
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
            _output.WriteLine($"Image CRC32: 0x{imageCrc:X8}, XXHash64: 0x{imageXxHash:X16}");

            // ── Act: Ingest into DataStore as CUE with FileName metadata ──

            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(setName);

            long imageId;
            using (IImageWriter writer = store.AddImage(setName, imageName, "Default", ImageFormat.Cue))
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

                // Write the user data (stripped of sector headers by stride)
                writer.WriteData(0, dataTrackUserData, BlockType.File, offsetStart: 0);

                // Track 2: Audio track with FileName metadata (no stride - full sector)
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

                imageId = writer.Image.Id;
            }

            store.WaitForSetIdle(setName, 30000);
            _output.WriteLine("CUE image ingested into DataStore");

            // ── Act: Reconstruct via ImageBuilderIso9660Stream ────────────
            // This simulates what the Expand-Iso-CueToc step does when exporting
            // a CUE image with no format specified (native CUE/BIN output).

            GlobalImageKey key = new GlobalImageKey(setName, imageId);
            using IImageReader reader = store.OpenImageReader(key);
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);

            byte[] reconstructed = new byte[totalImageSize];
            builder.Position = 0;
            int totalRead = ReadFully(builder, reconstructed, 0, (int)totalImageSize);

            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // ── Assert: Verify full output size ───────────────────────────
            Assert.Equal((int)totalImageSize, totalRead);

            // ── Assert: Verify data track matches byte-for-byte ───────────
            byte[] reconDataTrack = new byte[dataTrackAreaSize];
            SysBuffer.BlockCopy(reconstructed, 0, reconDataTrack, 0,
                (int)dataTrackAreaSize);

            uint reconDataCrc = ComputeCrc32(reconDataTrack);
            ulong reconDataXxHash = ComputeXxHash64(reconDataTrack);

            _output.WriteLine($"Data track - Original CRC: 0x{dataTrackCrc:X8}, " +
                $"Reconstructed CRC: 0x{reconDataCrc:X8}");

            Assert.Equal(dataTrackCrc, reconDataCrc);
            Assert.Equal(dataTrackXxHash, reconDataXxHash);

            // Verify each Mode1 sector reconstructed correctly
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

                // Verify mode byte (Mode1 = 0x01)
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

            // ── Assert: Verify audio track matches byte-for-byte ──────────
            byte[] reconAudioTrack = new byte[audioTrackAreaSize];
            SysBuffer.BlockCopy(reconstructed, (int)dataTrackAreaSize,
                reconAudioTrack, 0, (int)audioTrackAreaSize);

            uint reconAudioCrc = ComputeCrc32(reconAudioTrack);
            ulong reconAudioXxHash = ComputeXxHash64(reconAudioTrack);

            _output.WriteLine($"Audio track - Original CRC: 0x{audioTrackCrc:X8}, " +
                $"Reconstructed CRC: 0x{reconAudioCrc:X8}");

            Assert.Equal(audioTrackCrc, reconAudioCrc);
            Assert.Equal(audioTrackXxHash, reconAudioXxHash);

            // Verify each audio sector byte-for-byte (no reconstruction needed)
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] originalSector = audioTrackData
                    .AsSpan(i * 0x930, 0x930).ToArray();
                byte[] reconSector = reconAudioTrack
                    .AsSpan(i * 0x930, 0x930).ToArray();
                Assert.Equal(originalSector, reconSector);
            }

            _output.WriteLine("Audio track: all sectors verified byte-for-byte");

            // ── Assert: Verify full image hash ────────────────────────────
            uint fullReconCrc = ComputeCrc32(reconstructed);
            ulong fullReconXxHash = ComputeXxHash64(reconstructed);

            Assert.Equal(imageCrc, fullReconCrc);
            Assert.Equal(imageXxHash, fullReconXxHash);
            _output.WriteLine("Full image CRC32 and XXHash64 match");
        }

        /// <summary>
        /// Verifies that stored area-level hashes in the DataStore match the hashes computed
        /// from the reconstructed CUE/BIN output. This tests the verification path where
        /// per-area CRC32 and XXHash64 stored during ingestion are compared against
        /// reconstructed area hashes — the same comparison that the Verify-Image step performs.
        ///
        /// For CUE images with stride (Mode1 sectors), block-level hashes are computed over
        /// the clean/user data. The area-level hashes (passed to CreateArea) are computed over
        /// the full raw sectors. This test verifies the area hashes match after reconstruction.
        ///
        /// **Validates: Requirements 10.1, 10.3, 11.4**
        /// </summary>
        [Fact]
        public void CueExport_StoredAreaHashes_MatchReconstructedOutput()
        {
            // ── Arrange: build synthetic multi-track CUE image ────────────
            // Two tracks to verify per-area hash matching across track boundaries

            const int dataTrackSectors = 10;
            const int audioTrackSectors = 8;
            const long dataTrackPhysicalOffset = 150;
            const long audioTrackPhysicalOffset = 160;

            const string track1FileName = "disc (Track 01).bin";
            const string track2FileName = "disc (Track 02).bin";
            const string imageName = "disc";
            const string setName = "CueAreaHashTest";

            // Generate user data for Mode1 data track
            byte[] dataTrackUserData = new byte[dataTrackSectors * 0x800];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 6000 + i, size: 0x800);
                SysBuffer.BlockCopy(sectorData, 0, dataTrackUserData,
                    i * 0x800, 0x800);
            }

            // Build full raw sectors for data track
            byte[] dataTrackRawSectors = new byte[dataTrackSectors * 0x930];
            for (int i = 0; i < dataTrackSectors; i++)
            {
                byte[] userData = new byte[0x800];
                SysBuffer.BlockCopy(dataTrackUserData, i * 0x800, userData, 0, 0x800);
                byte[] sector = BuildMode1Sector(userData, dataTrackPhysicalOffset + i);
                SysBuffer.BlockCopy(sector, 0, dataTrackRawSectors,
                    i * 0x930, 0x930);
            }

            // Generate audio track data (full 2352-byte sectors)
            byte[] audioTrackData = new byte[audioTrackSectors * 0x930];
            for (int i = 0; i < audioTrackSectors; i++)
            {
                byte[] sectorData = GenerateData(seed: 7000 + i, size: 0x930);
                SysBuffer.BlockCopy(sectorData, 0, audioTrackData,
                    i * 0x930, 0x930);
            }

            long dataTrackAreaSize = (long)dataTrackSectors * 0x930;
            long audioTrackAreaSize = (long)audioTrackSectors * 0x930;
            long totalImageSize = dataTrackAreaSize + audioTrackAreaSize;

            // Compute per-area hashes (these are what the Verify step compares)
            uint dataTrackCrc = ComputeCrc32(dataTrackRawSectors);
            ulong dataTrackXxHash = ComputeXxHash64(dataTrackRawSectors);
            uint audioTrackCrc = ComputeCrc32(audioTrackData);
            ulong audioTrackXxHash = ComputeXxHash64(audioTrackData);

            // Full image hashes
            byte[] fullImage = new byte[totalImageSize];
            SysBuffer.BlockCopy(dataTrackRawSectors, 0, fullImage, 0,
                dataTrackRawSectors.Length);
            SysBuffer.BlockCopy(audioTrackData, 0, fullImage,
                dataTrackRawSectors.Length, audioTrackData.Length);
            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            // Build CUE index file
            string cueContent = string.Join("\r\n", new[]
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
                $"{dataTrackAreaSize} bytes");
            _output.WriteLine($"Audio track: {audioTrackSectors} sectors, " +
                $"{audioTrackAreaSize} bytes");
            _output.WriteLine($"Total image: {totalImageSize} bytes");

            // ── Act: Ingest into DataStore ────────────────────────────────

            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(setName);

            long imageId;
            using (IImageWriter writer = store.AddImage(setName, imageName, "Default", ImageFormat.Cue))
            {
                // Track 1: Mode1 data track with stride
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

                // Track 2: Audio track (no stride - full sector stored)
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

                writer.WriteFile("disc.cue", cueBytes);
                writer.FinalizeImage(size: totalImageSize,
                    crc32: imageCrc, xxhash64: imageXxHash);

                imageId = writer.Image.Id;
            }

            store.WaitForSetIdle(setName, 30000);
            _output.WriteLine("CUE image ingested into DataStore");

            // ── Act: Reconstruct and verify area hashes ──────────────────

            GlobalImageKey key = new GlobalImageKey(setName, imageId);
            using IImageReader reader = store.OpenImageReader(key);

            // Retrieve stored area records with their hashes
            List<AreaRecord> areas = reader.GetAreas().OrderBy(a => a.Offset).ToList();
            Assert.Equal(2, areas.Count);

            _output.WriteLine($"Stored area 0: offset={areas[0].Offset}, size={areas[0].Size}, " +
                $"CRC32=0x{areas[0].Crc32:X8}");
            _output.WriteLine($"Stored area 1: offset={areas[1].Offset}, size={areas[1].Size}, " +
                $"CRC32=0x{areas[1].Crc32:X8}");

            // Reconstruct the image
            using ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(reader);
            byte[] reconstructed = new byte[totalImageSize];
            builder.Position = 0;
            int totalRead = ReadFully(builder, reconstructed, 0, (int)totalImageSize);

            Assert.Equal((int)totalImageSize, totalRead);
            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // ── Assert: Verify per-area hashes match ingested values ──────
            // This is exactly what the Verify-Image step does: compare stored area
            // hashes with hashes computed from the reconstructed output.

            // Area 0 (data track): verify CRC32 and XXHash64 of reconstructed data
            byte[] reconArea0 = new byte[areas[0].Size];
            SysBuffer.BlockCopy(reconstructed, (int)areas[0].Offset,
                reconArea0, 0, (int)areas[0].Size);

            uint reconArea0Crc = ComputeCrc32(reconArea0);
            ulong reconArea0XxHash = ComputeXxHash64(reconArea0);

            Assert.Equal(areas[0].Crc32, reconArea0Crc);
            Assert.Equal(areas[0].XxHash64, reconArea0XxHash);
            _output.WriteLine($"Area 0 (data track): CRC32 and XXHash64 match " +
                $"(0x{reconArea0Crc:X8})");

            // Area 1 (audio track): verify CRC32 and XXHash64 of reconstructed data
            byte[] reconArea1 = new byte[areas[1].Size];
            SysBuffer.BlockCopy(reconstructed, (int)areas[1].Offset,
                reconArea1, 0, (int)areas[1].Size);

            uint reconArea1Crc = ComputeCrc32(reconArea1);
            ulong reconArea1XxHash = ComputeXxHash64(reconArea1);

            Assert.Equal(areas[1].Crc32, reconArea1Crc);
            Assert.Equal(areas[1].XxHash64, reconArea1XxHash);
            _output.WriteLine($"Area 1 (audio track): CRC32 and XXHash64 match " +
                $"(0x{reconArea1Crc:X8})");

            // ── Assert: Full image byte comparison ────────────────────────
            Assert.Equal(fullImage, reconstructed);
            _output.WriteLine("Full CUE/BIN output verified: byte-for-byte match with original");

            // ── Assert: Overall image hashes match ────────────────────────
            uint fullReconCrc = ComputeCrc32(reconstructed);
            ulong fullReconXxHash = ComputeXxHash64(reconstructed);

            Assert.Equal(imageCrc, fullReconCrc);
            Assert.Equal(imageXxHash, fullReconXxHash);
            _output.WriteLine("Overall image CRC32 and XXHash64 match ingested values");
        }
    }
}
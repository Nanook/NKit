#nullable enable
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Integration test for Xbox DataStore round-trip.
    /// Feature: nkds-iso-xbox-support, Property 11: DataStore Verify Round-Trip (Integration)
    ///
    /// Creates a synthetic Xbox image with video and game partitions,
    /// ingests it into a real DataStore using the IImageWriter API (simulating DataStoreXboxFormatter),
    /// reconstructs via ImageBuilderXboxStream, and verifies per-block XxHash64 and CRC32 hashes
    /// match the ingested values.
    ///
    /// Xbox does NOT use encryption. This test exercises the full ingestion → reconstruction pipeline
    /// without any encryption logic.
    ///
    /// **Validates: Requirements 9.1, 9.5**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class XboxDataStoreRoundTripIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public XboxDataStoreRoundTripIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds_xbox_rt_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        #region Helpers

        /// <summary>
        /// Creates deterministic test data with a recognizable pattern based on seed.
        /// </summary>
        private static byte[] MakeData(int seed, int size)
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
        /// Computes XXHash64 for a byte array.
        /// </summary>
        private static ulong ComputeXxHash64(byte[] data) => Nanook.NKit.XXHash64.Compute(data, 0, data.Length);

        /// <summary>
        /// Computes CRC32 for a segment of a byte array.
        /// </summary>
        private static uint ComputeCrc32(byte[] data, int offset, int length) => Nanook.NKit.Crc.Compute(data, offset, length);

        /// <summary>
        /// Computes XXHash64 for a segment of a byte array.
        /// </summary>
        private static ulong ComputeXxHash64(byte[] data, int offset, int length) => Nanook.NKit.XXHash64.Compute(data, offset, length);

        /// <summary>
        /// Waits for background compression tasks to complete for a set.
        /// </summary>
        private static void WaitForSetIdle(DataStore store, string setName) => store.WaitForSetIdle(setName, 30000);

        #endregion

        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 11: DataStore Verify Round-Trip (Integration)
        ///
        /// Full round-trip test: Creates a synthetic Xbox image with a video partition and a game partition,
        /// ingests it into a real DataStore, reconstructs it via ImageBuilderXboxStream, and verifies
        /// that per-block XxHash64 and CRC32 hashes of the reconstructed output match the original data.
        ///
        /// The test simulates what the DataStoreXboxFormatter does during ingestion:
        /// 1. Creates area records with Xbox-specific metadata (FsType, BlockSize=0x800, AreaOffsetBase)
        /// 2. Writes video partition data as BlockType.Other
        /// 3. Writes game partition data as BlockType.File
        /// 4. Finalizes the image with overall checksums
        ///
        /// Then simulates what the Verify step does:
        /// 1. Opens the image reader
        /// 2. Creates ImageBuilderXboxStream
        /// 3. Reads the reconstructed output
        /// 4. Computes per-block hashes and compares with stored block hashes
        ///
        /// **Validates: Requirements 9.1, 9.5**
        /// </summary>
        [Fact]
        public void XboxRoundTrip_IngestAndReconstruct_BlockHashesMatch()
        {
            // ── Arrange: Create synthetic Xbox image data ──────────────────
            const int blockSize = 0x10000; // 64KB blocks (DataStore block size)
            const string setName = "xbox";
            const string imageName = "TestXboxGame";

            // Video partition: 2 blocks of data (128KB)
            byte[] videoData = MakeData(seed: 42, size: blockSize * 2);

            // Game partition: 3 blocks of data (192KB)
            byte[] gameData = MakeData(seed: 99, size: blockSize * 3);

            // Layout: video partition at offset 0, game partition immediately after
            long videoOffset = 0;
            long videoSize = videoData.Length;
            long gameOffset = videoSize;
            long gameSize = gameData.Length;
            long totalImageSize = videoSize + gameSize;

            // Compute overall image checksums
            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(videoData, 0, fullImage, 0, videoData.Length);
            Array.Copy(gameData, 0, fullImage, gameOffset, gameData.Length);
            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            // ── Act: Ingest into DataStore ────────────────────────────────
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(setName, blockSize: blockSize);

            long imageId;
            using (IImageWriter writer = store.AddImage(setName, imageName, "XBox", ImageFormat.Iso))
            {
                // Create video partition area (FsType = "Other")
                AreaMetadata videoMeta = new AreaMetadata();
                videoMeta.Set(AreaValueType.FsType, "Other");
                videoMeta.Set(AreaValueType.BlockSize, 0x800L);
                videoMeta.Set(AreaValueType.AreaOffsetBase, videoOffset);
                writer.CreateArea(videoOffset, videoSize, ComputeCrc32(videoData), ComputeXxHash64(videoData),
                    sectionSize: 0x200000, metadata: videoMeta);

                // Create game partition area (FsType = "FileSystem")
                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gameOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gameOffset, gameSize, ComputeCrc32(gameData), ComputeXxHash64(gameData),
                    sectionSize: 0x200000, metadata: gameMeta);

                // Write video partition data
                writer.WriteData(videoOffset, videoData, BlockType.Other);

                // Write game partition data
                writer.WriteData(gameOffset, gameData, BlockType.File);

                // Finalize the image
                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);

                imageId = writer.Image.Id;
            }
            WaitForSetIdle(store, setName);

            // ── Act: Reconstruct via ImageBuilderXboxStream ───────────────
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, setName);

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            // Read the entire reconstructed image
            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = stream.Read(reconstructed, totalRead, (int)(totalImageSize - totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            // ── Assert: Verify per-block hashes match ─────────────────────
            Assert.Equal(totalImageSize, totalRead);

            // Verify the reconstructed output matches the original data byte-for-byte
            Assert.Equal(fullImage, reconstructed);

            // Verify per-block XxHash64 and CRC32 hashes match
            int blockCount = (int)((totalImageSize + blockSize - 1) / blockSize);
            for (int i = 0; i < blockCount; i++)
            {
                int offset = i * blockSize;
                int length = (int)Math.Min(blockSize, totalImageSize - offset);

                uint originalCrc = ComputeCrc32(fullImage, offset, length);
                ulong originalXxHash = ComputeXxHash64(fullImage, offset, length);

                uint reconstructedCrc = ComputeCrc32(reconstructed, offset, length);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, offset, length);

                Assert.Equal(originalCrc, reconstructedCrc);
                Assert.Equal(originalXxHash, reconstructedXxHash);
            }
        }

        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 11: DataStore Verify Round-Trip (Integration)
        ///
        /// Verifies that the stored block hashes in the DataStore match the hashes computed
        /// from the reconstructed output. This tests the actual verification path where
        /// block hashes stored during ingestion are compared against reconstructed block hashes.
        ///
        /// **Validates: Requirements 9.1, 9.5**
        /// </summary>
        [Fact]
        public void XboxRoundTrip_StoredBlockHashes_MatchReconstructedHashes()
        {
            // ── Arrange: Create synthetic Xbox image data ──────────────────
            const int blockSize = 0x10000; // 64KB blocks
            const string setName = "xbox";
            const string imageName = "TestXboxVerify";

            // Video partition: 1 block
            byte[] videoData = MakeData(seed: 101, size: blockSize);

            // Game partition: 2 blocks
            byte[] gameData = MakeData(seed: 202, size: blockSize * 2);

            long videoOffset = 0;
            long videoSize = videoData.Length;
            long gameOffset = videoSize;
            long gameSize = gameData.Length;
            long totalImageSize = videoSize + gameSize;

            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(videoData, 0, fullImage, 0, videoData.Length);
            Array.Copy(gameData, 0, fullImage, gameOffset, gameData.Length);
            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            // ── Act: Ingest into DataStore ────────────────────────────────
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(setName, blockSize: blockSize);

            long imageId;
            using (IImageWriter writer = store.AddImage(setName, imageName, "XBox", ImageFormat.Iso))
            {
                AreaMetadata videoMeta = new AreaMetadata();
                videoMeta.Set(AreaValueType.FsType, "Other");
                videoMeta.Set(AreaValueType.BlockSize, 0x800L);
                videoMeta.Set(AreaValueType.AreaOffsetBase, videoOffset);
                writer.CreateArea(videoOffset, videoSize, ComputeCrc32(videoData), ComputeXxHash64(videoData),
                    sectionSize: 0x200000, metadata: videoMeta);

                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gameOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gameOffset, gameSize, ComputeCrc32(gameData), ComputeXxHash64(gameData),
                    sectionSize: 0x200000, metadata: gameMeta);

                writer.WriteData(videoOffset, videoData, BlockType.Other);
                writer.WriteData(gameOffset, gameData, BlockType.File);
                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);

                imageId = writer.Image.Id;
            }
            WaitForSetIdle(store, setName);

            // ── Act: Read stored block hashes and reconstruct ─────────────
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, setName);

            // Collect all stored block keys (hashes) from offset records
            List<(long Offset, long Size, BlockKey Key)> storedBlocks = new();
            foreach (OffsetRecord offsetRecord in reader.GetOffsets())
            {
                if (!offsetRecord.HasBlocks)
                    continue;

                long currentOffset = offsetRecord.Offset;
                for (int i = 0; i < offsetRecord.BlockCount; i++)
                {
                    BlockKey key = offsetRecord.GetBlockAt(i);
                    long thisBlockSize = Math.Min(blockSize, offsetRecord.Size - ((long)i * blockSize));
                    storedBlocks.Add((currentOffset, thisBlockSize, key));
                    currentOffset += thisBlockSize;
                }
            }

            // Reconstruct the image
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = stream.Read(reconstructed, totalRead, (int)(totalImageSize - totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            Assert.Equal(totalImageSize, totalRead);

            // ── Assert: Verify stored block hashes match reconstructed data ──
            Assert.NotEmpty(storedBlocks);

            foreach ((long blockOffset, long blockLen, BlockKey storedKey) in storedBlocks)
            {
                // Compute hashes from the reconstructed data at this block's position
                uint reconstructedCrc = ComputeCrc32(reconstructed, (int)blockOffset, (int)blockLen);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, (int)blockOffset, (int)blockLen);

                // The stored block key contains the XxHash64 and CRC32 of the original data
                Assert.Equal(storedKey.Crc32, reconstructedCrc);
                Assert.Equal(storedKey.XxHash64, reconstructedXxHash);
            }
        }

        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 11: DataStore Verify Round-Trip (Integration)
        ///
        /// Verifies that a larger synthetic Xbox image (multiple blocks per partition)
        /// round-trips correctly through the DataStore. Uses a more realistic image layout
        /// with a larger game partition.
        ///
        /// **Validates: Requirements 9.1, 9.5**
        /// </summary>
        [Fact]
        public void XboxRoundTrip_LargerImage_MultipleBlocksPerPartition()
        {
            // ── Arrange ───────────────────────────────────────────────────
            const int blockSize = 0x10000; // 64KB blocks
            const string setName = "xbox";
            const string imageName = "TestXboxLarge";

            // Video partition 1: 64KB (1 block)
            byte[] video1Data = MakeData(seed: 10, size: blockSize);

            // Game partition: 256KB (4 blocks) — the main content
            byte[] gameData = MakeData(seed: 20, size: blockSize * 4);

            // Video partition 2: 128KB (2 blocks)
            byte[] video2Data = MakeData(seed: 30, size: blockSize * 2);

            // Layout: video1 | game | video2
            long video1Offset = 0;
            long video1Size = video1Data.Length;
            long gameOffset = video1Size;
            long gameSize = gameData.Length;
            long video2Offset = gameOffset + gameSize;
            long video2Size = video2Data.Length;
            long totalImageSize = video1Size + gameSize + video2Size;

            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(video1Data, 0, fullImage, video1Offset, video1Data.Length);
            Array.Copy(gameData, 0, fullImage, gameOffset, gameData.Length);
            Array.Copy(video2Data, 0, fullImage, video2Offset, video2Data.Length);
            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            // ── Act: Ingest ───────────────────────────────────────────────
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(setName, blockSize: blockSize);

            long imageId;
            using (IImageWriter writer = store.AddImage(setName, imageName, "XBox", ImageFormat.Iso))
            {
                // Video partition 1
                AreaMetadata video1Meta = new AreaMetadata();
                video1Meta.Set(AreaValueType.FsType, "Other");
                video1Meta.Set(AreaValueType.BlockSize, 0x800L);
                video1Meta.Set(AreaValueType.AreaOffsetBase, video1Offset);
                writer.CreateArea(video1Offset, video1Size, ComputeCrc32(video1Data), ComputeXxHash64(video1Data),
                    sectionSize: 0x200000, metadata: video1Meta);

                // Game partition
                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gameOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gameOffset, gameSize, ComputeCrc32(gameData), ComputeXxHash64(gameData),
                    sectionSize: 0x200000, metadata: gameMeta);

                // Video partition 2
                AreaMetadata video2Meta = new AreaMetadata();
                video2Meta.Set(AreaValueType.FsType, "Other");
                video2Meta.Set(AreaValueType.BlockSize, 0x800L);
                video2Meta.Set(AreaValueType.AreaOffsetBase, video2Offset);
                writer.CreateArea(video2Offset, video2Size, ComputeCrc32(video2Data), ComputeXxHash64(video2Data),
                    sectionSize: 0x200000, metadata: video2Meta);

                // Write all partition data
                writer.WriteData(video1Offset, video1Data, BlockType.Other);
                writer.WriteData(gameOffset, gameData, BlockType.File);
                writer.WriteData(video2Offset, video2Data, BlockType.Other);

                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);
                imageId = writer.Image.Id;
            }
            WaitForSetIdle(store, setName);

            // ── Act: Reconstruct ──────────────────────────────────────────
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, setName);

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = 0;
            while (totalRead < totalImageSize)
            {
                int bytesRead = stream.Read(reconstructed, totalRead, (int)(totalImageSize - totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            // ── Assert ────────────────────────────────────────────────────
            Assert.Equal(totalImageSize, totalRead);

            // Verify full image matches
            Assert.Equal(fullImage, reconstructed);

            // Verify per-block hashes
            int blockCount = (int)((totalImageSize + blockSize - 1) / blockSize);
            for (int i = 0; i < blockCount; i++)
            {
                int offset = i * blockSize;
                int length = (int)Math.Min(blockSize, totalImageSize - offset);

                uint originalCrc = ComputeCrc32(fullImage, offset, length);
                ulong originalXxHash = ComputeXxHash64(fullImage, offset, length);

                uint reconstructedCrc = ComputeCrc32(reconstructed, offset, length);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, offset, length);

                Assert.Equal(originalCrc, reconstructedCrc);
                Assert.Equal(originalXxHash, reconstructedXxHash);
            }

            // Also verify stored block keys match
            List<(long Offset, long Size, BlockKey Key)> storedBlocks = new();
            foreach (OffsetRecord offsetRecord in reader.GetOffsets())
            {
                if (!offsetRecord.HasBlocks)
                    continue;

                long currentOffset = offsetRecord.Offset;
                for (int i = 0; i < offsetRecord.BlockCount; i++)
                {
                    BlockKey key = offsetRecord.GetBlockAt(i);
                    long thisBlockSize = Math.Min(blockSize, offsetRecord.Size - ((long)i * blockSize));
                    storedBlocks.Add((currentOffset, thisBlockSize, key));
                    currentOffset += thisBlockSize;
                }
            }

            foreach ((long blockOffset, long blockLen, BlockKey storedKey) in storedBlocks)
            {
                uint reconstructedCrc = ComputeCrc32(reconstructed, (int)blockOffset, (int)blockLen);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, (int)blockOffset, (int)blockLen);

                Assert.Equal(storedKey.Crc32, reconstructedCrc);
                Assert.Equal(storedKey.XxHash64, reconstructedXxHash);
            }
        }
    }
}
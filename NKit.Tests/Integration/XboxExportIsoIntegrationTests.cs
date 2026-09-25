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
    /// Integration test for Xbox export to ISO format.
    /// Exercises the full pipeline: ingest Xbox ISO → export with --format iso → verify full ISO output.
    ///
    /// When exporting an Xbox image with format "iso", the Expand-XBox step reconstructs
    /// the full ISO including both the video partition and game partition. This test verifies
    /// that the reconstructed output matches the original ingested image byte-for-byte.
    ///
    /// **Validates: Requirements 6.1, 11.3**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class XboxExportIsoIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDir;
        private const int BlockSize = 0x10000; // 64KB block size
        private const string SetName = "xbox";
        private const string ImageName = "TestXboxExportIso";

        public XboxExportIsoIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds_xbox_iso_export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
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
        /// Computes CRC32 for a byte array.
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
        /// Full round-trip integration test for Xbox export to ISO format:
        /// 1. Creates a synthetic Xbox image with video partition and game partition
        /// 2. Ingests it into a DataStore
        /// 3. Reconstructs via ImageBuilderXboxStream (the Expand-XBox step for ISO output)
        /// 4. Verifies the full ISO output matches the original byte-for-byte
        /// 5. Verifies per-block hashes match ingested values
        ///
        /// The ISO export produces the FULL image including the video partition,
        /// unlike XISO export which only produces the game partition.
        ///
        /// **Validates: Requirements 6.1, 11.3**
        /// </summary>
        [Fact]
        public void XboxExportIso_IngestAndReconstruct_FullIsoOutputMatchesOriginal()
        {
            // ── Arrange: Create synthetic Xbox image data ──────────────────
            // Layout:
            //   [Video Partition: 0x00000 - 0x20000] (128KB, "Other" area)
            //   [Game Partition:  0x20000 - 0x80000] (384KB, "FileSystem" area)
            //     Game data: [0x20000 - 0x60000] = 256KB of game file data
            //     Gap:       [0x60000 - 0x80000] = 128KB (zero-filled, no data written)

            long videoPartitionOffset = 0x00000;
            long videoPartitionSize = 0x20000; // 128KB (2 blocks)
            long gamePartitionOffset = 0x20000;
            long gamePartitionSize = 0x60000; // 384KB (6 blocks)
            long gameDataOffset = 0x20000;
            long gameDataSize = 0x40000; // 256KB (4 blocks)
            long totalImageSize = videoPartitionSize + gamePartitionSize; // 512KB

            // Generate deterministic test data
            byte[] videoData = MakeData(seed: 10, size: (int)videoPartitionSize);
            byte[] gameData = MakeData(seed: 20, size: (int)gameDataSize);

            // Build the full expected image (video + game data + zero-filled gap)
            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(videoData, 0, fullImage, videoPartitionOffset, videoPartitionSize);
            Array.Copy(gameData, 0, fullImage, gameDataOffset, gameDataSize);
            // Gap region [0x60000 - 0x80000] remains zero-filled

            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            _output.WriteLine($"Synthetic Xbox image: {totalImageSize} bytes");
            _output.WriteLine($"  Video partition: 0x{videoPartitionOffset:X} - 0x{videoPartitionOffset + videoPartitionSize:X} ({videoPartitionSize} bytes)");
            _output.WriteLine($"  Game partition:  0x{gamePartitionOffset:X} - 0x{gamePartitionOffset + gamePartitionSize:X} ({gamePartitionSize} bytes)");
            _output.WriteLine($"  Game data:       0x{gameDataOffset:X} - 0x{gameDataOffset + gameDataSize:X} ({gameDataSize} bytes)");
            _output.WriteLine($"  Image CRC32: 0x{imageCrc:X8}, XXHash64: 0x{imageXxHash:X16}");

            // ── Act: Ingest into DataStore ────────────────────────────────
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(SetName, blockSize: BlockSize);

            long imageId;
            using (IImageWriter writer = store.AddImage(SetName, ImageName, "XBox", ImageFormat.Iso))
            {
                // Create video partition area (FsType = "Other")
                AreaMetadata videoMeta = new AreaMetadata();
                videoMeta.Set(AreaValueType.FsType, "Other");
                videoMeta.Set(AreaValueType.BlockSize, 0x800L);
                videoMeta.Set(AreaValueType.AreaOffsetBase, videoPartitionOffset);
                writer.CreateArea(videoPartitionOffset, videoPartitionSize,
                    ComputeCrc32(videoData), ComputeXxHash64(videoData),
                    sectionSize: 0x200000, metadata: videoMeta);

                // Create game partition area (FsType = "FileSystem")
                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gamePartitionOffset, gamePartitionSize,
                    ComputeCrc32(fullImage, (int)gamePartitionOffset, (int)gamePartitionSize),
                    ComputeXxHash64(fullImage, (int)gamePartitionOffset, (int)gamePartitionSize),
                    sectionSize: 0x200000, metadata: gameMeta);

                // Write video partition data
                writer.WriteData(videoPartitionOffset, videoData, BlockType.Other);

                // Write game partition data (only the actual game data, gap is implicit)
                writer.WriteData(gameDataOffset, gameData, BlockType.File);

                // Finalize the image
                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);

                imageId = writer.Image.Id;
            }

            store.WaitForSetIdle(SetName, 30000);
            _output.WriteLine("Xbox image ingested into DataStore");

            // ── Act: Reconstruct via ImageBuilderXboxStream (ISO export) ──
            // This simulates what the Expand-XBox step does when exporting to ISO format.
            // The full ISO output includes both video and game partitions.
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(SetName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, SetName);

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = ReadFully(stream, reconstructed, 0, (int)totalImageSize);

            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // ── Assert: Verify full ISO output size ───────────────────────
            Assert.Equal((int)totalImageSize, totalRead);

            // ── Assert: Verify video partition matches byte-for-byte ──────
            byte[] reconstructedVideo = new byte[videoPartitionSize];
            Array.Copy(reconstructed, videoPartitionOffset, reconstructedVideo, 0, videoPartitionSize);
            Assert.Equal(videoData, reconstructedVideo);
            _output.WriteLine("Video partition: byte-for-byte match confirmed");

            // ── Assert: Verify game data matches byte-for-byte ────────────
            byte[] reconstructedGameData = new byte[gameDataSize];
            Array.Copy(reconstructed, gameDataOffset, reconstructedGameData, 0, gameDataSize);
            Assert.Equal(gameData, reconstructedGameData);
            _output.WriteLine("Game data: byte-for-byte match confirmed");

            // ── Assert: Verify gap region is zero-filled ──────────────────
            long gapOffset = gameDataOffset + gameDataSize;
            long gapSize = gamePartitionSize - gameDataSize;
            byte[] reconstructedGap = new byte[gapSize];
            Array.Copy(reconstructed, gapOffset, reconstructedGap, 0, gapSize);
            Assert.All(reconstructedGap, b => Assert.Equal(0x00, b));
            _output.WriteLine($"Gap region at 0x{gapOffset:X}: correctly zero-filled ({gapSize} bytes)");

            // ── Assert: Verify full image matches expected output ──────────
            Assert.Equal(fullImage, reconstructed);
            _output.WriteLine("Full ISO output matches expected image byte-for-byte");

            // ── Assert: Verify per-block hashes match ─────────────────────
            int blockCount = (int)((totalImageSize + BlockSize - 1) / BlockSize);
            for (int i = 0; i < blockCount; i++)
            {
                int offset = i * BlockSize;
                int length = (int)Math.Min(BlockSize, totalImageSize - offset);

                uint originalCrc = ComputeCrc32(fullImage, offset, length);
                ulong originalXxHash = ComputeXxHash64(fullImage, offset, length);

                uint reconstructedCrc = ComputeCrc32(reconstructed, offset, length);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, offset, length);

                Assert.Equal(originalCrc, reconstructedCrc);
                Assert.Equal(originalXxHash, reconstructedXxHash);
            }
            _output.WriteLine($"Per-block hash verification passed for all {blockCount} blocks");
        }

        /// <summary>
        /// Verifies that stored block hashes in the DataStore match the hashes computed
        /// from the reconstructed ISO output. This tests the verification path where
        /// block hashes stored during ingestion are compared against reconstructed block hashes.
        ///
        /// **Validates: Requirements 6.1, 11.3**
        /// </summary>
        [Fact]
        public void XboxExportIso_StoredBlockHashes_MatchReconstructedIsoOutput()
        {
            // ── Arrange: Create synthetic Xbox image ──────────────────────
            // Simpler layout: video (1 block) + game (3 blocks)
            long videoPartitionOffset = 0;
            long videoPartitionSize = BlockSize; // 64KB (1 block)
            long gamePartitionOffset = videoPartitionSize;
            long gamePartitionSize = BlockSize * 3; // 192KB (3 blocks)
            long totalImageSize = videoPartitionSize + gamePartitionSize; // 256KB

            byte[] videoData = MakeData(seed: 100, size: (int)videoPartitionSize);
            byte[] gameData = MakeData(seed: 200, size: (int)gamePartitionSize);

            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(videoData, 0, fullImage, videoPartitionOffset, videoPartitionSize);
            Array.Copy(gameData, 0, fullImage, gamePartitionOffset, gamePartitionSize);

            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            // ── Act: Ingest into DataStore ────────────────────────────────
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(SetName, blockSize: BlockSize);

            long imageId;
            using (IImageWriter writer = store.AddImage(SetName, ImageName + "_hashes", "XBox", ImageFormat.Iso))
            {
                AreaMetadata videoMeta = new AreaMetadata();
                videoMeta.Set(AreaValueType.FsType, "Other");
                videoMeta.Set(AreaValueType.BlockSize, 0x800L);
                videoMeta.Set(AreaValueType.AreaOffsetBase, videoPartitionOffset);
                writer.CreateArea(videoPartitionOffset, videoPartitionSize,
                    ComputeCrc32(videoData), ComputeXxHash64(videoData),
                    sectionSize: 0x200000, metadata: videoMeta);

                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gamePartitionOffset, gamePartitionSize,
                    ComputeCrc32(gameData), ComputeXxHash64(gameData),
                    sectionSize: 0x200000, metadata: gameMeta);

                writer.WriteData(videoPartitionOffset, videoData, BlockType.Other);
                writer.WriteData(gamePartitionOffset, gameData, BlockType.File);
                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);

                imageId = writer.Image.Id;
            }

            store.WaitForSetIdle(SetName, 30000);

            // ── Act: Reconstruct via ImageBuilderXboxStream ───────────────
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(SetName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, SetName);

            // Collect stored block keys (hashes) from offset records
            List<(long Offset, long Size, BlockKey Key)> storedBlocks = new();
            foreach (OffsetRecord offsetRecord in reader.GetOffsets())
            {
                if (!offsetRecord.HasBlocks)
                    continue;

                long currentOffset = offsetRecord.Offset;
                for (int i = 0; i < offsetRecord.BlockCount; i++)
                {
                    BlockKey key = offsetRecord.GetBlockAt(i);
                    long thisBlockSize = Math.Min(BlockSize, offsetRecord.Size - ((long)i * BlockSize));
                    storedBlocks.Add((currentOffset, thisBlockSize, key));
                    currentOffset += thisBlockSize;
                }
            }

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = ReadFully(stream, reconstructed, 0, (int)totalImageSize);

            Assert.Equal((int)totalImageSize, totalRead);

            // ── Assert: Verify stored block hashes match reconstructed data
            Assert.NotEmpty(storedBlocks);
            _output.WriteLine($"Verifying {storedBlocks.Count} stored block hashes against reconstructed ISO output");

            foreach ((long blockOffset, long blockLen, BlockKey storedKey) in storedBlocks)
            {
                uint reconstructedCrc = ComputeCrc32(reconstructed, (int)blockOffset, (int)blockLen);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, (int)blockOffset, (int)blockLen);

                Assert.Equal(storedKey.Crc32, reconstructedCrc);
                Assert.Equal(storedKey.XxHash64, reconstructedXxHash);
            }

            _output.WriteLine("All stored block hashes match reconstructed ISO output");

            // ── Assert: Full image byte comparison ────────────────────────
            Assert.Equal(fullImage, reconstructed);
            _output.WriteLine("Full ISO output verified: byte-for-byte match with original");
        }

        /// <summary>
        /// Verifies that a larger Xbox image with multiple blocks per partition
        /// round-trips correctly through the DataStore when exported as ISO.
        /// Uses a more realistic layout with multiple video and game partition blocks.
        ///
        /// **Validates: Requirements 6.1, 11.3**
        /// </summary>
        [Fact]
        public void XboxExportIso_LargerImage_MultipleBlocksPerPartition_RoundTrip()
        {
            // ── Arrange ───────────────────────────────────────────────────
            // Layout:
            //   [Video Partition 1: 0x000000 - 0x020000] (128KB, 2 blocks)
            //   [Game Partition:    0x020000 - 0x0A0000] (512KB, 8 blocks)
            //   [Video Partition 2: 0x0A0000 - 0x0C0000] (128KB, 2 blocks)

            long video1Offset = 0x000000;
            long video1Size = BlockSize * 2; // 128KB
            long gameOffset = video1Size;
            long gameSize = BlockSize * 8; // 512KB
            long video2Offset = gameOffset + gameSize;
            long video2Size = BlockSize * 2; // 128KB
            long totalImageSize = video1Size + gameSize + video2Size; // 768KB

            byte[] video1Data = MakeData(seed: 30, size: (int)video1Size);
            byte[] gameData = MakeData(seed: 40, size: (int)gameSize);
            byte[] video2Data = MakeData(seed: 50, size: (int)video2Size);

            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(video1Data, 0, fullImage, video1Offset, video1Size);
            Array.Copy(gameData, 0, fullImage, gameOffset, gameSize);
            Array.Copy(video2Data, 0, fullImage, video2Offset, video2Size);

            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            _output.WriteLine($"Larger Xbox image: {totalImageSize} bytes ({totalImageSize / BlockSize} blocks)");

            // ── Act: Ingest into DataStore ────────────────────────────────
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(SetName, blockSize: BlockSize);

            long imageId;
            using (IImageWriter writer = store.AddImage(SetName, ImageName + "_large", "XBox", ImageFormat.Iso))
            {
                // Video partition 1
                AreaMetadata video1Meta = new AreaMetadata();
                video1Meta.Set(AreaValueType.FsType, "Other");
                video1Meta.Set(AreaValueType.BlockSize, 0x800L);
                video1Meta.Set(AreaValueType.AreaOffsetBase, video1Offset);
                writer.CreateArea(video1Offset, video1Size,
                    ComputeCrc32(video1Data), ComputeXxHash64(video1Data),
                    sectionSize: 0x200000, metadata: video1Meta);

                // Game partition
                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gameOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gameOffset, gameSize,
                    ComputeCrc32(gameData), ComputeXxHash64(gameData),
                    sectionSize: 0x200000, metadata: gameMeta);

                // Video partition 2
                AreaMetadata video2Meta = new AreaMetadata();
                video2Meta.Set(AreaValueType.FsType, "Other");
                video2Meta.Set(AreaValueType.BlockSize, 0x800L);
                video2Meta.Set(AreaValueType.AreaOffsetBase, video2Offset);
                writer.CreateArea(video2Offset, video2Size,
                    ComputeCrc32(video2Data), ComputeXxHash64(video2Data),
                    sectionSize: 0x200000, metadata: video2Meta);

                // Write all partition data
                writer.WriteData(video1Offset, video1Data, BlockType.Other);
                writer.WriteData(gameOffset, gameData, BlockType.File);
                writer.WriteData(video2Offset, video2Data, BlockType.Other);

                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);
                imageId = writer.Image.Id;
            }

            store.WaitForSetIdle(SetName, 30000);

            // ── Act: Reconstruct via ImageBuilderXboxStream ───────────────
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(SetName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, SetName);

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = ReadFully(stream, reconstructed, 0, (int)totalImageSize);

            // ── Assert ────────────────────────────────────────────────────
            Assert.Equal((int)totalImageSize, totalRead);

            // Verify full image matches
            Assert.Equal(fullImage, reconstructed);
            _output.WriteLine("Full ISO output matches original for larger multi-partition image");

            // Verify per-block hashes
            int blockCount = (int)((totalImageSize + BlockSize - 1) / BlockSize);
            for (int i = 0; i < blockCount; i++)
            {
                int offset = i * BlockSize;
                int length = (int)Math.Min(BlockSize, totalImageSize - offset);

                uint originalCrc = ComputeCrc32(fullImage, offset, length);
                ulong originalXxHash = ComputeXxHash64(fullImage, offset, length);

                uint reconstructedCrc = ComputeCrc32(reconstructed, offset, length);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, offset, length);

                Assert.Equal(originalCrc, reconstructedCrc);
                Assert.Equal(originalXxHash, reconstructedXxHash);
            }

            _output.WriteLine($"Per-block hash verification passed for all {blockCount} blocks");

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
                    long thisBlockSize = Math.Min(BlockSize, offsetRecord.Size - ((long)i * BlockSize));
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

            _output.WriteLine($"Stored block key verification passed for {storedBlocks.Count} blocks");
        }
    }
}
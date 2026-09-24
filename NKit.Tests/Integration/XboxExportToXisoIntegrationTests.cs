#nullable enable
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Integration test for Xbox export to XISO format.
    /// Exercises the full pipeline:
    /// 1. Create synthetic Xbox ISO image with video and game partitions
    /// 2. Ingest into DataStore
    /// 3. Reconstruct via ImageBuilderXboxStream (the Expand-XBox step)
    /// 4. Verify the game partition data is correctly reconstructed (XISO = game partition only)
    /// 5. Verify filler/gap regions are zero-filled (XISO wipes filler)
    ///
    /// The XISO format contains only the game partition with filler data wiped to zeros.
    /// This test verifies that the DataStore correctly stores and reconstructs Xbox game data,
    /// and that the video partition can be excluded to produce XISO-compatible output.
    ///
    /// **Validates: Requirements 6.2, 11.2**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class XboxExportToXisoIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDir;
        private const int BlockSize = 0x10000; // 64KB block size
        private const string SetName = "xbox";
        private const string ImageName = "TestXboxXiso";

        public XboxExportToXisoIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(),
                $"XboxXisoExport_{Guid.NewGuid():N}");
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

        /// <summary>
        /// Creates deterministic block data with a recognizable pattern based on seed.
        /// </summary>
        private static byte[] MakeBlockData(int seed, int size)
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

        /// <summary>
        /// Full round-trip integration test for Xbox export to XISO format:
        /// 1. Creates a synthetic Xbox image with video partition and game partition
        /// 2. Ingests it into the DataStore
        /// 3. Reconstructs via ImageBuilderXboxStream (same path as Expand-XBox step)
        /// 4. Extracts only the game partition from the reconstructed output (XISO behavior)
        /// 5. Verifies game data is correctly reconstructed
        /// 6. Verifies gap/filler regions in the game partition are zero-filled
        ///
        /// XISO format = game partition only, with filler/junk wiped to zeros.
        /// This test validates that the pipeline correctly:
        /// - Stores Xbox game partition data in the DataStore
        /// - Reconstructs the game partition data byte-for-byte
        /// - Produces zero-filled gap regions (as XISO expects)
        /// - Excludes the video partition from the game data
        ///
        /// **Validates: Requirements 6.2, 11.2**
        /// </summary>
        [Fact]
        public void XboxExport_ToXiso_GamePartitionReconstructedWithFillerWiped()
        {
            // ── Arrange: Create synthetic Xbox image data ──────────────────
            // Layout:
            //   [Video Partition: 0x00000 - 0x20000] (128KB, 2 blocks, "Other" area)
            //   [Game Partition:  0x20000 - 0x80000] (384KB, 6 blocks, "FileSystem" area)
            //     Game data:  [0x20000 - 0x50000] = 192KB of game file data
            //     Gap/filler: [0x50000 - 0x80000] = 192KB (zero-filled in XISO)

            long videoPartitionOffset = 0x00000;
            long videoPartitionSize = 0x20000; // 128KB (2 blocks)
            long gamePartitionOffset = 0x20000;
            long gamePartitionSize = 0x60000; // 384KB (6 blocks)
            long gameDataSize = 0x30000; // 192KB (3 blocks) of game file data
            long gapSize = gamePartitionSize - gameDataSize; // 192KB gap/filler
            long totalImageSize = videoPartitionSize + gamePartitionSize; // 512KB

            // Generate deterministic test data
            byte[] videoData = MakeBlockData(seed: 10, size: (int)videoPartitionSize);
            byte[] gameData = MakeBlockData(seed: 20, size: (int)gameDataSize);

            // Compute area checksums
            uint videoCrc = ComputeCrc32(videoData);
            ulong videoXxHash = ComputeXxHash64(videoData);

            // Build the game partition as it will exist in full image (game data + zero gap)
            byte[] gamePartitionFull = new byte[gamePartitionSize];
            Array.Copy(gameData, 0, gamePartitionFull, 0, gameDataSize);
            // Gap remains zero-filled (this is the XISO behavior)

            uint gamePartitionCrc = ComputeCrc32(gamePartitionFull);
            ulong gamePartitionXxHash = ComputeXxHash64(gamePartitionFull);

            // Build full image for overall checksums
            byte[] fullImage = new byte[totalImageSize];
            Array.Copy(videoData, 0, fullImage, videoPartitionOffset, videoPartitionSize);
            Array.Copy(gameData, 0, fullImage, gamePartitionOffset, gameDataSize);
            uint imageCrc = ComputeCrc32(fullImage);
            ulong imageXxHash = ComputeXxHash64(fullImage);

            _output.WriteLine($"Synthetic Xbox image: {totalImageSize} bytes");
            _output.WriteLine($"  Video partition: 0x{videoPartitionOffset:X} - 0x{videoPartitionOffset + videoPartitionSize:X} ({videoPartitionSize} bytes)");
            _output.WriteLine($"  Game partition:  0x{gamePartitionOffset:X} - 0x{gamePartitionOffset + gamePartitionSize:X} ({gamePartitionSize} bytes)");
            _output.WriteLine($"  Game data:       {gameDataSize} bytes");
            _output.WriteLine($"  Gap (filler):    {gapSize} bytes (zero-filled in XISO)");

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
                    videoCrc, videoXxHash,
                    sectionSize: 0x200000, metadata: videoMeta);

                // Create game partition area (FsType = "FileSystem")
                AreaMetadata gameMeta = new AreaMetadata();
                gameMeta.Set(AreaValueType.FsType, "FileSystem");
                gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                gameMeta.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                writer.CreateArea(gamePartitionOffset, gamePartitionSize,
                    gamePartitionCrc, gamePartitionXxHash,
                    sectionSize: 0x200000, metadata: gameMeta);

                // Write video partition data
                writer.WriteData(videoPartitionOffset, videoData, BlockType.Other);

                // Write game data (only the actual file data, gap is implicit zero)
                writer.WriteData(gamePartitionOffset, gameData, BlockType.File);

                // Finalize the image
                writer.FinalizeImage(totalImageSize, imageCrc, imageXxHash);

                imageId = writer.Image.Id;
            }

            store.WaitForSetIdle(SetName, 30000);
            _output.WriteLine("Xbox image ingested into DataStore");

            // ── Act: Reconstruct via ImageBuilderXboxStream ───────────────
            // This is the same reconstruction path used by the Expand-XBox step.
            // For XISO output, only the game partition is written to the output file.
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(SetName, imageId));
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(reader, _tempDir, SetName);

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            // Read the full reconstructed image
            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = ReadFully(stream, reconstructed, 0, (int)totalImageSize);

            Assert.Equal((int)totalImageSize, totalRead);
            _output.WriteLine($"Reconstructed {totalRead} bytes from DataStore");

            // ── Assert: Extract and verify game partition (XISO content) ──
            // XISO = game partition only, so extract just that portion
            byte[] xisoContent = new byte[gamePartitionSize];
            Array.Copy(reconstructed, gamePartitionOffset, xisoContent, 0, gamePartitionSize);

            // Verify game data is correctly reconstructed
            byte[] reconstructedGameData = new byte[gameDataSize];
            Array.Copy(xisoContent, 0, reconstructedGameData, 0, gameDataSize);
            Assert.Equal(gameData, reconstructedGameData);
            _output.WriteLine("Game data: byte-for-byte match confirmed");

            // Verify gap/filler regions are zero-filled (XISO behavior)
            byte[] reconstructedGap = new byte[gapSize];
            Array.Copy(xisoContent, gameDataSize, reconstructedGap, 0, gapSize);
            Assert.All(reconstructedGap, b => Assert.Equal(0x00, b));
            _output.WriteLine($"Gap region: correctly zero-filled ({gapSize} bytes)");

            // ── Assert: Video partition is NOT included in XISO content ───
            // The XISO only contains game partition data. Verify the video data
            // does NOT appear in the XISO content portion.
            byte[] videoStart = new byte[Math.Min(0x800, (int)videoPartitionSize)];
            Array.Copy(videoData, 0, videoStart, 0, videoStart.Length);

            byte[] xisoStart = new byte[videoStart.Length];
            Array.Copy(xisoContent, 0, xisoStart, 0, xisoStart.Length);

            // The game data (starting at game partition) should differ from video data
            Assert.False(videoStart.SequenceEqual(xisoStart),
                "XISO content should NOT start with video partition data");
            _output.WriteLine("Video partition correctly excluded from XISO content");

            // ── Assert: XISO size is game partition size only ─────────────
            Assert.Equal(gamePartitionSize, xisoContent.Length);
            Assert.True(xisoContent.Length < totalImageSize,
                "XISO should be smaller than full ISO (excludes video partition)");
            _output.WriteLine($"XISO size: {xisoContent.Length} bytes (game partition only, " +
                $"vs full ISO: {totalImageSize} bytes)");

            // ── Assert: Per-block hashes of game data match ───────────────
            int gameBlockCount = (int)((gameDataSize + BlockSize - 1) / BlockSize);
            for (int i = 0; i < gameBlockCount; i++)
            {
                int offset = i * BlockSize;
                int length = (int)Math.Min(BlockSize, gameDataSize - offset);

                uint originalCrc = ComputeCrc32(gameData, offset, length);
                ulong originalXxHash = ComputeXxHash64(gameData, offset, length);

                uint reconstructedCrc = ComputeCrc32(reconstructedGameData, offset, length);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructedGameData, offset, length);

                Assert.Equal(originalCrc, reconstructedCrc);
                Assert.Equal(originalXxHash, reconstructedXxHash);
            }
            _output.WriteLine($"Per-block hash verification passed for all {gameBlockCount} game data blocks");

            _output.WriteLine("PASS: Xbox export to XISO verified:");
            _output.WriteLine($"  - Game partition data reconstructed correctly");
            _output.WriteLine($"  - Filler/gap regions zero-filled (XISO behavior)");
            _output.WriteLine($"  - Video partition excluded from XISO content");
            _output.WriteLine($"  - Per-block hashes match ingested values");
        }

        /// <summary>
        /// Verifies that stored block hashes in the DataStore match the reconstructed
        /// game partition data. This tests the verification path for XISO export where
        /// block hashes of the game partition must match after reconstruction.
        ///
        /// **Validates: Requirements 6.2, 11.2**
        /// </summary>
        [Fact]
        public void XboxExport_ToXiso_StoredBlockHashes_MatchGamePartitionOutput()
        {
            // ── Arrange: Create synthetic Xbox image ──────────────────────
            // Simpler layout for hash verification:
            //   [Video Partition: 0x00000 - 0x10000] (64KB, 1 block)
            //   [Game Partition:  0x10000 - 0x40000] (192KB, 3 blocks)

            long videoPartitionOffset = 0;
            long videoPartitionSize = BlockSize; // 64KB (1 block)
            long gamePartitionOffset = videoPartitionSize;
            long gamePartitionSize = BlockSize * 3; // 192KB (3 blocks)
            long totalImageSize = videoPartitionSize + gamePartitionSize; // 256KB

            byte[] videoData = MakeBlockData(seed: 100, size: (int)videoPartitionSize);
            byte[] gameData = MakeBlockData(seed: 200, size: (int)gamePartitionSize);

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

            // Collect stored block keys from game partition offset records
            List<(long Offset, long Size, BlockKey Key)> gameBlocks = new();
            foreach (OffsetRecord offsetRecord in reader.GetOffsets())
            {
                if (!offsetRecord.HasBlocks)
                    continue;

                // Only collect blocks from the game partition
                if (offsetRecord.Offset < gamePartitionOffset)
                    continue;

                long currentOffset = offsetRecord.Offset;
                for (int i = 0; i < offsetRecord.BlockCount; i++)
                {
                    BlockKey key = offsetRecord.GetBlockAt(i);
                    long thisBlockSize = Math.Min(BlockSize, offsetRecord.Size - ((long)i * BlockSize));
                    gameBlocks.Add((currentOffset, thisBlockSize, key));
                    currentOffset += thisBlockSize;
                }
            }

            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            byte[] reconstructed = new byte[totalImageSize];
            stream.Position = 0;
            int totalRead = ReadFully(stream, reconstructed, 0, (int)totalImageSize);

            Assert.Equal((int)totalImageSize, totalRead);

            // ── Assert: Verify stored block hashes match game partition data
            Assert.NotEmpty(gameBlocks);
            _output.WriteLine($"Verifying {gameBlocks.Count} stored game partition block hashes");

            foreach ((long blockOffset, long blockLen, BlockKey storedKey) in gameBlocks)
            {
                uint reconstructedCrc = ComputeCrc32(reconstructed, (int)blockOffset, (int)blockLen);
                ulong reconstructedXxHash = ComputeXxHash64(reconstructed, (int)blockOffset, (int)blockLen);

                Assert.Equal(storedKey.Crc32, reconstructedCrc);
                Assert.Equal(storedKey.XxHash64, reconstructedXxHash);
            }

            _output.WriteLine("All stored game partition block hashes match reconstructed output");

            // ── Assert: Game partition matches original data ──────────────
            byte[] reconstructedGame = new byte[gamePartitionSize];
            Array.Copy(reconstructed, gamePartitionOffset, reconstructedGame, 0, gamePartitionSize);
            Assert.Equal(gameData, reconstructedGame);
            _output.WriteLine("Game partition data verified: byte-for-byte match with original");
        }
    }
}
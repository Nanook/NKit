#nullable enable
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS.Aux
{
    /// <summary>
    /// Integration test for Xbox DataStore round-trip with aux mode.
    /// Exercises the full ingestion → reconstruction pipeline with aux mode enabled.
    /// Filler/junk data is routed to the aux store during ingestion and restored from
    /// aux blocks during reconstruction.
    ///
    /// **Validates: Requirements 9.1, 9.5, 10.3, 10.8**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Aux")]
    public class XboxDataStoreAuxRoundTripIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private const int BlockSize = 0x10000; // 64KB block size
        private const long ShardSize = 50L * 1024 * 1024; // 50MB shard (small for testing)
        private const string PrimarySetName = "XBox";
        private const string AuxSetName = "XBox.aux";
        private const string ImageName = "TestXboxImage";

        public XboxDataStoreAuxRoundTripIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "NKit_XboxAuxIntegration_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        /// <summary>
        /// Full round-trip integration test with aux mode:
        /// 1. Creates a synthetic Xbox image with a video partition and a game partition
        /// 2. The game partition contains game data blocks and filler/junk data
        /// 3. Ingests via DataStore API: game data → primary, filler → aux
        /// 4. Reconstructs via ImageBuilderXboxStream with auxBlockProvider
        /// 5. Verifies filler regions are restored byte-for-byte from aux blocks
        /// 6. Verifies per-block XxHash64 and CRC32 hashes match ingested values
        ///
        /// **Validates: Requirements 9.1, 9.5, 10.3, 10.8**
        /// </summary>
        [Fact]
        public void XboxRoundTrip_WithAuxMode_FillerRestoredFromAux_HashesMatch()
        {
            // === ARRANGE: Create synthetic Xbox image data ===
            // Layout:
            //   [0x00000 - 0x10000) = Video partition (Other) - 64KB
            //   [0x10000 - 0x50000) = Game partition (FileSystem) - 256KB
            //     Game data:  [0x10000 - 0x30000) = 128KB of game file data
            //     Filler:     [0x30000 - 0x50000) = 128KB of filler/junk data (routed to aux)

            long videoPartitionOffset = 0x00000;
            long videoPartitionSize = 0x10000; // 64KB
            long gamePartitionOffset = 0x10000;
            long gamePartitionSize = 0x40000; // 256KB
            long gameDataOffset = 0x10000;
            long gameDataSize = 0x20000; // 128KB
            long fillerOffset = 0x30000;
            long fillerSize = 0x20000; // 128KB
            long totalImageSize = videoPartitionSize + gamePartitionSize; // 320KB
            int sectionSize = (int)totalImageSize; // Use total image size as section size for simplicity

            // Generate deterministic test data
            Random rng = new Random(42);
            byte[] videoData = new byte[videoPartitionSize];
            rng.NextBytes(videoData);

            byte[] gameData = new byte[gameDataSize];
            rng.NextBytes(gameData);

            byte[] fillerData = new byte[fillerSize];
            rng.NextBytes(fillerData);
            // Ensure filler has non-zero bytes (so we can verify it's not just zero-fill)
            fillerData[0] = 0xAB;
            fillerData[fillerData.Length - 1] = 0xCD;

            // === ACT: Ingest into DataStore (simulating what DataStoreXboxFormatter does) ===
            using (DataStore dataStore = new DataStore(_tempDir))
            {
                // Create primary and aux sets
                dataStore.CreateSet(PrimarySetName, ShardSize, BlockSize);
                dataStore.CreateSet(AuxSetName, ShardSize, BlockSize);

                // --- Write to PRIMARY set ---
                using (IImageWriter primaryWriter = dataStore.AddImage(PrimarySetName, ImageName, "XBox", ImageFormat.Iso))
                {
                    // Create video partition area (Other)
                    AreaMetadata videoMeta = new AreaMetadata();
                    videoMeta.Set(AreaValueType.FsType, "Other");
                    videoMeta.Set(AreaValueType.BlockSize, 0x800L);
                    videoMeta.Set(AreaValueType.AreaOffsetBase, videoPartitionOffset);
                    primaryWriter.CreateArea(videoPartitionOffset, videoPartitionSize, 0, 0, sectionSize, videoMeta);

                    // Create game partition area (FileSystem)
                    AreaMetadata gameMeta = new AreaMetadata();
                    gameMeta.Set(AreaValueType.FsType, "FileSystem");
                    gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                    gameMeta.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                    gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                    primaryWriter.CreateArea(gamePartitionOffset, gamePartitionSize, 0, 0, sectionSize, gameMeta);

                    // Write video partition data to primary as BlockType.Other
                    primaryWriter.WriteData(videoPartitionOffset, videoData, BlockType.Other);

                    // Write game data to primary as BlockType.FileSystem
                    primaryWriter.WriteData(gameDataOffset, gameData, BlockType.FileSystem);

                    // The filler region in primary has no data written — it will be a gap.
                    // The ImageBuilder will look for BlockType.Other offset records to overlay
                    // onto gap regions. Those records exist in the aux store.

                    // Finalize primary image
                    primaryWriter.FinalizeImage(totalImageSize, 0, 0);
                }

                // --- Write to AUX set ---
                using (IImageWriter auxWriter = dataStore.AddImage(AuxSetName, ImageName, "XBox", ImageFormat.Iso))
                {
                    // Mirror area records to aux (same as formatter does)
                    AreaMetadata videoMeta = new AreaMetadata();
                    videoMeta.Set(AreaValueType.FsType, "Other");
                    videoMeta.Set(AreaValueType.BlockSize, 0x800L);
                    videoMeta.Set(AreaValueType.AreaOffsetBase, videoPartitionOffset);
                    auxWriter.CreateArea(videoPartitionOffset, videoPartitionSize, 0, 0, sectionSize, videoMeta);

                    AreaMetadata gameMeta = new AreaMetadata();
                    gameMeta.Set(AreaValueType.FsType, "FileSystem");
                    gameMeta.Set(AreaValueType.BlockSize, 0x800L);
                    gameMeta.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                    gameMeta.Set(AreaValueType.TitleKeyMissing, true);
                    auxWriter.CreateArea(gamePartitionOffset, gamePartitionSize, 0, 0, sectionSize, gameMeta);

                    // Write filler data to aux as BlockType.Other at the filler offset
                    auxWriter.WriteData(fillerOffset, fillerData, BlockType.Other);

                    // Finalize aux image
                    auxWriter.FinalizeImage(totalImageSize, 0, 0);
                }

                // Wait for background compression to complete
                dataStore.WaitForSetIdle(PrimarySetName);
                dataStore.WaitForSetIdle(AuxSetName);
            }

            // === ACT: Reconstruct via ImageBuilderXboxStream with aux block provider ===
            byte[] reconstructedOutput;
            using (DataStore dataStore = new DataStore(_tempDir))
            {
                // Open primary image reader
                List<ImageRecord> primaryImages = dataStore.ListImagesInSet(PrimarySetName);
                Assert.Single(primaryImages);
                ImageRecord primaryImage = primaryImages[0];

                using IImageReader primaryReader = dataStore.OpenImageReader(new GlobalImageKey(PrimarySetName, primaryImage.Id));

                // Open aux image reader
                List<ImageRecord> auxImages = dataStore.ListImagesInSet(AuxSetName);
                Assert.Single(auxImages);
                ImageRecord auxImage = auxImages[0];

                using IImageReader auxReader = dataStore.OpenImageReader(new GlobalImageKey(AuxSetName, auxImage.Id));

                // Create block provider chain: primary → aux fallback
                // This mirrors what DataStore.CreateBlockProvider does
                IBlockProvider primaryProvider = new ReaderBlockProviderWrapper(primaryReader);
                IBlockProvider auxBlockProvider = new AuxBlockProvider(primaryProvider, auxReader);

                // Construct ImageBuilderXboxStream with aux block provider
                using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(primaryReader, blockProvider: auxBlockProvider, auxBlockProvider: auxBlockProvider);

                // Read the entire reconstructed image
                reconstructedOutput = new byte[totalImageSize];
                stream.Position = 0;
                int totalRead = 0;
                while (totalRead < totalImageSize)
                {
                    int bytesRead = stream.Read(reconstructedOutput, totalRead, (int)(totalImageSize - totalRead));
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }

                Assert.Equal((int)totalImageSize, totalRead);
            }

            // === ASSERT: Verify video partition data matches byte-for-byte ===
            byte[] reconstructedVideo = new byte[videoPartitionSize];
            Array.Copy(reconstructedOutput, videoPartitionOffset, reconstructedVideo, 0, videoPartitionSize);
            Assert.Equal(videoData, reconstructedVideo);

            // === ASSERT: Verify game data matches byte-for-byte ===
            byte[] reconstructedGameData = new byte[gameDataSize];
            Array.Copy(reconstructedOutput, gameDataOffset, reconstructedGameData, 0, gameDataSize);
            Assert.Equal(gameData, reconstructedGameData);

            // === ASSERT: Verify filler regions are restored byte-for-byte from aux blocks ===
            byte[] reconstructedFiller = new byte[fillerSize];
            Array.Copy(reconstructedOutput, fillerOffset, reconstructedFiller, 0, fillerSize);
            Assert.Equal(fillerData, reconstructedFiller);

            // === ASSERT: Verify per-block XxHash64 and CRC32 hashes match ===
            // Compute hashes per 0x800 sector (Xbox sector size) and verify they match
            VerifyBlockHashes(videoData, reconstructedVideo, "Video partition");
            VerifyBlockHashes(gameData, reconstructedGameData, "Game data");
            VerifyBlockHashes(fillerData, reconstructedFiller, "Filler data (from aux)");
        }

        /// <summary>
        /// Verifies that per-block XxHash64 and CRC32 hashes match between original and reconstructed data.
        /// Uses 0x800 (2048) byte sectors which is the Xbox sector size.
        /// </summary>
        private static void VerifyBlockHashes(byte[] original, byte[] reconstructed, string regionName)
        {
            Assert.Equal(original.Length, reconstructed.Length);

            int sectorSize = 0x800; // Xbox sector size
            int sectorCount = (original.Length + sectorSize - 1) / sectorSize;

            for (int i = 0; i < sectorCount; i++)
            {
                int offset = i * sectorSize;
                int length = Math.Min(sectorSize, original.Length - offset);

                // Compute CRC32 for both blocks
                uint originalCrc = NKitDataStore.Crc.Compute(original, offset, length);
                uint reconstructedCrc = NKitDataStore.Crc.Compute(reconstructed, offset, length);
                Assert.True(originalCrc == reconstructedCrc,
                    $"{regionName}: CRC32 mismatch at sector {i} (offset 0x{offset:X}). " +
                    $"Expected 0x{originalCrc:X8}, got 0x{reconstructedCrc:X8}");

                // Compute XxHash64 for both blocks
                ulong originalXxHash = Nanook.NKit.XXHash64.Compute(original, offset, length);
                ulong reconstructedXxHash = Nanook.NKit.XXHash64.Compute(reconstructed, offset, length);
                Assert.True(originalXxHash == reconstructedXxHash,
                    $"{regionName}: XxHash64 mismatch at sector {i} (offset 0x{offset:X}). " +
                    $"Expected 0x{originalXxHash:X16}, got 0x{reconstructedXxHash:X16}");
            }
        }

        /// <summary>
        /// Wrapper around IImageReader that implements IBlockProvider for the primary reader.
        /// This is needed because the internal ReaderBlockProvider is not directly constructable
        /// from tests without going through DataStore.CreateBlockProvider.
        /// </summary>
        private class ReaderBlockProviderWrapper : IBlockProvider
        {
            private readonly IImageReader _reader;

            public ReaderBlockProviderWrapper(IImageReader reader)
            {
                _reader = reader;
            }

            public BlockRecord? GetBlock(BlockKey key) => _reader.GetBlock(key);

            public System.Threading.Tasks.Task<BlockRecord?> GetBlockAsync(BlockKey key) =>
                System.Threading.Tasks.Task.FromResult(_reader.GetBlock(key));

            public BlockRecord? GetBlock(OffsetRecord record, int blockIndex)
            {
                BlockKey key = record.GetBlockAt(blockIndex);
                return _reader.GetBlock(key);
            }
        }
    }
}
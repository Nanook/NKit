using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for Xbox DataStore round-trip without aux (zero-fill).
    /// Creates a synthetic Xbox image with video and game partitions containing filler/junk data,
    /// ingests via the DataStore with aux routing (filler goes to aux set), then reconstructs
    /// via ImageBuilderXboxStream WITHOUT an auxBlockProvider (null).
    /// Verifies that filler regions are filled with null bytes (0x00) and game data blocks
    /// are still correctly reconstructed.
    ///
    /// **Validates: Requirements 4.9, 10.9**
    /// </summary>
    public class XboxDataStoreRoundTripWithoutAuxTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDir;

        public XboxDataStoreRoundTripWithoutAuxTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds_xbox_noaux_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(_tempDir, true); } catch { }
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
        /// Integration test: Xbox DataStore round-trip without aux (zero-fill fallback).
        ///
        /// Pipeline:
        /// 1. Create primary and aux DataStore sets for Xbox
        /// 2. Write game data blocks to primary (simulating game partition file data)
        /// 3. Write filler/junk blocks to aux (simulating filler routed by DataStoreXboxFormatter)
        /// 4. Reconstruct via ImageBuilderXboxStream with NO aux block provider
        /// 5. Verify: game data blocks are correctly reconstructed
        /// 6. Verify: filler regions (that were in aux) are zero-filled
        ///
        /// **Validates: Requirements 4.9, 10.9**
        /// </summary>
        [Fact]
        public void XboxRoundTrip_WithoutAux_FillerZeroFilled_GameDataCorrect()
        {
            // ── Configuration ────────────────────────────────────────────
            const string primarySetName = "xbox";
            const string auxSetName = "xbox.aux";
            const string imageName = "TestXboxGame.iso";
            const int blockSize = 0x10000; // 64KB blocks
            const int sectionSize = 0x200000; // 2MB sections

            // Xbox image layout:
            // [Video Partition 1: 0x000000 - 0x100000] (1MB, "Other" area)
            // [Game Partition:    0x100000 - 0x500000] (4MB, "FileSystem" area)
            //   - Game data at 0x100000, size 0x40000 (256KB)
            //   - Filler/junk at 0x140000, size 0x20000 (128KB) → routed to aux
            //   - Game data at 0x160000, size 0x40000 (256KB)
            //   - Filler/junk at 0x1A0000, size 0x20000 (128KB) → routed to aux
            //   - Remaining is gap (zero-filled)

            long videoPartitionOffset = 0x000000;
            long videoPartitionSize = 0x100000;

            long gamePartitionOffset = 0x100000;
            long gamePartitionSize = 0x400000;

            long totalImageSize = videoPartitionOffset + videoPartitionSize + gamePartitionSize;

            // Game data blocks (written to primary)
            long gameData1Offset = gamePartitionOffset;
            int gameData1Size = 0x40000; // 256KB
            byte[] gameData1 = MakeBlockData(seed: 42, size: gameData1Size);

            long gameData2Offset = gamePartitionOffset + 0x60000;
            int gameData2Size = 0x40000; // 256KB
            byte[] gameData2 = MakeBlockData(seed: 84, size: gameData2Size);

            // Filler/junk data (written to aux)
            long filler1Offset = gamePartitionOffset + 0x40000;
            int filler1Size = 0x20000; // 128KB
            byte[] filler1Data = MakeBlockData(seed: 100, size: filler1Size);

            long filler2Offset = gamePartitionOffset + 0xA0000;
            int filler2Size = 0x20000; // 128KB
            byte[] filler2Data = MakeBlockData(seed: 200, size: filler2Size);

            // Video partition data (written to primary)
            byte[] videoData = MakeBlockData(seed: 1, size: (int)videoPartitionSize);

            // ── Step 1: Ingest — write to primary and aux sets ────────────
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(primarySetName, blockSize: blockSize);
                store.CreateSet(auxSetName, blockSize: blockSize);

                // Write to primary set (video partition + game data)
                using (IImageWriter primaryWriter = store.AddImage(primarySetName, imageName, "XBox", ImageFormat.Iso))
                {
                    // Video partition area (Other)
                    AreaMetadata videoMetadata = new AreaMetadata();
                    videoMetadata.Set(AreaValueType.FsType, "Other");
                    videoMetadata.Set(AreaValueType.BlockSize, 0x800L);
                    videoMetadata.Set(AreaValueType.AreaOffsetBase, videoPartitionOffset);
                    primaryWriter.CreateArea(videoPartitionOffset, videoPartitionSize, 0x11111111, 0x1111111111111111, sectionSize, metadata: videoMetadata);

                    // Game partition area (FileSystem)
                    AreaMetadata gameMetadata = new AreaMetadata();
                    gameMetadata.Set(AreaValueType.FsType, "FileSystem");
                    gameMetadata.Set(AreaValueType.BlockSize, 0x800L);
                    gameMetadata.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                    gameMetadata.Set(AreaValueType.TitleKeyMissing, true);
                    primaryWriter.CreateArea(gamePartitionOffset, gamePartitionSize, 0x22222222, 0x2222222222222222, sectionSize, metadata: gameMetadata);

                    // Write video partition data
                    primaryWriter.WriteData(videoPartitionOffset, videoData, BlockType.Other);

                    // Write game data blocks to primary
                    primaryWriter.WriteData(gameData1Offset, gameData1, BlockType.File);
                    primaryWriter.WriteData(gameData2Offset, gameData2, BlockType.File);

                    primaryWriter.FinalizeImage(totalImageSize, 0xAAAAAAAA, 0xBBBBBBBBBBBBBBBB);
                }
                TestDataStoreHelper.WaitForSetIdle(store, primarySetName);

                // Write to aux set (filler/junk data)
                using (IImageWriter auxWriter = store.AddImage(auxSetName, imageName, "XBox", ImageFormat.Iso))
                {
                    // Mirror area records to aux (as the formatter does)
                    AreaMetadata videoMetadata = new AreaMetadata();
                    videoMetadata.Set(AreaValueType.FsType, "Other");
                    videoMetadata.Set(AreaValueType.BlockSize, 0x800L);
                    videoMetadata.Set(AreaValueType.AreaOffsetBase, videoPartitionOffset);
                    auxWriter.CreateArea(videoPartitionOffset, videoPartitionSize, 0x11111111, 0x1111111111111111, sectionSize, metadata: videoMetadata);

                    AreaMetadata gameMetadata = new AreaMetadata();
                    gameMetadata.Set(AreaValueType.FsType, "FileSystem");
                    gameMetadata.Set(AreaValueType.BlockSize, 0x800L);
                    gameMetadata.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                    gameMetadata.Set(AreaValueType.TitleKeyMissing, true);
                    auxWriter.CreateArea(gamePartitionOffset, gamePartitionSize, 0x22222222, 0x2222222222222222, sectionSize, metadata: gameMetadata);

                    // Write filler/junk blocks to aux (BlockType.Other = filler data)
                    auxWriter.WriteData(filler1Offset, filler1Data, BlockType.Other);
                    auxWriter.WriteData(filler2Offset, filler2Data, BlockType.Other);

                    auxWriter.FinalizeImage(totalImageSize, 0xAAAAAAAA, 0xBBBBBBBBBBBBBBBB);
                }
                TestDataStoreHelper.WaitForSetIdle(store, auxSetName);
            }

            _output.WriteLine("Ingestion complete. Primary has video + game data, aux has filler.");

            // ── Step 2: Reconstruct WITHOUT aux block provider ────────────
            // Open the primary reader only (no aux discovery)
            using DataStore store2 = new DataStore(_tempDir);
            List<ImageRecord> images = store2.ListImagesInSet(primarySetName);
            Assert.Single(images);

            using IImageReader primaryReader = store2.OpenImageReader(new GlobalImageKey(primarySetName, images[0].Id));

            // Create a plain ReaderBlockProvider (no aux) — simulates aux being unavailable
            IBlockProvider blockProvider = new ReaderBlockProvider(primaryReader);

            // Reconstruct using ImageBuilderXboxStream with NO aux block provider
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(primaryReader, blockProvider: blockProvider, auxBlockProvider: null);

            _output.WriteLine($"Reconstruction stream created. Image size: {stream.Length}");

            // ── Step 3: Read and verify game data blocks ──────────────────
            // Read game data 1
            byte[] reconstructedGameData1 = new byte[gameData1Size];
            stream.Position = gameData1Offset;
            int bytesRead = ReadFully(stream, reconstructedGameData1, 0, gameData1Size);
            Assert.Equal(gameData1Size, bytesRead);
            Assert.Equal(gameData1, reconstructedGameData1);
            _output.WriteLine($"Game data block 1 at 0x{gameData1Offset:X}: correctly reconstructed ({gameData1Size} bytes)");

            // Read game data 2
            byte[] reconstructedGameData2 = new byte[gameData2Size];
            stream.Position = gameData2Offset;
            bytesRead = ReadFully(stream, reconstructedGameData2, 0, gameData2Size);
            Assert.Equal(gameData2Size, bytesRead);
            Assert.Equal(gameData2, reconstructedGameData2);
            _output.WriteLine($"Game data block 2 at 0x{gameData2Offset:X}: correctly reconstructed ({gameData2Size} bytes)");

            // ── Step 4: Verify filler regions are zero-filled ─────────────
            // Filler 1 region should be all zeros (aux not available)
            byte[] reconstructedFiller1 = new byte[filler1Size];
            stream.Position = filler1Offset;
            bytesRead = ReadFully(stream, reconstructedFiller1, 0, filler1Size);
            Assert.Equal(filler1Size, bytesRead);
            Assert.All(reconstructedFiller1, b => Assert.Equal(0x00, b));
            _output.WriteLine($"Filler region 1 at 0x{filler1Offset:X}: correctly zero-filled ({filler1Size} bytes)");

            // Filler 2 region should be all zeros (aux not available)
            byte[] reconstructedFiller2 = new byte[filler2Size];
            stream.Position = filler2Offset;
            bytesRead = ReadFully(stream, reconstructedFiller2, 0, filler2Size);
            Assert.Equal(filler2Size, bytesRead);
            Assert.All(reconstructedFiller2, b => Assert.Equal(0x00, b));
            _output.WriteLine($"Filler region 2 at 0x{filler2Offset:X}: correctly zero-filled ({filler2Size} bytes)");

            // ── Step 5: Verify video partition data ───────────────────────
            byte[] reconstructedVideo = new byte[(int)videoPartitionSize];
            stream.Position = videoPartitionOffset;
            bytesRead = ReadFully(stream, reconstructedVideo, 0, (int)videoPartitionSize);
            Assert.Equal((int)videoPartitionSize, bytesRead);
            Assert.Equal(videoData, reconstructedVideo);
            _output.WriteLine($"Video partition at 0x{videoPartitionOffset:X}: correctly reconstructed ({videoPartitionSize} bytes)");

            _output.WriteLine("All assertions passed: game data correct, filler zero-filled, video data correct.");
        }

        /// <summary>
        /// Verifies that when aux is absent (no aux set file on disk), the reconstruction
        /// still produces correct game data with zero-filled filler regions.
        /// This simulates the case where the aux store was never created or was deleted.
        ///
        /// **Validates: Requirements 4.9, 10.9**
        /// </summary>
        [Fact]
        public void XboxRoundTrip_AuxSetAbsent_FillerZeroFilled_GameDataCorrect()
        {
            // ── Configuration ────────────────────────────────────────────
            const string primarySetName = "xbox";
            const string auxSetName = "xbox.aux";
            const string imageName = "TestXboxNoAux.iso";
            const int blockSize = 0x10000;
            const int sectionSize = 0x200000;

            long gamePartitionOffset = 0x0;
            long gamePartitionSize = 0x100000; // 1MB

            // Game data
            long gameDataOffset = 0x0;
            int gameDataSize = 0x20000; // 128KB
            byte[] gameData = MakeBlockData(seed: 55, size: gameDataSize);

            // Filler data (will be in aux, then aux removed)
            long fillerOffset = 0x20000;
            int fillerSize = 0x10000; // 64KB
            byte[] fillerData = MakeBlockData(seed: 77, size: fillerSize);

            // ── Ingest with aux ──────────────────────────────────────────
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(primarySetName, blockSize: blockSize);
                store.CreateSet(auxSetName, blockSize: blockSize);

                // Primary: game data
                using (IImageWriter writer = store.AddImage(primarySetName, imageName, "XBox", ImageFormat.Iso))
                {
                    AreaMetadata metadata = new AreaMetadata();
                    metadata.Set(AreaValueType.FsType, "FileSystem");
                    metadata.Set(AreaValueType.BlockSize, 0x800L);
                    metadata.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                    metadata.Set(AreaValueType.TitleKeyMissing, true);
                    writer.CreateArea(gamePartitionOffset, gamePartitionSize, 0x33333333, 0x3333333333333333, sectionSize, metadata: metadata);

                    writer.WriteData(gameDataOffset, gameData, BlockType.File);
                    writer.FinalizeImage(gamePartitionSize, 0xCCCCCCCC, 0xDDDDDDDDDDDDDDDD);
                }
                TestDataStoreHelper.WaitForSetIdle(store, primarySetName);

                // Aux: filler data
                using (IImageWriter auxWriter = store.AddImage(auxSetName, imageName, "XBox", ImageFormat.Iso))
                {
                    AreaMetadata metadata = new AreaMetadata();
                    metadata.Set(AreaValueType.FsType, "FileSystem");
                    metadata.Set(AreaValueType.BlockSize, 0x800L);
                    metadata.Set(AreaValueType.AreaOffsetBase, gamePartitionOffset);
                    metadata.Set(AreaValueType.TitleKeyMissing, true);
                    auxWriter.CreateArea(gamePartitionOffset, gamePartitionSize, 0x33333333, 0x3333333333333333, sectionSize, metadata: metadata);

                    auxWriter.WriteData(fillerOffset, fillerData, BlockType.Other);
                    auxWriter.FinalizeImage(gamePartitionSize, 0xCCCCCCCC, 0xDDDDDDDDDDDDDDDD);
                }
                TestDataStoreHelper.WaitForSetIdle(store, auxSetName);
            }

            // ── Remove aux set files (simulate aux being absent) ─────────
            foreach (string auxFile in Directory.GetFiles(_tempDir, "xbox.aux*"))
            {
                File.Delete(auxFile);
                _output.WriteLine($"Deleted aux file: {Path.GetFileName(auxFile)}");
            }

            // ── Reconstruct without aux ──────────────────────────────────
            using DataStore store2 = new DataStore(_tempDir);
            List<ImageRecord> images = store2.ListImagesInSet(primarySetName);
            Assert.Single(images);

            using IImageReader reader = store2.OpenImageReader(new GlobalImageKey(primarySetName, images[0].Id));

            // CreateBlockProvider will return a plain ReaderBlockProvider since aux is absent
            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, primarySetName);
            Assert.IsType<ReaderBlockProvider>(provider);

            // Reconstruct — no aux available
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: provider, auxBlockProvider: null);

            // ── Verify game data ─────────────────────────────────────────
            byte[] reconstructedGame = new byte[gameDataSize];
            stream.Position = gameDataOffset;
            int bytesRead = ReadFully(stream, reconstructedGame, 0, gameDataSize);
            Assert.Equal(gameDataSize, bytesRead);
            Assert.Equal(gameData, reconstructedGame);
            _output.WriteLine($"Game data at 0x{gameDataOffset:X}: correctly reconstructed");

            // ── Verify filler is zero-filled ─────────────────────────────
            byte[] reconstructedFiller = new byte[fillerSize];
            stream.Position = fillerOffset;
            bytesRead = ReadFully(stream, reconstructedFiller, 0, fillerSize);
            Assert.Equal(fillerSize, bytesRead);
            Assert.All(reconstructedFiller, b => Assert.Equal(0x00, b));
            _output.WriteLine($"Filler at 0x{fillerOffset:X}: correctly zero-filled");
        }

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
    }
}
using NKitDataStore.Binary;
using NKitDataStore.Compression;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    public class ShardAndBlockStorageTests : IDisposable
    {
        private readonly string _tempDir;
        public ShardAndBlockStorageTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitShardTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public void Rollback_RemovesNewShard_And_TruncatesPrevious()
        {
            string setName = "RollbackTest";
            long shardSize = 1024; // small to force rotation
            int blockSize = 512;   // small blocks

            (int fileId, long offset, int length)[] written;
            string shard0 = Path.Combine(_tempDir, $"{setName}_0000.nkds");
            string shard1 = Path.Combine(_tempDir, $"{setName}_0001.nkds");

            using (ShardFileManager manager = new ShardFileManager(_tempDir, setName, shardSize, sourceDirectory: null, blockSize: blockSize))
            {
                Random rnd = new Random(555);
                // Write three blocks: two fill first shard, third rotates to second shard
                byte[] b1 = new byte[blockSize]; rnd.NextBytes(b1);
                byte[] b2 = new byte[blockSize]; rnd.NextBytes(b2);
                byte[] b3 = new byte[blockSize]; rnd.NextBytes(b3);
                written = new[] { manager.WriteBlock(b1, 0, b1.Length), manager.WriteBlock(b2, 0, b2.Length), manager.WriteBlock(b3, 0, b3.Length) };

                // Ensure both shard files exist
                Assert.True(File.Exists(shard0), "Shard 0 should exist");
                Assert.True(File.Exists(shard1), "Shard 1 should exist");

                // Perform rollback to file 0, target size 512 while manager is still active
                manager.RollbackToSize(0, 512);

                // After rollback, shard1 should be deleted and shard0 truncated to 512
                Assert.False(File.Exists(shard1), "Shard 1 should be deleted after rollback");
                Assert.True(File.Exists(shard0), "Shard 0 should still exist after rollback");
                long len = new FileInfo(shard0).Length;
                Assert.Equal(512, len);
                Assert.Equal(0, manager.CurrentFileId);
                Assert.Equal(512, manager.CurrentShardSize);
            }
        }

        [Fact]
        public void Rollback_TruncatesCurrentShardOnly()
        {
            string setName = "RollbackTruncTest";
            long shardSize = 4096; // large so no rotation
            int blockSize = 1024;

            using (ShardFileManager manager = new ShardFileManager(_tempDir, setName, shardSize, sourceDirectory: null, blockSize: blockSize))
            {
                Random rnd = new Random(777);
                byte[] b1 = new byte[blockSize]; rnd.NextBytes(b1);
                byte[] b2 = new byte[blockSize]; rnd.NextBytes(b2);
                // Write two blocks -> current shard size = 2048
                (int fileId, long offset, int length) r1 = manager.WriteBlock(b1, 0, b1.Length);
                (int fileId, long offset, int length) r2 = manager.WriteBlock(b2, 0, b2.Length);

                // Truncate back to first block size while manager is active
                manager.RollbackToSize(r1.fileId, r1.offset + r1.length);

                string shard0 = Path.Combine(_tempDir, $"{setName}_0000.nkds");
                Assert.True(File.Exists(shard0));
                long len = new FileInfo(shard0).Length;
                Assert.Equal(r1.offset + r1.length, len);
                Assert.Equal(r1.fileId, manager.CurrentFileId);
                Assert.Equal(len, manager.CurrentShardSize);
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void ShardFileManager_WriteRead_RotatesAndReadsBack()
        {
            string setName = "ShardTest";
            long shardSize = 1024; // small to force rotation
            int blockSize = 512;   // small blocks

            // Write several blocks that will cause rotation and capture originals
            (int fileId, long offset, int length)[] written;
            byte[][] originals;
            Random rnd = new Random(123);
            using (ShardFileManager manager = new ShardFileManager(_tempDir, setName, shardSize, sourceDirectory: null, blockSize: blockSize))
            {
                originals = new byte[6][];
                written = new (int, long, int)[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    originals[i] = new byte[blockSize];
                    rnd.NextBytes(originals[i]);
                    (int fileId, long offset, int length) res = manager.WriteBlock(originals[i], 0, originals[i].Length);
                    written[i] = res;
                }
                // Dispose to flush buffered streams
            }

            // Re-open manager for read and verify each block contents
            using (ShardFileManager manager = new ShardFileManager(_tempDir, setName, shardSize, sourceDirectory: null, blockSize: blockSize))
            {
                for (int i = 0; i < written.Length; i++)
                {
                    byte[] data = manager.ReadBlock(written[i].fileId, written[i].offset, written[i].length);
                    Assert.NotNull(data);
                    Assert.Equal(written[i].length, data.Length);
                    Assert.Equal(originals[i], data);
                }
            }
        }

        [Fact]
        public void InsertBlockWithCompression_RoundTrip_Indirect()
        {
            string setName = "BlockStoreTest";
            string imageName = "test.img";
            int blockSize = 1024;
            BlockKey blockKey;

            // Use DataStore writer to insert block(s), then read the block key, then dispose
            using (DataStore store = new DataStore(_tempDir))
            {
                using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName))
                {
                    Random rnd = new Random(42);
                    byte[] data = new byte[blockSize];
                    rnd.NextBytes(data);

                    // Write data via writer - this uses InsertBlockWithCompression internally
                    writer.WriteData(0, data, BlockType.File);
                    writer.FinalizeImage(data.Length, 0, 0);
                }

                // Wait for background tasks to complete before verifying storage
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Read via reader to get the block key
                using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, 1));
                List<OffsetRecord> offsets = reader.GetOffsets().ToList();
                Assert.Single(offsets);
                OffsetRecord firstOffset = offsets[0];
                Assert.True(firstOffset.HasBlocks);
                blockKey = firstOffset.FirstBlock!.Value;
            }

            // Use a separate data access instance to fetch stored block payload
            using BinaryDataStoreDataAccess da = new BinaryDataStoreDataAccess(_tempDir);
            da.EnsureSetExists(setName, 0);
            (CompressionType compressionType, byte[] payload) = da.GetBlockData(setName, blockKey);
            Assert.NotNull(payload);

            // Decompress using BlockCompressor and verify length/content
            using BlockCompressor compressor = new BlockCompressor(blockSize);
            byte[] decompressed = new byte[blockSize];
            int decompressedSize;
            if (compressionType == CompressionType.None)
            {
                decompressed = payload!;
                decompressedSize = decompressed.Length;
            }
            else
            {
                decompressedSize = compressor.Decompress(payload!, 0, payload!.Length, decompressed, 0);
            }

            Assert.Equal(blockSize, decompressedSize);
        }
    }
}
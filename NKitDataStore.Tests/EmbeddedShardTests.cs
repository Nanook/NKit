using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    public class EmbeddedShardTests : IDisposable
    {
        private readonly string _tempDir;

        public EmbeddedShardTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitEmbeddedTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void ShardSizeZero_PackedDatabase_StoresDbInSingleFile_And_ReadBackWorks()
        {
            const string setName = "EmbeddedSet";
            const string imageName = "EmbeddedImage";

            // Arrange - create a datastore and a set with shardSize = 0 (embedded DB)
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet(setName, shardSize: 0, blockSize: 65536);

            // Prepare data (2 blocks)
            byte[] original = new byte[131072];
            Random rnd = new Random(12345);
            rnd.NextBytes(original);

            // Act - write the image
            using (IImageWriter writer = store.AddImage(setName, imageName))
            {
                writer.WriteData(0, original, BlockType.File);
                writer.FinalizeImage(original.Length, 0x12345678, 0xABCDEF0123456789);
            }

            // Wait for background tasks/flush to complete
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Assert - main database file exists and no separate shard files should exist
            string main = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            Assert.True(File.Exists(main), "Main embedded database file should exist");

            string[] shardFiles = Directory.GetFiles(_tempDir, $"{setName}_*.nkds");
            Assert.Empty(shardFiles);

            // Act - read back via DataStore reader (this will extract embedded DB internally)
            GlobalImageKey imageKey = new GlobalImageKey(setName, 1);
            byte[] readData = new byte[original.Length];
            using (IImageReader reader = store.OpenImageReader(imageKey))
            using (Stream stream = reader.OpenStream(0))
            {
                int totalRead = 0;
                while (totalRead < readData.Length)
                {
                    int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                Assert.Equal(original.Length, totalRead);
            }

            Assert.Equal(original, readData);
        }

        [Fact]
        public void ShardSizeZero_WriteAndReadFromSeparateInstances_Works()
        {
            const string setName = "EmbeddedSet";
            const string imageName = "EmbeddedImage";

            // Prepare data (2 blocks worth of random data to ensure compression)
            byte[] original = new byte[131072];
            Random rnd = new Random(12345);
            rnd.NextBytes(original);

            // Write with one DataStore instance
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: 0, blockSize: 65536);

                using (IImageWriter writer = store.AddImage(setName, imageName))
                {
                    writer.WriteData(0, original, BlockType.File);
                    writer.FinalizeImage(original.Length, 0x12345678, 0xABCDEF0123456789);
                }

                TestDataStoreHelper.WaitForSetIdle(store, setName);
            }

            // Verify the embedded file exists
            string main = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            Assert.True(File.Exists(main), "Main embedded database file should exist");

            // Read with a completely new DataStore instance (simulates verify step)
            using (DataStore store2 = new DataStore(_tempDir))
            {
                GlobalImageKey imageKey = new GlobalImageKey(setName, 1);
                byte[] readData = new byte[original.Length];
                using (IImageReader reader = store2.OpenImageReader(imageKey))
                using (Stream stream = reader.OpenStream(0))
                {
                    int totalRead = 0;
                    while (totalRead < readData.Length)
                    {
                        int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    Assert.Equal(original.Length, totalRead);
                }

                Assert.Equal(original, readData);
            }
        }

        [Fact]
        public void ShardSizeZero_WriteAndReadLooseFile_FromSeparateInstances_Works()
        {
            const string setName = "EmbeddedSet";
            const string imageName = "EmbeddedImage";

            // Prepare block data and a loose file
            byte[] blockData = new byte[65536];
            Random rnd = new Random(99999);
            rnd.NextBytes(blockData);

            byte[] looseFileData = Encoding.UTF8.GetBytes("Hello, this is a test system file!");

            // Write with one DataStore instance
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: 0, blockSize: 65536);

                using (IImageWriter writer = store.AddImage(setName, imageName))
                {
                    writer.WriteData(0, blockData, BlockType.File);
                    writer.WriteFile("system.bin", looseFileData, isSystem: true);
                    writer.FinalizeImage(blockData.Length, 0, 0);
                }

                TestDataStoreHelper.WaitForSetIdle(store, setName);
            }

            // Read with a completely new DataStore instance
            using (DataStore store2 = new DataStore(_tempDir))
            {
                GlobalImageKey imageKey = new GlobalImageKey(setName, 1);
                using (IImageReader reader = store2.OpenImageReader(imageKey))
                {
                    // Read loose file
                    byte[] readFile = reader.ReadFile("system.bin");
                    Assert.NotNull(readFile);
                    Assert.Equal(looseFileData, readFile);

                    // Read block data via stream
                    byte[] readData = new byte[blockData.Length];
                    using (Stream stream = reader.OpenStream(0))
                    {
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }
                        Assert.Equal(blockData.Length, totalRead);
                    }
                    Assert.Equal(blockData, readData);
                }
            }
        }
    }
}
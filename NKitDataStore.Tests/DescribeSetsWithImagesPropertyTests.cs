using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for DescribeSetsWithImages always loading filesystem.yaml.
    ///
    /// Feature: mandatory-filesystem-storage, Property 3: DescribeSetsWithImages always loads filesystem.yaml
    /// </summary>
    public class DescribeSetsWithImagesPropertyTests : IDisposable
    {
        private readonly string _baseTestDirectory;

        public DescribeSetsWithImagesPropertyTests()
        {
            _baseTestDirectory = Path.Combine(Path.GetTempPath(), $"NKitDescribeFsTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_baseTestDirectory);
        }

        public void Dispose()
        {

            if (Directory.Exists(_baseTestDirectory))
            {
                try
                {
                    Thread.Sleep(50);
                    Directory.Delete(_baseTestDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.1, 2.2**
        ///
        /// Property 3: DescribeSetsWithImages always loads filesystem.yaml.
        /// For any set containing images with filesystem.yaml data, calling
        /// DescribeSetsWithImages with includeFileSystemYaml = true SHALL return
        /// non-empty FileSystemYamlData for those images. The store_fs column
        /// was removed — filesystem storage is now unconditionally on.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool DescribeSetsWithImages_AlwaysLoadsFileSystemYaml(
            NonNegativeInt setNameSeed)
        {
            string setName = $"fsSet{setNameSeed.Get}";
            string imageName = $"Image{setNameSeed.Get}.iso";

            // Create a unique subdirectory per test iteration
            string testDir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                // Use a large shard size (non-zero) so the .nkds file remains a plain
                // SQLite database. shardSize=0 triggers embedded packing which makes
                // the file unreadable by raw SQLite connections.
                long shardSize = 50L * 1024 * 1024 * 1024;

                // Step 1: Create a DataStore, add an image with filesystem.yaml data
                using (DataStore store = new DataStore(testDir))
                {
                    using (IImageWriter writer = TestDataStoreHelper.AddImage(
                        store, setName, imageName, shardSize: shardSize))
                    {
                        byte[] testData = new byte[] { 1, 2, 3, 4 };
                        writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);

                        // Build filesystem.yaml data
                        FsYaml fsYaml = new FsYaml();
                        FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                        root.AddFile("test.bin", 0, 1024, 0, 0);
                        string yaml = fsYaml.ToYaml();
                        writer.WriteFile(DataStore.FileSystemYamlRootPath, Encoding.UTF8.GetBytes(yaml));

                        writer.FinalizeImage(testData.Length, 0, 0);
                    }

                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Step 2: Reopen the DataStore and call DescribeSetsWithImages

                using (DataStore store = new DataStore(testDir))
                {
                    List<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> results = store.DescribeSetsWithImages(
                        filterSetName: setName,
                        includeFileSystemYaml: true).ToList();

                    if (results.Count != 1)
                        return false;

                    (SetInfo info, List<ImageRecord> images, Dictionary<long, byte[]> fsYamlData) = results[0];

                    // Verify we got the set and image back
                    if (info.SetName != setName)
                        return false;
                    if (images.Count != 1)
                        return false;

                    // The key property: filesystem.yaml data MUST be loaded
                    // regardless of the store_fs value in the database
                    long imageId = images[0].Id;
                    if (!fsYamlData.ContainsKey(imageId))
                        return false;

                    // Verify the data is non-empty
                    if (fsYamlData[imageId] == null || fsYamlData[imageId].Length == 0)
                        return false;

                    return true;
                }
            }
            finally
            {
                try
                {
                    Thread.Sleep(20);
                    if (Directory.Exists(testDir))
                        Directory.Delete(testDir, recursive: true);
                }
                catch { }
            }
        }
    }
}
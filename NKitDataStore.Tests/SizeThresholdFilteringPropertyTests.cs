using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for size-threshold filtering in DescribeSetsWithImages.
    ///
    /// Feature: lazy-filesystem-yaml-loading, Property 1: Size-threshold filtering partitions filesystem.yaml loading
    /// </summary>
    public class SizeThresholdFilteringPropertyTests : IDisposable
    {
        private readonly string _baseTestDirectory;

        public SizeThresholdFilteringPropertyTests()
        {
            _baseTestDirectory = Path.Combine(Path.GetTempPath(), $"NKitSizeThresholdTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_baseTestDirectory);
        }

        public void Dispose()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

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
        /// **Validates: Requirements 1.2, 1.3, 8.2**
        ///
        /// Property 1: Size-threshold filtering partitions filesystem.yaml loading.
        /// For any image with a filesystem.yaml of a given stored (compressed) size and
        /// for any non-negative threshold value, DescribeSetsWithImages() SHALL include
        /// the filesystem.yaml in the result dictionary if and only if
        /// FileRecord.Size &lt;= threshold. Images whose stored size exceeds the threshold
        /// SHALL be excluded from the dictionary.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool SizeThresholdFiltering_PartitionsFileSystemYamlLoading(
            NonNegativeInt dataSizeSeed,
            NonNegativeInt thresholdSeed)
        {
            // Generate a filesystem.yaml data size between 1 and 10000 bytes.
            // Small enough to keep tests fast, large enough to exercise the threshold logic.
            int dataSize = (dataSizeSeed.Get % 10000) + 1;

            // Generate a threshold value. We use a wide range to cover cases where
            // the threshold is well below, near, or well above the compressed size.
            long threshold = thresholdSeed.Get % 20000;

            string setName = $"threshSet";
            string imageName = $"Image.iso";

            string testDir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                long shardSize = 50L * 1024 * 1024 * 1024;

                // Step 1: Create a DataStore with an image containing filesystem.yaml data
                using (DataStore store = new DataStore(testDir))
                {
                    using (IImageWriter writer = TestDataStoreHelper.AddImage(
                        store, setName, imageName, shardSize: shardSize))
                    {
                        byte[] imageData = new byte[] { 1, 2, 3, 4 };
                        writer.WriteData(0, new MemoryStream(imageData), imageData.Length, BlockType.File);

                        // Build filesystem.yaml data of the target size
                        byte[] fsYamlBytes = CreateFsYamlDataOfSize(dataSize);
                        writer.WriteFile(DataStore.FileSystemYamlRootPath, fsYamlBytes);

                        writer.FinalizeImage(imageData.Length, 0, 0);
                    }

                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Step 2: Query the actual compressed FileRecord.Size from the DataStore API
                long actualCompressedSize = QueryFileRecordSize(testDir, setName);
                if (actualCompressedSize < 0)
                    return false; // Could not read the file record

                // Step 3: Call DescribeSetsWithImages with the threshold

                using (DataStore store = new DataStore(testDir))
                {
                    List<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> results = store.DescribeSetsWithImages(
                        filterSetName: setName,
                        includeFileSystemYaml: true,
                        maxFileSystemYamlSize: threshold).ToList();

                    if (results.Count != 1)
                        return false;

                    (SetInfo info, List<ImageRecord> images, Dictionary<long, byte[]> fsYamlData) = results[0];

                    if (info.SetName != setName || images.Count != 1)
                        return false;

                    long imageId = images[0].Id;
                    bool isIncluded = fsYamlData.ContainsKey(imageId);

                    // THE PROPERTY: filesystem.yaml is included iff FileRecord.Size <= threshold
                    bool shouldBeIncluded = actualCompressedSize <= threshold;

                    return isIncluded == shouldBeIncluded;
                }
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    Thread.Sleep(20);
                    if (Directory.Exists(testDir))
                        Directory.Delete(testDir, recursive: true);
                }
                catch { }
            }
        }

        /// <summary>
        /// Creates filesystem.yaml byte data of approximately the given size.
        /// Uses a simple YAML structure with padding to reach the target size.
        /// </summary>
        private static byte[] CreateFsYamlDataOfSize(int targetSize)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("- name: .");
            sb.AppendLine("  type: d");
            sb.AppendLine("  children:");

            // Add file entries until we reach the target size
            int index = 0;
            while (sb.Length < targetSize)
            {
                sb.AppendLine($"  - name: file{index++}.bin");
                sb.AppendLine("    type: f");
                sb.AppendLine("    size: 1024");
            }

            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());

            // Trim or pad to get close to the target size
            if (data.Length > targetSize)
            {
                byte[] trimmed = new byte[targetSize];
                Array.Copy(data, trimmed, targetSize);
                return trimmed;
            }

            return data;
        }

        /// <summary>
        /// Queries the compressed size of the filesystem.yaml FileRecord via the DataStore API.
        /// </summary>
        private static long QueryFileRecordSize(string testDir, string setName)
        {
            using DataStore store = new DataStore(testDir);
            List<ImageRecord> images = store.ListImagesInSet(setName);
            if (images.Count == 0)
                return -1;

            long imageId = images[0].Id;
            GlobalImageKey key = new GlobalImageKey(setName, imageId);
            using IImageReader reader = store.OpenImageReader(key);
            IEnumerable<FileRecord> files = reader.ListFiles();
            FileRecord fileRecord = files.FirstOrDefault(f => f.Name == DataStore.FileSystemYamlRootPath);
            if (fileRecord == null)
                return -1;

            return fileRecord.Size;
        }
    }
}
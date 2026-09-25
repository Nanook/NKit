using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration test for CueFolder open and child enumeration.
    /// Creates a DataStore with 2 CUE child images, builds a CueFolder that references them,
    /// then opens the CueFolder and verifies child images are enumerated as individual disc entries.
    ///
    /// **Validates: Requirements 10.1, 10.6**
    /// </summary>
    public class CueFolderOpenAndChildEnumerationIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public CueFolderOpenAndChildEnumerationIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(),
                $"CueFolderEnum_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Directory.Exists(_testDir))
                    Directory.Delete(_testDir, recursive: true);
            }
            catch { }
        }

        /// <summary>
        /// Creates a CueFolder from a multi-disc folder containing 2 CUE images,
        /// then opens the CueFolder and verifies child images are enumerated as
        /// individual disc entries via the ifs section of filesystem.yaml.
        ///
        /// Steps:
        /// 1. Create 2 child CUE images in the DataStore with FileName-tagged areas
        /// 2. Create a CueFolder image with filesystem.yaml referencing both child images
        /// 3. Open the CueFolder image and read its filesystem.yaml
        /// 4. Verify the ifs section contains exactly 2 entries referencing the correct child images
        ///
        /// **Validates: Requirements 10.1, 10.6**
        /// </summary>
        [Fact]
        public void CueFolder_WithTwoCueChildImages_EnumeratesChildDiscsCorrectly()
        {
            // ── Arrange: create 2 child CUE images ───────────────────────

            string storePath = Path.Combine(_testDir, "store");
            const string setName = "MultiDiscSet";
            const string disc1Name = "Game Disc 1";
            const string disc2Name = "Game Disc 2";

            long disc1ImageId;
            long disc2ImageId;

            using (DataStore store = new DataStore(storePath))
            {
                // Child image 1: 2-track CUE image
                using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, disc1Name,
                    shardSize: 0, system: "PS1", format: ImageFormat.Cue))
                {
                    byte[] trackData1 = new byte[2048];
                    new Random(42).NextBytes(trackData1);
                    writer.WriteData(0, new MemoryStream(trackData1), trackData1.Length, BlockType.File);

                    AreaMetadata meta1 = new AreaMetadata();
                    meta1.Set(AreaValueType.FileName, "track01.bin");
                    meta1.Set(AreaValueType.BlockSize, 0x930L);
                    meta1.Set(AreaValueType.Track, 1L);
                    writer.CreateArea(0, 734003280, 0x11111111, 0xAAAAAAAAAAAAAAAA, 0x200000, metadata: meta1);

                    AreaMetadata meta2 = new AreaMetadata();
                    meta2.Set(AreaValueType.FileName, "track02.bin");
                    meta2.Set(AreaValueType.BlockSize, 0x930L);
                    meta2.Set(AreaValueType.Track, 2L);
                    writer.CreateArea(734003280, 52920000, 0x22222222, 0xBBBBBBBBBBBBBBBB, 0x200000, metadata: meta2);

                    writer.FinalizeImage(734003280 + 52920000, 0, 0);
                }

                // Child image 2: 3-track CUE image
                using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, disc2Name,
                    shardSize: 0, system: "PS1", format: ImageFormat.Cue))
                {
                    byte[] trackData2 = new byte[2048];
                    new Random(43).NextBytes(trackData2);
                    writer.WriteData(0, new MemoryStream(trackData2), trackData2.Length, BlockType.File);

                    AreaMetadata meta1 = new AreaMetadata();
                    meta1.Set(AreaValueType.FileName, "track01.bin");
                    meta1.Set(AreaValueType.BlockSize, 0x930L);
                    meta1.Set(AreaValueType.Track, 1L);
                    writer.CreateArea(0, 734003280, 0x33333333, 0xCCCCCCCCCCCCCCCC, 0x200000, metadata: meta1);

                    AreaMetadata meta2 = new AreaMetadata();
                    meta2.Set(AreaValueType.FileName, "track02.bin");
                    meta2.Set(AreaValueType.BlockSize, 0x930L);
                    meta2.Set(AreaValueType.Track, 2L);
                    writer.CreateArea(734003280, 52920000, 0x44444444, 0xDDDDDDDDDDDDDDDD, 0x200000, metadata: meta2);

                    AreaMetadata meta3 = new AreaMetadata();
                    meta3.Set(AreaValueType.FileName, "track03.bin");
                    meta3.Set(AreaValueType.BlockSize, 0x930L);
                    meta3.Set(AreaValueType.Track, 3L);
                    writer.CreateArea(734003280 + 52920000, 176400000, 0x55555555, 0xEEEEEEEEEEEEEEEE, 0x200000, metadata: meta3);

                    writer.FinalizeImage(734003280 + 52920000 + 176400000, 0, 0);
                }

                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Get the image IDs for the child images
                List<ImageRecord> images = store.ListImagesInSet(setName).ToList();
                ImageRecord disc1Image = images.First(i => i.Name == disc1Name);
                ImageRecord disc2Image = images.First(i => i.Name == disc2Name);
                disc1ImageId = disc1Image.Id;
                disc2ImageId = disc2Image.Id;

                _output.WriteLine($"Child image 1: '{disc1Name}' (ID={disc1ImageId})");
                _output.WriteLine($"Child image 2: '{disc2Name}' (ID={disc2ImageId})");

                // ── Act: create CueFolder referencing both child images ──────

                const string cueFolderName = "Multi-Disc Game";

                using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, cueFolderName,
                    shardSize: 0, system: "PS1", format: ImageFormat.CueFolder))
                {
                    // Write minimal data so the image has content
                    byte[] placeholder = new byte[] { 0 };
                    writer.WriteData(0, new MemoryStream(placeholder), placeholder.Length, BlockType.File);

                    // Build filesystem.yaml with ifs section referencing child images
                    FsYaml fsYaml = new FsYaml();
                    fsYaml.AddFileSystem(".", 0);

                    // Add ifs entries for each child disc image
                    fsYaml.AddIfsEntry("Game Disc 1.cue", disc1ImageId, 734003280 + 52920000);
                    fsYaml.AddIfsEntry("Game Disc 2.cue", disc2ImageId, 734003280 + 52920000 + 176400000);

                    // Write as NkFs binary (the standard format)
                    byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();
                    writer.WriteFile(DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);

                    writer.FinalizeImage(placeholder.Length, 0, 0);
                }

                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // ── Assert: open CueFolder and verify child enumeration ──────

                // Find the CueFolder image
                List<ImageRecord> allImages = store.ListImagesInSet(setName).ToList();
                ImageRecord cueFolderImage = allImages.FirstOrDefault(i =>
                    i.Name == cueFolderName && i.Format == ImageFormat.CueFolder);

                Assert.NotNull(cueFolderImage);
                _output.WriteLine($"CueFolder: '{cueFolderImage.Name}' (ID={cueFolderImage.Id}, Format={cueFolderImage.Format})");

                // Verify the CueFolder has the correct format
                Assert.Equal(ImageFormat.CueFolder, cueFolderImage.Format);

                // Open the CueFolder and read its filesystem data
                GlobalImageKey cueFolderKey = new GlobalImageKey(setName, cueFolderImage.Id);
                byte[] nkfsData = store.ReadFile(cueFolderKey, DataStore.FileSystemNkfsRootPath);
                Assert.NotNull(nkfsData);

                // Parse the filesystem.yaml from the NkFs binary
                FsYaml parsedFsYaml = NkFs.FromBytes(nkfsData).ToFsYaml();
                Assert.NotNull(parsedFsYaml);

                _output.WriteLine($"CueFolder filesystem.yaml parsed successfully");
                _output.WriteLine($"  ifs entries: {parsedFsYaml.ImageFileSystems.Count}");

                // Verify the ifs section contains exactly 2 entries (one per child disc)
                Assert.Equal(2, parsedFsYaml.ImageFileSystems.Count);

                // Verify first child disc entry
                FsYamlIfsEntry ifsEntry1 = parsedFsYaml.ImageFileSystems[0];
                Assert.Equal("Game Disc 1.cue", ifsEntry1.FileName);
                Assert.Equal(disc1ImageId, ifsEntry1.ImageId);
                Assert.Equal(734003280L + 52920000L, ifsEntry1.Size);

                _output.WriteLine($"  ifs[0]: '{ifsEntry1.FileName}' → imageId={ifsEntry1.ImageId}, size={ifsEntry1.Size}");

                // Verify second child disc entry
                FsYamlIfsEntry ifsEntry2 = parsedFsYaml.ImageFileSystems[1];
                Assert.Equal("Game Disc 2.cue", ifsEntry2.FileName);
                Assert.Equal(disc2ImageId, ifsEntry2.ImageId);
                Assert.Equal(734003280L + 52920000L + 176400000L, ifsEntry2.Size);

                _output.WriteLine($"  ifs[1]: '{ifsEntry2.FileName}' → imageId={ifsEntry2.ImageId}, size={ifsEntry2.Size}");

                // Verify child images can be resolved from the ifs entries
                foreach (FsYamlIfsEntry ifsEntry in parsedFsYaml.ImageFileSystems)
                {
                    GlobalImageKey childKey = new GlobalImageKey(setName, ifsEntry.ImageId);
                    using IImageReader childReader = store.OpenImageReader(childKey);
                    Assert.NotNull(childReader);
                    Assert.NotNull(childReader.Image);

                    // Verify the child image has the expected format
                    Assert.Equal(ImageFormat.Cue, childReader.Image.Format);

                    // Verify the child image has FileName-tagged areas
                    List<AreaRecord> areas = childReader.GetAreas().ToList();
                    Assert.True(areas.Count >= 2, $"Child image '{childReader.Image.Name}' should have at least 2 areas");

                    foreach (AreaRecord area in areas)
                    {
                        string fileName = area.Metadata?.GetString(AreaValueType.FileName);
                        Assert.False(string.IsNullOrEmpty(fileName),
                            $"Area at offset {area.Offset} in '{childReader.Image.Name}' should have FileName metadata");
                    }

                    _output.WriteLine($"  Child '{childReader.Image.Name}': {areas.Count} areas with FileName metadata verified");
                }

                _output.WriteLine("PASS: CueFolder open and child enumeration verified");
            }
        }
    }
}
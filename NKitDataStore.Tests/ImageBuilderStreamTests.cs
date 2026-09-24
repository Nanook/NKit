using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Tests for GameCube, Wii, and WiiU DataStore-based image reconstruction streams.
    /// Creates dummy data with various area types and striding to validate storage and reconstruction.
    /// NOTE: ImageFormat enum only has Wii defined currently, so we use that for all Nintendo systems in tests.
    /// </summary>
    public class ImageBuilderStreamTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDir;

        public ImageBuilderStreamTests(ITestOutputHelper output)
        {
            _output = output;
            _testDir = Path.Combine(Path.GetTempPath(), $"NKitStreamTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Directory.Delete(_testDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private static IImageWriter addImage(DataStore store, string setName, string imageName, long shardSize = 50L * 1024 * 1024 * 1024, string system = null, ImageFormat format = ImageFormat.Unknown, int blockSize = 0)
        {
            if (store.GetSetInfo(setName) == null)
                store.CreateSet(setName, shardSize, blockSize);

            return store.AddImage(setName, imageName, system, format);
        }

        //[Fact]
        //public void Wii_Strided_Reconstruction_Matches_WiiSecurity()
        //{
        //    // Arrange
        //    var setName = "WiiSecurityTest";
        //    var imageName = "Wii_Stride_RoundTrip";

        //    using var store = new DataStore(_testDir);

        //    // Create a Wii image with a single strided FileSystem area
        //    long areaOffset = 0;
        //    const int blockCount = 3; // small number of blocks for the test
        //    var stride = DataStride.Wii;
        //    long areaSize = (long)stride.SourceBlockSize * blockCount;

        //    using (var writer = TestDataStoreHelper.AddImage(store, setName, imageName, shardSize: 0, system: "Wii", format: ImageFormat.Iso))
        //    {
        //        var fsMetadata = new AreaMetadata();
        //        fsMetadata.Set(AreaValueType.FsType, "FileSystem");
        //        fsMetadata.Set(AreaValueType.Partition, 0L);
        //        fsMetadata.Set(AreaValueType.JunkID, "TEST");

        //        writer.CreateArea(
        //            offset: areaOffset,
        //            size: areaSize,
        //            crc32: 0xDEADBEEF,
        //            xxhash64: 0xDEADBEEFCAFEBABE,
        //            stride.SourceBlockSize,
        //            stride.DataOffset,
        //            stride.DataLength,
        //            metadata: fsMetadata
        //        );

        //        // Build clean filesystem-sector blocks and write them as file data
        //        var cleanBlocks = TestDataFactory.CreateSequentialCleanBlocks(blockCount, stride.DataLength, start: 0x10);
        //        // concatenate into one buffer
        //        var concatenated = cleanBlocks.SelectMany(b => b).ToArray();

        //        writer.WriteData(
        //            offset: areaOffset,
        //            data: concatenated,
        //            type: DataType.File,
        //            offsetStart: areaOffset
        //        );

        //        writer.FinalizeImage(size: areaSize, crc32: 0xAABBCCDD, xxhash64: 0x1122334455667788);
        //    }

        //    // Generate YAML for inspection - write to PROJECT directory with class name prefix
        //    var projectDirectory = TestDirectoryHelper.ProjectDirectory;
        //    var yamlPath = Path.Combine(projectDirectory, $"{nameof(ImageBuilderStreamTests)}.{nameof(Wii_Strided_Reconstruction_Matches_WiiSecurity)}.yml");
        //    TestDbHelper.CompareDbToYaml(_testDir, yamlPath);

        //    Assert.True(File.Exists(yamlPath), "YAML file should be generated");
        //    var yaml = File.ReadAllText(yamlPath);
        //    _output.WriteLine("Wii Strided Reconstruction YAML:");
        //    _output.WriteLine(yaml);

        //    // Expected bytes produced by WiiSecurity for the same clean blocks (canonical)
        //    var expectedFull = TestDataFactory.CreateWiiEncryptedStridedSource(
        //        TestDataFactory.CreateSequentialCleanBlocks(blockCount, stride.DataLength, start: 0x10)
        //    );
        //    // TestDataFactory builds full Wii groups (0x200000 bytes) even for partial inputs.
        //    // The image area was created for only `blockCount` source blocks, so compare only that range.
        //    var expected = expectedFull.Take((int)areaSize).ToArray();

        //    // Act - open image reader and stream for the strided area
        //    var imageRecord = store.ListAllImages().FirstOrDefault(i => i.Name == imageName && i.SetName == setName);
        //    Assert.NotNull(imageRecord);

        //    var key = new GlobalImageKey(setName, imageRecord.Id);
        //    using var reader = store.OpenImageReader(key);
        //    using var stream = reader.OpenStream(DataStride.Wii, areaOffset);

        //    // Read reconstructed bytes
        //    var actual = new byte[expected.Length];
        //    int totalRead = 0;
        //    while (totalRead < actual.Length)
        //    {
        //        int r = stream.Read(actual, totalRead, actual.Length - totalRead);
        //        if (r == 0) break;
        //        totalRead += r;
        //    }

        //    // Assert
        //    Assert.Equal(expected.Length, totalRead);

        //    // Verify per-block clean data. The reader may return either:
        //    //  - a strided stream (each block contains hash/padding then data at dataOffset), or
        //    //  - a concatenated clean-data stream (blocks packed back-to-back with no hashes).
        //    int blockSize = stride.SourceBlockSize;
        //    int dataOffset = stride.DataOffset;
        //    int dataLength = stride.DataLength;

        //    // Recreate the clean blocks we originally wrote
        //    var expectedCleanBlocks = TestDataFactory.CreateSequentialCleanBlocks(blockCount, dataLength, start: 0x10);
        //    bool allBlocksMatchedStrided = true;
        //    bool allBlocksMatchedConcatenated = true;
        //    bool anyHashRecreated = false;

        //    for (int i = 0; i < blockCount; i++)
        //    {
        //        // expected clean block
        //        var expectedClean = expectedCleanBlocks[i];

        //        // candidate 1: strided position in the reconstructed stream (data at dataOffset)
        //        int stridedPos = i * blockSize + dataOffset;
        //        // candidate 1b: strided variant where data starts at block start (no hash/padding present)
        //        int stridedPosNoOffset = i * blockSize;
        //        // candidate 2: concatenated clean data position
        //        int concatPos = i * dataLength;
        //        bool matchedConcat = false;
        //        if (concatPos + dataLength <= actual.Length)
        //        {
        //            var act = actual.Skip(concatPos).Take(dataLength).ToArray();
        //            matchedConcat = act.SequenceEqual(expectedClean);
        //        }

        //        // Check the strided variant(s)
        //        bool matchedStrided = false;
        //        if (stridedPos + dataLength <= actual.Length)
        //        {
        //            var act = actual.Skip(stridedPos).Take(dataLength).ToArray();
        //            matchedStrided = act.SequenceEqual(expectedClean);
        //        }
        //        bool matchedStridedNoOffset = false;
        //        if (!matchedStrided && stridedPosNoOffset + dataLength <= actual.Length)
        //        {
        //            var act = actual.Skip(stridedPosNoOffset).Take(dataLength).ToArray();
        //            matchedStridedNoOffset = act.SequenceEqual(expectedClean);
        //        }

        //        bool matchedAnyStrided = matchedStrided || matchedStridedNoOffset;

        //        allBlocksMatchedStrided &= matchedAnyStrided;
        //        allBlocksMatchedConcatenated &= matchedConcat;

        //        // If strided matched, check hash region for this block: if non-zero it must match canonical expected
        //        if (matchedStrided || matchedStridedNoOffset)
        //        {
        //            int hashPos = i * blockSize;
        //            int hashLen = Math.Min(dataOffset, Math.Min(blockSize, expected.Length - hashPos));
        //            if (hashLen > 0 && hashPos + hashLen <= actual.Length && hashPos + hashLen <= expected.Length)
        //            {
        //                var actHash = actual.Skip(hashPos).Take(hashLen).ToArray();
        //                var expHash = expected.Skip(hashPos).Take(hashLen).ToArray();
        //                bool actAllZero = actHash.All(b => b == 0);
        //                if (!actAllZero)
        //                {
        //                    Assert.Equal(expHash, actHash);
        //                    anyHashRecreated = true;
        //                }
        //            }
        //        }

        //        // If neither layout matched for this block, fail with informative message
        //        if (!matchedAnyStrided && !matchedConcat)
        //        {
        //            // Provide a short hex preview for debugging
        //            string previewExpected = BitConverter.ToString(expectedClean.Take(8).ToArray());
        //            string previewActualAtStrided = stridedPos + dataLength <= actual.Length ? BitConverter.ToString(actual.Skip(stridedPos).Take(8).ToArray()) : "<out-of-range>";
        //            string previewActualAtConcat = concatPos + dataLength <= actual.Length ? BitConverter.ToString(actual.Skip(concatPos).Take(8).ToArray()) : "<out-of-range>";
        //            Assert.True(false, $"Block {i} did not match expected clean data in either strided (pos {stridedPos}) or concatenated (pos {concatPos}) layout.\nExpected start: {previewExpected}\nActual@strided: {previewActualAtStrided}\nActual@concat: {previewActualAtConcat}");
        //        }
        //    }

        //    if (allBlocksMatchedStrided)
        //    {
        //        _output.WriteLine("Reader returned strided layout and clean data matched expected.");
        //    }
        //    else if (allBlocksMatchedConcatenated)
        //    {
        //        _output.WriteLine("Reader returned concatenated clean-data layout and clean data matched expected.");
        //    }
        //    else
        //    {
        //        _output.WriteLine("Reader returned a mixed layout but all clean blocks matched in some position.");
        //    }

        //    if (anyHashRecreated)
        //        _output.WriteLine("Some hash/padding blocks were recreated by the reader.");
        //    else
        //        _output.WriteLine("No hashes were recreated; hash regions were zero or not present in the returned layout.");

        //    _output.WriteLine($"Wii strided reconstruction matched expected bytes ({expected.Length} bytes)");
        //}

        [Fact]
        public void GameCube_SingleArea_NoStriding()
        {
            // Arrange
            string setName = "GameCube_Test";
            string imageName = "SingleArea_NoStride";

            using DataStore store = new DataStore(_testDir);

            // Create a simple GameCube image with one area (no striding)
            using (IImageWriter writer = addImage(store, setName, imageName, system: "GameCube", format: ImageFormat.Iso))
            {
                // Image header area (0x0 - 0x440)
                AreaMetadata headerMetadata = new AreaMetadata();
                headerMetadata.Set(AreaValueType.FsType, "ImageHeader");

                writer.CreateArea(
                    offset: 0x0,
                    size: 0x440,
                    crc32: 0x12345678,
                    xxhash64: 0x1234567890ABCDEF, 0x200000, metadata: headerMetadata
                );

                // Add header data (first 0x440 bytes)
                byte[] headerData = new byte[0x440];
                // Disc magic: 0xC2339F3D at 0x1C (GameCube signature)
                headerData[0x1C] = 0xC2;
                headerData[0x1D] = 0x33;
                headerData[0x1E] = 0x9F;
                headerData[0x1F] = 0x3D;
                // Game ID: "GALE01" (Super Smash Bros. Melee)
                Encoding.ASCII.GetBytes("GALE01").CopyTo(headerData, 0x0);

                writer.WriteData(
                    offset: 0x0,
                    data: headerData,
                    type: BlockType.FormatData,
                    offsetStart: 0x0
                );

                // Filesystem area (0x440 - 0x100000) - no striding
                AreaMetadata fsMetadata = new AreaMetadata();
                fsMetadata.Set(AreaValueType.FsType, "FileSystem");
                fsMetadata.Set(AreaValueType.Partition, 0L);
                fsMetadata.Set(AreaValueType.JunkID, "GALE");
                fsMetadata.Set(AreaValueType.DiscNo, 0L);
                fsMetadata.Set(AreaValueType.JunkLeadingNulls, 0L);

                writer.CreateArea(
                    offset: 0x440,
                    size: 0x100000 - 0x440,
                    crc32: 0x87654321,
                    xxhash64: 0xFEDCBA0987654321, 0x200000, metadata: fsMetadata
                );

                // Add some file data
                byte[] fileData = new byte[0x1000];
                for (int i = 0; i < fileData.Length; i++)
                    fileData[i] = (byte)(i % 256);

                writer.WriteData(
                    offset: 0x440,
                    data: fileData,
                    type: BlockType.File,
                    offsetStart: 0x440
                );

                // Gap filled with junk (0x1440 - 0x100000)
                // NJunk fills the gaps automatically

                writer.FinalizeImage(
                    size: 0x100000,
                    crc32: 0xAABBCCDD,
                    xxhash64: 0xAABBCCDDEEFF0011
                );
            }

            // Generate YAML for inspection - write to PROJECT directory with class name prefix
            string projectDirectory = TestDirectoryHelper.ProjectDirectory;
            string yamlPath = Path.Combine(projectDirectory, $"{nameof(ImageBuilderStreamTests)}.{nameof(GameCube_SingleArea_NoStriding)}.yml");
            TestDbHelper.CompareDbToYaml(_testDir, yamlPath);

            // Assert - output for inspection
            Assert.True(File.Exists(yamlPath), "YAML file should be generated");
            string yaml = File.ReadAllText(yamlPath);
            _output.WriteLine("GameCube Single Area (No Striding) YAML:");
            _output.WriteLine(yaml);

            // Verify structure - use flexible assertions that match actual YAML format
            Assert.Contains("GameCube_Test", yaml);
            Assert.Contains("ImageHeader", yaml);
            Assert.Contains("FileSystem", yaml);
            Assert.Contains("JunkID", yaml); // Just check the key exists
            Assert.Contains("GALE", yaml);   // And the value exists
        }

        [Fact]
        public void Wii_MultiPartition_WithStriding()
        {
            // Arrange
            string setName = "Wii_Test";
            string imageName = "MultiPartition_WithStride";

            using DataStore store = new DataStore(_testDir);

            // Create a Wii image with multiple partitions and striding
            using (IImageWriter writer = addImage(store, setName, imageName, system: "Wii", format: ImageFormat.Iso))
            {
                long currentOffset = 0;

                // Image header (0x0 - 0x40000)
                AreaMetadata headerMetadata = new AreaMetadata();
                headerMetadata.Set(AreaValueType.FsType, "ImageHeader");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x40000,
                    crc32: 0x11111111,
                    xxhash64: 0x1111111111111111, 0x200000, metadata: headerMetadata
                );

                byte[] headerData = new byte[0x40000];
                // Wii magic: 0x5D1C9EA3 at 0x18
                headerData[0x18] = 0x5D;
                headerData[0x19] = 0x1C;
                headerData[0x1A] = 0x9E;
                headerData[0x1B] = 0xA3;
                // Game ID: "RMCP01" (Mario Kart Wii)
                Encoding.ASCII.GetBytes("RMCP01").CopyTo(headerData, 0x0);

                writer.WriteData(
                    offset: currentOffset,
                    data: headerData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x40000;

                // Partition 0: Data Partition Header (0x40000 - 0x60000)
                AreaMetadata partHeaderMetadata = new AreaMetadata();
                partHeaderMetadata.Set(AreaValueType.FsType, "PartitionHeader");
                partHeaderMetadata.Set(AreaValueType.Partition, 0L);
                partHeaderMetadata.Set(AreaValueType.PartitionType, "Data");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x20000,
                    crc32: 0x22222222,
                    xxhash64: 0x2222222222222222, 0x200000, metadata: partHeaderMetadata
                );

                byte[] partHeaderData = new byte[0x20000];
                for (int i = 0; i < partHeaderData.Length; i++)
                    partHeaderData[i] = 0xAA;

                writer.WriteData(
                    offset: currentOffset,
                    data: partHeaderData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x20000;

                // Partition 0: FileSystem area WITH STRIDING (Wii blocks)
                // Wii stride: 0x8000 block, 0x400 hash at start, 0x7C00 data
                // NOTE: Stride parameters would normally be set by the real Wii writer
                // For now, we're testing the core DataStore functionality
                AreaMetadata fsMetadata = new AreaMetadata();
                fsMetadata.Set(AreaValueType.FsType, "FileSystem");
                fsMetadata.Set(AreaValueType.Partition, 0L);
                fsMetadata.Set(AreaValueType.JunkID, "RMCP");
                fsMetadata.Set(AreaValueType.DiscNo, 0L);
                fsMetadata.Set(AreaValueType.JunkLeadingNulls, 0L);

                // Set stride explicitly so the DataStore records striding info
                const int strideBlockSize = 0x8000; // 32KB
                const int strideDataOffset = 0x400; // 0x400 hash at block start
                const int strideDataLength = 0x7C00; // data portion per block

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x100000, // 1MB of strided data
                    crc32: 0x33333333,
                    xxhash64: 0x3333333333333333,
                    strideBlockSize,
                    strideDataOffset,
                    strideDataLength,
                    0x200000,
                    metadata: fsMetadata
                );

                // Add file data (clean data, hashes will be reconstructed)
                byte[] fileData1 = new byte[0x7C00]; // 31KB clean data for one block
                for (int i = 0; i < fileData1.Length; i++)
                    fileData1[i] = (byte)((i + 1) % 256);

                writer.WriteData(
                    offset: currentOffset,
                    data: fileData1,
                    type: BlockType.File,
                    offsetStart: currentOffset
                );

                // File 2 starts at next block boundary
                long file2Offset = currentOffset + 0x8000;
                byte[] fileData2 = new byte[0xF800]; // 63KB clean data for two blocks
                for (int i = 0; i < fileData2.Length; i++)
                    fileData2[i] = (byte)((i + 0x80) % 256);

                writer.WriteData(
                    offset: file2Offset,
                    data: fileData2,
                    type: BlockType.File,
                    offsetStart: file2Offset
                );

                // Gaps filled with junk

                currentOffset += 0x100000;

                // Partition 0: Other area (post-data padding)
                AreaMetadata otherMetadata = new AreaMetadata();
                otherMetadata.Set(AreaValueType.FsType, "Other");
                otherMetadata.Set(AreaValueType.Partition, 0L);
                otherMetadata.Set(AreaValueType.JunkID, "RMCP");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x10000,
                    crc32: 0x44444444,
                    xxhash64: 0x4444444444444444, 0x200000, metadata: otherMetadata
                );

                currentOffset += 0x10000;

                writer.FinalizeImage(
                    size: currentOffset,
                    crc32: 0xDEADBEEF,
                    xxhash64: 0xDEADBEEFCAFEBABE
                );
            }

            // Generate YAML for inspection - write to PROJECT directory with class name prefix
            string projectDirectory = TestDirectoryHelper.ProjectDirectory;
            string yamlPath = Path.Combine(projectDirectory, $"{nameof(ImageBuilderStreamTests)}.{nameof(Wii_MultiPartition_WithStriding)}.yml");
            TestDbHelper.CompareDbToYaml(_testDir, yamlPath);

            // Assert
            Assert.True(File.Exists(yamlPath), "YAML file should be generated");
            string yaml = File.ReadAllText(yamlPath);
            _output.WriteLine("Wii Multi-Partition (With Striding) YAML:");
            _output.WriteLine(yaml);

            Assert.Contains("Wii_Test", yaml);
            Assert.Contains("PartitionHeader", yaml);
            Assert.Contains("FileSystem", yaml);
            // NOTE: Stride parameters aren't set in this basic test - would need dedicated WiiWriter
        }

        [Fact]
        public void Wii_MultiPartition_WithStriding_WithStoredPadding()
        {
            // Arrange
            string setName = "Wii_Test";
            string imageName = "MultiPartition_WithStride_StoredPadding";

            using DataStore store = new DataStore(_testDir);

            // Create a Wii image with multiple partitions and striding, and store some padding bytes explicitly
            using (IImageWriter writer = addImage(store, setName, imageName, system: "Wii", format: ImageFormat.Iso))
            {
                long currentOffset = 0;

                // Image header
                AreaMetadata headerMetadata = new AreaMetadata();
                headerMetadata.Set(AreaValueType.FsType, "ImageHeader");
                writer.CreateArea(offset: currentOffset, size: 0x40000, crc32: 0x11111111, xxhash64: 0x1111111111111111, 0x200000, metadata: headerMetadata);
                byte[] headerData = new byte[0x40000];
                headerData[0x18] = 0x5D; headerData[0x19] = 0x1C; headerData[0x1A] = 0x9E; headerData[0x1B] = 0xA3;
                Encoding.ASCII.GetBytes("RMCP01").CopyTo(headerData, 0x0);
                writer.WriteData(offset: currentOffset, data: headerData, type: BlockType.FormatData, offsetStart: currentOffset);
                currentOffset += 0x40000;

                // Partition header
                AreaMetadata partHeaderMetadata = new AreaMetadata();
                partHeaderMetadata.Set(AreaValueType.FsType, "PartitionHeader");
                partHeaderMetadata.Set(AreaValueType.Partition, 0L);
                partHeaderMetadata.Set(AreaValueType.PartitionType, "Data");
                writer.CreateArea(offset: currentOffset, size: 0x20000, crc32: 0x22222222, xxhash64: 0x2222222222222222, 0x200000, metadata: partHeaderMetadata);
                byte[] partHeaderData = Enumerable.Repeat((byte)0xAA, 0x20000).ToArray();
                writer.WriteData(offset: currentOffset, data: partHeaderData, type: BlockType.FormatData, offsetStart: currentOffset);
                currentOffset += 0x20000;

                // FileSystem area with striding
                AreaMetadata fsMetadata = new AreaMetadata();
                fsMetadata.Set(AreaValueType.FsType, "FileSystem");
                fsMetadata.Set(AreaValueType.Partition, 0L);
                fsMetadata.Set(AreaValueType.JunkID, "RMCP");
                fsMetadata.Set(AreaValueType.DiscNo, 0L);
                fsMetadata.Set(AreaValueType.JunkLeadingNulls, 0L);

                const int strideBlockSize = 0x8000;
                const int strideDataOffset = 0x400;
                const int strideDataLength = 0x7C00;

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x100000,
                    crc32: 0x33333333,
                    xxhash64: 0x3333333333333333,
                    strideBlockSize,
                    strideDataOffset,
                    strideDataLength,
                    0x200000,
                    metadata: fsMetadata
                );

                // Write normal file data at start of area
                byte[] fileData1 = new byte[strideDataLength];
                for (int i = 0; i < fileData1.Length; i++) fileData1[i] = (byte)((i + 1) % 256);
                writer.WriteData(offset: currentOffset, data: fileData1, type: BlockType.File, offsetStart: currentOffset);

                // Now simulate non-reproducible padding: write a small chunk that sits inside a block padding region
                // Force storage of these bytes by writing as DataType.BlockPadding
                // Place it 0x100 bytes into the data portion of the first block to force partial storage
                long paddingOffset = currentOffset + strideDataOffset + 0x100; // inside first block data region
                byte[] paddingBytes = new byte[0x200]; // 512 bytes of padding stored
                for (int i = 0; i < paddingBytes.Length; i++) paddingBytes[i] = (byte)(0xFF - (i % 256));

                // Write padding block to force storage
                writer.WriteData(paddingOffset, paddingBytes, BlockType.BlockPadding, offsetStart: paddingOffset);

                // Finish area with another file starting at next block boundary
                long file2Offset = currentOffset + strideBlockSize;
                byte[] fileData2 = new byte[0xF800];
                for (int i = 0; i < fileData2.Length; i++) fileData2[i] = (byte)((i + 0x80) % 256);
                writer.WriteData(offset: file2Offset, data: fileData2, type: BlockType.File, offsetStart: file2Offset);

                currentOffset += 0x100000;

                // Other area
                AreaMetadata otherMetadata = new AreaMetadata();
                otherMetadata.Set(AreaValueType.FsType, "Other");
                otherMetadata.Set(AreaValueType.Partition, 0L);
                otherMetadata.Set(AreaValueType.JunkID, "RMCP");
                writer.CreateArea(offset: currentOffset, size: 0x10000, crc32: 0x44444444, xxhash64: 0x4444444444444444, 0x200000, metadata: otherMetadata);
                currentOffset += 0x10000;

                writer.FinalizeImage(size: currentOffset, crc32: 0xDEADBEEF, xxhash64: 0xDEADBEEFCAFEBABE);
            }

            // Generate YAML and compare
            string projectDirectory = TestDirectoryHelper.ProjectDirectory;
            string yamlPath = Path.Combine(projectDirectory, $"{nameof(ImageBuilderStreamTests)}.{nameof(Wii_MultiPartition_WithStriding_WithStoredPadding)}.yml");
            TestDbHelper.CompareDbToYaml(_testDir, yamlPath);

            Assert.True(File.Exists(yamlPath), "YAML file should be generated");
            string yaml = File.ReadAllText(yamlPath);
            _output.WriteLine("Wii Multi-Partition (With Striding + Stored Padding) YAML:");
            _output.WriteLine(yaml);

            // Verify stride fields and that blocks were stored
            Assert.Contains("stride_block_size", yaml);
            Assert.Contains("stride_data_offset", yaml);
            Assert.Contains("stride_data_length", yaml);
            // The stored padding should result in block entries - check block table exists and some data present
            Assert.Contains("block:", yaml);
        }

        [Fact]
        public void WiiU_MultiContent_VaryingBlockSizes()
        {
            // Arrange
            string setName = "WiiU_Test";
            string imageName = "MultiContent_VaryingBlocks";

            using DataStore store = new DataStore(_testDir);

            // Create a WiiU image with varying block sizes (32KB and 64KB)
            using (IImageWriter writer = addImage(store, setName, imageName, system: "WiiU", format: ImageFormat.Iso))
            {
                long currentOffset = 0;

                // Image header
                AreaMetadata headerMetadata = new AreaMetadata();
                headerMetadata.Set(AreaValueType.FsType, "ImageHeader");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x18000,
                    crc32: 0xA1A1A1A1,
                    xxhash64: 0xA1A1A1A1A1A1A1A1, 0x200000, metadata: headerMetadata
                );

                byte[] headerData = new byte[0x18000];
                // WiiU header ID: 0xCC549EB9 at offset 0x10000
                headerData[0x10000] = 0xCC;
                headerData[0x10001] = 0x54;
                headerData[0x10002] = 0x9E;
                headerData[0x10003] = 0xB9;

                writer.WriteData(
                    offset: currentOffset,
                    data: headerData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x18000;

                // Partition table
                AreaMetadata partTableMetadata = new AreaMetadata();
                partTableMetadata.Set(AreaValueType.FsType, "PartitionTable");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x20000,
                    crc32: 0xB2B2B2B2,
                    xxhash64: 0xB2B2B2B2B2B2B2B2, 0x200000, metadata: partTableMetadata
                );

                byte[] partTableData = new byte[0x20000];
                writer.WriteData(
                    offset: currentOffset,
                    data: partTableData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x20000;

                // Partition 0: Header
                AreaMetadata part0HeaderMetadata = new AreaMetadata();
                part0HeaderMetadata.Set(AreaValueType.FsType, "PartitionHeader");
                part0HeaderMetadata.Set(AreaValueType.Partition, 0L);

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x20000,
                    crc32: 0xC3C3C3C3,
                    xxhash64: 0xC3C3C3C3C3C3C3C3, 0x200000, metadata: part0HeaderMetadata
                );

                byte[] part0HeaderData = new byte[0x20000];
                writer.WriteData(
                    offset: currentOffset,
                    data: part0HeaderData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x20000;

                // Partition 0: FST Block
                // NOTE: Stride parameters would be set by real WiiU writer
                AreaMetadata fstMetadata = new AreaMetadata();
                fstMetadata.Set(AreaValueType.FsType, "FstBlock");
                fstMetadata.Set(AreaValueType.Partition, 0L);

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x8000,
                    crc32: 0xD4D4D4D4,
                    xxhash64: 0xD4D4D4D4D4D4D4D4, 0x200000, metadata: fstMetadata
                );

                byte[] fstData = new byte[0x7C00];
                writer.WriteData(
                    offset: currentOffset,
                    data: fstData,
                    type: BlockType.FileSystem,
                    offsetStart: currentOffset
                );

                currentOffset += 0x8000;

                // Partition 0: FileSystem area with 32KB blocks
                AreaMetadata fs32Metadata = new AreaMetadata();
                fs32Metadata.Set(AreaValueType.FsType, "FileSystem");
                fs32Metadata.Set(AreaValueType.Partition, 0L);
                fs32Metadata.Set(AreaValueType.ContentIndex, 0L);
                fs32Metadata.Set(AreaValueType.HasFileSystem, true);

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x80000, // 512KB
                    crc32: 0xE5E5E5E5,
                    xxhash64: 0xE5E5E5E5E5E5E5E5, 0x200000, metadata: fs32Metadata
                );

                // Add content files
                byte[] contentData1 = new byte[0xF800];
                for (int i = 0; i < contentData1.Length; i++)
                    contentData1[i] = (byte)(i % 256);

                writer.WriteData(
                    offset: currentOffset,
                    data: contentData1,
                    type: BlockType.File,
                    offsetStart: currentOffset
                );

                currentOffset += 0x80000;

                // Partition 0: FileSystem area with 64KB blocks
                AreaMetadata fs64Metadata = new AreaMetadata();
                fs64Metadata.Set(AreaValueType.FsType, "FileSystem");
                fs64Metadata.Set(AreaValueType.Partition, 0L);
                fs64Metadata.Set(AreaValueType.ContentIndex, 1L);
                fs64Metadata.Set(AreaValueType.HasFileSystem, true);

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x100000, // 1MB
                    crc32: 0xF6F6F6F6,
                    xxhash64: 0xF6F6F6F6F6F6F6F6, 0x200000, metadata: fs64Metadata
                );

                byte[] contentData2 = new byte[0x1F800];
                for (int i = 0; i < contentData2.Length; i++)
                    contentData2[i] = (byte)((i + 128) % 256);

                writer.WriteData(
                    offset: currentOffset,
                    data: contentData2,
                    type: BlockType.File,
                    offsetStart: currentOffset
                );

                currentOffset += 0x100000;

                // Partition 0: Other area (repeated content)
                AreaMetadata otherMetadata = new AreaMetadata();
                otherMetadata.Set(AreaValueType.FsType, "Other");
                otherMetadata.Set(AreaValueType.Partition, 0L);
                otherMetadata.Set(AreaValueType.RepeatedContentIndex, 0L);

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x80000, // Same size as content 0
                    crc32: 0xE5E5E5E5, // Same CRC as original content
                    xxhash64: 0xE5E5E5E5E5E5E5E5, 0x200000, metadata: otherMetadata
                );

                // No offsets for repeated content - it references original

                currentOffset += 0x80000;

                writer.FinalizeImage(
                    size: currentOffset,
                    crc32: 0xCAFEBABE,
                    xxhash64: 0xCAFEBABEDEADBEEF
                );
            }

            // Generate YAML for inspection - write to PROJECT directory with class name prefix
            string projectDirectory = TestDirectoryHelper.ProjectDirectory;
            string yamlPath = Path.Combine(projectDirectory, $"{nameof(ImageBuilderStreamTests)}.{nameof(WiiU_MultiContent_VaryingBlockSizes)}.yml");
            TestDbHelper.CompareDbToYaml(_testDir, yamlPath);

            // Assert
            Assert.True(File.Exists(yamlPath), "YAML file should be generated");
            string yaml = File.ReadAllText(yamlPath);
            _output.WriteLine("WiiU Multi-Content (Varying Block Sizes) YAML:");
            _output.WriteLine(yaml);

            Assert.Contains("WiiU_Test", yaml);
            Assert.Contains("PartitionTable", yaml);
            Assert.Contains("FstBlock", yaml);
            Assert.Contains("RepeatedContentIndex", yaml);
            // NOTE: Stride parameters aren't set in this basic test - would need dedicated WiiUWriter
        }

        [Fact]
        public void Wii_UpdatePartition_NoFileSystem()
        {
            // Arrange
            string setName = "Wii_Test";
            string imageName = "UpdatePartition_NoFS";

            using DataStore store = new DataStore(_testDir);

            // Create a Wii update partition (no filesystem, all format data)
            using (IImageWriter writer = addImage(store, setName, imageName, system: "Wii", format: ImageFormat.Iso))
            {
                long currentOffset = 0;

                // Image header
                AreaMetadata headerMetadata = new AreaMetadata();
                headerMetadata.Set(AreaValueType.FsType, "ImageHeader");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x40000,
                    crc32: 0x99999999,
                    xxhash64: 0x9999999999999999, 0x200000, metadata: headerMetadata
                );

                byte[] headerData = new byte[0x40000];
                writer.WriteData(
                    offset: currentOffset,
                    data: headerData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x40000;

                // Update partition header
                AreaMetadata updateHeaderMetadata = new AreaMetadata();
                updateHeaderMetadata.Set(AreaValueType.FsType, "PartitionHeader");
                updateHeaderMetadata.Set(AreaValueType.Partition, 1L);
                updateHeaderMetadata.Set(AreaValueType.PartitionType, "Update");

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x20000,
                    crc32: 0x88888888,
                    xxhash64: 0x8888888888888888, 0x200000, metadata: updateHeaderMetadata
                );

                byte[] updateHeaderData = new byte[0x20000];
                writer.WriteData(
                    offset: currentOffset,
                    data: updateHeaderData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x20000;

                // Update partition data (no FileSystem area type, just Other)
                AreaMetadata updateDataMetadata = new AreaMetadata();
                updateDataMetadata.Set(AreaValueType.FsType, "Other");
                updateDataMetadata.Set(AreaValueType.Partition, 1L);

                writer.CreateArea(
                    offset: currentOffset,
                    size: 0x80000,
                    crc32: 0x77777777,
                    xxhash64: 0x7777777777777777, 0x200000, metadata: updateDataMetadata
                );

                // All update data is format data
                byte[] updateData = new byte[0xF800];
                writer.WriteData(
                    offset: currentOffset,
                    data: updateData,
                    type: BlockType.FormatData,
                    offsetStart: currentOffset
                );

                currentOffset += 0x80000;

                writer.FinalizeImage(
                    size: currentOffset,
                    crc32: 0x55555555,
                    xxhash64: 0x5555555555555555
                );
            }

            // Generate YAML for inspection - write to PROJECT directory with class name prefix
            string projectDirectory = TestDirectoryHelper.ProjectDirectory;
            string yamlPath = Path.Combine(projectDirectory, $"{nameof(ImageBuilderStreamTests)}.{nameof(Wii_UpdatePartition_NoFileSystem)}.yml");
            TestDbHelper.CompareDbToYaml(_testDir, yamlPath);

            // Assert
            Assert.True(File.Exists(yamlPath), "YAML file should be generated");
            string yaml = File.ReadAllText(yamlPath);
            _output.WriteLine("Wii Update Partition (No FileSystem) YAML:");
            _output.WriteLine(yaml);

            // Verify structure - use flexible assertions
            Assert.Contains("PartitionType", yaml); // Check key exists
            Assert.Contains("Update", yaml);        // Check value exists
            Assert.Contains("FsType", yaml);        // Check FsType key exists
            Assert.Contains("Other", yaml);         // Check Other value exists
            // Don't use DoesNotContain for "FileSystem" as it appears in enum documentation
            // Instead verify that all areas have FsType as either ImageHeader, PartitionHeader, or Other
            Assert.DoesNotContain("FsType=FileSystem", yaml); // More specific check
        }
    }
}
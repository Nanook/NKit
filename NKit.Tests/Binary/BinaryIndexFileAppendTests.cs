using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Tests for the append-only invariant of BinaryIndexFile.
    ///
    /// Feature: binary-index-format
    /// Property 10: Append preserves existing bytes
    /// **Validates: Requirements 6.1**
    ///
    /// WHEN adding a new image, THE BinaryDataStoreDataAccess SHALL append the new
    /// Image_Metadata_Section and Image_BlockMap_Section at the end of the file
    /// without modifying any previously written bytes (except the Header during Atomic_Commit).
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class BinaryIndexFileAppendTests : IDisposable
    {
        private readonly string _tempDir;

        public BinaryIndexFileAppendTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BinaryIndexFileAppendTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        /// <summary>
        /// **Validates: Requirements 6.1**
        ///
        /// After appending an image and committing, the bytes in the data region (from 0x1200)
        /// and the original file end must remain unchanged. New data is appended after the original end.
        /// </summary>
        [Fact]
        public void AppendImage_PreservesExistingBytesAfterHeaders()
        {
            // Arrange: create a valid file with one image already present
            string path = Path.Combine(_tempDir, "append_test.nkds");

            byte[] originalContentAfterHeaders;
            long originalFileEnd;

            // Create file and add a first image to have some content beyond the headers
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                // Serialize dummy metadata and blockmap for the first image
                (byte[] metadata1, int _) = ImageMetadataSectionSerializer.Serialize(
                    new List<AreaRecord>
                    {
                        new AreaRecord { Offset = 0, Size = 1024, SectionSize = 0x200000, Crc32 = 0xAABBCCDD, XxHash64 = 0x1122334455667788 }
                    },
                    new List<FileRecord>());

                (byte[] blockMap1, int _) = ImageBlockMapSectionSerializer.Serialize(
                    new List<OffsetRecord>(),
                    new Dictionary<BlockKey, (int FileId, long Offset, int Size)>());

                // Append first image
                (long metaOff, int metaSize, long bmOff, int bmSize) = file.AppendImage(imageId: 1, metadata1, blockMap1, newBlocks: null);

                // Update directory with the new image entry
                ImageDirectory directory = file.GetDirectory();
                directory.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 1,
                    Name = "image1.iso",
                    Size = 1024,
                    Crc32 = 0xAABBCCDD,
                    XxHash64 = 0x1122334455667788,
                    Format = ImageFormat.Iso,
                    MetadataSectionOffset = metaOff,
                    MetadataSectionCompressedSize = metaSize,
                    BlockMapSectionOffset = bmOff,
                    BlockMapSectionCompressedSize = bmSize
                });
                file.UpdateDirectory(directory);
                file.AtomicCommit();
            }

            // Record the file content after the data region start (headers + directory region)
            byte[] fileContentBeforeAppend = File.ReadAllBytes(path);
            originalFileEnd = fileContentBeforeAppend.Length;
            // Data region starts at 0x200 + DirectoryRegionCapacity (4096) = 0x1200
            int dataRegionStart = (FileHeader.HeaderSize * 2) + 4096; // 512 + 4096 = 4608 (0x1200)
            originalContentAfterHeaders = new byte[originalFileEnd - dataRegionStart];
            Array.Copy(fileContentBeforeAppend, dataRegionStart, originalContentAfterHeaders, 0, originalContentAfterHeaders.Length);

            // Act: append a second image and commit
            using (BinaryIndexFile file = BinaryIndexFile.Open(path))
            {
                (byte[] metadata2, int _) = ImageMetadataSectionSerializer.Serialize(
                    new List<AreaRecord>
                    {
                        new AreaRecord { Offset = 0, Size = 2048, SectionSize = 0x200000, Crc32 = 0x11223344, XxHash64 = 0xAABBCCDDEEFF0011 }
                    },
                    new List<FileRecord>
                    {
                        new FileRecord { Name = "test.bin", FileId = 0, Offset = 0, Size = 100, UncompressedSize = 200, IsSystem = false }
                    });

                (byte[] blockMap2, int _) = ImageBlockMapSectionSerializer.Serialize(
                    new List<OffsetRecord>
                    {
                        new OffsetRecord { Offset = 0, Size = 2048, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { new BlockKey(0xDEADBEEF, 0x12345678) } }
                    },
                    new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
                    {
                        { new BlockKey(0xDEADBEEF, 0x12345678), (0, 0, 2048) }
                    });

                (long metaOff2, int metaSize2, long bmOff2, int bmSize2) = file.AppendImage(imageId: 2, metadata2, blockMap2, newBlocks: null);

                // Update directory
                ImageDirectory directory = file.GetDirectory();
                directory.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 2,
                    Name = "image2.iso",
                    Size = 2048,
                    Crc32 = 0x11223344,
                    XxHash64 = 0xAABBCCDDEEFF0011,
                    Format = ImageFormat.Iso,
                    MetadataSectionOffset = metaOff2,
                    MetadataSectionCompressedSize = metaSize2,
                    BlockMapSectionOffset = bmOff2,
                    BlockMapSectionCompressedSize = bmSize2
                });
                file.UpdateDirectory(directory);
                file.AtomicCommit();
            }

            // Assert: read the file after append
            byte[] fileContentAfterAppend = File.ReadAllBytes(path);

            // 1. The file must be larger than before (new data was appended)
            Assert.True(fileContentAfterAppend.Length > originalFileEnd,
                $"File should have grown. Before: {originalFileEnd}, After: {fileContentAfterAppend.Length}");

            // 2. The original bytes in the data region (from 0x1200 to original end) must be unchanged
            int dataRegionStart2 = (FileHeader.HeaderSize * 2) + 4096;
            byte[] currentContentInOriginalRange = new byte[originalContentAfterHeaders.Length];
            Array.Copy(fileContentAfterAppend, dataRegionStart2, currentContentInOriginalRange, 0, originalContentAfterHeaders.Length);

            Assert.Equal(originalContentAfterHeaders, currentContentInOriginalRange);

            // 3. New data exists after the original file end
            long newDataLength = fileContentAfterAppend.Length - originalFileEnd;
            Assert.True(newDataLength > 0, "New data should have been appended after the original file end.");
        }

        /// <summary>
        /// **Validates: Requirements 6.1**
        ///
        /// Appending multiple images sequentially preserves all previously written content
        /// (excluding headers) at each step.
        /// </summary>
        [Fact]
        public void AppendMultipleImages_PreservesAllPreviousContent()
        {
            string path = Path.Combine(_tempDir, "multi_append_test.nkds");

            // Create file
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                file.AtomicCommit();
            }

            // Data region starts at 0x200 + DirectoryRegionCapacity (4096) = 0x1200
            int dataRegionStart = (FileHeader.HeaderSize * 2) + 4096;

            // Append 3 images, verifying preservation at each step
            for (int i = 1; i <= 3; i++)
            {
                // Record content before this append (data region only)
                byte[] beforeAppend = File.ReadAllBytes(path);
                long beforeLength = beforeAppend.Length;
                byte[] contentBeforeAppend = new byte[beforeLength - dataRegionStart];
                Array.Copy(beforeAppend, dataRegionStart, contentBeforeAppend, 0, contentBeforeAppend.Length);

                // Append image i
                using (BinaryIndexFile file = BinaryIndexFile.Open(path))
                {
                    (byte[] metadata, int _) = ImageMetadataSectionSerializer.Serialize(
                        new List<AreaRecord>
                        {
                            new AreaRecord { Offset = i * 1000, Size = i * 500, SectionSize = 0x200000, Crc32 = (uint)(i * 111), XxHash64 = (ulong)(i * 222) }
                        },
                        new List<FileRecord>());

                    (byte[] blockMap, int _) = ImageBlockMapSectionSerializer.Serialize(
                        new List<OffsetRecord>(),
                        new Dictionary<BlockKey, (int FileId, long Offset, int Size)>());

                    (long metaOff, int metaSize, long bmOff, int bmSize) = file.AppendImage(imageId: i, metadata, blockMap, newBlocks: null);

                    ImageDirectory directory = file.GetDirectory();
                    directory.AddOrUpdate(new ImageDirectoryEntry
                    {
                        ImageId = i,
                        Name = $"image{i}.iso",
                        Size = i * 500,
                        Crc32 = (uint)(i * 111),
                        XxHash64 = (ulong)(i * 222),
                        Format = ImageFormat.Iso,
                        MetadataSectionOffset = metaOff,
                        MetadataSectionCompressedSize = metaSize,
                        BlockMapSectionOffset = bmOff,
                        BlockMapSectionCompressedSize = bmSize
                    });
                    file.UpdateDirectory(directory);
                    file.AtomicCommit();
                }

                // Verify: content in the data region is unchanged (append-only)
                byte[] afterAppend = File.ReadAllBytes(path);
                Assert.True(afterAppend.Length > beforeLength,
                    $"File should grow after appending image {i}. Before: {beforeLength}, After: {afterAppend.Length}");

                byte[] currentOriginalRange = new byte[contentBeforeAppend.Length];
                Array.Copy(afterAppend, dataRegionStart, currentOriginalRange, 0, contentBeforeAppend.Length);

                Assert.Equal(contentBeforeAppend, currentOriginalRange);
            }
        }
    }
}
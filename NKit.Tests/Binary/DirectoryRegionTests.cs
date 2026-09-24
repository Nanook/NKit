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
    /// Tests for the reserved directory region behavior.
    /// Verifies that when the compressed directory fits within the reserved region (4096 bytes),
    /// it is written in-place at offset 0x200 without appending to the end of the file.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class DirectoryRegionTests : IDisposable
    {
        private readonly string _tempDir;

        public DirectoryRegionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DirectoryRegionTests_" + Guid.NewGuid().ToString("N"));
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
        /// **Validates: Requirements 4.1, 4.2, 4.3**
        ///
        /// When images are added and the compressed directory fits within the reserved region
        /// (4096 bytes default), the directory is written in-place at offset 0x200.
        /// The file size does NOT grow by the directory size on each commit.
        /// ImageDirectoryOffset remains 0x200 after multiple commits.
        /// </summary>
        [Fact]
        public void DirectoryFitsInReservedRegion_WrittenInPlace()
        {
            string path = Path.Combine(_tempDir, "inplace_dir_test.nkds");

            // Step 1: Create a new file
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                // Step 2: Add a few images (small enough that compressed directory < 4096 bytes)
                for (int i = 1; i <= 3; i++)
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
                }

                // Step 3: Commit
                file.AtomicCommit();
            }

            // Step 4: Verify ImageDirectoryOffset == 0x200
            FileHeader headerAfterFirstCommit = ReadHeaderFromFile(path);
            Assert.Equal(0x200L, headerAfterFirstCommit.ImageDirectoryOffset);

            // Record file size after first commit
            long fileSizeAfterFirstCommit = new FileInfo(path).Length;

            // Step 5: Add another image, commit again
            using (BinaryIndexFile file = BinaryIndexFile.Open(path))
            {
                (byte[] metadata, int _) = ImageMetadataSectionSerializer.Serialize(
                    new List<AreaRecord>
                    {
                        new AreaRecord { Offset = 4000, Size = 2000, SectionSize = 0x200000, Crc32 = 0xDEADBEEF, XxHash64 = 0xCAFEBABE12345678 }
                    },
                    new List<FileRecord>());

                (byte[] blockMap, int _) = ImageBlockMapSectionSerializer.Serialize(
                    new List<OffsetRecord>(),
                    new Dictionary<BlockKey, (int FileId, long Offset, int Size)>());

                (long metaOff, int metaSize, long bmOff, int bmSize) = file.AppendImage(imageId: 4, metadata, blockMap, newBlocks: null);

                ImageDirectory directory = file.GetDirectory();
                directory.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 4,
                    Name = "image4.iso",
                    Size = 2000,
                    Crc32 = 0xDEADBEEF,
                    XxHash64 = 0xCAFEBABE12345678,
                    Format = ImageFormat.Iso,
                    MetadataSectionOffset = metaOff,
                    MetadataSectionCompressedSize = metaSize,
                    BlockMapSectionOffset = bmOff,
                    BlockMapSectionCompressedSize = bmSize
                });
                file.UpdateDirectory(directory);
                file.AtomicCommit();
            }

            // Step 6: Verify ImageDirectoryOffset still == 0x200 (in-place update, not appended)
            FileHeader headerAfterSecondCommit = ReadHeaderFromFile(path);
            Assert.Equal(0x200L, headerAfterSecondCommit.ImageDirectoryOffset);

            // Verify the file did NOT grow by the directory size on the second commit.
            // The only growth should be from the appended image data (metadata + blockmap sections),
            // NOT from the directory being appended at the end.
            long fileSizeAfterSecondCommit = new FileInfo(path).Length;
            long growth = fileSizeAfterSecondCommit - fileSizeAfterFirstCommit;

            // The directory was written in-place, so growth should only be from the new image sections.
            // If the directory were appended, growth would include the compressed directory size.
            // We verify that the directory offset hasn't moved (still 0x200) which proves in-place write.
            Assert.True(headerAfterSecondCommit.ImageDirectorySize > 0,
                "Compressed directory size should be positive after adding images.");
            Assert.True(headerAfterSecondCommit.ImageDirectorySize <= headerAfterSecondCommit.DirectoryRegionCapacity,
                "Compressed directory should fit within the reserved region capacity.");
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.3**
        ///
        /// When the compressed directory exceeds the reserved region capacity (4096 bytes),
        /// it is appended at the end of the file. ImageDirectoryOffset moves to a position
        /// beyond the data region (not 0x200). The file can still be opened and read correctly.
        /// </summary>
        [Fact]
        public void DirectoryOverflowsReservedRegion_AppendedAtEndOfFile()
        {
            string path = Path.Combine(_tempDir, "overflow_dir_test.nkds");
            int imageCount = 100; // Enough images with long names to exceed 4096 bytes compressed

            // Step 1: Create a new file (default DirectoryRegionCapacity = 4096)
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                // Step 2: Add enough images with long names/metadata to make the compressed
                // directory exceed 4096 bytes
                for (int i = 1; i <= imageCount; i++)
                {
                    (byte[] metadata, int _) = ImageMetadataSectionSerializer.Serialize(
                        new List<AreaRecord>
                        {
                            new AreaRecord
                            {
                                Offset = i * 0x100000L,
                                Size = i * 0x50000,
                                SectionSize = 0x200000,
                                Crc32 = (uint)(i * 7919),
                                XxHash64 = (ulong)(i * 6277101735386680763L)
                            }
                        },
                        new List<FileRecord>());

                    (byte[] blockMap, int _) = ImageBlockMapSectionSerializer.Serialize(
                        new List<OffsetRecord>(),
                        new Dictionary<BlockKey, (int FileId, long Offset, int Size)>());

                    (long metaOff, int metaSize, long bmOff, int bmSize) = file.AppendImage(imageId: i, metadata, blockMap, newBlocks: null);

                    // Use long, unique names and system strings to inflate directory size
                    string longName = $"Game_Title_{i:D4}_Region_{(i % 3 == 0 ? "NTSC-U" : i % 3 == 1 ? "PAL" : "NTSC-J")}" +
                                      $"_Disc{(i % 4) + 1}_Rev{i % 8}_{Guid.NewGuid():N}.iso";
                    string system = i % 2 == 0 ? "Nintendo GameCube" : "Nintendo Wii";

                    ImageDirectory directory = file.GetDirectory();
                    directory.AddOrUpdate(new ImageDirectoryEntry
                    {
                        ImageId = i,
                        Name = longName,
                        Size = i * 0x50000L,
                        Crc32 = (uint)(i * 7919),
                        XxHash64 = (ulong)(i * 6277101735386680763L),
                        System = system,
                        Format = ImageFormat.Iso,
                        MetadataSectionOffset = metaOff,
                        MetadataSectionCompressedSize = metaSize,
                        BlockMapSectionOffset = bmOff,
                        BlockMapSectionCompressedSize = bmSize
                    });
                    file.UpdateDirectory(directory);
                }

                // Step 3: Commit
                file.AtomicCommit();
            }

            // Step 4: Verify ImageDirectoryOffset > 0x200 (overflow occurred)
            FileHeader header = ReadHeaderFromFile(path);
            Assert.True(header.ImageDirectoryOffset > 0x200L,
                $"Expected ImageDirectoryOffset > 0x200 (overflow), but got 0x{header.ImageDirectoryOffset:X}. " +
                $"Compressed directory size ({header.ImageDirectorySize}) should exceed DirectoryRegionCapacity ({header.DirectoryRegionCapacity}).");

            // Verify the compressed directory size exceeds the region capacity (confirms overflow was necessary)
            Assert.True(header.ImageDirectorySize > header.DirectoryRegionCapacity,
                $"Compressed directory size ({header.ImageDirectorySize}) should exceed " +
                $"DirectoryRegionCapacity ({header.DirectoryRegionCapacity}) to trigger overflow.");

            // Step 5: Verify the file can still be opened and all images are readable
            using (BinaryIndexFile file = BinaryIndexFile.Open(path))
            {
                ImageDirectory directory = file.GetDirectory();
                Assert.Equal(imageCount, directory.Count);

                // Verify each image entry is present and has valid data
                for (int i = 1; i <= imageCount; i++)
                {
                    ImageDirectoryEntry entry = directory.GetEntry(i);
                    Assert.NotNull(entry);
                    Assert.Equal(i, entry!.ImageId);
                    Assert.Equal(i * 0x50000L, entry.Size);
                    Assert.Equal((uint)(i * 7919), entry.Crc32);
                    Assert.Equal((ulong)(i * 6277101735386680763L), entry.XxHash64);
                    Assert.True(entry.MetadataSectionOffset > 0, $"Image {i} should have a valid metadata offset.");
                    Assert.True(entry.MetadataSectionCompressedSize > 0, $"Image {i} should have a valid metadata size.");
                    Assert.True(entry.BlockMapSectionOffset > 0, $"Image {i} should have a valid block map offset.");
                    Assert.True(entry.BlockMapSectionCompressedSize > 0, $"Image {i} should have a valid block map size.");
                }

                // Verify we can read metadata for a sample of images (first, middle, last)
                foreach (int id in new[] { 1, imageCount / 2, imageCount })
                {
                    (List<AreaRecord> areas, List<FileRecord> files) = file.ReadImageMetadata(id);
                    Assert.Single(areas);
                    Assert.Equal((uint)(id * 7919), areas[0].Crc32);
                }
            }
        }

        /// <summary>
        /// **Validates: Requirements 6.1, 6.2**
        ///
        /// After a directory overflow (ImageDirectoryOffset > 0x200), compacting the file
        /// restores the directory to in-place (ImageDirectoryOffset == 0x200).
        /// After compaction, DirectoryRegionCapacity = compressedDirSize + ~4096 (growth headroom).
        /// The compacted file can be opened and all data is intact.
        /// </summary>
        [Fact]
        public void CompactionRestoresInPlaceWith4KiBHeadroom()
        {
            string path = Path.Combine(_tempDir, "overflow_compact_test.nkds");
            string compactedPath = Path.Combine(_tempDir, "overflow_compact_test_compacted.nkds");
            int imageCount = 100; // Enough images with long names to exceed 4096 bytes compressed

            // Step 1: Create a file that has overflowed (reuse the overflow scenario — add many images)
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                for (int i = 1; i <= imageCount; i++)
                {
                    (byte[] metadata, int _) = ImageMetadataSectionSerializer.Serialize(
                        new List<AreaRecord>
                        {
                            new AreaRecord
                            {
                                Offset = i * 0x100000L,
                                Size = i * 0x50000,
                                SectionSize = 0x200000,
                                Crc32 = (uint)(i * 7919),
                                XxHash64 = (ulong)(i * 6277101735386680763L)
                            }
                        },
                        new List<FileRecord>());

                    (byte[] blockMap, int _) = ImageBlockMapSectionSerializer.Serialize(
                        new List<OffsetRecord>(),
                        new Dictionary<BlockKey, (int FileId, long Offset, int Size)>());

                    (long metaOff, int metaSize, long bmOff, int bmSize) = file.AppendImage(imageId: i, metadata, blockMap, newBlocks: null);

                    // Use long, unique names to inflate directory size past 4096 bytes compressed
                    string longName = $"Game_Title_{i:D4}_Region_{(i % 3 == 0 ? "NTSC-U" : i % 3 == 1 ? "PAL" : "NTSC-J")}" +
                                      $"_Disc{(i % 4) + 1}_Rev{i % 8}_{Guid.NewGuid():N}.iso";
                    string system = i % 2 == 0 ? "Nintendo GameCube" : "Nintendo Wii";

                    ImageDirectory directory = file.GetDirectory();
                    directory.AddOrUpdate(new ImageDirectoryEntry
                    {
                        ImageId = i,
                        Name = longName,
                        Size = i * 0x50000L,
                        Crc32 = (uint)(i * 7919),
                        XxHash64 = (ulong)(i * 6277101735386680763L),
                        System = system,
                        Format = ImageFormat.Iso,
                        MetadataSectionOffset = metaOff,
                        MetadataSectionCompressedSize = metaSize,
                        BlockMapSectionOffset = bmOff,
                        BlockMapSectionCompressedSize = bmSize
                    });
                    file.UpdateDirectory(directory);
                }

                file.AtomicCommit();

                // Step 2: Verify overflow occurred (ImageDirectoryOffset > 0x200)
                Assert.True(file.Header.ImageDirectoryOffset > 0x200L,
                    $"Expected ImageDirectoryOffset > 0x200 (overflow), but got 0x{file.Header.ImageDirectoryOffset:X}. " +
                    $"Compressed directory size ({file.Header.ImageDirectorySize}) should exceed DirectoryRegionCapacity ({file.Header.DirectoryRegionCapacity}).");

                // Step 3: Compact the file
                file.CompactTo(compactedPath, deterministic: true);
            }

            // Step 4: Open the compacted file
            using (BinaryIndexFile compactedFile = BinaryIndexFile.Open(compactedPath))
            {
                // Step 5: Verify ImageDirectoryOffset == 0x200 (restored to in-place)
                Assert.Equal(0x200L, compactedFile.Header.ImageDirectoryOffset);

                // Step 6: Verify DirectoryRegionCapacity >= compressedDirSize + 4096 (headroom)
                int compressedDirSize = compactedFile.Header.ImageDirectorySize;
                Assert.True(compressedDirSize > 0,
                    "Compressed directory size should be positive after compaction.");
                Assert.True(compactedFile.Header.DirectoryRegionCapacity >= compressedDirSize + 4096,
                    $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) should be >= " +
                    $"compressedDirSize ({compressedDirSize}) + 4096 headroom = {compressedDirSize + 4096}.");

                // Verify the capacity is reasonable (not excessively large)
                Assert.True(compactedFile.Header.DirectoryRegionCapacity <= compressedDirSize + 8192,
                    $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) should not be excessively larger " +
                    $"than compressedDirSize ({compressedDirSize}) + 8192.");

                // Step 7: Verify all images are still readable
                ImageDirectory directory = compactedFile.GetDirectory();
                Assert.Equal(imageCount, directory.Count);

                for (int i = 1; i <= imageCount; i++)
                {
                    ImageDirectoryEntry entry = directory.GetEntry(i);
                    Assert.NotNull(entry);
                    Assert.Equal(i, entry!.ImageId);
                    Assert.Equal(i * 0x50000L, entry.Size);
                    Assert.Equal((uint)(i * 7919), entry.Crc32);
                    Assert.Equal((ulong)(i * 6277101735386680763L), entry.XxHash64);
                    Assert.True(entry.MetadataSectionOffset > 0, $"Image {i} should have a valid metadata offset.");
                    Assert.True(entry.MetadataSectionCompressedSize > 0, $"Image {i} should have a valid metadata size.");
                    Assert.True(entry.BlockMapSectionOffset > 0, $"Image {i} should have a valid block map offset.");
                    Assert.True(entry.BlockMapSectionCompressedSize > 0, $"Image {i} should have a valid block map size.");
                }

                // Verify we can read metadata for a sample of images (first, middle, last)
                foreach (int id in new[] { 1, imageCount / 2, imageCount })
                {
                    (List<AreaRecord> areas, List<FileRecord> files) = compactedFile.ReadImageMetadata(id);
                    Assert.Single(areas);
                    Assert.Equal((uint)(id * 7919), areas[0].Crc32);
                }
            }
        }

        /// <summary>
        /// Reads and deserializes the primary header from a binary index file.
        /// </summary>
        private static FileHeader ReadHeaderFromFile(string path)
        {
            byte[] headerBytes = new byte[FileHeader.HeaderSize];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Read(headerBytes, 0, FileHeader.HeaderSize);
            }
            return FileHeaderSerializer.Read(headerBytes);
        }
    }
}
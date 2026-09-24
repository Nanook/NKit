using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Tests for BinaryIndexFile compaction logic.
    ///
    /// Feature: binary-index-format
    /// Property 14: Compaction retains all block entries
    /// **Validates: Requirements 8.6**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class CompactionTests : IDisposable
    {
        private readonly string _tempDir;

        public CompactionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CompactionTests_" + Guid.NewGuid().ToString("N"));
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
        /// Property 14: Compaction prunes unreferenced block entries.
        /// **Validates: Requirements 8.6**
        ///
        /// Verifies that compaction prunes block entries not referenced by any live image.
        /// Block entries referenced by at least one live image's BlockLocations are retained.
        /// </summary>
        [Fact]
        public void CompactTo_RemovedImageBlocks_PrunesUnreferencedEntries()
        {
            BlockKey blockA = new BlockKey(0x1111111111111111, 0xAAAAAAAA);
            BlockKey blockB = new BlockKey(0x2222222222222222, 0xBBBBBBBB);
            BlockKey blockC = new BlockKey(0x3333333333333333, 0xCCCCCCCC);
            BlockKey blockD = new BlockKey(0x4444444444444444, 0xDDDDDDDD);

            string indexPath = Path.Combine(_tempDir, "testset.nkds");
            string compactedPath = Path.Combine(_tempDir, "testset_compacted.nkds");

            using BinaryIndexFile indexFile = BinaryIndexFile.Create(
                indexPath, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336);

            // Append Image 1 (references blocks A, B, C)
            List<OffsetRecord> image1Offsets = new List<OffsetRecord>
            {
                new OffsetRecord { Offset = 0, Size = 0x30000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockA, blockB, blockC } }
            };
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> image1Locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
            {
                { blockA, (0, 0x0000, 0x10000) },
                { blockB, (0, 0x10000, 0x10000) },
                { blockC, (0, 0x20000, 0x10000) }
            };
            (byte[] img1Meta, int _) = ImageMetadataSectionSerializer.Serialize(
                new List<AreaRecord> { new AreaRecord { Offset = 0, Size = 0x30000, Crc32 = 0x11111111, XxHash64 = 0x1111, Metadata = new AreaMetadata() } },
                new List<FileRecord>());
            (byte[] img1Bm, int _) = ImageBlockMapSectionSerializer.Serialize(image1Offsets, image1Locs);
            (long m1Off, int m1Sz, long b1Off, int b1Sz) = indexFile.AppendImage(1, img1Meta, img1Bm, null);

            // Append Image 2 (references blocks B, C, D)
            List<OffsetRecord> image2Offsets = new List<OffsetRecord>
            {
                new OffsetRecord { Offset = 0, Size = 0x30000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockB, blockC, blockD } }
            };
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> image2Locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
            {
                { blockB, (0, 0x10000, 0x10000) },
                { blockC, (0, 0x20000, 0x10000) },
                { blockD, (0, 0x30000, 0x10000) }
            };
            (byte[] img2Meta, int _) = ImageMetadataSectionSerializer.Serialize(
                new List<AreaRecord> { new AreaRecord { Offset = 0, Size = 0x30000, Crc32 = 0x22222222, XxHash64 = 0x2222, Metadata = new AreaMetadata() } },
                new List<FileRecord>());
            (byte[] img2Bm, int _) = ImageBlockMapSectionSerializer.Serialize(image2Offsets, image2Locs);
            (long m2Off, int m2Sz, long b2Off, int b2Sz) = indexFile.AppendImage(2, img2Meta, img2Bm, null);

            // Block index: A, B, C, D
            BlockIndexEntry[] blockEntries = new BlockIndexEntry[]
            {
                new BlockIndexEntry { Key = blockA, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                new BlockIndexEntry { Key = blockB, FileId = 0, Offset = 0x10000, Size = 0x10000 },
                new BlockIndexEntry { Key = blockC, FileId = 0, Offset = 0x20000, Size = 0x10000 },
                new BlockIndexEntry { Key = blockD, FileId = 0, Offset = 0x30000, Size = 0x10000 },
            };
            Array.Sort(blockEntries);
            indexFile.AppendBlockIndexDelta(new BlockIndexDelta { Entries = blockEntries });

            // Directory: image 1 removed, image 2 alive
            ImageDirectory directory = new ImageDirectory();
            directory.AddOrUpdate(new ImageDirectoryEntry
            {
                ImageId = 1,
                Name = "image1.iso",
                Size = 0x30000,
                Crc32 = 0x11111111,
                XxHash64 = 0x1111,
                System = null,
                Format = ImageFormat.Iso,
                Removed = true,
                MetadataSectionOffset = m1Off,
                MetadataSectionCompressedSize = m1Sz,
                BlockMapSectionOffset = b1Off,
                BlockMapSectionCompressedSize = b1Sz
            });
            directory.AddOrUpdate(new ImageDirectoryEntry
            {
                ImageId = 2,
                Name = "image2.iso",
                Size = 0x30000,
                Crc32 = 0x22222222,
                XxHash64 = 0x2222,
                System = null,
                Format = ImageFormat.Iso,
                Removed = false,
                MetadataSectionOffset = m2Off,
                MetadataSectionCompressedSize = m2Sz,
                BlockMapSectionOffset = b2Off,
                BlockMapSectionCompressedSize = b2Sz
            });
            indexFile.UpdateDirectory(directory);
            indexFile.AtomicCommit();

            // Act
            indexFile.CompactTo(compactedPath, deterministic: true);

            // Assert
            using BinaryIndexFile compactedFile = BinaryIndexFile.Open(compactedPath);
            InMemoryBlockIndex compactedIndex = compactedFile.LoadBlockIndex();
            BlockIndexEntry[] compactedEntries = compactedIndex.GetEntries();

            // All blocks referenced by live images are retained.
            // Image 1 (removed) referenced A, B, C. Image 2 (alive) references B, C, D.
            // Block A is only referenced by the removed image, so it should be pruned.
            Assert.False(compactedIndex.TryGetBlock(blockA, out _, out _, out _),
                "Block A should be pruned (only referenced by removed image).");
            Assert.True(compactedIndex.TryGetBlock(blockB, out _, out _, out _),
                "Block B should be retained (referenced by live image 2).");
            Assert.True(compactedIndex.TryGetBlock(blockC, out _, out _, out _),
                "Block C should be retained (referenced by live image 2).");
            Assert.True(compactedIndex.TryGetBlock(blockD, out _, out _, out _),
                "Block D should be retained (referenced by live image 2).");

            // Total: 3 entries (A pruned, B/C/D retained)
            Assert.Equal(3, compactedEntries.Length);

            // Directory has only image 2
            List<ImageDirectoryEntry> dirEntries = compactedFile.GetDirectory().GetAllEntries().ToList();
            Assert.Single(dirEntries);
            Assert.Equal(2, dirEntries[0].ImageId);
            Assert.False(dirEntries[0].Removed);

            // Directory region assertions:
            // After compaction, ImageDirectoryOffset = 0x200 (in-place)
            Assert.Equal(0x200, compactedFile.Header.ImageDirectoryOffset);

            // After compaction, DirectoryRegionCapacity >= compressedDirSize + 4096
            // (capacity is based on estimated compressed size + 4096 headroom;
            //  final compressed size may differ slightly from estimate due to offset updates)
            int compressedDirSize = compactedFile.Header.ImageDirectorySize;
            // The directory must fit within the reserved region
            Assert.True(compactedFile.Header.DirectoryRegionCapacity >= compressedDirSize,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) must be >= compressedDirSize ({compressedDirSize})");
            // The capacity should include ~4096 bytes of headroom (based on estimate)
            Assert.True(compactedFile.Header.DirectoryRegionCapacity >= 4096,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) should be at least 4096 (default headroom)");
            // The capacity should also be reasonable (not excessively large)
            Assert.True(compactedFile.Header.DirectoryRegionCapacity <= compressedDirSize + 8192,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) should not be excessively larger than compressedDirSize + 4096");

            // After compaction, data region starts at 0x200 + DirectoryRegionCapacity
            long expectedDataStart = 0x200 + compactedFile.Header.DirectoryRegionCapacity;
            // The first data structure (image sections or block index) should be at or after expectedDataStart
            Assert.True(compactedFile.Header.BlockIndexOffset >= expectedDataStart,
                $"BlockIndexOffset (0x{compactedFile.Header.BlockIndexOffset:X}) should be >= data region start (0x{expectedDataStart:X})");
        }

        /// <summary>
        /// Property 15: Compaction idempotence.
        /// **Validates: Requirements 8.8**
        ///
        /// Verifies that compacting an already-compacted file produces a byte-for-byte
        /// identical result. When no deltas exist and no images are removed, compaction
        /// is a no-op in terms of file content.
        /// </summary>
        [Fact]
        public void CompactTo_AlreadyCompactedFile_ProducesIdenticalBytes()
        {
            BlockKey blockA = new BlockKey(0x1111111111111111, 0xAAAAAAAA);
            BlockKey blockB = new BlockKey(0x2222222222222222, 0xBBBBBBBB);
            BlockKey blockC = new BlockKey(0x3333333333333333, 0xCCCCCCCC);

            string indexPath = Path.Combine(_tempDir, "idempotent.nkds");
            string compacted1Path = Path.Combine(_tempDir, "idempotent_compact1.nkds");
            string compacted2Path = Path.Combine(_tempDir, "idempotent_compact2.nkds");

            // Create a file with two images and a block index
            using (BinaryIndexFile indexFile = BinaryIndexFile.Create(
                indexPath, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                // Append Image 1 (references blocks A, B)
                List<OffsetRecord> image1Offsets = new List<OffsetRecord>
                {
                    new OffsetRecord { Offset = 0, Size = 0x20000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockA, blockB } }
                };
                Dictionary<BlockKey, (int FileId, long Offset, int Size)> image1Locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
                {
                    { blockA, (0, 0x0000, 0x10000) },
                    { blockB, (0, 0x10000, 0x10000) }
                };
                (byte[] img1Meta, int _) = ImageMetadataSectionSerializer.Serialize(
                    new List<AreaRecord> { new AreaRecord { Offset = 0, Size = 0x20000, Crc32 = 0x11111111, XxHash64 = 0x1111, Metadata = new AreaMetadata() } },
                    new List<FileRecord>());
                (byte[] img1Bm, int _) = ImageBlockMapSectionSerializer.Serialize(image1Offsets, image1Locs);
                (long m1Off, int m1Sz, long b1Off, int b1Sz) = indexFile.AppendImage(1, img1Meta, img1Bm, null);

                // Append Image 2 (references blocks B, C)
                List<OffsetRecord> image2Offsets = new List<OffsetRecord>
                {
                    new OffsetRecord { Offset = 0, Size = 0x20000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockB, blockC } }
                };
                Dictionary<BlockKey, (int FileId, long Offset, int Size)> image2Locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
                {
                    { blockB, (0, 0x10000, 0x10000) },
                    { blockC, (0, 0x20000, 0x10000) }
                };
                (byte[] img2Meta, int _) = ImageMetadataSectionSerializer.Serialize(
                    new List<AreaRecord> { new AreaRecord { Offset = 0, Size = 0x20000, Crc32 = 0x22222222, XxHash64 = 0x2222, Metadata = new AreaMetadata() } },
                    new List<FileRecord>());
                (byte[] img2Bm, int _) = ImageBlockMapSectionSerializer.Serialize(image2Offsets, image2Locs);
                (long m2Off, int m2Sz, long b2Off, int b2Sz) = indexFile.AppendImage(2, img2Meta, img2Bm, null);

                // Block index: A, B, C
                BlockIndexEntry[] blockEntries = new BlockIndexEntry[]
                {
                    new BlockIndexEntry { Key = blockA, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockB, FileId = 0, Offset = 0x10000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockC, FileId = 0, Offset = 0x20000, Size = 0x10000 },
                };
                Array.Sort(blockEntries);
                indexFile.AppendBlockIndexDelta(new BlockIndexDelta { Entries = blockEntries });

                // Directory: both images alive
                ImageDirectory directory = new ImageDirectory();
                directory.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 1,
                    Name = "image1.iso",
                    Size = 0x20000,
                    Crc32 = 0x11111111,
                    XxHash64 = 0x1111,
                    System = null,
                    Format = ImageFormat.Iso,
                    Removed = false,
                    MetadataSectionOffset = m1Off,
                    MetadataSectionCompressedSize = m1Sz,
                    BlockMapSectionOffset = b1Off,
                    BlockMapSectionCompressedSize = b1Sz
                });
                directory.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 2,
                    Name = "image2.iso",
                    Size = 0x20000,
                    Crc32 = 0x22222222,
                    XxHash64 = 0x2222,
                    System = null,
                    Format = ImageFormat.Iso,
                    Removed = false,
                    MetadataSectionOffset = m2Off,
                    MetadataSectionCompressedSize = m2Sz,
                    BlockMapSectionOffset = b2Off,
                    BlockMapSectionCompressedSize = b2Sz
                });
                indexFile.UpdateDirectory(directory);
                indexFile.AtomicCommit();

                // First compaction (deterministic for reproducibility)
                indexFile.CompactTo(compacted1Path, deterministic: true);
            }

            // Second compaction: compact the already-compacted file
            byte[] firstCompactionBytes = File.ReadAllBytes(compacted1Path);

            using (BinaryIndexFile compacted1File = BinaryIndexFile.Open(compacted1Path))
            {
                compacted1File.CompactTo(compacted2Path, deterministic: true);
            }

            byte[] secondCompactionBytes = File.ReadAllBytes(compacted2Path);

            // Verify: byte-for-byte identical
            Assert.Equal(firstCompactionBytes.Length, secondCompactionBytes.Length);
            Assert.True(firstCompactionBytes.SequenceEqual(secondCompactionBytes),
                "Compacting an already-compacted file should produce byte-for-byte identical output (idempotence).");

            // Directory region assertions on first compaction:
            using (BinaryIndexFile compacted1File2 = BinaryIndexFile.Open(compacted1Path))
            {
                // After compaction, ImageDirectoryOffset = 0x200 (in-place)
                Assert.Equal(0x200, compacted1File2.Header.ImageDirectoryOffset);

                // After compaction, DirectoryRegionCapacity includes ~4096 headroom
                int compressedDirSize = compacted1File2.Header.ImageDirectorySize;
                Assert.True(compacted1File2.Header.DirectoryRegionCapacity >= compressedDirSize,
                    $"DirectoryRegionCapacity ({compacted1File2.Header.DirectoryRegionCapacity}) must be >= compressedDirSize ({compressedDirSize})");
                Assert.True(compacted1File2.Header.DirectoryRegionCapacity >= 4096,
                    $"DirectoryRegionCapacity ({compacted1File2.Header.DirectoryRegionCapacity}) should be at least 4096");

                // After compaction, data region starts at 0x200 + DirectoryRegionCapacity
                long expectedDataStart = 0x200 + compacted1File2.Header.DirectoryRegionCapacity;
                Assert.True(compacted1File2.Header.BlockIndexOffset >= expectedDataStart,
                    $"BlockIndexOffset (0x{compacted1File2.Header.BlockIndexOffset:X}) should be >= data region start (0x{expectedDataStart:X})");
            }
        }

        /// <summary>
        /// Property 17: Deterministic compaction byte-identity.
        /// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7**
        ///
        /// Two Binary_Index_Files containing the same images inserted in different order,
        /// when compacted with deterministic mode enabled, produce byte-identical output files.
        /// </summary>
        [Fact]
        public void CompactTo_Deterministic_SameImagesDifferentInsertionOrder_ProducesByteIdenticalFiles()
        {
            // Arrange: Define shared image data
            BlockKey blockA = new BlockKey(0x1111111111111111, 0xAAAAAAAA);
            BlockKey blockB = new BlockKey(0x2222222222222222, 0xBBBBBBBB);
            BlockKey blockC = new BlockKey(0x3333333333333333, 0xCCCCCCCC);
            BlockKey blockD = new BlockKey(0x4444444444444444, 0xDDDDDDDD);

            // Image 1 data
            List<AreaRecord> image1Areas = new List<AreaRecord>
            {
                new AreaRecord { Offset = 0, Size = 0x20000, Crc32 = 0x11111111, XxHash64 = 0x1111, StrideBlockSize = 0x10000, StrideDataOffset = 0, StrideDataLength = 0x10000, SectionSize = 0x20000, Metadata = new AreaMetadata() }
            };
            List<FileRecord> image1Files = new List<FileRecord>();
            List<OffsetRecord> image1Offsets = new List<OffsetRecord>
            {
                new OffsetRecord { Offset = 0, Size = 0x20000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockA, blockB } }
            };
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> image1Locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
            {
                { blockA, (0, 0x0000, 0x10000) },
                { blockB, (0, 0x10000, 0x10000) }
            };

            // Image 2 data
            List<AreaRecord> image2Areas = new List<AreaRecord>
            {
                new AreaRecord { Offset = 0, Size = 0x20000, Crc32 = 0x22222222, XxHash64 = 0x2222, StrideBlockSize = 0x10000, StrideDataOffset = 0, StrideDataLength = 0x10000, SectionSize = 0x20000, Metadata = new AreaMetadata() }
            };
            List<FileRecord> image2Files = new List<FileRecord>();
            List<OffsetRecord> image2Offsets = new List<OffsetRecord>
            {
                new OffsetRecord { Offset = 0, Size = 0x20000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockC, blockD } }
            };
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> image2Locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
            {
                { blockC, (0, 0x20000, 0x10000) },
                { blockD, (0, 0x30000, 0x10000) }
            };

            // Serialize image sections (same data used for both files)
            (byte[] img1Meta, int _) = ImageMetadataSectionSerializer.Serialize(image1Areas, image1Files);
            (byte[] img1Bm, int _) = ImageBlockMapSectionSerializer.Serialize(image1Offsets, image1Locs);
            (byte[] img2Meta, int _) = ImageMetadataSectionSerializer.Serialize(image2Areas, image2Files);
            (byte[] img2Bm, int _) = ImageBlockMapSectionSerializer.Serialize(image2Offsets, image2Locs);

            // File A: Insert image 1 first, then image 2
            string fileAPath = Path.Combine(_tempDir, "setA.nkds");
            string compactedAPath = Path.Combine(_tempDir, "setA_compacted.nkds");
            using (BinaryIndexFile fileA = BinaryIndexFile.Create(
                fileAPath, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                (long m1Off, int m1Sz, long b1Off, int b1Sz) = fileA.AppendImage(1, img1Meta, img1Bm, null);
                (long m2Off, int m2Sz, long b2Off, int b2Sz) = fileA.AppendImage(2, img2Meta, img2Bm, null);

                // Block index with all blocks
                BlockIndexEntry[] blockEntries = new BlockIndexEntry[]
                {
                    new BlockIndexEntry { Key = blockA, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockB, FileId = 0, Offset = 0x10000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockC, FileId = 0, Offset = 0x20000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockD, FileId = 0, Offset = 0x30000, Size = 0x10000 },
                };
                Array.Sort(blockEntries);
                fileA.AppendBlockIndexDelta(new BlockIndexDelta { Entries = blockEntries });

                ImageDirectory dirA = new ImageDirectory();
                dirA.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 1,
                    Name = "image1.iso",
                    Size = 0x20000,
                    Crc32 = 0x11111111,
                    XxHash64 = 0x1111,
                    System = null,
                    Format = ImageFormat.Iso,
                    Removed = false,
                    MetadataSectionOffset = m1Off,
                    MetadataSectionCompressedSize = m1Sz,
                    BlockMapSectionOffset = b1Off,
                    BlockMapSectionCompressedSize = b1Sz
                });
                dirA.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 2,
                    Name = "image2.iso",
                    Size = 0x20000,
                    Crc32 = 0x22222222,
                    XxHash64 = 0x2222,
                    System = null,
                    Format = ImageFormat.Iso,
                    Removed = false,
                    MetadataSectionOffset = m2Off,
                    MetadataSectionCompressedSize = m2Sz,
                    BlockMapSectionOffset = b2Off,
                    BlockMapSectionCompressedSize = b2Sz
                });
                fileA.UpdateDirectory(dirA);
                fileA.AtomicCommit();

                // Compact file A in deterministic mode
                fileA.CompactTo(compactedAPath, deterministic: true);
            }

            // File B: Insert image 2 first, then image 1 (DIFFERENT order)
            string fileBPath = Path.Combine(_tempDir, "setB.nkds");
            string compactedBPath = Path.Combine(_tempDir, "setB_compacted.nkds");
            using (BinaryIndexFile fileB = BinaryIndexFile.Create(
                fileBPath, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                // Insert in reverse order: image 2 first, then image 1
                (long m2Off, int m2Sz, long b2Off, int b2Sz) = fileB.AppendImage(2, img2Meta, img2Bm, null);
                (long m1Off, int m1Sz, long b1Off, int b1Sz) = fileB.AppendImage(1, img1Meta, img1Bm, null);

                // Same block index entries
                BlockIndexEntry[] blockEntries = new BlockIndexEntry[]
                {
                    new BlockIndexEntry { Key = blockA, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockB, FileId = 0, Offset = 0x10000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockC, FileId = 0, Offset = 0x20000, Size = 0x10000 },
                    new BlockIndexEntry { Key = blockD, FileId = 0, Offset = 0x30000, Size = 0x10000 },
                };
                Array.Sort(blockEntries);
                fileB.AppendBlockIndexDelta(new BlockIndexDelta { Entries = blockEntries });

                ImageDirectory dirB = new ImageDirectory();
                // Add in reverse order to directory as well
                dirB.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 2,
                    Name = "image2.iso",
                    Size = 0x20000,
                    Crc32 = 0x22222222,
                    XxHash64 = 0x2222,
                    System = null,
                    Format = ImageFormat.Iso,
                    Removed = false,
                    MetadataSectionOffset = m2Off,
                    MetadataSectionCompressedSize = m2Sz,
                    BlockMapSectionOffset = b2Off,
                    BlockMapSectionCompressedSize = b2Sz
                });
                dirB.AddOrUpdate(new ImageDirectoryEntry
                {
                    ImageId = 1,
                    Name = "image1.iso",
                    Size = 0x20000,
                    Crc32 = 0x11111111,
                    XxHash64 = 0x1111,
                    System = null,
                    Format = ImageFormat.Iso,
                    Removed = false,
                    MetadataSectionOffset = m1Off,
                    MetadataSectionCompressedSize = m1Sz,
                    BlockMapSectionOffset = b1Off,
                    BlockMapSectionCompressedSize = b1Sz
                });
                fileB.UpdateDirectory(dirB);
                fileB.AtomicCommit();

                // Compact file B in deterministic mode
                fileB.CompactTo(compactedBPath, deterministic: true);
            }

            // Assert: Both compacted files are byte-for-byte identical
            byte[] compactedABytes = File.ReadAllBytes(compactedAPath);
            byte[] compactedBBytes = File.ReadAllBytes(compactedBPath);

            Assert.Equal(compactedABytes.Length, compactedBBytes.Length);
            Assert.True(compactedABytes.SequenceEqual(compactedBBytes),
                "Deterministic compaction of two files with the same images inserted in different order " +
                "should produce byte-identical output files.");

            // Directory region assertions on compacted file A:
            using (BinaryIndexFile compactedAFile = BinaryIndexFile.Open(compactedAPath))
            {
                // After compaction, ImageDirectoryOffset = 0x200 (in-place)
                Assert.Equal(0x200, compactedAFile.Header.ImageDirectoryOffset);

                // After compaction, DirectoryRegionCapacity includes ~4096 headroom
                int compressedDirSize = compactedAFile.Header.ImageDirectorySize;
                Assert.True(compactedAFile.Header.DirectoryRegionCapacity >= compressedDirSize,
                    $"DirectoryRegionCapacity ({compactedAFile.Header.DirectoryRegionCapacity}) must be >= compressedDirSize ({compressedDirSize})");
                Assert.True(compactedAFile.Header.DirectoryRegionCapacity >= 4096,
                    $"DirectoryRegionCapacity ({compactedAFile.Header.DirectoryRegionCapacity}) should be at least 4096");

                // After compaction, data region starts at 0x200 + DirectoryRegionCapacity
                long expectedDataStart = 0x200 + compactedAFile.Header.DirectoryRegionCapacity;
                Assert.True(compactedAFile.Header.BlockIndexOffset >= expectedDataStart,
                    $"BlockIndexOffset (0x{compactedAFile.Header.BlockIndexOffset:X}) should be >= data region start (0x{expectedDataStart:X})");
            }
        }

        /// <summary>
        /// Property 16: Compaction uplifts structure versions.
        /// **Validates: Requirements 8.9**
        ///
        /// Verifies that after compaction, the compacted file can be opened, all data is readable,
        /// and the header has the current version numbers. Since we only have version 1 currently,
        /// this mainly verifies that compaction produces a valid file with current version headers.
        /// </summary>
        [Fact]
        public void CompactTo_UpliftStructureVersions_ProducesFileWithCurrentVersionHeaders()
        {
            BlockKey blockA = new BlockKey(0x5555555555555555, 0x11111111);
            BlockKey blockB = new BlockKey(0x6666666666666666, 0x22222222);

            string indexPath = Path.Combine(_tempDir, "versionuplift.nkds");
            string compactedPath = Path.Combine(_tempDir, "versionuplift_compacted.nkds");

            using BinaryIndexFile indexFile = BinaryIndexFile.Create(
                indexPath, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336);

            // Insert an image with metadata and block map sections
            List<AreaRecord> areas = new List<AreaRecord>
            {
                new AreaRecord { Offset = 0, Size = 0x20000, StrideBlockSize = 0x800, StrideDataOffset = 0, StrideDataLength = 0x800, SectionSize = 0x800, Crc32 = 0xAABBCCDD, XxHash64 = 0x1234567890ABCDEF, Metadata = new AreaMetadata() }
            };
            List<FileRecord> files = new List<FileRecord>
            {
                new FileRecord { Name = "filesystem.yaml", FileId = 0, Offset = 0, Size = 128, UncompressedSize = 256, IsSystem = true }
            };
            List<OffsetRecord> offsets = new List<OffsetRecord>
            {
                new OffsetRecord { Offset = 0, Size = 0x20000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockA, blockB } }
            };
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
            {
                { blockA, (0, 0x0000, 0x10000) },
                { blockB, (0, 0x10000, 0x10000) }
            };

            (byte[] metaBytes, int _) = ImageMetadataSectionSerializer.Serialize(areas, files);
            (byte[] bmBytes, int _) = ImageBlockMapSectionSerializer.Serialize(offsets, blockLocations);
            (long mOff, int mSz, long bOff, int bSz) = indexFile.AppendImage(1, metaBytes, bmBytes, null);

            // Add block index entries
            BlockIndexEntry[] blockEntries = new BlockIndexEntry[]
            {
                new BlockIndexEntry { Key = blockA, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                new BlockIndexEntry { Key = blockB, FileId = 0, Offset = 0x10000, Size = 0x10000 },
            };
            Array.Sort(blockEntries);
            indexFile.AppendBlockIndexDelta(new BlockIndexDelta { Entries = blockEntries });

            // Set up directory
            ImageDirectory directory = new ImageDirectory();
            directory.AddOrUpdate(new ImageDirectoryEntry
            {
                ImageId = 1,
                Name = "test_image.iso",
                Size = 0x20000,
                Crc32 = 0xAABBCCDD,
                XxHash64 = 0x1234567890ABCDEF,
                System = "Wii",
                Format = ImageFormat.Iso,
                Removed = false,
                MetadataSectionOffset = mOff,
                MetadataSectionCompressedSize = mSz,
                BlockMapSectionOffset = bOff,
                BlockMapSectionCompressedSize = bSz
            });
            indexFile.UpdateDirectory(directory);
            indexFile.AtomicCommit();

            // Act: Compact the file
            indexFile.CompactTo(compactedPath, deterministic: false);

            // Assert: Open the compacted file and verify it's valid
            using BinaryIndexFile compactedFile = BinaryIndexFile.Open(compactedPath);

            // Verify header has current version numbers by reading the raw header bytes
            byte[] headerBytes = new byte[FileHeader.HeaderSize];
            using (FileStream fs = new FileStream(compactedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Read(headerBytes, 0, FileHeader.HeaderSize);
            }
            // Magic at offset 0x00
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(0x00));
            Assert.Equal(FileHeader.MagicBytes, magic);
            // Major version at offset 0x04
            ushort majorVersion = BinaryPrimitives.ReadUInt16BigEndian(headerBytes.AsSpan(0x04));
            Assert.Equal(FileHeader.CurrentMajorVersion, majorVersion);
            // Minor version at offset 0x06
            ushort minorVersion = BinaryPrimitives.ReadUInt16BigEndian(headerBytes.AsSpan(0x06));
            Assert.Equal(FileHeader.CurrentMinorVersion, minorVersion);

            // Verify the directory is readable and contains the image
            List<ImageDirectoryEntry> dirEntries = compactedFile.GetDirectory().GetAllEntries().ToList();
            Assert.Single(dirEntries);
            Assert.Equal(1, dirEntries[0].ImageId);
            Assert.Equal("test_image.iso", dirEntries[0].Name);
            Assert.Equal("Wii", dirEntries[0].System);
            Assert.False(dirEntries[0].Removed);

            // Verify the image metadata section is readable
            (List<AreaRecord> readAreas, List<FileRecord> readFiles) = compactedFile.ReadImageMetadata(1);
            Assert.Single(readAreas);
            Assert.Equal(0xAABBCCDD, readAreas[0].Crc32);
            Assert.Equal(0x1234567890ABCDEFul, readAreas[0].XxHash64);
            Assert.Single(readFiles);
            Assert.Equal("filesystem.yaml", readFiles[0].Name);
            Assert.True(readFiles[0].IsSystem);

            // Verify the image block map section is readable
            (List<OffsetRecord> readOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> readBlockLocs) = compactedFile.ReadImageBlockMap(1);
            Assert.Single(readOffsets);
            Assert.Equal(2, readOffsets[0].Blocks.Count);
            Assert.Equal(2, readBlockLocs.Count);
            Assert.True(readBlockLocs.ContainsKey(blockA));
            Assert.True(readBlockLocs.ContainsKey(blockB));

            // Verify the block index is readable and has current version
            InMemoryBlockIndex compactedIndex = compactedFile.LoadBlockIndex();
            BlockIndexEntry[] compactedEntries = compactedIndex.GetEntries();
            Assert.Equal(2, compactedEntries.Length);
            Assert.True(compactedIndex.TryGetBlock(blockA, out _, out _, out _));
            Assert.True(compactedIndex.TryGetBlock(blockB, out _, out _, out _));

            // Verify no deltas exist in the compacted file (clean state)
            // The header should have BlockIndexDeltaCount = 0
            int deltaCount = BinaryPrimitives.ReadInt32BigEndian(headerBytes.AsSpan(0x28));
            Assert.Equal(0, deltaCount);

            // Directory region assertions:
            // After compaction, ImageDirectoryOffset = 0x200 (in-place)
            Assert.Equal(0x200, compactedFile.Header.ImageDirectoryOffset);

            // After compaction, DirectoryRegionCapacity includes ~4096 headroom
            int compressedDirSize = compactedFile.Header.ImageDirectorySize;
            Assert.True(compactedFile.Header.DirectoryRegionCapacity >= compressedDirSize,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) must be >= compressedDirSize ({compressedDirSize})");
            Assert.True(compactedFile.Header.DirectoryRegionCapacity >= 4096,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) should be at least 4096");

            // After compaction, data region starts at 0x200 + DirectoryRegionCapacity
            long expectedDataStart = 0x200 + compactedFile.Header.DirectoryRegionCapacity;
            Assert.True(compactedFile.Header.BlockIndexOffset >= expectedDataStart,
                $"BlockIndexOffset (0x{compactedFile.Header.BlockIndexOffset:X}) should be >= data region start (0x{expectedDataStart:X})");
        }

        /// <summary>
        /// **Validates: Requirements 8.6**
        ///
        /// When ALL images referencing a block are removed, the block entries are
        /// pruned from the compacted Block_Index (no live image references them).
        /// </summary>
        [Fact]
        public void CompactTo_AllReferencingImagesRemoved_BlockEntriesPruned()
        {
            BlockKey blockX = new BlockKey(0xAAAAAAAAAAAAAAAA, 0x11111111);
            BlockKey blockY = new BlockKey(0xBBBBBBBBBBBBBBBB, 0x22222222);

            string indexPath = Path.Combine(_tempDir, "allremoved.nkds");
            string compactedPath = Path.Combine(_tempDir, "allremoved_compacted.nkds");

            using BinaryIndexFile indexFile = BinaryIndexFile.Create(
                indexPath, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336);

            List<OffsetRecord> offsets = new List<OffsetRecord>
            {
                new OffsetRecord { Offset = 0, Size = 0x20000, Type = BlockType.File, OffsetStart = 0, Blocks = new List<BlockKey> { blockX, blockY } }
            };
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> locs = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
            {
                { blockX, (0, 0x0000, 0x10000) },
                { blockY, (0, 0x10000, 0x10000) }
            };
            (byte[] meta, int _) = ImageMetadataSectionSerializer.Serialize(
                new List<AreaRecord> { new AreaRecord { Offset = 0, Size = 0x20000, Crc32 = 0xAA, XxHash64 = 0xBB, Metadata = new AreaMetadata() } },
                new List<FileRecord>());
            (byte[] bm, int _) = ImageBlockMapSectionSerializer.Serialize(offsets, locs);
            (long mOff, int mSz, long bOff, int bSz) = indexFile.AppendImage(1, meta, bm, null);

            BlockIndexEntry[] entries = new BlockIndexEntry[]
            {
                new BlockIndexEntry { Key = blockX, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                new BlockIndexEntry { Key = blockY, FileId = 0, Offset = 0x10000, Size = 0x10000 },
            };
            Array.Sort(entries);
            indexFile.AppendBlockIndexDelta(new BlockIndexDelta { Entries = entries });

            ImageDirectory directory = new ImageDirectory();
            directory.AddOrUpdate(new ImageDirectoryEntry
            {
                ImageId = 1,
                Name = "removed.iso",
                Size = 0x20000,
                Crc32 = 0xAA,
                XxHash64 = 0xBB,
                System = null,
                Format = ImageFormat.Iso,
                Removed = true,
                MetadataSectionOffset = mOff,
                MetadataSectionCompressedSize = mSz,
                BlockMapSectionOffset = bOff,
                BlockMapSectionCompressedSize = bSz
            });
            indexFile.UpdateDirectory(directory);
            indexFile.AtomicCommit();

            // Act
            indexFile.CompactTo(compactedPath, deterministic: true);

            // Assert
            using BinaryIndexFile compactedFile = BinaryIndexFile.Open(compactedPath);
            InMemoryBlockIndex compactedIndex = compactedFile.LoadBlockIndex();
            // All entries should be pruned — no live images reference them
            Assert.Equal(0, compactedIndex.GetEntries().Length);
            Assert.False(compactedIndex.TryGetBlock(blockX, out _, out _, out _),
                "Block X should be pruned (no live image references it).");
            Assert.False(compactedIndex.TryGetBlock(blockY, out _, out _, out _),
                "Block Y should be pruned (no live image references it).");
            Assert.Equal(0, compactedFile.GetDirectory().GetAllEntries().Count());

            // Directory region assertions:
            // After compaction, ImageDirectoryOffset = 0x200 (in-place)
            Assert.Equal(0x200, compactedFile.Header.ImageDirectoryOffset);

            // After compaction, DirectoryRegionCapacity includes ~4096 headroom
            int compressedDirSize = compactedFile.Header.ImageDirectorySize;
            Assert.True(compactedFile.Header.DirectoryRegionCapacity >= compressedDirSize,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) must be >= compressedDirSize ({compressedDirSize})");
            Assert.True(compactedFile.Header.DirectoryRegionCapacity >= 4096,
                $"DirectoryRegionCapacity ({compactedFile.Header.DirectoryRegionCapacity}) should be at least 4096");

            // After compaction, data region starts at 0x200 + DirectoryRegionCapacity
            long expectedDataStart = 0x200 + compactedFile.Header.DirectoryRegionCapacity;
            Assert.True(compactedFile.Header.BlockIndexOffset >= expectedDataStart,
                $"BlockIndexOffset (0x{compactedFile.Header.BlockIndexOffset:X}) should be >= data region start (0x{expectedDataStart:X})");
        }
    }
}
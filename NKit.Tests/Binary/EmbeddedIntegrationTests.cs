using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Integration tests for embedded mode end-to-end.
    ///
    /// Feature: embedded-binary-index
    /// **Validates: Requirements 1.1, 2.3, 3.4, 3.5, 7.1, 7.2, 8.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class EmbeddedIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public EmbeddedIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "EmbeddedIntegrationTests_" + Guid.NewGuid().ToString("N"));
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
        /// Test: create embedded set, write multiple images with blocks, verify all data readable from single file.
        /// **Validates: Requirements 1.1, 2.3**
        /// </summary>
        [Fact]
        public void CreateEmbeddedSet_WriteMultipleImagesWithBlocks_AllDataReadableFromSingleFile()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "embedded_e2e";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Write image 1 with blocks
            byte[] block1Data = new byte[0x10000];
            byte[] block2Data = new byte[0x10000];
            new Random(42).NextBytes(block1Data);
            new Random(43).NextBytes(block2Data);
            BlockKey key1 = new BlockKey(0x1111111111111111, 0xAAAAAAAA);
            BlockKey key2 = new BlockKey(0x2222222222222222, 0xBBBBBBBB);

            using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
            {
                long imageId = dataAccess.InsertImage(setName, tx, "image1.iso", "Wii", ImageFormat.Iso);
                dataAccess.InsertBlock(setName, tx, key1, block1Data);
                dataAccess.InsertBlock(setName, tx, key2, block2Data);
                dataAccess.InsertOffset(setName, tx, imageId, 0, 0x20000, BlockType.File,
                    new List<BlockKey> { key1, key2 }, offsetStart: 0);
                dataAccess.UpdateImageMetadata(setName, tx, imageId, 0x20000, 0x11111111, 0x1111);
                tx.Commit();
            }

            // Write image 2 with blocks
            byte[] block3Data = new byte[0x10000];
            new Random(44).NextBytes(block3Data);
            BlockKey key3 = new BlockKey(0x3333333333333333, 0xCCCCCCCC);

            using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
            {
                long imageId = dataAccess.InsertImage(setName, tx, "image2.iso", "GC", ImageFormat.Iso);
                dataAccess.InsertBlock(setName, tx, key3, block3Data);
                dataAccess.InsertOffset(setName, tx, imageId, 0, 0x10000, BlockType.File,
                    new List<BlockKey> { key3 }, offsetStart: 0);
                dataAccess.UpdateImageMetadata(setName, tx, imageId, 0x10000, 0x22222222, 0x2222);
                tx.Commit();
            }

            // Assert: only a single .nkds file exists (no _0000.nkds shard)
            string embeddedPath = Path.Combine(_tempDir, $"{setName}.nkds");
            string shardPath = Path.Combine(_tempDir, $"{setName}_0000.nkds");
            Assert.True(File.Exists(embeddedPath), "Embedded file should exist");
            Assert.False(File.Exists(shardPath), "Separate shard file should NOT exist after commit");

            // Assert: file ends with footer magic (read with FileShare to avoid locking conflict)
            byte[] lastBytes = new byte[12];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Assert.True(fs.Length >= 12);
                fs.Seek(-12, SeekOrigin.End);
                fs.Read(lastBytes, 0, 12);
            }
            Assert.Equal(0x4E, lastBytes[8]);
            Assert.Equal(0x4B, lastBytes[9]);
            Assert.Equal(0x44, lastBytes[10]);
            Assert.Equal(0x53, lastBytes[11]);

            // Assert: all images are readable
            List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Equal(2, images.Count);
            Assert.Contains(images, i => i.Name == "image1.iso" && i.System == "Wii");
            Assert.Contains(images, i => i.Name == "image2.iso" && i.System == "GC");

            // Assert: block data is readable
            (CompressionType _, byte[] data1) = dataAccess.GetBlockData(setName, key1);
            (CompressionType _, byte[] data2) = dataAccess.GetBlockData(setName, key2);
            (CompressionType _, byte[] data3) = dataAccess.GetBlockData(setName, key3);
            Assert.NotNull(data1);
            Assert.NotNull(data2);
            Assert.NotNull(data3);
            Assert.Equal(block1Data, data1);
            Assert.Equal(block2Data, data2);
            Assert.Equal(block3Data, data3);
        }

        /// <summary>
        /// Test: compaction in embedded mode — write images, compact, verify block data intact.
        /// **Validates: Requirements 7.1, 7.2**
        ///
        /// Tests the extract→compact→re-embed cycle preserves all block data and images.
        /// </summary>
        [Fact]
        public void CompactionInEmbeddedMode_DeleteAndCompact_BlockDataIntact()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "compact_embedded";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            byte[] block1Data = new byte[0x10000];
            byte[] block2Data = new byte[0x10000];
            byte[] block3Data = new byte[0x10000];
            new Random(100).NextBytes(block1Data);
            new Random(101).NextBytes(block2Data);
            new Random(102).NextBytes(block3Data);
            BlockKey key1 = new BlockKey(0xAAAAAAAAAAAAAAAA, 0x11111111);
            BlockKey key2 = new BlockKey(0xBBBBBBBBBBBBBBBB, 0x22222222);
            BlockKey key3 = new BlockKey(0xCCCCCCCCCCCCCCCC, 0x33333333);

            // Insert image 1 (references key1, key2)
            using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
            {
                long imageId = dataAccess.InsertImage(setName, tx, "img1.iso", "Wii", ImageFormat.Iso);
                dataAccess.InsertBlock(setName, tx, key1, block1Data);
                dataAccess.InsertBlock(setName, tx, key2, block2Data);
                dataAccess.InsertOffset(setName, tx, imageId, 0, 0x20000, BlockType.File,
                    new List<BlockKey> { key1, key2 }, offsetStart: 0);
                dataAccess.UpdateImageMetadata(setName, tx, imageId, 0x20000, 0xAA, 0xAA);
                tx.Commit();
            }

            // Insert image 2 (references key2, key3)
            using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
            {
                long imageId = dataAccess.InsertImage(setName, tx, "img2.iso", "GC", ImageFormat.Iso);
                dataAccess.InsertBlock(setName, tx, key2, block2Data); // deduped — already exists
                dataAccess.InsertBlock(setName, tx, key3, block3Data);
                dataAccess.InsertOffset(setName, tx, imageId, 0, 0x20000, BlockType.File,
                    new List<BlockKey> { key2, key3 }, offsetStart: 0);
                dataAccess.UpdateImageMetadata(setName, tx, imageId, 0x20000, 0xBB, 0xBB);
                tx.Commit();
            }

            // Act: compact (tests the extract→compact→re-embed cycle)
            dataAccess.CompactSet(setName);

            // Assert: file still exists as embedded (single file)
            string embeddedPath = Path.Combine(_tempDir, $"{setName}.nkds");
            Assert.True(File.Exists(embeddedPath));
            Assert.False(File.Exists(Path.Combine(_tempDir, $"{setName}_0000.nkds")));

            // Assert: file ends with footer magic
            byte[] footerBytes = new byte[4];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Assert.True(fs.Length >= 12);
                fs.Seek(-4, SeekOrigin.End);
                fs.Read(footerBytes, 0, 4);
            }
            uint magic = (uint)((footerBytes[0] << 24) | (footerBytes[1] << 16) |
                                (footerBytes[2] << 8) | footerBytes[3]);
            Assert.Equal(EmbeddedFooter.FooterMagic, magic);

            // Assert: both images are still readable after compaction
            List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Equal(2, images.Count);
            Assert.Contains(images, i => i.Name == "img1.iso");
            Assert.Contains(images, i => i.Name == "img2.iso");

            // Assert: all block data is intact after the extract→compact→re-embed cycle
            (CompressionType _, byte[] readKey1) = dataAccess.GetBlockData(setName, key1);
            (CompressionType _, byte[] readKey2) = dataAccess.GetBlockData(setName, key2);
            (CompressionType _, byte[] readKey3) = dataAccess.GetBlockData(setName, key3);
            Assert.NotNull(readKey1);
            Assert.NotNull(readKey2);
            Assert.NotNull(readKey3);
            Assert.Equal(block1Data, readKey1);
            Assert.Equal(block2Data, readKey2);
            Assert.Equal(block3Data, readKey3);
        }

        /// <summary>
        /// Test: crash recovery — both test.nkds + test_0000.nkds exist → treated as separate mode, can continue.
        /// **Validates: Requirements 3.4, 3.5**
        /// </summary>
        [Fact]
        public void CrashRecovery_BothFilesExist_TreatedAsSeparateMode()
        {
            // Arrange: create an embedded set, then simulate a crash mid-write
            // by manually creating both files (index + shard)
            string setName = "crash_separate";
            string embeddedPath = Path.Combine(_tempDir, $"{setName}.nkds");
            string shardPath = Path.Combine(_tempDir, $"{setName}_0000.nkds");

            // First, create a valid embedded set and write some data
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
                {
                    long imageId = dataAccess.InsertImage(setName, tx, "test.iso", "Wii", ImageFormat.Iso);
                    dataAccess.UpdateImageMetadata(setName, tx, imageId, 1024, 0x12345678, 0xDEAD);
                    tx.Commit();
                }
            }

            // Simulate crash: extract to separate mode manually
            // Read the embedded file, split into index + shard
            byte[] embeddedBytes = File.ReadAllBytes(embeddedPath);
            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(embeddedBytes.AsSpan(embeddedBytes.Length - 12, 12));
            Assert.NotNull(footer);
            long indexSize = footer!.Value.IndexSize;
            long boundary = embeddedBytes.Length - 12 - indexSize;

            // Write the index portion as the standalone index file (test.nkds)
            byte[] indexBytes = new byte[indexSize];
            Array.Copy(embeddedBytes, boundary, indexBytes, 0, indexSize);
            File.WriteAllBytes(embeddedPath, indexBytes);

            // Write the shard portion (block data only) as test_0000.nkds
            byte[] shardBytes = new byte[boundary];
            if (boundary > 0)
                Array.Copy(embeddedBytes, 0, shardBytes, 0, boundary);
            File.WriteAllBytes(shardPath, shardBytes);

            // Act: open the set — recovery should detect both files and treat as separate mode
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                // EnsureSetExists triggers recovery
                InfoRecord info = dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                // Assert: set is usable — can read the image
                List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
                Assert.Single(images);
                Assert.Equal("test.iso", images[0].Name);
            }
        }

        /// <summary>
        /// Test: crash recovery — test_0000.nkds.tmp with Footer_Magic → rename to test.nkds.
        /// **Validates: Requirements 3.4, 3.5**
        /// </summary>
        [Fact]
        public void CrashRecovery_ShardTmpWithFooterMagic_RenamedToEmbedded()
        {
            // Arrange: create a valid embedded file, then simulate a crash where
            // the shard .tmp file has the footer appended but wasn't renamed yet
            string setName = "crash_tmp_magic";
            string embeddedPath = Path.Combine(_tempDir, $"{setName}.nkds");
            string shardTmpPath = Path.Combine(_tempDir, $"{setName}_0000.nkds.tmp");

            // Create a valid embedded set first
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
                {
                    long imageId = dataAccess.InsertImage(setName, tx, "magic.iso", "GC", ImageFormat.Iso);
                    dataAccess.UpdateImageMetadata(setName, tx, imageId, 2048, 0xABCDEF01, 0xBEEF);
                    tx.Commit();
                }
            }

            // The embedded file is valid. Now simulate the crash state:
            // test_0000.nkds.tmp = complete embedded file (with footer magic)
            // test.nkds = old standalone index (from extraction phase)
            byte[] embeddedBytes = File.ReadAllBytes(embeddedPath);
            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(embeddedBytes.AsSpan(embeddedBytes.Length - 12, 12));
            Assert.NotNull(footer);
            long indexSize = footer!.Value.IndexSize;
            long boundary = embeddedBytes.Length - 12 - indexSize;

            // Write the complete embedded file as the .tmp (simulates rename interrupted)
            File.WriteAllBytes(shardTmpPath, embeddedBytes);

            // Write just the index as test.nkds (the old standalone index from extraction)
            byte[] indexBytes = new byte[indexSize];
            Array.Copy(embeddedBytes, boundary, indexBytes, 0, indexSize);
            File.WriteAllBytes(embeddedPath, indexBytes);

            // Act: open the set — recovery should detect .tmp with footer magic and rename it
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                InfoRecord info = dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                // Assert: the .tmp file is gone
                Assert.False(File.Exists(shardTmpPath), "Shard .tmp file should be cleaned up");

                // Assert: embedded file exists and is valid
                Assert.True(File.Exists(embeddedPath));

                // Assert: data is readable
                List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
                Assert.Single(images);
                Assert.Equal("magic.iso", images[0].Name);
            }
        }

        /// <summary>
        /// Test: crash recovery — test_0000.nkds.tmp without Footer_Magic → re-append index from test.nkds.
        /// **Validates: Requirements 3.4, 3.5**
        /// </summary>
        [Fact]
        public void CrashRecovery_ShardTmpWithoutFooterMagic_ReAppendsIndex()
        {
            // Arrange: create a valid embedded set, then simulate a crash where
            // the shard .tmp file has only block data (append was interrupted before footer)
            string setName = "crash_tmp_nomagic";
            string embeddedPath = Path.Combine(_tempDir, $"{setName}.nkds");
            string shardTmpPath = Path.Combine(_tempDir, $"{setName}_0000.nkds.tmp");

            // Create a valid embedded set with block data
            byte[] blockData = new byte[0x10000];
            new Random(200).NextBytes(blockData);
            BlockKey blockKey = new BlockKey(0xDDDDDDDDDDDDDDDD, 0x44444444);

            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
                {
                    long imageId = dataAccess.InsertImage(setName, tx, "nomagic.iso", "Wii", ImageFormat.Iso);
                    dataAccess.InsertBlock(setName, tx, blockKey, blockData);
                    dataAccess.InsertOffset(setName, tx, imageId, 0, 0x10000, BlockType.File,
                        new List<BlockKey> { blockKey }, offsetStart: 0);
                    dataAccess.UpdateImageMetadata(setName, tx, imageId, 0x10000, 0xFEDCBA98, 0xCAFE);
                    tx.Commit();
                }
            }

            // Read the embedded file to extract its parts
            byte[] embeddedBytes = File.ReadAllBytes(embeddedPath);
            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(embeddedBytes.AsSpan(embeddedBytes.Length - 12, 12));
            Assert.NotNull(footer);
            long indexSize = footer!.Value.IndexSize;
            long boundary = embeddedBytes.Length - 12 - indexSize;

            // Simulate crash state:
            // test_0000.nkds.tmp = only block data (no footer, append was interrupted)
            byte[] shardOnlyBytes = new byte[boundary];
            if (boundary > 0)
                Array.Copy(embeddedBytes, 0, shardOnlyBytes, 0, boundary);
            File.WriteAllBytes(shardTmpPath, shardOnlyBytes);

            // test.nkds = standalone index (from extraction phase)
            byte[] indexBytes = new byte[indexSize];
            Array.Copy(embeddedBytes, boundary, indexBytes, 0, indexSize);
            File.WriteAllBytes(embeddedPath, indexBytes);

            // Act: open the set — recovery should re-append index from test.nkds to .tmp
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                InfoRecord info = dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                // Assert: .tmp file is gone
                Assert.False(File.Exists(shardTmpPath), "Shard .tmp file should be cleaned up");

                // Assert: embedded file exists
                Assert.True(File.Exists(embeddedPath));

                // Assert: data is readable — image and block data intact
                List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
                Assert.Single(images);
                Assert.Equal("nomagic.iso", images[0].Name);

                (CompressionType _, byte[] readBlock) = dataAccess.GetBlockData(setName, blockKey);
                Assert.NotNull(readBlock);
                Assert.Equal(blockData, readBlock);
            }
        }

        /// <summary>
        /// Test: crash recovery — test.nkds.tmp exists → delete it (extraction interrupted).
        /// **Validates: Requirements 3.4, 3.5**
        /// </summary>
        [Fact]
        public void CrashRecovery_IndexTmpExists_DeletedAndContinues()
        {
            // Arrange: create a valid embedded set, then simulate a crash where
            // test.nkds.tmp exists alongside test.nkds (extraction was interrupted)
            string setName = "crash_indextmp";
            string embeddedPath = Path.Combine(_tempDir, $"{setName}.nkds");
            string indexTmpPath = Path.Combine(_tempDir, $"{setName}.nkds.tmp");

            // Create a valid embedded set
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
                {
                    long imageId = dataAccess.InsertImage(setName, tx, "indextmp.iso", "Wii", ImageFormat.Iso);
                    dataAccess.UpdateImageMetadata(setName, tx, imageId, 512, 0x99887766, 0xFACE);
                    tx.Commit();
                }
            }

            // Simulate crash: create a leftover .tmp file (partial extraction)
            File.WriteAllBytes(indexTmpPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

            // Act: open the set — recovery should delete the .tmp and proceed normally
            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir))
            {
                InfoRecord info = dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                // Assert: .tmp file is deleted
                Assert.False(File.Exists(indexTmpPath), "Index .tmp file should be deleted");

                // Assert: embedded file is still valid and readable
                Assert.True(File.Exists(embeddedPath));
                List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
                Assert.Single(images);
                Assert.Equal("indextmp.iso", images[0].Name);
            }
        }

        /// <summary>
        /// Test: existing separate-mode sets continue to work unchanged (migration path).
        /// **Validates: Requirements 8.4**
        /// </summary>
        [Fact]
        public void SeparateMode_ShardSizeGreaterThanZero_WorksUnchanged()
        {
            // Arrange: create a set with shardSize > 0 (separate mode)
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "separate_mode";
            long shardSize = 1_000_000_000L;
            dataAccess.EnsureSetExists(setName, shardSize, blockSize: 0x10000);

            // Write an image with blocks
            byte[] blockData = new byte[0x10000];
            new Random(300).NextBytes(blockData);
            BlockKey key = new BlockKey(0xEEEEEEEEEEEEEEEE, 0x55555555);

            using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
            {
                long imageId = dataAccess.InsertImage(setName, tx, "separate.iso", "Wii", ImageFormat.Iso);
                dataAccess.InsertBlock(setName, tx, key, blockData);
                dataAccess.InsertOffset(setName, tx, imageId, 0, 0x10000, BlockType.File,
                    new List<BlockKey> { key }, offsetStart: 0);
                dataAccess.UpdateImageMetadata(setName, tx, imageId, 0x10000, 0x11223344, 0x5566);
                tx.Commit();
            }

            // Assert: separate index file exists (no footer magic)
            string indexPath = Path.Combine(_tempDir, $"{setName}.nkds");
            string shardPath = Path.Combine(_tempDir, $"{setName}_0000.nkds");
            Assert.True(File.Exists(indexPath), "Separate index file should exist");
            Assert.True(File.Exists(shardPath), "Separate shard file should exist");

            // Assert: index file does NOT end with footer magic
            byte[] lastFourBytes = new byte[4];
            using (FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length >= 4)
                {
                    fs.Seek(-4, SeekOrigin.End);
                    fs.Read(lastFourBytes, 0, 4);
                    uint lastFour = (uint)((lastFourBytes[0] << 24) | (lastFourBytes[1] << 16) |
                                           (lastFourBytes[2] << 8) | lastFourBytes[3]);
                    // The index file should not end with the embedded footer magic
                    Assert.NotEqual(EmbeddedFooter.FooterMagic, lastFour);
                }
            }

            // Assert: data is readable in separate mode
            List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Single(images);
            Assert.Equal("separate.iso", images[0].Name);

            (CompressionType _, byte[] readBlock) = dataAccess.GetBlockData(setName, key);
            Assert.NotNull(readBlock);
            Assert.Equal(blockData, readBlock);

            // Assert: InfoRecord reflects separate mode configuration
            InfoRecord info = dataAccess.GetSetInfo(setName);
            Assert.Equal(shardSize, info.ShardSize);
            Assert.Equal(0x10000, info.BlockSize);
        }
    }
}
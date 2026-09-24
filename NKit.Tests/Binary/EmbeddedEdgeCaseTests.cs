using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using System;
using System.Buffers.Binary;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Unit tests for embedded mode edge cases.
    /// Tests detection, boundary enforcement, initialization, and error handling.
    ///
    /// Feature: embedded-binary-index
    /// **Validates: Requirements 1.5, 2.5, 2.6, 5.4, 6.4, 8.2, 8.3, 8.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class EmbeddedEdgeCaseTests : IDisposable
    {
        private readonly string _tempDir;

        public EmbeddedEdgeCaseTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "NKit_EmbEdgeCase_" + Guid.NewGuid().ToString("N"));
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
        /// Requirement 1.5: Separate mode unchanged (shardSize > 0, no footer, separate files work).
        /// When shardSize > 0, the system creates a separate index file with no EmbeddedFooter.
        /// </summary>
        [Fact]
        public void SeparateMode_ShardSizeGreaterThanZero_NoFooterCreated()
        {
            string setDir = Path.Combine(_tempDir, "separate_mode");
            Directory.CreateDirectory(setDir);

            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir))
            {
                dataAccess.EnsureSetExists("test", shardSize: 1024 * 1024, blockSize: 0x10000);
            }

            // The index file should exist without an embedded footer
            string indexPath = Path.Combine(setDir, "test.nkds");
            Assert.True(File.Exists(indexPath), "Index file should exist in separate mode");

            byte[] fileBytes = File.ReadAllBytes(indexPath);
            Assert.True(fileBytes.Length >= 12, "Index file should be at least 12 bytes");

            // Last 4 bytes should NOT be the footer magic (separate mode has no footer)
            ReadOnlySpan<byte> lastFour = fileBytes.AsSpan(fileBytes.Length - 4, 4);
            Assert.False(EmbeddedFooter.IsMagicValid(lastFour),
                "Separate mode index file should NOT end with EmbeddedFooter magic");
        }

        /// <summary>
        /// Requirement 6.4: EnsureSetExists with shardSize=0 creates an embedded file.
        /// The file should contain an empty binary index followed by an EmbeddedFooter.
        /// </summary>
        [Fact]
        public void EmbeddedMode_EnsureSetExists_CreatesEmbeddedFile()
        {
            string setDir = Path.Combine(_tempDir, "embedded_create");
            Directory.CreateDirectory(setDir);

            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir))
            {
                InfoRecord info = dataAccess.EnsureSetExists("test", shardSize: 0, blockSize: 0x10000);
                Assert.NotNull(info);
            }

            // The embedded file should exist with a valid footer
            string embeddedPath = Path.Combine(setDir, "test.nkds");
            Assert.True(File.Exists(embeddedPath), "Embedded file should exist");

            byte[] fileBytes = File.ReadAllBytes(embeddedPath);
            Assert.True(fileBytes.Length >= EmbeddedFooter.FooterSize,
                "Embedded file should be at least 12 bytes (footer size)");

            // Last 4 bytes should be the footer magic
            ReadOnlySpan<byte> lastFour = fileBytes.AsSpan(fileBytes.Length - 4, 4);
            Assert.True(EmbeddedFooter.IsMagicValid(lastFour),
                "Embedded file should end with EmbeddedFooter magic");

            // Deserialize the footer and verify Shard_Boundary = 0 (no block data)
            ReadOnlySpan<byte> footerSpan = fileBytes.AsSpan(fileBytes.Length - EmbeddedFooter.FooterSize);
            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerSpan);
            Assert.NotNull(footer);

            long indexSize = footer!.Value.IndexSize;
            long shardBoundary = fileBytes.Length - EmbeddedFooter.FooterSize - indexSize;
            Assert.Equal(0, shardBoundary);
        }

        /// <summary>
        /// Requirement 2.6: Detection on plain shard file (no magic → separate mode).
        /// A file without the footer magic at the end should not be detected as embedded.
        /// </summary>
        [Fact]
        public void Detection_PlainShardFile_NoMagic_TreatedAsSeparateMode()
        {
            // Create a file with random content that does NOT end with footer magic
            string filePath = Path.Combine(_tempDir, "plain_shard.nkds");
            byte[] content = new byte[1024];
            new Random(42).NextBytes(content);

            // Ensure last 4 bytes are NOT the footer magic
            content[^4] = 0x00;
            content[^3] = 0x00;
            content[^2] = 0x00;
            content[^1] = 0x00;

            File.WriteAllBytes(filePath, content);

            // EmbeddedFooter.Deserialize should return null (no valid footer)
            ReadOnlySpan<byte> lastTwelve = content.AsSpan(content.Length - EmbeddedFooter.FooterSize);
            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(lastTwelve);
            Assert.Null(footer);

            // IsMagicValid should return false
            ReadOnlySpan<byte> lastFour = content.AsSpan(content.Length - 4, 4);
            Assert.False(EmbeddedFooter.IsMagicValid(lastFour));
        }

        /// <summary>
        /// Requirement 8.2: Detection on small file (< 12 bytes → not embedded).
        /// Files smaller than 12 bytes cannot contain an embedded footer.
        /// </summary>
        [Fact]
        public void Detection_SmallFile_LessThan12Bytes_NotEmbedded()
        {
            // Test various sizes from 0 to 11 bytes
            for (int size = 0; size < 12; size++)
            {
                byte[] content = new byte[size];
                if (size >= 4)
                {
                    // Even if we put magic bytes at the end, it's still too small for a full footer
                    BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(size - 4, 4), EmbeddedFooter.FooterMagic);
                }

                // Deserialize should return null for any buffer < 12 bytes
                EmbeddedFooter? footer = EmbeddedFooter.Deserialize(content.AsSpan());
                Assert.Null(footer);
            }
        }

        /// <summary>
        /// Requirement 2.5: Invalid footer (IndexSize too large) → throws InvalidDataException.
        /// When the IndexSize in the footer exceeds file_size - 12, an error should be raised.
        /// </summary>
        [Fact]
        public void InvalidFooter_IndexSizeTooLarge_ThrowsInvalidDataException()
        {
            string setDir = Path.Combine(_tempDir, "invalid_footer");
            Directory.CreateDirectory(setDir);
            string filePath = Path.Combine(setDir, "test.nkds");

            // Create a small file (e.g., 100 bytes) with a valid footer magic
            // but an IndexSize that exceeds file_size - 12
            byte[] fileContent = new byte[100];
            new Random(123).NextBytes(fileContent);

            // Write a footer with IndexSize = 200 (exceeds 100 - 12 = 88)
            long invalidIndexSize = 200;
            BinaryPrimitives.WriteInt64BigEndian(fileContent.AsSpan(88, 8), invalidIndexSize);
            BinaryPrimitives.WriteUInt32BigEndian(fileContent.AsSpan(96, 4), EmbeddedFooter.FooterMagic);

            File.WriteAllBytes(filePath, fileContent);

            // Opening this file with BinaryDataStoreDataAccess should throw InvalidDataException
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            {
                using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir);
                dataAccess.EnsureSetExists("test", shardSize: 0, blockSize: 0x10000);
            });

            Assert.Contains("IndexSize", ex.Message);
        }

        /// <summary>
        /// Requirement 5.4: Block offset beyond boundary → throws InvalidOperationException.
        /// ShardFileManager should reject reads at or beyond the shard boundary.
        /// </summary>
        [Fact]
        public void BlockOffset_BeyondBoundary_ThrowsInvalidOperationException()
        {
            string setDir = Path.Combine(_tempDir, "boundary_test");
            Directory.CreateDirectory(setDir);

            // Create a shard file with some data
            string shardPath = Path.Combine(setDir, "test_0000.nkds");
            byte[] shardData = new byte[1024];
            new Random(99).NextBytes(shardData);
            File.WriteAllBytes(shardPath, shardData);

            // Create a ShardFileManager with a boundary set at 512
            using ShardFileManager sfm = new ShardFileManager(setDir, "test", shardSize: 0, blockSize: 0x10000);
            sfm.SetShardBoundary(512);

            // Reading at offset 512 (= boundary) should throw
            Assert.Throws<InvalidOperationException>(() => sfm.ReadBlock(0, 512, 10));

            // Reading at offset 600 (> boundary) should throw
            Assert.Throws<InvalidOperationException>(() => sfm.ReadBlock(0, 600, 10));

            // Reading at offset 0 (< boundary) should succeed
            byte[] result = sfm.ReadBlock(0, 0, 10);
            Assert.Equal(10, result.Length);
            Assert.Equal(shardData.AsSpan(0, 10).ToArray(), result);
        }

        /// <summary>
        /// Requirement 8.3: No _0000.nkds shard file lookup in embedded mode.
        /// In embedded mode, the system should NOT create or look for a separate shard file.
        /// </summary>
        [Fact]
        public void EmbeddedMode_NoShardFileLookup()
        {
            string setDir = Path.Combine(_tempDir, "no_shard_lookup");
            Directory.CreateDirectory(setDir);

            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir))
            {
                dataAccess.EnsureSetExists("test", shardSize: 0, blockSize: 0x10000);
            }

            // Verify that no _0000.nkds file was created
            string shardPath = Path.Combine(setDir, "test_0000.nkds");
            Assert.False(File.Exists(shardPath),
                "Embedded mode should NOT create a _0000.nkds shard file");

            // Only the main .nkds file should exist
            string embeddedPath = Path.Combine(setDir, "test.nkds");
            Assert.True(File.Exists(embeddedPath),
                "Only the main .nkds file should exist in embedded mode");
        }

        /// <summary>
        /// Requirement 8.4: Temp file cleanup after successful write.
        /// After a successful write transaction in embedded mode, no temporary files should remain.
        /// </summary>
        [Fact]
        public void EmbeddedMode_TempFileCleanup_AfterSuccessfulWrite()
        {
            string setDir = Path.Combine(_tempDir, "temp_cleanup");
            Directory.CreateDirectory(setDir);

            using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir))
            {
                dataAccess.EnsureSetExists("test", shardSize: 0, blockSize: 0x10000);

                // Perform a write transaction
                using (IDataStoreTransaction tx = dataAccess.BeginTransaction("test"))
                {
                    long imageId = dataAccess.InsertImage("test", tx, "image1.iso", "Wii", ImageFormat.Iso);
                    byte[] blockData = new byte[0x10000];
                    new Random(42).NextBytes(blockData);
                    BlockKey key = new BlockKey(0x1111111111111111, 0xAAAAAAAA);
                    dataAccess.InsertBlock("test", tx, key, blockData);
                    dataAccess.InsertOffset("test", tx, imageId, 0, blockData.Length, BlockType.File, key, 0, 0);
                    dataAccess.InsertArea("test", tx, imageId, 0, blockData.Length, 0xDEADBEEF, 0x123456789ABCDEF0);
                    dataAccess.UpdateImageMetadata("test", tx, imageId, blockData.Length, 0xDEADBEEF, 0x123456789ABCDEF0);
                    tx.Commit();
                }
            }

            // After commit, verify no temp files remain
            string[] remainingFiles = Directory.GetFiles(setDir);
            string embeddedPath = Path.Combine(setDir, "test.nkds");

            // Only the main embedded file should exist
            Assert.Single(remainingFiles);
            Assert.Equal(embeddedPath, remainingFiles[0]);

            // Specifically check that no .tmp or _0000 files exist
            Assert.False(File.Exists(Path.Combine(setDir, "test.nkds.tmp")),
                "No .tmp file should remain after successful write");
            Assert.False(File.Exists(Path.Combine(setDir, "test_0000.nkds")),
                "No _0000.nkds file should remain after successful write");
            Assert.False(File.Exists(Path.Combine(setDir, "test_0000.nkds.tmp")),
                "No _0000.nkds.tmp file should remain after successful write");
        }
    }
}
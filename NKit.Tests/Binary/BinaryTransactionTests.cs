using NKitDataStore;
using NKitDataStore.Binary;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Tests for BinaryTransaction commit/rollback semantics.
    ///
    /// Feature: binary-index-format
    /// Property 18: Transaction commit/rollback semantics
    /// **Validates: Requirements 10.4**
    ///
    /// *For any* set of pending changes (new image, areas, offsets, blocks, files) accumulated
    /// in a BinaryTransaction: (a) after Commit(), all changes SHALL be visible to subsequent
    /// read operations; (b) after Rollback(), the Binary_Index_File SHALL remain at its
    /// pre-transaction state with no changes visible.
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class BinaryTransactionTests : IDisposable
    {
        private readonly string _tempDir;

        public BinaryTransactionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BinaryTransactionTests_" + Guid.NewGuid().ToString("N"));
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
        /// **Validates: Requirements 10.4**
        ///
        /// After Commit(), the image appears in the directory and the file has grown
        /// (all changes are persisted via Atomic_Commit).
        /// </summary>
        [Fact]
        public void Commit_PersistsAllChanges_ImageVisibleInDirectory()
        {
            // Arrange: create a fresh binary index file
            string path = Path.Combine(_tempDir, "commit_test.nkds");

            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                long fileSizeBefore = new FileInfo(path).Length;
                int directoryCountBefore = file.GetDirectory().Count;

                // Load block index for the transaction
                InMemoryBlockIndex blockIndex = file.LoadBlockIndex();

                // Create a transaction
                BinaryTransaction transaction = new BinaryTransaction("testset", file, blockIndex);

                // Set pending image
                transaction.SetPendingImage(new ImageRecord
                {
                    Id = 1,
                    Name = "game.iso",
                    Size = 4_700_000_000L,
                    Crc32 = 0xDEADBEEF,
                    XxHash64 = 0x1234567890ABCDEF,
                    SetName = "testset",
                    System = "Wii",
                    Format = ImageFormat.Iso
                });

                // Add pending areas
                transaction.AddPendingArea(new AreaRecord
                {
                    Offset = 0,
                    Size = 2048,
                    SectionSize = 0x200000,
                    Crc32 = 0xAABBCCDD,
                    XxHash64 = 0x1111222233334444
                });

                // Add pending offsets
                transaction.AddPendingOffset(new OffsetRecord
                {
                    Offset = 0,
                    Size = 0x10000,
                    Type = BlockType.File,
                    OffsetStart = 0,
                    Blocks = new List<BlockKey> { new BlockKey(0xFEDCBA9876543210, 0xABCD1234) }
                });

                // Add pending files
                transaction.AddPendingFile(new FileRecord
                {
                    Name = "main.dol",
                    FileId = 0,
                    Offset = 0,
                    Size = 512,
                    UncompressedSize = 1024,
                    IsSystem = true
                });

                // Add a pending block location
                transaction.AddPendingBlockLocation(
                    new BlockKey(0xFEDCBA9876543210, 0xABCD1234), fileId: 0, offset: 0, size: 0x10000);

                // Add a pending new block entry
                transaction.AddPendingBlock(new BlockIndexEntry
                {
                    Key = new BlockKey(0xFEDCBA9876543210, 0xABCD1234),
                    FileId = 0,
                    Offset = 0,
                    Size = 0x10000,
                });

                // Act: commit the transaction
                transaction.Commit();

                // Assert: transaction is completed
                Assert.True(transaction.IsCompleted);

                // Assert: directory now contains the image
                ImageDirectory directory = file.GetDirectory();
                Assert.Equal(directoryCountBefore + 1, directory.Count);

                ImageDirectoryEntry entry = directory.GetEntry(1);
                Assert.NotNull(entry);
                Assert.Equal("game.iso", entry!.Name);
                Assert.Equal(4_700_000_000L, entry.Size);
                Assert.Equal(0xDEADBEEFu, entry.Crc32);
                Assert.Equal(0x1234567890ABCDEFul, entry.XxHash64);
                Assert.Equal("Wii", entry.System);
                Assert.Equal(ImageFormat.Iso, entry.Format);
                Assert.False(entry.Removed);

                // Assert: file has grown
                long fileSizeAfter = new FileInfo(path).Length;
                Assert.True(fileSizeAfter > fileSizeBefore,
                    $"File should have grown after commit. Before: {fileSizeBefore}, After: {fileSizeAfter}");

                // Assert: metadata section is readable
                (List<AreaRecord> areas, List<FileRecord> files) = file.ReadImageMetadata(1);
                Assert.Single(areas);
                Assert.Equal(0xAABBCCDDu, areas[0].Crc32);
                Assert.Single(files);
                Assert.Equal("main.dol", files[0].Name);

                // Assert: block map section is readable
                (List<OffsetRecord> offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations) = file.ReadImageBlockMap(1);
                Assert.Single(offsets);
                Assert.Equal(0x10000L, offsets[0].Size);
                Assert.True(blockLocations.ContainsKey(new BlockKey(0xFEDCBA9876543210, 0xABCD1234)));
            }
        }

        /// <summary>
        /// **Validates: Requirements 10.4**
        ///
        /// After Rollback(), the Binary_Index_File remains at its pre-transaction state:
        /// no new image in the directory, file size unchanged.
        /// </summary>
        [Fact]
        public void Rollback_DiscardsAllChanges_FileUnchanged()
        {
            // Arrange: create a fresh binary index file
            string path = Path.Combine(_tempDir, "rollback_test.nkds");

            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                // Commit the initial state so the file is in a known good state
                file.AtomicCommit();
            }

            // Record file state before the transaction
            byte[] fileContentBefore = File.ReadAllBytes(path);
            long fileSizeBefore = fileContentBefore.Length;

            using (BinaryIndexFile file = BinaryIndexFile.Open(path))
            {
                int directoryCountBefore = file.GetDirectory().Count;
                InMemoryBlockIndex blockIndex = file.LoadBlockIndex();

                // Create a transaction and accumulate changes
                BinaryTransaction transaction = new BinaryTransaction("testset", file, blockIndex);

                transaction.SetPendingImage(new ImageRecord
                {
                    Id = 1,
                    Name = "game.iso",
                    Size = 4_700_000_000L,
                    Crc32 = 0xDEADBEEF,
                    XxHash64 = 0x1234567890ABCDEF,
                    SetName = "testset",
                    System = "Wii",
                    Format = ImageFormat.Iso
                });

                transaction.AddPendingArea(new AreaRecord
                {
                    Offset = 0,
                    Size = 2048,
                    SectionSize = 0x200000,
                    Crc32 = 0xAABBCCDD,
                    XxHash64 = 0x1111222233334444
                });

                transaction.AddPendingOffset(new OffsetRecord
                {
                    Offset = 0,
                    Size = 0x10000,
                    Type = BlockType.File,
                    OffsetStart = 0,
                    Blocks = new List<BlockKey> { new BlockKey(0xFEDCBA9876543210, 0xABCD1234) }
                });

                transaction.AddPendingFile(new FileRecord
                {
                    Name = "main.dol",
                    FileId = 0,
                    Offset = 0,
                    Size = 512,
                    UncompressedSize = 1024,
                    IsSystem = true
                });

                transaction.AddPendingBlockLocation(
                    new BlockKey(0xFEDCBA9876543210, 0xABCD1234), fileId: 0, offset: 0, size: 0x10000);

                transaction.AddPendingBlock(new BlockIndexEntry
                {
                    Key = new BlockKey(0xFEDCBA9876543210, 0xABCD1234),
                    FileId = 0,
                    Offset = 0,
                    Size = 0x10000,
                });

                // Act: rollback the transaction
                transaction.Rollback();

                // Assert: transaction is completed
                Assert.True(transaction.IsCompleted);

                // Assert: directory is unchanged
                Assert.Equal(directoryCountBefore, file.GetDirectory().Count);
                Assert.Null(file.GetDirectory().GetEntry(1));
            }

            // Assert: file content is unchanged (byte-for-byte identical)
            byte[] fileContentAfter = File.ReadAllBytes(path);
            Assert.Equal(fileSizeBefore, fileContentAfter.Length);
            Assert.Equal(fileContentBefore, fileContentAfter);
        }

        /// <summary>
        /// **Validates: Requirements 10.4**
        ///
        /// Disposing a transaction without calling Commit() triggers auto-rollback:
        /// the file remains unchanged (same as explicit Rollback).
        /// </summary>
        [Fact]
        public void DisposeWithoutCommit_AutoRollback_FileUnchanged()
        {
            // Arrange: create a fresh binary index file
            string path = Path.Combine(_tempDir, "dispose_test.nkds");

            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
                file.AtomicCommit();
            }

            // Record file state before the transaction
            byte[] fileContentBefore = File.ReadAllBytes(path);
            long fileSizeBefore = fileContentBefore.Length;

            using (BinaryIndexFile file = BinaryIndexFile.Open(path))
            {
                int directoryCountBefore = file.GetDirectory().Count;
                InMemoryBlockIndex blockIndex = file.LoadBlockIndex();

                // Create a transaction, accumulate changes, then dispose without committing
                using (BinaryTransaction transaction = new BinaryTransaction("testset", file, blockIndex))
                {
                    transaction.SetPendingImage(new ImageRecord
                    {
                        Id = 1,
                        Name = "game.iso",
                        Size = 4_700_000_000L,
                        Crc32 = 0xDEADBEEF,
                        XxHash64 = 0x1234567890ABCDEF,
                        SetName = "testset",
                        System = "Wii",
                        Format = ImageFormat.Iso
                    });

                    transaction.AddPendingArea(new AreaRecord
                    {
                        Offset = 0,
                        Size = 2048,
                        SectionSize = 0x200000,
                        Crc32 = 0xAABBCCDD,
                        XxHash64 = 0x1111222233334444
                    });

                    transaction.AddPendingOffset(new OffsetRecord
                    {
                        Offset = 0,
                        Size = 0x10000,
                        Type = BlockType.File,
                        OffsetStart = 0,
                        Blocks = new List<BlockKey> { new BlockKey(0xFEDCBA9876543210, 0xABCD1234) }
                    });

                    transaction.AddPendingFile(new FileRecord
                    {
                        Name = "main.dol",
                        FileId = 0,
                        Offset = 0,
                        Size = 512,
                        UncompressedSize = 1024,
                        IsSystem = true
                    });

                    transaction.AddPendingBlockLocation(
                        new BlockKey(0xFEDCBA9876543210, 0xABCD1234), fileId: 0, offset: 0, size: 0x10000);

                    // Do NOT call Commit() — let Dispose() handle it
                }
                // transaction is now disposed

                // Assert: directory is unchanged
                Assert.Equal(directoryCountBefore, file.GetDirectory().Count);
                Assert.Null(file.GetDirectory().GetEntry(1));
            }

            // Assert: file content is unchanged (byte-for-byte identical)
            byte[] fileContentAfter = File.ReadAllBytes(path);
            Assert.Equal(fileSizeBefore, fileContentAfter.Length);
            Assert.Equal(fileContentBefore, fileContentAfter);
        }
    }
}
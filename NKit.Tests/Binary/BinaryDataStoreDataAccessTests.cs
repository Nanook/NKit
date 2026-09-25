using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Tests for BinaryDataStoreDataAccess set initialization and configuration.
    ///
    /// Feature: binary-index-format
    /// Property 20: EnsureSetExists idempotence and GetSetInfo correctness
    /// **Validates: Requirements 14.3, 14.5**
    ///
    /// *For any* valid set configuration (shardSize >= 0, blockSize > 0), calling EnsureSetExists
    /// SHALL create the file and return an InfoRecord. Calling EnsureSetExists again with the same
    /// setName SHALL return the same InfoRecord without reinitializing the file. GetSetInfo SHALL
    /// return an InfoRecord with ShardSize, BlockSize, and MaxOffsetBlocks matching the values
    /// stored in the Header.
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class BinaryDataStoreDataAccessTests : IDisposable
    {
        private readonly string _tempDir;

        public BinaryDataStoreDataAccessTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BinaryDataStoreDataAccessTests_" + Guid.NewGuid().ToString("N"));
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
        /// **Validates: Requirements 14.3, 14.5**
        ///
        /// EnsureSetExists creates the file and returns an InfoRecord with correct configuration.
        /// Calling EnsureSetExists again with the same set name returns the same InfoRecord
        /// (idempotent — does not reinitialize the file).
        /// </summary>
        [Fact]
        public void EnsureSetExists_Idempotent_ReturnsSameInfoRecord()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "testset";
            long shardSize = 1_000_000_000L;
            int blockSize = 0x10000;

            // Act: first call creates the file
            InfoRecord first = dataAccess.EnsureSetExists(setName, shardSize, blockSize);

            // Act: second call with same set name should be idempotent
            InfoRecord second = dataAccess.EnsureSetExists(setName, shardSize, blockSize);

            // Assert: both calls return equivalent InfoRecords
            Assert.Equal(first.ShardSize, second.ShardSize);
            Assert.Equal(first.BlockSize, second.BlockSize);
            Assert.Equal(first.MaxOffsetBlocks, second.MaxOffsetBlocks);
            Assert.Equal(first.Version, second.Version);

            // Assert: configuration matches what was requested
            Assert.Equal(shardSize, first.ShardSize);
            Assert.Equal(blockSize, first.BlockSize);
            Assert.Equal(336, first.MaxOffsetBlocks); // Default max_offset_blocks
        }

        /// <summary>
        /// **Validates: Requirements 14.3, 14.5**
        ///
        /// GetSetInfo returns an InfoRecord with ShardSize, BlockSize, and MaxOffsetBlocks
        /// matching the values stored in the Header (same as what EnsureSetExists returned).
        /// </summary>
        [Fact]
        public void GetSetInfo_ReturnsCorrectInfoRecord_MatchingHeader()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "infoset";
            long shardSize = 500_000_000L;
            int blockSize = 0x20000;

            // Create the set first
            InfoRecord created = dataAccess.EnsureSetExists(setName, shardSize, blockSize);

            // Act: get set info
            InfoRecord info = dataAccess.GetSetInfo(setName);

            // Assert: GetSetInfo returns same values as EnsureSetExists
            Assert.Equal(created.ShardSize, info.ShardSize);
            Assert.Equal(created.BlockSize, info.BlockSize);
            Assert.Equal(created.MaxOffsetBlocks, info.MaxOffsetBlocks);
            Assert.Equal(created.Version, info.Version);

            // Assert: values match what was configured
            Assert.Equal(shardSize, info.ShardSize);
            Assert.Equal(blockSize, info.BlockSize);
            Assert.Equal(336, info.MaxOffsetBlocks);
        }

        /// <summary>
        /// **Validates: Requirements 14.5**
        ///
        /// EnsureSetExists for an already-existing set does not reinitialize the file.
        /// The file on disk should not be recreated (same creation time, same content).
        /// </summary>
        [Fact]
        public void EnsureSetExists_ExistingSet_DoesNotReinitializeFile()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "existingset";
            long shardSize = 2_000_000_000L;
            int blockSize = 0x10000;

            // Create the set
            dataAccess.EnsureSetExists(setName, shardSize, blockSize);

            // Record file state after creation
            string filePath = Path.Combine(_tempDir, $"{setName}.nkds");
            long fileSizeAfterCreate = new FileInfo(filePath).Length;
            DateTime creationTimeAfterCreate = File.GetCreationTimeUtc(filePath);

            // Act: call EnsureSetExists again
            InfoRecord second = dataAccess.EnsureSetExists(setName, shardSize, blockSize);

            // Assert: file was not recreated
            long fileSizeAfterSecondCall = new FileInfo(filePath).Length;
            DateTime creationTimeAfterSecondCall = File.GetCreationTimeUtc(filePath);

            Assert.Equal(fileSizeAfterCreate, fileSizeAfterSecondCall);
            Assert.Equal(creationTimeAfterCreate, creationTimeAfterSecondCall);

            // Assert: returned info is still correct
            Assert.Equal(shardSize, second.ShardSize);
            Assert.Equal(blockSize, second.BlockSize);
        }

        /// <summary>
        /// **Validates: Requirements 14.3, 14.5**
        ///
        /// EnsureSetExists with shardSize = 0 (unlimited) works correctly and is idempotent.
        /// </summary>
        [Fact]
        public void EnsureSetExists_ZeroShardSize_WorksAndIsIdempotent()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "unlimitedset";
            long shardSize = 0; // unlimited
            int blockSize = 0x10000;

            // Act
            InfoRecord first = dataAccess.EnsureSetExists(setName, shardSize, blockSize);
            InfoRecord second = dataAccess.EnsureSetExists(setName, shardSize, blockSize);

            // Assert
            Assert.Equal(0L, first.ShardSize);
            Assert.Equal(first.ShardSize, second.ShardSize);
            Assert.Equal(first.BlockSize, second.BlockSize);
            Assert.Equal(first.MaxOffsetBlocks, second.MaxOffsetBlocks);
        }

        /// <summary>
        /// **Validates: Requirements 14.3, 14.5**
        ///
        /// EnsureSetExists with default blockSize (0 → 0x10000) works correctly.
        /// </summary>
        [Fact]
        public void EnsureSetExists_DefaultBlockSize_UsesDefault64KiB()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "defaultblockset";
            long shardSize = 1_000_000_000L;

            // Act: blockSize = 0 means use default
            InfoRecord info = dataAccess.EnsureSetExists(setName, shardSize, blockSize: 0);

            // Assert: default block size is 0x10000 (64 KiB)
            Assert.Equal(0x10000, info.BlockSize);
        }

        /// <summary>
        /// **Validates: Requirements 14.3**
        ///
        /// Negative shardSize throws ArgumentOutOfRangeException.
        /// </summary>
        [Fact]
        public void EnsureSetExists_NegativeShardSize_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                dataAccess.EnsureSetExists("badset", shardSize: -1, blockSize: 0x10000));
        }

        /// <summary>
        /// **Validates: Requirements 14.3**
        ///
        /// Negative shardSize does not create any file on disk.
        /// </summary>
        [Fact]
        public void EnsureSetExists_NegativeShardSize_DoesNotCreateFile()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "neverexists";

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                dataAccess.EnsureSetExists(setName, shardSize: -100, blockSize: 0x10000));

            // Assert: no file was created
            string filePath = Path.Combine(_tempDir, $"{setName}.nkds");
            Assert.False(File.Exists(filePath));
        }

        #region Property 12: Rollback marks higher IDs as removed

        /// <summary>
        /// Feature: binary-index-format
        /// Property 12: Rollback marks higher IDs as removed
        /// **Validates: Requirements 13.1**
        ///
        /// WHEN rollback is requested for a specific image, THE BinaryDataStoreDataAccess SHALL mark
        /// all images with an ID greater than the specified image's ID as removed in the Image_Directory.
        /// The target image itself SHALL NOT be marked as removed.
        /// </summary>
        [Fact]
        public void Rollback_MarksHigherIDsAsRemoved_PreservesTargetAndLower()
        {
            // Arrange: create set, insert 3 images (IDs 1, 2, 3), commit each
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "rollbackset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Insert image 1
            using (IDataStoreTransaction tx1 = dataAccess.BeginTransaction(setName))
            {
                long id1 = dataAccess.InsertImage(setName, tx1, "image1.iso", "Wii", ImageFormat.Iso);
                dataAccess.UpdateImageMetadata(setName, tx1, id1, size: 0, crc32: 0, xxhash64: 0);
                tx1.Commit();
            }

            // Insert image 2
            using (IDataStoreTransaction tx2 = dataAccess.BeginTransaction(setName))
            {
                long id2 = dataAccess.InsertImage(setName, tx2, "image2.iso", "Wii", ImageFormat.Iso);
                dataAccess.UpdateImageMetadata(setName, tx2, id2, size: 0, crc32: 0, xxhash64: 0);
                tx2.Commit();
            }

            // Insert image 3
            using (IDataStoreTransaction tx3 = dataAccess.BeginTransaction(setName))
            {
                long id3 = dataAccess.InsertImage(setName, tx3, "image3.iso", "GC", ImageFormat.Iso);
                dataAccess.UpdateImageMetadata(setName, tx3, id3, size: 0, crc32: 0, xxhash64: 0);
                tx3.Commit();
            }

            // Verify all 3 images exist and are not removed
            List<ImageRecord> allBefore = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Equal(3, allBefore.Count);

            // Act: Rollback to image 1
            long imageId1 = allBefore.OrderBy(i => i.Id).First().Id;
            dataAccess.Rollback(setName, imageId1);

            // Assert: image 1 is NOT removed
            ImageRecord img1 = dataAccess.GetImage(setName, imageId1);
            Assert.NotNull(img1);
            Assert.False(img1!.Removed);

            // Assert: images 2 and 3 ARE removed (not in non-removed list)
            List<ImageRecord> nonRemoved = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Single(nonRemoved);
            Assert.Equal(imageId1, nonRemoved[0].Id);

            // Assert: images 2 and 3 are physically removed from the directory
            // (physical rollback truncates shard data and removes directory entries)
            List<ImageRecord> allAfter = dataAccess.GetAllImagesInSetIncludingRemoved(setName).ToList();
            Assert.Single(allAfter);
            Assert.Equal(imageId1, allAfter[0].Id);
        }

        /// <summary>
        /// **Validates: Requirements 13.1**
        ///
        /// Rollback for a non-existent image ID throws ArgumentException without modifying the file.
        /// </summary>
        [Fact]
        public void Rollback_NonExistentImageId_ThrowsArgumentException()
        {
            // Arrange: create set with one image
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "rollbackerrset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
            {
                dataAccess.InsertImage(setName, tx, "image1.iso", "Wii", ImageFormat.Iso);
                tx.Commit();
            }

            // Act & Assert: rollback for non-existent ID throws ArgumentException
            Assert.Throws<ArgumentException>(() => dataAccess.Rollback(setName, 9999));

            // Assert: existing image is unaffected
            List<ImageRecord> images = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Single(images);
            Assert.False(images[0].Removed);
        }

        #endregion

        #region Property 11: Delete/Restore lifecycle

        /// <summary>
        /// Feature: binary-index-format
        /// Property 11: Delete/Restore lifecycle
        /// **Validates: Requirements 11.1, 11.2, 11.3**
        ///
        /// *For any* image in the directory: (a) after DeleteImage, GetAllImagesInSet SHALL exclude it
        /// while GetAllImagesInSetIncludingRemoved SHALL include it with Removed=true; (b) after
        /// RestoreImage on a removed image, GetAllImagesInSet SHALL include it with Removed=false;
        /// (c) the directory entry SHALL retain all metadata fields and section offsets unchanged
        /// through delete and restore operations (only the Removed flag changes).
        /// </summary>
        [Fact]
        public void DeleteImage_ExcludesFromGetAllImagesInSet_ButIncludedInGetAllIncludingRemoved()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "deleteset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Insert an image and commit
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, "game.iso", "Wii", ImageFormat.Iso);
            dataAccess.UpdateImageMetadata(setName, transaction, imageId, 4_700_000_000L, 0xAABBCCDD, 0x1122334455667788);
            transaction.Commit();

            // Verify image is visible before deletion
            List<ImageRecord> imagesBefore = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Single(imagesBefore);
            Assert.Equal(imageId, imagesBefore[0].Id);

            // Act: delete the image
            dataAccess.DeleteImage(setName, imageId);

            // Assert: GetAllImagesInSet excludes the deleted image
            List<ImageRecord> imagesAfterDelete = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Empty(imagesAfterDelete);

            // Assert: GetAllImagesInSetIncludingRemoved still includes it with Removed=true
            List<ImageRecord> allImagesAfterDelete = dataAccess.GetAllImagesInSetIncludingRemoved(setName).ToList();
            Assert.Single(allImagesAfterDelete);
            Assert.Equal(imageId, allImagesAfterDelete[0].Id);
            Assert.True(allImagesAfterDelete[0].Removed);

            // Assert: GetImage returns it with Removed=true
            ImageRecord deletedImage = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(deletedImage);
            Assert.True(deletedImage!.Removed);
        }

        /// <summary>
        /// **Validates: Requirements 11.2, 11.3**
        ///
        /// After RestoreImage on a removed image, GetAllImagesInSet SHALL include it with Removed=false.
        /// </summary>
        [Fact]
        public void RestoreImage_IncludesInGetAllImagesInSet_WithRemovedFalse()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "restoreset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Insert an image and commit
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, "restore.iso", "GC", ImageFormat.Iso);
            dataAccess.UpdateImageMetadata(setName, transaction, imageId, 1_459_978_240L, 0x12345678, 0xDEADBEEFCAFEBABE);
            transaction.Commit();

            // Delete the image first
            dataAccess.DeleteImage(setName, imageId);
            Assert.Empty(dataAccess.GetAllImagesInSet(setName));

            // Act: restore the image
            dataAccess.RestoreImage(setName, imageId);

            // Assert: GetAllImagesInSet includes it again
            List<ImageRecord> imagesAfterRestore = dataAccess.GetAllImagesInSet(setName).ToList();
            Assert.Single(imagesAfterRestore);
            Assert.Equal(imageId, imagesAfterRestore[0].Id);
            Assert.False(imagesAfterRestore[0].Removed);

            // Assert: GetImage returns it with Removed=false
            ImageRecord restoredImage = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(restoredImage);
            Assert.False(restoredImage!.Removed);
        }

        /// <summary>
        /// **Validates: Requirements 11.1, 11.2, 11.3**
        ///
        /// The directory entry SHALL retain all metadata fields unchanged through delete and restore
        /// operations (only the Removed flag changes).
        /// </summary>
        [Fact]
        public void DeleteAndRestore_PreservesAllMetadataFields()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "preserveset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Insert an image with specific metadata
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, "metadata.iso", "Wii", ImageFormat.Iso);
            dataAccess.UpdateImageMetadata(setName, transaction, imageId, 8_511_160_320L, 0xFEDCBA98, 0xABCDEF0123456789);
            transaction.Commit();

            // Capture the original image record
            ImageRecord original = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(original);

            // Act: delete
            dataAccess.DeleteImage(setName, imageId);
            ImageRecord afterDelete = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(afterDelete);

            // Assert: all fields except Removed are unchanged after delete
            Assert.Equal(original!.Id, afterDelete!.Id);
            Assert.Equal(original.Name, afterDelete.Name);
            Assert.Equal(original.Size, afterDelete.Size);
            Assert.Equal(original.Crc32, afterDelete.Crc32);
            Assert.Equal(original.XxHash64, afterDelete.XxHash64);
            Assert.Equal(original.System, afterDelete.System);
            Assert.Equal(original.Format, afterDelete.Format);
            Assert.True(afterDelete.Removed); // Only this changed

            // Act: restore
            dataAccess.RestoreImage(setName, imageId);
            ImageRecord afterRestore = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(afterRestore);

            // Assert: all fields are back to original state
            Assert.Equal(original.Id, afterRestore!.Id);
            Assert.Equal(original.Name, afterRestore.Name);
            Assert.Equal(original.Size, afterRestore.Size);
            Assert.Equal(original.Crc32, afterRestore.Crc32);
            Assert.Equal(original.XxHash64, afterRestore.XxHash64);
            Assert.Equal(original.System, afterRestore.System);
            Assert.Equal(original.Format, afterRestore.Format);
            Assert.False(afterRestore.Removed); // Back to original
        }

        /// <summary>
        /// **Validates: Requirements 11.1, 11.3**
        ///
        /// DeleteImage for a non-existent image ID throws KeyNotFoundException.
        /// </summary>
        [Fact]
        public void DeleteImage_NonExistentId_ThrowsKeyNotFoundException()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "deleteerrorset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Act & Assert: deleting a non-existent image throws
            Assert.Throws<KeyNotFoundException>(() =>
                dataAccess.DeleteImage(setName, 999));
        }

        /// <summary>
        /// **Validates: Requirements 11.2, 11.3**
        ///
        /// RestoreImage for a non-existent image ID throws KeyNotFoundException.
        /// </summary>
        [Fact]
        public void RestoreImage_NonExistentId_ThrowsKeyNotFoundException()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "restoreerrorset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Act & Assert: restoring a non-existent image throws
            Assert.Throws<KeyNotFoundException>(() =>
                dataAccess.RestoreImage(setName, 999));
        }

        #endregion

        #region Property 19: File storage round-trip

        /// <summary>
        /// Feature: binary-index-format
        /// Property 19: File storage round-trip
        /// **Validates: Requirements 15.3, 15.4, 15.6**
        ///
        /// *For any* valid file data (arbitrary byte array), after InsertFile(imageId, name, data, isSystem)
        /// and commit: GetFile(imageId, name) SHALL return a FileRecord with matching name and IsSystem flag,
        /// and ReadFileData(record) SHALL return a byte array identical to the original data.
        /// </summary>
        [Property(MaxTest = 50)]
        public bool FileStorageRoundTrip_DataPreserved(NonNegativeInt seed)
        {
            Random rng = new Random(seed.Get);

            // Generate random file data (1 to 4096 bytes)
            int dataSize = rng.Next(1, 4097);
            byte[] data = new byte[dataSize];
            rng.NextBytes(data);

            // Pick a file name and isSystem flag
            string[] names = { "filesystem.yaml", "main.dol", "bi2.bin", "apploader.img", "test_file.dat" };
            string name = names[rng.Next(names.Length)];
            bool isSystem = rng.Next(2) == 1;

            string tempDir = Path.Combine(Path.GetTempPath(), "FileRoundTrip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(tempDir);
                string setName = "roundtripset";

                // 1. Create the set
                dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                // 2. Begin transaction, insert image, insert file, commit
                using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
                long imageId = dataAccess.InsertImage(setName, transaction, "testimage.iso", "Wii", ImageFormat.Iso);
                dataAccess.InsertFile(setName, transaction, imageId, name, data, isSystem);
                transaction.Commit();

                // 3. GetFile to retrieve the FileRecord
                FileRecord fileRecord = dataAccess.GetFile(setName, imageId, name);
                if (fileRecord == null) return false;

                // 4. Verify FileRecord metadata
                if (fileRecord.Name != name) return false;
                if (fileRecord.IsSystem != isSystem) return false;

                // 5. ReadFileData to get the decompressed data
                byte[] readData = dataAccess.ReadFileData(setName, fileRecord);
                if (readData == null) return false;

                // 6. Verify the data matches the original
                if (readData.Length != data.Length) return false;
                for (int i = 0; i < data.Length; i++)
                {
                    if (readData[i] != data[i]) return false;
                }

                return true;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }

        /// <summary>
        /// **Validates: Requirements 15.3, 15.4, 15.6**
        ///
        /// Verifies the full InsertFile → GetFile → ReadFileData pipeline with known data.
        /// </summary>
        [Fact]
        public void FileStorageRoundTrip_KnownData_ReturnsIdenticalBytes()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "fileset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            byte[] originalData = Encoding.UTF8.GetBytes("name: Test Game\nregion: NTSC\ndisc: 1\n");
            string fileName = "filesystem.yaml";
            bool isSystem = true;

            // Act: insert image and file
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, "game.iso", "Wii", ImageFormat.Iso);
            dataAccess.InsertFile(setName, transaction, imageId, fileName, originalData, isSystem);
            transaction.Commit();

            // Act: retrieve file record
            FileRecord fileRecord = dataAccess.GetFile(setName, imageId, fileName);

            // Assert: FileRecord metadata
            Assert.NotNull(fileRecord);
            Assert.Equal(fileName, fileRecord!.Name);
            Assert.True(fileRecord.IsSystem);
            Assert.Equal(originalData.Length, (int)fileRecord.UncompressedSize);

            // Act: read file data
            byte[] readData = dataAccess.ReadFileData(setName, fileRecord);

            // Assert: data matches original
            Assert.NotNull(readData);
            Assert.Equal(originalData, readData);
        }

        /// <summary>
        /// **Validates: Requirements 15.3, 15.6**
        ///
        /// GetFilesForImage returns all files inserted for an image.
        /// </summary>
        [Fact]
        public void FileStorageRoundTrip_MultipleFiles_AllRetrievable()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "multifileset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            byte[] data1 = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            byte[] data2 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

            // Act: insert image with two files
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, "multi.iso", "GC", ImageFormat.Iso);
            dataAccess.InsertFile(setName, transaction, imageId, "file1.bin", data1, isSystem: false);
            dataAccess.InsertFile(setName, transaction, imageId, "file2.sys", data2, isSystem: true);
            transaction.Commit();

            // Assert: GetFilesForImage returns both files
            IEnumerable<FileRecord> files = dataAccess.GetFilesForImage(setName, imageId);
            Assert.Equal(2, files.Count());

            // Assert: each file round-trips correctly
            FileRecord record1 = dataAccess.GetFile(setName, imageId, "file1.bin");
            Assert.NotNull(record1);
            Assert.False(record1!.IsSystem);
            byte[] readData1 = dataAccess.ReadFileData(setName, record1);
            Assert.Equal(data1, readData1);

            FileRecord record2 = dataAccess.GetFile(setName, imageId, "file2.sys");
            Assert.NotNull(record2);
            Assert.True(record2!.IsSystem);
            byte[] readData2 = dataAccess.ReadFileData(setName, record2);
            Assert.Equal(data2, readData2);
        }

        #endregion

        #region Property 13: Rename changes only name field

        /// <summary>
        /// Feature: binary-index-format
        /// Property 13: Rename changes only name field
        /// **Validates: Requirements 12.1**
        ///
        /// *For any* image inserted with known metadata, calling UpdateImageName with a new name
        /// SHALL change only the Name field — all other fields (Size, Crc32, XxHash64, System,
        /// Format, Removed) remain identical.
        /// </summary>
        [Fact]
        public void RenameImage_ChangesOnlyNameField_OtherFieldsUnchanged()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "renameset";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            string originalName = "original_game.iso";
            string system = "Wii";
            ImageFormat format = ImageFormat.Iso;

            // Insert an image with known metadata
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, originalName, system, format);

            // Update metadata to give it non-zero Size, Crc32, XxHash64
            dataAccess.UpdateImageMetadata(setName, transaction, imageId, size: 4_700_000_000L, crc32: 0xDEADBEEF, xxhash64: 0x123456789ABCDEF0);
            transaction.Commit();

            // Record all fields before rename
            ImageRecord before = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(before);
            long sizeBefore = before!.Size;
            uint crc32Before = before.Crc32;
            ulong xxHash64Before = before.XxHash64;
            string systemBefore = before.System;
            ImageFormat formatBefore = before.Format;
            bool removedBefore = before.Removed;

            // Act: rename the image
            string newName = "renamed_game.iso";
            using IDataStoreTransaction renameTransaction = dataAccess.BeginTransaction(setName);
            dataAccess.UpdateImageName(setName, renameTransaction, imageId, newName);

            // Assert: get the image again and verify only Name changed
            ImageRecord after = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(after);

            // Name should be updated
            Assert.Equal(newName, after!.Name);
            Assert.NotEqual(originalName, after.Name);

            // All other fields must remain identical
            Assert.Equal(sizeBefore, after.Size);
            Assert.Equal(crc32Before, after.Crc32);
            Assert.Equal(xxHash64Before, after.XxHash64);
            Assert.Equal(systemBefore, after.System);
            Assert.Equal(formatBefore, after.Format);
            Assert.Equal(removedBefore, after.Removed);
        }

        /// <summary>
        /// **Validates: Requirements 12.1**
        ///
        /// Rename with a null system field preserves the null value.
        /// </summary>
        [Fact]
        public void RenameImage_NullSystem_PreservesNullSystem()
        {
            // Arrange
            using BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(_tempDir);
            string setName = "renamenullsys";
            dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

            // Insert image with null system
            using IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName);
            long imageId = dataAccess.InsertImage(setName, transaction, "nosystem.iso", null, ImageFormat.Bin);
            dataAccess.UpdateImageMetadata(setName, transaction, imageId, size: 1000L, crc32: 0xAABBCCDD, xxhash64: 0xFEDCBA9876543210);
            transaction.Commit();

            // Record state before rename
            ImageRecord before = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(before);
            Assert.Null(before!.System);

            // Act: rename
            using IDataStoreTransaction renameTransaction = dataAccess.BeginTransaction(setName);
            dataAccess.UpdateImageName(setName, renameTransaction, imageId, "renamed_nosystem.iso");

            // Assert: system is still null, other fields unchanged
            ImageRecord after = dataAccess.GetImage(setName, imageId);
            Assert.NotNull(after);
            Assert.Equal("renamed_nosystem.iso", after!.Name);
            Assert.Null(after.System);
            Assert.Equal(before.Size, after.Size);
            Assert.Equal(before.Crc32, after.Crc32);
            Assert.Equal(before.XxHash64, after.XxHash64);
            Assert.Equal(before.Format, after.Format);
            Assert.Equal(before.Removed, after.Removed);
        }

        #endregion
    }
}
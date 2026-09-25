using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based and scenario tests for header validation logic.
    ///
    /// Feature: binary-index-format
    /// Property 8: Header validation logic
    /// **Validates: Requirements 1.5, 7.3**
    ///
    /// For any 4096-byte buffer representing a header: validation SHALL accept the header
    /// if and only if the magic byte sequence matches, the format version is supported,
    /// and all file-offset pointers reference positions within the file's actual size and are non-zero.
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class FileHeaderValidationTests : IDisposable
    {
        private readonly string _tempDir;

        public FileHeaderValidationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "NKit_HeaderValidation_" + Guid.NewGuid().ToString("N"));
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

        private string GetTempFilePath(string name = "test.nkds") => Path.Combine(_tempDir, name);

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// A file created with BinaryIndexFile.Create should be openable with BinaryIndexFile.Open,
        /// confirming that valid headers pass validation.
        /// </summary>
        [Property(MaxTest = 20)]
        public bool ValidHeaderPassesValidation(PositiveInt blockSizeFactor, PositiveInt maxOffsetBlocks)
        {
            int blockSize = Math.Max(1, blockSizeFactor.Get % 0x100000);
            int maxBlocks = Math.Max(1, maxOffsetBlocks.Get % 1000);
            string path = GetTempFilePath($"valid_{blockSize}_{maxBlocks}.nkds");

            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: blockSize, maxOffsetBlocks: maxBlocks))
            {
                // File created successfully
            }

            // Open should succeed without throwing
            using (BinaryIndexFile file = BinaryIndexFile.Open(path))
            {
                FileHeader header = file.Header;
                return header.Magic == FileHeader.MagicBytes
                    && header.MajorVersion == FileHeader.CurrentMajorVersion
                    && header.MinorVersion == FileHeader.CurrentMinorVersion
                    && header.BlockSize == blockSize
                    && header.MaxOffsetBlocks == maxBlocks;
            }
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// Corrupting the magic bytes in both headers should cause Open to throw InvalidDataException.
        /// </summary>
        [Fact]
        public void CorruptedMagicBytesCausesOpenToFail()
        {
            string path = GetTempFilePath("corrupt_magic.nkds");

            // Create a valid file
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
            }

            // Corrupt magic bytes in both primary and secondary headers
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                // Corrupt primary header magic at offset 0x00
                stream.Position = 0x00;
                stream.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 4);

                // Corrupt secondary header magic at offset 4096 + 0x00
                stream.Position = FileHeader.HeaderSize + 0x00;
                stream.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 4);
            }

            // Open should throw InvalidDataException because both headers are corrupted
            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// Setting an unsupported major version in both headers should cause Open to throw.
        /// </summary>
        [Fact]
        public void UnsupportedMajorVersionCausesOpenToFail()
        {
            string path = GetTempFilePath("bad_major_version.nkds");

            // Create a valid file
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
            }

            // Read the valid header, modify the major version, rewrite with valid checksum
            CorruptHeaderField(path, header =>
            {
                header.MajorVersion = (ushort)(FileHeader.CurrentMajorVersion + 1);
                return header;
            });

            // Open should throw because both headers have unsupported major version
            Exception ex = Assert.ThrowsAny<Exception>(() => BinaryIndexFile.Open(path));
            Assert.True(ex is InvalidDataException || ex is NotSupportedException,
                $"Expected InvalidDataException or NotSupportedException but got {ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// A file smaller than 8192 bytes (Header + Secondary_Header) should cause Open to throw.
        /// </summary>
        [Fact]
        public void FileSizeLessThan8192CausesOpenToFail()
        {
            string path = GetTempFilePath("too_small.nkds");

            // Create a file that is too small (less than 8192 bytes)
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                // Write only 4096 bytes (one header, missing secondary)
                byte[] data = new byte[4096];
                stream.Write(data, 0, data.Length);
            }

            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// A file with exactly 8191 bytes should cause Open to throw.
        /// </summary>
        [Fact]
        public void FileSizeExactly8191BytesCausesOpenToFail()
        {
            string path = GetTempFilePath("size_8191.nkds");

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] data = new byte[8191];
                stream.Write(data, 0, data.Length);
            }

            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// An empty file should cause Open to throw.
        /// </summary>
        [Fact]
        public void EmptyFileCausesOpenToFail()
        {
            string path = GetTempFilePath("empty.nkds");

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                // Write nothing - 0 bytes
            }

            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// Offsets pointing beyond the file size should cause Open to fail when both headers
        /// have invalid offsets.
        /// </summary>
        [Fact]
        public void OffsetsPointingBeyondFileSizeCausesOpenToFail()
        {
            string path = GetTempFilePath("bad_offsets.nkds");

            // Create a valid file
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
            }

            long fileSize;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fileSize = stream.Length;
            }

            // Corrupt the ImageDirectoryOffset to point beyond file size in both headers
            CorruptHeaderField(path, header =>
            {
                header.ImageDirectoryOffset = fileSize + 10000;
                return header;
            });

            // Open should throw because both headers have invalid offsets
            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// BlockIndexOffset pointing beyond file size should cause Open to fail.
        /// </summary>
        [Fact]
        public void BlockIndexOffsetBeyondFileSizeCausesOpenToFail()
        {
            string path = GetTempFilePath("bad_block_offset.nkds");

            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
            }

            long fileSize;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fileSize = stream.Length;
            }

            CorruptHeaderField(path, header =>
            {
                header.BlockIndexOffset = fileSize + 5000;
                return header;
            });

            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// FileEndOffset pointing beyond file size should cause Open to fail.
        /// </summary>
        [Fact]
        public void FileEndOffsetBeyondFileSizeCausesOpenToFail()
        {
            string path = GetTempFilePath("bad_end_offset.nkds");

            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
            }

            long fileSize;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fileSize = stream.Length;
            }

            CorruptHeaderField(path, header =>
            {
                header.FileEndOffset = fileSize + 1;
                return header;
            });

            Assert.Throws<InvalidDataException>(() => BinaryIndexFile.Open(path));
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 7.3**
        ///
        /// Property test: for any valid file created with Create, truncating the file
        /// below 8192 bytes should always cause Open to fail.
        /// </summary>
        [Property(MaxTest = 10)]
        public bool TruncatedFileBelowMinimumAlwaysFails(PositiveInt truncateSize)
        {
            int size = truncateSize.Get % 8192; // Always less than 8192
            string path = GetTempFilePath($"truncated_{size}.nkds");

            // Create a valid file first
            using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336))
            {
            }

            // Truncate to less than 8192 bytes
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(size);
            }

            try
            {
                using (BinaryIndexFile file = BinaryIndexFile.Open(path))
                {
                    return false; // Should not succeed
                }
            }
            catch (InvalidDataException)
            {
                return true; // Expected
            }
            catch (EndOfStreamException)
            {
                return true; // Also acceptable for very small files
            }
        }

        /// <summary>
        /// Helper method that reads both headers from a file, applies a mutation function,
        /// and rewrites both headers with valid checksums.
        /// This ensures the checksum is valid but the header content is corrupted.
        /// </summary>
        private static void CorruptHeaderField(string path, Func<FileHeader, FileHeader> mutate)
        {
            byte[] primaryBuffer = new byte[FileHeader.HeaderSize];
            byte[] secondaryBuffer = new byte[FileHeader.HeaderSize];

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                // Read primary header
                stream.Position = 0;
                stream.Read(primaryBuffer, 0, FileHeader.HeaderSize);
                FileHeader primary = FileHeaderSerializer.Read(primaryBuffer);

                // Read secondary header
                stream.Position = FileHeader.HeaderSize;
                stream.Read(secondaryBuffer, 0, FileHeader.HeaderSize);
                FileHeader secondary = FileHeaderSerializer.Read(secondaryBuffer);

                // Apply mutation to both
                primary = mutate(primary);
                secondary = mutate(secondary);

                // Rewrite primary header with valid checksum
                FileHeaderSerializer.Write(primaryBuffer, primary);
                stream.Position = 0;
                stream.Write(primaryBuffer, 0, FileHeader.HeaderSize);

                // Rewrite secondary header with valid checksum
                FileHeaderSerializer.Write(secondaryBuffer, secondary);
                stream.Position = FileHeader.HeaderSize;
                stream.Write(secondaryBuffer, 0, FileHeader.HeaderSize);

                stream.Flush();
            }
        }
    }
}
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKit.Tests.Binary.Generators;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using System.Buffers.Binary;
using System.IO;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for EmbeddedFooter serialization and embedded mode detection.
    ///
    /// Feature: embedded-binary-index
    /// Property 1: Embedded Footer serialization round-trip
    /// Property 7: Header validation with relative offsets
    /// Property 8: Embedded mode detection
    /// **Validates: Requirements 1.3, 1.4, 2.6, 4.5, 8.1, 8.2, 10.1, 10.2, 10.3**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class EmbeddedFooterTests : IDisposable
    {
        private readonly string _tempDir;

        public EmbeddedFooterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "NKit_EmbFooterTests_" + Guid.NewGuid().ToString("N"));
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

        private string GetTempFilePath(string name) => Path.Combine(_tempDir, name);

        private static Arbitrary<long> ValidIndexSizeArbitrary() => Arb.From(BinaryIndexGenerators.ValidIndexSize());

        /// <summary>
        /// **Validates: Requirements 1.3, 1.4, 10.1, 10.2, 10.3**
        ///
        /// Property 1: Embedded Footer serialization round-trip.
        /// For any valid IndexSize value (int64, range 0 to int64.MaxValue),
        /// serializing an EmbeddedFooter to a 12-byte buffer and deserializing back
        /// SHALL produce an EmbeddedFooter with an identical IndexSize value.
        /// The serialized buffer SHALL be exactly 12 bytes, with the first 8 bytes
        /// being the big-endian int64 IndexSize and the last 4 bytes being the
        /// Footer_Magic 0x4E4B4453.
        /// </summary>
        [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmbeddedFooterTests) })]
        public bool FooterSerializationRoundTrip_PreservesIndexSize(long indexSize)
        {
            if (indexSize < 0)
                return true; // Skip negative values; property only covers 0 to int64.MaxValue

            EmbeddedFooter footer = new EmbeddedFooter(indexSize);

            // Serialize
            byte[] buffer = footer.Serialize();

            // Deserialize
            EmbeddedFooter? deserialized = EmbeddedFooter.Deserialize(buffer);

            // Verify round-trip produces identical IndexSize
            return deserialized.HasValue && deserialized.Value.IndexSize == indexSize;
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 10.3**
        ///
        /// Property 1 (size invariant): The serialized buffer SHALL be exactly 12 bytes.
        /// </summary>
        [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmbeddedFooterTests) })]
        public bool FooterSerialization_ProducesExactly12Bytes(long indexSize)
        {
            if (indexSize < 0)
                return true; // Skip negative values

            EmbeddedFooter footer = new EmbeddedFooter(indexSize);
            byte[] buffer = footer.Serialize();

            return buffer.Length == EmbeddedFooter.FooterSize;
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 10.2**
        ///
        /// Property 1 (magic invariant): The last 4 bytes of the serialized buffer
        /// SHALL always be 0x4E, 0x4B, 0x44, 0x53 (Footer_Magic "NKDS").
        /// </summary>
        [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmbeddedFooterTests) })]
        public bool FooterSerialization_LastFourBytesAreMagic(long indexSize)
        {
            if (indexSize < 0)
                return true; // Skip negative values

            EmbeddedFooter footer = new EmbeddedFooter(indexSize);
            byte[] buffer = footer.Serialize();

            return buffer[8] == 0x4E
                && buffer[9] == 0x4B
                && buffer[10] == 0x44
                && buffer[11] == 0x53;
        }

        // ===================================================================
        // Property 8: Embedded mode detection
        // Feature: embedded-binary-index
        // **Validates: Requirements 2.6, 8.1, 8.2**
        // ===================================================================

        /// <summary>
        /// **Validates: Requirements 2.6, 8.1, 8.2**
        ///
        /// Property 8: Embedded mode detection.
        /// For any file ≥ 12 bytes where the last 4 bytes equal FooterMagic (0x4E4B4453),
        /// the file SHALL be detected as embedded mode via IsMagicValid returning true.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool FileWithFooterMagicAtEnd_IsDetectedAsEmbedded(NonNegativeInt extraSize)
        {
            // Generate a file that is at least 12 bytes with valid footer magic at the end
            int totalSize = 12 + (extraSize.Get % 10000); // 12 to ~10012 bytes
            byte[] fileData = new byte[totalSize];

            // Fill with random data (use extraSize as seed)
            Random rng = new Random(extraSize.Get);
            rng.NextBytes(fileData);

            // Write FooterMagic as the last 4 bytes
            BinaryPrimitives.WriteUInt32BigEndian(fileData.AsSpan(totalSize - 4, 4), EmbeddedFooter.FooterMagic);

            // Detection: check last 4 bytes with IsMagicValid
            ReadOnlySpan<byte> lastFour = fileData.AsSpan(totalSize - 4, 4);
            return EmbeddedFooter.IsMagicValid(lastFour);
        }

        /// <summary>
        /// **Validates: Requirements 2.6, 8.1, 8.2**
        ///
        /// Property 8: Embedded mode detection.
        /// For any file &lt; 12 bytes, the file SHALL NOT be detected as embedded mode.
        /// A valid Deserialize requires at least 12 bytes, so any shorter buffer returns null.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool FileSmallerThan12Bytes_IsNotDetectedAsEmbedded(PositiveInt sizeRaw)
        {
            // File size from 0 to 11 bytes
            int fileSize = sizeRaw.Get % 12; // 0..11
            byte[] fileData = new byte[fileSize];

            // Even if we try to put magic in the last 4 bytes (when size >= 4),
            // Deserialize should return null because the buffer is < 12 bytes
            if (fileSize >= 4)
            {
                BinaryPrimitives.WriteUInt32BigEndian(fileData.AsSpan(fileSize - 4, 4), EmbeddedFooter.FooterMagic);
            }

            // Attempt to deserialize the entire file as a footer — should return null
            EmbeddedFooter? result = EmbeddedFooter.Deserialize(fileData.AsSpan());
            return result == null;
        }

        /// <summary>
        /// **Validates: Requirements 2.6, 8.1, 8.2**
        ///
        /// Property 8: Embedded mode detection.
        /// For any file ≥ 12 bytes where the last 4 bytes do NOT equal FooterMagic,
        /// the file SHALL NOT be detected as embedded mode.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool FileWithoutFooterMagicAtEnd_IsNotDetectedAsEmbedded(NonNegativeInt extraSize, UInt32 lastFourValue)
        {
            // Ensure the last 4 bytes are NOT the footer magic
            uint nonMagicValue = lastFourValue == EmbeddedFooter.FooterMagic
                ? lastFourValue ^ 0x01  // Flip a bit to make it different
                : lastFourValue;

            // Generate a file that is at least 12 bytes
            int totalSize = 12 + (extraSize.Get % 10000);
            byte[] fileData = new byte[totalSize];

            // Fill with random data
            Random rng = new Random(extraSize.Get);
            rng.NextBytes(fileData);

            // Write non-magic value as the last 4 bytes
            BinaryPrimitives.WriteUInt32BigEndian(fileData.AsSpan(totalSize - 4, 4), nonMagicValue);

            // Detection: check last 4 bytes with IsMagicValid — should be false
            ReadOnlySpan<byte> lastFour = fileData.AsSpan(totalSize - 4, 4);
            return !EmbeddedFooter.IsMagicValid(lastFour);
        }

        /// <summary>
        /// **Validates: Requirements 2.6, 8.1, 8.2**
        ///
        /// Property 8: Embedded mode detection.
        /// For any file ≥ 12 bytes with a valid footer (last 4 bytes = FooterMagic),
        /// Deserialize SHALL return a non-null EmbeddedFooter, confirming embedded detection.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ValidFooterBuffer_DeserializesSuccessfully(NonNegativeInt indexSizeSeed)
        {
            // Generate a valid 12-byte footer buffer
            long indexSize = (long)(indexSizeSeed.Get % int.MaxValue);
            EmbeddedFooter footer = new EmbeddedFooter(indexSize);
            byte[] serialized = footer.Serialize();

            // Deserialize should succeed and detect as embedded
            EmbeddedFooter? result = EmbeddedFooter.Deserialize(serialized.AsSpan());
            if (result == null)
                return false;

            // Also verify IsMagicValid on the last 4 bytes
            return EmbeddedFooter.IsMagicValid(serialized.AsSpan(8, 4));
        }

        // ===================================================================
        // Property 7: Header validation with relative offsets
        // Feature: embedded-binary-index
        // **Validates: Requirements 4.5**
        // ===================================================================

        /// <summary>
        /// **Validates: Requirements 4.5**
        ///
        /// Property 7: Header validation with relative offsets.
        /// For any valid binary index created at a non-negative baseOffset,
        /// Open SHALL succeed because all internal pointers are non-negative
        /// and within the IndexSize bounds. This verifies that validation uses
        /// IndexSize (not total file size) and is independent of Base_Offset.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ValidPointersWithinIndexSize_OpenSucceeds(NonNegativeInt baseOffsetSeed)
        {
            // Use a range of baseOffsets to prove validation is independent of total file size
            long baseOffset = (long)(baseOffsetSeed.Get % 100_000);
            string path = GetTempFilePath($"valid_ptrs_{baseOffsetSeed.Get}.nkds");

            try
            {
                // Create a valid index at the given baseOffset
                // BinaryIndexFile.Create writes valid headers with pointers within IndexSize
                using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336, baseOffset: baseOffset))
                {
                    // The file is created with valid pointers relative to index start
                }

                // Open with the same baseOffset — should succeed because pointers are valid
                // relative to IndexSize, regardless of total file size (baseOffset + indexSize)
                using (BinaryIndexFile file = BinaryIndexFile.Open(path, baseOffset: baseOffset))
                {
                    // Verify the index was opened successfully
                    // The IndexSize should be positive (at least headers + empty structures)
                    return file.IndexSize > 0 && file.BaseOffset == baseOffset;
                }
            }
            catch
            {
                return false; // Open should not throw for valid pointers
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        /// <summary>
        /// **Validates: Requirements 4.5**
        ///
        /// Property 7: Header validation with relative offsets.
        /// For any header where a pointer field exceeds IndexSize (pointer > IndexSize),
        /// Open SHALL reject the header (both primary and secondary are corrupted),
        /// throwing InvalidDataException. This verifies bounds checking against IndexSize.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool PointersExceedingIndexSize_OpenFails(NonNegativeInt baseOffsetSeed, NonNegativeInt excessSeed)
        {
            long baseOffset = (long)(baseOffsetSeed.Get % 10_000);
            long excess = (long)(excessSeed.Get % 1_000_000) + 1; // At least 1 byte beyond
            string path = GetTempFilePath($"exceed_ptrs_{baseOffsetSeed.Get}_{excessSeed.Get}.nkds");

            try
            {
                // Create a valid index first
                using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336, baseOffset: baseOffset))
                {
                }

                // Read the file to get the actual IndexSize, then corrupt the header
                long indexSize;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    indexSize = stream.Length - baseOffset;
                }

                // Corrupt both headers: set ImageDirectoryOffset to exceed IndexSize
                CorruptHeaderField(path, baseOffset, h =>
                {
                    h.ImageDirectoryOffset = indexSize + excess;
                    return h;
                });

                // Open should fail because the pointer exceeds IndexSize
                try
                {
                    using (BinaryIndexFile file = BinaryIndexFile.Open(path, baseOffset: baseOffset))
                    {
                        return false; // Should not succeed
                    }
                }
                catch (InvalidDataException)
                {
                    return true; // Expected: validation rejects pointers > IndexSize
                }
            }
            catch (InvalidDataException)
            {
                return true; // Also acceptable if corruption causes early failure
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        /// <summary>
        /// **Validates: Requirements 4.5**
        ///
        /// Property 7: Header validation with relative offsets.
        /// For any header where a pointer field is negative (below the minimum valid offset),
        /// Open SHALL reject the header, throwing InvalidDataException.
        /// Pointers must be non-negative (≥ HeaderSize * 2 for structural offsets).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NegativePointers_OpenFails(NonNegativeInt baseOffsetSeed, PositiveInt negativeMagnitude)
        {
            long baseOffset = (long)(baseOffsetSeed.Get % 10_000);
            string path = GetTempFilePath($"neg_ptrs_{baseOffsetSeed.Get}_{negativeMagnitude.Get}.nkds");

            try
            {
                // Create a valid index first
                using (BinaryIndexFile file = BinaryIndexFile.Create(path, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336, baseOffset: baseOffset))
                {
                }

                // Corrupt both headers: set BlockIndexOffset to a negative value
                // (below the minimum valid offset of HeaderSize * 2 = 512)
                long invalidOffset = -(long)negativeMagnitude.Get;
                CorruptHeaderField(path, baseOffset, h =>
                {
                    h.BlockIndexOffset = invalidOffset;
                    return h;
                });

                // Open should fail because the pointer is negative/below minimum
                try
                {
                    using (BinaryIndexFile file = BinaryIndexFile.Open(path, baseOffset: baseOffset))
                    {
                        return false; // Should not succeed
                    }
                }
                catch (InvalidDataException)
                {
                    return true; // Expected: validation rejects negative pointers
                }
            }
            catch (InvalidDataException)
            {
                return true; // Also acceptable
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        /// <summary>
        /// **Validates: Requirements 4.5**
        ///
        /// Property 7: Header validation with relative offsets.
        /// For any valid binary index, validation is independent of total file size and Base_Offset.
        /// Creating the same index content at different baseOffsets (simulating different amounts
        /// of block data before the index) SHALL always succeed, proving that validation uses
        /// IndexSize rather than total file size.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ValidationIndependentOfFileSizeAndBaseOffset(NonNegativeInt offsetASeed, NonNegativeInt offsetBSeed)
        {
            // Two different baseOffsets — the index content is identical, only the
            // total file size differs
            long baseOffsetA = (long)(offsetASeed.Get % 50_000);
            long baseOffsetB = (long)(offsetBSeed.Get % 50_000) + 50_000; // Ensure different from A
            string pathA = GetTempFilePath($"indep_a_{offsetASeed.Get}.nkds");
            string pathB = GetTempFilePath($"indep_b_{offsetBSeed.Get}.nkds");

            try
            {
                // Create identical indexes at different baseOffsets
                using (BinaryIndexFile fileA = BinaryIndexFile.Create(pathA, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336, baseOffset: baseOffsetA))
                {
                }
                using (BinaryIndexFile fileB = BinaryIndexFile.Create(pathB, shardSize: 0, blockSize: 0x10000, maxOffsetBlocks: 336, baseOffset: baseOffsetB))
                {
                }

                // Both should open successfully — validation uses IndexSize, not total file size
                long indexSizeA, indexSizeB;
                using (BinaryIndexFile fileA = BinaryIndexFile.Open(pathA, baseOffset: baseOffsetA))
                {
                    indexSizeA = fileA.IndexSize;
                }
                using (BinaryIndexFile fileB = BinaryIndexFile.Open(pathB, baseOffset: baseOffsetB))
                {
                    indexSizeB = fileB.IndexSize;
                }

                // The IndexSize should be identical (same index content) even though
                // total file sizes differ (baseOffsetA + indexSizeA ≠ baseOffsetB + indexSizeB)
                return indexSizeA == indexSizeB && indexSizeA > 0;
            }
            catch
            {
                return false; // Neither should throw
            }
            finally
            {
                if (File.Exists(pathA)) File.Delete(pathA);
                if (File.Exists(pathB)) File.Delete(pathB);
            }
        }

        /// <summary>
        /// Helper method that reads both headers from a file at a given baseOffset,
        /// applies a mutation function, and rewrites both headers with valid checksums.
        /// This ensures the checksum is valid but the header content is corrupted.
        /// </summary>
        private static void CorruptHeaderField(string path, long baseOffset, Func<FileHeader, FileHeader> mutate)
        {
            byte[] primaryBuffer = new byte[FileHeader.HeaderSize];
            byte[] secondaryBuffer = new byte[FileHeader.HeaderSize];

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                // Read primary header at baseOffset
                stream.Position = baseOffset;
                ReadExactly(stream, primaryBuffer, FileHeader.HeaderSize);
                FileHeader primary = FileHeaderSerializer.Read(primaryBuffer);

                // Read secondary header at baseOffset + 0x100
                stream.Position = baseOffset + FileHeader.HeaderSize;
                ReadExactly(stream, secondaryBuffer, FileHeader.HeaderSize);
                FileHeader secondary = FileHeaderSerializer.Read(secondaryBuffer);

                // Apply mutation to both
                primary = mutate(primary);
                secondary = mutate(secondary);

                // Rewrite primary header with valid checksum
                FileHeaderSerializer.Write(primaryBuffer, primary);
                stream.Position = baseOffset;
                stream.Write(primaryBuffer, 0, FileHeader.HeaderSize);

                // Rewrite secondary header with valid checksum
                FileHeaderSerializer.Write(secondaryBuffer, secondary);
                stream.Position = baseOffset + FileHeader.HeaderSize;
                stream.Write(secondaryBuffer, 0, FileHeader.HeaderSize);

                stream.Flush();
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = stream.Read(buffer, totalRead, count - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException();
                totalRead += bytesRead;
            }
        }
    }
}
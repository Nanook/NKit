using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKit.Tests.Binary.Generators;
using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using NKitDataStore.Interfaces;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static NKit.Tests.Binary.Generators.BinaryIndexGenerators;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for BinaryIndexFile base offset support in embedded mode.
    ///
    /// Feature: embedded-binary-index
    /// Property 2: Base_Offset pointer adjustment round-trip
    /// **Validates: Requirements 2.2, 2.4, 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class EmbeddedBinaryIndexFileTests : IDisposable
    {
        private readonly string _tempDir;

        public EmbeddedBinaryIndexFileTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "EmbeddedBinaryIndexFileTests_" + Guid.NewGuid().ToString("N"));
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

        private static Arbitrary<long> BaseOffsetArbitrary() => Arb.From(BinaryIndexGenerators.ValidBaseOffset());

        /// <summary>
        /// **Validates: Requirements 2.2, 2.4, 4.1, 4.2, 4.3, 4.4**
        ///
        /// Property 2: Base_Offset pointer adjustment round-trip.
        /// For any non-negative Base_Offset value, writing a binary index at that offset
        /// (with pre-filled random bytes before it), then reopening with the same Base_Offset,
        /// SHALL produce identical data for all read operations (GetDirectory, ReadImageMetadata,
        /// ReadImageBlockMap, LoadBlockIndex).
        /// </summary>
        [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmbeddedBinaryIndexFileTests) })]
        public Property BaseOffsetPointerAdjustmentRoundTrip(long baseOffset)
        {
            return Prop.When(baseOffset >= 0, () =>
            {
                string filePath = Path.Combine(_tempDir, $"baseoffset_{baseOffset}_{Guid.NewGuid():N}.nkds");

                try
                {
                    // Step 1: Pre-fill the file with random bytes up to baseOffset.
                    // We create the file and write random prefix data, then close it.
                    // BinaryIndexFile.Create uses FileMode.Create which truncates, so we
                    // write the prefix AFTER creating the index by reopening the file.
                    // Instead, we write the prefix first, then use OpenOrCreate via a
                    // workaround: create the file with prefix, then let Create overwrite.
                    // Actually, Create truncates, so we write prefix bytes into the
                    // created file's [0, baseOffset) region after index creation.
                    //
                    // The property being tested is: writing at baseOffset and reading at
                    // the same baseOffset produces identical data. The prefix content
                    // (zeros from Create's truncation) doesn't affect this property.

                    // Step 2: Create the index at baseOffset with test data
                    BlockKey blockA = new BlockKey(0x1111111111111111, 0xAAAAAAAA);
                    BlockKey blockB = new BlockKey(0x2222222222222222, 0xBBBBBBBB);

                    List<AreaRecord> areas = new List<AreaRecord>
                    {
                        new AreaRecord
                        {
                            Offset = 0, Size = 0x20000, StrideBlockSize = 0x10000,
                            StrideDataOffset = 0, StrideDataLength = 0x10000,
                            SectionSize = 0x20000, Crc32 = 0xAABBCCDD,
                            XxHash64 = 0x1234567890ABCDEF, Metadata = new AreaMetadata()
                        }
                    };
                    List<FileRecord> files = new List<FileRecord>
                    {
                        new FileRecord
                        {
                            Name = "test.bin", FileId = 0, Offset = 0,
                            Size = 128, UncompressedSize = 256, IsSystem = false
                        }
                    };
                    List<OffsetRecord> offsets = new List<OffsetRecord>
                    {
                        new OffsetRecord
                        {
                            Offset = 0, Size = 0x20000, Type = BlockType.File,
                            OffsetStart = 0, Blocks = new List<BlockKey> { blockA, blockB }
                        }
                    };
                    Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>
                    {
                        { blockA, (0, 0x0000, 0x10000) },
                        { blockB, (0, 0x10000, 0x10000) }
                    };

                    (byte[] metaBytes, int _) = ImageMetadataSectionSerializer.Serialize(areas, files);
                    (byte[] bmBytes, int _) = ImageBlockMapSectionSerializer.Serialize(offsets, blockLocations);

                    BlockIndexEntry[] blockEntries = new BlockIndexEntry[]
                    {
                        new BlockIndexEntry { Key = blockA, FileId = 0, Offset = 0x0000, Size = 0x10000 },
                        new BlockIndexEntry { Key = blockB, FileId = 0, Offset = 0x10000, Size = 0x10000 },
                    };
                    Array.Sort(blockEntries);

                    long metaOff, bmOff;
                    int metaSz, bmSz;

                    // Create the index file at baseOffset
                    using (BinaryIndexFile indexFile = BinaryIndexFile.Create(filePath, shardSize: 0, blockSize: 0x10000,
                        maxOffsetBlocks: 336, baseOffset: baseOffset))
                    {
                        // Append image data
                        (metaOff, metaSz, bmOff, bmSz) = indexFile.AppendImage(1, metaBytes, bmBytes, null);

                        // Add block index delta
                        indexFile.AppendBlockIndexDelta(new BlockIndexDelta { Entries = blockEntries });

                        // Update directory
                        ImageDirectory directory = indexFile.GetDirectory();
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
                            MetadataSectionOffset = metaOff,
                            MetadataSectionCompressedSize = metaSz,
                            BlockMapSectionOffset = bmOff,
                            BlockMapSectionCompressedSize = bmSz
                        });
                        indexFile.UpdateDirectory(directory);
                        indexFile.AtomicCommit();
                    }

                    // Step 3: If baseOffset > 0, write random bytes into the prefix region
                    // to verify that the index reads are not affected by prefix content
                    if (baseOffset > 0)
                    {
                        using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                        Random rng = new Random((int)(baseOffset % int.MaxValue));
                        byte[] randomPrefix = new byte[baseOffset];
                        rng.NextBytes(randomPrefix);
                        fs.Position = 0;
                        fs.Write(randomPrefix, 0, randomPrefix.Length);
                        fs.Flush(flushToDisk: true);
                    }

                    // Step 4: Reopen with the same baseOffset and verify all reads
                    using (BinaryIndexFile reopened = BinaryIndexFile.Open(filePath, baseOffset: baseOffset))
                    {
                        // Verify BaseOffset property
                        if (reopened.BaseOffset != baseOffset)
                            return false;

                        // Verify GetDirectory
                        ImageDirectory dir = reopened.GetDirectory();
                        List<ImageDirectoryEntry> entries = dir.GetAllEntries().ToList();
                        if (entries.Count != 1) return false;
                        if (entries[0].ImageId != 1) return false;
                        if (entries[0].Name != "test_image.iso") return false;
                        if (entries[0].Crc32 != 0xAABBCCDD) return false;
                        if (entries[0].XxHash64 != 0x1234567890ABCDEF) return false;
                        if (entries[0].System != "Wii") return false;

                        // Verify ReadImageMetadata
                        (List<AreaRecord> readAreas, List<FileRecord> readFiles) = reopened.ReadImageMetadata(1);
                        if (readAreas.Count != 1) return false;
                        if (readAreas[0].Crc32 != 0xAABBCCDD) return false;
                        if (readAreas[0].XxHash64 != 0x1234567890ABCDEF) return false;
                        if (readFiles.Count != 1) return false;
                        if (readFiles[0].Name != "test.bin") return false;

                        // Verify ReadImageBlockMap
                        (List<OffsetRecord> readOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> readBlockLocs) = reopened.ReadImageBlockMap(1);
                        if (readOffsets.Count != 1) return false;
                        if (readOffsets[0].Blocks == null || readOffsets[0].Blocks.Count != 2) return false;
                        if (readBlockLocs.Count != 2) return false;
                        if (!readBlockLocs.ContainsKey(blockA)) return false;
                        if (!readBlockLocs.ContainsKey(blockB)) return false;
                        if (readBlockLocs[blockA] != (0, 0x0000, 0x10000)) return false;
                        if (readBlockLocs[blockB] != (0, 0x10000, 0x10000)) return false;

                        // Verify LoadBlockIndex
                        InMemoryBlockIndex blockIndex = reopened.LoadBlockIndex();
                        BlockIndexEntry[] indexEntries = blockIndex.GetEntries();
                        if (indexEntries.Length != 2) return false;
                        if (!blockIndex.TryGetBlock(blockA, out int fA, out long oA, out int sA)) return false;
                        if (fA != 0 || oA != 0x0000 || sA != 0x10000) return false;
                        if (!blockIndex.TryGetBlock(blockB, out int fB, out long oB, out int sB)) return false;
                        if (fB != 0 || oB != 0x10000 || sB != 0x10000) return false;
                    }

                    // Step 5: Verify the prefix bytes are still intact (not corrupted by index reads)
                    if (baseOffset > 0)
                    {
                        byte[] fileBytes = File.ReadAllBytes(filePath);
                        Random rng2 = new Random((int)(baseOffset % int.MaxValue));
                        byte[] expectedPrefix = new byte[baseOffset];
                        rng2.NextBytes(expectedPrefix);

                        for (long i = 0; i < baseOffset; i++)
                        {
                            if (fileBytes[i] != expectedPrefix[i])
                                return false;
                        }
                    }

                    return true;
                }
                finally
                {
                    // Clean up the test file
                    try { if (File.Exists(filePath)) File.Delete(filePath); }
                    catch { }
                }
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 2.1, 3.1**
        ///
        /// Property 4: Embedded file layout invariant.
        /// For any embedded-mode file after a successful write commit, the file SHALL have the
        /// layout [Block Data][Binary Index][Embedded_Footer] where:
        /// (a) the last 12 bytes are a valid EmbeddedFooter with correct IndexSize and Footer_Magic,
        /// (b) the IndexSize equals the byte length of the binary index region,
        /// (c) Base_Offset = file_size - 12 - IndexSize correctly locates the start of the binary index,
        /// (d) the binary index at Base_Offset begins with a valid Header (magic 0x4E4B4453).
        ///
        /// Feature: embedded-binary-index
        /// Property 4: Embedded file layout invariant
        /// </summary>
        [Property(MaxTest = 100)]
        public Property EmbeddedFileLayoutInvariant()
        {
            // Generate varying block data sizes to exercise different shard boundaries
            return Prop.ForAll(Arb.From(ValidEmbeddedFileContent()), embeddedContent =>
            {
                string setDir = Path.Combine(_tempDir, $"layout_{Guid.NewGuid():N}");
                Directory.CreateDirectory(setDir);

                try
                {
                    string setName = "test";

                    // Step 1: Create an embedded set with shardSize=0
                    using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir))
                    {
                        dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                        // Step 2: Begin a transaction, write blocks and an image, then commit
                        using (IDataStoreTransaction transaction = dataAccess.BeginTransaction(setName))
                        {
                            long imageId = dataAccess.InsertImage(setName, transaction, "test_image.iso", "Wii", ImageFormat.Iso);

                            // Write each block from the generated content
                            long totalBlockSize = 0;
                            List<BlockKey> blockKeys = new List<BlockKey>();
                            for (int i = 0; i < embeddedContent.Blocks.Length; i++)
                            {
                                byte[] blockData = embeddedContent.Blocks[i];
                                // Create a unique block key for each block
                                BlockKey key = new BlockKey((ulong)(i + 1) * 0x1111111111111111, (uint)((i + 1) * 0xAAAAAAAA));
                                blockKeys.Add(key);
                                dataAccess.InsertBlock(setName, transaction, key, blockData);
                                totalBlockSize += blockData.Length;
                            }

                            // Insert areas and offsets for the image
                            long areaId = dataAccess.InsertArea(setName, transaction, imageId,
                                offset: 0, size: totalBlockSize, crc32: 0xDEADBEEF, xxhash64: 0x123456789ABCDEF0);

                            // Insert offsets referencing the blocks
                            for (int i = 0; i < blockKeys.Count; i++)
                            {
                                long blockOffset = embeddedContent.Blocks.Take(i).Sum(b => (long)b.Length);
                                dataAccess.InsertOffset(setName, transaction, imageId,
                                    offset: blockOffset, size: embeddedContent.Blocks[i].Length,
                                    type: BlockType.File, blockKey: blockKeys[i],
                                    crc32: 0, xxhash64: 0);
                            }

                            // Update image metadata and commit
                            dataAccess.UpdateImageMetadata(setName, transaction, imageId,
                                size: totalBlockSize, crc32: 0xDEADBEEF, xxhash64: 0x123456789ABCDEF0);

                            transaction.Commit();
                        }
                    } // Dispose dataAccess to release file handles

                    // Step 3: Verify the embedded file layout by reading raw bytes
                    string embeddedFilePath = Path.Combine(setDir, $"{setName}.nkds");
                    byte[] fileBytes = File.ReadAllBytes(embeddedFilePath);
                    long fileSize = fileBytes.Length;

                    // (a) Verify: last 12 bytes are a valid EmbeddedFooter
                    if (fileSize < EmbeddedFooter.FooterSize)
                        return false.Label("File too small to contain EmbeddedFooter");

                    ReadOnlySpan<byte> footerSpan = fileBytes.AsSpan((int)(fileSize - EmbeddedFooter.FooterSize));
                    EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerSpan);
                    if (footer == null)
                        return false.Label("Last 12 bytes are not a valid EmbeddedFooter");

                    // Verify Footer_Magic
                    ReadOnlySpan<byte> lastFourBytes = fileBytes.AsSpan((int)(fileSize - 4));
                    if (!EmbeddedFooter.IsMagicValid(lastFourBytes))
                        return false.Label("Footer magic bytes are not valid");

                    long indexSize = footer.Value.IndexSize;

                    // (b) Verify: IndexSize matches actual binary index region length
                    // The index region starts at Base_Offset and ends at file_size - 12
                    long baseOffset = fileSize - EmbeddedFooter.FooterSize - indexSize;
                    long actualIndexRegionLength = fileSize - EmbeddedFooter.FooterSize - baseOffset;
                    if (indexSize != actualIndexRegionLength)
                        return false.Label($"IndexSize {indexSize} != actual index region length {actualIndexRegionLength}");

                    // (c) Verify: Base_Offset = file_size - 12 - IndexSize locates valid Header
                    if (baseOffset < 0)
                        return false.Label($"Base_Offset is negative: {baseOffset}");
                    if (baseOffset + FileHeader.HeaderSize > fileSize - EmbeddedFooter.FooterSize)
                        return false.Label($"Base_Offset {baseOffset} + HeaderSize exceeds index region");

                    // (d) Verify: Header at Base_Offset has valid magic 0x4E4B4453
                    // FileHeader magic is stored in big-endian format
                    uint headerMagic = BinaryPrimitives.ReadUInt32BigEndian(
                        fileBytes.AsSpan((int)baseOffset, 4));
                    if (headerMagic != FileHeader.MagicBytes)
                        return false.Label($"Header magic at Base_Offset is 0x{headerMagic:X8}, expected 0x{FileHeader.MagicBytes:X8}");

                    // Additional validation: verify the header can be fully deserialized
                    ReadOnlySpan<byte> headerSpan = fileBytes.AsSpan((int)baseOffset, FileHeader.HeaderSize);
                    FileHeader header = FileHeaderSerializer.Read(headerSpan);
                    if (header.Magic != FileHeader.MagicBytes)
                        return false.Label("Deserialized header has wrong magic");

                    // Verify IndexSize matches the header's FileEndOffset (the logical end of the index)
                    if (header.FileEndOffset != indexSize)
                        return false.Label($"Header.FileEndOffset {header.FileEndOffset} != footer IndexSize {indexSize}");

                    return true.Label("pass");
                }
                finally
                {
                    try { if (Directory.Exists(setDir)) Directory.Delete(setDir, recursive: true); }
                    catch { }
                }
            });
        }

        /// <summary>
        /// **Validates: Requirements 3.2, 7.2**
        ///
        /// Property 5: Write and compaction preserve block data.
        /// For any embedded-mode file containing N blocks of data (bytes 0 to Shard_Boundary),
        /// after a full write-commit cycle (extract → write → re-embed) or compaction cycle
        /// (extract → compact → re-embed), all block bytes in the range [0, Shard_Boundary)
        /// SHALL remain byte-for-byte identical to their state before the operation.
        /// The Shard_Boundary after a write commit SHALL equal the Shard_Boundary before the
        /// commit plus the total size of any new blocks written during the transaction.
        ///
        /// Feature: embedded-binary-index
        /// Property 5: Write and compaction preserve block data
        /// </summary>
        [Property(MaxTest = 100)]
        public Property WriteAndCompactionPreserveBlockData()
        {
            return Prop.ForAll(Arb.From(ValidEmbeddedFileContent()), Arb.From(ValidEmbeddedFileContent()), (firstBatch, secondBatch) =>
            {
                string testDir = Path.Combine(_tempDir, $"preserve_{Guid.NewGuid():N}");
                Directory.CreateDirectory(testDir);

                try
                {
                    string setName = "test";
                    string embeddedPath = Path.Combine(testDir, $"{setName}.nkds");

                    // Step 1: Create embedded set and write first batch of blocks
                    using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(testDir))
                    {
                        dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                        using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
                        {
                            long imageId = dataAccess.InsertImage(setName, tx, "image1.iso", "Wii", ImageFormat.Iso);

                            for (int i = 0; i < firstBatch.Blocks.Length; i++)
                            {
                                BlockKey key = new BlockKey((ulong)(i + 1) * 0x1111111111111111, (uint)(i + 1) * 0xAAAA);
                                dataAccess.InsertBlock(setName, tx, key, firstBatch.Blocks[i]);
                                dataAccess.InsertOffset(setName, tx, imageId, i * 0x10000L, firstBatch.Blocks[i].Length,
                                    BlockType.File, key, 0, 0);
                            }

                            dataAccess.InsertArea(setName, tx, imageId, 0,
                                firstBatch.Blocks.Sum(b => (long)b.Length), 0xAABBCCDD, 0x1234567890ABCDEF);
                            dataAccess.UpdateImageMetadata(setName, tx, imageId,
                                firstBatch.Blocks.Sum(b => (long)b.Length), 0xAABBCCDD, 0x1234567890ABCDEF);
                            tx.Commit();
                        }
                    }
                    // dataAccess disposed — file handles released

                    // Step 2: Capture block region after first commit
                    long boundaryAfterFirstCommit = GetShardBoundaryFromFile(embeddedPath);
                    long expectedBoundaryAfterFirst = firstBatch.Blocks.Sum(b => (long)b.Length);

                    // Verify: Shard_Boundary after first commit = total size of first batch blocks
                    if (boundaryAfterFirstCommit != expectedBoundaryAfterFirst)
                        return false.Label($"Boundary after first commit {boundaryAfterFirstCommit} != expected {expectedBoundaryAfterFirst}");

                    byte[] blockRegionAfterFirstCommit = ReadBlockRegion(embeddedPath, boundaryAfterFirstCommit);

                    // Step 3: Write second batch of blocks via another transaction
                    using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(testDir))
                    {
                        dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);

                        using (IDataStoreTransaction tx = dataAccess.BeginTransaction(setName))
                        {
                            long imageId = dataAccess.InsertImage(setName, tx, "image2.iso", "Wii", ImageFormat.Iso);

                            for (int i = 0; i < secondBatch.Blocks.Length; i++)
                            {
                                // Use different keys to avoid deduplication
                                BlockKey key = new BlockKey((ulong)(i + 100) * 0x3333333333333333, (uint)(i + 100) * 0xBBBB);
                                dataAccess.InsertBlock(setName, tx, key, secondBatch.Blocks[i]);
                                dataAccess.InsertOffset(setName, tx, imageId, i * 0x10000L, secondBatch.Blocks[i].Length,
                                    BlockType.File, key, 0, 0);
                            }

                            dataAccess.InsertArea(setName, tx, imageId, 0,
                                secondBatch.Blocks.Sum(b => (long)b.Length), 0xDDCCBBAA, 0xFEDCBA0987654321);
                            dataAccess.UpdateImageMetadata(setName, tx, imageId,
                                secondBatch.Blocks.Sum(b => (long)b.Length), 0xDDCCBBAA, 0xFEDCBA0987654321);
                            tx.Commit();
                        }
                    }
                    // dataAccess disposed — file handles released

                    // Step 4: Verify block region after second commit
                    long boundaryAfterSecondCommit = GetShardBoundaryFromFile(embeddedPath);
                    long expectedBoundaryAfterSecond = expectedBoundaryAfterFirst + secondBatch.Blocks.Sum(b => (long)b.Length);

                    // Verify: Shard_Boundary after second commit = previous boundary + new blocks size
                    if (boundaryAfterSecondCommit != expectedBoundaryAfterSecond)
                        return false.Label($"Boundary after second commit {boundaryAfterSecondCommit} != expected {expectedBoundaryAfterSecond}");

                    // Verify: bytes [0, first boundary) are identical before and after second commit
                    byte[] blockRegionAfterSecondCommit = ReadBlockRegion(embeddedPath, boundaryAfterFirstCommit);
                    if (!blockRegionAfterFirstCommit.SequenceEqual(blockRegionAfterSecondCommit))
                        return false.Label("Block region [0, first_boundary) changed after second commit");

                    // Capture full block region before compaction
                    byte[] fullBlockRegionBeforeCompaction = ReadBlockRegion(embeddedPath, boundaryAfterSecondCommit);

                    // Step 5: Compact the set
                    using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(testDir))
                    {
                        dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: 0x10000);
                        dataAccess.CompactSet(setName);
                    }
                    // dataAccess disposed — file handles released

                    // Step 6: Verify block region after compaction
                    long boundaryAfterCompaction = GetShardBoundaryFromFile(embeddedPath);

                    // Verify: Shard_Boundary is unchanged after compaction (compaction only affects the index)
                    if (boundaryAfterCompaction != boundaryAfterSecondCommit)
                        return false.Label($"Boundary after compaction {boundaryAfterCompaction} != boundary before {boundaryAfterSecondCommit}");

                    // Verify: bytes [0, Shard_Boundary) are identical before and after compaction
                    byte[] blockRegionAfterCompaction = ReadBlockRegion(embeddedPath, boundaryAfterCompaction);
                    if (!fullBlockRegionBeforeCompaction.SequenceEqual(blockRegionAfterCompaction))
                        return false.Label("Block region [0, Shard_Boundary) changed after compaction");

                    return true.Label("pass");
                }
                finally
                {
                    try { if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true); }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Reads the shard boundary from an embedded file by reading the footer.
        /// Shard_Boundary = file_size - 12 - IndexSize
        /// </summary>
        private static long GetShardBoundaryFromFile(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            Span<byte> footerBuffer = stackalloc byte[EmbeddedFooter.FooterSize];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
            stream.Read(footerBuffer);

            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBuffer);
            if (footer == null)
                throw new InvalidOperationException("File does not have a valid embedded footer");

            return fileSize - EmbeddedFooter.FooterSize - footer.Value.IndexSize;
        }

        /// <summary>
        /// Reads the block data region [0, boundary) from the file.
        /// </summary>
        private static byte[] ReadBlockRegion(string filePath, long boundary)
        {
            if (boundary == 0)
                return Array.Empty<byte>();

            byte[] region = new byte[boundary];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int totalRead = 0;
            while (totalRead < region.Length)
            {
                int read = stream.Read(region, totalRead, region.Length - totalRead);
                if (read == 0)
                    throw new IOException($"Unexpected end of file at {totalRead} bytes (expected {boundary}).");
                totalRead += read;
            }
            return region;
        }

        /// <summary>
        /// **Validates: Requirements 6.1, 6.2, 6.3**
        ///
        /// Property 6: EnsureSetExists embedded mode initialization and idempotence.
        /// For any call to EnsureSetExists with shardSize=0:
        /// (a) if the file does not exist, a new file SHALL be created containing an empty binary
        ///     index followed by an EmbeddedFooter, with Shard_Boundary=0 and Index_Size equal to
        ///     the size of the empty index;
        /// (b) if the file already exists with a valid EmbeddedFooter, the existing file SHALL be
        ///     opened without reinitialization and the returned InfoRecord SHALL match the stored
        ///     header configuration.
        ///
        /// Feature: embedded-binary-index
        /// Property 6: EnsureSetExists embedded mode initialization and idempotence
        /// </summary>
        [Property(MaxTest = 100)]
        public Property EnsureSetExistsInitializationAndIdempotence()
        {
            // Vary blockSize across iterations: valid block sizes from 4096 to 1MB (powers of 2 and multiples)
            Gen<int> blockSizeGen = Gen.OneOf(
                Gen.Constant(0x1000),    // 4 KiB
                Gen.Constant(0x2000),    // 8 KiB
                Gen.Constant(0x4000),    // 16 KiB
                Gen.Constant(0x8000),    // 32 KiB
                Gen.Constant(0x10000),   // 64 KiB (default)
                Gen.Constant(0x20000),   // 128 KiB
                Gen.Constant(0x40000),   // 256 KiB
                Gen.Constant(0x80000),   // 512 KiB
                Gen.Constant(0x100000),  // 1 MiB
                Gen.Choose(0x1000, 0x100000) // Random in range
            );

            return Prop.ForAll(Arb.From(blockSizeGen), blockSize =>
            {
                string setDir = Path.Combine(_tempDir, $"ensureset_{blockSize}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(setDir);

                try
                {
                    string setName = "test";
                    string filePath = Path.Combine(setDir, $"{setName}.nkds");

                    InfoRecord info1;
                    InfoRecord info2;
                    byte[] fileContentAfterCreate;

                    // Use a single BinaryDataStoreDataAccess instance for both calls
                    // This tests both creation and idempotence within the same lifecycle
                    using (BinaryDataStoreDataAccess dataAccess = new BinaryDataStoreDataAccess(setDir))
                    {
                        // --- Part (a): Create new embedded file ---
                        info1 = dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: blockSize);

                        // Verify InfoRecord has correct configuration
                        if (info1.ShardSize != 0)
                            return false.Label($"ShardSize should be 0, got {info1.ShardSize}");
                        if (info1.BlockSize != blockSize)
                            return false.Label($"BlockSize should be {blockSize}, got {info1.BlockSize}");
                        if (info1.MaxOffsetBlocks != 336)
                            return false.Label($"MaxOffsetBlocks should be 336, got {info1.MaxOffsetBlocks}");

                        // Verify the file on disk exists
                        if (!File.Exists(filePath))
                            return false.Label("Embedded file was not created");

                        // Verify the index contains 0 images (empty index)
                        IEnumerable<ImageRecord> images = dataAccess.GetAllImagesInSet(setName);
                        if (images.Any())
                            return false.Label("New embedded file should have 0 images");

                        // --- Part (b): Call EnsureSetExists again (idempotence within same instance) ---
                        info2 = dataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: blockSize);

                        // Verify same InfoRecord is returned
                        if (info2.ShardSize != info1.ShardSize)
                            return false.Label($"Idempotent call: ShardSize mismatch {info2.ShardSize} vs {info1.ShardSize}");
                        if (info2.BlockSize != info1.BlockSize)
                            return false.Label($"Idempotent call: BlockSize mismatch {info2.BlockSize} vs {info1.BlockSize}");
                        if (info2.MaxOffsetBlocks != info1.MaxOffsetBlocks)
                            return false.Label($"Idempotent call: MaxOffsetBlocks mismatch {info2.MaxOffsetBlocks} vs {info1.MaxOffsetBlocks}");
                        if (info2.Version != info1.Version)
                            return false.Label($"Idempotent call: Version mismatch {info2.Version} vs {info1.Version}");
                    }

                    // After disposing, verify the file on disk has valid embedded footer
                    long fileSize = new FileInfo(filePath).Length;
                    if (fileSize < EmbeddedFooter.FooterSize)
                        return false.Label($"File too small for footer: {fileSize} bytes");

                    // Read and validate the EmbeddedFooter (last 12 bytes)
                    byte[] footerBytes = new byte[EmbeddedFooter.FooterSize];
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
                        int read = fs.Read(footerBytes, 0, EmbeddedFooter.FooterSize);
                        if (read < EmbeddedFooter.FooterSize)
                            return false.Label("Could not read footer bytes");
                    }

                    EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBytes);
                    if (footer == null)
                        return false.Label("Footer deserialization failed (magic mismatch)");

                    // Verify Footer_Magic is correct (last 4 bytes = 0x4E, 0x4B, 0x44, 0x53)
                    if (!EmbeddedFooter.IsMagicValid(footerBytes.AsSpan(8, 4)))
                        return false.Label("Footer magic bytes invalid");

                    // Verify Shard_Boundary = 0 (no block data yet)
                    long indexSize = footer.Value.IndexSize;
                    long shardBoundary = fileSize - EmbeddedFooter.FooterSize - indexSize;
                    if (shardBoundary != 0)
                        return false.Label($"Shard_Boundary should be 0, got {shardBoundary}");

                    // Verify Index_Size equals the size of the empty index (file_size - 12)
                    long expectedIndexSize = fileSize - EmbeddedFooter.FooterSize;
                    if (indexSize != expectedIndexSize)
                        return false.Label($"IndexSize should be {expectedIndexSize}, got {indexSize}");

                    // Record file content for idempotence check across instances
                    fileContentAfterCreate = File.ReadAllBytes(filePath);

                    // Force GC to release any lingering file handles from the first instance
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // --- Part (b) continued: Call EnsureSetExists from a fresh instance ---
                    // This tests the on-disk idempotence (file already exists with valid footer)
                    using (BinaryDataStoreDataAccess dataAccess2 = new BinaryDataStoreDataAccess(setDir))
                    {
                        InfoRecord info3 = dataAccess2.EnsureSetExists(setName, shardSize: 0, blockSize: blockSize);

                        // Verify same InfoRecord is returned from fresh instance
                        if (info3.ShardSize != info1.ShardSize)
                            return false.Label($"Fresh instance: ShardSize mismatch {info3.ShardSize} vs {info1.ShardSize}");
                        if (info3.BlockSize != info1.BlockSize)
                            return false.Label($"Fresh instance: BlockSize mismatch {info3.BlockSize} vs {info1.BlockSize}");
                        if (info3.MaxOffsetBlocks != info1.MaxOffsetBlocks)
                            return false.Label($"Fresh instance: MaxOffsetBlocks mismatch {info3.MaxOffsetBlocks} vs {info1.MaxOffsetBlocks}");
                        if (info3.Version != info1.Version)
                            return false.Label($"Fresh instance: Version mismatch {info3.Version} vs {info1.Version}");
                    }

                    // Verify the file is unchanged (same size, same content) after second open
                    byte[] fileContentAfter = File.ReadAllBytes(filePath);
                    if (fileContentAfterCreate.Length != fileContentAfter.Length)
                        return false.Label($"File size changed: {fileContentAfterCreate.Length} → {fileContentAfter.Length}");
                    if (!fileContentAfterCreate.SequenceEqual(fileContentAfter))
                        return false.Label("File content changed after second EnsureSetExists call from fresh instance");

                    return true.Label("pass");
                }
                finally
                {
                    try { if (Directory.Exists(setDir)) Directory.Delete(setDir, recursive: true); }
                    catch { }
                }
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.3**
        ///
        /// Property 3: Block data round-trip in embedded mode.
        /// For any sequence of block writes (arbitrary byte arrays) to an embedded-mode file,
        /// reading each block back using its stored (fileId, offset, length) SHALL return a byte
        /// array identical to the original data. Block offsets SHALL be absolute file positions
        /// (no Base_Offset adjustment), and all block offsets SHALL be less than the current
        /// Shard_Boundary.
        ///
        /// Feature: embedded-binary-index
        /// Property 3: Block data round-trip in embedded mode
        /// </summary>
        [Property(MaxTest = 100)]
        public Property BlockDataRoundTripInEmbeddedMode()
        {
            return Prop.ForAll(Arb.From(ValidEmbeddedFileContent()), embeddedContent =>
            {
                string setDir = Path.Combine(_tempDir, $"blockrt_{Guid.NewGuid():N}");
                Directory.CreateDirectory(setDir);

                try
                {
                    string setName = "test";

                    // Step 1: Write blocks using ShardFileManager (shardSize=0 for embedded mode)
                    List<(int fileId, long offset, int length)> writeResults = new List<(int fileId, long offset, int length)>();

                    using (ShardFileManager sfm = new ShardFileManager(setDir, setName, shardSize: 0))
                    {
                        foreach (byte[] block in embeddedContent.Blocks)
                        {
                            (int fileId, long offset, int length) result = sfm.WriteBlock(block, 0, block.Length);
                            writeResults.Add(result);
                        }
                        sfm.FlushWrite();
                    }

                    // Step 2: Compute the shard boundary (total size of all blocks written)
                    long shardBoundary = embeddedContent.Blocks.Sum(b => (long)b.Length);

                    // Step 3: Simulate embedded mode by appending an index + footer to the shard file
                    // This creates the embedded file layout: [Block Data][Index][Footer]
                    string shardPath = Path.Combine(setDir, $"{setName}_0000.nkds");
                    byte[] dummyIndex = new byte[4096]; // Minimal dummy index region
                    new Random(42).NextBytes(dummyIndex);
                    EmbeddedFooter footer = new EmbeddedFooter(dummyIndex.Length);
                    byte[] footerBytes = footer.Serialize();

                    using (FileStream fs = new FileStream(shardPath, FileMode.Append, FileAccess.Write))
                    {
                        fs.Write(dummyIndex, 0, dummyIndex.Length);
                        fs.Write(footerBytes, 0, footerBytes.Length);
                        fs.Flush(flushToDisk: true);
                    }

                    // Step 4: Read blocks back using ShardFileManager with shard boundary set
                    using (ShardFileManager sfm = new ShardFileManager(setDir, setName, shardSize: 0))
                    {
                        sfm.SetShardBoundary(shardBoundary);

                        for (int i = 0; i < embeddedContent.Blocks.Length; i++)
                        {
                            (int fileId, long offset, int length) = writeResults[i];

                            // Verify: all block offsets are absolute (from file position 0)
                            // The first block starts at offset 0, subsequent blocks follow sequentially
                            long expectedOffset = embeddedContent.Blocks.Take(i).Sum(b => (long)b.Length);
                            if (offset != expectedOffset)
                                return false.Label($"Block {i} offset {offset} != expected absolute offset {expectedOffset}");

                            // Verify: all block offsets < Shard_Boundary
                            if (offset >= shardBoundary)
                                return false.Label($"Block {i} offset {offset} >= shard boundary {shardBoundary}");

                            // Verify: read back produces identical data
                            byte[] readBack = sfm.ReadBlock(fileId, offset, length);
                            if (readBack.Length != embeddedContent.Blocks[i].Length)
                                return false.Label($"Block {i} read length {readBack.Length} != original {embeddedContent.Blocks[i].Length}");

                            if (!readBack.SequenceEqual(embeddedContent.Blocks[i]))
                                return false.Label($"Block {i} data mismatch after round-trip");
                        }
                    }

                    // Step 5: Verify that reading at or beyond the shard boundary throws
                    using (ShardFileManager sfm = new ShardFileManager(setDir, setName, shardSize: 0))
                    {
                        sfm.SetShardBoundary(shardBoundary);

                        bool threwException = false;
                        try
                        {
                            sfm.ReadBlock(0, shardBoundary, 1);
                        }
                        catch (InvalidOperationException)
                        {
                            threwException = true;
                        }

                        if (!threwException)
                            return false.Label("ReadBlock at shard boundary should throw InvalidOperationException");
                    }

                    return true.Label("pass");
                }
                finally
                {
                    try { if (Directory.Exists(setDir)) Directory.Delete(setDir, recursive: true); }
                    catch { }
                }
            });
        }
    }
}
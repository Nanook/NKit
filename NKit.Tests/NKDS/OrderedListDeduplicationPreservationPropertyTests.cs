using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Property-based tests verifying OrderedList deduplication preservation.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 2: OrderedList deduplication preservation
    ///
    /// For any sequence of file insertions where K entries share the same FsOffset,
    /// the OrderedList SHALL contain exactly one entry per unique offset, and InsertIfMissing
    /// SHALL return existed=true for all subsequent insertions at that offset without modifying the list.
    ///
    /// **Validates: Requirements 1.5, 3.1**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class OrderedListDeduplicationPreservationPropertyTests
    {
        /// <summary>
        /// Represents a file insertion request with offset, size, and filesystem type.
        /// </summary>
        private record struct FileInsertionRequest(long Offset, long Size, FsType FsType);

        /// <summary>
        /// Filesystem types suitable for file insertion testing.
        /// </summary>
        private static readonly FsType[] ValidFsTypes = new[]
        {
            FsType.Iso9660,
            FsType.Joliet,
            FsType.Udf,
            FsType.Other
        };

        /// <summary>
        /// Creates a minimal ISO9660 FstContext suitable for testing AddFile behavior.
        /// </summary>
        private static FstContext CreateTestFstContext()
        {
            AreaInfo areaInfo = new AreaInfo(0, AreaType.FileSystem, 0);
            byte[] headerData = new byte[0x8000 * 0x40];
            ImageHeader header = new ImageHeader(headerData, areaInfo);
            return header.FstContext;
        }

        /// <summary>
        /// Generator for file insertion requests with controlled collision patterns.
        /// Uses a small offset pool to force duplicate offsets.
        /// </summary>
        private static Gen<FileInsertionRequest[]> FileInsertionRequestsGen()
        {
            return from count in Gen.Choose(2, 20)
                   from poolSize in Gen.Choose(1, Math.Max(1, count / 2))
                   from offsetPool in Gen.ArrayOf(Gen.Choose(1, 50000).Select(o => (long)o * 0x800), poolSize)
                   from requests in Gen.ArrayOf(
                       from offsetIdx in Gen.Choose(0, poolSize - 1)
                       from size in Gen.Choose(1, 10000).Select(s => (long)s)
                       from fsTypeIdx in Gen.Choose(0, ValidFsTypes.Length - 1)
                       select new FileInsertionRequest(
                           offsetPool[offsetIdx % offsetPool.Length],
                           size,
                           ValidFsTypes[fsTypeIdx]),
                       count)
                   select requests;
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 3.1**
        ///
        /// Property 2: OrderedList deduplication preservation.
        /// For any sequence of file insertions where K entries share the same FsOffset,
        /// the OrderedList SHALL contain exactly one entry per unique offset, and
        /// the OrderedList entries SHALL be in sorted offset order.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OrderedList_ContainsExactlyOneEntryPerUniqueOffset()
        {
            return Prop.ForAll(FileInsertionRequestsGen().ToArbitrary(), insertions =>
            {
                FstContext fstContext = CreateTestFstContext();
                FstFolder rootFolder = new FstFolder(FsType.Iso9660);

                // Perform all insertions
                foreach (FileInsertionRequest req in insertions)
                {
                    fstContext.AddFile(
                        rootFolder,
                        $"file_{req.Offset:X}_{req.Size}_{req.FsType}",
                        req.FsType,
                        req.Offset,
                        req.Size,
                        FsItemType.File,
                        false,
                        FsBlockEndian.Both,
                        out _,
                        out _);
                }

                // Compute expected unique offsets
                List<long> uniqueOffsets = insertions.Select(r => r.Offset).Distinct().ToList();

                // Assert: OrderedList contains exactly one entry per unique offset
                bool countCorrect = fstContext.FileSystem.Count == uniqueOffsets.Count;

                // Assert: OrderedList entries are in sorted offset order
                bool sorted = true;
                for (int i = 1; i < fstContext.FileSystem.Count; i++)
                {
                    if (fstContext.FileSystem[i].FsOffset <= fstContext.FileSystem[i - 1].FsOffset)
                    {
                        sorted = false;
                        break;
                    }
                }

                return countCorrect
                    .Label($"OrderedList count ({fstContext.FileSystem.Count}) should equal unique offset count ({uniqueOffsets.Count})")
                    .And(sorted)
                    .Label("OrderedList entries should be in sorted offset order");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.5, 3.1**
        ///
        /// Property 2 (supplementary): Verifies that subsequent insertions at an existing offset
        /// do not modify the OrderedList — the list count and offsets remain identical before
        /// and after duplicate-offset insertions.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property DuplicateOffset_DoesNotModify_OrderedList()
        {
            Gen<(long[] offsets, int[] dupOffsetIndices, long[] dupSizes, int[] dupFsTypeIndices)> testGen =
                from uniqueCount in Gen.Choose(2, 10)
                from offsets in Gen.ArrayOf(Gen.Choose(1, 50000).Select(o => (long)o * 0x800), uniqueCount)
                from dupCount in Gen.Choose(1, 8)
                from dupOffsetIndices in Gen.ArrayOf(Gen.Choose(0, uniqueCount - 1), dupCount)
                from dupSizes in Gen.ArrayOf(Gen.Choose(1, 10000).Select(s => (long)s), dupCount)
                from dupFsTypeIndices in Gen.ArrayOf(Gen.Choose(0, ValidFsTypes.Length - 1), dupCount)
                select (offsets, dupOffsetIndices, dupSizes, dupFsTypeIndices);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (long[] offsets, int[] dupOffsetIndices, long[] dupSizes, int[] dupFsTypeIndices) = data;

                // Deduplicate offsets to ensure unique initial insertions
                long[] uniqueOffsets = offsets.Distinct().ToArray();
                if (uniqueOffsets.Length < 2)
                    return true.Label("Trivial case: fewer than 2 unique offsets");

                FstContext fstContext = CreateTestFstContext();
                FstFolder rootFolder = new FstFolder(FsType.Iso9660);

                // Insert unique files first
                foreach (long offset in uniqueOffsets)
                {
                    fstContext.AddFile(
                        rootFolder,
                        $"file_{offset:X}",
                        FsType.Iso9660,
                        offset,
                        1000L,
                        FsItemType.File,
                        false,
                        FsBlockEndian.Both,
                        out _,
                        out _);
                }

                // Snapshot the OrderedList state
                int countBefore = fstContext.FileSystem.Count;
                List<long> offsetsBefore = fstContext.FileSystem.Select(f => f.FsOffset).ToList();

                // Now insert duplicates at existing offsets
                for (int i = 0; i < dupOffsetIndices.Length; i++)
                {
                    long dupOffset = uniqueOffsets[dupOffsetIndices[i] % uniqueOffsets.Length];
                    long dupSize = dupSizes[i];
                    FsType dupFsType = ValidFsTypes[dupFsTypeIndices[i]];

                    fstContext.AddFile(
                        rootFolder,
                        $"dup_{dupOffset:X}_{i}",
                        dupFsType,
                        dupOffset,
                        dupSize,
                        FsItemType.File,
                        false,
                        FsBlockEndian.Both,
                        out _,
                        out _);
                }

                // Assert: OrderedList count unchanged
                bool countUnchanged = fstContext.FileSystem.Count == countBefore;

                // Assert: OrderedList offsets unchanged
                List<long> offsetsAfter = fstContext.FileSystem.Select(f => f.FsOffset).ToList();
                bool offsetsUnchanged = offsetsBefore.SequenceEqual(offsetsAfter);

                return countUnchanged
                    .Label($"OrderedList count should remain {countBefore} after duplicate insertions, got {fstContext.FileSystem.Count}")
                    .And(offsetsUnchanged)
                    .Label("OrderedList offsets should remain unchanged after duplicate insertions");
            });
        }
    }
}
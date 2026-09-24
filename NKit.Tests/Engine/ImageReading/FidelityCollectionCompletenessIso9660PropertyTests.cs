using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests verifying fidelity collection completeness for ISO9660 FstContext.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 1: Fidelity collection completeness
    ///
    /// For any sequence of N file declarations (with arbitrary offset collision patterns),
    /// the FidelityFileList.Count SHALL equal the expected count after all insertions complete.
    ///
    /// In ISO9660, same-offset same-size entries are merged as FstLinks (they DON'T create new
    /// fidelity entries). So the expected count is:
    ///   unique offsets + different-size collisions
    /// (NOT same-size collisions since those merge on the existing entry's reference already
    /// present in the fidelity collection from the first insert).
    ///
    /// **Validates: Requirements 1.1, 1.3, 6.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class FidelityCollectionCompletenessIso9660PropertyTests
    {
        /// <summary>
        /// Non-extension filesystem types suitable for file insertion.
        /// </summary>
        private static readonly FsType[] ValidFsTypes = new[]
        {
            FsType.Iso9660,
            FsType.Joliet,
            FsType.RockRidge,
            FsType.Udf,
            FsType.Other
        };

        /// <summary>
        /// Creates a minimal ISO9660 FstContext suitable for testing AddFile behavior.
        /// </summary>
        private static FstContext CreateTestFstContext()
        {
            AreaInfo areaInfo = new AreaInfo(0, AreaType.FileSystem, 0);
            byte[] headerData = new byte[0x8000 * 0x40]; // minimal header buffer
            ImageHeader header = new ImageHeader(headerData, areaInfo);
            return header.FstContext;
        }

        /// <summary>
        /// Computes the expected fidelity count for a sequence of file declarations
        /// following ISO9660 FstContext rules:
        /// - First insert at a unique offset: adds to fidelity (+1)
        /// - Same offset + merge condition met + !largerOverlapsNext: merge (+0)
        /// - Same offset + collision condition: collision (+1)
        ///
        /// Merge conditions (any of): same size, different FsItemType (N/A here, all File),
        /// same name (N/A here, all different), system file (N/A), size==0, f.FsSize==0,
        /// size>f.FsSize (unless largerOverlapsNext), isSameLogicalFile.
        ///
        /// Since all entries use FsItemType.File, different names, non-system, and we only
        /// track FsSize/offset, the relevant conditions are:
        /// - f.FsSize == size → merge
        /// - size == 0 || f.FsSize == 0 → merge
        /// - size > f.FsSize && !largerOverlapsNext → merge
        /// - size > f.FsSize && largerOverlapsNext → collision
        /// - size < f.FsSize && both non-zero → collision
        /// </summary>
        private static int ComputeExpectedFidelityCount(List<(long Offset, long Size, FsType FsType)> declarations)
        {
            int count = 0;
            // Track what's been inserted: offset -> size of first entry at that offset
            Dictionary<long, long> insertedOffsets = new Dictionary<long, long>();
            // Track the OrderedList offsets in sorted order to determine "next offset"
            List<long> sortedOffsets = new List<long>();

            foreach ((long offset, long size, FsType _) in declarations)
            {
                if (!insertedOffsets.ContainsKey(offset))
                {
                    // New unique offset: first insert adds to fidelity
                    insertedOffsets[offset] = size;
                    // Insert in sorted position
                    int insertIdx = sortedOffsets.BinarySearch(offset);
                    if (insertIdx < 0) insertIdx = ~insertIdx;
                    sortedOffsets.Insert(insertIdx, offset);
                    count++;
                }
                else
                {
                    long existingSize = insertedOffsets[offset];

                    // Compute largerOverlapsNext (same logic as production code)
                    // This fires whenever size > existingSize (even when existingSize == 0)
                    bool largerOverlapsNext = false;
                    if (size > existingSize)
                    {
                        int idx = sortedOffsets.BinarySearch(offset);
                        if (idx >= 0 && idx + 1 < sortedOffsets.Count)
                        {
                            long nextOffset = sortedOffsets[idx + 1];
                            largerOverlapsNext = (offset + size) > nextOffset;
                        }
                    }

                    // Check if merge condition is met (any of the OR conditions)
                    bool mergeCondition = existingSize == size || size == 0 || existingSize == 0 || size > existingSize;

                    if (mergeCondition && !largerOverlapsNext)
                    {
                        // Merge: no new fidelity entry
                    }
                    else
                    {
                        // Collision: new fidelity entry
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.3, 6.3**
        ///
        /// Property 1: Fidelity collection completeness.
        /// For any sequence of file declarations with arbitrary offset collision patterns,
        /// the FidelityFileList.Count SHALL equal the expected count after all insertions:
        ///   expected = unique_offsets + different_size_collisions
        /// (same-size collisions merge as FstLinks and don't add new fidelity entries).
        /// </summary>
        [Property(MaxTest = 200)]
        public Property FidelityCount_Equals_UniqueOffsets_Plus_DifferentSizeCollisions()
        {
            // Generate a list of file declarations with controlled collision patterns.
            // Use a small offset pool to force collisions.
            Gen<List<(long o, long s, FsType)>> declarationGen =
                from count in Gen.Choose(1, 30)
                from poolSize in Gen.Choose(1, Math.Max(1, count / 2))
                from offsetPool in Gen.Choose(1, 100).ListOf(poolSize)
                    .Select(l => l.Distinct().Select(o => (long)o * 0x800).ToList())
                    .Where(l => l.Count > 0)
                from offsets in Gen.Elements(offsetPool.ToArray()).ListOf(count)
                from sizes in Gen.OneOf(
                    Gen.Constant(0L),
                    Gen.Choose(1, 50).Select(s => (long)s * 0x800)
                ).ListOf(count)
                from fsTypeIndices in Gen.Choose(0, ValidFsTypes.Length - 1).ListOf(count)
                select offsets.Zip(sizes, (o, s) => (o, s))
                    .Zip(fsTypeIndices, (pair, fti) => (pair.o, pair.s, ValidFsTypes[fti]))
                    .ToList();

            return Prop.ForAll(declarationGen.ToArbitrary(), declarations =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                foreach ((long offset, long size, FsType fsType) in declarations)
                {
                    ctx.AddFile(root, $"file_{offset:X}_{size}_{fsType}",
                        fsType, offset, size, FsItemType.File);
                }

                int expectedCount = ComputeExpectedFidelityCount(declarations);
                int actualCount = ctx.FidelityFiles.Count;

                return (actualCount == expectedCount)
                    .Label($"Expected fidelity count {expectedCount} but got {actualCount} " +
                           $"for {declarations.Count} declarations");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.3, 6.3**
        ///
        /// Property 1 (supplementary): When all offsets are unique (no collisions),
        /// the fidelity count equals the total number of declarations.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AllUniqueOffsets_FidelityCount_EqualsDeclarationCount()
        {
            // Generate declarations with strictly unique offsets (each file at a distinct offset)
            Gen<List<(long Offset, long Size, FsType FsType)>> uniqueOffsetsGen =
                from count in Gen.Choose(1, 50)
                from sizes in Gen.Choose(0, 100).Select(s => (long)s * 0x800).ListOf(count)
                from fsTypeIndices in Gen.Choose(0, ValidFsTypes.Length - 1).ListOf(count)
                select Enumerable.Range(0, count)
                    .Select(i => (Offset: (long)(i + 1) * 0x800, Size: sizes[i], FsType: ValidFsTypes[fsTypeIndices[i]]))
                    .ToList();

            return Prop.ForAll(uniqueOffsetsGen.ToArbitrary(), declarations =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                foreach ((long offset, long size, FsType fsType) in declarations)
                {
                    ctx.AddFile(root, $"file_{offset:X}_{size}",
                        fsType, offset, size, FsItemType.File);
                }

                int actualCount = ctx.FidelityFiles.Count;

                return (actualCount == declarations.Count)
                    .Label($"Expected fidelity count {declarations.Count} (all unique offsets) " +
                           $"but got {actualCount}");
            });
        }
    }
}
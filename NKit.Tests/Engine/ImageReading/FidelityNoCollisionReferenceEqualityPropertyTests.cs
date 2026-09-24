using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests verifying no-collision reference equality between
    /// the fidelity collection and the OrderedList.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 11: No-collision reference equality
    ///
    /// For any set of file declarations where all offsets are unique, every entry in the
    /// fidelity collection SHALL be reference-equal to the corresponding entry in the OrderedList.
    ///
    /// **Validates: Requirements 6.1, 6.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class FidelityNoCollisionReferenceEqualityPropertyTests
    {
        /// <summary>
        /// Creates a minimal ISO9660 FstContext suitable for testing AddFile behavior.
        /// </summary>
        private static FstContext CreateTestFstContext()
        {
            AreaInfo areaInfo = new AreaInfo(0, AreaType.FileSystem, 0);
            ImageHeader header = new ImageHeader(new byte[0x8800], areaInfo);
            return header.FstContext;
        }

        /// <summary>
        /// Feature: filesystem-fidelity-preservation, Property 11: No-collision reference equality
        ///
        /// For any set of file declarations where all offsets are unique, every entry in the
        /// fidelity collection SHALL be reference-equal to the corresponding entry in the OrderedList.
        ///
        /// **Validates: Requirements 6.1, 6.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property UniqueOffsets_FidelityEntries_AreReferenceEqual_ToOrderedListEntries()
        {
            // Generate a list of 1..20 unique offsets (positive, sector-aligned values)
            Gen<List<long>> uniqueOffsetsGen = Gen.Choose(1, 10000)
                .ListOf(20)
                .Select(offsets => offsets.Distinct().Select(o => (long)o * 0x800).ToList())
                .Where(offsets => offsets.Count > 0);

            return Prop.ForAll(uniqueOffsetsGen.ToArbitrary(), offsets =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder rootFolder = new FstFolder("root", FsType.Iso9660, null);

                // Add files with unique offsets
                for (int i = 0; i < offsets.Count; i++)
                {
                    ctx.AddFile(rootFolder, $"file{i}.bin", FsType.Iso9660,
                        offsets[i], (i + 1) * 1024L, FsItemType.File, false, FsBlockEndian.Both,
                        out _, out _);
                }

                // Verify count equality
                bool countEqual = ctx.FidelityFiles.Count == ctx.FileSystem.Count;
                if (!countEqual)
                    return false.ToProperty().Label(
                        $"Count mismatch: FidelityFiles={ctx.FidelityFiles.Count}, FileSystem={ctx.FileSystem.Count}");

                // Verify reference equality: for each fidelity entry, find the matching
                // OrderedList entry by offset and assert ReferenceEquals
                bool allReferenceEqual = true;
                string failureDetail = "";

                foreach (IFsFile fidelityEntry in ctx.FidelityFiles.Entries)
                {
                    // Find the corresponding entry in the OrderedList by offset
                    int idx = ctx.FileSystem.KeyIndex(fidelityEntry.FsOffset, out bool found);
                    if (!found)
                    {
                        allReferenceEqual = false;
                        failureDetail = $"Fidelity entry at offset {fidelityEntry.FsOffset} not found in OrderedList";
                        break;
                    }

                    IFsFile orderedListEntry = ctx.FileSystem[idx];
                    if (!ReferenceEquals(fidelityEntry, orderedListEntry))
                    {
                        allReferenceEqual = false;
                        failureDetail = $"Entry at offset {fidelityEntry.FsOffset} is not reference-equal";
                        break;
                    }
                }

                return allReferenceEqual
                    .Label(allReferenceEqual ? "All entries reference-equal" : failureDetail);
            });
        }

        /// <summary>
        /// Feature: filesystem-fidelity-preservation, Property 11: No-collision reference equality
        ///
        /// Supplementary: verifies that with unique offsets, no additional IFsFile objects are
        /// allocated beyond those in the OrderedList (memory overhead requirement 6.4).
        ///
        /// **Validates: Requirements 6.1, 6.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property UniqueOffsets_NoAdditionalAllocations_BeyondOrderedList()
        {
            // Generate a list of 1..30 unique offsets with varying sizes
            Gen<List<(long Offset, long Size)>> fileDataGen = Gen.Choose(1, 50000)
                .ListOf(30)
                .Select(offsets => offsets.Distinct().Select((o, i) => (Offset: (long)o * 0x800, Size: (long)(i + 1) * 512)).ToList())
                .Where(list => list.Count > 0);

            return Prop.ForAll(fileDataGen.ToArbitrary(), fileData =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder rootFolder = new FstFolder("root", FsType.Iso9660, null);

                // Add files with unique offsets
                foreach ((long offset, long size) in fileData)
                {
                    ctx.AddFile(rootFolder, $"f_{offset:X}.dat", FsType.Iso9660,
                        offset, size, FsItemType.File, false, FsBlockEndian.Both,
                        out _, out _);
                }

                // With all unique offsets, the fidelity collection should contain
                // exactly the same object instances as the OrderedList
                HashSet<IFsFile> orderedListSet = new HashSet<IFsFile>(
                    Enumerable.Range(0, ctx.FileSystem.Count).Select(i => ctx.FileSystem[i]),
                    ReferenceEqualityComparer.Instance);

                HashSet<IFsFile> fidelitySet = new HashSet<IFsFile>(
                    ctx.FidelityFiles.Entries,
                    ReferenceEqualityComparer.Instance);

                // Both sets should be identical (same references, same count)
                bool sameCount = orderedListSet.Count == fidelitySet.Count;
                bool sameReferences = orderedListSet.SetEquals(fidelitySet);

                return (sameCount && sameReferences)
                    .Label($"sameCount={sameCount} (OL={orderedListSet.Count}, Fid={fidelitySet.Count}), " +
                           $"sameReferences={sameReferences}");
            });
        }

        /// <summary>
        /// Reference equality comparer for IFsFile instances.
        /// </summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<IFsFile>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public bool Equals(IFsFile x, IFsFile y) => ReferenceEquals(x, y);
            public int GetHashCode(IFsFile obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
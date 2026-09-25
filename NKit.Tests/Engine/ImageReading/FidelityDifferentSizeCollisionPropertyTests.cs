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
    /// Property-based tests verifying that different-size collisions create separate fidelity entries.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 9: Different-size collision creates separate fidelity entry
    ///
    /// For any file insertion at an offset that already exists in the OrderedList where the new file
    /// has a different FsSize than the existing entry, the fidelity collection SHALL contain a separate
    /// IFsFile instance (not reference-equal to the existing entry) for the new file.
    ///
    /// **Validates: Requirements 5.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class FidelityDifferentSizeCollisionPropertyTests
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
            ImageHeader header = new ImageHeader(new byte[0x8800], areaInfo);
            return header.FstContext;
        }

        /// <summary>
        /// Feature: filesystem-fidelity-preservation, Property 9: Different-size collision creates separate fidelity entry
        ///
        /// For any file insertion at an offset that already exists in the OrderedList where the new
        /// file has a SMALLER FsSize than the existing entry (with different name, same FsItemType,
        /// non-system, both non-zero), the fidelity collection SHALL contain a separate
        /// IFsFile instance (not reference-equal to the existing entry) for the new file.
        ///
        /// Note: Larger sizes MERGE (add a link) rather than creating collisions.
        ///
        /// **Validates: Requirements 5.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property DifferentSizeCollision_Creates_SeparateFidelityEntry()
        {
            // Generate pairs: first file at offset O with size S1, second file at same offset O
            // with SMALLER size S2 (S2 < S1) — only smaller-size entries create collisions
            Gen<(long Offset, long Size1, long Size2)> collisionPairGen =
                from offset in Gen.Choose(1, 10000).Select(o => (long)o * 0x800)
                from size1 in Gen.Choose(2, 5000).Select(s => (long)s * 0x800)
                from size2 in Gen.Choose(1, 5000).Select(s => (long)s * 0x800).Where(s2 => s2 < size1)
                select (Offset: offset, Size1: size1, Size2: size2);

            return Prop.ForAll(collisionPairGen.ToArbitrary(), pair =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert first file at offset O with size S1
                FstFile firstFile = ctx.AddFile(root, "first.bin", FsType.Iso9660,
                    pair.Offset, pair.Size1, FsItemType.File);

                // Insert second file at same offset O with SMALLER size S2
                // (different name, same FsItemType.File, non-system, both non-zero → COLLISION)
                ctx.AddFile(root, "second.bin", FsType.Iso9660,
                    pair.Offset, pair.Size2, FsItemType.File);

                // Verify: the fidelity collection contains 2 entries
                bool hasTwoEntries = ctx.FidelityFiles.Count == 2;
                if (!hasTwoEntries)
                    return false.ToProperty().Label(
                        $"Expected 2 fidelity entries but got {ctx.FidelityFiles.Count} " +
                        $"(offset={pair.Offset}, size1={pair.Size1}, size2={pair.Size2})");

                // Verify: the second entry (collision) is NOT reference-equal to the first entry
                IFsFile fidelityFirst = ctx.FidelityFiles.Entries[0];
                IFsFile fidelitySecond = ctx.FidelityFiles.Entries[1];

                bool notReferenceEqual = !ReferenceEquals(fidelityFirst, fidelitySecond);
                if (!notReferenceEqual)
                    return false.ToProperty().Label(
                        $"Collision entry should NOT be reference-equal to the first entry " +
                        $"(offset={pair.Offset}, size1={pair.Size1}, size2={pair.Size2})");

                // Verify: the first fidelity entry is reference-equal to the OrderedList entry
                int idx = ctx.FileSystem.KeyIndex(pair.Offset, out bool found);
                bool firstIsOrderedListEntry = found && ReferenceEquals(fidelityFirst, ctx.FileSystem[idx]);
                if (!firstIsOrderedListEntry)
                    return false.ToProperty().Label(
                        $"First fidelity entry should be reference-equal to the OrderedList entry");

                // Verify: the collision entry has the correct offset and size (O, S2)
                bool correctOffset = fidelitySecond.FsOffset == pair.Offset;
                bool correctSize = fidelitySecond.FsSize == pair.Size2;

                return (correctOffset && correctSize)
                    .Label($"Collision entry: expected offset={pair.Offset}, size={pair.Size2} " +
                           $"but got offset={fidelitySecond.FsOffset}, size={fidelitySecond.FsSize}");
            });
        }

        /// <summary>
        /// Feature: filesystem-fidelity-preservation, Property 9: Different-size collision creates separate fidelity entry
        ///
        /// Supplementary: verifies that multiple SMALLER-size insertions at the same offset
        /// each produce their own separate fidelity entry, all distinct from the OrderedList entry.
        /// Larger-size entries MERGE (add a link) and do not create new fidelity entries.
        ///
        /// **Validates: Requirements 5.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property MultipleDifferentSizeCollisions_EachCreate_SeparateFidelityEntries()
        {
            // Generate one offset with the first (largest) size, then multiple smaller sizes
            var multiCollisionGen =
                from offset in Gen.Choose(1, 10000).Select(o => (long)o * 0x800)
                from collisionCount in Gen.Choose(2, 5)
                from firstSize in Gen.Choose(collisionCount + 1, 10000).Select(s => (long)s * 0x800)
                from smallerSizes in Gen.Choose(1, (int)(firstSize / 0x800) - 1)
                    .Select(s => (long)s * 0x800)
                    .ListOf(collisionCount)
                    .Select(list => list.Distinct().ToList())
                    .Where(list => list.Count >= 2)
                select new { Offset = offset, FirstSize = firstSize, SmallerSizes = smallerSizes };

            return Prop.ForAll(multiCollisionGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert first file with the largest size
                ctx.AddFile(root, "file0.bin", FsType.Iso9660,
                    data.Offset, data.FirstSize, FsItemType.File);

                // Insert smaller files — each should create a collision
                for (int i = 0; i < data.SmallerSizes.Count; i++)
                {
                    ctx.AddFile(root, $"file{i + 1}.bin", FsType.Iso9660,
                        data.Offset, data.SmallerSizes[i], FsItemType.File);
                }

                // Expected fidelity count: 1 (first insert) + N smaller-size collisions
                int expectedCount = 1 + data.SmallerSizes.Count;
                bool correctCount = ctx.FidelityFiles.Count == expectedCount;
                if (!correctCount)
                    return false.ToProperty().Label(
                        $"Expected {expectedCount} fidelity entries but got {ctx.FidelityFiles.Count} " +
                        $"for 1 original + {data.SmallerSizes.Count} smaller collisions at offset {data.Offset}");

                // Verify: all fidelity entries are distinct object instances (no two are reference-equal)
                List<IFsFile> entries = ctx.FidelityFiles.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    for (int j = i + 1; j < entries.Count; j++)
                    {
                        if (ReferenceEquals(entries[i], entries[j]))
                            return false.ToProperty().Label(
                                $"Fidelity entries [{i}] and [{j}] are reference-equal but should be distinct");
                    }
                }

                // Verify: the OrderedList entry is the first one inserted (first size)
                int idx = ctx.FileSystem.KeyIndex(data.Offset, out bool found);
                if (!found)
                    return false.ToProperty().Label("Offset not found in OrderedList");

                IFsFile orderedListEntry = ctx.FileSystem[idx];
                bool firstEntryIsOL = ReferenceEquals(entries[0], orderedListEntry);

                // Verify: first entry has the first-registered size
                bool firstSizeCorrect = entries[0].FsSize == data.FirstSize;

                // Verify: collision entries have correct smaller sizes
                bool smallerSizesCorrect = true;
                for (int i = 0; i < data.SmallerSizes.Count; i++)
                {
                    if (entries[i + 1].FsOffset != data.Offset || entries[i + 1].FsSize != data.SmallerSizes[i])
                    {
                        smallerSizesCorrect = false;
                        break;
                    }
                }

                return (firstEntryIsOL && firstSizeCorrect && smallerSizesCorrect)
                    .Label($"firstEntryIsOL={firstEntryIsOL}, firstSizeCorrect={firstSizeCorrect}, smallerSizesCorrect={smallerSizesCorrect}");
            });
        }
    }
}
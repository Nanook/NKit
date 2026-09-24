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
    /// Property-based tests verifying that same-size collisions merge as FstLink in ISO9660.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 10: Same-size collision merges as FstLink
    ///
    /// For any ISO9660 file insertion at an offset that already exists in the OrderedList where the
    /// new file has the same FsSize and a different FsType, the existing FstFile.Links collection
    /// SHALL grow by one, and no new FstFile instance SHALL be added to the fidelity collection.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class FidelitySameSizeCollisionMergesAsFstLinkPropertyTests
    {
        /// <summary>
        /// Non-extension filesystem types suitable for file insertion.
        /// These must be distinct to trigger the FstLink merge path.
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
        /// Feature: filesystem-fidelity-preservation, Property 10: Same-size collision merges as FstLink
        ///
        /// For any ISO9660 file insertion at an offset that already exists in the OrderedList where
        /// the new file has the same FsSize and a different FsType, the existing FstFile.Links
        /// collection SHALL grow by one, and no new FstFile instance SHALL be added to the fidelity
        /// collection.
        ///
        /// **Validates: Requirements 5.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SameSizeCollision_MergesAsFstLink_NoNewFidelityEntry()
        {
            // Generate pairs: first file at offset O with size S and FsType T1,
            // second file at same offset O with same size S but different FsType T2
            Gen<(long Offset, long Size, FsType FsType1, FsType FsType2)> collisionPairGen =
                from offset in Gen.Choose(1, 10000).Select(o => (long)o * 0x800)
                from size in Gen.Choose(1, 5000).Select(s => (long)s * 0x800)
                from fsType1Idx in Gen.Choose(0, ValidFsTypes.Length - 1)
                from fsType2Idx in Gen.Choose(0, ValidFsTypes.Length - 1)
                    .Where(idx => idx != fsType1Idx)
                select (Offset: offset, Size: size,
                        FsType1: ValidFsTypes[fsType1Idx], FsType2: ValidFsTypes[fsType2Idx]);

            return Prop.ForAll(collisionPairGen.ToArbitrary(), pair =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert first file at offset O with size S and FsType T1
                FstFile firstFile = ctx.AddFile(root, "first.bin", pair.FsType1,
                    pair.Offset, pair.Size, FsItemType.File);

                // Record the Links count after first insertion
                int linksCountAfterFirst = firstFile.Links.Count;

                // Insert second file at same offset O with same size S but different FsType T2
                ctx.AddFile(root, "second.bin", pair.FsType2,
                    pair.Offset, pair.Size, FsItemType.File);

                // Verify: the fidelity collection count remains 1 (no new entry added)
                bool fidelityCountIs1 = ctx.FidelityFiles.Count == 1;
                if (!fidelityCountIs1)
                    return false.ToProperty().Label(
                        $"Expected fidelity count 1 but got {ctx.FidelityFiles.Count} " +
                        $"(offset={pair.Offset}, size={pair.Size}, " +
                        $"fsType1={pair.FsType1}, fsType2={pair.FsType2})");

                // Verify: the existing FstFile.Links collection grew by one (now has 2 links)
                int linksCountAfterSecond = firstFile.Links.Count;
                bool linksGrew = linksCountAfterSecond == linksCountAfterFirst + 1;
                if (!linksGrew)
                    return false.ToProperty().Label(
                        $"Expected Links count to grow from {linksCountAfterFirst} to " +
                        $"{linksCountAfterFirst + 1} but got {linksCountAfterSecond} " +
                        $"(offset={pair.Offset}, size={pair.Size}, " +
                        $"fsType1={pair.FsType1}, fsType2={pair.FsType2})");

                // Verify: the new link has FsType T2
                bool hasNewLinkType = firstFile.Links.Any(l => l.FsType == pair.FsType2);
                if (!hasNewLinkType)
                    return false.ToProperty().Label(
                        $"Expected a link with FsType={pair.FsType2} but none found " +
                        $"(offset={pair.Offset}, size={pair.Size})");

                // Verify: the fidelity collection entry is reference-equal to the OrderedList entry
                bool sameReference = ReferenceEquals(ctx.FidelityFiles.Entries[0], firstFile);

                return sameReference
                    .Label($"Fidelity entry should be reference-equal to the OrderedList entry");
            });
        }

        /// <summary>
        /// Feature: filesystem-fidelity-preservation, Property 10: Same-size collision merges as FstLink
        ///
        /// Supplementary: verifies that multiple same-size collisions with distinct FsTypes at the
        /// same offset each add a link to the existing FstFile without creating new fidelity entries.
        ///
        /// **Validates: Requirements 5.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property MultipleSameSizeCollisions_EachMergeAsLink_NoNewFidelityEntries()
        {
            // Generate one offset with same size but multiple distinct FsTypes (2..5 types)
            Gen<(long Offset, long Size, List<FsType> FsTypes)> multiMergeGen =
                from offset in Gen.Choose(1, 10000).Select(o => (long)o * 0x800)
                from size in Gen.Choose(1, 5000).Select(s => (long)s * 0x800)
                from typeCount in Gen.Choose(2, Math.Min(5, ValidFsTypes.Length))
                from typeIndices in Gen.Shuffle(Enumerable.Range(0, ValidFsTypes.Length).ToArray())
                    .Select(arr => arr.Take(typeCount).ToList())
                select (Offset: offset, Size: size,
                        FsTypes: typeIndices.Select(i => ValidFsTypes[i]).ToList());

            return Prop.ForAll(multiMergeGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert all files at the same offset with the same size but different FsTypes
                FstFile firstFile = null;
                for (int i = 0; i < data.FsTypes.Count; i++)
                {
                    FstFile result = ctx.AddFile(root, $"file{i}.bin", data.FsTypes[i],
                        data.Offset, data.Size, FsItemType.File);
                    if (i == 0)
                        firstFile = result;
                }

                // Verify: the fidelity collection count remains 1 (no new entries added)
                bool fidelityCountIs1 = ctx.FidelityFiles.Count == 1;
                if (!fidelityCountIs1)
                    return false.ToProperty().Label(
                        $"Expected fidelity count 1 but got {ctx.FidelityFiles.Count} " +
                        $"for {data.FsTypes.Count} same-size insertions at offset {data.Offset}");

                // Verify: the Links collection has one link per distinct FsType
                int expectedLinkCount = data.FsTypes.Count;
                bool correctLinkCount = firstFile.Links.Count == expectedLinkCount;
                if (!correctLinkCount)
                    return false.ToProperty().Label(
                        $"Expected {expectedLinkCount} links but got {firstFile.Links.Count} " +
                        $"for {data.FsTypes.Count} distinct FsTypes at offset {data.Offset}");

                // Verify: each FsType is represented in the Links collection
                bool allTypesPresent = data.FsTypes.All(
                    ft => firstFile.Links.Any(l => l.FsType == ft));

                return allTypesPresent
                    .Label($"Not all FsTypes found in Links collection. " +
                           $"Expected: [{string.Join(", ", data.FsTypes)}], " +
                           $"Got: [{string.Join(", ", firstFile.Links.Select(l => l.FsType))}]");
            });
        }
    }
}
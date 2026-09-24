using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests verifying shared-offset independent output entries.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 7: Shared-offset independent output entries
    ///
    /// For any set of files sharing the same FsOffset, the FsYaml output SHALL contain one entry
    /// per file, each with its own distinct full path and the shared offset value.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class SharedOffsetIndependentOutputPropertyTests
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
        /// **Validates: Requirements 2.3**
        ///
        /// Property 7: Shared-offset independent output entries.
        ///
        /// For any group of files sharing the same FsOffset where the incoming files have
        /// SMALLER sizes than the first-registered entry (with different names, same FsItemType.File),
        /// the fidelity collection SHALL contain one entry per collision, each with its own distinct
        /// full path and the shared offset value.
        ///
        /// Note: Larger-size entries MERGE (add a link) and don't create separate fidelity entries.
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SharedOffset_DifferentSizes_ProduceIndependentEntries_WithDistinctPaths()
        {
            // Generate groups of files sharing the same offset.
            // First file is the largest; subsequent files are smaller to ensure collisions.
            var sharedOffsetGroupGen =
                from sharedOffset in Gen.Choose(1, 10000).Select(o => (long)o * 0x800)
                from groupSize in Gen.Choose(2, 6)
                from firstSize in Gen.Choose(groupSize, 500).Select(s => (long)s * 0x800)
                from smallerSizes in Gen.Choose(1, (int)(firstSize / 0x800) - 1)
                    .Select(s => (long)s * 0x800)
                    .ListOf(groupSize - 1)
                    .Select(list => list.Distinct().ToList())
                    .Where(list => list.Count >= 1)
                select new
                {
                    Offset = sharedOffset,
                    FirstSize = firstSize,
                    SmallerSizes = smallerSizes
                };

            return Prop.ForAll(sharedOffsetGroupGen.ToArbitrary(), group =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Create distinct folders for each file to ensure distinct full paths
                FstFolder folder0 = new FstFolder("dir0", FsType.Iso9660, root);
                ctx.AddFile(folder0, "file0.bin", FsType.Iso9660,
                    group.Offset, group.FirstSize, FsItemType.File);

                for (int i = 0; i < group.SmallerSizes.Count; i++)
                {
                    FstFolder folder = new FstFolder($"dir{i + 1}", FsType.Iso9660, root);
                    ctx.AddFile(folder, $"file{i + 1}.bin", FsType.Iso9660,
                        group.Offset, group.SmallerSizes[i], FsItemType.File);
                }

                // Expected fidelity count = 1 (first insert) + N smaller-size collisions
                int expectedCount = 1 + group.SmallerSizes.Count;
                bool countCorrect = ctx.FidelityFiles.Count == expectedCount;

                if (!countCorrect)
                    return false.ToProperty().Label(
                        $"Expected {expectedCount} fidelity entries but got {ctx.FidelityFiles.Count}");

                // Verify each entry has the shared offset value
                bool allShareOffset = ctx.FidelityFiles.Entries
                    .All(e => e.FsOffset == group.Offset);

                if (!allShareOffset)
                {
                    List<long> offsets = ctx.FidelityFiles.Entries.Select(e => e.FsOffset).ToList();
                    return false.ToProperty().Label(
                        $"Not all entries share offset {group.Offset}: [{string.Join(", ", offsets)}]");
                }

                // Verify each entry has a distinct full path
                List<string> fullNames = ctx.FidelityFiles.Entries
                    .Select(e => e.FullName)
                    .ToList();

                bool allDistinctPaths = fullNames.Distinct().Count() == fullNames.Count;

                if (!allDistinctPaths)
                    return false.ToProperty().Label(
                        $"Full paths are not all distinct: [{string.Join(", ", fullNames)}]");

                // Verify no full path is empty
                bool allNonEmpty = fullNames.All(fn => !string.IsNullOrEmpty(fn));

                return allNonEmpty
                    .Label($"All {expectedCount} entries have shared offset {group.Offset} " +
                           $"and distinct non-empty paths");
            });
        }

        /// <summary>
        /// **Validates: Requirements 2.3**
        ///
        /// Property 7 (supplementary): When multiple groups of files each share a different offset,
        /// the fidelity collection SHALL contain independent entries for each group, with each
        /// entry preserving its own path and its group's shared offset value.
        ///
        /// Only entries with sizes SMALLER than the first-registered size at each offset create
        /// collisions. Larger sizes merge as links.
        /// </summary>
        [Property(MaxTest = 100)]
        public Property MultipleSharedOffsetGroups_AllEntries_HaveCorrectOffsetAndDistinctPaths()
        {
            // Generate multiple groups, each sharing a distinct offset.
            // Within each group, the first entry is largest and subsequent entries are smaller.
            var multiGroupGen =
                from groupCount in Gen.Choose(2, 4)
                from offsets in Gen.Choose(1, 50000)
                    .Select(o => (long)o * 0x800)
                    .ListOf(groupCount)
                    .Select(list => list.Distinct().ToList())
                    .Where(list => list.Count >= 2)
                from groupSizes in Gen.Choose(2, 4).ListOf(offsets.Count)
                select offsets.Zip(groupSizes, (offset, size) => new
                {
                    Offset = offset,
                    FileCount = size
                }).ToList();

            return Prop.ForAll(multiGroupGen.ToArbitrary(), groups =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                int totalExpected = 0;
                Dictionary<long, int> expectedOffsetCounts = new Dictionary<long, int>();

                int folderIndex = 0;
                foreach (var group in groups)
                {
                    // First file at this offset: largest size (uses FileCount * 0x2000 as the largest)
                    long firstSize = (long)(group.FileCount + 1) * 0x2000;
                    FstFolder firstFolder = new FstFolder($"g{group.Offset:X}_d0", FsType.Iso9660, root);
                    ctx.AddFile(firstFolder, $"f{folderIndex}.dat", FsType.Iso9660,
                        group.Offset, firstSize, FsItemType.File);
                    folderIndex++;

                    // Subsequent files: decreasing sizes smaller than firstSize → collisions
                    for (int i = 1; i < group.FileCount; i++)
                    {
                        FstFolder folder = new FstFolder($"g{group.Offset:X}_d{i}", FsType.Iso9660, root);
                        long size = (long)i * 0x1000; // guaranteed smaller than firstSize
                        ctx.AddFile(folder, $"f{folderIndex}.dat", FsType.Iso9660,
                            group.Offset, size, FsItemType.File);
                        folderIndex++;
                    }

                    totalExpected += group.FileCount; // 1 original + (FileCount-1) collisions
                    expectedOffsetCounts[group.Offset] = group.FileCount;
                }

                // Verify total fidelity count
                bool totalCorrect = ctx.FidelityFiles.Count == totalExpected;
                if (!totalCorrect)
                    return false.ToProperty().Label(
                        $"Expected total {totalExpected} fidelity entries but got {ctx.FidelityFiles.Count}");

                // Verify each entry's offset matches one of the group offsets
                // and the count per offset matches expected
                Dictionary<long, int> actualOffsetCounts = ctx.FidelityFiles.Entries
                    .GroupBy(e => e.FsOffset)
                    .ToDictionary(g => g.Key, g => g.Count());

                bool offsetCountsMatch = expectedOffsetCounts.All(kvp =>
                    actualOffsetCounts.ContainsKey(kvp.Key) &&
                    actualOffsetCounts[kvp.Key] == kvp.Value);

                if (!offsetCountsMatch)
                    return false.ToProperty().Label(
                        $"Offset counts mismatch. Expected: [{string.Join(", ", expectedOffsetCounts.Select(k => $"{k.Key:X}={k.Value}"))}], " +
                        $"Actual: [{string.Join(", ", actualOffsetCounts.Select(k => $"{k.Key:X}={k.Value}"))}]");

                // Verify all full paths are distinct across all groups
                List<string> allPaths = ctx.FidelityFiles.Entries.Select(e => e.FullName).ToList();
                bool allDistinct = allPaths.Distinct().Count() == allPaths.Count;

                return allDistinct
                    .Label($"All {totalExpected} entries across {groups.Count} groups have distinct paths");
            });
        }
    }
}
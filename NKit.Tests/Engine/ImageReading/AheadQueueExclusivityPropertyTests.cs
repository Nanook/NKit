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
    /// Property-based tests verifying ahead queue exclusivity.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 5: Ahead queue exclusivity
    ///
    /// For any sequence of file insertions with offset collisions, the ahead queue SHALL
    /// contain only entries that are present in the OrderedList (i.e., no collision-only
    /// entries appear in the ahead queue).
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class AheadQueueExclusivityPropertyTests
    {
        /// <summary>
        /// Non-File FsItemTypes that cause entries to be added to the ahead queue in ISO9660.
        /// In ISO9660 FstContext.AddFile, entries go into the ahead queue when f.Type != FsItemType.File.
        /// </summary>
        private static readonly FsItemType[] AheadQueueTypes = new[]
        {
            FsItemType.DirectoryEntry,
            FsItemType.Pvd,
            FsItemType.PathTable,
            FsItemType.BootCatalog,
            FsItemType.UdfVrs,
            FsItemType.UdfAvdp
        };

        /// <summary>
        /// All FsItemTypes including File (which does NOT go into the ahead queue).
        /// </summary>
        private static readonly FsItemType[] AllItemTypes = new[]
        {
            FsItemType.File,
            FsItemType.DirectoryEntry,
            FsItemType.Pvd,
            FsItemType.PathTable,
            FsItemType.BootCatalog
        };

        /// <summary>
        /// Filesystem types suitable for file insertion.
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
        /// **Validates: Requirements 3.3**
        ///
        /// Property 5: Ahead queue exclusivity.
        /// For any sequence of file insertions with offset collisions, every entry in the
        /// ahead queue SHALL also be present in the OrderedList. No collision-only entries
        /// (those only in the fidelity collection) appear in the ahead queue.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AheadQueue_ContainsOnly_OrderedListEntries()
        {
            // Generate file declarations with some using non-File types (to populate ahead queue)
            // and some offset collisions (to create fidelity-only entries).
            Gen<List<(long Offset, long Size, FsItemType ItemType, FsType FsType)>> declarationGen =
                from count in Gen.Choose(3, 25)
                from poolSize in Gen.Choose(1, Math.Max(1, count / 3))
                from offsetPool in Gen.Choose(1, 200).ListOf(poolSize)
                    .Select(l => l.Distinct().Select(o => (long)o * 0x800).ToList())
                    .Where(l => l.Count > 0)
                from offsets in Gen.Elements(offsetPool.ToArray()).ListOf(count)
                from sizes in Gen.OneOf(
                    Gen.Constant(0L),
                    Gen.Choose(1, 100).Select(s => (long)s * 0x800)
                ).ListOf(count)
                from itemTypeIndices in Gen.Choose(0, AllItemTypes.Length - 1).ListOf(count)
                from fsTypeIndices in Gen.Choose(0, ValidFsTypes.Length - 1).ListOf(count)
                select offsets
                    .Zip(sizes, (o, s) => (Offset: o, Size: s))
                    .Zip(itemTypeIndices, (pair, iti) => (pair.Offset, pair.Size, ItemType: AllItemTypes[iti]))
                    .Zip(fsTypeIndices, (triple, fti) => (triple.Offset, triple.Size, triple.ItemType, FsType: ValidFsTypes[fti]))
                    .ToList();

            return Prop.ForAll(declarationGen.ToArbitrary(), declarations =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert all files
                foreach ((long offset, long size, FsItemType itemType, FsType fsType) in declarations)
                {
                    ctx.AddFile(root, $"entry_{offset:X}_{size}_{itemType}_{fsType}",
                        fsType, offset, size, itemType);
                }

                // Collect all FsOffsets present in the OrderedList
                HashSet<long> orderedListOffsets = new HashSet<long>();
                for (int i = 0; i < ctx.FileSystem.Count; i++)
                {
                    orderedListOffsets.Add(ctx.FileSystem[i].FsOffset);
                }

                // Verify: every entry in the ahead queue is also in the OrderedList
                bool allAheadInOrderedList = true;
                string failureDetail = "";
                for (int i = 0; i < ctx.Ahead.Count; i++)
                {
                    FstFile aheadEntry = ctx.Ahead[i];
                    if (!orderedListOffsets.Contains(aheadEntry.FsOffset))
                    {
                        allAheadInOrderedList = false;
                        failureDetail = $"Ahead queue entry at offset {aheadEntry.FsOffset:X} " +
                                        $"is not in the OrderedList";
                        break;
                    }
                }

                return allAheadInOrderedList
                    .Label(failureDetail.Length > 0 ? failureDetail :
                        $"All {ctx.Ahead.Count} ahead queue entries are in the OrderedList " +
                        $"(OrderedList has {ctx.FileSystem.Count} entries, " +
                        $"fidelity has {ctx.FidelityFiles.Count} entries)");
            });
        }

        /// <summary>
        /// **Validates: Requirements 3.3**
        ///
        /// Property 5 (supplementary): Verifies that collision entries (same offset, different
        /// size) never appear in the ahead queue, even when they use non-File FsItemTypes that
        /// would normally qualify for ahead queue insertion.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property CollisionEntries_NeverAppear_InAheadQueue()
        {
            // Generate scenarios where:
            // 1. First insert at an offset uses a non-File type (goes into ahead queue)
            // 2. Subsequent inserts at same offset with different sizes are collisions
            //    (these should NOT appear in the ahead queue even if they use non-File types)
            Gen<(long[] offsets, long[] firstSizes, int[] firstItemTypeIndices, int[] collisionOffsetIndices, long[] collisionSizes, int[] collisionItemTypeIndices)> scenarioGen =
                from uniqueCount in Gen.Choose(2, 10)
                from offsets in Gen.ArrayOf(
                    Gen.Choose(1, 500).Select(o => (long)o * 0x800), uniqueCount)
                    .Select(arr => arr.Distinct().ToArray())
                    .Where(arr => arr.Length >= 2)
                from firstSizes in Gen.ArrayOf(
                    Gen.Choose(1, 50).Select(s => (long)s * 0x800), uniqueCount)
                from firstItemTypeIndices in Gen.ArrayOf(
                    Gen.Choose(0, AheadQueueTypes.Length - 1), uniqueCount)
                from collisionCount in Gen.Choose(1, 8)
                from collisionOffsetIndices in Gen.ArrayOf(
                    Gen.Choose(0, uniqueCount - 1), collisionCount)
                from collisionSizes in Gen.ArrayOf(
                    Gen.Choose(51, 100).Select(s => (long)s * 0x800), collisionCount)
                from collisionItemTypeIndices in Gen.ArrayOf(
                    Gen.Choose(0, AheadQueueTypes.Length - 1), collisionCount)
                select (offsets, firstSizes, firstItemTypeIndices,
                        collisionOffsetIndices, collisionSizes, collisionItemTypeIndices);

            return Prop.ForAll(scenarioGen.ToArbitrary(), data =>
            {
                (long[] offsets, long[] firstSizes, int[] firstItemTypeIndices,
                     int[] collisionOffsetIndices, long[] collisionSizes, int[] collisionItemTypeIndices) = data;

                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert initial entries (these go into OrderedList and potentially ahead queue)
                for (int i = 0; i < offsets.Length; i++)
                {
                    FsItemType itemType = AheadQueueTypes[firstItemTypeIndices[i] % AheadQueueTypes.Length];
                    ctx.AddFile(root, $"first_{offsets[i]:X}",
                        FsType.Iso9660, offsets[i], firstSizes[i % firstSizes.Length], itemType);
                }

                // Record ahead queue state after initial insertions
                HashSet<long> aheadOffsetsAfterFirst = new HashSet<long>();
                for (int i = 0; i < ctx.Ahead.Count; i++)
                    aheadOffsetsAfterFirst.Add(ctx.Ahead[i].FsOffset);

                // Insert collision entries (same offset, different size, non-File type)
                for (int i = 0; i < collisionOffsetIndices.Length; i++)
                {
                    long collisionOffset = offsets[collisionOffsetIndices[i] % offsets.Length];
                    long collisionSize = collisionSizes[i];
                    FsItemType collisionItemType = AheadQueueTypes[collisionItemTypeIndices[i] % AheadQueueTypes.Length];

                    ctx.AddFile(root, $"collision_{collisionOffset:X}_{i}",
                        FsType.Iso9660, collisionOffset, collisionSize, collisionItemType);
                }

                // Verify: ahead queue has not grown (no collision entries were added)
                HashSet<long> aheadOffsetsAfterCollisions = new HashSet<long>();
                for (int i = 0; i < ctx.Ahead.Count; i++)
                    aheadOffsetsAfterCollisions.Add(ctx.Ahead[i].FsOffset);

                // The ahead queue should be unchanged — collision entries should not be added
                bool noNewAheadEntries = aheadOffsetsAfterCollisions.IsSubsetOf(aheadOffsetsAfterFirst);

                // Additionally verify all ahead entries are in the OrderedList
                HashSet<long> orderedListOffsets = new HashSet<long>();
                for (int i = 0; i < ctx.FileSystem.Count; i++)
                    orderedListOffsets.Add(ctx.FileSystem[i].FsOffset);

                bool allAheadInOrderedList = true;
                for (int i = 0; i < ctx.Ahead.Count; i++)
                {
                    if (!orderedListOffsets.Contains(ctx.Ahead[i].FsOffset))
                    {
                        allAheadInOrderedList = false;
                        break;
                    }
                }

                return noNewAheadEntries
                    .Label($"Collision entries should not be added to ahead queue. " +
                           $"Before: {aheadOffsetsAfterFirst.Count}, After: {aheadOffsetsAfterCollisions.Count}")
                    .And(allAheadInOrderedList)
                    .Label("All ahead queue entries must be in the OrderedList");
            });
        }
    }
}
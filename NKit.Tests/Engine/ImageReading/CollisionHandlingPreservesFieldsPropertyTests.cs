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
    /// Property-based tests verifying collision handling preserves existing entry fields.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 4: Collision handling preserves existing entry fields
    ///
    /// For any file already in the OrderedList at offset O with fields (FsOffset, FsSize, PostGapSize, PostGapFsOffset),
    /// inserting a new file at the same offset O into the system SHALL NOT alter any of those four field values
    /// on the existing entry.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class CollisionHandlingPreservesFieldsPropertyTests
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
        /// Snapshot of the four critical fields on an OrderedList entry.
        /// </summary>
        private record struct FieldSnapshot(long FsOffset, long FsSize, long PostGapSize, long PostGapFsOffset);

        /// <summary>
        /// Takes a snapshot of the four critical fields for every entry in the OrderedList.
        /// </summary>
        private static List<FieldSnapshot> SnapshotFields(FstContext ctx)
        {
            List<FieldSnapshot> snapshots = new List<FieldSnapshot>();
            for (int i = 0; i < ctx.FileSystem.Count; i++)
            {
                IFsFile f = ctx.FileSystem[i];
                snapshots.Add(new FieldSnapshot(f.FsOffset, f.FsSize, f.PostGapSize, f.PostGapFsOffset));
            }
            return snapshots;
        }

        /// <summary>
        /// **Validates: Requirements 3.5**
        ///
        /// Property 4: Collision handling preserves existing entry fields.
        /// For any file already in the OrderedList at offset O with fields (FsOffset, FsSize, PostGapSize, PostGapFsOffset),
        /// inserting a new file at the same offset O (with a different size) SHALL NOT alter any of those four
        /// field values on the existing entry.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property CollisionInsertion_DoesNotAlter_ExistingEntryFields()
        {
            // Generate a set of unique offsets for initial files, then generate collision entries
            // at a subset of those offsets with different sizes.
            Gen<(int[] offsetMultipliers, long[] sizes, int[] collisionTargetIndices, long[] collisionSizes, int[] collisionFsTypeIndices)> testGen =
                from uniqueCount in Gen.Choose(2, 15)
                from offsetMultipliers in Gen.ArrayOf(Gen.Choose(1, 500), uniqueCount)
                from sizes in Gen.ArrayOf(Gen.Choose(1, 200).Select(s => (long)s * 0x800), uniqueCount)
                from collisionCount in Gen.Choose(1, Math.Max(1, uniqueCount))
                from collisionTargetIndices in Gen.ArrayOf(Gen.Choose(0, uniqueCount - 1), collisionCount)
                from collisionSizes in Gen.ArrayOf(Gen.Choose(1, 200).Select(s => (long)s * 0x800), collisionCount)
                from collisionFsTypeIndices in Gen.ArrayOf(Gen.Choose(0, ValidFsTypes.Length - 1), collisionCount)
                select (offsetMultipliers, sizes, collisionTargetIndices, collisionSizes, collisionFsTypeIndices);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (int[] offsetMultipliers, long[] sizes, int[] collisionTargetIndices, long[] collisionSizes, int[] collisionFsTypeIndices) = data;

                // Deduplicate offsets to ensure unique initial insertions
                long[] uniqueOffsets = offsetMultipliers.Select(m => (long)m * 0x800).Distinct().ToArray();
                if (uniqueOffsets.Length < 2)
                    return true.Label("Trivial case: fewer than 2 unique offsets");

                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert initial files with unique offsets
                for (int i = 0; i < uniqueOffsets.Length; i++)
                {
                    long size = sizes[i % sizes.Length];
                    ctx.AddFile(root, $"file_{uniqueOffsets[i]:X}",
                        FsType.Iso9660, uniqueOffsets[i], size, FsItemType.File,
                        false, FsBlockEndian.Both, out _, out _);
                }

                // Snapshot all field values before collision insertions
                List<FieldSnapshot> snapshotBefore = SnapshotFields(ctx);

                // Now insert collision entries at existing offsets with DIFFERENT sizes
                for (int i = 0; i < collisionTargetIndices.Length; i++)
                {
                    int targetIdx = collisionTargetIndices[i] % uniqueOffsets.Length;
                    long targetOffset = uniqueOffsets[targetIdx];
                    long collisionSize = collisionSizes[i];
                    FsType collisionFsType = ValidFsTypes[collisionFsTypeIndices[i]];

                    // Find the existing entry's size at this offset to ensure we use a different size
                    IFsFile existingEntry = ctx.FileSystem.Cast<IFsFile>().FirstOrDefault(f => f.FsOffset == targetOffset);
                    if (existingEntry != null && existingEntry.FsSize == collisionSize)
                    {
                        // Adjust collision size to be different from existing
                        collisionSize = collisionSize + 0x800;
                    }

                    ctx.AddFile(root, $"collision_{targetOffset:X}_{i}",
                        collisionFsType, targetOffset, collisionSize, FsItemType.File,
                        false, FsBlockEndian.Both, out _, out _);
                }

                // Snapshot all field values after collision insertions
                List<FieldSnapshot> snapshotAfter = SnapshotFields(ctx);

                // Verify: OrderedList count should not have changed
                bool countUnchanged = snapshotBefore.Count == snapshotAfter.Count;

                // Verify: all four fields on every existing entry remain unchanged
                bool fieldsPreserved = true;
                string failureDetail = "";
                for (int i = 0; i < snapshotBefore.Count && i < snapshotAfter.Count; i++)
                {
                    FieldSnapshot before = snapshotBefore[i];
                    FieldSnapshot after = snapshotAfter[i];

                    if (before.FsOffset != after.FsOffset)
                    {
                        fieldsPreserved = false;
                        failureDetail = $"Entry[{i}] FsOffset changed from {before.FsOffset:X} to {after.FsOffset:X}";
                        break;
                    }
                    if (before.FsSize != after.FsSize)
                    {
                        fieldsPreserved = false;
                        failureDetail = $"Entry[{i}] FsSize changed from {before.FsSize:X} to {after.FsSize:X}";
                        break;
                    }
                    if (before.PostGapSize != after.PostGapSize)
                    {
                        fieldsPreserved = false;
                        failureDetail = $"Entry[{i}] PostGapSize changed from {before.PostGapSize:X} to {after.PostGapSize:X}";
                        break;
                    }
                    if (before.PostGapFsOffset != after.PostGapFsOffset)
                    {
                        fieldsPreserved = false;
                        failureDetail = $"Entry[{i}] PostGapFsOffset changed from {before.PostGapFsOffset:X} to {after.PostGapFsOffset:X}";
                        break;
                    }
                }

                return countUnchanged
                    .Label($"OrderedList count should remain {snapshotBefore.Count} after collisions, got {snapshotAfter.Count}")
                    .And(fieldsPreserved)
                    .Label($"All four fields should be preserved after collision insertions. {failureDetail}");
            });
        }

        /// <summary>
        /// **Validates: Requirements 3.5**
        ///
        /// Property 4 (supplementary): Same-size collision (FstLink merge) also preserves
        /// all four fields on the existing entry.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property SameSizeCollision_DoesNotAlter_ExistingEntryFields()
        {
            // Generate unique-offset files, then insert same-offset same-size entries
            // (which trigger FstLink merge) and verify fields are unchanged.
            Gen<(int[] offsetMultipliers, long[] sizes, int[] mergeTargetIndices, int[] mergeFsTypeIndices)> testGen =
                from uniqueCount in Gen.Choose(2, 15)
                from offsetMultipliers in Gen.ArrayOf(Gen.Choose(1, 500), uniqueCount)
                from sizes in Gen.ArrayOf(Gen.Choose(1, 200).Select(s => (long)s * 0x800), uniqueCount)
                from mergeCount in Gen.Choose(1, Math.Max(1, uniqueCount))
                from mergeTargetIndices in Gen.ArrayOf(Gen.Choose(0, uniqueCount - 1), mergeCount)
                from mergeFsTypeIndices in Gen.ArrayOf(Gen.Choose(0, ValidFsTypes.Length - 1), mergeCount)
                select (offsetMultipliers, sizes, mergeTargetIndices, mergeFsTypeIndices);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (int[] offsetMultipliers, long[] sizes, int[] mergeTargetIndices, int[] mergeFsTypeIndices) = data;

                // Deduplicate offsets
                long[] uniqueOffsets = offsetMultipliers.Select(m => (long)m * 0x800).Distinct().ToArray();
                if (uniqueOffsets.Length < 2)
                    return true.Label("Trivial case: fewer than 2 unique offsets");

                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert initial files with unique offsets
                Dictionary<long, long> insertedSizes = new Dictionary<long, long>();
                for (int i = 0; i < uniqueOffsets.Length; i++)
                {
                    long size = sizes[i % sizes.Length];
                    ctx.AddFile(root, $"file_{uniqueOffsets[i]:X}",
                        FsType.Iso9660, uniqueOffsets[i], size, FsItemType.File,
                        false, FsBlockEndian.Both, out _, out _);
                    insertedSizes[uniqueOffsets[i]] = size;
                }

                // Snapshot all field values before merge insertions
                List<FieldSnapshot> snapshotBefore = SnapshotFields(ctx);

                // Insert same-offset same-size entries (triggers FstLink merge)
                for (int i = 0; i < mergeTargetIndices.Length; i++)
                {
                    int targetIdx = mergeTargetIndices[i] % uniqueOffsets.Length;
                    long targetOffset = uniqueOffsets[targetIdx];
                    long sameSize = insertedSizes[targetOffset]; // same size as existing
                    FsType mergeFsType = ValidFsTypes[mergeFsTypeIndices[i]];

                    ctx.AddFile(root, $"merge_{targetOffset:X}_{i}",
                        mergeFsType, targetOffset, sameSize, FsItemType.File,
                        false, FsBlockEndian.Both, out _, out _);
                }

                // Snapshot all field values after merge insertions
                List<FieldSnapshot> snapshotAfter = SnapshotFields(ctx);

                // Verify: all four fields on every existing entry remain unchanged
                bool fieldsPreserved = true;
                string failureDetail = "";
                for (int i = 0; i < snapshotBefore.Count && i < snapshotAfter.Count; i++)
                {
                    FieldSnapshot before = snapshotBefore[i];
                    FieldSnapshot after = snapshotAfter[i];

                    if (before.FsOffset != after.FsOffset ||
                        before.FsSize != after.FsSize ||
                        before.PostGapSize != after.PostGapSize ||
                        before.PostGapFsOffset != after.PostGapFsOffset)
                    {
                        fieldsPreserved = false;
                        failureDetail = $"Entry[{i}] fields changed: " +
                            $"FsOffset {before.FsOffset:X}->{after.FsOffset:X}, " +
                            $"FsSize {before.FsSize:X}->{after.FsSize:X}, " +
                            $"PostGapSize {before.PostGapSize:X}->{after.PostGapSize:X}, " +
                            $"PostGapFsOffset {before.PostGapFsOffset:X}->{after.PostGapFsOffset:X}";
                        break;
                    }
                }

                return (snapshotBefore.Count == snapshotAfter.Count)
                    .Label($"OrderedList count should remain {snapshotBefore.Count} after merges, got {snapshotAfter.Count}")
                    .And(fieldsPreserved)
                    .Label($"All four fields should be preserved after same-size collision (FstLink merge). {failureDetail}");
            });
        }
    }
}
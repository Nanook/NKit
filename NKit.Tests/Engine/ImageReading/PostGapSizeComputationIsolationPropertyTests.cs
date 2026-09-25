using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests verifying PostGapSize computation isolation.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 3: PostGapSize computation isolation
    ///
    /// For any OrderedList containing entries at offsets O₁ &lt; O₂ &lt; ... &lt; Oₙ with sizes
    /// S₁, S₂, ..., Sₙ, the PostGapSize of entry i SHALL equal O(i+1) - (Oᵢ + Sᵢ),
    /// computed exclusively from consecutive OrderedList entries regardless of how many
    /// entries exist in the fidelity collection.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class PostGapSizeComputationIsolationPropertyTests
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
        /// **Validates: Requirements 3.2**
        ///
        /// Property 3: PostGapSize computation isolation.
        /// For any sequence of files with unique offsets (sorted) inserted via FstContext.AddFile,
        /// plus additional collision entries (same offset, different size) that go into fidelity only,
        /// the PostGapSize for each OrderedList entry equals the gap between consecutive entries:
        /// nextOffset - (currentOffset + currentSize).
        /// The collision entries do NOT affect PostGapSize values.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property PostGapSize_ComputedExclusivelyFromConsecutiveOrderedListEntries()
        {
            // Generate a sorted sequence of unique offsets with known sizes,
            // plus some collision entries at existing offsets with different sizes.
            var testDataGen =
                from entryCount in Gen.Choose(2, 20)
                // Generate strictly increasing offsets by using cumulative gaps
                from gaps in Gen.ArrayOf(Gen.Choose(1, 100).Select(g => (long)g * 0x800), entryCount)
                    // Generate sizes that are smaller than the gap to the next entry (to keep things valid)
                from sizes in Gen.ArrayOf(Gen.Choose(1, 50).Select(s => (long)s * 0x800), entryCount)
                    // Generate collision count (0 to entryCount collisions)
                from collisionCount in Gen.Choose(0, Math.Max(1, entryCount - 1))
                    // For each collision, pick which existing offset to collide with
                from collisionTargets in Gen.ArrayOf(Gen.Choose(0, entryCount - 1), collisionCount)
                    // For each collision, generate a different size
                from collisionSizes in Gen.ArrayOf(Gen.Choose(1, 200).Select(s => (long)s * 0x800), collisionCount)
                select new
                {
                    Gaps = gaps,
                    Sizes = sizes,
                    CollisionTargets = collisionTargets,
                    CollisionSizes = collisionSizes
                };

            return Prop.ForAll(testDataGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Build sorted unique offsets from cumulative gaps
                int entryCount = data.Gaps.Length;
                long[] offsets = new long[entryCount];
                offsets[0] = 0x800; // start at sector 1
                for (int i = 1; i < entryCount; i++)
                    offsets[i] = offsets[i - 1] + data.Gaps[i - 1] + data.Sizes[i - 1];

                // Ensure sizes don't exceed gap to next entry (for non-last entries)
                long[] sizes = new long[entryCount];
                for (int i = 0; i < entryCount; i++)
                    sizes[i] = data.Sizes[i];

                // Insert unique-offset files into FstContext (these go into OrderedList + fidelity)
                for (int i = 0; i < entryCount; i++)
                {
                    ctx.AddFile(root, $"file_{i}",
                        FsType.Iso9660, offsets[i], sizes[i], FsItemType.File);
                }

                // Now insert collision entries (same offset, different size → fidelity only)
                for (int c = 0; c < data.CollisionTargets.Length; c++)
                {
                    int targetIdx = data.CollisionTargets[c];
                    long collisionOffset = offsets[targetIdx];
                    long collisionSize = data.CollisionSizes[c];

                    // Ensure collision size differs from the original to trigger the collision path
                    if (collisionSize == sizes[targetIdx])
                        collisionSize = sizes[targetIdx] + 0x800;

                    ctx.AddFile(root, $"collision_{c}",
                        FsType.Joliet, collisionOffset, collisionSize, FsItemType.File);
                }

                // Verify: OrderedList still has exactly entryCount entries (collisions don't add)
                bool orderedListCountCorrect = ctx.FileSystem.Count == entryCount;

                // Verify: fidelity collection has more entries than OrderedList (due to collisions)
                bool fidelityHasCollisions = data.CollisionTargets.Length == 0
                    || ctx.FidelityFiles.Count > entryCount;

                // Verify: PostGapSize for each non-last entry equals nextOffset - (currentOffset + currentSize)
                bool postGapSizeCorrect = true;
                string failureDetail = "";
                for (int i = 0; i < entryCount - 1; i++)
                {
                    FstFile entry = (FstFile)ctx.FileSystem[i];
                    FstFile nextEntry = (FstFile)ctx.FileSystem[i + 1];

                    long expectedPostGapSize = nextEntry.FsOffset - (entry.FsOffset + entry.FsSize);
                    if (entry.PostGapSize != expectedPostGapSize)
                    {
                        postGapSizeCorrect = false;
                        failureDetail = $"Entry[{i}] at offset {entry.FsOffset:X}: " +
                            $"PostGapSize={entry.PostGapSize}, expected={expectedPostGapSize} " +
                            $"(next={nextEntry.FsOffset:X}, current+size={entry.FsOffset + entry.FsSize:X})";
                        break;
                    }
                }

                // Verify: PostGapFsOffset for each entry equals offset + size
                bool postGapFsOffsetCorrect = true;
                string fsOffsetFailure = "";
                for (int i = 0; i < entryCount; i++)
                {
                    FstFile entry = (FstFile)ctx.FileSystem[i];
                    long expectedPostGapFsOffset = entry.FsOffset + entry.FsSize;
                    if (entry.PostGapFsOffset != expectedPostGapFsOffset)
                    {
                        postGapFsOffsetCorrect = false;
                        fsOffsetFailure = $"Entry[{i}] at offset {entry.FsOffset:X}: " +
                            $"PostGapFsOffset={entry.PostGapFsOffset:X}, expected={expectedPostGapFsOffset:X}";
                        break;
                    }
                }

                return orderedListCountCorrect
                    .Label($"OrderedList count should be {entryCount}, got {ctx.FileSystem.Count}")
                    .And(postGapSizeCorrect)
                    .Label($"PostGapSize isolation violated: {failureDetail}")
                    .And(postGapFsOffsetCorrect)
                    .Label($"PostGapFsOffset incorrect: {fsOffsetFailure}");
            });
        }

        /// <summary>
        /// **Validates: Requirements 3.2, 11.1, 11.4**
        ///
        /// Property 3 (supplementary): Verifies that adding collision entries with SMALLER
        /// sizes after initial insertion does not change the PostGapSize values that were
        /// already computed. Smaller collisions go to fidelity only and cannot influence
        /// the disc-mapping pipeline's gap calculations.
        ///
        /// Note: Collisions with LARGER sizes intentionally replace existing entries per
        /// the largest-size-wins rule (Requirement 11.1), so PostGapSize is expected to
        /// change for those replacements.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property SmallerCollisionInsertions_DoNotAlter_PostGapSize()
        {
            var testDataGen =
                from entryCount in Gen.Choose(2, 15)
                from gaps in Gen.ArrayOf(Gen.Choose(1, 80).Select(g => (long)g * 0x800), entryCount)
                from sizes in Gen.ArrayOf(Gen.Choose(2, 40).Select(s => (long)s * 0x800), entryCount)
                from collisionCount in Gen.Choose(1, Math.Max(1, entryCount))
                from collisionTargets in Gen.ArrayOf(Gen.Choose(0, entryCount - 1), collisionCount)
                select new
                {
                    EntryCount = entryCount,
                    Gaps = gaps,
                    Sizes = sizes,
                    CollisionTargets = collisionTargets,
                };

            return Prop.ForAll(testDataGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Build sorted unique offsets
                long[] offsets = new long[data.EntryCount];
                offsets[0] = 0x800;
                for (int i = 1; i < data.EntryCount; i++)
                    offsets[i] = offsets[i - 1] + data.Gaps[i - 1] + data.Sizes[i - 1];

                // Insert unique-offset files
                for (int i = 0; i < data.EntryCount; i++)
                {
                    ctx.AddFile(root, $"file_{i}",
                        FsType.Iso9660, offsets[i], data.Sizes[i], FsItemType.File);
                }

                // Snapshot PostGapSize values before collisions
                long[] postGapSizesBefore = new long[data.EntryCount];
                long[] postGapFsOffsetsBefore = new long[data.EntryCount];
                for (int i = 0; i < data.EntryCount; i++)
                {
                    FstFile entry = (FstFile)ctx.FileSystem[i];
                    postGapSizesBefore[i] = entry.PostGapSize;
                    postGapFsOffsetsBefore[i] = entry.PostGapFsOffset;
                }

                // Insert collision entries with SMALLER sizes (these go to fidelity only)
                for (int c = 0; c < data.CollisionTargets.Length; c++)
                {
                    int targetIdx = data.CollisionTargets[c];
                    long collisionOffset = offsets[targetIdx];
                    // Ensure collision size is strictly smaller than the existing entry
                    long collisionSize = data.Sizes[targetIdx] - 0x800;
                    if (collisionSize <= 0)
                        collisionSize = 0x800; // minimum valid size — still smaller since Sizes min is 2*0x800

                    // Only insert if truly smaller (skip if it would be equal or larger)
                    if (collisionSize >= data.Sizes[targetIdx])
                        continue;

                    ctx.AddFile(root, $"collision_{c}",
                        FsType.Joliet, collisionOffset, collisionSize, FsItemType.File);
                }

                // Verify: PostGapSize values unchanged after smaller collision insertions
                bool postGapSizesUnchanged = true;
                string failureDetail = "";
                for (int i = 0; i < data.EntryCount; i++)
                {
                    FstFile entry = (FstFile)ctx.FileSystem[i];
                    if (entry.PostGapSize != postGapSizesBefore[i])
                    {
                        postGapSizesUnchanged = false;
                        failureDetail = $"Entry[{i}] PostGapSize changed from {postGapSizesBefore[i]} to {entry.PostGapSize} after smaller collisions";
                        break;
                    }
                    if (entry.PostGapFsOffset != postGapFsOffsetsBefore[i])
                    {
                        postGapSizesUnchanged = false;
                        failureDetail = $"Entry[{i}] PostGapFsOffset changed from {postGapFsOffsetsBefore[i]:X} to {entry.PostGapFsOffset:X} after smaller collisions";
                        break;
                    }
                }

                return postGapSizesUnchanged
                    .Label($"PostGapSize/PostGapFsOffset should not change after smaller collision insertions: {failureDetail}");
            });
        }
    }
}
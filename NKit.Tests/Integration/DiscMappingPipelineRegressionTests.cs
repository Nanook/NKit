using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Integration test for disc-mapping pipeline regression.
    ///
    /// Verifies that the disc-mapping pipeline (OrderedList, PostGapSize computation,
    /// ahead queue scheduling) produces identical results regardless of fidelity
    /// collection activity. Uses hardcoded known values for deterministic verification.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class DiscMappingPipelineRegressionTests
    {
        /// <summary>
        /// Creates a minimal ISO9660 FstContext suitable for testing.
        /// </summary>
        private static FstContext CreateTestFstContext()
        {
            AreaInfo areaInfo = new AreaInfo(0, AreaType.FileSystem, 0);
            byte[] headerData = new byte[0x8000 * 0x40]; // minimal header buffer
            ImageHeader header = new ImageHeader(headerData, areaInfo);
            return header.FstContext;
        }

        /// <summary>
        /// Regression test: Insert a known sequence of files (including collisions) and verify
        /// the disc-mapping pipeline output (OrderedList entries, PostGapSize values, ahead queue)
        /// matches a pre-computed baseline.
        ///
        /// The baseline represents the expected behavior of the disc-mapping pipeline:
        /// - Only unique-offset entries appear in the OrderedList
        /// - PostGapSize = nextOffset - (currentOffset + currentSize)
        /// - Ahead queue contains only non-File type entries from the OrderedList
        /// - Collision entries (same offset, different size) do NOT affect any of the above
        ///
        /// **Validates: Requirements 3.1, 3.2, 3.3**
        /// </summary>
        [Fact]
        public void DiscMappingPipeline_WithKnownInputs_ProducesBaselineOutput()
        {
            // Arrange: Define a known sequence of file declarations
            // These represent a realistic ISO9660 filesystem with:
            // - System entries (PVD, path tables) that go into the ahead queue
            // - Regular files at various offsets
            // - Collisions (same offset, different size) that should only go to fidelity
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // === Known file declarations (in insertion order) ===
            // Entry 0: PVD at offset 0x8000, size 0x800 (non-File → ahead queue)
            ctx.AddFile(root, "pvd_entry", FsType.Iso9660, 0x8000, 0x800, FsItemType.Pvd);

            // Entry 1: Path table at offset 0x10000, size 0x1000 (non-File → ahead queue)
            ctx.AddFile(root, "path_table", FsType.Iso9660, 0x10000, 0x1000, FsItemType.PathTable);

            // Entry 2: Directory entry at offset 0x20000, size 0x800 (non-File → ahead queue)
            ctx.AddFile(root, "root_dir", FsType.Iso9660, 0x20000, 0x800, FsItemType.DirectoryEntry);

            // Entry 3: Regular file at offset 0x30000, size 0x5000 (File → NOT in ahead queue)
            ctx.AddFile(root, "game.bin", FsType.Iso9660, 0x30000, 0x5000, FsItemType.File);

            // Entry 4: Regular file at offset 0x40000, size 0x2000 (File → NOT in ahead queue)
            ctx.AddFile(root, "readme.txt", FsType.Iso9660, 0x40000, 0x2000, FsItemType.File);

            // Entry 5: Regular file at offset 0x50000, size 0x8000 (File → NOT in ahead queue)
            ctx.AddFile(root, "data.dat", FsType.Iso9660, 0x50000, 0x8000, FsItemType.File);

            // Entry 6: Boot catalog at offset 0x60000, size 0x800 (non-File → ahead queue)
            ctx.AddFile(root, "boot_catalog", FsType.Iso9660, 0x60000, 0x800, FsItemType.BootCatalog);

            // Entry 7: Regular file at offset 0x70000, size 0x10000 (File → NOT in ahead queue)
            ctx.AddFile(root, "bigfile.iso", FsType.Iso9660, 0x70000, 0x10000, FsItemType.File);

            // === Collision entries (same offset, different size → fidelity only) ===
            // Collision at offset 0x30000 with different size (should NOT affect OrderedList)
            ctx.AddFile(root, "game_alt.bin", FsType.Joliet, 0x30000, 0x3000, FsItemType.File);

            // Collision at offset 0x50000 with different size
            ctx.AddFile(root, "data_alt.dat", FsType.Joliet, 0x50000, 0x4000, FsItemType.File);

            // Collision at offset 0x20000 with different size (non-File type, but still collision)
            ctx.AddFile(root, "dir_alt", FsType.Joliet, 0x20000, 0x1000, FsItemType.DirectoryEntry);

            // === Expected baseline values ===
            // OrderedList should have exactly 8 entries (one per unique offset)
            long[] expectedOrderedListOffsets = new long[]
            {
                0x8000, 0x10000, 0x20000, 0x30000, 0x40000, 0x50000, 0x60000, 0x70000
            };

            long[] expectedOrderedListSizes = new long[]
            {
                0x800, 0x1000, 0x800, 0x5000, 0x2000, 0x8000, 0x800, 0x10000
            };

            // PostGapSize[i] = offset[i+1] - (offset[i] + size[i])
            // Entry 0: 0x10000 - (0x8000 + 0x800) = 0x10000 - 0x8800 = 0x7800
            // Entry 1: 0x20000 - (0x10000 + 0x1000) = 0x20000 - 0x11000 = 0xF000
            // Entry 2: 0x30000 - (0x20000 + 0x800) = 0x30000 - 0x20800 = 0xF800
            // Entry 3: 0x40000 - (0x30000 + 0x5000) = 0x40000 - 0x35000 = 0xB000
            // Entry 4: 0x50000 - (0x40000 + 0x2000) = 0x50000 - 0x42000 = 0xE000
            // Entry 5: 0x60000 - (0x50000 + 0x8000) = 0x60000 - 0x58000 = 0x8000
            // Entry 6: 0x70000 - (0x60000 + 0x800) = 0x70000 - 0x60800 = 0xF800
            // Entry 7: last entry → PostGapSize = PhysicalVolumeSize - (0x70000 + 0x10000)
            //          PhysicalVolumeSize defaults to 0, so = 0 - 0x80000 = -0x80000
            long[] expectedPostGapSizes = new long[]
            {
                0x7800, 0xF000, 0xF800, 0xB000, 0xE000, 0x8000, 0xF800, -0x80000
            };

            // PostGapFsOffset[i] = offset[i] + size[i]
            long[] expectedPostGapFsOffsets = new long[]
            {
                0x8800, 0x11000, 0x20800, 0x35000, 0x42000, 0x58000, 0x60800, 0x80000
            };

            // Ahead queue should contain only non-File entries from the OrderedList:
            // PVD (0x8000), PathTable (0x10000), DirectoryEntry (0x20000), BootCatalog (0x60000)
            long[] expectedAheadQueueOffsets = new long[]
            {
                0x8000, 0x10000, 0x20000, 0x60000
            };

            // Act & Assert

            // 1. OrderedList contains exactly the expected entries (one per unique offset)
            Assert.Equal(expectedOrderedListOffsets.Length, ctx.FileSystem.Count);
            for (int i = 0; i < expectedOrderedListOffsets.Length; i++)
            {
                FstFile entry = (FstFile)ctx.FileSystem[i];
                Assert.Equal(expectedOrderedListOffsets[i], entry.FsOffset);
                Assert.Equal(expectedOrderedListSizes[i], entry.FsSize);
            }

            // 2. PostGapSize for each entry equals the expected gap value
            for (int i = 0; i < expectedPostGapSizes.Length; i++)
            {
                FstFile entry = (FstFile)ctx.FileSystem[i];
                Assert.Equal(expectedPostGapSizes[i], entry.PostGapSize);
                Assert.Equal(expectedPostGapFsOffsets[i], entry.PostGapFsOffset);
            }

            // 3. Ahead queue contains exactly the expected entries (non-File types only, from OrderedList only)
            Assert.Equal(expectedAheadQueueOffsets.Length, ctx.Ahead.Count);
            for (int i = 0; i < expectedAheadQueueOffsets.Length; i++)
            {
                Assert.Equal(expectedAheadQueueOffsets[i], ctx.Ahead[i].FsOffset);
            }

            // 4. No collision entries appear in the OrderedList or ahead queue
            // Verify collision offsets (0x30000, 0x50000, 0x20000) still have original sizes
            FstFile entry30000 = (FstFile)ctx.FileSystem[3]; // offset 0x30000
            Assert.Equal(0x5000, entry30000.FsSize); // original size, not collision size 0x3000

            FstFile entry50000 = (FstFile)ctx.FileSystem[5]; // offset 0x50000
            Assert.Equal(0x8000, entry50000.FsSize); // original size, not collision size 0x4000

            FstFile entry20000 = (FstFile)ctx.FileSystem[2]; // offset 0x20000
            Assert.Equal(0x800, entry20000.FsSize); // original size, not collision size 0x1000

            // Verify no collision entries in ahead queue (all ahead entries have original sizes)
            foreach (FstFile aheadEntry in Enumerable.Range(0, ctx.Ahead.Count).Select(i => ctx.Ahead[i]))
            {
                // Each ahead entry must be reference-equal to the corresponding OrderedList entry
                int idx = ctx.FileSystem.KeyIndex(aheadEntry.FsOffset, out bool exists);
                Assert.True(exists, $"Ahead entry at {aheadEntry.FsOffset:X} must exist in OrderedList");
                Assert.Same(ctx.FileSystem[idx], aheadEntry);
            }

            // 5. The fidelity collection has MORE entries than the OrderedList (proving collisions were captured separately)
            // 8 original entries + 2 collision entries = 10 total fidelity entries
            // (collision at 0x20000 with size 0x1000 > existing 0x800 → MERGE, not collision)
            // (collision at 0x30000 with size 0x3000 < existing 0x5000 → COLLISION)
            // (collision at 0x50000 with size 0x4000 < existing 0x8000 → COLLISION)
            Assert.Equal(10, ctx.FidelityFiles.Count);
            Assert.True(ctx.FidelityFiles.Count > ctx.FileSystem.Count,
                $"Fidelity collection ({ctx.FidelityFiles.Count}) should have more entries " +
                $"than OrderedList ({ctx.FileSystem.Count}) due to collision entries");
        }

        /// <summary>
        /// Regression test: Verify that inserting collision entries at every position in the
        /// OrderedList does not alter any PostGapSize or PostGapFsOffset values.
        /// This is a stronger regression guarantee using a different insertion pattern.
        ///
        /// **Validates: Requirements 3.1, 3.2, 3.3**
        /// </summary>
        [Fact]
        public void DiscMappingPipeline_CollisionsAtEveryOffset_DoNotAlterGapValues()
        {
            // Arrange: Create a context and insert files at known offsets
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // Insert 5 files at evenly spaced offsets
            long[] offsets = { 0x10000, 0x20000, 0x30000, 0x40000, 0x50000 };
            long[] sizes = { 0x2000, 0x4000, 0x1000, 0x3000, 0x5000 };

            for (int i = 0; i < offsets.Length; i++)
            {
                ctx.AddFile(root, $"file_{i}", FsType.Iso9660, offsets[i], sizes[i], FsItemType.File);
            }

            // Snapshot the baseline PostGapSize and PostGapFsOffset values
            long[] baselinePostGapSizes = new long[offsets.Length];
            long[] baselinePostGapFsOffsets = new long[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                FstFile entry = (FstFile)ctx.FileSystem[i];
                baselinePostGapSizes[i] = entry.PostGapSize;
                baselinePostGapFsOffsets[i] = entry.PostGapFsOffset;
            }

            // Act: Insert collision entries at EVERY offset with different sizes
            for (int i = 0; i < offsets.Length; i++)
            {
                long collisionSize = sizes[i] + 0x800; // different size to trigger collision path
                ctx.AddFile(root, $"collision_{i}", FsType.Joliet, offsets[i], collisionSize, FsItemType.File);
            }

            // Assert: All PostGapSize and PostGapFsOffset values remain unchanged
            for (int i = 0; i < offsets.Length; i++)
            {
                FstFile entry = (FstFile)ctx.FileSystem[i];
                Assert.Equal(baselinePostGapSizes[i], entry.PostGapSize);
                Assert.Equal(baselinePostGapFsOffsets[i], entry.PostGapFsOffset);
            }

            // Assert: OrderedList count unchanged (still 5)
            Assert.Equal(5, ctx.FileSystem.Count);

            // Assert: Fidelity has 5 entries (all collisions used larger sizes → merged as links, no new fidelity entries)
            Assert.Equal(5, ctx.FidelityFiles.Count);
        }

        /// <summary>
        /// Regression test: Verify that the ahead queue is populated correctly and
        /// collision entries with non-File types do NOT get added to the ahead queue.
        ///
        /// **Validates: Requirements 3.3**
        /// </summary>
        [Fact]
        public void DiscMappingPipeline_AheadQueue_ExcludesCollisionEntries()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // Insert entries: mix of File and non-File types
            // Non-File types go into the ahead queue
            ctx.AddFile(root, "pvd", FsType.Iso9660, 0x8000, 0x800, FsItemType.Pvd);
            ctx.AddFile(root, "dir1", FsType.Iso9660, 0x18000, 0x800, FsItemType.DirectoryEntry);
            ctx.AddFile(root, "file1", FsType.Iso9660, 0x28000, 0x4000, FsItemType.File);
            ctx.AddFile(root, "dir2", FsType.Iso9660, 0x38000, 0x800, FsItemType.DirectoryEntry);
            ctx.AddFile(root, "file2", FsType.Iso9660, 0x48000, 0x6000, FsItemType.File);

            // Baseline: ahead queue should have 3 entries (pvd, dir1, dir2)
            Assert.Equal(3, ctx.Ahead.Count);
            Assert.Equal(0x8000, ctx.Ahead[0].FsOffset);
            Assert.Equal(0x18000, ctx.Ahead[1].FsOffset);
            Assert.Equal(0x38000, ctx.Ahead[2].FsOffset);

            // Act: Insert collision entries at non-File offsets with different sizes
            // These use non-File types but should NOT be added to the ahead queue
            ctx.AddFile(root, "pvd_collision", FsType.Joliet, 0x8000, 0x1000, FsItemType.Pvd);
            ctx.AddFile(root, "dir1_collision", FsType.Joliet, 0x18000, 0x1000, FsItemType.DirectoryEntry);
            ctx.AddFile(root, "dir2_collision", FsType.Joliet, 0x38000, 0x1000, FsItemType.DirectoryEntry);

            // Also insert collision at a File offset with a non-File type
            ctx.AddFile(root, "file1_as_dir", FsType.Joliet, 0x28000, 0x2000, FsItemType.DirectoryEntry);

            // Assert: Ahead queue is unchanged (still 3 entries, same offsets)
            Assert.Equal(3, ctx.Ahead.Count);
            Assert.Equal(0x8000, ctx.Ahead[0].FsOffset);
            Assert.Equal(0x18000, ctx.Ahead[1].FsOffset);
            Assert.Equal(0x38000, ctx.Ahead[2].FsOffset);

            // Assert: OrderedList unchanged (still 5 entries)
            Assert.Equal(5, ctx.FileSystem.Count);

            // Assert: Fidelity has captured only original entries (all "collisions" actually merged)
            // - pvd_collision: size 0x1000 > existing 0x800 → MERGE
            // - dir1_collision: size 0x1000 > existing 0x800 → MERGE
            // - dir2_collision: size 0x1000 > existing 0x800 → MERGE
            // - file1_as_dir: different FsItemType (DirectoryEntry vs File) → MERGE (f.Type != type)
            Assert.Equal(5, ctx.FidelityFiles.Count);
        }

        /// <summary>
        /// Regression test: Verify that same-size collisions (FstLink merges) also do not
        /// affect the disc-mapping pipeline state.
        ///
        /// **Validates: Requirements 3.1, 3.2**
        /// </summary>
        [Fact]
        public void DiscMappingPipeline_SameSizeMerges_DoNotAlterPipelineState()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // Insert files at known offsets
            ctx.AddFile(root, "file_a", FsType.Iso9660, 0x10000, 0x3000, FsItemType.File);
            ctx.AddFile(root, "file_b", FsType.Iso9660, 0x20000, 0x5000, FsItemType.File);
            ctx.AddFile(root, "file_c", FsType.Iso9660, 0x30000, 0x2000, FsItemType.File);

            // Snapshot baseline
            int baselineCount = ctx.FileSystem.Count;
            long[] baselinePostGapSizes = new long[baselineCount];
            long[] baselinePostGapFsOffsets = new long[baselineCount];
            long[] baselineFsOffsets = new long[baselineCount];
            long[] baselineFsSizes = new long[baselineCount];
            for (int i = 0; i < baselineCount; i++)
            {
                FstFile entry = (FstFile)ctx.FileSystem[i];
                baselinePostGapSizes[i] = entry.PostGapSize;
                baselinePostGapFsOffsets[i] = entry.PostGapFsOffset;
                baselineFsOffsets[i] = entry.FsOffset;
                baselineFsSizes[i] = entry.FsSize;
            }

            // Act: Insert same-size entries at existing offsets (triggers FstLink merge)
            ctx.AddFile(root, "file_a_joliet", FsType.Joliet, 0x10000, 0x3000, FsItemType.File);
            ctx.AddFile(root, "file_b_joliet", FsType.Joliet, 0x20000, 0x5000, FsItemType.File);
            ctx.AddFile(root, "file_c_rockridge", FsType.RockRidge, 0x30000, 0x2000, FsItemType.File);

            // Assert: OrderedList count unchanged
            Assert.Equal(baselineCount, ctx.FileSystem.Count);

            // Assert: All PostGapSize and PostGapFsOffset values unchanged
            for (int i = 0; i < baselineCount; i++)
            {
                FstFile entry = (FstFile)ctx.FileSystem[i];
                Assert.Equal(baselineFsOffsets[i], entry.FsOffset);
                Assert.Equal(baselineFsSizes[i], entry.FsSize);
                Assert.Equal(baselinePostGapSizes[i], entry.PostGapSize);
                Assert.Equal(baselinePostGapFsOffsets[i], entry.PostGapFsOffset);
            }

            // Assert: Fidelity count unchanged (same-size merges don't add new fidelity entries)
            // Original 3 entries are in fidelity; same-size merges don't add new ones
            Assert.Equal(3, ctx.FidelityFiles.Count);
        }
    }
}
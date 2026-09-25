using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for FstContext.AddFile collision resolution edge cases.
    /// Feature: nkfs-multi-extent-files
    ///
    /// **Validates: Requirements 11.4**
    ///
    /// Tests:
    /// - Replacing last entry in OrderedList transfers IsLastFile
    /// - Predecessor PostGapSize is recomputed after replacement
    /// - Equal-size entries still merge links (existing behavior preserved)
    /// </summary>
    public class FstContextCollisionResolutionUnitTests
    {
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

        #region IsLastFile Transfer Tests

        /// <summary>
        /// When a larger file is added at the same offset as the last entry, it MERGES
        /// (adds a link) rather than replacing. The original entry keeps its size and IsLastFile.
        /// </summary>
        [Fact]
        public void ReplacingLastEntry_TransfersIsLastFile_ToNewEntry()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert first file (will be last in OrderedList since it's the only one)
            ctx.AddFile(folder, "first.bin", FsType.Iso9660, 0x1000, 0x2000, FsItemType.File);

            // Insert second file at higher offset (becomes the new last entry)
            ctx.AddFile(folder, "last.bin", FsType.Iso9660, 0x8000, 0x1000, FsItemType.File);

            // Verify the second file is marked as last
            FstFile lastEntry = (FstFile)ctx.FileSystem[1];
            Assert.True(lastEntry.IsLastFile, "Second entry should be IsLastFile before merge");

            // Adding a larger file at the same offset triggers MERGE (size > f.FsSize → merge path)
            ctx.AddFile(folder, "bigger_last.bin", FsType.Udf, 0x8000, 0x3000, FsItemType.File);

            // The original entry is preserved with its original size and IsLastFile status
            FstFile merged = (FstFile)ctx.FileSystem[1];
            Assert.True(merged.IsLastFile, "Original entry should retain IsLastFile after merge");
            Assert.Equal(0x1000, merged.FsSize); // Original size kept (first-registered-wins)
        }

        /// <summary>
        /// When the last entry in the OrderedList is replaced, the predecessor should
        /// NOT have IsLastFile set (only the actual last entry should).
        /// </summary>
        [Fact]
        public void ReplacingLastEntry_PredecessorDoesNotGetIsLastFile()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert two files
            ctx.AddFile(folder, "first.bin", FsType.Iso9660, 0x1000, 0x2000, FsItemType.File);
            ctx.AddFile(folder, "last.bin", FsType.Iso9660, 0x8000, 0x1000, FsItemType.File);

            // Replace the last entry with a larger file
            ctx.AddFile(folder, "bigger_last.bin", FsType.Udf, 0x8000, 0x5000, FsItemType.File);

            // Predecessor should NOT have IsLastFile
            FstFile predecessor = (FstFile)ctx.FileSystem[0];
            Assert.False(predecessor.IsLastFile, "Predecessor should not have IsLastFile");

            // Only the replacement should
            FstFile replacement = (FstFile)ctx.FileSystem[1];
            Assert.True(replacement.IsLastFile, "Replacement should have IsLastFile");
        }

        /// <summary>
        /// Replacing a non-last entry should not affect the existing IsLastFile on the actual last entry.
        /// </summary>
        [Fact]
        public void ReplacingNonLastEntry_DoesNotAffectIsLastFile()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert three files at increasing offsets
            ctx.AddFile(folder, "first.bin", FsType.Iso9660, 0x1000, 0x500, FsItemType.File);
            ctx.AddFile(folder, "middle.bin", FsType.Iso9660, 0x4000, 0x1000, FsItemType.File);
            ctx.AddFile(folder, "last.bin", FsType.Iso9660, 0xA000, 0x2000, FsItemType.File);

            // Verify last entry has IsLastFile
            FstFile lastBefore = (FstFile)ctx.FileSystem[2];
            Assert.True(lastBefore.IsLastFile);

            // Replace the middle entry with a larger file
            ctx.AddFile(folder, "bigger_middle.bin", FsType.Udf, 0x4000, 0x3000, FsItemType.File);

            // Last entry should still have IsLastFile
            FstFile lastAfter = (FstFile)ctx.FileSystem[2];
            Assert.True(lastAfter.IsLastFile, "Last entry's IsLastFile should be preserved");

            // Middle replacement should NOT have IsLastFile
            FstFile middle = (FstFile)ctx.FileSystem[1];
            Assert.False(middle.IsLastFile, "Middle replacement should not have IsLastFile");
        }

        #endregion

        #region PostGapSize Recomputation Tests

        /// <summary>
        /// When a larger file is added at the same offset, it MERGES (adds a link).
        /// The predecessor's PostGapSize remains unchanged because the OrderedList entry
        /// keeps its original size (first-registered-wins).
        /// </summary>
        [Fact]
        public void ReplacingEntry_PredecessorPostGapSize_IsRecomputed()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert predecessor at offset 0x1000, size 0x2000 (ends at 0x3000)
            ctx.AddFile(folder, "pred.bin", FsType.Iso9660, 0x1000, 0x2000, FsItemType.File);

            // Insert target at offset 0x5000, size 0x1000
            ctx.AddFile(folder, "target.bin", FsType.Iso9660, 0x5000, 0x1000, FsItemType.File);

            // Check predecessor's gap: 0x5000 - 0x3000 = 0x2000
            FstFile pred = (FstFile)ctx.FileSystem[0];
            Assert.Equal(0x2000, pred.PostGapSize);

            // Add larger file at same offset (triggers MERGE, not replacement)
            ctx.AddFile(folder, "bigger_target.bin", FsType.Udf, 0x5000, 0x4000, FsItemType.File);

            // Predecessor gap is UNCHANGED because the OrderedList entry retains original size
            pred = (FstFile)ctx.FileSystem[0];
            Assert.Equal(0x2000, pred.PostGapSize);

            // The entry's PostGapSize also remains unchanged (original size 0x1000 → ends at 0x6000)
            // PostGapSize = PhysicalVolumeSize - 0x6000 (since it's the last entry)
            FstFile target = (FstFile)ctx.FileSystem[1];
            Assert.Equal(0x100000 - 0x6000, target.PostGapSize);
        }

        /// <summary>
        /// When a larger file is added at a middle offset, it MERGES (adds a link).
        /// The middle entry's PostGapSize remains unchanged because the OrderedList entry
        /// keeps its original size (first-registered-wins).
        /// </summary>
        [Fact]
        public void ReplacingMiddleEntry_PostGapSize_ReflectsNewSize()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert three files:
            // File A at 0x1000, size 0x1000 (ends at 0x2000)
            // File B at 0x5000, size 0x1000 (ends at 0x6000)
            // File C at 0xA000, size 0x2000 (ends at 0xC000)
            ctx.AddFile(folder, "a.bin", FsType.Iso9660, 0x1000, 0x1000, FsItemType.File);
            ctx.AddFile(folder, "b.bin", FsType.Iso9660, 0x5000, 0x1000, FsItemType.File);
            ctx.AddFile(folder, "c.bin", FsType.Iso9660, 0xA000, 0x2000, FsItemType.File);

            // File B's PostGapSize = 0xA000 - 0x6000 = 0x4000
            FstFile fileB = (FstFile)ctx.FileSystem[1];
            Assert.Equal(0x4000, fileB.PostGapSize);

            // Add a larger file at same offset (triggers MERGE, not replacement)
            ctx.AddFile(folder, "bigger_b.bin", FsType.Udf, 0x5000, 0x3000, FsItemType.File);

            // File B's PostGapSize remains UNCHANGED (original size 0x1000 is kept)
            FstFile mergedB = (FstFile)ctx.FileSystem[1];
            Assert.Equal(0x4000, mergedB.PostGapSize);
            // PostGapFsOffset = 0x5000 + 0x1000 = 0x6000 (unchanged)
            Assert.Equal(0x6000, mergedB.PostGapFsOffset);
        }

        /// <summary>
        /// When the predecessor is recomputed after replacement, its PostGapFsOffset should
        /// correctly reflect its own end position (not the replacement's).
        /// </summary>
        [Fact]
        public void ReplacingEntry_PredecessorPostGapFsOffset_IsCorrect()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Predecessor at offset 0x2000, size 0x3000 (ends at 0x5000)
            ctx.AddFile(folder, "pred.bin", FsType.Iso9660, 0x2000, 0x3000, FsItemType.File);

            // Target at offset 0x8000, size 0x1000
            ctx.AddFile(folder, "target.bin", FsType.Iso9660, 0x8000, 0x1000, FsItemType.File);

            // Replace target with larger
            ctx.AddFile(folder, "bigger.bin", FsType.Udf, 0x8000, 0x5000, FsItemType.File);

            FstFile pred = (FstFile)ctx.FileSystem[0];
            // PostGapFsOffset = pred.FsOffset + pred.FsSize = 0x2000 + 0x3000 = 0x5000
            Assert.Equal(0x5000, pred.PostGapFsOffset);
            // PostGapSize = target.FsOffset - pred.PostGapFsOffset = 0x8000 - 0x5000 = 0x3000
            Assert.Equal(0x3000, pred.PostGapSize);
        }

        #endregion

        #region Equal-Size Link Merge Tests

        /// <summary>
        /// When a file with the same offset AND same size is added, the existing entry should
        /// gain a new FstLink (multi-view alias merge), NOT be replaced.
        /// </summary>
        [Fact]
        public void EqualSizeEntry_MergesLinks_IntoExisting()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert initial file
            ctx.AddFile(folder, "file.bin", FsType.Iso9660, 0x4000, 0x2000, FsItemType.File);

            // Insert file with same offset and same size but different FsType
            FstFolder udfFolder = new FstFolder("udf_root", FsType.Udf, null);
            ctx.AddFile(udfFolder, "file.bin", FsType.Udf, 0x4000, 0x2000, FsItemType.File);

            // OrderedList should still have just one entry at that offset
            Assert.Equal(1, ctx.FileSystem.Count);

            // The existing entry should now have 2 links
            FstFile entry = (FstFile)ctx.FileSystem[0];
            Assert.Equal(2, entry.Links.Count);
        }

        /// <summary>
        /// Equal-size merge preserves the original entry's properties (FsSize, FsOffset)
        /// and does not create a new FstFile object.
        /// </summary>
        [Fact]
        public void EqualSizeEntry_PreservesOriginalEntry()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder folder = new FstFolder("root", FsType.Iso9660, null);

            // Insert initial file
            FstFile original = ctx.AddFile(folder, "data.bin", FsType.Iso9660, 0x6000, 0x3000, FsItemType.File);

            // Insert equal-size duplicate
            FstFolder jolietFolder = new FstFolder("joliet", FsType.Joliet, null);
            FstFile result = ctx.AddFile(jolietFolder, "data.bin", FsType.Joliet, 0x6000, 0x3000, FsItemType.File);

            // The returned file should be the same object as the original
            Assert.Same(original, result);

            // Properties should be unchanged
            Assert.Equal(0x6000, result.FsOffset);
            Assert.Equal(0x3000, result.FsSize);
        }

        /// <summary>
        /// Equal-size merge should add a new FstLink with the correct FsType and folder/name.
        /// </summary>
        [Fact]
        public void EqualSizeEntry_AddsCorrectFstLink()
        {
            FstContext ctx = CreateTestFstContext();
            ctx.PhysicalVolumeSize = 0x100000;
            FstFolder isoFolder = new FstFolder("iso", FsType.Iso9660, null);
            FstFolder udfFolder = new FstFolder("udf", FsType.Udf, null);

            // Insert initial ISO entry
            ctx.AddFile(isoFolder, "shared.bin", FsType.Iso9660, 0x2000, 0x1000, FsItemType.File);

            // Insert UDF entry at same offset and size
            ctx.AddFile(udfFolder, "shared_udf.bin", FsType.Udf, 0x2000, 0x1000, FsItemType.File);

            FstFile entry = (FstFile)ctx.FileSystem[0];

            // Should have links from both filesystem types
            Assert.Equal(2, entry.Links.Count);

            // Verify the first link is ISO9660
            FstLink isoLink = entry.Links.FirstOrDefault(l => l.FsType == FsType.Iso9660);
            Assert.NotNull(isoLink);

            // Verify the second link is UDF
            FstLink udfLink = entry.Links.FirstOrDefault(l => l.FsType == FsType.Udf);
            Assert.NotNull(udfLink);
        }

        #endregion
    }
}
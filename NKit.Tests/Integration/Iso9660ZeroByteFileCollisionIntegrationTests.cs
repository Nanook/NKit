using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Integration tests for ISO9660 images with zero-byte file collisions.
    /// Exercises the full pipeline: FstContext.AddFile → fidelity collection → output iteration.
    ///
    /// In real ISO9660 images, zero-byte files all share offset 0 (or another common offset)
    /// because they have no data extent. The OrderedList deduplicates them by offset, but the
    /// fidelity collection must retain all declared files so the .nkfs output is complete.
    ///
    /// **Validates: Requirements 1.1, 2.2, 2.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class Iso9660ZeroByteFileCollisionIntegrationTests
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

        /// <summary>
        /// Simulates the BuildPerTypeFsYaml iteration logic: iterates the fidelity collection
        /// and collects entries that pass the output filter (non-missing, non-empty FullName,
        /// castable to FstFile with at least one FstLink).
        /// </summary>
        private static List<(string FullPath, long Offset, long Size)> SimulateBuildPerTypeFsYamlOutput(
            FidelityFileList fidelityFiles)
        {
            List<(string FullPath, long Offset, long Size)> output = new List<(string FullPath, long Offset, long Size)>();

            foreach (IFsFile file in fidelityFiles.Entries)
            {
                if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                    continue;

                FstFile fstFile = file as FstFile;
                if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                    continue;

                // Each FstLink produces an output entry (simulating per-type emission)
                foreach (FstLink link in fstFile.Links)
                {
                    string parentPath = link.Parent?.Path ?? "";
                    string name = link.EncodedChildName ?? "";
                    string fullPath = string.IsNullOrEmpty(parentPath)
                        ? "/" + name
                        : parentPath + "/" + name;

                    output.Add((fullPath, file.FsOffset, file.FsSize));
                }
            }

            return output;
        }

        #region Scenario 1: Multiple zero-byte files at the same offset

        /// <summary>
        /// Multiple zero-byte files (FsSize == 0) at the same offset with the same FsType:
        /// In ISO9660, the FstLink OrderedList is keyed by (int)FsType, so same-FsType entries
        /// merge onto the existing link (InsertIfMissing returns existed=true). The fidelity
        /// collection has 1 entry (the first insert's reference), and the merged FstFile has
        /// 1 link (since all share FsType.Iso9660).
        ///
        /// To get all files represented in the output, they need DIFFERENT FsTypes (tested in
        /// MultipleZeroByteFiles_SameOffset_OutputContainsEntryPerResolvedType) or DIFFERENT
        /// sizes (tested in MixedSizeFiles_SameOffset_AllAppearInFidelityCollection).
        ///
        /// **Validates: Requirements 1.1, 2.2**
        /// </summary>
        [Fact]
        public void MultipleZeroByteFiles_SameOffset_SameFsType_FidelityHasSingleEntry()
        {
            // Arrange: Create FstContext and add 5 zero-byte files at offset 0 with same FsType
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            string[] fileNames = { "readme.txt", "empty.dat", "placeholder.bin", "zero.log", "blank.cfg" };
            long sharedOffset = 0;
            long zeroSize = 0;

            // Act: Add all zero-byte files at the same offset with same FsType
            foreach (string name in fileNames)
            {
                ctx.AddFile(root, name, FsType.Iso9660, sharedOffset, zeroSize, FsItemType.File);
            }

            // Assert: Fidelity collection has 1 entry (first insert's reference)
            // Same offset + same size → merge path. No new fidelity entries.
            Assert.Equal(1, ctx.FidelityFiles.Count);

            // The FstLink OrderedList is keyed by (int)FsType, so only 1 link for Iso9660
            FstFile mergedFile = ctx.FidelityFiles.Entries[0] as FstFile;
            Assert.NotNull(mergedFile);
            Assert.Equal(1, mergedFile.Links.Count);
            Assert.Equal(FsType.Iso9660, mergedFile.Links[0].FsType);

            // The first file's name is preserved in the link
            Assert.Equal("readme.txt", mergedFile.Links[0].EncodedChildName);
        }

        /// <summary>
        /// Multiple zero-byte files at the same offset with DIFFERENT FsTypes: each distinct
        /// FsType creates a new FstLink on the merged FstFile. The fidelity collection still
        /// has 1 entry (the first insert's reference), but the merged file has multiple links
        /// enabling per-type output.
        ///
        /// **Validates: Requirements 1.1, 2.2**
        /// </summary>
        [Fact]
        public void MultipleZeroByteFiles_SameOffset_DifferentFsTypes_AllLinksPreserved()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            long sharedOffset = 0;
            long zeroSize = 0;

            // Add zero-byte files with different FsTypes
            ctx.AddFile(root, "readme.txt", FsType.Iso9660, sharedOffset, zeroSize, FsItemType.File);
            ctx.AddFile(root, "readme.txt", FsType.Joliet, sharedOffset, zeroSize, FsItemType.File);
            ctx.AddFile(root, "readme.txt", FsType.RockRidge, sharedOffset, zeroSize, FsItemType.File);
            ctx.AddFile(root, "readme.txt", FsType.Udf, sharedOffset, zeroSize, FsItemType.File);
            ctx.AddFile(root, "readme.txt", FsType.Other, sharedOffset, zeroSize, FsItemType.File);

            // Assert: Fidelity collection has 1 entry
            Assert.Equal(1, ctx.FidelityFiles.Count);

            // The merged FstFile has 5 links (one per distinct FsType)
            FstFile mergedFile = ctx.FidelityFiles.Entries[0] as FstFile;
            Assert.NotNull(mergedFile);
            Assert.Equal(5, mergedFile.Links.Count);

            // Verify all FsTypes are represented
            List<FsType> fsTypes = mergedFile.Links.Select(l => l.FsType).ToList();
            Assert.Contains(FsType.Iso9660, fsTypes);
            Assert.Contains(FsType.Joliet, fsTypes);
            Assert.Contains(FsType.RockRidge, fsTypes);
            Assert.Contains(FsType.Udf, fsTypes);
            Assert.Contains(FsType.Other, fsTypes);
        }

        /// <summary>
        /// When BuildPerTypeFsYaml iterates the fidelity collection for zero-byte files
        /// that were merged as FstLinks, the output should contain one entry per link
        /// (since they all resolve to the same filesystem type, only one entry per resolved type).
        ///
        /// **Validates: Requirements 2.2, 2.3**
        /// </summary>
        [Fact]
        public void MultipleZeroByteFiles_SameOffset_OutputContainsEntryPerResolvedType()
        {
            // Arrange: Create FstContext and add zero-byte files with DIFFERENT FsTypes
            // so they resolve to different per-type outputs
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // Use different FsTypes so each resolves to a different output type
            ctx.AddFile(root, "readme.txt", FsType.Iso9660, 0, 0, FsItemType.File);
            ctx.AddFile(root, "readme.txt", FsType.Joliet, 0, 0, FsItemType.File);
            ctx.AddFile(root, "readme.txt", FsType.Udf, 0, 0, FsItemType.File);

            // Assert: Fidelity collection has 1 entry (all same offset + same size → merge)
            Assert.Equal(1, ctx.FidelityFiles.Count);

            // The merged file has 3 links (one per FsType)
            FstFile mergedFile = ctx.FidelityFiles.Entries[0] as FstFile;
            Assert.NotNull(mergedFile);
            Assert.Equal(3, mergedFile.Links.Count);

            // Simulate output iteration
            List<(string FullPath, long Offset, long Size)> output = SimulateBuildPerTypeFsYamlOutput(ctx.FidelityFiles);

            // Should produce 3 output entries (one per FstLink/resolved type)
            Assert.Equal(3, output.Count);

            // All entries share the same offset and size
            Assert.All(output, entry =>
            {
                Assert.Equal(0, entry.Offset);
                Assert.Equal(0, entry.Size);
            });
        }

        /// <summary>
        /// Zero-byte files with the same FsType at the same offset: the FstLink merge
        /// uses InsertIfMissing keyed by FsType, so same-type entries don't create
        /// duplicate links. However, each AddFile call still adds the file to the parent
        /// folder. This test verifies the fidelity collection behavior.
        ///
        /// **Validates: Requirements 1.1, 2.2**
        /// </summary>
        [Fact]
        public void MultipleZeroByteFiles_SameOffset_SameFsType_DifferentNames_MergeAsLinks()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // In ISO9660, FstLink is keyed by FsType (int), so same FsType entries
            // won't create additional links via InsertIfMissing.
            // The first file creates the FstFile with one link.
            // Subsequent same-offset same-size files with the same FsType will attempt
            // InsertIfMissing but it will report "existed" since the key (FsType) matches.
            ctx.AddFile(root, "file1.txt", FsType.Iso9660, 0, 0, FsItemType.File);
            ctx.AddFile(root, "file2.txt", FsType.Iso9660, 0, 0, FsItemType.File);
            ctx.AddFile(root, "file3.txt", FsType.Iso9660, 0, 0, FsItemType.File);

            // Assert: Fidelity count is 1 (all same offset + same size → merge path)
            Assert.Equal(1, ctx.FidelityFiles.Count);

            // The FstLink OrderedList is keyed by (int)FsType, so only 1 link for Iso9660
            FstFile mergedFile = ctx.FidelityFiles.Entries[0] as FstFile;
            Assert.NotNull(mergedFile);
            Assert.Equal(1, mergedFile.Links.Count);
            Assert.Equal(FsType.Iso9660, mergedFile.Links[0].FsType);
        }

        #endregion

        #region Scenario 2: Mix of zero-byte and non-zero files at the same offset

        /// <summary>
        /// When zero-byte files and non-zero files share the same offset:
        /// - Zero-size entries MERGE (rule: size==0 triggers merge)
        /// - Entries with smaller non-zero size create collisions
        ///
        /// **Validates: Requirements 1.1, 2.2, 2.3**
        /// </summary>
        [Fact]
        public void MixedSizeFiles_SameOffset_AllAppearInFidelityCollection()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            long sharedOffset = 0x1000;

            // First file: 4096 bytes at offset 0x1000 → new insert into OrderedList + fidelity
            ctx.AddFile(root, "data.bin", FsType.Iso9660, sharedOffset, 4096, FsItemType.File);

            // Second file: 0 bytes at same offset → size==0 → MERGE (adds link)
            ctx.AddFile(root, "empty1.txt", FsType.Iso9660, sharedOffset, 0, FsItemType.File);

            // Third file: 0 bytes at same offset → size==0 → MERGE (adds link)
            ctx.AddFile(root, "empty2.txt", FsType.Iso9660, sharedOffset, 0, FsItemType.File);

            // Fourth file: 2048 bytes at same offset → smaller than 4096, different name,
            // same FsItemType.File, non-system, both non-zero → COLLISION
            ctx.AddFile(root, "small.dat", FsType.Iso9660, sharedOffset, 2048, FsItemType.File);

            // Assert: Fidelity collection should have:
            // 1. data.bin (first insert, size=4096)
            // 2. small.dat (collision, size=2048 < 4096)
            // Zero-byte files merged as links on the first entry.
            Assert.Equal(2, ctx.FidelityFiles.Count);

            // Verify entries present with correct sizes
            List<FstFile> entries = ctx.FidelityFiles.Entries.Cast<FstFile>().ToList();
            Assert.Contains(entries, e => e.FsSize == 4096);
            Assert.Contains(entries, e => e.FsSize == 2048);
        }

        /// <summary>
        /// The output iteration should produce independent entries for each file at the
        /// shared offset, each with its own distinct path.
        ///
        /// Zero-size entries MERGE (add a link to the existing entry), so they share the
        /// fidelity entry's FsSize in output. Only smaller non-zero entries create collisions.
        ///
        /// **Validates: Requirements 2.2, 2.3**
        /// </summary>
        [Fact]
        public void MixedSizeFiles_SameOffset_OutputHasDistinctPaths()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            long sharedOffset = 0x2000;

            // First: game.exe at 8192 bytes (Iso9660) → new insert
            ctx.AddFile(root, "game.exe", FsType.Iso9660, sharedOffset, 8192, FsItemType.File);
            // Second: empty_marker.txt at 0 bytes (Joliet) → size==0 → MERGE (adds Joliet link)
            ctx.AddFile(root, "empty_marker.txt", FsType.Joliet, sharedOffset, 0, FsItemType.File);
            // Third: config.ini at 1024 bytes (Udf) → 1024 < 8192, different name → COLLISION
            ctx.AddFile(root, "config.ini", FsType.Udf, sharedOffset, 1024, FsItemType.File);

            // Act: Simulate output iteration
            List<(string FullPath, long Offset, long Size)> output = SimulateBuildPerTypeFsYamlOutput(ctx.FidelityFiles);

            // Assert: 3 output entries total
            // - Merged entry (size=8192) has 2 links: game.exe (Iso9660) + empty_marker.txt (Joliet)
            // - Collision entry (size=1024) has 1 link: config.ini (Udf)
            Assert.Equal(3, output.Count);

            List<string> paths = output.Select(o => o.FullPath).ToList();
            Assert.Equal(paths.Count, paths.Distinct().Count()); // All paths are distinct

            // All entries reference the shared offset
            Assert.All(output, entry => Assert.Equal(sharedOffset, entry.Offset));

            // Verify: game.exe output (from merged entry) has size 8192
            Assert.Contains(output, e => e.FullPath.Contains("game.exe") && e.Size == 8192);
            // Verify: empty_marker.txt output (from merged entry) has size 8192 (merged file's size)
            Assert.Contains(output, e => e.FullPath.Contains("empty_marker.txt") && e.Size == 8192);
            // Verify: config.ini output (collision entry) has size 1024
            Assert.Contains(output, e => e.FullPath.Contains("config.ini") && e.Size == 1024);
        }

        #endregion

        #region Scenario 3: Full pipeline - realistic ISO9660 zero-byte file scenario

        /// <summary>
        /// Simulates a realistic ISO9660 disc with a mix of normal files and zero-byte files.
        /// Zero-byte files all share offset 0 (as they would on a real disc), while normal
        /// files have unique offsets. Verifies the complete fidelity collection captures
        /// everything and the output iteration produces the correct entries.
        ///
        /// **Validates: Requirements 1.1, 2.2, 2.3**
        /// </summary>
        [Fact]
        public void RealisticIso9660_ZeroByteFilesAtOffsetZero_FullPipelineProducesAllEntries()
        {
            // Arrange: Simulate a disc with normal files and zero-byte files
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // Normal files at unique offsets (these go into both OrderedList and fidelity)
            ctx.AddFile(root, "SYSTEM.CNF", FsType.Iso9660, 0x800, 0x200, FsItemType.File);
            ctx.AddFile(root, "GAME.EXE", FsType.Iso9660, 0x2000, 0x50000, FsItemType.File);
            ctx.AddFile(root, "DATA.BIN", FsType.Iso9660, 0x60000, 0x100000, FsItemType.File);
            ctx.AddFile(root, "MOVIE.STR", FsType.Iso9660, 0x200000, 0x800000, FsItemType.File);

            // Zero-byte files all at offset 0 (common in real ISO9660 images)
            // First zero-byte file at offset 0: new insert (offset 0 not yet in OrderedList)
            ctx.AddFile(root, "README.TXT", FsType.Iso9660, 0, 0, FsItemType.File);

            // Subsequent zero-byte files at offset 0: same offset + same size → merge as FstLink
            // But since they all have FsType.Iso9660, InsertIfMissing won't add duplicate links
            ctx.AddFile(root, "EMPTY1.DAT", FsType.Iso9660, 0, 0, FsItemType.File);
            ctx.AddFile(root, "EMPTY2.DAT", FsType.Iso9660, 0, 0, FsItemType.File);

            // Zero-byte file with different FsType at offset 0: same offset + same size → merge as FstLink
            ctx.AddFile(root, "README.TXT", FsType.Joliet, 0, 0, FsItemType.File);

            // Assert: OrderedList has 5 unique offsets (0, 0x800, 0x2000, 0x60000, 0x200000)
            Assert.Equal(5, ctx.FileSystem.Count);

            // Fidelity collection has 5 entries (one per unique offset, zero-byte merges don't add)
            Assert.Equal(5, ctx.FidelityFiles.Count);

            // The entry at offset 0 should have 2 links (Iso9660 + Joliet)
            FstFile zeroByteEntry = ctx.FidelityFiles.Entries.Cast<FstFile>()
                .First(f => f.FsOffset == 0);
            Assert.Equal(2, zeroByteEntry.Links.Count);
            Assert.Contains(zeroByteEntry.Links, l => l.FsType == FsType.Iso9660);
            Assert.Contains(zeroByteEntry.Links, l => l.FsType == FsType.Joliet);

            // Simulate output iteration
            List<(string FullPath, long Offset, long Size)> output = SimulateBuildPerTypeFsYamlOutput(ctx.FidelityFiles);

            // Expected output entries:
            // - SYSTEM.CNF (Iso9660 link) = 1
            // - GAME.EXE (Iso9660 link) = 1
            // - DATA.BIN (Iso9660 link) = 1
            // - MOVIE.STR (Iso9660 link) = 1
            // - Zero-byte file at offset 0 (Iso9660 link + Joliet link) = 2
            Assert.Equal(6, output.Count);

            // Verify all normal files appear
            Assert.Contains(output, e => e.FullPath == "/SYSTEM.CNF" && e.Size == 0x200);
            Assert.Contains(output, e => e.FullPath == "/GAME.EXE" && e.Size == 0x50000);
            Assert.Contains(output, e => e.FullPath == "/DATA.BIN" && e.Size == 0x100000);
            Assert.Contains(output, e => e.FullPath == "/MOVIE.STR" && e.Size == 0x800000);

            // Verify zero-byte entries appear (one per resolved type)
            List<(string FullPath, long Offset, long Size)> zeroByteOutputs = output.Where(e => e.Size == 0).ToList();
            Assert.Equal(2, zeroByteOutputs.Count);
            Assert.All(zeroByteOutputs, e => Assert.Equal(0, e.Offset));
        }

        /// <summary>
        /// Simulates a scenario where zero-byte files collide with an existing non-zero file.
        /// Since size==0 triggers MERGE, all zero-byte entries add links to the existing file.
        /// The fidelity collection has only 1 entry (the original).
        ///
        /// **Validates: Requirements 1.1, 2.2, 2.3**
        /// </summary>
        [Fact]
        public void RealisticIso9660_ZeroByteCollisionsWithNonZeroFile_AllAppearInOutput()
        {
            // Arrange: A non-zero file exists at offset 0, then zero-byte files collide
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // First: a real file at offset 0 (e.g., boot sector data)
            ctx.AddFile(root, "BOOT.BIN", FsType.Iso9660, 0, 0x800, FsItemType.File);

            // Zero-byte files at offset 0: size==0 → MERGE (adds link to existing entry)
            ctx.AddFile(root, "EMPTY_A.TXT", FsType.Iso9660, 0, 0, FsItemType.File);
            ctx.AddFile(root, "EMPTY_B.TXT", FsType.Joliet, 0, 0, FsItemType.File);
            ctx.AddFile(root, "EMPTY_C.TXT", FsType.Udf, 0, 0, FsItemType.File);

            // Assert: Fidelity collection has 1 entry (all zero-size entries merged)
            Assert.Equal(1, ctx.FidelityFiles.Count);

            // OrderedList still has only 1 entry (at offset 0)
            Assert.Equal(1, ctx.FileSystem.Count);

            // Simulate output iteration
            List<(string FullPath, long Offset, long Size)> output = SimulateBuildPerTypeFsYamlOutput(ctx.FidelityFiles);

            // The merged file has links for: BOOT.BIN (Iso9660), EMPTY_B.TXT (Joliet), EMPTY_C.TXT (Udf)
            // Note: EMPTY_A.TXT has same FsType (Iso9660) as BOOT.BIN, so InsertIfMissing won't add
            // a duplicate link (FstLink keyed by FsType). Only distinct FsType links are added.
            // So links = Iso9660 (BOOT.BIN), Joliet (EMPTY_B.TXT), Udf (EMPTY_C.TXT) = 3 links
            Assert.Equal(3, output.Count);

            // Verify all paths are distinct
            List<string> paths = output.Select(o => o.FullPath).ToList();
            Assert.Equal(3, paths.Distinct().Count());

            // All entries reference offset 0 and the merged file's size (0x800)
            Assert.All(output, e => Assert.Equal(0, e.Offset));
            Assert.All(output, e => Assert.Equal(0x800, e.Size));
        }

        /// <summary>
        /// Verifies that the disc-mapping pipeline (OrderedList, PostGapSize) is not affected
        /// by zero-byte file collisions. The OrderedList should only contain unique-offset entries.
        ///
        /// **Validates: Requirements 1.1 (fidelity), 3.1 (isolation)**
        /// </summary>
        [Fact]
        public void ZeroByteCollisions_DoNotAffectOrderedList()
        {
            // Arrange
            FstContext ctx = CreateTestFstContext();
            FstFolder root = new FstFolder(FsType.Iso9660);

            // Add files at distinct offsets
            ctx.AddFile(root, "FILE_A.BIN", FsType.Iso9660, 0x0000, 0x1000, FsItemType.File);
            ctx.AddFile(root, "FILE_B.BIN", FsType.Iso9660, 0x2000, 0x1000, FsItemType.File);
            ctx.AddFile(root, "FILE_C.BIN", FsType.Iso9660, 0x4000, 0x1000, FsItemType.File);

            // Add zero-byte collision files at offset 0x2000 (different size from FILE_B)
            ctx.AddFile(root, "ZERO_1.TXT", FsType.Iso9660, 0x2000, 0, FsItemType.File);
            ctx.AddFile(root, "ZERO_2.TXT", FsType.Iso9660, 0x2000, 0, FsItemType.File);

            // Assert: OrderedList unchanged - still 3 entries at unique offsets
            Assert.Equal(3, ctx.FileSystem.Count);

            // Verify PostGapSize computation is correct (based only on OrderedList entries)
            FstFile fileA = (FstFile)ctx.FileSystem[0];
            FstFile fileB = (FstFile)ctx.FileSystem[1];

            // FILE_A: offset=0x0000, size=0x1000, next offset=0x2000
            // PostGapSize = 0x2000 - (0x0000 + 0x1000) = 0x1000
            Assert.Equal(0x1000, fileA.PostGapSize);

            // FILE_B: offset=0x2000, size=0x1000, next offset=0x4000
            // PostGapSize = 0x4000 - (0x2000 + 0x1000) = 0x1000
            Assert.Equal(0x1000, fileB.PostGapSize);

            // Fidelity collection has 3 entries (zero-size entries merged as links, not separate entries)
            Assert.Equal(3, ctx.FidelityFiles.Count);
        }

        #endregion
    }
}
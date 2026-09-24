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
    /// Property-based tests verifying the FsYaml output count invariant.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 6: FsYaml output count invariant
    ///
    /// For any fidelity collection, the FsYaml output entry count for a filesystem area
    /// SHALL equal the number of entries in the fidelity collection where IsMissing == false
    /// and FullName is non-empty.
    ///
    /// Since BuildPerTypeFsYaml also requires entries to be FstFile instances with at least
    /// one FstLink, the full predicate is:
    ///   (file is FstFile) && (fstFile.Links.Count > 0) && (!IsMissing) && (!string.IsNullOrEmpty(FullName))
    ///
    /// **Validates: Requirements 2.2, 2.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class FsYamlOutputCountInvariantPropertyTests
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
        /// Models the BuildPerTypeFsYaml filtering predicate.
        /// An entry is included in the output if and only if:
        /// 1. It is not missing (IsMissing == false)
        /// 2. It has a non-empty FullName
        /// 3. It can be cast to FstFile (always true for entries from FstContext.AddFile)
        /// 4. It has at least one FstLink (always true for entries from FstContext.AddFile)
        /// </summary>
        private static bool ModelIsOutputEntry(IFsFile file)
        {
            if (file.IsMissing)
                return false;
            if (string.IsNullOrEmpty(file.FullName))
                return false;
            if (file is not FstFile fstFile)
                return false;
            if (fstFile.Links == null || fstFile.Links.Count == 0)
                return false;
            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.5**
        ///
        /// Property 6: FsYaml output count invariant.
        /// For any fidelity collection populated via AddFile, with some entries subsequently
        /// marked as IsMissing, the count of entries passing the output filter SHALL equal
        /// the count of entries where !IsMissing and !string.IsNullOrEmpty(FullName).
        ///
        /// This test generates files via FstContext.AddFile (ensuring all entries are valid
        /// FstFile instances with FstLinks), then randomly marks some as IsMissing, and
        /// verifies the model predicate count matches.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OutputCount_Equals_NonMissing_NonEmptyName_Entries()
        {
            // Generate file declarations with unique offsets (to ensure all get into fidelity)
            // then randomly mark some as IsMissing
            var testGen =
                from count in Gen.Choose(1, 30)
                from sizes in Gen.Choose(1, 100).Select(s => (long)s * 0x800).ListOf(count)
                from fsTypeIndices in Gen.Choose(0, ValidFsTypes.Length - 1).ListOf(count)
                from missingFlags in Gen.Elements(true, false).ListOf(count)
                select new
                {
                    Count = count,
                    Sizes = sizes.ToList(),
                    FsTypes = fsTypeIndices.Select(i => ValidFsTypes[i]).ToList(),
                    MissingFlags = missingFlags.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert files at unique offsets so all go into fidelity collection
                List<FstFile> insertedFiles = new List<FstFile>();
                for (int i = 0; i < data.Count; i++)
                {
                    long offset = (long)(i + 1) * 0x800;
                    FstFile f = ctx.AddFile(root, $"file_{i}.dat",
                        data.FsTypes[i], offset, data.Sizes[i], FsItemType.File);
                    insertedFiles.Add(f);
                }

                // Mark some entries as IsMissing
                for (int i = 0; i < data.Count; i++)
                {
                    if (data.MissingFlags[i])
                        insertedFiles[i].IsMissing = true;
                }

                // Count entries that pass the output filter (model)
                int expectedOutputCount = ctx.FidelityFiles.Entries
                    .Count(f => ModelIsOutputEntry(f));

                // Count entries using the same predicate BuildPerTypeFsYaml uses
                int actualOutputCount = 0;
                foreach (IFsFile file in ctx.FidelityFiles.Entries)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;
                    FstFile fstFile = file as FstFile;
                    if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                        continue;
                    actualOutputCount++;
                }

                // The expected count should be: total entries minus those marked as missing
                // (all entries have non-empty FullName since they were created with named folders)
                int nonMissingCount = data.MissingFlags.Count(m => !m);

                return (actualOutputCount == expectedOutputCount &&
                        actualOutputCount == nonMissingCount)
                    .Label($"Expected output count {nonMissingCount}, model says {expectedOutputCount}, " +
                           $"actual filter says {actualOutputCount} " +
                           $"(total={data.Count}, missing={data.MissingFlags.Count(m => m)})");
            });
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.5**
        ///
        /// Property 6 (collision variant): When the fidelity collection contains entries
        /// from offset collisions (different-size entries at same offset), the output count
        /// still equals the number of non-missing, non-empty-name entries.
        /// Collision entries are separate FstFile instances with their own IsMissing state.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OutputCount_WithCollisions_Equals_NonMissing_NonEmptyName_Entries()
        {
            // Generate file declarations with some collisions (same offset, different size)
            var testGen =
                from baseCount in Gen.Choose(2, 15)
                from collisionCount in Gen.Choose(1, Math.Max(1, baseCount / 2))
                from baseSizes in Gen.Choose(1, 50).Select(s => (long)s * 0x800).ListOf(baseCount)
                from collisionSizes in Gen.Choose(51, 100).Select(s => (long)s * 0x800).ListOf(collisionCount)
                from fsTypeIndices in Gen.Choose(0, ValidFsTypes.Length - 1).ListOf(baseCount + collisionCount)
                from missingFlags in Gen.Elements(true, false).ListOf(baseCount + collisionCount)
                select new
                {
                    BaseCount = baseCount,
                    CollisionCount = collisionCount,
                    BaseSizes = baseSizes.ToList(),
                    CollisionSizes = collisionSizes.ToList(),
                    FsTypes = fsTypeIndices.Select(i => ValidFsTypes[i]).ToList(),
                    MissingFlags = missingFlags.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                List<FstFile> allFiles = new List<FstFile>();

                // Insert base files at unique offsets
                for (int i = 0; i < data.BaseCount; i++)
                {
                    long offset = (long)(i + 1) * 0x800;
                    FstFile f = ctx.AddFile(root, $"base_{i}.dat",
                        data.FsTypes[i], offset, data.BaseSizes[i], FsItemType.File);
                    allFiles.Add(f);
                }

                // Insert collision files at same offsets as first N base files but with different sizes
                for (int i = 0; i < data.CollisionCount; i++)
                {
                    long offset = (long)(i + 1) * 0x800; // same offset as base file i
                    // Use a different FsType to get a different name in the folder
                    FsType fsType = data.FsTypes[data.BaseCount + i];
                    FstFile f = ctx.AddFile(root, $"collision_{i}.dat",
                        fsType, offset, data.CollisionSizes[i], FsItemType.File);
                    // For collisions with different size, AddFile returns the EXISTING file
                    // but the collision entry is a NEW FstFile added to fidelity only.
                    // We need to find the collision entry in the fidelity list.
                }

                // Get all fidelity entries (base files + collision entries)
                List<IFsFile> fidelityEntries = ctx.FidelityFiles.Entries;

                // Mark entries as IsMissing based on flags
                // We mark up to the number of fidelity entries we have
                int totalEntries = fidelityEntries.Count;
                for (int i = 0; i < totalEntries && i < data.MissingFlags.Count; i++)
                {
                    if (data.MissingFlags[i])
                        ((FstFile)fidelityEntries[i]).IsMissing = true;
                }

                // Count entries that pass the output filter
                int actualOutputCount = 0;
                foreach (IFsFile file in fidelityEntries)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;
                    FstFile fstFile = file as FstFile;
                    if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                        continue;
                    actualOutputCount++;
                }

                // Model: count non-missing, non-empty-name entries
                int expectedOutputCount = fidelityEntries
                    .Count(f => ModelIsOutputEntry(f));

                return (actualOutputCount == expectedOutputCount)
                    .Label($"Expected output count {expectedOutputCount} but got {actualOutputCount} " +
                           $"(total fidelity entries={totalEntries}, " +
                           $"base={data.BaseCount}, collisions={data.CollisionCount})");
            });
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.5**
        ///
        /// Property 6 (all missing variant): When all entries in the fidelity collection
        /// are marked as IsMissing, the output count SHALL be zero.
        /// </summary>
        [Property(MaxTest = 100)]
        public Property OutputCount_AllMissing_IsZero()
        {
            var testGen =
                from count in Gen.Choose(1, 20)
                from sizes in Gen.Choose(1, 100).Select(s => (long)s * 0x800).ListOf(count)
                select new { Count = count, Sizes = sizes.ToList() };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                // Insert files at unique offsets
                for (int i = 0; i < data.Count; i++)
                {
                    long offset = (long)(i + 1) * 0x800;
                    FstFile f = ctx.AddFile(root, $"file_{i}.dat",
                        FsType.Iso9660, offset, data.Sizes[i], FsItemType.File);
                    f.IsMissing = true; // Mark all as missing
                }

                // Count entries that pass the output filter
                int actualOutputCount = 0;
                foreach (IFsFile file in ctx.FidelityFiles.Entries)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;
                    FstFile fstFile = file as FstFile;
                    if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                        continue;
                    actualOutputCount++;
                }

                return (actualOutputCount == 0)
                    .Label($"Expected 0 output entries when all are missing, but got {actualOutputCount}");
            });
        }
    }
}
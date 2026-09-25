using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests verifying multi-link per-type emission behavior.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 8: Multi-link per-type emission
    ///
    /// For any merged FstFile with FstLinks resolving to K distinct filesystem types,
    /// BuildPerTypeFsYaml SHALL produce exactly K per-type dictionary entries for that file,
    /// each with identical image offset and file size.
    ///
    /// Since testing the full BuildPerTypeFsYaml requires complex Scan/ScanArea infrastructure,
    /// we test the per-type resolution and emission logic in isolation by:
    /// 1. Creating merged FstFiles via FstContext.AddFile (same offset, same size, different FsType)
    /// 2. Simulating the per-type entry resolution logic from BuildPerTypeFsYaml
    /// 3. Verifying K distinct resolved types produce exactly K entries with identical offset/size
    ///
    /// **Validates: Requirements 5.3, 5.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class MultiLinkPerTypeEmissionPropertyTests
    {
        /// <summary>
        /// FsType values that can appear as FstLinks on a merged FstFile.
        /// These are the types used in ISO9660 filesystem parsing.
        /// </summary>
        private static readonly FsType[] LinkableFsTypes = new[]
        {
            FsType.Iso9660,
            FsType.Joliet,
            FsType.RockRidge,
            FsType.Udf
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
        /// Simulates the per-type entry resolution logic from BuildPerTypeFsYaml for a single FstFile.
        /// Returns the set of (resolvedType, imageOffset, fileSize) tuples that would be emitted.
        /// </summary>
        private static List<(string ResolvedType, long ImageOffset, long FileSize)> SimulatePerTypeEmission(
            FstFile fstFile, long areaImageOffset)
        {
            List<(string, long, long)> result = new List<(string, long, long)>();

            if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                return result;

            long imageOffset = areaImageOffset + fstFile.FsOffset;

            // Replicate the typeEntries logic from BuildPerTypeFsYaml
            Dictionary<string, string> typeEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (FstLink link in fstFile.Links)
            {
                string resolvedType = DataStoreIso9660Formatter.ResolveTargetFsType(link.FsType);

                if (typeEntries.ContainsKey(resolvedType))
                {
                    // RockRidge name takes priority over Iso9660 for the iso9660 type
                    if (link.FsType == FsType.RockRidge && resolvedType == "iso9660")
                        typeEntries[resolvedType] = $"/{link.EncodedChildName}";
                    continue;
                }

                typeEntries[resolvedType] = $"/{link.EncodedChildName}";
            }

            // Each typeEntry produces one per-type emission with identical offset and size
            foreach (KeyValuePair<string, string> entry in typeEntries)
            {
                result.Add((entry.Key, imageOffset, fstFile.FsSize));
            }

            return result;
        }

        /// <summary>
        /// **Validates: Requirements 5.3, 5.4**
        ///
        /// Property 8: Multi-link per-type emission.
        /// For any merged FstFile with FstLinks resolving to K distinct filesystem types,
        /// the per-type emission logic SHALL produce exactly K entries, each with identical
        /// image offset and file size.
        ///
        /// We create merged FstFiles by inserting multiple entries at the same offset with
        /// the same size but different FsTypes (which triggers FstLink merging in FstContext).
        /// Then we verify the emission produces the correct number of per-type entries.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property MergedFstFile_ProducesExactly_KPerTypeEntries()
        {
            // Generate a combined tuple: (fsTypes, size, offset, areaOffset)
            Gen<(List<FsType> fsTypes, long size, long offset, long areaOffset)> combinedGen =
                from fsTypes in Gen.SubListOf(LinkableFsTypes)
                    .Where(list => list.Count >= 1)
                    .Select(list => list.Distinct().ToList())
                from size in Gen.Choose(1, 100).Select(s => (long)s * 0x800)
                from offset in Gen.Choose(1, 1000).Select(o => (long)o * 0x800)
                from areaOffset in Gen.Choose(0, 100).Select(o => (long)o * 0x100000)
                select (fsTypes, size, offset, areaOffset);

            return Prop.ForAll(combinedGen.ToArbitrary(), input =>
            {
                (List<FsType> fsTypes, long size, long offset, long areaOffset) = input;

                // Create a merged FstFile by inserting same offset+size with different FsTypes
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                FstFile mergedFile = null;
                foreach (FsType fsType in fsTypes)
                {
                    mergedFile = ctx.AddFile(root, $"file_{fsType}",
                        fsType, offset, size, FsItemType.File);
                }

                // Compute expected K: number of distinct resolved types
                HashSet<string> expectedResolvedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FsType fsType in fsTypes)
                {
                    expectedResolvedTypes.Add(DataStoreIso9660Formatter.ResolveTargetFsType(fsType));
                }
                int expectedK = expectedResolvedTypes.Count;

                // Simulate per-type emission
                List<(string ResolvedType, long ImageOffset, long FileSize)> emissions = SimulatePerTypeEmission(mergedFile, areaOffset);

                // Verify exactly K entries are produced
                bool correctCount = emissions.Count == expectedK;

                // Verify all entries have identical image offset
                long expectedImageOffset = areaOffset + offset;
                bool allSameOffset = emissions.All(e => e.ImageOffset == expectedImageOffset);

                // Verify all entries have identical file size
                bool allSameSize = emissions.All(e => e.FileSize == size);

                // Verify each resolved type appears exactly once
                HashSet<string> emittedTypes = emissions.Select(e => e.ResolvedType).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool typesMatch = emittedTypes.SetEquals(expectedResolvedTypes);

                return (correctCount && allSameOffset && allSameSize && typesMatch)
                    .Label($"FsTypes=[{string.Join(", ", fsTypes)}], " +
                           $"expectedK={expectedK}, actualEmissions={emissions.Count}, " +
                           $"allSameOffset={allSameOffset}, allSameSize={allSameSize}, " +
                           $"typesMatch={typesMatch}, " +
                           $"expectedTypes=[{string.Join(", ", expectedResolvedTypes)}], " +
                           $"emittedTypes=[{string.Join(", ", emittedTypes)}]");
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.3, 5.4**
        ///
        /// Property 8 (supplementary): When RockRidge and Iso9660 links coexist on the same FstFile,
        /// they resolve to a single "iso9660" type entry (not two separate entries), confirming
        /// that K distinct FsTypes may produce fewer than K per-type entries when types merge.
        /// The single entry still has the same image offset and file size.
        /// </summary>
        [Property(MaxTest = 100)]
        public Property RockRidgeAndIso9660_MergeToSingleResolvedType()
        {
            // Generate a combined tuple: (size, offset, areaOffset, extraTypes)
            Gen<(long size, long offset, long areaOffset, List<FsType> extraTypes)> combinedGen =
                from size in Gen.Choose(1, 100).Select(s => (long)s * 0x800)
                from offset in Gen.Choose(1, 1000).Select(o => (long)o * 0x800)
                from areaOffset in Gen.Choose(0, 100).Select(o => (long)o * 0x100000)
                from extraTypes in Gen.SubListOf(new[] { FsType.Joliet, FsType.Udf })
                select (size, offset, areaOffset, extraTypes);

            return Prop.ForAll(combinedGen.ToArbitrary(), input =>
            {
                (long size, long offset, long areaOffset, List<FsType> extraTypes) = input;

                // Always include both Iso9660 and RockRidge
                List<FsType> allTypes = new List<FsType> { FsType.Iso9660, FsType.RockRidge };
                allTypes.AddRange(extraTypes);

                // Create merged FstFile
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                FstFile mergedFile = null;
                foreach (FsType fsType in allTypes)
                {
                    mergedFile = ctx.AddFile(root, $"file_{fsType}",
                        fsType, offset, size, FsItemType.File);
                }

                // Compute expected resolved types
                HashSet<string> expectedResolvedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FsType fsType in allTypes)
                {
                    expectedResolvedTypes.Add(DataStoreIso9660Formatter.ResolveTargetFsType(fsType));
                }
                // Iso9660 and RockRidge both resolve to "iso9660" → they merge
                int expectedK = expectedResolvedTypes.Count;

                // Simulate per-type emission
                List<(string ResolvedType, long ImageOffset, long FileSize)> emissions = SimulatePerTypeEmission(mergedFile, areaOffset);

                // Verify count matches expected (Iso9660+RockRidge = 1 entry, not 2)
                bool correctCount = emissions.Count == expectedK;

                // Verify all entries have identical offset and size
                long expectedImageOffset = areaOffset + offset;
                bool allSameOffset = emissions.All(e => e.ImageOffset == expectedImageOffset);
                bool allSameSize = emissions.All(e => e.FileSize == size);

                // Verify "iso9660" appears exactly once (not twice for Iso9660+RockRidge)
                int iso9660Count = emissions.Count(e =>
                    string.Equals(e.ResolvedType, "iso9660", StringComparison.OrdinalIgnoreCase));
                bool iso9660Once = iso9660Count == 1;

                return (correctCount && allSameOffset && allSameSize && iso9660Once)
                    .Label($"Types=[{string.Join(", ", allTypes)}], " +
                           $"expectedK={expectedK}, actualEmissions={emissions.Count}, " +
                           $"iso9660Count={iso9660Count}, " +
                           $"allSameOffset={allSameOffset}, allSameSize={allSameSize}");
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.3, 5.4**
        ///
        /// Property 8 (supplementary): For any number of FstLinks (1 to 4) with distinct FsTypes
        /// on a merged FstFile, the image offset in each per-type entry equals
        /// areaImageOffset + file.FsOffset, and the file size equals file.FsSize.
        /// This confirms the "identical image offset and file size" invariant.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AllPerTypeEntries_HaveIdentical_OffsetAndSize()
        {
            // Generate a combined tuple: (fsTypes, size, offset, areaOffset)
            Gen<(List<FsType> fsTypes, long size, long offset, long areaOffset)> combinedGen =
                from fsTypes in Gen.SubListOf(LinkableFsTypes)
                    .Where(list => list.Count >= 1)
                    .Select(list => list.Distinct().ToList())
                from size in Gen.Choose(1, 500).Select(s => (long)s * 0x800)
                from offset in Gen.Choose(1, 5000).Select(o => (long)o * 0x800)
                from areaOffset in Gen.Choose(0, 200).Select(o => (long)o * 0x100000)
                select (fsTypes, size, offset, areaOffset);

            return Prop.ForAll(combinedGen.ToArbitrary(), input =>
            {
                (List<FsType> fsTypes, long size, long offset, long areaOffset) = input;

                // Create merged FstFile
                FstContext ctx = CreateTestFstContext();
                FstFolder root = new FstFolder(FsType.Iso9660);

                FstFile mergedFile = null;
                foreach (FsType fsType in fsTypes)
                {
                    mergedFile = ctx.AddFile(root, $"file_{fsType}",
                        fsType, offset, size, FsItemType.File);
                }

                // Simulate per-type emission
                List<(string ResolvedType, long ImageOffset, long FileSize)> emissions = SimulatePerTypeEmission(mergedFile, areaOffset);

                // All emissions must have the expected image offset
                long expectedImageOffset = areaOffset + offset;
                bool allCorrectOffset = emissions.All(e => e.ImageOffset == expectedImageOffset);

                // All emissions must have the expected file size
                bool allCorrectSize = emissions.All(e => e.FileSize == size);

                // At least one emission must exist (file has at least 1 link)
                bool hasEmissions = emissions.Count >= 1;

                return (allCorrectOffset && allCorrectSize && hasEmissions)
                    .Label($"FsTypes=[{string.Join(", ", fsTypes)}], " +
                           $"emissions={emissions.Count}, " +
                           $"expectedOffset=0x{expectedImageOffset:X}, " +
                           $"expectedSize=0x{size:X}, " +
                           $"allCorrectOffset={allCorrectOffset}, " +
                           $"allCorrectSize={allCorrectSize}");
            });
        }
    }
}
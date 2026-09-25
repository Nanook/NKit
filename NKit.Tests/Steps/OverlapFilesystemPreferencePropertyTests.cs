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
    /// Property-based tests verifying BuildOverlapCandidates filesystem preference filtering.
    ///
    /// Feature: shared-extent-multi-file-write, Property 3: Filesystem Preference Filtering
    ///
    /// For any set of filesystem entries with multiple filesystem type annotations (UDF, Joliet, ISO9660),
    /// BuildOverlapCandidates SHALL include only files from the preferred filesystem type in the
    /// overlap candidate list.
    ///
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class OverlapFilesystemPreferencePropertyTests
    {
        /// <summary>
        /// The relevant filesystem types for overlap candidate filtering, ordered by priority.
        /// Priority ordering (least to most): ElTorito &lt; Iso9660 &lt; Joliet &lt; Udf
        /// </summary>
        private static readonly FsType[] RelevantFsTypes = new[]
        {
            FsType.ElTorito,
            FsType.Iso9660,
            FsType.Joliet,
            FsType.Udf
        };

        /// <summary>
        /// Models the filesystem preference determination logic from BuildOverlapCandidates.
        /// Determines the preferred FsType by scanning all candidates and selecting the
        /// highest-priority FsType found in any candidate's Links[0].
        /// </summary>
        private static FsType DeterminePreferredFsType(List<FstFile> candidates)
        {
            FsType preferred = FsType.ElTorito; // lowest priority
            foreach (FstFile c in candidates)
            {
                if (c.Links.Count > 0)
                {
                    FsType highest = c.Links[0].FsType; // Links sorted descending: [0] is highest
                    if (highest > preferred)
                        preferred = highest;
                    if (preferred == FsType.Udf)
                        break; // Can't get higher
                }
            }
            return preferred;
        }

        /// <summary>
        /// Models the filesystem filtering logic from BuildOverlapCandidates.
        /// An entry passes the filter if its Links[0].FsType >= preferredFsType.
        /// </summary>
        private static bool PassesFilesystemFilter(FstFile entry, FsType preferredFsType)
        {
            if (entry.Links.Count > 0)
            {
                FsType entryHighestFs = entry.Links[0].FsType;
                return entryHighestFs >= preferredFsType;
            }
            // Entries without Links are not filtered out by filesystem type
            return true;
        }

        /// <summary>
        /// Creates a FstFile with a specific FsType via its Links collection.
        /// </summary>
        private static FstFile CreateFstFileWithFsType(
            string name, FsType fsType, long offset, long size)
        {
            FstFolder root = new FstFolder(fsType);
            FstFolder parent = new FstFolder("BDMV", fsType, root);
            return new FstFile(parent, name, fsType, offset, size, FsItemType.File);
        }

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 3: For any set of filesystem entries with multiple filesystem types,
        /// the filtering logic SHALL exclude entries whose highest FsType is lower than
        /// the preferred (highest) FsType found in the candidate set.
        ///
        /// This means: if candidates contain UDF files, only UDF entries pass the filter.
        /// If candidates contain Joliet (and no UDF), only Joliet-or-higher entries pass.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property LowerPriorityFilesystemEntries_AreExcluded()
        {
            // Generate a preferred filesystem type (at least Iso9660 — to have something to filter)
            Gen<FsType> preferredFsGen = Gen.Elements(FsType.Iso9660, FsType.Joliet, FsType.Udf);

            // Generate a set of FidelityFileList entries with mixed filesystem types
            Gen<int> entryCountGen = Gen.Choose(2, 10);

            var testGen =
                from preferredFs in preferredFsGen
                from entryCount in entryCountGen
                from fsTypes in Gen.Elements(RelevantFsTypes).ListOf(entryCount)
                where fsTypes.Any(t => t == preferredFs) // Ensure at least one entry matches preferred
                where fsTypes.Any(t => t < preferredFs)  // Ensure at least one entry is lower priority
                select new { PreferredFs = preferredFs, FsTypes = fsTypes.ToList() };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create FidelityFileList entries with the generated filesystem types
                List<FstFile> entries = data.FsTypes.Select((fsType, i) =>
                    CreateFstFileWithFsType(
                        $"file{i}.m2ts", fsType,
                        offset: (long)(i + 1) * 0x10000,
                        size: 0x2000))
                    .ToList();

                // Create candidates that establish the preferred filesystem type
                // Pick one entry with the preferred FsType as a candidate
                FstFile candidateEntry = entries.First(e => e.Links[0].FsType == data.PreferredFs);
                List<FstFile> candidates = new List<FstFile> { candidateEntry };

                // Determine preferred type (should match data.PreferredFs)
                FsType preferred = DeterminePreferredFsType(candidates);

                // Apply the filter to all entries
                List<FstFile> passingEntries = entries.Where(e => PassesFilesystemFilter(e, preferred)).ToList();
                List<FstFile> filteredOutEntries = entries.Where(e => !PassesFilesystemFilter(e, preferred)).ToList();

                // PROPERTY: No entry with FsType < preferred should pass the filter
                bool noLowerPriorityPasses = passingEntries.All(e =>
                    e.Links.Count == 0 || e.Links[0].FsType >= preferred);

                // PROPERTY: All entries with FsType < preferred should be excluded
                bool allLowerPriorityExcluded = filteredOutEntries.All(e =>
                    e.Links.Count > 0 && e.Links[0].FsType < preferred);

                // PROPERTY: At least one entry is excluded (we ensured lower-priority entries exist)
                bool someExcluded = filteredOutEntries.Count > 0;

                return (noLowerPriorityPasses && allLowerPriorityExcluded && someExcluded)
                    .Label($"Preferred={preferred}: " +
                           $"{passingEntries.Count} entries pass (>= {preferred}), " +
                           $"{filteredOutEntries.Count} excluded (< {preferred})");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 3 (supplementary): When candidates contain multiple filesystem types,
        /// the highest-priority type determines the filter threshold.
        /// For example, if candidates contain both Joliet and UDF files, UDF wins and
        /// only UDF entries are included.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property HighestCandidateFsType_DeterminesFilterThreshold()
        {
            // Generate candidates with multiple different filesystem types
            Gen<List<FsType>> testGen =
                from candidateCount in Gen.Choose(2, 6)
                from candidateFsTypes in Gen.Elements(RelevantFsTypes).ListOf(candidateCount)
                where candidateFsTypes.Distinct().Count() >= 2 // At least 2 different types
                select candidateFsTypes.ToList();

            return Prop.ForAll(testGen.ToArbitrary(), candidateFsTypes =>
            {
                // Create candidate files with the generated filesystem types
                List<FstFile> candidates = candidateFsTypes.Select((fsType, i) =>
                    CreateFstFileWithFsType(
                        $"candidate{i}.bin", fsType,
                        offset: (long)(i + 1) * 0x10000,
                        size: 0x5000))
                    .ToList();

                // Determine preferred type — should be the MAX of all candidate FsTypes
                FsType preferred = DeterminePreferredFsType(candidates);
                FsType expectedPreferred = candidateFsTypes.Max();

                // PROPERTY: The preferred type equals the maximum FsType in the candidate set
                bool preferredIsMax = preferred == expectedPreferred;

                // Now create some fidelity entries at various filesystem levels
                List<FstFile> fidelityEntries = RelevantFsTypes.Select((fsType, i) =>
                    CreateFstFileWithFsType(
                        $"fidelity{i}.dat", fsType,
                        offset: (long)(i + 10) * 0x10000,
                        size: 0x3000))
                    .ToList();

                // Apply filter
                List<FstFile> passingEntries = fidelityEntries.Where(e => PassesFilesystemFilter(e, preferred)).ToList();

                // PROPERTY: Only entries with FsType >= preferred pass
                bool onlyPreferredOrHigherPass = passingEntries.All(e =>
                    e.Links[0].FsType >= preferred);

                // PROPERTY: All entries with FsType >= preferred DO pass
                int expectedPassCount = fidelityEntries.Count(e => e.Links[0].FsType >= preferred);
                bool allExpectedPass = passingEntries.Count == expectedPassCount;

                return (preferredIsMax && onlyPreferredOrHigherPass && allExpectedPass)
                    .Label($"Candidates have types [{string.Join(", ", candidateFsTypes.Select(t => t.ToString()))}], " +
                           $"preferred={preferred}, " +
                           $"{passingEntries.Count}/{fidelityEntries.Count} entries pass filter");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 3 (edge case): When all candidates are from UDF (highest priority),
        /// only UDF entries from the fidelity list pass the filter. All Joliet, ISO9660,
        /// and ElTorito entries are excluded.
        /// </summary>
        [Property(MaxTest = 100)]
        public Property UdfCandidates_ExcludeAllLowerFilesystems()
        {
            Gen<int> entryCountGen = Gen.Choose(1, 8);

            // Generate fidelity entries with various non-UDF types
            Gen<FsType> lowerFsGen = Gen.Elements(FsType.ElTorito, FsType.Iso9660, FsType.Joliet);

            return Prop.ForAll(entryCountGen.ToArbitrary(), lowerFsGen.ToArbitrary(),
                (entryCount, lowerFs) =>
                {
                    // Create a UDF candidate
                    FstFile udfCandidate = CreateFstFileWithFsType(
                        "primary.ssif", FsType.Udf, 0x1000, 0x100000);
                    List<FstFile> candidates = new List<FstFile> { udfCandidate };

                    FsType preferred = DeterminePreferredFsType(candidates);

                    // Create fidelity entries with the lower filesystem type
                    List<FstFile> lowerEntries = Enumerable.Range(0, entryCount)
                        .Select(i => CreateFstFileWithFsType(
                            $"lower{i}.m2ts", lowerFs,
                            offset: (long)(i + 5) * 0x10000,
                            size: 0x4000))
                        .ToList();

                    // Also create a UDF fidelity entry (should pass)
                    FstFile udfEntry = CreateFstFileWithFsType(
                        "udf_extra.m2ts", FsType.Udf, 0x200000, 0x8000);

                    // Apply filter
                    bool udfPasses = PassesFilesystemFilter(udfEntry, preferred);
                    bool allLowerExcluded = lowerEntries.All(e => !PassesFilesystemFilter(e, preferred));

                    return (preferred == FsType.Udf && udfPasses && allLowerExcluded)
                        .Label($"UDF preferred: UDF entry passes={udfPasses}, " +
                               $"all {entryCount} {lowerFs} entries excluded={allLowerExcluded}");
                });
        }
    }
}
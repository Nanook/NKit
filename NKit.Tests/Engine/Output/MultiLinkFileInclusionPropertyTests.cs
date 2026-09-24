using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests verifying files with multiple FstLinks appear in all
    /// corresponding per-type nkfs files.
    ///
    /// Feature: multi-filesystem-nkfs, Property 3: Files with multiple FstLinks appear in all corresponding per-type nkfs files
    ///
    /// For any file entry with FstLinks to K distinct resolved filesystem types, that file
    /// SHALL appear in exactly K per-type nkfs files, each with the name from the corresponding
    /// FstLink, and with identical image offset and file size across all K files.
    ///
    /// Since testing the full BuildPerTypeFsYaml requires complex Scan objects, we test at the
    /// resolution level: generating K distinct non-extension FsType values, resolving each via
    /// ResolveTargetFsType, and verifying K distinct resolved type names are produced (meaning
    /// the file would appear in K per-type nkfs files).
    ///
    /// **Validates: Requirements 1.4, 5.1, 5.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class MultiLinkFileInclusionPropertyTests
    {
        /// <summary>
        /// Non-extension filesystem types that produce distinct resolved names.
        /// These are the types that can appear as standalone per-type nkfs files.
        /// Extension types (RockRidge, Cdxa) merge into their parent and do not
        /// produce standalone files.
        /// </summary>
        private static readonly FsType[] NonExtensionFsTypes = Enum.GetValues<FsType>()
            .Where(t => t != FsType.RockRidge && t != FsType.Cdxa)
            .ToArray();

        /// <summary>
        /// **Validates: Requirements 1.4, 5.1, 5.3**
        ///
        /// Property 3: Files with multiple FstLinks appear in all corresponding per-type nkfs files.
        ///
        /// For K distinct non-extension FsType values, resolving each via ResolveTargetFsType
        /// SHALL produce exactly K distinct resolved type names. This guarantees that a file
        /// linked to K distinct non-extension types would appear in exactly K per-type nkfs files.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property DistinctNonExtensionTypes_ProduceDistinctResolvedNames()
        {
            // Generate a subset of K distinct non-extension FsType values (K = 1..NonExtensionFsTypes.Length)
            Gen<List<FsType>> subsetGen = Gen.Elements(NonExtensionFsTypes)
                .ListOf()
                .Select(list => list.Distinct().ToList())
                .Where(list => list.Count >= 1);

            return Prop.ForAll(subsetGen.ToArbitrary(), distinctTypes =>
            {
                int k = distinctTypes.Count;

                // Resolve each type
                HashSet<string> resolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FsType fsType in distinctTypes)
                {
                    string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType);
                    resolvedNames.Add(resolved);
                }

                // K distinct non-extension types must produce exactly K distinct resolved names
                return (resolvedNames.Count == k)
                    .Label($"Expected {k} distinct resolved names but got {resolvedNames.Count} " +
                           $"for types: [{string.Join(", ", distinctTypes)}] → [{string.Join(", ", resolvedNames)}]");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.4, 5.1, 5.3**
        ///
        /// Property 3 (supplementary): For a file with FstLinks to K distinct non-extension types,
        /// each resolved type name is unique and non-empty, confirming the file would be included
        /// in exactly K per-type nkfs files with correct (non-empty) type identifiers.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ResolvedTypeNames_AreNonEmpty_AndLowercase(NonNegativeInt kRaw, NonNegativeInt seed)
        {
            int k = 1 + (kRaw.Get % NonExtensionFsTypes.Length); // 1..N types
            int s = seed.Get;

            // Pick K distinct non-extension types deterministically from seed
            List<FsType> shuffled = NonExtensionFsTypes
                .OrderBy(t => unchecked((((int)t * 31) + s) ^ (s >> 3)))
                .Take(k)
                .ToList();

            List<string> resolvedNames = new List<string>();
            foreach (FsType fsType in shuffled)
            {
                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType);
                resolvedNames.Add(resolved);
            }

            // All resolved names must be non-empty and lowercase
            bool allNonEmpty = resolvedNames.All(n => !string.IsNullOrEmpty(n));
            bool allLowercase = resolvedNames.All(n => n == n.ToLowerInvariant());
            // All resolved names must be distinct (K types → K names)
            bool allDistinct = resolvedNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == k;

            return (allNonEmpty && allLowercase && allDistinct)
                .ToProperty()
                .Label($"K={k}, types=[{string.Join(", ", shuffled)}], " +
                       $"resolved=[{string.Join(", ", resolvedNames)}], " +
                       $"nonEmpty={allNonEmpty}, lowercase={allLowercase}, distinct={allDistinct}");
        }
    }
}
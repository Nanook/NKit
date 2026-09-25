using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests verifying RockRidge merges into ISO9660 with name priority.
    ///
    /// Feature: multi-filesystem-nkfs, Property 4: RockRidge merges into ISO9660 with name priority
    ///
    /// For any file with a RockRidge FstLink (and optionally an Iso9660 FstLink), the file SHALL
    /// appear in filesystem.iso9660.nkfs using the RockRidge name, and SHALL NOT appear in any
    /// filesystem.rockridge.nkfs file.
    ///
    /// **Validates: Requirements 2.1, 2.3, 2.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class RockRidgeMergingPropertyTests
    {
        private static readonly FsType[] AllFsTypes = Enum.GetValues<FsType>();

        /// <summary>
        /// Non-extension filesystem types that can appear alongside RockRidge.
        /// </summary>
        private static readonly FsType[] NonExtensionFsTypes = AllFsTypes
            .Where(t => t != FsType.RockRidge && t != FsType.Cdxa)
            .ToArray();

        /// <summary>
        /// **Validates: Requirements 2.1, 2.3, 2.4**
        ///
        /// Property 4: RockRidge always resolves to "iso9660".
        /// For any parent FsType (or null), ResolveTargetFsType(FsType.RockRidge) SHALL always
        /// return "iso9660", ensuring RockRidge entries merge into the ISO9660 filesystem file.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property RockRidge_AlwaysResolvesTo_Iso9660()
        {
            // Parent types are always non-extension types (or null)
            Gen<FsType?> parentFsTypeGen = Gen.OneOf(
                Gen.Constant<FsType?>(null),
                Gen.Elements(NonExtensionFsTypes).Select(f => (FsType?)f));

            return Prop.ForAll(parentFsTypeGen.ToArbitrary(),
                parentFsType =>
                {
                    string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.RockRidge, parentFsType);

                    return (resolved == "iso9660")
                        .Label($"Expected 'iso9660' but got '{resolved}' for RockRidge with parent={parentFsType}");
                });
        }

        /// <summary>
        /// **Validates: Requirements 2.1, 2.3, 2.4**
        ///
        /// Property 4: No "rockridge" type name ever appears in resolved output.
        /// For any combination of FsType values that includes RockRidge, the resolved type
        /// SHALL never be "rockridge". This ensures no filesystem.rockridge.nkfs is ever produced.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ResolvedTypes_NeverContain_Rockridge(NonNegativeInt linkCountRaw, NonNegativeInt seed)
        {
            int linkCount = 1 + (linkCountRaw.Get % 8); // 1..8 FstLinks
            int s = seed.Get;

            Random random = new Random(s);
            HashSet<string> resolvedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Always include at least one RockRidge link
            resolvedTypes.Add(DataStoreIso9660Formatter.ResolveTargetFsType(FsType.RockRidge));

            // Add random additional links
            for (int i = 0; i < linkCount; i++)
            {
                FsType fsType = AllFsTypes[random.Next(AllFsTypes.Length)];
                FsType? parentFsType = random.Next(3) == 0
                    ? null
                    : NonExtensionFsTypes[random.Next(NonExtensionFsTypes.Length)];

                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType, parentFsType);
                resolvedTypes.Add(resolved);
            }

            // "rockridge" must never appear in the resolved set
            if (resolvedTypes.Contains("rockridge"))
                return false.ToProperty().Label(
                    $"Found 'rockridge' in resolved types: [{string.Join(", ", resolvedTypes)}]");

            return true.ToProperty().Label("No 'rockridge' in resolved types");
        }

        /// <summary>
        /// **Validates: Requirements 2.1, 2.3, 2.4**
        ///
        /// Property 4: When both Iso9660 and RockRidge are present, they resolve to the same
        /// type ("iso9660"), meaning only one entry exists (with RockRidge name priority).
        /// The distinct resolved count doesn't increase when RockRidge is added to a set
        /// that already contains Iso9660.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property Iso9660AndRockRidge_ResolveToSameType_NoDistinctIncrease(NonNegativeInt seed)
        {
            int s = seed.Get;
            Random random = new Random(s);

            // Build a set of FsTypes that includes both Iso9660 and RockRidge
            List<FsType> fsTypes = new List<FsType> { FsType.Iso9660, FsType.RockRidge };

            // Add some random additional types
            int extraCount = random.Next(0, 5);
            for (int i = 0; i < extraCount; i++)
            {
                fsTypes.Add(NonExtensionFsTypes[random.Next(NonExtensionFsTypes.Length)]);
            }

            // Resolve all types
            HashSet<string> resolvedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FsType fsType in fsTypes)
            {
                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType);
                resolvedTypes.Add(resolved);
            }

            // Resolve without RockRidge to compare
            HashSet<string> resolvedWithoutRockRidge = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FsType fsType in fsTypes.Where(t => t != FsType.RockRidge))
            {
                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType);
                resolvedWithoutRockRidge.Add(resolved);
            }

            // Adding RockRidge should not increase the distinct count because it merges into iso9660
            bool noIncrease = resolvedTypes.Count == resolvedWithoutRockRidge.Count;

            // Both Iso9660 and RockRidge must resolve to "iso9660"
            string iso9660Resolved = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Iso9660);
            string rockRidgeResolved = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.RockRidge);
            bool sameType = iso9660Resolved == rockRidgeResolved && rockRidgeResolved == "iso9660";

            return (noIncrease && sameType)
                .Label($"noIncrease={noIncrease} (with={resolvedTypes.Count}, without={resolvedWithoutRockRidge.Count}), " +
                       $"sameType={sameType} (iso9660='{iso9660Resolved}', rockridge='{rockRidgeResolved}')");
        }
    }
}
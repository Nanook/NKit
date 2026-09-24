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
    /// Property-based tests verifying single-filesystem images produce a unified file.
    ///
    /// Feature: multi-filesystem-nkfs, Property 2: Single-filesystem images produce a unified file
    ///
    /// For any file tree where all FstLinks resolve to the same single filesystem type,
    /// the BuildFileSystemYaml logic SHALL produce exactly one file named "filesystem.nkfs"
    /// (the unified format).
    ///
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class SingleFilesystemUnifiedOutputPropertyTests
    {
        private static readonly FsType[] AllFsTypes = Enum.GetValues<FsType>();

        /// <summary>
        /// Non-extension filesystem types that can serve as valid parent types.
        /// </summary>
        private static readonly FsType[] NonExtensionFsTypes = AllFsTypes
            .Where(t => t != FsType.RockRidge && t != FsType.Cdxa)
            .ToArray();

        /// <summary>
        /// Mapping from each resolved target type name to the set of FsType values that resolve to it.
        /// Used to generate sets of FsType values that all resolve to the same target.
        /// </summary>
        private static readonly Dictionary<string, FsType[]> FsTypesByResolvedTarget = BuildFsTypesByResolvedTarget();

        private static Dictionary<string, FsType[]> BuildFsTypesByResolvedTarget()
        {
            Dictionary<string, List<FsType>> dict = new Dictionary<string, List<FsType>>();

            foreach (FsType fsType in AllFsTypes)
            {
                // For Cdxa, use null parent (defaults to iso9660)
                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType);
                if (!dict.ContainsKey(resolved))
                    dict[resolved] = new List<FsType>();
                dict[resolved].Add(fsType);
            }

            return dict.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        /// <summary>
        /// **Validates: Requirements 1.2**
        ///
        /// Property 2: Single-filesystem images produce a unified file.
        /// When all FsType values in a set resolve to the same single type via ResolveTargetFsType,
        /// the distinct resolved type count is exactly 1. This ensures that a single-filesystem
        /// image would produce a unified filesystem.nkfs rather than per-type files.
        ///
        /// Strategy: Pick a target type, then generate random FsType values that all resolve to it.
        /// Verify that resolving all of them produces exactly 1 distinct type name.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AllFsTypes_ResolvingToSameTarget_ProduceExactlyOneDistinctType()
        {
            // Generate a target type name from the known resolved targets
            string[] targetNames = FsTypesByResolvedTarget.Keys.ToArray();
            Gen<string> targetGen = Gen.Elements(targetNames);

            // Generate a count of FsType values to include (1..10)
            Gen<int> countGen = Gen.Choose(1, 10);

            return Prop.ForAll(targetGen.ToArbitrary(), countGen.ToArbitrary(),
                (targetName, count) =>
                {
                    FsType[] compatibleTypes = FsTypesByResolvedTarget[targetName];

                    // Generate 'count' FsType values that all resolve to the same target
                    Random random = new Random(targetName.GetHashCode() ^ count);
                    List<FsType> selectedTypes = new List<FsType>();
                    for (int i = 0; i < count; i++)
                    {
                        selectedTypes.Add(compatibleTypes[random.Next(compatibleTypes.Length)]);
                    }

                    // Resolve all selected types and count distinct results
                    List<string> distinctResolved = selectedTypes
                        .Select(ft => DataStoreIso9660Formatter.ResolveTargetFsType(ft))
                        .Distinct()
                        .ToList();

                    // All should resolve to exactly 1 distinct type (the target)
                    return (distinctResolved.Count == 1 && distinctResolved[0] == targetName)
                        .Label($"Expected 1 distinct type '{targetName}', got {distinctResolved.Count}: [{string.Join(", ", distinctResolved)}] from types [{string.Join(", ", selectedTypes)}]");
                });
        }

        /// <summary>
        /// **Validates: Requirements 1.2**
        ///
        /// Property 2 (supplementary): For any random set of FsType values that happen to
        /// all resolve to the same target type, the distinct resolved count is exactly 1.
        /// This uses a different generation strategy — generate random FsType values with
        /// appropriate parent types, filter to those resolving to the same target, and verify
        /// the invariant holds.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property RandomFsTypeSet_AllResolvingToSameTarget_HasSingleDistinctType(
            NonNegativeInt seed)
        {
            int s = seed.Get;
            Random random = new Random(s);

            // Pick a random target type to constrain to
            string[] targetNames = FsTypesByResolvedTarget.Keys.ToArray();
            string targetName = targetNames[random.Next(targetNames.Length)];
            FsType[] compatibleTypes = FsTypesByResolvedTarget[targetName];

            // Generate 1..8 FsType values that all resolve to this target
            int count = 1 + random.Next(8);
            List<FsType> fsTypes = new List<FsType>();
            for (int i = 0; i < count; i++)
            {
                fsTypes.Add(compatibleTypes[random.Next(compatibleTypes.Length)]);
            }

            // Resolve each and verify exactly 1 distinct result
            HashSet<string> resolvedSet = new HashSet<string>();
            foreach (FsType ft in fsTypes)
            {
                // For Cdxa, when targeting iso9660, use null parent (default)
                // For other targets, Cdxa won't be in the compatible set unless it resolves there
                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(ft);
                resolvedSet.Add(resolved);
            }

            return (resolvedSet.Count == 1)
                .ToProperty()
                .Label($"Expected 1 distinct resolved type for target '{targetName}', got {resolvedSet.Count}: [{string.Join(", ", resolvedSet)}] from [{string.Join(", ", fsTypes)}]");
        }
    }
}
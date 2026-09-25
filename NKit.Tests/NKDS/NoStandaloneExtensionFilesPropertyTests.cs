using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Property-based tests verifying no standalone extension filesystem files are produced.
    ///
    /// Feature: multi-filesystem-nkfs, Property 5: No standalone extension filesystem files are produced
    ///
    /// For any input file tree (regardless of FstLink composition), the output SHALL never contain
    /// files named "filesystem.rockridge.nkfs" or "filesystem.cdxa.nkfs".
    ///
    /// **Validates: Requirements 2.4, 8.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class NoStandaloneExtensionFilesPropertyTests
    {
        private static readonly FsType[] AllFsTypes = Enum.GetValues<FsType>();

        /// <summary>
        /// Non-extension filesystem types that can serve as valid parent types.
        /// Extension types (RockRidge, Cdxa) never appear as parent filesystems in practice
        /// because they are extensions of other filesystem types, not standalone filesystems.
        /// </summary>
        private static readonly FsType[] NonExtensionFsTypes = AllFsTypes
            .Where(t => t != FsType.RockRidge && t != FsType.Cdxa)
            .ToArray();

        /// <summary>
        /// **Validates: Requirements 2.4, 8.4**
        ///
        /// Property 5: No standalone extension filesystem files are produced.
        /// For any FsType value (including RockRidge and Cdxa) with a valid parent FsType
        /// (non-extension type or null), ResolveTargetFsType SHALL never return "rockridge"
        /// or "cdxa". This ensures extension types always merge into their parent filesystem
        /// and no standalone extension filesystem files are ever produced.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ResolveTargetFsType_NeverReturns_ExtensionTypeNames()
        {
            // Generate any FsType (including extension types)
            Gen<FsType> fsTypeGen = Gen.Elements(AllFsTypes);

            // Parent types are always non-extension types (or null) because extension
            // types cannot be parents — they augment other filesystems.
            Gen<FsType?> parentFsTypeGen = Gen.OneOf(
                Gen.Constant<FsType?>(null),
                Gen.Elements(NonExtensionFsTypes).Select(f => (FsType?)f));

            return Prop.ForAll(fsTypeGen.ToArbitrary(), parentFsTypeGen.ToArbitrary(),
                (fsType, parentFsType) =>
                {
                    string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType, parentFsType);

                    // The resolved type must never be "rockridge" or "cdxa"
                    // because these extension types always merge into their parent
                    return resolved != "rockridge" && resolved != "cdxa";
                });
        }

        /// <summary>
        /// **Validates: Requirements 2.4, 8.4**
        ///
        /// Property 5 (supplementary): For any combination of FsType values representing
        /// a file tree's FstLinks, constructing per-type nkfs filenames from the resolved
        /// types SHALL never produce "filesystem.rockridge.nkfs" or "filesystem.cdxa.nkfs".
        /// Simulates arbitrary file trees with various FstLink compositions.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property PerTypeFilenames_NeverContain_ExtensionFilesystemFiles(
            NonNegativeInt linkCountRaw,
            NonNegativeInt seed)
        {
            int linkCount = 1 + (linkCountRaw.Get % 10); // 1..10 FstLinks
            int s = seed.Get;

            // Simulate a file tree with arbitrary FstLink compositions
            Random random = new Random(s);
            for (int i = 0; i < linkCount; i++)
            {
                FsType fsType = AllFsTypes[random.Next(AllFsTypes.Length)];

                // Parent types in practice are always non-extension types (or null)
                FsType? parentFsType = random.Next(3) == 0
                    ? null
                    : NonExtensionFsTypes[random.Next(NonExtensionFsTypes.Length)];

                string resolved = DataStoreIso9660Formatter.ResolveTargetFsType(fsType, parentFsType);
                string fileName = $"filesystem.{resolved}.nkfs";

                if (fileName == "filesystem.rockridge.nkfs" || fileName == "filesystem.cdxa.nkfs")
                    return false.ToProperty().Label(
                        $"Produced forbidden filename '{fileName}' for FsType={fsType}, parent={parentFsType}");
            }

            return true.ToProperty().Label("No extension filesystem files produced");
        }
    }
}
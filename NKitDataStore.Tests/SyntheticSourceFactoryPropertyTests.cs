using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for SyntheticSourceFactory.CreateSyntheticSources()
    /// (Property 11: Synthetic SourceFile Creation Correctness).
    ///
    /// These tests generate random FolderGroupInfo lists and verify that the
    /// factory produces correct synthetic SourceFile instances with the expected
    /// flags, references, names, status, and ordering.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 4.3**
    /// </summary>
    public class SyntheticSourceFactoryPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 4.3**
        ///
        /// Property 11: Synthetic SourceFile Creation Correctness.
        /// For any list of FolderGroupInfo:
        ///   1. Output count matches input count (Req 2.1)
        ///   2. Each output has IsSyntheticFolder == true (Req 2.2)
        ///   3. Each output has SyntheticFolderGroup referencing the correct FolderGroupInfo (Req 2.2, 4.3)
        ///   4. Each output has Name == group.BaseName (Req 2.2)
        ///   5. Each output has Status == SourceFileResult.Valid (Req 2.2)
        ///   6. Output order matches input order (Req 2.4)
        ///   7. ImageFiles and IndexFile are null (Req 2.3)
        /// </summary>
        [Property]
        public bool SyntheticSourceCreation_AllPropertiesCorrect(
            NonNegativeInt countWrapper,
            NonNegativeInt seed)
        {
            int count = countWrapper.Get % 10; // 0..9 groups
            int s = seed.Get;

            // Generate random FolderGroupInfo list
            List<FolderGroupInfo> folderGroups = GenerateFolderGroups(count, s);

            // Call the real factory
            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(folderGroups);

            // 1. Output count matches input count
            if (result.Count != folderGroups.Count)
                return false;

            for (int i = 0; i < folderGroups.Count; i++)
            {
                FolderGroupInfo group = folderGroups[i];
                SourceFile sf = result[i];

                // 2. IsSyntheticFolder is true
                if (!sf.IsSyntheticFolder)
                    return false;

                // 3. SyntheticFolderGroup references the correct FolderGroupInfo
                if (!ReferenceEquals(sf.SyntheticFolderGroup, group))
                    return false;

                // 4. Name matches group.BaseName
                if (sf.Name != group.BaseName)
                    return false;

                // 5. Status is Valid
                if (sf.Status != SourceFileResult.Valid)
                    return false;

                // 6. Order preservation is verified by iterating in lockstep (i)

                // 7. ImageFiles and IndexFile are null
                if (sf.ImageFiles != null)
                    return false;
                if (sf.IndexFile != null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirement 2.1**
        ///
        /// Property 11 (empty input): An empty FolderGroupInfo list produces
        /// an empty synthetic SourceFile list.
        /// </summary>
        [Property]
        public bool SyntheticSourceCreation_EmptyInput_EmptyOutput()
        {
            List<FolderGroupInfo> empty = new List<FolderGroupInfo>();
            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(empty);
            return result.Count == 0;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 4.3**
        ///
        /// Property 11 (reference identity): Each synthetic SourceFile's
        /// SyntheticFolderGroup is the exact same object (reference equality)
        /// as the input FolderGroupInfo, not a copy.
        /// </summary>
        [Property]
        public bool SyntheticSourceCreation_ReferenceIdentity(
            NonNegativeInt countWrapper,
            NonNegativeInt seed)
        {
            int count = (countWrapper.Get % 8) + 1; // 1..8 groups
            int s = seed.Get;

            List<FolderGroupInfo> folderGroups = GenerateFolderGroups(count, s);
            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(folderGroups);

            for (int i = 0; i < folderGroups.Count; i++)
            {
                if (!ReferenceEquals(result[i].SyntheticFolderGroup, folderGroups[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirement 2.4**
        ///
        /// Property 11 (order preservation): The output list preserves the
        /// exact order of the input FolderGroupInfo list, verified by matching
        /// BaseName at each index.
        /// </summary>
        [Property]
        public bool SyntheticSourceCreation_OrderPreserved(
            NonNegativeInt countWrapper,
            NonNegativeInt seed)
        {
            int count = (countWrapper.Get % 10) + 1; // 1..10 groups
            int s = seed.Get;

            List<FolderGroupInfo> folderGroups = GenerateFolderGroups(count, s);
            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(folderGroups);

            if (result.Count != folderGroups.Count)
                return false;

            for (int i = 0; i < folderGroups.Count; i++)
            {
                if (result[i].Name != folderGroups[i].BaseName)
                    return false;
            }

            return true;
        }

        // === Helper Methods ===

        /// <summary>
        /// Generates a list of FolderGroupInfo with deterministic random data.
        /// Varies group types, base names, source folders, system types, and child counts.
        /// </summary>
        private static List<FolderGroupInfo> GenerateFolderGroups(int count, int seed)
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>(count);
            FolderGroupType[] groupTypes = new[] { FolderGroupType.TmdAppFolder, FolderGroupType.CueFolder, FolderGroupType.GdiFolder };
            SystemType[] systemTypes = new[] { SystemType.WiiU, SystemType.GameCube, SystemType.Wii, SystemType.Default };

            for (int i = 0; i < count; i++)
            {
                string baseName = GenerateBaseName(seed, i);
                string sourceFolder = $"/games/{baseName}";
                int childCount = (((seed + (i * 37)) & 0x7FFFFFFF) % 5) + 2; // 2..6 children
                FolderGroupType groupType = groupTypes[((seed + (i * 11)) & 0x7FFFFFFF) % groupTypes.Length];
                SystemType systemType = systemTypes[((seed + (i * 19)) & 0x7FFFFFFF) % systemTypes.Length];

                List<SourceFile> children = new List<SourceFile>();
                for (int c = 0; c < childCount; c++)
                {
                    children.Add(new SourceFile
                    {
                        Name = baseName,
                        CleanName = baseName,
                        Status = SourceFileResult.Valid,
                        SystemType = systemType,
                    });
                }

                groups.Add(new FolderGroupInfo
                {
                    GroupType = groupType,
                    BaseName = baseName,
                    SourceFolder = sourceFolder,
                    ChildSources = children,
                    SystemType = systemType,
                });
            }

            return groups;
        }

        /// <summary>
        /// Generates a deterministic base name from a seed and index.
        /// </summary>
        private static string GenerateBaseName(int seed, int index)
        {
            string[] prefixes = { "Mario", "Zelda", "Metroid", "Kirby", "Splatoon", "Pikmin", "Xenoblade", "Donkey Kong" };
            string[] suffixes = { "HD", "Deluxe", "3D World", "Kart 8", "Bros U", "Party", "Maker", "Chronicles" };
            int pi = ((seed + (index * 13)) & 0x7FFFFFFF) % prefixes.Length;
            int si = ((seed + (index * 29)) & 0x7FFFFFFF) % suffixes.Length;
            return $"{prefixes[pi]} {suffixes[si]} {index}";
        }
    }
}
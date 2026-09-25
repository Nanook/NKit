using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using System;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests for filesystem YAML round-trip.
    /// Feature: nkds-iso-xbox-support, Property 12: Filesystem YAML Round-Trip
    ///
    /// For any valid file system tree (XDvdFs for Xbox, ISO9660 for other systems) containing
    /// files with arbitrary names, sizes, and directory structures, generating filesystem YAML
    /// from the tree and then parsing it back SHALL produce an equivalent file listing
    /// (same paths, same sizes, same offsets).
    ///
    /// **Validates: Requirements 1.7, 2.9**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class FilesystemYamlRoundTripPropertyTests
    {
        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 12: Filesystem YAML Round-Trip (NkFs Binary)
        ///
        /// For any arbitrary file system tree, building an FsYaml, converting to NkFs binary
        /// format (NkFs.FromFsYaml → ToBytes → FromBytes → ToFsYaml), and comparing the
        /// result SHALL produce an equivalent file listing with the same paths, sizes, and offsets.
        ///
        /// This tests the path used by DataStoreXboxFormatter and DataStoreIso9660Formatter
        /// when persisting filesystem descriptors to the DataStore.
        ///
        /// **Validates: Requirements 1.7, 2.9**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FilesystemYaml_NkFsBinaryRoundTrip_PreservesFileTree(
            NonNegativeInt fileCountRaw,
            NonNegativeInt dirDepthRaw,
            NonNegativeInt seed)
        {
            int fileCount = fileCountRaw.Get % 20; // 0..19 files
            int dirDepth = 1 + (dirDepthRaw.Get % 4); // 1..4 levels deep
            int s = seed.Get;

            FsYaml original = BuildArbitraryFileTree(fileCount, dirDepth, s);

            // Round-trip through NkFs binary format
            NkFs nkFs = NkFs.FromFsYaml(original);
            byte[] bytes = nkFs.ToBytes();
            NkFs parsed = NkFs.FromBytes(bytes);
            FsYaml roundTripped = parsed.ToFsYaml();

            return FileTreesEquivalent(original, roundTripped);
        }

        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 12: Filesystem YAML Round-Trip (YAML Text)
        ///
        /// For any arbitrary file system tree, serializing to YAML text and parsing back
        /// SHALL produce an equivalent file listing with the same paths, sizes, and offsets.
        ///
        /// This tests the YAML serialization path used by BuildFileSystemYaml in both
        /// DataStoreXboxFormatter (XDvdFs trees) and DataStoreIso9660Formatter (ISO9660 trees).
        ///
        /// **Validates: Requirements 1.7, 2.9**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FilesystemYaml_YamlTextRoundTrip_PreservesFileTree(
            NonNegativeInt fileCountRaw,
            NonNegativeInt dirDepthRaw,
            NonNegativeInt seed)
        {
            int fileCount = fileCountRaw.Get % 20; // 0..19 files
            int dirDepth = 1 + (dirDepthRaw.Get % 4); // 1..4 levels deep
            int s = seed.Get;

            FsYaml original = BuildArbitraryFileTree(fileCount, dirDepth, s);

            // Round-trip through YAML text format
            string yamlText = original.ToYaml();
            FsYaml roundTripped = FsYaml.FromYaml(yamlText);

            return FileTreesEquivalent(original, roundTripped);
        }

        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 12: Filesystem YAML Round-Trip (Xbox-style XDvdFs)
        ///
        /// Generates file trees resembling Xbox XDvdFs structures (flat game data files with
        /// typical Xbox naming patterns) and verifies the NkFs binary round-trip preserves
        /// all file entries.
        ///
        /// **Validates: Requirements 1.7**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FilesystemYaml_XboxStyleTree_NkFsRoundTrip(
            NonNegativeInt fileCountRaw,
            NonNegativeInt seed)
        {
            int fileCount = 1 + (fileCountRaw.Get % 15); // 1..15 files
            int s = seed.Get;

            FsYaml original = BuildXboxStyleTree(fileCount, s);

            NkFs nkFs = NkFs.FromFsYaml(original);
            byte[] bytes = nkFs.ToBytes();
            NkFs parsed = NkFs.FromBytes(bytes);
            FsYaml roundTripped = parsed.ToFsYaml();

            return FileTreesEquivalent(original, roundTripped);
        }

        /// <summary>
        /// Feature: nkds-iso-xbox-support, Property 12: Filesystem YAML Round-Trip (ISO9660-style)
        ///
        /// Generates file trees resembling ISO9660 structures (8.3 filenames, deeper directory
        /// hierarchies typical of PS1/PS2/Dreamcast games) and verifies the NkFs binary
        /// round-trip preserves all file entries.
        ///
        /// **Validates: Requirements 2.9**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FilesystemYaml_Iso9660StyleTree_NkFsRoundTrip(
            NonNegativeInt fileCountRaw,
            NonNegativeInt dirCountRaw,
            NonNegativeInt seed)
        {
            int fileCount = 1 + (fileCountRaw.Get % 15); // 1..15 files
            int dirCount = 1 + (dirCountRaw.Get % 5); // 1..5 directories
            int s = seed.Get;

            FsYaml original = BuildIso9660StyleTree(fileCount, dirCount, s);

            NkFs nkFs = NkFs.FromFsYaml(original);
            byte[] bytes = nkFs.ToBytes();
            NkFs parsed = NkFs.FromBytes(bytes);
            FsYaml roundTripped = parsed.ToFsYaml();

            return FileTreesEquivalent(original, roundTripped);
        }

        #region Tree Builders

        /// <summary>
        /// Builds an arbitrary file tree with nested directories and files.
        /// Exercises various filename patterns, sizes, and offsets.
        /// </summary>
        private static FsYaml BuildArbitraryFileTree(int fileCount, int maxDepth, int seed)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);

            if (fileCount == 0)
                return yaml;

            // Create some directories at various depths
            List<FsYamlNode> directories = new List<FsYamlNode> { root };
            int dirSeed = seed;
            for (int depth = 0; depth < maxDepth && directories.Count < fileCount + 3; depth++)
            {
                int parentIdx = Math.Abs(dirSeed) % directories.Count;
                string dirName = GenerateSafeDirectoryName(dirSeed, depth);
                FsYamlNode dir = directories[parentIdx].AddDirectory(dirName);
                directories.Add(dir);
                dirSeed = (dirSeed * 31) + 7;
            }

            // Distribute files across directories
            for (int i = 0; i < fileCount; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 13));
                int dirIdx = hash % directories.Count;
                string fileName = GenerateSafeFileName(seed, i);
                long offset = (long)(hash & 0x7FFFFFFF) * 0x800;
                long size = (long)(((hash * 3) + (i * 997)) & 0x7FFFFFFF);
                ulong xxhash = (ulong)(uint)(((hash * 5) + (i * 333)) & 0x7FFFFFFF);
                uint crc = (uint)(((hash * 7) + (i * 555)) & 0x7FFFFFFF);

                directories[dirIdx].AddFile(fileName, offset, size, xxhash, crc);
            }

            return yaml;
        }

        /// <summary>
        /// Builds a file tree resembling Xbox XDvdFs structure:
        /// relatively flat with game data files and a few subdirectories.
        /// </summary>
        private static FsYaml BuildXboxStyleTree(int fileCount, int seed)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);

            // Xbox games typically have a few top-level directories
            string[] xboxDirs = ["data", "media", "update", "content"];
            List<FsYamlNode> dirs = new List<FsYamlNode> { root };
            int dirCount = Math.Min(1 + (Math.Abs(seed) % 3), xboxDirs.Length);
            for (int i = 0; i < dirCount; i++)
            {
                dirs.Add(root.AddDirectory(xboxDirs[i]));
            }

            string[] xboxExtensions = [".xbe", ".xex", ".dat", ".bin", ".pak", ".wad", ".bik"];

            for (int i = 0; i < fileCount; i++)
            {
                int hash = Math.Abs((seed * 41) + (i * 19));
                int dirIdx = hash % dirs.Count;
                string ext = xboxExtensions[hash % xboxExtensions.Length];
                string fileName = $"file{i:D4}{ext}";
                long offset = (long)(hash & 0x7FFFFFFF) * 0x800;
                long size = (long)(((hash * 3) + (i * 1024)) & 0x7FFFFFFF);
                ulong xxhash = (ulong)(uint)(((hash * 5) + (i * 256)) & 0x7FFFFFFF);
                uint crc = (uint)(((hash * 7) + (i * 512)) & 0x7FFFFFFF);

                dirs[dirIdx].AddFile(fileName, offset, size, xxhash, crc);
            }

            return yaml;
        }

        /// <summary>
        /// Builds a file tree resembling ISO9660 structure:
        /// deeper directory hierarchies with 8.3-style filenames typical of PS1/PS2 games.
        /// </summary>
        private static FsYaml BuildIso9660StyleTree(int fileCount, int dirCount, int seed)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);

            // ISO9660 games often have deeper directory structures
            string[] isoDirs = ["SYSTEM", "DATA", "MOVIE", "SOUND", "BGM", "SE", "STAGE", "CHAR"];
            List<FsYamlNode> dirs = new List<FsYamlNode> { root };

            int actualDirCount = Math.Min(dirCount, isoDirs.Length);
            FsYamlNode lastDir = root;
            for (int i = 0; i < actualDirCount; i++)
            {
                int hash = Math.Abs((seed * 23) + (i * 11));
                // Sometimes nest directories, sometimes add at root
                FsYamlNode parent = (hash % 3 == 0 && lastDir != root) ? lastDir! : root;
                FsYamlNode dir = parent.AddDirectory(isoDirs[i]);
                dirs.Add(dir);
                lastDir = dir;
            }

            string[] isoExtensions = [".DAT", ".BIN", ".STR", ".XA", ".TIM", ".VAB", ".SEQ", ".BS"];

            for (int i = 0; i < fileCount; i++)
            {
                int hash = Math.Abs((seed * 43) + (i * 17));
                int dirIdx = hash % dirs.Count;
                string ext = isoExtensions[hash % isoExtensions.Length];
                // ISO9660 Level 1: 8.3 filenames
                string fileName = $"F{i:D7}{ext}";
                long offset = (long)(hash & 0x7FFFFFFF) * 0x800;
                long size = (long)(((hash * 3) + (i * 2048)) & 0x7FFFFFFF);
                ulong xxhash = (ulong)(uint)(((hash * 5) + (i * 128)) & 0x7FFFFFFF);
                uint crc = (uint)(((hash * 7) + (i * 64)) & 0x7FFFFFFF);

                dirs[dirIdx].AddFile(fileName, offset, size, xxhash, crc);
            }

            return yaml;
        }

        #endregion

        #region Name Generators

        private static string GenerateSafeDirectoryName(int seed, int index)
        {
            string[] patterns = ["dir", "subdir", "folder", "data", "content", "assets", "res"];
            int patternIdx = Math.Abs(seed + index) % patterns.Length;
            return $"{patterns[patternIdx]}{index}";
        }

        private static string GenerateSafeFileName(int seed, int index)
        {
            // Generate filenames that exercise various patterns but remain safe for round-trip
            string[] patterns =
            [
                "file{0}.dat",
                "game{0}.bin",
                "track{0}.raw",
                "data_{0}.iso",
                "content{0}.pak",
                "movie{0}.str",
                "sound{0}.xa",
                "stage{0}.map"
            ];
            int patternIdx = Math.Abs(seed + index) % patterns.Length;
            return string.Format(patterns[patternIdx], index);
        }

        #endregion

        #region Comparison Helpers

        /// <summary>
        /// Compares two FsYaml objects for equivalence: same file paths, sizes, and offsets.
        /// </summary>
        private static bool FileTreesEquivalent(FsYaml expected, FsYaml actual)
        {
            // Compare filesystem roots
            if (expected.FileSystems.Count != actual.FileSystems.Count)
                return false;

            for (int i = 0; i < expected.FileSystems.Count; i++)
            {
                if (!NodesEquivalent(expected.FileSystems[i], actual.FileSystems[i]))
                    return false;
            }

            // Compare ifs entries
            if (expected.ImageFileSystems.Count != actual.ImageFileSystems.Count)
                return false;

            for (int i = 0; i < expected.ImageFileSystems.Count; i++)
            {
                if (expected.ImageFileSystems[i].FileName != actual.ImageFileSystems[i].FileName ||
                    expected.ImageFileSystems[i].ImageId != actual.ImageFileSystems[i].ImageId ||
                    expected.ImageFileSystems[i].Size != actual.ImageFileSystems[i].Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively compares two FsYamlNode trees for equivalence.
        /// </summary>
        private static bool NodesEquivalent(FsYamlNode expected, FsYamlNode actual)
        {
            if (expected.Name != actual.Name)
                return false;

            if (expected.IsFile != actual.IsFile)
                return false;

            if (expected.IsFile)
            {
                return expected.Offset == actual.Offset &&
                       expected.Size == actual.Size &&
                       expected.XxHash64 == actual.XxHash64 &&
                       expected.Crc32 == actual.Crc32;
            }

            // Directory comparison
            if (expected.Children == null && actual.Children == null)
                return true;
            if (expected.Children == null || actual.Children == null)
                return false;
            if (expected.Children.Count != actual.Children.Count)
                return false;

            for (int i = 0; i < expected.Children.Count; i++)
            {
                if (!NodesEquivalent(expected.Children[i], actual.Children[i]))
                    return false;
            }

            return true;
        }

        #endregion
    }
}
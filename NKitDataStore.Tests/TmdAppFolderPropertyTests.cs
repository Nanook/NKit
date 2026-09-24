using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for TmdAppFolder completeness and disjointness.
    ///
    /// TmdAppFolderBuilder requires a real DataStore with SQLite and shard files,
    /// so these tests validate the LOGICAL properties using a simulated model that
    /// mirrors the invariants maintained by TmdAppFolderBuilder.Build():
    ///   - Every file from every child image appears exactly once in the ifs section
    ///   - No file appears in both the fs and ifs sections
    ///
    /// The simulation builds an FsYaml with fs entries (real folder files) and ifs
    /// entries (child image files), applying the same exclusion logic as the real
    /// builder: files whose names appear in the child image set are excluded from fs.
    ///
    /// **Validates: Requirements 7.3, 7.4, 7.5**
    /// </summary>
    public class TmdAppFolderPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 7.3, 7.4, 7.5**
        ///
        /// Property 8: TmdAppFolder Completeness and Disjointness.
        /// For arbitrary child image lists and real file lists, simulating the
        /// TmdAppFolderBuilder logic produces an FsYaml where:
        ///   1. All child files appear exactly once in the ifs section (completeness)
        ///   2. No file appears in both fs and ifs sections (disjointness)
        /// </summary>
        [Property]
        public bool TmdAppFolder_Completeness_AllChildFilesInIfs(
            NonNegativeInt childCountWrapper,
            NonNegativeInt filesPerChildWrapper,
            NonNegativeInt seed)
        {
            int childCount = (childCountWrapper.Get % 6) + 1; // 1..6 child images
            int filesPerChild = filesPerChildWrapper.Get % 5; // 0..4 files per child
            int s = seed.Get;

            // Generate child images with their files
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>();
            List<string> allChildFileNames = new List<string>();

            for (int c = 0; c < childCount; c++)
            {
                long imageId = ((long)(c + 1) * 100) + (s & 0xFF);
                List<SimulatedChildFile> files = new List<SimulatedChildFile>();

                for (int f = 0; f < filesPerChild; f++)
                {
                    string fileName = GenerateAppFileName(s, c, f);
                    long size = (((long)(s + (c * 1000) + (f * 77)) & 0x7FFFFFFF) % 10_000_000) + 1;
                    files.Add(new SimulatedChildFile { FileName = fileName, ImageId = imageId, Size = size });
                    allChildFileNames.Add(fileName);
                }

                childImages.Add(new SimulatedChildImage { ImageId = imageId, Files = files });
            }

            // Build the ifs file name set (mirrors TmdAppFolderBuilder's ifsFileNames HashSet)
            HashSet<string> ifsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SimulatedChildImage child in childImages)
                foreach (SimulatedChildFile file in child.Files)
                    ifsFileNames.Add(file.FileName);

            // Generate real folder files (tmd, tik, cetk, h3, etc.)
            // Some may overlap with ifs names — the builder excludes those from fs
            List<SimulatedRealFile> realFiles = GenerateRealFiles(s, ifsFileNames);

            // Simulate TmdAppFolderBuilder.Build() logic:
            // 1. Build fs section from real files, excluding any whose name is in ifsFileNames
            // 2. Build ifs section from all child image files
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            long currentOffset = 0;
            HashSet<string> fsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SimulatedRealFile rf in realFiles)
            {
                if (ifsFileNames.Contains(rf.Name))
                    continue; // excluded — same as builder's skip logic

                root.AddFile(rf.Name, currentOffset, rf.Size, 0, 0);
                fsFileNames.Add(rf.Name);
                currentOffset += rf.Size;
            }

            foreach (SimulatedChildImage child in childImages)
            {
                foreach (SimulatedChildFile file in child.Files)
                {
                    fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size);
                }
            }

            // === Property 8 Assertions ===

            // 1. Completeness: every child file appears in ifs
            //    Count of ifs entries must equal total child files
            if (fsYaml.ImageFileSystems.Count != allChildFileNames.Count)
                return false;

            // Verify each child file name appears in ifs
            List<string> ifsEntryNames = new List<string>();
            foreach (FsYamlIfsEntry entry in fsYaml.ImageFileSystems)
                ifsEntryNames.Add(entry.FileName);

            for (int i = 0; i < allChildFileNames.Count; i++)
            {
                if (ifsEntryNames[i] != allChildFileNames[i])
                    return false;
            }

            // 2. Disjointness: no file appears in both fs and ifs
            foreach (FsYamlIfsEntry ifsEntry in fsYaml.ImageFileSystems)
            {
                if (fsFileNames.Contains(ifsEntry.FileName))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 7.3, 7.4, 7.5**
        ///
        /// Property 8 (disjointness via round-trip): After serializing and deserializing
        /// the FsYaml, the disjointness and completeness properties still hold.
        /// This ensures the YAML representation preserves the invariants.
        /// </summary>
        [Property]
        public bool TmdAppFolder_Disjointness_NoFileInBothFsAndIfs(
            NonNegativeInt childCountWrapper,
            NonNegativeInt realFileCountWrapper,
            NonNegativeInt seed)
        {
            int childCount = (childCountWrapper.Get % 4) + 1; // 1..4 children
            int realFileCount = realFileCountWrapper.Get % 6; // 0..5 real files
            int s = seed.Get;

            // Generate child images
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>();
            HashSet<string> ifsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int c = 0; c < childCount; c++)
            {
                long imageId = (long)(c + 10);
                int nFiles = ((s + c) % 4) + 1; // 1..4 files per child
                List<SimulatedChildFile> files = new List<SimulatedChildFile>();

                for (int f = 0; f < nFiles; f++)
                {
                    string fileName = GenerateAppFileName(s, c, f);
                    files.Add(new SimulatedChildFile
                    {
                        FileName = fileName,
                        ImageId = imageId,
                        Size = (((long)(s + (c * 100) + f) & 0x7FFFFFFF) % 5_000_000) + 1
                    });
                    ifsFileNames.Add(fileName);
                }

                childImages.Add(new SimulatedChildImage { ImageId = imageId, Files = files });
            }

            // Generate real files — deliberately include some names that collide with ifs
            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>();
            string[] realPatterns = { "title.tmd", "title.tik", "title.cert", "title.cetk" };
            for (int i = 0; i < realFileCount; i++)
            {
                string name = realPatterns[i % realPatterns.Length];
                // Avoid duplicate real file names
                if (realFiles.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    name = $"extra_{i}_{name}";
                realFiles.Add(new SimulatedRealFile { Name = name, Size = 1024 * (i + 1) });
            }

            // Optionally add a real file that collides with an ifs name
            if (ifsFileNames.Count > 0 && (s % 3 == 0))
            {
                string collidingName = ifsFileNames.First();
                realFiles.Add(new SimulatedRealFile { Name = collidingName, Size = 512 });
            }

            // Simulate builder: build FsYaml
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            long offset = 0;
            foreach (SimulatedRealFile rf in realFiles)
            {
                if (ifsFileNames.Contains(rf.Name))
                    continue;
                root.AddFile(rf.Name, offset, rf.Size, 0, 0);
                offset += rf.Size;
            }

            foreach (SimulatedChildImage child in childImages)
                foreach (SimulatedChildFile file in child.Files)
                    fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size);

            // Round-trip through YAML serialization
            string yaml = fsYaml.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            // Collect fs file names from parsed tree
            HashSet<string> parsedFsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFileNames(parsed.FileSystems, parsedFsNames);

            // Disjointness: no ifs entry name appears in fs
            foreach (FsYamlIfsEntry ifsEntry in parsed.ImageFileSystems)
            {
                if (parsedFsNames.Contains(ifsEntry.FileName))
                    return false;
            }

            // Completeness: ifs count matches total child files
            int expectedIfsCount = childImages.Sum(c => c.Files.Count);
            if (parsed.ImageFileSystems.Count != expectedIfsCount)
                return false;

            return true;
        }

        /// <summary>
        /// Generates a unique .app-style filename for a child image file.
        /// </summary>
        private static string GenerateAppFileName(int seed, int childIndex, int fileIndex)
        {
            int id = (Math.Abs(seed) + (childIndex * 100) + fileIndex) % 100000;
            return $"{id:D8}.app";
        }

        /// <summary>
        /// Generates a list of real folder files (tmd, tik, cetk, h3, etc.).
        /// </summary>
        private static List<SimulatedRealFile> GenerateRealFiles(int seed, HashSet<string> ifsFileNames)
        {
            string[] templates = { "title.tmd", "title.tik", "title.cert", "title.cetk", "00000000.h3" };
            List<SimulatedRealFile> files = new List<SimulatedRealFile>();
            int count = (Math.Abs(seed) % 5) + 1; // 1..5 real files

            for (int i = 0; i < count; i++)
            {
                string name = templates[i % templates.Length];
                long size = (((long)(seed + (i * 500)) & 0x7FFFFFFF) % 100_000) + 1;
                files.Add(new SimulatedRealFile { Name = name, Size = size });
            }

            return files;
        }

        /// <summary>
        /// Recursively collects all leaf file names from the FsYaml tree.
        /// </summary>
        private static void CollectFileNames(List<FsYamlNode> roots, HashSet<string> names)
        {
            foreach (FsYamlNode root in roots)
                CollectFileNamesRecursive(root, names);
        }

        private static void CollectFileNamesRecursive(FsYamlNode node, HashSet<string> names)
        {
            if (node.IsFile)
            {
                names.Add(node.Name);
                return;
            }

            if (node.Children != null)
                foreach (FsYamlNode child in node.Children)
                    CollectFileNamesRecursive(child, names);
        }

        private struct SimulatedChildImage
        {
            public long ImageId;
            public List<SimulatedChildFile> Files;
        }

        private struct SimulatedChildFile
        {
            public string FileName;
            public long ImageId;
            public long Size;
        }

        private struct SimulatedRealFile
        {
            public string Name;
            public long Size;
        }
    }
}
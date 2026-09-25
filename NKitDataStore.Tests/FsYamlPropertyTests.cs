using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for FsYaml serialization round-trip.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.5, 4.1** (Property 3: FsYaml Full Round-Trip)
    /// </summary>
    public class FsYamlPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 3.1, 3.2, 3.5, 4.1**
        ///
        /// Property 3: FsYaml Full Round-Trip.
        /// For any valid FsYaml object Y with arbitrary combinations of fs and ifs entries,
        /// FromYaml(ToYaml(Y)) produces an FsYaml object with equivalent FileSystems and
        /// ImageFileSystems. The output starts with "version: 1.0".
        /// </summary>
        [Property]
        public bool FsYaml_FullRoundTrip(
            NonNegativeInt fileCount,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            // Use seed to deterministically build varied FsYaml objects
            int nFiles = fileCount.Get % 6;   // 0..5 files
            int nIfs = ifsCount.Get % 6;      // 0..5 ifs entries
            int s = seed.Get;

            FsYaml original = BuildFsYaml(nFiles, nIfs, s);

            string yamlText = original.ToYaml();

            // Output must start with version: 1.0
            if (!yamlText.StartsWith("version: 1.0"))
                return false;

            FsYaml roundTripped = FsYaml.FromYaml(yamlText);

            // Compare ifs entries
            if (!IfsEntriesEquivalent(original.ImageFileSystems, roundTripped.ImageFileSystems))
                return false;

            // Compare fs entries
            if (!FileSystemsEquivalent(original.FileSystems, roundTripped.FileSystems))
                return false;

            return true;
        }

        /// <summary>
        /// Builds a FsYaml object with the given number of fs files and ifs entries,
        /// using the seed to vary the generated values.
        /// </summary>
        private static FsYaml BuildFsYaml(int nFiles, int nIfs, int seed)
        {
            FsYaml yaml = new FsYaml();

            // Always add the "." root — ToYaml() always emits "fs:" and FromYaml()
            // always creates a "." root when it encounters "fs:", so the round-trip
            // always produces at least one filesystem root.
            FsYamlNode root = yaml.AddFileSystem(".", 0);
            for (int i = 0; i < nFiles; i++)
            {
                string name = GenerateFileName(seed, i);
                long offset = (long)(seed + (i * 1000)) & 0x7FFFFFFF;
                long size = (long)((seed + (i * 777)) & 0x7FFFFFFF);
                ulong xxhash = (ulong)(uint)((seed + (i * 333)) & 0x7FFFFFFF);
                uint crc = (uint)((seed + (i * 555)) & 0x7FFFFFFF);
                root.AddFile(name, offset, size, xxhash, crc);
            }

            for (int i = 0; i < nIfs; i++)
            {
                string name = GenerateIfsFileName(seed, i);
                long imageId = (long)((seed + (i * 111)) & 0x7FFFFFFF);
                long size = (long)((seed + (i * 222)) & 0x7FFFFFFF);
                yaml.AddIfsEntry(name, imageId, size);
            }

            return yaml;
        }

        /// <summary>
        /// Generates a safe filename for fs entries. Uses a mix of alpha, numeric,
        /// and special characters that exercise the quoting logic.
        /// </summary>
        private static string GenerateFileName(int seed, int index)
        {
            // Rotate through different filename patterns to exercise quoting
            string[] patterns =
            [
                "file{0}.dat",
                "my-file{0}",
                "data_{0}.bin",
                "{0}starts_with_digit.txt",   // triggers quoting (starts with digit)
                "has space {0}.app",
                "special[{0}].h3",            // triggers quoting (brackets)
                "colon:{0}.tmd",              // triggers quoting (colon)
                "quoted\"{0}\".tik",          // triggers quoting (double quote)
            ];
            int patternIdx = (Math.Abs(seed) + index) % patterns.Length;
            return string.Format(patterns[patternIdx], index);
        }

        /// <summary>
        /// Generates a safe filename for ifs entries.
        /// </summary>
        private static string GenerateIfsFileName(int seed, int index)
        {
            string[] patterns =
            [
                "app{0}.bin",
                "{0:D8}.app",                 // triggers quoting (starts with digit)
                "content[{0}].app",           // triggers quoting (brackets)
                "title.tmd{0}",
            ];
            int patternIdx = (Math.Abs(seed) + index) % patterns.Length;
            return string.Format(patterns[patternIdx], index);
        }

        /// <summary>
        /// **Validates: Requirements 3.3, 12.2**
        ///
        /// Property 4: FsYaml Backward Compatibility.
        /// For any valid YAML string containing only version: 1.0 and fs: sections (no ifs:),
        /// FromYaml() produces a valid FsYaml with empty ImageFileSystems and correctly parsed FileSystems.
        /// </summary>
        [Property]
        public bool FsYaml_BackwardCompatibility_NoIfsSection(
            NonNegativeInt fileCount,
            NonNegativeInt seed)
        {
            // Build an FsYaml with only fs entries (no ifs entries)
            int nFiles = fileCount.Get % 6; // 0..5 files
            int s = seed.Get;

            FsYaml original = BuildFsYaml(nFiles, nIfs: 0, s);

            string yamlText = original.ToYaml();

            // Verify the YAML does not contain an ifs: section
            if (yamlText.Contains("\nifs:"))
                return false;

            // Parse it back
            FsYaml parsed = FsYaml.FromYaml(yamlText);

            // ImageFileSystems must be empty (backward compatibility)
            if (parsed.ImageFileSystems.Count != 0)
                return false;

            // FileSystems must be correctly parsed and equivalent to the original
            if (!FileSystemsEquivalent(original.FileSystems, parsed.FileSystems))
                return false;

            return true;
        }

        private static bool IfsEntriesEquivalent(List<FsYamlIfsEntry> expected, List<FsYamlIfsEntry> actual)
        {
            if (expected.Count != actual.Count)
                return false;

            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i].FileName != actual[i].FileName ||
                    expected[i].ImageId != actual[i].ImageId ||
                    expected[i].Size != actual[i].Size)
                    return false;
            }

            return true;
        }

        private static bool FileSystemsEquivalent(List<FsYamlNode> expected, List<FsYamlNode> actual)
        {
            if (expected.Count != actual.Count)
                return false;

            for (int i = 0; i < expected.Count; i++)
            {
                if (!NodesEquivalent(expected[i], actual[i]))
                    return false;
            }

            return true;
        }

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
    }
}
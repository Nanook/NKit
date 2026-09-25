using Nanook.NKit.Iso.Iso9660;
using System.Reflection;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Verifies that the excludeUdfFs workaround has been completely removed
    /// from Playstation3FixData, FstIsoUdf, and fix_ps3.yaml.
    /// Validates: Requirements 7.1, 7.2, 7.3, 7.5
    /// </summary>
    public class Ps3WorkaroundRemovalVerificationTests
    {
        /// <summary>
        /// Locates the fix_ps3.yaml file relative to the NKit project source.
        /// </summary>
        private static string GetFixPs3YamlPath()
        {
            // Navigate from test assembly location up to the solution root, then into NKit/defaults/fix
            string assemblyDir = Path.GetDirectoryName(typeof(Ps3WorkaroundRemovalVerificationTests).Assembly.Location)!;
            // Assembly is in NKitDataStore.Tests/bin/{Config}/{TFM}/ — go up 4 levels to solution root
            string solutionRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", ".."));
            string yamlPath = Path.Combine(solutionRoot, "NKit", "defaults", "fix", "fix_ps3.yaml");
            return yamlPath;
        }

        /// <summary>
        /// Test that fix_ps3.yaml does not contain an active (uncommented) 'excludeUdfFs' section.
        /// The word may appear in comments explaining the removed workaround, which is acceptable.
        /// Validates: Requirement 7.1
        /// </summary>
        [Fact]
        public void FixPs3Yaml_DoesNotContain_ExcludeUdfFs()
        {
            string yamlPath = GetFixPs3YamlPath();
            Assert.True(File.Exists(yamlPath), $"fix_ps3.yaml not found at: {yamlPath}");

            string[] lines = File.ReadAllLines(yamlPath);
            // Check that no uncommented line starts with 'excludeUdfFs' (as an active YAML key)
            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#"))
                    continue; // skip comments
                Assert.DoesNotContain("excludeUdfFs", trimmed);
            }
        }

        /// <summary>
        /// Test that Playstation3FixData class does not have a UdfImageOffsets property.
        /// Validates: Requirement 7.2
        /// </summary>
        [Fact]
        public void Playstation3FixData_DoesNotHave_UdfImageOffsetsProperty()
        {
            Type type = typeof(Playstation3FixData);

            PropertyInfo property = type.GetProperty(
                "UdfImageOffsets",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            Assert.Null(property);
        }

        /// <summary>
        /// Test that FstIsoUdf class does not have a _skipFstItems field.
        /// Validates: Requirement 7.3
        /// </summary>
        [Fact]
        public void FstIsoUdf_DoesNotHave_SkipFstItemsField()
        {
            Type type = typeof(FstIsoUdf);

            FieldInfo field = type.GetField(
                "_skipFstItems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            Assert.Null(field);
        }

        /// <summary>
        /// Test that Playstation3FixData still parses the layerbreaks section without error
        /// and returns correct layerbreak values after the workaround removal.
        /// Validates: Requirement 7.5
        /// </summary>
        [Fact]
        public void Playstation3FixData_LayerbreakParsing_StillWorksAfterRemoval()
        {
            string yamlPath = GetFixPs3YamlPath();
            Assert.True(File.Exists(yamlPath), $"fix_ps3.yaml not found at: {yamlPath}");

            Playstation3FixData fixData = new Playstation3FixData();
            fixData.Load(Nanook.NKit.SystemType.PS3, new FileInfo(yamlPath), Path.GetDirectoryName(yamlPath)!);

            // Verify YAML loaded successfully
            Assert.True(fixData.YamlLoaded);

            // Verify known layerbreak entries are parseable (from the yaml file)
            // "7b739c3e: 9930208" - Tekken Hybrid
            long? layerbreak = fixData.GetLayerbreak(0x7b739c3e);
            Assert.NotNull(layerbreak);
            Assert.Equal(9930208L, layerbreak.Value);

            // "7c4ca087: 32505856" - Biohazard 5
            long? layerbreak2 = fixData.GetLayerbreak(0x7c4ca087);
            Assert.NotNull(layerbreak2);
            Assert.Equal(32505856L, layerbreak2.Value);

            // Verify a non-existent CRC returns null
            long? noLayerbreak = fixData.GetLayerbreak(0xFFFFFFFF);
            Assert.Null(noLayerbreak);
        }

        /// <summary>
        /// Test that FstIsoUdf.processFileEntry does not contain skip logic —
        /// verified by ensuring no field or method references _skipFstItems-like patterns.
        /// Also verifies the class has no "excluded offsets" collection of any kind.
        /// Validates: Requirement 7.3
        /// </summary>
        [Fact]
        public void FstIsoUdf_HasNoExclusionFields()
        {
            Type type = typeof(FstIsoUdf);

            // Check all fields — none should be related to skipping/excluding UDF entries
            FieldInfo[] allFields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (FieldInfo field in allFields)
            {
                Assert.DoesNotContain("skip", field.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("exclude", field.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Test that SetDisc method exists but has no exclusion logic (empty body).
        /// After workaround removal, SetDisc should not populate any exclusion list.
        /// Validates: Requirement 7.2
        /// </summary>
        [Fact]
        public void Playstation3FixData_SetDisc_DoesNotPopulateExclusionList()
        {
            string yamlPath = GetFixPs3YamlPath();
            Assert.True(File.Exists(yamlPath), $"fix_ps3.yaml not found at: {yamlPath}");

            Playstation3FixData fixData = new Playstation3FixData();
            fixData.Load(Nanook.NKit.SystemType.PS3, new FileInfo(yamlPath), Path.GetDirectoryName(yamlPath)!);

            // SetDisc should not throw and should not populate any exclusion property
            fixData.SetDisc(0x7b739c3e); // known CRC from layerbreaks

            // Verify no UdfImageOffsets property exists (redundant with reflection test, but
            // exercises the SetDisc code path to ensure it doesn't crash)
            Type type = typeof(Playstation3FixData);
            PropertyInfo prop = type.GetProperty(
                "UdfImageOffsets",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(prop);
        }
    }
}
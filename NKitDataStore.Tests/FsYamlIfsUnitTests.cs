namespace NKitDataStore.Tests
{
    /// <summary>
    /// Example-based unit tests for FsYaml ifs section serialization and deserialization.
    ///
    /// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5
    /// </summary>
    public class FsYamlIfsUnitTests
    {
        #region ToYaml Tests

        [Fact]
        public void ToYaml_EmptyIfs_DoesNotEmitIfsSection()
        {
            FsYaml yaml = new FsYaml();
            yaml.AddFileSystem(".", 0);

            string output = yaml.ToYaml();

            Assert.StartsWith("version: 1.0", output);
            Assert.Contains("fs:", output);
            Assert.DoesNotContain("ifs:", output);
        }

        [Fact]
        public void ToYaml_SingleIfsEntry_EmitsCorrectFormat()
        {
            FsYaml yaml = new FsYaml();
            yaml.AddFileSystem(".", 0);
            yaml.AddIfsEntry("00000001.app", 42, 1048576);

            string output = yaml.ToYaml();

            Assert.StartsWith("version: 1.0", output);
            Assert.Contains("ifs:", output);
            // Filename starts with digit so it gets quoted
            Assert.Contains("\"00000001.app\": [42, 1048576]", output);
        }

        [Fact]
        public void ToYaml_MultipleIfsEntries_EmitsAllEntries()
        {
            FsYaml yaml = new FsYaml();
            yaml.AddFileSystem(".", 0);
            yaml.AddIfsEntry("00000001.app", 42, 1000000);
            yaml.AddIfsEntry("00000002.app", 42, 2000000);
            yaml.AddIfsEntry("00000003.app", 43, 500000);

            string output = yaml.ToYaml();

            Assert.Contains("ifs:", output);
            Assert.Contains("\"00000001.app\": [42, 1000000]", output);
            Assert.Contains("\"00000002.app\": [42, 2000000]", output);
            Assert.Contains("\"00000003.app\": [43, 500000]", output);
        }

        [Fact]
        public void ToYaml_IfsWithUnquotedFilename_EmitsWithoutQuotes()
        {
            FsYaml yaml = new FsYaml();
            yaml.AddFileSystem(".", 0);
            yaml.AddIfsEntry("content.app", 10, 500);

            string output = yaml.ToYaml();

            Assert.Contains("  content.app: [10, 500]", output);
        }

        [Fact]
        public void ToYaml_IfsAfterFsSection()
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);
            root.AddFile("title.tmd", 0, 1024, 12345, 67890);
            yaml.AddIfsEntry("00000001.app", 42, 1048576);

            string output = yaml.ToYaml();

            int fsIndex = output.IndexOf("fs:");
            int ifsIndex = output.IndexOf("ifs:");
            Assert.True(fsIndex < ifsIndex, "ifs: section should appear after fs: section");
        }

        [Fact]
        public void ToYaml_OutputStartsWithVersion()
        {
            FsYaml yaml = new FsYaml();
            yaml.AddFileSystem(".", 0);
            yaml.AddIfsEntry("file.app", 1, 100);

            string output = yaml.ToYaml();

            Assert.StartsWith("version: 1.0", output);
        }

        #endregion

        #region FromYaml Malformed ifs Tests

        [Fact]
        public void FromYaml_IfsEntryWithOneValue_SkipsEntry()
        {
            string input = "version: 1.0\nfs:\nifs:\n  badfile.app: [42]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
        }

        [Fact]
        public void FromYaml_IfsEntryWithThreeValues_SkipsEntry()
        {
            string input = "version: 1.0\nfs:\nifs:\n  badfile.app: [42, 100, 999]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
        }

        [Fact]
        public void FromYaml_IfsEntryWithNonNumericImageId_SkipsEntry()
        {
            string input = "version: 1.0\nfs:\nifs:\n  badfile.app: [abc, 100]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
        }

        [Fact]
        public void FromYaml_IfsEntryWithNonNumericSize_SkipsEntry()
        {
            string input = "version: 1.0\nfs:\nifs:\n  badfile.app: [42, xyz]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
        }

        [Fact]
        public void FromYaml_MixOfValidAndMalformedIfsEntries_ParsesValidOnly()
        {
            string input =
                "version: 1.0\n" +
                "fs:\n" +
                "ifs:\n" +
                "  good.app: [10, 500]\n" +
                "  bad.app: [abc, 100]\n" +
                "  \"00000002.app\": [20, 1000]\n" +
                "  wrong.app: [1]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Equal(2, result.ImageFileSystems.Count);
            Assert.Equal("good.app", result.ImageFileSystems[0].FileName);
            Assert.Equal(10, result.ImageFileSystems[0].ImageId);
            Assert.Equal(500, result.ImageFileSystems[0].Size);
            Assert.Equal("00000002.app", result.ImageFileSystems[1].FileName);
            Assert.Equal(20, result.ImageFileSystems[1].ImageId);
            Assert.Equal(1000, result.ImageFileSystems[1].Size);
        }

        [Fact]
        public void FromYaml_IfsEntryWithNoValues_SkipsEntry()
        {
            string input = "version: 1.0\nfs:\nifs:\n  novals.app:\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
        }

        #endregion

        #region Backward Compatibility Tests

        [Fact]
        public void FromYaml_NoIfsSection_ProducesEmptyImageFileSystems()
        {
            string input =
                "version: 1.0\n" +
                "fs:\n" +
                "  title.tmd: [0, 1024, 12345, 67890]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
            Assert.Single(result.FileSystems);
        }

        [Fact]
        public void FromYaml_NoIfsSection_ParsesFsCorrectly()
        {
            string input =
                "version: 1.0\n" +
                "fs:\n" +
                "  title.tmd: [0, 1024, 12345, 67890]\n" +
                "  title.tik: [1024, 512, 11111, 22222]\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.ImageFileSystems);
            Assert.Single(result.FileSystems);
            FsYamlNode root = result.FileSystems[0];
            Assert.Equal(".", root.Name);
            Assert.NotNull(root.Children);
            Assert.Equal(2, root.Children!.Count);
            Assert.Equal("title.tmd", root.Children[0].Name);
            Assert.Equal(1024L, root.Children[0].Size);
            Assert.Equal("title.tik", root.Children[1].Name);
            Assert.Equal(512L, root.Children[1].Size);
        }

        [Fact]
        public void FromYaml_EmptyString_ProducesEmptyFsYaml()
        {
            FsYaml result = FsYaml.FromYaml("");

            Assert.Empty(result.FileSystems);
            Assert.Empty(result.ImageFileSystems);
        }

        [Fact]
        public void FromYaml_VersionOnlyNoFsNoIfs_ProducesEmptyLists()
        {
            string input = "version: 1.0\n";

            FsYaml result = FsYaml.FromYaml(input);

            Assert.Empty(result.FileSystems);
            Assert.Empty(result.ImageFileSystems);
        }

        #endregion

        #region Round-Trip Tests

        [Fact]
        public void RoundTrip_FsAndIfs_PreservesAllEntries()
        {
            FsYaml original = new FsYaml();
            FsYamlNode root = original.AddFileSystem(".", 0);
            root.AddFile("title.tmd", 0, 1024, 12345, 67890);
            root.AddFile("title.tik", 1024, 512, 11111, 22222);
            original.AddIfsEntry("00000001.app", 42, 1048576);
            original.AddIfsEntry("00000002.app", 43, 2097152);

            string yamlText = original.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yamlText);

            // Verify fs entries
            Assert.Single(parsed.FileSystems);
            Assert.Equal(2, parsed.FileSystems[0].Children!.Count);
            Assert.Equal("title.tmd", parsed.FileSystems[0].Children![0].Name);
            Assert.Equal("title.tik", parsed.FileSystems[0].Children![1].Name);

            // Verify ifs entries
            Assert.Equal(2, parsed.ImageFileSystems.Count);
            Assert.Equal("00000001.app", parsed.ImageFileSystems[0].FileName);
            Assert.Equal(42, parsed.ImageFileSystems[0].ImageId);
            Assert.Equal(1048576, parsed.ImageFileSystems[0].Size);
            Assert.Equal("00000002.app", parsed.ImageFileSystems[1].FileName);
            Assert.Equal(43, parsed.ImageFileSystems[1].ImageId);
            Assert.Equal(2097152, parsed.ImageFileSystems[1].Size);
        }

        [Fact]
        public void RoundTrip_OnlyIfs_PreservesEntries()
        {
            FsYaml original = new FsYaml();
            original.AddFileSystem(".", 0);
            original.AddIfsEntry("content.app", 99, 5000);

            string yamlText = original.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yamlText);

            Assert.Single(parsed.ImageFileSystems);
            Assert.Equal("content.app", parsed.ImageFileSystems[0].FileName);
            Assert.Equal(99, parsed.ImageFileSystems[0].ImageId);
            Assert.Equal(5000, parsed.ImageFileSystems[0].Size);
        }

        #endregion
    }
}
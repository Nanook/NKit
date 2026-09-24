using Nanook.NKit.Vfs;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for CLI parameter wiring of --max-fsyaml-size.
    ///
    /// Uses the --help flag alongside mount options so that Parse() skips
    /// filesystem validation while still populating the request properties.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </summary>
    public class CliParameterWiringTests
    {
        [Fact]
        public void MaxFsYamlSize_ExplicitValue_SetsProperty()
        {
            var request = NkdsCommandLine.Parse(new[] { "mount", "--help", "--max-fsyaml-size", "200" });

            Assert.Equal(200, request.MountMaxFsYamlSizeKiB);
        }

        [Fact]
        public void MaxFsYamlSize_Omitted_DefaultsTo64k()
        {
            var request = NkdsCommandLine.Parse(new[] { "mount", "--help" });

            Assert.Equal(64, request.MountMaxFsYamlSizeKiB);
        }

        [Fact]
        public void MaxFsYamlSize_Zero_SetsValueToZero()
        {
            var request = NkdsCommandLine.Parse(new[] { "mount", "--help", "--max-fsyaml-size", "0" });

            Assert.Equal(0, request.MountMaxFsYamlSizeKiB);
        }
    }
}

using Nanook.NKit.Vfs;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for CLI parsing of the ogmr command.
    ///
    /// Uses the --help flag alongside ogmr options so that Parse() skips
    /// filesystem validation while still populating the request properties.
    ///
    /// **Validates: Requirements 1.1, 1.2, 2.1, 3.2**
    /// </summary>
    public class OgmrCliParsingTests
    {
        [Fact]
        public void Ogmr_IsRecognisedAsValidCommand()
        {
            var request = NkdsCommandLine.Parse(new[] { "ogmr", "--help" });

            Assert.Equal(NkdsCommand.Help, request.Command);
            Assert.Equal(NkdsCommand.Ogmr, request.HelpTopic);
        }

        [Fact]
        public void Ogmr_PositionalYamlPath_IsParsedCorrectly()
        {
            var request = NkdsCommandLine.Parse(new[] { "ogmr", "games.yaml", "--help" });

            Assert.Equal("games.yaml", request.OgmrYamlPath);
        }

        [Fact]
        public void Ogmr_PositionalYamlPathAndInputs_AreParsedCorrectly()
        {
            var request = NkdsCommandLine.Parse(new[] { "ogmr", "games.yaml", "input1.iso", "input2.iso", "--help" });

            Assert.Equal("games.yaml", request.OgmrYamlPath);
            Assert.Equal(new[] { "input1.iso", "input2.iso" }, request.Inputs);
        }

        [Fact]
        public void Ogmr_OptionAlternative_ParsesYamlPath()
        {
            var request = NkdsCommandLine.Parse(new[] { "ogmr", "--ogmr", "games.yaml", "--input", "input1.iso", "--help" });

            Assert.Equal("games.yaml", request.OgmrYamlPath);
            Assert.Equal(new[] { "input1.iso" }, request.Inputs);
        }

        [Fact]
        public void Ogmr_MissingYamlPath_ThrowsCommandLineException()
        {
            var ex = Assert.Throws<CommandLineException>(() =>
                NkdsCommandLine.Parse(new[] { "ogmr" }));

            Assert.Contains("OGMR YAML file path", ex.Message);
        }

        [Fact]
        public void Ogmr_DataStoreWithNkdsExtension_ThrowsCommandLineException()
        {
            var ex = Assert.Throws<CommandLineException>(() =>
                NkdsCommandLine.Parse(new[] { "ogmr", "games.yaml", "--datastore", "path.nkds" }));

            Assert.Contains("directory path", ex.Message);
        }
    }
}

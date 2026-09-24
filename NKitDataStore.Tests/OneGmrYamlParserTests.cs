using Nanook.NKit.Ogmr;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for OgmrYamlParser.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 11.1, 11.2**
    /// </summary>
    public class OgmrYamlParserTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();

        private string WriteTempYaml(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), $"1gmr_test_{Guid.NewGuid()}.yaml");
            File.WriteAllText(path, content);
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (string path in _tempFiles)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        // --- Valid YAML parsing ---

        [Fact]
        public void Parse_ValidYaml_ReturnsCorrectGameEntryList()
        {
            string yaml = @"games:
  - name: Zelda Twilight Princess
    masks:
      - '^Zelda'
      - '^Legend of Zelda'
  - name: Mario Kart Wii
    masks:
      - '^Mario Kart'
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            Assert.Equal(2, entries.Count);
            Assert.Equal("Zelda Twilight Princess", entries[0].Name);
            Assert.Equal(2, entries[0].CompiledMasks.Length);
            Assert.Equal("Mario Kart Wii", entries[1].Name);
            Assert.Equal(1, entries[1].CompiledMasks.Length);
        }

        [Fact]
        public void Parse_ValidYaml_SanitizedNameDerivedFromName()
        {
            string yaml = @"games:
  - name: '007: Quantum of Solace'
    masks:
      - '^007'
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            Assert.Single(entries);
            Assert.Equal("007: Quantum of Solace", entries[0].Name);
            Assert.Equal("007_ Quantum of Solace", entries[0].SanitizedName);
        }

        [Fact]
        public void Parse_ValidYaml_MasksCompiledCaseInsensitive()
        {
            string yaml = @"games:
  - name: Test Game
    masks:
      - '^TestPattern'
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            // Case-insensitive matching should work
            Assert.Matches(entries[0].CompiledMasks[0], "testpattern");
            Assert.Matches(entries[0].CompiledMasks[0], "TESTPATTERN");
            Assert.Matches(entries[0].CompiledMasks[0], "TestPattern");
        }

        [Fact]
        public void Parse_QuotedValues_QuotesStripped()
        {
            string yaml = @"games:
  - name: 'My Game'
    masks:
      - '^MyGame'
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            Assert.Equal("My Game", entries[0].Name);
        }

        [Fact]
        public void Parse_DoubleQuotedValues_QuotesStripped()
        {
            string yaml = @"games:
  - name: ""My Game""
    masks:
      - ""^MyGame""
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            Assert.Equal("My Game", entries[0].Name);
        }

        // --- Error cases ---

        [Fact]
        public void Parse_FileNotFound_ThrowsCommandLineException()
        {
            string nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.yaml");

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(nonExistentPath));
            Assert.Contains("not found", ex.Message);
            Assert.Contains(nonExistentPath, ex.Message);
        }

        [Fact]
        public void Parse_MissingGamesKey_ThrowsCommandLineException()
        {
            string yaml = @"something_else:
  - name: Test
    masks:
      - '^Test'
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            Assert.Contains("games:", ex.Message);
        }

        [Fact]
        public void Parse_EntryMissingName_ThrowsCommandLineException()
        {
            // When a name: field is present but empty, the parser reports the missing name
            string yaml = @"games:
  - name:
    masks:
      - '^Pattern'
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            Assert.Contains("name", ex.Message.ToLower());
        }

        [Fact]
        public void Parse_EntryWithoutNameField_ThrowsCommandLineException()
        {
            // When the name field is entirely absent, the parser cannot start an entry
            // and the games list ends up empty
            string yaml = @"games:
  - masks:
      - '^Pattern'
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            // Parser reports empty list since no entry was started
            Assert.IsType<OgmrException>(ex);
        }

        [Fact]
        public void Parse_EntryEmptyName_ThrowsCommandLineException()
        {
            string yaml = @"games:
  - name:
    masks:
      - '^Pattern'
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            Assert.Contains("name", ex.Message.ToLower());
        }

        [Fact]
        public void Parse_EntryEmptyMasks_ThrowsCommandLineException()
        {
            string yaml = @"games:
  - name: Test Game
    masks:
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            Assert.Contains("no masks", ex.Message.ToLower());
            Assert.Contains("Test Game", ex.Message);
        }

        [Fact]
        public void Parse_InvalidRegexPattern_ThrowsCommandLineException()
        {
            string yaml = @"games:
  - name: Bad Regex Game
    masks:
      - '[invalid('
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            Assert.Contains("Bad Regex Game", ex.Message);
            Assert.Contains("[invalid(", ex.Message);
            Assert.Contains("invalid regex", ex.Message.ToLower());
        }

        // --- Additional edge cases ---

        [Fact]
        public void Parse_CommentsIgnored_ParsesCorrectly()
        {
            string yaml = @"# This is a comment
games:
  # Another comment
  - name: Test Game
    masks:
      # Mask comment
      - '^Test'
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            Assert.Single(entries);
            Assert.Equal("Test Game", entries[0].Name);
        }

        [Fact]
        public void Parse_EmptyGamesList_ThrowsCommandLineException()
        {
            string yaml = @"games:
";
            string path = WriteTempYaml(yaml);

            OgmrException ex = Assert.Throws<OgmrException>(() => OgmrYamlParser.Parse(path));
            Assert.Contains("empty", ex.Message.ToLower());
        }

        [Fact]
        public void Parse_MultipleGamesMultipleMasks_AllParsedCorrectly()
        {
            string yaml = @"games:
  - name: Game One
    masks:
      - '^GameOne'
      - '^Game_One'
      - '^Game 1'
  - name: Game Two
    masks:
      - '^GameTwo'
  - name: Game Three
    masks:
      - '^GameThree'
      - '^Game3'
";
            string path = WriteTempYaml(yaml);

            List<GameEntry> entries = OgmrYamlParser.Parse(path);

            Assert.Equal(3, entries.Count);
            Assert.Equal("Game One", entries[0].Name);
            Assert.Equal(3, entries[0].CompiledMasks.Length);
            Assert.Equal("Game Two", entries[1].Name);
            Assert.Equal(1, entries[1].CompiledMasks.Length);
            Assert.Equal("Game Three", entries[2].Name);
            Assert.Equal(2, entries[2].CompiledMasks.Length);
        }
    }
}
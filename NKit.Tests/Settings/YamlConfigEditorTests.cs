using Nanook.NKit.App.Cli;
using Xunit;

namespace NKit.Tests.Settings
{
    /// <summary>Tests the comment-preserving surgical YAML editor used for config seeding + write-back.</summary>
    [Trait("Area", "Settings")]
    public class YamlConfigEditorTests
    {
        private const string Sample =
@"# header comment
task: 
recursive: y   # scan recursively

sys:
  wii:
    dat: $configPath$/dats/wii/*.zip//*.dat   # wii dats
    format: rvz:zstd:19:128k:16  #best
  ps2:
    format: cso:9:16k:4/cue:split
";

        [Fact]
        public void HasRootValue_DetectsInline_List_AndEmpty()
        {
            // Inline scalar value.
            Assert.True(new YamlConfigEditor("in: file.iso\n").HasRootValue("input", "in"));
            // YAML list block under the key (the dev-config shape).
            string list = "in:\n  - L:\\games\\a.iso\n  #- commented\n  - L:\\games\\b.iso\n";
            Assert.True(new YamlConfigEditor(list).HasRootValue("input", "in"));
            // Key present but empty (no inline value, no list items).
            Assert.False(new YamlConfigEditor("in:\ntask: scan\n").HasRootValue("input", "in"));
            // Key with only a commented-out list item = no real value.
            Assert.False(new YamlConfigEditor("in:\n  #- L:\\games\\a.iso\n").HasRootValue("input", "in"));
            // Key absent.
            Assert.False(new YamlConfigEditor("task: scan\n").HasRootValue("input", "in"));
        }

        [Fact]
        public void ReadsRootAndSystemValues()
        {
            YamlConfigEditor y = new YamlConfigEditor(Sample);
            Assert.Equal("y", y.GetRoot("recursive"));
            Assert.Equal("rvz:zstd:19:128k:16", y.GetSystem("wii", "format"));
            Assert.Equal("cso:9:16k:4/cue:split", y.GetSystem("ps2", "format"));
            Assert.Equal("$configPath$/dats/wii/*.zip//*.dat", y.GetSystem("wii", "dat"));
            Assert.Null(y.GetSystem("ps2", "dat"));   // absent
        }

        [Fact]
        public void SetSystem_ReplacesValue_PreservesComment()
        {
            YamlConfigEditor y = new YamlConfigEditor(Sample);
            y.SetSystem("wii", "format", "wbfs:y");
            string text = y.Text;

            Assert.Equal("wbfs:y", y.GetSystem("wii", "format"));
            Assert.Contains("#best", text);            // trailing comment preserved
            Assert.Contains("# header comment", text); // other content intact
            Assert.Contains("cso:9:16k:4/cue:split", text); // ps2 untouched
        }

        [Fact]
        public void SetRoot_ReplacesValue_PreservesComment()
        {
            YamlConfigEditor y = new YamlConfigEditor(Sample);
            y.SetRoot("recursive", "n");
            Assert.Equal("n", y.GetRoot("recursive"));
            Assert.Contains("# scan recursively", y.Text);
        }

        [Fact]
        public void SetSystem_InsertsMissingKey_UnderBlock()
        {
            YamlConfigEditor y = new YamlConfigEditor(Sample);
            y.SetSystem("ps2", "dat", "d.zip//*.dat");
            Assert.Equal("d.zip//*.dat", y.GetSystem("ps2", "dat"));
            // wii's dat is still its own value (didn't leak into ps2)
            Assert.Equal("$configPath$/dats/wii/*.zip//*.dat", y.GetSystem("wii", "dat"));
        }
    }
}
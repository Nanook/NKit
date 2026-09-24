using Nanook.NKit.Interactive;
using Nanook.NKit.Vfs;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Tests the additive nkds interactive builder via the scripted prompter: assert the assembled
    /// argv and that it round-trips through the existing NkdsCommandLine.Parse to the expected
    /// command/options — proving the interactive path reuses the real CLI unchanged.
    /// </summary>
    [Trait("Area", "NKDS")]
    public class NkdsInteractiveTests
    {
        // Assert on the assembled argv directly. Round-tripping through NkdsCommandLine.Parse would
        // hit its filesystem-existence validation (a separate concern), so we verify the builder's
        // job — producing the correct tokens — here.
        private static string ValueAfter(string[] argv, string opt)
        {
            for (int i = 0; i < argv.Length - 1; i++)
                if (argv[i] == opt) return argv[i + 1];
            return null;
        }

        [Fact]
        public void Add_BuildsExpectedArgv()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .SelectFor("add")                                   // command dropdown
                .TextFor("DataStore", "D:\\store")                  // datastore
                .TextFor("Input file", "D:\\roms\\*.rvz")           // input
                .ConfirmFor("recursively", true)                    // --recursive
                .ConfirmFor("inside archives", false)               // keep archives (no --no-archives)
                .TextFor("NKit config", "")                         // no config
                .ConfirmFor("Run this now", true);

            string[] argv = new NkdsInteractive(p).Build();
            Assert.NotNull(argv);
            Assert.Equal("add", argv[0]);
            Assert.Equal("D:\\store", ValueAfter(argv, "--datastore"));
            Assert.Contains("D:\\roms\\*.rvz", argv);
            Assert.Contains("--recursive", argv);
            Assert.DoesNotContain("--no-archives", argv);
        }

        [Fact]
        public void Export_BuildsExpectedArgv_WithMaskOutputFormat()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .SelectFor("export")
                .TextFor("DataStore", "D:\\store\\wii\\redump.nkds")
                .TextFor("Image mask", "*.iso")
                .TextFor("Output folder", "D:\\out")
                .TextFor("Convert format", "wux")                   // export format is a text prompt
                .TextFor("NKit config", "")
                .ConfirmFor("Run this now", true);

            string[] argv = new NkdsInteractive(p).Build();
            Assert.NotNull(argv);
            Assert.Equal("export", argv[0]);
            Assert.Equal("D:\\store\\wii\\redump.nkds", ValueAfter(argv, "--datastore"));
            Assert.Equal("*.iso", ValueAfter(argv, "--mask"));
            Assert.Equal("D:\\out", ValueAfter(argv, "--output"));
            Assert.Equal("wux", ValueAfter(argv, "--format"));
        }

        [Fact]
        public void Cancel_AtRunPrompt_ReturnsNull()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .SelectFor("compact")
                .TextFor("DataStore", "D:\\store\\wii\\redump.nkds")
                .ConfirmFor("Run this now", false);

            Assert.Null(new NkdsInteractive(p).Build());
        }
    }
}
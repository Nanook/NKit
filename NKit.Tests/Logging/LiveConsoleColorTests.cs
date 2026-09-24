using Spectre.Console;
using System.Reflection;
using Xunit;

namespace NKit.Tests.Logging
{
    /// <summary>
    /// Verifies LiveConsole.colorizeTags. Two regimes, driven by the CONSOLE verbosity:
    /// <list type="bullet">
    /// <item><b>Detail/Trace</b>: the leading [percent] and single [tag] (a pipeline stage OR a
    /// category) are wrapped in Spectre colour markup (percent = bold white; In=aqua, Out=blue,
    /// Pre=yellow, Proc=fuchsia, Core=purple, everything else gold3; green/red reserved), the tag
    /// brackets are preserved, and the message stays uncoloured.</item>
    /// <item><b>Info</b>: only the Info-summary prefixes ([Title]/[InParam]/[OutParam]) are
    /// recognised — the prefix is stripped from the display and the line coloured; every other line
    /// passes through uncoloured (raw stage tags do not reach the console at Info verbosity).</item>
    /// </list>
    /// In all cases the produced markup must PARSE (Spectre throws on malformed markup).
    /// </summary>
    [Trait("Area", "Logging")]
    public class LiveConsoleColorTests
    {
        // LiveConsole is internal to NKitApp; reach colorizeTags by name via reflection so this test
        // does not need InternalsVisibleTo.
        private static readonly MethodInfo _colorize =
            Assembly.Load("nkit")                                   // NKitApp assembly (AssemblyName=nkit)
                .GetType("Nanook.NKit.App.LiveConsole")
                .GetMethod("colorizeTags", BindingFlags.NonPublic | BindingFlags.Static);

        // colorizeTags is (string line, LogLevel level, LogLevel consoleLevel). Both level params are
        // the enum Nanook.NKit.LogLevel; build values by name via reflection so the test needs no
        // compile-time reference.
        private static readonly System.Type _levelType = _colorize.GetParameters()[1].ParameterType;
        private static object Level(string name) => System.Enum.Parse(_levelType, name);

        // Existing stage/category tag tests run at Detail verbosity (that is the only regime where
        // those tags reach the console).
        private static string Colorize(string line, string level = "Info", string consoleLevel = "Detail")
            => (string)_colorize.Invoke(null, new object[] { line, Level(level), Level(consoleLevel) });

        [Theory]
        // Pipeline stages (plain [In]/[Out]/[Pre]/[Proc#N] — no :category suffix any more).
        [InlineData("[10%] [In] Area 3 x", "bold white", "aqua")]
        [InlineData("[5%] [Out] done", "bold white", "blue")]
        [InlineData("[5%] [Pre] fst", "bold white", "yellow")]
        [InlineData("[42%] [Proc#5] worker", "bold white", "fuchsia")]
        // Cross-cutting categories — all gold.
        [InlineData("[9%] [Core] Worker 1 spawned", "bold white", "purple")]
        [InlineData("[Input] System detected", null, "gold3")]
        [InlineData("[Params] Step chain", null, "gold3")]
        [InlineData("[Config] Out path", null, "gold3")]
        [InlineData("[Results] VerifySuccess", null, "gold3")]
        [InlineData("[Progress] 812 MB/s", null, "gold3")]
        public void Colorize_ColoursTagsAndParses_AtDetail(string input, string pctColour, string tagColour)
        {
            string markup = Colorize(input);
            if (pctColour != null)
                Assert.Contains($"[{pctColour}]", markup);
            Assert.Contains($"[{tagColour}]", markup);
            // No reserved colours used.
            Assert.DoesNotContain("[green]", markup);
            Assert.DoesNotContain("[red]", markup);
            // Must be valid Spectre markup (constructor throws otherwise).
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Colorize_SectionTitleTaskSystem_NotColoured_AtDetail()
        {
            // "[Scan/GameCube]" is a SECTION TITLE, not a scope/component tag (it contains '/').
            // At Detail it must render as literal, UNCOLOURED text — not swept up as a gold scope.
            string markup = Colorize("[Scan/GameCube]  FreeLoader for GameCube (Europe)");
            Assert.Contains("[[Scan/GameCube]]", markup); // literal brackets preserved
            Assert.DoesNotContain("[gold3]", markup);     // NOT coloured as a scope
            Assert.DoesNotContain("[yellow]", markup);
            Assert.DoesNotContain("[green]", markup);
            Assert.DoesNotContain("[red]", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Colorize_MessageBracketsPreservedNotColoured_AtDetail()
        {
            // The message's own "[Other]" must survive as literal text, not be turned into a colour.
            string markup = Colorize("[10%] [In] Area 3 [Other] offset");
            Assert.Contains("Area 3 [[Other]] offset", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Colorize_TwoLeadingTags_BothColoured_MessagePlain_AtDetail()
        {
            // "[In] [CSO] info": the stage tag AND the component tag are both coloured leading tags;
            // the message stays plain. This is the "[scope] [component]" format.
            string markup = Colorize("[In] [CSO] block table @0x18");
            Assert.Contains("[aqua]", markup);   // [In] stage
            Assert.Contains("[gold3]", markup);  // [CSO] component (unknown → gold)
            Assert.Contains("block table @0x18", markup); // message, uncoloured
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Colorize_ErrorLevel_WholeLineRed()
        {
            // Error lines are rendered entirely red (tags + message), overriding per-tag colours,
            // regardless of console verbosity.
            string markup = Colorize("[In] Bad Encryption setting", "Error");
            Assert.StartsWith("[red]", markup);
            Assert.Contains("Bad Encryption setting", markup);
            Assert.DoesNotContain("[aqua]", markup); // no per-tag colouring on an error
            Assert.NotNull(new Markup(markup));
        }

        // ── Info verbosity: Info-summary prefixes ──────────────────────────────────

        [Fact]
        public void Info_TitlePrefix_TaskSystemYellow_NameGreen_CounterWhite()
        {
            // At Info verbosity the [Title] prefix is removed; "[Task/System]" is yellow, the name
            // green, and the trailing "[X/Y]" counter white. Inner bracket text preserved literally.
            string markup = Colorize("[Title] [Scan/GameCube]  FreeLoader for GameCube (Europe)  [1/1]", "Info", "Info");
            Assert.DoesNotContain("[[Title]]", markup);   // prefix stripped, not shown
            Assert.Contains("[yellow]", markup);          // [Task/System]
            Assert.Contains("[green]", markup);           // name
            Assert.Contains("[bold white]", markup);      // counter
            Assert.Contains("[[Scan/GameCube]]", markup); // inner title text preserved literally
            Assert.Contains("FreeLoader for GameCube (Europe)", markup);
            Assert.Contains("[[1/1]]", markup);           // X/Y counter preserved
            Assert.DoesNotContain("[red]", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_InParamPrefix_LabelWhite_ValueCyan()
        {
            string markup = Colorize("[InParam] InFile    : game.iso", "Info", "Info");
            Assert.DoesNotContain("[[InParam]]", markup);
            Assert.Contains("[bold white]", markup); // label (bright white)
            Assert.Contains("[cyan]", markup);       // value
            Assert.Contains("InFile", markup);
            Assert.Contains("game.iso", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_OutParamPrefix_LabelWhite_ValuePurple_ColonUncoloured()
        {
            // A NON-Verify output line: value is bright purple.
            string markup = Colorize("[OutParam] OutScan   : game.nkit.yaml", "Info", "Info");
            Assert.DoesNotContain("[[OutParam]]", markup);
            Assert.Contains("[bold white]", markup);     // label (bright white)
            Assert.Contains("[mediumpurple1]", markup);  // value (bright purple)
            Assert.Contains("OutScan", markup);
            Assert.Contains("game.nkit.yaml", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_OutParam_VerifySuccess_WordGreen_RestPurple()
        {
            string markup = Colorize("[OutParam] Verify    : VerifySuccess (InChecksums [XxHash+Crc32])", "Info", "Info");
            Assert.Contains("[bold white]", markup);       // label
            Assert.Contains("[green]VerifySuccess[/]", markup); // only the outcome word is green
            Assert.Contains("[mediumpurple1]", markup);    // the rest of the value stays purple
            Assert.DoesNotContain("[red]", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_OutParam_VerifyFailed_WordRed_RestPurple()
        {
            string markup = Colorize("[OutParam] Verify    : VerifyFailed (InChecksums [XxHash+Crc32])", "Info", "Info");
            Assert.Contains("[bold white]", markup);       // label
            Assert.Contains("[red]VerifyFailed[/]", markup); // only the outcome word is red
            Assert.Contains("[mediumpurple1]", markup);    // the rest of the value stays purple
            Assert.DoesNotContain("[green]", markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_ParamValueWithColon_SplitsOnFirstColon()
        {
            // A value containing ':' (e.g. a Windows path) must stay intact in the value span.
            string markup = Colorize(@"[InParam] OutPath   : D:\Games\out", "Info", "Info");
            Assert.Contains("[cyan]", markup);
            Assert.Contains(@"D:\Games\out", markup); // colon-containing value preserved
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_NoRecognisedPrefix_PassthroughUncoloured()
        {
            // A divider (or any non-prefixed Info line) has no recognised prefix → left as-is,
            // uncoloured. It must still parse (brackets escaped).
            string divider = "----------------------------------------";
            string markup = Colorize(divider, "Info", "Info");
            Assert.DoesNotContain("[gold3]", markup);
            Assert.DoesNotContain("[grey]", markup);
            Assert.DoesNotContain("[green]", markup);
            Assert.DoesNotContain("[red]", markup);
            Assert.Contains(divider, markup);
            Assert.NotNull(new Markup(markup));
        }

        [Fact]
        public void Info_MessageThatLooksLikePrefixButIsNot_Passthrough()
        {
            // A line whose text merely CONTAINS a token later on (not at the very start) is not a
            // prefix — passthrough, uncoloured.
            string markup = Colorize("Extracted 42 files [InParam] not a prefix", "Info", "Info");
            Assert.DoesNotContain("[grey]", markup);
            Assert.Contains("Extracted 42 files", markup);
            Assert.NotNull(new Markup(markup));
        }

        // ── Non-dynamic (redirected / non-ANSI) plain strip ────────────────────────

        // Reach the instance method stripInfoPrefixPlain(string) and the ConsoleLevel setter via
        // reflection so redirected/plain output can be asserted without a real terminal.
        private static string StripPlain(string message, string consoleLevel)
        {
            System.Type liveType = Assembly.Load("nkit").GetType("Nanook.NKit.App.LiveConsole");
            object live = System.Activator.CreateInstance(liveType, nonPublic: true);
            liveType.GetProperty("ConsoleLevel").SetValue(live, Level(consoleLevel));
            MethodInfo m = liveType.GetMethod("stripInfoPrefixPlain",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (string)m.Invoke(live, new object[] { message });
        }

        [Fact]
        public void Plain_Info_PrefixStripped_NewlinePreserved()
        {
            Assert.Equal("InFile    : game.iso\r\n",
                StripPlain("[InParam] InFile    : game.iso\r\n", "Info"));
            Assert.Equal("[Scan/GameCube]  FreeLoader  [1/1]\r\n",
                StripPlain("[Title] [Scan/GameCube]  FreeLoader  [1/1]\r\n", "Info"));
        }

        [Fact]
        public void Plain_Info_NoPrefix_Unchanged()
        {
            string line = "----------------------------------------\r\n";
            Assert.Equal(line, StripPlain(line, "Info"));
        }

        [Fact]
        public void Plain_Detail_PrefixLeftInPlace()
        {
            // At Detail verbosity the plain path leaves the prefix verbatim (mirrors the coloured path).
            string line = "[InParam] InFile    : game.iso\r\n";
            Assert.Equal(line, StripPlain(line, "Detail"));
        }

        [Fact]
        public void Detail_TitlePrefix_LeftVerbatim_NotStripped()
        {
            // At Detail verbosity the [Title] prefix is NOT stripped — the raw tagged text shows.
            string markup = Colorize("[Title] [Scan/GameCube]  FreeLoader", "Info", "Detail");
            Assert.Contains("[[Title]]", markup); // prefix left in place (as a literal tag)
            Assert.NotNull(new Markup(markup));
        }
    }
}

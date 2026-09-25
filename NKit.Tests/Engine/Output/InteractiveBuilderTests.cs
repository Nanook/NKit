using Nanook.NKit.App.Cli;
using Nanook.NKit.Interactive;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Interactive builder tests via the scripted prompter. Flow: task → input → output (unless the
    /// task has none) → editable options table. Selects are matched to table row prefixes; texts and
    /// confirms are title-keyed. Asserts the assembled argv round-trips through the parser.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class InteractiveBuilderTests
    {
        [Fact]
        public void Convert_EditFormatViaTable_BuildsExpectedArgv()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("convert")                 // task
                .TextFor("Input", "game.iso")           // input
                .TextFor("Output directory", "D:\\o")   // output (convert has one)
                                                        // table navigation: pick the "format" row, edit it, then Done
                .SelectFor("format", "format")          // pick format row (matched by prefix)
                .ConfirmFor("per system", false)        // not per-system
                .TextFor("Convert format", "cso:9:16k:4/cue:split")
                .SelectFor(">> Run")            // finish the table
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(seed: CliParser.Parse(System.Array.Empty<string>()), defaultVerb: null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            Assert.Equal("convert", c.Verb.Name);
            Assert.Equal(new[] { "game.iso" }, c.Inputs.ToArray());
            Assert.Equal("D:\\o", c.Get("output"));
            Assert.Equal("cso:9:16k:4/cue:split", c.Get("format"));
        }

        [Fact]
        public void HelpChoice_ShowsHelp_ThenReturnsToTaskList()
        {
            // Picking the 'help' entry in the task list shows help and re-prompts; then convert runs.
            ScriptedPrompter p = new ScriptedPrompter()
                .SelectFor("help")                      // first task-list pick: help
                .SelectFor("convert")                   // second task-list pick: convert
                .TextFor("Input", "game.iso")
                .TextFor("Output directory", "D:\\o")
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            Assert.Equal("convert", c.Verb.Name);
            // The Ctrl+C banner and the chosen-task echo were emitted.
            Assert.Contains(p.InfoLines, l => l.Contains("Ctrl+C"));
            Assert.Contains(p.InfoLines, l => l.Contains("Task: convert"));
        }

        [Fact]
        public void Verify_NoOutputPrompt()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("verify")
                .TextFor("Input", "game.iso")
                // no "Output directory" prompt for verify — if one were issued it'd return null anyway
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            Assert.Equal("verify", c.Verb.Name);
            Assert.Null(c.Get("output"));   // never set — verify has no output dir
        }

        [Fact]
        public void DragDropSeed_PreseedsInput_EditMaskViaTable()
        {
            ParsedCommand seed = CliParser.Parse(new[] { "game.iso" }); // no verb, one seeded input
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("extract")
                .TextFor("Output directory", "D:\\out")
                .SelectFor("mask", "mask")
                .ConfirmFor("per system", false)
                .TextFor("Extract selection", "mi:*")
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(seed, null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            Assert.Equal("extract", c.Verb.Name);
            Assert.Equal(new[] { "game.iso" }, c.Inputs.ToArray());
            Assert.Equal("mi:*", c.Get("mask"));
        }

        [Fact]
        public void ContextSensitiveHelp_FormatExamplesFilteredBySystem()
        {
            CliOption format = OptionModel.FindOption("format");

            // PS2 sees the PS2 format(s), not Wii's rvz.
            IReadOnlyList<OptionExample> ps2 = format.ExamplesFor("ps2");
            Assert.Contains(ps2, e => e.Value.StartsWith("cso:9:16k:4/cue"));
            Assert.DoesNotContain(ps2, e => e.Value.StartsWith("rvz"));

            // Wii sees rvz/wbfs, not the PS2 cso/cue.
            IReadOnlyList<OptionExample> wii = format.ExamplesFor("wii");
            Assert.Contains(wii, e => e.Value.StartsWith("rvz:zstd"));
            Assert.DoesNotContain(wii, e => e.Value.Contains("/cue"));

            // No system chosen → all examples.
            Assert.True(format.ExamplesFor(null).Count >= format.ExamplesFor("wii").Count);
        }

        [Fact]
        public void HelpPanelShown_ForRichOption_DuringEdit()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("convert")
                .TextFor("Input", "game.iso")
                .TextFor("Output directory", "D:\\o")
                // set system to PS2 first, then edit format (help should show, PS2-filtered)
                .SelectFor("system", "system")
                .SelectFor("PS2")
                .SelectFor("format", "format")
                .ConfirmFor("per system", false)
                // Guided PS2 flow (system chosen → not free-text): single format + its sub-options,
                // then the indexed (cue) format + its sub-options.
                .SelectFor("cso")          // single format
                .SelectFor("9")            // compression level
                .SelectFor("16kb")         // block size
                .SelectFor("cue")          // indexed format
                .SelectFor("split")        // cue type
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            // A help panel for 'format' was shown, scoped to PS2.
            Assert.Contains(p.HelpShown, h => h.title.Contains("format") && h.title.Contains("PS2"));
            (_, IReadOnlyList<(string value, string note)> ex) = p.HelpShown.Find(h => h.title.Contains("format"));
            Assert.Contains(ex, e => e.value.StartsWith("cso:9:16k:4/cue"));
            Assert.DoesNotContain(ex, e => e.value.StartsWith("rvz"));

            // The guided picker composed a valid PS2 "single/indexed" format string.
            ParsedCommand c = CliParser.Parse(argv);
            string fmt = c.Get("format");
            Assert.StartsWith("cso:9:16kb:", fmt);
            Assert.Contains("/cue:split", fmt);
        }

        [Fact]
        public void ConfigSeedsDefaults_AndWritesBackChange()
        {
            // A temp config with a wii format; the builder should seed it and, after the user edits
            // it, offer to write the change back to the file.
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nkitcfg_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "nkit.yaml");
            System.IO.File.WriteAllText(path,
                "task: \nsys:\n  wii:\n    format: rvz:zstd:19:128k:16  #best\n");
            try
            {
                ConfigDefaults cfg = ConfigDefaults.Load(path);

                ScriptedPrompter p = new ScriptedPrompter()
                    .QueueSelect("convert")
                    .TextFor("Input", "game.iso")
                    .TextFor("Output directory", "D:\\o")
                    // choose Wii so the format row re-seeds from wii config, then change it via the
                    // guided flow (Wii has a single format only — no indexed part).
                    .SelectFor("system", "system")
                    .SelectFor("Wii")
                    .SelectFor("format", "format")
                    .ConfirmFor("per system", false)
                    .SelectFor("wbfs")     // single format (change from the seeded rvz value)
                    .SelectFor("yes")      // lossless → wbfs:y
                    .SelectFor(">> Run")
                    .ConfirmFor("Save these to the config", true)  // accept write-back
                    .ConfirmFor("Run this now", true);

                InteractiveBuilder b = new InteractiveBuilder(p, cfg);
                string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

                Assert.NotNull(argv);
                // The config file now has the changed wii format, comment preserved.
                string saved = System.IO.File.ReadAllText(path);
                Assert.Contains("wbfs:y", saved);
                Assert.Contains("#best", saved);
                Assert.DoesNotContain("rvz:zstd:19:128k:16", saved);
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Convert_Wii_GuidedRvz_ComposesFormatString()
        {
            // Wii has a single format only (no indexed). The guided flow walks encoding → level →
            // block, composing a valid "rvz:zstd:<level>:<block>:<par>" string.
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("convert")
                .TextFor("Input", "game.iso")
                .TextFor("Output directory", "D:\\o")
                .SelectFor("system", "system")
                .SelectFor("Wii")
                .SelectFor("format", "format")
                .ConfirmFor("per system", false)
                .SelectFor("rvz")          // single format
                .SelectFor("ZStd")         // RVZ encoding
                .SelectFor("19")           // compression level
                .SelectFor("128kb")        // block size
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            string fmt = c.Get("format");
            Assert.StartsWith("rvz:zstd:19:128kb:", fmt);
            Assert.DoesNotContain("/", fmt); // Wii has no indexed part
        }

        [Fact]
        public void Convert_Saturn_GuidedDualFormat_ComposesSingleAndCue()
        {
            // The dual iso-family/cue form is NOT PS2-specific — it applies to every ISO9660/CD
            // system that supports both a single and an indexed format. Saturn is one such system;
            // the guided flow should offer a single format AND the cue indexed part, joined by '/'.
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("convert")
                .TextFor("Input", "game.iso")
                .TextFor("Output directory", "D:\\o")
                .SelectFor("system", "system")
                .SelectFor("Saturn")
                .SelectFor("format", "format")
                .ConfirmFor("per system", false)
                .SelectFor("cso")          // single format
                .SelectFor("9")            // compression level
                .SelectFor("16kb")         // block size
                .SelectFor("cue")          // indexed (disc) format
                .SelectFor("split")        // cue type
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            string fmt = c.Get("format");
            Assert.StartsWith("cso:9:16kb:", fmt);
            Assert.Contains("/cue:split", fmt); // dual format composed for a non-PS2 system
        }

        [Fact]
        public void Convert_NoSystem_FormatUsesFreeText()
        {
            // With no system chosen (auto-detect), the format is entered as free text so it can span
            // multiple detected systems — the guided per-system picker does NOT engage.
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("convert")
                .TextFor("Input", "game.iso")
                .TextFor("Output directory", "D:\\o")
                .SelectFor("format", "format")
                .ConfirmFor("per system", false)
                .TextFor("Convert format", "rvz:zstd:19:128k:16")
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            Assert.Equal("rvz:zstd:19:128k:16", c.Get("format"));
        }

        [Fact]
        public void Convert_GameCube_HidesKeysRow()
        {
            // 'keys' only applies to WiiU/PS3/XBox/XBox360. With GameCube chosen it must be hidden,
            // so a SelectFor("keys") can never match — the run still completes via >> Run.
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("convert")
                .TextFor("Input", "game.iso")
                .TextFor("Output directory", "D:\\o")
                .SelectFor("system", "system")
                .SelectFor("GameCube")
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);

            Assert.NotNull(argv);
            ParsedCommand c = CliParser.Parse(argv);
            Assert.Equal("convert", c.Verb.Name);
            Assert.Null(c.Get("keys")); // never offered → never set
        }

        [Fact]
        public void Cancel_ReturnsNull()
        {
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("scan")
                .TextFor("Input", "game.iso")
                .SelectFor(">> Quit");

            InteractiveBuilder b = new InteractiveBuilder(p);
            Assert.Null(b.Build(CliParser.Parse(System.Array.Empty<string>()), null));
        }

        [Fact]
        public void EquivalentCommand_OmitsUnsetDefaults_KeepsMandatoryAndUserSet()
        {
            // No config → nothing seeded. User only sets task + input, changes nothing else.
            // The shown "Equivalent command line" should be just the verb + input (no default flags).
            ScriptedPrompter p = new ScriptedPrompter()
                .QueueSelect("scan")
                .TextFor("Input", "game.iso")
                .SelectFor(">> Run")
                .ConfirmFor("Run this now", true);

            InteractiveBuilder b = new InteractiveBuilder(p);   // no ConfigDefaults → no seeds
            string[] argv = b.Build(CliParser.Parse(System.Array.Empty<string>()), null);
            Assert.NotNull(argv);

            string shown = p.InfoLines.Find(l => l != null && l.TrimStart().StartsWith("nkit "));
            Assert.NotNull(shown);
            Assert.Contains("nkit scan", shown);
            Assert.Contains("game.iso", shown);
            // No config-default flags should appear since the user set none.
            Assert.DoesNotContain("--verify", shown);
            Assert.DoesNotContain("--parallelism", shown);
            Assert.DoesNotContain("--console-level", shown);
            Assert.DoesNotContain("--no-config", shown);
        }
    }
}
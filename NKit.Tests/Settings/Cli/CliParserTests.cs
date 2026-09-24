using Nanook.NKit.App.Cli;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Settings.Cli
{
    /// <summary>Table-driven tests for the pure CLI parser and the AppSettings bridge.</summary>
    [Trait("Area", "Settings")]
    [Trait("Group", "Cli")]
    public class CliParserTests
    {
        [Fact]
        public void VerbAndPositionalInputs()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "game1.iso", "game2.iso" });
            Assert.Equal("convert", c.Verb.Name);
            Assert.Equal(new[] { "game1.iso", "game2.iso" }, c.Inputs.ToArray());
        }

        [Fact]
        public void ShortenedVerbAliases_Removed()
        {
            // Shortened task names (vfy/dd/fx/cnv/ext) were dropped — only full verb names resolve,
            // so a shortened token is treated as an input (drag-drop), not a verb.
            ParsedCommand c = CliParser.Parse(new[] { "vfy", "game.iso" });
            Assert.Null(c.Verb);
            Assert.Contains("vfy", c.Inputs);
        }

        [Fact]
        public void OgmrVerb_Resolves()
        {
            ParsedCommand c = CliParser.Parse(new[] { "ogmr", "routing.yaml" });
            Assert.Equal("ogmr", c.Verb.Name);
        }

        [Fact]
        public void LegacyOgmrAlias_1gmr_NoLongerResolves()
        {
            // "1gmr" was never public and has been removed as an alias. A leading unknown token is
            // treated as a drag-drop input, so no verb resolves.
            ParsedCommand c = CliParser.Parse(new[] { "1gmr", "routing.yaml" });
            Assert.Null(c.Verb);
        }

        [Fact]
        public void LongAndShortOptions_SpaceAndEquals()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "--output", "D:\\out", "-f=rvz:19" });
            Assert.Equal("D:\\out", c.Get("output"));
            Assert.Equal("rvz:19", c.Get("format"));
        }

        [Fact]
        public void ShortInAndOut()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "-in", "game.iso", "-out", "D:\\o" });
            Assert.Equal(new[] { "game.iso" }, c.Inputs.ToArray());
            Assert.Equal("D:\\o", c.Get("output"));
        }

        [Fact]
        public void SwitchPresence()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "game.iso", "--recursive", "--no-archives" });
            Assert.True(c.HasSwitch("recursive"));
            Assert.True(c.HasSwitch("no-archives"));
        }

        [Fact]
        public void SystemPrefixScoping()
        {
            // Flag-prefix scoping: the system qualifies the FLAG (--wii:format), mirroring the
            // config's sys: structure. The value is carried through verbatim.
            ParsedCommand c = CliParser.Parse(new[] { "convert", "roms", "--wii:format", "rvz:19", "--gamecube:format", "wbfs:y" });
            ScopedValue wii = c.Values.First(v => v.Option.Canonical == "format" && v.System == "wii");
            ScopedValue gc = c.Values.First(v => v.Option.Canonical == "format" && v.System == "gamecube");
            Assert.Equal("rvz:19", wii.Value);
            Assert.Equal("wbfs:y", gc.Value);
        }

        [Fact]
        public void FormatValueWithColon_NotMistakenForSystemScope()
        {
            // Unscoped flag: "rvz:19" is a plain value (colons in the VALUE are never a scope).
            ParsedCommand c = CliParser.Parse(new[] { "convert", "game.iso", "-f", "rvz:19" });
            ScopedValue f = c.Values.First(v => v.Option.Canonical == "format");
            Assert.Null(f.System);
            Assert.Equal("rvz:19", f.Value);
        }

        [Fact]
        public void FlagPrefix_NonSystemLeader_IsUnknownOption() =>
            // "--rvz:format" — leader "rvz" is not a system, so the flag does not resolve.
            Assert.Throws<CliException>(() => CliParser.Parse(new[] { "convert", "game.iso", "--rvz:format", "x" }));

        [Fact]
        public void FlagPrefix_ShortName_Scopes()
        {
            // Prefix attaches to the resolved option, so the short form works too: --wii:f rvz:19.
            ParsedCommand c = CliParser.Parse(new[] { "convert", "roms", "--wii:f", "rvz:19" });
            ScopedValue wii = c.Values.First(v => v.Option.Canonical == "format" && v.System == "wii");
            Assert.Equal("rvz:19", wii.Value);
        }

        [Fact]
        public void MaskPreservedVerbatim_ArchiveInnerAndWildcards()
        {
            ParsedCommand c = CliParser.Parse(new[] { "scan", "dats.zip//*.dat", "--dat", "d.zip//*.dat" });
            Assert.Equal(new[] { "dats.zip//*.dat" }, c.Inputs.ToArray());
            Assert.Equal("d.zip//*.dat", c.Get("dat"));
        }

        [Fact]
        public void EndOfOptionsMarker()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "--", "-weird-name.iso" });
            Assert.Equal(new[] { "-weird-name.iso" }, c.Inputs.ToArray());
        }

        [Fact]
        public void DragDrop_InputOnly_NoVerb()
        {
            ParsedCommand c = CliParser.Parse(new[] { "game.iso" });
            Assert.Null(c.Verb);
            Assert.Equal(new[] { "game.iso" }, c.Inputs.ToArray());
        }

        [Fact]
        public void NoArgs_Empty()
        {
            ParsedCommand c = CliParser.Parse(System.Array.Empty<string>());
            Assert.Null(c.Verb);
            Assert.Empty(c.Inputs);
        }

        [Fact]
        public void HelpAndVersion()
        {
            Assert.True(CliParser.Parse(new[] { "help" }).ShowHelp);
            Assert.Equal("convert", CliParser.Parse(new[] { "help", "convert" }).HelpVerb);
            Assert.True(CliParser.Parse(new[] { "convert", "--help" }).ShowHelp);
            ParsedCommand vh = CliParser.Parse(new[] { "convert", "help" });
            Assert.True(vh.ShowHelp);
            Assert.Equal("convert", vh.HelpVerb);
            Assert.True(CliParser.Parse(new[] { "version" }).ShowVersion);
            Assert.True(CliParser.Parse(new[] { "--version" }).ShowVersion);
        }

        [Fact]
        public void UnknownOption_Throws() => Assert.Throws<CliException>(() => CliParser.Parse(new[] { "convert", "--nope", "x" }));

        // ── Bridge ──────────────────────────────────────────────────────────────

        [Fact]
        public void Bridge_VerbToTask_AndInputs()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "a.iso", "b.iso", "-f", "rvz:19" });
            (string cfg, Dictionary<string, string> o) = AppSettingsBridge.Build(c);
            Assert.Equal("convert", o["task"]);
            Assert.Equal("a.iso\0b.iso", o["in"]);
            Assert.Equal("rvz:19", o["convert"]);   // legacy config key
        }

        [Fact]
        public void Bridge_SystemScopedOverride()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "roms", "--wii:format", "rvz:19" });
            (_, Dictionary<string, string> o) = AppSettingsBridge.Build(c);
            Assert.Equal("rvz:19", o["wii:convert"]);
        }

        [Fact]
        public void Bridge_SwitchesToLegacyYn()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "x.iso", "--recursive", "--no-archives" });
            (_, Dictionary<string, string> o) = AppSettingsBridge.Build(c);
            Assert.Equal("y", o["r"]);
            Assert.Equal("n", o["arc"]);   // no-archives inverts to arc=n
        }

        [Fact]
        public void Bridge_ConfigFileExtracted()
        {
            ParsedCommand c = CliParser.Parse(new[] { "convert", "x.iso", "-c", "other.yaml" });
            (string cfg, Dictionary<string, string> o) = AppSettingsBridge.Build(c);
            Assert.Equal("other.yaml", cfg);
            Assert.False(o.ContainsKey("cfg"));
        }

        [Fact]
        public void Bridge_ForensicFoldedIntoExtractMask()
        {
            ParsedCommand c = CliParser.Parse(new[] { "extract", "x.iso", "-m", "ri:.*", "--forensic" });
            (_, Dictionary<string, string> o) = AppSettingsBridge.Build(c);
            Assert.StartsWith("f", o["extract"]);      // forensic flag prepended
            Assert.Contains("ri:.*".Substring(3), o["extract"]); // mask body retained
        }

        [Fact]
        public void DatCollections_LongForms_MapToConfigKeys()
        {
            ParsedCommand c = CliParser.Parse(new[]
            {
                "scan", "roms",
                "--dats-redump",   "D:\\Dats\\Redump",
                "--dats-nointro",  "D:\\Dats\\No-Intro",
                "--dats-tosec",    "D:\\Dats\\TOSEC",
            });
            (_, Dictionary<string, string> o) = AppSettingsBridge.Build(c);

            Assert.Equal("D:\\Dats\\Redump", o["redumpDatsPath"]);
            Assert.Equal("D:\\Dats\\No-Intro", o["noIntroDatsPath"]);
            Assert.Equal("D:\\Dats\\TOSEC", o["tosecDatsPath"]);
        }

        [Fact]
        public void DatCollections_ShortForms_MapToConfigKeys()
        {
            ParsedCommand c = CliParser.Parse(new[]
            {
                "verify", "roms",
                "-dr", "D:\\Dats\\Redump",
                "-dn", "D:\\Dats\\No-Intro",
                "-dt", "D:\\Dats\\TOSEC",
            });
            (_, Dictionary<string, string> o) = AppSettingsBridge.Build(c);

            Assert.Equal("D:\\Dats\\Redump", o["redumpDatsPath"]);
            Assert.Equal("D:\\Dats\\No-Intro", o["noIntroDatsPath"]);
            Assert.Equal("D:\\Dats\\TOSEC", o["tosecDatsPath"]);
        }

        [Fact]
        public void DatCollections_LegacyConfigKeyAliases_Resolve()
        {
            // The legacy config-key spellings are accepted as aliases too.
            ParsedCommand c = CliParser.Parse(new[] { "scan", "roms", "--redumpDatsPath", "D:\\R" });
            Assert.Equal("D:\\R", c.Get("dats-redump"));
        }
    }
}
using System;
using System.Collections.Generic;

namespace Nanook.NKit.App.Cli
{
    /// <summary>The kind of value an option carries (drives parsing, help, and interactive widget).</summary>
    public enum OptionKind
    {
        /// <summary>Presence switch — no value (e.g. --recursive).</summary>
        Switch,
        /// <summary>Free value.</summary>
        Value,
        /// <summary>One of a fixed set (<see cref="CliOption.EnumValues"/>).</summary>
        Enum,
        /// <summary>A filesystem path.</summary>
        Path,
        /// <summary>A file mask / selection spec (preserved verbatim — see FileMask).</summary>
        Mask,
        /// <summary>A convert-format spec (e.g. rvz:zstd:19:128k:16).</summary>
        Format,
        /// <summary>A logging level.</summary>
        Level,
    }

    /// <summary>
    /// A worked example for an option, optionally scoped to specific systems so the interactive
    /// help can show only the examples relevant to the system already chosen (context-sensitive).
    /// </summary>
    public sealed class OptionExample
    {
        public OptionExample(string value, string note, params string[] systems)
        {
            Value = value;
            Note = note;
            Systems = systems ?? Array.Empty<string>(); // empty = applies to all systems
        }

        public string Value { get; }
        public string Note { get; }
        public string[] Systems { get; }

        public bool AppliesTo(string system)
        {
            if (Systems.Length == 0) return true;
            if (string.IsNullOrEmpty(system)) return true; // no system chosen → show all
            foreach (string s in Systems)
                if (string.Equals(s, system, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>
    /// Declarative description of a single CLI/config option. The canonical name is the CLI long
    /// form (hyphenated) and is also the config key; aliases include the legacy config/CLI names.
    /// Static data only — no reflection — so it is AOT-safe. This is the single source of truth
    /// consumed by the parser, help renderer and interactive builder.
    /// </summary>
    public sealed class CliOption
    {
        public CliOption(string canonical, string shortName, string[] aliases, OptionKind kind,
            bool root, bool prefixable, string[] verbs, string[] enumValues, string configKey, string help,
            string longHelp = null, OptionExample[] examples = null, bool deprecated = false)
        {
            Canonical = canonical;
            Short = shortName;
            Aliases = aliases ?? Array.Empty<string>();
            Kind = kind;
            Root = root;
            Prefixable = prefixable;
            Verbs = verbs;                 // null = available to all verbs
            EnumValues = enumValues ?? Array.Empty<string>();
            ConfigKey = configKey;         // the existing NKit param key the bridge writes
            Help = help;
            LongHelp = longHelp;
            Examples = examples ?? Array.Empty<OptionExample>();
            Deprecated = deprecated;
        }

        public string Canonical { get; }
        public string Short { get; }
        public string[] Aliases { get; }
        public OptionKind Kind { get; }
        public bool Root { get; }
        public bool Prefixable { get; }
        public string[] Verbs { get; }
        public string[] EnumValues { get; }
        public string ConfigKey { get; }
        public string Help { get; }
        /// <summary>Longer multi-line description shown in the help panel (null = use Help).</summary>
        public string LongHelp { get; }
        /// <summary>Worked examples, optionally system-scoped for context-sensitive help.</summary>
        public OptionExample[] Examples { get; }
        /// <summary>Retired option: still PARSED (accepted for back-compat) but IGNORED by the engine.
        /// Hidden from the interactive builder and help so it is not presented as a working knob.</summary>
        public bool Deprecated { get; }

        public bool IsSwitch => Kind == OptionKind.Switch;
        public bool HasRichHelp => LongHelp != null || Examples.Length != 0;

        public bool AvailableFor(string verb)
            => Verbs == null || Array.IndexOf(Verbs, verb) >= 0;

        /// <summary>Examples relevant to the chosen system (all if none chosen).</summary>
        public IReadOnlyList<OptionExample> ExamplesFor(string system)
        {
            List<OptionExample> list = new List<OptionExample>();
            foreach (OptionExample e in Examples)
                if (e.AppliesTo(system)) list.Add(e);
            return list;
        }
    }

    /// <summary>Declarative description of a task verb.</summary>
    public sealed class CliVerb
    {
        public CliVerb(string name, string[] aliases, string task, bool hasOutputDir, string help)
        {
            Name = name;
            Aliases = aliases ?? Array.Empty<string>();
            Task = task;        // the NKit 'task' param value
            HasOutputDir = hasOutputDir;
            Help = help;
        }

        public string Name { get; }
        public string[] Aliases { get; }
        public string Task { get; }
        /// <summary>Whether this task writes to a plain output directory (prompted up front).
        /// False for verify (no output) and scan/dedupe/ogmr (their output is not a plain dir).</summary>
        public bool HasOutputDir { get; }
        public string Help { get; }
    }

    /// <summary>
    /// The NKit CLI option model: all verbs and options as static data. Backward-compatible aliases
    /// (legacy config keys and short forms) are declared alongside the canonical names.
    /// </summary>
    public static class OptionModel
    {
        // Recognised system names for value-prefix scoping (e.g. --format wii:rvz:19). Lowercased.
        // Mirrors the config's sys: section names (SystemType, plus the config spellings).
        public static readonly string[] Systems =
        {
            "dreamcast", "gamecube", "pcengine", "ps1", "ps2", "ps3", "psp", "saturn",
            "segacd", "wii", "wiiu", "xbox", "xbox360", "cdi", "default",
        };

        public static readonly CliVerb[] Verbs =
        {
            new CliVerb("convert",    Array.Empty<string>(), "convert",    hasOutputDir: true,  "Convert images to another format."),
            new CliVerb("expand",     Array.Empty<string>(), "expand",     hasOutputDir: true,  "Expand images to full images."),
            new CliVerb("extract",    Array.Empty<string>(), "extract",    hasOutputDir: true,  "Extract filesystem contents to a subdirectory."),
            new CliVerb("fix",        Array.Empty<string>(), "fix",        hasOutputDir: true,  "Recover files to fully expanded images (supported systems)."),
            new CliVerb("fixextract", Array.Empty<string>(), "fixExtract", hasOutputDir: true,  "Extract the files used by the fix task."),
            new CliVerb("scan",       Array.Empty<string>(), "scan",       hasOutputDir: false, "Analyze images and save XML scan blueprints."),
            new CliVerb("verify",     Array.Empty<string>(), "verify",     hasOutputDir: false, "Verify using checksums, dats and NKit scans."),
            new CliVerb("dedupe",     Array.Empty<string>(), "dedupe",     hasOutputDir: false, "Deduplicate images into a DataStore."),
            new CliVerb("ogmr",       Array.Empty<string>(), "dedupe",     hasOutputDir: false, "Batch dedupe routed by regex masks from an OGMR YAML."),
            new CliVerb("wipe",       Array.Empty<string>(), "wipe",       hasOutputDir: true,  "Wipe game data to produce a small lossy test image (dev/testing)."),
        };

        public static readonly CliOption[] Options =
        {
            // ── Global (root) options — never system-scoped ────────────────────────────
            new CliOption("input", "in", new[] { "in" }, OptionKind.Path, root: true, prefixable: false,
                verbs: null, enumValues: null, configKey: "in", help: "Input file, folder or mask (* and ? supported). Positional or repeatable."),
            new CliOption("output", "out", new[] { "out" }, OptionKind.Path, true, false,
                null, null, "out", "Output directory."),
            new CliOption("recursive", "r", new[] { "r" }, OptionKind.Switch, true, false,
                null, null, "r", "Scan input folders recursively."),
            new CliOption("no-archives", "na", new[] { "arc" }, OptionKind.Switch, true, false,
                null, null, "arc", "Do not scan inside archives (archives are scanned by default)."),
            new CliOption("verify", "v", new[] { "v" }, OptionKind.Enum, true, false,
                null, new[] { "y", "n", "dat" }, "v", "Verify results [y, n, dat]."),
            new CliOption("system", "sys", new[] { "system" }, OptionKind.Value, true, false,
                null, null, "system", "Limit processing to a single system."),
            new CliOption("temp", "t", new[] { "tmp" }, OptionKind.Path, true, false,
                null, null, "tmp", "Temporary working directory."),
            new CliOption("config", "c", new[] { "cfg" }, OptionKind.Path, true, false,
                null, null, "cfg", "Config file path, or 'n' for no config."),
            new CliOption("no-config", "nocfg", Array.Empty<string>(), OptionKind.Switch, true, false,
                null, null, null, "Do not load any config file (overrides --config)."),
            // Force the guided interactive builder even when a task/input could otherwise run directly.
            // CLI-only (no config key); parsed on any verb. Consumed by CliFrontEnd, never by the engine.
            new CliOption("interactive", "i", Array.Empty<string>(), OptionKind.Switch, true, false,
                null, null, null, "Launch the guided interactive builder (even when a task/input is already set)."),
            new CliOption("results", "rs", Array.Empty<string>(), OptionKind.Switch, true, false,
                null, null, "results", "Save a results summary file."),
            new CliOption("results-out", "ro", new[] { "resultsOut" }, OptionKind.Path, true, false,
                null, null, "resultsOut", "Directory for results summary files."),
            // Output-lifecycle switches. Effective only on tasks that emit a replacement image with
            // a delete-source-candidate step: convert, expand and fix (see NKitTask + _StepsDefs
            // 'del:Y'). Still root config keys and still accepted by the parser on any verb, but only
            // OFFERED here for the verbs where they do something.
            new CliOption("delete-processed", "dp", new[] { "deleteProcessed" }, OptionKind.Switch, true, false,
                new[] { "convert", "expand", "fix" }, null, "deleteProcessed",
                "Delete the source file after a successful, verified convert/expand/fix (never for archived sources)."),
            new CliOption("skip-if-completed", "sc", new[] { "skipIfCompleted" }, OptionKind.Switch, true, false,
                new[] { "convert", "expand", "fix" }, null, "skipIfCompleted",
                "Skip processing if the output image already exists (convert/expand/fix). Ignored with --dat-match."),
            new CliOption("dat-match", "dm", new[] { "outAsDatMatch" }, OptionKind.Switch, true, false,
                null, null, "outAsDatMatch", "Rename output images to a matched dat entry."),
            new CliOption("console-level", "cl", new[] { "consoleLevel" }, OptionKind.Level, true, false,
                null, new[] { "none", "info", "detail", "error", "debug" }, "consoleLevel", "Console logging level."),
            new CliOption("log-level", "ll", new[] { "logOutLevel" }, OptionKind.Level, true, false,
                null, new[] { "none", "info", "detail", "error", "debug" }, "logOutLevel", "File logging level."),
            new CliOption("log", "l", new[] { "logOut" }, OptionKind.Path, true, false,
                null, null, "logOut", "Log file path."),

            // ── Per-system (prefixable) options ─────────────────────────────────────────
            new CliOption("format", "f", new[] { "convert" }, OptionKind.Format, false, true,
                new[] { "convert" }, null, "convert", "Convert format (system-scopable). The parameters differ per format — see below.",
                longHelp:
                    "A ':'-separated string whose parameters DIFFER PER FORMAT. Trailing parts are optional and\n" +
                    "fall back to per-system config defaults. Pick the format for the input's system:\n" +
                    "\n" +
                    "  rvz    Wii/GameCube, lossless.  rvz:encoding:level:blockSize:parallelism\n" +
                    "         encoding = zstd | lzma | none (none omits the level).\n" +
                    "         e.g. rvz:zstd:19:128k:16  |  rvz:lzma:9:128k:16  |  rvz:none:128k:4\n" +
                    "  wbfs   Wii/GameCube, lossless.   wbfs:lossless           e.g. wbfs:y\n" +
                    "  ciso   Wii/GameCube, lossless.   ciso:lossless           e.g. ciso:y\n" +
                    "  wux    WiiU, lossless.           wux (no parameters)\n" +
                    "  apptmd WiiU, lossy app+tmd.      apptmd (no parameters)\n" +
                    "  deciso PS3, decrypted ISO.       deciso (no parameters)\n" +
                    "  cso    compressed (ISO systems). cso:level:blockSize:parallelism   e.g. cso:9:16k:4\n" +
                    "  zso    compressed (ISO systems). zso:level:blockSize:parallelism   (level ignored) e.g. zso::16k:4\n" +
                    "  iso    XBox/XBox360, plain ISO.  iso (no parameters)\n" +
                    "  xiso   XBox, rewritten XDVDFS.   xiso (no parameters)\n" +
                    "  cue    Redump cue/bin.           cue:split | cue:joined   e.g. cue  |  cue:joined\n" +
                    "  gdi    Dreamcast TOSEC.          gdi (no parameters)\n" +
                    "\n" +
                    "DUAL FORMAT (all ISO9660/CD systems — PS1, PS2, PS3, Saturn, SegaCD, CD-i, PC Engine,\n" +
                    "and generic ISO): a single iso-family format and a cue joined by '/', so the right\n" +
                    "output is used whether the source is a plain image or a cue/bin — e.g. cso:9:16k:4/cue:split.\n" +
                    "blockSize is a size like 128k/16k/2k; parallelism is a thread count.",
                examples: new[]
                {
                    new OptionExample("rvz:zstd:19:128k:16", "best compression",        "wii", "gamecube"),
                    new OptionExample("rvz:lzma:9:128k:16",  "lzma encoding",           "wii", "gamecube"),
                    new OptionExample("rvz:none:128k:4",     "fast, no compression",    "wii", "gamecube"),
                    new OptionExample("wbfs:y",              "lossless WBFS",           "wii", "gamecube"),
                    new OptionExample("ciso:y",              "lossless CISO",           "wii", "gamecube"),
                    new OptionExample("wux",                 "WiiU WUX",                "wiiu"),
                    new OptionExample("apptmd",              "WiiU app+tmd (lossy)",    "wiiu"),
                    new OptionExample("deciso",              "PS3 decrypted ISO",       "ps3"),
                    new OptionExample("cso:9:16k:4",         "CSO (compressed ISO)",    "ps1", "ps2", "ps3", "saturn", "segacd", "cdi", "pcengine"),
                    new OptionExample("cso:9:16k:4/cue:split","iso + cue (split) — dual","ps1", "ps2", "ps3", "saturn", "segacd", "cdi", "pcengine"),
                    new OptionExample("zso::16k:4/cue:split","ZSO + cue — dual",        "ps1", "ps2", "ps3", "saturn", "segacd", "cdi", "pcengine"),
                    new OptionExample("cso:9:2k:4",          "PSP CSO",                 "psp"),
                    new OptionExample("iso",                 "XBox ISO",                "xbox", "xbox360"),
                    new OptionExample("xiso",                "XBox rewritten ISO",      "xbox"),
                    new OptionExample("cue",                 "redump cue/bin",          "ps1", "saturn", "segacd", "cdi", "pcengine", "dreamcast"),
                    new OptionExample("cue:joined",          "single joined bin",       "ps1", "saturn", "segacd", "cdi", "pcengine"),
                    new OptionExample("gdi",                 "Dreamcast GDI (tosec)",   "dreamcast"),
                }),
            new CliOption("mask", "m", new[] { "extract" }, OptionKind.Mask, false, true,
                new[] { "extract", "fixextract" }, null, "extract", "Extract selection: flags:pattern, e.g. mi:* or ri:^(.*)$ (system-scopable).",
                longHelp: "flags:pattern — flags: m=mask (wildcards * ?), r=regex, i=case-insensitive (case-sensitive if omitted). Use --forensic to also extract image parts with addresses.",
                examples: new[]
                {
                    new OptionExample("mi:*",             "all files (mask, case-insensitive)"),
                    new OptionExample("mi:*.txt|*.png",   "masks, OR-separated"),
                    new OptionExample("ri:^(.*)$",        "all files (regex)"),
                    new OptionExample("ri:^(.*\\.iso)$",  "regex match"),
                }),
            new CliOption("forensic", "fx", Array.Empty<string>(), OptionKind.Switch, false, false,
                new[] { "extract", "fixextract" }, null, null, "Extract image parts with addresses (forensic mode)."),
            new CliOption("dat", "d", Array.Empty<string>(), OptionKind.Mask, false, true,
                new[] { "scan", "verify", "fix", "convert" }, null, "dat", "Dat archive/mask, e.g. dats.zip//*.dat (system-scopable).",
                longHelp: "A dat file, a folder of dats, or an archive with an inner mask via '//'. The latest matching file is used. Latest-modified wins on multiple matches.",
                examples: new[]
                {
                    new OptionExample("path/Dats/WiiDats*.zip//*.dat", "dat inside a zip"),
                    new OptionExample("path/directoryOfDats//*.dat",   "folder of dats"),
                }),
            // Whole-collection dat roots. Unlike --dat (a single dat/mask), these point at a folder
            // or archive holding an entire Redump / No-Intro / TOSEC set; NKit auto-selects the right
            // dat per system using built-in filename masks (DatManager). Global (not system-scopable).
            new CliOption("dats-redump", "dr", new[] { "redumpDatsPath" }, OptionKind.Path, true, false,
                new[] { "scan", "verify", "fix", "convert" }, null, "redumpDatsPath",
                "Folder/archive of the Redump dat collection (per-system dat auto-selected).",
                longHelp: "A directory or zip/rar/7z archive containing the Redump dat set. NKit picks the dat matching each image's system automatically. Use --dat instead to point at one specific dat.",
                examples: new[]
                {
                    new OptionExample("path/Dats/Redump",        "folder of Redump dats"),
                    new OptionExample("path/Dats/Redump.zip",    "Redump dats inside a zip"),
                }),
            new CliOption("dats-nointro", "dn", new[] { "noIntroDatsPath" }, OptionKind.Path, true, false,
                new[] { "scan", "verify", "fix", "convert" }, null, "noIntroDatsPath",
                "Folder/archive of the No-Intro dat collection (per-system dat auto-selected).",
                longHelp: "A directory or zip/rar/7z archive containing the No-Intro dat set. NKit picks the dat matching each image's system automatically. Use --dat instead to point at one specific dat.",
                examples: new[]
                {
                    new OptionExample("path/Dats/No-Intro",      "folder of No-Intro dats"),
                    new OptionExample("path/Dats/No-Intro.zip",  "No-Intro dats inside a zip"),
                }),
            new CliOption("dats-tosec", "dt", new[] { "tosecDatsPath" }, OptionKind.Path, true, false,
                new[] { "scan", "verify", "fix", "convert" }, null, "tosecDatsPath",
                "Folder/archive of the TOSEC dat collection (per-system dat auto-selected).",
                longHelp: "A directory or zip/rar/7z archive containing the TOSEC dat set. NKit picks the dat matching each image's system automatically. Use --dat instead to point at one specific dat.",
                examples: new[]
                {
                    new OptionExample("path/Dats/TOSEC",         "folder of TOSEC dats"),
                    new OptionExample("path/Dats/TOSEC.zip",     "TOSEC dats inside a zip"),
                }),
            new CliOption("keys", "k", Array.Empty<string>(), OptionKind.Mask, false, true,
                null, null, "keys", "Key file/folder/archive mask (system-scopable).",
                longHelp: "A directory or zip/rar/7z/gzip archive mask of key files. The latest modified match is used. Used by WiiU and PS3.",
                examples: new[]
                {
                    new OptionExample("path/keys/*.zip", "keys inside a zip", "wiiu", "ps3"),
                    new OptionExample("path/folderOfKeys", "folder of keys",  "wiiu", "ps3"),
                }),
            new CliOption("fix-info", "fi", new[] { "fixInfo" }, OptionKind.Path, false, true,
                new[] { "fix", "fixextract" }, null, "fixInfo", "Path to a fix YAML (system-scopable)."),
            new CliOption("fix-files", "ff", new[] { "fixFiles" }, OptionKind.Path, false, true,
                new[] { "fix", "fixextract" }, null, "fixFiles", "Path to NKit recovery files (system-scopable)."),
            new CliOption("dedupe", "dd", Array.Empty<string>(), OptionKind.Value, false, true,
                new[] { "dedupe", "ogmr" }, null, "dedupe", "Dedupe config: setName:shard:block:aux (system-scopable).",
                longHelp: "setName:shardSize:blockSize:auxMode — all parts optional. Append :y as the 4th part to auto-create an aux store. Blank uses all defaults (system name, 50GiB shards, 64KiB blocks).",
                examples: new[]
                {
                    new OptionExample("",                "all defaults"),
                    new OptionExample("mySet",           "custom set name"),
                    new OptionExample(":100g:128k:n",    "100GiB shards, 128KiB blocks, no aux"),
                    new OptionExample("mySet:50g:64k:y", "all specified + aux"),
                }),
            new CliOption("base-in", "bi", new[] { "baseInPath" }, OptionKind.Path, false, true,
                null, null, "baseInPath", "Per-system base path for input routing (system-scopable)."),
            new CliOption("scan-out", "so", new[] { "scanOut" }, OptionKind.Path, false, true,
                new[] { "scan", "convert", "expand", "extract", "dedupe", "fix" }, null, "scanOut", "Directory to save full image scans (system-scopable)."),
            // Scan output format — exposed for the SCAN VERB ONLY. The documented choices are the
            // two query-friendly YAML modes; the legacy "Xml" value is intentionally omitted from
            // the advertised set (undocumented) but is still accepted if typed or set in config, and
            // is honoured only for the scan task (see SystemSettings / NKitTask).
            new CliOption("scan-format", "sf", new[] { "scanFormat" }, OptionKind.Enum, false, true,
                new[] { "scan" },
                new[] { "YamlCompact", "YamlVerbose" }, "scanFormat",
                "Scan file format: YamlCompact (default) or YamlVerbose. YAML is query-friendly (system-scopable)."),
            new CliOption("scan-in", "si", new[] { "scanIn" }, OptionKind.Path, false, true,
                new[] { "verify" }, null, "scanIn", "Directory to look up scans for verifying (system-scopable)."),
            new CliOption("ogmr", null, Array.Empty<string>(), OptionKind.Path, false, true,
                new[] { "ogmr", "dedupe" }, null, "ogmr", "Path to an OGMR routing YAML (system-scopable)."),
        };

        // ── Lookups (built once) ────────────────────────────────────────────────────────

        private static readonly Dictionary<string, CliOption> _byName = buildOptionLookup();
        private static readonly Dictionary<string, CliVerb> _byVerb = buildVerbLookup();
        private static readonly HashSet<string> _systemSet = buildSystemSet();

        /// <summary>Resolve an option by canonical name, alias, or short form (case-insensitive).</summary>
        public static CliOption FindOption(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            return _byName.TryGetValue(token.ToLowerInvariant(), out CliOption o) ? o : null;
        }

        /// <summary>Resolve a verb by name or alias (case-insensitive).</summary>
        public static CliVerb FindVerb(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            return _byVerb.TryGetValue(token.ToLowerInvariant(), out CliVerb v) ? v : null;
        }

        public static bool IsSystem(string token)
            => token != null && _systemSet.Contains(token.ToLowerInvariant());

        private static Dictionary<string, CliOption> buildOptionLookup()
        {
            Dictionary<string, CliOption> d = new Dictionary<string, CliOption>(StringComparer.OrdinalIgnoreCase);
            foreach (CliOption o in Options)
            {
                add(d, o.Canonical, o);
                if (!string.IsNullOrEmpty(o.Short)) add(d, o.Short, o);
                foreach (string a in o.Aliases) add(d, a, o);
            }
            return d;
        }

        private static void add(Dictionary<string, CliOption> d, string key, CliOption o)
        {
            // First declaration wins; canonical is declared first so it takes precedence.
            if (!d.ContainsKey(key)) d.Add(key, o);
        }

        private static Dictionary<string, CliVerb> buildVerbLookup()
        {
            Dictionary<string, CliVerb> d = new Dictionary<string, CliVerb>(StringComparer.OrdinalIgnoreCase);
            foreach (CliVerb v in Verbs)
            {
                d[v.Name] = v;
                foreach (string a in v.Aliases) if (!d.ContainsKey(a)) d.Add(a, v);
            }
            return d;
        }

        private static HashSet<string> buildSystemSet()
        {
            HashSet<string> s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sys in Systems) s.Add(sys);
            return s;
        }
    }
}
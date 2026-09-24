using System;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Nanook.NKit.Vfs
{
    internal static class NkdsCommandLineHelp
    {
        // ── Colour scheme (matches the nkit CLI / log / interactive scheme) ─────────────
        //   app name (nkds)      -> green
        //   version              -> yellow
        //   section titles       -> cyan   (a line that is just "Word:" — Usage:/Options:/…)
        //   option long names    -> bold white   ("--name")
        //   option shorthand     -> yellow       (", -x")
        // Everything else is left uncoloured. The raw help strings stay plain and readable; this
        // post-processes them into Spectre markup at print time.
        private const string AppColour = "green";
        private const string TitleColour = "cyan";
        private const string OptionColour = "bold white";
        private const string ShortColour = "yellow";

        // A whole-line "section title": optional indent then a word/phrase ending in ':' with nothing
        // after it (e.g. "Usage:", "Global options:", "OGMR YAML format:").
        // Section titles sit at column 0 (no leading whitespace) — e.g. "Usage:", "Options:",
        // "OGMR YAML format:". Indented "word:" lines (YAML sample keys) are NOT titles.
        private static readonly Regex _titleLine = new Regex(@"^([A-Za-z][A-Za-z0-9 /()]*:)\s*$", RegexOptions.Compiled);
        // An option line: indent then "--long[, -s]" then the rest (value hint + help).
        private static readonly Regex _optionLine = new Regex(@"^(\s*)(--[A-Za-z0-9\-?]+)(, -[A-Za-z0-9?]+)?(.*)$", RegexOptions.Compiled);
        // The "nkds <cmd> - description" first line (or "nkds - ...").
        private static readonly Regex _headerLine = new Regex(@"^nkds( [A-Za-z0-9]+)? - .*$", RegexOptions.Compiled);

        // A command-list line inside the "Commands:" block: indent, the command word, 2+ spaces,
        // then "(aliases) description". The command word (and the "(aliases)") is coloured white.
        private static readonly Regex _commandLine = new Regex(@"^(\s+)([a-z][a-z0-9]*)(\s+)(\(.*?\)\s+)?(.*)$", RegexOptions.Compiled);

        // A line whose first non-space token is "nkds" → colour that token green, rest plain/escaped.
        private static string colourizeLeadingApp(string line)
        {
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            // Token must be exactly "nkds" followed by end-of-line or a space.
            if (line.Length - indent >= 4
                && string.CompareOrdinal(line, indent, "nkds", 0, 4) == 0
                && (line.Length == indent + 4 || line[indent + 4] == ' '))
            {
                return line.Substring(0, indent)
                     + "[" + AppColour + "]nkds[/]"
                     + Markup.Escape(line.Substring(indent + 4));
            }
            return Markup.Escape(line);
        }

        // Turn a plain help string into Spectre markup following the scheme. Applied per line so the
        // rest of each line (paths, examples, notes) is passed through escaped/uncoloured.
        private static string colourize(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length + 256);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            // Track whether we are inside the "Commands:" block so command names (which look like
            // ordinary indented words) are coloured only there, not in Notes/Examples. Likewise the
            // "... YAML format:" sample block, which is rendered entirely yellow.
            bool inCommands = false;
            bool inYaml = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                Match title = _titleLine.Match(line);
                if (title.Success)
                {
                    string t = title.Groups[1].Value;
                    inCommands = t.Equals("Commands:", StringComparison.Ordinal);
                    // A "... YAML format:" heading starts a sample block shown entirely in yellow.
                    inYaml = t.EndsWith("YAML format:", StringComparison.Ordinal);
                    sb.Append('[').Append(TitleColour).Append(']')
                      .Append(Markup.Escape(t)).Append("[/]");
                }
                else if (inYaml && line.Trim().Length != 0)
                {
                    // Whole YAML sample line in yellow. The sample ends at the first blank line;
                    // any following explanatory prose falls through to normal handling.
                    sb.Append('[').Append(ShortColour).Append(']')
                      .Append(Markup.Escape(line)).Append("[/]");
                }
                else if (inYaml)
                {
                    // Blank line — ends the YAML sample block.
                    inYaml = false;
                    sb.Append(line);
                }
                else if (inCommands && line.Trim().Length != 0 && _commandLine.Match(line) is { Success: true } cmd)
                {
                    // "  mount    (alias) desc" → command word (and any "(alias)") bold white,
                    // matching how nkit styles a task and its aliases. Description plain.
                    sb.Append(cmd.Groups[1].Value)
                      .Append('[').Append(OptionColour).Append(']')
                      .Append(Markup.Escape(cmd.Groups[2].Value)).Append("[/]")
                      .Append(cmd.Groups[3].Value);
                    if (cmd.Groups[4].Success && cmd.Groups[4].Value.Length != 0)
                        sb.Append('[').Append(OptionColour).Append(']')
                          .Append(Markup.Escape(cmd.Groups[4].Value)).Append("[/]");
                    sb.Append(Markup.Escape(cmd.Groups[5].Value));
                }
                else if (_headerLine.IsMatch(line))
                {
                    // "nkds" green; "<cmd>" (if present) bold white; the " - description" plain.
                    int dash = line.IndexOf(" - ", StringComparison.Ordinal);
                    string head = line.Substring(0, dash);   // "nkds" or "nkds mount"
                    string rest = line.Substring(dash);       // " - description"
                    int sp = head.IndexOf(' ');
                    if (sp < 0)
                        sb.Append('[').Append(AppColour).Append(']').Append("nkds").Append("[/]");
                    else
                        sb.Append('[').Append(AppColour).Append(']').Append("nkds").Append("[/]")
                          .Append(' ').Append('[').Append(OptionColour).Append(']')
                          .Append(Markup.Escape(head.Substring(sp + 1))).Append("[/]");
                    sb.Append(Markup.Escape(rest));
                }
                else
                {
                    Match opt = _optionLine.Match(line);
                    if (opt.Success && opt.Groups[2].Value.StartsWith("--"))
                    {
                        sb.Append(opt.Groups[1].Value)
                          .Append('[').Append(OptionColour).Append(']')
                          .Append(Markup.Escape(opt.Groups[2].Value)).Append("[/]");
                        if (opt.Groups[3].Success && opt.Groups[3].Value.Length != 0)
                            sb.Append('[').Append(ShortColour).Append(']')
                              .Append(Markup.Escape(opt.Groups[3].Value)).Append("[/]");
                        sb.Append(Markup.Escape(opt.Groups[4].Value));
                    }
                    else
                    {
                        // Usage/Examples/Help lines: colour a leading "nkds" token green (as nkit
                        // greens "nkit"); the remainder of the line is plain.
                        sb.Append(colourizeLeadingApp(line));
                    }
                }

                if (i < lines.Length - 1)
                    sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string ExampleDataStorePath => getExampleDataStorePath();
        private static string ExampleIndexFilePath => getExampleIndexFilePath();
        private static string ExampleMountPoint => getExampleMountPoint();
        private static string ExampleSetPath => getExampleSetPath();
        private static string ExampleInputPath => getExampleInputPath();

        public static void Write(NkdsCommand? command = null) => AnsiConsole.MarkupLine(colourize(getText(command)));

        private static string getText(NkdsCommand? command)
        {
            return command switch
            {
                NkdsCommand.Mount => getMountHelp(),
                NkdsCommand.List => getListHelp(),
                NkdsCommand.Add => getAddHelp(),
                NkdsCommand.AddDir => getAddDirHelp(),
                NkdsCommand.Ogmr => getOgmrHelp(),
                NkdsCommand.Remove => getRemoveHelp(),
                NkdsCommand.Restore => getRestoreHelp(),
                NkdsCommand.Compact => getCompactHelp(),
                NkdsCommand.Export => getExportHelp(),
                NkdsCommand.Verify => getVerifyHelp(),
                NkdsCommand.Create => getCreateHelp(),
                NkdsCommand.Rollback => getRollbackHelp(),
                NkdsCommand.Sets => getSetsHelp(),
                NkdsCommand.Stats => getStatsHelp(),
                NkdsCommand.Version => getVersionHelp(),
                _ => getGeneralHelp(),
            };
        }

        private static string getGeneralHelp() =>
            $$"""
            nkds - NKit DataStore filesystem and inspection tool

            Usage:
              nkds <command> [options]
              nkds <command> help
              nkds help [command]
              nkds --help
              nkds --version

            Commands:
              mount    Mount a datastore or set as a virtual filesystem.
              list     (ls) List images across sets.
              add      (import) Import images through the NKit pipeline.
              adddir   Store directory contents directly into a set.
              ogmr     Batch import images routed by regex masks from an OGMR YAML file.
              remove   (rm) Mark images as removed within a set.
              restore  (rst) Restore images previously marked as removed.
              compact  (cp) Permanently delete marked images and unused blocks.
              export   (copy, convert) Export images from a set through the NKit pipeline.
              verify   (vfy) Verify images through the NKit pipeline.
              create   (newset) Create a new set in a datastore.
              rollback Rollback a set to a specific image.
              sets     List sets in a datastore.
              stats    (info) Show statistics for one set or all sets.
              help     Show help for a specific command.
              version  Show version information.

            Global options:
              --datastore, -ds <path>   DataStore directory or `.nkds` set path
              --help, -h, -?            Show help
              --version, -v             Show version

            Help:
              nkds <command> help
              nkds help <command>

            Examples:
              nkds create --datastore {{ExampleDataStorePath}} {{ExampleSetPath}}
              nkds add --datastore {{ExampleDataStorePath}} {{ExampleInputPath}}
              nkds add --datastore {{ExampleIndexFilePath}} {{ExampleInputPath}}
              nkds export --datastore {{ExampleIndexFilePath}} --mask *.iso -out C:\Temp
              nkds export --datastore {{ExampleIndexFilePath}} --mask *.iso -out C:\Temp --format wux
              nkds list help
            """;

        private static string getMountHelp() =>
            $$"""
            nkds mount - Mount a datastore or set as a virtual filesystem

            Usage:
              nkds mount [options]
              nkds mount help

            Options:
              --datastore, -ds <path>   DataStore directory or `.nkds` set path
              --mount, -m <path>        Mount point
              --image, -i               Show full images as files (e.g. .iso)
              --filesystem, -fs         Show filesystem tree folders per image
              --system, -s              Show system entries, headers, and stored files
                                        (e.g. filesystem.yaml) for supported systems
              --update, -u              Update mode: enable rename support for images and app folders
              --allow-other             Allow other users to access the mount (Linux only)
              --uid <id|user>           Override file owner UID/User in the mount (Linux only)
              --gid <id|group>          Override group owner GID/Group in the mount (Linux only)
              --help, -h, -?            Show help

            When none of -i, -fs, or -s are specified the mount shows images and
            filesystem folders (the default behaviour).  When one or more flags are
            given, only the selected views are shown.

            Notes:
              - `--allow-other` is automatically enabled if `--uid` or `--gid` is specified.

            Example:
              nkds mount --datastore {{ExampleDataStorePath}} --mount {{ExampleMountPoint}}
              nkds mount --datastore {{ExampleIndexFilePath}} --mount {{ExampleMountPoint}}
              nkds mount -ds {{ExampleDataStorePath}} -m {{ExampleMountPoint}} -i -fs
              nkds mount -ds {{ExampleDataStorePath}} -m {{ExampleMountPoint}} -fs -s
              nkds mount -ds {{ExampleDataStorePath}} -m {{ExampleMountPoint}} --uid 1000
              nkds mount -ds {{ExampleDataStorePath}} -m {{ExampleMountPoint}} --uid root --gid root
            """;

        private static string getListHelp() =>
            $$"""
            nkds list - List images across sets

            Usage:
              nkds list [options]
              nkds list help

            Options:
              --datastore, -ds <path>   DataStore directory or `.nkds` set path
              --set, -s <path>          Filter by `.nkds` set path
              --system, -sys <name>     Filter by system
              --search, -sch <text>     Filter by image name text
              --format, -f <text|json>  Output format
              --removed, -r             Show removed images
              --help, -h, -?            Show help

            Example:
              nkds list --datastore {{ExampleDataStorePath}} --system Wii
              nkds list --datastore {{ExampleIndexFilePath}}
            """;

        private static string getAddHelp() =>
            $$"""
            nkds add - Import images through the NKit pipeline

            Usage:
              nkds add [options] <input ...>
              nkds add help

            Options:
              --datastore, -ds <path>   DataStore directory or `.nkds` set path
              --config, -cfg <path>     NKit config file
              --input, -in <path>       Add an input file, folder, or mask
              --recursive, -r           Scan input folders recursively
              --no-archives             Skip archive scanning during add
              --help, -h, -?            Show help

            Notes:
              - If `-ds` points to a directory, the target set is resolved automatically.
              - If `-ds` points to a `.nkds` file, that exact set is targeted.
              - `--config`, `--recursive`, `--no-archives`, and `--datastore` are translated to NKit parameters.
              - Extra NKit command-line parameters are passed through for add.
              - `-task dedupe` is always forced by `nkds add`.

            Example:
              nkds add --datastore {{ExampleDataStorePath}} {{ExampleInputPath}}
              nkds add --datastore {{ExampleIndexFilePath}} {{ExampleInputPath}}
              nkds add --datastore {{ExampleIndexFilePath}} -cfg nkit.yaml {{ExampleInputPath}} -v n
            """;

        private static string getAddDirHelp() =>
            $$"""
            nkds adddir - Store directory contents directly into a set

            Usage:
              nkds adddir [options] <directory ...>
              nkds adddir help

            Options:
              --datastore, -ds <path>   DataStore directory or `.nkds` set path
              --set, -s <name>          Target set name (default: resolved from path or "folders")
              --help, -h, -?            Show help

            Notes:
              - Each input directory becomes one Folder image named after the directory.
              - Files are stored directly via block deduplication — no NKit pipeline is used.
              - If `--set` is omitted, the set is resolved from the `.nkds` set path or defaults to "folders".
              - Non-existent input directories are reported as errors; remaining directories are still processed.

            Example:
              nkds adddir --datastore {{ExampleDataStorePath}} C:\MyFolder
              nkds adddir --datastore {{ExampleIndexFilePath}} C:\Folder1 C:\Folder2
              nkds adddir --datastore {{ExampleDataStorePath}} --set myfolders C:\Data
            """;

        private static string getOgmrHelp() =>
            $"""
            nkds ogmr - Batch import disc images routed by regex masks from an OGMR YAML file

            Usage:
              nkds ogmr <yaml-file> [input-paths...] [options]
              nkds ogmr --ogmr <yaml-file> [options]
              nkds ogmr help

            The OGMR (One Game, Many ROMs) command imports disc images into per-game sets.
            Each input file's name is matched against regex masks defined in the YAML file to
            determine which game set it belongs to. Sets are created automatically in the
            datastore directory.

            OGMR YAML format:
              games:
                - name: Game Title
                  masks:
                    - '^Pattern One(?= \(|\.iso|$)'
                    - '^Pattern Two(?= \(|\.iso|$)'
                - name: Another Game
                  masks:
                    - '^Another(?= \(|\.iso|$)'

              Each entry has a `name` (used as the set name) and `masks` (list of .NET regex
              patterns). Filenames are matched case-insensitively against masks in order;
              the first matching game entry wins.

            Options:
              --ogmr <path>               Path to the OGMR YAML file (alternative to positional)
              --datastore, -ds <path>     DataStore directory (must be a directory, not a `.nkds` set path)
              --input, -in <path>         Add an input file, folder, or mask
              --recursive, -r             Scan input folders recursively
              --no-archives               Skip archive scanning
              --shard-size, -ss <size>    Shard size for new sets (default: 50GiB)
              --block-size, -bs <size>    Block size for new sets (default: 64KiB)
              --config, -cfg <path>       NKit config file
              --help, -h, -?             Show help

            Notes:
              - `--datastore` must point to a directory, not a `.nkds` set path.
              - Sets are created automatically using the sanitized game name from the YAML.
              - Unmatched files (no regex match) are listed after the import summary.
              - Aux store routing is automatic when an aux `.nkds` set exists in the directory.
              - Extra NKit command-line parameters are passed through.

            Example:
              nkds ogmr games.yaml {ExampleInputPath} --datastore {ExampleDataStorePath}
              nkds ogmr games.yaml {ExampleInputPath} -ds {ExampleDataStorePath} -r
              nkds ogmr --ogmr games.yaml -in {ExampleInputPath} -ds {ExampleDataStorePath} --shard-size 25GiB
            """;

        private static string getRemoveHelp() =>
            $$"""
            nkds remove - Mark images as removed within a set.

            Usage:
              nkds remove --datastore <path.nkds> [image_id...]
              nkds remove help

            Options:
              --datastore, -ds <path>   `.nkds` set path
              --help, -h, -?            Show help

            Notes:
              - This flags the image records so they no longer appear in views.
              - Use the compact command afterwards to permanently clean up data.

            Example:
              nkds remove --datastore {{ExampleIndexFilePath}} 1 2 3
            """;

        private static string getRestoreHelp() =>
            $$"""
            nkds restore - Restores images previously marked as removed within a set.

            Usage:
              nkds restore --datastore <path.nkds> [image_id...]
              nkds restore help

            Options:
              --datastore, -ds <path>   `.nkds` set path
              --help, -h, -?            Show help

            Example:
              nkds restore --datastore {{ExampleIndexFilePath}} 1 2 3
            """;

        private static string getCompactHelp() =>
            $$"""
            nkds compact - Permanently delete marked images and unused blocks.

            Usage:
              nkds compact --datastore <path.nkds>
              nkds compact help

            Options:
              --datastore, -ds <path>   `.nkds` set path
              --help, -h, -?            Show help

            Example:
              nkds compact --datastore {{ExampleIndexFilePath}}
            """;

        private static string getVerifyHelp() =>
            $$"""
            nkds verify - Verify images through the NKit pipeline

            Usage:
              nkds verify --datastore <path.nkds> --mask <mask> [options]
              nkds verify help

            Options:
              --datastore, -ds <path>   `.nkds` set path
              --mask, -m <mask>         Image mask within the set
              --config, -cfg <path>     NKit config file
              --help, -h, -?            Show help

            Notes:
              - The NKit input is built as `{{ExampleIndexFilePath}}//*.iso`.
              - `-task verify` and `-v y` are always forced by `nkds verify`.
              - Extra NKit command-line parameters are passed through for verify.

            Example:
              nkds verify --datastore {{ExampleIndexFilePath}} --mask *.iso
              nkds verify --datastore {{ExampleIndexFilePath}} --mask *.iso -cfg nkit.yaml
            """;

        private static string getExportHelp() =>
            $$"""
            nkds export - Export images from a set through the NKit pipeline

            Usage:
              nkds export --datastore <path.nkds> --mask <mask> --output <folder> [options]
              nkds export help

            Options:
              --datastore, -ds <path>   `.nkds` set path
              --mask, -m <mask>         Image mask within the set
              --output, -out <folder>   Output folder
              --format, -f <value>      Optional NKit convert format value
              --config, -cfg <path>     NKit config file
              --help, -h, -?            Show help

            Notes:
              - The NKit input is built as `{{ExampleIndexFilePath}}//*.iso`.
              - If `--format` is omitted, `nkds export` uses the full stored format via `-task expand`.
              - If `--format` is specified, `nkds export` uses `-task convert -convert <value>`.
              - Extra NKit command-line parameters are passed through for export.

            Example:
              nkds export --datastore {{ExampleIndexFilePath}} --mask *.iso -out C:\Temp
              nkds export --datastore {{ExampleIndexFilePath}} --mask *.iso -out C:\Temp --format wux
              nkds export --datastore {{ExampleIndexFilePath}} --mask *.iso -out C:\Temp --format rvz:zstd:19:128k:16 -cfg nkit.yaml
            """;

        private static string getCreateHelp() =>
            $$"""
            nkds create - Create a new set in a datastore

            Usage:
              nkds create <set-name> [options]
              nkds create --datastore <path.nkds> [options]
              nkds create help

            Options:
              --datastore, -ds <path>   DataStore directory or `.nkds` set path
              --shard-size, -ss <size>  Shard data file size (default: 50GiB; 0 = single DB)
              --block-size, -bs <size>  Max stored item size (2KiB..2MiB)
              --help, -h, -?            Show help

            Example:
              nkds create --datastore {{ExampleDataStorePath}} {{ExampleSetPath}} --shard-size 50GiB --block-size 64KiB
              nkds create --datastore {{ExampleIndexFilePath}} --shard-size 50GiB --block-size 64KiB
            """;

        private static string getSetsHelp() =>
            """
            nkds sets - List sets in a datastore

            Usage:
              nkds sets [options]
              nkds sets help

            Options:
              --datastore, -ds <path>   DataStore directory
              --format, -f <text|json>  Output format
              --help, -h, -?            Show help

            Example:
              nkds sets --format json
            """;

        private static string getStatsHelp() =>
            $$"""
            nkds stats - Show statistics for one set or all sets

            Usage:
              nkds stats [options]
              nkds stats help

            Options:
              --datastore, -ds <path>        DataStore directory or `.nkds` set path
              --set, -s <path>               `.nkds` set path (optional)
              --details                      Include per-image stats details
              --format, -f <text|json|yaml>  Output format
              --help, -h, -?                 Show help

            Example:
              nkds stats --datastore {{ExampleDataStorePath}}
              nkds stats --set {{ExampleSetPath}} --details
              nkds stats --datastore {{ExampleIndexFilePath}}
            """;

        private static string getVersionHelp() =>
            """
            nkds version - Show version information

            Usage:
              nkds version
              nkds --version
            """;

        private static string getRollbackHelp() =>
            $$"""
            nkds rollback - Rollback a set to a specific image

            Usage:
              nkds rollback <image_id> [options]
              nkds rollback help

            Options:
              --datastore, -ds <path>   `.nkds` set path
              --help, -h, -?            Show help

            Notes:
              - Call rollback without an ID or use the list command to list images ordered by their insertion.

            Example:
              nkds rollback --datastore {{ExampleIndexFilePath}}
              nkds rollback --datastore {{ExampleIndexFilePath}} 42
            """;

                private static string getExampleDataStorePath()
        {
#if WINDOWS
            return @"D:\NKitData";
#elif LINUX
            return "/var/lib/nkitdata";
#else
            return "/mnt/nkitdata";
#endif
        }

        private static string getExampleMountPoint()
        {
#if WINDOWS
            return @"N:\";
#else
            return "/mnt/nkit";
#endif
        }

        private static string getExampleSetPath()
        {
#if WINDOWS
            return @"wii\redump.nkds";
#else
            return "wii/redump.nkds";
#endif
        }

        private static string getExampleInputPath()
        {
#if WINDOWS
            return @"D:\Roms\*.rvz";
#else
            return "/data/roms/*.rvz";
#endif
        }

        private static string getExampleIndexFilePath()
        {
#if WINDOWS
            return @"D:\NKitData\wii\redump.nkds";
#else
            return "/var/lib/nkitdata/wii/redump.nkds";
#endif
        }

    }
}
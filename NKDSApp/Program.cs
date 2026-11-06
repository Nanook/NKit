using System;
using System.Diagnostics;
using System.Text;

namespace Nanook.NKit.Vfs
{
    class Program
    {
        private static int Main(string[] args)
        {
            // Cursor safety net: restore the terminal cursor on any process exit (normal or Ctrl+C),
            // so a Ctrl+C during a Spectre interactive prompt cannot leave the cursor hidden on Linux.
            EnsureCursorRestoredOnExit();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Enable diagnostic trace output to console for library diagnostics
            Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
            Trace.AutoFlush = true;

            // Set up the config directory structure before anything else — including before the
            // interactive builder runs, so $configPath$/keys/, dats/, fix/ etc. already exist
            // when the user is filling in prompts. This is a no-op if already set up.
            bool configCreated = Nanook.NKit.AppSettings.EnsureDefaultConfigExists();
            if (configCreated)
            {
                Console.WriteLine($"Created default config: {Nanook.NKit.AppSettings.GetUserConfigFullName()}");
                Console.WriteLine();
            }

            // Interactive mode rules (mirrors nkit's CliFrontEnd logic):
            //
            //   1. No arguments at all in an interactive terminal → enter interactive (no seed).
            //
            //   2. First argument is a non-option, non-verb token (drag-drop positional path):
            //      → always enter interactive, pre-seeding that path as the datastore.
            //      This handles: nkds /path/to/store  or  nkds store.nkds  (file dropped on exe).
            //
            //   3. Any other argument combination → fall through to the normal CLI parse.
            //
            // Redirected/non-TTY cases for (1) fall through to normal parse (shows help for empty args).

            string seededDatastorePath = null;
            bool goInteractive = false;

            if (args == null || args.Length == 0)
            {
                goInteractive = NkdsTerminal.IsInteractive;
            }
            else if (!args[0].StartsWith("-") && !NkdsCommandLine.IsKnownCommand(args[0]) && NkdsTerminal.IsInteractive)
            {
                // First arg looks like a path, not a command — treat as drag-drop datastore.
                seededDatastorePath = args[0];
                goInteractive = true;
            }

            if (goInteractive)
            {
                string[] built = new NkdsInteractive(new Nanook.NKit.Cli.SpectrePrompter()).Build(seededDatastorePath);

                // Spectre.Console interactive prompts (SelectionPrompt etc.) hide the cursor via
                // ESC[?25l and may not restore it if the prompt exits in an unexpected state on
                // Linux. Explicitly restore the cursor here so the terminal is usable afterwards.
                try { if (!Console.IsOutputRedirected) Console.CursorVisible = true; } catch { }

                if (built == null)
                    return 0; // user cancelled
                args = built;
            }

            NkdsCommandRequest request = null;
            try
            {
                request = NkdsCommandLine.Parse(args);

                switch (request.Command)
                {
                    case NkdsCommand.Help:
                        NkdsCommandLine.WriteHelp(request.HelpTopic);
                        return 0;
                    case NkdsCommand.Version:
                        NkdsCommandLine.WriteVersion();
                        return 0;
                    case NkdsCommand.List:
                        NkdsCommandLine.ExecuteList(request);
                        return 0;
                    case NkdsCommand.Add:
                        NkdsCommandLine.ExecuteAdd(request);
                        return 0;
                    case NkdsCommand.Remove:
                        NkdsCommandLine.ExecuteRemove(request);
                        return 0;
                    case NkdsCommand.Restore:
                        NkdsCommandLine.ExecuteRestore(request);
                        return 0;
                    case NkdsCommand.Compact:
                        NkdsCommandLine.ExecuteCompact(request);
                        return 0;
                    case NkdsCommand.Export:
                        NkdsCommandLine.ExecuteExport(request);
                        return 0;
                    case NkdsCommand.Verify:
                        NkdsCommandLine.ExecuteVerify(request);
                        return 0;
                    case NkdsCommand.Create:
                        NkdsCommandLine.ExecuteCreate(request);
                        return 0;
                    case NkdsCommand.Rollback:
                        NkdsCommandLine.ExecuteRollback(request);
                        return 0;
                    case NkdsCommand.Sets:
                        NkdsCommandLine.ExecuteSets(request);
                        return 0;
                    case NkdsCommand.Stats:
                        NkdsCommandLine.ExecuteStats(request);
                        return 0;
                    case NkdsCommand.AddDir:
                        NkdsCommandLine.ExecuteAddDir(request);
                        return 0;
                    case NkdsCommand.Ogmr:
                        NkdsCommandLine.ExecuteOgmr(request);
                        return 0;
                    case NkdsCommand.Mount:
                        NkdsCommandLine.ExecuteMount(request);
                        return 0;
                    default:
                        throw new InvalidOperationException($"Unknown command '{request.Command}'.");
                }
            }
            catch (CommandLineException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
                NkdsCommand? helpTopic = request?.HelpTopic
                    ?? (request?.Command is { } command && command is not NkdsCommand.Help and not NkdsCommand.Version ? (NkdsCommand?)command : null)
                    ?? NkdsCommandLine.InferHelpTopic(args);
                NkdsCommandLine.WriteHelp(helpTopic);
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
#if DEBUG
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
#endif
                return 1;
            }
        }

        // Restore the terminal cursor on any process exit (normal or Ctrl+C), so a Ctrl+C during a
        // Spectre interactive prompt cannot leave the cursor hidden. Idempotent and Linux-safe.
        private static void EnsureCursorRestoredOnExit()
        {
            static void restore() { try { if (!Console.IsOutputRedirected) Console.CursorVisible = true; } catch { } }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => restore();
            Console.CancelKeyPress += (_, _) => restore(); // leave a.Cancel default → Ctrl+C still exits
        }
    }
}
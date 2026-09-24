using Nanook.NKit.Ogmr;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nanook.NKit.App
{
    class Program
    {
        static int Main(string[] args)
        {
            // Cursor safety net: Spectre hides the terminal cursor (ESC[?25l) during interactive
            // prompts and only restores it when a prompt returns normally. A Ctrl+C (SIGINT) during
            // a prompt tears the process down before that restore runs, leaving the cursor hidden —
            // the Linux complaint. Register process-exit + Ctrl+C hooks up front so the cursor is
            // always restored, however the process ends. Guarded/try-caught (the setter can throw on
            // some non-Windows terminals). We do NOT set a.Cancel here, so Ctrl+C still terminates.
            EnsureCursorRestoredOnExit();

            //byte[] sector = new byte[0x930 * 2];
            //sector[0x930 + 0x00C] = 0xC1;
            //sector[0x930 + 0x00D] = 0x25;
            //sector[0x930 + 0x00E] = 0x23;

            //long x = Ecm.SectorToLba(sector, 0x930);
            //x -= 0;
            //Ecm.ReconstructPrefix(ref sector, 0x930, true, x);
            //Debug.WriteLine($"{sector.ReadUInt32B(0x930 + 0xc):X}");

            //x -= 150;
            //Ecm.ReconstructPrefix(ref sector, 0x930, true, x);
            //Debug.WriteLine($"{sector.ReadUInt32B(0x930 + 0xc):X}");



            //Debug.WriteLine("");
            //byte[] d = new byte[0x100000];
            //for (int i = 0; i < d.Length; i+=4)
            //    d.WriteUInt32B(i, (uint)i);
            //MemoryStream msx = new MemoryStream(d);
            //CacheStream c = new CacheStream(msx);
            //byte[] data = new byte[0x1000];
            //c.Read(data, 0, -0x500);
            //c.Read(data, 0, 0x500);
            //c.Read(data, 0, -0x1000);
            //byte[] data2 = new byte[0x1000];
            //c.Read(data2, 0, 0x1000);
            //bool x = data.Equals(0, data2, 0, 0x1000);
            //if (x)
            //{
            //}

            //StringBuilder sb = new StringBuilder(1024);
            //byte[] x = "".HexToBytes();
            //for (int i = 0; i < x.Length; i++)
            //{
            //    if (i != 0)
            //        sb.Append(",");
            //    if (i % (4 * 4) == 0)
            //    {
            //        sb.AppendLine();
            //        if (i % (4 * 4 * 8) == 0)
            //            sb.AppendLine();
            //    }
            //    else if (i % 4 == 0)
            //        sb.Append(" ");

            //    sb.Append("0x" + ((byte)(x[i] ^ 0xdc)).ToString("X2"));
            //}
            //Trace.WriteLine(sb.ToString());


            int errorImages = 0;
            AppSettings settings;
            NKitProcessor p;
            Log log;

            CancellationTokenSource cancel = new CancellationTokenSource();

            // Set up the config directory structure before anything else — including before the
            // interactive builder runs, so $configPath$/keys/, dats/, fix/ etc. already exist
            // when the user is filling in prompts. This is a no-op if already set up.
            bool configCreated = AppSettings.EnsureDefaultConfigExists();
            if (configCreated)
            {
                Console.WriteLine($"Created default config: {AppSettings.GetUserConfigFullName()}");
                Console.WriteLine();
            }

            // New verb-based CLI front-end: parse argv, handle help/version, launch interactive when
            // there is no task and the terminal allows it, else translate to AppSettings overrides.
            // NOTE: the graceful Ctrl+C handler is registered AFTER this call — during the interactive
            // builder we want Ctrl+C to terminate the process (default behaviour), not be swallowed.
            Cli.CliResult cli = Cli.CliFrontEnd.Process(
                args ?? Array.Empty<string>(),
                AppSettings.GetVersion(),
                defaultTask: null,               // config default task pre-select (future: read from config)
                prompterFactory: () => new Nanook.NKit.Cli.SpectrePrompter());

            if (!cli.ShouldRun)
                return cli.ExitCode;

            // From here on (processing), Ctrl+C should cancel gracefully rather than kill the process.
            Console.CancelKeyPress += new ConsoleCancelEventHandler((s, a) =>
            {
                a.Cancel = true;
                cancel.Cancel();
            });

            try
            {
                settings = new AppSettings(cli.ConfigFile, cli.Overrides);
            }
            catch (Exception ex)
            {
                prompt(ex.Message.ToString(), args);
                return 3; //couldn't load the file or it was corrupt
            }

            LiveConsole live = new LiveConsole();
            live.ConsoleLevel = settings.ConsoleLevel; // drives Info-summary prefix strip/colour
            try
            {
                log = settings.GetLog(live.Sink, live.IsDynamic);
            }
            catch (Exception ex)
            {
                prompt(ex.Message.ToString(), args);
                return 4; //couldn't write to log
            }

            // OGMR routing setup
            FileRouter fileRouter = null;
            SystemType systemType = settings.SystemType != SystemType.NotSet ? settings.SystemType : SystemType.Default;
            string ogmrYamlPath = settings[systemType].OgmrYamlPath;

            // If not found in the resolved system, check if there's a system filter that has it
            if (string.IsNullOrWhiteSpace(ogmrYamlPath) && settings.SystemType == SystemType.NotSet)
            {
                // Try each system that might have ogmr configured
                foreach (SystemType st in new[] { SystemType.Wii, SystemType.WiiU, SystemType.GameCube })
                {
                    try
                    {
                        SystemSettings sysSettings = settings[st];
                        if (!string.IsNullOrWhiteSpace(sysSettings.OgmrYamlPath))
                        {
                            ogmrYamlPath = sysSettings.OgmrYamlPath;
                            systemType = st;
                            break;
                        }
                    }
                    catch { /* system not configured */ }
                }
            }
            if (!string.IsNullOrWhiteSpace(ogmrYamlPath))
            {
                if (settings.TaskType != TaskType.Dedupe)
                {
                    Console.Error.WriteLine("Error: -ogmr is only valid with the dedupe task.");
                    return 3;
                }

                if (!File.Exists(ogmrYamlPath))
                {
                    Console.Error.WriteLine($"Error: OGMR YAML file not found: '{ogmrYamlPath}'");
                    return 3;
                }

                try
                {
                    List<GameEntry> games = OgmrYamlParser.Parse(ogmrYamlPath);
                    fileRouter = new FileRouter(games);
                }
                catch (OgmrException ex)
                {
                    Console.Error.WriteLine($"Error: Failed to parse OGMR YAML: {ex.Message}");
                    return 3;
                }
            }

            try
            {
                if (!settings.Check(log))
                {
                    prompt("No input files specified or Task not set. Read the config file or documentation for usage.", args);
                    return 2;
                }
                if (settings?.In?.Length != 0)
                {
                    List<SourceFile> images = settings.TaskType == TaskType.Dedupe
                        ? SourceFiles.ScanGrouped(settings.In, settings.R, settings.Arc, true, log, cancel.Token)
                        : SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, cancel.Token).OrderBy(a => a.Name).ToList();

                    if (log.FileWriteError)
                    {
                        prompt("Could not write to log", args);
                        return 4; //couldn't write to log
                    }

                    int img = 0;
                    int totalFiles = images.Count;
                    int importedCount = 0;
                    int skippedCount = 0;
                    int failedCount = 0;

                    foreach (SourceFile file in images)
                    {
                        img++; // 1-based index; carried into the library so the Title line shows [X/Y]
                        if (cancel.IsCancellationRequested)
                            throw new HandledException("Cancelled!");

                        // Per-file OGMR routing
                        if (fileRouter != null)
                        {
                            string filename = Path.GetFileName(file.Name);
                            GameEntry match = fileRouter.Match(filename);
                            if (match == null)
                            {
                                log.Info(() => $"Unmatched: {filename}");
                                skippedCount++;
                                continue;
                            }

                            // Override only the set name on all system settings.
                            // Each system keeps its own shard/block/aux from its dedupe config.
                            settings.OverrideDedupeSetName(match.SanitizedName);
                        }

                        p = null;
                        try
                        {
                            p = new NKitProcessor(settings, file, live.Sink, live.IsDynamic);
                            p.ImageIndex = img;             // X of the [X/Y] title counter
                            p.ImageTotal = images.Count;    // Y
                            p.ProgressEvent += (s, e) =>
                            {
                                string stepName = (e.Steps != null && e.Step >= 0 && e.Step < e.Steps.Length) ? e.Steps[e.Step] : "";
                                if (e.IsStart || (e.Progress == 0f))
                                    live.BeginStep(e.Step, e.StepTotal, stepName);
                                else if (e.Progress >= 1f)
                                    // Commit THIS step's completion summary as a permanent log line
                                    // (above the footer) as each step finishes, so every step's line
                                    // stays and the next step's footer appears below it. The CRC /
                                    // completion detail replaces the live stats block on the kept line.
                                    live.CompleteStep(e.CompleteMessage);
                                else
                                    live.ReportProgress(e.Step, e.StepTotal, e.Progress, e.MiBPerSec);
                            };
                            p.StatsEvent += (s, e) => live.ReportStats(e);
                            NKitProcessor pRun = p;
                            NKitTaskResults results = null;
                            // Run the processing inside the dynamic footer scope so log lines scroll
                            // above the pinned progress line. In non-dynamic mode Run just invokes it.
                            live.Run(() => results = pRun.Process(cancel.Token));
                            if (!string.IsNullOrWhiteSpace(results.ErrorMsg))
                            {
                                failedCount++;
                                errorImages++;
                            }
                            else
                            {
                                importedCount++;
                            }
                        }
                        catch (Exception e)
                        {
                            if (p != null && p.ResultsWriteError)
                            {
                                Console.WriteLine(e.Message.ToString());
                                return 5; //couldn't write to results file
                            }
                            failedCount++;
                            errorImages++;
                        }
                    }

                    // Display processing summary when OGMR routing is active
                    if (fileRouter != null)
                    {
                        log.Info(() => Log.Divider);
                        log.Info(() => $"OGMR Summary: {totalFiles} total, {importedCount} imported, {skippedCount} skipped, {failedCount} failed");
                    }


                }
            }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested)
                {
                    Console.WriteLine();
                    Console.WriteLine("Cancelled!");
                    return 7;
                }

                prompt(ex.Message.ToString(), args);
                return 6;
            }
            finally
            {
                log.Dispose();
            }
            if (cancel.IsCancellationRequested)
                return 7;
            return errorImages == 0 ? 0 : 1;
        }

        // Restore the terminal cursor on any process exit (normal or Ctrl+C), so a Ctrl+C during a
        // Spectre interactive prompt cannot leave the cursor hidden. Idempotent and Linux-safe.
        private static void EnsureCursorRestoredOnExit()
        {
            static void restore() { try { if (!Console.IsOutputRedirected) Console.CursorVisible = true; } catch { } }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => restore();
            Console.CancelKeyPress += (_, _) => restore(); // leave a.Cancel default → Ctrl+C still exits
        }

        private static void prompt(string message, string[] args)
        {
            Console.WriteLine();
            Console.WriteLine(message);

            Console.WriteLine();
            Console.WriteLine($"Use '{Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0])} -h' for help");
            Console.WriteLine();
            if (args.Length == 0 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine();
                Console.Write("No arguments, press any key to exit");
                Console.ReadKey();
            }
        }

    }
}
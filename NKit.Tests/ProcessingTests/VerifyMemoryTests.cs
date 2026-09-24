using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;


namespace NKit.Tests.Full.TextDriven
{
    /// <summary>
    /// Memory-bloat reproduction harness. Verifies ONE image file N times in a loop, mirroring the
    /// NKit.UI verify path (build AppSettings -> scan -> NKitProcessor.Process -> read scalars ->
    /// NKitTaskResults.ReleaseHeavyReferences), and reports managed + process memory across the run
    /// so a leak/climb is visible.
    ///
    /// This test is a MANUAL diagnostic: it is skipped unless you point it at a real file via the
    /// NKIT_MEMTEST_FILE env var. Optional knobs:
    ///   NKIT_MEMTEST_FILE   - full path to the .nkit.gcz (or any) image to verify.  (REQUIRED to run)
    ///   NKIT_MEMTEST_ITERS  - iteration count (default 200).
    ///   NKIT_MEMTEST_RELEASE- "0" to SKIP ReleaseHeavyReferences() (worst-case leak); default "1"
    ///                          faithfully mirrors the UI which DOES release.
    ///   NKIT_MEMTEST_LOG    - optional path; the per-iteration CSV is appended here live (xUnit
    ///                          captures ITestOutputHelper so it never reaches the console; this
    ///                          lets kirotest.ps1 tail progress in real time).
    ///
    /// Run via the test exe, e.g.:
    ///   $env:NKIT_MEMTEST_FILE = 'D:\roms\game.nkit.gcz'; $env:NKIT_MEMTEST_ITERS = '300'
    ///   .\NKit.Tests.exe -method '*VerifyMemoryTests.VerifyFileManyTimes'
    /// </summary>
    [Trait("Area", "Full")]
    [Trait("Group", "TextDriven")]
    public class VerifyMemoryTests
    {
        private readonly ITestOutputHelper _out;

        public VerifyMemoryTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void VerifyFileManyTimes()
        {
            string file = Environment.GetEnvironmentVariable("NKIT_MEMTEST_FILE");
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                _out.WriteLine($"SKIPPED: set NKIT_MEMTEST_FILE to an existing image file to run. Got: '{file}'");
                return; // treated as pass; this is a manual diagnostic
            }

            int iters = 200;
            string itersEnv = Environment.GetEnvironmentVariable("NKIT_MEMTEST_ITERS");
            if (!string.IsNullOrWhiteSpace(itersEnv) && int.TryParse(itersEnv, out int parsed) && parsed > 0)
                iters = parsed;

            // Release mode mirrors the UI's retention of one NKitTaskResults per row:
            //   none  - keep the full result (worst case; reproduces the raw leak)
            //   heavy - call ReleaseHeavyReferences() (nulls StepResults, KEEPS Scan/Source)
            //   all   - call ReleaseAllReferences() (also nulls Scan/Source) — the UI fix
            string mode = (Environment.GetEnvironmentVariable("NKIT_MEMTEST_RELEASE") ?? "all").Trim().ToLowerInvariant();
            if (mode != "none" && mode != "heavy" && mode != "all") mode = "all";
            string logPath = Environment.GetEnvironmentVariable("NKIT_MEMTEST_LOG");

            Process proc = Process.GetCurrentProcess();

            void emit(string line)
            {
                _out.WriteLine(line);
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
                }
            }

            emit($"=== VerifyMemoryTests ===");
            emit($"file    : {file}");
            emit($"size    : {new FileInfo(file).Length:N0} bytes");
            emit($"iters   : {iters}");
            emit($"release : {mode}  (none=keep all, heavy=ReleaseHeavyReferences, all=ReleaseAllReferences [UI fix])");
            emit("");
            emit("iter,verify,gcHeldMB,workingSetMB,privateMB,gc0,gc1,gc2,ms");

            long baselineManaged = -1;
            long baselineWorking = -1;

            // Retain every result for the whole run, exactly like the UI keeps one per row. This is
            // what makes a per-image leak visible — the earlier version dropped each result, which
            // is why the CLI-style loop looked flat.
            List<NKitTaskResults> retained = new System.Collections.Generic.List<NKitTaskResults>(iters);

            for (int i = 1; i <= iters; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                string verify = verifyOnce(file, mode, retained);
                sw.Stop();

                // Force a settled measurement (NKitProcessor already GCs internally, but be explicit).
                long managed = GC.GetTotalMemory(forceFullCollection: true);
                proc.Refresh();
                long working = proc.WorkingSet64;
                long priv = proc.PrivateMemorySize64;

                if (baselineManaged < 0) { baselineManaged = managed; baselineWorking = working; }

                emit(string.Format(
                    "{0},{1},{2:F1},{3:F1},{4:F1},{5},{6},{7},{8}",
                    i, verify,
                    managed / 1048576.0,
                    working / 1048576.0,
                    priv / 1048576.0,
                    GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                    sw.ElapsedMilliseconds));
            }

            long finalManaged = GC.GetTotalMemory(forceFullCollection: true);
            proc.Refresh();
            long finalWorking = proc.WorkingSet64;

            double managedGrowthMB = (finalManaged - baselineManaged) / 1048576.0;
            double workingGrowthMB = (finalWorking - baselineWorking) / 1048576.0;

            emit("");
            emit($"retained results                    : {retained.Count}");
            emit($"managed heap growth (iter1 -> final): {managedGrowthMB:F1} MB");
            emit($"working set growth  (iter1 -> final): {workingGrowthMB:F1} MB");
            emit($"per-iteration managed growth        : {(iters > 1 ? managedGrowthMB / (iters - 1) : 0):F3} MB/iter");
            // Touch retained so it can't be optimized away before the final measurement.
            emit($"(retained sentinel: {retained.Count} results held)");
        }

        // One faithful verify pass, mirroring NKitService.Run per-file: fresh AppSettings, scan to a
        // SourceFile, NKitProcessor.Process, set the result on the "row" (read scalar verify fields),
        // release per the mode, then RETAIN the result in the list exactly as the UI keeps it per row.
        private static string verifyOnce(string file, string mode, System.Collections.Generic.List<NKitTaskResults> retained)
        {
            SystemPresetSettings presets = new SystemPresetSettings() { Task = TaskType.Verify };
            presets.In.Add(file);

            AppSettings settings = new AppSettings(presets);
            StringBuilder sink = new StringBuilder();
            CancellationTokenSource cancel = new CancellationTokenSource();

            string verifyResult;
            using (Log log = settings.GetLog((ms, lv) => { }))
            {
                SourceFile img = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, null)
                                            .FirstOrDefault();
                if (img == null)
                    return "NoSource";

                NKitProcessor p = new NKitProcessor(settings, img, (ms, lv) => sink.Append(ms));
                NKitTaskResults results = p.Process(cancel.Token);

                // Read the scalar verify outcome BEFORE releasing (UI reads results, then releases).
                verifyResult = results.VerifyResult.ToString();

                if (mode == "heavy")
                    results.ReleaseHeavyReferences();
                else if (mode == "all")
                    results.ReleaseAllReferences();
                // mode == "none": keep everything (worst case)

                // Retain the result for the whole run, like a UI row.
                retained.Add(results);
            }

            return verifyResult;
        }
    }
}
using Nanook.NKit.SampleProcessingApp.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Nanook.NKit.SampleProcessingApp
{
    // Reference consumer of the NKit library. Demonstrates the intended embedding model:
    //   1. Configure a task + source (as the CLI/UI do).
    //   2. Process, receiving each processed SECTION BLOCK via a callback - no disk output.
    //   3. A consumer-side ISectionHandler does something useful with the feed (expand / crc /
    //      extract) - each ported down from NKit's own output Steps using ONLY public types.
    //   4. Read the final result (Scan / verify / checksums).
    //
    // This file is written as the public API SHOULD look for an embedder. Where it does not
    // compile, that is a real gap in NKit's public surface to be closed deliberately (either
    // promote a member to public, or expose it via a read-only interface) - NOT by blanket
    // publicising internals. See NKitVault/10 Refactor/Public API and Embedding Model.md.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: NKit.SampleProcessingApp <image-path> [task] [output-dir]");
                Console.WriteLine("  task       : scan (default) | expand | crc | extract | rebuild");
                Console.WriteLine("  output-dir : where expand/extract/rebuild write (default: .\\nkit-sample-out)");
                Console.WriteLine();
                Console.WriteLine("  scan    - just process and print the result (no section handler)");
                Console.WriteLine("  expand  - reconstruct the full decoded image from the section feed");
                Console.WriteLine("  crc     - fold each block's CRC into a whole-image CRC and verify it");
                Console.WriteLine("  extract - write the image's files to disk from the section feed");
                Console.WriteLine("  rebuild - pure passthrough: stream source → WiiDiscBuilder → output ISO");
                return 1;
            }

            string inputPath = args[0];
            string taskArg = args.Length > 1 ? args[1].ToLowerInvariant() : "scan";
            string outputDir = args.Length > 2 ? args[2] : Path.Combine(Directory.GetCurrentDirectory(), "nkit-sample-out");

            // Optional: --replacements <file> — pipe-separated discpath|localpath lines
            string replacementsFile = null;
            for (int i = 3; i < args.Length - 1; i++)
                if (args[i].ToLowerInvariant() == "--replacements")
                    replacementsFile = args[i + 1];

            CancellationTokenSource cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancel.Cancel(); };

            // ---- 1. Configuration ─────────────────────────────────────────────────────────────
            // For task=rebuild: run NKit's extract task (100% file coverage via SkipToImageOffsetSet)
            // to a temp directory. After extraction, WiiDiscRebuildHandler reads files from disk.
            // No injection into NKitProcessor — the extract task writes to disk in the normal way.
            string nkitTask = taskArg == "rebuild" ? "extract" : "convert";
            Action<string, LogLevel> consoleLog = (message, level) => Console.WriteLine($"[{level}] {message}");

            // For task=rebuild: extract to a unique temp directory, clean up after build.
            // For other tasks: use a subdirectory so NKit's own output doesn't conflict.
            string nkitTempOut = taskArg == "rebuild"
                ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nkit_rebuild_{System.Guid.NewGuid():N}")
                : System.IO.Path.Combine(outputDir, "__nkit_convert_output");
            System.IO.Directory.CreateDirectory(nkitTempOut);

            AppSettings settings = new AppSettings(
                configFileName: "n",
                overrideValues: new Dictionary<string, string>
                {
                    ["in"]   = inputPath,
                    ["task"] = nkitTask,
                    ["out"]  = nkitTempOut,
                });

            Log log = settings.GetLog(consoleLog);

            // ---- 2. Discover the source -------------------------------------------------------
            List<SourceFile> images = new List<SourceFile>(
                SourceFiles.Scan(settings.In, settings.R, settings.Arc, validOnly: true, log: log, cancel: cancel.Token));

            if (images.Count == 0)
            {
                Console.WriteLine($"No processable image found at: {inputPath}");
                return 2;
            }

            int exit = 0;
            foreach (SourceFile file in images)
            {
                Console.WriteLine($"Processing: {file.Name}  (task={taskArg})");

                // ---- 3. Create the processor --------------------------------------------------
                NKitProcessor processor = new NKitProcessor(settings, file, consoleLog);

                processor.ProgressEvent += (s, e) =>
                {
                    if (e.Progress >= 1f)
                        Console.WriteLine($"  step {e.Step + 1}/{e.StepTotal} complete{e.CompleteMessage}");
                };

                // ---- 4. Pick a section handler and wire the embedding callback ----------------
                ISectionHandler handler = taskArg == "rebuild"
                    ? null
                    : CreateHandler(taskArg, outputDir, file.Name, replacementsFile);

                if (taskArg == "rebuild")
                {
                    // For rebuild: run NKit's extract task to disk (no injection), capture
                    // disc/partition headers from OnSection, then build the output disc from
                    // the extracted files using WiiDiscRebuildHandler.Build().
                    byte[] capturedImageHeader     = null;
                    byte[] capturedPartitionHeader = null;

                    // Capture the last PartitionHeader — on standard Wii discs UPDATE comes
                    // before DATA, so the last one captured is always the DATA (game) partition.
                    processor.OnSection = block =>
                    {
                        if (block.Type == AreaType.ImageHeader)
                            capturedImageHeader = block.ReadBytes(0, (int)block.FsSize);
                        else if (block.Type == AreaType.PartitionHeader)
                            capturedPartitionHeader = block.ReadBytes(0, (int)block.FsSize);
                    };

                    NKitTaskResults results2 = processor.Process(cancel.Token);

                    if (!string.IsNullOrWhiteSpace(results2.ErrorMsg))
                        Console.WriteLine($"  NKit extract: {results2.ErrorMsg}");

                    // Build the output disc from the extracted files.
                    // NKit creates a named subdirectory inside nkitTempOut (e.g. "DiscName~/").
                    // Find the DATA/sys directory to confirm extraction succeeded.
                    string extractRoot = nkitTempOut;
                    string dataDir = System.IO.Path.Combine(extractRoot, "DATA");
                    if (!System.IO.Directory.Exists(dataDir))
                    {
                        // Look one level deeper — NKit may have created a named subdirectory
                        string sub = System.IO.Directory.GetDirectories(extractRoot).FirstOrDefault();
                        if (sub != null && System.IO.Directory.Exists(System.IO.Path.Combine(sub, "DATA")))
                            extractRoot = sub;
                    }

                    string outIso = System.IO.Path.Combine(outputDir,
                        System.IO.Path.GetFileNameWithoutExtension(file.Name) + "_rebuilt.iso");
                    System.IO.Directory.CreateDirectory(outputDir);

                    var rebuildHandler = new WiiDiscRebuildHandler(outIso,
                        WiiDiscRebuildHandler.LoadReplacements(replacementsFile));
                    rebuildHandler.Build(capturedImageHeader, capturedPartitionHeader, extractRoot);

                    // Clean up NKit's extracted files — the rebuilt ISO is all we need
                    try { System.IO.Directory.Delete(nkitTempOut, recursive: true); }
                    catch (Exception ex) { Console.WriteLine($"  (extract cleanup failed: {ex.Message})"); }

                    continue;
                }

                if (handler != null)
                    processor.OnSection = handler.OnSection;

                // ---- 5. Run -------------------------------------------------------------------
                NKitTaskResults results = processor.Process(cancel.Token);

                // ---- 5b. For extract: run a second pass for geometry recovery ----------------
                if (handler is ExtractHandler)
                {
                    // Single-pass now handles overlaps — no second pass needed.
                }

                // ---- 6. Read the final result -------------------------------------------------
                // Note: task=convert may fail with ErrorMsg if it can't write its own output
                // (e.g. path issues), but our RebuildHandler may have completed successfully.
                // Log the error but don't skip handler.Completed().
                if (!string.IsNullOrWhiteSpace(results.ErrorMsg))
                {
                    Console.WriteLine($"  NKit result: {results.ErrorMsg}");
                    exit = 5;
                }
                else
                {
                    Console.WriteLine($"  system      : {results.System}");
                    Console.WriteLine($"  container   : {results.ContainerType}");
                    Console.WriteLine($"  size        : {results.Size:N0} bytes");
                    Console.WriteLine($"  crc         : {results.CRC:X8}");
                    if (!string.IsNullOrEmpty(results.VerifyChecksum))
                        Console.WriteLine($"  checksum    : {results.VerifyChecksum}");
                }
                // ---- 7. Let the handler flush its sink and report -----------------------------
                handler?.Completed(results);

                // Clean up NKit's own convert output (we only need our handler's rebuilt ISO)
                if (System.IO.Directory.Exists(nkitTempOut))
                {
                    Console.WriteLine($"  Cleaning NKit temp output: {nkitTempOut}");
                    try { System.IO.Directory.Delete(nkitTempOut, recursive: true); }
                    catch (Exception ex) { Console.WriteLine($"  (cleanup failed: {ex.Message})"); }
                }

                // The full Scan (fingerprint) is available for the consumer to inspect / persist.
                Scan scan = results.Scan;
                if (scan != null)
                    Console.WriteLine($"  scan crc    : {scan.Crc:X8}");
            }

            return exit;
        }

        /// <summary>
        /// Maps the task argument to a consumer-side section handler. "scan" attaches no handler
        /// (just prints the result); the others attach a ported-down Step implementation.
        /// </summary>
        private static ISectionHandler CreateHandler(string task, string outputDir, string imageName,
                                                     string replacementsFile = null)
        {
            switch (task)
            {
                case "expand":
                    return new SaveHandler(outputDir, imageName);
                case "crc":
                    return new CrcHandler();
                case "extract":
                    return new ExtractHandler(outputDir, imageName);
                case "rebuild":
                    return new RebuildHandler(outputDir, imageName, RebuildHandler.LoadReplacements(replacementsFile));
                case "scan":
                    return null;
                default:
                    Console.WriteLine($"Unknown task '{task}' - defaulting to scan (no handler).");
                    return null;
            }
        }
    }
}

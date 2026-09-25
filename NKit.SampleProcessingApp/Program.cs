using Nanook.NKit.SampleProcessingApp.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
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
                Console.WriteLine("  task       : scan (default) | expand | crc | extract");
                Console.WriteLine("  output-dir : where expand/extract write (default: .\\nkit-sample-out)");
                Console.WriteLine();
                Console.WriteLine("  scan    - just process and print the result (no section handler)");
                Console.WriteLine("  expand  - reconstruct the full decoded image from the section feed");
                Console.WriteLine("  crc     - fold each block's CRC into a whole-image CRC and verify it");
                Console.WriteLine("  extract - write the image's files to disk from the section feed");
                return 1;
            }

            string inputPath = args[0];
            string taskArg = args.Length > 1 ? args[1].ToLowerInvariant() : "scan";
            string outputDir = args.Length > 2 ? args[2] : Path.Combine(Directory.GetCurrentDirectory(), "nkit-sample-out");

            CancellationTokenSource cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancel.Cancel(); };

            // ---- 1. Configuration -------------------------------------------------------------
            // An embedder configures NKit the same way the CLI does: via AppSettings. We use the
            // config-overrides constructor with "cfg=n" (no config file on disk) and canonical keys.
            //
            // The section handlers below observe the feed regardless of the NKit task. We always
            // drive a FULL-READ task (verify) so every block - including every FileSystem block with
            // its parsed file items - streams through OnSection in image order. We deliberately do
            // NOT use NKit's own "extract" task: that task attaches its own output step which does
            // area-skip optimisation for its on-disk extraction, and a consumer riding the same feed
            // would only see the un-skipped remainder. A verify read gives the handler the complete,
            // linear feed and lets the consumer own all extraction. The FST is parsed up front for
            // every FileSystem area regardless of task, so file items are always populated.
            string nkitTask = "verify";
            Action<string, LogLevel> consoleLog = (message, level) => Console.WriteLine($"[{level}] {message}");

            AppSettings settings = new AppSettings(
                configFileName: "n", // "n" => no config file
                overrideValues: new Dictionary<string, string>
                {
                    ["in"] = inputPath,
                    ["task"] = nkitTask,
                    ["v"] = "y", // verify (full read + checksum)
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
                // The task chooses which ported-down Step logic runs over the linear block feed.
                ISectionHandler handler = CreateHandler(taskArg, outputDir, file.Name);
                if (handler != null)
                    processor.OnSection = handler.OnSection;

                // ---- 5. Run -------------------------------------------------------------------
                NKitTaskResults results = processor.Process(cancel.Token);

                // ---- 6. Read the final result -------------------------------------------------
                if (!string.IsNullOrWhiteSpace(results.ErrorMsg))
                {
                    Console.WriteLine($"  FAILED: {results.ErrorMsg}");
                    exit = 5;
                    continue;
                }

                Console.WriteLine($"  system      : {results.System}");
                Console.WriteLine($"  container   : {results.ContainerType}");
                Console.WriteLine($"  size        : {results.Size:N0} bytes");
                Console.WriteLine($"  crc         : {results.CRC:X8}");
                Console.WriteLine($"  verify      : {results.VerifyResult} ({results.VerifyType})");
                if (!string.IsNullOrEmpty(results.VerifyChecksum))
                    Console.WriteLine($"  checksum    : {results.VerifyChecksum}");

                // ---- 7. Let the handler flush its sink and report -----------------------------
                handler?.Completed(results);

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
        private static ISectionHandler CreateHandler(string task, string outputDir, string imageName)
        {
            switch (task)
            {
                case "expand":
                    return new SaveHandler(outputDir, imageName);
                case "crc":
                    return new CrcHandler();
                case "extract":
                    return new ExtractHandler(outputDir, imageName);
                case "scan":
                    return null;
                default:
                    Console.WriteLine($"Unknown task '{task}' - defaulting to scan (no handler).");
                    return null;
            }
        }
    }
}

using Nanook.NKit.Configuration;
using Nanook.NKit.Ogmr;
using Nanook.NKit.Steps.Shared;
using NKDS;
using NKDS.Models;
using NKDS.Mount;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit.Vfs
{
    internal enum NkdsCommand
    {
        Mount,
        List,
        Add,
        Remove,
        Restore,
        Compact,
        Export,
        Verify,
        Create,
        Rollback,
        Sets,
        Stats,
        Help,
        Version,
        AddDir,
        Ogmr,
    }

    internal sealed class NkdsCommandRequest
    {
        public NkdsCommand Command { get; init; } = NkdsCommand.Mount;
        public NkdsCommand? HelpTopic { get; init; }
        public string DataStorePath { get; init; } = string.Empty;
        public string Mask { get; init; }
        public string ConvertFormat { get; init; }
        public string OutputPath { get; init; }
        public string MountPoint { get; init; } = string.Empty;
        public string ConfigFile { get; init; }
        public string SetName { get; init; }
        public string SystemName { get; init; }
        public string Search { get; init; }
        public string OgmrYamlPath { get; init; }
        public string[] Inputs { get; init; } = Array.Empty<string>();
        public string ShardSizeText { get; init; }
        public string BlockSizeText { get; init; }
        public bool Recursive { get; init; }
        public bool ScanArchives { get; init; } = true;
        public bool IncludeImageDetails { get; init; }
        public bool ShowRemoved { get; init; }
        public bool MountShowImage { get; init; } = true;
        public bool MountShowFileSystem { get; init; } = true;
        public bool MountShowSystem { get; init; }
        public bool MountUpdateMode { get; init; }
        public bool MountAllowOther { get; init; }
        public uint? MountUid { get; init; }
        public uint? MountGid { get; init; }
        public int MountMaxFsYamlSizeKiB { get; init; } = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB;
        public string Format { get; init; } = "text";
        public string[] ExtraArgs { get; init; } = Array.Empty<string>();
    }

    internal sealed class CommandLineException : Exception
    {
        public CommandLineException(string message)
            : base(message)
        {
        }
    }

    internal static class NkdsCommandLine
    {
        private sealed class ParseState
        {
            public string DataStorePath { get; set; } = string.Empty;
            public string Mask { get; set; }
            public string ConvertFormat { get; set; }
            public string OutputPath { get; set; }
            public string MountPoint { get; set; } = string.Empty;
            public string ConfigFile { get; set; }
            public string SetName { get; set; }
            public string SystemName { get; set; }
            public string Search { get; set; }
            public string OgmrYamlPath { get; set; }
            public List<string> Inputs { get; } = [];
            public string ShardSizeText { get; set; }
            public string BlockSizeText { get; set; }
            public bool Recursive { get; set; }
            public bool ScanArchives { get; set; } = true;
            public bool IncludeImageDetails { get; set; }
            public bool ShowRemoved { get; set; }
            public bool? MountShowImage { get; set; }
            public bool? MountShowFileSystem { get; set; }
            public bool? MountShowSystem { get; set; }
            public bool? MountUpdateMode { get; set; }
            public bool MountAllowOther { get; set; }
            public uint? MountUid { get; set; }
            public uint? MountGid { get; set; }
            public int MountMaxFsYamlSizeKiB { get; set; } = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB;
            public string Format { get; set; } = "text";
            public bool ShowHelp { get; set; }
            public bool ShowVersion { get; set; }
            public NkdsCommand? HelpTopic { get; set; }
            // True only when the first arg was an explicit command verb. When false, 'command' is the
            // defaulted Mount sentinel, which must NOT be treated as a help topic (so 'nkds --help'
            // shows the GENERAL help, matching 'nkit --help').
            public bool ExplicitCommand { get; set; }
            public List<string> ExtraArgs { get; } = [];
        }

        public static NkdsCommandRequest Parse(string[] args)
        {
            ParseState state = new ParseState
            {
                DataStorePath = getDefaultDataStorePath(),
                MountPoint = getDefaultMountPoint(),
            };

            NkdsCommand command = NkdsCommand.Mount;

            int index = 0;
            if (args.Length > 0 && !isOption(args[0]))
            {
                command = parseCommand(args[0]);
                state.ExplicitCommand = true;
                index = 1;
            }

            while (index < args.Length)
            {
                string token = args[index];
                if (tryReadHelpTopic(command, token, state))
                {
                    index++;
                    continue;
                }

                if (tryReadCommandValue(command, token, state))
                {
                    index++;
                    continue;
                }

                applyOption(args, ref index, token, command, state);
            }

            if (state.ShowHelp)
                command = NkdsCommand.Help;
            else if (state.ShowVersion)
                command = NkdsCommand.Version;

            resolveDataStoreInput(command, state);

            validate(command, state.DataStorePath, state.MountPoint, state.SetName, state.Format, state.Mask, state.ConvertFormat, state.OutputPath, state.SystemName, state.Inputs.ToArray(), state.ConfigFile, state.ShardSizeText, state.BlockSizeText, state.OgmrYamlPath);

            return createRequest(command, state);
        }

        /// <summary>
        /// Returns true when <paramref name="value"/> is a recognised nkds command verb or alias.
        /// Used by Program.cs to distinguish a verb from a drag-dropped path when the first
        /// argument is a non-option token.
        /// </summary>
        public static bool IsKnownCommand(string value)
            => tryParseCommand(value, out _);

        public static NkdsCommand? InferHelpTopic(string[] args)
        {
            if (args == null || args.Length == 0)
                return null;

            if (!isOption(args[0]))
            {
                if (!tryParseCommand(args[0], out NkdsCommand command))
                    return null;

                if (command == NkdsCommand.Help && args.Length > 1 && !isOption(args[1]) && tryParseCommand(args[1], out NkdsCommand helpTopic))
                    return helpTopic;

                return command is NkdsCommand.Help or NkdsCommand.Version ? null : command;
            }

            return null;
        }

        public static void ExecuteCreate(NkdsCommandRequest request)
        {
            long shardSize = string.IsNullOrWhiteSpace(request.ShardSizeText)
                ? parseSizeToBytes("50GiB")
                : parseSizeToBytes(request.ShardSizeText);
            int blockSize = string.IsNullOrWhiteSpace(request.BlockSizeText)
                ? 0x10000
                : parseBlockSizeToBytes(request.BlockSizeText);

            int? auxBlockSize = DataStore.GetAuxBlockSize(request.DataStorePath);
            if (auxBlockSize != null && auxBlockSize.Value != blockSize)
            {
                Console.Error.WriteLine($"ERROR: Block size {blockSize} conflicts with aux store block size {auxBlockSize.Value}. Forcing block size to {auxBlockSize.Value}.");
                blockSize = auxBlockSize.Value;
            }

            using NkdsOperations operations = new NkdsOperations();
            OperationResult result = operations.CreateSet(request.DataStorePath, request.SetName, shardSize, blockSize);

            if (!result.Success)
            {
                string reason = result.Errors.Count > 0 ? result.Errors[0].Reason : "Unknown error";
                throw new CommandLineException($"Failed to create set: {reason}");
            }

            Console.WriteLine($"Created set: {request.SetName}");
            Console.WriteLine($"Index DB: {Path.Combine(request.DataStorePath, request.SetName + DataStore.DatabaseFileExtension)}");
            Console.WriteLine($"Shard Size: {formatBytes(shardSize)}");
            Console.WriteLine($"Block Size: {formatBytes(blockSize)}");
        }

        public static void ExecuteAdd(NkdsCommandRequest request)
        {
            AppSettings settings = createImportAppSettings(request);
            CancellationTokenSource cancel = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancel.Cancel();
            };

            Log log = null;
            try
            {
                Console.CancelKeyPress += handler;
                log = settings.GetLog((message, level) => Console.Write(message));

                if (!settings.Check(log))
                    throw new CommandLineException("No input files were specified.");

                List<SourceFile> images = SourceFiles
                    .ScanGrouped(settings.In, settings.R, settings.Arc, true, log, cancel.Token);

                if (images.Count == 0)
                {
                    Console.WriteLine("No valid images were found.");
                    return;
                }

                Console.WriteLine();
                Console.WriteLine($"Images queued: {images.Count:N0}");
                Console.WriteLine($"Target set: {request.SetName}");
                Console.WriteLine($"DataStore path: {request.DataStorePath}");
                Console.WriteLine();

                int errors = 0;
                int imageIndex = 0;
                foreach (SourceFile file in images)
                {
                    imageIndex++;
                    if (cancel.IsCancellationRequested)
                        throw new HandledException("Cancelled!");

                    log.Info(() => $"--[{imageIndex}/{images.Count}]--");

                    try
                    {
                        NKitProcessor processor = new NKitProcessor(settings, file, (message, level) => Console.Write(message));
                        NKitTaskResults result = processor.Process(cancel.Token);
                        if (!string.IsNullOrWhiteSpace(result.ErrorMsg))
                        {
                            errors++;
                            Console.WriteLine(result.ErrorMsg);
                        }
                    }
                    catch (HandledException ex)
                    {
                        errors++;
                        Console.WriteLine(ex.FriendlyErrorMessage.TrimEnd());
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.WriteLine($"Failed to import '{file.Name}': {ex.Message}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Imported {images.Count - errors:N0} of {images.Count:N0} image(s).");

                if (errors != 0)
                    throw new HandledException($"{errors:N0} image(s) failed to import.");
            }
            finally
            {
                Console.CancelKeyPress -= handler;
                log?.Dispose();
                cancel.Dispose();


            }
        }

        public static void ExecuteOgmr(NkdsCommandRequest request)
        {
            // 1. Parse YAML — validates structure and compiles all regexes upfront
            List<GameEntry> games;
            try
            {
                games = OgmrYamlParser.Parse(request.OgmrYamlPath);
            }
            catch (OgmrException ex)
            {
                throw new CommandLineException(ex.Message);
            }

            // 2. Create DataStore directory if needed
            if (!Directory.Exists(request.DataStorePath))
                Directory.CreateDirectory(request.DataStorePath);

            // 3. Scan input files using the same infrastructure as the add command
            AppSettings settings = createImportAppSettings(request);
            CancellationTokenSource cancel = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancel.Cancel();
            };

            Log log = null;
            try
            {
                Console.CancelKeyPress += handler;
                log = settings.GetLog((message, level) => Console.Write(message));

                List<SourceFile> images = SourceFiles
                    .ScanGrouped(settings.In, settings.R, settings.Arc, true, log, cancel.Token);

                if (images.Count == 0)
                {
                    Console.WriteLine("No valid images were found.");
                    return;
                }

                // 4. Create file router for matching filenames to game entries
                FileRouter router = new FileRouter(games);

                Console.WriteLine();
                Console.WriteLine($"Images queued: {images.Count:N0}");
                Console.WriteLine($"Game entries: {games.Count:N0}");
                Console.WriteLine($"DataStore path: {request.DataStorePath}");
                Console.WriteLine();

                // 5. Per-file routing and processing loop
                int imported = 0;
                int failed = 0;
                List<string> unmatchedFiles = new List<string>();
                int imageIndex = 0;

                foreach (SourceFile file in images)
                {
                    imageIndex++;

                    if (cancel.Token.IsCancellationRequested)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Cancelled.");
                        break;
                    }

                    string filename = Path.GetFileName(file.Name);
                    GameEntry matchedGame = router.Match(filename);

                    if (matchedGame == null)
                    {
                        unmatchedFiles.Add(filename);
                        continue;
                    }

                    Console.WriteLine($"[{imageIndex}/{images.Count}] {filename} -> {matchedGame.SanitizedName}");

                    try
                    {
                        // Build a per-file request targeting this game's sanitized set name
                        NkdsCommandRequest perFileRequest = new NkdsCommandRequest
                        {
                            Command = request.Command,
                            DataStorePath = request.DataStorePath,
                            SetName = matchedGame.SanitizedName,
                            ConfigFile = request.ConfigFile,
                            ShardSizeText = request.ShardSizeText,
                            BlockSizeText = request.BlockSizeText,
                            Recursive = request.Recursive,
                            ScanArchives = request.ScanArchives,
                            Inputs = request.Inputs,
                            ExtraArgs = request.ExtraArgs
                        };

                        AppSettings perFileSettings = createImportAppSettings(perFileRequest);
                        NKitProcessor processor = new NKitProcessor(perFileSettings, file, (message, level) => Console.Write(message));
                        NKitTaskResults result = processor.Process(cancel.Token);

                        if (!string.IsNullOrWhiteSpace(result.ErrorMsg))
                        {
                            failed++;
                            Console.WriteLine(result.ErrorMsg);
                        }
                        else
                        {
                            imported++;
                        }
                    }
                    catch (HandledException ex)
                    {
                        failed++;
                        Console.WriteLine(ex.FriendlyErrorMessage.TrimEnd());
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"Failed to import '{filename}': {ex.Message}");
                    }
                }

                // 6. Summary and unmatched file reporting (Task 6.3)
                Console.WriteLine();
                Console.WriteLine($"Total: {images.Count:N0}, Imported: {imported:N0}, Unmatched: {unmatchedFiles.Count:N0}, Failed: {failed:N0}");

                if (unmatchedFiles.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Unmatched files:");
                    foreach (string unmatched in unmatchedFiles)
                        Console.WriteLine($"  {unmatched}");
                }
            }
            finally
            {
                Console.CancelKeyPress -= handler;
                log?.Dispose();
                cancel.Dispose();
            }
        }

        public static void ExecuteAddDir(NkdsCommandRequest request)
        {
            string dataStorePath = request.DataStorePath;
            string setName = request.SetName;

            // Resolve datastore directory and set name from .nkds path
            if (isExplicitDataStoreFilePath(dataStorePath))
            {
                string fullPath = Path.GetFullPath(dataStorePath);
                dataStorePath = Path.GetDirectoryName(fullPath)
                    ?? throw new CommandLineException("The datastore path must have a parent directory.");
                setName ??= Path.GetFileNameWithoutExtension(fullPath);
            }

            // Default set name to "folders" when not specified
            setName ??= "folders";

            if (!Directory.Exists(dataStorePath))
                Directory.CreateDirectory(dataStorePath);

            // Resolve shard/block sizes: use existing set info or defaults
            long shardSize = 50L * 1024 * 1024 * 1024; // 50 GiB
            int blockSize = 0x10000; // 64 KB

            using (DataStore ds = new DataStore(dataStorePath))
            {
                SetInfo existingSet = ds.GetSetInfo(setName);
                if (existingSet != null)
                {
                    shardSize = existingSet.ShardSize;
                    blockSize = existingSet.BlockSize;
                }
                else
                {
                    ds.CreateSet(setName, shardSize, blockSize);
                }
            }

            Console.WriteLine($"DataStore path: {dataStorePath}");
            Console.WriteLine($"Target set: {setName}");
            Console.WriteLine();

            int successCount = 0;
            int errorCount = 0;

            foreach (string inputDir in request.Inputs)
            {
                string fullDir = Path.GetFullPath(inputDir);
                if (!Directory.Exists(fullDir))
                {
                    Console.WriteLine($"Error: Directory '{inputDir}' does not exist.");
                    errorCount++;
                    continue;
                }

                string dirName = Path.GetFileName(fullDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(dirName))
                    dirName = "unnamed";

                Console.WriteLine($"Adding directory: {fullDir}");

                try
                {
                    using (DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(dataStorePath, dirName, shardSize, blockSize, setName))
                    {
                        List<(string FullPath, string RelativePath)> files = Directory.EnumerateFiles(fullDir, "*", SearchOption.AllDirectories)
                            .Select(f => (FullPath: f, RelativePath: Path.GetRelativePath(fullDir, f).Replace(Path.DirectorySeparatorChar, '/')))
                            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        long totalSize = 0;
                        int fileCount = 0;

                        foreach ((string FullPath, string RelativePath) file in files)
                        {
                            long fileSize = new FileInfo(file.FullPath).Length;
                            Console.WriteLine($"  [{fileCount + 1}/{files.Count}] {file.RelativePath} ({formatBytes(fileSize)})");
                            using (FileStream stream = File.OpenRead(file.FullPath))
                            {
                                formatter.StoreFile(file.RelativePath, stream, fileSize);
                                totalSize += fileSize;
                            }
                            fileCount++;
                        }

                        formatter.BuildFileSystemYaml();
                        formatter.FinalizeImage(totalSize, 0, 0);

                        Console.WriteLine($"  Stored {fileCount:N0} file(s), {formatBytes(totalSize)}");
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error: {ex.Message}");
                    errorCount++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Added {successCount:N0} of {request.Inputs.Length:N0} directory(ies).{(errorCount > 0 ? $" {errorCount:N0} failed." : "")}");
        }

        public static void ExecuteRemove(NkdsCommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SetName))
                throw new CommandLineException("A datastore set file (.nkds) must be specified for removal.");

            if (request.Inputs.Length == 0)
                throw new CommandLineException("No image IDs were specified for removal.");

            using IDataStore dataStore = openDataStore(request.DataStorePath);

            int successCount = 0;
            int failCount = 0;

            foreach (string input in request.Inputs)
            {
                if (long.TryParse(input, out long imageId))
                {
                    try
                    {
                        Console.WriteLine($"Removing image {imageId} from '{request.SetName}'...");
                        dataStore.DeleteImage(new GlobalImageKey(request.SetName, imageId));
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error removing {imageId}: {ex.Message}");
                        failCount++;
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid image ID: '{input}'");
                    failCount++;
                }
            }

            Console.WriteLine($"\nRemoval complete. Removed: {successCount}, Failed: {failCount}");
        }

        public static void ExecuteRestore(NkdsCommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SetName))
                throw new CommandLineException("A datastore set file (.nkds) must be specified for restore.");

            if (request.Inputs.Length == 0)
                throw new CommandLineException("No image IDs were specified for restore.");

            using IDataStore dataStore = openDataStore(request.DataStorePath);

            int successCount = 0;
            int failCount = 0;

            foreach (string input in request.Inputs)
            {
                if (long.TryParse(input, out long imageId))
                {
                    try
                    {
                        Console.WriteLine($"Restoring image {imageId} in '{request.SetName}'...");
                        dataStore.RestoreImage(new GlobalImageKey(request.SetName, imageId));
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error restoring {imageId}: {ex.Message}");
                        failCount++;
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid image ID: '{input}'");
                    failCount++;
                }
            }

            Console.WriteLine($"\nRestore complete. Restored: {successCount}, Failed: {failCount}");
        }

        public static void ExecuteCompact(NkdsCommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SetName))
                throw new CommandLineException("A datastore set file (.nkds) must be specified for compaction.");

            using IDataStore dataStore = openDataStore(request.DataStorePath);

            Console.WriteLine($"Compacting set '{request.SetName}'...");
            try
            {
                dataStore.CompactSet(request.SetName);
                Console.WriteLine("Compaction complete.");
            }
            catch (Exception ex)
            {
                throw new CommandLineException($"Error compacting '{request.SetName}': {ex.Message}");
            }
        }

        public static void ExecuteList(NkdsCommandRequest request)
        {
            using IDataStore dataStore = openDataStore(request.DataStorePath);

            List<ImageRecord> images = getImages(dataStore, request);

            if (request.Format == "json")
            {
                writeListJson(request, images);
                return;
            }

            writeListText(images);
        }

        public static void ExecuteMount(NkdsCommandRequest request)
        {
            Console.WriteLine("NKit DataStore Virtual Filesystem");
            Console.WriteLine("=================================");
            Console.WriteLine();
            Console.WriteLine($"DataStore Path: {request.DataStorePath}");
            if (!string.IsNullOrWhiteSpace(request.SetName))
                Console.WriteLine($"Target Set:     {request.SetName}");
            Console.WriteLine($"Mount Point:    {request.MountPoint}");
            Console.WriteLine($"Views:          image={flag(request.MountShowImage)}, filesystem={flag(request.MountShowFileSystem)}, system={flag(request.MountShowSystem)}");
            Console.WriteLine();

            IPlatformFsHostFactory factory = PlatformFsHostFactory.CreateForCurrentPlatform();
            using MountOrchestrator orchestrator = new MountOrchestrator(factory);

            using ManualResetEventSlim mountDone = new ManualResetEventSlim(false);
            string mountError = null;

            orchestrator.StateChanged += (s, e) =>
            {
                if (e.ErrorMessage != null)
                    Console.WriteLine($"[{e.State}] {e.MountPoint} {e.ErrorMessage}");
                else
                    Console.WriteLine($"[{e.State}] {e.MountPoint}");

                // Signal completion when mount ends (unmounted or error)
                if (e.State == MountState.Unmounted || e.State == MountState.Error)
                {
                    mountError = e.ErrorMessage;
                    mountDone.Set();
                }
            };

            MountRequest mountRequest = new MountRequest
            {
                DataStorePaths = new[] { request.DataStorePath },
                MountPoint = request.MountPoint,
                SetName = request.SetName,
                Options = new NKDS.Models.MountOptions
                {
                    ShowImage = request.MountShowImage,
                    ShowFileSystem = request.MountShowFileSystem,
                    ShowSystem = request.MountShowSystem,
                    UpdateMode = request.MountUpdateMode
                },
                AllowOther = request.MountAllowOther,
                Uid = request.MountUid,
                Gid = request.MountGid,
                MaxFileSystemYamlSizeKiB = request.MountMaxFsYamlSizeKiB
            };

            OperationResult result = orchestrator.MountAsync(mountRequest).GetAwaiter().GetResult();
            if (!result.Success)
            {
                string reason = result.Errors.Count > 0 ? result.Errors[0].Reason : "Unknown error";
                throw new CommandLineException($"Mount failed: {reason}");
            }

            Console.WriteLine("Press Enter to unmount...");

            // Wait for either user input (Enter) or external unmount/error.
            // Use a background thread to read console input so we can also detect
            // external unmount events (e.g., fusermount -u) without blocking forever.
            using ManualResetEventSlim userRequestedUnmount = new ManualResetEventSlim(false);
            Thread inputThread = new Thread(() =>
            {
                try
                {
                    Console.ReadLine();
                    userRequestedUnmount.Set();
                }
                catch { /* stdin closed or interrupted */ }
            })
            {
                IsBackground = true,
                Name = "MountInputReader"
            };
            inputThread.Start();

            // Wait for whichever comes first: user presses Enter, or mount exits on its own
            int triggered = WaitHandle.WaitAny(new[]
            {
                userRequestedUnmount.WaitHandle,
                mountDone.WaitHandle
            });

            if (triggered == 0)
            {
                // User pressed Enter — initiate unmount
                Console.WriteLine("Unmounting...");
                orchestrator.Unmount(request.MountPoint);

                // Wait for the unmount to complete (host thread to exit)
                mountDone.Wait(TimeSpan.FromSeconds(10));
            }

            if (mountError != null)
                throw new CommandLineException($"Mount error: {mountError}");
        }

        private static string flag(bool value) => value ? "on" : "off";

        public static void WriteMountInfo(NkdsCommandRequest request)
        {
            Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            using IDataStore dataStore = openDataStore(request.DataStorePath);

            IEnumerable<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> setsWithImages = dataStore.DescribeSetsWithImages(request.SetName);
            sw.Stop();
            Console.WriteLine($"[Timer] WriteMountInfo: Described sets with images in {sw.ElapsedMilliseconds}ms");

            // Filter sets per request
            List<ImageRecord> images = setsWithImages
                .SelectMany(s => s.Images)
                .Where(image => request.ShowRemoved || !image.Removed)
                .OrderBy(image => image.SetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(image => image.System ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                .ThenBy(image => image.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Image listing
            Console.WriteLine($"Images: {images.Count:N0}");
            Console.WriteLine();

            foreach (IGrouping<string, ImageRecord> group in images.GroupBy(image => image.SetName).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{group.Key} ({group.Count():N0})");
                foreach (ImageRecord image in group)
                    Console.WriteLine($"  {image.Id,-5} {getMountFileName(image)} {image.System ?? "Unknown"} {image.Format} {formatBytes(image.Size)}{(image.Removed ? " [REMOVED]" : "")}");

                Console.WriteLine();
            }

            if (images.Count == 0)
                Console.WriteLine("No images matched the current filters.");
        }

        public static void ExecuteSets(NkdsCommandRequest request)
        {
            using IDataStore dataStore = openDataStore(request.DataStorePath);

            List<SetInfo> sets = dataStore
                .DescribeSets()
                .OrderBy(set => set.SetName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (request.Format == "json")
            {
                writeSetsJson(request, sets);
                return;
            }

            writeSetsText(sets);
        }

        public static void ExecuteStats(NkdsCommandRequest request)
        {
            using IDataStore dataStore = openDataStore(request.DataStorePath);
            List<DataStoreStatistics> stats = getStatistics(dataStore, request);

            if (!string.IsNullOrWhiteSpace(request.SetName) && stats.Count == 0)
                throw new CommandLineException($"Set '{request.SetName}' was not found.");

            if (request.Format == "json")
            {
                writeStatsJson(request, stats);
                return;
            }

            if (request.Format == "yaml")
            {
                writeStatsYaml(stats);
                return;
            }

            writeStatsTexts(stats, request.IncludeImageDetails);
        }

        public static void ExecuteRollback(NkdsCommandRequest request)
        {
            using IDataStore dataStore = openDataStore(request.DataStorePath);

            // If no input provided, list images ordered by ID
            if (request.Inputs.Length == 0)
            {
                List<ImageRecord> images = getImages(dataStore, request)
                    .OrderBy(i => i.Id)
                    .ToList();

                Console.WriteLine("Images ordered by insert order:");
                foreach (ImageRecord img in images)
                {
                    Console.WriteLine($"  ID: {img.Id,-5} | Name: {img.Name}");
                }
                Console.WriteLine("\nTo rollback to a specific image, provide its ID: nkds rollback <id>");
                return;
            }

            if (!long.TryParse(request.Inputs[0], out long imageId))
                throw new CommandLineException("Invalid image ID provided for rollback.");

            Console.WriteLine($"Rolling back dataset to image ID {imageId}...");
            dataStore.RollbackImage(new GlobalImageKey(request.SetName, imageId));
            Console.WriteLine("Dataset rolled back successfully.");
        }

        private static bool tryReadCommandValue(NkdsCommand command, string token, ParseState state)
        {
            if ((command == NkdsCommand.Add || command == NkdsCommand.AddDir || command == NkdsCommand.Rollback || command == NkdsCommand.Remove || command == NkdsCommand.Restore) && !isOption(token))
            {
                state.Inputs.Add(token);
                return true;
            }

            if (command == NkdsCommand.Create && !isOption(token) && string.IsNullOrWhiteSpace(state.SetName))
            {
                state.SetName = token;
                return true;
            }

            if (command == NkdsCommand.Ogmr && !isOption(token))
            {
                if (string.IsNullOrWhiteSpace(state.OgmrYamlPath))
                    state.OgmrYamlPath = token;
                else
                    state.Inputs.Add(token);
                return true;
            }

            return false;
        }

        private static bool tryReadHelpTopic(NkdsCommand command, string token, ParseState state)
        {
            if (isOption(token))
                return false;

            if (command == NkdsCommand.Help)
            {
                state.HelpTopic ??= parseCommand(token);
                return true;
            }

            if (token.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                state.ShowHelp = true;
                if (state.ExplicitCommand)
                    state.HelpTopic ??= command; // 'nkds <verb> help' → that verb's help
                return true;
            }

            return false;
        }

        private static void applyOption(string[] args, ref int index, string token, NkdsCommand command, ParseState state)
        {
            if (!trySplitOption(token, out string optionName, out string inlineValue))
                throw new CommandLineException($"Unexpected argument '{token}'.");

            string option = normalizeOption(optionName);
            if (isMaskedTaskCommand(command) && optionName.Equals("m", StringComparison.OrdinalIgnoreCase))
                option = "mask";
            else if (isOutputTaskCommand(command) && (optionName.Equals("out", StringComparison.OrdinalIgnoreCase) || optionName.Equals("output", StringComparison.OrdinalIgnoreCase)))
                option = "output";
            else if (command == NkdsCommand.List && option == "recursive")
                option = "removed";

            if (command == NkdsCommand.Mount)
            {
                if (optionName.Equals("s", StringComparison.OrdinalIgnoreCase) ||
                    optionName.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                    optionName.Equals("sys", StringComparison.OrdinalIgnoreCase))
                    option = "mountsystem";
                else if (optionName.Equals("i", StringComparison.OrdinalIgnoreCase) ||
                         optionName.Equals("image", StringComparison.OrdinalIgnoreCase))
                    option = "mountimage";
                else if (optionName.Equals("fs", StringComparison.OrdinalIgnoreCase) ||
                         optionName.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                    option = "mountfilesystem";
                else if (optionName.Equals("u", StringComparison.OrdinalIgnoreCase) ||
                         optionName.Equals("update", StringComparison.OrdinalIgnoreCase))
                    option = "mountupdatemode";
            }

            if ((command == NkdsCommand.Add || command == NkdsCommand.Ogmr || isMaskedTaskCommand(command)) && tryCaptureTaskPassthroughOption(args, ref index, token, option, inlineValue, state, command))
                return;

            switch (option)
            {
                case "help":
                    state.ShowHelp = true;
                    // Only carry a topic when a real verb preceded --help (e.g. 'nkds mount --help').
                    // Bare 'nkds --help' leaves HelpTopic null → general help.
                    if (command != NkdsCommand.Help && state.ExplicitCommand)
                        state.HelpTopic ??= command;
                    index++;
                    break;
                case "version":
                    state.ShowVersion = true;
                    index++;
                    break;
                case "details":
                    state.IncludeImageDetails = true;
                    index++;
                    break;
                case "removed":
                    state.ShowRemoved = true;
                    index++;
                    break;
                case "recursive":
                    if (isMaskedTaskCommand(command))
                        throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command does not support --recursive. Use --mask with an explicit .nkds path.");
                    if (command == NkdsCommand.AddDir)
                        throw new CommandLineException("The adddir command does not support --recursive.");
                    state.Recursive = true;
                    index++;
                    break;
                case "noarchives":
                    if (isMaskedTaskCommand(command))
                        throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command does not support --no-archives.");
                    if (command == NkdsCommand.AddDir)
                        throw new CommandLineException("The adddir command does not support --no-archives.");
                    state.ScanArchives = false;
                    index++;
                    break;
                case "datastore":
                    state.DataStorePath = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "config":
                    if (command == NkdsCommand.AddDir)
                        throw new CommandLineException("The adddir command does not support --config.");
                    state.ConfigFile = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "mask":
                    state.Mask = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "output":
                    state.OutputPath = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "input":
                    if (isMaskedTaskCommand(command))
                        throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command does not support --input. Use --mask <mask>.");
                    state.Inputs.Add(readOptionValue(args, ref index, inlineValue, optionName));
                    break;
                case "id":
                    state.Inputs.Add(readOptionValue(args, ref index, inlineValue, optionName));
                    break;
                case "mount":
                    state.MountPoint = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "set":
                    if (command == NkdsCommand.Add)
                        throw new CommandLineException("The add command no longer supports --set. Use an explicit .nkds path with --datastore instead.");

                    state.SetName = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "system":
                    state.SystemName = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "search":
                    state.Search = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "format":
                    if (command == NkdsCommand.Export)
                        state.ConvertFormat = readOptionValue(args, ref index, inlineValue, optionName);
                    else if (isFormattedCommand(command))
                        state.Format = readOptionValue(args, ref index, inlineValue, optionName);
                    else
                        throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command does not support --format.");
                    break;
                case "shardsize":
                    state.ShardSizeText = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "blocksize":
                    state.BlockSizeText = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "ogmr":
                    state.OgmrYamlPath = readOptionValue(args, ref index, inlineValue, optionName);
                    break;
                case "allowother":
                    state.MountAllowOther = true;
                    index++;
                    break;
                case "uid":
                    string uidStr = readOptionValue(args, ref index, inlineValue, optionName);
                    if (uint.TryParse(uidStr, out uint uid))
                        state.MountUid = uid;
                    else
                        state.MountUid = resolveId(uidStr, "-u");
                    state.MountAllowOther = true;
                    break;
                case "gid":
                    string gidStr = readOptionValue(args, ref index, inlineValue, optionName);
                    if (uint.TryParse(gidStr, out uint gid))
                        state.MountGid = gid;
                    else
                        state.MountGid = resolveId(gidStr, "-g");
                    state.MountAllowOther = true;
                    break;
                case "mountsystem":
                    state.MountShowSystem = true;
                    index++;
                    break;
                case "mountimage":
                    state.MountShowImage = true;
                    index++;
                    break;
                case "mountfilesystem":
                    state.MountShowFileSystem = true;
                    index++;
                    break;
                case "mountupdatemode":
                    state.MountUpdateMode = true;
                    index++;
                    break;
                case "maxfsyamlsize":
                    string maxFsYamlStr = readOptionValue(args, ref index, inlineValue, optionName);
                    if (!int.TryParse(maxFsYamlStr, out int maxFsYamlSize) || maxFsYamlSize < 0)
                        throw new CommandLineException($"Option '{optionName}' requires a non-negative integer value.");
                    state.MountMaxFsYamlSizeKiB = maxFsYamlSize;
                    break;
                default:
                    throw new CommandLineException($"Unknown option '{token}'.");
            }
        }

        public static void ExecuteVerify(NkdsCommandRequest request)
        {
            executeMaskedTask(createVerifyAppSettings(request),
                result => !string.IsNullOrWhiteSpace(result.ErrorMsg) || result.VerifyResult != VerifyResult.VerifySuccess,
                "verify",
                "Verified");
        }

        public static void ExecuteExport(NkdsCommandRequest request)
        {
            executeMaskedTask(createExportAppSettings(request),
                result => !string.IsNullOrWhiteSpace(result.ErrorMsg),
                "export",
                "Exported");
        }

        private static void executeMaskedTask(AppSettings settings, Func<NKitTaskResults, bool> hasFailed, string action, string completedVerb)
        {
            CancellationTokenSource cancel = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancel.Cancel();
            };

            Log log = null;
            try
            {
                Console.CancelKeyPress += handler;
                log = settings.GetLog((message, level) => Console.Write(message));

                if (!settings.Check(log))
                    throw new CommandLineException("No input files were specified.");

                List<SourceFile> images = SourceFiles
                    .Scan(settings.In, settings.R, settings.Arc, true, log, cancel.Token)
                    .OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (images.Count == 0)
                {
                    Console.WriteLine("No valid images were found.");
                    return;
                }

                Console.WriteLine();
                Console.WriteLine($"Images queued: {images.Count:N0}");
                Console.WriteLine();

                int errors = 0;
                int imageIndex = 0;
                foreach (SourceFile file in images)
                {
                    imageIndex++;
                    if (cancel.IsCancellationRequested)
                        throw new HandledException("Cancelled!");

                    log.Info(() => $"--[{imageIndex}/{images.Count}]--");

                    try
                    {
                        NKitProcessor processor = new NKitProcessor(settings, file, (message, level) => Console.Write(message));
                        NKitTaskResults result = processor.Process(cancel.Token);
                        if (hasFailed(result))
                        {
                            errors++;
                            if (!string.IsNullOrWhiteSpace(result.ErrorMsg))
                                Console.WriteLine(result.ErrorMsg);
                        }
                    }
                    catch (HandledException ex)
                    {
                        errors++;
                        Console.WriteLine(ex.FriendlyErrorMessage.TrimEnd());
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.WriteLine($"Failed to {action} '{file.Name}': {ex.Message}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"{completedVerb} {images.Count - errors:N0} of {images.Count:N0} image(s).");

                if (errors != 0)
                    throw new HandledException($"{errors:N0} image(s) failed to {action}.");
            }
            finally
            {
                Console.CancelKeyPress -= handler;
                log?.Dispose();
                cancel.Dispose();
            }
        }

        private static bool tryCaptureTaskPassthroughOption(string[] args, ref int index, string token, string normalizedOption, string inlineValue, ParseState state, NkdsCommand command)
        {
            if (!shouldPassthroughTaskOption(args, index, normalizedOption, inlineValue, command))
                return false;

            state.ExtraArgs.Add(token);
            index++;

            if (inlineValue == null && index < args.Length && !isOption(args[index]))
            {
                state.ExtraArgs.Add(args[index]);
                index++;
            }

            return true;
        }

        private static bool shouldPassthroughTaskOption(string[] args, int index, string normalizedOption, string inlineValue, NkdsCommand command)
        {
            if (normalizedOption is "help" or "datastore" or "config" or "input" or "mask" or "output" or "set" or "recursive" or "noarchives" or "ogmr" or "shardsize" or "blocksize")
                return false;

            if (command == NkdsCommand.Export && normalizedOption == "format")
                return false;

            return true;
        }

        private static NkdsCommandRequest createRequest(NkdsCommand command, ParseState state)
        {
            string normalizedSetName = string.IsNullOrWhiteSpace(state.SetName)
                ? null
                : normalizeSetSpecifier(state.DataStorePath, state.SetName);

            bool anyMountOption = state.MountShowImage.HasValue || state.MountShowFileSystem.HasValue || state.MountShowSystem.HasValue;

            return new NkdsCommandRequest
            {
                Command = command,
                HelpTopic = state.HelpTopic,
                DataStorePath = state.DataStorePath,
                Mask = state.Mask,
                ConvertFormat = state.ConvertFormat,
                OutputPath = state.OutputPath,
                MountPoint = state.MountPoint,
                ConfigFile = state.ConfigFile,
                SetName = normalizedSetName,
                SystemName = state.SystemName,
                Search = state.Search,
                OgmrYamlPath = state.OgmrYamlPath,
                Inputs = state.Inputs.ToArray(),
                ShardSizeText = state.ShardSizeText,
                BlockSizeText = state.BlockSizeText,
                Recursive = state.Recursive,
                ScanArchives = state.ScanArchives,
                IncludeImageDetails = state.IncludeImageDetails,
                ShowRemoved = state.ShowRemoved,
                MountShowImage = anyMountOption ? (state.MountShowImage ?? false) : true,
                MountShowFileSystem = anyMountOption ? (state.MountShowFileSystem ?? false) : true,
                MountShowSystem = anyMountOption ? (state.MountShowSystem ?? false) : false,
                MountUpdateMode = state.MountUpdateMode ?? false,
                MountAllowOther = state.MountAllowOther,
                MountUid = state.MountUid,
                MountGid = state.MountGid,
                MountMaxFsYamlSizeKiB = state.MountMaxFsYamlSizeKiB,
                Format = normalizeFormat(command, state.Format),
                ExtraArgs = state.ExtraArgs.ToArray(),
            };
        }

        private static void resolveDataStoreInput(NkdsCommand command, ParseState state)
        {
            if (string.IsNullOrWhiteSpace(state.DataStorePath))
                return;

            if (command == NkdsCommand.Create)
            {
                string createPath = Path.GetFullPath(state.DataStorePath);
                if (!createPath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                    return;

                if (!string.IsNullOrWhiteSpace(state.SetName))
                    throw new CommandLineException("Use either --datastore <path.nkds> or a positional set name for create, not both.");

                state.DataStorePath = Path.GetDirectoryName(createPath) ?? throw new CommandLineException("The set file must have a parent directory.");
                state.SetName = Path.GetFileName(createPath);
                return;
            }

            if (command is not (NkdsCommand.List or NkdsCommand.Stats or NkdsCommand.Mount or NkdsCommand.Rollback or NkdsCommand.Remove or NkdsCommand.Restore or NkdsCommand.Compact))
                return;

            string fullPath = Path.GetFullPath(state.DataStorePath);
            if (!fullPath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                return;

            state.DataStorePath = Path.GetDirectoryName(fullPath) ?? throw new CommandLineException("The index file must have a parent directory.");
            state.SetName ??= Path.GetFileNameWithoutExtension(fullPath);
        }

        private static List<ImageRecord> getImages(IDataStore dataStore, NkdsCommandRequest request)
        {
            return dataStore
                .ListAllImages(image => matches(image, request))
                .OrderBy(image => image.SetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(image => image.System ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                .ThenBy(image => image.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<DataStoreStatistics> getStatistics(IDataStore dataStore, NkdsCommandRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.SetName))
            {
                DataStoreStatistics stats = dataStore.GetSetStatistics(request.SetName, request.IncludeImageDetails);
                return stats == null ? [] : [stats];
            }

            List<SetInfo> sets = dataStore
                .DescribeSets()
                .OrderBy(set => set.SetName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sets.Count == 0)
                return [];

            Console.Error.WriteLine($"Computing statistics for {sets.Count} sets...");

            // Process sets in parallel for speed
            DataStoreStatistics[] results = new DataStoreStatistics[sets.Count];
            int completed = 0;

            Parallel.For(0, sets.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                results[i] = dataStore.GetSetStatistics(sets[i].SetName, request.IncludeImageDetails);
                int done = Interlocked.Increment(ref completed);
                Console.Error.Write($"\r  Sets completed: {done}/{sets.Count}");
            });
            Console.Error.WriteLine();

            return results.Where(s => s != null).Cast<DataStoreStatistics>().ToList();
        }

        private static void writeListJson(NkdsCommandRequest request, List<ImageRecord> images)
        {
            writeJson(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("command", "list");
                writer.WriteString("dataStorePath", request.DataStorePath);
                writer.WriteNumber("count", images.Count);
                writer.WritePropertyName("images");
                writer.WriteStartArray();
                foreach (ImageRecord image in images)
                    writeImageRecordJson(writer, image);
                writer.WriteEndArray();
                writer.WriteEndObject();
            });
        }

        private static void writeListText(List<ImageRecord> images)
        {
            Console.WriteLine($"Images: {images.Count:N0}");
            Console.WriteLine();

            foreach (IGrouping<string, ImageRecord> group in images.GroupBy(image => image.SetName).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{group.Key} ({group.Count():N0})");
                foreach (ImageRecord image in group)
                    Console.WriteLine($"  {image.Id,-5} {getMountFileName(image)} {image.System ?? "Unknown"} {image.Format} {formatBytes(image.Size)}{(image.Removed ? " [REMOVED]" : "")}");

                Console.WriteLine();
            }

            if (images.Count == 0)
                Console.WriteLine("No images matched the current filters.");
        }

        private static void writeSetsJson(NkdsCommandRequest request, List<SetInfo> sets)
        {
            writeJson(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("command", "sets");
                writer.WriteString("dataStorePath", request.DataStorePath);
                writer.WriteNumber("count", sets.Count);
                writer.WritePropertyName("sets");
                writer.WriteStartArray();
                foreach (SetInfo set in sets)
                {
                    long totalFileSize = getTotalFileSize(set.FilePaths);
                    double? shrinkRatio = getShrinkRatio(set.TotalSize, totalFileSize);
                    double? shrinkByPercent = getShrinkPercent(set.TotalSize, totalFileSize);

                    writer.WriteStartObject();
                    writer.WriteString("setName", set.SetName);
                    writer.WriteNumber("shardSize", set.ShardSize);
                    writer.WriteString("shardSizeText", formatBytes(set.ShardSize));
                    writer.WriteNumber("blockSize", set.BlockSize);
                    writer.WriteString("blockSizeText", formatBytes(set.BlockSize));
                    writer.WriteNumber("maxOffsetBlocks", set.MaxOffsetBlocks);
                    writer.WriteNumber("imageCount", set.ImageCount);
                    writer.WriteNumber("totalSize", set.TotalSize);
                    writer.WriteString("totalSizeText", formatBytes(set.TotalSize));
                    writer.WriteNumber("totalFileSize", totalFileSize);
                    writer.WriteString("totalFileSizeText", formatBytes(totalFileSize));
                    if (shrinkRatio.HasValue)
                        writer.WriteNumber("shrinkRatio", shrinkRatio.Value);
                    else
                        writer.WriteNull("shrinkRatio");
                    if (shrinkByPercent.HasValue)
                        writer.WriteNumber("shrinkByPercent", shrinkByPercent.Value);
                    else
                        writer.WriteNull("shrinkByPercent");

                    writer.WritePropertyName("files");
                    writer.WriteStartArray();
                    foreach (string path in set.FilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                        writeFileEntryJson(writer, path);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            });
        }

        private static void writeSetsText(List<SetInfo> sets)
        {
            Console.WriteLine($"Sets: {sets.Count:N0}");
            Console.WriteLine();

            foreach (SetInfo set in sets)
            {
                long totalFileSize = getTotalFileSize(set.FilePaths);
                double? shrinkRatio = getShrinkRatio(set.TotalSize, totalFileSize);
                double? shrinkPercent = getShrinkPercent(set.TotalSize, totalFileSize);

                Console.WriteLine($"{set.SetName}");
                Console.WriteLine($"  Images: {set.ImageCount:N0}");
                Console.WriteLine($"  Total Size: {formatBytes(set.TotalSize)}");
                Console.WriteLine($"  Set File Size: {formatBytes(totalFileSize)}");
                if (shrinkPercent.HasValue)
                {
                    string shrinkRatioText = shrinkRatio.HasValue ? $" ({shrinkRatio.Value:F2}x)" : string.Empty;
                    Console.WriteLine($"  Shrink By: {shrinkPercent.Value:F1}%{shrinkRatioText}");
                }
                Console.WriteLine($"  Block Size: {formatBytes(set.BlockSize)}");
                Console.WriteLine($"  Shard Size: {formatBytes(set.ShardSize)}");
                Console.WriteLine($"  Max Offset Blocks: {set.MaxOffsetBlocks:N0}");
                Console.WriteLine($"  Files: {set.FilePaths.Count:N0}");
                foreach (string filePath in set.FilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    long fileSize = getFileSize(filePath);
                    string fileSizeText = fileSize >= 0 ? formatBytes(fileSize) : "missing";
                    Console.WriteLine($"    {filePath} ({fileSizeText})");
                }

                Console.WriteLine();
            }

            if (sets.Count == 0)
                Console.WriteLine("No data sets were found.");
        }

        private static long getFileSize(string path)
        {
            return File.Exists(path)
                ? new FileInfo(path).Length
                : -1;
        }

        private static long getTotalFileSize(IEnumerable<string> filePaths)
        {
            long total = 0;
            foreach (string filePath in filePaths)
            {
                long size = getFileSize(filePath);
                if (size > 0)
                    total += size;
            }

            return total;
        }

        private static double? getShrinkRatio(long totalSize, long totalFileSize)
        {
            return totalFileSize > 0 && totalSize > 0
                ? (double)totalSize / totalFileSize
                : null;
        }

        private static double? getShrinkPercent(long totalSize, long totalFileSize)
        {
            return totalFileSize > 0 && totalSize > 0
                ? (1d - ((double)totalFileSize / totalSize)) * 100d
                : null;
        }

        private static void writeStatsText(DataStoreStatistics stats, bool includeImageDetails)
        {
            Console.WriteLine($"Set: {stats.SetName}");
            Console.WriteLine($"Images: {stats.ImageCount:N0}");
            Console.WriteLine($"Total Image Data: {formatBytes(stats.TotalImageDataSize)}");
            Console.WriteLine($"Database Size: {formatBytes(stats.TotalDatabaseFileSize)} across {stats.DatabaseFileCount:N0} files");
            Console.WriteLine();
            Console.WriteLine("Block Storage");
            Console.WriteLine($"  Unique Blocks: {stats.UniqueBlocksStored:N0}");
            Console.WriteLine($"  Block References: {stats.TotalBlockReferences:N0}");
            Console.WriteLine($"  Physical Storage: {formatBytes(stats.TotalPhysicalBlockStorage)}");
            Console.WriteLine($"  Block Size: {formatBytes(stats.BlockSize)}");
            Console.WriteLine();
            Console.WriteLine("Efficiency");
            Console.WriteLine($"  Deduplication Ratio: {stats.DeduplicationRatio:F2}x");
            Console.WriteLine($"  Compression Ratio: {stats.CompressionRatio:F3}");
            Console.WriteLine($"  Overall Efficiency: {stats.OverallStorageEfficiency:F3}");
            Console.WriteLine($"  Deduplication Savings: {formatBytes(stats.DeduplicationSavings)}");
            Console.WriteLine($"  Compression Savings: {formatBytes(stats.CompressionSavings)}");
            Console.WriteLine($"  Total Saved: {formatBytes(stats.TotalStorageSaved)}");
            Console.WriteLine();
            Console.WriteLine("Compression");
            Console.WriteLine($"  Compressed Blocks: {stats.CompressedBlocks:N0} ({stats.CompressionPercentage:F1}%)");
            Console.WriteLine($"  Uncompressed Blocks: {stats.UncompressedBlocks:N0} ({100 - stats.CompressionPercentage:F1}%)");
            Console.WriteLine($"  Average Compressed Block: {formatBytes((long)stats.AverageCompressedBlockSize)}");
            Console.WriteLine($"  Average Uncompressed Block: {formatBytes((long)stats.AverageUncompressedBlockSize)}");

            if (includeImageDetails && stats.ImageDetails != null && stats.ImageDetails.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Image Details");
                foreach (ImageStatistics image in stats.ImageDetails.OrderByDescending(image => image.Size).ThenBy(image => image.ImageName, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  {image.ImageName}");
                    Console.WriteLine($"    Size: {formatBytes(image.Size)}");
                    Console.WriteLine($"    Block References: {image.BlockReferences:N0}");
                    Console.WriteLine($"    Unique Blocks: {image.UniqueBlocks:N0}");
                    Console.WriteLine($"    Deduplication Ratio: {image.DeduplicationRatio:F2}x");
                }
            }
        }

        private static void writeStatsTexts(List<DataStoreStatistics> stats, bool includeImageDetails)
        {
            if (stats.Count == 0)
            {
                Console.WriteLine("No data sets were found.");
                return;
            }

            for (int i = 0; i < stats.Count; i++)
            {
                writeStatsText(stats[i], includeImageDetails);
                if (i + 1 < stats.Count)
                    Console.WriteLine();
            }
        }

        private static void writeStatsYaml(List<DataStoreStatistics> stats)
        {
            if (stats.Count == 0)
            {
                Console.WriteLine("[]");
                return;
            }

            Console.WriteLine(string.Join(Environment.NewLine + "---" + Environment.NewLine,
                stats.Select(stat => stat.ToYaml().TrimEnd())));
        }

        private static void writeStatsJson(NkdsCommandRequest request, List<DataStoreStatistics> stats)
        {
            if (!string.IsNullOrWhiteSpace(request.SetName) && stats.Count == 1)
            {
                writeJson(writer => writeDataStoreStatisticsJson(writer, stats[0]));
                return;
            }

            writeJson(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("command", "stats");
                writer.WriteString("dataStorePath", request.DataStorePath);
                writer.WriteNumber("count", stats.Count);
                writer.WritePropertyName("stats");
                writer.WriteStartArray();
                foreach (DataStoreStatistics stat in stats)
                    writeDataStoreStatisticsJson(writer, stat);
                writer.WriteEndArray();
                writer.WriteEndObject();
            });
        }

        public static void WriteHelp(NkdsCommand? command = null) => NkdsCommandLineHelp.Write(command);

        public static void WriteVersion() => Spectre.Console.AnsiConsole.MarkupLine($"[green]nkds[/] [yellow]v{Spectre.Console.Markup.Escape(AppSettings.GetVersion())}[/]");

        private static IDataStore openDataStore(string dataStorePath)
        {
            validateDataStorePath(dataStorePath);
            return new DataStore(dataStorePath);
        }

        private static void validate(NkdsCommand command, string dataStorePath, string mountPoint, string setName, string format, string mask = null, string convertFormat = null, string outputPath = null, string systemName = null, string[] inputs = null, string configFile = null, string shardSizeText = null, string blockSizeText = null, string ogmrYamlPath = null)
        {
            if (command is NkdsCommand.Help or NkdsCommand.Version)
                return;

            if (command == NkdsCommand.Ogmr)
            {
                if (string.IsNullOrWhiteSpace(ogmrYamlPath))
                    throw new CommandLineException("The ogmr command requires an OGMR YAML file path.");

                if (!string.IsNullOrWhiteSpace(dataStorePath) && dataStorePath.EndsWith(".nkds", StringComparison.OrdinalIgnoreCase))
                    throw new CommandLineException("The ogmr command requires a directory path for --datastore, not a set file path.");

                return;
            }

            if (command == NkdsCommand.AddDir)
            {
                validateAddDataStorePath(dataStorePath);
                if (inputs == null || inputs.Length == 0)
                    throw new CommandLineException("The adddir command requires one or more input directories.");
                return;
            }

            if (command == NkdsCommand.Add && (inputs == null || inputs.Length == 0))
                throw new CommandLineException("The add command requires one or more input files or masks.");

            if (isMaskedTaskCommand(command))
            {
                if (string.IsNullOrWhiteSpace(dataStorePath) || !isExplicitDataStoreFilePath(dataStorePath))
                    throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command requires --datastore <path.nkds>.");

                if (!File.Exists(Path.GetFullPath(dataStorePath)))
                    throw new CommandLineException($"DataStore path '{dataStorePath}' does not exist.");

                if (inputs != null && inputs.Length != 0)
                    throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command does not accept positional inputs. Use --mask <mask>.");

                if (string.IsNullOrWhiteSpace(mask))
                    throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command requires --mask <mask>.");

                if (isOutputTaskCommand(command) && string.IsNullOrWhiteSpace(outputPath))
                    throw new CommandLineException($"The {command.ToString().ToLowerInvariant()} command requires --output <folder>.");
            }

            if (command == NkdsCommand.Add)
                return;

            if (isMaskedTaskCommand(command))
                return;

            if (command == NkdsCommand.Rollback)
            {
                // Must either specify a SetName explicitly or strip it from a .nkds file.
                if (string.IsNullOrWhiteSpace(setName))
                    throw new CommandLineException("The rollback command requires a set name or an explicit .nkds path via --datastore.");
            }

            validateDataStorePath(dataStorePath);

            if (command == NkdsCommand.Mount && string.IsNullOrWhiteSpace(mountPoint))
                throw new CommandLineException("A mount point is required.");

            if (command == NkdsCommand.Create && string.IsNullOrWhiteSpace(setName))
                throw new CommandLineException("The create command requires a set name.");

            if (command == NkdsCommand.Create && !string.IsNullOrWhiteSpace(shardSizeText))
                _ = parseSizeToBytes(shardSizeText);

            if (command == NkdsCommand.Create && !string.IsNullOrWhiteSpace(blockSizeText))
                _ = parseBlockSizeToBytes(blockSizeText);

            _ = normalizeFormat(command, format);
        }

        private static void validateDataStorePath(string dataStorePath)
        {
            if (string.IsNullOrWhiteSpace(dataStorePath))
                throw new CommandLineException("A data store path is required.");

            if (isExplicitDataStoreFilePath(dataStorePath))
            {
                if (!File.Exists(Path.GetFullPath(dataStorePath)))
                    throw new CommandLineException($"DataStore path '{dataStorePath}' does not exist.");
            }
            else if (!Directory.Exists(dataStorePath))
            {
                throw new CommandLineException($"DataStore path '{dataStorePath}' does not exist.");
            }
        }

        private static void validateAddDataStorePath(string dataStorePath)
        {
            if (string.IsNullOrWhiteSpace(dataStorePath))
                throw new CommandLineException("A data store path is required.");

            if (isExplicitDataStoreFilePath(dataStorePath))
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(dataStorePath));
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    throw new CommandLineException($"DataStore path '{dataStorePath}' parent directory does not exist.");

                return;
            }

            if (!Directory.Exists(dataStorePath))
                throw new CommandLineException($"DataStore path '{dataStorePath}' does not exist.");
        }

        private static bool isExplicitDataStoreFilePath(string dataStorePath)
            => !string.IsNullOrWhiteSpace(dataStorePath)
                && dataStorePath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase);

        private static string normalizeFormat(NkdsCommand command, string format)
        {
            string normalized = string.IsNullOrWhiteSpace(format)
                ? "text"
                : format.Trim().ToLowerInvariant();

            return command switch
            {
                NkdsCommand.Stats when normalized is "text" or "json" or "yaml" => normalized,
                NkdsCommand.List or NkdsCommand.Sets when normalized is "text" or "json" => normalized,
                _ when normalized == "text" => normalized,
                _ => throw new CommandLineException($"Unsupported format '{format}' for the {command.ToString().ToLowerInvariant()} command."),
            };
        }

        private static bool isFormattedCommand(NkdsCommand command) =>
            command is NkdsCommand.List or NkdsCommand.Sets or NkdsCommand.Stats;

        private static bool isMaskedTaskCommand(NkdsCommand command) =>
            command == NkdsCommand.Verify || command == NkdsCommand.Export;

        private static bool isOutputTaskCommand(NkdsCommand command) =>
            command == NkdsCommand.Export;

        private static bool matches(ImageRecord image, NkdsCommandRequest request)
        {
            if (!request.ShowRemoved && image.Removed)
                return false;

            if (!string.IsNullOrWhiteSpace(request.SetName) && !string.Equals(image.SetName, request.SetName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(request.SystemName) && !string.Equals(image.System ?? "Unknown", request.SystemName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(request.Search) && image.Name.IndexOf(request.Search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            return true;
        }

        private static NkdsCommand parseCommand(string value)
        {
            return tryParseCommand(value, out NkdsCommand command)
                ? command
                : throw new CommandLineException($"Unknown command '{value}'.");
        }

        private static bool tryParseCommand(string value, out NkdsCommand command)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "mount":
                    command = NkdsCommand.Mount;
                    return true;
                case "list":
                case "ls":
                    command = NkdsCommand.List;
                    return true;
                case "add":
                case "import":
                    command = NkdsCommand.Add;
                    return true;
                case "remove":
                case "rm":
                    command = NkdsCommand.Remove;
                    return true;
                case "restore":
                    command = NkdsCommand.Restore;
                    return true;
                case "compact":
                case "cp":
                    command = NkdsCommand.Compact;
                    return true;
                case "export":
                    command = NkdsCommand.Export;
                    return true;
                case "copy":
                case "convert":
                    command = NkdsCommand.Export;
                    return true;
                case "verify":
                case "vfy":
                    command = NkdsCommand.Verify;
                    return true;
                case "create":
                case "newset":
                    command = NkdsCommand.Create;
                    return true;
                case "rollback":
                    command = NkdsCommand.Rollback;
                    return true;
                case "sets":
                    command = NkdsCommand.Sets;
                    return true;
                case "stats":
                case "info":
                    command = NkdsCommand.Stats;
                    return true;
                case "help":
                    command = NkdsCommand.Help;
                    return true;
                case "version":
                    command = NkdsCommand.Version;
                    return true;
                case "adddir":
                    command = NkdsCommand.AddDir;
                    return true;
                case "ogmr":
                    command = NkdsCommand.Ogmr;
                    return true;
                default:
                    command = default;
                    return false;
            }
        }

        private static string normalizeOption(string option)
        {
            string normalized = option.Trim().TrimStart('-', '/').ToLowerInvariant();
            return normalized switch
            {
                "datastore" or "ds" => "datastore",
                "config" or "c" or "cfg" => "config",
                "input" or "i" or "in" => "input",
                "id" => "id",
                "mask" => "mask",
                "mount" or "m" => "mount",
                "set" or "s" => "set",
                "system" or "sys" => "system",
                "search" or "sch" => "search",
                "format" or "f" => "format",
                "shard-size" or "shardsize" or "ss" => "shardsize",
                "block-size" or "blocksize" or "bs" => "blocksize",
                "details" => "details",
                "recursive" or "r" => "recursive",
                "no-archives" => "noarchives",
                "ogmr" => "ogmr",
                "max-fsyaml-size" or "maxfsyamlsize" => "maxfsyamlsize",
                "help" or "h" or "?" => "help",
                "version" or "v" => "version",
                _ => normalized,
            };
        }

        private static bool trySplitOption(string token, out string optionName, out string inlineValue)
        {
            optionName = string.Empty;
            inlineValue = null;

            if (!isOption(token))
                return false;

            string raw = token.TrimStart('-');
            int separatorIndex = raw.IndexOf('=');
            if (separatorIndex >= 0)
            {
                optionName = raw.Substring(0, separatorIndex);
                inlineValue = raw.Substring(separatorIndex + 1);
                return true;
            }

            optionName = raw;
            return true;
        }

        private static uint resolveId(string name, string argType)
        {
            try
            {
                Process proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "id",
                        Arguments = $"{argType} {name}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    }
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && uint.TryParse(output, out uint id))
                    return id;
            }
            catch { }
            string typeName = argType == "-u" ? "user" : "group";
            throw new CommandLineException($"Invalid {typeName} '{name}'. Must be a valid name or numeric ID.");
        }

        private static string readOptionValue(string[] args, ref int index, string inlineValue, string optionName)
        {
            if (inlineValue != null)
            {
                index++;
                return inlineValue;
            }

            if (index + 1 >= args.Length || isOption(args[index + 1]))
                throw new CommandLineException($"Option '{optionName}' requires a value.");

            index += 2;
            return args[index - 1];
        }

        private static bool isOption(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.StartsWith("-");
        }

        private static string getMountFileName(ImageRecord image)
        {
            string extension = image.Format.GetFileExtension();
            return Path.HasExtension(image.Name)
                ? image.Name
                : image.Name + extension;
        }

        private static string getDefaultDataStorePath()
        {
            if (OperatingSystem.IsWindows())
                return @"C:\NKitDataStore";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "nkit-datastore");
        }

        private static string getDefaultMountPoint()
        {
            if (OperatingSystem.IsWindows())
                return @"N:\";

            return "/mnt/nkit";
        }

        private static AppSettings createImportAppSettings(NkdsCommandRequest request) =>
            //if (string.IsNullOrWhiteSpace(request.ConfigFile))
            //    AppSettings.EnsureDefaultConfigExists();

            new AppSettings(request.ConfigFile, createImportCommandLineArgs(request));

        private static AppSettings createVerifyAppSettings(NkdsCommandRequest request) => new AppSettings(request.ConfigFile, createVerifyCommandLineArgs(request));

        private static AppSettings createExportAppSettings(NkdsCommandRequest request)
        {
            return new AppSettings(request.ConfigFile, createMaskedTaskCommandLineArgs(
                request,
                string.IsNullOrWhiteSpace(request.ConvertFormat) ? TaskType.Expand : TaskType.Convert,
                request.ConvertFormat));
        }

        private static SystemPresetSettings createImportSettings(NkdsCommandRequest request)
        {
            string workingRoot = Path.Combine(request.DataStorePath, ".nkds");
            string tempPath = Path.Combine(workingRoot, "tmp");

            Directory.CreateDirectory(workingRoot);
            Directory.CreateDirectory(tempPath);

            SystemPresetSettings presets = new SystemPresetSettings
            {
                System = SystemType.NotSet,
                Task = TaskType.Dedupe,
                Out = request.DataStorePath,
                Tmp = tempPath,
                R = request.Recursive,
                Arc = request.ScanArchives,
                V = Verify.N,
                ConsoleLevel = LogLevel.Info,
                LogOutLevel = LogLevel.None,
                Results = false,
                ResultsOut = workingRoot,
                Dedupe = buildDedupeParam(request),
            };

            foreach (string input in request.Inputs)
                presets.In.Add(input);

            return presets;
        }

        private static string[] createImportCommandLineArgs(NkdsCommandRequest request)
        {
            List<string> args = new List<string>
            {
                "nkds",
                "-task", TaskType.Dedupe.ToString(),
                "-out", request.DataStorePath
            };

            string dedupeParam = buildDedupeParam(request);
            if (!string.IsNullOrWhiteSpace(dedupeParam))
            {
                args.Add("-dedupe");
                args.Add(dedupeParam);
            }

            if (!string.IsNullOrWhiteSpace(request.ConfigFile))
            {
                args.Add("-cfg");
                args.Add(request.ConfigFile!);
            }

            if (request.Recursive)
            {
                args.Add("-r");
                args.Add("y");
            }

            if (!request.ScanArchives)
            {
                args.Add("-arc");
                args.Add("n");
            }

            foreach (string input in request.Inputs)
            {
                args.Add("-in");
                args.Add(input);
            }

            args.AddRange(request.ExtraArgs);

            return args.ToArray();
        }

        /// <summary>
        /// Builds the dedupe: config param from the request's set name, shard size, block size and filesystem flag.
        /// Format: setName:shardSize:blockSize:persistFs � all parts optional.
        /// Returns null/empty if everything is default.
        /// </summary>
        private static string buildDedupeParam(NkdsCommandRequest request)
        {
            string setName = string.IsNullOrWhiteSpace(request.SetName) ? "" : request.SetName.Trim();
            string shardSize = string.IsNullOrWhiteSpace(request.ShardSizeText) ? "" : request.ShardSizeText.Trim();
            string blockSize = string.IsNullOrWhiteSpace(request.BlockSizeText) ? "" : request.BlockSizeText.Trim();

            // Build colon-separated, trimming trailing empty parts
            string[] parts = new[] { setName, shardSize, blockSize };
            int lastNonEmpty = -1;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    lastNonEmpty = i;
                    break;
                }
            }

            if (lastNonEmpty < 0)
                return null; // all defaults

            return string.Join(":", parts, 0, lastNonEmpty + 1);
        }

        private static string[] createMaskedTaskCommandLineArgs(NkdsCommandRequest request, TaskType taskType, string convertFormat = null)
        {
            if (string.IsNullOrWhiteSpace(request.Mask))
                throw new CommandLineException($"The {request.Command.ToString().ToLowerInvariant()} command requires --mask <mask>.");

            if (isOutputTaskCommand(request.Command) && string.IsNullOrWhiteSpace(request.OutputPath))
                throw new CommandLineException($"The {request.Command.ToString().ToLowerInvariant()} command requires --output <path>.");

            List<string> args = new List<string>
            {
                "nkds",
                "-task", taskType.ToString()
            };

            if (!string.IsNullOrWhiteSpace(convertFormat))
            {
                args.Add("-convert");
                args.Add(convertFormat);
            }

            if (!string.IsNullOrWhiteSpace(request.ConfigFile))
            {
                args.Add("-cfg");
                args.Add(request.ConfigFile!);
            }

            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                args.Add("-out");
                args.Add(request.OutputPath);
            }

            args.Add("-in");
            args.Add(request.DataStorePath + "//" + request.Mask.TrimStart('/'));

            args.AddRange(request.ExtraArgs);

            return args.ToArray();
        }

        private static string[] createVerifyCommandLineArgs(NkdsCommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Mask))
                throw new CommandLineException("The verify command requires --mask <mask>.");

            List<string> args = new List<string>
            {
                "nkds",
                "-task", TaskType.Verify.ToString(),
                "-v", Verify.Y.ToString()
            };

            if (!string.IsNullOrWhiteSpace(request.ConfigFile))
            {
                args.Add("-cfg");
                args.Add(request.ConfigFile!);
            }

            args.Add("-in");
            args.Add(request.DataStorePath + "//" + request.Mask.TrimStart('/'));

            args.AddRange(request.ExtraArgs);

            return args.ToArray();
        }

        private static string normalizeSetSpecifier(string dataStorePath, string setSpecifier)
        {
            string value = setSpecifier.Trim();
            if (value.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - DataStore.DatabaseFileExtension.Length);

            string fullDataStorePath = Path.GetFullPath(dataStorePath);
            string relative = Path.IsPathRooted(value)
                ? Path.GetRelativePath(fullDataStorePath, Path.GetFullPath(value))
                : value;

            string normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string fullSetPath = Path.GetFullPath(Path.Combine(fullDataStorePath, normalized));
            string basePathWithSeparator = fullDataStorePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullSetPath.StartsWith(basePathWithSeparator, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullSetPath, fullDataStorePath, StringComparison.OrdinalIgnoreCase))
                throw new CommandLineException("Set paths must be inside the datastore root.");

            if (string.IsNullOrWhiteSpace(normalized))
                throw new CommandLineException("Set path cannot be empty.");

            return normalized;
        }

        private static int parseBlockSizeToBytes(string blockSizeText)
        {
            string normalized = normalizeSizeToken(blockSizeText);
            if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, ConfigSettingsConstants.BlockSizePatternCso, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new CommandLineException($"Invalid block size '{blockSizeText}'. Valid options: {ConfigSettingsConstants.BlockSizeErrorCso.Replace("|", ", ")}");

            return normalized switch
            {
                "2kb" => 0x800,
                "4kb" => 0x1000,
                "8kb" => 0x2000,
                "16kb" => 0x4000,
                "32kb" => 0x8000,
                "64kb" => 0x10000,
                "128kb" => 0x20000,
                "256kb" => 0x40000,
                "512kb" => 0x80000,
                "1mb" => 0x100000,
                "2mb" => 0x200000,
                _ => throw new CommandLineException($"Invalid block size '{blockSizeText}'.")
            };
        }

        private static long parseSizeToBytes(string sizeText)
        {
            string normalized = normalizeSizeToken(sizeText);
            int unitStart = 0;
            while (unitStart < normalized.Length && char.IsDigit(normalized[unitStart]))
                unitStart++;

            if (unitStart == 0 || !long.TryParse(normalized.Substring(0, unitStart), out long value) || value < 0)
                throw new CommandLineException($"Invalid size '{sizeText}'.");

            string unit = normalized.Substring(unitStart);
            return unit switch
            {
                "" or "b" => value,
                "kb" => value * 1024L,
                "mb" => value * 1024L * 1024L,
                "gb" => value * 1024L * 1024L * 1024L,
                "tb" => value * 1024L * 1024L * 1024L * 1024L,
                _ => throw new CommandLineException($"Invalid size '{sizeText}'.")
            };
        }

        private static string normalizeSizeToken(string value)
        {
            string normalized = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return normalized switch
            {
                var size when size.EndsWith("kib", StringComparison.Ordinal) => size.Substring(0, size.Length - 3) + "kb",
                var size when size.EndsWith("mib", StringComparison.Ordinal) => size.Substring(0, size.Length - 3) + "mb",
                var size when size.EndsWith("gib", StringComparison.Ordinal) => size.Substring(0, size.Length - 3) + "gb",
                var size when size.EndsWith("tib", StringComparison.Ordinal) => size.Substring(0, size.Length - 3) + "tb",
                var size when size.EndsWith("k", StringComparison.Ordinal) => size.Substring(0, size.Length - 1) + "kb",
                var size when size.EndsWith("m", StringComparison.Ordinal) => size.Substring(0, size.Length - 1) + "mb",
                var size when size.EndsWith("g", StringComparison.Ordinal) => size.Substring(0, size.Length - 1) + "gb",
                var size when size.EndsWith("t", StringComparison.Ordinal) => size.Substring(0, size.Length - 1) + "tb",
                _ => normalized,
            };
        }

        private static string formatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            if (order == 0)
                return ((long)len).ToString(CultureInfo.InvariantCulture) + " " + sizes[order];

            return string.Format(CultureInfo.InvariantCulture, "{0:F2} {1}", len, sizes[order]);
        }

        private static void writeImageRecordJson(Utf8JsonWriter writer, ImageRecord image)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", image.Id);
            writer.WriteString("setName", image.SetName);
            writer.WriteString("system", image.System ?? "Unknown");
            writer.WriteString("name", image.Name);
            writer.WriteString("fileName", getMountFileName(image));
            writer.WriteNumber("size", image.Size);
            writer.WriteString("sizeText", formatBytes(image.Size));
            writer.WriteString("format", image.Format.ToString());
            writer.WriteNumber("crc32", image.Crc32);
            writer.WriteNumber("xxHash64", image.XxHash64);
            writer.WriteBoolean("removed", image.Removed);
            writer.WriteEndObject();
        }

        private static void writeFileEntryJson(Utf8JsonWriter writer, string path)
        {
            long size = getFileSize(path);

            writer.WriteStartObject();
            writer.WriteString("path", path);
            writer.WriteNumber("size", size);
            writer.WriteString("sizeText", size >= 0 ? formatBytes(size) : "missing");
            writer.WriteEndObject();
        }

        private static void writeDataStoreStatisticsJson(Utf8JsonWriter writer, DataStoreStatistics stats)
        {
            writer.WriteStartObject();
            writer.WriteString("setName", stats.SetName);
            writer.WriteNumber("imageCount", stats.ImageCount);
            writer.WriteNumber("totalImageDataSize", stats.TotalImageDataSize);
            writer.WriteNumber("uniqueBlocksStored", stats.UniqueBlocksStored);
            writer.WriteNumber("totalBlockReferences", stats.TotalBlockReferences);
            writer.WriteNumber("compressedBlocks", stats.CompressedBlocks);
            writer.WriteNumber("uncompressedBlocks", stats.UncompressedBlocks);
            writer.WriteNumber("totalPhysicalBlockStorage", stats.TotalPhysicalBlockStorage);
            writer.WriteNumber("averageCompressedBlockSize", stats.AverageCompressedBlockSize);
            writer.WriteNumber("averageUncompressedBlockSize", stats.AverageUncompressedBlockSize);
            writer.WriteNumber("blockSize", stats.BlockSize);
            writer.WriteNumber("estimatedUncompressedSize", stats.EstimatedUncompressedSize);
            writer.WriteNumber("deduplicationRatio", stats.DeduplicationRatio);
            writer.WriteNumber("deduplicationSavings", stats.DeduplicationSavings);
            writer.WriteNumber("compressionRatio", stats.CompressionRatio);
            writer.WriteNumber("compressionSavings", stats.CompressionSavings);
            writer.WriteNumber("overallStorageEfficiency", stats.OverallStorageEfficiency);
            writer.WriteNumber("totalStorageSaved", stats.TotalStorageSaved);
            writer.WriteNumber("compressionPercentage", stats.CompressionPercentage);
            writer.WriteNumber("totalDatabaseFileSize", stats.TotalDatabaseFileSize);
            writer.WriteNumber("databaseFileCount", stats.DatabaseFileCount);

            writer.WritePropertyName("imageDetails");
            if (stats.ImageDetails == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartArray();
                foreach (ImageStatistics image in stats.ImageDetails)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("imageId", image.ImageId);
                    writer.WriteString("imageName", image.ImageName);
                    writer.WriteNumber("size", image.Size);
                    writer.WriteNumber("blockReferences", image.BlockReferences);
                    writer.WriteNumber("uniqueBlocks", image.UniqueBlocks);
                    writer.WriteNumber("deduplicationRatio", image.DeduplicationRatio);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        private static void writeJson(Action<Utf8JsonWriter> writeValue)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writeValue(writer);
            }

            Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
        }
    }
}
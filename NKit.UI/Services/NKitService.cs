using Nanook.NKit;
using Nanook.NKit.Ogmr;
using NKit.Ui.Helpers;
using NKit.Ui.Models;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = Nanook.NKit.LogLevel;

namespace NKit.Ui.Services
{
    internal static class NKitService
    {
        public static EventHandler ProcessingProgressChangedEvent;

        public static async Task Run(ObservableCollection<SourceFileRecord> fileQueue, CancellationToken cancelToken)
        {
            await Task.Run(() => { }); // To clear async without await warning.
            ConsoleOutput consoleOutput = Locator.Current.GetService<ConsoleOutput>();
            NKitSettings settingsSingleton = Locator.Current.GetService<NKitSettings>();
            ISettingsStore settingsStore = Locator.Current.GetService<ISettingsStore>();

            try
            {
                foreach (SourceFileRecord file in fileQueue)
                {
                    if ((settingsStore.UiSettings.ReprocessCompletedFiles && file.ProcessingStatus == ProcessingStatus.Completed) ||
                            (settingsStore.UiSettings.ReprocessFailedFiles && file.ProcessingStatus == ProcessingStatus.Failed) ||
                            (settingsStore.UiSettings.ReprocessSkippedFiles && (file.ProcessingStatus == ProcessingStatus.Skipped || file.ProcessingStatus == ProcessingStatus.AlreadyExists)) ||
                            file.ProcessingStatus == ProcessingStatus.Cancelled)
                    {
                        file.ProcessingStatus = ProcessingStatus.Queued;
                        file.Progress = 0;
                        file.Result = null;
                        file.VerifyResultMessage = string.Empty;
                        file.ResetProcessingMessages();
                    }
                }

                ProcessingProgressChangedEvent?.Invoke(fileQueue, null);

                SystemPresetSettings presets = settingsSingleton.ToSystemPresetSettings();

                settingsStore.StoreSettings(settingsSingleton);
                settingsStore.WriteSettingsToDisk();

                if (settingsStore.UiSettings.PersistFileQueue)
                    settingsStore.WriteFileQueueToDisk(fileQueue);

                AppSettings settings = new AppSettings(presets);

                consoleOutput.Append($"Beginning task {settings.TaskType}.", LogLevel.Info);

                // 1GMR routing setup: construct FileRouter before processing any files
                FileRouter fileRouter = null;
                if (settings.TaskType == TaskType.Dedupe && !string.IsNullOrWhiteSpace(settingsSingleton.OgmrYamlPath))
                {
                    try
                    {
                        List<GameEntry> games = OgmrYamlParser.Parse(settingsSingleton.OgmrYamlPath);
                        fileRouter = new FileRouter(games);
                    }
                    catch (OgmrException ex)
                    {
                        consoleOutput.Append($"1GMR Error: {ex.Message}", LogLevel.Error);
                        return;
                    }
                }

                // 1GMR routing counters
                int totalFiles = 0;
                int importedCount = 0;
                int skippedCount = 0;
                int failedCount = 0;

                using (Log l = new Log())
                {
                    l.Initialise(settings.ConsoleLevel, LogLevel.None, (ms, lv) => { Console.Write(ms); }, null);
                    for (int i = 0; i < fileQueue.LongCount(); i++)
                    {
                        SourceFileRecord file = fileQueue[i];
                        AppSettings fileSettings = null;

                        try
                        {
                            if (cancelToken.IsCancellationRequested)
                            {
                                l.Trace(() => "UI: Skipping file as Cancellation requested.");
                                break;
                            }
                            if (!settingsStore.UiSettings.ReprocessCompletedFiles && file.ProcessingStatus == ProcessingStatus.Completed)
                            {
                                l.Trace(() => "UI: Skipping file as ReprocessCompletedFiles = false and ProcessingStatus = Completed.");
                                continue;
                            }


                            if (!settingsStore.UiSettings.ReprocessSkippedFiles && (file.ProcessingStatus == ProcessingStatus.Skipped || file.ProcessingStatus == ProcessingStatus.AlreadyExists))
                            {
                                l.Trace(() => "UI: Skipping file as ReprocessSkippedFiles = false and ProcessingStatus = Skipped/AlreadyExists.");
                                continue;
                            }

                            if (!settingsStore.UiSettings.ReprocessFailedFiles && file.ProcessingStatus == ProcessingStatus.Failed)
                            {
                                l.Trace(() => "UI: Skipping file as ReprocessFailedFiles = false and ProcessingStatus = Failed.");
                                continue;
                            }

                            if (file.ProcessingStatus == ProcessingStatus.Queued && file.SourceFile is null)
                            {
                                string sourcePath = file.Filepath;
                                string archivePath = sourcePath;
                                if (SourceFiles.TrySplitArchivePath(sourcePath, out string parsedArchivePath, out _))
                                    archivePath = parsedArchivePath;

                                if (File.Exists(archivePath) || Directory.Exists(archivePath))
                                {
                                    l.Trace(() => "UI: Scanning file from persisted file queue.");
                                    IEnumerable<SourceFile> images = SourceFiles.Scan(new string[] { sourcePath }, true, true, true, null, null);

                                    file = new SourceFileRecord(images.FirstOrDefault());
                                    fileQueue[i] = file;
                                }
                                else
                                {
                                    string fileNotFoundMessage = $"File not found: {sourcePath}";

                                    consoleOutput.Append(fileNotFoundMessage);
                                    file.AppendProcessingMessage(fileNotFoundMessage);
                                    l.Trace(() => $"UI: {fileNotFoundMessage}");

                                    file.ProcessingStatus = ProcessingStatus.Skipped;
                                    file.VerifyResultMessage = fileNotFoundMessage;
                                    file.VerifyResultIcon = "HelpCircleOutline";
                                    file.VerifyResultIconColour = "Orange";
                                    continue;
                                }

                            }
                            presets.In.Clear();
                            file.ProcessingStatus = ProcessingStatus.Processing;
                            ProcessingProgressChangedEvent?.Invoke(file, null);
                            UiSettings.RowFiltersChangedEvent?.Invoke(settingsSingleton, null);

                            l.Trace(() => "UI: Beginning NKit Processing.");
                            SystemPresetSettings filePresets = settingsSingleton.ToSystemPresetSettings();
                            if (file.SourceFile?.SystemType != SystemType.NotSet)
                                filePresets.System = file.SourceFile.SystemType;

                            fileSettings = new AppSettings(filePresets);

                            // Per-file 1GMR routing
                            if (fileRouter != null)
                            {
                                totalFiles++;
                                string filename = Path.GetFileName(file.SourceFile?.Name ?? file.Filepath);
                                GameEntry match = fileRouter.Match(filename);
                                if (match == null)
                                {
                                    consoleOutput.Append($"Unmatched: {filename}", LogLevel.Info);
                                    file.AppendProcessingMessage($"Unmatched: {filename}");
                                    file.ProcessingStatus = ProcessingStatus.Skipped;
                                    skippedCount++;
                                    continue;
                                }

                                // Override only the set name, preserving system's shard/block/aux settings
                                fileSettings.OverrideDedupeSetName(match.SanitizedName);
                            }

                            NKitProcessor p = new NKitProcessor(fileSettings, file.SourceFile, (s, ll) =>
                            {
                                consoleOutput.Append(s);
                                file.AppendProcessingMessage(s);
                            });

                            p.ProgressEvent += file.UpdateProgress;

                            // look at results class to see if it was successful
                            NKitTaskResults nKitResults = p.Process(cancelToken);

                            l.Trace(() => "UI: Completed NKit Processing.");

                            file.Result = nKitResults;
                            file.OutFileName = nKitResults.OutFileName;

                            if (nKitResults.ImageSkipped)
                            {
                                file.ProcessingStatus = ProcessingStatus.Skipped;
                                if (fileRouter != null)
                                    skippedCount++;
                            }
                            else if (cancelToken.IsCancellationRequested)
                                file.ProcessingStatus = ProcessingStatus.Cancelled;
                            else if (nKitResults.ImageAlreadyExists)
                            {
                                // Dedupe add found the image already in the set; it was rolled back
                                // rather than stored a second time. Not a failure — report it plainly
                                // so the row lands on a real terminal state instead of getting stuck.
                                file.ProcessingStatus = ProcessingStatus.AlreadyExists;
                                file.AppendProcessingMessage("Image already exists in the set - not added.");
                                if (fileRouter != null)
                                    skippedCount++;
                            }
                            else if (file.VerifyResultStatus == VerifyResult.Error)
                            {
                                file.ProcessingStatus = ProcessingStatus.Failed;
                                if (fileRouter != null)
                                    failedCount++;
                            }
                            else
                            {
                                file.ProcessingStatus = ProcessingStatus.Completed;
                                if (fileRouter != null)
                                    importedCount++;
                            }

                            // The row retains this result for the rest of the session. The Result
                            // setter (above) has already distilled everything the UI shows into scalar
                            // fields (VerifyResult/VerifyType/ErrorMsg -> message/icon). The UI never
                            // reads Scan or Source, yet for a Wii/GC image Scan transitively roots the
                            // parsed filesystem + every ScanArea/ScanSection + section buffers (tens of
                            // MiB per image). ReleaseHeavyReferences alone only freed StepResults and
                            // KEPT Scan/Source, so those still accumulated across rows — the dominant
                            // UI memory climb. Release ALL heavy references now that the scalars are
                            // captured.
                            nKitResults.ReleaseAllReferences();

                            // Hygiene: drop the progress delegate so the processor holds no reference
                            // to the retained row. p is a per-iteration local (collectable regardless),
                            // but unsubscribing keeps the wiring symmetric and avoids any accidental
                            // rooting if p's lifetime ever changes.
                            p.ProgressEvent -= file.UpdateProgress;
                        }
                        catch (Exception ex)
                        {
                            string exceptionMessage = $"Processing Exception: {ex.Message}";

                            consoleOutput.Append(exceptionMessage, LogLevel.Error);
                            file.AppendProcessingMessage(exceptionMessage);
                            file.VerifyResultStatus = VerifyResult.Error;
                            file.ProcessingStatus = ProcessingStatus.Failed;
                            if (fileRouter != null)
                                failedCount++;

                            l.Error(() => $"UI: {ExceptionHelper.GetFullExceptionDetail(ex)}");
                        }
                        finally
                        {
                            // A FRESH AppSettings (and its Log -> NKitLog bus -> async FileLogSink
                            // background thread + BlockingCollection wait handles) is created PER IMAGE
                            // here. Dispose it now that the image is done, or each image leaks a sink
                            // thread + OS handles for the life of this long-lived process — the UI's
                            // climbing thread/handle count and working set even when managed is flat.
                            try { fileSettings?.DisposeLog(); } catch { }

                            ProcessingProgressChangedEvent?.Invoke(file, null);

                            if (settingsStore.UiSettings.PersistFileQueue)
                            {
                                settingsStore.WriteFileQueueToDisk(fileQueue);
                            }

                            l.Trace(() => $"UI: Completed file '{file.Name}'");
                            l.Trace(() => Log.Divider);
                        }
                    }

                    try
                    {
                        UiSettings.RowFiltersChangedEvent?.Invoke(settingsSingleton, null);
                        fileQueue.ForEach(x => x.RefreshConsoleOutput());
                    }
                    catch (Exception ex)
                    {
                        consoleOutput.Append($"Post-Processing Exception: {ex.Message}", LogLevel.Error);
                        l.Error(() => $"UI: {ExceptionHelper.GetFullExceptionDetail(ex)}");
                    }
                }

                // Display processing summary when 1GMR routing is active
                if (fileRouter != null)
                {
                    consoleOutput.Append($"1GMR Summary: {totalFiles} total, {importedCount} imported, {skippedCount} skipped, {failedCount} failed", LogLevel.Info);
                }

                consoleOutput.Append($"Completed task {settings.TaskType}.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                consoleOutput.Append($"Pre-Processing Exception: {ex.Message}", LogLevel.Error);
                consoleOutput.Append(ExceptionHelper.GetFullExceptionDetail(ex));
                return;
            }
        }

    }
}
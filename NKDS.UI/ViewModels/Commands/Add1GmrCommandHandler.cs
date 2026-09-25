using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Diagnostics;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Add 1GMR toolbar operation.
/// Lifecycle: show Add1GmrDialog → persist config → session management →
/// create progress → process files per set → reconcile.
/// </summary>
public class Add1GmrCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            try
            {
                // 1. Show dialog
                Add1GmrDialogViewModel dialogVm = new Add1GmrDialogViewModel(ctx.ConfigService);
                bool confirmed = await ctx.ShowAdd1GmrDialog.Handle(dialogVm);
                if (!confirmed) return RxVoid.Default;

                // 2. Persist YAML path and output folder to history
                if (!string.IsNullOrWhiteSpace(dialogVm.OgmrYamlPath))
                    ctx.ConfigService.AddOgmrYamlPath(dialogVm.OgmrYamlPath);
                if (!string.IsNullOrWhiteSpace(dialogVm.OutputFolder))
                    ctx.ConfigService.AddOgmrOutputPath(dialogVm.OutputFolder);

                string dataStorePath = dialogVm.OutputFolder!;
                List<MatchedFileItem> matchedFiles = dialogVm.MatchedFiles.Where(f => f.IsMatched).ToList();
                if (matchedFiles.Count == 0) return RxVoid.Default;

                // Group files by their matched set name
                Dictionary<string, List<string>> filesBySet = matchedFiles
                    .GroupBy(f => f.MatchedSetName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Select(f => f.FilePath).ToList());

                // Ensure DataStore directory exists
                if (!Directory.Exists(dataStorePath))
                    Directory.CreateDirectory(dataStorePath);

                // Normalize the output folder path the same way SessionManager does
                string normalizedOutputFolder = Path.GetFullPath(dataStorePath);
                if (!normalizedOutputFolder.EndsWith(Path.DirectorySeparatorChar))
                    normalizedOutputFolder += Path.DirectorySeparatorChar;

                // Always close the current session and open the 1GMR target folder
                if (ctx.SessionManager.HasSession)
                    ctx.SessionManager.Close();

                // Flush UI events from the close before opening the new folder
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                await ctx.SessionManager.OpenDirectoryAsync(dataStorePath);
                ctx.ConfigService.AddDataStorePath(dataStorePath);

                // Convert shard/block sizes to text format for the dedupe string
                string shardSizeText = dialogVm.SelectedShardSize == "Single File"
                    ? "0"
                    : dialogVm.SelectedShardSize?.Replace(" ", "") ?? "0";
                string? blockSizeText = dialogVm.SelectedBlockSize?.StartsWith("Default") == true
                    ? null
                    : dialogVm.SelectedBlockSize?.Replace(" ", "");
                string? auxModeText = dialogVm.IsAuxMode ? "y" : null;

                // 3. Create progress tracking
                OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Add 1GMR" };
                Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                ctx.ActiveOperations.Add(progressVm);
                ctx.SharedState.OperationInProgressOnSet = "Add 1GMR";

                try
                {
                    int totalFiles = matchedFiles.Count;
                    int filesProcessedSoFar = 0;
                    progressVm.TotalItems = totalFiles;

                    foreach ((string? setName, List<string>? filePaths) in filesBySet)
                    {
                        foreach (string filePath in filePaths)
                        {
                            if (progressVm.CancellationToken.IsCancellationRequested)
                                break;

                            try
                            {
                                using NkdsOperations ops = new NkdsOperations();
                                OperationResult result = await ops.AddAsync(
                                    dataStorePath, setName, new[] { filePath },
                                    null,
                                    onImageCommitted: evt => { _ = ctx.SyncService.InsertImageAsync(evt); },
                                    onImageProgress: evt =>
                                    {
                                        _ = ctx.SyncService.UpdateImageProgressAsync(evt);
                                        string imageName = ctx.ImageList.FindRow(evt.SetName, evt.ImageId)?.Name
                                                        ?? $"Image {evt.ImageId}";
                                        progressVm.CurrentItem = $"{imageName} \u2014 {evt.StepName}";
                                        progressVm.Percentage = (int)(evt.OverallProgress * 100);
                                        progressVm.Elapsed = sw.Elapsed;
                                    },
                                    onImageOutput: (sn, imageId, text) =>
                                    {
                                        _ = ctx.SyncService.AppendImageOutputAsync(sn, imageId, text);
                                    },
                                    cancellationToken: progressVm.CancellationToken,
                                    shardSizeText: shardSizeText,
                                    blockSizeText: blockSizeText,
                                    auxModeText: auxModeText);
                                filesProcessedSoFar += result.ItemsProcessed;
                            }
                            catch (Exception ex)
                            {
                                ctx.ErrorNotification.PublishImageError(
                                    setName, 0,
                                    $"Add 1GMR failed for '{Path.GetFileName(filePath)}': {ex.Message}");
                                filesProcessedSoFar++;
                            }

                            progressVm.ItemsProcessed = filesProcessedSoFar;
                            progressVm.TotalItems = totalFiles;
                        }
                    }

                    // Flush pending UI dispatches before session open/reopen
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                    // Always open the output folder after processing (creates session with the new sets)
                    await ctx.SessionManager.OpenDirectoryAsync(dataStorePath);
                    ctx.ConfigService.AddDataStorePath(dataStorePath);

                    // Reconcile each set to merge live-inserted rows while preserving state
                    foreach (string setName2 in filesBySet.Keys)
                        await ctx.SyncService.ReconcileAsync(setName2);
                    await ctx.SyncService.RefreshAvailableSetNamesAsync();
                }
                catch (OperationCanceledException)
                {
                    ctx.ErrorNotification.PublishCancellation("Add 1GMR");
                }
                catch (Exception ex)
                {
                    ctx.ErrorNotification.PublishOperationError(
                        $"Add 1GMR processing failed: {ex.Message}");
                }
                finally
                {
                    await progressVm.WaitForMinimumDisplayTimeAsync();
                    ctx.ActiveOperations.Remove(progressVm);
                    progressVm.Dispose();
                    ctx.SharedState.OperationInProgressOnSet = null;
                }
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Add 1GMR failed: {ex.Message}");
            }

            return RxVoid.Default;
        });
    }
}
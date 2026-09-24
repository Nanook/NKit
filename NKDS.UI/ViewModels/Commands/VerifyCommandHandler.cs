using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Verify toolbar operation.
/// Read-only operation: does NOT use ExclusiveAccessManager or SessionManager.WithExclusiveAccessAsync.
/// SQLite supports concurrent readers, so no session close/reopen is needed.
/// Per-image verify failures are published to ErrorNotificationService per-image stream.
/// </summary>
public class VerifyCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            List<ImageRowViewModel> selectedRows = ctx.ImageList.SelectedImages.ToList();

            // Determine what to verify — group by set to handle multi-set selections
            List<(string SetName, string DataStorePath, List<long>? ImageIds)> verifyGroups = new();

            if (selectedRows.Count > 0)
            {
                // Group selected images by their set
                IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
                foreach (IGrouping<string, ImageRowViewModel> group in selectedRows.GroupBy(r => r.Image.SetName))
                {
                    string groupSetName = group.Key;
                    List<long> ids = group.Select(r => r.Image.Id).Distinct().ToList();
                    ImageSessionModel? session = SessionResolver.FindSessionForSet(sessions, groupSetName);
                    if (session == null) continue;
                    (string dataStorePath, string originalPath) paths = SessionResolver.ResolveDataStorePath(session);
                    verifyGroups.Add((groupSetName, paths.dataStorePath, ids));
                }
            }
            else if (ctx.Toolbar.SelectedSetName != null && !string.Equals(ctx.Toolbar.SelectedSetName, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
                if (sessions.Count == 0) return RxVoid.Default;
                (string dataStorePath, string originalPath) paths = SessionResolver.ResolveDataStorePath(sessions[0]);
                verifyGroups.Add((ctx.Toolbar.SelectedSetName, paths.dataStorePath, null));
            }
            else
            {
                return RxVoid.Default; // Can't verify "All" without selection
            }

            if (verifyGroups.Count == 0) return RxVoid.Default;

            // Resolve key/fix paths from config for the NKit pipeline
            ResolvedKeyFixPaths keyFixPaths = KeyFixPathsResolver.Resolve(ctx.ConfigService);

            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Verify" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = verifyGroups.FirstOrDefault().SetName;

            try
            {
                int totalImages = verifyGroups.Sum(g => g.ImageIds?.Count ?? 0);
                int cumulativeProcessed = 0;

                // If any group has null ImageIds (verify all in set), we can't pre-compute total
                bool hasNullIds = verifyGroups.Any(g => g.ImageIds == null);

                // Set initial total on progress VM
                if (!hasNullIds)
                {
                    progressVm.TotalItems = totalImages;
                    progressVm.ItemsProcessed = 0;
                }

                foreach ((string? setName, string? dataStorePath, List<long>? imageIds) in verifyGroups)
                {
                    int groupOffset = cumulativeProcessed;

                    // Progress wrapper: show per-image % on the bar, but track items across all groups
                    IProgress<OperationProgress> groupProgress;
                    if (hasNullIds)
                    {
                        groupProgress = progressVm.CreateProgressReporter();
                    }
                    else
                    {
                        groupProgress = new Progress<OperationProgress>(p =>
                        {
                            progressVm.CurrentItem = p.CurrentItem;
                            progressVm.ItemsProcessed = groupOffset + p.ItemsProcessed;
                            progressVm.TotalItems = totalImages;
                            progressVm.Elapsed = p.Elapsed;
                        });
                    }

                    using NkdsOperations ops = new NkdsOperations();
                    OperationResult result = await ops.VerifyAsync(
                        dataStorePath, setName, imageIds,
                        groupProgress,
                        onImageProgress: evt =>
                        {
                            _ = ctx.SyncService.UpdateImageProgressAsync(evt);
                            // This callback fires on the VerifyAsync background (Task.Run) thread.
                            // progressVm is a data-bound ReactiveObject and ImageList.FindRow walks
                            // the UI-owned _allImages collection — touching either from the background
                            // thread raises PropertyChanged off-thread (forcing Avalonia to marshal it)
                            // and races the UI thread's own collection mutation. At the high frequency
                            // this callback fires that live-locks the UI dispatcher (the Verify hang;
                            // the CLI has no UI thread and verifies fine). Marshal the bound-state
                            // update onto the UI thread with a non-blocking Post (fire-and-forget, so
                            // the verify thread never waits on the dispatcher).
                            string setName2 = evt.SetName;
                            long imageId2 = evt.ImageId;
                            string stepName2 = evt.StepName;
                            int pct2 = (int)(evt.OverallProgress * 100);
                            Dispatcher.UIThread.Post(() =>
                            {
                                string imageName = ctx.ImageList.FindRow(setName2, imageId2)?.Name ?? $"Image {imageId2}";
                                progressVm.CurrentItem = $"{imageName} \u2014 {stepName2}";
                                // Show per-image progress (0-100%) rather than batch progress
                                progressVm.Percentage = pct2;
                            });
                        },
                        onImageOutput: (sn, imageId, text) => { _ = ctx.SyncService.AppendImageOutputAsync(sn, imageId, text); },
                        onImageVerified: (imageId, success) =>
                        {
                            VerifyResultStatus status = success
                                ? VerifyResultStatus.VerifySuccess
                                : VerifyResultStatus.VerifyFailed;
                            _ = ctx.SyncService.SetVerifyResultAsync(setName, imageId, status);

                            // Per-image errors published to ErrorNotificationService per-image stream
                            if (!success)
                            {
                                ctx.ErrorNotification.PublishImageError(
                                    setName, imageId, "Verification failed");
                            }
                        },
                        cancellationToken: progressVm.CancellationToken,
                        keyFixPaths: keyFixPaths);

                    cumulativeProcessed += imageIds?.Count ?? result.ItemsProcessed;
                }
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Verify");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError($"Verify failed: {ex.Message}");
            }
            finally
            {
                // Drain any in-flight per-image UI posts (verify-thread callbacks marshalled via
                // Dispatcher.UIThread.Post) before the grid reset, so the reset does not fight a
                // still-arriving callback storm — mirrors the drain the Add path uses.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                // Force DataGrid to refresh cells (Avalonia DataGridTemplateColumn doesn't always
                // pick up PropertyChanged on existing rows without a collection notification)
                ctx.ImageList.FilteredImages.RaiseReset();

                await progressVm.WaitForMinimumDisplayTimeAsync();
                ctx.ActiveOperations.Remove(progressVm);
                ctx.SharedState.OperationInProgressOnSet = null;
                progressVm.Dispose();
            }

            return RxVoid.Default;
        });
    }
}
using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the "Process" context menu action for AddCancelled rows.
/// Collects selected AddCancelled rows, extracts their CandidateSource data,
/// transitions them back to Pending, and calls AddPreScannedAsync to re-process.
/// </summary>
public class ProcessAddCancelledCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Collect selected AddCancelled rows that have CandidateSource data
            List<ImageRowViewModel> selectedRows = ctx.ImageList.SelectedImages
                .Where(r => r.ProcessingStatus == ImageProcessingStatus.AddCancelled
                         && r.CandidateSource != null)
                .ToList();

            if (selectedRows.Count == 0) return RxVoid.Default;

            // 2. Build the CandidateImage list from stored source data
            List<CandidateImage> candidates = selectedRows
                .Select(r => r.CandidateSource!)
                .ToList();

            // Determine the set name from the first selected row
            string setName = selectedRows[0].SetName;

            // Resolve the dataStorePath for this set
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            string? dataStorePath = SessionResolver.ResolveDataStorePathForSet(sessions, setName);
            if (dataStorePath == null) return RxVoid.Default;

            // 3. Transition selected rows to Pending status
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (ImageRowViewModel row in selectedRows)
                {
                    row.ProcessingStatus = ImageProcessingStatus.Pending;
                    row.StatusReason = null;
                }
            });

            // 4. Set up progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Process Add Cancelled" };
            progressVm.TotalItems = candidates.Count;
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = setName;

            // Resolve key/fix paths from config for the NKit pipeline
            ResolvedKeyFixPaths keyFixPaths = KeyFixPathsResolver.Resolve(ctx.ConfigService);

            try
            {
                // Suppress intermediate session emissions during close→reopen cycle
                ctx.ImageList.SuppressFilters();

                // Use SessionManager's exclusive access pattern to close/reopen cleanly
                await ctx.SessionManager.WithExclusiveAccessAsync(async () =>
                {
                    using NkdsOperations ops = new NkdsOperations();

                    // Call AddPreScannedAsync to re-process the candidates
                    await ops.AddPreScannedAsync(
                        dataStorePath, setName, candidates,
                        onImageCommitted: evt =>
                        {
                            _ = ctx.SyncService.InsertImageAsync(evt);
                        },
                        onImageProgress: evt =>
                        {
                            _ = ctx.SyncService.UpdateImageProgressAsync(evt);
                            string imageName = ctx.ImageList.FindRow(evt.SetName, evt.ImageId)?.Name ?? $"Image {evt.ImageId}";
                            progressVm.CurrentItem = $"{imageName} \u2014 {evt.StepName}";
                            progressVm.Percentage = (int)(evt.OverallProgress * 100);
                        },
                        onImageOutput: (outputSetName, imageId, text) =>
                        {
                            _ = ctx.SyncService.AppendImageOutputAsync(outputSetName, imageId, text);
                        },
                        onItemStarted: (index, total) =>
                        {
                            progressVm.ItemsProcessed = index;
                        },
                        cancellationToken: progressVm.CancellationToken,
                        keyFixPaths: keyFixPaths);

                    progressVm.ItemsProcessed = candidates.Count;

                    // Flush pending UI dispatches before session reopen
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                });

                // Resume filters with current sessions after exclusive access completes
                ctx.ImageList.ResumeFiltersWithoutRebuild();

                // Reconcile to merge any images that weren't live-inserted
                await ctx.SyncService.ReconcileAsync(setName);
            }
            catch (OperationCanceledException)
            {
                // Mark remaining Pending rows as AddCancelled again
                await ctx.SyncService.SetBulkAddCancelledAsync(setName);

                ctx.ImageList.ResumeFiltersWithoutRebuild();
                await ctx.SyncService.ReconcileAsync(setName);
                ctx.ErrorNotification.PublishCancellation("Process Add Cancelled");
            }
            catch (Exception ex)
            {
                ctx.ImageList.ResumeFiltersWithoutRebuild();
                await ctx.SyncService.ReconcileAsync(setName);
                ctx.ErrorNotification.PublishOperationError($"Process Add Cancelled failed: {ex.Message}");
            }
            finally
            {
                await progressVm.WaitForMinimumDisplayTimeAsync();
                ctx.ActiveOperations.Remove(progressVm);
                ctx.SharedState.OperationInProgressOnSet = null;
                progressVm.Dispose();
            }

            return RxVoid.Default;
        });
    }
}
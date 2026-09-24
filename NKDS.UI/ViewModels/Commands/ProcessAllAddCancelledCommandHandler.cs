using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the "Process All" context menu action for AddCancelled rows.
/// Collects ALL AddCancelled rows (regardless of selection), extracts their CandidateSource data,
/// transitions them back to Pending, and calls AddPreScannedAsync to re-process.
/// </summary>
public class ProcessAllAddCancelledCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Collect ALL AddCancelled rows that have CandidateSource data
            List<ImageRowViewModel> allRows = ctx.ImageList.GetAllImages()
                .Where(r => r.ProcessingStatus == ImageProcessingStatus.AddCancelled
                         && r.CandidateSource != null)
                .ToList();

            if (allRows.Count == 0) return RxVoid.Default;

            // 2. Build the CandidateImage list from stored source data
            List<CandidateImage> candidates = allRows
                .Select(r => r.CandidateSource!)
                .ToList();

            // Determine the set name from the first row
            string setName = allRows[0].SetName;

            // Resolve the dataStorePath for this set
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            string? dataStorePath = SessionResolver.ResolveDataStorePathForSet(sessions, setName);
            if (dataStorePath == null) return RxVoid.Default;

            // 3. Transition all rows to Pending status
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (ImageRowViewModel row in allRows)
                {
                    row.ProcessingStatus = ImageProcessingStatus.Pending;
                    row.StatusReason = null;
                }
            });

            // 4. Set up progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Process All Add Cancelled" };
            progressVm.TotalItems = candidates.Count;
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = setName;

            // Resolve key/fix paths from config for the NKit pipeline
            ResolvedKeyFixPaths keyFixPaths = KeyFixPathsResolver.Resolve(ctx.ConfigService);

            try
            {
                // Suppress intermediate session emissions during close→reopen cycle
                ctx.ImageList.SuppressFilters();

                await ctx.SessionManager.WithExclusiveAccessAsync(async () =>
                {
                    using NkdsOperations ops = new NkdsOperations();

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

                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                });

                ctx.ImageList.ResumeFiltersWithoutRebuild();
                await ctx.SyncService.ReconcileAsync(setName);
            }
            catch (OperationCanceledException)
            {
                await ctx.SyncService.SetBulkAddCancelledAsync(setName);
                ctx.ImageList.ResumeFiltersWithoutRebuild();
                await ctx.SyncService.ReconcileAsync(setName);
                ctx.ErrorNotification.PublishCancellation("Process All Add Cancelled");
            }
            catch (Exception ex)
            {
                ctx.ImageList.ResumeFiltersWithoutRebuild();
                await ctx.SyncService.ReconcileAsync(setName);
                ctx.ErrorNotification.PublishOperationError($"Process All Add Cancelled failed: {ex.Message}");
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
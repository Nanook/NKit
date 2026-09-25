using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Add Images toolbar operation.
/// Lifecycle: resolve session → show AddImagesDialog → create progress →
/// exclusive access via SessionManager → NkdsOperations.AddAsync → reconcile via SyncService.
/// </summary>
public class AddCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx) => ExecuteCore(ctx, null);

    /// <summary>
    /// Executes the Add Images flow with files pre-populated in the dialog.
    /// Called when files are dragged onto the main window.
    /// </summary>
    public IObservable<RxVoid> ExecuteWithFiles(OperationContext ctx, IReadOnlyList<string> prePopulatedFiles) => ExecuteCore(ctx, prePopulatedFiles);

    private IObservable<RxVoid> ExecuteCore(OperationContext ctx, IReadOnlyList<string>? prePopulatedFiles)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0 || ctx.Toolbar.SelectedSetName == null) return RxVoid.Default;

            // Filter out "All" — only real set names are valid targets for Add
            List<string> availableSetNames = ctx.Toolbar.AvailableSetNames
                .Where(n => !string.Equals(n, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            string? defaultSet = string.Equals(ctx.Toolbar.SelectedSetName, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase)
                ? availableSetNames.FirstOrDefault()
                : ctx.Toolbar.SelectedSetName;

            AddImagesDialogViewModel dialogVm = new AddImagesDialogViewModel(availableSetNames, defaultSet, ctx.ConfigService);

            // Pre-populate files if provided (e.g. from drag-and-drop on the main window)
            if (prePopulatedFiles != null)
            {
                foreach (string file in prePopulatedFiles)
                {
                    if (!dialogVm.SelectedFiles.Contains(file))
                        dialogVm.SelectedFiles.Add(file);
                }
            }

            bool confirmed = await ctx.ShowAddImagesDialog.Handle(dialogVm);
            if (!confirmed || dialogVm.SelectedFiles.Count == 0 || dialogVm.SelectedSetName == null) return RxVoid.Default;

            // Resolve the correct dataStorePath from the session that owns the selected set
            string? dataStorePath = SessionResolver.ResolveDataStorePathForSet(sessions, dialogVm.SelectedSetName);
            if (dataStorePath == null) return RxVoid.Default;

            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Add Images" };
            progressVm.TotalItems = 0; // Updated after PreScan completes
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = dialogVm.SelectedSetName;

            // Resolve key/fix paths from config for the NKit pipeline
            ResolvedKeyFixPaths keyFixPaths = KeyFixPathsResolver.Resolve(ctx.ConfigService);
            string setName = dialogVm.SelectedSetName;

            try
            {
                // Suppress intermediate session emissions from clearing the image list
                // during the close→reopen cycle. Live insertions still work (they bypass ApplyFilters).
                ctx.ImageList.SuppressFilters();

                // Use SessionManager's exclusive access pattern to close/reopen cleanly
                await ctx.SessionManager.WithExclusiveAccessAsync(async () =>
                {
                    using NkdsOperations ops = new NkdsOperations();
                    List<string> files = dialogVm.SelectedFiles.ToList();

                    // Phase 1: Pre-Scan — discover all candidate images upfront
                    IReadOnlyList<CandidateImage> candidates = await ops.PreScanAsync(
                        dataStorePath, setName, files,
                        keyFixPaths: keyFixPaths,
                        cancellationToken: progressVm.CancellationToken);

                    // Set accurate progress total from true image count (excluding synthetic folder entries)
                    progressVm.TotalItems = candidates.Count(c => !c.Source.IsSyntheticFolder);

                    // Clear sort so pre-populated rows appear in scan order (processing order)
                    await Dispatcher.UIThread.InvokeAsync(() => ctx.ImageList.ClearSort());

                    // Phase 2: Pre-Populate — insert Pending rows into Image_List before processing
                    await ctx.SyncService.PrePopulateAsync(setName, dataStorePath, candidates);

                    // Phase 3: Process — execute pipeline for each candidate sequentially
                    // Detect aux mode: if an aux store already exists in the directory, enable it.
                    // This ensures Xbox dual mode (aux + split) works when aux mode was previously configured.
                    string? auxModeForAdd = null;
                    try
                    {
                        string primarySetPath = System.IO.Path.Combine(dataStorePath, setName + NKitDataStore.DataStore.DatabaseFileExtension);
                        if (System.IO.File.Exists(primarySetPath))
                        {
                            string? existingAux = NKitDataStore.DataStore.ResolveAuxSetName(primarySetPath);
                            if (existingAux != null)
                                auxModeForAdd = "y";
                        }
                    }
                    catch { /* ignore — aux detection failure is non-fatal */ }

                    OperationResult result = await ops.AddPreScannedAsync(
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
                        keyFixPaths: keyFixPaths,
                        auxModeText: auxModeForAdd);

                    progressVm.ItemsProcessed = candidates.Count(c => !c.Source.IsSyntheticFolder);

                    // Report if processing failed entirely (no items processed)
                    if (!result.Success && result.ItemsProcessed == 0 && result.Errors.Count > 0)
                    {
                        OperationErrorEntry firstError = result.Errors[0];
                        ctx.ErrorNotification.PublishOperationError(
                            $"Add Images pipeline error: {firstError.Reason}");
                    }

                    // If cancelled, ensure remaining rows are marked AddCancelled
                    if (result.WasCancelled)
                        await ctx.SyncService.SetBulkAddCancelledAsync(setName);

                    // Flush pending UI dispatches before session reopen
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                });

                // Flush deferred Sessions emissions from the close/reopen cycle.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                // Resume without a full rebuild — pre-populated rows are already in _allImages
                // and committed rows have been updated in-place via InsertImageAsync.
                ctx.ImageList.ResumeFiltersWithoutRebuild();

                // NOTE: deliberately NO post-add ReconcileAsync here. Committed images are already
                // shown via the live onImageCommitted -> InsertImageAsync updates, so reconcile was
                // only a safety-net merge — but it also PRUNED terminal outcome rows that have no
                // backing store image (a rolled-back duplicate shows "Skipped"), making them flash
                // and vanish. We now leave those outcome rows in place; a manual Refresh re-queries
                // the store and clears them. (A rare committed-but-not-live-inserted image likewise
                // appears on the next Refresh.)
                await ctx.SyncService.RefreshAvailableSetNamesAsync();
            }
            catch (OperationCanceledException)
            {
                // Mark all remaining Pending rows as AddCancelled
                await ctx.SyncService.SetBulkAddCancelledAsync(setName);

                // Flush deferred Sessions emissions while still suppressed
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                ctx.ImageList.ResumeFiltersWithoutRebuild();
                // No ReconcileAsync needed — cancelled images weren't committed to DataStore
                ctx.ErrorNotification.PublishCancellation("Add Images");
            }
            catch (Exception ex)
            {
                // Flush deferred Sessions emissions while still suppressed
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                ctx.ImageList.ResumeFiltersWithoutRebuild();
                // No ReconcileAsync needed — error path, rows are preserved as-is
                ctx.ErrorNotification.PublishOperationError($"Add Images failed: {ex.Message}");
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
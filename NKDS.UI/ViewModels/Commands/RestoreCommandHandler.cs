using NKDS;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Restore toolbar operation.
/// Lifecycle: session resolution → create progress → exclusive access → NkdsOperations.Restore → reconcile.
/// Operates on selected removed images directly (no dialog).
/// Per-image errors are published to ErrorNotificationService per-image stream.
/// </summary>
public class RestoreCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve selected removed images
            List<ImageRowViewModel> selectedRows = ctx.ImageList.SelectedImages
                .Where(r => r.Removed)
                .ToList();
            if (selectedRows.Count == 0) return RxVoid.Default;

            // 2. Resolve session for the first row's set
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0) return RxVoid.Default;

            ImageSessionModel? firstSession = SessionResolver.FindSessionForSet(sessions, selectedRows[0].Image.SetName);
            if (firstSession == null) return RxVoid.Default;
            string dataStorePath = SessionResolver.ResolveDataStorePath(firstSession).dataStorePath;

            // 3. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Restore" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = selectedRows[0].Image.SetName;

            try
            {
                progressVm.TotalItems = selectedRows.Count;

                // Suppress filter updates during session close/reopen — Restore only changes
                // a property in-place, the list shouldn't visually rebuild
                ctx.ImageList.SuppressFilters();

                await ctx.SessionManager.WithExclusiveAccessAsync(async () =>
                {
                    using NkdsOperations ops = new NkdsOperations();
                    foreach (ImageRowViewModel row in selectedRows)
                    {
                        try
                        {
                            ops.Restore(dataStorePath, row.Image.SetName, new[] { row.Image.Id });
                            row.MarkRestored();
                        }
                        catch (Exception ex)
                        {
                            ctx.ErrorNotification.PublishImageError(
                                row.Image.SetName,
                                row.Image.Id,
                                $"Restore failed: {ex.Message}");
                        }

                        progressVm.ItemsProcessed++;
                        progressVm.Percentage = (int)(progressVm.ItemsProcessed * 100.0 / progressVm.TotalItems);
                        progressVm.CurrentItem = row.Name;
                    }
                });

                ctx.ImageList.ResumeFilters();

                // No reconcile needed — MarkRestored() updates the row's Removed property in-place
                // and the DataGrid picks up the change via INotifyPropertyChanged binding.

                // Re-evaluate whether there are still removed images
                IReadOnlyList<ImageSessionModel> updatedSessions = await ctx.DataStoreService.Sessions.FirstAsync();
                updateHasRemovedImages(updatedSessions, ctx.SharedState);
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Restore");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Restore failed: {ex.Message}");
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

    /// <summary>
    /// Re-evaluates whether there are still removed images in the active set.
    /// </summary>
    private static void updateHasRemovedImages(IReadOnlyList<ImageSessionModel> sessions, SharedObservableState sharedState)
    {
        string? selectedSetName = sharedState.SelectedSetName;
        if (string.IsNullOrEmpty(selectedSetName))
        {
            sharedState.HasRemovedImages = false;
            return;
        }

        foreach (ImageSessionModel session in sessions)
        {
            if (string.Equals(selectedSetName, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase))
            {
                if (session.Images.Any(img => img.Removed))
                {
                    sharedState.HasRemovedImages = true;
                    return;
                }
            }
            else if (session.Images.Any(img => img.SetName == selectedSetName && img.Removed))
            {
                sharedState.HasRemovedImages = true;
                return;
            }
        }

        sharedState.HasRemovedImages = false;
    }
}
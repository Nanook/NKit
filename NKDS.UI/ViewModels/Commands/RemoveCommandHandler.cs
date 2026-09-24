using NKDS;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Remove toolbar operation.
/// Lifecycle: resolve session → create progress → exclusive access → NkdsOperations.Remove per-image → update UI.
/// No dialog is shown — Remove operates on the currently selected (non-removed) images directly.
/// </summary>
public class RemoveCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Gather selected non-removed images
            List<ImageRowViewModel> selectedRows = ctx.ImageList.SelectedImages
                .Where(r => !r.Removed)
                .ToList();
            if (selectedRows.Count == 0) return RxVoid.Default;

            // 2. Resolve session from the first image's set name
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0) return RxVoid.Default;

            string setName = selectedRows[0].Image.SetName;
            string? dataStorePath = SessionResolver.ResolveDataStorePathForSet(sessions, setName);
            if (dataStorePath == null) return RxVoid.Default;

            // 3. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Remove" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = setName;

            try
            {
                progressVm.TotalItems = selectedRows.Count;

                // Suppress filter updates during session close/reopen — Remove only changes
                // a property in-place, the list shouldn't visually rebuild
                ctx.ImageList.SuppressFilters();
                await ctx.SessionManager.WithExclusiveAccessAsync(async () =>
                {
                    using NkdsOperations ops = new NkdsOperations();
                    foreach (ImageRowViewModel row in selectedRows)
                    {
                        try
                        {
                            ops.Remove(dataStorePath, row.Image.SetName, new[] { row.Image.Id });
                            row.MarkRemoved();
                        }
                        catch (Exception ex)
                        {
                            ctx.ErrorNotification.PublishImageError(
                                row.Image.SetName, row.Image.Id,
                                $"Remove failed for {row.Name}: {ex.Message}");
                        }

                        progressVm.ItemsProcessed++;
                        progressVm.Percentage = (int)(progressVm.ItemsProcessed * 100.0 / progressVm.TotalItems);
                        progressVm.CurrentItem = row.Name;
                    }
                });
                ctx.ImageList.ResumeFilters();

                ctx.SharedState.HasRemovedImages = true;
            }
            catch (OperationCanceledException)
            {
                ctx.ImageList.ResumeFilters(applyNow: false);
                ctx.ErrorNotification.PublishCancellation("Remove");
            }
            catch (Exception ex)
            {
                ctx.ImageList.ResumeFilters(applyNow: false);
                ctx.ErrorNotification.PublishOperationError(
                    $"Remove failed: {ex.Message}");
            }
            finally
            {
                await progressVm.WaitForMinimumDisplayTimeAsync();
                ctx.ActiveOperations.Remove(progressVm);
                progressVm.Dispose();
                ctx.SharedState.OperationInProgressOnSet = null;
            }

            return RxVoid.Default;
        });
    }
}
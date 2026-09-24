using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Rollback toolbar operation.
/// Lifecycle: resolve session → gather images → show RollbackDialog → create progress
///            → exclusive access → NkdsOperations.Rollback → reconcile.
/// </summary>
public class RollbackCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve session
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0) return RxVoid.Default;

            ImageSessionModel session = sessions[0];
            (string? dataStorePath, string _) = SessionResolver.ResolveDataStorePath(session);

            if (ctx.Toolbar.SelectedSetName == null) return RxVoid.Default;

            // 2. Gather images from all real sets (the dialog will filter by selected set)
            List<RollbackImageItem> images = new List<RollbackImageItem>();
            foreach (ImageSessionModel? s in sessions)
            {
                try
                {
                    foreach (string setName in s.DataStore.ListSetNames())
                    {
                        List<RollbackImageItem> setImages = s.DataStore.ListImagesInSet(setName)
                            .Where(img => !img.Removed)
                            .Select(img => new RollbackImageItem { Id = img.Id, Name = img.Name, SetName = setName })
                            .ToList();
                        images.AddRange(setImages);
                    }
                }
                catch { /* Skip if DataStore access fails */ }
            }

            if (images.Count == 0) return RxVoid.Default;

            // 3. Pre-select the toolbar's current set (or first available if "All")
            string? activeSet = string.Equals(ctx.Toolbar.SelectedSetName, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase) ? null : ctx.Toolbar.SelectedSetName;

            RollbackDialogViewModel dialogVm = new RollbackDialogViewModel(images, ctx.Toolbar.AvailableSetNames, activeSet);
            bool confirmed = await ctx.ShowRollbackDialog.Handle(dialogVm);
            if (!confirmed || !dialogVm.SelectedTargetId.HasValue || dialogVm.SelectedSetName == null) return RxVoid.Default;

            string targetSetName = dialogVm.SelectedSetName;

            // 4. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Rollback" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = targetSetName;

            try
            {
                // 5. Exclusive access → rollback → reconcile
                await ctx.ExclusiveAccessManager.WithExclusiveAccessAsync(
                    dataStorePath,
                    path =>
                    {
                        using NkdsOperations ops = new NkdsOperations();
                        IProgress<OperationProgress> reporter = progressVm.CreateProgressReporter();
                        ops.Rollback(path, targetSetName, dialogVm.SelectedTargetId.Value, reporter);
                    });

                // 6. Post-operation reconciliation
                await ctx.SyncService.ReconcileAsync(targetSetName);
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Rollback");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Rollback failed: {ex.Message}");
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
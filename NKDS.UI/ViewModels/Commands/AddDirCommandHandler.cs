using NKDS;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Add Directory toolbar operation.
/// Lifecycle: resolve session → show folder picker → create progress →
/// exclusive access → NkdsOperations.AddDirAsync → reconcile.
/// </summary>
public class AddDirCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve session
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0 || ctx.Toolbar.SelectedSetName == null) return RxVoid.Default;

            // 2. Show folder picker
            string selectedPath = await ctx.ShowFolderPicker.Handle(RxVoid.Default);
            if (string.IsNullOrEmpty(selectedPath)) return RxVoid.Default;

            // 3. Determine target set (skip "All")
            string? targetSet = string.Equals(ctx.Toolbar.SelectedSetName, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase)
                ? ctx.Toolbar.AvailableSetNames.FirstOrDefault(n => !string.Equals(n, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase))
                : ctx.Toolbar.SelectedSetName;
            if (targetSet == null) return RxVoid.Default;

            // 4. Resolve the correct dataStorePath from the session that owns the target set
            ImageSessionModel? session = SessionResolver.FindSessionForSet(sessions, targetSet);
            if (session == null) return RxVoid.Default;
            string dataStorePath = SessionResolver.ResolveDataStorePath(session).dataStorePath;

            // 5. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Add Directory" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = targetSet;

            try
            {
                // 6. Exclusive access → AddDirAsync → reconcile
                ctx.ImageList.SuppressFilters();
                await ctx.ExclusiveAccessManager.WithExclusiveAccessAsync(
                    dataStorePath,
                    async path =>
                    {
                        using NkdsOperations ops = new NkdsOperations();
                        await ops.AddDirAsync(path, targetSet, new[] { selectedPath },
                            progressVm.CreateProgressReporter(), progressVm.CancellationToken);
                    });
                IReadOnlyList<ImageSessionModel> currentSessions = await ctx.DataStoreService.Sessions.FirstAsync();
                ctx.ImageList.ResumeFilters(currentSessions);

                // 7. Post-operation reconciliation
                await ctx.SyncService.ReconcileAsync(targetSet);
                await ctx.SyncService.RefreshAvailableSetNamesAsync();
            }
            catch (OperationCanceledException)
            {
                IReadOnlyList<ImageSessionModel> cancelledSessions = await ctx.DataStoreService.Sessions.FirstAsync();
                ctx.ImageList.ResumeFilters(cancelledSessions);
                await ctx.SyncService.ReconcileAsync(targetSet);
                ctx.ErrorNotification.PublishCancellation("Add Directory");
            }
            catch (Exception ex)
            {
                IReadOnlyList<ImageSessionModel> errorSessions = await ctx.DataStoreService.Sessions.FirstAsync();
                ctx.ImageList.ResumeFilters(errorSessions);
                await ctx.SyncService.ReconcileAsync(targetSet);
                ctx.ErrorNotification.PublishOperationError($"Add Directory failed: {ex.Message}");
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
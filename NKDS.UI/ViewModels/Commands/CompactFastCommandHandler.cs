using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Fast Compact toolbar operation.
/// Index-only compact: merges all Block_Index deltas and removes dead image sections.
/// Shard files are never touched.
/// Lifecycle: resolve session → show CompactDialog → create progress → exclusive access → compact-fast → reconcile.
/// </summary>
public class CompactFastCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve session
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0 || ctx.Toolbar.SelectedSetName == null) return RxVoid.Default;

            ImageSessionModel session = sessions[0];
            (string? dataStorePath, string? originalPath) = SessionResolver.ResolveDataStorePath(session);

            // 2. Show CompactDialog (same checklist as full compact — user picks which sets)
            List<string> availableSetNames = ctx.Toolbar.AvailableSetNames.ToList();
            CompactDialogViewModel dialogVm = new CompactDialogViewModel(availableSetNames, ctx.Toolbar.SelectedSetName);
            bool confirmed = await ctx.ShowCompactDialog.Handle(dialogVm);
            if (!confirmed) return RxVoid.Default;

            IReadOnlyList<string> selectedSets = dialogVm.SelectedSetNames;
            if (selectedSets.Count == 0) return RxVoid.Default;

            // 3. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Fast Compact" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = selectedSets.Count == 1
                ? selectedSets[0]
                : string.Join(", ", selectedSets);

            try
            {
                // 4. Exclusive access → compact-fast
                await ctx.SessionManager.WithExclusiveAccessAsync(async () =>
                {
                    using NkdsOperations ops = new NkdsOperations();
                    for (int i = 0; i < selectedSets.Count; i++)
                    {
                        string setName = selectedSets[i];
                        string prefix = $"[{i + 1}/{selectedSets.Count}] {setName}";

                        IProgress<OperationProgress> reporter = progressVm.CreateProgressReporter();
                        Progress<OperationProgress> setProgress = new Progress<OperationProgress>(p =>
                        {
                            try
                            {
                                reporter.Report(new OperationProgress
                                {
                                    Percentage = p.Percentage,
                                    CurrentItem = $"{prefix} - [{p.CurrentItem?.Split(" - [").LastOrDefault()?.TrimEnd(']') ?? ""}]",
                                    ItemsProcessed = i,
                                    TotalItems = selectedSets.Count,
                                    Elapsed = p.Elapsed
                                });
                            }
                            catch { }
                        });

                        await ops.CompactFastAsync(dataStorePath, setName, setProgress, progressVm.CancellationToken);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Fast Compact");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError($"Fast Compact failed: {ex.Message}");
            }
            finally
            {
                await progressVm.WaitForMinimumDisplayTimeAsync();
                ctx.ActiveOperations.Remove(progressVm);
                ctx.SharedState.OperationInProgressOnSet = null;
                progressVm.Dispose();
            }

            // 5. Post-operation reconciliation
            foreach (string setName in selectedSets)
            {
                await ctx.SyncService.ReconcileAsync(setName);
            }

            return RxVoid.Default;
        });
    }
}
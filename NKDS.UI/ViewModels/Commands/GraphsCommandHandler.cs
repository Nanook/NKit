using NkdsUi.Models;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Diagnostics;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Graphs toolbar operation.
/// Read-only operation — does NOT use ExclusiveAccessManager.
/// Lifecycle: resolve session → check if stats already computed →
///   if yes: show brief progress → build graph VM → show dialog
///   if no: create progress → calculate stats with progress → build graph VM → show dialog
/// </summary>
public class GraphsCommandHandler : ICommandHandler
{
    private readonly StatsCalculationViewModel _statsCalculation;

    public GraphsCommandHandler(StatsCalculationViewModel statsCalculation)
    {
        _statsCalculation = statsCalculation;
    }

    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve session
            IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            if (sessions.Count == 0 || ctx.Toolbar.SelectedSetName == null) return RxVoid.Default;

            if (_statsCalculation.HasComputedStats)
            {
                // Stats already computed — show a brief progress indicator while gathering graph data
                OperationProgressViewModel graphProgressVm = new OperationProgressViewModel
                {
                    OperationName = "Stats",
                    CurrentItem = "Calculating graph...",
                    Percentage = 100
                };
                ctx.ActiveOperations.Add(graphProgressVm);

                try
                {
                    StatsGraphsViewModel graphVm = await _statsCalculation.BuildGraphViewModelAsync();
                    ctx.ActiveOperations.Remove(graphProgressVm);
                    graphProgressVm.Dispose();
                    await _statsCalculation.ShowGraphsDialog.Handle(graphVm);
                }
                catch (OperationCanceledException)
                {
                    ctx.ActiveOperations.Remove(graphProgressVm);
                    graphProgressVm.Dispose();
                    ctx.ErrorNotification.PublishCancellation("Graphs");
                }
                catch (Exception ex)
                {
                    ctx.ActiveOperations.Remove(graphProgressVm);
                    graphProgressVm.Dispose();
                    ctx.ErrorNotification.PublishOperationError($"Graphs failed: {ex.Message}");
                }
                return RxVoid.Default;
            }

            // 2. Stats not yet computed — calculate first, then show graphs
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Stats" };
            ctx.ActiveOperations.Add(progressVm);
            Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            // Subscribe to StatsCalculation progress and forward to progressVm
            using IDisposable progressSub = _statsCalculation.WhenAnyValue(x => x.ProcessedCount, x => x.TotalCount)
                .Subscribe(t =>
                {
                    progressVm.ItemsProcessed = t.Item1;
                    progressVm.TotalItems = t.Item2;
                    progressVm.Elapsed = sw.Elapsed;
                });

            bool hasStarted = false;
            using IDisposable textSub = _statsCalculation.WhenAnyValue(x => x.ProgressText)
                .Subscribe(text =>
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        if (hasStarted)
                        {
                            progressVm.CurrentItem = "Calculating graph...";
                            progressVm.Percentage = 100;
                        }
                        return;
                    }
                    hasStarted = true;

                    if (text.StartsWith("Gathering"))
                    {
                        progressVm.CurrentItem = "Gathering data...";
                    }
                    else if (text.StartsWith("Reading"))
                    {
                        progressVm.CurrentItem = "Reading block references...";
                    }
                    else if (text.StartsWith("Calculating totals"))
                    {
                        progressVm.CurrentItem = "Calculating totals...";
                    }

                    // Extract percentage from end of text (e.g. "Reading blocks 45%")
                    int spaceIdx = text.LastIndexOf(' ');
                    if (spaceIdx >= 0)
                    {
                        ReadOnlySpan<char> pctSpan = text.AsSpan(spaceIdx + 1).TrimEnd('%');
                        if (int.TryParse(pctSpan, out int pct))
                            progressVm.Percentage = Math.Clamp(pct, 0, 100);
                    }
                });

            try
            {
                // Link the progress panel's cancel button to the stats calculation's cancellation
                using IDisposable cancelSub = progressVm.CancelCommand
                    .Subscribe(_ => _statsCalculation.CancelCalculation());

                await _statsCalculation.CalculateStatsWithProgressAsync(progressVm);

                if (_statsCalculation.HasComputedStats)
                {
                    // Keep progress bar showing "Calculating graph..." while gathering graph data
                    progressVm.CurrentItem = "Calculating graph...";
                    progressVm.Percentage = 100;
                    StatsGraphsViewModel graphVm = await _statsCalculation.BuildGraphViewModelAsync();

                    // Remove progress bar just before showing the dialog
                    ctx.ActiveOperations.Remove(progressVm);
                    progressVm.Dispose();

                    await _statsCalculation.ShowGraphsDialog.Handle(graphVm);
                }
                else
                {
                    ctx.ActiveOperations.Remove(progressVm);
                    progressVm.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Graphs");
                ctx.ActiveOperations.Remove(progressVm);
                progressVm.Dispose();
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError($"Graphs/Stats failed: {ex.Message}");
                ctx.ActiveOperations.Remove(progressVm);
                progressVm.Dispose();
            }

            return RxVoid.Default;
        });
    }
}
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Diagnostics;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Stats toolbar operation.
/// Read-only operation: does NOT use ExclusiveAccessManager.
/// Lifecycle: create progress → subscribe to StatsCalculation progress → calculate stats → clean up.
/// </summary>
public class StatsCommandHandler : ICommandHandler
{
    private readonly StatsCalculationViewModel _statsCalculation;

    public StatsCommandHandler(StatsCalculationViewModel statsCalculation)
    {
        _statsCalculation = statsCalculation;
    }

    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Stats" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = "Stats";
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
                            // Calculation finished — show 100% briefly before panel is removed
                            progressVm.CurrentItem = "Complete";
                            progressVm.Percentage = 100;
                        }
                        return;
                    }
                    hasStarted = true;

                    // Show phase name and extract percentage from text
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
                // Link the progress panel's cancel button to the stats calculation's cancellation.
                using IDisposable cancelSub = progressVm.CancelCommand
                    .Subscribe(_ => _statsCalculation.CancelCalculation());

                // Call CalculateStatsWithProgressAsync which calls ExecuteCalculateStatsAsync directly
                // (not through the ReactiveCommand, which can have issues with await + cancellation).
                await _statsCalculation.CalculateStatsWithProgressAsync(progressVm);
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Stats");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError($"Stats failed: {ex.Message}");
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
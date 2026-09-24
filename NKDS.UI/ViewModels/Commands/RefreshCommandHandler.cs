using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Refresh toolbar operation.
/// Triggers SyncService reconciliation without exclusive access — picks up any
/// external changes to the DataStore by re-reading the current set from disk.
/// </summary>
public class RefreshCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            try
            {
                string? selectedSet = ctx.SharedState.SelectedSetName;
                if (string.IsNullOrEmpty(selectedSet))
                    return RxVoid.Default;

                if (selectedSet == "All")
                {
                    // "All" is a virtual filter — reconcile each real set
                    IReadOnlyList<string> availableSetNames = ctx.SharedState.AvailableSetNames;
                    foreach (string setName in availableSetNames)
                    {
                        if (setName == "All") continue;
                        await ctx.SyncService.ReconcileAsync(setName);
                    }
                }
                else
                {
                    await ctx.SyncService.ReconcileAsync(selectedSet);
                }

                await ctx.SyncService.RefreshAvailableSetNamesAsync();
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Refresh");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Refresh failed: {ex.Message}");
            }

            return RxVoid.Default;
        });
    }
}
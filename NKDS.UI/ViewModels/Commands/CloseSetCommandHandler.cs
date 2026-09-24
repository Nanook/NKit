using Avalonia.Threading;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Close Set toolbar operation.
/// Closes the current session without requiring exclusive access.
/// Lifecycle: reset UI state → suppress updates → close session → drain → resume.
/// </summary>
public class CloseSetCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            try
            {
                // 1. Reset transient UI state in preparation for session change
                ctx.SharedState.ShowFilterBar = false;
                ctx.SharedState.SelectedSetName = "All";
                ctx.ImageList.SelectedImages.Clear();
                ctx.ImageList.NameFilter = "";
                ctx.ImageList.SelectedSystemFilter = null;

                // 2. Clear all image rows (including any residual AddCancelled rows)
                ctx.ImageList.ClearAllRows();

                // 3. Clear residual progress counters and status indicators
                foreach (OperationProgressViewModel? op in ctx.ActiveOperations.ToList())
                    op.Dispose();
                ctx.ActiveOperations.Clear();

                // 4. Suppress intermediate reactive updates during close.
                //    Unmounting active mounts before the DataStore is disposed is handled centrally
                //    by SessionManager.Close() (via its BeforeClose hook), so it applies uniformly to
                //    every close path — not just this command. We just call Close() here.
                ctx.ImageList.SuppressFilters();
                try
                {
                    ctx.SessionManager.Close();

                    // Allow any queued reactive emissions to drain
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                finally
                {
                    ctx.ImageList.ResumeFilters();
                }
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Close Set");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Close Set failed: {ex.Message}");
            }

            return RxVoid.Default;
        });
    }
}
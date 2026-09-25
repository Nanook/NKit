using NkdsUi.Services;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Group toolbar operation.
/// Opens (or brings to focus) the GroupingWindow using ServiceRegistry services.
/// Lifecycle: create/reuse GroupingWindowViewModel → raise ShowGroupingWindow interaction → set IsGroupWindowOpen.
/// </summary>
public class GroupCommandHandler : ICommandHandler
{
    private readonly IHashCacheService _hashCacheService;
    private readonly IThresholdGroupingService _thresholdGroupingService;
    private GroupingWindowViewModel? _groupingWindowVm;

    public GroupCommandHandler(
        IHashCacheService hashCacheService,
        IThresholdGroupingService thresholdGroupingService)
    {
        _hashCacheService = hashCacheService;
        _thresholdGroupingService = thresholdGroupingService;
    }

    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.Create<RxVoid>(observer =>
        {
            try
            {
                if (_groupingWindowVm == null)
                {
                    // Window not open: create new GroupingWindowViewModel
                    _groupingWindowVm = new GroupingWindowViewModel(
                        _hashCacheService,
                        _thresholdGroupingService,
                        () => ctx.ImageList.FilteredImages.Select(r => r.Image).ToList());

                    ctx.ShowGroupingWindow.Handle(_groupingWindowVm).Subscribe();
                    ctx.SharedState.IsGroupWindowOpen = true;
                }
                else
                {
                    // Window already open: raise interaction again to bring to focus
                    ctx.ShowGroupingWindow.Handle(_groupingWindowVm).Subscribe();
                }

                observer.OnNext(RxVoid.Default);
                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Group failed: {ex.Message}");
                observer.OnNext(RxVoid.Default);
                observer.OnCompleted();
            }

            return EmptyDisposable.Instance;
        });
    }

    /// <summary>
    /// Called when the Grouping Window is closed.
    /// Resets state so the window can be reopened fresh.
    /// The ViewModel is disposed by the window's own Closed handler.
    /// </summary>
    public void OnGroupWindowClosed(SharedObservableState sharedState)
    {
        sharedState.IsGroupWindowOpen = false;
        _groupingWindowVm = null;
    }
}
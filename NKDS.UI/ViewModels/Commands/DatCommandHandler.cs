using NKDS.DatVerification;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Dat Verification toolbar operation.
/// Opens (or brings to focus) the DatVerificationWindow using ServiceRegistry services.
/// Lifecycle: create/reuse DatVerificationWindowViewModel → raise ShowDatVerificationWindow interaction → set IsDatWindowOpen.
/// </summary>
public class DatCommandHandler : ICommandHandler
{
    private readonly IDatVerificationService _datVerificationService;
    private readonly Interaction<DatVerificationWindowViewModel, RxVoid> _showDatVerificationWindow;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private DatVerificationWindowViewModel? _datVerificationWindowVm;

    public DatCommandHandler(
        IDatVerificationService datVerificationService,
        Interaction<DatVerificationWindowViewModel, RxVoid> showDatVerificationWindow,
        MainWindowViewModel mainWindowViewModel)
    {
        _datVerificationService = datVerificationService;
        _showDatVerificationWindow = showDatVerificationWindow;
        _mainWindowViewModel = mainWindowViewModel;
    }

    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.Create<RxVoid>(observer =>
        {
            try
            {
                if (_datVerificationWindowVm == null)
                {
                    // Window not open: create new DatVerificationWindowViewModel
                    _datVerificationWindowVm = new DatVerificationWindowViewModel(
                        _datVerificationService,
                        ctx.ConfigService,
                        ctx.DataStoreService,
                        _mainWindowViewModel);

                    _showDatVerificationWindow.Handle(_datVerificationWindowVm).Subscribe();
                    ctx.SharedState.IsDatWindowOpen = true;
                }
                else
                {
                    // Window already open: raise interaction again to bring to focus
                    _showDatVerificationWindow.Handle(_datVerificationWindowVm).Subscribe();
                }

                observer.OnNext(RxVoid.Default);
                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Dat Verification failed: {ex.Message}");
                observer.OnNext(RxVoid.Default);
                observer.OnCompleted();
            }

            return EmptyDisposable.Instance;
        });
    }

    /// <summary>
    /// Called when the Dat Verification Window is closed.
    /// Resets state so the window can be reopened fresh.
    /// The ViewModel is disposed by the window's own Closed handler.
    /// </summary>
    public void OnDatWindowClosed(SharedObservableState sharedState)
    {
        sharedState.IsDatWindowOpen = false;
        _datVerificationWindowVm = null;
    }
}
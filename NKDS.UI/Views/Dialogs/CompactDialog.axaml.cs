using Avalonia.Controls;
using NkdsUi.ViewModels;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views.Dialogs;

public partial class CompactDialog : Window
{
    private MultipleDisposable? _commandDisposables;

    public CompactDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _commandDisposables?.Dispose();
        _commandDisposables = null;

        if (DataContext is not CompactDialogViewModel viewModel)
            return;

        _commandDisposables = new MultipleDisposable();

        // Full compact — returns FullCompact
        viewModel.ConfirmCommand
            .Subscribe(_ => Close(CompactDialogResult.FullCompact))
            .DisposeWith(_commandDisposables);

        // Fast compact (index only) — returns FastCompact
        viewModel.ConfirmFastCommand
            .Subscribe(_ => Close(CompactDialogResult.FastCompact))
            .DisposeWith(_commandDisposables);

        // Cancel — returns Cancelled (default 0, same as X-button close)
        viewModel.CancelCommand
            .Subscribe(_ => Close(CompactDialogResult.Cancelled))
            .DisposeWith(_commandDisposables);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _commandDisposables?.Dispose();
        _commandDisposables = null;
        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }
}

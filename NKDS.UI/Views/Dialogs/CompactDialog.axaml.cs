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

        // When Confirm is executed, close the dialog with true result
        viewModel.ConfirmCommand
            .Subscribe(_ => Close(true))
            .DisposeWith(_commandDisposables);

        // When Cancel is executed, close the dialog with false result
        viewModel.CancelCommand
            .Subscribe(_ => Close(false))
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
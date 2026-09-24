using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Primitives;
using System.Globalization;

namespace NkdsUi.Views.Dialogs;

public partial class RollbackDialog : ReactiveWindow<RollbackDialogViewModel>
{
    private static readonly IValueConverter IsAfterSelectedToOpacityConverter = new AfterSelectedOpacityConverter();

    public RollbackDialog()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            if (ViewModel is null)
                return;

            // When ConfirmCommand executes (returns true), close the dialog with result true
            ViewModel.ConfirmCommand
                .Subscribe(result => Close(result))
                .DisposeWith(disposables);

            // When CancelCommand executes (returns false), close the dialog with result false
            ViewModel.CancelCommand
                .Subscribe(result => Close(result))
                .DisposeWith(disposables);
        });
    }

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        // Bind the row's opacity to the item's IsAfterSelected property
        e.Row.Bind(OpacityProperty, new Binding(nameof(RollbackImageItem.IsAfterSelected))
        {
            Converter = IsAfterSelectedToOpacityConverter
        });
    }

    /// <summary>
    /// Converts IsAfterSelected (bool) to an opacity value: true = dimmed (0.35), false = full (1.0).
    /// </summary>
    private class AfterSelectedOpacityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? 0.35 : 1.0;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
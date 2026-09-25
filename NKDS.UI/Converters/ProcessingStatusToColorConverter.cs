using Avalonia.Data.Converters;
using Avalonia.Media;
using NkdsUi.Models;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts an ImageProcessingStatus enum value to a colour brush for the status icon.
/// </summary>
public class ProcessingStatusToColorConverter : IValueConverter
{
    public static readonly ProcessingStatusToColorConverter Instance = new();

    private static readonly IBrush GreyBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));
    private static readonly IBrush BlueBrush = new SolidColorBrush(Color.Parse("#2196F3"));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F44336"));
    private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.Parse("#FF9800"));
    private static readonly IBrush AmberBrush = new SolidColorBrush(Color.Parse("#FFC107"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ImageProcessingStatus status)
        {
            return status switch
            {
                ImageProcessingStatus.None => Brushes.Transparent,
                ImageProcessingStatus.Pending => GreyBrush,
                ImageProcessingStatus.Processing => BlueBrush,
                ImageProcessingStatus.Completed => GreenBrush,
                ImageProcessingStatus.Failed => RedBrush,
                ImageProcessingStatus.Skipped => OrangeBrush,
                ImageProcessingStatus.Cancelled => RedBrush,
                ImageProcessingStatus.AddCancelled => AmberBrush,
                _ => GreyBrush
            };
        }
        return GreyBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
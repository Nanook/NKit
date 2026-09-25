using Avalonia.Data.Converters;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts a 0.0–1.0 ratio to a pixel width for bar chart visualization.
/// Max width is 600px.
/// </summary>
public class BarWidthConverter : IValueConverter
{
    public static readonly BarWidthConverter Instance = new();
    private const double MaxWidth = 600;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double ratio)
            return ratio * MaxWidth;
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
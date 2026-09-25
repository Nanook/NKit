using Avalonia.Controls;
using Avalonia.Data.Converters;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts a 0.0–1.0 ratio to a GridLength star value for proportional column widths.
/// </summary>
public class StarWidthConverter : IValueConverter
{
    public static readonly StarWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ratio = 0;
        if (value is double d)
            ratio = d;

        if (ratio > 0)
        {
            // If parameter is "100", divide by 100 to convert percentage to 0.0-1.0 ratio
            if (parameter is string paramStr && paramStr == "100")
                ratio /= 100.0;
            return new GridLength(ratio, GridUnitType.Star);
        }
        return new GridLength(0, GridUnitType.Pixel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
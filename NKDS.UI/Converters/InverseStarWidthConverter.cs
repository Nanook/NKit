using Avalonia.Controls;
using Avalonia.Data.Converters;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts a 0.0–1.0 ratio (or 0–100 percentage with parameter "100") to the inverse star value.
/// Used for the complementary column in a two-column grid (e.g., 35% → 0.65*).
/// </summary>
public class InverseStarWidthConverter : IValueConverter
{
    public static readonly InverseStarWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ratio = 0;
        if (value is double d)
            ratio = d;

        // If parameter is "100", divide by 100 to convert percentage to 0.0-1.0 ratio
        if (parameter is string paramStr && paramStr == "100")
            ratio /= 100.0;

        double inverse = 1.0 - Math.Clamp(ratio, 0, 1);
        if (inverse > 0)
            return new GridLength(inverse, GridUnitType.Star);
        return new GridLength(0, GridUnitType.Pixel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
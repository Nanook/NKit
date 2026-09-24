using Avalonia.Data.Converters;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts an operation name (e.g. "Add") to an SVG asset path (e.g. "/Assets/Toolbar/Add.svg").
/// The folder is specified via the ConverterParameter (defaults to "Toolbar").
/// Returns null for null, empty, "NotSet", or "0" values.
/// </summary>
public class SafeSvgPathConverter : IValueConverter
{
    public static readonly SafeSvgPathConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return null;

        string? enumValue = value.ToString();
        string folder = parameter?.ToString() ?? "Toolbar";

        // Check for invalid enum values
        if (string.IsNullOrWhiteSpace(enumValue) ||
            enumValue == "NotSet" ||
            enumValue == "0")
        {
            return null;
        }

        return $"/Assets/{folder}/{enumValue}.svg";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
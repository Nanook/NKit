using Avalonia.Data.Converters;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts an enum value to a boolean by comparing it to the converter parameter.
/// Used for binding radio buttons to enum properties.
/// Returns true when the bound value equals the parameter, false otherwise.
/// Setting the value back converts the parameter to the enum type.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return parameter;

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
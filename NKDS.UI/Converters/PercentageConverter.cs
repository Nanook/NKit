using Avalonia.Data.Converters;
using System.Globalization;

namespace NkdsUi.Converters
{
    public class PercentageConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
                return $"{d:F2}%";

            if (value is float f)
                return $"{f:F2}%";

            if (value is null)
                return "0.00%";

            // Try numeric conversion for other numeric types
            if (value is IConvertible convertible)
            {
                try
                {
                    double converted = convertible.ToDouble(CultureInfo.InvariantCulture);
                    return $"{converted:F2}%";
                }
                catch
                {
                    // Fall through to default
                }
            }

            return string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
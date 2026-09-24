using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace NKit.Ui.Converters
{
    public class BoolToTooltipParamConverter : IValueConverter
    {
        // Expects value: bool; parameter: tooltip string
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool enabled = value is bool b && b;
            if (!enabled) return null;
            return parameter?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
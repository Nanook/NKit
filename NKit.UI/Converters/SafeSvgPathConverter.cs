using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace NKit.Ui.Converters
{
    public class SafeSvgPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;

            string enumValue = value.ToString();
            string folder = parameter?.ToString() ?? "Tasks";

            // Check for invalid enum values
            if (string.IsNullOrWhiteSpace(enumValue) ||
                enumValue == "NotSet" ||
                enumValue == "0")
            {
                return null;
            }

            return $"/Assets/{folder}/{enumValue}.svg";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
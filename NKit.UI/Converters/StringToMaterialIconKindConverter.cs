using Avalonia.Data.Converters;
using Material.Icons;
using System;
using System.Globalization;

namespace NKit.Ui.Converters
{
    public class StringToMaterialIconKindConverter : IValueConverter
    {
        private const MaterialIconKind Default = MaterialIconKind.HelpCircleOutline;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Default;

            if (value is MaterialIconKind mk)
                return mk;

            string s = value as string;
            if (string.IsNullOrWhiteSpace(s))
                return Default;

            if (Enum.TryParse<MaterialIconKind>(s, true, out MaterialIconKind parsed))
                return parsed;

            return Default;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
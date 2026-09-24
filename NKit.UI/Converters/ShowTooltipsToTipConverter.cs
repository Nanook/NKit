using Avalonia.Data.Converters;
using NKit.Ui.Models;
using Splat;
using System;
using System.Globalization;

namespace NKit.Ui.Converters
{
    /// <summary>
    /// Returns the ConverterParameter (tooltip text) when global UI setting ShowTooltips is true; otherwise returns null.
    /// If the settings store or UiSettings cannot be resolved, defaults to returning the tooltip text to avoid silently hiding help.
    /// </summary>
    public class ShowTooltipsToTipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                ISettingsStore settingsStore = Locator.Current.GetService<ISettingsStore>();
                if (settingsStore == null)
                {
                    // If no settings store is available, assume tooltips should be shown
                    return parameter?.ToString() ?? string.Empty;
                }

                UiSettings ui = settingsStore.UiSettings;
                if (ui == null)
                {
                    // If UiSettings not present, assume tooltips should be shown
                    return parameter?.ToString() ?? string.Empty;
                }

                if (ui.ShowTooltips)
                    return parameter?.ToString() ?? string.Empty;
            }
            catch { }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
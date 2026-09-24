using Avalonia.Data.Converters;
using Avalonia.Media;
using NkdsUi.Models;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts a VerifyResultStatus enum value to a colour brush for display.
/// Green for success, red for failed, grey for unverified/none.
/// </summary>
public class VerifyResultToColorConverter : IValueConverter
{
    public static readonly VerifyResultToColorConverter Instance = new();

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F44336"));
    private static readonly IBrush GreyBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VerifyResultStatus status)
        {
            return status switch
            {
                VerifyResultStatus.VerifySuccess => GreenBrush,
                VerifyResultStatus.VerifyFailed => RedBrush,
                VerifyResultStatus.Unverified => GreyBrush,
                VerifyResultStatus.None => GreyBrush,
                _ => GreyBrush
            };
        }
        return GreyBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
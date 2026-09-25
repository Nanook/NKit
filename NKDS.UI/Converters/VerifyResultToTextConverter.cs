using Avalonia.Data.Converters;
using NkdsUi.Models;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts a VerifyResultStatus enum value to a display text string.
/// </summary>
public class VerifyResultToTextConverter : IValueConverter
{
    public static readonly VerifyResultToTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VerifyResultStatus status)
        {
            return status switch
            {
                VerifyResultStatus.None => string.Empty,
                VerifyResultStatus.VerifySuccess => "\u2713 Verified",
                VerifyResultStatus.Unverified => "\u2014 Unverified",
                VerifyResultStatus.VerifyFailed => "\u2717 Verify Failed",
                _ => string.Empty
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
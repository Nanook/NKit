using Avalonia.Data.Converters;
using NkdsUi.Models;
using System.Globalization;

namespace NkdsUi.Converters;

/// <summary>
/// Converts an ImageProcessingStatus enum value to a Unicode icon character for display.
/// </summary>
public class ProcessingStatusToIconConverter : IValueConverter
{
    public static readonly ProcessingStatusToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ImageProcessingStatus status)
        {
            return status switch
            {
                ImageProcessingStatus.None => string.Empty,
                ImageProcessingStatus.Pending => "\u25CC",      // ◌ (dotted circle)
                ImageProcessingStatus.Processing => "\u27F3",   // ⟳ (clockwise arrow)
                ImageProcessingStatus.Completed => "\u2713",    // ✓ (check mark)
                ImageProcessingStatus.Failed => "\u2717",       // ✗ (ballot x)
                ImageProcessingStatus.Skipped => "?",           // ? (question mark)
                ImageProcessingStatus.Cancelled => "\u2298",    // ⊘ (circled division slash)
                ImageProcessingStatus.AddCancelled => "\u2298", // ⊘ (circled division slash)
                _ => string.Empty
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
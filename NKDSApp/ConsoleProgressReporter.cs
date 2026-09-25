using NKDS.Models;
using System;
using System.Globalization;

namespace Nanook.NKit.Vfs;

/// <summary>
/// Reports <see cref="OperationProgress"/> to the console in a fixed-width format.
/// Output format: [  45%] Processing: filename.iso (3/10) [00:01:23]
/// </summary>
internal sealed class ConsoleProgressReporter : IProgress<OperationProgress>
{
    private readonly object _lock = new();
    private int _lastLineLength;

    public void Report(OperationProgress value)
    {
        if (value == null)
            return;

        string line = FormatProgress(value);

        lock (_lock)
        {
            // Overwrite the current line with carriage return
            Console.Write('\r');
            Console.Write(line);

            // Clear any leftover characters from the previous line
            int padding = _lastLineLength - line.Length;
            if (padding > 0)
                Console.Write(new string(' ', padding));

            _lastLineLength = line.Length;
        }
    }

    /// <summary>
    /// Writes a final newline after the last progress report so subsequent output starts on a new line.
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            if (_lastLineLength > 0)
            {
                Console.WriteLine();
                _lastLineLength = 0;
            }
        }
    }

    internal static string FormatProgress(OperationProgress value)
    {
        string elapsed = FormatElapsed(value.Elapsed);
        string percentage = value.Percentage.ToString(CultureInfo.InvariantCulture).PadLeft(3);
        string counts = $"({value.ItemsProcessed}/{value.TotalItems})";
        string item = value.CurrentItem ?? "";

        // Truncate item name if it would make the line too long for typical console width
        const int maxItemLength = 60;
        if (item.Length > maxItemLength)
            item = "..." + item[(item.Length - maxItemLength + 3)..];

        return $"[{percentage}%] Processing: {item} {counts} [{elapsed}]";
    }

    internal static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
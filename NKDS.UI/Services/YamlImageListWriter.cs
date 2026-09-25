using NkdsUi.ViewModels;
using System.Globalization;

namespace NkdsUi.Services;

/// <summary>
/// Writes image list data as a YAML 1.2 document using direct StreamWriter output.
/// No reflection APIs are used — fully AOT and trimming compatible.
/// Follows the same pattern as <see cref="PairwiseMatrixYamlWriter"/>.
/// </summary>
public static class YamlImageListWriter
{
    /// <summary>
    /// Writes the image list as a YAML 1.2 document to the given writer.
    /// </summary>
    /// <param name="writer">The StreamWriter to write to.</param>
    /// <param name="images">The images to export.</param>
    /// <param name="includeStats">Whether to include stats fields (when the image has computed stats).</param>
    /// <param name="source">The source identifier (set name or "All").</param>
    public static void Write(
        StreamWriter writer,
        IReadOnlyList<ImageRowViewModel> images,
        bool includeStats,
        string source)
    {
        // YAML document start marker
        writer.WriteLine("---");

        // Metadata section
        WriteMetadata(writer, images.Count, source);

        // Images section
        writer.WriteLine("images:");
        for (int i = 0; i < images.Count; i++)
        {
            WriteImage(writer, images[i], includeStats);
        }
    }

    private static void WriteMetadata(StreamWriter writer, int imageCount, string source)
    {
        string exportDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        writer.WriteLine("metadata:");
        writer.WriteLine($"  exportDate: \"{exportDate}\"");
        writer.WriteLine($"  imageCount: {imageCount}");
        writer.WriteLine($"  source: {QuoteIfNeeded(source)}");
    }

    private static void WriteImage(StreamWriter writer, ImageRowViewModel image, bool includeStats)
    {
        // Sequence indicator on first key
        writer.WriteLine($"  - name: {QuoteIfNeeded(image.Name)}");
        writer.WriteLine($"    system: {QuoteIfNeeded(image.System ?? "")}");
        writer.WriteLine($"    setName: {QuoteIfNeeded(image.SetName)}");
        writer.WriteLine($"    size: {image.Size}");
        writer.WriteLine($"    format: {QuoteIfNeeded(image.Format.ToString())}");
        writer.WriteLine($"    crc32: \"{image.Crc32.ToString("X8")}\"");
        writer.WriteLine($"    xxHash64: \"{image.XxHash64.ToString("X16")}\"");
        writer.WriteLine($"    removed: {(image.Removed ? "true" : "false")}");

        // Optional stats fields (only when stats are visible AND this image has stats)
        if (includeStats && image.HasStats)
        {
            if (image.UniqueRawSize.HasValue)
                writer.WriteLine($"    uniqueRawSize: {image.UniqueRawSize.Value}");
            if (image.UniqueCompressedSize.HasValue)
                writer.WriteLine($"    uniqueCompressedSize: {image.UniqueCompressedSize.Value}");
            if (image.SharedRawSize.HasValue)
                writer.WriteLine($"    sharedRawSize: {image.SharedRawSize.Value}");
            if (image.SharedCompressedSize.HasValue)
                writer.WriteLine($"    sharedCompressedSize: {image.SharedCompressedSize.Value}");
            if (image.SavedSize.HasValue)
                writer.WriteLine($"    savedSize: {image.SavedSize.Value}");
            if (image.ReducedBy.HasValue)
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "    reducedBy: {0:F1}", image.ReducedBy.Value));
        }
    }

    /// <summary>
    /// Quotes a string value if it contains YAML-special characters or is empty.
    /// </summary>
    internal static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        if (NeedsQuoting(value))
            return $"\"{EscapeYamlString(value)}\"";

        return value;
    }

    /// <summary>
    /// Determines if a string value needs quoting for YAML safety.
    /// </summary>
    internal static bool NeedsQuoting(string value)
    {
        if (value.Length == 0) return true;
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])) return true;

        foreach (char c in value)
        {
            if (IsYamlSpecialChar(c)) return true;
        }
        return false;
    }

    private static bool IsYamlSpecialChar(char c) => c switch
    {
        ':' or '#' or '[' or ']' or '{' or '}' or '&' or '*' or
        '!' or '|' or '>' or '\'' or '"' or '%' or ',' or '@' => true,
        _ => false
    };

    /// <summary>
    /// Escapes special characters within a double-quoted YAML string.
    /// In YAML double-quoted strings, backslash and double-quote must be escaped.
    /// </summary>
    internal static string EscapeYamlString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
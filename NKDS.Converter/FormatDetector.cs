namespace NKDS.Converter;

/// <summary>
/// Detects whether a file is SQLite format or Binary format by reading magic bytes.
/// </summary>
public static class FormatDetector
{
    public enum IndexFormat { Unknown, Sqlite, Binary, EmbeddedBinary }

    /// <summary>
    /// SQLite magic: "SQLite format 3\0" (16 bytes).
    /// </summary>
    private static readonly byte[] SqliteMagic =
        "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// NKDS binary magic: 0x4E4B4453 ("NKDS" in ASCII, big-endian).
    /// </summary>
    private static readonly byte[] NkdsMagic = [0x4E, 0x4B, 0x44, 0x53];

    /// <summary>
    /// Reads the first 16 bytes of the file to determine format.
    /// SQLite: starts with "SQLite format 3\0" (16 bytes)
    /// Binary: starts with 0x4E4B4453 (4 bytes "NKDS")
    /// Embedded: last 4 bytes are 0x4E4B4453 (footer magic)
    /// </summary>
    /// <param name="filePath">Path to the file to detect.</param>
    /// <returns>The detected format.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static IndexFormat Detect(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Source file not found.", filePath);

        using FileStream stream = File.OpenRead(filePath);
        long length = stream.Length;

        // Need at least 4 bytes to check any magic
        if (length < 4)
            return IndexFormat.Unknown;

        // Read first 16 bytes (or fewer if file is smaller)
        int headerSize = (int)Math.Min(16, length);
        byte[] header = new byte[headerSize];
        stream.ReadExactly(header, 0, headerSize);

        // Check SQLite magic (16 bytes)
        if (headerSize >= 16 && header.AsSpan(0, 16).SequenceEqual(SqliteMagic))
            return IndexFormat.Sqlite;

        // Check NKDS binary magic (first 4 bytes)
        if (header.AsSpan(0, 4).SequenceEqual(NkdsMagic))
            return IndexFormat.Binary;

        // Check embedded footer magic (last 4 bytes of file)
        if (length >= 4)
        {
            byte[] footer = new byte[4];
            stream.Seek(-4, SeekOrigin.End);
            stream.ReadExactly(footer, 0, 4);

            if (footer.AsSpan().SequenceEqual(NkdsMagic))
                return IndexFormat.EmbeddedBinary;
        }

        return IndexFormat.Unknown;
    }
}
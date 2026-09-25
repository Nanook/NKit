using Microsoft.Data.Sqlite;
using NKDS.Converter.Conversion;
using NKDS.Converter.Verification;

namespace NKDS.Converter;

/// <summary>
/// NKDS Converter - Bidirectional conversion between SQLite and binary NKDS index formats.
/// </summary>
public class Program
{
    private const string Usage =
        """
        NKDS Converter - SQLite <-> Binary index format converter

        Usage:
          NKDS.Converter <source-file> [options]

        Arguments:
          <source-file>    Path to the source index file (SQLite or binary .nkds)

        Options:
          --direction <to-binary|to-sqlite>  Conversion direction (auto-detected if omitted)
          --output <path>                    Output file path (defaults to same directory)
          --force                            Overwrite output file if it exists
          --verify                           Verify output after conversion

        Examples:
          NKDS.Converter mydata.nkds --direction to-binary
          NKDS.Converter mydata.nkds --output converted.nkds --force
          NKDS.Converter mydata.nkds --verify
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        // Parse arguments
        string? sourcePath = null;
        string? direction = null;
        string? outputPath = null;
        bool force = false;
        bool verify = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--direction":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --direction requires a value (to-binary or to-sqlite).");
                        Console.Error.WriteLine(Usage);
                        return 1;
                    }
                    direction = args[++i];
                    if (direction != "to-binary" && direction != "to-sqlite")
                    {
                        Console.Error.WriteLine($"Error: Invalid direction '{direction}'. Must be 'to-binary' or 'to-sqlite'.");
                        Console.Error.WriteLine(Usage);
                        return 1;
                    }
                    break;

                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --output requires a file path.");
                        Console.Error.WriteLine(Usage);
                        return 1;
                    }
                    outputPath = args[++i];
                    break;

                case "--force":
                    force = true;
                    break;

                case "--verify":
                    verify = true;
                    break;

                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Error: Unknown option '{args[i]}'.");
                        Console.Error.WriteLine(Usage);
                        return 1;
                    }
                    if (sourcePath != null)
                    {
                        Console.Error.WriteLine($"Error: Unexpected argument '{args[i]}'. Only one source file is allowed.");
                        Console.Error.WriteLine(Usage);
                        return 1;
                    }
                    sourcePath = args[i];
                    break;
            }
        }

        // Validate source path was provided
        if (sourcePath == null)
        {
            Console.Error.WriteLine("Error: Source file path is required.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        // Validate source file exists
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Error: Source file not found: {sourcePath}");
            return 1;
        }

        // Detect format if direction not specified
        FormatDetector.IndexFormat detectedFormat;
        try
        {
            detectedFormat = FormatDetector.Detect(sourcePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to detect source format: {ex.Message}");
            return 1;
        }

        if (direction == null)
        {
            // Auto-detect direction from source format
            switch (detectedFormat)
            {
                case FormatDetector.IndexFormat.Sqlite:
                    direction = "to-binary";
                    Console.WriteLine("Auto-detected source format: SQLite. Converting to binary.");
                    break;
                case FormatDetector.IndexFormat.Binary:
                case FormatDetector.IndexFormat.EmbeddedBinary:
                    direction = "to-sqlite";
                    Console.WriteLine($"Auto-detected source format: {detectedFormat}. Converting to SQLite.");
                    break;
                case FormatDetector.IndexFormat.Unknown:
                    Console.Error.WriteLine($"Error: File is not a valid SQLite or NKDS binary index: {sourcePath}");
                    return 1;
                default:
                    Console.Error.WriteLine($"Error: File is not a valid SQLite or NKDS binary index: {sourcePath}");
                    return 1;
            }
        }
        else
        {
            // Validate that the specified direction makes sense with the detected format
            if (direction == "to-binary" && detectedFormat != FormatDetector.IndexFormat.Sqlite)
            {
                if (detectedFormat == FormatDetector.IndexFormat.Unknown)
                {
                    Console.Error.WriteLine($"Error: File is not a valid SQLite or NKDS binary index: {sourcePath}");
                    return 1;
                }
            }
            else if (direction == "to-sqlite" && detectedFormat == FormatDetector.IndexFormat.Unknown)
            {
                Console.Error.WriteLine($"Error: File is not a valid SQLite or NKDS binary index: {sourcePath}");
                return 1;
            }
        }

        // Determine output path if not specified
        if (outputPath == null)
        {
            string sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? ".";
            string setName = Path.GetFileNameWithoutExtension(sourcePath);

            // Strip .sqlite suffix if present (e.g., "mydata.sqlite.nkds" → setName = "mydata.sqlite" → "mydata")
            if (setName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
                setName = setName[..^".sqlite".Length];

            if (direction == "to-binary")
            {
                // SQLite → Binary: output is {setname}.binary.nkds (avoids collision with source .nkds)
                outputPath = Path.Combine(sourceDir, $"{setName}.binary.nkds");
            }
            else
            {
                // Binary → SQLite: output is {setname}.sqlite.nkds
                outputPath = Path.Combine(sourceDir, $"{setName}.sqlite.nkds");
            }

            // If the computed output path is the same as the source, add a suffix to avoid collision
            if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(outputPath);
                string nameWithoutExt = outputPath[..^ext.Length];
                outputPath = $"{nameWithoutExt}.converted{ext}";
            }
        }

        // Check if output file already exists
        if (File.Exists(outputPath) && !force)
        {
            Console.Error.WriteLine($"Error: Output file already exists: {outputPath}. Use --force to overwrite.");
            return 1;
        }

        // Run conversion
        Console.WriteLine($"Converting: {sourcePath}");
        Console.WriteLine($"Direction:  {direction}");
        Console.WriteLine($"Output:     {outputPath}");
        Console.WriteLine();

        Progress<string> progress = new Progress<string>(message => Console.WriteLine($"  {message}"));

        try
        {
            ConversionResult result;

            if (direction == "to-binary")
            {
                SqliteToBinaryConverter converter = new SqliteToBinaryConverter();
                result = converter.Convert(sourcePath, outputPath, progress);
            }
            else
            {
                BinaryToSqliteConverter converter = new BinaryToSqliteConverter();
                result = converter.Convert(sourcePath, outputPath, progress);
            }

            Console.WriteLine();
            Console.WriteLine($"Conversion complete in {result.Duration.TotalSeconds:F1}s:");
            Console.WriteLine($"  Images:  {result.ImageCount}");
            Console.WriteLine($"  Areas:   {result.AreaCount}");
            Console.WriteLine($"  Offsets: {result.OffsetCount}");
            Console.WriteLine($"  Blocks:  {result.BlockCount}");
            Console.WriteLine($"  Files:   {result.FileCount}");

            if (verify)
            {
                Console.WriteLine();
                Console.WriteLine("Verifying output...");

                ConversionVerifier verifier = new ConversionVerifier();
                List<ConversionVerifier.Mismatch> mismatches = verifier.Verify(sourcePath, outputPath, direction);

                if (mismatches.Count == 0)
                {
                    Console.WriteLine("  Verification passed: all records match.");
                }
                else
                {
                    Console.Error.WriteLine($"  Verification failed: {mismatches.Count} mismatch(es) found:");
                    foreach (ConversionVerifier.Mismatch? m in mismatches.Take(20))
                    {
                        Console.Error.WriteLine($"    [{m.Table}] {m.RecordId} — {m.Field}: expected={m.Expected}, actual={m.Actual}");
                    }
                    if (mismatches.Count > 20)
                    {
                        Console.Error.WriteLine($"    ... and {mismatches.Count - 20} more.");
                    }
                    return 2;
                }
            }

            return 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // SQLITE_BUSY
        {
            Console.Error.WriteLine("Error: Source database is locked by another process.");
            return 1;
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine($"Error: SQLite error: {ex.Message}");
            return 1;
        }
        catch (InvalidDataException ex)
        {
            // Corrupted binary header or invalid data format
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (NotSupportedException ex)
        {
            // Unsupported binary format version
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            // Record parse failure, validation mismatch, or conversion logic error
            Console.Error.WriteLine($"Error: Conversion failed: {ex.Message}");
            return 2;
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            Console.Error.WriteLine("Error: Disk full writing output.");
            return 3;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error: I/O error during conversion: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Conversion failed: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Determines whether an IOException represents a disk full condition.
    /// Checks for Windows HRESULT ERROR_DISK_FULL (0x70) and ERROR_HANDLE_DISK_FULL (0x27),
    /// as well as Unix ENOSPC (28).
    /// </summary>
    private static bool IsDiskFull(IOException ex)
    {
        // Windows: ERROR_DISK_FULL = 0x70 (112), ERROR_HANDLE_DISK_FULL = 0x27 (39)
        // The HResult for these is 0x80070070 and 0x80070027 respectively.
        const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);
        const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
        // Unix: ENOSPC = 28 → HResult 0x80131620 (COR_E_IO) but message contains "No space left"
        // Fallback: check the message for common disk full indicators

        int hr = ex.HResult;
        if (hr == HR_ERROR_DISK_FULL || hr == HR_ERROR_HANDLE_DISK_FULL)
            return true;

        // On Unix/macOS, disk full may not have a distinct HResult, so check message
        string msg = ex.Message;
        if (msg.Contains("No space left", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("not enough space", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("disk full", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
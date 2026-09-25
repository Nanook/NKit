namespace NKDS.Validation;

/// <summary>
/// Validation methods for NKDS set creation parameters.
/// Returns both a validity flag and an error message for UI display.
/// </summary>
public static class NkdsValidation
{
    // Set name constraints
    public const int SetNameMinLength = 1;
    public const int SetNameMaxLength = 128;

    // Shard size constraints
    public const long ShardSizeMin = 100L * 1024 * 1024;           // 100 MiB = 104_857_600
    public const long ShardSizeMax = 64L * 1024 * 1024 * 1024;     // 64 GiB = 68_719_476_736

    // Block size constraints
    public const int BlockSizeMin = 4 * 1024;                       // 4 KiB = 4096
    public const int BlockSizeMax = 1024 * 1024;                    // 1 MiB = 1_048_576

    private static readonly char[] _PathSeparators = ['/', '\\'];
    private static readonly char[] _ReservedFilenameChars = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// Validates a set name for use in a DataStore.
    /// A valid set name is 1–128 characters, contains no path separators (/ or \),
    /// no control characters (0x00–0x1F), no reserved filename characters (&lt; &gt; : " | ? *),
    /// and has no leading or trailing whitespace.
    /// </summary>
    public static (bool IsValid, string Error) ValidateSetName(string name)
    {
        if (name is null)
            return (false, "Set name cannot be null.");

        if (name.Length < SetNameMinLength)
            return (false, "Set name cannot be empty.");

        if (name.Length > SetNameMaxLength)
            return (false, $"Set name cannot exceed {SetNameMaxLength} characters (got {name.Length}).");

        if (name.Length != name.TrimStart().Length || name.Length != name.TrimEnd().Length)
            return (false, "Set name cannot have leading or trailing whitespace.");

        if (name.AsSpan().IndexOfAny(_PathSeparators) >= 0)
            return (false, "Set name cannot contain path separator characters (/ or \\).");

        foreach (char c in name)
        {
            if (c <= '\u001F')
                return (false, $"Set name cannot contain control characters (found U+{(int)c:X4}).");
        }

        if (name.AsSpan().IndexOfAny(_ReservedFilenameChars) >= 0)
            return (false, "Set name cannot contain reserved filename characters (< > : \" | ? *).");

        return (true, null);
    }

    /// <summary>
    /// Validates a shard size value.
    /// A valid shard size is in the range [100 MiB, 64 GiB] inclusive.
    /// </summary>
    public static (bool IsValid, string Error) ValidateShardSize(long size)
    {
        if (size == 0)
            return (true, null); // 0 = embedded/single-file mode

        if (size < ShardSizeMin)
            return (false, $"Shard size must be at least 100 MiB ({ShardSizeMin:N0} bytes). Got {size:N0} bytes.");

        if (size > ShardSizeMax)
            return (false, $"Shard size must be at most 64 GiB ({ShardSizeMax:N0} bytes). Got {size:N0} bytes.");

        return (true, null);
    }

    /// <summary>
    /// Validates a block size value.
    /// A valid block size is a power of 2 in the range [4 KiB, 1 MiB] inclusive.
    /// </summary>
    public static (bool IsValid, string Error) ValidateBlockSize(int size)
    {
        if (size < BlockSizeMin)
            return (false, $"Block size must be at least 4 KiB ({BlockSizeMin:N0} bytes). Got {size:N0} bytes.");

        if (size > BlockSizeMax)
            return (false, $"Block size must be at most 1 MiB ({BlockSizeMax:N0} bytes). Got {size:N0} bytes.");

        if (!isPowerOfTwo(size))
            return (false, $"Block size must be a power of 2. Got {size:N0} bytes.");

        return (true, null);
    }

    private static bool isPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
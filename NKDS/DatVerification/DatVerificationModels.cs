namespace NKDS.DatVerification;

/// <summary>
/// Classification status for a dat verification result.
/// </summary>
public enum DatVerificationStatus
{
    /// <summary>Image CRC matches dat entry CRC and name matches.</summary>
    Correct,

    /// <summary>Dat entry has no corresponding image with matching CRC or name.</summary>
    Missing,

    /// <summary>Image CRC matches dat entry CRC but name does not match.</summary>
    BadlyNamed,

    /// <summary>Image name matches dat entry name but CRC does not match.</summary>
    WrongCrc,

    /// <summary>Image has no CRC match and no name match against any dat entry.</summary>
    Unmatched
}

/// <summary>
/// A single verification result row representing the comparison outcome
/// for an image or dat entry.
/// </summary>
public sealed class DatResultModel
{
    /// <summary>Status classification for this result.</summary>
    public DatVerificationStatus Status { get; init; }

    /// <summary>The dat entry name (null for Unmatched images).</summary>
    public string DatEntryName { get; init; }

    /// <summary>The image name (null for Missing dat entries).</summary>
    public string ImageName { get; init; }

    /// <summary>
    /// CRC32 displayed: for Missing entries this is the expected dat CRC;
    /// for all other statuses this is the image CRC.
    /// </summary>
    public uint Crc32 { get; init; }

    /// <summary>The image ID (for rename operations). Null for Missing entries.</summary>
    public long? ImageId { get; init; }

    /// <summary>The set name the image belongs to. Null for Missing entries.</summary>
    public string SetName { get; init; }
}

/// <summary>
/// Result of loading a dat file.
/// </summary>
public sealed class DatLoadResult
{
    /// <summary>Whether the dat file was successfully parsed.</summary>
    public bool Success { get; init; }

    /// <summary>The dat name from the dat file header.</summary>
    public string DatName { get; init; }

    /// <summary>Error message when loading fails.</summary>
    public string ErrorMessage { get; init; }

    /// <summary>Parsed dat entries with combined CRCs. Null on failure.</summary>
    public IReadOnlyList<DatEntryInfo> Entries { get; init; }
}

/// <summary>
/// Flattened dat entry info for verification, with pre-computed combined CRC.
/// </summary>
public sealed class DatEntryInfo
{
    /// <summary>The full filename from the dat entry.</summary>
    public string Name { get; init; } = "";

    /// <summary>The filename without extension, used for matching.</summary>
    public string NameStem { get; init; } = "";

    /// <summary>Combined CRC32 computed from all bin parts using the NKit CRC rule.</summary>
    public uint CombinedCrc32 { get; init; }
}

/// <summary>
/// Flattened image info for verification.
/// </summary>
public sealed class ImageInfo
{
    /// <summary>The image record ID.</summary>
    public long Id { get; init; }

    /// <summary>The full image filename.</summary>
    public string Name { get; init; } = "";

    /// <summary>The filename without extension, used for matching.</summary>
    public string NameStem { get; init; } = "";

    /// <summary>The image CRC32 checksum.</summary>
    public uint Crc32 { get; init; }

    /// <summary>The set name the image belongs to.</summary>
    public string SetName { get; init; } = "";
}
namespace NKDS.Converter.Conversion;

/// <summary>
/// Statistics from a completed conversion.
/// </summary>
public record ConversionResult(
    int ImageCount,
    long AreaCount,
    long OffsetCount,
    long BlockCount,
    long FileCount,
    TimeSpan Duration,
    bool Verified = false);
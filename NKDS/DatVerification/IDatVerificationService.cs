using Nanook.NKit.Dats;
using NKDS.DatVerification;

namespace NKDS.DatVerification;

/// <summary>
/// Provides dat file loading, CRC combination, and verification of images against dat entries.
/// </summary>
public interface IDatVerificationService
{
    /// <summary>
    /// Loads and parses a Logiqx-format dat file.
    /// </summary>
    /// <param name="filePath">Path to the dat file to load.</param>
    /// <returns>A result containing parsed entries on success, or an error message on failure.</returns>
    DatLoadResult LoadDat(string filePath);

    /// <summary>
    /// Computes the combined CRC32 for a dat entry from its bin parts
    /// using the NKit CRC combination rule.
    /// </summary>
    /// <param name="datItem">The dat item whose bin parts to combine.</param>
    /// <returns>The combined CRC32 value.</returns>
    uint ComputeCombinedCrc(DatItem datItem);

    /// <summary>
    /// Performs verification of images against dat entries, classifying each
    /// image and dat entry into a status category.
    /// </summary>
    /// <param name="datEntries">The dat entries to verify against.</param>
    /// <param name="images">The images to verify.</param>
    /// <returns>A list of verification results with status classifications.</returns>
    IReadOnlyList<DatResultModel> Verify(
        IReadOnlyList<DatEntryInfo> datEntries,
        IReadOnlyList<ImageInfo> images);
}
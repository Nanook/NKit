using Nanook.NKit;

namespace NKDS;

/// <summary>
/// Computes the disambiguated display name for a SourceFile.
/// Centralises the naming logic so that PreScanAsync and AddPreScannedAsync
/// produce identical names for the same input.
/// </summary>
public static class DisambiguatedNameHelper
{
    /// <summary>
    /// Returns the disambiguated name for the given <paramref name="sourceFile"/>.
    /// When the SourceFile has a non-null IndexFile with FileType == TmdApp,
    /// the name is formatted as "{SourceFile.Name} [{IndexFile.NameOnly}]".
    /// Otherwise, SourceFile.Name is returned as-is.
    /// </summary>
    public static string Compute(SourceFile sourceFile)
    {
        string name = sourceFile.Name ?? "Unknown";

        string indexFileName = sourceFile.IndexFile?.NameOnly;
        if (!string.IsNullOrEmpty(indexFileName) && sourceFile.IndexFile?.FileType == IndexFileType.TmdApp)
            name = $"{name} [{indexFileName}]";

        return name;
    }
}
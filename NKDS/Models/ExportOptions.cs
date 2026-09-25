namespace NKDS.Models;

/// <summary>
/// Options for exporting images from a DataStore set.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>
    /// Path to the DataStore directory.
    /// </summary>
    public string DataStorePath { get; init; } = "";

    /// <summary>
    /// Name of the source set within the DataStore.
    /// </summary>
    public string SetName { get; init; } = "";

    /// <summary>
    /// List of image IDs to export.
    /// </summary>
    public IReadOnlyList<long> ImageIds { get; init; } = [];

    /// <summary>
    /// Directory path where exported files will be written.
    /// </summary>
    public string OutputDirectory { get; init; } = "";

    /// <summary>
    /// Target format for conversion. Null means stored format (full expand).
    /// </summary>
    public string ConvertFormat { get; init; }
}
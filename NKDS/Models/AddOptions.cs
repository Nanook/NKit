namespace NKDS.Models;

/// <summary>
/// Options for adding files to a DataStore set.
/// </summary>
public sealed class AddOptions
{
    /// <summary>
    /// Path to the DataStore directory.
    /// </summary>
    public string DataStorePath { get; init; } = "";

    /// <summary>
    /// Name of the target set within the DataStore.
    /// </summary>
    public string SetName { get; init; } = "";

    /// <summary>
    /// List of file paths to import into the set.
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; init; } = [];

    /// <summary>
    /// Optional path to a configuration file for the import operation.
    /// </summary>
    public string ConfigFile { get; init; }
}
namespace NKDS.Models;

/// <summary>
/// Represents an active virtual filesystem mount of a DataStore set.
/// </summary>
public sealed class ActiveMount
{
    /// <summary>
    /// The directory path where the set is mounted.
    /// </summary>
    public string MountPoint { get; init; } = "";

    /// <summary>
    /// The name of the mounted set.
    /// </summary>
    public string SetName { get; init; } = "";

    /// <summary>
    /// The path to the DataStore containing the mounted set.
    /// </summary>
    public string DataStorePath { get; init; } = "";
}
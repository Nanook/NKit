namespace NKDS.Models;

/// <summary>
/// Options for creating a new set within a DataStore.
/// </summary>
public sealed class CreateSetOptions
{
    /// <summary>
    /// Name of the set to create (1–128 characters, valid filename characters only).
    /// </summary>
    public string SetName { get; init; } = "";

    /// <summary>
    /// Maximum size of each shard file in bytes. Default is 4 GiB.
    /// Valid range: [100 MiB, 64 GiB].
    /// </summary>
    public long ShardSize { get; init; } = 4L * 1024 * 1024 * 1024;  // 4 GiB default

    /// <summary>
    /// Block size in bytes used for deduplication. Must be a power of 2.
    /// Default is 64 KiB. Valid range: [4 KiB, 1 MiB].
    /// </summary>
    public int BlockSize { get; init; } = 64 * 1024;  // 64 KiB default
}
namespace NKDS.Models;

/// <summary>
/// Holds resolved (absolute) key and fix file paths for passing to the NKit pipeline.
/// All paths should be resolved to absolute paths before constructing this record.
/// Null or empty paths are omitted from the generated CLI arguments.
/// </summary>
public sealed class ResolvedKeyFixPaths
{
    public string WiiUKeysPath { get; init; }
    public string Ps3KeysPath { get; init; }
    public string GameCubeFixInfoPath { get; init; }
    public string GameCubeFixFilesPath { get; init; }
    public string WiiFixInfoPath { get; init; }
    public string WiiFixFilesPath { get; init; }
    public string Ps3FixInfoPath { get; init; }
    public string Ps3FixFilesPath { get; init; }
    public string DreamcastFixInfoPath { get; init; }
}
namespace NkdsUi.Services;

/// <summary>
/// Holds all key and fix file path settings. Each property stores either a relative path
/// (when under App_Directory) or an absolute path.
/// </summary>
public sealed class KeysAndFixPathsConfig
{
    public string WiiUKeysPath { get; set; } = "keys/wiiu/*.zip";
    public string Ps3KeysPath { get; set; } = "keys/ps3/*.zip";
    public string GameCubeFixInfoPath { get; set; } = "fix/fix_gamecube.yaml";
    public string GameCubeFixFilesPath { get; set; } = "fix/gamecube";
    public string WiiFixInfoPath { get; set; } = "fix/fix_wii.yaml";
    public string WiiFixFilesPath { get; set; } = "fix/wii";
    public string Ps3FixInfoPath { get; set; } = "fix/fix_ps3.yaml";
    public string Ps3FixFilesPath { get; set; } = "fix/ps3";
    public string DreamcastFixInfoPath { get; set; } = "fix/fix_dreamcast.yaml";

    /// <summary>
    /// Creates a new instance with all paths set to their default relative values.
    /// </summary>
    public static KeysAndFixPathsConfig CreateDefault() => new();
}
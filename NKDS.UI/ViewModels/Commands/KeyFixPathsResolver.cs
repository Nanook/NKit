using NKDS.Models;
using NkdsUi.Services;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Resolves stored key/fix paths from ConfigService into absolute paths
/// suitable for passing to the NKit pipeline.
/// </summary>
internal static class KeyFixPathsResolver
{
    /// <summary>
    /// Reads the configured key/fix paths from the config service and resolves
    /// each one to an absolute path using PathResolver.
    /// </summary>
    public static ResolvedKeyFixPaths Resolve(IConfigService configService)
    {
        KeysAndFixPathsConfig config = configService.KeysAndFixPaths;
        return new ResolvedKeyFixPaths
        {
            WiiUKeysPath = PathResolver.ToAbsolute(config.WiiUKeysPath),
            Ps3KeysPath = PathResolver.ToAbsolute(config.Ps3KeysPath),
            GameCubeFixInfoPath = PathResolver.ToAbsolute(config.GameCubeFixInfoPath),
            GameCubeFixFilesPath = PathResolver.ToAbsolute(config.GameCubeFixFilesPath),
            WiiFixInfoPath = PathResolver.ToAbsolute(config.WiiFixInfoPath),
            WiiFixFilesPath = PathResolver.ToAbsolute(config.WiiFixFilesPath),
            Ps3FixInfoPath = PathResolver.ToAbsolute(config.Ps3FixInfoPath),
            Ps3FixFilesPath = PathResolver.ToAbsolute(config.Ps3FixFilesPath),
            DreamcastFixInfoPath = PathResolver.ToAbsolute(config.DreamcastFixInfoPath),
        };
    }
}
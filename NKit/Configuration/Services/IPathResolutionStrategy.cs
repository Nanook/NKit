using Nanook.NKit.Configuration.Models;

namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Strategy for resolving configuration paths based on mode (portable vs system)
    /// </summary>
    internal interface IPathResolutionStrategy
    {
        ConfigurationContext ResolveConfiguration(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem);
    }
}

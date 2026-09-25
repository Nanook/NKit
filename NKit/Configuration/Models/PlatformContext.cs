using System;

namespace Nanook.NKit.Configuration.Models
{
    /// <summary>
    /// Platform context information for configuration resolution
    /// </summary>
    internal class PlatformContext
    {
        public PlatformType Platform { get; }
        public string ExecutableDirectory { get; }
        public bool IsBundle { get; }
        public string AppType { get; }

        public PlatformContext(PlatformType platform, string executableDirectory, bool isBundle, string appType)
        {
            Platform = platform;
            ExecutableDirectory = executableDirectory ?? throw new ArgumentNullException(nameof(executableDirectory));
            IsBundle = isBundle;
            AppType = appType ?? throw new ArgumentNullException(nameof(appType));
        }
    }

    /// <summary>
    /// Platform types supported by NKit
    /// </summary>
    internal enum PlatformType
    {
        Windows,
        Linux,
        MacOS
    }
}

using Nanook.NKit.Vfs;

namespace NKDS.Mount;

/// <summary>
/// Default implementation of <see cref="IPlatformFsHostFactory"/> that uses
/// RuntimeInformation.IsOSPlatform to select the correct host type at runtime.
/// The actual host creation is delegated to a registered factory function,
/// allowing the platform-specific assemblies (Dokan/FUSE) to remain in their
/// respective projects while the UI project uses this abstraction.
/// </summary>
public sealed class PlatformFsHostFactory : IPlatformFsHostFactory
{
    private readonly Func<IPlatformFsHost> _creator;
    private readonly bool _isSupported;
    private readonly string _unsupportedReason;

    /// <summary>
    /// Creates a factory with an explicit host creator function.
    /// </summary>
    /// <param name="creator">Function that creates a new platform host instance.</param>
    public PlatformFsHostFactory(Func<IPlatformFsHost> creator)
    {
        _creator = creator ?? throw new ArgumentNullException(nameof(creator));
        _isSupported = true;
        _unsupportedReason = null;
    }

    /// <summary>
    /// Creates a factory that reports the platform as unsupported.
    /// Used when no platform host implementation is available.
    /// </summary>
    /// <param name="unsupportedReason">Reason the platform is not supported.</param>
    public PlatformFsHostFactory(string unsupportedReason)
    {
        _creator = null;
        _isSupported = false;
        _unsupportedReason = unsupportedReason;
    }

    /// <inheritdoc />
    public bool IsSupported => _isSupported;

    /// <inheritdoc />
    public string UnsupportedReason => _unsupportedReason;

    /// <inheritdoc />
    public IPlatformFsHost Create()
    {
        if (!_isSupported || _creator == null)
            throw new PlatformNotSupportedException(
                _unsupportedReason ?? "No platform filesystem host is available.");

        return _creator();
    }

    /// <summary>
    /// Creates a factory for the current platform using conditional compilation.
    /// Platform host types are selected at compile time via WINDOWS/LINUX defines,
    /// which are set based on the RuntimeIdentifier in the csproj.
    /// </summary>
    public static IPlatformFsHostFactory CreateForCurrentPlatform()
    {
#if WINDOWS
        return new PlatformFsHostFactory(() => new WindowsPlatformFsHost());
#elif LINUX
        // Check FUSE 3 availability now (libfuse3.so.3 / libfuse3.so) so an unsupported
        // factory is returned immediately with a useful install hint, matching the macOS behaviour.
        // LibFuseLoader already tries both the SONAME and the unversioned fallback.
        if (Tmds.Fuse.Fuse.CheckDependencies())
            return new PlatformFsHostFactory(() => new LinuxPlatformFsHost());
        else
            return new PlatformFsHostFactory(Tmds.Fuse.Fuse.InstallationInstructions);
#elif MACOS
        if (Tmds.Fuse.Fuse.CheckDependencies())
            return new PlatformFsHostFactory(() => new MacOsPlatformFsHost());
        else
            return new PlatformFsHostFactory(Tmds.Fuse.Fuse.InstallationInstructions);
#else
        return new PlatformFsHostFactory(
            $"Mounting is not supported on {RuntimeInformation.OSDescription}.");
#endif
    }
}

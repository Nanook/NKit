namespace NKDS.Mount;

/// <summary>
/// Abstracts platform host creation so the UI project doesn't need compile-time
/// platform conditionals. Uses RuntimeInformation.IsOSPlatform at runtime to
/// select the correct host type.
/// </summary>
public interface IPlatformFsHostFactory
{
    /// <summary>
    /// Creates the platform-specific filesystem host for the current OS.
    /// </summary>
    /// <returns>A new <see cref="IPlatformFsHost"/> instance.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the current platform is not supported for mounting.
    /// </exception>
    IPlatformFsHost Create();

    /// <summary>
    /// Whether the current platform supports filesystem host creation.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// If <see cref="IsSupported"/> is false, provides a human-readable reason.
    /// </summary>
    string UnsupportedReason { get; }
}
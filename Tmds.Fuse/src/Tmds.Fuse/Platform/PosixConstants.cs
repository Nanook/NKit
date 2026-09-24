namespace Tmds.Fuse;

/// <summary>
/// POSIX constants shared between Linux and macOS.
/// Values are identical on both platforms for all listed codes except ENOSYS and ENOTEMPTY.
/// </summary>
public static class Posix
{
    // errno values (shared)
    public const int EPERM = 1;
    public const int ENOENT = 2;
    public const int EIO = 5;
    public const int EBADF = 9;
    public const int ENOMEM = 12;
    public const int EACCES = 13;
    public const int EEXIST = 17;
    public const int ENOTDIR = 20;
    public const int EISDIR = 21;
    public const int EINVAL = 22;

    // Platform-conditional errno values
#if MACOS
    public const int ENOSYS = 78;
    public const int ENOTEMPTY = 66;
#else
    public const int ENOSYS = 38;
    public const int ENOTEMPTY = 39;
#endif

    // File type mode flags
    public const uint S_IFMT = 0xF000;
    public const uint S_IFDIR = 0x4000;
    public const uint S_IFREG = 0x8000;
    public const uint S_IFLNK = 0xA000;

    // Open flags
    public const int O_RDONLY = 0;
    public const int O_WRONLY = 1;
    public const int O_RDWR = 2;
    public const int O_ACCMODE = 3;

    // Timespec special values
    public const long UTIME_NOW = (1L << 30) - 1;
    public const long UTIME_OMIT = (1L << 30) - 2;
}

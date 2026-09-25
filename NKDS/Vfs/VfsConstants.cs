namespace Nanook.NKit.Vfs;

public static class VfsConstants
{
    // System type names
    public const string SystemDirectories = "Directories";
    public const string SystemWiiU = "WiiU";

    // Mount root folder names
    public const string RootImages = "Images";
    public const string RootFilesystems = "Filesystems";

    // Numeric defaults
    public const int DefaultNkfsCacheCapacity = 64;
    public const int DefaultImageReaderCacheCapacity = 16;
    public const int MountWatchdogIntervalMs = 1000;
    public const int UnmountTimeoutMs = 5000;
    public const int PreWarmTimeoutSeconds = 30;
    public const int PreLoadNkfsTimeoutSeconds = 10;
    public const int MaxPreWarmDatabases = 500;

    // Separator characters
    public const char WindowsSeparator = '\\';
    public const char LinuxSeparator = '/';
}
#if !LINUX && !MACOS
using System.Runtime.InteropServices;
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters
#pragma warning disable IDE1006 // Naming Styles

namespace Tmds.Fuse;

/// <summary>
/// Stub stat struct for unsupported platforms (Windows).
/// This allows the project to compile but FUSE operations are not functional.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct stat
{
    public uint st_mode;
    public uint st_uid;
    public uint st_gid;
    public long st_size;
    public timespec st_atim;
    public timespec st_mtim;
    public timespec st_ctim;
}

/// <summary>
/// Stub statvfs struct for unsupported platforms (Windows).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct statvfs
{
    public ulong f_bsize;
    public ulong f_frsize;
    public ulong f_blocks;
    public ulong f_bfree;
    public ulong f_bavail;
    public ulong f_files;
    public ulong f_ffree;
    public ulong f_favail;
    public ulong f_fsid;
    public ulong f_flag;
    public ulong f_namemax;
}

#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters
#endif

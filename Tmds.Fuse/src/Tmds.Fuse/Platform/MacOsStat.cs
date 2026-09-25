#if MACOS
using System.Runtime.InteropServices;
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters
#pragma warning disable IDE1006 // Naming Styles

namespace Tmds.Fuse;

/// <summary>
/// macOS (Darwin) struct stat. 144 bytes on both x86_64 and arm64.
/// Field order matches /usr/include/sys/stat.h ($INODE64 variant).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct stat
{
    public int st_dev;
    public ushort st_mode;
    public ushort st_nlink;
    public ulong st_ino;
    public uint st_uid;
    public uint st_gid;
    public int st_rdev;
    public timespec st_atim;
    public timespec st_mtim;
    public timespec st_ctim;
    public timespec st_birthtim;
    public long st_size;
    public long st_blocks;
    public int st_blksize;
    public uint st_flags;
    public uint st_gen;
    private int __spare0;
    private long __spare1;
    private long __spare2;
}

/// <summary>macOS struct statvfs (from sys/statvfs.h).</summary>
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

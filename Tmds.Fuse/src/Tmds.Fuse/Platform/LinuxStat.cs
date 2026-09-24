#if LINUX
using System.Runtime.InteropServices;
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters
#pragma warning disable IDE1006 // Naming Styles

namespace Tmds.Fuse;

/// <summary>
/// Linux x86_64/arm64 struct stat (glibc). 144 bytes.
/// Field order matches /usr/include/bits/struct_stat.h.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct stat
{
    public ulong st_dev;
    public ulong st_ino;
    public ulong st_nlink;
    public mode_t st_mode;
    public uint st_uid;
    public uint st_gid;
    private uint __pad0;
    public ulong st_rdev;
    public long st_size;
    public long st_blksize;
    public long st_blocks;
    public timespec st_atim;
    public timespec st_mtim;
    public timespec st_ctim;
    private long __reserved0;
    private long __reserved1;
    private long __reserved2;
}

/// <summary>Linux struct statvfs (glibc x86_64/arm64).</summary>
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
    private uint __f_spare0;
    private uint __f_spare1;
    private uint __f_spare2;
    private uint __f_spare3;
    private uint __f_spare4;
    private uint __f_spare5;
}

#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters
#endif

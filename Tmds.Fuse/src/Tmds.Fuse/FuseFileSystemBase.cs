using System;

namespace Tmds.Fuse
{
    public class FuseFileSystemBase : IFuseFileSystem
    {
        private static byte[] _rootPath = new byte[] { (byte)'/' };
        public static ReadOnlySpan<byte> RootPath => _rootPath;

        public virtual bool SupportsMultiThreading => false;

        public virtual int Access(ReadOnlySpan<byte> path, mode_t mode)
            => -Posix.ENOSYS;

        public virtual int ChMod(ReadOnlySpan<byte> path, mode_t mode, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public virtual int Chown(ReadOnlySpan<byte> path, uint uid, uint gid, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public virtual int Create(ReadOnlySpan<byte> path, mode_t mode, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual void Dispose()
        { }

        public virtual int FAllocate(ReadOnlySpan<byte> path, int mode, ulong offset, long length, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int Flush(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int FSync(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int FSyncDir(ReadOnlySpan<byte> readOnlySpan, bool onlyData, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int GetAttr(ReadOnlySpan<byte> path, ref stat stat, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public virtual int GetXAttr(ReadOnlySpan<byte> path, ReadOnlySpan<byte> name, Span<byte> data)
            => -Posix.ENOSYS;

        public virtual int Link(ReadOnlySpan<byte> fromPath, ReadOnlySpan<byte> toPath)
            => -Posix.ENOSYS;

        public virtual int ListXAttr(ReadOnlySpan<byte> path, Span<byte> list)
            => -Posix.ENOSYS;

        public virtual int MkDir(ReadOnlySpan<byte> path, mode_t mode)
            => -Posix.ENOSYS;

        public virtual int Open(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int OpenDir(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => 0;

        public virtual int Read(ReadOnlySpan<byte> path, ulong offset, Span<byte> buffer, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int ReadDir(ReadOnlySpan<byte> path, ulong offset, ReadDirFlags flags, DirectoryContent content, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int ReadLink(ReadOnlySpan<byte> path, Span<byte> buffer)
            => -Posix.ENOSYS;

        public virtual void Release(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
        { }

        public virtual int ReleaseDir(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public virtual int RemoveXAttr(ReadOnlySpan<byte> path, ReadOnlySpan<byte> name)
            => -Posix.ENOSYS;
        public virtual int Rename(ReadOnlySpan<byte> path, ReadOnlySpan<byte> newPath, int flags)
            => -Posix.ENOSYS;

        public virtual int RmDir(ReadOnlySpan<byte> path)
            => -Posix.ENOSYS;

        public virtual int SetXAttr(ReadOnlySpan<byte> path, ReadOnlySpan<byte> name, ReadOnlySpan<byte> data, int flags)
            => -Posix.ENOSYS;

        public virtual int StatFS(ReadOnlySpan<byte> path, ref statvfs statfs)
            => -Posix.ENOSYS;

        public virtual int SymLink(ReadOnlySpan<byte> path, ReadOnlySpan<byte> target)
            => -Posix.ENOSYS;

        public virtual int Truncate(ReadOnlySpan<byte> path, ulong length, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public virtual int Unlink(ReadOnlySpan<byte> path)
            => -Posix.ENOSYS;

        public virtual int UpdateTimestamps(ReadOnlySpan<byte> path, ref timespec atime, ref timespec mtime, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public virtual int Write(ReadOnlySpan<byte> path, ulong off, ReadOnlySpan<byte> span, ref FuseFileInfo fi)
            => -Posix.ENOSYS;
    }
}
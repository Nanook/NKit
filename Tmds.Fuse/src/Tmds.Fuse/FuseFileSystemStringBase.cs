using System;
using System.Text;

#pragma warning disable IDE1006 // Naming Styles
namespace Tmds.Fuse
{
    public class FuseFileSystemStringBase : FuseFileSystemBase
    {
#pragma warning disable CS0108 // Member hides inherited member; missing new keyword
        public static string RootPath => "/";
#pragma warning restore CS0108 // Member hides inherited member; missing new keyword

        private readonly Encoding _pathEncoding;

        public FuseFileSystemStringBase(Encoding encoding)
        {
            _pathEncoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        }

        private unsafe string ToString(ReadOnlySpan<byte> span)
        {
            fixed (byte* ptr = span)
            {
                return _pathEncoding.GetString(ptr, span.Length);
            }
        }


        public override int Access(ReadOnlySpan<byte> path, mode_t mode)
            => Access(ToString(path), mode);

        public virtual int Access(string path, mode_t mode)
            => -Posix.ENOSYS;

        public override int ChMod(ReadOnlySpan<byte> path, mode_t mode, FuseFileInfoRef fiRef)
            => ChMod(ToString(path), mode, fiRef);

        public virtual int ChMod(string path, mode_t mode, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public override int Chown(ReadOnlySpan<byte> path, uint uid, uint gid, FuseFileInfoRef fiRef)
            => Chown(ToString(path), uid, gid, fiRef);

        public virtual int Chown(string path, uint uid, uint gid, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public override int Create(ReadOnlySpan<byte> path, mode_t mode, ref FuseFileInfo fi)
            => Create(ToString(path), mode, ref fi);
        public virtual int Create(string path, mode_t mode, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int FAllocate(ReadOnlySpan<byte> path, int mode, ulong offset, long length, ref FuseFileInfo fi)
            => FAllocate(ToString(path), mode, offset, length, ref fi);
        public virtual int FAllocate(string path, int mode, ulong offset, long length, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int Flush(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => Flush(ToString(path), ref fi);
        public virtual int Flush(string path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int FSync(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => FSync(ToString(path), ref fi);
        public virtual int FSync(string path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int GetAttr(ReadOnlySpan<byte> path, ref stat stat, FuseFileInfoRef fiRef)
            => GetAttr(ToString(path), ref stat, fiRef);
        public virtual int GetAttr(string path, ref stat stat, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public override int GetXAttr(ReadOnlySpan<byte> path, ReadOnlySpan<byte> name, Span<byte> data)
            => GetXAttr(ToString(path), ToString(name), data);
        public virtual int GetXAttr(string path, string name, Span<byte> data)
            => -Posix.ENOSYS;

        public override int Link(ReadOnlySpan<byte> fromPath, ReadOnlySpan<byte> toPath)
            => Link(ToString(fromPath), ToString(toPath));
        public virtual int Link(string fromPath, string toPath)
            => -Posix.ENOSYS;

        public override int ListXAttr(ReadOnlySpan<byte> path, Span<byte> list)
            => ListXAttr(ToString(path), list);
        public virtual int ListXAttr(string path, Span<byte> list)
            => -Posix.ENOSYS;

        public override int MkDir(ReadOnlySpan<byte> path, mode_t mode)
            => MkDir(ToString(path), mode);
        public virtual int MkDir(string path, mode_t mode)
            => -Posix.ENOSYS;

        public override int Open(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => Open(ToString(path), ref fi);
        public virtual int Open(string path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int OpenDir(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => OpenDir(ToString(path), ref fi);
        public virtual int OpenDir(string path, ref FuseFileInfo fi)
            => 0;

        public override int Read(ReadOnlySpan<byte> path, ulong offset, Span<byte> buffer, ref FuseFileInfo fi)
            => Read(ToString(path), offset, buffer, ref fi);
        public virtual int Read(string path, ulong offset, Span<byte> buffer, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int ReadDir(ReadOnlySpan<byte> path, ulong offset, ReadDirFlags flags, DirectoryContent content, ref FuseFileInfo fi)
            => ReadDir(ToString(path), offset, flags, content, ref fi);
        public virtual int ReadDir(string path, ulong offset, ReadDirFlags flags, DirectoryContent content, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int ReadLink(ReadOnlySpan<byte> path, Span<byte> buffer)
            => ReadLink(ToString(path), buffer);
        public virtual int ReadLink(string path, Span<byte> buffer)
            => -Posix.ENOSYS;

        public override void Release(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => Release(ToString(path), ref fi);
        public virtual void Release(string path, ref FuseFileInfo fi)
        { }

        public override int ReleaseDir(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
            => ReleaseDir(ToString(path), ref fi);
        public virtual int ReleaseDir(string path, ref FuseFileInfo fi)
            => -Posix.ENOSYS;

        public override int RemoveXAttr(ReadOnlySpan<byte> path, ReadOnlySpan<byte> name)
            => RemoveXAttr(ToString(path), ToString(name));
        public virtual int RemoveXAttr(string path, string name)
            => -Posix.ENOSYS;

        public override int Rename(ReadOnlySpan<byte> path, ReadOnlySpan<byte> newPath, int flags)
            => Rename(ToString(path), ToString(newPath), flags);
        public virtual int Rename(string path, string newPath, int flags)
            => -Posix.ENOSYS;

        public override int RmDir(ReadOnlySpan<byte> path)
            => RmDir(ToString(path));
        public virtual int RmDir(string path)
            => -Posix.ENOSYS;

        public override int SetXAttr(ReadOnlySpan<byte> path, ReadOnlySpan<byte> name, ReadOnlySpan<byte> data, int flags)
            => SetXAttr(ToString(path), ToString(name), data, flags);
        public virtual int SetXAttr(string path, string name, ReadOnlySpan<byte> data, int flags)
            => -Posix.ENOSYS;

        public override int StatFS(ReadOnlySpan<byte> path, ref statvfs statfs)
            => StatFS(ToString(path), ref statfs);
        public virtual int StatFS(string path, ref statvfs statfs)
            => -Posix.ENOSYS;

        public override int SymLink(ReadOnlySpan<byte> path, ReadOnlySpan<byte> target)
            => SymLink(ToString(path), ToString(target));
        public virtual int SymLink(string path, string target)
            => -Posix.ENOSYS;

        public override int Truncate(ReadOnlySpan<byte> path, ulong length, FuseFileInfoRef fiRef)
            => Truncate(ToString(path), length, fiRef);
        public virtual int Truncate(string path, ulong length, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public override int Unlink(ReadOnlySpan<byte> path)
            => Unlink(ToString(path));
        public virtual int Unlink(string path)
            => -Posix.ENOSYS;

        public override int UpdateTimestamps(ReadOnlySpan<byte> path, ref timespec atime, ref timespec mtime, FuseFileInfoRef fiRef)
            => UpdateTimestamps(ToString(path), ref atime, ref mtime, fiRef);
        public virtual int UpdateTimestamps(string path, ref timespec atime, ref timespec mtime, FuseFileInfoRef fiRef)
            => -Posix.ENOSYS;

        public override int Write(ReadOnlySpan<byte> path, ulong off, ReadOnlySpan<byte> span, ref FuseFileInfo fi)
            => Write(ToString(path), off, span, ref fi);
        public virtual int Write(string path, ulong off, ReadOnlySpan<byte> span, ref FuseFileInfo fi)
            => -Posix.ENOSYS;
    }
}
#pragma warning restore IDE1006 // Naming Styles

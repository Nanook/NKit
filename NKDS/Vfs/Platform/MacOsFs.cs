#if MACOS
using Tmds.Fuse;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// FUSE-T-based filesystem implementation for macOS.
    /// Provides read-only access to NKit DataStore images as virtual ISO files.
    /// </summary>
    internal class MacOsFs : FuseFileSystemBase, IVfsOperations
    {
        private readonly VfsModel _model;
        private readonly Dictionary<ulong, VfsContext> _openFiles = new Dictionary<ulong, VfsContext>();
        private ulong _nextFileHandle = 1;
        private readonly object _handleLock = new object();
        private uint? _overrideUid;
        private uint? _overrideGid;
        private readonly char _separator = '/';
        private IFuseMount _fuseMount;
        private readonly string _setName;

        [DllImport("libSystem.B.dylib", EntryPoint = "getuid")]
        private static extern uint getuid();

        [DllImport("libSystem.B.dylib", EntryPoint = "getgid")]
        private static extern uint getgid();

        public MacOsFs(string dataStorePath, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, int maxFileSystemYamlSizeKiB = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB)
        {
            _setName = setName;
            _model = new VfsModel(dataStorePath, _separator, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
        }

        // Implement the interface Mount(string) and provide an extended overload
        // that accepts FUSE-specific options.
        public void Mount(string mountPoint)
        {
            Mount(mountPoint, false, null, null);
        }

        public void Mount(string mountPoint, bool allowOther = false, uint? uid = null, uint? gid = null)
        {
            if (!Fuse.CheckDependencies())
                throw new FuseException($"FUSE-T dependencies not available.\n{Fuse.InstallationInstructions}");

            _overrideUid = uid;
            _overrideGid = gid;

            // Clear any stale FUSE-T mount left from an abnormal previous exit
            try { Fuse.LazyUnmount(mountPoint); } catch { }

            var options = new MountOptions
            {
                SingleThread = false,
                AllowOther = allowOther,
                Uid = uid,
                Gid = gid
            };

            // Add macOS-specific FUSE-T options
            if (!string.IsNullOrEmpty(_setName))
                options.AdditionalOptions.Add($"volname={_setName}");

            options.AdditionalOptions.Add("nobrowse");

            try
            {
                _fuseMount = Fuse.Mount(mountPoint, this, options);
                Trace.WriteLine("Filesystem mounted.");

                // Block until unmount — the caller (MountOrchestrator) expects Run() to block
                // until the filesystem is actually unmounted.
                _fuseMount.WaitForUnmountAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Mount] Error mounting filesystem: {ex.Message}");
                throw;
            }
        }

        public new void Dispose()
        {
            lock (_handleLock)
            {
                foreach (var ctx in _openFiles.Values)
                    try { VfsStreamHelper.CloseContext(ctx, _model.GetResourcesByIndex(ctx.DataStoreIndex)); } catch { }
                _openFiles.Clear();
            }
            // Mounted readers are owned by VfsModel; _model.Dispose() releases them.
            _fuseMount?.Dispose();
            _model?.Dispose();
            base.Dispose();
        }

        #region FUSE Operations

        public override int GetAttr(ReadOnlySpan<byte> path, ref stat stat, FuseFileInfoRef fiRef)
        {
            try
            {
                string pathStr = getPath(path);
                Debug.WriteLine($"[FUSE] GetAttr: {pathStr}");

                IFsItem item = _model.GetScanFsItem(pathStr, _separator);

                if (item == null)
                    return -Posix.ENOENT;

                int depth = pathStr.Split(new[] { _separator }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool isWritable = _model.UpdateMode && depth <= 2;

                if (item is IFsFolder)
                {
                    // macOS stat: st_mode is ushort, S_IFDIR is uint, permission bits are uint
                    stat.st_mode = (ushort)(Posix.S_IFDIR | (isWritable ? 511u : 365u)); // 511 is 0777, 365 is 0555
                    stat.st_nlink = 2;
                    stat.st_size = 0;
                }
                else if (item is IFsFile file)
                {
                    stat.st_mode = (ushort)(Posix.S_IFREG | (isWritable ? 438u : 292u)); // 438 is 0666, 292 is 0444
                    stat.st_nlink = 1;
                    stat.st_size = (long)file.FsSize;
                }

                stat.st_uid = _overrideUid ?? getuid();
                stat.st_gid = _overrideGid ?? getgid();

                // Set file date to Jan 1, 2026, 12:00 PM (Noon) UTC
                long fixedTime = 1767268800;
                stat.st_atim = new timespec { tv_sec = fixedTime };
                stat.st_mtim = new timespec { tv_sec = fixedTime };
                stat.st_ctim = new timespec { tv_sec = fixedTime };
                stat.st_birthtim = new timespec { tv_sec = fixedTime };

                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FUSE] GetAttr error: {ex.Message}");
                return -Posix.EIO;
            }
        }

        public override int ReadDir(ReadOnlySpan<byte> path, ulong offset, ReadDirFlags flags, DirectoryContent content, ref FuseFileInfo fi)
        {
            try
            {
                string pathStr = getPath(path);
                Debug.WriteLine($"[FUSE] ReadDir: {pathStr}");

                // Add standard entries
                content.AddEntry(".");
                content.AddEntry("..");

                // Get directory contents from model
                IEnumerable<IFsItem> items = _model.GetScanFsItems(pathStr, "*", _separator);

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        content.AddEntry(item.Name);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FUSE] ReadDir error: {ex.Message}");
                return -Posix.EIO;
            }
        }

        public override int Open(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
        {
            try
            {
                string pathStr = getPath(path);
                Debug.WriteLine($"[FUSE] Open: {pathStr}");

                // Check if file exists and get context
                VfsContext context = _model.GetScanFsItemAsContext(pathStr);

                if (context == null)
                    return -Posix.ENOENT;

                // Only allow read-only access
                if ((fi.flags & Posix.O_ACCMODE) != Posix.O_RDONLY)
                    return -Posix.EACCES;

                // Assign a file handle
                lock (_handleLock)
                {
                    ulong handle = _nextFileHandle++;
                    _openFiles[handle] = context;
                    fi.fh = handle;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FUSE] Open error: {ex.Message}");
                return -Posix.EIO;
            }
        }

        public override int Read(ReadOnlySpan<byte> path, ulong offset, Span<byte> buffer, ref FuseFileInfo fi)
        {
            try
            {
                VfsContext context;
                lock (_handleLock)
                {
                    if (!_openFiles.TryGetValue(fi.fh, out context))
                        return -Posix.EBADF;
                }

                lock (context.ReadLock)
                {
                    // Lazy-initialize the stream on first read
                    try
                    {
                        VfsStreamHelper.EnsureStream(_model, context, _model.GetResourcesByIndex(context.DataStoreIndex));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FUSE] Error opening stream: {ex.Message}");
                        return -Posix.EIO;
                    }

                    if (context.FileStream == null)
                        return -Posix.EBADF;

                    if ((long)offset >= context.FileStream.Length)
                        return 0;

                    // Read from the stream
                    context.FileStream.Position = (long)offset;

#if NETCOREAPP || NET5_0_OR_GREATER
                    int bytesRead = context.FileStream.Read(buffer);
#else
                    byte[] tempBuff = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
                    int bytesRead;
                    try
                    {
                        bytesRead = context.FileStream.Read(tempBuff, 0, buffer.Length);
                        new Span<byte>(tempBuff, 0, bytesRead).CopyTo(buffer);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(tempBuff);
                    }
#endif

                    Debug.WriteLine($"[FUSE] Read {bytesRead} bytes at offset {offset}");
                    return bytesRead;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FUSE] Read error: {ex.Message}");
                return -Posix.EIO;
            }
        }

        public override void Release(ReadOnlySpan<byte> path, ref FuseFileInfo fi)
        {
            try
            {
                Debug.WriteLine($"[FUSE] Release: handle={fi.fh}");

                VfsContext context;
                lock (_handleLock)
                {
                    if (!_openFiles.TryGetValue(fi.fh, out context))
                        return;
                    _openFiles.Remove(fi.fh);
                }

                // Lock the context so any in-flight Read completes before we dispose the stream.
                lock (context.ReadLock)
                {
                    VfsStreamHelper.CloseContext(context, _model.GetResourcesByIndex(context.DataStoreIndex));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FUSE] Release error: {ex.Message}");
            }
        }

        public override int StatFS(ReadOnlySpan<byte> path, ref statvfs buf)
        {
            try
            {
                // Return reasonable filesystem statistics
                buf.f_bsize = 4096;  // Block size
                buf.f_frsize = 4096; // Fragment size
                buf.f_blocks = 1024 * 1024 * 256; // Total blocks (1TB)
                buf.f_bfree = 1024 * 1024 * 100;  // Free blocks (400GB)
                buf.f_bavail = buf.f_bfree;       // Available blocks
                buf.f_files = 1000000;            // Total inodes
                buf.f_ffree = 500000;             // Free inodes
                buf.f_favail = buf.f_ffree;       // Available inodes
                buf.f_namemax = 255;              // Max filename length

                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FUSE] StatFs error: {ex.Message}");
                return -Posix.EIO;
            }
        }

        public override int Rename(ReadOnlySpan<byte> oldPath, ReadOnlySpan<byte> newPath, int flags)
        {
            if (!_model.UpdateMode)
                return -Posix.EACCES;

            string oldNameStr = getPath(oldPath);
            string newNameStr = getPath(newPath);

            if (_model.TryRenameImage(oldNameStr, newNameStr))
                return 0;

            return -Posix.EACCES;
        }

        public override int Unlink(ReadOnlySpan<byte> path)
        {
            if (!_model.UpdateMode)
                return -Posix.EACCES;

            string fileName = getPath(path);

            if (_model.TryDeleteImage(fileName))
                return 0;

            return -Posix.EACCES;
        }

        public override int RmDir(ReadOnlySpan<byte> path)
        {
            if (!_model.UpdateMode)
                return -Posix.EACCES;

            string fileName = getPath(path);

            if (_model.TryDeleteImage(fileName))
                return 0;

            return -Posix.EACCES;
        }

        #endregion

        #region Helper Methods

        private string getPath(ReadOnlySpan<byte> path)
        {
            if (path.Length == 0)
                return "/";

            return Encoding.UTF8.GetString(path);
        }

        #endregion
    }
}
#endif
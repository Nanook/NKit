#if WINDOWS
using DokanNet;
using DokanNet.Logging;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using FileAccess = DokanNet.FileAccess;

namespace Nanook.NKit.Vfs
{
    internal class WindowsFs : IDokanOperations, IVfsOperations
    {
        private const char _Separator = VfsConstants.WindowsSeparator;
        private VfsModel _model;

        /// <summary>
        /// Gets the correct resource manager for a VfsContext based on its DataStoreIndex.
        /// </summary>
        private IMountResourceManager getResourcesForContext(VfsContext ctx) => _model.GetResourcesByIndex(ctx.DataStoreIndex);

        // Per-context locking (ctx.ReadLock) serialises concurrent access to the same handle.

        public WindowsFs(string dataStorePath, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, int maxFileSystemYamlSizeKiB = DataStore.DefaultMaxFileSystemSizeKiB)
        {
            _model = new VfsModel(dataStorePath, _Separator, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
        }

        public WindowsFs(IEnumerable<string> dataStorePaths, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, int maxFileSystemYamlSizeKiB = DataStore.DefaultMaxFileSystemSizeKiB)
        {
            _model = new VfsModel(dataStorePaths, _Separator, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
        }

        public void Dispose()
        {
            // Dispose model and force datastore cleanup on unmount to ensure SQLite WAL/SHM files
            // are checkpointed and OS handles released.
            try
            {
                _model?.Dispose();
            }
            catch { }

            try
            {
                // Allow finalizers to run and OS to free file handles
                try { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(50); } catch { }
            }
            catch { }
        }

        [SupportedOSPlatform("windows")]
        public void Mount(string mountPoint) => Mount(mountPoint, null);

        [SupportedOSPlatform("windows")]
        public void Mount(string mountPoint, ManualResetEventSlim unmountSignal)
        {
            DokanOptions opts = _model.UpdateMode ? 0 : DokanOptions.WriteProtection;
            ConsoleLogger logger = new ConsoleLogger("[NKDS] ");
#if DEBUG
            //opts |= DokanOptions.DebugMode;
#endif
            DokanInstanceBuilder builder = new DokanInstanceBuilder(new Dokan(logger))
                .ConfigureOptions(options =>
                {
                    options.MountPoint = mountPoint;
                    options.Options = opts;
                });
            using (DokanInstance instance = builder.Build(this))
            {
                if (unmountSignal != null)
                {
                    // UI mode: block until the unmount signal is set (via Dispose)
                    unmountSignal.Wait();
                }
                else
                {
                    // CLI mode: block until signalled externally
                    System.Diagnostics.Trace.WriteLine("Filesystem mounted - waiting for unmount signal...");
                }
            }
            System.Diagnostics.Trace.WriteLine("Filesystem unmounted.");
        }

        protected NtStatus Trace(string method, string fileName, IDokanFileInfo info, NtStatus result,
            params object[] parameters) => result;

        private NtStatus trace(string method, string fileName, IDokanFileInfo info,
            FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
            NtStatus result) => result;

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            NtStatus result = DokanResult.Success;
            VfsContext context = _model.GetScanFsItemAsContext(fileName);

            if (context != null)
            {
                if (mode == FileMode.Open)
                    info.Context = context;
                else
                    result = DokanResult.AccessDenied;
            }
            else
                result = DokanResult.FileNotFound;

            return result;
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            if (info.Context is VfsContext ctx)
                VfsStreamHelper.CloseContext(ctx, getResourcesForContext(ctx));
            info.Context = null;
            Trace(nameof(Cleanup), fileName, info, DokanResult.Success);
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            if (info.Context is VfsContext ctx)
                VfsStreamHelper.CloseContext(ctx, getResourcesForContext(ctx));
            info.Context = null;
            Trace(nameof(CloseFile), fileName, info, DokanResult.Success);
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            bytesRead = 0;
            if (info.Context is not VfsContext ctx)
                return Trace(nameof(ReadFile), fileName, info, DokanResult.Success, "out 0", offset.ToString(CultureInfo.InvariantCulture));

            lock (ctx.ReadLock)
            {
                try
                {
                    try
                    {
                        VfsStreamHelper.EnsureStream(_model, ctx, getResourcesForContext(ctx));
                    }
                    catch
                    {
                        return ctx.Type == FsItemType.IsoFs ? DokanResult.FileNotFound : DokanResult.InternalError;
                    }

                    if (ctx.FileStream != null)
                    {
                        try
                        {
                            ctx.FileStream.Position = offset;
                            bytesRead = ctx.FileStream.Read(buffer, 0, buffer.Length);
                        }
                        catch (Exception)
                        {
                            return DokanResult.InternalError;
                        }
                    }

                    return Trace(nameof(ReadFile), fileName, info, DokanResult.Success, "out " + bytesRead.ToString(), offset.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    bytesRead = 0;
                    return DokanResult.InternalError;
                }
            }
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            return Trace(nameof(WriteFile), fileName, info, DokanResult.Success, "out " + bytesWritten.ToString(), offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            try
            {
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.Success);
            }
            catch (IOException)
            {
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.DiskFull);
            }
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            IFsItem fs = _model.GetScanFsItem(fileName, _Separator);
            if (fs == null)
            {
                fileInfo = new FileInformation();
                return DokanResult.FileNotFound;
            }
            int depth = fileName.Split(new[] { _Separator }, StringSplitOptions.RemoveEmptyEntries).Length;
            bool isWritable = _model.UpdateMode && depth <= 2;
            fileInfo = fs.ToFileInfo(isWritable);
            info.IsDirectory = (fileInfo.Attributes & FileAttributes.Directory) != 0;

            return NtStatus.Success;
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = FindFilesHelper(fileName, "*");
            return Trace(nameof(FindFiles), fileName, info, DokanResult.Success);
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info) => NtStatus.AccessDenied;

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
            DateTime? lastWriteTime, IDokanFileInfo info) => NtStatus.AccessDenied;

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            if (!_model.UpdateMode)
                return NtStatus.AccessDenied;

            if (_model.TryDeleteImage(fileName))
                return DokanResult.Success;

            return NtStatus.AccessDenied;
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            if (!_model.UpdateMode)
                return NtStatus.AccessDenied;

            if (_model.TryDeleteImage(fileName))
                return DokanResult.Success;

            return NtStatus.AccessDenied;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            if (!_model.UpdateMode)
                return NtStatus.AccessDenied;

            if (_model.TryRenameImage(oldName, newName))
                return DokanResult.Success;

            return NtStatus.AccessDenied;
        }

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info) => NtStatus.AccessDenied;

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info) => NtStatus.AccessDenied;

        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info) => DokanResult.NotImplemented;

        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info) => DokanResult.NotImplemented;

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            // Get stats from the first available drive
            DriveInfo dinfo = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);

            if (dinfo != null)
            {
                freeBytesAvailable = dinfo.AvailableFreeSpace;
                totalNumberOfBytes = dinfo.TotalSize;
                totalNumberOfFreeBytes = dinfo.TotalFreeSpace;
            }
            else
            {
                // Fallback values
                freeBytesAvailable = 1024L * 1024 * 1024 * 100; // 100 GB
                totalNumberOfBytes = 1024L * 1024 * 1024 * 1000; // 1 TB
                totalNumberOfFreeBytes = freeBytesAvailable;
            }

            return Trace(nameof(GetDiskFreeSpace), null, info, DokanResult.Success, "out " + freeBytesAvailable.ToString(),
                "out " + totalNumberOfBytes.ToString(), "out " + totalNumberOfFreeBytes.ToString());
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
            out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            volumeLabel = "NKit DataStore";
            fileSystemName = "NTFS";
            maximumComponentLength = 256;

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, "out " + volumeLabel,
                "out " + features.ToString(), "out " + fileSystemName);
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            security = null;
            return DokanResult.NotImplemented;
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info) => DokanResult.NotImplemented;

        public NtStatus Mounted(IDokanFileInfo info) => Trace(nameof(Mounted), null, info, DokanResult.Success);

        public NtStatus Mounted(string mountPoint, IDokanFileInfo info) => Trace(nameof(Mounted), null, info, DokanResult.Success);

        public NtStatus Unmounted(IDokanFileInfo info) => Trace(nameof(Unmounted), null, info, DokanResult.Success);

        public NtStatus FindStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, IDokanFileInfo info)
        {
            streamName = string.Empty;
            streamSize = 0;
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented, enumContext.ToString(),
                "out " + streamName, "out " + streamSize.ToString());
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            streams = new FileInformation[0];
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented);
        }

        public IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            try
            {
                int parentDepth = fileName.Split(new[] { _Separator }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool isWritable = _model.UpdateMode && (parentDepth + 1) <= 2;
                return _model.GetScanFsItems(fileName, searchPattern, _Separator)
                    .Select(a => a.ToFileInfo(isWritable))
                    .ToList();
            }
            catch
            {
                return new List<FileInformation>();
            }
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            int parentDepth = fileName.Split(new[] { _Separator }, StringSplitOptions.RemoveEmptyEntries).Length;
            bool isWritable = _model.UpdateMode && (parentDepth + 1) <= 2;
            files = _model.GetScanFsItems(fileName, searchPattern, _Separator).Select(a => a.ToFileInfo(isWritable)).ToList();
            return NtStatus.Success;
        }
    }
}
#endif
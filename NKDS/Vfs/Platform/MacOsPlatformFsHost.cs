#if MACOS
using System;
using System.Diagnostics;
using System.Threading;
using NKitDataStore;

namespace Nanook.NKit.Vfs
{
    public class MacOsPlatformFsHost : NKDS.Mount.IPlatformFsHost
    {
        private MacOsFs _fs;

        public void Run(string dataStorePath, string mountPoint, string? setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null, int maxFileSystemYamlSizeKiB = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB)
        {
            _fs = new MacOsFs(dataStorePath, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
            _fs.Mount(mountPoint, allowOther, uid, gid);
            // Best-effort cleanup after unmount
            try
            {
                try { GC.Collect(); GC.WaitForPendingFinalizers(); System.Threading.Thread.Sleep(50); } catch { }

                // FUSE-T / umount may delete the mount point directory on unmount.
                // Recreate it so the user's directory isn't permanently lost.
                try
                {
                    if (!string.IsNullOrEmpty(mountPoint) && !System.IO.Directory.Exists(mountPoint))
                        System.IO.Directory.CreateDirectory(mountPoint);
                }
                catch { /* best-effort */ }
            }
            catch { }
        }

        public void Dispose()
        {
            _fs?.Dispose();
        }

        public void FinalizeDatabases(string dataStorePath, string? setName = null)
        {
            try
            {
                try { GC.Collect(); GC.WaitForPendingFinalizers(); System.Threading.Thread.Sleep(50); } catch { }
            }
            catch { }
        }

        public string GetErrorMessage(Exception ex)
        {
            var msg = "FUSE-T Error: " + ex.Message
                + "\n\nMake sure FUSE-T is installed:"
                + "\n  Install via Homebrew: brew install macos-fuse-t/homebrew-cask/fuse-t"
                + "\n  Or download from: https://github.com/macos-fuse-t/fuse-t/releases";
#if DEBUG
            msg += "\n\nStack trace:\n" + ex.StackTrace;
#endif
            return msg;
        }
    }
}
#endif
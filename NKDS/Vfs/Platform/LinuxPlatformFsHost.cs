#if LINUX
using System;
using System.Diagnostics;
using System.Threading;
using NKitDataStore;

namespace Nanook.NKit.Vfs
{
    public class LinuxPlatformFsHost : NKDS.Mount.IPlatformFsHost
    {
        private LinuxFs _fs;

        public void Run(string dataStorePath, string mountPoint, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null, int maxFileSystemYamlSizeKiB = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB)
        {
            _fs = new LinuxFs(dataStorePath, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
            _fs.Mount(mountPoint, allowOther, uid, gid);
            // Best-effort cleanup after unmount
            try
            {
                try { GC.Collect(); GC.WaitForPendingFinalizers(); System.Threading.Thread.Sleep(50); } catch { }

                // libfuse3 / fusermount3 may delete the mount point directory on unmount.
                // Recreate it so the user's directory isn't permanently lost.
                try
                {
                    if (!string.IsNullOrEmpty(mountPoint) && !System.IO.Directory.Exists(mountPoint))
                        System.IO.Directory.CreateDirectory(mountPoint);
                }
                catch { /* best-effort — don't crash if we can't recreate it */ }
            }
            catch { }
        }

        public void Dispose()
        {
            _fs?.Dispose();
        }

        public void FinalizeDatabases(string dataStorePath, string setName = null)
        {
            try
            {
                try { GC.Collect(); GC.WaitForPendingFinalizers(); System.Threading.Thread.Sleep(50); } catch { }
            }
            catch { }
        }

        public string GetErrorMessage(Exception ex)
        {
            // Provide a helpful hint for FUSE-related issues
            var msg = "FUSE Error: " + ex.Message
                + "\n\nMake sure FUSE 3 is installed:"
                + "\n  Debian/Ubuntu: sudo apt install fuse3 libfuse3-dev"
                + "\n  Fedora/RHEL:   sudo dnf install fuse3 fuse3-devel"
                + "\n  Arch:          sudo pacman -S fuse3";
#if DEBUG
            msg += "\n\nStack trace:\n" + ex.StackTrace;
#endif
            return msg;
        }
    }
}
#endif
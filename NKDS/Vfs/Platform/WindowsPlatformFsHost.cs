#if WINDOWS
#pragma warning disable CA1416 // Validate platform compatibility
using NKitDataStore;

namespace Nanook.NKit.Vfs
{
    public class WindowsPlatformFsHost : NKDS.Mount.IPlatformFsHost
    {
        private WindowsFs _fs;
        private ManualResetEventSlim _unmountSignal = new ManualResetEventSlim(false);

        /// <summary>
        /// Optional list of additional DataStore paths to mount alongside the primary path.
        /// Set this before calling Run() to mount multiple DataStores in a single Dokan instance.
        /// </summary>
        public string[] DataStorePaths { get; set; }

        public void Run(string dataStorePath, string mountPoint, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null, int maxFileSystemYamlSizeKiB = DataStore.DefaultMaxFileSystemSizeKiB)
        {
            if (DataStorePaths != null && DataStorePaths.Length > 0)
                _fs = new WindowsFs(DataStorePaths, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
            else
                _fs = new WindowsFs(dataStorePath, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB: maxFileSystemYamlSizeKiB);
            try
            {
                _fs.Mount(mountPoint, _unmountSignal);
            }
            finally
            {
                // Dispose the filesystem/model HERE, on the mount (host) thread, AFTER the Dokan
                // instance has been torn down by Mount() returning. Disposing from Dispose() on the
                // caller thread instead would run concurrently with Dokan still delivering
                // Cleanup/CloseFile/ReadFile callbacks into the same VfsModel — a data race that
                // crashed or hung the close. Single-thread ownership of teardown removes that race.
                try { _fs?.Dispose(); } catch { }
                _fs = null;

                // Best-effort cleanup after unmount
                try
                {
                    try { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(50); } catch { }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            // Signal the mount thread to exit. The model/filesystem is disposed by the mount thread
            // itself once Run()'s Mount loop returns (see Run's finally), so we must NOT dispose it
            // here — doing so would race the still-running Dokan callbacks. The orchestrator joins
            // the mount thread after calling Dispose() to guarantee teardown has completed.
            _unmountSignal.Set();
        }

        public void FinalizeDatabases(string dataStorePath, string setName = null)
        {
            try
            {
                try { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(50); } catch { }
            }
            catch { }
        }

        public string GetErrorMessage(Exception ex)
        {
            if (ex is DokanNet.DokanException dex)
            {
                string msg = "Dokan Error: " + dex.Message
                    + "\n\nMake sure the Dokan driver is installed:"
                    + "\n  Install Dokan v2.3.1.1000 or later from:"
                    + "\n  https://github.com/dokan-dev/dokany/releases"
                    + "\n  A reboot is required after installation.";
#if DEBUG
                msg += "\n\nStack trace:\n" + dex.StackTrace;
#endif
                return msg;
            }
            string generic = "Error: " + ex.Message;
#if DEBUG
            generic += "\n\nStack trace:\n" + ex.StackTrace;
#endif
            return generic;
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
#endif
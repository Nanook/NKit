namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Abstraction for platform-specific filesystem implementations (Windows/Dokan and Linux/FUSE).
    /// </summary>
    internal interface IVfsOperations : System.IDisposable
    {
        /// <summary>
        /// Mounts the filesystem at the given mount point and blocks until unmounted.
        /// </summary>
        void Mount(string mountPoint);
    }
}
using NKitDataStore;
using NKitDataStore.Interfaces;

namespace Nanook.NKit.Vfs
{
    internal enum FsItemType { PreVfs, IsoFs, FileSystemFs, StoredFileFs }

    internal class VfsContext
    {
        // Serialises concurrent access to the same file handle (lazy stream init + Position + Read).
        // Different file handles have independent contexts so they proceed in parallel.
        internal readonly object ReadLock = new object();

        public string VfsFullName { get; internal set; }
        public ImageRecord ImageRecord { get; internal set; }
        public IDataStore DataStore { get; internal set; }
        public FsItemType Type { get; internal set; }
        public Stream FileStream { get; internal set; }
        public IFsItem FsItem { get; internal set; }
        public IImageReader ImageReader { get; internal set; }

        // Resource tracking for MountResourceManager release in CloseContext.
        // Set when a shared reader/buffer cache is acquired so CloseContext knows what to release.
        public string ResourceSetName { get; internal set; }
        public long? ResourceImageId { get; internal set; }

        /// <summary>
        /// Index into VfsModel._allDataStores identifying which DataStore this context's image belongs to.
        /// Used for multi-DataStore mount support to route resource manager calls correctly.
        /// </summary>
        public int DataStoreIndex { get; internal set; }
    }
}
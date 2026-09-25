using NKitDataStore;
using NKitDataStore.Interfaces;

namespace Nanook.NKit.Vfs
{
    internal interface IMountHandler
    {
        /// <summary>
        /// When true, duplicate images within the same set are merged into a single
        /// virtual folder for this handler's mount kind. The handler is responsible for
        /// aggregating content from all duplicates (e.g. via getMergedAreas).
        /// </summary>
        bool MergesDuplicates { get; }
        // List top-level items for a given system and optional mask. Root param is the configured root name.
        // This method lists the root items.
        IEnumerable<IFsItem> ListRoot(string systemName, string root, string mask);
        // List items directly under an image folder (e.g., listing the image's top-level filesystem entries and stored files)
        // This method lists the items in a folder.
        IEnumerable<IFsItem> ListFolder(VfsModelItem folderImage, string root, string mask);
        // Find child within a specific image folder by single name (used for APP-area resolution)
        // This method finds a child item by name.
        IFsItem FindChild(VfsModelItem folderImage, string root, string childName, out FsItemType type, out ImageRecord imageRecord);
        // Resolve a path inside an image folder's filesystem. startIndex points to the first filesystem segment.
        // This method resolves a path in the folder.
        IFsItem FindInFolder(VfsModelItem folderImage, string root, string[] pth, int startIndex, out FsItemType type, out ImageRecord imageRecord);
        // Attempt to create a readable stream for a file item handled by this mount handler.
        // Returns true if a stream was created. Implementations must not use reflection.
        bool TryCreateAreaStream(VfsModelItem folderImage, IFsFile fsItem, out System.IO.Stream stream, out IImageReader reader);
        // (loaders removed) Handlers do not expose loader methods; loading is centralized in MountRegistry.
    }
}
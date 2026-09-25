using NKitDataStore;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Vfs
{
    internal class BaseMountHandler : IMountHandler
    {
        protected readonly VfsModel Model;
        public BaseMountHandler(VfsModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        /// <inheritdoc />
        public virtual bool MergesDuplicates => false;

        public static bool NameMatches(string pattern, string name)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*")
                return true;

            // Simple wildcard to regex: '*' => '.*', '?' => '.'
            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
        }

        public virtual IEnumerable<IFsItem> ListRoot(string systemName, string root, string mask)
        {
            // default: list images (iso) and image folders
            // If named roots exist for this system (Images/Filesystems), do not list images directly under the system root.
            if (string.IsNullOrEmpty(root) && Model.HasNamedRoots(systemName))
                yield break;

            foreach (VfsModelItem img in Model.Images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only list images/folders that belong to the appropriate root placement
                // If root is non-empty, items belong under that root and should not be listed here
                if (!string.IsNullOrEmpty(root))
                    continue;

                // Respect model flags: image view vs filesystem/folder view
                if (Model.ShowImageFlag && BaseMountHandler.NameMatches(mask, img.NameAsIso))
                    yield return new FsImage() { Name = img.NameAsIso, FsSize = img.ImageSize };

                if (Model.ShowFolderFlag && BaseMountHandler.NameMatches(mask, img.NameAsFolder))
                    yield return new FsFolder() { Name = img.NameAsFolder, Parent = null };
            }
        }

        public virtual IFsItem FindChild(VfsModelItem folderImage, string root, string childName, out FsItemType type, out ImageRecord imageRecord)
        {
            type = FsItemType.FileSystemFs;
            imageRecord = folderImage.ImageRecord;
            return null;
        }

        // List items under an image folder: default - nothing
        public virtual IEnumerable<IFsItem> ListFolder(VfsModelItem folderImage, string root, string mask)
        {
            yield break;
        }

        // Resolve a path inside an image folder: default - not handled
        public virtual IFsItem FindInFolder(VfsModelItem folderImage, string root, string[] pth, int startIndex, out FsItemType type, out ImageRecord imageRecord)
        {
            // Default behavior: no filesystem traversal provided by base handler
            type = FsItemType.PreVfs;
            imageRecord = folderImage?.ImageRecord;
            return null;
        }

        // No loader methods in base handler - loading is managed by MountRegistry.
        // Default stream creation: handlers that do not support area streams should return false.
        public virtual bool TryCreateAreaStream(VfsModelItem folderImage, IFsFile fsItem, out System.IO.Stream stream, out NKitDataStore.Interfaces.IImageReader reader)
        {
            stream = null;
            reader = null;
            return false;
        }
    }
}
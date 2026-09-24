using NKitDataStore;

namespace Nanook.NKit.Vfs
{
    internal class FilesystemMountHandler : BaseMountHandler
    {
        public FilesystemMountHandler(VfsModel model) : base(model) { }

        public override IEnumerable<IFsItem> ListRoot(string systemName, string root, string mask)
        {
            // For filesystem-kind root listings we should expose folder-style image entries
            // (the folder view) instead of ISO image file nodes. Base.ListRoot returns
            // both image and folder entries depending on model flags which causes
            // duplicates when multiple mount kinds are active. Return only the
            // folder view here and respect the model's folder visibility flags.
            if (!string.IsNullOrEmpty(root))
                yield break;

            // If the model has named roots for this system, avoid listing images at the system root
            if (string.IsNullOrEmpty(root) && Model.HasNamedRoots(systemName))
                yield break;

            if (!Model.ShowFolderFlag)
                yield break;

            foreach (VfsModelItem img in Model.Images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // TmdAppFolder images are not shown in filesystem mode (Req 10.4).
                // They are an image-mode concept; filesystem mode shows individual
                // disambiguated "Image Name [tmd.X]" images instead.
                if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                    continue;

                if (BaseMountHandler.NameMatches(mask, img.NameAsFolder))
                    yield return new FsFolder() { Name = img.NameAsFolder, Parent = null };
            }
        }

        public override IEnumerable<IFsItem> ListFolder(VfsModelItem folderImage, string root, string mask)
        {
            if (folderImage == null)
                yield break;

            if (Model.ShowFileSystemFlag)
            {
                // Ensure NkFs is loaded
                Model.GetMountRegistry().LoadNkfs(folderImage, Model);

                if (Model.ShowSystemFlag)
                {
                    Model.GetMountRegistry().LoadStoredFiles(folderImage, Model);
                    if (folderImage.StoredFiles != null)
                    {
                        IFsFolder rootFolder = Model.GetOrCreateImageFolder(
                            folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                        foreach (StoredFileEntry storedEntry in folderImage.StoredFiles)
                        {
                            if (isFilesystemStoredFileName(storedEntry.Name)
                                && BaseMountHandler.NameMatches(mask, storedEntry.Name))
                            {
                                yield return new VfsModel.FsStoredFile
                                {
                                    Name = storedEntry.Name,
                                    FsSize = storedEntry.Size,
                                    StoredFileName = storedEntry.StoredFileName ?? storedEntry.Name,
                                    Parent = rootFolder
                                };
                            }
                        }
                    }
                }

                if (folderImage.IsMultiFilesystem)
                {
                    IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

                    if (Model.ShowSystemFlag)
                    {
                        // System mode: show all filesystem types as subfolders
                        foreach (string typeName in folderImage.FileSystemNkfsPerType.Keys)
                        {
                            if (BaseMountHandler.NameMatches(mask, typeName))
                                yield return new FsFolder() { Name = typeName, Parent = imageRootFolder };
                        }
                    }
                    else
                    {
                        // Non-system mode: show only the best filesystem's contents directly
                        NkFs bestNkfs = getBestFilesystem(folderImage.FileSystemNkfsPerType);
                        if (bestNkfs != null)
                        {
                            foreach ((int idx, NkFsEntry child) in bestNkfs.GetChildren(0))
                            {
                                string childName = bestNkfs.GetEntryName(idx);
                                if (child.SystemFlag)
                                    continue;
                                if (!BaseMountHandler.NameMatches(mask, childName))
                                    continue;
                                if (child.IsDirectory)
                                    yield return new NkFsFolderItem(bestNkfs, idx, child) { Parent = imageRootFolder };
                                else
                                    yield return new NkFsFileItem(bestNkfs, idx, child) { Parent = imageRootFolder };
                            }
                        }
                    }
                }
                else
                {
                    NkFs nkfs = folderImage.FileSystemNkfs;
                    if (nkfs != null)
                    {
                        IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                            folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

                        if (Model.ShowSystemFlag && folderImage.FileSystemNkfsPerType != null && folderImage.FileSystemNkfsPerType.Count == 1)
                        {
                            // System mode with single per-type file: show as subfolder
                            string typeName = folderImage.FileSystemNkfsPerType.Keys.First();
                            if (BaseMountHandler.NameMatches(mask, typeName))
                                yield return new FsFolder() { Name = typeName, Parent = imageRootFolder };
                        }
                        else
                        {
                            // Non-system mode or unified filesystem: show contents directly
                            foreach ((int idx, NkFsEntry child) in nkfs.GetChildren(0))
                            {
                                string childName = nkfs.GetEntryName(idx);
                                bool isSystem = child.SystemFlag;

                                if (!Model.ShowSystemFlag && isSystem)
                                    continue;

                                if (!BaseMountHandler.NameMatches(mask, childName))
                                    continue;

                                if (child.IsDirectory)
                                    yield return new NkFsFolderItem(nkfs, idx, child) { Parent = imageRootFolder };
                                else
                                    yield return new NkFsFileItem(nkfs, idx, child) { Parent = imageRootFolder };
                            }
                        }
                    }
                }
            }
        }

        public override IFsItem FindChild(VfsModelItem folderImage, string root, string childName, out FsItemType type, out ImageRecord imageRecord)
        {
            if (folderImage != null && Model.ShowSystemFlag
                && isFilesystemStoredFileName(childName))
            {
                Model.GetMountRegistry().LoadStoredFiles(folderImage, Model);
                StoredFileEntry storedEntry = folderImage.StoredFiles?.FirstOrDefault(
                    f => string.Equals(f.Name, childName, StringComparison.OrdinalIgnoreCase));
                if (storedEntry != null)
                {
                    type = FsItemType.StoredFileFs;
                    imageRecord = folderImage.ImageRecord;
                    IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    return new VfsModel.FsStoredFile
                    {
                        Name = storedEntry.Name,
                        FsSize = storedEntry.Size,
                        StoredFileName = storedEntry.StoredFileName ?? storedEntry.Name,
                        Parent = imageRootFolder
                    };
                }
            }

            // System mode: resolve type subfolder names as folders
            if (folderImage != null && Model.ShowSystemFlag)
            {
                Model.GetMountRegistry().LoadNkfs(folderImage, Model);

                if (folderImage.FileSystemNkfsPerType != null
                    && folderImage.FileSystemNkfsPerType.TryGetValue(childName, out NkFs perTypeNkfs))
                {
                    type = FsItemType.FileSystemFs;
                    imageRecord = folderImage.ImageRecord;
                    IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    NkFsEntry rootEntry = perTypeNkfs.GetEntry(0);
                    return new NkFsFolderItem(perTypeNkfs, 0, rootEntry) { Parent = imageRootFolder };
                }
            }

            type = FsItemType.PreVfs;
            imageRecord = folderImage?.ImageRecord;
            return null;
        }

        public override IFsItem FindInFolder(VfsModelItem folderImage, string root, string[] pth,
            int startIndex, out FsItemType type, out ImageRecord imageRecord)
        {
            if (folderImage != null && Model.ShowSystemFlag && startIndex == pth.Length - 1
                && isFilesystemStoredFileName(pth[startIndex]))
            {
                Model.GetMountRegistry().LoadStoredFiles(folderImage, Model);
                StoredFileEntry storedEntry = folderImage.StoredFiles?.FirstOrDefault(
                    f => string.Equals(f.Name, pth[startIndex], StringComparison.OrdinalIgnoreCase));
                if (storedEntry != null)
                {
                    type = FsItemType.StoredFileFs;
                    imageRecord = folderImage.ImageRecord;
                    IFsFolder rootFolder = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    return new VfsModel.FsStoredFile
                    {
                        Name = storedEntry.Name,
                        FsSize = storedEntry.Size,
                        StoredFileName = storedEntry.StoredFileName ?? storedEntry.Name,
                        Parent = rootFolder
                    };
                }
            }

            type = FsItemType.FileSystemFs;
            imageRecord = folderImage?.ImageRecord;

            if (folderImage == null)
                return null;

            Model.GetMountRegistry().LoadNkfs(folderImage, Model);

            // Multi-filesystem path resolution
            if (folderImage.IsMultiFilesystem && startIndex < pth.Length)
            {
                if (Model.ShowSystemFlag)
                {
                    // System mode: first path segment is the filesystem type subfolder
                    string fsTypeName = pth[startIndex];
                    if (folderImage.FileSystemNkfsPerType.TryGetValue(fsTypeName, out NkFs perTypeNkfs))
                    {
                        // If the path is just the type subfolder itself, return the NkFs root
                        // as an NkFsFolderItem so its children are lazily enumerable
                        if (startIndex == pth.Length - 1)
                        {
                            IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                                folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                            NkFsEntry rootEntry = perTypeNkfs.GetEntry(0);
                            return new NkFsFolderItem(perTypeNkfs, 0, rootEntry) { Parent = imageRootFolder };
                        }

                        // Resolve remaining path within this filesystem's NkFs
                        string subPath = string.Join("/", pth, startIndex + 1, pth.Length - startIndex - 1);
                        int entryIndex = perTypeNkfs.ResolvePath(subPath);
                        if (entryIndex < 0)
                            return null;

                        NkFsEntry entry = perTypeNkfs.GetEntry(entryIndex);
                        IFsFolder imageRootFolder2 = Model.GetOrCreateImageFolder(
                            folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

                        if (entry.IsDirectory)
                            return new NkFsFolderItem(perTypeNkfs, entryIndex, entry) { Parent = imageRootFolder2 };
                        else
                            return new NkFsFileItem(perTypeNkfs, entryIndex, entry) { Parent = imageRootFolder2 };
                    }

                    // Type name not found in per-type dictionary — not found
                    return null;
                }
                else
                {
                    // Non-system mode: resolve directly in the best filesystem
                    NkFs bestNkfs = getBestFilesystem(folderImage.FileSystemNkfsPerType);
                    if (bestNkfs == null)
                        return null;

                    string subPath = string.Join("/", pth, startIndex, pth.Length - startIndex);
                    int entryIndex = bestNkfs.ResolvePath(subPath);
                    if (entryIndex < 0)
                        return null;

                    NkFsEntry entry = bestNkfs.GetEntry(entryIndex);
                    IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

                    if (entry.IsDirectory)
                        return new NkFsFolderItem(bestNkfs, entryIndex, entry) { Parent = imageRootFolder };
                    else
                        return new NkFsFileItem(bestNkfs, entryIndex, entry) { Parent = imageRootFolder };
                }
            }

            // Single per-type filesystem in system mode: type subfolder navigation
            if (Model.ShowSystemFlag && folderImage.FileSystemNkfsPerType != null
                && folderImage.FileSystemNkfsPerType.Count == 1 && startIndex < pth.Length)
            {
                string singleTypeName = folderImage.FileSystemNkfsPerType.Keys.First();
                NkFs singleNkfs = folderImage.FileSystemNkfsPerType.Values.First();

                if (string.Equals(pth[startIndex], singleTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (startIndex == pth.Length - 1)
                    {
                        IFsFolder imageRootFolder = Model.GetOrCreateImageFolder(
                            folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                        NkFsEntry rootEntry = singleNkfs.GetEntry(0);
                        return new NkFsFolderItem(singleNkfs, 0, rootEntry) { Parent = imageRootFolder };
                    }

                    string subPath = string.Join("/", pth, startIndex + 1, pth.Length - startIndex - 1);
                    int entryIndex = singleNkfs.ResolvePath(subPath);
                    if (entryIndex < 0)
                        return null;

                    NkFsEntry entry = singleNkfs.GetEntry(entryIndex);
                    IFsFolder imageRootFolder2 = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

                    if (entry.IsDirectory)
                        return new NkFsFolderItem(singleNkfs, entryIndex, entry) { Parent = imageRootFolder2 };
                    else
                        return new NkFsFileItem(singleNkfs, entryIndex, entry) { Parent = imageRootFolder2 };
                }
            }

            // Single-filesystem (unified): resolve directly
            NkFs nkfs = folderImage.FileSystemNkfs;
            if (nkfs == null)
                return null;

            // Build the sub-path from startIndex and resolve via flat entry table
            string subPath2 = string.Join("/", pth, startIndex, pth.Length - startIndex);
            int entryIndex2 = nkfs.ResolvePath(subPath2);
            if (entryIndex2 < 0)
                return null;

            NkFsEntry entry2 = nkfs.GetEntry(entryIndex2);
            IFsFolder imageRootFolder3 = Model.GetOrCreateImageFolder(
                folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

            if (entry2.IsDirectory)
                return new NkFsFolderItem(nkfs, entryIndex2, entry2) { Parent = imageRootFolder3 };
            else
                return new NkFsFileItem(nkfs, entryIndex2, entry2) { Parent = imageRootFolder3 };
        }

        /// <summary>
        /// Returns true if the name matches a filesystem stored file pattern:
        /// filesystem.nkfs, filesystem.yaml, filesystem.{type}.nkfs, or filesystem.{type}.yaml.
        /// </summary>
        private static bool isFilesystemStoredFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (!name.StartsWith("filesystem.", StringComparison.OrdinalIgnoreCase))
                return false;
            return name.EndsWith(".nkfs", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Priority order for filesystem types (highest priority first).
        /// When multiple filesystems exist, the best one is shown in non-system mode.
        /// </summary>
        private static readonly string[] _fsPriority = { "udf", "joliet", "rockridge", "romeo", "iso9660" };

        /// <summary>
        /// Selects the best (highest priority) filesystem from a per-type dictionary.
        /// After mount-time merge, "system" is no longer present in the dictionary
        /// (unless it's the only type, in which case it's acceptable to return it).
        /// </summary>
        private static NkFs getBestFilesystem(Dictionary<string, NkFs> perType)
        {
            if (perType == null || perType.Count == 0)
                return null;

            foreach (string priority in _fsPriority)
            {
                if (perType.TryGetValue(priority, out NkFs nkfs))
                    return nkfs;
            }

            // Unknown type — return the first entry
            return perType.Values.First();
        }
    }
}
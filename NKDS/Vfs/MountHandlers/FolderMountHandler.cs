using NKitDataStore;
using NKitDataStore.Interfaces;

namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Generic mount handler for <see cref="ImageFormat.Folder"/> and <see cref="ImageFormat.TmdAppFolder"/> images.
    /// Presents folder-stored content as a navigable directory tree using NkFs,
    /// with direct block-backed file reads for fs entries and child-image resolution for ifs entries.
    /// </summary>
    internal class FolderMountHandler : BaseMountHandler
    {
        public FolderMountHandler(VfsModel model) : base(model) { }

        /// <inheritdoc />
        public override bool MergesDuplicates => false;

        public override IEnumerable<IFsItem> ListRoot(string systemName, string root, string mask)
        {
            // List Folder and TmdAppFolder images as folders
            if (!string.IsNullOrEmpty(root))
                yield break;

            if (string.IsNullOrEmpty(root) && Model.HasNamedRoots(systemName))
                yield break;

            foreach (VfsModelItem img in Model.Images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (img.ImageRecord == null)
                    continue;

                if (img.ImageRecord.Format != ImageFormat.Folder && img.ImageRecord.Format != ImageFormat.TmdAppFolder && img.ImageRecord.Format != ImageFormat.CueFolder && img.ImageRecord.Format != ImageFormat.Cue && img.ImageRecord.Format != ImageFormat.Gdi)
                    continue;

                string folderName = img.NameAsFolder;
                if (BaseMountHandler.NameMatches(mask, folderName))
                    yield return new FsFolder() { Name = folderName, Parent = null };
            }
        }

        public override IEnumerable<IFsItem> ListFolder(VfsModelItem folderImage, string root, string mask)
        {
            if (folderImage?.ImageRecord == null)
                yield break;

            if (folderImage.ImageRecord.Format != ImageFormat.Folder && folderImage.ImageRecord.Format != ImageFormat.TmdAppFolder && folderImage.ImageRecord.Format != ImageFormat.CueFolder && folderImage.ImageRecord.Format != ImageFormat.Cue && folderImage.ImageRecord.Format != ImageFormat.Gdi)
                yield break;

            // Ensure NkFs is loaded
            Model.GetMountRegistry().LoadNkfs(folderImage, Model);

            IFsFolder parentFolder = Model.GetOrCreateImageFolder(folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

            // In system mode, show filesystem.nkfs and filesystem.yaml as stored file entries
            if (Model.ShowSystemFlag)
            {
                Model.GetMountRegistry().LoadStoredFiles(folderImage, Model);
                if (folderImage.StoredFiles != null)
                {
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
                                Parent = parentFolder
                            };
                        }
                    }
                }
            }

            // List entries from NkFs (skip for CUE/GDI — they use area-based listing below)
            NkFs nkfs = (folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi)
                ? null : folderImage.FileSystemNkfs;
            if (nkfs != null)
            {
                foreach ((int idx, NkFsEntry child) in nkfs.GetChildren(0))
                {
                    if (child.IsImageFile)
                    {
                        // IFS entry: read image index from string table prefix
                        string ifsName = nkfs.GetEntryName(idx);

                        if (!BaseMountHandler.NameMatches(mask, ifsName))
                            continue;

                        long imageId = nkfs.GetImageIndex(idx);
                        yield return new FsIfsFileItem
                        {
                            Name = ifsName,
                            FsSize = nkfs.GetFileSize(idx),
                            ReferencedImageId = imageId,
                            Parent = parentFolder
                        };
                    }
                    else
                    {
                        string childName = nkfs.GetEntryName(idx);
                        bool isSystem = child.SystemFlag;

                        if (!Model.ShowSystemFlag && isSystem)
                            continue;

                        if (!BaseMountHandler.NameMatches(mask, childName))
                            continue;

                        if (child.IsDirectory)
                            yield return new NkFsFolderItem(nkfs, idx, child) { Parent = parentFolder };
                        else
                            yield return new NkFsFileItem(nkfs, idx, child) { Parent = parentFolder };
                    }
                }
            }
            // CUE/GDI images without NkFs ifs entries: list areas + loose files directly
            if (nkfs == null && (folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi))
            {
                GlobalImageKey key = new GlobalImageKey(folderImage.ImageRecord.SetName, folderImage.ImageRecord.Id);
                using (IImageReader reader = Model.DataStore.OpenImageReader(key))
                {
                    // List loose files (index file: .cue or .gdi)
                    foreach (FileRecord file in reader.ListFiles())
                    {
                        // Skip system files (e.g. filesystem.nkfs) when not in system mode
                        if (file.IsSystem && !Model.ShowSystemFlag)
                            continue;
                        if (!BaseMountHandler.NameMatches(mask, file.Name))
                            continue;
                        yield return new VfsModel.FsStoredFile
                        {
                            Name = file.Name,
                            FsSize = file.UncompressedSize > 0 ? file.UncompressedSize : file.Size,
                            StoredFileName = file.Name,
                            Parent = parentFolder
                        };
                    }

                    // List areas as track files
                    foreach (AreaRecord area in reader.GetAreas())
                    {
                        string fileName = area.Metadata?.GetString(AreaValueType.FileName);
                        if (string.IsNullOrEmpty(fileName))
                            continue;
                        if (!BaseMountHandler.NameMatches(mask, fileName))
                            continue;
                        yield return new FsImageAreaFile
                        {
                            Name = fileName,
                            FsSize = area.Size,
                            FsOffset = area.Offset,
                            Parent = parentFolder
                        };
                    }
                }
            }
        }

        public override IFsItem FindChild(VfsModelItem folderImage, string root, string childName,
            out FsItemType type, out ImageRecord imageRecord)
        {
            type = FsItemType.PreVfs;
            imageRecord = folderImage?.ImageRecord;

            if (folderImage?.ImageRecord == null)
                return null;

            if (folderImage.ImageRecord.Format != ImageFormat.Folder && folderImage.ImageRecord.Format != ImageFormat.TmdAppFolder && folderImage.ImageRecord.Format != ImageFormat.CueFolder && folderImage.ImageRecord.Format != ImageFormat.Cue && folderImage.ImageRecord.Format != ImageFormat.Gdi)
                return null;

            // In system mode, check for filesystem stored files (nkfs/yaml, including per-type)
            if (Model.ShowSystemFlag && isFilesystemStoredFileName(childName))
            {
                Model.GetMountRegistry().LoadStoredFiles(folderImage, Model);
                StoredFileEntry storedEntry = folderImage.StoredFiles?.FirstOrDefault(
                    f => string.Equals(f.Name, childName, StringComparison.OrdinalIgnoreCase));
                if (storedEntry != null)
                {
                    type = FsItemType.StoredFileFs;
                    IFsFolder parentFolder2 = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    return new VfsModel.FsStoredFile
                    {
                        Name = storedEntry.Name,
                        FsSize = storedEntry.Size,
                        StoredFileName = storedEntry.StoredFileName ?? storedEntry.Name,
                        Parent = parentFolder2
                    };
                }
            }

            // Ensure NkFs is loaded
            Model.GetMountRegistry().LoadNkfs(folderImage, Model);

            if (folderImage.FileSystemNkfs == null
                || folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi)
            {
                // CUE/GDI images: resolve by area FileName or loose file name
                if (folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi)
                {
                    IFsFolder parentFolderCue = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    GlobalImageKey key = new GlobalImageKey(folderImage.ImageRecord.SetName, folderImage.ImageRecord.Id);
                    using (IImageReader cueReader = Model.DataStore.OpenImageReader(key))
                    {
                        // Check loose files first (the .cue/.gdi index)
                        foreach (FileRecord file in cueReader.ListFiles())
                        {
                            if (file.IsSystem && !Model.ShowSystemFlag)
                                continue;
                            if (file.Name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                            {
                                type = FsItemType.StoredFileFs;
                                return new VfsModel.FsStoredFile
                                {
                                    Name = file.Name,
                                    FsSize = file.UncompressedSize > 0 ? file.UncompressedSize : file.Size,
                                    StoredFileName = file.Name,
                                    Parent = parentFolderCue
                                };
                            }
                        }
                        // Check areas by FileName metadata
                        foreach (AreaRecord area in cueReader.GetAreas())
                        {
                            string fileName = area.Metadata?.GetString(AreaValueType.FileName);
                            if (!string.IsNullOrEmpty(fileName) && fileName.Equals(childName, StringComparison.OrdinalIgnoreCase))
                            {
                                type = FsItemType.FileSystemFs;
                                return new FsImageAreaFile
                                {
                                    Name = fileName,
                                    FsSize = area.Size,
                                    FsOffset = area.Offset,
                                    Parent = parentFolderCue
                                };
                            }
                        }
                    }
                }
                return null;
            }

            NkFs nkfs = folderImage.FileSystemNkfs;
            IFsFolder parentFolder = Model.GetOrCreateImageFolder(
                folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

            // Search fs section via ResolvePath (single segment)
            int entryIndex = nkfs.ResolvePath(childName);
            if (entryIndex > 0)
            {
                NkFsEntry entry = nkfs.GetEntry(entryIndex);
                if (entry.IsImageFile)
                {
                    type = FsItemType.StoredFileFs;
                    long imageId = nkfs.GetImageIndex(entryIndex);
                    return new FsIfsFileItem
                    {
                        Name = nkfs.GetEntryName(entryIndex),
                        FsSize = nkfs.GetFileSize(entryIndex),
                        ReferencedImageId = imageId,
                        Parent = parentFolder
                    };
                }

                type = FsItemType.FileSystemFs;
                if (entry.IsDirectory)
                    return new NkFsFolderItem(nkfs, entryIndex, entry) { Parent = parentFolder };
                else
                    return new NkFsFileItem(nkfs, entryIndex, entry) { Parent = parentFolder };
            }

            return null;
        }

        public override IFsItem FindInFolder(VfsModelItem folderImage, string root, string[] pth,
            int startIndex, out FsItemType type, out ImageRecord imageRecord)
        {
            type = FsItemType.PreVfs;
            imageRecord = folderImage?.ImageRecord;

            if (folderImage?.ImageRecord == null || startIndex >= pth.Length)
                return null;

            if (folderImage.ImageRecord.Format != ImageFormat.Folder && folderImage.ImageRecord.Format != ImageFormat.TmdAppFolder && folderImage.ImageRecord.Format != ImageFormat.CueFolder && folderImage.ImageRecord.Format != ImageFormat.Cue && folderImage.ImageRecord.Format != ImageFormat.Gdi)
                return null;

            // In system mode, check for filesystem stored files (nkfs/yaml, including per-type)
            if (Model.ShowSystemFlag && startIndex == pth.Length - 1
                && isFilesystemStoredFileName(pth[startIndex]))
            {
                Model.GetMountRegistry().LoadStoredFiles(folderImage, Model);
                StoredFileEntry storedEntry = folderImage.StoredFiles?.FirstOrDefault(
                    f => string.Equals(f.Name, pth[startIndex], StringComparison.OrdinalIgnoreCase));
                if (storedEntry != null)
                {
                    type = FsItemType.StoredFileFs;
                    IFsFolder parentFolder2 = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    return new VfsModel.FsStoredFile
                    {
                        Name = storedEntry.Name,
                        FsSize = storedEntry.Size,
                        StoredFileName = storedEntry.StoredFileName ?? storedEntry.Name,
                        Parent = parentFolder2
                    };
                }
            }

            // Ensure NkFs is loaded
            Model.GetMountRegistry().LoadNkfs(folderImage, Model);

            if (folderImage.FileSystemNkfs == null
                || folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi)
            {
                // CUE/GDI images: resolve by area FileName or loose file name
                if ((folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi)
                    && startIndex == pth.Length - 1)
                {
                    string childName2 = pth[startIndex];
                    IFsFolder parentFolderCue2 = Model.GetOrCreateImageFolder(
                        folderImage.ImageRecord.System, root, folderImage.NameAsFolder);
                    GlobalImageKey key2 = new GlobalImageKey(folderImage.ImageRecord.SetName, folderImage.ImageRecord.Id);
                    using (IImageReader cueReader2 = Model.DataStore.OpenImageReader(key2))
                    {
                        foreach (FileRecord file in cueReader2.ListFiles())
                        {
                            if (file.IsSystem && !Model.ShowSystemFlag)
                                continue;
                            if (file.Name.Equals(childName2, StringComparison.OrdinalIgnoreCase))
                            {
                                type = FsItemType.StoredFileFs;
                                return new VfsModel.FsStoredFile
                                {
                                    Name = file.Name,
                                    FsSize = file.UncompressedSize > 0 ? file.UncompressedSize : file.Size,
                                    StoredFileName = file.Name,
                                    Parent = parentFolderCue2
                                };
                            }
                        }
                        foreach (AreaRecord area in cueReader2.GetAreas())
                        {
                            string fileName = area.Metadata?.GetString(AreaValueType.FileName);
                            if (!string.IsNullOrEmpty(fileName) && fileName.Equals(childName2, StringComparison.OrdinalIgnoreCase))
                            {
                                type = FsItemType.FileSystemFs;
                                return new FsImageAreaFile
                                {
                                    Name = fileName,
                                    FsSize = area.Size,
                                    FsOffset = area.Offset,
                                    Parent = parentFolderCue2
                                };
                            }
                        }
                    }
                }
                return null;
            }

            NkFs nkfs = folderImage.FileSystemNkfs;
            IFsFolder parentFolder = Model.GetOrCreateImageFolder(
                folderImage.ImageRecord.System, root, folderImage.NameAsFolder);

            // Try resolving through the fs section
            string subPath = string.Join("/", pth, startIndex, pth.Length - startIndex);
            int entryIndex = nkfs.ResolvePath(subPath);
            if (entryIndex > 0)
            {
                NkFsEntry entry = nkfs.GetEntry(entryIndex);
                if (entry.IsImageFile)
                {
                    type = FsItemType.StoredFileFs;
                    long imageId = nkfs.GetImageIndex(entryIndex);
                    return new FsIfsFileItem
                    {
                        Name = nkfs.GetEntryName(entryIndex),
                        FsSize = nkfs.GetFileSize(entryIndex),
                        ReferencedImageId = imageId,
                        Parent = parentFolder
                    };
                }

                type = FsItemType.FileSystemFs;
                if (entry.IsDirectory)
                    return new NkFsFolderItem(nkfs, entryIndex, entry) { Parent = parentFolder };
                else
                    return new NkFsFileItem(nkfs, entryIndex, entry) { Parent = parentFolder };
            }

            // If path is a single segment, check IFS entries (flat, not nested)
            if (startIndex == pth.Length - 1)
            {
                foreach ((int idx, NkFsEntry child) in nkfs.GetChildren(0))
                {
                    if (child.IsImageFile)
                    {
                        string ifsName = nkfs.GetEntryName(idx);
                        if (ifsName.Equals(pth[startIndex], StringComparison.OrdinalIgnoreCase))
                        {
                            type = FsItemType.StoredFileFs;
                            long imageId = nkfs.GetImageIndex(idx);
                            return new FsIfsFileItem
                            {
                                Name = ifsName,
                                FsSize = nkfs.GetFileSize(idx),
                                ReferencedImageId = imageId,
                                Parent = parentFolder
                            };
                        }
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public override bool TryCreateAreaStream(VfsModelItem folderImage, IFsFile fsItem,
            out Stream stream, out IImageReader reader)
        {
            stream = null;
            reader = null;

            try
            {
                if (folderImage?.ImageRecord == null || fsItem == null)
                    return false;

                // Case 1: fs entries — files stored directly in this image's blocks
                if (fsItem is NkFsFileItem)
                {
                    // 0-byte files have no offset records — return an empty stream
                    if (fsItem.FsSize == 0)
                    {
                        stream = new MemoryStream(Array.Empty<byte>(), false);
                        reader = null;
                        return true;
                    }

                    IImageReader cachedReader = Model.Resources.AcquireReader(folderImage.ImageRecord.SetName, folderImage.ImageRecord.Id);

                    // Multi-extent files: read each extent sequentially from its recorded ImageOffset
                    IFsFileParts splitParts = fsItem.SplitParts;
                    if (splitParts != null && splitParts.Parts.Count >= 2)
                    {
                        stream = new MultiExtentStream(cachedReader, splitParts.Parts);
                        reader = null;
                        return true;
                    }

                    long offsetStart = fsItem.FsOffset;
                    stream = cachedReader.OpenStream(offsetStart);
                    reader = null; // Caller must not dispose the cached reader
                    return true;
                }

                // Case 1b: CUE/GDI area files — track data stored as areas with FileName metadata
                if (fsItem is FsImageAreaFile areaFile
                    && (folderImage.ImageRecord.Format == ImageFormat.Cue || folderImage.ImageRecord.Format == ImageFormat.Gdi))
                {
                    long off = areaFile.FsOffset;
                    long len = areaFile.FsSize;
                    if (len <= 0)
                        return false;

                    string setName = folderImage.ImageRecord.SetName;
                    long imageId = folderImage.ImageRecord.Id;
                    IImageReader sharedReader = Model.Resources.AcquireReader(setName, imageId);

                    string baseDir = Model.DataStorePath;
                    if (baseDir.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                        baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(baseDir))!;
                    NKitDataStore.IBlockProvider blockProvider = NKitDataStore.DataStore.CreateBlockProvider(sharedReader, baseDir, setName);

                    int bufferSize = 0x200000;
                    ImageBufferCache cache = Model.Resources.AcquireBufferCache(imageId, bufferSize, 16);

                    List<AreaRecord> areas = sharedReader.GetAreas().OrderBy(a => a.Offset).ToList();
                    int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();
                    AreaRecord firstArea = areas.FirstOrDefault();
                    if (firstArea != null && firstArea.SectionSize > 0)
                        sectionSize = firstArea.SectionSize;
                    int storeBlockSize = sharedReader.Info.BlockSize;
                    OffsetsManagerCacheResult cachedOffsets = Model.Resources.AcquireOffsetsManager(setName, imageId, sectionSize, storeBlockSize);

                    ImageBuilderIso9660Stream builder = new ImageBuilderIso9660Stream(
                        sharedReader, disposeReader: false, blockProvider: blockProvider, sharedBufferCache: cache, cachedOffsets: cachedOffsets);

                    try { builder.Position = off; } catch { }
                    stream = new BoundedStream(builder, off, len, ownsBaseStream: true);
                    reader = null;
                    return true;
                }

                // Case 2: ifs entries — files in child images referenced by imageId
                if (fsItem is FsIfsFileItem ifsItem)
                {
                    string setName = folderImage.ImageRecord.SetName;
                    long childImageId = ifsItem.ReferencedImageId;

                    // Acquire shared reader from the mount resource manager
                    IImageReader sharedReader = Model.Resources.AcquireReader(setName, childImageId);

                    // If the child image is soft-deleted, release and return null
                    if (sharedReader.Image == null || sharedReader.Image.Removed)
                    {
                        Model.Resources.ReleaseReader(setName, childImageId);
                        reader = null;
                        return false;
                    }

                    // Find the matching area by FileName or App metadata
                    NKitDataStore.AreaRecord matchingArea = null;
                    foreach (NKitDataStore.AreaRecord area in sharedReader.GetAreas())
                    {
                        string fn = area.Metadata?.GetString(NKitDataStore.AreaValueType.FileName);
                        if (string.IsNullOrEmpty(fn))
                            fn = area.Metadata?.GetString(NKitDataStore.AreaValueType.App);
                        if (!string.IsNullOrEmpty(fn) &&
                            fn.Equals(ifsItem.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            matchingArea = area;
                            break;
                        }
                    }

                    if (matchingArea == null)
                    {
                        Model.Resources.ReleaseReader(setName, childImageId);
                        reader = null;
                        return false;
                    }

                    // Create block provider with aux fallback for child image
                    string childBaseDir = Model.DataStorePath;
                    if (childBaseDir.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                        childBaseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(childBaseDir))!;
                    NKitDataStore.IBlockProvider childBlockProvider = NKitDataStore.DataStore.CreateBlockProvider(sharedReader, childBaseDir, setName);

                    int bufferSize = 0x200000; // 2MiB
                    ImageBufferCache cache = Model.Resources.AcquireBufferCache(childImageId, bufferSize, 16);

                    // Derive section size from areas (same logic as ImageBuilder)
                    List<AreaRecord> areas = sharedReader.GetAreas().OrderBy(a => a.Offset).ToList();
                    int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();
                    AreaRecord firstArea = areas.FirstOrDefault();
                    if (firstArea != null && firstArea.SectionSize > 0)
                        sectionSize = firstArea.SectionSize;
                    int storeBlockSize = sharedReader.Info.BlockSize;
                    OffsetsManagerCacheResult cachedOffsets = Model.Resources.AcquireOffsetsManager(setName, childImageId, sectionSize, storeBlockSize);

                    // Create ImageBuilderWiiUStream with shared reader (disposeReader: false) and shared buffer cache
                    ImageBuilderWiiUStream builder = new Nanook.NKit.ImageBuilderWiiUStream(
                        sharedReader, disposeReader: false, encrypt: true, blockProvider: childBlockProvider, sharedBufferCache: cache, cachedOffsets: cachedOffsets);

                    try { builder.Position = matchingArea.Offset; } catch { }
                    stream = new BoundedStream(builder, matchingArea.Offset,
                        matchingArea.Size, ownsBaseStream: true);
                    reader = null; // Shared reader — caller must not dispose
                    return true;
                }
            }
            catch
            {
                try { stream?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                stream = null;
                reader = null;
            }

            return false;
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
    }

    /// <summary>
    /// Represents a file from the ifs (Image File System) section of a TmdAppFolder's filesystem.
    /// Carries the referenced child image ID for stream creation.
    /// </summary>
    internal class FsIfsFileItem : IFsFile
    {
        public string Name { get; set; }
        public long FsSize { get; set; }
        public long ReferencedImageId { get; set; }
        public IFsFolder Parent { get; set; }

        public string Path
        {
            get
            {
                try
                {
                    if (Parent == null || Parent.Parent == null)
                        return "/" + (Name ?? "");
                    return Parent?.Path + "/" + (Name ?? "");
                }
                catch { return Name ?? ""; }
            }
        }

        public string FullName => (Parent != null ? (Parent.Path ?? "") + "/" : "") + (Name ?? "");
        public bool IsMissing => false;
        public bool IsLastFile => false;
        public ulong XxHash { get => 0; set { } }
        public uint Crc { get => 0; set { } }
        public uint GapCrc { get => 0; set { } }
        public bool IsSystemFile => false;
        public long FsOffset => 0;
        public long PostGapSize => 0;
        public long PostGapFsOffset => 0;
        public int SplitIndex => 0;
        public IFsFileParts SplitParts => null;

        public IFsFile Clone() => this;
        public override string ToString() => Name ?? "";
    }
}
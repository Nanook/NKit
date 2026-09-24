using NKitDataStore;
using NKitDataStore.Interfaces;

namespace Nanook.NKit.Vfs
{
    internal class WiiUMountHandler : BaseMountHandler
    {
        private readonly FolderMountHandler _folderHandler;

        public WiiUMountHandler(VfsModel model) : base(model)
        {
            _folderHandler = new FolderMountHandler(model);
        }

        /// <inheritdoc />
        public override bool MergesDuplicates => true;

        /// <summary>
        /// Finds all image records that belong to the same merge group as the given item.
        /// A merge group is WiiU APP items with the same base name and set name.
        /// Returns a single-element list when no siblings exist.
        /// </summary>
        private List<ImageRecord> getImageRecordsForMergeGroup(VfsModelItem folderImage)
        {
            if (folderImage.MergedImageRecords != null)
                return folderImage.MergedImageRecords;

            ImageRecord imgRec = folderImage.ImageRecord;
            if (imgRec == null || imgRec.Format != NKitDataStore.ImageFormat.App)
                return new List<ImageRecord> { imgRec };

            List<ImageRecord> siblings = new List<ImageRecord>();
            foreach (VfsModelItem other in Model.Images)
            {
                if (other.ImageRecord == null) continue;
                if (other.ImageRecord.Format != NKitDataStore.ImageFormat.App) continue;
                if (!string.Equals(other.ImageRecord.Name, imgRec.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(other.ImageRecord.SetName, imgRec.SetName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(other.System, folderImage.System, StringComparison.OrdinalIgnoreCase)) continue;
                siblings.Add(other.ImageRecord);
            }

            return siblings.Count > 0 ? siblings : new List<ImageRecord> { imgRec };
        }

        /// <summary>
        /// Describes a single area file entry produced by the merged-area aggregation.
        /// </summary>
        private struct MergedAreaEntry
        {
            public string DisplayName;
            public long Offset;
            public long Size;
            public ImageRecord SourceImage;
        }

        private readonly Dictionary<long, List<MergedAreaEntry>> _mergedAreasCache = new Dictionary<long, List<MergedAreaEntry>>();
        private readonly Dictionary<long, List<(StoredFileEntry File, ImageRecord Source)>> _mergedStoredFilesCache = new Dictionary<long, List<(StoredFileEntry File, ImageRecord Source)>>();

        /// <summary>
        /// Aggregates areas from all images in a merged VfsModelItem, deduplicating by
        /// name + hash and appending an index suffix for name collisions with different hashes.
        /// For non-merged items this returns the areas from the single image as-is.
        /// </summary>
        private List<MergedAreaEntry> getMergedAreas(VfsModelItem folderImage)
        {
            if (folderImage.ImageRecord != null && _mergedAreasCache.TryGetValue(folderImage.ImageRecord.Id, out List<MergedAreaEntry> cached))
                return cached;

            List<MergedAreaEntry> result = new List<MergedAreaEntry>();
            List<ImageRecord> imageRecords = folderImage.MergedImageRecords ?? getImageRecordsForMergeGroup(folderImage);

            // (displayName, xxhash, crc) → first entry already added — used for dedup
            Dictionary<string, List<(ulong xx, uint crc, MergedAreaEntry entry)>> seen = new Dictionary<string, List<(ulong xx, uint crc, MergedAreaEntry entry)>>(StringComparer.OrdinalIgnoreCase);

            foreach (ImageRecord imgRec in imageRecords)
            {
                try
                {
                    IImageReader reader = Model.Resources.AcquireReader(imgRec.SetName, imgRec.Id);
                    IEnumerable<AreaRecord> areas = reader.GetAreas();
                    if (areas == null) continue;

                    foreach (AreaRecord a in areas)
                    {
                        try
                        {
                            if (a is NKitDataStore.AreaRecord ar)
                            {
                                string displayName = ar.Metadata.GetString(NKitDataStore.AreaValueType.FileName);
                                if (string.IsNullOrEmpty(displayName))
                                {
                                    displayName = ar.Metadata.GetString(NKitDataStore.AreaValueType.App);
                                    if (string.IsNullOrEmpty(displayName))
                                        continue;
                                    if (!displayName.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && !displayName.Contains('.'))
                                        displayName = displayName + ".app";
                                }

                                ulong xx = ar.XxHash64;
                                uint crc = ar.Crc32;

                                if (!seen.TryGetValue(displayName, out List<(ulong xx, uint crc, MergedAreaEntry entry)> existing))
                                {
                                    MergedAreaEntry entry = new MergedAreaEntry { DisplayName = displayName, Offset = ar.Offset, Size = ar.Size, SourceImage = imgRec };
                                    seen[displayName] = new List<(ulong, uint, MergedAreaEntry)> { (xx, crc, entry) };
                                    result.Add(entry);
                                }
                                else
                                {
                                    // Check if an entry with the same hash already exists (dedup)
                                    bool dup = false;
                                    foreach ((ulong xx, uint crc, MergedAreaEntry entry) e in existing)
                                    {
                                        if (e.xx == xx && e.crc == crc)
                                        { dup = true; break; }
                                    }
                                    if (!dup)
                                    {
                                        // Different hash — add with index suffix
                                        int idx = existing.Count + 1;
                                        string baseName = displayName;
                                        int dot = baseName.LastIndexOf('.');
                                        string suffixed = dot > 0
                                            ? baseName.Substring(0, dot) + $"_{idx}" + baseName.Substring(dot)
                                            : baseName + $"_{idx}";
                                        MergedAreaEntry entry = new MergedAreaEntry { DisplayName = suffixed, Offset = ar.Offset, Size = ar.Size, SourceImage = imgRec };
                                        existing.Add((xx, crc, entry));
                                        result.Add(entry);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            if (folderImage.ImageRecord != null)
                _mergedAreasCache[folderImage.ImageRecord.Id] = result;

            return result;
        }

        /// <summary>
        /// Aggregates stored files from all images in a merged VfsModelItem, deduplicating by name.
        /// </summary>
        private List<(StoredFileEntry File, ImageRecord Source)> getMergedStoredFiles(VfsModelItem folderImage)
        {
            if (folderImage.ImageRecord != null && _mergedStoredFilesCache.TryGetValue(folderImage.ImageRecord.Id, out List<(StoredFileEntry File, ImageRecord Source)> cached))
                return cached;

            List<(StoredFileEntry, ImageRecord)> result = new List<(StoredFileEntry, ImageRecord)>();
            List<ImageRecord> imageRecords = folderImage.MergedImageRecords
                ?? getImageRecordsForMergeGroup(folderImage);
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ImageRecord imgRec in imageRecords)
            {
                try
                {
                    // Create a temporary VfsModelItem for LoadStoredFiles
                    VfsModelItem tmpItem = new VfsModelItem { ImageRecord = imgRec };
                    Model.GetMountRegistry().LoadStoredFiles(tmpItem, Model);
                    if (tmpItem.StoredFiles != null)
                    {
                        foreach (StoredFileEntry sf in tmpItem.StoredFiles)
                        {
                            if (seenNames.Add(sf.Name))
                                result.Add((sf, imgRec));
                        }
                    }
                }
                catch { }
            }

            // Also propagate to the parent item so future calls use the cached list
            if (folderImage.StoredFiles == null && result.Count > 0)
            {
                folderImage.StoredFiles = result.Select(r => r.Item1).ToList();
                folderImage.StoredFilesLoaded = true;
            }

            if (folderImage.ImageRecord != null)
                _mergedStoredFilesCache[folderImage.ImageRecord.Id] = result;

            return result;
        }

        public override IEnumerable<IFsItem> ListRoot(string systemName, string root, string mask)
        {
            // For WiiU we still list images but respect the configured root (images live under root == "")
            // If named roots exist for this system (Images/Filesystems) do not list images at the system root
            if (string.IsNullOrEmpty(root) && Model.HasNamedRoots(systemName))
                yield break;

            // Collect the set of base names that have a non-removed TmdAppFolder image.
            // App images whose base name matches a TmdAppFolder are hidden (the TmdAppFolder
            // represents the unified folder view).
            HashSet<string> tmdAppFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VfsModelItem img in Model.Images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                    && !img.ImageRecord.Removed)
                {
                    tmdAppFolderNames.Add(img.ImageRecord.Name);
                }
            }

            foreach (VfsModelItem img in Model.Images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(root))
                    continue; // images are mounted directly under system when root is empty

                // In merged mode, skip secondary items — the primary represents the group
                if (img.IsMergedSecondary)
                    continue;

                if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                {
                    // TmdAppFolder is CDN-format content, not a disc image — only show it
                    // in the folder/filesystem view, never in the image (iso) view.
                    if (!Model.ShowFolderFlag)
                        continue;

                    string folderName = img.ImageRecord.Name;
                    if (BaseMountHandler.NameMatches(mask, folderName))
                        yield return new FsFolder() { Name = folderName, Parent = null };
                }
                else if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App)
                {
                    // App is CDN-format content — only show it in the folder/filesystem view.
                    if (!Model.ShowFolderFlag)
                        continue;

                    // Hide App images that have a parent TmdAppFolder
                    string baseName = extractBaseName(img.ImageRecord.Name);
                    if (tmdAppFolderNames.Contains(baseName))
                        continue;

                    // Legacy APP images without a TmdAppFolder: show with existing behavior
                    string folderName = img.MergedImageRecords != null
                        ? img.ImageRecord.Name
                        : img.NameAsFolder;
                    if (BaseMountHandler.NameMatches(mask, folderName))
                        yield return new FsFolder() { Name = folderName, Parent = null };
                }
                else
                {
                    if (!BaseMountHandler.NameMatches(mask, img.NameAsIso))
                        continue;
                    yield return new VfsModel.FsImage() { Name = img.NameAsIso, FsSize = img.ImageSize };
                }
            }
        }

        /// <summary>
        /// Extracts the base name from a potentially disambiguated image name.
        /// E.g., "Game Title [tmd.0]" → "Game Title", "Game Title" → "Game Title".
        /// </summary>
        private static string extractBaseName(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
                return imageName;

            int bracketStart = imageName.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracketStart >= 0 && imageName.EndsWith("]"))
                return imageName.Substring(0, bracketStart);

            return imageName;
        }

        public override bool TryCreateAreaStream(VfsModelItem folderImage, IFsFile fsItem, out Stream stream, out IImageReader reader)
        {
            stream = null;
            reader = null;
            try
            {
                if (folderImage?.ImageRecord == null || fsItem == null)
                    return false;

                // Delegate TmdAppFolder images to FolderMountHandler
                if (folderImage.ImageRecord.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                    return _folderHandler.TryCreateAreaStream(folderImage, fsItem, out stream, out reader);

                // Only handle APP-format area files
                if (folderImage.ImageRecord.Format != NKitDataStore.ImageFormat.App)
                    return false;

                // Only handle area files — stored files (title.tik, title.tmd etc.)
                // must be read via DataStore.ReadFile, not through ImageBuilderWiiUStream.
                if (!(fsItem is FsImageAreaFile))
                    return false;

                long off = fsItem.FsOffset;
                long len = fsItem.FsSize;
                if (len <= 0)
                    return false;

                // Acquire shared reader from the mount resource manager
                // For merged items, use the area's source image record if available
                FsImageAreaFile areaFile = fsItem as FsImageAreaFile;
                ImageRecord targetImageRecord = areaFile?.SourceImageRecord ?? folderImage.ImageRecord;
                IImageReader sharedReader = Model.Resources.AcquireReader(targetImageRecord.SetName, targetImageRecord.Id);

                // Build ImageBuilderWiiUStream which reconstructs FS areas.
                // APP-format .app files are encrypted content containers — encryption
                // must be applied so the output matches the original format.
                int bufferSize = 0x200000; // 2MiB
                ImageBufferCache cache = Model.Resources.AcquireBufferCache(targetImageRecord.Id, bufferSize, 16);

                // Create block provider with aux fallback
                string baseDir = Model.DataStorePath;
                if (baseDir.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                    baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(baseDir))!;
                NKitDataStore.IBlockProvider blockProvider = NKitDataStore.DataStore.CreateBlockProvider(sharedReader, baseDir, targetImageRecord.SetName);

                // Derive section size from areas (same logic as ImageBuilder)
                List<AreaRecord> areas = sharedReader.GetAreas().OrderBy(a => a.Offset).ToList();
                int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();
                AreaRecord firstAreaEntry = areas.FirstOrDefault();
                if (firstAreaEntry != null && firstAreaEntry.SectionSize > 0)
                    sectionSize = firstAreaEntry.SectionSize;
                int storeBlockSize = sharedReader.Info.BlockSize;
                OffsetsManagerCacheResult cachedOffsets = Model.Resources.AcquireOffsetsManager(targetImageRecord.SetName, targetImageRecord.Id, sectionSize, storeBlockSize);

                ImageBuilderWiiUStream builder = new Nanook.NKit.ImageBuilderWiiUStream(
                    sharedReader, disposeReader: false, encrypt: true, blockProvider: blockProvider, sharedBufferCache: cache, cachedOffsets: cachedOffsets);
                if (off < 0 || off > builder.Length)
                {
                    try { builder.Dispose(); } catch { }
                    Model.Resources.ReleaseReader(targetImageRecord.SetName, targetImageRecord.Id);
                    Model.Resources.ReleaseBufferCache(targetImageRecord.Id);
                    Model.Resources.ReleaseOffsetsManager(targetImageRecord.SetName, targetImageRecord.Id);
                    return false;
                }

                try { builder.Position = off; } catch { }
                stream = new BoundedStream(builder, off, Math.Max(0, len), ownsBaseStream: true);
                reader = null; // Shared reader — caller must not dispose
                return true;
            }
            catch
            {
                // On error, dispose the builder-backed stream and the standalone reader
                try { stream?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                stream = null;
                reader = null;
                return false;
            }
        }

        public override IFsItem FindChild(VfsModelItem folderImage, string root, string childName, out FsItemType type, out ImageRecord imageRecord)
        {
            type = FsItemType.PreVfs;
            imageRecord = folderImage.ImageRecord;

            // Delegate TmdAppFolder images to FolderMountHandler
            if (folderImage.ImageRecord != null && folderImage.ImageRecord.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                return _folderHandler.FindChild(folderImage, root, childName, out type, out imageRecord);

            if (folderImage.ImageRecord != null && folderImage.ImageRecord.Format == NKitDataStore.ImageFormat.App)
            {
                // Use merged aggregation to handle both single and multi-image items
                string parentName = folderImage.MergedImageRecords != null
                    ? folderImage.ImageRecord.Name
                    : folderImage.NameAsFolder;
                IFsFolder parent = Model.GetOrCreateImageFolder(folderImage.ImageRecord.System, root, parentName);
                foreach (MergedAreaEntry entry in getMergedAreas(folderImage))
                {
                    if (string.Equals(entry.DisplayName, childName, StringComparison.OrdinalIgnoreCase))
                    {
                        type = FsItemType.StoredFileFs;
                        imageRecord = folderImage.ImageRecord;
                        return new FsImageAreaFile { Name = entry.DisplayName, FsSize = entry.Size, FsOffset = entry.Offset, SourceImageRecord = entry.SourceImage, Parent = parent };
                    }
                }

                // Also check stored files (e.g. title.tik, title.tmd) excluding internal artifacts
                foreach ((StoredFileEntry sf, ImageRecord srcImg) in getMergedStoredFiles(folderImage))
                {
                    if (string.Equals(sf.Name, NKitDataStore.DataStore.FileSystemYamlName, StringComparison.OrdinalIgnoreCase) || string.Equals(sf.Name, NKitDataStore.DataStore.FileSystemYamlRootPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(sf.Name, childName, StringComparison.OrdinalIgnoreCase))
                    {
                        type = FsItemType.StoredFileFs;
                        imageRecord = srcImg;
                        return new VfsModel.FsStoredFile { Name = sf.Name, FsSize = sf.Size, StoredFileName = sf.StoredFileName ?? sf.Name, Parent = parent };
                    }
                }
            }

            return null;
        }

        public override IFsItem FindInFolder(VfsModelItem folderImage, string root, string[] pth, int startIndex, out FsItemType type, out ImageRecord imageRecord)
        {
            // For APP-format images, use the same area resolution logic to return files or folders
            type = FsItemType.PreVfs;
            imageRecord = folderImage.ImageRecord;

            // Delegate TmdAppFolder images to FolderMountHandler
            if (folderImage.ImageRecord != null && folderImage.ImageRecord.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                return _folderHandler.FindInFolder(folderImage, root, pth, startIndex, out type, out imageRecord);

            if (folderImage.ImageRecord != null && folderImage.ImageRecord.Format == NKitDataStore.ImageFormat.App)
            {
                // Use merged aggregation to handle both single and multi-image items
                string parentName2 = folderImage.MergedImageRecords != null
                    ? folderImage.ImageRecord.Name
                    : folderImage.NameAsFolder;
                IFsFolder parent = Model.GetOrCreateImageFolder(folderImage.ImageRecord.System, root, parentName2);
                if (startIndex < pth.Length)
                {
                    foreach (MergedAreaEntry entry in getMergedAreas(folderImage))
                    {
                        if (string.Equals(entry.DisplayName, pth[startIndex], StringComparison.OrdinalIgnoreCase))
                        {
                            type = FsItemType.StoredFileFs;
                            imageRecord = folderImage.ImageRecord;
                            return new FsImageAreaFile { Name = entry.DisplayName, FsSize = entry.Size, FsOffset = entry.Offset, SourceImageRecord = entry.SourceImage, Parent = parent };
                        }
                    }

                    // Also check stored files (e.g. title.tik, title.tmd) excluding internal artifacts
                    foreach ((StoredFileEntry sf, ImageRecord srcImg) in getMergedStoredFiles(folderImage))
                    {
                        if (string.Equals(sf.Name, NKitDataStore.DataStore.FileSystemYamlName, StringComparison.OrdinalIgnoreCase) || string.Equals(sf.Name, NKitDataStore.DataStore.FileSystemYamlRootPath, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (string.Equals(sf.Name, pth[startIndex], StringComparison.OrdinalIgnoreCase))
                        {
                            type = FsItemType.StoredFileFs;
                            imageRecord = srcImg;
                            return new VfsModel.FsStoredFile { Name = sf.Name, FsSize = sf.Size, StoredFileName = sf.StoredFileName ?? sf.Name, Parent = parent };
                        }
                    }
                }
            }

            return null;
        }

        public override IEnumerable<IFsItem> ListFolder(VfsModelItem folderImage, string root, string mask)
        {
            if (folderImage == null)
                yield break;

            // Delegate TmdAppFolder images to FolderMountHandler
            if (folderImage.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder)
            {
                foreach (IFsItem item in _folderHandler.ListFolder(folderImage, root, mask))
                    yield return item;
                yield break;
            }

            string parentName = folderImage.MergedImageRecords != null
                ? folderImage.ImageRecord.Name
                : folderImage.NameAsFolder;
            IFsFolder parent = Model.GetOrCreateImageFolder(folderImage.ImageRecord.System, root, parentName);

            List<IFsItem> areaItems = new List<IFsItem>();
            List<IFsItem> storedItems = new List<IFsItem>();

            // Collect APP areas using merged aggregation (handles both single and multi-image items)
            if (folderImage.ImageRecord != null && folderImage.ImageRecord.Format == NKitDataStore.ImageFormat.App)
            {
                foreach (MergedAreaEntry entry in getMergedAreas(folderImage))
                {
                    if (BaseMountHandler.NameMatches(mask, entry.DisplayName))
                        areaItems.Add(new FsImageAreaFile { Name = entry.DisplayName, FsSize = entry.Size, FsOffset = entry.Offset, SourceImageRecord = entry.SourceImage, Parent = parent });
                }
            }

            // Collect stored files using merged aggregation
            foreach ((StoredFileEntry sf, ImageRecord srcImg) in getMergedStoredFiles(folderImage))
            {
                // Hide internal datastore artifacts from the listing (never show filesystem files for WiiU APP images)
                if (sf.Name.StartsWith("filesystem.", StringComparison.OrdinalIgnoreCase)
                    && (sf.Name.EndsWith(".nkfs", StringComparison.OrdinalIgnoreCase)
                        || sf.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (BaseMountHandler.NameMatches(mask, sf.Name))
                    storedItems.Add(new VfsModel.FsStoredFile { Name = sf.Name, FsSize = sf.Size, StoredFileName = sf.StoredFileName ?? sf.Name, Parent = parent });
            }

            // Yield collected items (safe to yield outside try/catch)
            foreach (IFsItem it in areaItems)
                yield return it;
            foreach (IFsItem it in storedItems)
                yield return it;
        }
    }
}
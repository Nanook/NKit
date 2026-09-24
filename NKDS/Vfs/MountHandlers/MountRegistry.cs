using NKitDataStore;
using NKitDataStore.Interfaces;
using System.Diagnostics;

namespace Nanook.NKit.Vfs
{
    internal class MountRegistry
    {
        private readonly Dictionary<string, List<MountSpec>> _systemMountSpecs;

        public MountRegistry(IEnumerable<string> systemNames)
        {
            _systemMountSpecs = new Dictionary<string, List<MountSpec>>(StringComparer.OrdinalIgnoreCase);
            if (systemNames == null)
                return;

            foreach (string system in systemNames)
            {
                List<MountSpec> specs = new List<MountSpec>();
                if (string.Equals(system, VfsConstants.SystemWiiU, StringComparison.OrdinalIgnoreCase))
                {
                    specs.Add(new MountSpec { Kind = MountKind.Image, Root = "" });
                    specs.Add(new MountSpec { Kind = MountKind.FileSystem, Root = "" });
                    specs.Add(new MountSpec { Kind = MountKind.AppFolder, Root = "" });
                }
                else
                {
                    specs.Add(new MountSpec { Kind = MountKind.Image, Root = "" });
                    specs.Add(new MountSpec { Kind = MountKind.FileSystem, Root = "" });
                }
                _systemMountSpecs[system] = specs;
            }
        }

        public List<MountSpec> GetMountSpecsForSystem(string systemName)
        {
            if (string.IsNullOrEmpty(systemName))
                return new List<MountSpec>();
            if (_systemMountSpecs.TryGetValue(systemName, out List<MountSpec> specs))
                return specs;
            return new List<MountSpec>();
        }

        public IMountHandler GetHandlerForSystem(string systemName, VfsModel model)
        {
            if (string.Equals(systemName, VfsConstants.SystemWiiU, StringComparison.OrdinalIgnoreCase))
                return getOrCreateHandler(systemName, () => new WiiUMountHandler(model));
            return getOrCreateHandler(systemName, () => new BaseMountHandler(model));
        }

        /// <summary>
        /// Returns the appropriate mount handler for a given image format.
        /// <see cref="ImageFormat.Folder"/> and <see cref="ImageFormat.TmdAppFolder"/> images
        /// are routed through <see cref="FolderMountHandler"/>; WiiU APP images through
        /// <see cref="WiiUMountHandler"/>; all others through the system default.
        /// </summary>
        public IMountHandler GetHandlerForFormat(ImageFormat format, string systemName, VfsModel model)
        {
            if (format == ImageFormat.Folder || format == ImageFormat.TmdAppFolder
                || format == ImageFormat.CueFolder || format == ImageFormat.Cue || format == ImageFormat.Gdi)
                return getOrCreateHandler("__folder__", () => new FolderMountHandler(model));
            return GetHandlerForSystem(systemName, model);
        }

        public IMountHandler GetHandlerForKind(string systemName, MountKind kind, VfsModel model)
        {
            switch (kind)
            {
                case MountKind.FileSystem:
                    return getOrCreateHandler(systemName + ":" + kind.ToString(), () => new FilesystemMountHandler(model));
                case MountKind.AppFolder:
                    return GetHandlerForSystem(systemName, model);
                case MountKind.Image:
                default:
                    return GetHandlerForSystem(systemName, model);
            }
        }

        /// <summary>
        /// Returns the handler for a given kind, taking the image format into account.
        /// <see cref="ImageFormat.Folder"/> and <see cref="ImageFormat.TmdAppFolder"/>
        /// images are always routed through <see cref="FolderMountHandler"/>.
        /// </summary>
        public IMountHandler GetHandlerForKind(string systemName, MountKind kind, ImageFormat format, VfsModel model)
        {
            if (format == ImageFormat.Folder || format == ImageFormat.TmdAppFolder
                || format == ImageFormat.CueFolder)
                return GetHandlerForFormat(format, systemName, model);
            // CUE/GDI route to FolderMountHandler for image/folder kinds, but NOT for filesystem kind
            if ((format == ImageFormat.Cue || format == ImageFormat.Gdi) && kind != MountKind.FileSystem)
                return GetHandlerForFormat(format, systemName, model);
            return GetHandlerForKind(systemName, kind, model);
        }

        private readonly Dictionary<string, IMountHandler> _handlerCache = new Dictionary<string, IMountHandler>(StringComparer.OrdinalIgnoreCase);

        private IMountHandler getOrCreateHandler(string key, Func<IMountHandler> creator)
        {
            if (_handlerCache.TryGetValue(key, out IMountHandler h))
                return h;
            IMountHandler nh = creator();
            _handlerCache[key] = nh;
            return nh;
        }

        // Loader helpers moved into registry: handlers no longer perform lazy loads directly.
        /// <summary>
        /// Ensures the NkFs filesystem data is loaded for the given item.
        /// Tries filesystem.nkfs first, falls back to filesystem.yaml (converting to NkFs).
        /// Thread-safe with per-item locking and LRU cache integration.
        /// </summary>
        public void LoadNkfs(VfsModelItem item, VfsModel model)
        {
            if (item == null || item.FileSystemNkfsLoaded)
            {
                // Already loaded — just touch the LRU cache to keep it fresh
                if (item?.FileSystemNkfsLoaded == true && item.ImageRecord != null)
                    model.TouchNkfsCache(item.ImageRecord.Id);
                return;
            }

            // Per-item lock: allows concurrent loads of different items
            object itemLock = model.GetNkfsLoadLock(item.ImageRecord.Id);
            lock (itemLock)
            {
                // Double-check after acquiring lock
                if (item.FileSystemNkfsLoaded)
                {
                    model.TouchNkfsCache(item.ImageRecord.Id);
                    return;
                }

                try
                {
                    NkFs nkfs = null;
                    GlobalImageKey key = new GlobalImageKey(item.ImageRecord.SetName, item.ImageRecord.Id);
                    // Use the correct DataStore for this item (multi-DataStore mount support)
                    IDataStore dataStore = model.GetDataStoreByIndex(item.DataStoreIndex);

                    // 1. Try per-type nkfs files (e.g., filesystem.iso9660.nkfs, filesystem.joliet.nkfs)
                    List<(string typeName, byte[] data)> perTypeFiles = ProbePerTypeNkfs(dataStore, key);

                    if (perTypeFiles.Count > 0)
                    {
                        Dictionary<string, NkFs> dict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);
                        foreach ((string typeName, byte[] data) in perTypeFiles)
                        {
                            try
                            {
                                dict[typeName] = NkFs.FromBytes(data);
                            }
                            catch
                            {
                                Trace.WriteLine($"[LazyLoad] Corrupt filesystem.{typeName}.nkfs for '{item.ImageRecord.Name}', skipping");
                            }
                        }

                        if (dict.Count > 0)
                        {
                            item.FileSystemNkfsPerType = dict;

                            // Merge system entries for ISO9660-based systems
                            if (dict.Count > 0 && isIso9660SystemType(item.ImageRecord.System))
                            {
                                mergeSystemFilesystem(dict);
                            }

                            item.FileSystemNkfs = dict.Count == 1 ? dict.Values.First() : null;
                            Trace.WriteLine($"[LazyLoad] Loaded {dict.Count} per-type nkfs file(s) for '{item.ImageRecord.Name}' "
                                + $"in set '{item.ImageRecord.SetName}' (on-demand, per-type)");
                        }
                        else
                        {
                            // All per-type files were corrupt — fall through to unified fallback
                            Trace.WriteLine($"[LazyLoad] All per-type nkfs files corrupt for '{item.ImageRecord.Name}', "
                                + "falling back to unified filesystem.nkfs");
                        }
                    }

                    // 2. Fall back to unified filesystem.nkfs (existing behavior)
                    if (item.FileSystemNkfsPerType == null)
                    {
                        byte[] nkfsData = null;
                        try { nkfsData = dataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemNkfsRootPath); } catch { }

                        if (nkfsData != null)
                        {
                            try
                            {
                                nkfs = NkFs.FromBytes(nkfsData);
                                Trace.WriteLine($"[LazyLoad] Loaded filesystem.nkfs for '{item.ImageRecord.Name}' "
                                    + $"in set '{item.ImageRecord.SetName}' (on-demand, binary)");
                            }
                            catch
                            {
                                // Corrupt NkFs — fall through to YAML fallback
                                nkfs = null;
                                Trace.WriteLine($"[LazyLoad] Corrupt filesystem.nkfs for '{item.ImageRecord.Name}', "
                                    + "falling back to filesystem.yaml");
                            }
                        }

                        // 3. Fallback: try filesystem.yaml (YAML text → convert to NkFs)
                        if (nkfs == null)
                        {
                            byte[] yamlData = null;
                            try { yamlData = dataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemYamlRootPath); } catch { }
                            if (yamlData == null)
                            {
                                try { yamlData = dataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemYamlName); } catch { }
                            }

                            if (yamlData != null)
                            {
                                try
                                {
                                    FsYaml fsYaml = FsYaml.FromBytes(yamlData);
                                    nkfs = NkFs.FromFsYaml(fsYaml);
                                    Trace.WriteLine($"[LazyLoad] Loaded filesystem.yaml for '{item.ImageRecord.Name}' "
                                        + $"in set '{item.ImageRecord.SetName}' (on-demand, YAML→NkFs)");
                                }
                                catch { }
                            }
                        }

                        item.FileSystemNkfs = nkfs;
                    }
                }
                catch
                {
                    // On failure, mark as loaded to prevent infinite retries
                }

                item.FileSystemNkfsLoaded = true;

                // Register in LRU cache (may trigger eviction of another item)
                if (item.FileSystemNkfs != null)
                    model.TrackNkfsLoaded(item.ImageRecord.Id);
            }
        }

        /// <summary>
        /// Probes the DataStore for per-type nkfs files (pattern "filesystem.{type}.nkfs")
        /// by listing all stored files for an image and filtering with TryExtractFsTypeName.
        /// Returns a list of (typeName, data) tuples for discovered per-type files.
        /// </summary>
        private List<(string typeName, byte[] data)> ProbePerTypeNkfs(IDataStore dataStore, GlobalImageKey key)
        {
            List<(string, byte[])> results = new List<(string, byte[])>();
            using (IImageReader reader = dataStore.OpenImageReader(key))
            {
                foreach (FileRecord file in reader.ListFiles())
                {
                    if (NKitDataStore.DataStore.TryExtractFsTypeName(file.Name, out string typeName))
                    {
                        byte[] data = dataStore.ReadFile(key, file.Name);
                        if (data != null)
                            results.Add((typeName, data));
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Returns true for system types where the system filesystem merge applies.
        /// </summary>
        private static bool isIso9660SystemType(string systemTypeName)
        {
            if (string.IsNullOrEmpty(systemTypeName))
                return false;
            return string.Equals(systemTypeName, "Default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "Dreamcast", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "PS1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "PS2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "PS3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "PSP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "SegaCD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "Saturn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "CDi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(systemTypeName, "PcEngine", StringComparison.OrdinalIgnoreCase);
        }

        public void LoadStoredFiles(VfsModelItem item, VfsModel model)
        {
            if (item == null || item.StoredFilesLoaded)
                return;

            try
            {
                // Use the correct DataStore and Resources for this item (multi-DataStore mount support)
                IDataStore itemDataStore = model.GetDataStoreByIndex(item.DataStoreIndex);
                IMountResourceManager itemResources = model.GetResourcesByIndex(item.DataStoreIndex);

                // For merged items, iterate all merged image records and deduplicate by name
                // so the full set of stored files is available regardless of which image owns each file.
                List<ImageRecord> imageRecords = item.MergedImageRecords ?? new List<ImageRecord> { item.ImageRecord };
                List<StoredFileEntry> files = new List<StoredFileEntry>();
                HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (ImageRecord imgRec in imageRecords)
                {
                    try
                    {
                        IImageReader reader = itemResources.AcquireReader(imgRec.SetName, imgRec.Id);
                        foreach (FileRecord fr in reader.ListFiles())
                        {
                            // Skip system files (e.g. filesystem.nkfs) when not in system mode
                            if (fr.IsSystem && !model.ShowSystemFlag)
                                continue;
                            string displayName = fr.Name.StartsWith("/") ? fr.Name.Substring(1) : fr.Name;
                            if (seenNames.Add(displayName))
                                files.Add(new StoredFileEntry { Name = displayName, StoredFileName = fr.Name, Size = fr.UncompressedSize });
                        }

                        if (model.ShowSystemFlag)
                        {
                            try
                            {
                                GlobalImageKey imgKey = new GlobalImageKey(imgRec.SetName, imgRec.Id);

                                // Check for unified NkFs binary first
                                byte[] nkfsData = null;
                                try { nkfsData = itemDataStore.ReadFile(imgKey, NKitDataStore.DataStore.FileSystemNkfsRootPath); } catch { }

                                if (nkfsData != null)
                                {
                                    // Add the actual filesystem.nkfs binary file
                                    if (seenNames.Add(NKitDataStore.DataStore.FileSystemNkfsName))
                                    {
                                        files.Add(new StoredFileEntry
                                        {
                                            Name = NKitDataStore.DataStore.FileSystemNkfsName,
                                            StoredFileName = NKitDataStore.DataStore.FileSystemNkfsRootPath,
                                            Size = nkfsData.Length
                                        });
                                    }

                                    // Add virtual filesystem.yaml generated from binary data
                                    if (seenNames.Add(NKitDataStore.DataStore.FileSystemYamlName))
                                    {
                                        string yamlText = NkFs.FromBytes(nkfsData).ToFsYaml().ToYaml();
                                        files.Add(new StoredFileEntry
                                        {
                                            Name = NKitDataStore.DataStore.FileSystemYamlName,
                                            StoredFileName = NKitDataStore.DataStore.FileSystemNkfsRootPath,
                                            Size = System.Text.Encoding.UTF8.GetByteCount(yamlText)
                                        });
                                    }
                                }
                                else
                                {
                                    // Fall back to existing YAML file behavior
                                    byte[] fsYamlData = null;
                                    try { fsYamlData = itemDataStore.ReadFile(imgKey, NKitDataStore.DataStore.FileSystemYamlRootPath); } catch { }
                                    if (fsYamlData == null)
                                    {
                                        try { fsYamlData = itemDataStore.ReadFile(imgKey, NKitDataStore.DataStore.FileSystemYamlName); } catch { }
                                    }
                                    if (fsYamlData != null && seenNames.Add(NKitDataStore.DataStore.FileSystemYamlName))
                                        files.Add(new StoredFileEntry { Name = NKitDataStore.DataStore.FileSystemYamlName, StoredFileName = NKitDataStore.DataStore.FileSystemYamlRootPath, Size = fsYamlData.Length });
                                }

                                // Generate virtual YAML for each per-type nkfs file (e.g. filesystem.iso9660.nkfs -> filesystem.iso9660.yaml)
                                foreach (FileRecord fr in reader.ListFiles())
                                {
                                    if (NKitDataStore.DataStore.TryExtractFsTypeName(fr.Name, out string typeName))
                                    {
                                        string perTypeYamlName = $"filesystem.{typeName}.yaml";
                                        if (seenNames.Add(perTypeYamlName))
                                        {
                                            try
                                            {
                                                byte[] perTypeData = itemDataStore.ReadFile(imgKey, fr.Name);
                                                if (perTypeData != null)
                                                {
                                                    string yamlText = NkFs.FromBytes(perTypeData).ToFsYaml().ToYaml();
                                                    files.Add(new StoredFileEntry
                                                    {
                                                        Name = perTypeYamlName,
                                                        StoredFileName = fr.Name,
                                                        Size = System.Text.Encoding.UTF8.GetByteCount(yamlText)
                                                    });
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                item.StoredFiles = files.Count > 0 ? files : null;
            }
            catch { }

            item.StoredFilesLoaded = true;
        }

        /// <summary>
        /// If the per-type dictionary contains a "system" key, merges its entries
        /// into all non-system NkFs instances, then removes the "system" key.
        /// When no non-system types exist, the "system" key is retained.
        /// </summary>
        private static void mergeSystemFilesystem(Dictionary<string, NkFs> perType)
        {
            if (!perType.TryGetValue("system", out NkFs systemNkfs))
                return;

            List<string> nonSystemKeys = perType.Keys
                .Where(k => !string.Equals(k, "system", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nonSystemKeys.Count == 0)
                return; // Only system exists — retain it for fallback presentation

            foreach (string key in nonSystemKeys)
            {
                perType[key] = NkFsMerger.MergeSystemInto(perType[key], systemNkfs);
            }

            perType.Remove("system");
        }

        // Return mount specs augmented with virtual named roots when requested.
        // This centralizes the "Images" / "Filesystems" virtual roots so callers
        // don't hard-code those strings throughout the VFS model.
        public List<MountSpec> GetAugmentedMountSpecs(string systemName, bool includeImageRoot, bool includeFilesystemRoot)
        {
            List<MountSpec> specs = GetMountSpecsForSystem(systemName) ?? new List<MountSpec>();
            List<MountSpec> augmented = new List<MountSpec>(specs);

            // "Directories" system is folder-based storage — no named roots (Images/Filesystems).
            // Items appear directly under the system folder.
            if (string.Equals(systemName, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase))
                return augmented;

            if (includeImageRoot && !augmented.Any(s => !string.IsNullOrEmpty(s.Root) && string.Equals(s.Root, VfsConstants.RootImages, StringComparison.OrdinalIgnoreCase)))
                augmented.Add(new MountSpec { Kind = MountKind.Image, Root = VfsConstants.RootImages });

            if (includeFilesystemRoot && !augmented.Any(s => !string.IsNullOrEmpty(s.Root) && string.Equals(s.Root, VfsConstants.RootFilesystems, StringComparison.OrdinalIgnoreCase)))
                augmented.Add(new MountSpec { Kind = MountKind.FileSystem, Root = VfsConstants.RootFilesystems });

            return augmented;
        }
    }
}
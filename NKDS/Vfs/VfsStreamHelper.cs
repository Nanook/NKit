using NKitDataStore;
using NKitDataStore.Interfaces;

namespace Nanook.NKit.Vfs
{
    internal static class VfsStreamHelper
    {
        /// <summary>
        /// Lazily opens the correct stream on a context if it is not already open.
        /// Throws on failure; callers catch and return their platform-specific error code.
        /// </summary>
        internal static void EnsureStream(VfsModel model, VfsContext ctx, IMountResourceManager resources)
        {
            if (ctx.FileStream != null)
                return;

            // ifs entries (TmdAppFolder child-image files): deferred from CreateFile so
            // directory listings are instant. The child-image open + area query + crypto
            // stream setup only happens here on first actual Read.
            if (ctx.FsItem is FsIfsFileItem ifsItem && ctx.ImageRecord != null)
            {
                string setName = ctx.ImageRecord.SetName;
                long childImageId = ifsItem.ReferencedImageId;

                IImageReader reader = resources.AcquireReader(setName, childImageId);

                if (reader.Image == null || reader.Image.Removed)
                {
                    resources.ReleaseReader(setName, childImageId);
                    throw new FileNotFoundException($"Child image {childImageId} not found or removed.");
                }

                AreaRecord matchingArea = null;
                foreach (AreaRecord area in reader.GetAreas())
                {
                    string fn = area.Metadata?.GetString(AreaValueType.FileName);
                    if (string.IsNullOrEmpty(fn))
                        fn = area.Metadata?.GetString(AreaValueType.App);
                    if (!string.IsNullOrEmpty(fn) &&
                        fn.Equals(ifsItem.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingArea = area;
                        break;
                    }
                }

                if (matchingArea == null)
                {
                    resources.ReleaseReader(setName, childImageId);
                    throw new FileNotFoundException($"Area for '{ifsItem.Name}' not found in child image {childImageId}.");
                }

                // Create block provider with aux fallback for child image
                string childBaseDir = model.DataStorePath;
                if (childBaseDir.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                    childBaseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(childBaseDir))!;
                IBlockProvider childBlockProvider = NKitDataStore.DataStore.CreateBlockProvider(reader, childBaseDir, setName);

                int bufferSize = 0x200000; // 2MiB
                ImageBufferCache cache = resources.AcquireBufferCache(childImageId, bufferSize, 16);

                // Derive section size from areas (same logic as ImageBuilder)
                List<AreaRecord> areas = reader.GetAreas().OrderBy(a => a.Offset).ToList();
                int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();
                AreaRecord firstArea = areas.FirstOrDefault();
                if (firstArea != null && firstArea.SectionSize > 0)
                    sectionSize = firstArea.SectionSize;
                int storeBlockSize = reader.Info.BlockSize;
                OffsetsManagerCacheResult cachedOffsets = resources.AcquireOffsetsManager(setName, childImageId, sectionSize, storeBlockSize);

                ImageBuilderWiiUStream builder = new Nanook.NKit.ImageBuilderWiiUStream(
                    reader, disposeReader: false, encrypt: true, blockProvider: childBlockProvider, sharedBufferCache: cache, cachedOffsets: cachedOffsets);

                try { builder.Position = matchingArea.Offset; } catch { }
                ctx.ImageReader = null; // reader is shared, not owned by context
                ctx.ResourceSetName = setName;
                ctx.ResourceImageId = childImageId;
                ctx.FileStream = new BoundedStream(builder, matchingArea.Offset,
                    matchingArea.Size, ownsBaseStream: true);
                return;
            }

            if (ctx.Type == FsItemType.StoredFileFs && ctx.ImageRecord != null && ctx.FsItem is VfsModel.FsStoredFile storedFile)
            {
                GlobalImageKey key = new GlobalImageKey(ctx.ImageRecord.SetName, ctx.ImageRecord.Id);

                // NkFs-aware file serving: when StoredFileName references the NkFs path,
                // serve raw binary for filesystem.nkfs or convert to YAML text for virtual filesystem.yaml
                if (string.Equals(storedFile.StoredFileName, NKitDataStore.DataStore.FileSystemNkfsRootPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    byte[] data = ctx.DataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemNkfsRootPath);

                    if (data != null)
                    {
                        // If the display name is filesystem.nkfs, serve the raw binary data
                        // If the display name is filesystem.yaml, convert to YAML text
                        if (string.Equals(storedFile.Name, NKitDataStore.DataStore.FileSystemNkfsName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            // Serve raw NkFs binary
                        }
                        else
                        {
                            // Convert binary to YAML text for the virtual Filesystem.yaml
                            string yamlText = NKitDataStore.NkFs.FromBytes(data).ToFsYaml().ToYaml();
                            data = System.Text.Encoding.UTF8.GetBytes(yamlText);
                        }
                    }
                    else
                    {
                        // Fall back to YAML files
                        data = ctx.DataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemYamlRootPath)
                            ?? ctx.DataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemYamlName);
                    }

                    ctx.FileStream = data != null
                        ? new MemoryStream(data, writable: false)
                        : new MemoryStream(Array.Empty<byte>(), writable: false);
                }
                else
                {
                    byte[] data = ctx.DataStore.ReadFile(key, storedFile.StoredFileName);

                    // Per-type nkfs -> yaml conversion: if the display name is a per-type yaml
                    // (e.g. filesystem.iso9660.yaml) and the stored file is the corresponding nkfs binary,
                    // convert the binary data to YAML text.
                    if (data != null
                        && NKitDataStore.DataStore.TryExtractFsTypeName(storedFile.StoredFileName, out _)
                        && storedFile.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                        && !storedFile.Name.EndsWith(".nkfs", StringComparison.OrdinalIgnoreCase))
                    {
                        string yamlText = NKitDataStore.NkFs.FromBytes(data).ToFsYaml().ToYaml();
                        data = System.Text.Encoding.UTF8.GetBytes(yamlText);
                    }

                    // Cross-fallback: if filesystem.yaml not found, try filesystem.nkfs and convert
                    if (data == null && (string.Equals(storedFile.StoredFileName, NKitDataStore.DataStore.FileSystemYamlRootPath, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(storedFile.StoredFileName, NKitDataStore.DataStore.FileSystemYamlName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Try the other YAML path first
                        data = ctx.DataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemYamlRootPath)
                            ?? ctx.DataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemYamlName);

                        // If still not found, try NkFs and convert
                        if (data == null)
                        {
                            byte[] nkfsData = ctx.DataStore.ReadFile(key, NKitDataStore.DataStore.FileSystemNkfsRootPath);
                            if (nkfsData != null)
                            {
                                string yamlText = NKitDataStore.NkFs.FromBytes(nkfsData).ToFsYaml().ToYaml();
                                data = System.Text.Encoding.UTF8.GetBytes(yamlText);
                            }
                        }
                    }

                    ctx.FileStream = data != null
                        ? new MemoryStream(data, writable: false)
                        : new MemoryStream(Array.Empty<byte>(), writable: false);
                }
            }
            else if (ctx.Type == FsItemType.FileSystemFs && ctx.ImageRecord != null && ctx.FsItem is IFsFile fsFile)
            {
                IImageReader imageReader = model.DataStore.OpenImageReader(new GlobalImageKey(ctx.ImageRecord.SetName, ctx.ImageRecord.Id));
                ctx.ImageReader = imageReader;

                // Multi-extent files: join all extents into a single contiguous stream
                IFsFileParts splitParts = fsFile.SplitParts;
                if (splitParts != null && splitParts.Parts.Count >= 2)
                    ctx.FileStream = new Nanook.NKit.Vfs.MultiExtentStream(imageReader, splitParts.Parts);
                else
                    ctx.FileStream = imageReader.OpenStream(fsFile.FsOffset);
            }
            else if (ctx.Type == FsItemType.IsoFs && ctx.ImageRecord != null)
            {
                string setName = ctx.ImageRecord.SetName;
                long imageId = ctx.ImageRecord.Id;

                IImageReader imageReader = resources.AcquireReader(setName, imageId);

                // Create block provider with aux fallback (auto-discovers aux store by convention)
                string baseDir = model.DataStorePath;
                if (baseDir.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                    baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(baseDir))!;
                IBlockProvider blockProvider = NKitDataStore.DataStore.CreateBlockProvider(imageReader, baseDir, setName);

                int bufferSize = 0x200000; // 2MiB
                ImageBufferCache bufferCache = resources.AcquireBufferCache(imageId, bufferSize, 16);

                // Derive section size from areas (same logic as ImageBuilder)
                List<AreaRecord> areas = imageReader.GetAreas().OrderBy(a => a.Offset).ToList();
                int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();
                AreaRecord firstArea = areas.FirstOrDefault();
                if (firstArea != null && firstArea.SectionSize > 0)
                    sectionSize = firstArea.SectionSize;
                int storeBlockSize = imageReader.Info.BlockSize;
                OffsetsManagerCacheResult cachedOffsets = resources.AcquireOffsetsManager(setName, imageId, sectionSize, storeBlockSize);

                ctx.FileStream = ctx.ImageRecord.System switch
                {
                    "GameCube" => new ImageBuilderGameCubeStream(imageReader, disposeReader: false, blockProvider: blockProvider, sharedBufferCache: bufferCache, cachedOffsets: cachedOffsets),
                    "Wii" => new ImageBuilderWiiStream(imageReader, disposeReader: false, blockProvider: blockProvider, sharedBufferCache: bufferCache, cachedOffsets: cachedOffsets),
                    VfsConstants.SystemWiiU => new ImageBuilderWiiUStream(imageReader, disposeReader: false, encrypt: true, blockProvider: blockProvider, sharedBufferCache: bufferCache, cachedOffsets: cachedOffsets),
                    _ => new ImageBuilderIso9660Stream(imageReader, disposeReader: false, blockProvider: blockProvider, sharedBufferCache: bufferCache, cachedOffsets: cachedOffsets)
                };

                ctx.ImageReader = null; // reader is shared, not owned by context
                ctx.ResourceSetName = setName;
                ctx.ResourceImageId = imageId;
            }
        }

        /// <summary>
        /// Disposes the file stream and releases shared resources back to the manager.
        /// Standalone readers created for streaming (e.g. FileSystemFs) are disposed here.
        /// </summary>
        internal static void CloseContext(VfsContext ctx, IMountResourceManager resources)
        {
            ctx.FileStream?.Dispose();
            ctx.FileStream = null;
            // Release shared resources (reader + buffer cache + offsets cache) back to manager
            if (ctx.ResourceImageId.HasValue)
            {
                resources.ReleaseReader(ctx.ResourceSetName, ctx.ResourceImageId.Value);
                resources.ReleaseBufferCache(ctx.ResourceImageId.Value);
                resources.ReleaseOffsetsManager(ctx.ResourceSetName, ctx.ResourceImageId.Value);
                ctx.ResourceSetName = null;
                ctx.ResourceImageId = null;
            }
            ctx.ImageReader?.Dispose();
            ctx.ImageReader = null;
        }
    }
}
using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Processes synthetic folder SourceFiles by querying the datastore for
    /// child images and building a folder image (TmdAppFolder, CueFolder, etc.).
    /// This replaces the post-processing finalization approach.
    /// </summary>
    internal class FolderImageProcessor
    {
        /// <summary>
        /// Reads filesystem data for an image using NkFs-first fallback.
        /// Tries filesystem.nkfs first (converting to FsYaml), then falls back to filesystem.yaml.
        /// </summary>
        private static FsYaml ReadFsYaml(DataStore dataStore, GlobalImageKey key)
        {
            // Try NkFs first
            byte[] data = null;
            try { data = dataStore.ReadFile(key, DataStore.FileSystemNkfsRootPath); } catch { }
            if (data != null)
            {
                try { return NkFs.FromBytes(data).ToFsYaml(); } catch { }
            }

            // Fall back to YAML
            try { data = dataStore.ReadFile(key, DataStore.FileSystemYamlRootPath); } catch { }
            if (data == null)
            {
                try { data = dataStore.ReadFile(key, DataStore.FileSystemYamlName); } catch { }
            }

            if (data != null)
                return FsYaml.FromBytes(data);

            return null;
        }

        /// <summary>
        /// Processes a synthetic folder SourceFile.
        /// </summary>
        /// <param name="syntheticSource">The synthetic SourceFile with IsSyntheticFolder=true</param>
        /// <param name="dedupeDirectory">The datastore base directory</param>
        /// <param name="setName">The target set name</param>
        /// <param name="log">Logger for progress/error reporting</param>
        /// <returns>Result indicating success, skip, or error</returns>
        public FolderProcessResult Process(
            SourceFile syntheticSource,
            string dedupeDirectory,
            string setName,
            Action<string, LogLevel> log)
        {
            FolderGroupInfo group = syntheticSource.SyntheticFolderGroup;

            if (group.GroupType == FolderGroupType.CueFolder || group.GroupType == FolderGroupType.GdiFolder)
                return processCueFolder(group, dedupeDirectory, setName, log);

            return processTmdAppFolder(group, dedupeDirectory, setName, log);
        }

        /// <summary>
        /// Processes a TmdAppFolder group.
        /// </summary>
        private FolderProcessResult processTmdAppFolder(
            FolderGroupInfo group,
            string dedupeDirectory,
            string setName,
            Action<string, LogLevel> log)
        {
            try
            {
                // Output header in the same style as NKitTask (Title prefix so the host colours it)
                log?.Invoke(Log.Section, LogLevel.Info);
                log?.Invoke(InfoPrefix.Stamp(InfoPrefix.Title, $"[TmdAppFolder/{group.SystemType}]  {group.BaseName}"), LogLevel.Info);
                log?.Invoke(Log.Divider, LogLevel.Info);

                List<ChildImageInfo> childInfos;
                long shardSize;
                int blockSize;
                string system;

                using (DataStore dataStore = new DataStore(dedupeDirectory))
                {
                    List<ImageRecord> childImages = dataStore.ListAllImages(img =>
                        img.SetName == setName &&
                        !img.Removed &&
                        img.Format == ImageFormat.App &&
                        DataStore.IsTmdDisambiguatedName(img.Name) &&
                        string.Equals(
                            DataStore.ExtractBaseName(img.Name),
                            group.BaseName,
                            StringComparison.OrdinalIgnoreCase))
                        .OrderBy(img => img.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (childImages.Count == 0)
                    {
                        log?.Invoke("Result    : NoChildren (no child images in datastore)", LogLevel.Info);
                        return FolderProcessResult.NoChildren;
                    }

                    if (!needsRebuild(dataStore, setName, group.BaseName, childImages))
                    {
                        log?.Invoke("Result    : UpToDate", LogLevel.Info);
                        return FolderProcessResult.UpToDate;
                    }

                    childInfos = buildChildImageInfoList(
                        dataStore, setName, childImages);

                    if (childInfos.Count == 0)
                    {
                        log?.Invoke("Result    : NoChildren (no valid child data)", LogLevel.Info);
                        return FolderProcessResult.NoChildren;
                    }

                    SetInfo setInfo = dataStore.GetSetInfo(setName);
                    if (setInfo == null)
                        return FolderProcessResult.Error;

                    shardSize = setInfo.ShardSize;
                    blockSize = setInfo.BlockSize;
                    system = childImages.FirstOrDefault()?.System;
                }

                // Build outside the DataStore using block so the embedded file is not locked
                log?.Invoke($"Children  : {childInfos.Count}", LogLevel.Info);

                TmdAppFolderBuilder builder = new TmdAppFolderBuilder();
                builder.Build(
                    dedupeDirectory, setName, group.BaseName,
                    childInfos, group.SourceFolder,
                    shardSize, blockSize, system,
                    group.IsArchived, group.ArchiveFiles);

                log?.Invoke("Result    : Created", LogLevel.Info);

                return FolderProcessResult.Created;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Result    : Error - {ex.Message}", LogLevel.Error);
                return FolderProcessResult.Error;
            }
        }

        /// <summary>
        /// Processes a CueFolder group by querying the DataStore for child CUE/GDI images,
        /// checking staleness, and delegating to CueFolderBuilder.Build() when rebuild is needed.
        /// </summary>
        private FolderProcessResult processCueFolder(
            FolderGroupInfo group,
            string dedupeDirectory,
            string setName,
            Action<string, LogLevel> log)
        {
            try
            {
                // Output header (Title prefix so the host colours it)
                log?.Invoke(Log.Section, LogLevel.Info);
                log?.Invoke(InfoPrefix.Stamp(InfoPrefix.Title, $"[CueFolder/{group.SystemType}]  {group.BaseName}"), LogLevel.Info);
                log?.Invoke(Log.Divider, LogLevel.Info);

                // Collect the expected child source names from the group
                HashSet<string> childSourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (group.ChildSources != null)
                {
                    foreach (SourceFile child in group.ChildSources)
                        childSourceNames.Add(child.Name);
                }

                List<ChildImageInfo> childInfos;
                long shardSize;
                int blockSize;
                string system;

                using (DataStore dataStore = new DataStore(dedupeDirectory))
                {
                    // Query for child CUE/GDI images matching the group's child source names
                    List<ImageRecord> childImages = dataStore.ListAllImages(img =>
                        img.SetName == setName &&
                        !img.Removed &&
                        (img.Format == ImageFormat.Cue || img.Format == ImageFormat.Gdi) &&
                        childSourceNames.Contains(img.Name))
                        .OrderBy(img => img.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (childImages.Count == 0)
                    {
                        log?.Invoke("Result    : NoChildren (no child images in datastore)", LogLevel.Info);
                        return FolderProcessResult.NoChildren;
                    }

                    if (!cueFolderNeedsRebuild(dataStore, setName, group.BaseName, childImages.Count))
                    {
                        log?.Invoke("Result    : UpToDate", LogLevel.Info);
                        return FolderProcessResult.UpToDate;
                    }

                    childInfos = buildChildImageInfoList(
                        dataStore, setName, childImages);

                    if (childInfos.Count == 0)
                    {
                        log?.Invoke("Result    : NoChildren (no valid child data)", LogLevel.Info);
                        return FolderProcessResult.NoChildren;
                    }

                    SetInfo setInfo = dataStore.GetSetInfo(setName);
                    if (setInfo == null)
                        return FolderProcessResult.Error;

                    shardSize = setInfo.ShardSize;
                    blockSize = setInfo.BlockSize;
                    system = childImages.FirstOrDefault()?.System;
                }

                // Build outside the DataStore using block so the embedded file is not locked
                log?.Invoke($"Children  : {childInfos.Count}", LogLevel.Info);

                CueFolderBuilder builder = new CueFolderBuilder();
                builder.Build(
                    dedupeDirectory, setName, group.BaseName,
                    childInfos, group.SourceFolder,
                    shardSize, blockSize, system);

                log?.Invoke("Result    : Created", LogLevel.Info);

                return FolderProcessResult.Created;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Result    : Error - {ex.Message}", LogLevel.Error);
                return FolderProcessResult.Error;
            }
        }

        /// <summary>
        /// Checks whether an existing CueFolder is up to date by comparing
        /// its ifs entry count against the current child image count.
        /// </summary>
        private bool cueFolderNeedsRebuild(DataStore dataStore, string setName, string baseName, int currentChildCount)
        {
            ImageRecord existing = dataStore.ListAllImages(img =>
                img.SetName == setName &&
                !img.Removed &&
                img.Format == ImageFormat.CueFolder &&
                string.Equals(img.Name, baseName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (existing == null)
                return true;

            GlobalImageKey key = new GlobalImageKey(setName, existing.Id);
            FsYaml fsYaml = ReadFsYaml(dataStore, key);

            if (fsYaml == null)
                return true;

            int existingIfsCount = fsYaml.ImageFileSystems.Count;

            return existingIfsCount != currentChildCount;
        }

        /// <summary>
        /// Checks whether an existing TmdAppFolder is up to date by comparing
        /// its ifs entry count against the current child image file count.
        /// </summary>
        private bool needsRebuild(DataStore dataStore, string setName, string baseName, List<ImageRecord> childImages)
        {
            ImageRecord existing = dataStore.ListAllImages(img =>
                img.SetName == setName &&
                !img.Removed &&
                img.Format == ImageFormat.TmdAppFolder &&
                string.Equals(img.Name, baseName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (existing == null)
                return true;

            GlobalImageKey key = new GlobalImageKey(setName, existing.Id);
            FsYaml fsYaml = ReadFsYaml(dataStore, key);

            if (fsYaml == null)
                return true;

            int existingIfsCount = fsYaml.ImageFileSystems.Count;

            int currentChildFileCount = 0;
            foreach (ImageRecord child in childImages)
            {
                GlobalImageKey childKey = new GlobalImageKey(setName, child.Id);
                FsYaml childFs = ReadFsYaml(dataStore, childKey);

                if (childFs != null)
                {
                    currentChildFileCount += DataStore.CountFilesRecursive(childFs.FileSystems);
                }
            }

            return existingIfsCount != currentChildFileCount;
        }

        /// <summary>
        /// Builds the ChildImageInfo list by reading each child image's area metadata
        /// for ifs file names, falling back to filesystem.yaml if area metadata is unavailable.
        /// </summary>
        private List<ChildImageInfo> buildChildImageInfoList(DataStore dataStore, string setName, List<ImageRecord> childImages)
        {
            List<ChildImageInfo> result = new List<ChildImageInfo>();

            foreach (ImageRecord img in childImages)
            {
                GlobalImageKey key = new GlobalImageKey(setName, img.Id);
                string indexFileName = DataStore.ExtractIndexFileName(img.Name);
                List<ChildImageFile> childFiles = new List<ChildImageFile>();

                try
                {
                    using (IImageReader reader = dataStore.OpenImageReader(key))
                    {
                        foreach (AreaRecord area in reader.GetAreas())
                        {
                            string fileName = area.Metadata?.GetString(AreaValueType.FileName);
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                childFiles.Add(new ChildImageFile
                                {
                                    FileName = fileName,
                                    ImageId = img.Id,
                                    Size = area.Size,
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // Fall back to filesystem data if area metadata is unavailable
                    FsYaml childFsYaml = ReadFsYaml(dataStore, key);
                    if (childFsYaml?.FileSystems != null)
                    {
                        foreach (FsYamlNode fs in childFsYaml.FileSystems)
                            DataStore.CollectChildFiles(fs, img.Id, childFiles);
                    }
                }

                result.Add(new ChildImageInfo
                {
                    ImageId = img.Id,
                    ImageName = img.Name,
                    IndexFileName = indexFileName ?? "",
                    Files = childFiles,
                });
            }

            return result;
        }
    }
}
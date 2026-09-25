using NKitDataStore;
using NKitDataStore.Interfaces;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Nanook.NKit
{
    /// <summary>
    /// Standardises access to the local filesystem and archives
    /// </summary>
    internal class SourceFileSystem
    {
        private DirectoryInfo _path;
        private FileItem _archive;

        private ILogScope _log;

        public List<FileItem> Files { get; }

        public bool IsArchive => _archive != null;

        public static IEnumerable<FileItem> GetLocalFiles(FileMask mask, ILogScope log, CancellationToken? cancel)
        {
            SourceFileSystem sfs = new SourceFileSystem(new DirectoryInfo(mask.Path), log);
            return sfs.GetFiles(mask, cancel);
        }
        public static IEnumerable<FileItem> GetLocalArchiveFiles(FileMask mask, bool allowNonArchiveItems, ILogScope log, CancellationToken? cancel)
        {
            foreach (FileItem fi in GetLocalFiles(mask, log, cancel))
            {
                if (cancel?.IsCancellationRequested ?? false)
                    break;

                if (fi.IsArchive)
                {
                    SourceFileSystem sfsArc = new SourceFileSystem(fi, log);
                    foreach (FileItem arc in sfsArc.GetFiles(mask, cancel))
                        yield return arc;
                }
                else if (allowNonArchiveItems)
                    yield return fi;
            }
        }
        public static SourceFileSystemReader CreateReader(FileItem file, ILogScope log, CancellationToken? cancel)
        {
            if (file.IsArchived)
                return new SourceFileSystemReader(file.Archive, new List<FileItem> { file }, log, cancel);
            else
                return new SourceFileSystemReader(new DirectoryInfo(file.Path), new List<FileItem> { file }, log, cancel);
        }

        internal SourceFileSystem(ILogScope log)
        {
            this.Files = new List<FileItem>();
            _log = log;
        }

        public SourceFileSystem(IEnumerable<SourceFileItem> archiveParts, ILogScope log) : this(log)
        {
            foreach (SourceFileItem sf in archiveParts)
            {
                FileItem f = new FileItem(sf.Path + sf.FileName) { Size = sf.Size, Extension = sf.Extension, Type = FileItemType.Archive };
                _archive = f;
                //cache(f);
            }
        }

        public SourceFileSystem(FileItem archive, ILogScope log) : this(log)
        {
            _archive = archive;
        }

        public SourceFileSystem(DirectoryInfo path, ILogScope log) : this(log)
        {
            _path = path;
        }

        private void cache(FileItem file)
        {
            file.SourceOrder = this.Files.Count;
            this.Files.Add(file);
        }

        public List<FileItem> GetFiles(FileMask mask, CancellationToken? cancel) => GetFiles(new FileMask[] { mask }, cancel);

        /// <summary>
        /// Enum all files matching the mask, this.Files contains ALL files found (not just matching)
        /// </summary>
        public List<FileItem> GetFiles(FileMask[] masks, CancellationToken? cancel)
        {
            if (_archive == null && _path == null) //manual mode
                return this.Files;

            this.Files.Clear();

            if (_archive != null)
            {
                foreach (FileMask mask in masks)
                    addArchiveFiles(mask, a => cache(a), cancel);
            }
            else if (_path != null)
            {
                addLocalFiles(_path, masks, masks.Any(a => a.Recursive), a => cache(a), cancel);
            }

            return this.Files;
        }

        /// <summary>
        /// Unit testing method to add items to this.Files. Ensure file.DirectoryName is valid
        /// </summary>
        internal void AddFile(FileInfo file, long size, FileMask mask)
        {
            if (size == -1)
                size = file.Length;

            addFile(file.FullName, size, 0, mask, mask.IsMatch(file.FullName), a => cache(a));
        }

        public SourceFileSystemReader CreateReader(CancellationToken? cancel)
        {
            if (this.IsArchive)
                return new SourceFileSystemReader(_archive, this.Files, _log, cancel);
            else
                return new SourceFileSystemReader(_path, this.Files, _log, cancel);
        }

        private bool addLocalFiles(DirectoryInfo d, FileMask[] masks, bool recurse, Action<FileItem> addItem, CancellationToken? cancel)
        {
            try
            {
                if (d.Exists)
                {
                    foreach (FileInfo file in d.EnumerateFiles())
                    {
                        FileMask match = masks.FirstOrDefault(m => m.IsMatch(file.FullName));
                        if (match != null)
                            addFile(file.FullName, file.Length, 0, match, true, addItem);
                        else
                            addFile(file.FullName, file.Length, 0, masks[0], false, addItem);

                        if (cancel?.IsCancellationRequested ?? false)
                            return true;
                    }

                    if (recurse)
                    {
                        foreach (DirectoryInfo di in d.GetDirectories())
                        {
                            if (addLocalFiles(di, masks, recurse, addItem, cancel))
                                return true; //cancelled exit
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void addArchiveFiles(FileMask mask, Action<FileItem> addItem, CancellationToken? cancel)
        {
            try
            {
                if (!mask.IsMatch(_archive.PathFileName))
                    return;

                if (isDataStore(_archive))
                {
                    addDataStoreFiles(mask, addItem, cancel);
                    return;
                }

                using (IArchive archive = SourceFiles.ArchiveOpen(_archive)) //handles multipart archives
                {
                    foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        if (entry.IsEncrypted)
                            _log?.Info(() => $"Passworded Skipped: {_archive.PathFileName} - {entry.Key}");
                        else
                            addFile(entry.Key, entry.Size, entry.Crc, mask, mask.IsArcMatch(entry.Key), addItem);
                        if (cancel?.IsCancellationRequested ?? false)
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Info(() => $"Archive Failed to Open! [{_archive.PathFileName}] - {ex.Message} ");
            }
        }

        private void addDataStoreFiles(FileMask mask, Action<FileItem> addItem, CancellationToken? cancel)
        {
            string dataStoreFilePath = Path.GetFullPath(_archive.PathFileName);
            string dataStoreRoot = Path.GetDirectoryName(dataStoreFilePath) ?? throw new DirectoryNotFoundException($"DataStore path '{_archive.PathFileName}' does not have a parent directory.");
            string setName = Path.GetFileNameWithoutExtension(dataStoreFilePath);
            using (DataStore dataStore = new DataStore(dataStoreRoot))
            {
                List<ImageRecord> images = dataStore.ListImagesInSet(setName);
                // detect duplicate names within this set so we can make listed entry names unique
                HashSet<string> duplicateNames = images
                    .Where(i => i.Format != ImageFormat.TmdAppFolder && i.Format != ImageFormat.CueFolder)
                    .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (ImageRecord image in images)
                {
                    // Skip TmdAppFolder and CueFolder images — these are index-based aggregation
                    // containers that can't be processed through the normal pipeline.
                    if (image.Format == ImageFormat.TmdAppFolder || image.Format == ImageFormat.CueFolder)
                        continue;

                    string ext = getDataStoreEntryExtension(image.Format);
                    // For APP, CUE, and GDI images we present them as a folder named after the image (no extension)
                    string entryName = (image.Format == ImageFormat.App || image.Format == ImageFormat.Cue || image.Format == ImageFormat.Gdi)
                        ? image.Name
                        : image.Name + ext;
                    if (duplicateNames.Contains(image.Name))
                    {
                        // Append the image id in the unambiguous "{id}" marker to make each entry
                        // unique for datastore listings (see DataStoreAsIso.FormatDuplicateName).
                        entryName = Container.DataStoreAsIso.FormatDuplicateName(image.Name, image.Id) + ext;
                    }

                    SystemType imageSystem = Enum.TryParse(image.System, true, out SystemType parsedSystem)
                        ? parsedSystem
                        : SystemType.NotSet;

                    // For CUE/GDI folder entries, add the folder as a non-matching parent
                    // and enumerate only the index file and track files inside. The folder
                    // entry itself is not a processable image — only the .cue/.gdi index is.
                    // This mirrors how TmdApp folders work: the folder is a container, not an image.
                    if (image.Format == ImageFormat.Cue || image.Format == ImageFormat.Gdi)
                    {
                        addFile(entryName, Encoding.UTF8.GetByteCount(entryName), 0, mask, false, addItem, imageSystem);
                        addDataStoreFolderContents(dataStore, setName, image, entryName, mask, addItem, imageSystem);
                        if (cancel?.IsCancellationRequested ?? false)
                            return;
                    }
                    else
                    {
                        addFile(entryName, Encoding.UTF8.GetByteCount(entryName), 0, mask, mask.IsArcMatch(entryName), addItem, imageSystem);
                        if (cancel?.IsCancellationRequested ?? false)
                            return;
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates the contents of a CUE/GDI folder entry in the DataStore.
        /// Lists track files from FileName-tagged areas and index/auxiliary files from the loose file store.
        /// </summary>
        private void addDataStoreFolderContents(DataStore dataStore, string setName, ImageRecord image, string folderName, FileMask mask, Action<FileItem> addItem, SystemType imageSystem)
        {
            try
            {
                using (IImageReader reader = dataStore.OpenImageReader(new GlobalImageKey(setName, image.Id)))
                {
                    // 1. List all areas with AreaValueType.FileName metadata as track file entries
                    foreach (AreaRecord area in reader.GetAreas())
                    {
                        string fileName = area.Metadata?.GetString(AreaValueType.FileName);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            string trackEntryPath = folderName + "/" + fileName;
                            addFile(trackEntryPath, area.Size, area.Crc32, mask, mask.IsArcMatch(trackEntryPath), addItem, imageSystem, image.Id);
                        }
                    }

                    // 2. List non-system loose files from IImageReader.ListFiles() as index/auxiliary file entries.
                    // System files (filesystem.nkfs, filesystem.yaml) are metadata and should not
                    // be treated as processable images.
                    foreach (FileRecord file in reader.ListFiles())
                    {
                        if (file.IsSystem)
                            continue;

                        // Use the file name directly (e.g., "game.cue", "game.gdi")
                        string fileEntryPath = folderName + "/" + file.Name;
                        addFile(fileEntryPath, file.UncompressedSize > 0 ? file.UncompressedSize : file.Size, 0, mask, mask.IsArcMatch(fileEntryPath), addItem, imageSystem, image.Id);
                    }
                }
            }
            catch
            {
                // If we can't enumerate folder contents (e.g., corrupted image), skip silently.
                // The folder entry itself is still listed so the scanner can attempt to process it.
            }
        }

        private static bool isDataStore(FileItem item) => string.Equals(item?.Extension, DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase);

        private static string getDataStoreEntryExtension(ImageFormat format)
        {
            // APP, CUE, and GDI stored images are represented as folders (no extension)
            // so the scanner can enter them and discover their internal structure.
            // CueFolder is also empty but is skipped from listing entirely.
            if (format == ImageFormat.App)
                return string.Empty;
            if (format == ImageFormat.Cue)
                return string.Empty;
            if (format == ImageFormat.Gdi)
                return string.Empty;
            if (format == ImageFormat.CueFolder)
                return string.Empty;
            return format.GetFileExtension();
        }

        private void addFile(string pathFileName, long size, long crc, FileMask mask, bool isMatch, Action<FileItem> addItem, SystemType systemType = SystemType.NotSet, long? dataStoreImageId = null)
        {
            bool arc = _archive != null;
            string key = ((arc ? $"{_archive.PathFileName}//" : "") + Path.GetDirectoryName(pathFileName)).ToLower();

            FileItem f = new FileItem(pathFileName) { Size = size, Crc = crc, Mask = mask, IsMatch = isMatch, Parent = key, Archive = _archive, SystemType = systemType, DataStoreImageId = dataStoreImageId, IsFromDataStore = dataStoreImageId.HasValue };
            if (f.Populate() && isMatch /*|| f.IsSplit*/) //must be a match or be part of a split file
            {
                if (arc)
                    _log?.Trace(() => $"Mask [{mask.MaskInArc}] matched [{pathFileName}] in [{_archive.PathFileName}]");
                else
                    _log?.Trace(() => $"Mask [{mask.Mask}] matched: [{pathFileName}]");
            }
            addItem(f);
        }
    }
}
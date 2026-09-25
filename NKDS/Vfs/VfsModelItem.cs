using NKitDataStore;

namespace Nanook.NKit.Vfs
{
    internal class VfsModelItem
    {
        public string NameAsIso { get; internal set; }
        public string NameAsFolder { get; internal set; }
        public long ImageSize { get; internal set; }
        public string System { get; internal set; }
        public ImageRecord ImageRecord { get; internal set; }
        /// <summary>
        /// Index into VfsModel._allDataStores identifying which DataStore this item belongs to.
        /// Used for multi-DataStore mount support to route resource manager calls correctly.
        /// </summary>
        public int DataStoreIndex { get; internal set; }
        /// <summary>
        /// When multiple images with the same name within the same set are merged into
        /// a single VFS folder (e.g. WiiU APP duplicates), this list contains ALL merged
        /// image records. <see cref="ImageRecord"/> is the primary (first) image.
        /// Null or empty when the item represents a single non-merged image.
        /// </summary>
        public List<ImageRecord> MergedImageRecords { get; internal set; }
        /// <summary>
        /// True when this item is a secondary member of a merge group. Secondary items
        /// are hidden in handlers that merge duplicates but remain visible (with disambiguated
        /// names) in handlers that do not.
        /// </summary>
        public bool IsMergedSecondary { get; internal set; }
        /// <summary>
        /// Cached NkFs binary filesystem for this image, or null if not yet loaded or not stored.
        /// Mount handlers navigate this directly via GetChildren/ResolvePath/GetEntryName.
        /// Use <see cref="FileSystemNkfsLoaded"/> to distinguish "not loaded" from "doesn't exist".
        /// </summary>
        public NkFs FileSystemNkfs { get; internal set; }
        /// <summary>
        /// True once a lazy-load attempt has been made. When true and <see cref="FileSystemNkfs"/>
        /// is null, the image has no filesystem data stored.
        /// </summary>
        public bool FileSystemNkfsLoaded { get; internal set; }
        /// <summary>
        /// Per-filesystem-type NkFs instances for multi-filesystem images.
        /// Null when the image uses a single unified filesystem.nkfs.
        /// Keys are lowercase filesystem type names (e.g., "iso9660", "joliet", "udf").
        /// </summary>
        public Dictionary<string, NkFs> FileSystemNkfsPerType { get; internal set; }
        /// <summary>
        /// True when this image has multiple filesystem types (per-type nkfs files).
        /// </summary>
        public bool IsMultiFilesystem => FileSystemNkfsPerType != null && FileSystemNkfsPerType.Count > 1;
        /// <summary>
        /// Cached list of file records from the files table for this image, or null if not yet loaded.
        /// </summary>
        public List<StoredFileEntry> StoredFiles { get; internal set; }
        /// <summary>
        /// True once a lazy-load attempt has been made for stored files.
        /// </summary>
        public bool StoredFilesLoaded { get; internal set; }
    }
}
namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Describes a stored file entry from the files table for VFS display.
    /// </summary>
    internal class StoredFileEntry
    {
        public string Name { get; set; }
        public string StoredFileName { get; set; }
        public long Size { get; set; }
    }
}
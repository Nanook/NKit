using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Describes a group of source files from the same directory that should
    /// produce a synthetic folder image after all child images are processed.
    /// </summary>
    internal class FolderGroupInfo
    {
        /// <summary>
        /// The type of folder image to create.
        /// </summary>
        public FolderGroupType GroupType { get; set; }

        /// <summary>
        /// The base name for the folder image (typically the directory name).
        /// This becomes the TmdAppFolder image name.
        /// </summary>
        public string BaseName { get; set; }

        /// <summary>
        /// The full path to the source folder on disk, or the archive path
        /// when the source files are inside an archive.
        /// </summary>
        public string SourceFolder { get; set; }

        /// <summary>
        /// The child SourceFiles that belong to this group (e.g., tmd.0, tmd.1, tmd.2).
        /// Used for ordering: the synthetic source must come after all of these.
        /// </summary>
        public List<SourceFile> ChildSources { get; set; }

        /// <summary>
        /// The system type inherited from child sources (e.g., SystemType.WiiU).
        /// </summary>
        public SystemType SystemType { get; set; }

        /// <summary>
        /// True if the child sources are inside an archive (zip, rar, 7z, etc.).
        /// When true, SourceFolder is the archive path and ArchiveFiles contains
        /// the archive file items needed to open it.
        /// </summary>
        public bool IsArchived { get; set; }

        /// <summary>
        /// The archive file items from the first child source, used to open the
        /// archive for enumerating extra files. Null when IsArchived is false.
        /// </summary>
        public SourceFileItem[] ArchiveFiles { get; set; }
        /// <summary>
        /// True when this group is a WUA file or Loadiine folder — sources that are fed
        /// through <see cref="Containers.FolderFilesAsIso"/> and
        /// <see cref="Formats.FolderFiles.FolderFilesImage"/> as <c>AreaType.Other</c>
        /// sections rather than as a disc image with partition tables and an FST.
        ///
        /// Currently true for WuaFolder and LoadiineFolder. Adding a new folder-format
        /// group type here is the only change needed in core routing code.
        /// </summary>
        public bool IsFolderFormat =>
            GroupType == FolderGroupType.WuaFolder ||
            GroupType == FolderGroupType.LoadiineFolder;
    }

    public enum FolderGroupType
    {
        /// <summary>WiiU TmdApp folder: multiple tmd.X files in the same directory.</summary>
        TmdAppFolder,

        /// <summary>Future: CUE sheet folder with multiple .cue files.</summary>
        CueFolder,

        /// <summary>Future: GDI folder with multiple .gdi files.</summary>
        GdiFolder,

        /// <summary>WiiU ZArchive (.wua): a single file containing a Loadiine game folder.</summary>
        WuaFolder,

        /// <summary>WiiU Loadiine folder on disk: a directory containing code/ + content/ or meta/ subfolders.</summary>
        LoadiineFolder,
    }
}
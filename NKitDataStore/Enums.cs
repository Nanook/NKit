namespace NKitDataStore
{
    /// <summary>
    /// Defines the type of data stored in a block or an offset run.
    /// </summary>
    public enum BlockType
    {
        /// <summary>
        /// Other verifiable virtual data.
        /// </summary>
        Other = 0,

        /// <summary>
        /// Standard file data.
        /// </summary>
        File = 1,

        /// <summary>
        /// Format-specific data such as partition headers, volume descriptors, and other structural metadata.
        /// </summary>
        FormatData = 2,

        /// <summary>
        /// Filesystem-related blocks such as directory tables, file allocation tables, and filesystem metadata.
        /// </summary>
        FileSystem = 3,

        /// <summary>
        /// Block padding data (e.g., ISO sector headers, ECC data, or other format-specific padding).
        /// </summary>
        BlockPadding = 4,

        /// <summary>
        /// Verifiable junk data, algorithmically generated (e.g., NJunk).
        /// </summary>
        NJunk = 5,

        /// <summary>
        /// Xbox filler data (verifiable random padding pattern).
        /// </summary>
        XFiller = 6,
    }

    /// <summary>
    /// Defines the saved/output format of an image.
    /// Used to derive the default file extension.
    /// </summary>
    public enum ImageFormat
    {
        Unknown = 0,
        Iso = 1,
        Bin = 2,
        App = 3,
        Cdn = 4,
        Gdi = 5,
        Folder = 6,
        TmdAppFolder = 7,
        Cue = 8,
        CueFolder = 9,
        Chd = 10,
    }

    public static class ImageFormatExtensions
    {
        public static string GetFileExtension(this ImageFormat format) => format switch
        {
            ImageFormat.Unknown => ".iso",
            ImageFormat.Iso => ".iso",
            ImageFormat.Bin => ".bin",
            ImageFormat.App => ".app",
            ImageFormat.Cdn => ".cdn",
            ImageFormat.Gdi => ".gdi",
            ImageFormat.Cue => ".cue",
            ImageFormat.Chd => ".chd",
            ImageFormat.Folder => "",
            ImageFormat.TmdAppFolder => "",
            ImageFormat.CueFolder => "",
            _ => ".iso",
        };
    }

    /// <summary>
    /// Defines the compression algorithm used for a block.
    /// </summary>
    public enum CompressionType
    {
        /// <summary>
        /// The block is not compressed.
        /// </summary>
        None = 0,

        /// <summary>
        /// The block is compressed with Zstandard.
        /// </summary>
        Zstd = 1,
    }

    /// <summary>
    /// Defines the gaming console or disc system.
    /// </summary>
    public enum System
    {
        /// <summary>
        /// Default/catch-all system including standard ISO9660 images.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Nintendo GameCube.
        /// </summary>
        GameCube = 1,

        /// <summary>
        /// Nintendo Wii.
        /// </summary>
        Wii = 2,

        /// <summary>
        /// Nintendo Wii U.
        /// </summary>
        WiiU = 3,

        /// <summary>
        /// Sony PlayStation 1.
        /// </summary>
        PS1 = 4,

        /// <summary>
        /// Sony PlayStation 2.
        /// </summary>
        PS2 = 5,

        /// <summary>
        /// Sony PlayStation 3.
        /// </summary>
        PS3 = 6,

        /// <summary>
        /// Sony PlayStation Portable.
        /// </summary>
        PSP = 7,

        /// <summary>
        /// Microsoft Xbox.
        /// </summary>
        Xbox = 8,

        /// <summary>
        /// Microsoft Xbox 360.
        /// </summary>
        Xbox360 = 9,

        /// <summary>
        /// Sega Dreamcast.
        /// </summary>
        Dreamcast = 10,

        /// <summary>
        /// Sega Saturn.
        /// </summary>
        Saturn = 11,

        /// <summary>
        /// Sega CD / Mega CD.
        /// </summary>
        SegaCD = 12,

        /// <summary>
        /// Philips CD-i (Compact Disc Interactive).
        /// </summary>
        CDi = 13,

        /// <summary>
        /// NEC PC Engine / TurboGrafx-CD.
        /// </summary>
        PCEngine = 14,

        /// <summary>
        /// Generic directory storage (non-disc-image folders).
        /// </summary>
        Directories = 15,
        Diagnostics = 16,
    }

    /// <summary>
    /// Defines the type of area value stored in the area_val table.
    /// Area values provide additional attributes and metadata for disc image areas.
    /// </summary>
    public enum AreaValueType
    {
        FileName = 0,
        FileTimeStamp = 1,
        FileAttributes = 2,
        App = 3,
        AreaOffsetBase = 4,
        BlockSize = 5,
        CommonKeyCrc = 6,
        ContentHeaders = 7,
        ContentIndex = 8,
        ContentSha = 9,
        DecryptionValid = 10,
        DiscNo = 11,
        Duration = 12,
        Partition = 13,
        Encrypted = 14,
        FsType = 15,
        HasFileSystem = 16,
        HashRoot = 17,
        HashSize = 18,
        Hashes = 19,
        HeaderCrc = 20,
        HeaderDate = 21,
        HeaderSize = 22,
        HeaderXxHash = 23,
        ID = 24,
        JunkEndNullsOffset = 25,
        JunkID = 26,
        JunkLeadingNulls = 27,
        KeyCrc = 28,
        MissingFiles = 29,
        PartitionType = 30,
        Partitions = 31,
        PhysicalOffset = 32,
        PvdSectorCount = 33,
        Region = 34,
        RepeatedContentIndex = 35,
        Revision = 36,
        Session = 37,
        SessionOffsetBase = 38,
        Signed = 39,
        SystemDataCrc = 40,
        ThreeKey = 41,
        Title = 42,
        TitleId = 43,
        TitleKeyCrc = 44,
        TitleKeyMissing = 45,
        TmdVersion = 46,
        Track = 47,
        UpdatePartitionRemoved = 48,
        Version = 49,
        VolumeId = 50,

        /// <summary>
        /// File type/extension of an auxiliary file (e.g., "cue", "m3u", "gdi", "sfv").
        /// Used for files stored at negative offsets.
        /// </summary>
        AuxFileType = 51,

        /// <summary>
        /// Original filename of an auxiliary file (e.g., "game.cue", "playlist.m3u").
        /// Used for files stored at negative offsets.
        /// </summary>
        AuxFileName = 52,
        Type = 53,
        // Note: enum additions require restarting the running app to take effect.
        /// <summary>
        /// Base64-encoded title key for a content/partition (sensitive) - stored so builders can reapply encryption.
        /// </summary>
        TitleKey = 54,

        /// <summary>
        /// Pipe-delimited hex SeekIV values for hashless encrypted FileSystem sections.
        /// Each entry is a 32-char hex string (16-byte IV) or empty. Only present for
        /// Game partition content that is encrypted without hashes.
        /// </summary>
        SeekIv = 55,

        /// <summary>
        /// The original source folder path from which TMD files were ingested.
        /// Used by the finalization pass to discover real files (tmd, tik, cetk, h3)
        /// for the TmdAppFolder's fs section.
        /// </summary>
        SourceFolder = 56,

        /// <summary>
        /// Number of leading zero bytes trimmed from an audio partition during ingestion.
        /// Stored as a long. Absence means no trimming (backward compatible).
        /// </summary>
        TrimOffset = 57,
    }
}
namespace Nanook.NKit
{
    public enum PartitionType { Update = 1, Game = 0, Channel = 2, GameData = 3, Other, Si, GameUpdate }

    public enum AreaType { ImageHeader, PartitionTable, PartitionHeader, FstBlock, FileSystem, Other, None, Audio, RawKeyMissing }

    public enum ImageType { GameCube, Wii, WiiU, Iso9660, XBox }

    public enum ChecksumType { Crc32, Sha1, Md5, XxHash }

    public enum SystemType { NotSet, Default, Dreamcast, GameCube, PcEngine, PS1, PS2, PS3, PSP, Saturn, SegaCD, Wii, WiiU, XBox, XBox360, CDi }

    public enum CdSubType { None, Normal, Raw }

    public enum CdType { Cdxa, Cdda, Cdrom }

    public enum MediaType { Unknown, HD, CD, GD, DVD, AV, GC, WII, WUD, Disc, CDN, APP }


    public enum TaskType { NotSet, Scan, Convert, Expand, Fix, FixExtract, Dedupe, Extract, Verify, Wipe }

    public enum ContainerType { Unknown, DecIso, IsoDec, Wbfs, Ciso, Wia, Rvz, Wux, Wud, Iso, Gcz, Cso, Zso, Dax, Jso, Chd, Cue, Toc, Gdi, TmdApp, Ps3Jb }

    public enum OutputType { None, Scan, Image, FolderIndex, FolderFiles, Files, FileStore }

    public enum ReadMode { Read, Fix } //defaults to read

    public enum KeyMode { File, Files }

    public enum Verify { Y, N, DatLookup }
    public enum VerifyMethod { NoVerify, InChecksums, DataStore, DatLookup, DatMatch, ScanCompare, InScanCompare }

    public enum Region { Japan = 0, Usa = 1, Pal = 2, Korea = 4 }
    public enum VerifyResult { Unverified = 0, VerifySuccess = 1, VerifyFailed = 2, Error = 3 }

    public enum SkipType { None, Skip, End }
    internal enum FsType //order with least important to most important
    {
        ElTorito, //the items this block points to are least important. Use the FS names
        Iso9660,
        Romeo, //prefer over Iso
        RockRidge, //this is really an extension not a PVD Type
        Joliet, //prefer over Romeo
        System,
        Cdi,
        Cdxa,
        Udf,
        Other //used as a catch all for Nintendo as there's only 1 FsType per ImageType
    }

    internal interface IImage
    {
        void Setup();
        ImageType Type { get; }
        long Size { get; }
        AreaType Read(IBuffer buffer, out IFileSystemInfo fsInfo);

        // The image offset at which the CURRENT area ends (i.e. where the next area begins), as known
        // to the image after the most recent Read(). The current area's size is therefore
        // (CurrentAreaEndImageOffset - <current area ImageOffset>). Used by the NKitCore section
        // factory to size each core area as its first section is produced (areas are discovered as
        // reading progresses, but the extent is known once the area's first section is returned).
        long CurrentAreaEndImageOffset { get; }
        IBufferPreProcessor GetPreProcessor(); //one of these for full image

        ISectionProcessor CreateSectionProcessor(); //multiple are created to be used in parallel in rotation
        int SectionSize { get; }
        ISectionProcessor PatchSection(ScanSection section);
        IBuffer CreateBuffer();
        SystemType SystemType { get; }
        void SetScanProperties();
    }

}
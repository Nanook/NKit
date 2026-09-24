using System;

namespace Nanook.NKit.Iso.Iso9660
{
    [Flags]
    internal enum UdfFileCharacteristic : byte
    {
        Existence = 0x01,
        Directory = 0x02,
        Deleted = 0x04,
        Parent = 0x08,
        Metadata = 0x10
    }

    internal enum UdfShortAllocationFlags
    {
        RecordedAndAllocated = 0,
        AllocatedNotRecorded = 1,
        NotRecordedNotAllocated = 2,
        NextExtentOfAllocationDescriptors = 3
    }

    internal enum UdfIntegrityType : byte
    {
        Open = 0,
        Close = 1
    }
    internal enum UdfOsClass : byte
    {
        None = 0,
        Dos = 1,
        OS2 = 2,
        Macintosh = 3,
        Unix = 4,
        Windows9x = 5,
        WindowsNt = 6,
        Os400 = 7,
        BeOS = 8,
        WindowsCe = 9
    }

    internal enum UdfOsId : ushort
    {
        DosOrWindows3 = 0x0100,
        Os2 = 0x0200,
        MacintoshOs9 = 0x0300,
        MacintoshOsX = 0x0301,
        UnixGeneric = 0x0400,
        UnixAix = 0x0401,
        UnixSunOS = 0x0402,
        UnixHPUX = 0x0403,
        UnixIrix = 0x0404,
        UnixLinux = 0x0405,
        UnixMkLinux = 0x0406,
        UnixFreeBsd = 0x0407,
        UnixNetBsd = 0x0408,
        Windows9x = 0x0500,
        WindowsNt = 0x0600,
        Os400 = 0x0700,
        BeOS = 0x0800,
        WindowsCe = 0x0900
    }

    internal enum UdfCharacterSetType : byte
    {
        CharacterSet0 = 0,
        CharacterSet1 = 1,
        CharacterSet2 = 2,
        CharacterSet3 = 3,
        CharacterSet4 = 4,
        CharacterSet5 = 5,
        CharacterSet6 = 6,
        CharacterSet7 = 7,
        CharacterSet8 = 8
    }

    internal enum UdfTagId : ushort
    {
        None = 0x0000,
        PrimaryVolumeDescriptor = 0x0001,
        AnchorVolumeDescriptorPointer = 0x0002,
        VolumeDescriptorPointer = 0x0003,
        ImplementationUseVolumeDescriptor = 0x0004,
        PartitionDescriptor = 0x0005,
        LogicalVolumeDescriptor = 0x0006,
        UnallocatedSpaceDescriptor = 0x0007,
        TerminatingDescriptor = 0x0008,
        LogicalVolumeIntegrityDescriptor = 0x0009,

        FileSetDescriptor = 0x0100,
        FileIdentifierDescriptor = 0x0101,
        AllocationExtentDescriptor = 0x0102,
        IndirectEntry = 0x0103,
        TerminalEntry = 0x0104,
        FileEntry = 0x0105,
        ExtendedAttributeHeaderDescriptor = 0x0106,
        UnallocatedSpaceEntry = 0x0107,
        SpaceBitmapDescriptor = 0x0108,
        PartitionIntegrityEntry = 0x0109,
        ExtendedFileEntry = 0x0110
    }

    [Flags]
    internal enum UdfInformationControlBlockFlags
    {
        DirectorySorted = 0x0004,
        NonRelocatable = 0x0008,
        Archive = 0x0010,
        SetUid = 0x0020,
        SetGid = 0x0040,
        Sticky = 0x0080,
        Contiguous = 0x0100,
        System = 0x0200,
        Transformed = 0x0400,
        MultiVersions = 0x0800,
        Stream = 0x1000
    }

    internal enum UdfFileType : byte
    {
        None = 0,
        UnallocatedSpaceEntry = 1,
        PartitionIntegrityEntry = 2,
        IndirectEntry = 3,
        Directory = 4,
        RandomBytes = 5,
        SpecialBlockDevice = 6,
        SpecialCharacterDevice = 7,
        ExtendedAttributes = 8,
        Fifo = 9,
        Socket = 10,
        TerminalEntry = 11,
        SymbolicLink = 12,
        StreamDirectory = 13,

        UdfVirtualAllocationTable = 248,
        UdfRealTimeFile = 249,
        UdfMetadataFile = 250,
        UdfMetadataMirrorFile = 251,
        UdfMetadataBitmapFile = 252
    }

    internal enum UdfAllocationType
    {
        ShortDescriptors = 0,
        LongDescriptors = 1,
        ExtendedDescriptors = 2,
        Embedded = 3
    }

    [Flags]
    internal enum UdfFilePermissions
    {
        /// <summary>
        /// No permissions.
        /// </summary>
        None = 0,

        /// <summary>
        /// Any user execute permission.
        /// </summary>
        OthersExecute = 0x0001,

        /// <summary>
        /// Any user write permission.
        /// </summary>
        OthersWrite = 0x0002,

        /// <summary>
        /// Any user read permission.
        /// </summary>
        OthersRead = 0x0004,

        /// <summary>
        /// Any user change attributes permission.
        /// </summary>
        OthersChangeAttributes = 0x0008,

        /// <summary>
        /// Any user delete permission.
        /// </summary>
        OthersDelete = 0x0010,

        /// <summary>
        /// Group execute permission.
        /// </summary>
        GroupExecute = 0x0020,

        /// <summary>
        /// Group write permission.
        /// </summary>
        GroupWrite = 0x0040,

        /// <summary>
        /// Group read permission.
        /// </summary>
        GroupRead = 0x0080,

        /// <summary>
        /// Group change attributes permission.
        /// </summary>
        GroupChangeAttributes = 0x0100,

        /// <summary>
        /// Group delete permission.
        /// </summary>
        GroupDelete = 0x0200,

        /// <summary>
        /// Owner execute permission.
        /// </summary>
        OwnerExecute = 0x0400,

        /// <summary>
        /// Owner write permission.
        /// </summary>
        OwnerWrite = 0x0800,

        /// <summary>
        /// Owner read permission.
        /// </summary>
        OwnerRead = 0x1000,

        /// <summary>
        /// Owner change attributes permission.
        /// </summary>
        OwnerChangeAttributes = 0x2000,

        /// <summary>
        /// Owner delete permission.
        /// </summary>
        OwnerDelete = 0x4000
    }

    internal class UdfExtentDescriptor
    {
        public uint Length { get; private set; }
        public uint Location { get; private set; }
        public int Size => 0x8;
        public static UdfExtentDescriptor Parse(byte[] data, int offset)
        {
            return new UdfExtentDescriptor()
            {
                Length = data.ReadUInt32L(offset + 0x0),
                Location = data.ReadUInt32L(offset + 0x4)
            };
        }
    }

    internal class UdfCharacterSetSpecification
    {
        public byte[] Information { get; private set; }
        public UdfCharacterSetType Type { get; private set; }
        public int Size => 0x40;
        public static UdfCharacterSetSpecification Parse(byte[] data, int offset)
        {
            return new UdfCharacterSetSpecification()
            {
                Type = (UdfCharacterSetType)data.Read8(offset + 0x0),
                Information = data.Read(offset + 0x1, 0x3f)
            };
        }
    }

    internal struct UdfLogicalBlockAddress
    {
        public uint LogicalBlock { get; private set; }
        public ushort Partition { get; private set; }
        public int Size => 0x6;

        public static UdfLogicalBlockAddress Parse(byte[] buffer, int offset)
        {
            return new UdfLogicalBlockAddress()
            {
                LogicalBlock = buffer.ReadUInt32L(offset + 0x0),
                Partition = buffer.ReadUInt16L(offset + 0x4)
            };
        }

        public override string ToString() => LogicalBlock + ",p" + Partition;
    }

}
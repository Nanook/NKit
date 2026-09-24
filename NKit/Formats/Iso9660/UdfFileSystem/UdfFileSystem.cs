using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class UdfFileEntry
    {
        public DateTime AccessTime { get; protected set; }
        public byte[] AllocationDescriptors { get; protected set; }
        public int AllocationDescriptorsLength { get; protected set; }
        public DateTime AttributeTime { get; protected set; }
        public uint Checkpoint { get; protected set; }
        public UdfDescriptorTag DescriptorTag { get; protected set; }
        public UdfLongAllocationDescriptor ExtendedAttributeIcb { get; protected set; }
        public List<UdfExtendedAttributeRecord> ExtendedAttributes { get; protected set; }
        public int ExtendedAttributesLength { get; protected set; }
        public ushort FileLinkCount { get; protected set; }
        public uint Gid { get; protected set; }
        public UdfImplementationEntityId ImplementationIdentifier { get; protected set; }
        public UdfInformationControlBlock InformationControlBlock { get; protected set; }
        public ulong InformationLength { get; protected set; }
        public ulong LogicalBlocksRecorded { get; protected set; }
        public DateTime ModificationTime { get; protected set; }
        public UdfFilePermissions Permissions { get; protected set; }
        public byte RecordDisplayAttributes { get; protected set; }
        public byte RecordFormat { get; protected set; }
        public uint RecordLength { get; protected set; }
        public uint Uid { get; protected set; }
        public ulong UniqueId { get; protected set; }

        public virtual int Size => 0xb0 + ExtendedAttributesLength + AllocationDescriptorsLength;

        public static UdfFileEntry Parse(byte[] buffer, int offset)
        {
            UdfFileEntry obj = new UdfFileEntry()
            {
                DescriptorTag = UdfDescriptorTag.Parse(buffer, offset),
                InformationControlBlock = UdfInformationControlBlock.Parse(buffer, offset + 0x10),
                Uid = buffer.ReadUInt32L(offset + 0x24),
                Gid = buffer.ReadUInt32L(offset + 0x28),
                Permissions = (UdfFilePermissions)buffer.ReadUInt32L(offset + 0x2c),
                FileLinkCount = buffer.ReadUInt16L(offset + 0x30),
                RecordFormat = buffer[offset + 0x32],
                RecordDisplayAttributes = buffer[offset + 0x33],
                RecordLength = buffer.ReadUInt16L(offset + 0x34),
                InformationLength = buffer.ReadUInt64L(offset + 0x38),
                LogicalBlocksRecorded = buffer.ReadUInt64L(offset + 0x40),
                AccessTime = UdfUtils.ParseTimestamp(buffer, offset + 0x48),
                ModificationTime = UdfUtils.ParseTimestamp(buffer, offset + 0x54),
                AttributeTime = UdfUtils.ParseTimestamp(buffer, offset + 0x60),
                Checkpoint = buffer.ReadUInt32L(offset + 0x6c),
                ExtendedAttributeIcb = UdfLongAllocationDescriptor.Parse(buffer, offset + 0x70),
                ImplementationIdentifier = UdfImplementationEntityId.Parse(buffer, offset + 0x80),
                UniqueId = buffer.ReadUInt64L(offset + 0xa0),
                ExtendedAttributesLength = (int)buffer.ReadUInt32L(offset + 0xa8),
                AllocationDescriptorsLength = (int)buffer.ReadUInt32L(offset + 0xac)
            };
            obj.ExtendedAttributes = ReadExtendedAttributes(buffer.Read(offset + 0xb0, obj.ExtendedAttributesLength));
            obj.AllocationDescriptors = buffer.Read(offset + 0xb0 + obj.ExtendedAttributesLength, obj.AllocationDescriptorsLength);
            return obj;
        }

        protected static List<UdfExtendedAttributeRecord> ReadExtendedAttributes(byte[] eaData)
        {
            if (eaData != null && eaData.Length != 0)
            {
                UdfDescriptorTag eaTag = UdfDescriptorTag.Parse(eaData, 0);

                int implAttrLocation = (int)eaData.ReadUInt32L(0x10);
                int appAttrLocation = (int)eaData.ReadUInt32L(0x14);

                List<UdfExtendedAttributeRecord> extendedAttrs = new List<UdfExtendedAttributeRecord>();
                int pos = 0x18;
                while (pos < eaData.Length)
                {
                    UdfExtendedAttributeRecord ea;

                    if (implAttrLocation != -1 && pos >= implAttrLocation)
                        ea = UdfImplementationUseExtendedAttRecord.Parse(eaData, pos);
                    else
                        ea = UdfExtendedAttributeRecord.Parse(eaData, pos);

                    extendedAttrs.Add(ea);

                    pos += ea.Size;
                }

                return extendedAttrs;
            }
            return null;
        }
    }

    internal class UdfExtendedFileEntry : UdfFileEntry
    {
        public DateTime CreationTime { get; private set; }
        public ulong ObjectSize { get; private set; }
        public UdfLongAllocationDescriptor StreamDirectoryIcb { get; private set; }

        public override int Size => 216 + ExtendedAttributesLength + AllocationDescriptorsLength;

        public static new UdfExtendedFileEntry Parse(byte[] buffer, int offset)
        {
            UdfExtendedFileEntry obj = new UdfExtendedFileEntry()
            {
                DescriptorTag = UdfDescriptorTag.Parse(buffer, offset),
                InformationControlBlock = UdfInformationControlBlock.Parse(buffer, offset + 0x10),
                Uid = buffer.ReadUInt32L(offset + 0x24),
                Gid = buffer.ReadUInt32L(offset + 0x28),
                Permissions = (UdfFilePermissions)buffer.ReadUInt32L(offset + 0x2c),
                FileLinkCount = buffer.ReadUInt16L(offset + 0x30),
                RecordFormat = buffer[offset + 0x32],
                RecordDisplayAttributes = buffer[offset + 0x33],
                RecordLength = buffer.ReadUInt16L(offset + 0x34),
                InformationLength = buffer.ReadUInt64L(offset + 0x38),
                ObjectSize = buffer.ReadUInt64L(offset + 0x40),
                LogicalBlocksRecorded = buffer.ReadUInt64L(offset + 0x48),
                AccessTime = UdfUtils.ParseTimestamp(buffer, offset + 0x50),
                ModificationTime = UdfUtils.ParseTimestamp(buffer, offset + 0x5c),
                CreationTime = UdfUtils.ParseTimestamp(buffer, offset + 0x68),
                AttributeTime = UdfUtils.ParseTimestamp(buffer, offset + 0x74),
                Checkpoint = buffer.ReadUInt32L(offset + 0x80),
                ExtendedAttributeIcb = UdfLongAllocationDescriptor.Parse(buffer, offset + 0x88),
                StreamDirectoryIcb = UdfLongAllocationDescriptor.Parse(buffer, offset + 0x98),
                ImplementationIdentifier = UdfImplementationEntityId.Parse(buffer, offset + 0xa8),
                UniqueId = buffer.ReadUInt64L(offset + 0xc8),
                ExtendedAttributesLength = (int)buffer.ReadUInt32L(offset + 0xd0),
                AllocationDescriptorsLength = (int)buffer.ReadUInt32L(offset + 0xd4),
            };
            obj.AllocationDescriptors = buffer.Read(offset + 0xd8 + obj.ExtendedAttributesLength, obj.AllocationDescriptorsLength);

            byte[] eaData = buffer.Read(offset + 0xd8, obj.ExtendedAttributesLength);
            obj.ExtendedAttributes = ReadExtendedAttributes(eaData);
            return obj;
        }
    }

    internal class UdfExtendedAttributeRecord
    {
        public byte[] AttributeData { get; private set; }
        public byte AttributeSubType { get; private set; }
        public uint AttributeType { get; private set; }

        public int Size => 0xc + AttributeData.Length;
        public static UdfExtendedAttributeRecord Parse(byte[] buffer, int offset)
        {
            UdfExtendedAttributeRecord obj = new UdfExtendedAttributeRecord();
            Populate(obj, buffer, offset);
            return obj;
        }

        public static void Populate(UdfExtendedAttributeRecord record, byte[] buffer, int offset)
        {
            int dataLength = (int)buffer.ReadUInt32L(offset + 0x8) - 0xc;
            record.AttributeType = buffer.ReadUInt32L(offset + 0x0);
            record.AttributeSubType = buffer[offset + 0x4];
            record.AttributeData = buffer.Read(offset + 0xc, dataLength);
        }
    }

    internal class UdfInformationControlBlock
    {
        public UdfAllocationType AllocationType { get; private set; }
        public UdfFileType FileType { get; private set; }
        public UdfInformationControlBlockFlags Flags { get; private set; }
        public ushort MaxEntries { get; private set; }
        public UdfLogicalBlockAddress ParentICBLocation { get; private set; }
        public uint PriorDirectEntries { get; private set; }
        public ushort StrategyParameter { get; private set; }
        public ushort StrategyType { get; private set; }

        public int Size => 0x14;
        public static UdfInformationControlBlock Parse(byte[] buffer, int offset)
        {
            ushort flagsField = buffer.ReadUInt16L(offset + 18);
            return new UdfInformationControlBlock()
            {
                PriorDirectEntries = buffer.ReadUInt32L(offset),
                StrategyType = buffer.ReadUInt16L(offset + 0x4),
                StrategyParameter = buffer.ReadUInt16L(offset + 0x6),
                MaxEntries = buffer.ReadUInt16L(offset + 0x8),
                FileType = (UdfFileType)buffer[offset + 0xb],
                ParentICBLocation = UdfLogicalBlockAddress.Parse(buffer, offset + 0xc),
                AllocationType = (UdfAllocationType)(flagsField & 0x3),
                Flags = (UdfInformationControlBlockFlags)(flagsField & 0xFFFC)
            };
        }
    }

    internal sealed class UdfImplementationUseExtendedAttRecord : UdfExtendedAttributeRecord
    {
        public UdfImplementationEntityId ImplementationIdentifier;
        public byte[] ImplementationUseData;

        public static new UdfImplementationUseExtendedAttRecord Parse(byte[] buffer, int offset)
        {
            UdfImplementationUseExtendedAttRecord obj = new UdfImplementationUseExtendedAttRecord();
            UdfExtendedAttributeRecord.Populate(obj, buffer, offset);

            int iuSize = (int)buffer.ReadUInt32L(offset + 0xc);

            obj.ImplementationIdentifier = UdfImplementationEntityId.Parse(buffer, offset + 0x10);

            obj.ImplementationUseData = buffer.Read(offset + 0x30, iuSize);

            return obj;
        }
    }

    internal class UdfFile
    {
        protected uint _blockSize { get; private set; }
        protected UdfFileEntry _fileEntry { get; private set; }
        protected UdfVolume _partition { get; private set; }

        public UdfFile(UdfVolume partition, UdfFileEntry fileEntry, uint blockSize)
        {
            _partition = partition;
            _fileEntry = fileEntry;
            _blockSize = blockSize;
        }

        public List<UdfExtendedAttributeRecord> ExtendedAttributes => _fileEntry.ExtendedAttributes;

        public DateTime LastAccessTimeUtc => _fileEntry.AccessTime;

        public DateTime LastWriteTimeUtc => _fileEntry.ModificationTime;

        public DateTime CreationTimeUtc
        {
            get
            {
                UdfExtendedFileEntry efe = _fileEntry as UdfExtendedFileEntry;
                if (efe != null)
                    return efe.CreationTime;
                return LastWriteTimeUtc;
            }
        }

        public FileAttributes FileAttributes
        {
            get
            {
                FileAttributes attribs = 0;
                UdfInformationControlBlockFlags flags = _fileEntry.InformationControlBlock.Flags;

                if (_fileEntry.InformationControlBlock.FileType == UdfFileType.Directory)
                {
                    attribs |= FileAttributes.Directory;
                }
                else if (_fileEntry.InformationControlBlock.FileType == UdfFileType.Fifo
                         || _fileEntry.InformationControlBlock.FileType == UdfFileType.Socket
                         || _fileEntry.InformationControlBlock.FileType == UdfFileType.SpecialBlockDevice
                         || _fileEntry.InformationControlBlock.FileType == UdfFileType.SpecialCharacterDevice
                         || _fileEntry.InformationControlBlock.FileType == UdfFileType.TerminalEntry)
                    attribs |= FileAttributes.Device;

                if ((flags & UdfInformationControlBlockFlags.Archive) != 0)
                    attribs |= FileAttributes.Archive;

                if ((flags & UdfInformationControlBlockFlags.System) != 0)
                    attribs |= FileAttributes.System | FileAttributes.Hidden;

                if ((int)attribs == 0)
                    attribs = FileAttributes.Normal;

                return attribs;
            }
        }

        public long FileLength => (long)_fileEntry.InformationLength;
    }

    internal class UdfFileId
    {
        public UdfDescriptorTag DescriptorTag { get; private set; }
        public UdfFileCharacteristic FileCharacteristics { get; private set; }
        public UdfLongAllocationDescriptor FileLocation { get; private set; }
        public ushort FileVersionNumber { get; private set; }
        public byte[] ImplementationUse { get; private set; }
        public ushort ImplementationUseLength { get; private set; }
        public string Name { get; private set; }
        public byte NameLength { get; private set; }

        public DateTime CreationTimeUtc => throw new NotSupportedException();

        public FileAttributes FileAttributes => throw new NotSupportedException();

        public bool HasVfsFileAttributes { get; private set; }

        public bool HasVfsTimeInfo { get; private set; }

        public bool IsDirectory => (FileCharacteristics & UdfFileCharacteristic.Directory) != 0;

        public bool IsSymlink => false;

        public DateTime LastAccessTimeUtc => throw new NotSupportedException();

        public DateTime LastWriteTimeUtc => throw new NotSupportedException();

        public long UniqueCacheId => ((long)FileLocation.ExtentLocation.Partition << 32) | FileLocation.ExtentLocation.LogicalBlock;

        public int Size { get; private set; }

        public static UdfFileId Parse(byte[] data, int offset)
        {
            int implen = data.ReadUInt16L(offset + 0x24);
            byte nameLen = data.Read8(offset + 0x13);
            return new UdfFileId()
            {
                DescriptorTag = UdfDescriptorTag.Parse(data, offset),
                FileVersionNumber = data.ReadUInt16L(offset + 0x10),
                FileCharacteristics = (UdfFileCharacteristic)data.Read8(offset + 0x12),
                NameLength = nameLen,
                FileLocation = UdfLongAllocationDescriptor.Parse(data, offset + 0x14),
                ImplementationUseLength = data.ReadUInt16L(offset + 0x24),
                ImplementationUse = data.Read(offset + 0x26, implen),
                Name = UdfUtils.ReadDCharacters(data, offset + 0x26 + implen, nameLen),
                Size = 0x26 + implen + nameLen
            };
        }
    }

}
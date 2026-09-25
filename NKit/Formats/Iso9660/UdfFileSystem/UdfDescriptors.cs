using System;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class UdfDescriptorTag
    {
        public static UdfDescriptorTag Parse(byte[] data, int offset)
        {
            switch ((UdfTagId)data.ReadUInt16L(offset))
            {
                case UdfTagId.None: //0
                    return null;
                case UdfTagId.PrimaryVolumeDescriptor: //1
                    return UdfPrimaryVolumeDescriptor.Parse(data, offset);
                case UdfTagId.AnchorVolumeDescriptorPointer: //2
                    return UdfAnchorVolumeDescriptorPointer.Parse(data, offset);
                case UdfTagId.VolumeDescriptorPointer: //3
                    return UdfVolumeDescriptorPointer.Parse(data, offset);
                case UdfTagId.ImplementationUseVolumeDescriptor: //4
                    return UdfImplementationUseVolumeDescriptor.Parse(data, offset);
                case UdfTagId.PartitionDescriptor: //5
                    return UdfPartitionDescriptor.Parse(data, offset);
                case UdfTagId.LogicalVolumeDescriptor: //6
                    return UdfLogicalVolumeDescriptor.Parse(data, offset);
                case UdfTagId.UnallocatedSpaceDescriptor: //7
                    return UdfUnallocatedSpaceDescriptor.Parse(data, offset);
                case UdfTagId.TerminatingDescriptor: //8
                    return UdfTerminatingDescriptor.Parse(data, offset);
                case UdfTagId.LogicalVolumeIntegrityDescriptor: //9
                    return UdfLogicalVolumeIntegrityDescriptor.Parse(data, offset);
                default:
                    UdfDescriptorTag tag = new UdfDescriptorTag();
                    Populate(tag, data, offset);
                    return tag;
            }
        }

        public static void Populate(UdfDescriptorTag tag, byte[] data, int offset)
        {
            tag.TagId = (UdfTagId)data.ReadUInt16L(offset + 0x0);
            tag.DescriptorVersion = data.ReadUInt16L(offset + 0x2);
            tag.TagChecksum = data[offset + 0x4];
            tag.TagSerialNumber = data.ReadUInt16L(offset + 0x6);
            tag.DescriptorCrc = data.ReadUInt16L(offset + 0x8);
            tag.DescriptorCrcLength = data.ReadUInt16L(offset + 0xa);
            tag.TagLocation = data.ReadUInt32L(offset + 0xc);
        }

        public ushort DescriptorCrc { get; private set; }
        public ushort DescriptorCrcLength { get; private set; }
        public ushort DescriptorVersion { get; private set; }
        public byte TagChecksum { get; private set; }
        public UdfTagId TagId { get; private set; }
        public uint TagLocation { get; private set; }
        public ushort TagSerialNumber { get; private set; }

        public virtual int Size => 16;

        public override string ToString() => $"{TagId}";
        public static byte? CreateChecksum(byte[] data, int offset)
        {
            byte checkSum = 0;

            if (data.ReadUInt16L(offset) == 0)
                return null;

            for (int i = 0; i < 4; ++i)
                checkSum += data[offset + i];

            for (int i = 5; i < 16; ++i)
                checkSum += data[offset + i];

            return checkSum;
        }
    }

    //ID 1
    internal class UdfPrimaryVolumeDescriptor : UdfDescriptorTag
    {
        public UdfEntityId ApplicationId { get; private set; }
        public uint CharacterSetList { get; private set; }
        public UdfCharacterSetSpecification DescriptorCharSet { get; private set; }
        public UdfCharacterSetSpecification ExplanatoryCharSet { get; private set; }
        public ushort Flags { get; private set; }
        public UdfEntityId ImplementationId { get; private set; }
        public byte[] ImplementationUse { get; private set; }
        public ushort InterchangeLevel { get; private set; }
        public uint MaxCharacterSetList { get; private set; }
        public ushort MaxInterchangeLevel { get; private set; }
        public ushort MaxVolumeSquenceNumber { get; private set; }
        public uint PredecessorVolumeDescriptorSequenceLocation { get; private set; }
        public uint PrimaryVolumeDescriptorNumber { get; private set; }
        public DateTime RecordingTime { get; private set; }
        public UdfExtentDescriptor VolumeAbstractExtent { get; private set; }
        public UdfExtentDescriptor VolumeCopyrightNoticeExtent { get; private set; }
        public uint VolumeDescriptorSequenceNumber { get; private set; }
        public string VolumeId { get; private set; }
        public ushort VolumeSequenceNumber { get; private set; }
        public string VolumeSetId { get; private set; }
        public override int Size => 0x200;

        public static new UdfPrimaryVolumeDescriptor Parse(byte[] data, int offset)
        {
            UdfPrimaryVolumeDescriptor obj = new UdfPrimaryVolumeDescriptor()
            {
                VolumeDescriptorSequenceNumber = data.ReadUInt32L(offset + 0x10),
                PrimaryVolumeDescriptorNumber = data.ReadUInt32L(offset + 0x14),
                VolumeId = UdfUtils.ReadDString(data, offset + 0x18, 0x20),
                VolumeSequenceNumber = data.ReadUInt16L(offset + 0x38),
                MaxVolumeSquenceNumber = data.ReadUInt16L(offset + 0x3a),
                InterchangeLevel = data.ReadUInt16L(offset + 0x3c),
                MaxInterchangeLevel = data.ReadUInt16L(offset + 0x3e),
                CharacterSetList = data.ReadUInt32L(offset + 0x40),
                MaxCharacterSetList = data.ReadUInt32L(offset + 0x44),
                VolumeSetId = UdfUtils.ReadDString(data, offset + 0x48, 0x80),
                DescriptorCharSet = UdfCharacterSetSpecification.Parse(data, offset + 0xc8),
                ExplanatoryCharSet = UdfCharacterSetSpecification.Parse(data, offset + 0x108),
                VolumeAbstractExtent = UdfExtentDescriptor.Parse(data, offset + 0x148),
                VolumeCopyrightNoticeExtent = UdfExtentDescriptor.Parse(data, offset + 0x150),
                ApplicationId = UdfApplicationEntityId.Parse(data, offset + 0x158),
                RecordingTime = UdfUtils.ParseTimestamp(data, offset + 0x178),
                ImplementationId = UdfImplementationEntityId.Parse(data, offset + 0x184),
                ImplementationUse = data.Read(offset + 0x1a4, 0x40),
                PredecessorVolumeDescriptorSequenceLocation = data.ReadUInt32L(offset + 0x1e4),
                Flags = data.ReadUInt16L(offset + 0x1e8)
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            return obj;
        }
    }

    //ID 2
    internal class UdfAnchorVolumeDescriptorPointer : UdfDescriptorTag
    {
        public UdfExtentDescriptor MainDescriptorSequence { get; private set; }
        public UdfExtentDescriptor ReserveDescriptorSequence { get; private set; }
        public override int Size => 0x200;

        public static new UdfAnchorVolumeDescriptorPointer Parse(byte[] data, int offset)
        {
            UdfAnchorVolumeDescriptorPointer obj = new UdfAnchorVolumeDescriptorPointer()
            {
                MainDescriptorSequence = UdfExtentDescriptor.Parse(data, offset + 0x10),
                ReserveDescriptorSequence = UdfExtentDescriptor.Parse(data, offset + 0x18)
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            return obj;
        }
    }

    //ID 3 - Volume Descriptor Pointer 
    internal class UdfVolumeDescriptorPointer : UdfDescriptorTag
    {
        public uint SequenceNumber { get; private set; }
        public UdfExtentDescriptor NextDescriptorSequence { get; private set; }
        public override int Size => 0x200;

        public static new UdfVolumeDescriptorPointer Parse(byte[] data, int offset)
        {
            UdfVolumeDescriptorPointer obj = new UdfVolumeDescriptorPointer()
            {
                SequenceNumber = data.ReadUInt32L(offset + 0x10),
                NextDescriptorSequence = UdfExtentDescriptor.Parse(data, offset + 0x14)
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            return obj;
        }
    }

    //ID 4 - Implementation Use Volume Descriptor 
    internal class UdfImplementationUseVolumeDescriptor : UdfDescriptorTag
    {
        public uint VolumeDescriptorSequenceNumber { get; private set; }
        public UdfEntityId ImplementationId { get; private set; }
        public UdfCharacterSetSpecification LviCharSet { get; private set; }
        public string LviLogicalVolumeId { get; private set; }
        public string LviInfo1 { get; private set; }
        public string LviInfo2 { get; private set; }
        public string LviInfo3 { get; private set; }
        public UdfEntityId LviImplementationId { get; private set; }
        public byte[] LviImplementationUse { get; private set; }
        public override int Size => 0x200;

        public static new UdfImplementationUseVolumeDescriptor Parse(byte[] data, int offset)
        {
            int ioff = offset + 0x10 + 0x20 + 0x4; //inline implementation use offset

            UdfImplementationUseVolumeDescriptor obj = new UdfImplementationUseVolumeDescriptor()
            {
                VolumeDescriptorSequenceNumber = data.ReadUInt32L(offset + 0x10),
                ImplementationId = UdfEntityId.Parse(data, offset + 0x14),
                //inline LVInformation struct
                LviCharSet = UdfCharacterSetSpecification.Parse(data, ioff + 0x0),
                LviLogicalVolumeId = UdfUtils.ReadDString(data, ioff + 0x40, 0x80),
                LviInfo1 = UdfUtils.ReadDString(data, ioff + 0xC0, 0x24),
                LviInfo2 = UdfUtils.ReadDString(data, ioff + 0xe4, 0x24),
                LviInfo3 = UdfUtils.ReadDString(data, ioff + 0x108, 0x24),
                LviImplementationId = UdfEntityId.Parse(data, ioff + 0x12c),
                LviImplementationUse = data.Read(ioff + 0x14c, 0x80)
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            return obj;
        }
    }

    //ID 5 - Partition Descriptor
    internal class UdfPartitionDescriptor : UdfDescriptorTag
    {
        public uint AccessType { get; private set; }
        public UdfEntityId ImplementationId { get; private set; }
        public byte[] ImplementationUse { get; private set; }
        public UdfEntityId PartitionContents { get; private set; }
        public byte[] PartitionContentsUse { get; private set; }
        public ushort PartitionFlags { get; private set; }
        public uint PartitionLength { get; private set; }
        public ushort PartitionNumber { get; private set; }
        public uint PartitionStartingLocation { get; private set; }
        public uint VolumeDescriptorSequenceNumber { get; private set; }
        public override int Size => 0x200;

        public static new UdfPartitionDescriptor Parse(byte[] data, int offset)
        {
            UdfPartitionDescriptor obj = new UdfPartitionDescriptor()
            {
                VolumeDescriptorSequenceNumber = data.ReadUInt32L(offset + 0x10),
                PartitionFlags = data.ReadUInt16L(offset + 0x14),
                PartitionNumber = data.ReadUInt16L(offset + 0x16),
                PartitionContents = UdfApplicationEntityId.Parse(data, offset + 0x18),
                PartitionContentsUse = data.Read(offset + 0x38, 0x80),
                AccessType = data.ReadUInt32L(offset + 0xb8),
                PartitionStartingLocation = data.ReadUInt32L(offset + 0xbc),
                PartitionLength = data.ReadUInt32L(offset + 0xc0),
                ImplementationId = UdfImplementationEntityId.Parse(data, offset + 0xc4),
                ImplementationUse = data.Read(offset + 0xe4, 0x80),
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            return obj;
        }
    }

    //ID 6 - Logical Volume Descriptor
    internal sealed class UdfLogicalVolumeDescriptor : UdfDescriptorTag
    {
        private int _size;
        public byte[] DescriptorCharset { get; private set; }
        public UdfEntityId DomainId { get; private set; }
        public UdfEntityId ImplementationId { get; private set; }
        public byte[] ImplementationUse { get; private set; }
        public UdfExtentDescriptor IntegritySequenceExtent { get; private set; }
        public uint LogicalBlockSize { get; internal set; }
        public byte[] LogicalVolumeContentsUse { get; private set; }
        public string LogicalVolumeId { get; private set; }
        public uint MapTableLength { get; private set; }
        public uint NumPartitionMaps { get; private set; }
        public UdfPartitionMap[] PartitionMaps { get; private set; }
        public uint VolumeDescriptorSequenceNumber { get; private set; }
        public UdfLongAllocationDescriptor FileSetDescriptorLocation { get; private set; }
        public override int Size => _size;

        public static new UdfLogicalVolumeDescriptor Parse(byte[] data, int offset)
        {
            UdfLogicalVolumeDescriptor obj = new UdfLogicalVolumeDescriptor()
            {
                VolumeDescriptorSequenceNumber = data.ReadUInt32L(offset + 0x10),
                DescriptorCharset = data.Read(offset + 0x14, 0x40),
                LogicalVolumeId = UdfUtils.ReadDString(data, offset + 0x54, 0x80),
                LogicalBlockSize = data.ReadUInt32L(offset + 0xd4),
                DomainId = UdfDomainEntityId.Parse(data, offset + 0xd8),
                LogicalVolumeContentsUse = data.Read(offset + 0xf8, 0x10),
                MapTableLength = data.ReadUInt32L(offset + 0x108),
                NumPartitionMaps = data.ReadUInt32L(offset + 0x10c),
                ImplementationId = UdfImplementationEntityId.Parse(data, offset + 0x110),
                ImplementationUse = data.Read(offset + 0x130, 0x80),
                IntegritySequenceExtent = UdfExtentDescriptor.Parse(data, offset + 0x1b0)
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            int off = offset + 0x1b8;
            obj.PartitionMaps = new UdfPartitionMap[obj.NumPartitionMaps];
            for (int i = 0; i < obj.NumPartitionMaps; ++i)
            {
                obj.PartitionMaps[i] = UdfPartitionMap.Parse(data, off);
                off += obj.PartitionMaps[i].Size;
            }
            obj.FileSetDescriptorLocation = UdfLongAllocationDescriptor.Parse(obj.LogicalVolumeContentsUse, 0);
            obj._size = 0x1b8 + (int)obj.MapTableLength;
            return obj;
        }
    }

    //ID 7 - Unallocated Space Descriptor
    internal sealed class UdfUnallocatedSpaceDescriptor : UdfDescriptorTag
    {
        private int _size;
        public UdfExtentAllocationDescriptor[] Extents { get; private set; }
        public uint VolumeDescriptorSequenceNumber { get; private set; }
        public override int Size => _size;

        public static new UdfUnallocatedSpaceDescriptor Parse(byte[] data, int offset)
        {
            UdfUnallocatedSpaceDescriptor obj = new UdfUnallocatedSpaceDescriptor()
            {
                VolumeDescriptorSequenceNumber = data.ReadUInt32L(offset + 0x10),
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            uint c = data.ReadUInt32L(offset + 0x14);
            obj.Extents = new UdfExtentAllocationDescriptor[c];
            for (int i = 0; i < c; ++i)
                obj.Extents[i] = UdfExtentAllocationDescriptor.Parse(data, offset + 0x18 + (i << 3));

            obj._size = (int)(0x18 + (c << 3));
            return obj;
        }
    }

    //ID 8 - Terminating Descriptor
    internal sealed class UdfTerminatingDescriptor : UdfDescriptorTag
    {
        //private int _size;
        public override int Size => 0x200;

        public static new UdfUnallocatedSpaceDescriptor Parse(byte[] data, int offset)
        {
            UdfUnallocatedSpaceDescriptor obj = new UdfUnallocatedSpaceDescriptor();
            UdfDescriptorTag.Populate(obj, data, offset);
            return obj;
        }
    }

    //ID 9 - Logical Volume Integrity Descriptor
    internal class UdfLogicalVolumeIntegrityDescriptor : UdfDescriptorTag
    {
        private int _size;
        public DateTime RecordingTime { get; private set; }
        public UdfIntegrityType IntegrityType { get; private set; }
        public UdfExtentDescriptor NextIntegrityExtent { get; private set; }
        public byte[] LogicalVolumeContentsUse { get; private set; }
        public uint NumberOfPartitions { get; private set; }
        public uint[] FreeSpaceTable { get; private set; }
        public uint[] SizeTable { get; private set; }
        public uint LengthOfImplementationUse { get; private set; }

        public UdfEntityId ImplementationId { get; private set; }
        public uint FileCount { get; private set; }
        public uint DirectoryCount { get; private set; }
        public ushort MinUdfReadRevision { get; private set; }
        public ushort MinUdfWriteRevision { get; private set; }
        public ushort MaxUdfReadRevision { get; private set; }


        public override int Size => _size;

        public static new UdfLogicalVolumeIntegrityDescriptor Parse(byte[] data, int offset)
        {
            UdfLogicalVolumeIntegrityDescriptor obj = new UdfLogicalVolumeIntegrityDescriptor()
            {
                RecordingTime = UdfUtils.ParseTimestamp(data, offset + 0x10),
                IntegrityType = (UdfIntegrityType)data.ReadUInt32L(offset + 0x1c),
                NextIntegrityExtent = UdfExtentDescriptor.Parse(data, 0x20),
                LogicalVolumeContentsUse = data.Read(0x28, 0x20),
                NumberOfPartitions = data.ReadUInt32L(offset + 0x48),
                LengthOfImplementationUse = data.ReadUInt32L(offset + 0x4c),
            };
            UdfDescriptorTag.Populate(obj, data, offset);
            obj.FreeSpaceTable = new uint[obj.NumberOfPartitions];
            obj.SizeTable = new uint[obj.NumberOfPartitions];
            int off = offset + 0x50;
            for (int i = 0; i < obj.NumberOfPartitions; i++)
            {
                obj.FreeSpaceTable[i] = data.ReadUInt32L(off + (i << 2));
                obj.SizeTable[i] = data.ReadUInt32L(off + ((int)obj.NumberOfPartitions << 2) + (i << 2));
            }
            off += (int)obj.NumberOfPartitions << 3;
            obj.ImplementationId = UdfEntityId.Parse(data, off + 0x0);
            obj.FileCount = data.ReadUInt32L(off + 0x20);
            obj.DirectoryCount = data.ReadUInt32L(off + 0x24);
            obj.MinUdfReadRevision = data.ReadUInt16L(off + 0x28);
            obj.MinUdfWriteRevision = data.ReadUInt16L(off + 0x2a);
            obj.MaxUdfReadRevision = data.ReadUInt16L(off + 0x2c);
            obj._size = off + (int)obj.LengthOfImplementationUse - offset;
            return obj;
        }
    }
    //0x100
    internal class UdfFileSetDescriptor
    {
        public string AbstractFileId { get; private set; }
        public uint CharacterSetList { get; private set; }
        public string CopyrightFileId { get; private set; }
        public UdfDescriptorTag DescriptorTag { get; private set; }
        public UdfDomainEntityId DomainId { get; private set; }
        public UdfCharacterSetSpecification FileSetCharset { get; private set; }
        public uint FileSetDescriptorNumber { get; private set; }
        public string FileSetId { get; private set; }
        public uint FileSetNumber { get; private set; }
        public ushort InterchangeLevel { get; private set; }
        public string LogicalVolumeId { get; private set; }
        public UdfCharacterSetSpecification LogicalVolumeIdCharset { get; private set; }
        public uint MaximumCharacterSetList { get; private set; }
        public ushort MaximumInterchangeLevel { get; private set; }
        public UdfLongAllocationDescriptor NextExtent { get; private set; }
        public DateTime RecordingTime { get; private set; }
        public UdfLongAllocationDescriptor RootDirectoryIcb { get; private set; }
        public UdfLongAllocationDescriptor SystemStreamDirectoryIcb { get; private set; }

        public int Size => 0x200;

        public static UdfFileSetDescriptor Parse(byte[] buffer, int offset)
        {
            return new UdfFileSetDescriptor()
            {
                DescriptorTag = UdfDescriptorTag.Parse(buffer, offset),
                RecordingTime = UdfUtils.ParseTimestamp(buffer, offset + 0x10),
                InterchangeLevel = buffer.ReadUInt16L(offset + 0x1c),
                MaximumInterchangeLevel = buffer.ReadUInt16L(offset + 0x1e),
                CharacterSetList = buffer.ReadUInt32L(offset + 0x20),
                MaximumCharacterSetList = buffer.ReadUInt32L(offset + 0x24),
                FileSetNumber = buffer.ReadUInt32L(offset + 0x28),
                FileSetDescriptorNumber = buffer.ReadUInt32L(offset + 0x2c),
                LogicalVolumeIdCharset = UdfCharacterSetSpecification.Parse(buffer, offset + 0x30),
                LogicalVolumeId = UdfUtils.ReadDString(buffer, offset + 0x70, 0x80),
                FileSetCharset = UdfCharacterSetSpecification.Parse(buffer, offset + 0xf0),
                FileSetId = UdfUtils.ReadDString(buffer, offset + 0x130, 0x20),
                CopyrightFileId = UdfUtils.ReadDString(buffer, offset + 0x150, 0x20),
                AbstractFileId = UdfUtils.ReadDString(buffer, offset + 0x170, 0x20),
                RootDirectoryIcb = UdfLongAllocationDescriptor.Parse(buffer, offset + 0x190),
                DomainId = UdfDomainEntityId.Parse(buffer, offset + 0x1a0),
                NextExtent = UdfLongAllocationDescriptor.Parse(buffer, offset + 0x1c0),
                SystemStreamDirectoryIcb = UdfLongAllocationDescriptor.Parse(buffer, offset + 0x1d0),
            };
        }
    }
    internal class UdfAllocationExtentDescriptor : UdfDescriptorTag
    {
        public int AllocationDescriptorsLength { get; private set; }
        public uint PreviousAllocationExtentLocation { get; private set; }
        public byte[] AllocationDescriptors { get; protected set; }
        public static new UdfAllocationExtentDescriptor Parse(byte[] data, int offset)
        {
            UdfAllocationExtentDescriptor obj = new UdfAllocationExtentDescriptor();
            UdfDescriptorTag.Populate(obj, data, offset);
            obj.PreviousAllocationExtentLocation = data.ReadUInt32L(offset + obj.Size + 0x0);
            obj.AllocationDescriptorsLength = (int)data.ReadUInt32L(offset + obj.Size + 0x4);
            obj.AllocationDescriptors = data.Read(offset + obj.Size + 0x8, obj.AllocationDescriptorsLength);
            return obj;
        }
    }

    internal sealed class UdfExtentAllocationDescriptor
    {
        public uint ExtentLength { get; private set; }
        public uint ExtentLocation { get; private set; }

        public int Size => 0x8;

        public static UdfExtentAllocationDescriptor Parse(byte[] buffer, int offset)
        {
            return new UdfExtentAllocationDescriptor()
            {
                ExtentLength = buffer.ReadUInt32L(offset),
                ExtentLocation = buffer.ReadUInt32L(offset + 0x4)
            };
        }

        public override string ToString() => ExtentLocation + ":+" + ExtentLength;
    }

    internal sealed class UdfShortAllocationDescriptor
    {
        public uint ExtentLength { get; private set; }
        public uint ExtentLocation { get; private set; }
        public UdfShortAllocationFlags Flags { get; private set; }

        public int Size => 0x8;

        public static UdfShortAllocationDescriptor Parse(byte[] buffer, int offset)
        {
            uint len = buffer.ReadUInt32L(offset + 0x0);
            return new UdfShortAllocationDescriptor()
            {
                ExtentLocation = buffer.ReadUInt32L(offset + 0x4),
                ExtentLength = len & 0x3FFFFFFF,
                Flags = (UdfShortAllocationFlags)((len >> 0x1e) & 0x3)
            };
        }

        public override string ToString() => ExtentLocation + ":+" + ExtentLength + " [" + Flags + "]";
    }

    internal class UdfLongAllocationDescriptor
    {
        public uint ExtentLength { get; private set; }
        public UdfLogicalBlockAddress ExtentLocation { get; private set; }
        public byte[] ImplementationUse { get; private set; }
        public int Size => 0x10;

        public static UdfLongAllocationDescriptor Parse(byte[] buffer, int offset)
        {
            return new UdfLongAllocationDescriptor()
            {
                ExtentLength = buffer.ReadUInt32L(offset + 0x0),
                ExtentLocation = UdfLogicalBlockAddress.Parse(buffer, offset + 0x4),
                ImplementationUse = buffer.Read(offset + 0xa, 0x6)
            };
        }

        public override string ToString() => ExtentLocation + ":+" + ExtentLength;
    }
}
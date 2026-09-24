using System.IO;

namespace Nanook.NKit.Iso.Iso9660
{
    internal abstract class UdfPartitionMap
    {
        public byte Type;

        public abstract int Size { get; }
        public abstract ushort PartitionNumber { get; protected set; }
        public abstract ushort VolumeSequenceNumber { get; protected set; }
        public abstract UdfEntityId EntityId { get; protected set; }
        public string TypeName { get; protected set; }

        public static UdfPartitionMap Parse(byte[] data, int offset)
        {
            if (data[offset] == 1) //type == 1
                return UdfType1PartitionMap.Parse(data, offset);
            else
            {
                UdfEntityId id = UdfVerEntityId.Parse(data, offset + 0x4);
                switch (id.Id)
                {
                    case "*UDF Virtual Partition":
                        return UdfVirtualPartitionMap.Parse(id, data, offset);
                    case "*UDF Sparable Partition":
                        return UdfSparablePartitionMap.Parse(id, data, offset);
                    case "*UDF Metadata Partition":
                        return UdfMetadataPartitionMap.Parse(id, data, offset);
                    default:
                        throw new InvalidDataException("Unrecognised partition map entity id: " + id);
                }
            }
        }
    }

    internal class UdfType1PartitionMap : UdfPartitionMap
    {
        public override ushort PartitionNumber { get; protected set; }
        public override ushort VolumeSequenceNumber { get; protected set; }
        public override UdfEntityId EntityId { get; protected set; }
        public override int Size => 0x6;

        public static new UdfType1PartitionMap Parse(byte[] data, int offset)
        {
            return new UdfType1PartitionMap()
            {
                Type = 1,
                TypeName = "Type1",
                EntityId = null,
                VolumeSequenceNumber = data.ReadUInt16L(offset + 0x2),
                PartitionNumber = data.ReadUInt16L(offset + 0x4)
            };
        }
    }

    internal class UdfVirtualPartitionMap : UdfPartitionMap
    {
        public override ushort PartitionNumber { get; protected set; }
        public override ushort VolumeSequenceNumber { get; protected set; }
        public override UdfEntityId EntityId { get; protected set; }
        public override int Size => 0x40;

        public static UdfVirtualPartitionMap Parse(UdfEntityId id, byte[] data, int offset)
        {
            return new UdfVirtualPartitionMap()
            {
                Type = 2,
                TypeName = "Virtual",
                EntityId = id,
                VolumeSequenceNumber = data.ReadUInt16L(offset + 0x24),
                PartitionNumber = data.ReadUInt16L(offset + 0x26)
            };
        }
    }

    internal sealed class UdfSparablePartitionMap : UdfPartitionMap
    {
        public override ushort PartitionNumber { get; protected set; }
        public override ushort VolumeSequenceNumber { get; protected set; }
        public override UdfEntityId EntityId { get; protected set; }
        public uint[] LocationsOfSparingTables { get; private set; }
        public byte NumSparingTables { get; private set; }
        public ushort PacketLength { get; private set; }
        public uint SparingTableSize { get; private set; }
        public override int Size => 0x40;

        public static UdfSparablePartitionMap Parse(UdfEntityId id, byte[] data, int offset)
        {
            UdfSparablePartitionMap map = new UdfSparablePartitionMap()
            {
                Type = 2,
                TypeName = "Sparable",
                EntityId = id,
                VolumeSequenceNumber = data.ReadUInt16L(offset + 0x24),
                PartitionNumber = data.ReadUInt16L(offset + 0x26),
                PacketLength = data.ReadUInt16L(offset + 0x28),
                NumSparingTables = data.Read8(offset + 0x2a),
                SparingTableSize = data.ReadUInt32L(offset + 0x2c),
            };
            map.LocationsOfSparingTables = new uint[map.NumSparingTables];
            for (int i = 0; i < map.NumSparingTables; ++i)
                map.LocationsOfSparingTables[i] = data.ReadUInt32L(offset + 0x30 + (i << 2));
            return map;
        }
    }

    internal sealed class UdfMetadataPartitionMap : UdfPartitionMap
    {
        public override ushort PartitionNumber { get; protected set; }
        public override ushort VolumeSequenceNumber { get; protected set; }
        public override UdfEntityId EntityId { get; protected set; }
        public uint FileLocation { get; private set; }
        public uint MirrorFileLocation { get; private set; }
        public uint BitmapFileLocation { get; private set; }
        public uint AllocationUnitSize { get; private set; }
        public ushort AlignmentUnitSize { get; private set; }
        public byte Flags { get; private set; }
        public override int Size => 0x40;

        public static UdfMetadataPartitionMap Parse(UdfEntityId id, byte[] data, int offset)
        {
            UdfMetadataPartitionMap map = new UdfMetadataPartitionMap()
            {
                Type = 2,
                TypeName = "Metadata",
                EntityId = id,
                VolumeSequenceNumber = data.ReadUInt16L(offset + 0x24),
                PartitionNumber = data.ReadUInt16L(offset + 0x26),
                FileLocation = data.ReadUInt32L(offset + 0x28),
                MirrorFileLocation = data.ReadUInt32L(offset + 0x2c),
                BitmapFileLocation = data.ReadUInt32L(offset + 0x30),
                AllocationUnitSize = data.ReadUInt32L(offset + 0x34),
                AlignmentUnitSize = data.ReadUInt16L(offset + 0x38),
                Flags = data.Read8(offset + 0x3a),
            };
            return map;
        }
    }

}
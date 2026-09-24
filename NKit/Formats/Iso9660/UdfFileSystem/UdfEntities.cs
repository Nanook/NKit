using System;
using System.Globalization;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class UdfEntityId
    {
        public byte Flags { get; protected set; }
        public string Id { get; protected set; }
        public byte[] Suffix { get; protected set; }
        public int Size => 0x20;
        public static UdfEntityId Parse(byte[] data, int offset)
        {
            UdfEntityId entity = new UdfEntityId();
            Populate(entity, data, offset);
            return entity;
        }
        public static void Populate(UdfEntityId entity, byte[] data, int offset)
        {
            entity.Flags = data.Read8(offset + 0x0);
            entity.Id = data.ReadString(offset + 0x1, 0x17).TrimEnd('\0');
            entity.Suffix = data.Read(offset + 0x18, 0x8);
        }
    }

    internal class UdfVerEntityId : UdfEntityId
    {
        public new static UdfVerEntityId Parse(byte[] data, int offset)
        {
            UdfVerEntityId entity = new UdfVerEntityId();
            Populate(entity, data, offset);
            return entity;
        }
        public override string ToString()
        {
            string major = ((uint)Suffix[1]).ToString("X", CultureInfo.InvariantCulture);
            string minor = ((uint)Suffix[0]).ToString("X", CultureInfo.InvariantCulture);
            UdfOsClass osClass = (UdfOsClass)Suffix[2];
            UdfOsId osId = (UdfOsId)this.Suffix.ReadUInt16B(0); //don't swap bytes
            return string.Format(CultureInfo.InvariantCulture, "{0} [UDF {1}.{2} : OS {3} {4}]", Id, major,
                minor, osClass, osId);
        }
    }

    internal class UdfDomainEntityId : UdfEntityId
    {
        public new static UdfDomainEntityId Parse(byte[] data, int offset)
        {
            UdfDomainEntityId entity = new UdfDomainEntityId();
            Populate(entity, data, offset);
            return entity;
        }
        public override string ToString()
        {
            string major = ((uint)Suffix[1]).ToString("X", CultureInfo.InvariantCulture);
            string minor = ((uint)Suffix[0]).ToString("X", CultureInfo.InvariantCulture);
            DomainFlags flags = (DomainFlags)Suffix[2];
            return string.Format(CultureInfo.InvariantCulture, "{0} [UDF {1}.{2} : Flags {3}]", Id, major, minor,
                flags);
        }

        [Flags]
        private enum DomainFlags : byte
        {
            None = 0,
            HardWriteProtect = 1,
            SoftWriteProtect = 2
        }
    }

    internal class UdfImplementationEntityId : UdfEntityId
    {
        public new static UdfImplementationEntityId Parse(byte[] data, int offset)
        {
            UdfImplementationEntityId entity = new UdfImplementationEntityId();
            Populate(entity, data, offset);
            return entity;
        }

        public override string ToString()
        {
            UdfOsClass osClass = (UdfOsClass)Suffix[0];
            UdfOsId osId = (UdfOsId)this.Suffix.ReadUInt16B(0); //don't swap bytes
            return string.Format(CultureInfo.InvariantCulture, "{0} [OS {1} {2}]", Id, osClass, osId);
        }
    }

    internal class UdfApplicationEntityId : UdfEntityId
    {
        public new static UdfApplicationEntityId Parse(byte[] data, int offset)
        {
            UdfApplicationEntityId entity = new UdfApplicationEntityId();
            Populate(entity, data, offset);
            return entity;
        }
        public override string ToString() => Id;
    }
}
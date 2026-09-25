using System;

namespace Nanook.NKit.Iso.Iso9660
{

    [Flags]
    enum FileFlags : byte
    {
        Hidden = 0x01, Directory = 0x02, Associated = 0x04,
        Record = 0x08, Protected = 0x10, MultiExtent = 0x80
    }

    [Flags]
    enum Permissions : ushort
    {
        SystemRead = 0x01, SystemExecute = 0x04, OwnerRead = 0x10,
        OwnerExecute = 0x40, GroupRead = 0x100, GroupExecute = 0x400,
        OtherRead = 0x1000, OtherExecute = 0x4000
    }

    enum RecordFormat : byte
    {
        Unspecified = 0, FixedLength = 1, VariableLength = 2,
        VariableLengthAlternate = 3
    }

    enum RecordAttribute : byte
    {
        LFCR = 0, ISO1539 = 1, ControlContained = 2
    }
}
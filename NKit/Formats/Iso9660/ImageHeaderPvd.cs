using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class ImageHeaderPvd
    {
        private Regex _regexNameOnly;
        private Regex _isIso9660;

        internal static ImageHeaderPvd CreateSystem() => new ImageHeaderPvd() { RootFolder = new FstFolder(FsType.System), Type = FsType.System };

        public ImageHeaderPvd()
        {
            _regexNameOnly = new Regex("^(.*?)(;[0-9]{1,5})?\0*$", RegexOptions.Compiled);
            _isIso9660 = new Regex(@"^.{1,8}(\..{0,3})?$", RegexOptions.Compiled);
        }
        public static ImageHeaderPvd Parse(FstContext ctx, long imageOffset, long areaOffset, byte[] data, int blockSize, int blockFsOffset, int blockFsSize, long sessionBaseOffset)
        {
            ImageHeaderPvd pvd = null;

            string id = data.ReadString(0x0001, 5);
            bool isCdi = id == "CD-I ";
            bool isUdf = id == "BEA01" || id == "TEA01" || id == "NSR03" || id == "NSR02" || id == "BOOT2";

            if (id == "CD001" || isUdf || isCdi)
            {
                pvd = new ImageHeaderPvd();
                pvd.ImageOffset = imageOffset;
                pvd.FsOffset = Buffer.OffsetToFsOffset(areaOffset, blockSize, blockFsOffset, blockFsSize);
                pvd.Size = blockFsSize;

                pvd.PvdType = data.Read8(0x0000);
                pvd.PvdId = data.ReadString(0x0001, 5);
                pvd.PvdVersion = data.Read8(0x0006);

                if (isUdf)
                {
                    pvd.Type = FsType.Udf;
                    if (!ctx.Pvd.ContainsKey(FsType.Udf))
                        pvd.RootFolder = new FstFolder(pvd.Type);
                    else
                        pvd.RootFolder = ctx.Pvd[FsType.Udf].RootFolder;

                    if (id.StartsWith("NSR"))
                        pvd.PvdId = id;

                    ctx.AddFile(pvd.RootFolder, $"__udf_{id}_{pvd.FsOffset:X}", pvd.Type, pvd.FsOffset, pvd.Size, FsItemType.Pvd, true);
                }
                else if (data.ReadString(0x7, 0x20).TrimEnd('\0') == "EL TORITO SPECIFICATION")
                {
                    pvd.Type = FsType.ElTorito;
                    pvd.RootFolder = new FstFolder(pvd.Type);
                    if (pvd.Type == FsType.ElTorito)
                    {
                        ctx.AddFile(pvd.RootFolder, $"__pvd_{pvd.Type}_{pvd.FsOffset:X}", pvd.Type, pvd.FsOffset, pvd.Size, FsItemType.Pvd, true);
                        ctx.AddFile(pvd.RootFolder, $"__bootCatalog_{pvd.PvdTypeLPathTableOffset:X}", pvd.Type, data.ReadUInt32L(0x47) * (long)blockFsSize, blockFsSize, FsItemType.BootCatalog, true);
                    }
                }
                else
                {
                    pvd.PvdFlags = data.Read8(0x0007);
                    pvd.PvdSystemId = data.Read(0x0008, 0x20);
                    pvd.PvdVolumeId = data.Read(0x0028, 0x20);
                    pvd.Blocks = Math.Max(data.ReadUInt32L(0x0050), data.ReadUInt32B(0x0054)); //PvdVolumeSpaceSize
                    pvd.PvdEscapeSequences = data.Read(0x0058, 0x20);
                    pvd.PvdVolumeSetSize = Math.Max(data.ReadUInt16L(0x0078), data.ReadUInt16B(0x007a));
                    pvd.PvdVolumeSequenceNumber = Math.Max(data.ReadUInt16L(0x007c), data.ReadUInt16B(0x007e));
                    pvd.BlocksSize = Math.Max(data.ReadUInt16L(0x0080), data.ReadUInt16B(0x0082)); //PvdLogicalBlockSize
                    pvd.PvdPathTableSize = Math.Max(data.ReadUInt32L(0x0084), data.ReadUInt32B(0x0088));
                    pvd.PvdTypeLPathTable = data.ReadUInt32L(0x008c);
                    pvd.PvdTypeOptLPathTable = data.ReadUInt32L(0x0090);
                    pvd.PvdTypeMPathTable = data.ReadUInt32B(0x0094);
                    pvd.PvdTypeOptMPathTable = data.ReadUInt32B(0x0098);

                    pvd.PvdTypeLPathTableOffset = Math.Max((pvd.PvdTypeLPathTable * pvd.BlocksSize) - sessionBaseOffset, 0);
                    pvd.PvdTypeOptLPathTableOffset = Math.Max((pvd.PvdTypeOptLPathTable * pvd.BlocksSize) - sessionBaseOffset, 0);
                    pvd.PvdTypeMPathTableOffset = Math.Max((pvd.PvdTypeMPathTable * pvd.BlocksSize) - sessionBaseOffset, 0);
                    pvd.PvdTypeOptMPathTableOffset = Math.Max((pvd.PvdTypeOptMPathTable * pvd.BlocksSize) - sessionBaseOffset, 0);

                    //Directory Record
                    pvd.RootDirectoryRecord = IsoDirectory.Parse(data, 0x009c);

                    int offset = 0x009c + pvd.RootDirectoryRecord.Length;
                    pvd.PvdVolumeSetId = data.Read(offset + 0x0000, 0x80);
                    pvd.PvdPublisherId = data.Read(offset + 0x0080, 0x80);
                    pvd.PvdPreparerId = data.Read(offset + 0x0100, 0x80);
                    pvd.PvdApplicationId = data.Read(offset + 0x0180, 0x80);
                    pvd.PvdCopyrightFileId = data.Read(offset + 0x0200, 0x25);
                    pvd.PvdAbstractFileId = data.Read(offset + 0x0225, 0x25);
                    pvd.PvdBibliographicFileId = data.Read(offset + 0x024a, 0x25);
                    pvd.PvdCreationDate = data.Read(offset + 0x026f, 0x11 - 1);
                    pvd.PvdModificationDate = data.Read(offset + 0x0280, 0x11 - 1);
                    pvd.PvdExpirationDate = data.Read(offset + 0x0291, 0x11 - 1);
                    pvd.PvdEffectiveDate = data.Read(offset + 0x02a2, 0x11 - 1);
                    pvd.PvdFileStructureVersion = data.Read8(offset + 0x2b3);
                    pvd.PvdApplicationData = data.Read(offset + 0x2b5, 0x200);

                    if (isCdi)
                        pvd.Type = FsType.Cdi;
                    else if (pvd.PvdType == 2 && pvd.PvdEscapeSequences[0] == '%' && pvd.PvdEscapeSequences[1] == '/' && "@CE".Contains((char)pvd.PvdEscapeSequences[2]))
                        pvd.Type = FsType.Joliet;
                    else
                        pvd.Type = FsType.Iso9660;

                    int saLen = pvd.RootDirectoryRecord.Length;
                    uint rootLocation = pvd.RootDirectoryRecord.Extent;
                    uint rootSize = 0;
                    if (pvd.RootDirectoryRecord.Size > 0)
                        rootSize = (pvd.RootDirectoryRecord.Size / pvd.BlocksSize) + (pvd.RootDirectoryRecord.Size % pvd.BlocksSize == 0 ? 0u : 1u);

                    pvd.RootFolder = new FstFolder(pvd.Type);
                    ctx.AddFile(pvd.RootFolder, $"__pvd_{pvd.Type}_{pvd.FsOffset:X}", pvd.Type, pvd.FsOffset, pvd.Size, FsItemType.Pvd, true);

                    FstFile f = (FstFile)pvd.RootFolder.Files.FirstOrDefault(a => a.FsOffset == pvd.PvdTypeLPathTableOffset);
                    if (f == null && pvd.PvdTypeLPathTableOffset != 0)
                        ctx.AddFile(pvd.RootFolder, $"__paths_LittleEndian_{pvd.PvdTypeLPathTableOffset:X}", pvd.Type, pvd.PvdTypeLPathTableOffset, pvd.PvdPathTableSize, FsItemType.PathTable, true, FsBlockEndian.Little);

                    f = (FstFile)pvd.RootFolder.Files.FirstOrDefault(a => a.FsOffset == pvd.PvdTypeMPathTableOffset);
                    if (f == null && pvd.PvdTypeMPathTableOffset != 0)
                        ctx.AddFile(pvd.RootFolder, $"__paths_BigEndian_{pvd.PvdTypeMPathTableOffset:X}", pvd.Type, pvd.PvdTypeMPathTableOffset, pvd.PvdPathTableSize, FsItemType.PathTable, true, FsBlockEndian.Big);

                    f = (FstFile)pvd.RootFolder.Files.FirstOrDefault(a => a.FsOffset == pvd.PvdTypeOptLPathTable);
                    if (f == null && pvd.PvdTypeOptLPathTable != 0)
                    {
                        f = (FstFile)pvd.RootFolder.Files.FirstOrDefault(a => a.FsOffset == pvd.PvdTypeOptLPathTableOffset);
                        if (f == null)
                            ctx.AddFile(pvd.RootFolder, $"__pathsOpt_LittleEndian_{pvd.PvdTypeOptLPathTableOffset:X}", pvd.Type, pvd.PvdTypeOptLPathTableOffset, pvd.PvdPathTableSize, FsItemType.PathTable, true, FsBlockEndian.Little);
                    }

                    f = (FstFile)pvd.RootFolder.Files.FirstOrDefault(a => a.FsOffset == pvd.PvdTypeOptMPathTable);
                    if (f == null && pvd.PvdTypeOptMPathTable != 0)
                    {
                        f = (FstFile)pvd.RootFolder.Files.FirstOrDefault(a => a.FsOffset == pvd.PvdTypeOptMPathTableOffset);
                        if (f == null)
                            ctx.AddFile(pvd.RootFolder, $"__pathsOpt_BigEndian_{pvd.PvdTypeOptMPathTableOffset:X}", pvd.Type, pvd.PvdTypeOptMPathTableOffset, pvd.PvdPathTableSize, FsItemType.PathTable, true, FsBlockEndian.Big);
                    }
                }
            }
            return pvd;
        }

        internal string GetNameString(byte[] name, out bool isRomeo)
        {
            isRomeo = false;
            switch (this.Type)
            {
                case FsType.Iso9660:
                    string nm = null;
                    nm = getName(Encoding.ASCII.GetString(name));
                    if (nm.Length != 0 && (nm.Length > 12 || !_isIso9660.IsMatch(nm)))
                    {
                        nm = getName(Encoding.Default.GetString(name));
                        isRomeo = true;
                    }
                    return nm;
                case FsType.Joliet:
                    return getName(Encoding.BigEndianUnicode.GetString(name));
                case FsType.ElTorito:
                    // El Torito boot-catalog ID strings (Validation Entry ManufacId[24], Section Header
                    // Id[28]) are ASCII per the El Torito 1.0 spec (a PC/BIOS format — no code-page or
                    // Unicode concept; e.g. the "EL TORITO SPECIFICATION" system id is plain ASCII).
                    return Encoding.ASCII.GetString(name);
                default:
                    return Encoding.Default.GetString(name);
            }
            throw new NotImplementedException();
        }

        private string getName(string name) => _regexNameOnly.Match(name).Groups[1].Value;

        public long PvdTypeLPathTableOffset { get; private set; }
        public long PvdTypeMPathTableOffset { get; private set; }

        public long PvdTypeOptLPathTableOffset { get; private set; }
        public long PvdTypeOptMPathTableOffset { get; private set; }


        public FsType Type { get; private set; }

        public byte PvdType { get; private set; }
        public string PvdId { get; private set; }
        public byte PvdVersion { get; private set; }

        public byte PvdFlags { get; private set; }
        public byte[] PvdSystemId { get; private set; }
        public byte[] PvdVolumeId { get; private set; }
        public uint Blocks { get; private set; }

        public byte[] PvdEscapeSequences { get; private set; }
        public ushort PvdVolumeSetSize { get; private set; }
        public ushort PvdVolumeSequenceNumber { get; private set; }
        public ushort BlocksSize { get; private set; }
        public uint PvdPathTableSize { get; private set; }
        public uint PvdTypeLPathTable { get; private set; }
        public uint PvdTypeOptLPathTable { get; private set; }
        public uint PvdTypeMPathTable { get; private set; }
        public uint PvdTypeOptMPathTable { get; private set; }
        public IsoDirectory RootDirectoryRecord { get; private set; }
        public byte[] PvdVolumeSetId { get; private set; }
        public byte[] PvdPublisherId { get; private set; }
        public byte[] PvdPreparerId { get; private set; }
        public byte[] PvdApplicationId { get; private set; }
        public byte[] PvdCopyrightFileId { get; private set; }
        public byte[] PvdAbstractFileId { get; private set; }
        public byte[] PvdBibliographicFileId { get; private set; }
        public byte[] PvdCreationDate { get; private set; }
        public byte[] PvdModificationDate { get; private set; }
        public byte[] PvdExpirationDate { get; private set; }
        public byte[] PvdEffectiveDate { get; private set; }
        public byte PvdFileStructureVersion { get; private set; }
        public byte[] PvdApplicationData { get; private set; }

        public long ImageOffset { get; private set; }
        public long FsOffset { get; private set; }

        public bool IsPvdEnd => this.PvdFlags == 0xff;
        public int Size { get; private set; }

        public FstFolder RootFolder { get; private set; }

        public override string ToString() => string.Format("ImageOffset:{0}, FsType:{1}", this.ImageOffset.ToString("X"), this.Type.ToString());
    }
}
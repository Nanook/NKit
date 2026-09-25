using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class SectionProcessor : SectionProcessorBase, ISectionProcessor
    {
        private bool _populated; //expensive so only run once
        private readonly ReadMode _mode;

        internal IImageHeader Header { get; private set; }

        private uint _mode1;
        private uint _mode2Form1;
        private uint _mode2Form2;
        private uint _valid;
        private uint _invalid;

        // Per-sector recreatability breakdown for raw 0x930 CD sectors, computed ONCE here (in the
        // parallel Process()) so the dedupe/formatter side can serialize the Sector_Padding_Pack
        // without re-running the expensive reconstruct-and-compare. Null for non-raw sections.
        internal SectorFlags[] SectorPadding { get; private set; }

        // XBox/XBox360 reuse this Iso9660 processor, but a full XBox disc is layered
        // ISO9660(video L0) -> XDVDFS(game) -> ISO9660(video L1). Tag PER SECTION by the file-system
        // type actually being processed: the game partition's FileSystemData is an
        // XDvdFsFileSystemInfo -> [XDVDFS]; the video partitions use a plain ISO9660 FileSystemInfo
        // -> [ISO9660]. Non-XBox systems are always [ISO9660].
        protected override string SectionTag =>
            (this.FileSystemData is Nanook.NKit.Microsoft.XBox.XDvdFsFileSystemInfo)
                ? Nanook.NKit.LogScopes.XDvdFs : Nanook.NKit.LogScopes.Iso9660;

        // Only raw CD sectors (2352-byte, Mode1/Mode2) carry EDC/ECC that IsValid is derived from
        // (see Ecm.Validate, gated on blockSize == 0x930). PS3 (0x800 AES sectors) and XBox have no
        // such per-sector integrity data, so hashOk/creatable are omitted for them.
        protected override bool SectionHasVerifiableHashes =>
            this.Type == AreaType.FileSystem && (this.AreaInfo?.BlockSize ?? 0) == 0x930;

        public SectionProcessor(IImageHeader header, ReadMode mode, IImageInfo imageInfo) : base()
        {
            this.Header = header;
            this.ImageInfo = imageInfo;
            _mode = mode;
        }

        public override void Complete() => base.Complete();

        public void Process()
        {
            if (_populated)
                return;

            _mode1 = 0;
            _mode2Form1 = 0;
            _mode2Form2 = 0;
            _valid = 0;
            _invalid = 0;

            _populated = true;

            //TODO: check if we have the scan info
            //if (_profile.CreateScanInfo)
            processData(_mode == ReadMode.Fix, true);
            base.ParallelChecksumAndCleanse();
            base.XxHashParallel();

        }

        private void processData(bool recover, bool analyse) //recover or convert
        {
            int miGaps = 0; //move through missing data in order to prevent searching every time

            AreaInfo area = ((Buffer)base.Buffer).AreaInfo;
            IFileSystemInfo fsInfo = (IFileSystemInfo)this.FileSystemData;
            long baseOffset = 0;

            if (area != null)
                baseOffset = area?.BaseOffset ?? 0;

            if ((Header as ImageHeader)?.Ps3 != null && area.IsEncryptionSupported)
            {
                if (((Buffer)Buffer).IsEncrypted)
                    ((ImageHeader)Header).Ps3.Decrypt(this.ImageOffset, this.Size, base.Buffer.Encrypted, base.Buffer.Decrypted);
                else
                {
                    ((Buffer)Buffer).IsEncrypted = true; //set first to allow access to encrypted CiBuffer
                    ((ImageHeader)Header).Ps3.Encrypt(this.ImageOffset, this.Size, base.Buffer.Decrypted, base.Buffer.Encrypted);
                }
            }

            base.ProcessData(recover, analyse, baseOffset, si =>
            {
                if (si.File != null)
                {
                    base.AnalyseDataWithMeta(ref miGaps, true, (int)si.File.FsOffset, (int)si.File.FsSize, (o, s, t, n, b) => base.AnalysedItem(si, true, o, s, t, n, b));

                    ((SectionData)si.File).Crc = base.Buffer.CrcFsData((int)si.File.FsOffset, (int)si.File.FsSize);

                    if (this.FileSystemData is FileSystemInfo) //not xbox
                    {
                        si.FileSystems.Clear();
                        List<FsType> types = new List<FsType>(((FstFile)si.FsFile).Links.Select(a => a.FsType));
                        if (((FstFile)si.FsFile).RockRidge != null)
                            types.Add(FsType.RockRidge);
                        if (((FstFile)si.FsFile).Romeo)
                            types.Add(FsType.Romeo);
                        if (((FstFile)si.FsFile).Cdxa)
                            types.Add(FsType.Cdxa);
                        si.FileSystems.AddRange(types.OrderByDescending(a => a).Select(a => a.ToString()));
                    }
                }
                if (si.Gap != null)
                {
                    base.AnalyseDataWithMeta(ref miGaps, false, (int)si.Gap.FsOffset, (int)si.Gap.FsSize, (o, s, t, n, b) => base.AnalysedItem(si, false, o, s, t, n, b));
                    ((SectionData)si.Gap).Crc = base.Buffer.CrcFsData((int)si.Gap.FsOffset, (int)si.Gap.FsSize);
                }

            });
            this.SectorPadding = null;
            if (base.Buffer.Type == AreaType.FileSystem)
                this.IsValid = Ecm.Validate(base.Buffer.Decrypted, base.Buffer.BlockSize, base.Buffer.Size, out _mode1, out _mode2Form1, out _mode2Form2, out _valid, out _invalid);

            // Compute the per-sector recreatability breakdown for RAW 0x930 CD sectors. This is the
            // single, parallel pass; the dedupe formatter serializes the Sector_Padding_Pack from
            // SectorPadding without recomputing. IsCreatable is true only when EVERY sector needs no
            // sync/MSF/EDC/ECC stored (subheader + ext-user-data are content, not recreatability).
            // For non-raw ISO9660 blocks (no header/EDC/ECC) recreatability does not apply -> false.
            if (base.Buffer.Type == AreaType.FileSystem && base.Buffer.BlockSize == SectorPaddingPacker.RawSectorSize && this.Type != AreaType.Audio)
            {
                int sectorCount = (int)(base.Buffer.Size / SectorPaddingPacker.RawSectorSize);
                if (sectorCount > 0)
                {
                    // The first sector's own MSF encodes its physical position (LBA); use it as the
                    // section's start LBA so sync/MSF reconstruction matches the disc.
                    long startLba = Ecm.SectorToLba(base.Buffer.Decrypted, 0);
                    this.SectorPadding = SectorPaddingPacker.Analyse(base.Buffer.Decrypted, sectorCount, startLba);
                    this.IsCreatable = SectorPaddingPacker.IsFullyCreatable(this.SectorPadding);
                }
                else
                    this.IsCreatable = false;
            }
            else
                this.IsCreatable = false;

            if (_mode2Form2 != 0)
            {
                foreach (SectionItem si in this.Items)
                {
                    if (si.File != null)
                    {
                        int blocks = ((int)(si.File.FsSize / this.AreaInfo.BlockFsSize)) + (si.File.FsSize % this.AreaInfo.BlockFsSize == 0 ? 0 : 1);
                        int offset = (int)si.File.Offset - this.AreaInfo.BlockFsOffset;
                        if (this.FileSystemData is FileSystemInfo) //not xbox
                        {
                            for (int i = 0; i < blocks; i++)
                            {
                                if ((this.Buffer.Decrypted.Read8(offset + 0x12) & 0x20) != 0) //start of a file and mode 2 form 2
                                {
                                    ((FstFile)si.FsFile).Mode2Form2Sectors = true;
                                    ((SectionData)si.File).DataType = DataType.Mode2Fm2;
                                    break; //found one
                                }
                                offset += this.AreaInfo.BlockSize;
                            }
                        }
                    }
                }
            }
            if (this.ImageOffset == 0 && !area.IsEncryptionSupported && ((Header as ImageHeader)?.Ps3?.Has3k3yHeader ?? false))
            {
                this.Encrypted.WriteString(Consts.Ps33k3yOffsetHeader, 2, "En");
                this.Decrypted.WriteString(Consts.Ps33k3yOffsetHeader, 2, "En"); //force both to Encrypted for main CRC being calculated for consistent scanning
            }
        }

        public IEnumerable<NonCreatableData> NonCreatableItems => null;

        //Linear CsqThread safe postprocess
        public override void PostProcess()
        {
            if (this.FileSystemData is FileSystemInfo) //not xbox
            {
                FileSystemInfo fsInfo = (FileSystemInfo)base.FileSystemData;
                if (fsInfo != null && this.Type == AreaType.FileSystem)
                {
                    Properties p = this.AreaInfo.Properties;
                    p["Mode1"] = p.Get<long>("Mode1", 0) + _mode1;
                    p["Mode2Form1"] = p.Get<long>("Mode2Form1", 0) + _mode2Form1;
                    p["Mode2Form2"] = p.Get<long>("Mode2Form2", 0) + _mode2Form2;
                }
            }
            base.XxHashLinear();
            base.PostProcess();
        }

        public override void Update()
        {
            _populated = false;
            base.Update();
        }

        public void Write(int fsOffset, Stream fromStream, int size) => this.Buffer.WriteFsFromStream(fsOffset, fromStream, size);

        public void WriteBytes(int fsOffset, byte[] bytes, int offset, int size) => this.Buffer.WriteFs(bytes, offset, size, 0, size, fsOffset, size);

        public void Read(int fsOffset, int size, Stream toStream) => this.Buffer.ReadFsToStream(fsOffset, toStream, size);
        public byte[] ReadBytes(int fsOffset, int size) => this.Buffer.ReadFsBytes(fsOffset, size);

    }
}
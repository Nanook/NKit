using Nanook.GrindCore;
using Nanook.GrindCore.GZip;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit
{
    internal class FixExtractPs3Step : StepBase, IStep
    {
        private class fileMd5
        {
            public long Offset;
            public long Size;
            public byte[] Md5;
            public MD5 CalcMd5;
        }

        private string _name;
        private int SectorSize;
        private const long BdLayerSize = 0x5D3A00000;
        private IStepContext _context;
        private GZipStream _gzStream;
        private MemoryStream _headerStream;
        private MemoryStream _footerStream;
        private string _outName;
        private bool _isHybrid;
        public long _headerSize;
        private bool _headerComplete;
        private bool _filesComplete;
        private List<byte[]> _regionHashes;
        private List<PlayStation3DiscRegion> _regions;
        private MD5 _regionMd5;
        private List<fileMd5> _files;
        private Dictionary<string, fileMd5> _curr;
        private byte[] _sfb;
        private Dictionary<string, string> _sfbInfo;
        private byte[] _sfo;
        private Dictionary<string, string> _sfoInfo;
        private string _updVersion;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Files;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepFixExtractPs3;

        public override string ProposedName() => _outName; //can be left null for OutputType.Files - set for scan output

        internal FixExtractPs3Step(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);
            _headerComplete = false;
            _headerStream = new MemoryStream(0x200000);
            _footerStream = new MemoryStream(0x1000);
            _outName = context.SourceImageName;
            _files = new List<fileMd5>();
            _curr = new Dictionary<string, fileMd5>();
            _regionHashes = new List<byte[]>();
            _sfb = null;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
        }

        private GZipStream createCompressStream(Stream stream) => new GZipStream(stream, new CompressionOptions() { Type = CompressionType.Level9, LeaveOpen = true, Version = CompressionVersion.ZLibNg(ZLibNgVersion.v2_2_1) });

        private void writeHeader(ISection section)
        {
            long size = section.Items.FirstOrDefault(a => !(a.FsFile?.IsSystemFile ?? true))?.File?.Offset ?? section.Size;
            if (_headerStream.Position == 0)
                _gzStream = createCompressStream(_headerStream);

            if (size != 0)
                _gzStream.Write(section.Decrypted, 0, (int)size);

            _headerComplete = size != section.Size;
            if (_headerComplete)
            {
                _headerSize = section.ImageOffset + size;
                _gzStream.Dispose();
                _gzStream = null;
            }
        }

        private void writeFooter(ISection section)
        {
            if (_footerStream.Position == 0)
                _gzStream = createCompressStream(_footerStream);

            int offset = _regions.Count == 0 ? 0 : (int)(_regions[_regions.Count - 1].Offset + _regions[_regions.Count - 1].Size - section.ImageOffset);

            _gzStream.Write(section.Decrypted, offset, (int)(section.Size - offset));

            if (section.ImageOffset + section.Size == _context.ImageSize)
            {
                _gzStream.Dispose();
                _gzStream = null;
            }
        }

        private void saveFileData(ISection section)
        {
            if (section.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in section.Items)
                {
                    if (si.File != null)
                    {
                        if (si.FsFile.IsSystemFile)
                        {
                        }
                        else
                        {
                            if (section.AreaInfo.IsEncrypted && _context.SourceFile.Key == null) //don't save encrypted files when no key
                                throw new HandledException("Encrypted Region with no key");

                            string path = getPath(section, si.FsFile.Path);
                            string imagePath = Path.Combine(path, si.FsFile.Name);

                            long fullFileSize = si.FsFile.SplitParts?.Size ?? si.FsFile.FsSize;
                            _curr.TryGetValue(si.FsFile.FullName, out fileMd5 f);
                            if (f == null)
                            {
                                f = new fileMd5() { Offset = si.FsFile.FsOffset, CalcMd5 = MD5.Create() };
                                _files.Add(f);
                                _curr.Add(si.FsFile.FullName, f);
                                if (_sfbInfo == null && si.FsFile.FullName.ToUpper() == "/PS3_DISC.SFB")
                                {
                                    _regions[0].Offset = si.FsFile.FsOffset;
                                    _regions[0].Size -= si.FsFile.FsOffset;
                                    _sfb = new byte[si.FsFile.FsSize];
                                }
                                else if (_sfoInfo == null && si.FsFile.FullName.ToUpper() == "/PS3_GAME/PARAM.SFO")
                                    _sfo = new byte[si.FsFile.FsSize];
                                else if (_updVersion == null && si.FsFile.FullName.ToUpper() == "/PS3_UPDATE/PS3UPDAT.PUP")
                                {
                                    _regions[_regions.Count - 1].Size = si.FsFile.FsOffset + si.FsFile.FsSize - section.AreaInfo.ImageOffset;
                                    _updVersion = section.Decrypted.ReadString((int)si.File.FsOffset + section.Decrypted.ReadUInt16B((int)si.File.FsOffset + 0x3e), 4);
                                }
                            }

                            if (_sfb != null && _sfbInfo == null && si.FsFile.FullName.ToUpper() == "/PS3_DISC.SFB")
                            {
                                _sfb.Write((int)si.File.OffsetInItem, section.Decrypted, (int)si.File.FsOffset, (int)si.File.FsSize);
                                if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)
                                {
                                    _sfbInfo = PlayStation3.ReadDiscSfb(_sfb);
                                    _sfb = null;
                                }
                            }
                            else if (_sfo != null && _sfoInfo == null && si.FsFile.FullName.ToUpper() == "/PS3_GAME/PARAM.SFO")
                            {
                                _sfo.Write((int)si.File.OffsetInItem, section.Decrypted, (int)si.File.FsOffset, (int)si.File.FsSize);
                                if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)
                                {
                                    _sfoInfo = PlayStation3.ReadParamSfo(_sfo);
                                    _sfo = null;
                                }
                            }
                            else if (!_isHybrid && si.File.OffsetInItem == 0 && string.Compare(@"\BDMV", si.FsFile.FullName, true) == 0)
                                _isHybrid = true;

                            f.Size += si.File.FsSize;
                            bool fileComplete = f.Size == fullFileSize;

                            if (!fileComplete)
                                f.CalcMd5.TransformBlock(section.Decrypted, (int)si.File.Offset, (int)si.File.FsSize, null, 0);
                            else
                            {
                                f.CalcMd5.TransformFinalBlock(section.Decrypted, (int)si.File.Offset, (int)si.File.FsSize);
                                f.Md5 = f.CalcMd5.Hash;
                                f.CalcMd5 = null;
                                _curr.Remove(si.FsFile.FullName);
                            }
                        }
                    }
                }
                if (!_filesComplete)
                    _filesComplete = section.ImageOffset + section.Size >= _regions[_regions.Count - 1].Offset + _regions[_regions.Count - 1].Size;
            }

        }

        private void hashRegion(ISection section)
        {
            if (_regions.Count == 0)
                return;
            if (_regionMd5 == null)
                _regionMd5 = MD5.Create();

            int offset = (int)Math.Max(_regions[0].Offset - section.ImageOffset, 0);

            if (section.ImageOffset + section.Size >= _regions[0].Offset + _regions[0].Size)
            {
                int size = (int)(section.Size - (section.ImageOffset + section.Size - (_regions[0].Offset + _regions[0].Size)));
                _regionMd5.TransformFinalBlock(section.Encrypted, offset, (int)(size - offset)); //Encrypted has Decrypted data when not supported
                _regionHashes.Add(_regionMd5.Hash);
                _regionMd5.Dispose();
                _regionMd5 = null;
                _regions.RemoveAt(0);
            }
            else
                _regionMd5.TransformBlock(section.Encrypted, offset, (int)(section.Size - offset), null, 0);
        }

        private string getPath(ISection section, string path) =>
            //string extra = string.Format("{0}_{1}", section.AreaInfo.ImageOffset.ToString("X9"), section.AreaInfo.Type.ToString());
            path.Trim('\\', '/');

        public void Patched(ISection section)
        {
        }

        public override void Process(ISection section)
        {
            base.Process(section);


            if (section.ImageOffset == 0)
            {
                SectorSize = section.AreaInfo.BlockSize;
                PlayStation3 ps3 = new PlayStation3(section.Decrypted, _context.SourceFile.Key, _context.ImageSize, _context.Settings.AllKeys);
                _regions = new List<PlayStation3DiscRegion>(ps3.Regions);
            }

            if (!_headerComplete)
                writeHeader(section);

            if (_headerComplete)
            {
                if (_sfbInfo == null && section.AreaFileSystem?.Primary != null && section.AreaFileSystem.Primary.Files.FirstOrDefault(a => a.FullName == "/PS3_DISC.SFB") == null)
                    throw new Exception("PS3 image does not contain /PS3_DISC.SFB, IRD can not be created");
                if (!_filesComplete)
                    saveFileData(section); //writes footer if contained with files
                if (_filesComplete)
                    writeFooter(section); //partial footer
                hashRegion(section); //must be after header/file stuff as they modify the region
            }

        }

        public override void ProcessResults()
        {
            writeIrd();

            base.ProcessingComplete();
            base.ProcessResults();
        }

        private void writeIrd()
        {
            _name = _context.SourceFile.Name;

            if (!(_sfbInfo?.TryGetValue("TITLE_ID", out string titleId) ?? false))
            {
                if (!(_sfoInfo?.TryGetValue("TITLE_ID", out titleId) ?? false))
                    throw new HandledException("No Title ID from SFB/SFO");
            }

            if (!(_sfbInfo?.TryGetValue("VERSION", out string discVer) ?? false))
            {
                if (!(_sfoInfo?.TryGetValue("VERSION", out discVer) ?? false))
                    discVer = "\0\0\0\0\0";
            }

            if (!_sfoInfo.TryGetValue("TITLE", out string title))
                title = "\0\0\0\0\0\0\0\0\0";

            if (!_sfoInfo.TryGetValue("APP_VER", out string appVer))
                appVer = "\0\0\0\0\0";

            string sysVer = _updVersion ?? "\0\0\0\0";


            string suffix = $" [{titleId.ToUpper()}] [{this.Context.Scan.Crc:X8}]";
            if (!_name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                _name += suffix;
            base.OutStream.NewPart(_name, "ird", true, true);

            using (MemoryStream ms = new MemoryStream())
            {
                Crc crc = new Crc();
                byte[] tmp16 = new byte[2]; //numbered temps just to be explicit
                byte[] tmp32 = new byte[4];
                byte[] tmp64 = new byte[8];
                using (CryptoStream crcStream = new CryptoStream(new HackStream(ms, true), crc, CryptoStreamMode.Write))
                {
                    using (BinaryWriter bw = new BinaryWriter(crcStream))
                    {
                        int version = 9;
                        byte[] uid = new byte[4];
                        ushort extraConfig = 1;
                        ushort attachments = 0;

                        byte[] pic = generatePic(_context.ImageSize, this.Context.Scan.Crc);
                        byte[] discId = generateId(_context.ImageSize, Iso.Iso9660.Region.NONE);

                        bw.Write(Encoding.ASCII.GetBytes("3IRD"));
                        bw.Write((byte)version);
                        bw.Write(Encoding.ASCII.GetBytes(titleId.Replace("-", "").PadRight(9)), 0, 9);
                        bw.Write(title);
                        bw.Write(Encoding.ASCII.GetBytes(sysVer.PadRight(4)), 0, 4);
                        bw.Write(Encoding.ASCII.GetBytes(discVer.PadRight(5)), 0, 5);
                        bw.Write(Encoding.ASCII.GetBytes(appVer.PadRight(5)), 0, 5);

                        if (version == 7)
                            bw.Write(uid);

                        tmp32.WriteUInt32L(0, (uint)_headerStream.Position);
                        bw.Write(tmp32);
                        bw.Write(_headerStream.ToArray());

                        tmp32.WriteUInt32L(0, (uint)_footerStream.Position);
                        bw.Write(tmp32);
                        bw.Write(_footerStream.ToArray());

                        bw.Write((byte)_regionHashes.Count());
                        foreach (byte[] rgnHash in _regionHashes)
                            bw.Write(rgnHash);

                        bw.Write((uint)_files.Count());
                        foreach (fileMd5 m in _files)
                        {
                            tmp64.WriteUInt64L(0, (ulong)(m.Offset / SectorSize));
                            bw.Write(tmp64);
                            bw.Write(m.Md5);
                        }

                        tmp16.WriteUInt16L(0, extraConfig);
                        bw.Write(tmp16);
                        tmp16.WriteUInt16L(0, attachments);
                        bw.Write(tmp16);

                        if (version >= 9)
                            bw.Write(pic, 0, pic.Length);

                        bw.Write(FixIrd.GenerateD1(_context.SourceFile.Key));
                        bw.Write(FixIrd.GenerateD2(discId));

                        if (version < 9)
                            bw.Write(pic, 0, pic.Length);

                        if (version > 7)
                        {
                            if (this.Context.Scan != null && extraConfig >= 1)
                            {
                                tmp32.WriteUInt32L(0, (uint)this.Context.Scan.Crc);
                                bw.Write(tmp32);
                            }
                            else
                                bw.Write(uid);
                        }
                    }
                }
                tmp32.WriteUInt32L(0, crc.Hash.ReadUInt32B(0));
                ms.Write(tmp32, 0, 4); //ird crc

                using (GZipStream gz = createCompressStream(base.OutStream))
                {
                    ms.Position = 0;
                    ms.CopyTo(gz);
                }
            }
        }

        /// <summary>
        /// Generates a Disc ID given a size and region, where region is a single byte
        /// </summary>
        /// <returns>Valid Disc ID, byte array length 16</returns>
        private byte[] generateId(long size, Iso.Iso9660.Region region = Iso.Iso9660.Region.NONE)
        {
            if (size > BdLayerSize) // if BD-50, Disc ID is fixed
                return [ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
                         0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            else // else if BD-25, Disc ID has a byte referring to disc region
            {
                return [ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
                         0x00, 0x02, 0x00, (byte)region, 0x00, 0x00, 0x00, 0x01 ];
            }
        }

        /// <summary>
        /// Credit to IRDKit by Deterous
        /// Generates the PIC data for a given ISO size in bytes
        /// </summary>
        /// <param name="size">Total ISO size in number of bytes</param>
        /// <param name="layerbreak">Layer break value, byte at which disc layers are split across</param>
        /// <param name="exactIRD">True to generate a PIC in 3k3y style (0x03 at 115th byte for BD-50 discs)</param>
        /// <exception cref="ArgumentException"></exception>
        private byte[] generatePic(long size, uint crc, bool exactIRD = false)
        {
            //validate size
            if (size <= 0 || (size % (long)SectorSize) != 0)
                throw new ArgumentException("ISO Size in bytes must be a positive integer multiple of 2048", nameof(size));

            //generate the PIC based on the size and layerbreakBlock of the ISO
            byte[] pic = new byte[0x73];
            if (size > BdLayerSize) //if BD-50
            {
                //if (_isHybrid)
                //{
                long? layerbreakBlock = _context.Settings.FixData<Playstation3FixData>().GetLayerbreak(crc);
                if (layerbreakBlock == null)
                    layerbreakBlock = BdLayerSize / (long)SectorSize;

                if (layerbreakBlock <= 0 || (layerbreakBlock >= size))
                    throw new ArgumentException("Layerbreak in bytes must be a positive integer less than the ISO Size", nameof(size));
                if (layerbreakBlock >= 2 * BdLayerSize)
                    throw new ArgumentException("Layerbreak block is too large", nameof(size));
                //}

                long l0_start_sector = 0x100000L; //layer 0 start sector
                long l0_end_sector = (long)layerbreakBlock + l0_start_sector - 2; //layer 0 end sector = start sector + layerbreakBlock - 2
                long l1_start_sector = 0x1f00000L - layerbreakBlock.Value; //layer 1 start sector = end of disc - layerbreakBlock + 2

                //total sectors used = num_sectors + Layer 0 start + sectors_between_layers (usually 0x01358C00 - 0x00CA73FE - 3)
                long total_sectors = (size / (long)SectorSize) + l0_start_sector + (l1_start_sector - l0_end_sector - 3);

                pic.WriteUInt16B(0x0, 0x1002); //4098 bytes
                pic.WriteString(0x4, 2, "DI");
                pic.Write8(0x6, 0x1); //v1
                pic.Write8(0x7, 0x10); //units
                pic.WriteUInt32B(0x8, 0x2000); // DI num

                pic.WriteString(0xc, 3, "BDO");
                pic.WriteUInt32B(0xf, 0x1210103); //layers
                pic.WriteUInt32B(0x18, (uint)total_sectors); //total sectors on disc
                pic.WriteUInt32B(0x1c, 0x100000U); //1st Layer sector start location
                pic.WriteUInt32B(0x20, (uint)l0_end_sector); //1st Layer sector end location - 0x00CA73FE for default BD layerbreakBlock of 12219392
                //32 bytes of zeros
                pic.WriteString(0x44, 2, "DI");
                pic.Write8(0x46, 0x1); //v1
                pic.Write8(0x47, 0x11); //units
                pic.WriteUInt32B(0x48, 0x12000); //DI num

                pic.WriteString(0x4c, 3, "BDO");
                pic.WriteUInt32B(0x4f, 0x1210103); //layers
                pic.WriteUInt32B(0x58, (uint)total_sectors); //total sectors on disc
                pic.WriteUInt32B(0x5c, (uint)l1_start_sector); //2nd Layer sector start location, 0x01358C00 for default BD layerbreakBlock of 12219392
                pic.WriteUInt32B(0x60, 0x01effffeU); //2nd Layer sector end location
                //32 bytes of zeros
                if (exactIRD)
                    pic[0x72] = 0x03;
            }
            else //if BD-25
            {
                pic = new byte[0x73];
                pic.WriteUInt16B(0x0, 0x1002); //4098 bytes
                pic.WriteString(0x4, 2, "DI");
                pic.Write8(0x6, 0x1); //v1
                pic.Write8(0x7, 0x08); //units
                pic.WriteUInt32B(0x8, 0x2000); // DI num

                pic.WriteString(0xc, 3, "BDO");
                pic.WriteUInt32B(0xf, 0x1110101); //layers
                pic.WriteUInt32B(0x18, (uint)((size / (long)SectorSize) + 0xFFFFFL)); //total sectors used on disc: num_sectors + layer_sector_end
                pic.WriteUInt32B(0x1c, 0x100000U); //1st Layer sector start location
                pic.WriteUInt32B(0x20, (uint)((size / (long)SectorSize) + 0xFFFFEL)); //layer sector end location: num_sectors + layer_sector_end 0xFFFFE - 0x00CA73FE for default BD layerbreakBlock of 12219392
                //79 bytes of zeros
            }

            return pic;
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class SectionProcessor : SectionProcessorBase, ISectionProcessor
    {
        private WiiUSecurity _security;
        private bool _populated; //expensive so only run once
        private readonly ReadMode _mode;
        internal ImageHeader Header { get; private set; }

        protected override string SectionTag => Nanook.NKit.LogScopes.WiiU;
        // WiiU partition (FileSystem) sections carry verifiable H1/H2/H3 hashes when secured.
        protected override bool SectionHasVerifiableHashes => this.Type == AreaType.FileSystem && (this.AreaInfo?.IsEncryptionSupported ?? false);


        public SectionProcessor(ImageHeader header, ReadMode mode, IImageInfo imageInfo) : base()
        {
            this.Header = header;
            _mode = mode;
            this.ImageInfo = imageInfo;
        }

        public override void Complete() => base.Complete(); //will use decrypted if not encryptable);

        public void Process()
        {
            if (_populated)
                return;

            FileSystemInfo fsInfo = (FileSystemInfo)this.FileSystemData;
            FstBlock fst = fsInfo?.FstBlock;
            ContentHeader cntHeader = base.Buffer.FsIndex == -1 ? null : fst.ContentHeaders[base.Buffer.FsIndex];
            Items = new SectionItems();

            if (Header.EncryptedNoKeyMode)
            {
                if (this.AreaInfo.IsEncrypted)
                    Array.Copy(this.Encrypted, this.Decrypted, this.Size);

                _populated = true;
            }
            else
            {
                //non hashed decryption has already been done linearly by the preprocessor (required if area spans blocks as it required a continious stream)
                bool decrypt = this.AreaInfo.IsEncrypted; //&& this.AreaInfo.HasSecurity;

                long off = AreaOffset;

                if (this.Type == AreaType.Other && (cntHeader?.RepeatedApp ?? false))
                    off = this.AreaOffset % cntHeader.Size;


                if (decrypt) //hashes
                {
                    PartitionType ptype = fsInfo?.Type ?? this.Header.GetNextPartition(this.ImageOffset).Type;
                    _security.Populate(cntHeader, fsInfo?.SiData, ptype, this.Type, base.Buffer.Encrypted, base.Buffer.Decrypted, (int)base.Buffer.Size, this.AreaInfo.IsEncrypted, false, off);

                    //grab the IV to allow block seeking for things using the scan
                    if (this.AreaInfo.IsEncrypted && !this.AreaInfo.HasSecurity && ptype == PartitionType.Game && ((this.Type == AreaType.FileSystem) || (this.Type == AreaType.Other && (cntHeader?.RepeatedApp ?? false))))
                    {
                        _security.MarkDecrypted(); //decrypted by preprocessor
                        byte[] iv = new byte[0x10];
                        iv[1] = (byte)base.Buffer.FsIndex;
                        this.SeekIv = WiiUSecurity.CreateSeekIv(this.Encrypted, this.Decrypted, 0, WiiUSecurityContext.GetActiveEncryptionKey(ptype, this.Type, false, this.Header.Key, fsInfo?.SiData?.KeyTitle), iv);
                    }
                }
                _populated = true;

                //TODO: check if we have the scan info
                //if (_profile.CreateScanInfo)
                //{
                if (decrypt)
                    _security.Decrypt();

                processData(_mode == ReadMode.Fix, true, cntHeader);

                //if (decrypt) //profile needs to be set to control this
                //    _security.Encrypt();
                //}
            }

            base.ParallelChecksumAndCleanse();
            base.XxHashParallel();
        }

        public IEnumerable<NonCreatableData> NonCreatableItems
        {
            get
            {

                switch (this.Type)
                {
                    case AreaType.ImageHeader:
                    case AreaType.PartitionTable:
                    case AreaType.FstBlock:
                    case AreaType.PartitionHeader:
                        yield return new NonCreatableData() { Type = NonCreatableDataType.ImageSection, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = 0, Size = (int)this.Size };
                        break;
                    case AreaType.FileSystem:
                        if (this.AreaInfo.HasSecurity)
                        {
                            if (!this.IsValid) //invalid hashes
                            {
                                for (int i = 0; i < this.Size; i += this.AreaInfo.BlockSize)
                                    yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = i, Size = this.AreaInfo.BlockFsOffset };
                            }
                            else
                            {
                                if (!this.IsCreatable) //valid hashes, but unknown data at the end of the hash
                                {
                                    bool incomplete = _security.UnusedSectors != 0;
                                    int h0SectorsOff = incomplete ? _security.SectorOffset(_security.UsedSectors - (_security.UsedSectors % WiiUConsts.H0Count)) : int.MaxValue; //not used if complete
                                    //H2 entries where there's < 16*16*16 - the block size only covers a full H1 set
                                    for (int off = 0; off < this.Size; off += AreaInfo.BlockSize)
                                    {
                                        if (incomplete)
                                        {
                                            //H0 entries where there's < 16
                                            if (off >= h0SectorsOff)
                                                yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = off + WiiUConsts.H0Offset, Size = WiiUConsts.H0Len };
                                            //H1 entries where there's < 16*16
                                            yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = off + WiiUConsts.H1Offset, Size = WiiUConsts.H1Len };
                                        }

                                        //post hashes data if not nulls
                                        if (!this.Decrypted.Equals(off + WiiUConsts.H2Offset + WiiUConsts.H2Len, 0x40, 0))
                                            yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = off + WiiUConsts.H2Offset + WiiUConsts.H2Len, Size = 0x40 };

                                        //All H2 tables as we only hold enough data to validate 1 hash
                                        yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = off + WiiUConsts.H2Offset, Size = WiiUConsts.H2Len };
                                    }
                                }
                                else //All H2 tables as we only hold enough data to validate 1 hash
                                {
                                    for (int off = 0; off < this.Size; off += AreaInfo.BlockSize)
                                        yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = off + WiiUConsts.H2Offset, Size = WiiUConsts.H2Len };
                                }
                            }
                        }

                        foreach (ISectionItem si in this.Items)
                        {
                            if (si.Gap != null)
                            {
                                if (si.GapInfo?.Count > 0)
                                {
                                    foreach (ISectionData gd in si.GapInfo.Where(a => a.DataType == DataType.Other))
                                        yield return new NonCreatableData() { Type = NonCreatableDataType.Filler, ImageSectionOffset = this.ImageOffset, IsFs = true, Offset = (int)gd.FsOffset, Size = (int)gd.FsSize };
                                }
                                else if (si.Gap.DataType == DataType.Other)
                                    yield return new NonCreatableData() { Type = NonCreatableDataType.Filler, ImageSectionOffset = this.ImageOffset, IsFs = true, Offset = (int)si.Gap.FsOffset, Size = (int)si.Gap.FsSize };
                            }
                        }
                        break;
                    case AreaType.Other:
                        // TODO: check the CRC/XxHash of the previous partition. Assume it's fine for now
                        break;
                }
            }
        }

        //Linear CsqThread safe postprocess
        public override void PostProcess()
        {
            base.XxHashLinear();
            base.PostProcess();
        }

        private void processData(bool recover, bool analyse, ContentHeader cntHeader) //recover or convert
        {
            foreach (MetaData md in MissingData)
            {
                //if (md.Type == MetaDataType.BlockFilled && !recover)
                //{
                //    scrubFill(Decrypted, (int)md.Offset, (int)md.Size, md.ScrubByte);
                //}
            }

            bool creatable = false;
            // Only validate hashes when the section was actually decrypted — calling IsValid on an
            // uninitialized WiiUSecurity (never Populate()d) for a plaintext partition corrupts
            // buffer.Decrypted with stale cipher state, producing wrong bytes and CRC mismatches.
            base.IsValid = (base.AreaInfo.IsEncryptionSupported && base.AreaInfo.IsEncrypted) ? _security.IsValid(false, out creatable) : false;
            this.IsCreatable = creatable;

            int junkPadding = 0;
            int junkOffset = 0;

            if (cntHeader != null && base.Buffer.AreaOffset >= 0 && base.Buffer.Type == AreaType.FileSystem && base.Buffer.ImageOffset + base.Buffer.Size > cntHeader.ImageOffset + cntHeader.Size)
            {
                junkPadding = (int)(base.Buffer.ImageOffset + base.Buffer.Size - (cntHeader.ImageOffset + cntHeader.Size));
                junkOffset = (int)(base.Buffer.Size - junkPadding);
            }

            int miGaps = 0; //move through missing data in order to prevent searching every time

            base.ProcessData(recover, analyse, 0L, si =>
            {
                if (si.File != null)
                {
                    base.AnalyseDataWithMeta(ref miGaps, true, (int)si.File.FsOffset, (int)si.File.FsSize, (o, s, t, n, b) => base.AnalysedItem(si, true, o, s, t, n, b));
                    ((SectionData)si.File).Crc = base.Buffer.CrcFsData((int)si.File.FsOffset, (int)si.File.FsSize);
                }
                if (si.Gap != null)
                {
                    base.AnalyseDataWithMeta(ref miGaps, false, (int)si.Gap.FsOffset, (int)si.Gap.FsSize, (o, s, t, n, b) => base.AnalysedItem(si, false, o, s, t, n, b));
                    ((SectionData)si.Gap).Crc = base.Buffer.CrcFsData((int)si.Gap.FsOffset, (int)si.Gap.FsSize);
                }

            });

            if (Type == AreaType.Other)
            {
                Items.Add(new SectionItem(ImageOffset, AreaOffset, 0, null) { Gap = new SectionData() { FsOffset = 0, OffsetInItem = base.Buffer.FsOffset, FsSize = base.Buffer.FsSize, DataType = DataType.Other } });
            }
        }

        public override void Update()
        {
            _populated = false;
            if (_security == null)
                _security = new WiiUSecurity(this.Header);

            base.Update();
        }

        public void Write(int fsOffset, Stream fromStream, int size) => this.Buffer.WriteFsFromStream(fsOffset, fromStream, size);

        public void WriteBytes(int fsOffset, byte[] bytes, int offset, int size) => this.Buffer.WriteFs(bytes, offset, size, 0, size, fsOffset, size);

        public void Read(int fsOffset, int size, Stream toStream) => this.Buffer.ReadFsToStream(fsOffset, toStream, size);
        public byte[] ReadBytes(int fsOffset, int size) => this.Buffer.ReadFsBytes(fsOffset, size);

    }
}
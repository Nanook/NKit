using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiGc
{
    /// <summary>
    /// Wii Partition Info. Also holds data from the Boot.bin to Fst (Partition Data for Wii, Disc Data for GC)
    /// </summary>
    internal class FileSystemInfo : IFileSystemInfo
    {
        private FstFile _bootBin;
        private FstFile _bi2Bin;
        private FstFile _appLdr;
        private FstFile _mainDol;
        private FstFile _fstBin;
        private readonly long _discSize;
        private readonly ImageInfo _imageInfo;
        private readonly PartitionInfo _partitionTable;
        private readonly ImageHeader _wiiDiscHeader;
        public AreaInfo AreaInfo { get; }


        internal FileSystemInfo(ImageHeader wiiDiscHeader, long imageOffset, byte[] header, ImageInfo imageInfo, byte[] forceJunkId, long imageSize, long srcSize)
        {
            _wiiDiscHeader = wiiDiscHeader;
            Buffer = header;
            _imageInfo = imageInfo;
            _discSize = imageSize;
            _bootBin = null;
            FileSystem = null;
            Type = PartitionType.Game; //default for GC
            ForceJunkId = forceJunkId;
            FilesMoved = false;
            FilesReordered = false;

            Size = _discSize; //defaults
            FsSize = _discSize; //updated if GC Recovery or nkit;
            FsSizeSource = SizeSource = srcSize; //for GC this must be the real size if compacted or nkit
            ImageOffset = imageOffset;
            ImageOffsetData = imageOffset;

            InvalidPartitionData = false;
            InvalidFileSystem = false;

            if (_imageInfo.Type == ImageType.Wii && Buffer != null)
            {
                if (_wiiDiscHeader != null)
                {
                    _partitionTable = _wiiDiscHeader.GetPartition(imageOffset);
                    Type = _partitionTable?.Type ?? PartitionType.Other;
                }

                Size = SizeSource = Buffer.ReadUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset) * _imageInfo.Multiplier;

                int tmdOffset = (int)(Buffer.ReadUInt32B(WiiConsts.WiiPrtHdrTmdPtrOffset) * _imageInfo.Multiplier);
                int h3Offset = (int)(Buffer.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) * _imageInfo.Multiplier);
                if (h3Offset != 0)
                    this.H3Table = Buffer.Read(h3Offset, WiiConsts.WiiPrtHdrH3Size);

                // Determine the common key to use.
                IsRvt = GetIssuer(Buffer) == WiiConsts.RvtIssuer || GetIssuer(Buffer) == WiiConsts.RvtIssuer.Rot13Words(); //Use the RVT-R key.
                IsKorean = !IsRvt && Buffer.Read8(WiiConsts.WiiPrtHdrKoreanOffset) == 1; //Use the Korean Key
                IsRvtH = IsRvt && this.H3Table == null;
                IsRvtR = IsRvt && !IsRvtH;

                if (Size == 0) //assume rvth - set after above IsRVTH test
                    Size = _wiiDiscHeader.GetPartitionSize(imageOffset, _discSize) - Buffer.Length;

                if (_imageInfo.SourceHasHashes)
                    FsSizeSource = Nanook.NKit.Buffer.HashedLenToFsLen(Size, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
                else
                    FsSizeSource = Nanook.NKit.Buffer.HashedLenToFsLen(Size, WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize);
                FsSize = FsSizeSource;
                ImageOffsetData += Buffer.Length;

                if (tmdOffset != 0)
                    ContentSha1 = Buffer.Read(tmdOffset + 0x1e4 + 0x10, 20);

                GetKeys(Buffer, IsRvt, IsKorean, out byte[] common, out byte[] titleKey, out byte[] iv);
                IsWiped = iv.ReadUInt64B(0) == WiiConsts.NKitWipeIV;
                CommonKey = common;
                Key = titleKey;

                DecryptedBlockFilled00 = GetDecryptedBytes(Key, 0x0, 0x0);
                DecryptedBlockFilledFF = GetDecryptedBytes(Key, 0xFF, 0xFF);
                DecryptedBlockFilledFFHashes = GetDecryptedBytes(Key, 0x0, 0xFF);
            }
        }

        public static string GetIssuer(byte[] data) => Encoding.ASCII.GetString(data.Read(WiiConsts.WiiPrtHdrIssuerOffset, 64)).TrimEnd('\0');

        public static void GetKeys(byte[] partHdr, bool isRvt, bool isKorean, out byte[] commonKey, out byte[] key, out byte[] iv)
        {
            int i = isRvt ? 0 : isKorean ? 1 : 2;
            byte[] lame = Convert.FromBase64String(@"oWPrYLjkSisqarQicfReI2GFtU6TKS7krhNIi/LZ7P7FMvtFyLpzFkyB/Juqqn73");
            byte[] l = new byte[lame.Length / 3];
            for (int j = 0; j < l.Length; i += 3)
                l[j++] = lame[i];

            commonKey = l;

            byte[] titleKey = partHdr.Read(WiiConsts.WiiPrtHdrKeyPtrOffset, 16);
            byte[] ivy = partHdr.Read(WiiConsts.WiiPrtHdrIVPtrOffset, 16);
            Array.Clear(ivy, 8, 8);
            iv = ivy;

            if (iv.ReadUInt64B(0) == WiiConsts.NKitWipeIV)
                commonKey = (byte[])WiiConsts.NKitWipeCommonKey.Clone();

            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Key = commonKey;
                aes.IV = iv;
                using (ICryptoTransform cryptor = aes.CreateDecryptor())
                    cryptor.TransformBlock(titleKey, 0, 16, titleKey, 0);
            }
            key = titleKey;
        }

        public static byte[] GetDecryptedBytes(byte[] key, byte ivByte, byte bufferByte)
        {
            byte[] iv = new byte[16];

            if (ivByte != 0x0)
                iv.Clear(0, iv.Length, ivByte);

            return GetDecryptedBytes(key, iv, bufferByte, 16);
        }

        public static byte[] GetDecryptedBytes(byte[] key, byte[] iv, byte bufferByte, int size)
        {
            byte[] bytes = new byte[size];

            if (bufferByte != 0x0)
                bytes.Clear(0, bytes.Length, bufferByte);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Padding = PaddingMode.Zeros;

                using (ICryptoTransform cryptor = aes.CreateDecryptor())
                    cryptor.TransformBlock(bytes, 0, size, bytes, 0);
            }
            if (bytes.Length == size)
                return bytes;

            byte[] bytesOut = new byte[size];
            Array.Copy(bytes, bytesOut, size);
            return bytesOut;
        }

        public bool IsWiped { get; private set; }
        public int NkitVersion { get; private set; }
        public byte[] ForceJunkId { get; private set; }
        public bool AllFoldersParsed => this.FileSystem != null;
        public FidelityFileList FidelityFiles { get; internal set; }

        private IAreaFileSystemView _areaView;
        public IAreaFileSystemView AreaView => _areaView ??= AreaFileSystemView.TryBuild(this);

        public long ImageOffset { get; private set; } //GC is 0
        public long ImageOffsetData { get; private set; } //GC is 0
        public long FsSize { get; private set; } //GC is _discSize
        public long Size { get; private set; } //GC is _discSize

        public long FsSizeSource { get; private set; } //GC is _discSize
        public long SizeSource { get; private set; } //GC is _discSize

        internal List<IFsFile> SystemFiles { get; private set; }

        internal byte[] PreservedHashes { get; set; } //saved hashes because the can't be reproduced

        internal Dictionary<long, long> PreservedHashMap { get; set; }

        public IFileSystem FileSystem { get; private set; }

        public string Id => FstData?.ReadString(0, 4);
        public string Id6 => FstData?.ReadString(0, 6);
        public string Id8 => FstData == null ? null : string.Concat(Id6, DiscNo.ToString("X2"), Revision.ToString("X2"));
        public int DiscNo => FstData?.Read8(6) ?? 0;
        public int Revision => FstData?.Read8(7) ?? 0;
        public string Title => FstData?.ReadStringToNull(0x20, 0x60);
        public byte[] DecryptedBlockFilled00 { get; private set; }
        public byte[] DecryptedBlockFilledFF { get; private set; }
        public byte[] DecryptedBlockFilledFFHashes { get; private set; }

        public long BootBinOffset => WiiConsts.BootBinOffset;
        public long BootBinSize => WiiConsts.BootBinSize;
        public byte[] BootBin => readSystemFile(_bootBin);
        public long Bi2BinOffset => WiiConsts.Bi2BinOffset;
        public long Bi2BinSize => (long)WiiConsts.AppLoaderOffset - WiiConsts.Bi2BinOffset;
        public byte[] Bi2Bin => readSystemFile(_bi2Bin);
        public long AppldrBinOffset => WiiConsts.AppLoaderOffset;
        public long AppldrBinSize { get; private set; }
        public byte[] Appldr => readSystemFile(_appLdr);
        public long MainDolOffset { get; private set; } //can be updated if dol is in the filesystem
        public long MainDolSize { get; private set; }
        public byte[] MainDol => readSystemFile(_mainDol);
        public long FstOffset => (FstData?.ReadUInt32B(WiiConsts.FstPtrOffset) ?? 0) * _imageInfo.Multiplier;
        public long FstSize => (FstData?.ReadUInt32B(WiiConsts.FstSizeOffset) ?? 0) * _imageInfo.Multiplier;
        public byte[] FstBin => readSystemFile(_fstBin);
        public long FstMaxSize => (FstData?.ReadUInt32B(WiiConsts.FstSizeMaxOffset) ?? 0L) * _imageInfo.Multiplier;

        public byte[] JunkId => this.ForceJunkId ?? FstData?.Read(0, 4);
        public long JunkEndNullsOffset => this.FsSize - (this.FsSize % WiiConsts.WiiSectorSize);
        public long JunkLeadingNulls => (long)this.FstData?.Length;

        public long JunkStartFsOffset => FstOffset + FstSize + (FstOffset + FstSize == 0 ? 0L : WiiConsts.DataNullsCount);
        public long JunkPostNullsOffset => this.FsSize - (this.FsSize % WiiConsts.WiiSectorSize);

        public PartitionType Type { get; private set; }
        public byte[] H3Table { get; private set; }

        public bool IsKorean { get; private set; }
        public bool IsRvt { get; private set; }
        public bool IsRvtR { get; private set; }
        public bool IsRvtH { get; private set; }
        public bool InvalidPartitionData { get; private set; }
        public bool InvalidFileSystem { get; private set; }
        public byte[] ContentSha1 { get; private set; }

        public byte[] CommonKey { get; private set; }
        public byte[] Key { get; private set; }

        public byte[] Buffer { get; }

        public bool FilesMoved { get; internal set; }
        public bool FilesReordered { get; internal set; }
        public bool MainDolDupeRemoved { get; private set; }

        private byte[] readSystemFile(FstFile file)
        {
            if (file == null)
                return null;

            return FstData.Read((int)file.FsOffset, (int)file.FsSize);
        }

        internal void SetOutputFileSystem(byte[] fstData, long forceSize) //called when adjusting the filesystem
        {
            if (fstData != null) //Fst base recovery
            {
                List<IFsFile> oldFs = this.FileSystem?.Files;
                bool hasMainDol = this.MainDol != null;

                SystemFiles = null;
                FstData = fstData;
                if (forceSize != -1)
                    Size = FsSize = forceSize;
                process(fstData.Length, -1, forceSize); //set the real fs size

                List<IFsFile> newFs = this.FileSystem?.Files;

                if (oldFs != null && newFs != null)
                {
                    int max = Math.Max(oldFs.Count, newFs.Count);
                    for (int i = 0; i < max && (!this.FilesMoved || !this.FilesReordered); i++)
                    {
                        if (oldFs[i].FullName != newFs[i].FullName)
                            this.FilesReordered = this.FilesMoved = true; //will exit loop
                        else if (!this.FilesMoved || oldFs[i].FsOffset != newFs[i].FsOffset) //same name diff offset
                            this.FilesMoved = true;
                    }
                    if (this.MainDol == null && hasMainDol)
                        this.MainDolDupeRemoved = true;
                }

                //process(fstData.Length, -1, FsSize); //set the real fs size
            }
            else //nkit atm
            {
                this.FilesMoved = true;
            }
        }


        internal bool SetFsInfo(IBuffer buffer, long partitonDataImageOffset)
        {
            if (buffer.AreaInfo.IsEncrypted)
            {
                WiiSecurity wiiSecurity = new WiiSecurity(buffer.Encrypted.Length);
                bool force = this.IsFixFile || this.IsWiiRvzEncryptedPartition;
                wiiSecurity.Populate(Key, buffer.Encrypted, buffer.Decrypted, buffer.Size, force || _imageInfo.SourceHasEncryption, force || _imageInfo.SourceHasEncryptedHashes, !force && !_imageInfo.SourceHasHashes, buffer.AreaOffset, H3Table, null, false);
                wiiSecurity.Decrypt();
            }

            if (buffer.AreaOffset == 0)
            {
                byte[] bootBin = new byte[WiiConsts.BootBinSize];
                buffer.ReadFs(WiiConsts.BootBinOffset, bootBin, 0, bootBin.Length, 0, bootBin.Length, bootBin.Length);

                if (bootBin.ReadUInt32B(0) == 0x00000000) //if bootbin starts with nulls it's bad. This catches the Wii System images that have 32bit incrementing Update Partition filler
                {
                    FstData = bootBin; //Allow the properties on this class to return bootbin data
                    InvalidPartitionData = InvalidFileSystem = true; //WipePartition invalid. make fst invalid also
                    FileSystem = null;
                    return true; //we have all we can get
                }

                long fstPtr = bootBin.ReadUInt32B(WiiConsts.FstPtrOffset);
                long fstSize = bootBin.ReadUInt32B(WiiConsts.FstSizeOffset);
                long fstEnd = (fstPtr + fstSize) * _imageInfo.Multiplier;

                // Guard against overflow or unreasonably large FST (e.g., update partition with no real filesystem)
                //if (fstEnd <= 0 || fstEnd > buffer.FsSize * 1024)
                //{
                //    FstData = bootBin;
                //    InvalidPartitionData = InvalidFileSystem = true;
                //    FileSystem = null;
                //    return true;
                //}

                long sz = fstEnd / buffer.FsSize * buffer.FsSize;
                if (sz < fstEnd)
                    sz += buffer.FsSize; //round up

                FstData = new byte[fstEnd];
            }

            RangeResult rr = new RangeResult();
            buffer.TestFsRange(0, FstData.Length, rr);

            buffer.ReadFs(0, FstData, (int)rr.RangeOffset, FstData.Length, 0, FstData.Length, rr.Size);

            return process(rr.RangeOffset + rr.Size, partitonDataImageOffset, FsSizeSource); //use source size
        }

        //Write the new file offsets to the fst
        public void Complete()
        {
            if (!InvalidFileSystem) //false if WipePartition is invalid also
            {
                bool updateMainDol = this.MainDolOffset > this.FstOffset;
                foreach (FstFile f in FileSystem.Files)
                {
                    if (f.FstPtrOffset >= 0)
                    {
                        if (updateMainDol && FstData.ReadUInt32B((int)(FstOffset + f.FstPtrOffset)) == MainDolOffset) //update the main.dol address if it's in the filesystem
                        {
                            FstData.WriteUInt32B(WiiConsts.DolPtrOffset, (uint)(f.FsOffset / _imageInfo.Multiplier));
                            updateMainDol = false;
                        }
                        FstData.WriteUInt32B((int)(FstOffset + f.FstPtrOffset), (uint)(f.FsOffset / _imageInfo.Multiplier));
                        FstData.WriteUInt32B((int)(FstOffset + f.FstPtrOffset + 4), (uint)f.FsSize);
                    }
                }
            }
            if (_imageInfo.IsNkit && _imageInfo.Type == ImageType.Wii)
            {
                FstData.Clear(WiiConsts.BootBinOffset + WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize, 0x00);
                FstData.WriteUInt16B(WiiConsts.BootBinOffset + WiiConsts.DataHdrEncHashOffset, 0x0101);
                Buffer.WriteUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset, (uint)(Size / _imageInfo.Multiplier));
            }
        }

        internal byte[] FstData
        {
            get;
            private set;
        }
        public bool IsFixFile { get; internal set; }
        public bool IsWiiRvzEncryptedPartition { get; internal set; } //RVT-R system discs may be encoded this way by dolphin

        internal void AdjustPartitionSize(long size)
        {
            Size = size;
            if (_imageInfo.OutputHashes)
                FsSize = Nanook.NKit.Buffer.HashedLenToFsLen(Size, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
            else
                FsSize = Nanook.NKit.Buffer.HashedLenToFsLen(Size, WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize);
        }

        private bool process(long size, long newImageOffset, long newFsSize)
        {
            if (SystemFiles == null)
            {
                _bi2Bin = null;
                _appLdr = null;
                _mainDol = null;
                _fstBin = null;
                SystemFiles = new List<IFsFile>()
                {
                    (_bootBin = new FstFile(null, "__boot.bin", WiiConsts.BootBinOffset, WiiConsts.BootBinSize, -1, false, false) { IsSystemFile = true })
                };

                this.MainDolOffset = (FstData?.ReadUInt32B(WiiConsts.DolPtrOffset) ?? 0) * _imageInfo.Multiplier;

                if (FstData.ReadString(WiiConsts.BootBinOffset + WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrIdOffset, WiiConsts.NKitIdV1.Length) == WiiConsts.NKitIdV1)
                {
                    NkitVersion = 1;
                    if (newImageOffset != -1)
                    {
                        ImageOffset = newImageOffset - (ImageOffsetData - ImageOffset); //new pos minus the header length
                        ImageOffsetData = newImageOffset;
                        if (_partitionTable != null)
                            _wiiDiscHeader.UpdateImageOffset(_partitionTable, ImageOffset);
                    }
                    AdjustPartitionSize(FstData.ReadUInt32B(WiiConsts.BootBinOffset + WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrSrcLenOffset) * _imageInfo.Multiplier);
                    Array.Clear(FstData, WiiConsts.BootBinOffset + WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrIdOffset, WiiConsts.NKitHeaderSize);
                }
            }

            if (_bi2Bin == null && size >= AppldrBinOffset)
                SystemFiles.Add(_bi2Bin = new FstFile(null, "__bi2.bin", Bi2BinOffset, Bi2BinSize, -1, false, false) { IsSystemFile = true });

            if (_appLdr == null && (size >= Math.Min(MainDolOffset != 0 ? MainDolOffset : FstOffset, FstOffset))) //action replay can have zerod maindol
            {
                AppldrBinSize = 0x20 + FstData.ReadUInt32B((int)AppldrBinOffset + 0x14) + FstData.ReadUInt32B((int)AppldrBinOffset + 0x18);
                SystemFiles.Add(_appLdr = new FstFile(null, "__apploader.img", AppldrBinOffset, AppldrBinSize, -1, false, false) { IsSystemFile = true });
            }

            int dolOffsets = 18;
            if (_mainDol == null && MainDolOffset != 0 && size >= MainDolOffset + (dolOffsets * 4))
            {
                MainDolSize = 0;
                for (int i = 0; i < dolOffsets; i++)
                {
                    if (FstData.ReadUInt32B((int)MainDolOffset + 0x0 + (i * 4)) != 0) //7 text offsets, 11 data offsets
                        MainDolSize = Math.Max(MainDolSize, FstData.ReadUInt32B((int)MainDolOffset + 0x0 + (i * 4)) + FstData.ReadUInt32B((int)MainDolOffset + 0x90 + (i * 4)));
                }
                SystemFiles.Add(_mainDol = new FstFile(null, "__main.dol", MainDolOffset, MainDolSize, -1, false, false) { IsSystemFile = true });
            }

            if (_fstBin == null && size >= FstOffset + FstSize)
                SystemFiles.Add(_fstBin = new FstFile(null, "__fst.bin", FstOffset, FstSize, -1, true, false) { IsSystemFile = true });

            //catch datel oversized appLoader
            long minNotZero = MainDolOffset != 0 ? Math.Min(MainDolOffset, FstOffset) : FstOffset;
            if (minNotZero != 0 && AppldrBinOffset + AppldrBinSize > minNotZero)
            {
                ((FstFile)SystemFiles[2]).FsSize = minNotZero - AppldrBinOffset; //Fix wii freeloader
                ((FstFile)SystemFiles[2]).Analysis.FsOffset = SystemFiles[2].FsOffset + SystemFiles[2].FsSize;
            }


            if (size >= FstOffset + FstSize)
            {
                if (newFsSize != -1)
                    setFileSystem(newFsSize);
                return true;
            }
            return false;
        }

        private void setFileSystem(long newFsSize)
        {
            if (_fstBin != null)
            {
                try
                {
                    FileSystem = Fst.Parse(FstData, (int)FstOffset, SystemFiles, _imageInfo.Multiplier);
                    InvalidFileSystem = FileSystem == null;
                }
                catch
                {
                    FileSystem = null;
                    InvalidFileSystem = true;
                }

                IList<IFsFile> files = (IList<IFsFile>)FileSystem?.Files ?? SystemFiles;

                long next = newFsSize;
                bool notSupportedNkit = false;
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    FstFile f = (FstFile)files[i];
                    bool sharedOffset = i > 0 && files[i - 1].FsSize != 0 && f.FsOffset == files[i - 1].FsOffset && f.FsSize == files[i - 1].FsSize;
                    f.Analysis.Initialise(sharedOffset, next);
                    notSupportedNkit |= sharedOffset || f.Analysis.Invalid;
                    next = f.FsOffset;
                }

                // The "not supported NKit" analysis (shared offsets / aligning) applies to the
                // legacy ENCODED NKit FST layout. When the source has already been fully decoded
                // by NKitAsIso the FST is a normal decoded filesystem, so this check does not apply.
                if (_imageInfo.IsNkit && !_imageInfo.IsNkitDecoded && notSupportedNkit)
                    throw new Exception(WiiConsts.NKitExceptionMessage);

                if (InvalidFileSystem)
                    ((FstFile)files[files.Count - 1]).Analysis.ExpectedNulls = 0;

                //DebugFiles();
            }
        }

        internal void DebugFiles()
        {
            StringBuilder sb = new StringBuilder();
            foreach (FstFile f in (IList<IFsFile>)FileSystem?.Files ?? SystemFiles)
                sb.AppendLine(f.Analysis.ToString());

            //Trace.WriteLine(sb.ToString());
        }
    }
}
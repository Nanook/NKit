using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit.Builder
{
    //TODO:
    // - Wipe end of block data
    // - Wipe end of H3 Table
    // - Regen the H3 entry on Patch
    // - Create the parititon table
    // - Test Adding multiple partitions

    //Doesn't handle:
    // - Reusing an existing fst
    // - Having the Main.dol located in the filesystem (not before the fst)
    // - 2 Disc images where the Max Fst size needs to be set differently on both discs

    internal class WiiPartitionBuilder
    {
        private readonly byte[] _hdr;
        private byte[] _bootBin;
        private readonly FstBuilder _fst;
        private readonly WiiPartitionDataWriter _stream;
        private long _offset;
        private long _mainDolOffset;

        private readonly JunkStream _junk;
        public byte[] H3Table { get; private set; }
        public byte[] Key { get; private set; }
        public Aes Aes { get; private set; }
        private readonly long _fsOffset;
        private readonly SHA1 _sha1;
        public long PartitionOffset { get; private set; }
        public long PartitionSize { get; private set; }
        public long PartitionFsSize { get; private set; }
        public PartitionType Type { get; set; }
        public readonly bool IsKorean;
        public readonly bool IsRvt;
        public readonly bool IsRvtR;
        public readonly bool IsRvtH;
        public readonly byte[] ContentSha1;

        public byte[] BootBin => _bootBin;
        public byte[] Bi2Bin { get; private set; }
        public byte[] AppLoaderBin { get; private set; }
        public byte[] MainDol { get; private set; }
        public long FstOffset { get; private set; }
        public long FstReservedSize { get; private set; }

        private void filePad() => streamAlign(4, true);
        private long streamAlign(long align, bool withNulls)
        {
            long pad = 0;

            if (align != 0 && _offset % align != 0)
            {
                pad = align - (_offset % align);
                streamOffset(_offset + pad, withNulls ? 0x1c : 0);
            }
            return pad;
        }

        private long streamOffset(long offset, int nulls)
        {
            long pad = Math.Max(0, offset - _offset);
            if (pad != 0)
            {
                if (nulls != 0)
                {
                    int n = (int)Math.Min(pad, nulls);
                    ByteStream.Zeros.Copy(_stream, n);
                    _offset += n;
                    pad -= n;
                }
                if (pad != 0)
                {
                    _junk.Position = _offset;
                    _junk.Copy(_stream, (int)pad);
                    _offset += pad;
                }
            }
            return pad;
        }

        internal WiiPartitionBuilder(string junkId, Stream output, byte[] ptnHeader, long partitionOffset, PartitionType type)
        {
            _junk = new JunkStream(junkId, 0, WiiConsts.FullSizeWii5);
            Type = type;
            _hdr = ptnHeader;
            _fst = new FstBuilder();
            _offset = 0;
            PartitionOffset = partitionOffset;

            Aes = Aes.Create();
            _sha1 = SHA1.Create();

            Aes.Padding = PaddingMode.None;
            _fsOffset = _hdr.ReadUInt32B(WiiConsts.WiiPrtHdrSizeOffset) * 4L;
            PartitionSize = _hdr.ReadUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset) * 4L;
            PartitionFsSize = Buffer.HashedLenToFsLen(PartitionSize, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);

            int h3Offset = (int)_hdr.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) * 4;
            int tmdOffset = (int)(_hdr.ReadUInt32B(WiiConsts.WiiPrtHdrTmdPtrOffset) * 4);
            //if (h3Offset != 0)
            //    H3Table = _hdr.Read(h3Offset, Consts.WiiPrtHdrH3Size);
            //else
            H3Table = new byte[WiiConsts.WiiPrtHdrH3Size];
            if (tmdOffset != 0)
                ContentSha1 = _hdr.Read(tmdOffset + 0x1e4 + 0x10, 20);

            // Determine the common key to use.
            string issuer = Encoding.ASCII.GetString(_hdr.Read(0x140, 64)).TrimEnd('\0');
            IsRvt = issuer == "Root-CA00000002-XS00000006"; //Use the RVT-R key.
            IsKorean = !IsRvt && _hdr.Read8(WiiConsts.WiiPrtHdrKoreanOffset) == 1; //Use the Korean Key
            IsRvtH = IsRvt && PartitionSize == 0;
            IsRvtR = IsRvt && !IsRvtH;
            if (IsRvtH)
                return; //notsupported

            int i = IsRvt ? 0 : IsKorean ? 1 : 2;
            byte[] lame = Convert.FromBase64String(@"oWPrYLjkSisqarQicfReI2GFtU6TKS7krhNIi/LZ7P7FMvtFyLpzFkyB/Juqqn73");
            byte[] l = new byte[lame.Length / 3];
            for (int j = 0; j < l.Length; i += 3)
                l[j++] = lame[i];

            Aes.Key = l;

            byte[] titleKey = _hdr.Read(WiiConsts.WiiPrtHdrKeyPtrOffset, 16);
            byte[] iv = _hdr.Read(WiiConsts.WiiPrtHdrIVPtrOffset, 16);
            Array.Clear(iv, 8, 8);
            Aes.IV = iv;

            using (ICryptoTransform cryptor = Aes.CreateDecryptor())
                cryptor.TransformBlock(titleKey, 0, 16, titleKey, 0);

            Key = titleKey;
            Aes.Key = Key;

            _stream = new WiiPartitionDataWriter(output, Key, H3Table, 0);

        }

        public void SetDataHeader(byte[] bootBin, byte[] bi2Bin, byte[] appLoaderBin, byte[] mainDol, long reservedFstSize)
        {
            _stream.BaseStream.Write(_hdr, 0, _hdr.Length); //write WipePartition header

            //write WipePartition data sys files
            _bootBin = bootBin;
            Bi2Bin = bi2Bin;
            AppLoaderBin = appLoaderBin;
            MainDol = mainDol;

            _bootBin.WriteUInt16B(WiiConsts.DataHdrEncHashOffset, 0x0000);

            //_offset should still be 0
            write(bootBin, 0, bootBin.Length, WiiConsts.BootBinSize);
            write(bi2Bin, 0, bi2Bin.Length, WiiConsts.AppLoaderOffset);
            write(appLoaderBin, 0, appLoaderBin.Length, 0x20);
            _mainDolOffset = _offset;
            write(mainDol, 0, mainDol.Length, 0x20);
            FstOffset = _offset;

            _bootBin.WriteUInt32B(WiiConsts.DolPtrOffset, (uint)(_mainDolOffset >> 2));
            _bootBin.WriteUInt32B(WiiConsts.FstPtrOffset, (uint)(FstOffset >> 2));
            FstReservedSize = reservedFstSize;
            streamOffset(_offset + reservedFstSize, 0);
            streamAlign(0x20, false);
        }

        public void WriteFile(Stream file, string name, string path, int align, long size)
        {
            streamAlign(align, true);
            WriteFile(file, name, path, size);
        }

        public void WriteFile(Stream file, string name, string path, long size) => WriteFile(file, name, path, 0L, size);

        public void WriteFile(Stream file, string name, string path, long fsOffset, long size)
        {
            streamOffset(fsOffset, WiiConsts.DataNullsCount);
            _fst.AddFile(_offset, name, path, size);
            write(file, size, 0x4);
        }

        //copy stream to output, handle
        private void write(Stream s, long size, int align)
        {
            s.Copy(_stream, (int)size);
            _offset += size;
            filePad();
        }

        public void WriteFile(byte[] f, int offset, string name, string path, int align, int size)
        {
            streamAlign(align, true);
            WriteFile(f, offset, name, path, size);
        }

        public void WriteFile(byte[] f, int offset, string name, string path, int size) => WriteFile(f, offset, name, path, 0L, size);

        public void WriteFile(byte[] f, int offset, string name, string path, long fsOffset, int size)
        {
            streamOffset(fsOffset, WiiConsts.DataNullsCount);
            _fst.AddFile(_offset, name, path, size);
            write(f, offset, size, 0x4);
        }

        //copy stream to output, handle
        private void write(byte[] f, int offset, int size, int align)
        {
            _stream.Write(f, offset, size);
            _offset += size;
            filePad();
        }

        /// <summary>
        /// Complete the WipePartition, write the WipePartition size, h3 table and fst
        /// </summary>
        internal long Complete()
        {

            //mimmic junk and nulls as created by N. The junk stream does this for free if we'd known the WipePartition size upfront
            if (_offset % WiiConsts.WiiSectorFsSize != 0)
            {
                long pad = WiiConsts.WiiSectorFsSize - (_offset % WiiConsts.WiiSectorFsSize);
                long junkPad = WiiConsts.WiiSectorSize - (_offset % WiiConsts.WiiSectorSize);
                if (pad - junkPad > 0)
                    pad -= streamAlign(WiiConsts.WiiSectorSize, true);

                ByteStream.Zeros.Copy(_stream, (int)pad);
                _offset += pad;
            }
            _stream.Dispose(); //write any partial blocks
            PartitionFsSize = _offset;
            PartitionSize = Buffer.FsLenToHashedLen(PartitionFsSize, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
            return PartitionSize;
        }

        /// <summary>
        /// Stream must be seekable and at the start of the WipePartition
        /// </summary>
        internal void Patch(Stream s)
        {
            long offset = s.Position;

            _hdr.WriteUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset, (uint)(PartitionSize >> 2));

            int pad = WiiConsts.DataNullsCount;
            byte[] fst = _fst.ToArray(pad, 2); //add padding
            _bootBin.WriteUInt32B(WiiConsts.FstSizeOffset, (uint)(fst.Length - pad) >> 2);
            _bootBin.WriteUInt32B(WiiConsts.FstSizeMaxOffset, (uint)(fst.Length - pad) >> 2);
            Array.Clear(_bootBin, WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize); //wipe nkit header

            s.Position = offset + (_hdr.ReadUInt32B(WiiConsts.WiiPrtHdrSizeOffset) << 2);
            using (WiiPartitionDataWriter w = new WiiPartitionDataWriter(s, Key, H3Table, PartitionSize))
            {
                w.DataPosition = 0;
                w.Patch(_bootBin, 0, _bootBin.Length);

                w.DataPosition = FstOffset;
                w.Patch(fst, 0, fst.Length);
            }

            //update the H3 table at the end
            _hdr.Write((int)(_hdr.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) << 2), H3Table);
            Sign();
            s.Position = offset;
            s.Write(_hdr, 0, _hdr.Length);
        }

        internal void Sign()
        {
            int tmdSize = (int)_hdr.ReadUInt32B(WiiConsts.WiiPrtHdrTmdSizeOffset);
            int tmdOffset = (int)_hdr.ReadUInt32B(WiiConsts.WiiPrtHdrTmdPtrOffset) << 2;

            _hdr.Clear(tmdOffset + 0x4, 256, 0x00); //clear with trucha signature

            _hdr.Write(tmdOffset + 0x1f4, _sha1.ComputeHash(H3Table)); //tmd Hash (of H3 table)

            ushort i = 0;
            while (_sha1.ComputeHash(_hdr, tmdOffset + 0x140, tmdSize - 0x140)[0] != 0) //fake sign
                _hdr.WriteUInt16L(tmdOffset + 0x19a, ++i);//update reserved bytes to alter the hash - L to match WiiScrubber
        }

    }
}
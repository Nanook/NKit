using Nanook.NKit.Chd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit.Container
{

    internal class ChdAsIso : Stream, IAsIso
    {
        private class chdItem
        {
            public ChdCompressionType CompType;
            public uint? Crc = null; // V3 & V4
            public ushort? Crc16 = null; // V5
            public ulong MiniEncodeRle = 0;
        }

        private byte[] _nulls;
        private byte[] _header;
        private int _version;
        private int _headerSize;
        private Stream _stream;
        private ulong _internalSize;
        private ulong _size;
        private ulong _metaOffset;
        private int _unitBytes;
        public ChdMetaData MetaData { get; private set; }
        public Checksums _verifyChecksums;

        private ImageBlockReader<chdItem> _blockReader;
        private bool _allowSeek;
        private long _position;
        private ContainerType _format;
        private ChdCodecType[] _compressionType;
        private ChdDecompress[] _decompress;
        private byte[] _md5;
        private byte[] _sha1;
        private byte[] _metaDataSha1;
        private MD5 _md5Calc;
        private SHA1 _sha1Calc;
        private uint _sectorSize;
        private uint _sectors;
        private ulong _tableOffset;
        private int _enqueueSize;
        private int _maxThreads;
        private ChdCodec[] _codec;
        private bool _canUseCustomChkSum;
        private string _name;

        private long _tempCacheSize;
        private byte[] _tempCache; //used to store full blocks before removing sub data
        private int _tempCachePos;
        private long _tempCacheImageOffset;

        private long _verifyPos;
        private bool _verifyMode; //process in raw mode as stored
        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public NKitHeader NKitHeader { get; private set; }
        public static IAsIso Create(byte[] id, string name, bool canUseCustomChkSum)
        {
            if (id.ReadString(0, 8) == "MComprHD")
                return new ChdAsIso(name, canUseCustomChkSum);
            return null;
        }

        public ChdAsIso(string name, bool canUseCustomChkSum)
        {
            _verifyPos = 0;
            _canUseCustomChkSum = canUseCustomChkSum;
            _nulls = new byte[0x1000];
            _name = name;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _verifyChecksums = null;
            _stream = stream;
            _allowSeek = allowSeek;
            _enqueueSize = 10;
            _maxThreads = 40;

            _header = _stream.ReadBytes(0x10); //more than we need
            _format = ContainerType.Chd;
            _headerSize = (int)_header.ReadUInt32B(0x8);
            _version = (int)_header.ReadUInt32B(0xc);

            ChdError result = readHeader(_stream, _header.Length, _version);
            if (result != ChdError.CHDERR_NONE)
                throw new Exception($"Error reading CHD v{_version} header - Result:{result}");

            //Seek
            if (_stream.Position != (long)_tableOffset)
                _stream.SafeSeek((long)_tableOffset, SeekOrigin.Begin);

            result = readTable(_stream);
            if (result != ChdError.CHDERR_NONE)
                throw new Exception($"Error reading CHD v{_version} blockTable - Result:{result}");

            //Seek
            //if (_stream.Position != (long)_metaOffset)
            //    _stream.SafeSeek((long)_metaOffset, SeekOrigin.Begin);

            result = readMetaData(_stream); //seeks
            if (result != ChdError.CHDERR_NONE)
                throw new Exception($"Error reading CHD v{_version} metaData - Result:{result}");


            finaliseTable(); //PAD required empty blocks to be inserted


            if (_verifyMode && this.MetaData != null)
                this.MetaData.Tracks = null;

            if (MetaData?.Tracks != null)
                _size = (ulong)MetaData.Tracks.Sum(a => a.Size);
            else
                _size = _internalSize;

            _codec = new ChdCodec[_maxThreads];
            for (int i = 0; i < _maxThreads; i++)
            {
                _codec[i] = new ChdCodec();
                _codec[i].FlacSettings = new CUETools.Codecs.Flake.AudioPCMConfig(16, 2, 44100);
                _codec[i].FlacAudioDecoder = new CUETools.Codecs.Flake.AudioDecoder(_codec[i].FlacSettings);
                _codec[i].FlacAudioBuffer = new CUETools.Codecs.Flake.AudioBuffer(_codec[i].FlacSettings, (int)_sectorSize); //audio CiBuffer to take decoded samples and read them to bytes.
            }

            if (_unitBytes == 0)
                _unitBytes = MetaData?.Tracks == null ? _blockReader.BlockSize : MetaData.Tracks.Max(a => a.BlockSize);

            _tempCache = null;
            _position = 0;

            int blockSize = 0x800;
            if ((MetaData.Tracks?.Count ?? 0) != 0)
                blockSize = MetaData.Tracks.Max(a => a.BlockSize);

            _md5Calc = null;
            _sha1Calc = null;

            verifySetup();

            return 0x200000 + (0x200000 % blockSize == 0 ? 0 : blockSize - (0x200000 % blockSize)); //keep the default size
        }

        private void verifySetup()
        {
            if (_canUseCustomChkSum && this.Checksums.Count != 0)
            {
                ChecksumType type = this.Checksums.ToDictionary().Keys.OrderByDescending(a => (int)a).First();
                if (type == ChecksumType.Md5)
                    _md5Calc = _md5 != null ? MD5.Create() : null;
                else if (type == ChecksumType.Sha1)
                    _sha1Calc = _sha1 != null ? SHA1.Create() : null;
            }
            _verifyMode = _md5Calc != null || _sha1Calc != null;
            if (_verifyMode)
            {
                _verifyChecksums = new Checksums();
                if (_md5Calc != null)
                    _verifyChecksums.Md5 = new byte[0]; //placeholder
                if (_sha1Calc != null)
                    _verifyChecksums.Sha1 = new byte[0];
            }
        }

        private void setTempCache(int size)
        {
            if ((MetaData?.Tracks?.Count ?? 0) != 0)
            {
                if (!MetaData.Tracks.All(a => a.BlockSize == _unitBytes))
                {
                    long minUnit = MetaData.Tracks.Min(a => a.BlockSize);
                    long newSize = ((size / minUnit) + (size % minUnit == 0 ? 0 : 1)) * (long)_unitBytes;
                    if (newSize > size)
                        size = (int)newSize;
                }
            }
            // Grow the cache whenever the required size exceeds the current allocation.
            // (The guard was inverted — `size < _tempCacheSize` only reallocated when shrinking,
            // so a subsequent larger request bumped _tempCacheSize without growing _tempCache,
            // leaving an undersized array that the oversized-sector copy loop then overran:
            // "Source array was not long enough".)
            if (_tempCache == null || size > _tempCache.Length)
                _tempCache = new byte[size];

            _tempCacheSize = size;

        }
        private long dataToFullOffset(long offset, out int partial)
        {
            SourceFileTrack t = MetaData?.Tracks?.FirstOrDefault(a => _position >= a.ImageOffset && _position < a.ImageOffset + a.Size);
            return dataToFullOffset(offset, t, out partial);
        }

        private long dataToFullOffset(long offset, SourceFileTrack t, out int partial)
        {
            if (offset - t.ImageOffset > t.Size)
                offset = t.Size;
            else
                offset -= t.ImageOffset;

            int frame = (int)(offset / t.BlockSize); //partialFrameBytes removed (at start of frame)
            partial = (int)(offset % t.BlockSize);
            offset = (frame * (long)_unitBytes) + partial;
            return (long)t.LogicalOffset + offset;
        }

        public override int Read(byte[] buffer, int offset, int length)
        {
            if (length == 0)
                return 0;

            long fullPosition = _position;
            long origLength = length;
            int partial = 0;
            int pos = offset;

            byte[] buff = buffer;
            int buffPos = offset;
            long buffImageOffset = _position;

            //translate chd blocks to offset
            SourceFileTrack t = MetaData?.Tracks?.FirstOrDefault(a => _position >= a.ImageOffset && _position < a.ImageOffset + a.Size);
            bool cddaEndianSwap = false;

            if (t != null)
            {
                cddaEndianSwap = t.TrackType == IndexTrackType.Audio; //if (((mode == MODE_GDI && input_chd.version() > 4) || (mode == MODE_CUEBIN)) && (trackinfo.trktype == cdrom_file::CD_TRACK_AUDIO))

                if (buffer.Length > _tempCacheSize) //it's changed - reallocate - we must have tracks
                    setTempCache(buffer.Length);

                if (_tempCache != null)
                    _tempCachePos = 0;

                long fullOffset = dataToFullOffset(_position, t, out partial);
                long fullEnd = dataToFullOffset(_position + length, t, out _);

                //logical unit size != image block size
                if (fullOffset != _position || _unitBytes != t.BlockSize)
                {
                    length = (int)(fullEnd - fullOffset);
                    if (_tempCache != null)
                        _tempCacheImageOffset = fullOffset;
                    buff = _tempCache;
                    buffPos = _tempCachePos;
                    buffImageOffset = _tempCacheImageOffset;
                    fullPosition = buffImageOffset - (long)buffPos; //correct for blockreader
                }

                if (_verifyMode)
                {
                    while (_verifyPos < (long)t.LogicalOffset && _verifyPos < fullOffset)
                    {
                        int sz = (int)Math.Min(fullOffset - _verifyPos, _nulls.Length);
                        _md5Calc?.TransformBlock(_nulls, 0, sz, null, 0);
                        _sha1Calc?.TransformBlock(_nulls, 0, sz, null, 0);
                        _verifyPos += sz;
                    }
                    _verifyPos = fullEnd;
                }
            }
            ImageBlockInfo<chdItem> firstGroup;
            int bytesRead = _blockReader.Read(fullPosition, _stream, buff, buffPos, length, null,
                (blockData, threadIdx) =>
                {
                    if (blockData.Info.Type == ImageBlockType.Raw) //compression_type.COMPRESSION_NONE
                        Array.Copy(blockData.Buff, blockData.Result, blockData.Info.FullSize); //possibly only do - if (mapentry.UseCount > 0)
                    else
                    {
                        ChdCompressionType type = blockData.Info.Item.CompType;
                        switch (type)
                        {
                            case ChdCompressionType.COMPRESSION_TYPE_0:
                            case ChdCompressionType.COMPRESSION_TYPE_1:
                            case ChdCompressionType.COMPRESSION_TYPE_2:
                            case ChdCompressionType.COMPRESSION_TYPE_3:

                                //_compressionType[(int)type]  - debug to see compressiontype  -  Use full BlockSize size even for last block that may be shorter
                                ChdError ret = _decompress[(int)type](blockData.Buff, blockData.BuffSize, blockData.Result, _blockReader.BlockSize /*blockData.Info.FullSize*/, _codec[threadIdx]);
                                break;

                            case ChdCompressionType.COMPRESSION_MINI: //RLE
                                blockData.Result.WriteUInt64B(0, blockData.Info.Item.MiniEncodeRle);
                                for (int i = 8; i < blockData.Info.FullSize; i++)
                                    blockData.Result[i] = blockData.Result[i - 8];
                                break;

                            default: //compression_type.COMPRESSION_SELF: //should be replaced on table read. Should then get cached or reread
                                //return chd_error.CHDERR_DECOMPRESSION_ERROR;
                                break;

                        }
                    }
                }, out firstGroup);


            int vfyOff = offset;
            int vfySz = bytesRead;
            buffPos += bytesRead;
            if (buffer == buff)
                offset += buffPos;
            else //we're using oversized chd sectors, copy to the datasize
            {
                int c = 0;
                if (partial != 0)
                {
                    int sz = (int)Math.Min(t.BlockSize - partial, origLength);
                    Array.Copy(buff, 0, buffer, offset, sz);
                    offset += sz;
                    c += t.BlockSize - partial;
                    c += _unitBytes - t.BlockSize;
                }

                while (c < length)
                {
                    int sz = (int)Math.Min(t.BlockSize, origLength - (offset - pos));
                    Array.Copy(buff, c, buffer, offset, sz);
                    offset += sz;
                    c += _unitBytes;
                }
                vfyOff = 0;
                vfySz = buffPos;
            }

            if (_verifyMode)
            {
                // Trace.Write($"{vfySz:x8}");
                _md5Calc?.TransformBlock(buff, vfyOff, vfySz, null, 0);
                _sha1Calc?.TransformBlock(buff, vfyOff, vfySz, null, 0);
            }

            if (cddaEndianSwap)
            {
                byte tmp;
                for (long i = pos; i < offset; i += 2)
                {
                    tmp = buffer[i];
                    buffer[i] = buffer[i + 1];
                    buffer[i + 1] = tmp;
                }
            }

            _position += origLength;
            return (int)origLength;
        }


        private ChdError readHeader(Stream s, long offset, int version)
        {
            _tableOffset = (new ulong[] { 0x4c, 0x50, 0x78, 0x6c, 0x7c })[version - 1]; //default table offset / header size

            byte[] hdr = s.ReadBytes((int)_tableOffset - offset);

            switch (version)
            {
                case 1:
                case 2: //there are no V2 chd's in the wild
                    {
                        _compressionType = [ChdCodecType.CHD_CODEC_ZLIB];

                        const int HARD_DISK_SECTOR_SIZE = 0x200;

                        uint flags = hdr.ReadUInt32B(0x0);
                        uint compression = hdr.ReadUInt32B(0x4);
                        _sectorSize = version != 2
                            ? hdr.ReadUInt32B(0x8) * HARD_DISK_SECTOR_SIZE //v1 - unused in v2
                            : hdr.ReadUInt32B(0x3c); //v2 after parentmd5
                        _sectors = hdr.ReadUInt32B(0xc);
                        uint cylinders = hdr.ReadUInt32B(0x10);
                        uint heads = hdr.ReadUInt32B(0x14);
                        uint sectors = hdr.ReadUInt32B(0x18);
                        _md5 = hdr.Read(0x1c, 0x10);
                        byte[] parentmd5 = hdr.Read(0x2c, 0x10);

                        _internalSize = cylinders * heads * sectors * HARD_DISK_SECTOR_SIZE;
                        this.Checksums = new Checksums();
                        this.Checksums[ChecksumType.Md5] = _md5;
                        break;
                    }
                case 3:
                case 4:
                    {
                        _compressionType = [ChdCommon.CompTypeConv(hdr.ReadUInt32B(0x4))];

                        uint flags3 = hdr.ReadUInt32B(0x0);

                        _sectors = hdr.ReadUInt32B(0x8); // total number of CHD Blocks
                        _internalSize = hdr.ReadUInt64B(0xc);  // total byte size of the image
                        _metaOffset = hdr.ReadUInt64B(0x14);

                        this.Checksums = new Checksums();
                        if (version == 3)
                        {
                            _md5 = hdr.Read(0x1c, 0x10);
                            byte[] parentmd5 = hdr.Read(0x2c, 0x10);
                            _sectorSize = hdr.ReadUInt32B(0x3c);    // length of a CHD Block
                            _sha1 = hdr.Read(0x40, 0x14);
                            byte[] parentsha1 = hdr.Read(0x54, 0x14);
                            this.Checksums[ChecksumType.Md5] = _md5;
                        }
                        else
                        {
                            _sectorSize = hdr.ReadUInt32B(0x1c);    // length of a CHD Block
                            _metaDataSha1 = hdr.Read(0x20, 0x14);
                            byte[] parentsha1 = hdr.Read(0x34, 0x14);
                            _sha1 = hdr.Read(0x48, 0x14);
                        }
                        this.Checksums[ChecksumType.Sha1] = _sha1;
                        break;
                    }
                case 5:
                    {
                        _compressionType = new ChdCodecType[4];
                        for (int i = 0; i < 4; i++)
                            _compressionType[i] = (ChdCodecType)hdr.ReadUInt32B(i << 2);

                        _internalSize = hdr.ReadUInt64B(0x10);  // total byte size of the image
                        _tableOffset = hdr.ReadUInt64B(0x18);
                        _metaOffset = hdr.ReadUInt64B(0x20);

                        _sectorSize = hdr.ReadUInt32B(0x28);    // length of a CHD Hunk (Block)
                        _unitBytes = (int)hdr.ReadUInt32B(0x2c);
                        _sha1 = hdr.Read(0x30, 0x14);
                        _metaDataSha1 = hdr.Read(0x44, 0x14);
                        byte[] parentsha1 = hdr.Read(0x58, 0x14);

                        _sectors = (uint)((_internalSize + _sectorSize - 1) / _sectorSize);

                        this.Checksums = new Checksums();
                        this.Checksums[ChecksumType.Sha1] = _sha1;
                        break;
                    }
                default:
                    throw new HandledException($"CHD version {version} is not supported, only 1 to 5 are currently supported");
            }

            _decompress = new ChdDecompress[_compressionType.Length];
            for (int i = 0; i < _compressionType.Length; i++)
                _decompress[i] = ChdReaders.Get(_compressionType[i]);

            return ChdError.CHDERR_NONE;
        }

        private ChdError readTable(Stream file)
        {
            _blockReader = new ImageBlockReader<chdItem>(true, true, false, _enqueueSize, _maxThreads);

            switch (_version)
            {
                case 1:
                case 2:
                    {
                        byte[] table = file.ReadBytes(_sectors * 0x8);
                        for (int i = 0; i < table.Length; i += 0x8)
                        {
                            ulong val = table.ReadUInt64B(i);
                            ulong offset = val & 0xfffffffffff;
                            int sz = (int)(val >> 44);
                            chdItem itm = new chdItem() { CompType = sz == _sectorSize ? ChdCompressionType.COMPRESSION_NONE : ChdCompressionType.COMPRESSION_TYPE_0 };
                            _blockReader.AddItem(offset, sz, (uint)_sectorSize, itm.CompType == ChdCompressionType.COMPRESSION_NONE ? ImageBlockType.Raw : ImageBlockType.Compressed, itm);
                        }
                        break;
                    }
                case 3:
                case 4:
                    {
                        byte[] table = file.ReadBytes(_sectors * 0x10);
                        for (int i = 0; i < table.Length; i += 0x10)
                        {
                            ulong offset = table.ReadUInt64B(i);
                            int sz = (int)(table.ReadUInt16B(i + 0xc) | (table.Read8(i + 0xe) << 16)); //3 bytes, read 4 then fix
                            ChdFlags mapflag = (ChdFlags)table.Read8(i + 0xf);

                            chdItem itm = new chdItem()
                            {
                                Crc = _version == 4 || (mapflag & ChdFlags.MAP_ENTRY_FLAG_NO_CRC) != 0 ? null : table.ReadUInt32B(i + 0x8), //crc for v3 with no flag set
                                CompType = ChdCommon.ConvMapFlagstoCompressionType(mapflag)
                            };
                            _blockReader.AddItem(offset, sz, (uint)_sectorSize, ImageBlockType.Compressed, itm); //block type set by cleanseCompressionTypes()
                        }
                        cleanseCompressionTypes();
                        break;
                    }
                case 5:
                    {
                        if (_compressionType[0] == ChdCodecType.CHD_CODEC_NONE) //not compressed
                        {
                            byte[] table = file.ReadBytes(_sectors * 0x4);
                            for (int i = 0; i < table.Length; i += 0x4)
                            {
                                ulong offset = table.ReadUInt32B(i);
                                chdItem itm = new chdItem() { CompType = ChdCompressionType.COMPRESSION_NONE };
                                _blockReader.AddItem(offset, (int)_sectorSize, (uint)_sectorSize, itm.CompType == ChdCompressionType.COMPRESSION_NONE ? ImageBlockType.Raw : ImageBlockType.Compressed, itm);
                            }
                        }
                        else
                            v5CompressedTable(file);
                        break;
                    }
                default:
                    throw new HandledException($"CHD version {_version} is not supported, only 1 to 5 are currently supported");
            }

            return ChdError.CHDERR_NONE;
        }

        private ChdError v5CompressedTable(Stream file)
        {
            /* read the reader */
            byte[] hdr = file.ReadBytes(0x10);
            uint mapbytes = hdr.ReadUInt32B(0);
            ulong firstoffs = hdr.ReadUInt64B(0x4) >> 16; //6 bytes, read 8 then fix
            ushort mapcrc = hdr.ReadUInt16B(0xa);
            byte lengthbits = hdr.Read8(0xc);
            byte selfbits = hdr.Read8(0xd);
            byte parentbits = hdr.Read8(0xe);
            //hdr.Read8(0xf);                       //15 not used

            byte[] compressed_arr = file.ReadBytes(mapbytes);

            BitStream bitbuf = new BitStream(compressed_arr, 0, (int)mapbytes);

            /* first decode the compression types */
            Huffman decoder = new Huffman(16, 8, bitbuf);
            if (decoder == null)
                return ChdError.CHDERR_OUT_OF_MEMORY;

            HuffmanError err = decoder.ImportTreeRle();
            if (err != HuffmanError.HUFFERR_NONE)
                return ChdError.CHDERR_DECOMPRESSION_ERROR;

            int repcount = 0;
            ChdCompressionType lastcomp = 0;
            for (uint blockIndex = 0; blockIndex < _sectors; blockIndex++)
            {
                chdItem itm = new chdItem();
                if (repcount > 0)
                    repcount--;
                else
                {
                    ChdCompressionType val = (ChdCompressionType)decoder.DecodeOne();
                    if (val == ChdCompressionType.COMPRESSION_RLE_SMALL)
                        repcount = 2 + (int)decoder.DecodeOne();
                    else if (val == ChdCompressionType.COMPRESSION_RLE_LARGE)
                        repcount = 2 + 16 + ((int)decoder.DecodeOne() << 4) + (int)decoder.DecodeOne();
                    else
                        lastcomp = val;
                }
                itm.CompType = lastcomp;
                _blockReader.AddItem(0, 0, (uint)_sectorSize, ImageBlockType.Compressed, itm); //placeholder vals - set in next loop
            }

            /* then iterate through the hunks and extract the needed data */
            uint last_self = 0;
            ulong last_parent = 0;
            ulong curoffset = firstoffs;
            // for (uint blockIndex = 0; blockIndex < _sectors; blockIndex++)
            foreach (ImageBlockInfo<chdItem> block in _blockReader.Items)
            {
                ulong offset = curoffset;
                uint sz = 0;
                switch (block.Item.CompType)
                {
                    /* base types */
                    case ChdCompressionType.COMPRESSION_TYPE_0:
                    case ChdCompressionType.COMPRESSION_TYPE_1:
                    case ChdCompressionType.COMPRESSION_TYPE_2:
                    case ChdCompressionType.COMPRESSION_TYPE_3:
                        curoffset += sz = bitbuf.read(lengthbits);
                        block.Item.Crc16 = (ushort)bitbuf.read(0x10);
                        break;

                    case ChdCompressionType.COMPRESSION_NONE:
                        curoffset += sz = _sectorSize;
                        block.Item.Crc16 = (ushort)bitbuf.read(0x10);
                        break;

                    case ChdCompressionType.COMPRESSION_SELF:
                        last_self = (uint)(offset = bitbuf.read(selfbits));
                        break;

                    /* pseudo-types; convert into base types */
                    case ChdCompressionType.COMPRESSION_SELF_1:
                        last_self++;
                        goto case ChdCompressionType.COMPRESSION_SELF_0;

                    case ChdCompressionType.COMPRESSION_SELF_0:
                        block.Item.CompType = ChdCompressionType.COMPRESSION_SELF;
                        offset = last_self;
                        break;

                    case ChdCompressionType.COMPRESSION_PARENT_SELF:
                        block.Item.CompType = ChdCompressionType.COMPRESSION_PARENT;
                        last_parent = offset = ((ulong)block.Index) * ((ulong)_sectorSize) / (ulong)_unitBytes;
                        break;

                    case ChdCompressionType.COMPRESSION_PARENT:
                        offset = bitbuf.read(parentbits);
                        last_parent = offset;
                        break;

                    case ChdCompressionType.COMPRESSION_PARENT_1:
                        last_parent += _sectorSize / (ulong)_unitBytes;
                        goto case ChdCompressionType.COMPRESSION_PARENT_0;
                    case ChdCompressionType.COMPRESSION_PARENT_0:
                        block.Item.CompType = ChdCompressionType.COMPRESSION_PARENT;
                        offset = last_parent;
                        break;
                }
                block.Offset = offset;
                block.Size = (int)sz;
            }

            ///* verify the final CRC */
            //byte[] rawmap = new byte[_sectors * 12];
            //for (int blockIndex = 0; blockIndex < _sectors; blockIndex++)
            //{
            //    ImageBlockInfo<chdItem> blk = _blockReader.Items[blockIndex];
            //    int rawmapIndex = blockIndex * 12;
            //    rawmap.WriteUInt16B(rawmapIndex + 10, (ushort)blk.Item.Crc16);
            //    rawmap.WriteUInt64B(rawmapIndex + 2, blk.Offset); //only 6 bytes first 2 will be overwritten
            //    rawmap.WriteUInt32B(rawmapIndex, (uint)blk.Size); //only 3 bytes first byte will be overwritten
            //    rawmap[rawmapIndex] = (byte)blk.Item.CompType;
            //}
            //if (CRC16.calc(rawmap, (int)_sectorSize * 12) != mapcrc)
            //    return chd_error.CHDERR_DECOMPRESSION_ERROR;

            cleanseCompressionTypes();

            return ChdError.CHDERR_NONE;
        }

        private void finaliseTable() =>
            //long sz = MetaData?.Tracks?.Sum(a => (long)a.FullTrackDataSize) ?? 0;
            _blockReader.CompletedAddItems(false, 0, (int)_sectorSize, _internalSize, false);


        private void cleanseCompressionTypes() //set block type and replace self items for dupe blocks
        {
            foreach (ImageBlockInfo<chdItem> inf in _blockReader.Items)
            {
                if (inf.Item.CompType == ChdCompressionType.COMPRESSION_MINI)
                {
                    inf.Item.MiniEncodeRle = inf.Offset; //offset is repeated for block
                    inf.Offset = 0; //null it
                    inf.Size = 0; //nothing to read from image
                    inf.Type = ImageBlockType.Missing;
                }
                else if (inf.Item.CompType == ChdCompressionType.COMPRESSION_NONE)
                    inf.Type = ImageBlockType.Raw;
                else if (inf.Item.CompType == ChdCompressionType.COMPRESSION_SELF) //replace with original
                {
                    ImageBlockInfo<chdItem> ptr = _blockReader.Items[(int)inf.Offset];
                    inf.Offset = ptr.Offset;
                    inf.Size = ptr.Size;
                    inf.Item.Crc = ptr.Item.Crc;
                    inf.Type = ptr.Type;
                    inf.Item.CompType = ptr.Item.CompType;
                }
                else
                    inf.Type = ImageBlockType.Compressed;
            }
        }

        private ChdError readMetaData(Stream file)
        {
            // List<byte[]>metaHashes contains the byte data that is hashed below to validate the meta data
            // each metaHash is 24 bytes:
            // 0-3  : is the byte data for the metaTag
            // 4-23 : is the SHA1 of the metaData

            List<byte[]> metaData = new List<byte[]>();
            List<byte[]> metaHashes = new List<byte[]>();

            // loop over the metadata, until metaoffset=0
            ulong off = _metaOffset;
            while (off != 0)
            {
                file.SafeSeek((long)off, SeekOrigin.Begin);

                byte[] meta = file.ReadBytes(0x10);
                uint metaTag = meta.ReadUInt32B(0);
                uint metaLength = meta.ReadUInt32B(0x4);
                ulong metaNext = meta.ReadUInt64B(0x8);
                uint metaFlags = metaLength >> 24;
                metaLength &= 0x00ffffff;

                byte[] md = new byte[3 + 1 + 4 + 1 + metaLength];
                md.WriteString(0, 9, "TAG:     ");
                md.WriteUInt32B(4, metaTag);
                file.Read(md, md.Length - (int)metaLength, (int)metaLength);
                metaData.Add(md);

                // take the 4 byte metaTag, and the metaData
                // SHA1 the metaData to 20 byte SHA1
                // metadata_hash return these 24 bytes in a byte[24]
                if ((metaFlags & 0x01) != 0) //0x01 = CHD_MDFLAGS_CHECKSUM
                    metaHashes.Add(metadataHash(metaTag, md, md.Length - (int)metaLength, (int)metaLength));

                // set location of next meta data entry in the CHD (set to 0 if finished.)
                off = metaNext;
            }

            if (_metaDataSha1 == null)
                return ChdError.CHDERR_NONE;

            // binary sort the metaHashes
            metaHashes.Sort(Util.ByteArrCompare);

            // build the final SHA1
            // starting with the 20 byte rawsha1 from the main CHD data
            // then add the 24 byte for each meta data entry
            using SHA1 sha1Total = SHA1.Create();
            sha1Total.TransformBlock(_sha1, 0, _sha1.Length, null, 0);

            for (int i = 0; i < metaHashes.Count; i++)
                sha1Total.TransformBlock(metaHashes[i], 0, metaHashes[i].Length, null, 0);

            byte[] tmp = new byte[0];
            sha1Total.TransformFinalBlock(tmp, 0, 0);

            // compare the calculated metaData + rawData SHA1 with sha1 from the CHD header
            if (!Util.ByteArrEquals(_metaDataSha1, sha1Total.Hash))
                return ChdError.CHDERR_INVALID_METADATA;


            if (metaData?.Count != 0)
                MetaData = ChdMetaData.Parse(_name, metaData.Select(a => Encoding.ASCII.GetString(a).TrimEnd('\0')), _unitBytes, (long)_internalSize);

            return ChdError.CHDERR_NONE;
        }
        private byte[] metadataHash(uint metaTag, byte[] metaData, int offset, int count)
        {
            // make 24 byte metadata hash
            // 0-3  :  metaTag
            // 4-23 :  sha1 of the metaData

            byte[] metaHash = new byte[24];
            metaHash.WriteUInt32B(0, metaTag);
            using SHA1 sha1 = SHA1.Create();
            byte[] metaDataHash = sha1.ComputeHash(metaData, offset, count);

            for (int i = 0; i < 20; i++)
                metaHash[4 + i] = metaDataHash[i];

            return metaHash;
        }

        public Checksums CustomChecksums() => _verifyChecksums;
        public void SetRemovedBlock(Action<MetaData> setBlock)
        {
        }

        public void Complete()
        {
            if (_verifyMode)
            {
                while (_verifyPos < (long)_internalSize)
                {
                    int sz = (int)Math.Min((long)_internalSize - _verifyPos, _nulls.Length);
                    _md5Calc?.TransformBlock(_nulls, 0, sz, null, 0);
                    _sha1Calc?.TransformBlock(_nulls, 0, sz, null, 0);
                    _verifyPos += sz;
                }

                byte[] tmp = new byte[0];
                _md5Calc?.TransformFinalBlock(tmp, 0, 0);
                _sha1Calc?.TransformFinalBlock(tmp, 0, 0);

                if (_md5Calc != null)
                    _verifyChecksums[ChecksumType.Md5] = _md5Calc.Hash;
                if (_sha1Calc != null)
                    _verifyChecksums[ChecksumType.Sha1] = _sha1Calc.Hash;
                _verifyChecksums.Size = (long)_internalSize;
            }
        }

        public long Size => (long)_size;

        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Chd)}v{_version} size 0x{_size:X} block 0x{_sectorSize:X} blocks {_sectors}"
            + $" table@0x{_tableOffset:X} meta@0x{_metaOffset:X}"
            + (_sha1 != null ? $" sha1 {_sha1.ToHexString().Substring(0, 8)}…" : _md5 != null ? $" md5 {_md5.ToHexString().Substring(0, 8)}…" : "");

        public bool SeekRequired => false;

        public Checksums Checksums { get; private set; }

        protected override void Dispose(bool disposing)
        {
            try { _md5Calc?.Dispose(); } catch { }
            try { _sha1Calc?.Dispose(); } catch { }
            base.Dispose(disposing);
        }

        public override void Flush() => _stream.Flush();

        public override long Position { get => _position; set => this.Seek(value, SeekOrigin.Begin); }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current)
                _position += offset;
            else if (origin == SeekOrigin.End)
                throw new NotSupportedException();
            else
                _position = offset;

            _verifyMode = _canUseCustomChkSum && dataToFullOffset(_position, out _) == _verifyPos; //disable and reenable verify checksumming on seek and return
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => this.Size;
    }
}
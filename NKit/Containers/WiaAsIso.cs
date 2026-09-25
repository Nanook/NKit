using Nanook.GrindCore.Lzma;
using Nanook.NKit.Nintendo.WiiGc;
using SharpCompress.Compressors.BZip2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    internal class WiaAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private long _size;
        private ContainerType _format;
        private Action<MetaData> _setBlock;

        private ImageBlockReader<WiaGroup> _blockReader;

        private byte[] _header;
        private byte[] _encBuff;

        private byte[] _partitionH3; //used to cache WipePartition header

        private long _length;
        private int _chunkSize;
        private int _chunkSizePtn;
        private int _compressionType;
        private byte[] _compressionBytes;
        private byte[] _lzmaProperties;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public NKitHeader NKitHeader { get; private set; }

        public long Size => _size;

        public string FormatSummary =>
            $"{LogScopes.Tag(LogScopes.Wia)}size 0x{_size:X} physical 0x{_length:X} chunk 0x{_chunkSize:X} comp {_compressionType}"
            + $" nkitHdr:{(NKitHeader != null ? "y" : "n")}";

        public bool SeekRequired => true;

        public Checksums Checksums { get; }

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "WIA\u0001")
                return new WiaAsIso();
            return null;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _lzmaProperties = new byte[] { 0x5d, 0x80, 0x49, 0, 0 };
            _stream = stream;
            _allowSeek = allowSeek;

            _blockReader = new ImageBlockReader<WiaGroup>(true, true, true, 1, 1);

            _format = ContainerType.Wia;
            byte[] wiaFileHead = _stream.ReadBytes(0x48); //wia_file_head_t

            uint wiaDiscSize = wiaFileHead.ReadUInt32B(0xc);
            _size = (long)wiaFileHead.ReadUInt64B(0x24);
            _length = (long)wiaFileHead.ReadUInt64B(0x2c);
            byte[] wiaDisc = _stream.ReadBytes(wiaDiscSize); //wia_disc_t
            //this.DiscType = wiaDisc.ReadUInt32B(0x0) == 1 ? DiscType.GameCube : DiscType.Wii;
            _compressionType = (int)wiaDisc.ReadUInt32B(0x4);
            _chunkSize = (int)wiaDisc.ReadUInt32B(0xc);
            _chunkSizePtn = _chunkSize / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorFsSize; //0x8000 to 0x7c00
            _encBuff = new byte[WiiConsts.WiiGroupSize];

            byte[] buffer = new byte[Math.Max(_chunkSize, WiiConsts.WiiGroupSize) + (0x400 * 0x400 * 2)]; //arbitrary padding

            int partitions = (int)wiaDisc.ReadUInt32B(0x90);
            int prtSize = (int)wiaDisc.ReadUInt32B(0x94);
            long prtOffset = (long)wiaDisc.ReadUInt64B(0x98);

            int rawDataItems = (int)wiaDisc.ReadUInt32B(0xb4);
            long rawDataItemsOffset = (long)wiaDisc.ReadUInt64B(0xb8);
            int rawDataItemsSize = (int)wiaDisc.ReadUInt32B(0xc0);

            int groupsItems = (int)wiaDisc.ReadUInt32B(0xc4);
            long groupsOffset = (long)wiaDisc.ReadUInt64B(0xc8);
            int groupsSize = (int)wiaDisc.ReadUInt32B(0xd0);

            _compressionBytes = wiaDisc.Read(0xd5, (int)wiaDisc.Read8(0xd4));

            _stream.SafeSeek(rawDataItemsOffset, SeekOrigin.Begin);
            readUnpack(_stream, rawDataItemsSize, buffer);

            //decompress
            List<WiaItem> areas = new List<WiaItem>();
            for (int i = 0; i < rawDataItems; i++)
            {
                WiaItem wi = new WiaItem() { Index = i, Offset = (long)buffer.ReadUInt64B((i * 0x18) + 0x0), Size = (long)buffer.ReadUInt64B((i * 0x18) + 0x8), GroupIndex = (int)buffer.ReadUInt32B((i * 0x18) + 0x10), Groups = (int)buffer.ReadUInt32B((i * 0x18) + 0x14) };
                areas.Add(wi);
            }

            _stream.SafeSeek(groupsOffset, SeekOrigin.Begin);
            readUnpack(_stream, groupsSize, buffer);

            //decompress
            List<WiaGroup> groups = new List<WiaGroup>();
            for (int i = 0; i < groupsItems; i++)
            {
                WiaGroup wg = new WiaGroup() { Index = i, Offset = buffer.ReadUInt32B((i * 8) + 0x0) * 4L, Size = buffer.ReadUInt32B((i * 8) + 0x4) };
                groups.Add(wg);
            }

            _stream.SafeSeek(prtOffset, SeekOrigin.Begin);
            for (int i = 0; i < partitions; i++)
            {
                byte[] wiaPartKey = _stream.ReadBytes(0x10); //wia_part_t
                int partitionDataSegments = (prtSize - 0x10) / 0x10; //2
                List<WiaItem> items = new List<WiaItem>();
                for (int s = 0; s < partitionDataSegments; s++)
                {
                    byte[] wiaPartSeg = _stream.ReadBytes(0x10); //wia_part_data_t
                    WiaItem wi = new WiaItem()
                    {
                        Index = s,
                        Offset = wiaPartSeg.ReadUInt32B(0x0) * (long)WiiConsts.WiiSectorSize,
                        Size = wiaPartSeg.ReadUInt32B(0x4) * (long)WiiConsts.WiiSectorSize,
                        GroupIndex = (int)wiaPartSeg.ReadUInt32B(0x8),
                        Groups = (int)wiaPartSeg.ReadUInt32B(0xc)
                    };
                    items.Add(wi);
                }
                areas.Add(new WiaPartition(i, wiaPartKey, items.ToArray()));
            }

            _header = wiaDisc.Read(0x10, 0x80);
            _partitionH3 = new byte[WiiConsts.WiiPrtHdrH3Size];

            areas = areas.OrderBy(a => a.Offset).ToList();

            //fix wia/rvz header issue - fix offsets. When reading this area the Header must be copied over the first part of the block
            if (areas[0].Offset == _header.Length)
            {
                areas[0].Offset -= _header.Length;
                areas[0].Size -= _header.Length;
            }

            foreach (WiaItem itm in areas)
            {
                foreach (WiaItem seg in (itm as WiaPartition)?.Segments ?? new WiaItem[] { itm })
                {
                    for (int i = 0; i < seg.Groups; i++)
                    {
                        WiaGroup wg = groups[seg.GroupIndex + i];
                        wg.ParentItem = itm;
                        _blockReader.AddItem((ulong)wg.Offset, (int)wg.Size, (ulong)seg.Offset + ((ulong)i * (ulong)_chunkSize), wg.Size == 0 ? ImageBlockType.Missing : ImageBlockType.Compressed, wg);
                    }
                }
            }

            _blockReader.CompletedAddItems(false, 0, _chunkSize, (ulong)_size, true);

            _position = 0;
            return 0; //add an abitary 1k per group- for compression
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return 0;

            ImageBlockInfo<WiaGroup> firstGroup;
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count,
                (off, size, type) =>
                {
                    if (type == ImageBlockType.Missing && _setBlock != null)
                        _setBlock(new MetaData(off, size, MetaDataType.Fill, 0x0, null));
                },
                (blockData, threadId) =>
                {
                    WiaPartition ptn = blockData.Info?.Item?.ParentItem as WiaPartition;
                    if (blockData.Info.Type == ImageBlockType.Compressed)
                    {
                        int unpackSize;
                        if (ptn == null)
                            unpackSize = unpack(blockData.Buff, blockData.Result, blockData.Info.Size, blockData.Info.FullSize, false);
                        else
                        {
                            unpackSize = unpack(blockData.Buff, blockData.Temp, blockData.Info.Size, blockData.Info.FullSize, true);
                            decode(blockData.Temp, unpackSize, blockData.Result, (long)blockData.Info.FullOffset, ptn); //set the expanded group size
                        }
                    }
                    else if (blockData.Info.Type == ImageBlockType.Missing)
                        Array.Clear(blockData.Result, 0, blockData.Info.FullSize);
                    else
                        Array.Copy(blockData.Buff, blockData.Result, blockData.Info.FullSize);

                    //handle 0x80 header not being part of the data!!
                    if (blockData.Info.FullOffset == 0)
                        Array.Copy(_header, blockData.Result, _header.Length);
                }, out firstGroup);

            offset += bytesRead;
            WiaPartition ptn;
            //if is ptn header
            if (firstGroup?.Item?.ParentItem is not WiaPartition && firstGroup.Index + 1 < _blockReader.Items.Count && (ptn = _blockReader.Items[firstGroup.Index + 1].Item?.ParentItem as WiaPartition) != null && count > WiiConsts.WiiPrtHdrH3PtrOffset + 4 && offset >= (buffer.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) << 2) + WiiConsts.WiiPrtHdrH3Size) //test for first block before WipePartition data (the header)
                Array.Copy(buffer, buffer.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) << 2, _partitionH3, 0, WiiConsts.WiiPrtHdrH3Size); //copy the h3 table of the most recently read WipePartition

            _position += (long)bytesRead;
            return bytesRead;
        }

        private byte[] readUnpack(Stream stream, int size, byte[] buffer)
        {
            byte[] data = stream.ReadBytes(size);
            unpack(data, buffer, size, buffer.Length, false);
            return data;
        }

        private int unpack(byte[] srcData, byte[] dstData, int size, int fullSize, bool partition)
        {
            int destDataLength = dstData.Length;

            switch (_compressionType)
            {
                case 1: //purge
                    int writePos = 0;
                    int badBytes = 0;

                    if (partition) //calc bad hash size
                    {
                        badBytes = countExceptionBytes(srcData);
                        Array.Copy(srcData, dstData, badBytes);
                    }

                    int readPos = badBytes;

                    while (readPos < size - 20) //-20 to skip hash
                    {
                        int tmp = (int)srcData.ReadUInt32B(readPos);
                        readPos += 4;
                        Array.Clear(dstData, writePos + badBytes, tmp - writePos);
                        writePos += tmp - writePos;
                        tmp = (int)srcData.ReadUInt32B(readPos);
                        readPos += 4;
                        Array.Copy(srcData, readPos, dstData, writePos + badBytes, tmp);
                        readPos += tmp;
                        writePos += tmp;
                    }

                    int diff = (partition ? (fullSize / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorFsSize) : fullSize) - writePos;
                    Array.Clear(dstData, writePos + badBytes, diff);
                    writePos += diff;

                    // Return the same length convention as the NONE path (data_size = exception
                    // bytes + FS data), so decode() computes the same fullSize/group count. Returning
                    // only the FS size understated a SHORT partition group's length: decode's
                    // FsLenToHashedLen rounding then dropped a group (groups 2->1), producing wrong
                    // hashes for the first (partial) group of a partition. Full-size chunks were
                    // unaffected (the missing badBytes rounded away).
                    return writePos + badBytes;

                case 2: //bzip2
                    using (MemoryStream ms = new MemoryStream(dstData))
                    {
                        Stream ls = BZip2Stream.Create(new MemoryStream(srcData), SharpCompress.Compressors.CompressionMode.Decompress, false);
                        ls.CopyTo(ms);
                        return (int)ms.Position;
                    }
                case 3: //lzma
                    // LZMA needs the file's 5-byte compressor properties (lc/lp/pb + LE dict size,
                    // stored per WIA spec in wia_disc_t.compr_data). Use them; fall back to the
                    // hardcoded default only if the header omitted them. The hardcoded dict size did
                    // not match large chunk sizes, decoding to garbage — hence use _compressionBytes.
                    byte[] lzmaProps = (_compressionBytes != null && _compressionBytes.Length == 5) ? _compressionBytes : _lzmaProperties;
                    using (LzmaBlock ls = new LzmaBlock(new GrindCore.CompressionOptions() { BlockSize = dstData.Length, InitProperties = lzmaProps }))
                        ls.Decompress(srcData, 0, size, dstData, 0, ref destDataLength);
                    return destDataLength;

                case 4: //lzma2
                    // LZMA2 needs the single dictionary-size property byte from the WIA header
                    // (wia_disc_t.compr_data[0]); the decoder rejects empty properties (no-op decode)
                    // which previously produced garbage tables and a downstream NullReference.
                    using (Lzma2Block ls = new Lzma2Block(new GrindCore.CompressionOptions() { BlockSize = dstData.Length, InitProperties = _compressionBytes }))
                        ls.Decompress(srcData, 0, size, dstData, 0, ref destDataLength);
                    return destDataLength;

                default: //none
                    Array.Copy(srcData, dstData, size);
                    return size;
            }
        }

        private long decode(byte[] encData, int encSize, byte[] data, long imageOffset, WiaPartition ptn)
        {
            int badHashBytes = countExceptionBytes(encData);
            long fullSize = Buffer.FsLenToHashedLen(encSize, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
            int sectors = (int)fullSize / WiiConsts.WiiSectorSize;
            int groups = (int)(fullSize / WiiConsts.WiiGroupSize) + (fullSize % WiiConsts.WiiGroupSize == 0 ? 0 : 1);

            WiiSecurity hasher = new WiiSecurity((int)WiiConsts.WiiGroupSize);
            int badHashOffset = 0;
            int groupIdx = (int)((imageOffset - ptn.Offset) / WiiConsts.WiiGroupSize);

            for (int g = 0; g < groups; g++)
            {
                int localOffset = (int)(g * WiiConsts.WiiGroupSize);
                long groupOffset = imageOffset + localOffset;

                for (int s = 0; s < WiiConsts.WiiSectors && (g * WiiConsts.WiiSectors) + s < sectors; s++)
                {
                    int src = (((g * WiiConsts.WiiSectors) + s) * WiiConsts.WiiSectorFsSize) + badHashBytes;
                    int dst = (s * WiiConsts.WiiSectorSize) + 0x400;
                    Array.Copy(encData, src, data, localOffset + dst, WiiConsts.WiiSectorFsSize);
                }

                int groupSize = (int)WiiConsts.WiiGroupSize;
                if (localOffset > fullSize)
                    Array.Clear(data, localOffset + (int)(fullSize % groupSize), (int)(groupSize - (fullSize % groupSize)));
                if (fullSize - badHashBytes < groupSize)
                {
                    int gsize = (int)fullSize - badHashBytes;
                    Array.Clear(data, localOffset + gsize, localOffset + (groupSize - gsize));
                }

                hasher.Populate(ptn.Key, _encBuff, 0, data, localOffset, groupSize, false, false, true, groupOffset, _partitionH3, null, false);
                hasher.Decrypt();

                //write hash exceptions
                if (badHashOffset < badHashBytes)
                {
                    int h = (int)encData.ReadUInt16B(badHashOffset);
                    badHashOffset += 2;
                    for (int j = 0; j < h; j++)
                    {
                        uint off = encData.ReadUInt16B(badHashOffset);
                        off = (off / (uint)WiiConsts.WiiSectorHashSize * (uint)WiiConsts.WiiSectorSize) + (off % (uint)WiiConsts.WiiSectorHashSize);
                        badHashOffset += 2;
                        byte[] hash = encData.Read(badHashOffset, 20);
                        Array.Copy(hash, 0, data, localOffset + off, 20);
                        badHashOffset += 20;
                    }

                    //remove hashes for scrubbed areas - detect unmatching 
                    cleanseHashes(_partitionH3, groupIdx + g, data, localOffset, groupSize); //must even be done on fully scrubbed groups in a chunk that lead up to data
                }
            }
            return fullSize;
        }

        private int countExceptionBytes(byte[] mem)
        {
            int groups = _chunkSize / (int)WiiConsts.WiiGroupSize;
            int badBytes = 0;
            for (int g = 0; g < groups; g++)
                badBytes += (mem.ReadUInt16B(badBytes) * 22) + 2;

            if ((_compressionType == 0 || _compressionType == 1) && badBytes % 4 != 0)
                badBytes += 4 - (badBytes % 4);
            return badBytes;
        }

        private void cleanseHashes(byte[] h3, int groupIdx, byte[] group, int offset, int size)
        {
            SHA1 sha1 = SHA1.Create();

            for (int i = 0; i < size / WiiConsts.WiiSectorSize; i++) //any bad h2 sets are scrubbed
            {
                byte[] h3Value = sha1.ComputeHash(group, offset + (i * WiiConsts.WiiSectorSize) + 0x340, 20 * 8);
                if (!h3Value.Equals(0, h3, groupIdx * 20, 20))
                    Array.Clear(group, offset + (i * WiiConsts.WiiSectorSize), WiiConsts.WiiSectorHashSize);
            }
        }

        public Checksums CustomChecksums() => null;
        public void Complete()
        {
        }

        protected override void Dispose(bool disposing) => base.Dispose(disposing);

        public void SetRemovedBlock(Action<MetaData> setBlock) => _setBlock = setBlock;

        public override void Flush() => _stream.Flush();

        public override long Position { get => _position; set => _position = value; }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current)
                _position += offset;
            else if (origin == SeekOrigin.End)
                throw new NotSupportedException();
            else
                _position = offset;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => this.Size;
    }

    internal class WiaPartition : WiaItem
    {
        public WiaPartition(int index, byte[] key, long size) : this(index, key, new WiaItem(), new WiaItem())
        {
            this.Size = size;
        }

        public WiaPartition(int index, byte[] key, params WiaItem[] segments)
        {
            long minOffset = segments.Min(a => a.Offset);
            WiaItem header = segments.FirstOrDefault(a => a.Offset == minOffset);

            this.Index = index;
            this.Key = key;
            this.Segments = segments;
            this.HeaderGroupIndex = header?.Index ?? -1;
            this.Offset = minOffset;
            this.Size = segments.Sum(a => a.Size);
            this.GroupIndex = segments.Min(a => a.GroupIndex);
            this.Groups = segments.Sum(a => a.Groups);
        }
        public byte[] Key;
        public WiaItem[] Segments;
        public int HeaderGroupIndex;
        public byte[] DecryptedScrub;
        public byte[] DecryptedScrubFF;
        public byte[] DecryptedScrubFFHash;
        public byte[] DecryptedScrub55;
        public byte[] DecryptedScrub55Hash;
    }

    internal class WiaItem
    {
        public int Index;
        public long Offset;
        public long Size;
        public int GroupIndex;
        public int Groups;
        public override string ToString() => string.Format("{0}, Offset={1}, Size={2}, GroupIdx={3}, Groups={4}", Index.ToString("X4"), Offset.ToString("X9"), Size.ToString("X9"), GroupIndex.ToString("X4"), Groups.ToString("X4"));
    }

    internal class WiaGroup
    {
        public int Index;
        public long Offset;
        public long Size;
        public long CompSize;
        public byte[] ExceptionData;
        public WiaItem ParentItem;
        public override string ToString() => string.Format("{0}, Offset={1}, Size={2}, CompSize={3}", Index.ToString("X4"), Offset.ToString("X9"), Size.ToString("X9"), CompSize.ToString("X9"));
    }
}
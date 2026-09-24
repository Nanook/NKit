using Nanook.GrindCore.Lzma;
using Nanook.GrindCore.ZStd;
using Nanook.NKit.Nintendo.WiiGc;
using SharpCompress.Compressors.BZip2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    //https://github.com/dolphin-emu/dolphin/blob/master/docs/WiaAndRvz.md

    internal class RvzAsIso : Stream, IAsIso
    {
        private const int _NkitHeaderLen = 0x8 + 0x4 + 0x10 + 0x14 + 0x8;
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private long _size;
        private ContainerType _format;

        private ImageBlockReader<WiaGroup> _blockReader;
        private List<WiaItem> _partitions;

        private byte[] _header;
        //private byte[] _junkBuff;
        private byte[] _encBuff;

        private byte[] _partitionH3; //used to cache WipePartition header
        private WiiSecurity _hasher;

        // Whole-group buffer for partition reads. RVZ partition groups must be decoded, decrypted
        // and hash-fixed (processGroup) as a full 0x200000 group; a partial/misaligned group would
        // corrupt hashes and blank the tail (see WiiSecurity.Populate). When this reader is fronted
        // by a BufferStream cache, ensureCached can hand Read a slice of a group that straddles a
        // cache-block boundary (e.g. Size:0x190000). To stay framing-agnostic we decode+process the
        // containing group ONCE into _groupBuff, then serve any slice (partial or crossing) from it.
        private byte[] _groupBuff;
        private long _groupBuffBase = -1; //image-space offset of _groupBuff[0]; -1 = empty
        private int _groupBuffSize;       //valid bytes in _groupBuff (full group size, group-aligned)

        //private long _length;
        private int _chunkSize;
        // Captured for the format summary (else these are Construct locals).
        private long _sumPrtOffset;
        private long _sumGroupsOffset;
        private int _sumPartitionCount;
        private int _sumGroupCount;
        private byte[][] _chunkExceptions;
        private int _chunkSizePtn;
        private int _compressionType;
        private byte[] _compressionBytes;
        private ImageBlockCache _blockCache;
        public NKitHeader NKitHeader { get; private set; }


        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public long Size => _size;

        public bool SeekRequired => false;

        public Checksums Checksums { get; private set; }

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "RVZ\x1")
                return new RvzAsIso();
            return null;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _blockReader = new ImageBlockReader<WiaGroup>(false, true, true, 10, 10);

            _hasher = new WiiSecurity((int)WiiConsts.WiiGroupSize);
            _blockCache = new ImageBlockCache(true);

            //if (!_allowSeek && this.SeekRequired)
            //    throw new Exception("This Reader requires the source stream to be seekable");

            _format = ContainerType.Rvz;

            byte[] wiaFileHead = _stream.ReadBytes(0x48); //wia_file_head_t

            uint wiaDiscSize = wiaFileHead.ReadUInt32B(0xc);
            _size = (long)wiaFileHead.ReadUInt64B(0x24);
            //_length = (long)wiaFileHead.ReadUInt64B(0x2c);
            byte[] wiaDisc = _stream.ReadBytes(wiaDiscSize); //wia_disc_t

            //this.DiscType = wiaDisc.ReadUInt32B(0x0) == 1 ? DiscType.GameCube : DiscType.Wii;
            _compressionType = (int)wiaDisc.ReadUInt32B(0x4);
            _chunkSize = (int)wiaDisc.ReadUInt32B(0xc);
            _chunkSizePtn = _chunkSize / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorFsSize; //0x8000 to 0x7c00
            _chunkExceptions = new byte[WiiConsts.WiiGroupSize / _chunkSize][];

            byte[] buffer = new byte[Math.Max(_chunkSize, WiiConsts.WiiGroupSize) + (0x400 * 0x400 * 2)]; //arbitrary padding
            //_junkBuff = new byte[_chunkSize];
            _encBuff = new byte[WiiConsts.WiiGroupSize];
            _groupBuff = new byte[WiiConsts.WiiGroupSize];

            int partitions = (int)wiaDisc.ReadUInt32B(0x90);
            int prtSize = (int)wiaDisc.ReadUInt32B(0x94);
            long prtOffset = (long)wiaDisc.ReadUInt64B(0x98);
            _sumPrtOffset = prtOffset;
            _sumPartitionCount = partitions;

            int rawDataItems = (int)wiaDisc.ReadUInt32B(0xb4);
            long rawDataItemsOffset = (long)wiaDisc.ReadUInt64B(0xb8);
            int rawDataItemsSize = (int)wiaDisc.ReadUInt32B(0xc0);

            int groupsItems = (int)wiaDisc.ReadUInt32B(0xc4);
            long groupsOffset = (long)wiaDisc.ReadUInt64B(0xc8);
            _sumGroupsOffset = groupsOffset;
            _sumGroupCount = groupsItems;
            int groupsSize = (int)wiaDisc.ReadUInt32B(0xd0);

            _compressionBytes = wiaDisc.Read(0xd5, (int)wiaDisc.Read8(0xd4));

            if (prtOffset - _stream.Position >= _NkitHeaderLen) //size of nkit header
            {
                byte[] nkitHeader = _stream.ReadBytes(_NkitHeaderLen);
                if (nkitHeader.ReadString(0x0, 0x8) == "NKIT  v1")
                {
                    this.Checksums = new Checksums();
                    this.Checksums[ChecksumType.Crc32] = nkitHeader.Read(0x8, 0x4);
                    this.Checksums[ChecksumType.Md5] = nkitHeader.Read(0x8 + 0x4, 0x10);
                    this.Checksums[ChecksumType.Sha1] = nkitHeader.Read(0x8 + 0x4 + 0x10, 0x14);
                    this.Checksums[ChecksumType.XxHash] = nkitHeader.Read(0x8 + 0x4 + 0x10 + 0x14, 0x8);
                }
            }

            _stream.SafeSeek(prtOffset, SeekOrigin.Begin);

            _partitions = new List<WiaItem>();

            for (int i = 0; i < partitions; i++)
            {
                byte[] wiaPartKey = _stream.ReadBytes(0x10); //wia_part_t
                int partitionDataSegments = (prtSize - 0x10) / 0x10; //2
                List<WiaItem> items = new List<WiaItem>();
                for (int s = 0; s < partitionDataSegments; s++)
                {
                    byte[] wiaPartSeg = _stream.ReadBytes(0x10); //wia_part_data_t
                    items.Add(new WiaItem()
                    {
                        Index = s,
                        Offset = wiaPartSeg.ReadUInt32B(0x0) * (long)WiiConsts.WiiSectorSize,
                        Size = wiaPartSeg.ReadUInt32B(0x4) * (long)WiiConsts.WiiSectorSize,
                        GroupIndex = (int)wiaPartSeg.ReadUInt32B(0x8),
                        Groups = (int)wiaPartSeg.ReadUInt32B(0xc)
                    });
                }
                _partitions.Add(new WiaPartition(i, wiaPartKey, items.ToArray()));
            }

            _stream.SafeSeek(rawDataItemsOffset, SeekOrigin.Begin);
            readUnpack(_stream, rawDataItemsSize, buffer);

            //decompress
            for (int i = 0; i < rawDataItems; i++)
                _partitions.Add(new WiaItem() { Index = i, Offset = (long)buffer.ReadUInt64B((i * 0x18) + 0x0), Size = (long)buffer.ReadUInt64B((i * 0x18) + 0x8), GroupIndex = (int)buffer.ReadUInt32B((i * 0x18) + 0x10), Groups = (int)buffer.ReadUInt32B((i * 0x18) + 0x14) });

            _stream.SafeSeek(groupsOffset, SeekOrigin.Begin);
            readUnpack(_stream, groupsSize, buffer);

            //decompress
            List<WiaGroup> groups = new List<WiaGroup>();
            for (int i = 0; i < groupsItems; i++)
            {
                ulong offset = (ulong)buffer.ReadUInt32B((i * 12) + 0x0) << 2;
                _blockCache.Register(offset, _chunkSize);
                groups.Add(new WiaGroup() { Index = i, Offset = (long)offset, Size = buffer.ReadUInt32B((i * 12) + 0x4), CompSize = buffer.ReadUInt32B((i * 12) + 0x8) });
            }

            //cache.CacheGroups = null;
            _header = wiaDisc.Read(0x10, 0x80);
            _partitionH3 = new byte[WiiConsts.WiiPrtHdrH3Size];

            _partitions = _partitions.OrderBy(a => a.Offset).ToList();

            //fix wia/rvz header issue - fix offsets. When reading this area the Header must be copied over the first part of the block
            if (_partitions[0].Offset == _header.Length)
            {
                _partitions[0].Offset -= _header.Length;
                _partitions[0].Size -= _header.Length;
            }

            ImageBlockType type;
            foreach (WiaItem itm in _partitions)
            {
                foreach (WiaItem seg in (itm as WiaPartition)?.Segments ?? new WiaItem[] { itm })
                {
                    for (int i = 0; i < seg.Groups; i++)
                    {
                        WiaGroup wg = groups[seg.GroupIndex + i];
                        wg.ParentItem = itm;
                        if (wg.Size == 0)
                            type = ImageBlockType.Missing;
                        else if ((wg.Size & 0x80000000) != 0)
                            type = ImageBlockType.Compressed;
                        else if (wg.ParentItem is WiaPartition)
                            type = ImageBlockType.Process; //uncompressed WipePartition block - to ensure we process it for exception info
                        else
                            type = ImageBlockType.Raw;
                        wg.Size &= 0x7fffffff;
                        _blockReader.AddItem((ulong)wg.Offset, (int)wg.Size, (ulong)seg.Offset + ((ulong)i * (ulong)_chunkSize), type, wg);
                    }
                }
            }

            _blockReader.CompletedAddItems(false, 0, _chunkSize, (ulong)_size, true);
            _position = 0;
            return (int)(_chunkSize + (_chunkSize / WiiConsts.WiiGroupSize * 0x400L)); //add an abitary 1k per group- for compression
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
                return 0;

            ImageBlockInfo<WiaGroup> firstGroup = _blockReader.GetItem(_position);
            WiaPartition ptn = firstGroup?.Item?.ParentItem as WiaPartition;

            // Partition data must be decoded, decrypted and hash-fixed as a WHOLE group. When this
            // reader is fronted by a BufferStream cache the incoming (_position, count) can be a
            // slice of a group that straddles a cache-block boundary (partial or non-group-aligned),
            // so instead of requiring group-aligned reads we buffer the containing group once and
            // serve any slice from it. This makes RVZ behave like a normal seekable stream.
            if (ptn != null)
                return readPartitionSlice(buffer, offset, count, ptn, firstGroup);

            // Non-partition (raw / rawdata) area: cap the read so it never crosses into the next
            // partition, then decode straight into the caller's buffer (no hashing required).
            ptn = (WiaPartition)_partitions.FirstOrDefault(a => a is WiaPartition && a.Offset > _position);
            if (ptn != null && count > ptn.Offset - _position)
                count = (int)(ptn.Offset - _position);

            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count, null, decodeBlock, out firstGroup);
            _position += (long)bytesRead;
            return bytesRead;
        }

        // Decode + decrypt + hash-fix the group that contains _position (once, into _groupBuff), then
        // copy the requested slice out. Serves arbitrary counts/alignments within a single group; the
        // caller (BufferStream) re-invokes for the next group when a read spans a group boundary.
        private int readPartitionSlice(byte[] buffer, int offset, int count, WiaPartition ptn, ImageBlockInfo<WiaGroup> firstGroup)
        {
            long gBase = ptn.Offset + ((_position - ptn.Offset) / WiiConsts.WiiGroupSize * WiiConsts.WiiGroupSize);
            int groupSize = (int)Math.Min(WiiConsts.WiiGroupSize, ptn.Size - (gBase - ptn.Offset));

            if (_groupBuffBase != gBase) //not already decoded — decode + process the whole group once
            {
                int gRead = _blockReader.Read(gBase, _stream, _groupBuff, 0, groupSize, null, decodeBlock, out firstGroup);
                processGroup(_groupBuff, 0, gRead, ptn, gBase - ptn.Offset, firstGroup.Index);
                _groupBuffBase = gBase;
                _groupBuffSize = gRead;
            }

            int inGroup = (int)(_position - gBase);
            int avail = _groupBuffSize - inGroup;
            if (avail <= 0)
                return 0;
            if (count > avail)
                count = avail; //never span past this group; caller re-reads for the next

            Array.Copy(_groupBuff, inGroup, buffer, offset, count);
            _position += count;
            return count;
        }

        // ImageBlockReader block-decode callback: unpack (if compressed), decode packing/junk and
        // extract exception data for a single RVZ group into blockData.Result.
        private void decodeBlock(ImageBlockBuff<WiaGroup> blockData, int threadId)
        {
            if (blockData.Info.Type == ImageBlockType.Missing) //blank
                Array.Clear(blockData.Result, 0, blockData.Info.FullSize);
            else
            {
                bool isRvzPacked = blockData.Info.Item.CompSize != 0;
                WiaPartition ptn = blockData.Info?.Item?.ParentItem as WiaPartition;
                int unpackSize = blockData.BuffSize;
                byte[] src = blockData.Buff;

                if (blockData.Info.Type == ImageBlockType.Compressed)
                {
                    unpackSize = unpack(blockData.Buff, blockData.Temp, blockData.Info.Size, (int)blockData.Info.FullSize, ptn == null);
                    src = blockData.Temp;
                }

                decodeAndSetExceptions(src, unpackSize, blockData.Result, (long)blockData.Info.FullOffset, ptn, isRvzPacked, blockData.Info.Type != ImageBlockType.Compressed, out blockData.Info.Item.ExceptionData); //set the expanded group size
            }

            //handle 0x80 header not being part of the data!!
            if (blockData.Info.FullOffset == 0)
                Array.Copy(_header, blockData.Result, _header.Length);
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
                case 1: //purge - removed from RVZ
                    throw new Exception("Purge mode not supported for RVZ");

                case 2: //bzip2
                    using (MemoryStream ms = new MemoryStream(dstData))
                    {
                        Stream ls = BZip2Stream.Create(new MemoryStream(srcData), SharpCompress.Compressors.CompressionMode.Decompress, false);
                        ls.CopyTo(ms);
                        return (int)ms.Position;
                    }
                case 3: //lzma
                    using (LzmaBlock ls = new LzmaBlock(new GrindCore.CompressionOptions() { BlockSize = dstData.Length, InitProperties = _compressionBytes }))
                        ls.Decompress(srcData, 0, size, dstData, 0, ref destDataLength);
                    return destDataLength;

                case 4: //lzma2
                    using (Lzma2Block ls = new Lzma2Block(new GrindCore.CompressionOptions() { BlockSize = dstData.Length, InitProperties = _compressionBytes }))
                        ls.Decompress(srcData, 0, size, dstData, 0, ref destDataLength);
                    return destDataLength;

                case 5: //zstd
                    using (ZStdBlock ls = new ZStdBlock(new GrindCore.CompressionOptions() { BlockSize = dstData.Length }))
                        ls.Decompress(srcData, 0, size, dstData, 0, ref destDataLength);
                    return destDataLength;

                default: //none
                    Array.Copy(srcData, dstData, size);
                    return size;
            }
        }

        private long decodeAndSetExceptions(byte[] encData, int size, byte[] data, long imageOffset, WiaPartition ptn, bool hasPacking, bool uncompressed, out byte[] exceptionBytes)
        {
            int dstPos = 0;
            int pos = 0;
            byte[] junkBuff = new byte[0x8000];

            exceptionBytes = null;
            int exCount;
            if (ptn != null)
            {
                exCount = encData.ReadUInt16B(pos);
                pos = 2;
                if (exCount != 0)
                {
                    pos += (0x2 + 0x14) * exCount;
                    exceptionBytes = encData.Read(0, pos);
                }
                if (uncompressed && pos % 4 != 0)
                    pos += 4 - (pos % 4);
            }

            if (hasPacking) //decode packing
            {
                while (pos < size)
                {
                    uint len = encData.ReadUInt32B(pos);
                    pos += 4;

                    if ((len & 0x80000000) != 0)
                    {
                        long areaOffset = ptn == null ? 0L : (imageOffset - ptn.Offset);
                        dstPos = fillJunk(encData, pos, 0x44, data, dstPos, (int)(len & 0x7FFFFFFF), ptn != null, areaOffset, junkBuff);
                        pos += 0x44;
                    }
                    else
                    {
                        dstPos = dataWrite(encData, pos, data, dstPos, (int)len, ptn != null);
                        pos += (int)len;
                    }
                }
            }
            else
                dstPos = dataWrite(encData, pos, data, dstPos, size - pos, ptn != null);

            return dstPos;
        }

        private void processGroup(byte[] buffer, int offset, int size, WiaPartition ptn, long areaOffset, int blockIndex)
        {
            _hasher.Populate(ptn.Key, _encBuff, 0, buffer, offset, size, false, false, true, areaOffset, _partitionH3, null, false);
            _hasher.Decrypt();

            restoreExceptions(buffer, offset, size, blockIndex);
        }

        // Applies the group's stored H0-hash exceptions onto the decrypted output. This is a READ
        // operation and MUST be idempotent: it never mutates the cached ExceptionData, so re-reading
        // a group (e.g. an up-front FST peek followed by the real sequential read of the same group)
        // restores the same exceptions and produces identical bytes. (The old forward-only path
        // nulled ExceptionData on last use as a memory optimisation, which made a group re-read drop
        // its exceptions and return wrong hashes — only exposed on RVZ where reads are group-decoded.
        // The exception data is small and RefIndex-deduped, so retaining it for the image lifetime is
        // a negligible, bounded cost in exchange for correct re-reads.)
        private void restoreExceptions(byte[] buffer, int offset, int size, int blockIndex)
        {
            int end = offset + size;
            for (int chunkIdx = 0; offset < end; chunkIdx++, offset += _chunkSize)
            {
                ImageBlockInfo<WiaGroup> blk = _blockReader.Items[blockIndex + chunkIdx];
                byte[] mem = blk.RefIndex == 0 ? blk.Item.ExceptionData : _blockReader.Items[blk.RefIndex].Item.ExceptionData;
                if (mem != null)
                {
                    int pos = 0;
                    int c = mem.ReadUInt16B(pos);
                    pos += 2;

                    for (int i = 0; i < c; i++)
                    {
                        uint off = mem.ReadUInt16B(pos);
                        pos += 2;
                        off = (off / WiiConsts.WiiSectorHashSize * WiiConsts.WiiSectorSize) + (off % WiiConsts.WiiSectorHashSize);
                        if (offset + off < buffer.Length)
                            Array.Copy(mem, pos, buffer, offset + off, 0x14);
                        pos += 0x14; //ushort + sha1
                    }
                }
            }
        }

        private int fillJunk(byte[] src, int pos, int len, byte[] dst, int dstOffset, int fillLen, bool partition, long areaOffset, byte[] junkBuff)
        {
            uint[] baseJunk = new uint[NJunk.JunkBaseJunkIntsFull];
            bool nulls = true;
            for (int i = 0; i < len >> 2; i++)
            {
                if ((baseJunk[i] = src.ReadUInt32B(pos + (i << 2))) != 0 && nulls)
                    nulls = false;
            }

            if (!nulls)
            {
                int junkSize = fillLen + ((fillLen & 0x7fff) == 0 ? 0 : (0x8000 - (fillLen & 0x7fff)));
                NJunk.Expand(baseJunk, junkBuff, 0, junkSize);

                int fsSrcPos = (int)(!partition ? areaOffset + dstOffset : Buffer.OffsetToFsOffset(areaOffset + dstOffset, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize)) & 0x7fff;
                return dataWrite(junkBuff, fsSrcPos, dst, dstOffset, fillLen, partition);
            }
            else
                return dataWrite(null, 0, dst, dstOffset, fillLen, partition);
        }

        private int dataWrite(byte[] src, int offset, byte[] dst, int dstOffset, int size, bool partition)
        {
            if (partition)
            {
                int fsDstPos = (int)Buffer.OffsetToFsOffset(dstOffset, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
                if (src == null) //nulls for junk
                    Buffer.ProcessFsData(dst, fsDstPos, size, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize, (b, off, fsOff, sz) => { Array.Clear(b, off, sz); return true; });
                else
                    Buffer.Copy(src, offset, size, 0, size, dst, fsDstPos, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize, size);
                return (int)Buffer.FsOffsetToOffset(fsDstPos + size, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize, true);
            }
            else
            {
                if (src == null) //nulls for junk
                    Buffer.ProcessFsData(dst, dstOffset, size, size, 0, size, (b, off, fsOff, sz) => { Array.Clear(b, off, sz); return true; });
                else
                    Array.Copy(src, offset, dst, dstOffset, size);
                return dstOffset + size;
            }
        }

        public Checksums CustomChecksums() => null;
        public void Complete()
        {
        }
        protected override void Dispose(bool disposing)
        {
            // RVZ holds the largest per-image graph: a CACHING block reader (one ImageBlockInfo per
            // group, each able to cache a decompressed group), plus the group/enc scratch buffers,
            // the Wii hasher, the block cache (dictionary of cached decompressed chunks) and the
            // partition list. Dispose did none of this, so an .rvz image's buffers lingered past
            // completion — a large residual that never dropped between images in a long-lived
            // process (the UI). Release them here so image completion frees them deterministically.
            if (disposing)
            {
                try { _blockReader?.Release(); } catch { }
                _blockReader = null;
                _blockCache = null;
                _groupBuff = null;
                _encBuff = null;
                _hasher = null;
                _partitions = null;
                _partitionH3 = null;
            }
            base.Dispose(disposing);
        }

        public void SetRemovedBlock(Action<MetaData> setBlock)
        {
        }

        public bool IsPartition(long imageOffset) => _partitions.Any<WiaItem>(a => a.Offset <= imageOffset && a.Offset + a.Size > imageOffset && a is WiaPartition);

        // One-line format summary emitted once per source at Detail level (see IAsIso.FormatSummary).
        public string FormatSummary =>
            $"{LogScopes.Tag(LogScopes.Rvz)}size 0x{_size:X} chunk 0x{_chunkSize:X} comp {compName(_compressionType)}"
            + $" parts {_sumPartitionCount}@0x{_sumPrtOffset:X} groups {_sumGroupCount}@0x{_sumGroupsOffset:X}"
            // RVZ stores the embedded NKit header as Checksums (not NKitHeader), so use that as the signal.
            + (Checksums != null ? $" nkitHdr:y crc {Checksums.Crc:X8}" : " nkitHdr:n");

        private static string compName(int t)
        {
            switch (t)
            {
                case 0: return "none";
                case 1: return "purge";
                case 2: return "bzip2";
                case 3: return "lzma";
                case 4: return "lzma2";
                case 5: return "zstd";
                default: return t.ToString();
            }
        }

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
}
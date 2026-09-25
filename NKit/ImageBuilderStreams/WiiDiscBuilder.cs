using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Builder
{
    internal class WiiDiscBuilder
    {
        private const int _PartitionTableLength = 0x100;

        private ImageHeader _hdr;
        private readonly List<WiiPartitionBuilder> _ptns;
        private readonly Stream _output;
        private long _offset;
        private bool _complete;

        public string DiscId6 { get; set; }
        public int Revision { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public int Region { get; set; }
        public byte[] RegionData { get; set; }
        public ImageHeader DiscHeader => _hdr;

        public WiiDiscBuilder(Stream outStream)
        {
            DiscId6 = "NKITNK";
            Revision = 0;
            Number = 0;
            Title = "New NKit Builder Disc Image";
            Region = 0;
            RegionData = new byte[0x10];
            _ptns = new List<WiiPartitionBuilder>();
            _output = outStream;
            _offset = 0;
            _complete = false;
            byte[] hdr = new byte[WiiConsts.WiiDiscHdrSize];
            updateHeader(hdr);
            _hdr = new ImageHeader(hdr, true);
        }

        private void updateHeader(byte[] hdr)
        {
            hdr.WriteString(WiiConsts.DataHdrIdOffset, this.DiscId6.Length, this.DiscId6);
            hdr.Write8(WiiConsts.DataHdrDiscNoOffset, (byte)this.Number);
            hdr.Write8(WiiConsts.DataHdrRevisionOffset, (byte)this.Revision);
            Array.Clear(hdr, WiiConsts.DataHdrTitleOffset, WiiConsts.DataHdrTitleSize);
            hdr.WriteString(WiiConsts.DataHdrTitleOffset, this.Title.Length, this.Title);
            hdr.WriteUInt32B(WiiConsts.WiiDiscHdrRgnOffset, (uint)this.Region);
            hdr.WriteUInt32B(0x18, 0x5d1c9ea3);
        }

        /// <summary>
        /// Sets the Disc header and populates the properties. Updating the properties after calling this method will update them on output
        /// </summary>
        /// <param name="discHeader"></param>
        public void SetHeader(byte[] discHeader)
        {
            _hdr = new ImageHeader(discHeader, true);
            _hdr.Data.WriteUInt16B(WiiConsts.DataHdrEncHashOffset, 0x0000);
            Array.Clear(_hdr.Data, WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize); //wipe nkit header
            Title = _hdr.Title;
            DiscId6 = _hdr.Id6;
            Revision = _hdr.Revision;
            Number = _hdr.DiscNo;
            Region = (int)_hdr.Data.ReadUInt32B(WiiConsts.WiiDiscHdrRgnOffset);
            RegionData = _hdr.Data.Read(WiiConsts.WiiDiscHdrRgn2Offset, 0x10);
        }

        /// <summary>
        /// Creates a new WipePartition, populate it before adding another
        /// </summary>
        /// <param name="partitionHeader"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public WiiPartitionBuilder AddPartition(string id, byte[] partitionHeader, int align, PartitionType type)
        {
            long offset = _offset;
            if (offset % align != 0) //align
                offset += align - (offset % align);

            return AddPartition(id, partitionHeader, offset, type);
        }

        public WiiPartitionBuilder AddPartition(string id, byte[] partitionHeader, long offset, PartitionType type)
        {
            if (_ptns.Count == 0)
            {
                //apply header changes
                _output.Write(_hdr.Data, 0, _hdr.Data.Length);
                _offset = _hdr.Data.Length;
            }
            else //_offset will be start of last added WipePartition - add the length
                _offset += _ptns.Last().Complete();

            if (_offset < offset)
            {
                ByteStream.Zeros.Copy(_output, (int)(offset - _offset));
                _offset = offset;
            }

            _ptns.Add(new WiiPartitionBuilder(id, _output, partitionHeader, _offset, type));
            return _ptns.Last();
        }

        public void Complete(int align)
        {
            Complete();
            long finalSize = _offset;
            if (finalSize % align != 0) //align
                finalSize += align - (finalSize % align);

            Complete(finalSize);
        }

        public void Complete(long finalSize)
        {
            Complete();
            if (_offset < finalSize)
            {
                ByteStream.Zeros.Copy(_output, (int)(finalSize - _offset));
                _offset = finalSize;
            }

        }

        public void Complete()
        {
            if (_complete)
                return;

            if (_ptns.Count != 0)
                _offset += _ptns.Last().Complete();

            //_output.Close();
            _complete = true;

            _hdr.Data.Clear(WiiConsts.WiiDiscHdrPtnOffset, _PartitionTableLength, 0);
            int table = 0;
            int tables = 1;
            int offset = (int)(WiiConsts.WiiDiscHdrRgnOffset + 0x20 + (table * 0x20L));

            _hdr.Data.WriteUInt32B((int)(WiiConsts.WiiDiscHdrPtnOffset + (table * 0x8L)), (uint)tables);
            _hdr.Data.WriteUInt32B((int)(WiiConsts.WiiDiscHdrPtnOffset + (table * 0x8L) + 4), (uint)(offset >> 2));

            _output.Position = 0;
            //loop the partitions and write the fsts and WipePartition lengths
            //using (Stream s = File.Open(_outputFilename, FileMode.Open))
            //{
            foreach (WiiPartitionBuilder p in _ptns)
            {
                _hdr.Data.WriteUInt32B(offset + 0, (uint)(p.PartitionOffset >> 2));
                _hdr.Data.WriteUInt32B(offset + 4, (uint)p.Type);
                offset += 8;

                _output.Seek(p.PartitionOffset, SeekOrigin.Begin);
                p.Patch(_output); //and sign
            }

            //write the WipePartition table
            _output.Position = 0;
            _output.Write(_hdr.Data, 0, _hdr.Data.Length);
            //}
            _output.Position = 0;
        }
    }
}
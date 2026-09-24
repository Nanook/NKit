using Nanook.GrindCore;
using Nanook.GrindCore.DeflateZLib;
using Nanook.GrindCore.Lz4;
using System;
using System.IO;

namespace Nanook.NKit.Container
{
    internal class CsoZsoAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private ContainerType _format;
        private long _size;
        private bool _ver2;
        private DeflateBlock _deflate;
        private Lz4Block _lz4;

        private ImageBlockReader<object> _blockReader;

        private int _sectorSize;
        private int _sectors;
        private int _shift;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public NKitHeader NKitHeader { get; private set; }

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public static IAsIso Create(byte[] id)
        {
            string txt = id.ReadString(0, 4);
            if ((txt == "CISO" && id.ReadUInt32L(0x4) < 0x100) || txt == "ZISO")
                return new CsoZsoAsIso();
            return null;
        }

        public CsoZsoAsIso()
        {
            this.Checksums = new Checksums();
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;


            byte[] idsz = _stream.ReadBytes(0x8);
            _format = idsz.ReadString(0, 4) == "ZISO" ? ContainerType.Zso : ContainerType.Cso;
            uint headerSize = idsz.ReadUInt32L(4);
            if (headerSize == 0)
                headerSize = 0x18;
            byte[] hdr = _stream.ReadBytes(headerSize - 0x8);
            _size = (long)hdr.ReadUInt64L(0x0);
            _sectorSize = (int)hdr.ReadUInt32L(0x8);
            int version = hdr.Read8(0xC);
            _shift = hdr.Read8(0xD);
            _sectors = (int)(_size / _sectorSize) + (_size % _sectorSize == 0 ? 0 : 1);
            _ver2 = version == 2;
            if (version > 2)
                throw new HandledException($"{_format.ToString().ToUpper()} version {version} is not supported. Raise an issue to have it added.");

            long csoTableSize = (long)((_sectors + 1) << 2);
            byte[] table = _stream.ReadBytes(csoTableSize);
            //read block information
            uint prevOffset = table.ReadUInt32L(0);
            bool prevSigBitSet = (prevOffset & 0x80000000u) == 0;
            prevOffset &= ~0x80000000u;

            _deflate = new DeflateBlock(new CompressionOptions() { BlockSize = _sectorSize });
            _lz4 = new Lz4Block(new CompressionOptions() { BlockSize = _sectorSize });
            _blockReader = new ImageBlockReader<object>(true, true, true, 10, 10);
            for (int i = 1; i <= _sectors; i++) //there's one more offset for the max length to calc size
            {
                uint offset = table.ReadUInt32L(i << 2); //4 byte offsets
                bool sigBitSet = (offset & 0x80000000u) == 0;
                offset &= 0x7fffffffu;
                int sz = (int)((long)(offset << _shift) - (long)(prevOffset << _shift));

                ImageBlockType type;
                if (_format == ContainerType.Cso && _ver2)
                    type = sz < _sectorSize ? (prevSigBitSet ? ImageBlockType.Compressed : ImageBlockType.Process) : ImageBlockType.Raw; //zlib / lz4
                else
                    type = prevSigBitSet ? ImageBlockType.Compressed : ImageBlockType.Raw;

                _blockReader.AddItem((ulong)prevOffset << _shift, sz, (uint)_sectorSize, type, null);
                prevOffset = offset;
                prevSigBitSet = sigBitSet;
            }
            _blockReader.CompletedAddItems(false, 0, (int)_sectorSize, (ulong)this.Size, true);

            if ((long)_blockReader.Items[0].Offset > _stream.Position) //gap before data, check for nkit header
            {
                byte[] gap = _stream.ReadBytes((long)_blockReader.Items[0].Offset - _stream.Position);
                if (gap.ReadUInt32B(0) == 0)
                {
                    if (gap.ReadString(4, 4) == "NKIT")
                    {
                        NKitHeader nhdr = new NKitHeader(gap, 4);
                        if (nhdr.HasSize)
                            this.Checksums.Size = _size = nhdr.Size;
                        if (nhdr.HasCrc32)
                            this.Checksums.Crc = nhdr.Checksums.Crc;
                        if (nhdr.HasMd5)
                            this.Checksums.Md5 = nhdr.Checksums.Md5;
                        if (nhdr.HasSha1)
                            this.Checksums.Sha1 = nhdr.Checksums.Sha1;
                        if (nhdr.HasXxhash64)
                            this.Checksums.XxHash = nhdr.Checksums.XxHash;
                        this.NKitHeader = nhdr;
                    }
                }
            }

            _position = 0;
            return 0; //keep the default size
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count, null,
                (blockData, threadId) =>
                {
                    int blockFullSize = blockData.Info.FullSize;
                    if (blockData.Info.Type == ImageBlockType.Raw)
                        Array.Copy(blockData.Buff, blockData.Result, blockData.Info.FullSize);
                    else if (_format == ContainerType.Zso || blockData.Info.Type == ImageBlockType.Process)
                    {
                        int max = 1 << _shift;
                        for (int i = 0; i < max; i++) //horrible hack to fix rounding issue with some blocks
                        {
                            int sz = blockFullSize;
                            if (blockData.BuffSize == 0 || _lz4.Decompress(blockData.Buff, 0, blockData.BuffSize - i, blockData.Result, 0, ref sz) == CompressionResultCode.Success)
                                break;
                        }
                    }
                    else
                    {
                        _deflate.Decompress(blockData.Buff, 0, blockData.BuffSize, blockData.Result, 0, ref blockFullSize);
                    }
                },
                out _
            );
            _position += (long)bytesRead;
            return bytesRead;
        }

        public long Size => _size;

        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(_format.ToString().ToUpper())}v{(_ver2 ? "2" : "1")} size 0x{_size:X} block 0x{_sectorSize:X} blocks {_blockReader?.Items.Count ?? _sectors}"
            + $" shift {_shift}"
            + (this.NKitHeader != null ? $" nkitHdr:y crc {this.Checksums?.Crc:X8}" : " nkitHdr:n");

        public bool SeekRequired => false;

        public Checksums Checksums { get; }

        protected override void Dispose(bool disposing)
        {
            try { _lz4?.Dispose(); } catch { }
            try { _deflate?.Dispose(); } catch { }
            // Release the per-image block reader buffers (see ImageBlockReader.Release) so a
            // cso/zso image's block/cache buffers do not linger past image completion.
            try { _blockReader?.Release(); } catch { }
            _blockReader = null;
            base.Dispose(disposing);
        }

        public Checksums CustomChecksums() => null;

        public void Complete()
        {
        }

        public void SetRemovedBlock(Action<MetaData> setBlock)
        {
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

/*
 * Only solid documentation of JISO - https://github.com/lifehackerhansol/xenobox/blob/main/applet/getdiscid/jiso.c
typedef struct
{
	unsigned char magic[4];			// +0x00 : 'J','I','S','O'
	unsigned short version;			// +0x04 : 0001
	unsigned short compression_block_size;	// +0x06 : uncompressed block size in byte
	unsigned short compression_block_header;// +0x08 : block header size in byte (not used, keeped for legacy support)
	unsigned short compression_algorithm;	// +0x0A : compression algorithm (lzo / zlib)
	unsigned int  uncompressed_size;	// +0x0C : original iso file length in byte
	unsigned char md5_digest[16];		// +0x10 : md5 16-byte long digest
	unsigned int  index_offset;		// +0x20 : file offset to index block
	unsigned short nNCareas;		// +0x24 : number of non-compressed areas
	unsigned short reserved_short;		// +0x26 : reserved for futur use
	unsigned int  NCareas_offset;		// +0x28 : file offset to NCArea block
	unsigned int  reserved_long;		// +0x2C : reserved for futur use
} JISO_FH;		
*/
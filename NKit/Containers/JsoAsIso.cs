using Nanook.GrindCore;
using Nanook.GrindCore.DeflateZLib;
using System;
using System.IO;

namespace Nanook.NKit.Container
{
    internal class JsoAsIso : Stream, IAsIso
    {
        private const int _jsoBlockHeaderSize = 4;
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private ContainerType _format;
        private long _size;

        private DeflateBlock _deflate;
        private DeflateBlock _zlib;
        private ImageBlockReader<object> _blockReader;

        private int _sectorSize;
        private int _sectors;

        private bool _isLzo;
        private bool _headerlessBlock;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public NKitHeader NKitHeader { get; private set; }

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "JISO")
                return new JsoAsIso();
            return null;
        }

        public JsoAsIso()
        {
            this.Checksums = new Checksums();
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = ContainerType.Jso;

            byte[] hdr = _stream.ReadBytes(0x30);
            int vs = _sectorSize = hdr.ReadUInt16L(0x4);
            if (vs != 1)
                throw new HandledException($"{_format.ToString().ToUpper()} version {vs} is not supported. Raise an issue to have it added.");
            _sectorSize = hdr.ReadUInt16L(0x6);
            _headerlessBlock = hdr.Read8(0x8) == 0;
            _isLzo = hdr.Read8(0xa) == 0;
            _size = hdr.ReadUInt32L(0xc);
            this.Checksums[ChecksumType.Md5] = hdr.Read(0x10, 0x10);
            //0x1c is 0x30 and might be the header size
            _sectors = (int)(_size / _sectorSize) + (_size % _sectorSize == 0 ? 0 : 1);

            long tableSize = (long)((_sectors + 1) << 2);
            byte[] table = _stream.ReadBytes(tableSize);
            //read block information
            uint lastOffset = table.ReadUInt32L(0);
            int hdrLen = _headerlessBlock ? 0 : _jsoBlockHeaderSize;

            if (_headerlessBlock)
                _deflate = new DeflateBlock(new CompressionOptions() { BlockSize = _sectorSize });
            else
                _zlib = new ZLibBlock(new CompressionOptions() { BlockSize = _sectorSize });
            _blockReader = new ImageBlockReader<object>(true, false, true, 10, 10);
            for (int i = 1; i <= _sectors; i++) //there's one more offset for the max length to calc size
            {
                uint offset = table.ReadUInt32L(i << 2); //4 byte offsets
                int sz = (int)(offset - lastOffset);
                _blockReader.AddItem(lastOffset, sz, (uint)_sectorSize, sz - hdrLen != _sectorSize ? ImageBlockType.Compressed : ImageBlockType.Raw, null);
                lastOffset = offset;
            }
            _blockReader.CompletedAddItems(false, 0, (int)_sectorSize, (ulong)this.Size, _headerlessBlock && !_isLzo); //use temp if headerless zlib

            _position = 0;
            return 0; //keep the default size
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count, null,
                (blockData, threadId) =>
                {
                    int blockFullSize = blockData.Info.FullSize;

                    if (blockData.Info.Type == ImageBlockType.Compressed)
                    {
                        if (_isLzo)
                        {
                            if (UnLzoP.Lzo1xyDecompress(new Span<byte>(blockData.Result, 0, blockData.Info.FullSize), new Span<byte>(blockData.Buff, 0, blockData.BuffSize), 0) != 0)
                                throw new HandledException("LZO Decompression Failed");
                        }
                        else if (_headerlessBlock) //use CiBuffer to insert zlib header
                            _deflate.Decompress(blockData.Buff, 0, blockData.BuffSize, blockData.Result, 0, ref blockFullSize);
                        else
                            _zlib.Decompress(blockData.Buff, 0, blockData.BuffSize, blockData.Result, 0, ref blockFullSize);
                    }
                    else
                        Array.Copy(blockData.Buff, 0, blockData.Result, 0, blockData.Info.FullSize);
                },
                out _
            );
            _position += (long)bytesRead;
            return bytesRead;
        }

        public long Size => _size;

        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Jso)}size 0x{_size:X} block 0x{_sectorSize:X} blocks {_blockReader?.Items.Count ?? _sectors}"
            + $" codec {(_isLzo ? "lzo" : (_headerlessBlock ? "deflate-hl" : "zlib"))}"
            + (this.Checksums?.Md5 != null ? $" md5 {this.Checksums.Md5.ToHexString().Substring(0, 8)}…" : "");

        public bool SeekRequired => false;

        public Checksums Checksums { get; }

        protected override void Dispose(bool disposing)
        {
            try { _zlib?.Dispose(disposing); } catch { }
            try { _deflate?.Dispose(disposing); } catch { }
            // Release the per-image block reader buffers (see ImageBlockReader.Release) so a .jso
            // image's block/cache buffers do not linger past image completion.
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
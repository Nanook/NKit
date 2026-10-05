using System;
using System.IO;

namespace Nanook.NKit.Container
{
    //https://github.com/Maschell/JNUSLib/blob/master/src/de/mas/wiiu/jnus/WUDService.java
    //https://github.com/Maschell/JNUSLib/tree/master/src/de/mas/wiiu/jnus/implementations/wud
    //https://github.com/Maschell/JNUSLib/blob/master/src/de/mas/wiiu/jnus/implementations/wud/WUDImageCompressedInfo.java

    internal class WuxAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private long _position;
        private long _size;
        private ContainerType _format;
        private ImageBlockReader<object> _blockReader;

        private int _wuxSectorSize;
        private int _wuxSectors;
        private long _dataOffset;

        /// <summary>
        /// Set to true before <see cref="IAsIso.Construct"/> if the task reads every sector
        /// sequentially (FullScan=true, e.g. Scan, Convert, Dedupe). When true the
        /// <see cref="ImageBlockReader{T}"/> caches deduplicated physical sectors so re-reads
        /// are served from memory rather than seeking back to disk.
        /// Leave false (default) for partial-read tasks (Extract, Wipe) where caching adds
        /// memory overhead with no benefit.
        /// </summary>
        internal bool EnableCaching { get; set; }


        private byte[] _wuxHdr;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;

        public bool SizeEstimated => false;
        public NKitHeader NKitHeader { get; private set; }

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "WUX0")
                return new WuxAsIso();
            return null;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;

            _format = ContainerType.Wux;
            _wuxHdr = _stream.ReadBytes(0x20);

            uint magic = _wuxHdr.ReadUInt32L(0x04);
            _wuxSectorSize = (int)_wuxHdr.ReadUInt32L(0x08);
            _size = (long)_wuxHdr.ReadUInt64L(0x10);
            _wuxSectors = (int)((_size + _wuxSectorSize - 1) / _wuxSectorSize); //calc from from machshell's code - long indexTableEntryCount = (getUncompressedSize() + getSectorSize() - 1) / getSectorSize();
            byte[] data = _stream.ReadBytes(_wuxSectors << 2);

            _dataOffset = _wuxHdr.Length + data.Length;
            _dataOffset += _dataOffset % _wuxSectorSize == 0 ? 0 : (_wuxSectorSize - (_dataOffset % _wuxSectorSize));

            // Enable caching for deduplicated sectors — WUX maps multiple virtual sectors to
            // the same physical offset. With caching enabled, ImageBlockReader detects these
            // via RefIndex/RefLastIdx (built in CompletedAddItems) and serves re-reads from the
            // cached copy instead of seeking back to disk. The immediate eviction fix in
            // ImageBlockReader.cachedItem ensures Cached is released as soon as the last
            // reference is consumed, preventing unbounded accumulation across the image.
            // EnableCaching is set by NKitInput based on StepInfo.FullScan — only true for
            // full sequential scans (Convert, Scan, Dedupe); false for Extract/Wipe/Verify
            // where each sector is read at most once and caching wastes memory.
            _blockReader = new ImageBlockReader<object>(true, this.EnableCaching, false, 10, 10);
            ulong maxPointer = 0;
            for (int i = 0; i < _wuxSectors; i++)
            {
                uint val = data.ReadUInt32L(i << 2);
                _blockReader.AddItem((val * (ulong)_wuxSectorSize) + (ulong)_dataOffset, _wuxSectorSize, (uint)_wuxSectorSize, ImageBlockType.Raw, null);
                maxPointer = Math.Max((val * (ulong)_wuxSectorSize) + (ulong)_dataOffset, maxPointer);
            }
            _blockReader.CompletedAddItems(false, 0, (int)_wuxSectorSize, (ulong)_size, false);

            _position = 0;

            return 0; //keep the default size
        }


        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count, null,
                (blockData, threadId) => Array.Copy(blockData.Buff, blockData.Result, blockData.Info.FullSize),
                out _
            );

            _position += (long)bytesRead;
            return bytesRead;
        }

        public long Size => _size;

        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Wux)}size 0x{_size:X} block 0x{_wuxSectorSize:X} blocks {_blockReader?.Items.Count ?? _wuxSectors} data@0x{_dataOffset:X}";

        public bool SeekRequired => false;

        public Checksums Checksums { get; }

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
using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Microsoft.XBox
{
    internal class Image : IImage
    {
        private readonly IAsIso _iso;
        private IFileSystemReader _fsReader; // XDVDFS directory-tree reader (up-front coverage walk)
        private readonly BufferStream _stream;
        private readonly IImageContext _context;
        private readonly SourceFile _file;
        private readonly ImageInfo _info;
        private IImageHeader _header;
        //private ImageHeader _videoHeader;
        private SourceFileTrack _currTrack;
        private byte[] _key;
        private long _position;
        private IBufferPreProcessor _preProcessor;
        private bool _isMode2Form2;
        //private int _trackIdx;
        private bool _skipped;

        private long _baseOffset;
        private int _areaNumber;
        //private int _trackBlock1;
        private bool _fullImage;
        private List<IImageArea> _areas;
        //private long _areaBaseOffset;
        private AreaInfo _area;
        private AreaInfo _nextArea;
        private IFileSystemInfo _fsInfoVideo1;
        private IFileSystemInfo _fsInfo;

        private bool _firstHeaderSet;
        private bool _firstHeaderWritten;
        private long _firstHeaderSize;
        private uint _firstHeaderCrc;
        private ulong _firstHeaderXxHash;

        private static SystemType detectType(long imageSize, byte[] data, int size)
        {
            //X These comments (with X) enable xbox images to be read that have no ISO WipePartition

            //X try
            //X {
            if (size >= Consts.VolumeOffset + 0x800)
            {
                bool hasPvd = data.ReadString(0x8000, 6) == "\u0001CD001";

                if (hasPvd)
                {
                    // ISO
                    if (Consts.REDUMP_ISO_LENGTH.Contains(imageSize))
                    {
                        if (imageSize == Consts.OG_SIZE && (data.ReadStringToNull(0x8028).StartsWith("SEP13011042") || data.ReadStringToNull(0x8028).Rot13Words().StartsWith("SEP13011042"))) //this image could be anything so just test literal position
                            return SystemType.XBox;
                        else
                            return SystemType.XBox360;
                    }
                    //X else if (Consts.XISO_OFFSET_END.Contains(imageSize)) // hack test on the xiso offset and size
                    //X     return Consts.XISO_OFFSET_END[0] == imageSize ? SystemType.XBox : SystemType.XBox360;
                }

                //XDVD FS Only
                XDvdFsVolume vol = new XDvdFsVolume(data, Consts.VolumeOffset, 0x800);
                if (vol.IsValid)
                {
                    return vol.Version == 0 ? SystemType.XBox : SystemType.XBox360;
                    //    bool isXBoxXIso = !hasPvd && imageSize == 0x1BA3A8000; //preprod has pvd or will be this size (xdvdfs + gap + vid2)
                    //    if (isXBoxXIso || (vol.DateTime < new DateTime(2007, 06, 1) && vol.DateTime.ToString("yyyyMMddHHmmss") != "20060207091716"))
                    //        return SystemType.XBox;
                    //    else
                    //        return SystemType.XBox360;
                }
            }
            //X }
            //X catch { }
            return SystemType.NotSet;
        }


        internal static SystemType IsXBox(IImageContext context, IAsIso input, BufferStream isoStream, SystemType inPathSystem)
        {
            int sz = Consts.VolumeOffset + 0x800;
            if (isoStream.Length < sz)
                return SystemType.NotSet;

            byte[] data = new byte[sz];

            //long pos = isoStream.Position;
            isoStream.Read(data, 0, -data.Length);

            return detectType(isoStream.Length, data, sz);
        }

        public Image(IImageContext context, IAsIso input, BufferStream isoStream, SystemType inPathSystem)
        {
            _firstHeaderSet = false;
            _firstHeaderWritten = false;
            _skipped = false;
            _areaNumber = -1;
            _context = context;
            _file = _context.SourceFile;
            _iso = input;
            _stream = isoStream;
            _areaNumber = -1;
            this.Size = _iso.Size;

            if (_iso.Size < 0x8800)
                throw new HandledException("Image not valid.");

            _areas = new List<IImageArea>();

            int sz = Consts.VolumeOffset + 0x800;
            byte[] data = new byte[sz];
            isoStream.Read(data, 0, -data.Length);
            this.SystemType = detectType(isoStream.Length, data, sz);

            ImageHeader hdr = new ImageHeader(null, null);
            Iso.Iso9660.FstContext fst = new Iso.Iso9660.FstContext(hdr, null, null);
            ImageHeaderPvd pvdObj = ImageHeaderPvd.Parse(fst, 0x8000, 0x8000, data.Read(0x8000, 0x800), 0x800, 0, 0x800, 0);


            int xgdType = Array.IndexOf(Consts.REDUMP_ISO_LENGTH, _iso.Size);

            int xgdType4WaveType = xgdType != 4 ? -1 : Array.IndexOf(Consts.WAVE_PVD, pvdObj.PvdCreationDate.ReadString(0, 0x10));
            _fullImage = xgdType >= 0;
            int videoType = !_fullImage ? -1 : getVideoType(xgdType, xgdType4WaveType);
            long l0Length = videoType == -1 ? -1 : Consts.VIDEO_L0_LENGTH[videoType];
            long l1Length = videoType == -1 ? -1 : Consts.VIDEO_L1_LENGTH[videoType];
            int xDvdFsType = !_fullImage ? -1 : getXDvdFsType(xgdType);
            long xDvdFsOffset = xDvdFsType == -1 ? -1 : Consts.XISO_OFFSET[xDvdFsType];
            long xDvdFsOffsetLength = xDvdFsType == -1 ? -1 : Consts.XISO_LENGTH[xDvdFsType];

            //X if (!_fullImage && xgdType == -1)
            //X {
            //X     int xisoIdx = Array.IndexOf(Consts.XISO_OFFSET_END, _iso.Size);
            //X     if (xisoIdx != -1)
            //X     {
            //X         _fullImage = true;
            //X         l0Length = Consts.XISO_OFFSET[xisoIdx];
            //X         l1Length = 0;
            //X         xDvdFsOffset = Consts.XISO_OFFSET[xisoIdx];
            //X         xDvdFsOffsetLength = Consts.XISO_LENGTH[xisoIdx];
            //X     }
            //X }

            //X if (hdr.PvdSectorCount == 0)
            //X     _areas.Add(new ImageArea(0, AreaType.Other));
            //X else
            _areas.Add(new ImageArea(0, AreaType.FileSystem));
            if (_fullImage)
            {
                if (l0Length < xDvdFsOffset)
                    _areas.Add(new ImageArea(_areas[_areas.Count - 1].ImageOffset + l0Length, AreaType.Other));
                _areas.Add(new ImageArea(xDvdFsOffset, AreaType.FileSystem));
                if (xDvdFsOffset + xDvdFsOffsetLength < this.Size - l1Length)
                    _areas.Add(new ImageArea(xDvdFsOffset + xDvdFsOffsetLength, AreaType.Other));
                _areas.Add(new ImageArea(this.Size - l1Length, AreaType.FileSystem));
            }
            _areas.Add(new ImageArea(this.Size, AreaType.None));

            _nextArea = AreaInfo.NextArea(-1, -1, -1, _iso.Size, -1, AreaType.None, _areas, ++_areaNumber);
            _nextArea = setAreaBlockInfo(_nextArea, null, 0); //little hack so we fit in with the loop in Read() but grab the block info
            _area = _nextArea;

            byte[] header = new byte[_nextArea.BlockSize * 16];

            _stream.Read(header, 0, -header.Length); //read and rewind

            Buffer b = new Buffer(true, header);
            b.ReInitialise(_nextArea, true);

            _info = new ImageInfo();
            _info.SystemType = this.SystemType;
            _header = new ImageHeader(header, _area);
            _info.MediaType = MediaType.Unknown;
            _info.Tracks = _context.SourceFile.IndexFile?.Items; //always null when not chd
            _context.Header = _header;
            _context.ImageInfo = _info;

            _info.ImageSize = this.Size;
            _info.Type = this.Type;
            _info.ContainerType = _file.ImageType == SourceImageType.DecIso ? ContainerType.DecIso : _iso.Format;
            _info.IsFullImage = _info.ContainerType == ContainerType.DecIso || _info.ContainerType == ContainerType.Iso;
            _info.SourceSupportsEncryption = true;

        }

        public void Setup()
        {
            if (_key == null)
                _key = _context.SourceFile.Key;
            if (_key == null)
                _key = _context.Settings.GetKey(_file.CleanName, _file.BasePath);
            if (_key != null)
                _context.SourceFile.Key = _key; //SourceFile.Key may have the key already if this isn't the first task running

            //_trackIdx = -1;

            _info.SourceAreas = _areas.ToArray(); //read only copy for Output Task to use safely
            _info.IsFolderIndex = _context.SourceFile.IndexFile != null && _info.SourceAreas.Length > 1;

            // [In] Detail: reader identity + resolved geometry (once per image).
            ILogScope inScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
            _info.SectionLog = inScope; // wire the per-section Trace log for the SectionProcessor
            if (inScope != null && inScope.IsEnabled(LogLevel.Detail))
                inScope.Log(LogLevel.Detail,
                    $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.XDvdFs)}{this.SystemType} size 0x{this.Size:X} areas {_areas.Count} full:{(_fullImage ? "y" : "n")}");

            _context.Scan = new Scan(this.SystemType, _context.SourceFile.Name);

            _position = _nextArea.ImageOffset;

            _preProcessor = new BufferPreProcessor(_context);

            _info.StepImageInfo = new StepsImageInfo()
            {
                CustomChecksums = _iso.CustomChecksums(),
                Checksums = _iso.NKitHeader?.Checksums.Clone() ?? _iso.Checksums ?? new Checksums(),
                IsIndex = _context.SourceFile.IndexFile != null,
                ReqPatch = false,
                Size = this.Size
            };
        }

        public ImageType Type => ImageType.XBox;
        public long Size { get; }

        public long CurrentAreaEndImageOffset => _nextArea?.ImageOffset ?? this.Size;
        public int SectionSize => _area.SectionSize; // 0x200000 + (0x200000 % _area.BlockSize == 0 ? 0 : _area.BlockSize - (0x200000 % _area.BlockSize)); //2MiB + remainder for next block of 16 sectors
        //public int SectionSize => 0x200000 + (0x200000 % _area.BlockSize == 0 ? 0 : _area.BlockSize - (0x200000 % _area.BlockSize)); //2MiB + remainder for next block of 16 sectors

        private int readBuf(byte[] b, ref int pos, int len, System.IO.Stream stream = null)
        {
            int r = (stream ?? _stream).Read(b, pos, len);
            pos += r;
            return r;
        }

        public AreaType Read(IBuffer buffer, out IFileSystemInfo fsInfo)
        {
            fsInfo = null;
            if (_context.SkipType == SkipType.End)
            {
                buffer.ReInitialise(_area, true);
                buffer.Update(_position, _position - _area.ImageOffset, 0, 0, _area.IsEncrypted, _skipped);
                fsInfo = _fsInfo;
                return _area.Type;
            }

            byte[] buf = buffer.Decrypted;
            int pos = 0;

            int length = (int)Math.Min(_area.SectionSize, _nextArea.ImageOffset - _position); //0 on first call - is updated
            bool isEnd = _area.Type == AreaType.None;

            try
            {
                if (_position == _nextArea.ImageOffset)
                {
                    _area = setAreaBlockInfo(_nextArea, _fsInfo, _area.FsOffset);

                    //AddressMode = Area means we're not reusing the Filesystem from a previous track
                    if (_area.Type == AreaType.FileSystem)
                    {
                        if (_area.FsAddressMode == AddressMode.Area)
                        {
                            if ((_fullImage && (_area.AreaNo == 0 || _area.AreaNo == _areas.Count - 2)) || (!_fullImage && _area.AreaNo != 0))
                            {
                                //read the first 16 blocks
                                readBuf(buf, ref pos, _header.Data.Length); //read more if any

                                ImageHeader header = new ImageHeader(buf.Read(0, _header.Data.Length), _area);
                                _header = header;

                                int pvdOffset = _header.Data.Length; // pos;
                                readHeaders(buf, ref pos, pvdOffset);
                                long sessionBase = calculateBlockOffset(buf, ref pos, _position - _area.ImageOffset);
                                if (sessionBase != _baseOffset)
                                {
                                    _area.FsOffset = sessionBase;
                                    _baseOffset = sessionBase;
                                    header.FstContext.Ahead.RemoveAll(a => a.FsOffset >= _baseOffset); //remove added items before we knew the baseOffset was required
                                    header.FstContext.FileSystem.RemoveAll(a => a.FsOffset >= _baseOffset);
                                    readHeaders(buf, ref pos, pvdOffset); //adjust the offsets
                                }

                                if (header.PvdEndFsOffset != 0)
                                {
                                    byte[] hdr = new byte[header.PvdEndFsOffset];
                                    Buffer.Copy(buf, 0, _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, hdr, 0, hdr.Length, 0, hdr.Length, hdr.Length);
                                    header.PvdHeaderCrc = Crc.Compute(hdr);
                                    header.PvdHeaderXxHash = XXHash64.Compute(hdr, 0, hdr.Length);
                                    if (_area.FsOffset == 0)
                                    {
                                        _firstHeaderSize = header.PvdEndFsOffset;
                                        _firstHeaderCrc = header.PvdHeaderCrc;
                                        _firstHeaderXxHash = header.PvdHeaderXxHash;
                                        _firstHeaderSet = true;
                                    }
                                }
                                long volSize = _areas[1].ImageOffset - _areas[0].ImageOffset + (_areas[_areas.Count - 1].ImageOffset - _areas[_areas.Count - 2].ImageOffset);
                                _fsInfo = new FileSystemInfo(_position, _info.ImageSize, header, _area, volSize);
                                if (_fullImage && _area.ImageOffset == 0)
                                    _fsInfoVideo1 = _fsInfo;

                                // Resolve the ISO9660 video partition's file system FULLY UP FRONT,
                                // the same as Iso9660.Image. The engine no longer parses the FST in
                                // the preprocessor (the read-side filesystem-view refactor moved it
                                // up front), so without this the video partition's files are never
                                // parsed and the scan under-counts (only the XDVDFS game partition's
                                // tree would be seen). ResolveContiguous feeds the area as adjacent
                                // SectionSize blocks (cache-served, cursor restored) so the normal
                                // sequential read is unaffected.
                                //
                                // Bound the walk to THIS video partition's own FS extent
                                // (PvdSectorCount * BlockSize), clamped to the next area boundary.
                                // Using the combined video-partition span (volSize) would walk past
                                // this partition into the game partition, re-parsing unrelated blocks
                                // as ISO9660 and inserting duplicate file entries.
                                if (!_fsInfo.InvalidFileSystem && _fsInfo.FileSystem != null)
                                {
                                    long pvdEnd = _area.ImageOffset + (header.PvdSectorCount * _area.BlockSize);
                                    long nextAreaStart = _area.AreaNo + 1 < _areas.Count ? _areas[_area.AreaNo + 1].ImageOffset : this.Size;
                                    long fsBound = System.Math.Min(pvdEnd, nextAreaStart);
                                    Iso.Iso9660.Iso9660FileSystemReader fsReader = new Iso.Iso9660.Iso9660FileSystemReader(
                                        (FileSystemInfo)_fsInfo, _area.ImageOffset, (int)_area.SectionSize);
                                    FileSystemCoverage.ResolveContiguous(
                                        fsReader,
                                        _stream,
                                        _area,
                                        this.Size,
                                        fsBound,
                                        (int)_area.SectionSize,
                                        () => new Buffer(_area.SectionSize, _area.IsEncryptionSupported));
                                }
                            }
                            else //game WipePartition
                            {
                                readBuf(buf, ref pos, Consts.VolumeOffset + _area.BlockSize); //cache and rewind
                                //if (_header is ImageHeader)
                                //    _videoHeader = (ImageHeader)_header;
                                XDvdFsHeader header = new XDvdFsHeader(buf.Read(0, Consts.VolumeOffset + _area.BlockSize), _area, Buffer.FsOffsetToOffset(_areas[_area.AreaNo + 1].ImageOffset - _area.ImageOffset, _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, true));
                                _header = header;
                                header.PvdHeaderCrc = Crc.Compute(header.Data);
                                header.PvdHeaderXxHash = XXHash64.Compute(header.Data, 0, header.Data.Length);
                                header.SetVolumeInfo(_area.ImageOffset, 0, Consts.VolumeOffset, _area.BlockSize, _area.BlockSize);
                                _fsInfo = new XDvdFsFileSystemInfo(_area.ImageOffset, _info.ImageSize, header, _area, Buffer.FsOffsetToOffset(_area.ImageOffset, _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, true));

                                // Resolve the whole XDVDFS directory tree up front via the shared
                                // coverage loop. The reader reports each directory block it needs as
                                // a pending region; ResolveThrough fetches them (CanSeekTo-guarded,
                                // Retain-bracketed at the partition start, cursor restored), so the
                                // arbitrary-offset walk is cache-safe over a forward-only archive
                                // without the inline recursive seek. RequireFullFileSystemUpFront on
                                // the reader makes it resolve fully before any data section is emitted.
                                _fsReader = new XDvdFsFileSystemReader((XDvdFsFileSystemInfo)_fsInfo, _area.ImageOffset, _area.BlockSize, this.Size);
                                FileSystemCoverage.ResolveThrough(_fsReader, _stream, _area, long.MaxValue, _area.ImageOffset, () => new Buffer(0x200000, true));

                            }
                        }
                        else if (_area.AreaNo == _areas.Count - 2)
                        {
                            _fsInfo = _fsInfoVideo1;

                            // The second video partition reuses video1's FST but its tail holds
                            // UDF backup-anchor markers (__ufdAvdpBackup_) that only appear here.
                            // Discover them into the (shared) video1 FST now — the core processes
                            // areas in order and waits for each to complete, so video1's sections are
                            // all done and video2's have not started: a safe single-threaded window to
                            // append the tail markers before video2's sections are emitted. Feed the
                            // video2 region's tail through the same gap scan, using video1's fsInfo so
                            // the UDF signatures match. CanSeekTo-guarded / cursor restored.
                            if (_fsInfo != null && !_fsInfo.InvalidFileSystem && _fsInfo.FileSystem != null)
                            {
                                // The UDF backup markers sit at a FIXED offset within the (small) L1
                                // video partition — NOT at the image's very end — so scan the WHOLE
                                // video2 area, not just a trailing window. The area is the last FS
                                // area [ImageOffset, this.Size); the L1 video partition is small so
                                // this is bounded and cheap. The gap scan detects markers by
                                // signature wherever they land, using video1's fsInfo (shared UDF
                                // volume) so the signatures match, and the area geometry so the marker
                                // FS offsets are attributed to the 1D2658000 area.
                                long v2Start = _area.ImageOffset;
                                long v2End = this.Size;
                                FileSystemInfo v2FsInfo = (FileSystemInfo)_fsInfo;
                                FileSystemCoverage.ResolveTailMarkers(
                                    v2FsInfo.DiscoverTailMarkersBlock,
                                    _stream,
                                    _area,
                                    v2Start,
                                    v2End,
                                    (int)_area.SectionSize,
                                    () => new Buffer(_area.SectionSize, _area.IsEncryptionSupported));

                                // video1's AreaView was frozen during video1 processing (before these
                                // markers existed). Invalidate it so video2's sections see the
                                // markers via a rebuilt snapshot. Safe: video1 is complete (area
                                // ordering) so nothing is reading the old snapshot now.
                                v2FsInfo.ResetAreaView();
                            }
                        }

                        if (_currTrack != null && _area.BlockFsOffset >= 0x10)
                        {
                            if (pos == 0)
                                readBuf(buf, ref pos, (int)_area.BlockSize); //read more if any
                            _currTrack.PhysicalOffset = Ecm.SectorToLba(buf, 0) * _area.BlockSize;
                        }
                    }

                    long fsOffset = _fsInfo?.ImageOffset ?? 0;
                    long fsSize = (fsOffset + _fsInfo?.Size) ?? 0;
                    if (_fsInfo != null && _fsInfo.InvalidFileSystem)
                        fsSize = 0;
                    else if (_header is ImageHeader && _fullImage && _area.ImageOffset == 0)
                        fsSize = ((ImageHeader)_header).PvdSectorCount * _area.BlockSize;

                    // NOTE: not a multi-session gap (this text was carried over from Iso9660.Image).
                    // Xbox/XGD is a fixed video1 / XDVDFS-game / video2 partition layout, fully handled
                    // by the area machinery below (_fullImage cases + AddressMode.Relative for the last
                    // split partition). The game/video FS size is derived from the PVD
                    // (PvdSectorCount * BlockSize) rather than by seeking to the physical end; the FST
                    // walk seeks only WITHIN a partition, bounded to its own extent and cache-released
                    // after — no full-image buffering. Tracked in NKitVault/10 Refactor/Warning Cleanup.md.
                    _nextArea = AreaInfo.NextArea(_area.ImageOffset, _fsInfo == null ? -1 : fsOffset, _fsInfo == null ? -1 : fsSize, _info.ImageSize, -1, AreaType.None, _areas, ++_areaNumber);
                    if ((this.SystemType == SystemType.XBox || this.SystemType == SystemType.XBox360) && _nextArea.AreaNo == _areas.Count - 2)
                        _nextArea.FsAddressMode = AddressMode.Relative;

                    // [In] [XDVDFS] Detail: reader's own per-area progress (once per area transition).
                    ILogScope areaScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                    if (areaScope != null && areaScope.IsEnabled(LogLevel.Detail))
                        areaScope.Log(LogLevel.Detail,
                            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.XDvdFs)}area {_area.AreaNo} [{_area.Type}] offset 0x{_area.ImageOffset:X} size 0x{_nextArea.ImageOffset - _area.ImageOffset:X}{(_area.IsEncrypted ? " enc" : "")}");

                    length = (int)Math.Min(_area.SectionSize, _nextArea.ImageOffset - _position);
                }

                if (length - pos > 0)
                    readBuf(buf, ref pos, length - pos);

                if (length != 0 && pos == 0)
                    throw new HandledException(string.Format("Image.Read - No data read at Position {0} ({1}) - Requested {2}", _position.ToString("X"), _iso.RealPosition.ToString("X"), length.ToString()));

                buffer.ReInitialise(_area, true);

                //always write to encrypted the preprocessor will switch it to decompressed when it analyses things
                buffer.Update(_position, _position - _area.ImageOffset, length, 0, _area.IsEncryptionSupported && _area.IsEncrypted, _skipped);
                //CiBuffer.Update(_position, _position - _area.ImageOffset, length, 0, _area.IsEncryptionSupported && (_ps3 == null ? _area.IsEncrypted : _ps3.IsEncrypted), _skipped);

                //Trace.WriteLine($"{_area.AreaNo} : {_area.ImageOffset:x9} : {CiBuffer.BlockSize} : {CiBuffer.Size:x8}");

                _position += buffer.Size;

                if (_area.Type == AreaType.FileSystem)
                    fsInfo = _fsInfo;
                else
                    fsInfo = null;

                skip(); //after providing current data check if we can skip some of the current filesystem

                // Bound the shared cache: release cached blocks below the returned-data mark on
                // both layers. Layer-B (this _stream, wrapping the decoder) — the FST re-read above
                // walks records at arbitrary offsets within the partition and then seeks back to
                // the read cursor; while it runs it brackets itself with Retain(_area.ImageOffset)
                // .. Retain(long.MaxValue), so this end-of-read ReleaseTo can never drop a region
                // the walk (or a subsequent backward seek) still needs. Layer-A (the raw source the
                // decoder reads from, for a pass-through container over an archive entry) via
                // IReleasable — without it the raw-source cache grows to hold the whole image. For a
                // seekable source nothing is cached, so both are no-ops.
                _stream.ReleaseTo(_position);
                (_iso as IReleasable)?.ReleaseTo(_position);

                return _area.Type;
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Image.Read: {ex.Message}");
            }
        }

        private int getXDvdFsType(int xgdType)
        {
            return xgdType switch
            {
                0 => 0,
                1 or 2 or 3 or 4 => 1,
                5 => 2,
                6 or 7 => 3,
                _ => -1,
            };
        }
        private int getVideoType(int xgdType, int xgdType4WaveType)
        {
            return xgdType switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                4 => xgdType4WaveType switch
                {
                    0 => 1,
                    1 => 2,
                    2 => 3,
                    3 => 4,
                    4 or 5 or 6 or 7 => 5,
                    8 or 9 => 6,
                    10 or 11 or 12 => 7,
                    13 => 8,
                    14 or 15 => 9,
                    16 => 10,
                    17 or 18 => 11,
                    19 => 12,
                    20 => 13,
                    21 => 14,
                    _ => -1,
                },
                5 => 14,
                6 => 15,
                7 => 16,
                _ => -1,
            };
        }

        // Reposition to a skip target through the stream reads actually flow through (_stream, the
        // BufferStream), not _iso directly. Seeking _iso alone desyncs it from the BufferStream's
        // cursor, delivering wrong bytes at the skipped-to boundary (see Iso9660.Image.skipStreamTo).
        // Skips are always forward.
        private void skipStreamTo(long imageOffset) => _stream.SafeSeek(imageOffset, System.IO.SeekOrigin.Begin);

        //Allow skipping of rest of image, or filesystem / other areas
        private void skip()
        {
            long skipTo = _context.SkipToImageOffsetGet(_position);
            if (skipTo <= _position)
                return;

            long io;
            if (skipTo > _position && _context.SkipType != SkipType.End)
            {
                if ((_area.Type == AreaType.FileSystem && (_fsInfo.InvalidFileSystem || _fsInfo.FileSystem != null)) || _area.Type == AreaType.Other)
                {
                    bool toEnd = !getPartitionInfo(skipTo, out io); //end
                    if (toEnd)
                    {
                        _position = _nextArea.ImageOffset;
                        skipStreamTo(_position);
                        _context.SkipType = SkipType.End; //main loop will exit
                        _skipped = true;
                    }
                    else if (io >= _nextArea.ImageOffset) //always hit each section
                    {
                        _position = _nextArea.ImageOffset;
                        skipStreamTo(_position);
                        _context.SkipType = SkipType.Skip;
                        _skipped = true;
                    }
                    else if (io > _area.ImageOffset)
                    {
                        _position = io; //jump to closest sector start
                        skipStreamTo(_position);
                        _context.SkipType = SkipType.Skip;
                        _skipped = true;
                    }
                }
            }
        }

        private bool getPartitionInfo(long imageOffset, out long startImageOffset)
        {
            startImageOffset = imageOffset;
            int req = -1;
            int curr = -1;
            for (int i = 0; i < _areas.Count; i++)
            {
                if (_position >= _areas[i].ImageOffset)
                    curr = i;
                if (imageOffset >= _areas[i].ImageOffset)
                    req = i;
            }

            if (req >= _areas.Count - 1) //end of disc
                return false;
            else if (req > curr) //skip to next WipePartition
                startImageOffset = _areas[req].ImageOffset; //request beyond end of offsets. Set to end

            startImageOffset = ((startImageOffset - _areas[req].ImageOffset) / this.SectionSize * this.SectionSize) + _areas[req].ImageOffset; //section start
            return true;
        }

        private bool hasHeaders(byte[] buf, int pos)
        {
            int p = _header.Data.Length;
            return buf.ReadString(pos + p + 1, 5) == "CD001";
        }

        private void readHeaders(byte[] buf, ref int pos, int pvdOffset)
        {
            if (_header is ImageHeader)
            {
                ImageHeader header = (ImageHeader)_header;
                bool reread = header.Pvds.Count > 1; //more than just system
                header.Pvds.Clear();
                header.Pvds.Add(FsType.System, ImageHeaderPvd.CreateSystem());
                int p = pvdOffset;

                while (true)
                {
                    if (!reread)
                    {
                        if (pos < p + _area.BlockSize)
                            readBuf(buf, ref pos, p + _area.BlockSize - pos);
                    }
                    if (!header.SetPvd(_position + p, _position + p - _area.ImageOffset, buf.Read(p + _area.BlockFsOffset, _area.BlockFsSize), _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, _baseOffset))
                        break; //no more PVDs
                    p += _area.BlockSize;
                }
            }
        }

        private long calculateBlockOffset(byte[] buf, ref int pos, long areaOffset)
        {
            if (_header is ImageHeader)
            {
                ImageHeader header = (ImageHeader)_header;
                //get the root directory offset
                ImageHeaderPvd pvd = header?.Pvds?.Values.OrderBy(a => a.RootDirectoryRecord == null || a.RootDirectoryRecord.Extent == 0 ? uint.MaxValue : a.RootDirectoryRecord.Extent)?.FirstOrDefault();
                if (pvd == null || pvd.RootDirectoryRecord == null || pvd.RootDirectoryRecord.Extent == 0)
                    return 0;

                uint blockVal = pvd.RootDirectoryRecord.Extent;

                while (pos + _area.BlockSize < buf.Length)
                {
                    int p = pos;
                    if (readBuf(buf, ref pos, _area.BlockSize) == 0)
                        break;
                    if (buf.ReadUInt32L(p + _area.BlockFsOffset + 0x2) == blockVal && buf.ReadUInt32B(p + _area.BlockFsOffset + 0x6) == blockVal)
                        return (blockVal - ((areaOffset + (long)p) / _area.BlockSize)) * _area.BlockFsSize;
                }
            }
            return 0;
        }

        private AreaInfo setAreaBlockInfo(AreaInfo ai, IFileSystemInfo fsInfo, long fsOffset)
        {
            //_trackIdx = -1;

            int trackIndex = 0;
            int blockSize = 0x800;
            int blockFsOffset = 0x0;
            int blockFsSize = 0x800;

            SourceFileTrack trk = _file.IndexFile == null ? null : _file.IndexFile.Items.FirstOrDefault(a => a.ImageOffset == ai.ImageOffset);
            if (trk != null)
            {
                trackIndex = trk.TrackIndex;
                blockSize = trk.BlockSize;
                blockFsOffset = trk.BasicType == IndexTrackBasicType.Mode1 ? 0x10 : ((trk.BasicType == IndexTrackBasicType.Mode2 || trk.BasicType == IndexTrackBasicType.Cdi) ? 0x18 : 0);
                blockFsSize = _isMode2Form2 && trk.BasicType == IndexTrackBasicType.Mode2 ? 0x914 : (trk.BasicType == IndexTrackBasicType.Audio ? trk.BlockSize : 0x800); //2048
            }

            long nextImageOffset = _areas.FirstOrDefault(a => a.ImageOffset > ai.ImageOffset)?.ImageOffset ?? long.MaxValue;

            if (ai.FsAddressMode == AddressMode.Relative)
            {
                ai.BaseOffset = _areas[1].ImageOffset; // ai.ImageOffset - _areaBaseOffset;
                ai.FsOffset = fsOffset; //end of video part 1
            }
            //else if (ai.Type == AreaType.FileSystem) //track the last filesystem //if (ai.FsAddressMode == AddressMode.Area) // && ai.Type == AreaType.FileSystem)
            //    _areaBaseOffset = ai.ImageOffset;

            ai.SetBlock(blockSize, blockFsOffset, blockFsSize, 0x200000 + (0x200000 % blockSize == 0 ? 0 : blockSize - (0x200000 % blockSize))); //2MiB + remainder for next block of 16 sectors);

            SetAreaProperties(ai);

            return ai;
        }

        internal static void SetAreaProperties(AreaInfo ai)
        {
            switch (ai.Type)
            {
                case AreaType.FileSystem:
                    ai.SetProperties("FsType", "Version", "BlockSize", "AreaOffsetBase", "SessionOffsetBase", "HeaderSize", "HeaderCrc", "HeaderXxHash", "PvdSectorCount", "PhysicalOffset", "HeaderDate");
                    break;
                case AreaType.Other:
                    ai.SetProperties();
                    break;
                default:
                    break;
            }
        }

        public void SetScanProperties() => SetScanProperties(_context, SystemType, _key, _firstHeaderSet, _firstHeaderWritten, _firstHeaderSize, _firstHeaderCrc, _firstHeaderXxHash, _header);

        internal static void SetScanProperties(IImageContext context, SystemType systemType, byte[] key, bool firstHeaderSet, bool firstHeaderWritten, long firstHeaderSize, uint firstHeaderCrc, ulong firstHeaderXxHash, IImageHeader header)
        {
            int partition = -1;
            SourceFileTrack track = null;
            IFileSystemInfo fsInfo = null;
            Scan result = context.Scan;
            bool hasEnc = context.Scan.Areas.Any(a => a.AreaInfo.IsEncryptionSupported);

            foreach (ScanArea sra in context.Scan.Areas)
            {
                track = context.SourceFile?.IndexFile?.Items?.FirstOrDefault(a => a.ImageOffset == sra.ImageOffset);

                switch (sra.Type)
                {
                    case AreaType.FileSystem:
                        fsInfo = (IFileSystemInfo)sra.FsInfo;
                        sra.AreaInfo.Properties["BlockSize"] = sra.AreaInfo.BlockSize;
                        sra.AreaInfo.Properties["DataSize"] = sra.AreaInfo.BlockFsSize;
                        sra.AreaInfo.Properties["AreaOffsetBase"] = (ulong)sra.AreaInfo.BaseOffset; //not 0 if this area requires addressed to be adjusted (Partition 5 of DC)
                        if (fsInfo is FileSystemInfo)
                        {
                            sra.AreaInfo.Properties["FsType"] = "Iso9660";
                            sra.AreaInfo.Properties["SessionOffsetBase"] = (ulong)Buffer.FsOffsetToOffset(fsInfo.AreaInfo.FsOffset, fsInfo.AreaInfo.BlockSize, fsInfo.AreaInfo.BlockFsOffset, fsInfo.AreaInfo.BlockFsSize, true); //not 0 if this area requires addressed to be adjusted (Partition 5 of DC)
                            Match m = Regex.Match(((FileSystemInfo)fsInfo).Header.Pvds[FsType.Iso9660].PvdCreationDate.ReadString(0, 0xE), "^([0-9]{4})([0-9]{2})([0-9]{2})([0-9]{2})([0-9]{2})([0-9]{2})$");
                            if (m.Success)
                                sra.AreaInfo.Properties["HeaderDate"] = $"{m.Groups[1]}-{m.Groups[2]}-{m.Groups[3]}T{m.Groups[4]}:{m.Groups[5]}:{m.Groups[6]}"; //not 0 if this area requires addressed to be adjusted (Partition 5 of DC)
                        }
                        else if (fsInfo is XDvdFsFileSystemInfo)
                        {
                            sra.AreaInfo.Properties["FsType"] = "XDvdFs";
                            sra.AreaInfo.Properties["Version"] = (int)((XDvdFsFileSystemInfo)fsInfo).Header.Volume.Version;
                            sra.AreaInfo.Properties["HeaderDate"] = ((XDvdFsFileSystemInfo)fsInfo).Header.Volume.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"); //not 0 if this area requires addressed to be adjusted (Partition 5 of DC)
                        }
                        if (track != null && sra.AreaInfo.BlockSize >= 0x10)
                            sra.AreaInfo.Properties["PhysicalOffset"] = (ulong)track.PhysicalOffset;
                        if (firstHeaderSet && !firstHeaderWritten)
                        {
                            sra.AreaInfo.Properties["HeaderSize"] = (ulong)firstHeaderSize; //From 0 to to start of PvdEnd
                            sra.AreaInfo.Properties["HeaderCrc"] = (uint)firstHeaderCrc;
                            sra.AreaInfo.Properties["HeaderXxHash"] = (ulong)firstHeaderXxHash;
                            firstHeaderWritten = true;
                        }
                        if (fsInfo is FileSystemInfo && ((FileSystemInfo)fsInfo).Header.PvdSectorCount != 0 && sra.AreaInfo.FsAddressMode == AddressMode.Area)
                            sra.AreaInfo.Properties["PvdSectorCount"] = (ulong)((FileSystemInfo)fsInfo).Header.PvdSectorCount;
                        break;
                    case AreaType.Other:
                        sra.AreaInfo.Properties["Partition"] = partition;
                        break;
                    default:
                        break;
                }
            }

            result.Properties["System"] = systemType.ToString();
            result.Properties["Media"] = MediaType.Disc.ToString();
            result.Properties["Size"] = (ulong)result.Size;
            result.Properties["CRC"] = result.Crc;
            result.Properties["DecryptedCRC"] = result.CrcDecrypted;
        }
        public SystemType SystemType { get; internal set; }

        public ISectionProcessor CreateSectionProcessor() => new SectionProcessor(_header, _context.ImageInfo.Mode, _info);

        public IBufferPreProcessor GetPreProcessor() => _preProcessor;

        public ISectionProcessor PatchSection(ScanSection s) => null;

        public IBuffer CreateBuffer() => new Buffer(SectionSize, true);
    }
}
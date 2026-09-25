using Nanook.NKit.Chd;
using Nanook.NKit.Container;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class Image : IImage
    {
        private readonly IAsIso _iso;
        private readonly BufferStream _stream;
        private readonly IImageContext _context;
        private readonly SourceFile _file;
        private readonly ImageInfo _info;
        private ImageHeader _header;
        private SourceFileTrack _currTrack;
        private byte[] _key;
        private long _position;
        private IBufferPreProcessor _preProcessor;
        private bool _isMode2Form2;
        //private int _trackIdx;
        private bool _skipped;
        private bool _tracklessAreaInsert;

        private const string ps2IdA = "43E465A546660727E708C8E9C90BAB6B4C6D4D4D6DAC0B28C50380"; //has 08 sometimes also
        private const string ps2IdB = "43A445A546662727E708A8E9C90BAB6B4C6D4D4D6DACEA08C503";

        private int _areaNumber;
        private int _trackBlock1;
        private List<IImageArea> _areas;
        private string _detectVia; // how SystemType was decided (for Detail logging)
        private long _areaBaseOffset;
        private AreaInfo _area;
        private AreaInfo _nextArea;
        private IFileSystemInfo _fsInfo;
        private PlayStation3 _ps3;
        private ChdMetaData _chdMetaData;

        private bool _firstHeaderSet;
        private bool _firstHeaderWritten;
        private long _firstHeaderSize;
        private uint _firstHeaderCrc;
        private ulong _firstHeaderXxHash;

        public Image(IImageContext context, IAsIso input, BufferStream isoStream, SystemType inPathSystem)
        {
            _firstHeaderSet = false;
            _firstHeaderWritten = false;
            _skipped = false;
            _tracklessAreaInsert = false;
            _areaNumber = -1;
            _context = context;
            _file = _context.SourceFile;
            _iso = input;
            _stream = isoStream;
            _areaNumber = -1;
            this.Size = _iso.Size;

            _areas = new List<IImageArea>();
            _chdMetaData = (input as ChdAsIso)?.MetaData ?? (input as Nanook.NKit.Container.DataStoreAsIso)?.MetaData;
            if ((_chdMetaData?.Tracks?.Count ?? 0) != 0)
                _areas.AddRange(_chdMetaData.Tracks.Select(a => new ImageArea(a.ImageOffset, a.TrackType == IndexTrackType.Audio ? AreaType.Audio : AreaType.FileSystem)));
            else
            {
                if (_file.IndexFile == null)
                    _areas.Add(new ImageArea(0, AreaType.FileSystem));
                else
                    _areas.AddRange(_file.IndexFile.Items.Select(a => new ImageArea(a.ImageOffset, a.BasicType == IndexTrackBasicType.Audio ? AreaType.Audio : AreaType.FileSystem)));
            }
            _areas.Add(new ImageArea(this.Size, AreaType.None));

            // TODO(audio-sniff): when there are no track types (no IndexFile/CHD metadata) a single
            // FileSystem area is assumed at offset 0; an image that is actually audio is mis-framed.
            // The header is peekable a few lines below (BufferStream "read and rewind"), so sniffing
            // audio from the header is feasible here — the remaining need is an audio-without-tracktype
            // sample to define the signature and a test. Tracked in NKitVault/10 Refactor/Warning Cleanup.md.
            _nextArea = AreaInfo.NextArea(-1, -1, -1, this.Size, -1, AreaType.None, _areas, ++_areaNumber);
            _nextArea = setAreaBlockInfo(_nextArea, null, 0); //little hack so we fit in with the loop in Read() but grab the block info
            _area = _nextArea;

            byte[] header = new byte[_nextArea.BlockSize * 16];

            if (_iso.Size < Math.Max(header.Length, 0x8000))
                throw new HandledException("Image not valid.");

            _stream.Read(header, 0, -header.Length); //read and rewind

            _isMode2Form2 = _nextArea.BlockSize == 0x930 && _nextArea.BlockFsOffset == 0x18 && (header[0xb] & 0x20) == 0x20;
            if (_isMode2Form2)
                _nextArea.SetBlock(_nextArea.BlockSize, _nextArea.BlockFsOffset, 0x914, _nextArea.SectionSize);

            _trackBlock1 = (header[0xc].ToDecimal() * 60 * 75) + (header[0xd].ToDecimal() * 75) + header[0xe].ToDecimal();

            Buffer b = new Buffer(true, header);
            b.ReInitialise(_nextArea, true);

            if (b.ReadFsBytes(0x800, 0xc).ReadString(0, 0xc) == @"PlayStation3")
            { this.SystemType = SystemType.PS3; _detectVia = "sig 'PlayStation3' @0x800"; }
            else if (b.ReadFsBytes(0x7068, 0x19).ReadString(0, 0x19) == @"PlayStation Master Disc 3")
            { this.SystemType = SystemType.PS3; _detectVia = "sig 'PlayStation Master Disc 3' @0x7068"; }
            else if (b.ReadFsBytes(0x0, 0x10).ReadString(0, 0xe) == @"SEGADISCSYSTEM")
            { this.SystemType = SystemType.SegaCD; _detectVia = "sig 'SEGADISCSYSTEM'"; }
            else if (b.ReadFsBytes(0x0, 0x10).ReadString(0, 0xf) == @"SEGA SEGASATURN")
            { this.SystemType = SystemType.Saturn; _detectVia = "sig 'SEGA SEGASATURN'"; }
            else if (b.ReadFsBytes(0x0, 0x10).ReadString(0, 0xf) == @"SEGA SEGAKATANA")
            { this.SystemType = SystemType.Dreamcast; _detectVia = "sig 'SEGA SEGAKATANA'"; }
            else if (b.ReadFsBytes(0x2020, 0x1c).ReadString(0, 0x1b) == @"Sony Computer Entertainment")
            { this.SystemType = SystemType.PS1; _detectVia = "sig 'Sony Computer Entertainment' @0x2020"; }
            else if (isPs2(b.ReadFsBytes(0x0, 0x800)))
            { this.SystemType = SystemType.PS2; _detectVia = "PS2 header match"; }
            else
            {
                byte[] hdrPvd = new byte[header.Length + _nextArea.BlockSize];
                _stream.Read(hdrPvd, 0, -hdrPvd.Length);
                if (hdrPvd.ReadString(header.Length + _nextArea.BlockFsOffset + 0x8, 8) == "PSP GAME")
                { this.SystemType = SystemType.PSP; _detectVia = "sig 'PSP GAME'"; }
                else if (hdrPvd.ReadString(header.Length + _nextArea.BlockFsOffset + 0x1, 4) == "CD-I")
                { this.SystemType = SystemType.CDi; _detectVia = "sig 'CD-I'"; }
                else if (hdrPvd.ReadString(header.Length + _nextArea.BlockFsOffset + 0x1, 5) != "CD001" && nonTrack1HeaderMatch("SEGA SEGAKATANA", _iso, _file, _chdMetaData)) //expensive / cache/seek tests (only if track1 is audio and there's a datatrack
                { this.SystemType = SystemType.Dreamcast; _detectVia = "track2 'SEGA SEGAKATANA' (no CD001)"; }
                else
                { this.SystemType = inPathSystem; _detectVia = $"no signature -> inPath default [{inPathSystem}]"; } //use inpath default. so if it's audio and in the DC path then it's a MIL CD for example
            }

            _info = new ImageInfo();
            _info.SystemType = this.SystemType;
            _header = new ImageHeader(header, _area);
            _info.MediaType = _chdMetaData?.MediaType ?? MediaType.Unknown;
            _info.Tracks = _chdMetaData?.Tracks?.ToArray() ?? _context.SourceFile.IndexFile?.Items; //always null when not chd
            _context.Header = _header;
            _context.ImageInfo = _info;

            _info.ImageSize = this.Size;
            _info.Type = this.Type;
            _info.ContainerType = _file.ImageType == SourceImageType.DecIso ? ContainerType.DecIso : _iso.Format;
            _info.IsFullImage = _info.ContainerType == ContainerType.DecIso || _info.ContainerType == ContainerType.Iso;
            _info.SourceSupportsEncryption = this.SystemType == SystemType.PS3;

        }

        private bool nonTrack1HeaderMatch(string idMatch, IAsIso reader, SourceFile file, ChdMetaData chd)
        {
            SourceFileTrack[] tracks = chd?.Tracks?.ToArray() ?? file.IndexFile?.Items;
            SourceFileTrack trk = tracks?.Skip(1).FirstOrDefault(a => a.BasicType == IndexTrackBasicType.Mode1 || a.BasicType == IndexTrackBasicType.Mode2);
            if (trk != null)
            {
                if (file.IndexFile != null)
                {
                    using (Stream fl = file.OpenFilePart(file.ImageFiles[trk.TrackIndex - 1]))
                    {
                        byte[] tmp = fl.ReadBytes(trk.BlockSize);
                        int blockFsOffset = trk.BasicType == IndexTrackBasicType.Mode1 ? 0x10 : ((trk.BasicType == IndexTrackBasicType.Mode2 || trk.BasicType == IndexTrackBasicType.Cdi) ? 0x18 : 0);

                        return tmp.ReadString(blockFsOffset, 0xf) == idMatch;
                    }
                }
                else if (reader.Seekable) //chd
                {
                    long pos = reader.Position;
                    reader.Position = trk.BlockIdx * trk.BlockSize;
                    byte[] tmp = new byte[trk.BlockSize];
                    reader.Read(tmp, 0, tmp.Length);
                    int blockFsOffset = trk.BasicType == IndexTrackBasicType.Mode1 ? 0x10 : ((trk.BasicType == IndexTrackBasicType.Mode2 || trk.BasicType == IndexTrackBasicType.Cdi) ? 0x18 : 0);
                    reader.Position = pos;
                    return tmp.ReadString(blockFsOffset, 0xf) == idMatch;
                }
            }
            return false;
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

            if (this.SystemType == SystemType.Dreamcast)
            {
                _context.Settings.LoadFixData(new DreamcastFixData());
                //_context.Settings.FixData<DreamcastFixData>().SetDisc(_header.Data, _area.BlockFsOffset, null);
            }
            else if (this.SystemType == SystemType.PS3)
                _context.Settings.LoadFixData(new Playstation3FixData());

            if (this.SystemType == SystemType.PS3)
            {
                if (_iso.NKitHeader != null)
                {
                    if (_iso.NKitHeader.KeyType == HeaderKeyType.Aes && _iso.NKitHeader.Key != null)
                        _context.SourceFile.Key = _key = _iso.NKitHeader.Key;
                }

                _ps3 = new PlayStation3(_context.Header.Data.Read(_area.BlockFsOffset, _area.BlockFsSize), _key, this.Size, _context.Settings.AllKeys);
                _header.Ps3 = _ps3;
                _info.SourceSupportsEncryption = _ps3.IsEncryptionSupported(); //disc

                if (_ps3.Read3k3yKey(_header.Data) && _key == null)
                    _context.SourceFile.Key = _key = _ps3.Key3k3y;
                _areas = _ps3.GetOffsets.Select(a => (IImageArea)new ImageArea(a, AreaType.FileSystem)).ToList();
                _areas.Add(new ImageArea(this.Size, AreaType.None));
            }

            _info.SourceAreas = _areas.ToArray(); //read only copy for Output Task to use safely
            _info.IsFolderIndex = _chdMetaData?.IsFolderIndex ?? (_context.SourceFile.IndexFile != null && _info.SourceAreas.Length > 1);

            // [In] Detail: reader identity + resolved geometry (once per image).
            ILogScope inScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
            _info.SectionLog = inScope; // wire the per-section Trace log for the SectionProcessor
            if (inScope != null && inScope.IsEnabled(LogLevel.Detail))
                inScope.Log(LogLevel.Detail,
                    $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Iso9660)}{this.SystemType} size 0x{this.Size:X} areas {_areas.Count}"
                    + (string.IsNullOrEmpty(_detectVia) ? "" : $" detected: {_detectVia}"));

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

        public ImageType Type => ImageType.Iso9660;
        public long Size { get; }

        public long CurrentAreaEndImageOffset => _nextArea?.ImageOffset ?? this.Size;
        public int SectionSize => 0x200340; // _area.SectionSize; // 0x200000 + (0x200000 % _area.BlockSize == 0 ? 0 : _area.BlockSize - (0x200000 % _area.BlockSize)); //2MiB + remainder for next block of 16 sectors
        //public int SectionSize => 0x200000 + (0x200000 % _area.BlockSize == 0 ? 0 : _area.BlockSize - (0x200000 % _area.BlockSize)); //2MiB + remainder for next block of 16 sectors

        private int readBuf(byte[] b, ref int pos, int len, Stream stream = null)
        {
            int r = (stream ?? _stream).Read(b, pos, len);
            pos += r;
            return r;
        }

        public AreaType Read(IBuffer buffer, out IFileSystemInfo fsInfo)
        {
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
                            //read the first 16 blocks
                            readBuf(buf, ref pos, _header.Data.Length); //read more if any

                            _header = new ImageHeader(buf.Read(0, _tracklessAreaInsert ? 0 : _header.Data.Length), _area);
                            int pvdOffset = _header.Data.Length; // pos;
                            readHeaders(buf, ref pos, pvdOffset, _area.FsOffset);
                            bool rereadHeaders = false;
                            if (!_tracklessAreaInsert)
                            {
                                long sessionFsBase = calculateBlockOffset(buf, ref pos, _position - _area.ImageOffset);
                                rereadHeaders = sessionFsBase != _area.FsOffset;
                                if (rereadHeaders)
                                    _area.FsOffset = sessionFsBase;
                            }
                            else
                            {
                                _area.FsOffset = 0x8000; //no customheader
                                rereadHeaders = true;
                            }
                            if (rereadHeaders)
                            {
                                _header.FstContext.Ahead.RemoveAll(a => a.FsOffset >= _area.FsOffset); //remove added items before we knew the baseOffset was required
                                _header.FstContext.FileSystem.RemoveAll(a => a.FsOffset >= _area.FsOffset);
                                readHeaders(buf, ref pos, pvdOffset, _area.FsOffset); //adjust the offsets
                            }

                            //image with no index, 1 WipePartition, but short on the sector count (ps2 rockband)
                            long pvdSize = _header.PvdSectorCount * _area.BlockSize;
                            if ((_tracklessAreaInsert || (_areas.Count == 2 && _file.Index == 0)) && pvdSize != 0 && this.Size - (_area.ImageOffset + pvdSize) > _area.BlockSize * 75 * 4) //need to read adead at end of sector count or when files/udf WipePartition ends. Perhaps split a specific non FileSystem Area Type
                            {
                                _areas.Insert(1, new ImageArea(pvdSize, AreaType.FileSystem));
                                _tracklessAreaInsert = true;
                            }

                            if (_header.PvdEndFsOffset != 0)
                            {
                                byte[] hdr = new byte[_header.PvdEndFsOffset];
                                Buffer.Copy(buf, 0, _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, hdr, 0, hdr.Length, 0, hdr.Length, hdr.Length);
                                _header.PvdHeaderCrc = Crc.Compute(hdr);
                                _header.PvdHeaderXxHash = XXHash64.Compute(hdr, 0, hdr.Length);
                                if (_area.FsOffset == 0)
                                {
                                    _firstHeaderSize = _header.PvdEndFsOffset;
                                    _firstHeaderCrc = _header.PvdHeaderCrc;
                                    if (this.SystemType == SystemType.Dreamcast)
                                        _context.Settings.FixData<DreamcastFixData>().SetDisc(_header.Data, _area.BlockFsOffset, _firstHeaderCrc);

                                    _firstHeaderXxHash = _header.PvdHeaderXxHash;
                                    _firstHeaderSet = true;
                                }
                            }
                            if (SystemType == SystemType.PS3)
                            {
                                _context.Settings.FixData<Playstation3FixData>().SetDisc(_header.PvdHeaderCrc);
                                _header.FstContext.FixData = _context.Settings.FixData<Playstation3FixData>();
                                // Re-attach the PS3 encryption context to the freshly-created header.
                                // _header is replaced per FileSystem area here, but .Ps3 was only set on
                                // the original header in Setup(). Section processors bind to whichever
                                // _header instance is current when they are created; under the new core
                                // (lazy section-pool creation) most are created AFTER this reassignment,
                                // so without this line their Header.Ps3 is null and PS3 encrypt/decrypt
                                // is silently skipped, corrupting the output. (Legacy created all
                                // processors up front against the original header, hiding the bug.)
                                _header.Ps3 = _ps3;
                            }
                            _fsInfo = new FileSystemInfo(_position, _info.ImageSize, _header, _area, _info.ImageSize);

                            // Resolve the ISO9660 file system FULLY UP FRONT (before any data
                            // section of this area is emitted). Parsing fully before publish makes
                            // the file list immutable when parallel section processors read it (the
                            // AreaView snapshot), removing the old lockstep parse-while-emit hazard
                            // where the sorted OrderedList inserted below already-published indices.
                            if (!_fsInfo.InvalidFileSystem && _fsInfo.FileSystem != null)
                            {
                                // ISO9660's directory/path-table parse requires CONTIGUOUS, forward,
                                // non-overlapping block feeds: it stitches straddling directory
                                // extents across successive buffers and, per block, scans the whole
                                // buffer FS range once for gap markers. ResolveContiguous walks the
                                // area as adjacent SectionSize blocks (feeding each region exactly
                                // once) and, for the NON-LINEAR case, skips forward to a pending
                                // directory extent that lies beyond the contiguous cursor. The
                                // BufferStream caches the reads so the normal sequential read that
                                // follows is served from cache (works from zip/7z), and the cursor is
                                // restored on return.
                                // Bound the up-front FS read to the current track's extent so a read
                                // never crosses a track boundary (CHD Dreamcast interleaves data and
                                // audio tracks; a read spanning the data->audio boundary would be
                                // framed wholly as the start track by the container, corrupting the
                                // audio bytes and the container's running verify checksum). When
                                // there is no track (plain ISO) the whole image is the bound.
                                long fsUpperBound = _currTrack != null
                                    ? _currTrack.ImageOffset + _currTrack.Size
                                    : this.Size;
                                Iso9660FileSystemReader fsReader = new Iso9660FileSystemReader(
                                    (FileSystemInfo)_fsInfo, _area.ImageOffset, (int)_area.SectionSize);
                                FileSystemCoverage.ResolveContiguous(
                                    fsReader,
                                    _stream,
                                    _area,
                                    this.Size,
                                    fsUpperBound,
                                    (int)_area.SectionSize,
                                    () => new Buffer(_area.SectionSize, _area.IsEncryptionSupported));

                            }
                        }

                        if (_currTrack != null && _area.BlockFsOffset >= 0x10)
                        {
                            if (pos == 0)
                                readBuf(buf, ref pos, (int)_area.BlockSize); //read more if any
                            _currTrack.PhysicalOffset = Ecm.SectorToLba(buf, 0) * _area.BlockSize;
                        }
                    }

                    long fsOffset = _fsInfo?.ImageOffset ?? -1;
                    long fsSize = (fsOffset + _fsInfo?.Size) ?? -1;
                    if (_fsInfo != null && _fsInfo.InvalidFileSystem)
                        fsSize = 0;
                    else if (SystemType == SystemType.Dreamcast && _area.AreaNo + 1 == 2)
                        fsSize = 0;

                    // TODO(multi-session): only the first session's filesystem is walked; additional
                    // sessions on a multi-session CD are not enumerated. This is CD-only, so the image
                    // is small enough to fully buffer and seek — the historical forward-only-stream
                    // blocker no longer applies (BufferStream). Remaining work: locate each later
                    // session's PVD and emit an area per session in NextArea. Needs multi-session CD
                    // sample images + a test before implementing (do not change the single-session path
                    // blind). Tracked in NKitVault/10 Refactor/Warning Cleanup.md.
                    _nextArea = AreaInfo.NextArea(_area.ImageOffset, _fsInfo == null ? -1 : fsOffset, _fsInfo == null ? -1 : fsSize, _info.ImageSize, -1, AreaType.None, _areas, ++_areaNumber);

                    // [In] [ISO9660] Detail: reader's own per-area progress (once per area transition).
                    ILogScope areaScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                    if (areaScope != null && areaScope.IsEnabled(LogLevel.Detail))
                        areaScope.Log(LogLevel.Detail,
                            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Iso9660)}area {_area.AreaNo} [{_area.Type}] offset 0x{_area.ImageOffset:X} size 0x{_nextArea.ImageOffset - _area.ImageOffset:X}{(_area.IsEncrypted ? " enc" : "")}");

                    _nextArea.FsAddressMode = AddressMode.Area;
                    if (SystemType == SystemType.PS3)
                    {
                        if (_nextArea.ImageOffset > 0)
                            _nextArea.FsAddressMode = AddressMode.Relative;
                    }
                    else if (SystemType == SystemType.Dreamcast)
                    {
                        if (_areaBaseOffset != 0 && _nextArea.Type == AreaType.FileSystem && _nextArea.AreaNo > 2)
                            _nextArea.FsAddressMode = AddressMode.Relative;
                    }


                    length = (int)Math.Min(_area.SectionSize, _nextArea.ImageOffset - _position);
                }

                if (length - pos > 0)
                    readBuf(buf, ref pos, length - pos);

                if (length != 0 && pos == 0)
                    throw new HandledException(string.Format("Image.Read - No data read at Position {0} ({1}) - Requested {2}", _position.ToString("X"), _iso.RealPosition.ToString("X"), length.ToString()));

                buffer.ReInitialise(_area, true);

                if (_area.ImageOffset == _position && _ps3 != null)
                    detectPs3EncryptionStatus(buf, pos);

                //always write to encrypted the preprocessor will switch it to decompressed when it analyses things
                buffer.Update(_position, _position - _area.ImageOffset, length, 0, _area.IsEncryptionSupported && (_ps3 == null ? _area.IsEncrypted : _ps3.IsEncrypted), _skipped);

                //Trace.WriteLine($"{_area.AreaNo} : {_area.ImageOffset:x9} : {CiBuffer.BlockSize} : {CiBuffer.Size:x8}");

                _position += buffer.Size;

                if (_area.Type == AreaType.FileSystem)
                    fsInfo = _fsInfo;
                else
                    fsInfo = null;

                skip(); //after providing current data check if we can skip some of the current filesystem

                // Bound the shared cache: everything up to the returned-data mark (_position) has
                // been consumed and will not be re-read, so let the manager free cached blocks
                // below it. _position is the authoritative image-offset high-water.
                //
                // Two caches can accumulate: Layer-B (this _stream, wrapping the decoder _iso) and
                // Layer-A (the raw source the decoder reads from). For a pass-through container
                // (DefaultAsIso over an archive entry) the decoder reads the raw bytes at the same
                // image offset, so releasing Layer-A to _position is safe and necessary — without
                // it the raw-source cache grows to hold the whole image (a 40+ GiB PS3 .iso inside
                // an archive OOM'd here). IReleasable is only implemented by containers that read a
                // releasable BufferStream directly; others are a no-op.
                _stream.ReleaseTo(_position);
                (_iso as IReleasable)?.ReleaseTo(_position);

                return _area.Type;
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Image.Read: {ex.Message}");
            }
        }

        private void detectPs3EncryptionStatus(byte[] buffer, int pos)
        {
            bool supportsEncryption = _ps3.IsEncryptionSupported(_area.AreaNo);
            byte[] buf = buffer;

            if (_area.AreaNo == 1 && supportsEncryption)
            {
                // The file system is parsed FULLY UP FRONT (Iso9660FileSystemReader) before any
                // data section of the area is emitted, so by the time this runs the file list is
                // complete and immutable — no spin-wait on AllFoldersParsed and no defensive copy of
                // a concurrently-growing collection are needed.
                if (_fsInfo.AllFoldersParsed)
                {
                    IFsFile tstFile = _fsInfo.FileSystem.Files.FirstOrDefault(a => string.Compare(a.Name, "lic.dat", true) == 0 || string.Compare(a.Name, "eboot.bin", true) == 0);
                    if (tstFile != null) //disc has a file that can be checked
                    {
                        long imageOffset = Buffer.FsOffsetToOffset(tstFile.FsOffset, _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, false);

                        int offset = 0;
                        byte[] sector = null;
                        bool haveData = true;
                        if (_position >= imageOffset && imageOffset < (_position + pos - 0x800)) //do we have it in the current CiBuffer
                        {
                            offset = (int)(imageOffset - _position);
                            sector = buf;
                        }
                        else if (_stream.CanSeekTo(imageOffset)) //seek to the eboot / lic.dat
                        {
                            // CanSeekTo guards the reach: for a local (seekable) source the seek is
                            // free; for a forward-only archive it is allowed only when the target is
                            // within the cache reach limit. A PS3 image inside a zip can put the
                            // eboot tens of GiB in — far beyond the limit — so CanSeekTo returns
                            // false there and we fall through to the calculated-guess path rather
                            // than forcing the cache to hold the whole span (which OOM'd before).
                            long off = _stream.Position;
                            _stream.SafeSeek(imageOffset, SeekOrigin.Begin);
                            sector = new byte[offset + 0x800];
                            _stream.Read(sector, 0, sector.Length); //read and rewind
                            _stream.Seek(off, SeekOrigin.Begin);
                        }
                        else
                            haveData = false;

                        if (haveData)
                        {
                            _ps3.Ps3FileTest(sector, imageOffset, tstFile.Name, offset);
                            if (_context.SourceFile.Key == null)
                                _context.SourceFile.Key = _key = _ps3.Key;
                            // [In] [Ps3Enc] Detail: the key brute-force trail (masked keys only —
                            // first 4 ASCII chars + "...", never the key material).
                            ILogScope keyScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                            if (keyScope != null && keyScope.IsEnabled(LogLevel.Detail) && _ps3.LastKeyTestDiag != null)
                                keyScope.Log(LogLevel.Detail,
                                    $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Security)}{_ps3.LastKeyTestDiag}");
                        }
                    }

                    //attempt to work out if image is encrypted from the first block
                    if (_ps3.EncryptionTestSuccess) //has file
                    {
                        _context.Log.Info(() => $"Encryption [File test] - {(_ps3.IsEncrypted ? "Encrypted" : "Decrypted")}");
                        _info.SourceHasEncryption = _ps3.IsEncrypted;
                    }
                    else if (_ps3.HasKey || _context.Settings.AllKeys.Length != 0)
                    {
                        _ps3.Test1stEncryptionBlock(buf, 0x800, _area.ImageOffset);
                        _info.SourceHasEncryption = _ps3.IsEncrypted;
                        if (_info.ContainerType == ContainerType.Iso || _info.ContainerType == ContainerType.DecIso)
                            _info.ContainerType = _ps3.IsEncrypted ? ContainerType.Iso : ContainerType.DecIso;
                        _context.Log.Info(() => $"Encryption [Block test] - {(_ps3.IsEncrypted ? "Encrypted" : "Decrypted")}");
                        // [In] [Ps3Enc] Detail: the heuristic's signals + which branch decided the
                        // encrypted/plaintext verdict — so a wrong block-test call can be explained.
                        ILogScope encScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                        if (encScope != null && encScope.IsEnabled(LogLevel.Detail) && _ps3.LastEncTestDiag != null)
                            encScope.Log(LogLevel.Detail,
                                $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Security)}{(_ps3.IsEncrypted ? "encrypted" : "decrypted")} :: {_ps3.LastEncTestDiag}");
                    }
                    else
                    {
                        _ps3.IsEncrypted = _info.ContainerType != ContainerType.DecIso;
                        _info.SourceHasEncryption = _ps3.IsEncrypted;
                        _context.Log.Info(() => $"Encryption [No Key] - Defaulted to {(_ps3.IsEncrypted ? "Encrypted" : "Decrypted")} (Based on extension - .dec.iso [Decrypted] / .iso [Encrypted])");
                    }
                }
            }
            _area.SetSecurity(_info.SourceHasEncryption && supportsEncryption, supportsEncryption, false); //if enc is supported then set to true as it will be encrypted
        }

        // Reposition to a skip target. Reads flow through _stream (the BufferStream), which owns
        // the authoritative read cursor and drives the underlying _iso forward. Seeking _iso
        // directly (as this used to) desynced it from the BufferStream's internal cursor, so the
        // next read at the skipped-to track boundary returned bytes from the wrong offset (a
        // negative sector LBA / PhysicalOffset). Skips are always forward, so seeking _stream
        // forward keeps both cursors aligned without hitting the buffer's release floor.
        private void skipStreamTo(long imageOffset) => _stream.SafeSeek(imageOffset, SeekOrigin.Begin);

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

        private void readHeaders(byte[] buf, ref int pos, int pvdOffset, long baseOffset)
        {
            bool reread = _header.Pvds.Count > 1; //more than just system
            _header.Pvds.Clear();
            _header.Pvds.Add(FsType.System, ImageHeaderPvd.CreateSystem());
            int p = pvdOffset;

            while (true)
            {
                if (!reread)
                {
                    if (pos < p + _area.BlockSize)
                        readBuf(buf, ref pos, p + _area.BlockSize - pos);
                }
                if (!_header.SetPvd(_position + p, _position + p - _area.ImageOffset, buf.Read(p + _area.BlockFsOffset, _area.BlockFsSize), _area.BlockSize, _area.BlockFsOffset, _area.BlockFsSize, baseOffset))
                    break; //no more PVDs
                p += _area.BlockSize;
            }
        }

        private long calculateBlockOffset(byte[] buf, ref int pos, long areaOffset)
        {
            //get the root directory offset
            ImageHeaderPvd pvd = _header?.Pvds?.Values.OrderBy(a => a.RootDirectoryRecord == null || a.RootDirectoryRecord.Extent == 0 ? uint.MaxValue : a.RootDirectoryRecord.Extent)?.FirstOrDefault();
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

            return 0;
        }

        private AreaInfo setAreaBlockInfo(AreaInfo ai, IFileSystemInfo fsInfo, long fsOffset)
        {
            //_trackIdx = -1;

            int trackIndex = 0;
            int blockSize = 0x800;
            int blockFsOffset = 0x0;
            int blockFsSize = 0x800;

            SourceFileTrack trk = null;
            SourceFileTrack chdTrk = null;
            if ((_chdMetaData?.Tracks?.Count ?? 0) != 0)
            {
                chdTrk = _chdMetaData?.Tracks?.FirstOrDefault(a => a.ImageOffset == ai.ImageOffset);
                if (chdTrk != null)
                {
                    trackIndex = chdTrk.TrackIndex;
                    blockSize = chdTrk.BlockSize;
                    if (chdTrk.TrackType == IndexTrackType.Mode1 || chdTrk.TrackType == IndexTrackType.Mode1Raw)
                    {
                        blockFsOffset = chdTrk.TrackType == IndexTrackType.Mode1Raw ? 0x10 : 0;
                        blockFsSize = 0x800; //2048
                    }
                    else if (chdTrk.TrackType == IndexTrackType.Mode2 || chdTrk.TrackType == IndexTrackType.Mode2Form1 || chdTrk.TrackType == IndexTrackType.Mode2Form2 || chdTrk.TrackType == IndexTrackType.Mode2FormMix || chdTrk.TrackType == IndexTrackType.Mode2Raw)
                    {
                        blockFsOffset = 0x18;
                        blockFsSize = 0x800; // 0x914; //2336
                    }
                    else
                    {
                        blockFsOffset = 0x0;
                        blockFsSize = blockSize;
                    }
                }
            }
            if (chdTrk == null)
            {
                trk = _file.IndexFile == null ? null : _file.IndexFile.Items.FirstOrDefault(a => a.ImageOffset == ai.ImageOffset);
                if (trk != null)
                {
                    trackIndex = trk.TrackIndex;
                    blockSize = trk.BlockSize;
                    blockFsOffset = trk.BasicType == IndexTrackBasicType.Mode1 ? 0x10 : ((trk.BasicType == IndexTrackBasicType.Mode2 || trk.BasicType == IndexTrackBasicType.Cdi) ? 0x18 : 0);
                    blockFsSize = _isMode2Form2 && trk.BasicType == IndexTrackBasicType.Mode2 ? 0x914 : (trk.BasicType == IndexTrackBasicType.Audio ? trk.BlockSize : 0x800); //2048
                }
            }
            else
                trk = chdTrk;

            _currTrack = trk;

            if (ai.FsAddressMode == AddressMode.Relative)
            {
                ai.BaseOffset = ai.ImageOffset - _areaBaseOffset;
                if (SystemType == SystemType.Dreamcast)
                    ai.BaseOffset = Dreamcast.CalculateBaseOffset(SystemType, ai.Type, _file.IndexFile?.Items, trackIndex - 1, ai.ImageOffset - _areaBaseOffset);
                else
                    ai.FsOffset = fsOffset;
            }
            else if (ai.Type == AreaType.FileSystem) //track the last filesystem //if (ai.FsAddressMode == AddressMode.Area) // && ai.Type == AreaType.FileSystem)
                _areaBaseOffset = ai.ImageOffset;

            ai.SetBlock(blockSize, blockFsOffset, blockFsSize, 0x200000 + (0x200000 % blockSize == 0 ? 0 : blockSize - (0x200000 % blockSize))); //2MiB + remainder for next block of 16 sectors);

            SetAreaProperties(ai);

            return ai;
        }

        internal static void SetAreaProperties(AreaInfo ai)
        {
            switch (ai.Type)
            {
                case AreaType.FileSystem:
                    ai.SetProperties("Session", "Track", "Region", "BlockSize", "Mode1", "Mode2Form1", "Mode2Form2", "AreaOffsetBase", "SessionOffsetBase", "HeaderSize", "HeaderCrc", "HeaderXxHash", "PvdSectorCount", "PhysicalOffset", "TitleKeyCrc", "TitleKeyMissing", "ThreeKey", "DecryptionValid");
                    break;
                case AreaType.Audio:
                    ai.SetProperties("Session", "Track", "Region", "BlockSize", "Duration");
                    break;
                case AreaType.Other:
                    ai.SetProperties("Session", "Track", "Region", "Partition");
                    break;
                default:
                    break;
            }
        }

        //detection method used in aaru
        private bool isPs2(byte[] buf)
        {
            byte decryptByte = buf[0]; //xor the first byte value from everything

            int idx = -1;
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] ^= decryptByte;
                if (idx == -1 && buf[i] != 0x0)
                    idx = i;
            }

            //SHA256 localSha256Provider = SHA256.Create();
            //byte[] hash = localSha256Provider.ComputeHash(buf, 0, buf.Length);
            return idx != -1 && (buf.Equals(idx, ps2IdA.HexToBytes(), 0, ps2IdA.Length / 2) || buf.Equals(idx, ps2IdB.HexToBytes(), 0, ps2IdB.Length / 2));
        }

        public void SetScanProperties() => SetScanProperties(_context, SystemType, _ps3, _key, _firstHeaderSet, _firstHeaderWritten, _firstHeaderSize, _firstHeaderCrc, _firstHeaderXxHash, _header);

        internal static void SetScanProperties(IImageContext _context, SystemType systemType, PlayStation3 ps3, byte[] key, bool firstHeaderSet, bool firstHeaderWritten, long firstHeaderSize, uint firstHeaderCrc, ulong firstHeaderXxHash, ImageHeader header)
        {
            int partition = -1;
            SourceFileTrack track = null;
            FileSystemInfo fsInfo = null;
            Scan result = _context.Scan;
            bool hasEnc = _context.Scan.Areas.Any(a => a.AreaInfo.IsEncryptionSupported);

            foreach (ScanArea sra in _context.Scan.Areas)
            {
                track = _context.SourceFile?.IndexFile?.Items?.FirstOrDefault(a => a.ImageOffset == sra.ImageOffset);
                if (ps3 != null)
                    sra.AreaInfo.Properties["Region"] = sra.AreaInfo.AreaNo;
                sra.AreaInfo.Properties["Session"] = Dreamcast.SessionNo(systemType, sra.AreaInfo.AreaNo, (track == null || track.Session == 0) ? 0 : track.Session - 1);
                sra.AreaInfo.Properties["Track"] = (track == null || track.TrackIndex == 0) ? (ps3 == null ? sra.AreaInfo.AreaNo : 0) : track.TrackIndex - 1;

                switch (sra.Type)
                {
                    case AreaType.FileSystem:
                        fsInfo = (FileSystemInfo)sra.FsInfo;
                        sra.AreaInfo.Properties["BlockSize"] = sra.AreaInfo.BlockSize;
                        sra.AreaInfo.Properties["DataSize"] = sra.AreaInfo.BlockFsSize;
                        sra.AreaInfo.Properties["AreaOffsetBase"] = (ulong)sra.AreaInfo.BaseOffset; //not 0 if this area requires addressed to be adjusted (Partition 5 of DC)
                        sra.AreaInfo.Properties["SessionOffsetBase"] = (ulong)Buffer.FsOffsetToOffset(fsInfo.AreaInfo.FsOffset, fsInfo.AreaInfo.BlockSize, fsInfo.AreaInfo.BlockFsOffset, fsInfo.AreaInfo.BlockFsSize, true); //not 0 if this area requires addressed to be adjusted (Partition 5 of DC)
                        if (track != null && sra.AreaInfo.BlockSize >= 0x10)
                            sra.AreaInfo.Properties["PhysicalOffset"] = (ulong)track.PhysicalOffset;
                        if (firstHeaderSet && !firstHeaderWritten)
                        {
                            sra.AreaInfo.Properties["HeaderSize"] = (ulong)firstHeaderSize; //From 0 to to start of PvdEnd
                            sra.AreaInfo.Properties["HeaderCrc"] = (uint)firstHeaderCrc;
                            sra.AreaInfo.Properties["HeaderXxHash"] = (ulong)firstHeaderXxHash;
                            firstHeaderWritten = true;
                        }
                        if (fsInfo.Header.PvdSectorCount != 0 && sra.AreaInfo.FsAddressMode == AddressMode.Area)
                            sra.AreaInfo.Properties["PvdSectorCount"] = (ulong)fsInfo.Header.PvdSectorCount;
                        if (ps3 != null && sra.AreaInfo.AreaNo == 0)
                        {
                            if (hasEnc)
                            {
                                sra.AreaInfo.Properties["DecryptionValid"] = ((ImageHeader)_context.Header).Ps3.EncryptionTestSuccess;
                                if (key != null)
                                    sra.AreaInfo.Properties["TitleKeyCrc"] = Crc.Compute(key);
                                else
                                    sra.AreaInfo.Properties["TitleKeyMissing"] = true;
                            }
                            if (ps3.Has3k3yHeader)
                            {
                                byte[] hdr = header.Data.Read(Consts.Ps33k3yOffsetHeader, Consts.Ps33k3ySize);
                                if (hdr.ReadString(0, 2) == "De")
                                    hdr.WriteString(0, 2, "En");
                                string k = hdr.ToHexString().TrimEnd('0');
                                sra.AreaInfo.Properties["ThreeKey"] = k.PadRight(k.Length + (k.Length % 2));
                            }
                        }
                        break;
                    case AreaType.Audio:
                        sra.AreaInfo.Properties["BlockSize"] = sra.AreaInfo.BlockSize;
                        sra.AreaInfo.Properties["Duration"] = new TimeSpan((long)((double)sra.Size / ((double)44100 * (double)4) * 10000000));
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
            if (ps3 != null)
                result.Properties["DecryptedCRC"] = result.CrcDecrypted;
        }
        public SystemType SystemType { get; internal set; }

        public ISectionProcessor CreateSectionProcessor() => new SectionProcessor(_header, _context.ImageInfo.Mode, _info);

        public IBufferPreProcessor GetPreProcessor() => _preProcessor;

        public ISectionProcessor PatchSection(ScanSection s) => null;

        public IBuffer CreateBuffer() => new Buffer(SectionSize, true);

    }
}
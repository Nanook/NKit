using Nanook.NKit.Container;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Formats.FolderFiles
{
    /// <summary>
    /// A minimal <see cref="IImage"/> that presents a <see cref="FolderFilesAsIso"/> flat byte
    /// stream as a sequence of <see cref="AreaType.Other"/> areas — one per source file.
    ///
    /// Each area is exactly one file's byte content, delivered in <c>blockSize</c>-sized sections.
    /// The area's <c>AreaInfo.Properties</c> carry:
    ///   <c>"FileName"</c>  — relative path (e.g. "code/game.rpx")
    ///   <c>"FileSize"</c>  — file size as a long
    ///   <c>"FileIndex"</c> — zero-based index into the source file list
    ///
    /// No encryption, no hash tables — <c>IsEncrypted = false</c>, <c>HasSecurity = false</c>.
    /// Steps receive raw plaintext bytes in <c>section.Decrypted</c> (= <c>section.Encrypted</c>).
    /// </summary>
    internal sealed class FolderFilesImage : IImage
    {
        private const int BlockSize    = 0x8000;
        private const int _sectionSize = BlockSize * 0x40; // 2 MiB per section

        private readonly FolderFilesAsIso _iso;
        private readonly IImageContext    _context;
        private readonly FolderFilesImageInfo _info;

        // Pre-built area list: _areas[0..N-1] = one per file, _areas[N] = None sentinel.
        // Indexed directly so AreaNo is always sequential from 0 with no counter drift.
        private readonly List<AreaInfo> _areas;

        private AreaInfo  _area;      // area currently being read
        private AreaInfo  _nextArea;  // look-ahead (drives section length calc)
        private long      _position;
        private int       _currentFileIdx = -1;

        public FolderFilesImage(IImageContext context, FolderFilesAsIso iso)
        {
            _iso     = iso;
            _context = context;

            _info = new FolderFilesImageInfo
            {
                ImageSize  = iso.Size,
                SourceSize = iso.Size,
                SystemType = SystemType.WiiU,
                Type       = ImageType.Iso9660,
                ContainerType = ContainerType.Wua,
                StepImageInfo = new StepsImageInfo
                {
                    Size      = iso.Size,
                    Checksums = new Checksums(),
                    IsIndex   = false,
                }
            };
            _context.ImageInfo = _info;

            Size       = iso.Size;
            SystemType = SystemType.WiiU;

            // Pre-build all AreaInfo objects with sequential AreaNo 0..N-1, plus a None sentinel.
            _areas = new List<AreaInfo>(iso.Files.Count + 1);
            for (int i = 0; i < iso.Files.Count; i++)
            {
                var f  = iso.Files[i];
                var ai = new AreaInfo(f.StreamOffset, AreaType.Other, i);
                ai.SetBlock(BlockSize, 0, BlockSize, _sectionSize);
                ai.SetSecurity(false, false, false);
                ai.SetProperties("FileName", "FileSize", "FileIndex");
                ai.Properties["FileName"]  = f.RelPath;
                ai.Properties["FileSize"]  = f.Size;
                ai.Properties["FileIndex"] = i;
                _areas.Add(ai);
            }
            // Sentinel: AreaType.None at the end of the stream
            var sentinel = new AreaInfo(iso.Size, AreaType.None, iso.Files.Count);
            sentinel.SetBlock(BlockSize, 0, BlockSize, _sectionSize);
            sentinel.SetSecurity(false, false, false);
            _areas.Add(sentinel);

            _position = 0;
            _area     = _areas.Count > 1 ? _areas[0]  : _areas[_areas.Count - 1]; // file 0 or sentinel
            _nextArea = _areas.Count > 1 ? _areas[1]  : _areas[_areas.Count - 1]; // file 1 or sentinel
        }

        // ── IImage ────────────────────────────────────────────────────────────────────

        public void Setup()
        {
            _context.Scan = new Scan(SystemType, _context.SourceFile.Name);
        }

        public ImageType Type  => ImageType.Iso9660;
        public long Size       { get; }
        public long CurrentAreaEndImageOffset => _nextArea.ImageOffset;
        public SystemType SystemType { get; }

        public IBufferPreProcessor GetPreProcessor()
            => new FolderFilesPreProcessor();

        public ISectionProcessor CreateSectionProcessor()
            => new FolderFilesSectionProcessor();

        public int SectionSize => _sectionSize;

        public ISectionProcessor PatchSection(ScanSection section) => null;

        public IBuffer CreateBuffer()
            => new Buffer(SectionSize, false);

        public void SetScanProperties()
        {
            if (_context.Scan?.Properties != null)
            {
                _context.Scan.Properties["System"]    = SystemType.ToString();
                _context.Scan.Properties["Container"] = ContainerType.Wua.ToString();
            }
        }

        // ── Core read loop ────────────────────────────────────────────────────────────

        public AreaType Read(IBuffer buffer, out IFileSystemInfo fsInfo)
        {
            fsInfo = null;

            if (_context.SkipType == SkipType.End)
            {
                buffer.ReInitialise(_area, true);
                buffer.Update(_position, _position - _area.ImageOffset, 0, 0, false, true);
                return _area.Type;
            }

            // Advance through areas (by index into pre-built list) until the current area
            // contains _position. _currentFileIdx starts at -1 so first call advances to 0.
            while (_currentFileIdx + 1 < _iso.Files.Count &&
                   _position >= _areas[_currentFileIdx + 1].ImageOffset)
            {
                _currentFileIdx++;
                _area     = _areas[_currentFileIdx];
                _nextArea = _areas[_currentFileIdx + 1]; // always valid: sentinel is at [N]
            }

            // Enter the first file on the very first Read() call
            if (_currentFileIdx < 0 && _iso.Files.Count > 0)
            {
                _currentFileIdx = 0;
                _area     = _areas[0];
                _nextArea = _areas[1];
            }

            if (_area.Type == AreaType.None || _position >= Size)
            {
                buffer.ReInitialise(_area, true);
                buffer.Update(_position, 0, 0, 0, false, false);
                return _area.Type;
            }

            int length = (int)Math.Min(_area.SectionSize, _nextArea.ImageOffset - _position);
            if (length <= 0)
            {
                buffer.ReInitialise(_area, true);
                buffer.Update(_position, _position - _area.ImageOffset, 0, 0, false, false);
                return _area.Type;
            }

            byte[] dec = buffer.Decrypted;
            int read = 0;
            _iso.Position = _position;
            while (read < length)
            {
                int n = _iso.Read(dec, read, length - read);
                if (n == 0) break;
                read += n;
            }

            buffer.ReInitialise(_area, true);
            buffer.Update(_position, _position - _area.ImageOffset, read, 0, false, false);
            _position += read;

            return _area.Type;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private AreaInfo advanceArea() => null; // unused — kept for linker
    }

    // ── Supporting types ──────────────────────────────────────────────────────────────

    internal sealed class FolderFilesImageInfo : IImageInfo
    {
        public long ImageSize  { get; internal set; }
        public long ReadLength { get; internal set; }
        public long FixSize    { get; internal set; }
        public long SourceSize { get; internal set; }
        public SystemType SystemType { get; internal set; }
        public IStepsImageInfo StepImageInfo { get; internal set; }
        public ImageType Type { get; internal set; }
        public ContainerType ContainerType { get; internal set; }
        public ReadMode Mode { get; set; }
        public uint FixCrc { get; internal set; }
        public IFixData FixData { get; set; }
        public bool IsFolderIndex { get; internal set; }
        public IImageArea[] SourceAreas { get; internal set; }
        public bool IsFullImage { get; internal set; }
        public long Multiplier { get; internal set; }
        public bool OutputEncryption { get; internal set; }
        public bool OutputHashes { get; internal set; }
        public bool Patching { get; internal set; }
        public bool SourceSupportsEncryption { get; internal set; }
        public bool SourceHasEncryptedHashes { get; internal set; }
        public bool SourceHasEncryption { get; internal set; }
        public bool SourceHasHashes { get; internal set; }
        public MediaType MediaType { get; internal set; }
        public SourceFileTrack[] Tracks { get; internal set; }
        public ILogScope SectionLog { get; internal set; }
        public System.Nullable<CdType> CdDiscType { get; internal set; }
    }

    /// <summary>Pass-through pre-processor — no decryption or hash parsing needed.</summary>
    internal sealed class FolderFilesPreProcessor : IBufferPreProcessor
    {
        public long ImageSize => 0;
        public void Complete() { }
        public void GetFiles(IFileSystemInfo fsInfo, IBuffer buffer, System.Collections.Generic.List<IFsFile> fst, ref int fstState) { }
        public void PreDecrypt(IFileSystemInfo fsInfo, IBuffer buffer) { }
        public void DiscoverMarkers(IFileSystemInfo fsInfo, IBuffer buffer) { }
    }

    /// <summary>
    /// Minimal section processor for folder-file sections.
    /// Passes bytes straight through — no checksums, no hash validation.
    /// </summary>
    internal sealed class FolderFilesSectionProcessor : SectionProcessorBase, ISectionProcessor
    {
        private static readonly PatchInfo _staticPatchInfo = new PatchInfo();

        public FolderFilesSectionProcessor() : base()
        {
            // Pre-initialize a stub buffer so _buffer is never null when PatchInfo is
            // accessed before the real buffer is assigned by the pool factory.
            // The pool factory will overwrite this with the real CreateBuffer() result.
            this.Buffer = new Buffer(0x8000, false);
        }

        // Override PatchInfo to always return non-null — the base Buffer may not yet be
        // set when Scan.SectionProcessed accesses PatchInfo; return a static stub.
        public new IPatchInfo PatchInfo => base.Buffer?.PatchInfo ?? _staticPatchInfo;

        public override void Complete()
        {
            if (base.Buffer == null) { Status = CompletionStatus.Complete; return; }
            base.Complete();
        }
        public override void Update()   => base.Update();
        public override void PostProcess()
        {
            // Compute section-level XxHash so Scan.SectionProcessed gets a valid hash.
            // No per-file FST items exist for folder sections, so this is just the
            // whole-section hash (same as the last line of XxHashParallel in the base).
            this.XxHash = XXHash64.Compute(this.Decrypted, 0, (int)this.Size);
        }

        public void Process()
        {
            // No file items and no checksums needed for raw folder-file sections.
            // ParallelChecksumAndCleanse is intentionally not called here since Items
            // is null after Update() and there are no FsFile entries to CRC.
        }

        public new System.Collections.Generic.List<MetaData> MissingData => null;

        // ISection pass-through — SectionProcessorBase provides most members via Buffer
        public System.Collections.Generic.IEnumerable<NonCreatableData> NonCreatableItems
            => System.Linq.Enumerable.Empty<NonCreatableData>();

        public void Write(int fsOffset, System.IO.Stream fromStream, int size)
            => base.Buffer?.WriteFsFromStream(fsOffset, fromStream, size);
        public void WriteBytes(int fsOffset, byte[] bytes, int offset, int size)
            => base.Buffer?.WriteFs(bytes, offset, base.Buffer.BlockSize, 0, base.Buffer.BlockSize, fsOffset, size);
        public void Read(int fsOffset, int size, System.IO.Stream toStream)
            => base.Buffer?.ReadFsToStream(fsOffset, toStream, size);
        public byte[] ReadBytes(int fsOffset, int size)
            => base.Buffer?.ReadFsBytes(fsOffset, size);
    }
}

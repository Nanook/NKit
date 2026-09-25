using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// PS3 IRD Fix decorator over a source IAsIso (a PS3_DISC.SFB folder/archive, detected as
    /// <see cref="ContainerType.Ps3Jb"/>).
    ///
    /// <para>
    /// A PS3 "JB" (jailbroken/extracted) source is a folder of the game's files plus PS3_DISC.SFB /
    /// PARAM.SFO — NOT a disc image. The full ISO is reconstructed from a matching IRD: the IRD
    /// header supplies the disc header + full file-system directory tree, the game files supply the
    /// content at their file-system offsets, and the IRD footer supplies the tail. Missing/mismatched
    /// files are substituted from the fix-file store; every file is MD5-verified against the IRD and
    /// the results recorded on <see cref="ImageInfo.IrdResults"/> for the Fix step to report.
    /// </para>
    ///
    /// <para>
    /// Previously this was a bespoke <c>ImagePs3IrdFix : IImage</c> that duplicated the ISO9660
    /// area/scan/section pipeline. As a decorator it REBUILDS the ISO on demand into the read
    /// stream, so the standard <see cref="Iso9660.Image"/> consumes it like any other PS3 ISO —
    /// getting the up-front file-system resolve, gap-marker discovery, scan and section processing
    /// for free (matching the Wii/GC Fix decorator model).
    /// </para>
    ///
    /// <para>
    /// Reconstruction is RANDOM-ACCESS (position-independent): the consumer (Iso9660.Image) peeks
    /// ahead for the up-front FS resolve and re-reads regions, so any Read(pos,count) must yield the
    /// same bytes regardless of order. The file layout is precomputed as an offset-sorted segment
    /// list; MD5 verification / IRD-result recording happens once, up front, in prepare().
    /// </para>
    /// </summary>
    internal sealed class Ps3FixAsIso : Stream, IAsIso
    {
        private const int SectorSize = 0x800;

        private readonly IAsIso _inner;      // the SFB DefaultAsIso (kept for IAsIso delegation)
        private readonly IImageContext _context;
        private readonly SourceFile _file;

        private long _position;              // reconstructed-image position
        private long _size;                  // reconstructed full ISO size (from IRD/PS3 regions)
        private bool _prepared;
        private bool _passThrough;           // no IRD / fix unavailable -> serve inner unchanged

        // Resolution state (built once in prepare()).
        private SourceFileSystem _fileSystem;
        private List<FileItem> _files;
        private string _basePath;
        private Playstation3FixData _fixData;
        private FixIrd _ird;
        private byte[] _key;
        private PlayStation3 _ps3;

        // Precomputed, immutable file layout (offset-sorted). Each entry places [Offset, Offset+Size)
        // of the reconstructed image from a resolved source (original file / fix-file bytes / none).
        private List<Placed> _placed;        // non-system content files, sorted by Offset
        private long _footerPos;             // absolute offset where the IRD footer sits

        // MD5-verification results; surfaced to ImageInfo (read by FixPs3IrdStep) once available.
        private IrdFileResults _irdResults;
        private bool _irdResultsPublished;

        // A placed content file: its reconstructed-image range and the resolved source bytes/stream.
        // FixBytes (when non-null) is served directly; otherwise Path/ArchiveItem is opened per read.
        // When neither is set the region is missing and stays zero-filled.
        private sealed class Placed
        {
            public long Offset;              // reconstructed-image offset
            public long Size;                // file size in the reconstructed image
            public byte[] FixBytes;          // fix-file substitute content (served verbatim), or null
            public string FilePath;          // local file to open, or null
            public FileItem ArchiveItem;     // archive entry to open, or null
            public List<(long size, string path, FileItem item)> SplitPaths; // ordered split parts, or null
        }

        internal Ps3FixAsIso(IAsIso inner, IImageContext context)
        {
            _inner = inner;
            _context = context;
            _file = context.SourceFile;
            _size = inner.Size;
            _position = 0;
        }

        /// <summary>
        /// Wrap a PS3-JB (SFB) source for the Fix task. Returns null when it should not wrap
        /// (not a PS3-JB source, not a Fix step, or the verify re-read pass). Mirrors the
        /// Create convention of GcFixAsIso / WiiFixAsIso.
        /// </summary>
        public static IAsIso Create(IAsIso iso, SystemType detectedSystem, IImageContext context)
        {
            if (iso.Format != ContainerType.Ps3Jb || context.TaskType != TaskType.Fix
                || context.StepInfo?.StepType == TaskType.Verify
                || iso is Ps3FixAsIso)
                return null;

            return new Ps3FixAsIso(iso, context);
        }

        // ── IAsIso pass-through metadata ────────────────────────────────────────
        public ContainerType Format => ContainerType.Ps3Jb;
        public bool Seekable => true;               // reconstructed image is fully addressable
        public bool SeekRequired => _inner.SeekRequired;
        public long RealPosition => _inner.RealPosition;
        public long RealSize => _inner.RealSize;
        public bool SizeEstimated => false;         // size is exact (from the IRD PS3 regions)
        // Expose the IRD's expected full-image CRC (when the IRD carries one) so the pipeline's
        // StepImageInfo.Checksums gets it — this is what drives the Fix step's InChecksums verify
        // method (matching the old ImagePs3IrdFix, which set StepImageInfo.Checksums.Crc = ImageCrc).
        public Checksums Checksums
        {
            get
            {
                if (!_prepared)
                    prepare();
                Checksums c = new Checksums();
                if (_ird?.HasImageCrc ?? false)
                    c.Crc = _ird.ImageCrc;
                return c;
            }
        }
        public Checksums CustomChecksums() => _inner.CustomChecksums();
        public NKitHeader NKitHeader => _inner.NKitHeader;
        public void SetRemovedBlock(Action<MetaData> setBlock) => _inner.SetRemovedBlock(setBlock);
        public void Complete() => _inner.Complete();

        public int Construct(Stream stream, bool allowSeek)
        {
            int r = _inner.Construct(stream, allowSeek);
            _size = _inner.Size;
            _position = 0;
            return r;
        }

        public long Size
        {
            get
            {
                if (!_prepared)
                    prepare();
                return _size;
            }
        }

        // ── Resolution (once, on first Size/Read) ────────────────────────────────
        private void prepare()
        {
            if (_prepared)
                return;
            _prepared = true;

            _basePath = _file.BasePath.Replace('\\', '/');
            _fileSystem = (_file.ArchiveFiles?.Length ?? 0) != 0
                ? new SourceFileSystem(_file.ArchiveFiles, null)
                : new SourceFileSystem(new DirectoryInfo(_basePath), null);

            if ((_file.ArchiveFiles?.Length ?? 0) != 0)
                _files = _fileSystem.GetFiles(new FileMask(FileMask.MaskToRegex("*"), true), null);

            Dictionary<string, string> sfb = readSfbFromFile();
            Dictionary<string, string> sfo = readSfoFromFile();

            _context.Settings.LoadFixData(new Playstation3FixData());
            _fixData = _context.Settings.FixData<Playstation3FixData>();
            _fixData.LoadFixFiles();

            FixIrd[] irds = _fixData.MatchIrds(
                sfb.ContainsKey("TITLE_ID") ? sfo["TITLE_ID"] : null,
                sfb.ContainsKey("VERSION") ? sfb["VERSION"] : null,
                sfo.ContainsKey("TITLE_ID") ? sfo["TITLE_ID"] : null,
                sfo.ContainsKey("TITLE") ? sfo["TITLE"] : null,
                sfo.ContainsKey("VERSION") ? sfo["VERSION"] : null,
                sfo.ContainsKey("PS3_SYSTEM_VER") ? sfo["PS3_SYSTEM_VER"] : null,
                sfb.ContainsKey("APP_VER") ? sfo["APP_VER"] : null);

            if (irds.Length == 0)
                throw new HandledException($"No IRDs located for TitleId: {sfb["TITLE_ID"]}");

            _ird = irds[0];
            _ird.Populate();
            _key = _file.Key = _ird.Key;
            _ps3 = new PlayStation3(_ird.Header, _key, 0, new byte[0][]);
            _size = _ps3.Size;
            _footerPos = _size - _ird.Footer.Length;
            _irdResults = new IrdFileResults();

            // Parse the IRD header's file system: the header (0x8000) is the system area; the PVDs
            // (ISO9660/Joliet/UDF descriptors) follow and MUST be parsed (SetPvd loop) so
            // Fst.SetFsData can walk the whole directory tree — without them only the baseline
            // system entries are produced. Mirrors ImagePs3IrdFix.readHeaders.
            AreaInfo area = makeArea();
            ImageHeader header = new ImageHeader(_ird.Header.Read(0, 0x8000), area);
            header.Ps3 = _ps3;
            header.Pvds.Clear();
            header.Pvds.Add(FsType.System, ImageHeaderPvd.CreateSystem());
            for (int p = 0x8000; ; p += area.BlockSize)
            {
                if (!header.SetPvd(p, p, _ird.Header.Read(p + area.BlockFsOffset, area.BlockFsSize),
                        area.BlockSize, area.BlockFsOffset, area.BlockFsSize, 0))
                    break; // no more PVDs
            }

            Iso.Iso9660.FileSystemInfo fsInfo = new Iso.Iso9660.FileSystemInfo(0, _size, header, area, _size);
            Fst fst = (Fst)fsInfo.FileSystem;
            Buffer b = new Buffer(false, _ird.Header);
            b.ReInitialise(area, true);
            b.Update(0, 0, _ird.Header.Length, 0, false, false);
            fst.SetFsData(b);

            // Resolve + verify every non-system content file ONCE, building the immutable placement
            // list and the IRD results. Files inside the IRD-header region are served from the
            // header blob directly (see reconstruct), so only files at/after the header length are
            // placed from the source here. System entries are FST structure, not real data.
            buildPlacements(fst.Files);
        }

        private void buildPlacements(List<IFsFile> files)
        {
            _placed = new List<Placed>();

            foreach (IFsFile f in files)
            {
                if (f.IsSystemFile)
                    continue;
                if (f.FsOffset < _ird.Header.Length)
                    continue; // served from the header blob
                if (f.SplitIndex != 0)
                    continue; // handled by the split's first part (SplitParts placement)

                Placed pl = resolveFile(f);
                if (pl != null)
                    _placed.Add(pl);
            }

            _placed.Sort((a, x) => a.Offset.CompareTo(x.Offset));
        }

        // Resolve one file to its reconstructed placement, verifying its MD5 against the IRD and
        // recording the result. Mirrors ImagePs3IrdFix.writeBuf's per-file open/verify/fix logic,
        // but does it once up front and keeps the resolved source for random-access reads.
        private Placed resolveFile(IFsFile find)
        {
            long fullSize = find.SplitParts?.Size ?? find.FsSize;
            int l = Array.IndexOf(_ird.FileKeys, (find.SplitParts?.Parts?[0].FsFile ?? find).FsOffset / SectorSize);
            byte[] wantMd5 = l == -1 ? null : _ird.FileHashes[l];

            Placed pl = new Placed { Offset = find.FsOffset, Size = fullSize };
            byte[] actualMd5 = null;
            bool missing = false;
            string fixFileName = null;

            // Collect the ordered source streams for this file (single file, or split parts).
            List<Stream> parts = openParts(find);
            bool haveAll = parts.All(a => a != null);

            if (haveAll)
            {
                // Hash the concatenated content to verify against the IRD.
                using (MD5 md5 = MD5.Create())
                {
                    foreach (Stream s in parts)
                    {
                        s.Position = 0;
                        byte[] buf = new byte[0x100000];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                            md5.TransformBlock(buf, 0, n, null, 0);
                    }
                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    actualMd5 = md5.Hash;
                }
            }
            else
                missing = true;

            bool md5Ok = wantMd5 != null && actualMd5 != null && actualMd5.Equals(0, wantMd5, 0, 0x10);

            if (!md5Ok && wantMd5 != null && (find.SplitParts?.Parts?.Count ?? 0) == 0)
            {
                // Try a fix-file substitute (matches by size + expected MD5).
                byte[] fixBytes = _fixData.LookupFixFile(find.FsSize, wantMd5, out fixFileName);
                if (fixBytes != null)
                {
                    foreach (Stream s in parts) s?.Dispose();
                    pl.FixBytes = fixBytes;
                    missing = false;
                    md5Ok = true;
                    _irdResults.Add(new IrdFileResult
                    {
                        File = find,
                        FixFileName = fixFileName,
                        Md5 = wantMd5,
                        IsMissing = false,
                        SizeIsValid = fixBytes.Length == fullSize,
                        IsMd5Valid = true
                    });
                    return pl;
                }
            }

            // Not substituted: serve from the source file(s) directly (or leave missing/zero-filled).
            long haveSize = 0;
            if (haveAll)
            {
                haveSize = parts.Sum(a => a.Length);
                bindSource(pl, find);
            }
            foreach (Stream s in parts) s?.Dispose();

            _irdResults.Add(new IrdFileResult
            {
                File = find,
                FixFileName = null,
                Md5 = wantMd5,
                IsMissing = missing,
                SizeIsValid = haveSize == fullSize,
                IsMd5Valid = md5Ok
            });

            return missing && pl.FixBytes == null ? pl : pl; // always place (missing -> zero-fill)
        }

        // Record where this file's bytes come from for random-access reads (single or split).
        private void bindSource(Placed pl, IFsFile find)
        {
            // Split files are contiguous in the reconstructed image; store the ordered part paths.
            if (find.SplitParts?.Parts != null && find.SplitParts.Parts.Count != 0)
            {
                pl.SplitPaths = new List<(long size, string path, FileItem item)>();
                foreach (IFsFilePart part in find.SplitParts.Parts)
                {
                    resolvePath(part.FsFile.FullName, out string path, out FileItem item);
                    pl.SplitPaths.Add((part.FsFile.FsSize, path, item));
                }
            }
            else
            {
                resolvePath(find.FullName, out string path, out FileItem item);
                pl.FilePath = path;
                pl.ArchiveItem = item;
            }
        }

        private List<Stream> openParts(IFsFile find)
        {
            List<Stream> parts = new List<Stream>();
            if (find.SplitParts?.Parts != null && find.SplitParts.Parts.Count != 0)
                foreach (IFsFilePart part in find.SplitParts.Parts)
                    parts.Add(getFile(part.FsFile.FullName));
            else
                parts.Add(getFile(find.FullName));
            return parts;
        }

        private AreaInfo makeArea()
        {
            AreaInfo ai = new AreaInfo(0, AreaType.FileSystem, 0);
            ai.SetBlock(SectorSize, 0, SectorSize, 0x200000 + (0x200000 % SectorSize == 0 ? 0 : SectorSize - (0x200000 % SectorSize)));
            ai.SetSecurity(false, false, false);
            return ai;
        }

        // ── Stream / reads (random access, stateless) ─────────────────────────────
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_prepared)
                prepare();

            if (_passThrough)
            {
                ((Stream)_inner).Position = _position;
                int n = ((Stream)_inner).Read(buffer, offset, count);
                _position += n;
                return n;
            }

            publishIrdResults();

            long available = _size - _position;
            if (available <= 0)
                return 0;

            int toRead = (int)Math.Min(count, available);
            // The reconstructed image is a DECRYPTED ISO: file-system offset == image offset
            // (block 0x800, no header/hash framing), so we fill the raw output buffer by absolute
            // position. Zero-fill first (gaps between placed regions stay null).
            Array.Clear(buffer, offset, toRead);
            reconstruct(buffer, offset, _position, toRead);
            _position += toRead;
            return toRead;
        }

        // Fill buffer[offset..offset+count) with the reconstructed bytes for image range
        // [startPos, startPos+count). Position-independent: header/footer are copied from the IRD
        // blobs; content files are located by binary search over the immutable placement list and
        // read from their resolved source at the right within-file offset. Gaps stay zero-filled.
        private void reconstruct(byte[] buffer, int offset, long startPos, int count)
        {
            // Header region (IRD header blob).
            copyBlob(_ird.Header, 0, buffer, offset, startPos, count);
            // Footer region (IRD footer blob).
            copyBlob(_ird.Footer, _footerPos, buffer, offset, startPos, count);

            // Content files intersecting [startPos, startPos+count).
            long end = startPos + count;
            int idx = firstPlacedAtOrAfter(startPos);
            // Also check the placement just before idx (it may extend into the range).
            if (idx > 0 && _placed[idx - 1].Offset + _placed[idx - 1].Size > startPos)
                idx--;

            for (int i = idx; i < _placed.Count; i++)
            {
                Placed pl = _placed[i];
                if (pl.Offset >= end)
                    break;
                long isectStart = Math.Max(startPos, pl.Offset);
                long isectEnd = Math.Min(end, pl.Offset + pl.Size);
                if (isectEnd <= isectStart)
                    continue;

                int bufPos = offset + (int)(isectStart - startPos);
                long inFile = isectStart - pl.Offset;
                int len = (int)(isectEnd - isectStart);
                placeBytes(pl, inFile, buffer, bufPos, len);
            }
        }

        // Copy the intersection of blob range [blobImageOffset, +blob.Length) with the requested
        // [startPos, startPos+count) into the output buffer.
        private static void copyBlob(byte[] blob, long blobImageOffset, byte[] buffer, int offset, long startPos, int count)
        {
            if (blob == null || blob.Length == 0)
                return;
            long end = startPos + count;
            long isectStart = Math.Max(startPos, blobImageOffset);
            long isectEnd = Math.Min(end, blobImageOffset + blob.Length);
            if (isectEnd <= isectStart)
                return;
            int bufPos = offset + (int)(isectStart - startPos);
            Array.Copy(blob, isectStart - blobImageOffset, buffer, bufPos, (int)(isectEnd - isectStart));
        }

        // Place [inFile, inFile+len) of a resolved file into buffer[bufPos..].
        private void placeBytes(Placed pl, long inFile, byte[] buffer, int bufPos, int len)
        {
            if (pl.FixBytes != null)
            {
                int chunk = (int)Math.Min(len, Math.Max(0, pl.FixBytes.Length - inFile));
                if (chunk > 0)
                    Array.Copy(pl.FixBytes, inFile, buffer, bufPos, chunk);
                return; // rest (if fix file shorter) stays zero
            }

            if (pl.SplitPaths != null)
            {
                // Walk the split parts to find the one covering inFile.
                long partStart = 0;
                foreach ((long size, string path, FileItem item) in pl.SplitPaths)
                {
                    if (inFile < partStart + size)
                    {
                        long inPart = inFile - partStart;
                        int chunk = (int)Math.Min(len, size - inPart);
                        readSourceAt(path, item, inPart, buffer, bufPos, chunk);
                        bufPos += chunk;
                        len -= chunk;
                        inFile += chunk;
                        if (len <= 0)
                            return;
                    }
                    partStart += size;
                }
                return;
            }

            if (pl.FilePath != null || pl.ArchiveItem != null)
                readSourceAt(pl.FilePath, pl.ArchiveItem, inFile, buffer, bufPos, len);
            // else missing: leave zero-filled
        }

        // Open the source (local file or archive entry), seek to 'inFile', read up to 'len' bytes.
        private void readSourceAt(string path, FileItem item, long inFile, byte[] buffer, int bufPos, int len)
        {
            Stream s = null;
            try
            {
                if (path != null)
                    s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0x10000);
                else if (item != null)
                    s = SourceFileSystem.CreateReader(item, null, null).OpenRead(item);
                if (s == null)
                    return;

                if (s.CanSeek)
                    s.Position = inFile;
                else
                    skip(s, inFile);
                readExact(s, buffer, bufPos, len);
            }
            finally
            {
                s?.Dispose();
            }
        }

        private static void skip(Stream s, long count)
        {
            byte[] tmp = new byte[0x10000];
            while (count > 0)
            {
                int n = s.Read(tmp, 0, (int)Math.Min(tmp.Length, count));
                if (n == 0)
                    break;
                count -= n;
            }
        }

        // First placement whose Offset >= pos (binary search over the sorted list).
        private int firstPlacedAtOrAfter(long pos)
        {
            int lo = 0, hi = _placed.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_placed[mid].Offset < pos)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        // Surface the verification results onto the shared ImageInfo once the Image has created it
        // (the Image constructor's header read happens before ImageInfo is assigned).
        private void publishIrdResults()
        {
            if (_irdResultsPublished)
                return;
            if (_context.ImageInfo is ImageInfo info)
            {
                info.IrdResults = _irdResults;
                _irdResultsPublished = true;
            }
        }

        // ── SFB / SFO reads ───────────────────────────────────────────────────────
        private Dictionary<string, string> readSfbFromFile()
        {
            Stream sfb = getFile("PS3_DISC.SFB");
            if (sfb == null)
                throw new HandledException("PS3_DISC.SFB not found");
            try
            {
                byte[] b = new byte[sfb.Length];
                sfb.Read(b, 0, b.Length);
                if (b.ReadString(0x0, 0x4) != ".SFB")
                    throw new HandledException("PS3_DISC.SFB has invalid header ID");
                return PlayStation3.ReadDiscSfb(b);
            }
            finally
            {
                sfb.Close();
            }
        }

        private Dictionary<string, string> readSfoFromFile()
        {
            Stream sfo = getFile("PS3_GAME/PARAM.SFO");
            if (sfo == null)
                throw new HandledException("PS3_GAME/PARAM.SFO not found");
            try
            {
                byte[] b = new byte[sfo.Length];
                sfo.Read(b, 0, b.Length);
                if (b.ReadString(0x0, 0x4) != "\0PSF")
                    throw new HandledException("PS3_GAME/PARAM.SFO has invalid header ID");
                return PlayStation3.ReadParamSfo(b);
            }
            finally
            {
                sfo.Close();
            }
        }

        // Resolve a file-system relative name to a concrete local path or archive entry.
        private void resolvePath(string relativeName, out string path, out FileItem item)
        {
            path = null;
            item = null;
            if ((_file.ArchiveFiles?.Length ?? 0) != 0)
            {
                string n = relativeName.TrimStart('/');
                item = _files.FirstOrDefault(a => a.PathFileName == n);
            }
            else if (File.Exists(_basePath + relativeName))
                path = _basePath + relativeName;
        }

        private Stream getFile(string relativeName)
        {
            resolvePath(relativeName, out string path, out FileItem item);
            if (path != null)
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0x200000);
            if (item != null)
                return SourceFileSystem.CreateReader(item, null, null).OpenRead(item);
            return null;
        }

        private static int readExact(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, offset + total, count - total);
                if (n == 0)
                    break;
                total += n;
            }
            return total;
        }

        // ── Stream surface ──────────────────────────────────────────────────────
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => this.Size;
        public override long Position { get => _position; set => _position = value; }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current) _position += offset;
            else if (origin == SeekOrigin.End) _position = this.Size + offset;
            else _position = offset;
            return _position;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner?.Dispose();
            base.Dispose(disposing);
        }
    }
}
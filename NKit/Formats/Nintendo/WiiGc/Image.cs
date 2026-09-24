using Nanook.NKit.Container;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class Image : IImage
    {
        private readonly IAsIso _iso;
        private readonly BufferStream _stream;
        private readonly IImageContext _context;
        private readonly ImageHeader _header;
        private readonly ImageInfo _info;
        private long _position;
        private long _nkitNoUpdateAdjust; //used when inserting missing partitions etc
        private FileSystemInfo _fsInfo;
        private AreaInfo _area;
        private AreaInfo _nextArea;
        private int _areaNumber;
        private List<IImageArea> _areas;
        private string _imageType;
        private Stream _insertPtnStream;
        private bool _insertMode;
        private bool _nkitCisoMessageLogged;
        private bool _skipped;
        private bool _rvtMessageOutput;
        // Accumulated removed-block marks with ABSOLUTE image offsets. A lossless source
        // (IsoDec/CISO/WBFS) emits these via its SetRemovedBlock callback; a single section read
        // can trigger a large forward cache fill that emits marks for many sections at once, so
        // they are NOT tied to the read that produced them. Each section takes only the marks
        // within its own image range (takeRemovedBlocks) and leaves the rest for later sections.
        private List<MetaData> _removedBlocks = new List<MetaData>();
        private IFileSystemReader _fsReader; //parses the FST up front from the stream (peek+rewind)

        // Remove and return the accumulated removed-block marks whose absolute offset starts within
        // [imageStart, imageEnd). Marks outside the range remain for the section that contains them.
        private List<MetaData> takeRemovedBlocks(long imageStart, long imageEnd)
        {
            List<MetaData> taken = new List<MetaData>();
            for (int i = _removedBlocks.Count - 1; i >= 0; i--)
            {
                MetaData md = _removedBlocks[i];
                if (md.Offset >= imageStart && md.Offset < imageEnd)
                {
                    taken.Add(md);
                    _removedBlocks.RemoveAt(i);
                }
            }
            taken.Reverse(); // restore ascending order (we scanned back-to-front)
            return taken;
        }

        private byte[] _head; //wii disc header and first 0x8000 of first WipePartition

        public Image(IImageContext context, IAsIso input, BufferStream isoStream, bool isGameCube)
        {
            _insertMode = false;
            _nkitNoUpdateAdjust = 0;
            _insertPtnStream = null;
            _areaNumber = -1;
            _context = context;
            _iso = input;
            _stream = isoStream;
            _rvtMessageOutput = false;
            this.SystemType = isGameCube ? SystemType.GameCube : SystemType.Wii;
            this.Type = isGameCube ? ImageType.GameCube : ImageType.Wii;

            _imageType = "Retail";

            // An update-removed Wii NKit source reinserts its update partition itself during its
            // structure parse. The lookup lives in FixData, which the Wii domain layer resolves;
            // wire the targeted FixData into the container FIRST — BEFORE anything reads the source
            // (including `_iso.Size` below, which for a WiiFixAsIso-wrapped NKitAsIso triggers the
            // decorator's layout build → the inner NKitAsIso parse → update reinsertion). If it were
            // wired after, the parse would run with null recovery data and NULL-FILL the update
            // region instead of reinserting the recovery partition — changing the brute-force result
            // (a VerifyFailed). Reach the NKitAsIso through a Fix decorator (WiiFixAsIso may wrap it).
            Container.NKitAsIso nkitUpd = _iso as Container.NKitAsIso
                ?? (_iso as Container.WiiFixAsIso)?.Inner as Container.NKitAsIso;
            if (!isGameCube && nkitUpd != null && nkitUpd.NKitUpdateRemoved)
            {
                FixData recoveryFixData = null;
                try
                {
                    _context.Settings.LoadFixData(new FixData());
                    recoveryFixData = _context.Settings.FixData<FixData>();
                    recoveryFixData?.Setup(""); //recovery lookup needs FixPath only (id-independent)
                }
                catch { /* best-effort; container null-fills if unresolved */ }
                nkitUpd.SetRecoveryFixData(recoveryFixData, msg => _context.Log?.Info(() => msg));
            }

            // Now safe to read the source size (may trigger a WiiFixAsIso layout build / inner
            // NKitAsIso parse — recovery FixData is wired above so the update reinsertion works).
            this.Size = SizeOrig = _iso.SizeEstimated ? lenCalc(_iso.Size) : _iso.Size;

            byte[] header;
            if (this.Type != ImageType.Wii)
            {
                _head = new byte[0];
                header = new byte[WiiConsts.BootBinSize];
                _stream.Read(header, 0, -header.Length); //read and rewind
            }
            else
            {
                // Construct/Setup only need the disc header (0x50000) to gather format + partition
                // table. Read EXACTLY the disc header — not the extra 0x8000 partition-header
                // region — so that for a decoded NKitAsIso source this read does NOT reach into the
                // reinserted update partition (at 0x50000+) and force the container's full
                // structure parse (whole-source decompress + premature "Inserted update partition"
                // log) before processing has even started. The extra 0x8000 is only needed by the
                // legacy raw-NKit update-reinsert path (processNKitUpdatePartitionInfo, gated on
                // _info.IsNkit) and is read there on demand via the BufferStream (cached, safe).
                _head = new byte[WiiConsts.WiiDiscHdrSize];
                _stream.Read(_head, 0, -_head.Length); //read and rewind
                header = _head;
            }

            _info = new ImageInfo();
            _header = new ImageHeader(header, this.Type == ImageType.Wii);

            _context.Header = _header;
            _context.ImageInfo = _info;

            _info.ImageSize = _info.ReadLength = _iso.SizeEstimated ? lenCalc(_iso.Size) : _iso.Size;

            _info.IsNkit = _context.Header.Data.ReadString(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrIdOffset, 4) == WiiConsts.NKitId;
            _info.Type = this.Type;
            _info.SystemType = this.SystemType;
            _info.ContainerType = _iso.Format;
            // The NKitAsIso decoder presents fully-decoded partition data in the standard Wii
            // block layout (0x8000 sectors, 0x400 hash gaps left zero) — plaintext, not encrypted,
            // hashes not yet built. This is the RVZ-style contract: treat it as a hashed source
            // (so the 0x400-gap block layout and FS reads are correct) that is NOT source-encrypted
            // (so no source decryption runs on our plaintext). Hashes are rebuilt and encryption is
            // applied downstream in the SectionProcessor (OutputHashes/OutputEncryption). The old
            // in-pipeline NKit block-shuffle preprocessor is skipped for this source.
            // A Fix decorator (WiiFixAsIso/GcFixAsIso) may wrap the NKitAsIso decoder: the decoded
            // NKit already returns the correct disc (update reinserted or null-filled), and the
            // decorator sits on top for the layout/update-brute-force work. Reach through the
            // decorator so the decoded-source contract (plaintext, deferred encryption/hashes) is
            // detected either way — otherwise the wrapped bytes would be wrongly treated as
            // encrypted on-disc data.
            Container.NKitAsIso nkitInner = _iso as Container.NKitAsIso
                ?? (_iso as Container.WiiFixAsIso)?.Inner as Container.NKitAsIso
                ?? (_iso as Container.GcFixAsIso)?.Inner as Container.NKitAsIso;
            bool isNkitDecoded = nkitInner != null;
            bool isIso = !_info.IsNkit && (_info.ContainerType == ContainerType.Iso || _info.ContainerType == ContainerType.Gcz);
            bool isWii = this.Type == ImageType.Wii;
            _info.OutputEncryption = isWii;
            _info.OutputHashes = isWii;
            _info.SourceHasHashes = isWii && (!_info.IsNkit || isNkitDecoded);
            _info.SourceHasEncryptedHashes = _info.SourceHasHashes && !isNkitDecoded && _info.ContainerType != ContainerType.Wia && _info.ContainerType != ContainerType.Rvz;
            _info.SourceHasEncryption = isWii && !isNkitDecoded && (isIso || _info.ContainerType == ContainerType.Wbfs || _info.ContainerType == ContainerType.Ciso);
            _info.IsNkitDecoded = isNkitDecoded;
            _info.NKitSourceContainer = isNkitDecoded ? nkitInner.NKitSourceContainer : _iso.Format;
            // Wire the preserved-hash-group query so the SectionProcessor can reproduce (not
            // regenerate) preserved Wii partition hashes for a decoded NKit source.
            if (isNkitDecoded && isWii)
                _info.IsPreservedHashGroup = nkitInner.IsPreservedHashGroup;
            _info.Multiplier = isWii ? 4L : 1L;
            _info.SourceSupportsEncryption = isWii;
            _info.MediaType = this.Type == ImageType.Wii ? MediaType.WII : MediaType.GC;
            _info.Tracks = _context.SourceFile.IndexFile?.Items; //always null when not chd

            _info.IsFullImage = !_info.IsNkit && _info.ContainerType == ContainerType.Iso;

            if (!_info.IsNkit && _context.Header.Data.ReadUInt16B(WiiConsts.DataHdrEncHashOffset) == 0x0101)
                this.SetRvtH(false, false);

            _info.FixSize = isGameCube ? WiiConsts.FullSizeGameCube : (_iso.Size > WiiConsts.FullSizeWii5 ? WiiConsts.FullSizeWii9 : WiiConsts.FullSizeWii5); //size the image should be if truncated etc

            // [In] [WiiGc] Detail: the resolved source/output security-flag matrix + the branch that
            // set it. A wrong flag here silently double-encrypts, drops hashes, or treats plaintext
            // as encrypted — otherwise invisible. Once per image (ctor), guarded on Detail.
            ILogScope flagScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
            if (flagScope != null && flagScope.IsEnabled(LogLevel.Detail))
            {
                string branch = isNkitDecoded ? "nkitDecoded" : (_info.IsNkit ? "rawNkit" : (isIso ? "iso/gcz" : _info.ContainerType.ToString()));
                flagScope.Log(LogLevel.Detail,
                    $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.WiiGc)}flags [{branch}] container {_info.ContainerType}"
                    + $" srcEnc:{(_info.SourceHasEncryption ? "y" : "n")} srcHashes:{(_info.SourceHasHashes ? "y" : "n")}"
                    + $" srcEncHashes:{(_info.SourceHasEncryptedHashes ? "y" : "n")} outEnc:{(_info.OutputEncryption ? "y" : "n")}"
                    + $" outHashes:{(_info.OutputHashes ? "y" : "n")} mult:{_info.Multiplier}");
            }

            _iso.SetRemovedBlock(md => _removedBlocks.Add(md));
        }

        public void Setup()
        {
            FileInfo fi = _context.Settings.GetFixFile();
            _context.Settings.LoadFixData(new FixData());
            _info.FixData = _context.Settings.FixData<FixData>();
            _context.Settings.FixData<FixData>().Setup(_header.Id8);

            // GameCube Fix: the source was wrapped and corrected up front by GcFixAsIso (before
            // this Image was even constructed). Reflect that into ImageInfo so the downstream
            // pipeline treats the data as final (no re-run of gap-fill / FST-patch).
            if (this.Type == ImageType.GameCube && _iso is Container.GcFixAsIso gcFix && gcFix.IsCorrected)
                _info.IsGcFixApplied = true;

            // Wii Fix: the source layout was corrected up front by WiiFixAsIso (Game @ 0xF800000,
            // partition table rebuilt, sized to full disc). Skip the in-pipeline partition-table
            // reflow; the SectionProcessor still processes partition content.
            if (this.Type == ImageType.Wii && _iso is Container.WiiFixAsIso wiiFix && wiiFix.IsCorrected)
                _info.IsWiiFixLayoutApplied = true;

            // Wii Fix on a DECODED NKit source (single-step, no Expand): NKitAsIso already returns
            // the disc in its final, correct on-disc layout — partition table reflowed, offsets
            // repacked, the update partition reinserted from recovery (or null-filled if missing).
            // So the in-pipeline reflow (applyWiiPartitionTableFixes / applyDataPartitionFixes) must
            // NOT run: it was written to correct an EXPANDED encrypted ISO (the old Expand→Fix
            // path) and re-authoring offsets on the already-correct decoded image double-moves the
            // partitions, shifting per-area CRCs so the streamed CRC no longer matches the dat (the
            // Verify task, which never reflows, reads the SAME source back byte-correct). Treat it
            // like the GameCube IsGcFixApplied / WiiFixAsIso IsWiiFixLayoutApplied no-op: suppress
            // the reflow so an already-correct decoded NKit Fix reproduces the source CRC exactly
            // and the Fix step's fast dat match hits (no brute-force needed). The SectionProcessor
            // still unscrubs/hashes/encrypts partition content and reinserts a null-filled update.
            if (this.Type == ImageType.Wii && _info.IsNkitDecoded)
                _info.IsWiiFixLayoutApplied = true;

            _context.Scan = new Scan(this.SystemType, _context.SourceFile.Name);

            _info.StepImageInfo = new StepsImageInfo()
            {
                CustomChecksums = _iso.CustomChecksums(),
                Checksums = _iso.Checksums?.Clone() ?? new Checksums(),
                IsIndex = _context.SourceFile.IndexFile != null,
                // A decoded NKit source serves clean plaintext partition data (hashes/encryption
                // deferred to the parallel stage). That is fine for Scan/Convert/Expand, but a Wii
                // FIX must run the update-partition brute-force against a real, self-consistent,
                // on-disc ENCRYPTED image — so a decoded-nkit Wii Fix must first Expand to an ISO
                // (ReqPatch routes it through the Expand-Image-Patch step) rather than fixing the
                // decoded plaintext directly (which leaves the per-area CRCs inconsistent with the
                // streamed bytes and breaks the dat brute-force). GameCube fix is single-step and
                // unaffected. Non-Fix tasks keep the direct decoded path.
                //
                // Decoded-NKit Wii Fix no longer forces an Expand: WiiFixAsIso wraps the decoded
                // NKitAsIso (reading it through its own BufferStream) and the Fix step brute-forces
                // the update partition directly.
                //
                // ReqPatch (the Expand-Image-Patch route) is now scoped to the EXPAND task only:
                // expanding a legacy raw NKit (_info.IsNkit) to a full ISO IS the patch operation.
                // Every other task reads the corrected image in a single pass:
                //   • Wii Fix     — single-step Fix-WiiGc when no verify; TWO steps (Fix-WiiGc ->
                //                   Verify-Image) ONLY when a verify is required, because the Wii
                //                   update-partition brute-force + region/age calcs run on the
                //                   COMPLETED on-disc file, so verify must re-read the finished
                //                   image. This is driven by the Fix routing rows, NOT ReqPatch.
                //   • GameCube Fix — fixes AND verifies in one pass (GcFixAsIso corrects at read).
                //   • FixExtract/Extract/Dedupe/Convert/Scan/Verify/Wipe — single pass.
                // So ReqPatch stays n for everything except Expand; nothing falls through to
                // NotSupported.
                ReqPatch = _info.IsNkit && _context.TaskType == TaskType.Expand,
                Size = this.Size
            };

            if (_info.IsNkit)
            {
                this.Size = _context.Header.Data.ReadUInt32B(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrSrcLenOffset) * (this.Type == ImageType.Wii ? 4L : 1L);
                _info.NKitUpdatePartitionCrc = _context.Header.Data.ReadUInt32B(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrRemovedUpdateCrcOffset);
                _info.IsNkitUpdateRemoved = _info.NKitUpdatePartitionCrc != 0; //update not 0 means the update WipePartition has been removed
                _info.ImageSize = this.Size;
                _info.FixCrc = _context.Header.Data.ReadUInt32B(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrSrcCrcOffset);
                _info.FixJunkId = _context.Header.Data.Read(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrJunkIdOffset, 4);
                _info.StepImageInfo.Checksums.Crc = _context.Header.Data.ReadUInt32B(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrSrcCrcOffset);
                if (_info.IsNkitUpdateRemoved && _info.FixData != null)
                    _info.NKitUpdatePartition = ((Nintendo.WiiGc.FixData)_info.FixData).WiiUpdatePartitions.FirstOrDefault(a => a.Filename != null && a.Crc == _info.NKitUpdatePartitionCrc);
            }
            else if (!string.IsNullOrWhiteSpace(((Nintendo.WiiGc.FixData)_info.FixData)?.ForceJunkId))
                _info.FixJunkId = Encoding.ASCII.GetBytes(((Nintendo.WiiGc.FixData)_info.FixData).ForceJunkId);

            _info.StepImageInfo.Checksums.Merge(_iso.Checksums);
            _info.StepImageInfo.Checksums.Size = this.Size;

            processNKitUpdatePartitionInfo();
            if (_areas == null)
                buildPartitionOffsets();

            _info.SourceAreas = _areas.ToArray(); //read only copy for Output Task to use safely
            _info.IsFolderIndex = false;

            // ── Detail summaries (once per image) ────────────────────────────────────────────
            // Give the SectionProcessor a log scope ([Sect]) so it can emit a per-FileSystem-area
            // summary (the processor only holds IImageInfo, so route the Log through ImageInfo).
            // Reading is the [In] stage — the SectionProcessor summary and this Image summary both
            // log under [In] and identify their class in the message text (category None → the tag
            // is just "[In]").
            _info.SectionLog = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);

            ILogScope inScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
            if (inScope != null && inScope.IsEnabled(LogLevel.Detail))
                inScope.Log(LogLevel.Detail,
                    $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.WiiGc)}{this.Type} id {_header.Id6} '{_header.Title}' size 0x{this.Size:X}"
                    + $" nkit:{(_info.IsNkit ? "y" : "n")} decoded:{(_info.IsNkitDecoded ? "y" : "n")}"
                    + $" enc:{(_info.SourceHasEncryption ? "y" : "n")} areas:{_areas.Count}");

            // (Container/format internals are logged by NKitInput via IAsIso.FormatSummary under the
            // [In] scope — so no separate line here.)

            _nextArea = AreaInfo.NextArea(-1, -1, -1, this.SizeOrig, 0, this.Type == ImageType.Wii ? AreaType.ImageHeader : AreaType.FileSystem, null, ++_areaNumber);
            _nextArea = setAreaBlockInfo(_nextArea, null, 0); //little hack so we fit in with the loop in Read() but grab the block info
            _area = _nextArea;

            _position = 0;
        }
        private static int clearBuf(byte[] buf, ref int pos, int length)
        {
            Array.Clear(buf, pos, length);
            pos += length;
            return length;
        }

        private int readBuf(byte[] b, ref int pos, int len, Stream stream = null)
        {
            // Read the FULL requested length in one logical fill, looping over short reads. A
            // forward-only source (e.g. an .iso.dec inside a zip served via BufferStream) can
            // return fewer bytes than requested at an archive/cache boundary; a single Read would
            // leave the section partially filled. Draining to 'len' makes forward-only reads fill
            // the section exactly like a seekable single-read source. A zero read means genuine
            // EOF; the total (0 = nothing read) is still returned so callers that test "== 0"
            // (skipped/absent region) behave as before.
            Stream s = stream ?? _stream;
            int total = 0;
            while (total < len)
            {
                int r = s.Read(b, pos + total, len - total);
                if (r <= 0)
                    break; // EOF / no more data
                total += r;
            }
            pos += total;
            return total;
        }

        public AreaType Read(IBuffer buffer, out IFileSystemInfo fsInfo)
        {
            // Emit any buffered fix log lines now (first processing read) so they land in the
            // progress area, after the params banner — the corrected layout was built earlier
            // during construction when the header was read.
            if (_iso is Container.GcFixAsIso gcFixLog)
                gcFixLog.FlushLog();
            else if (_iso is Container.WiiFixAsIso wiiFixLog)
                wiiFixLog.FlushLog();

            if (_context.SkipType == SkipType.End)
            {
                buffer.ReInitialise(_area, true);
                buffer.Update(_position, _position - _area.ImageOffset, 0, 0, _area.IsEncrypted, _skipped);
                fsInfo = _fsInfo;
                return _area.Type;
            }

            byte[] buf = buffer.Decrypted;
            int pos = 0;
            // NOTE: _removedBlocks is NOT cleared per read. Removed-block marks arrive with
            // ABSOLUTE image offsets and a single section read can trigger a large forward cache
            // fill (BufferStreamManager) that emits marks for MANY future sections at once. They
            // are accumulated here and each section consumes only the marks within its own image
            // range (takeRemovedBlocks); marks for later sections persist until their section reads.

            //always use decrypted here, if the data is encrypted it will be rectified later after this method call
            int length = (int)Math.Min(this.SectionSize, _nextArea.ImageOffset - _position); //0 on first call - is updated

            try
            {
                if (_position == _nextArea.ImageOffset)
                {
                    _area = setAreaBlockInfo(_nextArea, _fsInfo, _area.FsOffset);
                    long nextArea = -1; //break the image up in to it's main areas
                    AreaType nextAreaType = AreaType.None;

                    if (_area.Type == AreaType.ImageHeader) //imageheader
                    {
                        length = _header.Data.Length;
                        readBuf(buf, ref pos, _header.Data.Length); //read the header
                        nextArea = length;
                        if (_info.IsNkitUpdateRemoved) //then skip the WipePartition header
                            _stream.Seek(WiiConsts.WiiSectorSize, SeekOrigin.Current);
                        if (_info.Mode == ReadMode.Fix)
                        {
                            // A decoded NKit source (IsWiiFixLayoutApplied) is already in its final,
                            // correct on-disc layout AND at its correct decoded size — NKitAsIso
                            // authored both. Skip the WHOLE Fix-mode header rework: the reflow
                            // (applyWiiPartitionTableFixes) AND the size normalisation
                            // (SizeOrig = FixSize). Overriding SizeOrig to the canonical FixSize
                            // would move the trailing area boundary and change the streamed CRC vs
                            // the Verify task (which never runs this branch and reads the same source
                            // back byte-correct). Leaving SizeOrig at its constructor value keeps the
                            // Setup() area list valid, so Fix streams byte-identical to Verify. When
                            // the decoded NKit's update was null-filled (recovery missing) the layout
                            // is still correct — only the update bytes are zero — so the streamed CRC
                            // won't match the dat and FixWiiGcStep still brute-forces/reinserts it.
                            if (!_info.IsWiiFixLayoutApplied)
                            {
                                _info.ImageSize = _info.FixSize;
                                this.SizeOrig = _info.FixSize;
                                applyWiiPartitionTableFixes(buf);
                                buildPartitionOffsets();
                            }
                        }
                    }
                    else if (_area.Type == AreaType.PartitionHeader) //WipePartition header
                    {
                        if (_info.Mode == ReadMode.Fix)
                            _stream.Seek(_area.ImageOffset, SeekOrigin.Begin); // fill may have been inserted
                        if (_insertPtnStream != null)
                        {
                            try { _insertPtnStream.Dispose(); } catch { } // dispose owned archive reader (if any)
                            _insertPtnStream = null;
                        }

                        PartitionInfo ptn = _header.GetPartition(_position);
                        _insertMode = ptn.IsPlaceholder;
                        if (_insertMode && ptn.FixPartition != null)
                            // Open the recovery partition data forward-only (a plain file, or the
                            // named entry inside a streamable archive per the Wii fix-file convention).
                            _insertPtnStream = ptn.FixPartition.OpenDataStream(_context.Log);

                        Stream hdrStream = _insertPtnStream ?? _stream; //use fill WipePartition if present
                        readBuf(buf, ref pos, 0x400, hdrStream);

                        length = (int)(buf.ReadUInt32B(WiiConsts.WiiPrtHdrSizeOffset) * _info.Multiplier);
                        nextArea = _position + length;
                        nextAreaType = AreaType.FileSystem;

                        bool skip = readBuf(buf, ref pos, length - pos, hdrStream) == 0; //read the rest of the header

                        if (!skip)
                        {
                            foreach (MetaData md in takeRemovedBlocks(_position, _position + pos))
                                Array.Clear(buf, (int)(md.Offset - _position), (int)md.Size); //rvz with 32k blocks can have removed blocks, fix so that the H3 / TMD is correct (absolute offsets; take only this section's, rebased)

                            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, WiiConsts.PublicKeyModulusRvtR, WiiConsts.PublicKeyExponent, buf.Read(0, pos), 0);

                            _fsInfo = new FileSystemInfo(_header, _position, ph.Data, _info, _info.FixJunkId != null && _info.FixJunkId.ReadUInt32B(0) != 0 ? _info.FixJunkId : null, this.Size, this.SizeOrig);
                            _fsInfo.IsFixFile = _insertPtnStream != null;
                            ptn.SignedStatus = ph.CertValidator.Validate(!_fsInfo.IsRvt, _fsInfo.IsRvtH);

                            if (_fsInfo.IsRvt)
                            {
                                _imageType = _fsInfo.IsRvtH ? "RVT-H" : "RVT-R";
                                if (_info.Mode == ReadMode.Fix)
                                    throw new HandledException("RVT (non-retail) images are not currently supported for Fix");
                                //if (!_fsInfo.IsRvtH && this.Size == WiiConsts.FullSizeWii5) //crashes when reading last bytes from the image. 
                                //{
                                //    this.Size = WiiConsts.FullSizeWiiRvtr;
                                //    _areas[_areas.Count - 1] = new ImageArea(this.Size, AreaType.None);
                                //}
                            }
                            ptn.Id = _fsInfo.ContentSha1.ToHexString();
                            if (_info.Mode == ReadMode.Fix && !_info.IsWiiFixLayoutApplied && _fsInfo.Type == PartitionType.Game)
                            {
                                if (applyDataPartitionFixes(_area.ImageOffset, _fsInfo.ImageOffsetData - _fsInfo.ImageOffset + _fsInfo.Size))
                                    buildPartitionOffsets();
                            }
                        }
                    }
                    else if (_area.Type == AreaType.FileSystem) //WipePartition data
                    {
                        if (this.Type == ImageType.GameCube)
                            _fsInfo = new FileSystemInfo(null, 0, _context.Header.Data, _info, _info.FixJunkId != null && _info.FixJunkId.ReadUInt32B(0) != 0 ? _info.FixJunkId : null, this.Size, this.SizeOrig);

                        //detect bad RVZ where the game WipePartition data is stored and not decrypted.
                        //Resolve the real RvzAsIso THROUGH any Fix decorator (WiiFixAsIso wraps the
                        //source for a Fix, so `_iso as RvzAsIso` would be null and NRE). Skip when a
                        //Fix decorator has already corrected the layout — the served bytes are the
                        //corrected disc, not raw RVZ partition data.
                        RvzAsIso rvzSrc = _iso as RvzAsIso
                                        ?? (_iso as Container.WiiFixAsIso)?.Inner as RvzAsIso;
                        if (this.Type == ImageType.Wii && _context.ImageInfo.ContainerType == ContainerType.Rvz && _area.Type == AreaType.FileSystem && !_fsInfo.IsRvtH
                            && !_info.IsWiiFixLayoutApplied && rvzSrc != null && !rvzSrc.IsPartition(_position))
                        {
                            _area.SetSecurity(true, true, true);
                            _fsInfo.IsWiiRvzEncryptedPartition = true;
                            if (!_rvtMessageOutput)
                                _context.Log.Info(() => $"RVZ with Encrypted Partition detected - recompress to shrink further");
                            _rvtMessageOutput = true;
                        }

                        //NKitAsIso reinserted the (already encrypted+hashed) update partition from a
                        //recovery file — pass it through unchanged (do not re-encrypt/re-hash). Reuse
                        //the RVZ encrypted-partition marker so it is treated as source-encrypted.
                        if (this.Type == ImageType.Wii && _area.Type == AreaType.FileSystem && !_fsInfo.IsRvtH
                            && _iso is Container.NKitAsIso nkitEnc && nkitEnc.IsInsertedEncryptedPartition(_area.ImageOffset))
                        {
                            _area.SetSecurity(true, true, true);
                            _fsInfo.IsWiiRvzEncryptedPartition = true;
                        }

                        //An inserted (placeholder) partition fed from a recovery file (_insertPtnStream)
                        //is verbatim, already-encrypted+hashed on-disc data — e.g. a Fix that injects a
                        //missing update partition from a recovery fix file. There is no parseable FST
                        //(the up-front read is skipped below while the insert stream is live), so it must
                        //NOT be decrypted / re-hashed / junk-filled. Mark it the same as the NKit-reinsert
                        //and RVZ-encrypted cases so SectionProcessor passes it through unchanged.
                        if (this.Type == ImageType.Wii && _area.Type == AreaType.FileSystem && !_fsInfo.IsRvtH
                            && _insertPtnStream != null)
                        {
                            _area.SetSecurity(true, true, true);
                            _fsInfo.IsWiiRvzEncryptedPartition = true;
                        }

                        // Parse the whole FST up front (before this area's nextArea/SizeSource is
                        // used) via a cache-safe PEEK of the data-area system region. MUST run AFTER
                        // the encrypted-partition detection above, so _area reflects the correct
                        // encryption state: a reinserted (NKit) or RVZ-encrypted Game partition is
                        // served encrypted, and the up-front read must feed it as encrypted so
                        // SetFsInfo decrypts it like every other Wii area (otherwise it reads raw
                        // encrypted bytes where boot.bin/FST should be).
                        //
                        // Runs for EVERY mode: the peek never moves the read cursor and (now that the
                        // RVZ group decode is idempotent) re-reading a peeked group returns identical
                        // bytes, so the partition data area reaches the preprocessor with the whole
                        // file system already available — no block holding, no inline FST caching.
                        if (_fsInfo != null && !_fsInfo.InvalidPartitionData && _insertPtnStream == null)
                            readFileSystemUpFront(_area, _position);

                        nextArea = _fsInfo.SizeSource == 0 ? -1 : (_position + _fsInfo.SizeSource); //anything above current _position, but before the next WipePartition
                        if (nextArea == -1)
                            nextAreaType = AreaType.None;
                        else
                            nextAreaType = _areas.Any(a => a.ImageOffset == nextArea) ? AreaType.PartitionHeader : AreaType.Other; //WipePartition padding (Other) is not always present - SSBB
                    }

                    _nextArea = AreaInfo.NextArea(_area.ImageOffset, -1, -1, this.SizeOrig, nextArea, nextAreaType, _areas, ++_areaNumber);

                    // [In] [WiiGc] Detail: reader's own per-area progress — the last line before a
                    // crash shows which area/offset/size the reader was on. Once per area transition
                    // (this block only runs when _position hits an area boundary), so no per-section cost.
                    ILogScope areaScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                    if (areaScope != null && areaScope.IsEnabled(LogLevel.Detail))
                        areaScope.Log(LogLevel.Detail,
                            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.WiiGc)}area {_area.AreaNo} [{_area.Type}] offset 0x{_area.ImageOffset:X} size 0x{_nextArea.ImageOffset - _area.ImageOffset:X}{(_area.IsEncrypted ? " enc" : "")}");

                    length = (int)Math.Min(this.SectionSize, _nextArea.ImageOffset - _position);
                }

                if (_insertMode)
                {
                    if (_insertPtnStream == null || _insertPtnStream.Position == _insertPtnStream.Length)
                        clearBuf(buf, ref pos, Math.Max(length - pos, 0));
                    else
                    {
                        _insertPtnStream.Read(buf, pos, length - pos);
                        pos = length;
                    }
                    if (_insertPtnStream != null && _insertPtnStream.Position == _insertPtnStream.Length)
                    {
                        _insertPtnStream.Close(); //close but don't null as _insertAdjust uses it
                        _insertPtnStream = null;
                    }
                }
                else if (_info.Mode == ReadMode.Fix && _area.Type == AreaType.Other && !_info.IsWiiFixLayoutApplied)
                    // Fix normally zero-fills the inter/trailing "Other" padding so the pipeline
                    // regenerates it deterministically. But a decoded NKit source (or a WiiFixAsIso
                    // decorator) already presents the disc in final layout WITH its correct trailing
                    // junk materialised in the stream — zeroing it here would replace real junk with
                    // nulls and change the CRC (the Verify/Convert path, which reads this region,
                    // reproduces the correct junk). So when the layout is already applied, READ the
                    // Other region verbatim like every non-Fix path.
                    clearBuf(buf, ref pos, Math.Max(length - pos, 0));
                else
                    readBuf(buf, ref pos, Math.Max(length - pos, 0));

                //always pads the read request this lets the engine quit when the image size is read (for truncated images / wbfs etc)
                if (pos == 0)
                    clearBuf(buf, ref pos, length);

                if (length != 0 && pos == 0 && _position >= this.Size)
                    throw new HandledException(string.Format("Image.Read - No data read at Position {0} ({1}) - Requested {2}", _position.ToString("X"), _iso.RealPosition.ToString("X"), length.ToString()));

                buffer.ReInitialise(_area, true);

                // Removed-block marks arrive as ABSOLUTE image offsets (invariant of whichever
                // buffer the source decoder filled — e.g. a BufferStream cache block, which can
                // cover many sections). Take only the marks that fall within THIS section's image
                // range, rebased to the section buffer, before the MetaData ctor derives FsOffset.
                foreach (MetaData md in takeRemovedBlocks(_position, _position + pos))
                    buffer.MissingData.Add(new MetaData(md.Offset - _position, md.Size, md.Type, md.BlockByte, null, _info.IsNkit, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize, true));

                buffer.Update(_position, _position - _area.ImageOffset, pos, 0, _area.IsEncrypted, _skipped);

                _position += buffer.Size;

                fsInfo = _fsInfo;

                if (!_nkitCisoMessageLogged && _iso is CisoAsIso ciso && ciso.NkitHeaderNotUsed)
                {
                    _nkitCisoMessageLogged = true;
                    _context.Log.Info(() => $"NKit Lossless CISO header found and NOT USED at the end of a non-seekable image. (Archives are not seekable)");
                }

                skip(); //after providing current data check if we can skip some of the current filesystem

                // Bound the shared cache: release cached blocks below the returned-data mark on
                // both layers — Layer-B (this _stream over the decoder) and, for a container that
                // reads a raw archive entry (DefaultAsIso, possibly behind a Fix decorator),
                // Layer-A (the raw source) via IReleasable — otherwise the raw-source cache grows
                // to hold the whole image when the source is a forward-only archive entry.
                _stream.ReleaseTo(_position);
                (_iso as IReleasable)?.ReleaseTo(_position);

                return _area.Type;
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Image.Read: {ex.Message}");
            }
        }

        private void processNKitUpdatePartitionInfo()
        {
            if (this.Type == ImageType.Wii)
            {
                if (_info.IsNkit) //nkit insert missing update WipePartition
                {
                    if (_info.IsNkitUpdateRemoved /*&& Header.HasUpdatePartition*/)
                    {
                        // The legacy raw-NKit path needs the 0x8000 partition-header region that
                        // follows the disc header. It is NOT read up front (the constructor reads
                        // only the 0x50000 disc header so a decoded NKitAsIso source is not forced
                        // to parse at Open). Read it here, on demand, via the BufferStream — a
                        // cached, cursor-safe peek that does not advance the sequential read.
                        byte[] ptnHdr = new byte[WiiConsts.WiiDiscHdrPtnSize];
                        long savedPos = _stream.Position;
                        _stream.SafeSeek(WiiConsts.WiiDiscHdrSize, System.IO.SeekOrigin.Begin);
                        _stream.Read(ptnHdr, 0, ptnHdr.Length);
                        _stream.SafeSeek(savedPos, System.IO.SeekOrigin.Begin);

                        byte[] tmpHdr = (byte[])_header.Data.Clone();
                        tmpHdr.Write(WiiConsts.WiiDiscHdrPtnOffset, ptnHdr, 0, WiiConsts.WiiDiscHdrPtnSize);
                        ImageHeader tmpHeader = new ImageHeader(tmpHdr, true);
                        long updateOffset = tmpHeader?.Partitions?.FirstOrDefault(a => a.Type == PartitionType.Update)?.ImageOffset ?? WiiConsts.WiiDefaultUpdatePtnOffset;
                        long dataOffset = tmpHeader?.Partitions?.FirstOrDefault(a => a.Type != PartitionType.Update)?.ImageOffset ?? WiiConsts.WiiDefaultDataPtnOffset;
                        _insertMode = true;
                        _header.AddPartitionPlaceHolder(new PartitionInfo(PartitionType.Update, updateOffset, 0, 0));
                        _header.Partitions[0].IsPlaceholder = true;
                        _header.Partitions[0].FixPartition = _info.NKitUpdatePartition;
                        _nkitNoUpdateAdjust = updateOffset - dataOffset + WiiConsts.WiiSectorSize; //adjust all src file reads by this
                        this.SizeOrig = this.SizeOrig + -_nkitNoUpdateAdjust;
                        buildPartitionOffsets();
                        if (_info.NKitUpdatePartition == null) //update missing
                        {
                            _header.RemoveUpdatePartition(_header.Partitions[0].ImageOffset);
                            _head.Write(0, _header.Data); //write the good header back with the update ptn removed
                            _header.Update(_header.Data, true);
                        }
                    }
                    else
                    {
                        this.SizeOrig = this.SizeOrig - WiiConsts.WiiSectorSize + (WiiConsts.WiiDefaultDataPtnOffset - WiiConsts.WiiDefaultUpdatePtnOffset);
                        buildPartitionOffsets();
                    }
                }
            }
        }

        // Reposition to a skip target through the stream reads actually flow through (_stream), not
        // _iso directly — seeking _iso alone desyncs it from the BufferStream cursor and delivers
        // wrong bytes at the skipped-to boundary. Skips are always forward.
        private void skipStreamTo(long imageOffset) => _stream.SafeSeek(imageOffset, SeekOrigin.Begin);

        //Allow skipping of rest of image, or filesystem / other areas
        private void skip()
        {
            _skipped = false;
            long skipTo = _context.SkipToImageOffsetGet(_position);
            if (skipTo <= _position)
                return;

            long io;
            if (skipTo > _position && _context.SkipType != SkipType.End)
            {
                if ((_area.Type == AreaType.FileSystem && _fsInfo.AllFoldersParsed && (_fsInfo.InvalidFileSystem || _fsInfo.FileSystem != null)) || _area.Type == AreaType.Other)
                {
                    bool toEnd = !getPartitionInfo(skipTo, out io); //end
                    if (toEnd)
                    {
                        _position = this.Size;
                        skipStreamTo(this.Size);
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
            else
                startImageOffset = ((startImageOffset - _area.ImageOffset) / this.SectionSize * this.SectionSize) + _area.ImageOffset; //section start
            return true;
        }

        private void buildPartitionOffsets()
        {
            _areas = new List<IImageArea>();

            if (this.Type != ImageType.Wii)
            {
            }
            else if (_info.IsNkitUpdateRemoved && _info.NKitUpdatePartition != null) //nkit with update ptn
            {
                _areas.AddRange(_header.Partitions.OrderBy(a => a.ImageOffset).Select(a => new ImageArea(a.ImageOffset, AreaType.PartitionHeader)));
            }
            else if (_info.Mode == ReadMode.Fix && _header.Data.Length == _header.Partitions[0].ImageOffset && _header.Partitions[0].IsPlaceholder)
            {
                _areas.Add(new ImageArea((long)_header.Data.Length, AreaType.Other));
                _areas.AddRange(_header.Partitions.Skip(1).OrderBy(a => a.ImageOffset).Select(a => new ImageArea(a.ImageOffset, AreaType.PartitionHeader)));
            }
            else if (_info.IsNkitUpdateRemoved && _info.NKitUpdatePartition == null) //missing update
            {
                _areas.Add(new ImageArea((long)_header.Data.Length, AreaType.Other));
                _areas.AddRange(_header.Partitions.Skip(1).OrderBy(a => a.ImageOffset).Select(a => new ImageArea(a.ImageOffset, AreaType.PartitionHeader)));
            }
            else if (_header.Data.Length == _header.Partitions[0].ImageOffset)
            {
                _areas.AddRange(_header.Partitions.OrderBy(a => a.ImageOffset).Select(a => new ImageArea(a.ImageOffset, AreaType.PartitionHeader)));
            }
            else //missing update WipePartition not in fix mode
            {
                _areas.Add(new ImageArea((long)_header.Data.Length, AreaType.Other));
                _areas.AddRange(_header.Partitions.OrderBy(a => a.ImageOffset).Select(a => new ImageArea(a.ImageOffset, AreaType.PartitionHeader)));
            }

            _areas.Add(new ImageArea(this.SizeOrig, AreaType.None));
        }

        public void SetRvtH(bool withHashes, bool withEncryption)
        {
            bool hasHashes = _info.SourceHasHashes; //can be changed
            if (this.Type == ImageType.Wii && _info.SourceHasHashes && !withHashes)
            {
                _info.SourceHasHashes = false;
                _info.OutputHashes = false;
                _info.SourceHasEncryptedHashes = false;
            }
            if (this.Type == ImageType.Wii && hasHashes && !withEncryption)
            {
                _info.SourceHasEncryption = false;
                _info.OutputEncryption = false;
                _info.SourceHasEncryptedHashes = false;
            }
        }

        private bool applyDataPartitionFixes(long dataPtnOffset, long dataPtnSize)
        {
            try
            {
                return WiiPartitionLayout.ApplyDataPartitionFixes(_header, dataPtnOffset, dataPtnSize, _context.Log);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "Fix: applyDataPartitionFixes");
            }
        }

        private void applyWiiPartitionTableFixes(byte[] buf)
        {
            try
            {
                long imgSize = _info.IsNkit ? this.Size : _iso.Size;
                WiiPartitionLayout.ApplyPartitionTableFixes(_header, (FixData)_info.FixData, imgSize, buf, _context.Log);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "Fix: applyPartitionTableFixes");
            }
        }


        private long lenCalc(long l)
        {
            if (Type == ImageType.GameCube)
                return WiiConsts.FullSizeGameCube; //GC ISO
            else if (l == WiiConsts.FullSizeWiiRvtr) //rvtr
                return l;
            else if (l <= WiiConsts.FullSizeWiiOversized) //oversized dvd5
                return WiiConsts.FullSizeWii5; //real size dvd5
            else
                return WiiConsts.FullSizeWii9; //dvd9
        }

        public ImageType Type { get; }

        public long Size { get; internal set; }
        public long SizeOrig { get; private set; }

        public long CurrentAreaEndImageOffset => _nextArea?.ImageOffset ?? this.Size;
        public int SectionSize => (int)WiiConsts.WiiGroupSize;

        // Parse the whole FST up front by PEEKING the (partition) data area's system region from the
        // stream, before any FileSystem-area section is emitted. Wii/GC keep the FST at the start of
        // the data area. The BufferStream peek idiom (negative count) reads each block WITHOUT moving
        // the read cursor — it is forward-only clean (never rewinds a decoder). After this returns
        // _fsInfo.FileSystem is populated, so the downstream preprocessor is a pure pass-through and
        // every emitted FileSystem section already knows its file coverage. Re-reading a peeked group
        // on the subsequent sequential pass is safe because the RVZ group decode is now idempotent.
        private void readFileSystemUpFront(AreaInfo fsAreaInfo, long partitionDataImageOffset)
        {
            _fsReader = new WiiGcFileSystemReader(_fsInfo, partitionDataImageOffset);

            long dataAreaStart = _stream.Position; //data-area start (stream sits here after the header read)

            // Pin the cache floor at the data-area start. On a forward-only / linear-verify source
            // (RVZ verify uses a non-seekable cached manager) the peek must pull the source forward
            // to reach the FST region; retaining keeps those blocks cached so the subsequent
            // sequential reads of this region are served from cache (not re-pulled), and the source
            // never has to move backwards.
            _stream.Retain(dataAreaStart);

            // Feed successive data-area blocks to the reader until it has accumulated the full FST.
            // SetFsInfo decrypts each block itself (via _fsInfo's key) when the area is encrypted.
            long areaOffset = 0;
            int guard = 0;
            while (!_fsReader.Complete)
            {
                Buffer fsBuf = new Buffer(fsAreaInfo.SectionSize, this.Type == ImageType.Wii);
                fsBuf.ReInitialise(fsAreaInfo, true);

                // PEEK the block at its absolute offset: Seek only sets the cursor (cache-served on a
                // forward-only source), then the negative-count Read reads WITHOUT advancing it, so
                // the cursor is left exactly where the caller expects for the normal sequential read.
                _stream.Position = dataAreaStart + areaOffset;
                int r = _stream.Read(fsBuf.Decrypted, 0, -fsAreaInfo.SectionSize);
                if (r == 0)
                    break; //truncated source; let downstream handle it

                // Update sizes the buffer to r and (for encrypted areas) swaps Decrypted<->encrypted
                // so the raw bytes we just peeked sit in the encrypted slot for SetFsInfo to decrypt.
                fsBuf.Update(partitionDataImageOffset + areaOffset, areaOffset, r, 0, fsAreaInfo.IsEncrypted, false);

                _fsReader.ProcessBlock(fsBuf);
                areaOffset += r;

                if (++guard > 0x4000) //safety: never spin forever on a malformed FST
                    break;
            }

            _stream.Position = dataAreaStart; //restore the cursor for the normal sequential reads (peek never moved it, but the per-block Seek did)
            _stream.Retain(long.MaxValue);     //clear the floor; normal ReleaseTo-driven eviction resumes
        }

        private AreaInfo setAreaBlockInfo(AreaInfo ai, IFileSystemInfo fsInfo, long fsOffset)
        {
            if (ai.Type == AreaType.FileSystem && this.Type == ImageType.Wii && (_info.SourceHasHashes || _insertPtnStream != null))
                ai.SetBlock(WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize, (int)WiiConsts.WiiGroupSize);
            else
                ai.SetBlock(WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize, (int)WiiConsts.WiiGroupSize);

            ai.SetSecurity(ai.Type == AreaType.FileSystem && Type == ImageType.Wii && (_info.SourceHasEncryption || _insertPtnStream != null), ai.Type == AreaType.FileSystem && Type == ImageType.Wii && !_fsInfo.IsRvtH, ai.Type == AreaType.FileSystem && _info.SourceHasHashes);

            SetPropertyItems(ai, Type == ImageType.Wii);
            return ai;
        }

        internal static void SetPropertyItems(AreaInfo ai, bool isWii)
        {
            switch (ai.Type)
            {
                case AreaType.ImageHeader:
                    ai.SetProperties("ID", "DiscNo", "Revision", "Region", "Title", "Partitions");
                    break;
                case AreaType.PartitionHeader:
                    ai.SetProperties("Partition", "PartitionType", "ContentSha", "CommonKeyCrc", "TitleKeyCrc", "Signed");
                    break;
                case AreaType.FileSystem:
                    if (isWii)
                        ai.SetProperties("Partition", "ID", "DiscNo", "Revision", "Title", "Encrypted", "BlockSize", "HashSize", "HasFileSystem", "SystemDataCrc", "JunkID", "JunkLeadingNulls", "JunkEndNullsOffset");
                    else
                        ai.SetProperties("ID", "DiscNo", "Revision", "Region", "Title", "SystemDataCrc", "JunkID", "JunkLeadingNulls");
                    break;
                case AreaType.Other:
                    ai.SetProperties("UpdatePartitionRemoved", "Partition", "JunkID");
                    break;
                default:
                    break;
            }
        }

        public SystemType SystemType { get; internal set; }

        public IBufferPreProcessor GetPreProcessor() => new BufferPreProcess(_context);

        public ISectionProcessor CreateSectionProcessor() => new SectionProcessor(_header.Id, _header.DiscNo, _info);

        public IBuffer CreateBuffer() => new Buffer(SectionSize, this.Type == ImageType.Wii);

        public void SetScanProperties()
        {
            int partition = -1;
            PartitionInfo ptn = null;
            FileSystemInfo fsInfo = null;
            bool isWii = this.Type == ImageType.Wii;
            Region region = isWii ? (Region)_header.Data.ReadUInt32B(WiiConsts.WiiDiscHdrRgnOffset) : (Region)_fsInfo.Bi2Bin.ReadUInt32B(0x18);

            Scan result = _context.Scan;

            foreach (ScanArea sra in result.Areas)
            {
                switch (sra.Type)
                {
                    case AreaType.ImageHeader:
                        sra.AreaInfo.Properties["ID"] = _header.Id6;
                        sra.AreaInfo.Properties["DiscNo"] = _header.DiscNo;
                        sra.AreaInfo.Properties["Revision"] = _header.Revision;
                        sra.AreaInfo.Properties["Region"] = region.ToString();
                        sra.AreaInfo.Properties["Title"] = _header.Title;
                        sra.AreaInfo.Properties["Partitions"] = _header.Partitions.Length;
                        break;
                    case AreaType.PartitionHeader:
                        fsInfo = (FileSystemInfo)sra.FsInfo;
                        ptn = _header.GetPartition(sra.ImageOffset);
                        sra.AreaInfo.Properties["Partition"] = ++partition;
                        sra.AreaInfo.Properties["PartitionType"] = Enum.IsDefined(typeof(PartitionType), ptn.Type) ? ptn.Type.ToString() : SourceFiles.CleanseFileName(Encoding.ASCII.GetString(((uint)ptn.Type).ToBytesBE()));
                        sra.AreaInfo.Properties["ContentSha"] = ptn.Id;
                        sra.AreaInfo.Properties["CommonKeyCrc"] = (uint)(fsInfo?.Key == null ? 0 : Crc.Compute(fsInfo.CommonKey));
                        sra.AreaInfo.Properties["TitleKeyCrc"] = (uint)(fsInfo?.Key == null ? 0 : Crc.Compute(fsInfo.Key));
                        if (ptn.SignedStatus != SignedStatus.None)
                            sra.AreaInfo.Properties["Signed"] = ptn.SignedStatus.ToString();
                        break;
                    case AreaType.FileSystem:
                        if (isWii)
                        {
                            sra.AreaInfo.Properties["Partition"] = partition;
                            sra.AreaInfo.Properties["ID"] = fsInfo.Id6;
                            sra.AreaInfo.Properties["DiscNo"] = fsInfo.DiscNo;
                            sra.AreaInfo.Properties["Revision"] = fsInfo.Revision;
                            sra.AreaInfo.Properties["Title"] = fsInfo.Title;
                            // A Wii disc is encrypted unless it is RVT-H (dev/debug). Report the DISC's
                            // encryption, not the source storage: IsEncrypted reflects whether the
                            // source bytes are stored encrypted (false for RVZ, which stores decrypted),
                            // but the scan describes the disc.
                            sra.AreaInfo.Properties["Encrypted"] = !fsInfo.IsRvtH;
                            sra.AreaInfo.Properties["BlockSize"] = (uint)sra.AreaInfo.BlockSize;
                            sra.AreaInfo.Properties["HashSize"] = (uint)sra.AreaInfo.BlockFsOffset;
                            // Require a PARSED filesystem: an inserted recovery channel/VC (served
                            // verbatim) has InvalidFileSystem==false yet FstData/JunkId==null because
                            // the up-front FST parse was skipped. Without the FstData check the junk
                            // property reads below dereference null (SystemDataCrc/JunkID).
                            bool hasFs = fsInfo != null && !fsInfo.InvalidFileSystem && fsInfo.FstData != null;
                            sra.AreaInfo.Properties["HasFileSystem"] = hasFs;
                            if (hasFs)
                            {
                                sra.AreaInfo.Properties["SystemDataCrc"] = Crc.Compute(fsInfo.FstData);
                                if (fsInfo.JunkId != null)
                                    sra.AreaInfo.Properties["JunkID"] = Encoding.ASCII.GetString(fsInfo.JunkId);
                                sra.AreaInfo.Properties["JunkLeadingNulls"] = (ulong)fsInfo.JunkLeadingNulls;
                                sra.AreaInfo.Properties["JunkEndNullsOffset"] = (ulong)fsInfo.JunkEndNullsOffset;
                            }
                        }
                        else
                        {
                            fsInfo = (FileSystemInfo)sra.FsInfo;
                            sra.AreaInfo.Properties["ID"] = _header.Id6;
                            sra.AreaInfo.Properties["DiscNo"] = _header.DiscNo;
                            sra.AreaInfo.Properties["Revision"] = _header.Revision;
                            sra.AreaInfo.Properties["Region"] = region;
                            sra.AreaInfo.Properties["Title"] = _header.Title;
                            sra.AreaInfo.Properties["SystemDataCrc"] = Crc.Compute(fsInfo.FstData);
                            sra.AreaInfo.Properties["JunkID"] = Encoding.ASCII.GetString(fsInfo.JunkId);
                            sra.AreaInfo.Properties["JunkLeadingNulls"] = (ulong)fsInfo.JunkLeadingNulls;
                        }
                        break;
                    case AreaType.Other:
                        if (sra.ImageOffset == WiiConsts.WiiDefaultUpdatePtnOffset)
                            sra.AreaInfo.Properties["UpdatePartitionRemoved"] = true;
                        else
                            sra.AreaInfo.Properties["Partition"] = partition;
                        if (fsInfo != null && fsInfo.Type != PartitionType.Update)
                            sra.AreaInfo.Properties["JunkID"] = fsInfo != null && fsInfo.Type == PartitionType.Game ? Encoding.ASCII.GetString(fsInfo.JunkId) : _header.Id;
                        break;
                    default:
                        break;
                }
            }

            result.Properties["System"] = this.SystemType.ToString();
            result.Properties["Media"] = MediaType.Disc.ToString();
            if (isWii)
                result.Properties["Type"] = _imageType;
            result.Properties["Size"] = (ulong)result.Size;
            result.Properties["CRC"] = result.Crc;
            if (isWii)
                result.Properties["DecryptedCRC"] = result.CrcDecrypted;
        }

        public ISectionProcessor PatchSection(ScanSection s)
        {
            _info.Patching = true;
            bool patchApplied = false;
            Buffer buf = null;
            SectionProcessor processor = null;
            FileSystemInfo fsInfo = (FileSystemInfo)s.ParentArea.FsInfo;
            if (s.PatchInfo.MarkForPatching || s.PatchInfo.MarkForCalculatedData)
            {
                processor = (SectionProcessor)CreateSectionProcessor();
                processor.FileSystemData = s.ParentArea.FsInfo;
                buf = new Buffer(this.Type != ImageType.GameCube, s.Data);
                buf.ReInitialise(s.ParentArea.AreaInfo, true);
                buf.Update(s.ImageOffset, s.AreaOffset, (int)s.Size, 0, false, false);
                buf.FileStartIndex = s.FileStartIndex;
                buf.FileEndIndex = s.FileEndIndex;
                buf.PatchInfo = s.PatchInfo.Clone();
                buf.PatchInfo.PrePatchXxHash = s.XxHash;
                processor.Buffer = buf;
                processor.Update();
            }

            if (s.PatchInfo.MarkForPatching && s.ParentArea.Type == AreaType.ImageHeader)
            {
                if (_info.IsNkit)
                {
                    _header.Data.Clear(WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize, 0x00);
                    _header.Data.WriteUInt16B(WiiConsts.DataHdrEncHashOffset, 0x0000);
                    s.Data.Clear(WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize, 0x00);
                    s.Data.WriteUInt16B(WiiConsts.DataHdrEncHashOffset, 0x0000);
                    if (_info.IsNkitUpdateRemoved && _header.Partitions[0].Type == PartitionType.Update && _info.NKitUpdatePartition == null) //update missing
                        _header.RemoveUpdatePartition(_header.Partitions[0].ImageOffset);
                }
                _header.UpdateOffsets(); //nsinfo updates the offsets //TODO: Refactor
                s.Data.Write(WiiConsts.WiiDiscHdrPtnOffset, _context.Header.Data, WiiConsts.WiiDiscHdrPtnOffset, WiiConsts.WiiDiscHdrRgnOffset - WiiConsts.WiiDiscHdrPtnOffset);
                patchApplied = true;
            }
            else if (s.PatchInfo.MarkForPatching && s.ParentArea.Type == AreaType.PartitionHeader)
            {
                s.Data = fsInfo.Buffer; //copy reference - not needed
                s.Data.CopyTo(processor.Decrypted, 0);
                patchApplied = true;
            }
            else if ((s.PatchInfo.MarkForPatching || s.PatchInfo.MarkForCalculatedData) && s.ParentArea.Type == AreaType.FileSystem)
            {
                if (s.PatchInfo.MarkForPatching && s.FsOffset < fsInfo.FstData.Length)
                {
                    // TODO: Set patched bootbin and fstbin hash properly - might work if commented out due to processor.Process being called
                    XXHash64 hash;
                    if (s.FileStartIndex <= 0 && s.FileEndIndex >= 0)
                    {
                        hash = new XXHash64();
                        hash.ComputeHash(fsInfo.FstData, (int)fsInfo.BootBinOffset, (int)fsInfo.BootBinSize);
                        fsInfo.SystemFiles[0].XxHash = hash.HashUInt64;
                        fsInfo.SystemFiles[0].Crc = Crc.Compute(fsInfo.FstData, (int)fsInfo.BootBinOffset, (int)fsInfo.BootBinSize);
                    }
                    if (s.FileStartIndex <= fsInfo.SystemFiles.Count - 1 && s.FileEndIndex >= fsInfo.SystemFiles.Count - 1)
                    {
                        hash = new XXHash64();
                        hash.ComputeHash(fsInfo.FstData, (int)fsInfo.FstOffset, (int)fsInfo.FstSize);
                        fsInfo.SystemFiles.Last().XxHash = hash.HashUInt64;
                        fsInfo.SystemFiles.Last().Crc = Crc.Compute(fsInfo.FstData, (int)fsInfo.FstOffset, (int)fsInfo.FstSize);
                    }

                    buf.WriteFs(fsInfo.FstData, (int)s.FsOffset, WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize, 0, (int)Math.Min(s.FsSize, fsInfo.FstData.Length - s.FsOffset));
                    patchApplied = true;
                }

                if (s.PatchInfo.MarkForCalculatedData) //we have stored hashes that were invalid and preserverd
                {
                    long off = fsInfo.PreservedHashMap[s.AreaOffset];
                    for (int i = 0; i < s.Size; i += WiiConsts.WiiSectorSize)
                    {
                        Array.Copy(fsInfo.PreservedHashes, off, processor.Decrypted, i, WiiConsts.WiiSectorHashSize);
                        processor.PatchInfo.MarkForCalculatedData = true; //important, prevents hashes being rebuilt
                        off += WiiConsts.WiiSectorHashSize;
                        patchApplied = true;
                    }
                }
            }

            if (patchApplied)
            {
                s.HashesValid = processor.Patch(s);
                s.IsCreatable = processor.IsCreatable;
                return processor;
            }

            return null;
        }
    }
}
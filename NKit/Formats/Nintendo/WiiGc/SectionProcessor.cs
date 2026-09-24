using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class SectionProcessor : SectionProcessorBase, ISectionProcessor
    {
        private long _junkFsOffset;
        private long _junkLength;
        protected byte[][] _junk;
        private bool _wiiPtn;
        private bool _populated; //expensive so only run once
        private bool _hasJunk;


        private WiiSecurity _security;

        private readonly bool _isWii;
        private readonly string _discId;
        private readonly int _discNo;
        private IBuffer _buffer;

        private FileSystemInfo _fsInfo;

        protected override string SectionTag => Nanook.NKit.LogScopes.WiiGc;
        // Wii partitions carry verifiable hashes; GameCube does not.
        protected override bool SectionHasVerifiableHashes => _isWii && (this.ImageInfo?.OutputHashes ?? false) && this.Type == AreaType.FileSystem;

        internal SectionProcessor(string discId, int discNo, IImageInfo imageInfo)
        {
            this.ImageInfo = imageInfo;
            _discId = discId;
            _discNo = discNo;
            _isWii = this.ImageInfo.Type == ImageType.Wii;
            _junk = null;
        }

        public override void Update()
        {
            Crc = 0;
            CrcDecrypted = 0;

            if (_security == null)
                _security = new WiiSecurity((int)WiiConsts.WiiGroupSize);

            base.Update();

            State.Populate((int)(WiiConsts.WiiGroupSize / WiiConsts.WiiSectorSize / 8) + 1); //64 sectors plus 1 byte for scrub type
            _wiiPtn = Type == AreaType.FileSystem && _isWii; //are we a wii partiton
            _populated = false;
        }


        //populate the expensive state (can ran in parallel) - after read passes it off
        public void Process()
        {
            if (_populated)
                return;

            _fsInfo = (FileSystemInfo)base.FileSystemData;
            _hasJunk = Type == AreaType.FileSystem || (Type == AreaType.Other && _fsInfo != null && _fsInfo.Type != PartitionType.Update);
            _buffer = base.Buffer;
            _populated = true;
            Items = new SectionItems();

            if (_hasJunk)
                createJunk();

            string pathLabel;
            if (!_isWii || !this.ImageInfo.OutputHashes)
            { pathLabel = "GcRvtH"; processGcAndRvtH(); }
            else if (((ImageInfo)this.ImageInfo).IsNkit) //use generic for patching as all nkit work has been done
            { pathLabel = "Nkit"; processWiiNkit(); }
            else if (((ImageInfo)this.ImageInfo).IsNkitDecoded)
            { pathLabel = "NkitDecoded"; processWiiNkitDecoded(); } //decoded NKit: clean FS w/ zero hash-gaps; rebuild hashes + encrypt once
            else if (this.ImageInfo.ContainerType == ContainerType.IsoDec)
            { pathLabel = "IsoDec"; processWiiIsoDec(); }
            else if ((this.ImageInfo.ContainerType == ContainerType.Wia || this.ImageInfo.ContainerType == ContainerType.Rvz) && !(Type == AreaType.FileSystem && (_fsInfo == null || _fsInfo.IsWiiRvzEncryptedPartition)))
            { pathLabel = "WiaRvz"; processWiiWiaRvz(); }
            else
            { pathLabel = "Generic"; processWiiGeneric(); }

            base.ParallelChecksumAndCleanse();
            base.XxHashParallel();

            logAreaSummary(pathLabel);
        }

        // [Sect] Detail: one concise summary per FileSystem AREA (partition), NOT per section. Pool
        // processors are reused and Process() runs per 2 MiB section, so gate on the STABLE area id
        // (AreaInfo.AreaNo) — the previous per-section AreaOffset changed every section. A section
        // is additionally logged (Warning, red) ONLY when it is bad (not valid), so failures stand
        // out without the healthy-section noise.
        private void logAreaSummary(string pathLabel)
        {
            if (Type != AreaType.FileSystem)
                return;

            ImageInfo info = (ImageInfo)this.ImageInfo;
            ILogScope log = info.SectionLog;
            if (log == null)
                return;

            // Per-area summary: emit exactly once per area, deduped ACROSS the whole processor pool
            // (TryMarkAreaLogged is shared on ImageInfo — a per-instance field would log once per
            // pooled processor, i.e. many times for the same area).
            if (AreaInfo != null && info.TryMarkAreaLogged(AreaInfo.AreaNo))
            {
                if (log.IsEnabled(LogLevel.Detail))
                {
                    string id = _fsInfo?.Id ?? "?";
                    int files = _fsInfo?.FileSystem?.Files?.Count ?? 0;
                    log.Log(LogLevel.Detail,
                        $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Section)}partition id '{id}' off 0x{AreaInfo.ImageOffset:X} path={pathLabel}"
                        + $" enc:{(AreaInfo.IsEncryptionSupported ? "y" : "n")} files {files}");
                }
            }

            // Bad section: log at Warning (rendered red) so it is not lost among healthy sections.
            // ONLY meaningful for sections that carry verifiable hashes (Wii partitions). GameCube /
            // RVT-H (GcRvtH path) have no hashes to validate, so IsValid is false by default there —
            // reporting those as "Bad" is a false positive, so skip them.
            if (!IsValid && this.SectionHasVerifiableHashes)
                log.Log(LogLevel.Warning,
                    $"Bad section off 0x{ImageOffset:X} size 0x{Size:X} in partition '{_fsInfo?.Id ?? "?"}'"
                    + $" (path={pathLabel}, creatable:{(IsCreatable ? "y" : "n")})");
        }

        public bool Patch(ScanSection srs)
        {
            _buffer = base.Buffer;
            _fsInfo = (FileSystemInfo)base.FileSystemData;
            _hasJunk = Type == AreaType.FileSystem || (Type == AreaType.Other && _fsInfo != null && _fsInfo.Type != PartitionType.Update);
            // GcFixAsIso already produced the final, correct GameCube image up front — do not
            // re-patch the FST/header here (it would overwrite the correct pointers with
            // recomputed ones and corrupt the already-valid output).
            bool fix = this.ImageInfo.Mode == ReadMode.Fix && !((ImageInfo)this.ImageInfo).IsGcFixApplied;

            this.Items = srs.Items;
            this.State = srs.State;
            bool creatable;
            bool valid = false;

            ((Buffer)Buffer).IsEncrypted = _buffer.AreaInfo.IsEncryptionSupported;

            if (this.AreaInfo.IsEncrypted)
            {
                _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, false, false, false, AreaOffset, _fsInfo.H3Table, State, false);

                if (srs.PatchInfo.MarkForPatching && !srs.PatchInfo.MarkForCalculatedData) //data has changed || hashes have been restored
                    _security.MarkDirty();

                this.IsValid = _security.IsValid(!srs.PatchInfo.MarkForCalculatedData, out creatable); //don't recalc hashes if we've restored them
                this.IsCreatable = creatable;
                _security.Encrypt();

                createJunk();

                if (analyseScrubbingFinalise())
                {
                    if (this.ImageInfo.Mode == ReadMode.Fix && ((ImageInfo)this.ImageInfo).IsNkit)
                    {
                        bool changed = unscrubAndClearMissingDataWii();

                        if (fix)
                            changed |= fsFixPatch();
                        //if (fillGaps) //or set to full junk fill repair (TO ADD)
                        //    changed = fsSectionGapFill();
                        valid = _security.IsValid(changed, out creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                        if (changed)
                            _security.Encrypt(); //force encryption
                    }
                    else
                    {
                        _security.ForceDecrypt(); //wipes the scrubbed sectors in the decrypted CiBuffer
                        valid = _security.IsValid(false, out creatable);
                    }
                    this.IsValid = valid;
                    this.IsCreatable = creatable;
                    srs.PatchInfo.ScrubbingChanged = true;
                    _hasJunk = Type == AreaType.FileSystem || (Type == AreaType.Other && _fsInfo != null && _fsInfo.Type != PartitionType.Update);
                }
                this.Items = new SectionItems();
                fsSectionCreateItems();
                fsSectionAnalyseItems();
                srs.Items = this.Items;
            }
            else if (fix)
                fsFixPatch();

            base.Complete();
            this.Status = CompletionStatus.Complete; //force complete

            //recalc any xxhashes. Any that span sections are calculated elsewhere
            foreach (SectionItem si in Items.Where(a => a.File != null && a.FsFile != null && a.File.FsSize == a.FsFile.FsSize))
                ((SectionData)si.File).XxHash = _buffer.XxHashFsData((int)si.File.FsOffset, (int)si.File.FsSize);
            srs.Crc = this.Crc;
            srs.CrcDecrypted = this.CrcDecrypted;
            srs.XxHash = base.XxHash = XXHash64.Compute(srs.Data, 0, (int)srs.Size);

            base.ParallelChecksumAndCleanse();
            return this.IsValid;
        }


        private void processWiiNkit()
        {
            bool encrypted = _buffer.AreaInfo.IsEncrypted && this.ImageInfo.SourceHasEncryption; //for patching
            bool fix = this.ImageInfo.Mode == ReadMode.Fix;
            bool valid = false;
            bool creatable = false;

            ((Buffer)Buffer).IsEncrypted = _buffer.AreaInfo.IsEncryptionSupported;

            if (Type == AreaType.FileSystem)
            {
                if (_fsInfo.IsWiiRvzEncryptedPartition)
                {
                    // Already-encrypted+hashed partition (e.g. NKitAsIso-reinserted update partition
                    // from a recovery file). Pass through unchanged: populate as encrypted, do NOT
                    // rebuild hashes or MarkDirty. Encrypt() is then a no-op returning the original
                    // bytes; IsValid(false) validates without rebuilding. Byte-identity preserved.
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, true, false, false, AreaOffset, _fsInfo.H3Table, State, false);
                    fsSectionCreateItems();
                    valid = _security.IsValid(false, out creatable);
                    _security.Encrypt(); //no-op (already encrypted) — returns source bytes
                    fsSectionAnalyseItems();
                    this.IsCreatable = creatable;
                    this.IsValid = valid;
                    return;
                }

                if (_fsInfo.IsFixFile)
                {
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, true, true, false, AreaOffset, _fsInfo.H3Table, State, false);
                    _security.Decrypt();
                }
                else
                {
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, encrypted, false, !this.PatchInfo.MarkForCalculatedData, AreaOffset, _fsInfo.H3Table, State, false);
                    _security.MarkDirty();
                }

                SectionProcessorNKit.FixMissingWiiFsScrubbing(this.Buffer, this.MissingData, _fsInfo);
                fillMissingData(this.MissingData, false);
                fsSectionCreateItems();
                SectionProcessorNKit.FixNulls(_buffer, _fsInfo, this.Items, isJunkFile);


                valid = _security.IsValid(true, out creatable); //build hashes

                if (!this.PatchInfo.MarkForPatching && !this.PatchInfo.MarkForCalculatedData) //data has changed || hashes have been restored
                {
                    fillMissingData(this.MissingData, false);

                    SectionProcessorNKit.FixNulls(_buffer, _fsInfo, this.Items, isJunkFile);
                    _security.Encrypt();
                    analyseScrubbing(); //mark filled encrypted sectors

                    if (fix) //check if we're valid - quick mode
                    {
                        bool changed = unscrubAndClearMissingDataWii(); //returns false if not wii
                        changed |= fsFixPatch();
                        //if (fillGaps) //or set to full junk fill repair (TO ADD)
                        //    changed = fsSectionGapFill();
                        if (changed) //creatable is set to false if called twice with no change
                        {
                            valid = _security.IsValid(changed, out creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                            _security.Encrypt(); //force encryption
                        }
                    }

                    if (!State.IsClear())
                        _security.ForceDecrypt(); //wipes the scrubbed sectors in the decrypted CiBuffer
                }
                this.MissingData.RemoveAll(a => a.Type == MetaDataType.Fill); //prevent nkit scrubbing being identified incorrectly
                fsSectionAnalyseItems();
            }
            else
            {
                if (Type == AreaType.Other)
                {
                    fillMissingData(this.MissingData, false);
                    processOtherSection(fix);
                }
                if (fix)
                    fsFixPatch();
            }

            this.IsCreatable = creatable;
            this.IsValid = valid;
        }

        // Lean processing for a fully-decoded NKit source (NKitAsIso). The FS data arrives clean and
        // correctly positioned with zeroed hash gaps and junk already materialised, so the work is
        // just: build hashes once and encrypt once. Skips the missing-data / double FixNulls /
        // second scrubbing-analysis / ForceDecrypt passes that the legacy processWiiNkit needs.
        private void processWiiNkitDecoded()
        {
            bool valid = false;
            bool creatable = false;

            ((Buffer)Buffer).IsEncrypted = _buffer.AreaInfo.IsEncryptionSupported;

            if (Type == AreaType.FileSystem)
            {
                if (_fsInfo.IsWiiRvzEncryptedPartition)
                {
                    // Already-encrypted+hashed partition (NKitAsIso-reinserted update partition).
                    // Pass through unchanged: populate as encrypted, no rebuild, Encrypt() no-op.
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, true, false, false, AreaOffset, _fsInfo.H3Table, State, false);
                    fsSectionCreateItems();
                    valid = _security.IsValid(false, out creatable);
                    _security.Encrypt(); //no-op — already encrypted
                    fsSectionAnalyseItems();
                }
                else if (((ImageInfo)this.ImageInfo).IsPreservedHashGroup != null
                         && ((ImageInfo)this.ImageInfo).IsPreservedHashGroup(ImageOffset - AreaOffset, AreaOffset))
                {
                    // This 0x200000 group has PRESERVED (non-recreatable) hashes. NKitAsIso already
                    // reproduced them verbatim into the decrypted hash gaps on read, so we must NOT
                    // regenerate them (a hacked disc's hashes are intentionally invalid — rebuilding
                    // would change them). Populate as decrypted with hashesRestored=true so the
                    // security layer treats the hashes as final; no MarkDirty, no rebuild.
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, false, false, false, AreaOffset, _fsInfo.H3Table, State, true);
                    fsSectionCreateItems();
                    valid = _security.IsValid(false, out creatable); //keep restored hashes
                    _security.Encrypt();
                    fsSectionAnalyseItems();
                }
                else
                {
                    // Plaintext, zero-hash-gap FS: populate as decrypted, mark dirty, build hashes,
                    // encrypt once.
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, false, false, true, AreaOffset, _fsInfo.H3Table, State, false);
                    _security.MarkDirty();
                    fsSectionCreateItems();
                    valid = _security.IsValid(true, out creatable); //build hashes
                    _security.Encrypt();
                    fsSectionAnalyseItems();
                }
            }
            else if (Type == AreaType.Other)
            {
                processOtherSection(false);
            }

            this.IsCreatable = creatable;
            this.IsValid = valid;
        }

        private void processWiiWiaRvz()
        {
            bool fix = this.ImageInfo.Mode == ReadMode.Fix;
            bool valid = false;
            bool creatable = false;
            bool isWia = this.ImageInfo.ContainerType == ContainerType.Wia; //false is rvz
            bool changed = false;


            fillMissingData(this.MissingData, false);

            ((Buffer)Buffer).IsEncrypted = _buffer.AreaInfo.IsEncryptionSupported;

            if (Type == AreaType.FileSystem)
            {
                _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, false, false, false, AreaOffset, _fsInfo.H3Table, State, false);
                fsSectionCreateItems();

                if (isWia) //decrypted and possibly scrubbed (full 32k sectors)
                {
                    analyseScrubbingDecryptedSafe();
                    if (fix)
                        changed = unscrubAndClearMissingDataWii(); //will mark security as dirty
                }
                if (fix)
                    changed |= fsFixPatch();


                valid = _security.IsValid(changed, out creatable);

                _security.Encrypt();
                fsSectionAnalyseItems();

                if (!isWia && analyseScrubbingFinalise())
                {
                    if (fix) //rvz recovery
                    {
                        changed = unscrubAndClearMissingDataWii();
                        changed |= fsFixPatch();
                        //if (fillGaps) //or set to full junk fill repair (TO ADD)
                        //    changed = fsSectionGapFill();
                        if (changed)
                        {
                            valid = _security.IsValid(changed, out creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                            _security.Encrypt(); //force encryption
                        }
                    }
                    else
                    {
                        _security.ForceDecrypt(); //wipes the scrubbed sectors in the decrypted CiBuffer
                        valid = _security.IsValid(false, out creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                    }

                    //if source was decrypted and there's scrubbing the scan can differ. This is because scrubbed areas will be decrypted to repeating 16 bytes. Nkit attempts to preserve the encrypted source. Attempt a fix here
                    this.Items = new SectionItems();
                    fsSectionCreateItems();
                    fsSectionAnalyseItems();
                }
            }
            else
            {
                if (Type == AreaType.Other)
                    processOtherSection(fix);
                if (fix)
                    fsFixPatch();
            }

            this.IsCreatable = creatable;
            this.IsValid = valid;
        }

        private void processWiiIsoDec()
        {
            bool fix = this.ImageInfo.Mode == ReadMode.Fix;
            bool valid = false;
            bool creatable = false;

            fillMissingData(this.MissingData, false);

            ((Buffer)Buffer).IsEncrypted = _buffer.AreaInfo.IsEncryptionSupported;

            if (Type == AreaType.FileSystem)
            {
                _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, false, true, false, AreaOffset, _fsInfo.H3Table, State, false);
                _security.Encrypt();

                analyseScrubbing(); //mark filled encrypted sectors

                fsSectionCreateItems();

                if (fix)
                {
                    bool changed = unscrubAndClearMissingDataWii();
                    changed |= fsFixPatch();
                    //if (fillGaps) //or set to full junk fill repair (TO ADD)
                    //    changed = fsSectionGapFill();
                    valid = _security.IsValid(changed, out creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                    if (changed)
                        _security.Encrypt(); //force encryption
                }
                else
                {
                    if (!State.IsClear())
                        _security.ForceDecrypt(); //blank the scrubbed decrypted data
                    valid = _security.IsValid(false, out creatable);
                }

                fsSectionAnalyseItems();
            }
            else
            {
                if (Type == AreaType.Other)
                    processOtherSection(fix);
                if (fix)
                    fsFixPatch();
            }

            this.IsCreatable = creatable;
            this.IsValid = valid;
        }

        private void processGcAndRvtH()
        {
            // GameCube Fix already fully applied up front by GcFixAsIso: the stream is the final,
            // proven-correct ISO (system-area brute force resolved deterministically by CRC, files
            // placed, gaps junk-filled, size corrected). Only build items and checksum — do NOT
            // run any buffer-mutating step (fillMissingData/gap-fill/FST-patch/analyse) over data
            // that is already final.
            if (((ImageInfo)this.ImageInfo).IsGcFixApplied)
            {
                fsSectionCreateItems();
                return;
            }

            bool fix = this.ImageInfo.Mode == ReadMode.Fix;

            fillMissingData(this.MissingData, false);

            fsSectionCreateItems();
            if (((ImageInfo)this.ImageInfo).IsNkit)
                SectionProcessorNKit.FixNulls(_buffer, _fsInfo, this.Items, isJunkFile);

            if (fix)
            {
                if (!_isWii)
                    fsSectionGapFill(); //fill all gaps, wii just does blocks currently
                fsFixPatch();
            }
            fsSectionAnalyseItems();
        }

        private void processWiiGeneric()
        {
            bool fix = this.ImageInfo.Mode == ReadMode.Fix;
            bool valid = false;
            bool creatable = false;

            ((Buffer)Buffer).IsEncrypted = _buffer.AreaInfo.IsEncryptionSupported;

            bool changed = false;
            if (this.MissingData.Count != 0)
                changed = processWiiGenericWbfsCisoFill();

            if (!changed)
                fillMissingData(this.MissingData, Type == AreaType.FileSystem);

            if (Type == AreaType.FileSystem && _fsInfo != null)
            {
                if (_fsInfo.IsWiiRvzEncryptedPartition)
                {
                    // Verbatim, already-encrypted+hashed partition served from a recovery file
                    // (a Fix that injects a missing update partition, or an RVZ-stored encrypted
                    // partition that fell through the router). There is no parsed FST (null
                    // FstData/FileSystem/JunkId), so pass it through unchanged: populate as
                    // encrypted, validate WITHOUT rebuilding hashes, Encrypt() is a no-op that
                    // returns the source bytes. Do NOT decrypt / re-hash / junk-fill / walk
                    // FileSystem.Files (which would NRE on the null FST).
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, true, false, false, AreaOffset, _fsInfo.H3Table, State, false);
                    fsSectionCreateItems();
                    valid = _security.IsValid(false, out creatable);
                    _security.Encrypt(); //no-op (already encrypted) — returns source bytes
                    fsSectionAnalyseItems();
                    this.IsCreatable = creatable;
                    this.IsValid = valid;
                }
                else
                {

                    analyseScrubbing(); //mark filled encrypted sectors

                    if (!changed)
                        _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, true, true, false, AreaOffset, _fsInfo.H3Table, this.State, false);
                    _security.Decrypt();

                    fsSectionCreateItems();
                    if (fix)
                    {
                        changed = unscrubAndClearMissingDataWii();
                        changed |= fsFixPatch();
                        // A wiped Wii disc scrubs the disc's JUNK gaps to encrypted-uniform blocks that
                        // decrypt to a repeating byte, but the sector hashes remain valid over that
                        // uniform data - so analyseScrubbing (encrypted domain) never flags them and the
                        // unscrub above no-ops. Regenerate the junk directly from the FST gap ranges:
                        // fsSectionGapFillChanged only writes GAP bytes (never file data) using each
                        // file's ExpectedNulls, and only MarkDirty's sections that actually had a gap.
                        // Sections that are pure file data (no gap) are left byte-for-byte untouched.
                        changed |= fsSectionGapFillChanged();
                        valid = _security.IsValid(changed, out creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                        if (changed)
                            _security.Encrypt(); //force encryption
                    }
                    else
                        valid = _security.IsValid(false, out creatable);

                    fsSectionAnalyseItems();
                } //end else (non-pass-through FileSystem)
            }
            else
            {
                // Other filler, OR a FileSystem-type area with no parsed FileSystemInfo (a
                // placeholder/inserted partition region that arrived without a valid partition
                // header, e.g. a wiped disc whose recovery layout advertises a partition the read
                // stage cannot parse). Per the Fix model the read stage must "do what it can and
                // not error": treat it as a plain null-filler gap. Injection/repair of these
                // regions is owned by the output stage (FixWiiGcStep.ProcessResults brute-force).
                if (Type == AreaType.Other || Type == AreaType.FileSystem)
                    processOtherSection(fix);
                if (fix)
                    fsFixPatch();
            }

            this.IsCreatable = creatable;
            this.IsValid = valid;
        }

        private bool processWiiGenericWbfsCisoFill()
        {
            if ((this.ImageInfo.ContainerType == ContainerType.Ciso || this.ImageInfo.ContainerType == ContainerType.Wbfs) && this.MissingData.Exists(a => a.Type == MetaDataType.NJunk))
            {
                if (this.Type == AreaType.FileSystem)
                {
                    _security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, true, true, true, AreaOffset, _fsInfo.H3Table, this.State, false);
                    _security.Decrypt();

                    fillMissingData(this.MissingData, true);
                    //_security.Populate(_fsInfo.Key, _buffer.Encrypted, _buffer.Decrypted, (int)Size, false, false, true, AreaOffset, _fsInfo.H3Table, this.State);
                    _security.MarkDirty();
                    bool valid = _security.IsValid(true, out bool creatable);  //builds scrubbed hashes - great for wbfs, ciso etc
                    _security.Encrypt(); //force encryption
                    this.MissingData.Clear();
                }
                else
                    fillMissingData(this.MissingData, false);
                return true;
            }
            else
                return false;
        }

        private bool fsFixPatch()
        {
            bool changed = false;
            if (((Nintendo.WiiGc.FixData)this.ImageInfo.FixData)?.DataPatches != null)
            {
                RangeResult rr = new RangeResult();
                foreach (DataPatches jp in ((Nintendo.WiiGc.FixData)this.ImageInfo.FixData)?.DataPatches.Where(a => (long)(a.Offset + a.Data.Length) > this.ImageOffset && (long)a.Offset < this.ImageOffset + (long)this.Size))
                {
                    long off = jp.Offset - this.AreaInfo.ImageOffset;
                    off = (int)Nanook.NKit.Buffer.OffsetToFsOffset(off, this.Buffer.BlockSize, this.Buffer.BlockFsOffset, this.Buffer.BlockFsSize);
                    this.Buffer.TestFsRange(off, jp.Data.Length, rr);

                    if (rr.Size != 0)
                    {
                        this.Buffer.WriteFs(jp.Data, (int)rr.RangeOffset, WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize, rr.BufferOffset, rr.Size);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private bool analyseScrubbingFinalise()
        {
            bool reanalyse = false;
            bool origHashScrub = hashesAreScrubbed;

            BitState b = State.Clone();
            analyseScrubbing();

            if (!b.Bytes.Equals(0, State.Bytes, 0, b.Bytes.Length)) //has something changed
            {
                bool scrubHashes = hashesAreScrubbed;
                bool scrubAllHash = !origHashScrub && scrubHashes; //if true we need to scrub all hashes with scrubbed data
                int bi = 0;
                for (int i = 0; i < State.Bytes.Length - 1; i++)
                {
                    if (State.Bytes[i] != 0 && (scrubAllHash || b.Bytes[i] != State.Bytes[i])) //something to do
                    {
                        reanalyse = true;
                        for (int bits = bi + 8; bi < bits; bi++)
                        {
                            bool origSet = b[bi];
                            if (!origSet && State[bi]) //flipped from off to on
                            {
                                if (scrubHashes)
                                    Array.Copy(this.Encrypted, bi * AreaInfo.BlockSize, this.Decrypted, bi * AreaInfo.BlockSize, AreaInfo.BlockSize); //0x8000 bytes
                                else
                                    Array.Copy(this.Encrypted, (bi * AreaInfo.BlockSize) + AreaInfo.BlockFsOffset, this.Decrypted, (bi * AreaInfo.BlockSize) + AreaInfo.BlockFsOffset, AreaInfo.BlockFsSize); //0x7c00
                            }
                            else if (origSet && scrubAllHash)
                                Array.Copy(this.Encrypted, bi * AreaInfo.BlockSize, this.Decrypted, bi * AreaInfo.BlockSize, AreaInfo.BlockFsOffset);
                        }
                    }
                    else
                        bi += 8;
                }
            }

            return reanalyse;
        }

        private void analyseScrubbing() //returns true if any bit is flipped
        {
            bool incHashes = true;
            int dataSize = AreaInfo.BlockFsSize;
            int hashSize = AreaInfo.BlockSize - AreaInfo.BlockFsSize;

            this.State.Clear();

            int sectors = (int)this.Size / AreaInfo.BlockSize;
            byte[] data = this.Encrypted;
            //bool s;

            for (int i = 0; i < sectors; i++)
            {
                int off = i * AreaInfo.BlockSize;
                byte scrubByte = data[off + hashSize];

                State[i] = data.Equals(off + hashSize, dataSize, scrubByte);

                //test all hashes are scrubbed for all scrubbed data blocks
                if (incHashes && State[i])
                    incHashes = data.Equals(off, hashSize, scrubByte); //must be same byte as data
            }

            this.hashesAreScrubbed = incHashes && !State.IsClear();
        }

        private void analyseScrubbingDecryptedSafe() //full 32k including hashes
        {
            int dataSize = AreaInfo.BlockSize;

            this.State.Clear();

            int sectors = (int)this.Size / dataSize;
            byte[] data = this.Decrypted;

            for (int i = 0; i < sectors; i++)
            {
                int off = i * AreaInfo.BlockSize;
                byte scrubByte = data[off];

                State[i] = data.Equals(off, dataSize, scrubByte);
            }

            this.hashesAreScrubbed = !State.IsClear();
        }

        private bool unscrubAndClearMissingDataWii()
        {
            if (!_isWii || this.State.IsClear())
                return false;

            int sectors = (int)this.Size / AreaInfo.BlockSize;

            int fi = this.FileStartIndex;

            if (fi != -1)
            {
                FstFile f = (FstFile)_fsInfo.FileSystem.Files[fi];

                for (int i = 0; i < sectors; i++)
                {
                    if (State[i])
                    {
                        int fsOffset = i * AreaInfo.BlockFsSize;
                        long sectorFsOffset = this.FsOffset + (long)fsOffset;

                        while (fi + 1 < _fsInfo.FileSystem.Files.Count)
                        {
                            if (_fsInfo.FileSystem.Files[fi + 1].PostGapFsOffset <= sectorFsOffset)
                                f = (FstFile)_fsInfo.FileSystem.Files[++fi];
                            else
                                break;
                        }

                        int junkNulls = 0;
                        if (f.PostGapFsOffset <= sectorFsOffset && f.PostGapFsOffset + f.Analysis.ExpectedNulls > sectorFsOffset)
                            junkNulls = (int)(f.PostGapFsOffset + f.Analysis.ExpectedNulls - sectorFsOffset);

                        junkFill(fsOffset, AreaInfo.BlockFsSize, junkNulls);

                        //harvestmoon wii wbfs has scrubbed sections containing 0 byte files and the nulls must be added part way through scrubbed / removed blocks
                        long sectorEnd = sectorFsOffset + AreaInfo.BlockFsSize;
                        while (fi + 1 < _fsInfo.FileSystem.Files.Count)
                        {
                            if (_fsInfo.FileSystem.Files[fi + 1].PostGapFsOffset <= sectorEnd)
                            {
                                f = (FstFile)_fsInfo.FileSystem.Files[++fi];
                                writeNulls((int)(f.PostGapFsOffset - this.FsOffset), Math.Min(f.Analysis.ExpectedNulls, (int)(sectorEnd - f.PostGapFsOffset)));
                            }
                            else
                                break;
                        }

                        State[i] = false;
                        if (Type == AreaType.FileSystem)
                            _security.MarkDirty(); //needs to be set to NOT scrubbed
                    }
                }
            }
            State.Clear();
            MissingData.Clear(); //we're resolved it all so pretent we had none
            return true;
        }

        private void fsSectionCreateItems()
        {
            RangeResult rr = new RangeResult();

            if (FileStartIndex == -1)
            {
                SectionItem si = new SectionItem(this.ImageOffset, AreaOffset, 0, null) { FileIndex = -1 };
                si.Gap = new SectionData(this.ImageOffset, this.AreaInfo, 0, FsSize) { OffsetInItem = 0 };
                Items.Add(si);
                return;
            }

            for (int idx = FileStartIndex; idx <= FileEndIndex; idx++)
            {
                IFsFile f = _fsInfo.FileSystem.Files[idx];

                SectionItem si = new SectionItem(ImageOffset, AreaOffset, 0, f);
                _buffer.TestFsRange(f.FsOffset, f.FsSize, rr);
                si.FileIndex = idx;
                if (rr.IsMatch)
                {
                    //file
                    si.File = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                }

                if ((!rr.IsMatch || rr.RangeComplete) && f.PostGapSize != 0)
                {
                    //gap
                    _buffer.TestFsRange(f.PostGapFsOffset, f.PostGapSize, rr);

                    if (rr.IsMatch && rr.Size != 0)
                        si.Gap = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                }
                Items.Add(si);
            }
        }

        private void fsSectionGapFill() => fsSectionGapFillChanged();

        // Regenerate junk into every gap of this section using the FST analysis (leading ExpectedNulls
        // then position-seeded junk). Returns true if any gap was written so the caller forces a hash
        // rebuild + re-encrypt. This restores the disc's junk/null pattern for a wiped source whose
        // gaps decrypt to nulls. Only gap ranges are touched — file data is never modified.
        private bool fsSectionGapFillChanged()
        {
            bool filled = false;
            foreach (SectionItem si in Items)
            {
                if (si.Gap != null)
                {
                    int nulls = 0;
                    if (si.FileIndex != -1)
                    {
                        FstFile f = (FstFile)_fsInfo.FileSystem.Files[si.FileIndex];
                        nulls = f.Analysis.ExpectedNulls;
                    }

                    int junkNulls = (int)Math.Min(Math.Min(si.Gap.FsSize, nulls), Math.Max(nulls - si.Gap.OffsetInItem, 0L));
                    junkFill((int)si.Gap.FsOffset, (int)si.Gap.FsSize, junkNulls); //write junk and aligning
                    filled = true;
                }
            }
            if (filled && Type == AreaType.FileSystem)
                _security.MarkDirty(); //decrypted buffer changed — force hash rebuild + re-encrypt
            State.Clear();
            MissingData.Clear(); //we're resolved it all so pretent we had none
            return filled;
        }

        private void fsSectionAnalyseItems()
        {
            int mdIdx = 0;

            foreach (SectionItem si in Items)
            {
                int nulls = 0;
                if (si.FileIndex != -1)
                {
                    FstFile f = (FstFile)_fsInfo.FileSystem.Files[si.FileIndex];
                    nulls = f.Analysis.ExpectedNulls;
                }

                if (si.File != null)
                {
                    FstFile prev = si.FileIndex <= 0 ? null : (FstFile)_fsInfo.FileSystem.Files[si.FileIndex - 1];
                    int junkNulls = prev == null ? 0 : Math.Max(0, (int)(prev.Analysis.MaxNullsSize - prev.Analysis.JunkNulls - si.File.OffsetInItem)); // f.Analysis.JunkNulls - si.File.OffsetInItem));
                    analyseDataWithMeta(ref mdIdx, junkNulls, (int)si.File.FsOffset, (int)si.File.FsSize, true, (o, s, t, n, b) => base.AnalysedItem(si, true, o, s, t, n, b));
                }

                if (si.Gap != null)
                {
                    int junkNulls = (int)Math.Min(Math.Min(si.Gap.FsSize, nulls), Math.Max(nulls - si.Gap.OffsetInItem, 0L));
                    analyseDataWithMeta(ref mdIdx, junkNulls, (int)si.Gap.FsOffset, (int)si.Gap.FsSize, false, (o, s, t, n, b) => base.AnalysedItem(si, false, o, s, t, n, b));
                }
            }
        }

        private void processOtherSection(bool fix)
        {
            int nulls = this.AreaOffset == 0 ? WiiConsts.DataNullsCount : 0;

            // The trailing 'Other' filler after a partition is JUNK seeded with the disc id (the
            // retail scan shows DataType=NJunk-1C/NJunk-00 here, JunkID = disc Id), NOT nulls. An
            // Other section is never a Wii partition (_wiiPtn=false), so createJunk seeds it from
            // _discId and does not need a parsed FST — a null FstData does not force nulls here.
            if (fix)
            {
                MissingData.Clear();
                State.Clear();
                if (_hasJunk)
                    junkFill(0, (int)Size, nulls);
                else
                    this.Decrypted.Clear(0, (int)Size, 0);
            }
            else //scan
            {
                if (!_hasJunk) //swap any junk missing datas to fill
                {
                    foreach (MetaData x in MissingData.Where(a => a.Type == MetaDataType.NJunk))
                        x.Type = MetaDataType.Fill;
                }

                //write some nulls if it's the first part of the data after the WipePartition data and we're recovering or data was missing
                if (_fsInfo != null && ImageOffset == _fsInfo.ImageOffsetData + _fsInfo.Size)
                {
                    nulls = (int)Math.Max(Math.Min(Size, WiiConsts.DataNullsCount), 0);
                    if (MissingData?.Count != 0 && MissingData[0].Type == MetaDataType.NJunk && MissingData[0].Offset == 0 && MissingData[0].Size >= nulls)
                        Array.Clear(Decrypted, 0, nulls);
                }
            }

            SectionItem si = new SectionItem(ImageOffset, AreaOffset, 0, null);
            SectionData gap = new SectionData() { FsOffset = 0, OffsetInItem = _buffer.FsOffset, FsSize = _buffer.FsSize };
            si.Gap = gap;

            int mdIdx = 0;
            if (_hasJunk)
                analyseDataWithMeta(ref mdIdx, nulls, (int)si.Gap.FsOffset, (int)si.Gap.FsSize, false, (o, s, t, n, b) => base.AnalysedItem(si, false, o, s, t, n, b));
            else
                base.AnalyseDataWithMeta(ref mdIdx, false, (int)si.Gap.FsOffset, (int)si.Gap.FsSize, (o, s, t, n, b) => base.AnalysedItem(si, false, o, s, t, n, b));

            Items.Add(si);
        }

        //Linear CsqThread safe postprocess
        public override void PostProcess()
        {
            base.XxHashLinear();
            base.PostProcess();
        }

        public IEnumerable<NonCreatableData> NonCreatableItems
        {
            get
            {
                switch (this.Type)
                {
                    case AreaType.ImageHeader:
                    case AreaType.PartitionHeader:
                        yield return new NonCreatableData() { Type = NonCreatableDataType.ImageSection, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = 0, Size = (int)this.Size };
                        break;
                    case AreaType.Other:
                    case AreaType.FileSystem:
                        bool isFs = this.Type == AreaType.FileSystem;

                        if (isFs && this.AreaInfo.HasSecurity && !this.IsCreatable)
                        {
                            for (int i = 0; i < _security.UsedSectors; i++)
                                yield return new NonCreatableData() { Type = NonCreatableDataType.Security, ImageSectionOffset = this.ImageOffset, IsFs = false, Offset = i * this.AreaInfo.BlockSize, Size = this.AreaInfo.BlockFsOffset };
                        }

                        foreach (ISectionData gd in this.Items.EnumSectionData().Where(a => a.DataType == DataType.Other))
                            yield return new NonCreatableData() { Type = NonCreatableDataType.Filler, ImageSectionOffset = this.ImageOffset, IsFs = isFs, Offset = (int)gd.FsOffset, Size = (int)gd.FsSize };
                        break;
                }
            }
        }

        private void fillMissingData(List<MetaData> missingData, bool encryptedBuffer)
        {
            if (encryptedBuffer)
            {
                foreach (MetaData md in missingData)
                {
                    if (md.Type == MetaDataType.Fill)
                        scrubFill(Encrypted, (int)md.Offset, (int)md.Size, md.BlockByte);
                    else if (md.Type == MetaDataType.NJunk) //lossless wbfs/ciso fill
                        junkFill((int)md.FsOffset, (int)md.FsSize, 0);
                }
            }
            else
            {
                foreach (MetaData md in missingData)
                {
                    if (Type == AreaType.FileSystem)
                    {
                        int align = (int)md.Size % 4;
                        if (md.Type == MetaDataType.Fill)
                            scrubFill(Decrypted, (int)md.Offset, (int)md.Size, md.BlockByte);
                        else if (md.Type == MetaDataType.NJunkFile && align != 0)
                            junkFill((int)md.FsOffset, (int)(md.FsSize + (4 - align)), 0); //add the aligning
                        else
                            junkFill((int)md.FsOffset, (int)md.FsSize, 0);
                    }
                    else
                    {
                        if (!_hasJunk || md.Type == MetaDataType.Fill)
                            scrubFill(Decrypted, (int)md.Offset, (int)md.Size, md.BlockByte);
                        else //recover needs to fill the full thing
                            junkFill((int)md.Offset, (int)md.Size, 0);
                    }
                }
            }
        }

        private bool hashesAreScrubbed
        {
            get => base.State[WiiConsts.WiiSectors];  //0x40
            set => base.State[WiiConsts.WiiSectors] = value;
        }

        public override void Complete()
        {
            //Base.Complete will set out Status to ToBePatched if nkit and Reuires patching
            _buffer.PatchInfo.MarkForPatching = ((ImageInfo)this.ImageInfo).IsNkit && SectionProcessorNKit.RequiresPatch(base.Buffer, _fsInfo);

            base.Complete();
        }

        private int isJunkFile(int mdIdx, int maxNulls, int sectionFsOffset, int size)
        {
            int sz = 0;
            int nulls = 0;
            analyseDataWithMeta(ref mdIdx, maxNulls, sectionFsOffset, size, true, (o, s, t, n, b) =>
            {
                if (t == DataType.NJunk)
                {
                    nulls += n;
                    sz += s;
                }
            });
            if (sz == size)
                return nulls; //return 0 or nulls size if valid junk
            return -1;
        }


        private void analyseDataWithMeta(ref int mdIdx, int maxNulls, int sectionFsOffset, int size, bool isFile, Action<int, int, DataType, int, byte> analysed)
        {
            int nullsCounted = 0;

            bool nullsCompleted = maxNulls == 0; //pretend they're checked if there's none to test

            int postFileNulls = 0;
            bool junkIsBlank = _fsInfo?.JunkStartFsOffset > (this.FsOffset + (long)sectionFsOffset);

            DataType dataType = this.Type == AreaType.ImageHeader || this.Type == AreaType.PartitionHeader || (this.Type == AreaType.FileSystem && isFile) ? DataType.Data : DataType.Other; //use Other for data type when gaps in FS

            DataType type;
            byte scrubByte = 0x00;

            this.ProcessBlocksWithMeta(ref mdIdx, sectionFsOffset, size, (off, fsOff, sz, mdIdx2) =>
            {
                if (_isWii)
                {
                    int blockIdx = fsOff / AreaInfo.BlockFsSize;
                    if (this.State[blockIdx])
                    {
                        analysed(fsOff, sz, DataType.Fill, 0, this.Encrypted[(blockIdx * AreaInfo.BlockSize) + AreaInfo.BlockFsOffset]);
                        return true;
                    }
                }

                int blockMaxNulls = nullsCompleted ? 0 : Math.Max(0, Math.Min(maxNulls - nullsCounted, sz));
                int remaining = sz;

                int blockNulls = 0;

                if (mdIdx2 != -1 && blockMaxNulls == 0) //use missing data - retest if there's nulls to be detected
                {
                    if (!nullsCompleted)
                    {
                        nullsCounted += blockMaxNulls; //assign and add
                        nullsCompleted = nullsCounted == maxNulls;
                    }

                    type = MissingData[mdIdx2].Type.ToDataType();
                    if (type == DataType.Data)
                        type = dataType;

                    analysed(fsOff, sz, type, blockMaxNulls, MissingData[mdIdx2].BlockByte);
                }
                else
                {
                    //analyseData(isFile ? WiiConsts.DataNullsCount : nulls, off, sz, isFile, analysed);
                    byte[] data = this.Decrypted;

                    if (isFile && maxNulls - nullsCounted > sz && sz % 4 != 0) //if file extend null check to cover any post file bytes to 4
                        postFileNulls = 4 - (sz % 4);

                    int joff = (int)((_wiiPtn ? FsOffset : ImageOffset) - _junkFsOffset); //align the junk block if it starts before this section
                    int junkIdx = (fsOff + joff) / NJunk.JunkBlockSize;
                    int junkOffset = (fsOff + joff) % NJunk.JunkBlockSize;

                    int jn = 0; //count any leading junk nulls
                    if (!nullsCompleted) //is true if no nulls required
                    {
                        while (blockNulls < blockMaxNulls && data[off + blockNulls] == 0)
                        {
                            if (jn == blockNulls && _junk[junkIdx][junkOffset + blockNulls] == 0)
                                jn++; //nulls in the 
                            blockNulls++;
                        }

                        if (blockNulls != 0 && blockNulls <= blockMaxNulls && (junkIsBlank || blockMaxNulls < 4 || jn != blockNulls)) //adjust rest of 0x400 block after testing nulls - blockMaxNulls < 4 is because these should always be null and junk algo sometimes has nulls and gives false positive
                        {
                            nullsCounted += blockNulls;
                            off += blockNulls;
                            junkOffset += blockNulls;
                            remaining -= blockNulls;
                            nullsCompleted = nullsCounted == maxNulls; //complete?
                        }
                        else
                        {
                            blockNulls = 0;
                            nullsCompleted = true; //no more checking
                        }
                    }

                    if (remaining == 0) //the end of the 0x400 block
                        type = isFile ? dataType : DataType.NJunk;
                    else
                    {
                        bool junk = _hasJunk && data.Equals(off, _junk[junkIdx], junkOffset, remaining);

                        if (junk)
                            type = DataType.NJunk;
                        else if (isFile) //file and not junk, should have quit out
                            type = DataType.Data;
                        else //gap
                        {
                            if (sz >= SectionProcessorBase.AnalyseBlockSize && AreaInfo.IsEncrypted && base.IsBlockFilled(this.Decrypted, off - nullsCounted, remaining + nullsCounted, ref scrubByte)) //check if scrubbed - only if >= to a sector block
                                type = DataType.Fill;
                            else if (base.IsBlockFilled(data, off - blockNulls, sz, ref scrubByte)) //check for blank decrypted data
                                type = DataType.Fill;
                            else
                                type = dataType;
                        }
                        nullsCounted = 0;
                    }

                    analysed(fsOff, sz, type, blockNulls, scrubByte); //if type is the same byte the scrubByte differs then use the last scrubbyte

                    if (isFile && (type != DataType.NJunk))
                        return false; //File and not junk exit - no more testing of this file
                }
                return true;
            });
        }

        private void junkFill(int sectionFsOffset, int size, int nulls) //skips hashes
        {
            if (nulls != 0)
            {
                writeNulls(sectionFsOffset, nulls);
                sectionFsOffset += nulls;
                size -= nulls;
            }

            if (size >= 0)
            {
                int joff = (int)((_wiiPtn ? FsOffset : ImageOffset) - _junkFsOffset); //align the junk block if it starts before this section
                int junkIdx = (sectionFsOffset + joff) / NJunk.JunkBlockSize;
                int junkOffset = (sectionFsOffset + joff) % NJunk.JunkBlockSize;

                int sz = Math.Min(size, NJunk.JunkBlockSize - junkOffset);
                while (size != 0)
                {
                    // Defence-in-depth: the junk window (_junk) is sized to this section only, so a
                    // junk-fill region that lands outside it means the caller passed a mis-based
                    // offset (e.g. a removed-block MetaData offset that was not section-relative).
                    // Fail loudly with context rather than throwing a bare IndexOutOfRangeException.
                    if (junkIdx < 0 || junkIdx >= _junk.Length)
                        throw new HandledException($"junkFill: junk block index {junkIdx} outside window [0,{_junk.Length}) (fsOffset=0x{sectionFsOffset:X}, joff=0x{joff:X}, size=0x{size:X})");
                    _buffer.WriteFs(_junk[junkIdx], junkOffset, WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize, sectionFsOffset, sz);
                    size -= sz;
                    junkIdx++;
                    junkOffset = 0;
                    sectionFsOffset += sz;
                    sz = Math.Min(size, NJunk.JunkBlockSize);
                }
            }
        }

        private void writeNulls(int sectionFsOffset, int size) => _buffer.ProcessFsData(sectionFsOffset, size, (data, off, fsOff, sz) => { data.Clear(off, sz, 0); return true; });

        private void scrubFill(byte[] buff, int offset, int size, byte scrubByte)
        {
            int bTst = offset;
            int bEnd = offset + size;

            if (!((ImageInfo)this.ImageInfo).IsNkit || _buffer.BlockSize == _buffer.BlockFsSize)
            {
                while (bTst < bEnd)
                    buff[bTst++] = scrubByte;
            }
            else
            {
                byte[] data;
                byte[] hash;
                int x = bTst % 16;

                if (scrubByte == 0x00)
                    data = hash = _fsInfo.DecryptedBlockFilled00;
                else if (scrubByte == 0xff)
                {
                    data = _fsInfo.DecryptedBlockFilledFF;
                    hash = _fsInfo.DecryptedBlockFilledFFHashes;
                }
                else
                    throw new Exception(string.Format("Wii Scrubbing does not support byte 0x{0}", scrubByte.ToString()));

                bool isHash = bTst % WiiConsts.WiiSectorSize == 0; //hash
                int bEndSec = bTst + (WiiConsts.WiiSectorSize - (bTst % WiiConsts.WiiSectorSize));

                while (bTst < bEnd)
                {
                    if (isHash)
                    {
                        int hashEnd = bTst + WiiConsts.WiiSectorHashSize;

                        //this.State[sec] = true; //hack to stop hashes being genned
                        while (bTst < hashEnd)
                        {
                            buff[bTst++] = hash[x++];
                            if (x == 16)
                                x = 0;
                        }
                    }
                    while (bTst < bEndSec)
                    {
                        buff[bTst++] = data[x++];
                        if (x == 16)
                            x = 0;
                    }
                    bEndSec += WiiConsts.WiiSectorSize;
                    if (bEndSec > bEnd)
                        bEndSec = bEnd;

                    isHash = true;
                }
            }
        }

        private void createJunk() //for wii and gc
        {
            if (_junk == null) //just for first use per WipePartition
            {
                _junk = new byte[(Decrypted.Length / NJunk.JunkBlockSize) + (Decrypted.Length % NJunk.JunkBlockSize == 0 ? 1 : 2)][]; //1 extra for alignment compensation
                for (int i = 0; i < _junk.Length; i++)
                    _junk[i] = new byte[NJunk.JunkBlockSize];
            }

            // A region with no parsed file system (null FstData) that is served VERBATIM or left
            // blank must be NULLS, not junk: an inserted channel/VC partition FileSystem (its bytes
            // come from the recovery file), or the update-partition region when no update is found
            // (Type==Other with a null/Update partition). There is no JunkId to seed from, so
            // zero-fill and skip generation - this also avoids dereferencing a null _fsInfo/JunkId.
            //
            // The one exception is the trailing 'Other' filler that follows a real/inserted
            // partition (Type==Other, partition is NOT the update): the disc seeds this with junk
            // from the DISC id (e.g. retail shows DataType=NJunk-1C, JunkID=<disc Id>). That path
            // below uses _discId (not _fsInfo.JunkId) so it is safe with a null FST - let it run.
            bool trailingDiscIdJunk = Type == AreaType.Other && _hasJunk && _fsInfo != null && _fsInfo.Type != PartitionType.Update;
            if (_fsInfo?.FstData == null && !trailingDiscIdJunk)
            {
                for (int i = 0; i < _junk.Length; i++)
                    Array.Clear(_junk[i], 0, _junk[i].Length);

                // Still set the junk window/offset (block-aligned) exactly as the normal path below.
                // fsSectionAnalyseItems -> analyseDataWithMeta indexes _junk[(fsOff + FsOffset -
                // _junkFsOffset) / JunkBlockSize]; if _junkFsOffset kept a stale value from the last
                // real partition the computed index falls outside _junk[] (IndexOutOfRangeException).
                // The buffers are zero-filled, so verbatim data simply classifies as Data/Fill, never
                // NJunk — which is correct for an FST-less verbatim/blank region.
                _junkLength = _wiiPtn ? FsSize : Size;
                _junkFsOffset = _wiiPtn ? FsOffset : ImageOffset;
                long jd = _junkFsOffset % NJunk.JunkBlockSize;
                if (jd != 0)
                {
                    _junkFsOffset -= jd;
                    _junkLength += jd;
                }
                jd = (_junkFsOffset + _junkLength) % NJunk.JunkBlockSize;
                if (jd != 0)
                    _junkLength += NJunk.JunkBlockSize - jd;
                return;
            }

            long fullSize = _wiiPtn || !_isWii ? _fsInfo.FsSize : this.ImageInfo.ImageSize;
            _junkLength = _wiiPtn ? FsSize : Size;
            _junkFsOffset = _wiiPtn ? FsOffset : ImageOffset;
            long junkDiff = _junkFsOffset % NJunk.JunkBlockSize; //move to the start of a junk block
            if (junkDiff != 0)
            {
                _junkFsOffset -= junkDiff;
                _junkLength += junkDiff;
            }

            junkDiff = (_junkFsOffset + _junkLength) % NJunk.JunkBlockSize; //move to the start of a junk block
            if (junkDiff != 0)
                _junkLength += NJunk.JunkBlockSize - junkDiff;

            if (_isWii && !_wiiPtn && !_hasJunk) //wii disc, but not a WipePartition
                fullSize = 0; //offset < 0xF800000 then junk is zeros

            byte[] junkId = _wiiPtn || !_isWii ? _fsInfo.JunkId : (_fsInfo != null && Type == AreaType.Other && _fsInfo.Type == PartitionType.Game ? _fsInfo.JunkId : Encoding.ASCII.GetBytes(_discId));

            int discNo = _wiiPtn ? _fsInfo.DiscNo : _discNo;
            long startOffset = _fsInfo?.JunkStartFsOffset ?? 0;

            Parallel.For(0, (int)(_junkLength / NJunk.JunkBlockSize), i =>
                NJunk.Fill(junkId, discNo, startOffset, fullSize, _junkFsOffset + (i * (long)NJunk.JunkBlockSize), _junk[i]));
        }

        public void Write(int fsOffset, Stream fromStream, int size) => this.Buffer.WriteFsFromStream(fsOffset, fromStream, size);

        public void WriteBytes(int fsOffset, byte[] bytes, int offset, int size) => this.Buffer.WriteFs(bytes, offset, size, 0, size, fsOffset, size);

        public void Read(int fsOffset, int size, Stream toStream) => this.Buffer.ReadFsToStream(fsOffset, toStream, size);
        public byte[] ReadBytes(int fsOffset, int size) => this.Buffer.ReadFsBytes(fsOffset, size);

    }

}
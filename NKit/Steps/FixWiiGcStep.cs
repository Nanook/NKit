using Nanook.NKit.Dats;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    internal class FixWiiGcStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outFilename;
        private bool _isDisposed;
        private string _outName;
        private string _outExt;

        private ChecksumStream _outStream;
        private string _fn;
        private FixData _fixData;
        private DatManager _datManager;
        private byte[] _origHeader;
        private ImageHeader _hdr;
        private List<CrcPart> _headers;
        //private List<FixPartition> _channels;
        private long _imageSize;
        private bool _isWii;
        private ILogScope _log;
        // Cache of recovery update-partition file byte CRCs, keyed by filename. The brute-force
        // combines the file's real byte CRC (not the identity CRC on FixPartition.Crc); computing
        // it once per file avoids re-reading large (100+ MiB) recovery files across candidates.
        private readonly Dictionary<string, uint> _recoveryFileCrcCache = new Dictionary<string, uint>();

        internal override bool ContractReqPatch => true;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => true;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepFixWiiGc;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal FixWiiGcStep(IStepContextConstruct context)
        {
            _outName = context.SourceImageName;
            _outExt = context.StepConfig;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;

            //if (_context.SystemType == SystemType.GameCube)
            //    _fix = new FixGeneric(this.OutTemp);
            //else
            if (_context.SystemType == SystemType.GameCube || _context.SystemType == SystemType.Wii)
                initFix(base.OutStream, _context.SystemType == SystemType.Wii, _outFilename, _context.DatManager, _context.Settings.FixData<FixData>(), _context.HeaderData, _context.ImageSize, _context.Log);
            else
                throw new HandledException($"Fix is not supported for System {_context.SystemType}");
        }

        public void initFix(ChecksumStream outStream, bool isWii, string fn, DatManager datManager, FixData fixData, byte[] hdr, long imageSize, ILogScope log)
        {
            _log = log;
            _isWii = isWii;
            _hdr = new ImageHeader(hdr, _isWii);
            if (_hdr.IsDatel)
                throw new HandledException("Datel image detected, Fix aborted");

            _fn = fn;
            _datManager = datManager;
            _imageSize = imageSize;
            _fixData = fixData;
            _outStream = outStream;
            _headers = new List<CrcPart>();
        }

        public override void Process(ISection section)
        {
            base.Process(section);
            if (section.ImageOffset == 0)
            {
                base.OutStream.NewPart(_outName, _outExt, true);
                _origHeader = section.Decrypted.Read(0, (int)section.Size); //header written out
            }
            _outStream.Write(section.Encrypted, 0, (int)section.Size);

        }
        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            try
            {
                Scan scan = this.Context.Scan;
                uint crc = this.Context.Scan.Crc;
                long size = this.Context.Scan.Size;

                _hdr.Update(_hdr.Data, _isWii); //don't clone as hdr is modified when processing

                if (_isWii)
                {
                    buildHeaders(scan);
                    applyIdBasedFixes();
                }

                DatItem match = _datManager?.FindImageMatch(scan.Crc, scan.Size);

                byte[] fixHeader = null;

                if (_isWii)
                {
                    FixPartition fixPartition = null;

                    if (!_hdr.HasUpdatePartition)
                        _hdr.AddPartitionPlaceHolder(new PartitionInfo(PartitionType.Update, WiiConsts.WiiDefaultUpdatePtnOffset, 0, 0) { IsPlaceholder = true }); //ensure update is first

                    if (match == null)
                    {
                        // No fast dat match — fall back to the slow region/header/update-partition
                        // brute-force. Report it so the pause is visible (real-time in the dynamic
                        // console, cached otherwise) rather than looking like a hang.
                        _log.Info(() => "Brute Forcing Update Partition...");
                        match = bruteForceMatch(out fixHeader, out fixPartition);
                    }


                    if (match == null)
                        fixHeader = _origHeader;

                    if (fixHeader != null && !fixHeader.Equals(0, _origHeader, 0, _origHeader.Length))
                    {
                        _outStream.Seek(0, SeekOrigin.Begin);
                        _outStream.Write(fixHeader, 0, fixHeader.Length);
                        if (fixHeader.ReadUInt32B(WiiConsts.WiiDiscHdrRgnOffset) != _origHeader.ReadUInt32B(WiiConsts.WiiDiscHdrRgnOffset))
                            _log.Info(() => $"Region changed from {(Region)fixHeader.ReadUInt32B(WiiConsts.WiiDiscHdrRgnOffset)} to {(Region)_origHeader.ReadUInt32B(WiiConsts.WiiDiscHdrRgnOffset)}");
                        if (!fixHeader.Read(WiiConsts.WiiDiscHdrRgn2Offset, WiiConsts.WiiDiscHdrRgn2Size).Equals(0, _origHeader.Read(WiiConsts.WiiDiscHdrRgn2Offset, WiiConsts.WiiDiscHdrRgn2Size), 0, WiiConsts.WiiDiscHdrRgn2Size))
                            _log.Info(() => $"Age ratings corrected");
                    }
                    if (fixPartition != null)
                    {
                        if (fixPartition.Filename != null)
                        {
                            _outStream.Seek((fixHeader ?? _origHeader).Length, SeekOrigin.Begin);
                            _log.Info(() => $"Inserted update partition [{fixPartition.DisplayName}]");
                            _outStream.Seek(WiiConsts.WiiDefaultUpdatePtnOffset, SeekOrigin.Begin);
                            withRecoveryData(fixPartition, s => s.CopyTo(_outStream));
                            ByteStream.Zeros.Copy(_outStream, WiiConsts.WiiDefaultDataPtnOffset - _outStream.Position);
                        }
                        else
                        {
                            _log.Info(() => $"Missing update partition [*_{fixPartition.Crc:X8}] - required to match [{match.Name}]");
                            ImageHeader ih = new ImageHeader(fixHeader, _isWii);
                            ih.RemoveUpdatePartition(WiiConsts.WiiDefaultUpdatePtnOffset);
                            _outStream.Position = 0;
                            _outStream.Write(fixHeader, 0, fixHeader.Length);
                            crc = Crc.Compute(fixHeader);
                            for (int i = 1; i < _headers.Count; i++)
                                crc = ~Crc.Combine(~crc, ~_headers[i].Crc, _headers[i].Size);
                            match = null;
                        }
                    }
                }

                bool a = false;
                bool b = false;
                bool c = false;
                foreach (ScanArea area in scan.Areas)
                {
                    if (area.FsInfo is Nintendo.WiiGc.FileSystemInfo fsi)
                    {
                        if (fsi.FilesReordered)
                            a = true;
                        else if (fsi.FilesMoved)
                            b = true;
                        if (fsi.MainDolDupeRemoved)
                            c = true;
                    }
                }
                //only log once per image
                if (a)
                    _log.Info(() => $"Files reordered");
                if (b)
                    _log.Info(() => $"File positions moved");
                if (c)
                    _log.Info(() => $"Duplicate main.dol removed");


                if (match != null)
                {
                    this.Context.Result.ResultCrc = match.Bins[0].Checksums.Crc; //invalidates the scan
                    this.Context.Result.ResultSize = match.Size; //invalidates the scan
                    this.Context.Result.MatchedDatItem = match;
                }
                else
                    this.Context.Result.ResultCrc = scan.Crc; //invalidates the scan
                this.Context.Result.ResultSize = scan.Size; //invalidates the scan
            }
            finally
            {
                //_outStream.Close();
            }
            base.ProcessResults();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    //if (_fix != null)
                    //    _fix.Dispose();
                    //_fix = null;
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void buildHeaders(Scan scan)
        {
            CrcPart cp;
            for (int i = 0; i < scan.Areas.Count; i++)
            {
                if (scan.Areas[i].Type == AreaType.ImageHeader)
                    _headers.Add(cp = new CrcPart() { Crc = scan.Areas[i].Crc, Size = scan.Areas[i].Size, PType = "Header" });
                else if (scan.Areas[i].Type == AreaType.PartitionHeader)
                {
                    _headers.Add(cp = new CrcPart() { Crc = scan.Areas[i].Crc, Size = scan.Areas[i].Size, Type = scan.Areas[i].Type, PType = (string)scan.Areas[i].AreaInfo.Properties["PartitionType"] });
                    if (i + 1 < scan.Areas.Count && scan.Areas[i + 1].Type == AreaType.FileSystem)
                    {
                        i++;
                        cp.Id = (string)scan.Areas[i].AreaInfo.Properties["ID"];
                        cp.Crc = ~Crc.Combine(~cp.Crc, ~scan.Areas[i].Crc, scan.Areas[i].Size);
                        cp.Size += scan.Areas[i].Size;
                    }
                    if (i + 1 < scan.Areas.Count && scan.Areas[i + 1].Type == AreaType.Other)
                    {
                        i++;
                        cp.Crc = ~Crc.Combine(~cp.Crc, ~scan.Areas[i].Crc, scan.Areas[i].Size);
                        cp.Size += scan.Areas[i].Size;
                    }
                }
                else if (i == 1 && scan.Areas[i].Type == AreaType.Other) //missing update
                {
                    _headers.Add(cp = new CrcPart() { Crc = scan.Areas[i].Crc, Size = scan.Areas[i].Size, Type = scan.Areas[i].Type, PType = "NoUpdate" });
                    if (i + 1 < scan.Areas.Count && scan.Areas[i + 1].Type == AreaType.Other)
                    {
                        i++;
                        cp.Crc = ~Crc.Combine(~cp.Crc, ~scan.Areas[i].Crc, scan.Areas[i].Size);
                        cp.Size += scan.Areas[i].Size;
                    }
                }
            }
        }

        private DatItem bruteForceMatch(out byte[] fixHeader, out FixPartition fixPart)
        {
            DatItem match;
            object lck = new object();
            FixPartition fixPrt;
            byte[] fixHdr;
            fixHeader = null;
            fixPart = null;

            if ((match = findMatch(-1, null, out fixHdr, out fixPrt)) == null)
            {
                foreach (Region r in new[] { Region.Japan, Region.Usa, Region.Pal, Region.Korea }) //test regions only
                {
                    if ((match = findMatch((int)r, null, out fixHdr, out fixPrt)) != null)
                        break;
                }
                ;

                if (match == null && _fixData?.RegionData != null)
                {
                    Parallel.ForEach(_fixData.RegionData, r => //test regions and advisors - last ditch attempt - slow - can cause false positives too :-(
                    {
                        if (match == null)
                        {
                            DatItem mR;
                            if ((mR = findMatch(r.Value, r.Key, out byte[] fixHdrR, out FixPartition fixPrtR)) != null)
                            {
                                lock (lck)
                                {
                                    fixHdr = fixHdrR;
                                    fixPrt = fixPrtR;
                                    match = mR;
                                }
                            }
                        }
                    });
                }
            }
            if (match != null) //has match
            {
                if (!_origHeader.Equals(0, fixHdr, 0, _origHeader.Length))
                    fixHeader = fixHdr;
                if (fixPrt != null)
                    fixPart = fixPrt;
            }

            return match;
        }

        // Open a recovery partition's DATA as a forward-readable stream and hand it to the callback.
        // The data is either a plain on-disk file (upd.InnerEntryName == null) or a single entry
        // inside a streamable archive (upd.Filename = archive path, upd.InnerEntryName = entry name,
        // per the Wii archive fix-file convention). Uses the same scan/read primitives as the
        // DatManager. Returns false when the data can't be opened.
        private bool withRecoveryData(FixPartition upd, Action<Stream> read)
        {
            if (upd?.Filename == null || !File.Exists(upd.Filename))
                return false;

            if (upd.InnerEntryName == null)
            {
                using (FileStream fs = File.OpenRead(upd.Filename))
                    read(fs);
                return true;
            }

            FileMask mask = FileMask.CreateLocalMask($"{upd.Filename}//{upd.InnerEntryName}", false);
            FileItem entry = SourceFileSystem.GetLocalArchiveFiles(mask, false, _log, null)
                .FirstOrDefault(a => string.Equals(a.FileName, upd.InnerEntryName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return false;

            using (SourceFileSystemReader rdr = SourceFileSystem.CreateReader(entry, _log, null))
            {
                Stream s = rdr.OpenRead(entry);
                if (s == null)
                    return false;
                read(s);
            }
            return true;
        }

        // CRC32 of a recovery update-partition's actual bytes (the on-disc update partition), cached
        // by filename. Returns 0 when there is no backing file (fileless redumpUpdateCrcs
        // placeholder) — such candidates cannot be reproduced/inserted and are skipped.
        private uint recoveryFileCrc(FixPartition upd)
        {
            if (upd?.Filename == null || !File.Exists(upd.Filename))
                return 0;
            if (_recoveryFileCrcCache.TryGetValue(upd.Filename, out uint cached))
                return cached;

            uint crc = 0;
            bool ok = withRecoveryData(upd, s =>
            {
                Crc c = new Crc();
                byte[] buf = new byte[0x100000];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    c.Sum(buf, 0, n);
                crc = c.Value;
            });
            if (!ok)
                return 0;
            _recoveryFileCrcCache[upd.Filename] = crc;
            return crc;
        }

        private DatItem findMatch(int region, byte[] regionData, out byte[] fixHeader, out FixPartition fixUpdate)
        {
            DatItem match = null;
            fixHeader = null;
            fixUpdate = null;
            List<uint> hdrCrcs = new List<uint>();
            List<byte[]> hdrHdr = new List<byte[]>();
            hdrHdr.Add((byte[])_origHeader.Clone());
            if (!_origHeader.Equals(0, _hdr.Data, 0, _hdr.Data.Length))
                hdrHdr.Add((byte[])_hdr.Data.Clone());

            foreach (byte[] hdr in hdrHdr)
            {
                if (region != -1)
                {
                    hdr.WriteUInt32B(WiiConsts.WiiDiscHdrRgnOffset, (uint)region);
                    if (regionData != null)
                        hdr.Write(WiiConsts.WiiDiscHdrRgn2Offset, regionData);
                }
                hdrCrcs.Add(NKit.Crc.Compute(hdr));
            }

            if (_fixData?.WiiUpdatePartitions != null)
            {
                for (int h = 0; h < hdrCrcs.Count; h++)
                {
                    foreach (FixPartition upd in new[] { (FixPartition)null }.Concat(_fixData.WiiUpdatePartitions))
                    {
                        uint crc = hdrCrcs[h];
                        long size = hdrHdr[h].Length;
                        for (int i = 1; i < _headers.Count; i++)
                        {
                            // Slot 1 with a candidate update: the source presents the missing-update
                            // region as ONE null gap (_headers[1].Size covers 0x50000..0xF800000).
                            // On disc the fix writes the update partition (upd.Length bytes) then
                            // zero-fills to the data-partition offset. So the region CRC must be
                            // combined in two parts — the update data over its REAL length, then the
                            // trailing null padding over the remainder — otherwise the candidate CRC
                            // is shifted by the whole gap and can never reproduce the redump CRC.
                            if (i == 1 && upd != null)
                            {
                                // Combine the candidate update over the update slot. IMPORTANT: use
                                // the CRC of the recovery FILE's actual on-disc bytes, not
                                // FixPartition.Crc (which is the update-IDENTITY CRC parsed from the
                                // filename / NKit header 0x218, used only to MATCH a removed update
                                // to its recovery file — it is NOT the CRC of the file bytes). The
                                // brute-force reproduces the real disc, which contains the file
                                // bytes, so it must combine their real CRC. Then the trailing null
                                // padding fills the rest of the update region to the data offset.
                                uint updBytesCrc = recoveryFileCrc(upd);
                                if (updBytesCrc == 0) // no file bytes available (fileless placeholder) — cannot reproduce
                                    break;
                                crc = ~Crc.Combine(~crc, ~updBytesCrc, upd.Length);
                                long pad = _headers[i].Size - upd.Length;
                                if (pad > 0)
                                    crc = ~Crc.Combine(~crc, ~Crc.ComputeZeros(pad), pad);
                            }
                            else
                                crc = ~Crc.Combine(~crc, ~_headers[i].Crc, _headers[i].Size);
                            size += _headers[i].Size;
                        }

                        match = _datManager?.FindImageMatch(crc, size);

                        if (match != null)
                        {
                            fixUpdate = upd;
                            fixHeader = hdrHdr[h];
                            break;
                        }
                    }
                }
            }
            return match;
        }

        private bool applyIdBasedFixes()
        {
            string hdrId = _hdr.Data.ReadString(0, 4);
            bool changed = false;
            try
            {
                if (hdrId == "010E" && _headers.FirstOrDefault(a => a.Type == AreaType.PartitionHeader && a.PType == "Game" && a.Id.StartsWith("RELS")) != null)
                {
                    _log.Info(() => $"Disc ID swapped from {_hdr.Id} to 4{_hdr.Id.Substring(1)}");
                    _hdr.Data[0] = (byte)'4';
                    changed = true;
                }

                //if (inStream.Id.StartsWith("RSB") && partHdr.Id.StartsWith("HA8")) //Super Smash Brothers Brawl
                if (hdrId.StartsWith("RSB")) //Super Smash Brothers Brawl
                {
                    foreach (PartitionInfo part in _hdr.Partitions.Where(a => a.Type != PartitionType.Update && a.Type != PartitionType.Game))
                    {
                        if (part.Table == 0)
                        {
                            part.Table = 1; //WBM swaps this for some reason
                            _log.Info(() => $"Partition {hdrId} moved from table 0 to 1 (WBM bug)");
                            changed = true;
                        }
                    }
                    if (changed)
                        _hdr.UpdateRepair();
                }
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "FixWii: applyFixes");
            }
            return changed;
        }

        private class CrcPart
        {
            public uint Crc;
            //public uint OriginalCrc;
            public long Offset;
            public long Size;
            public AreaType Type;
            public string Id;
            public byte[] Hdr;
            public string PType;
            public FixPartition FixPtn;
            public PartitionInfo PtnInfo;
        }

    }
}
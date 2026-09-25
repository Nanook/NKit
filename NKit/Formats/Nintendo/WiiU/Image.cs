using Nanook.NKit.Container;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class Image : IImage
    {
        private readonly IAsIso _iso;
        private readonly BufferStream _stream;
        private readonly IImageContext _context;
        private readonly ImageInfo _info;
        private readonly ImageHeader _header;
        private long _position;
        private List<IImageArea> _areas;
        private FileSystemInfo _fsInfo;
        private ContentHeader _currentCntHeader;
        private int _areaNumber;
        private bool _skipped;
        private bool _isWiped;

        private readonly BufferPreProcessor _preProcessor;
        private AreaInfo _area;
        private AreaInfo _nextArea;
        private bool _isIdx;
        private MediaType _mediaType;
        private byte[] _fstReadBuffer;
        private int _fstReadBufferOffset;
        private int _currentPartitionIndex = -1;

        public Image(IImageContext context, IAsIso input, BufferStream isoStream)
        {
            _areaNumber = -1;
            _context = context;
            _iso = input;
            _stream = isoStream;
            _info = new ImageInfo();
            this.SystemType = SystemType.WiiU;
            this.Size = _iso.Size;

            _isIdx = _iso.Format == ContainerType.TmdApp;

            if (_isIdx)
            {
                _mediaType = MediaType.CDN;
                if (_context.SourceFile?.IndexFile != null)
                    _mediaType = _context.SourceFile.IndexFile.FileName.ToLower().StartsWith("title") ? MediaType.APP : MediaType.CDN;

                _header = new ImageHeader(null);
                _info.ContainerType = ContainerType.TmdApp;
                _info.IsFullImage = false;
            }
            else
            {
                _mediaType = MediaType.WUD;
                byte[] header = new byte[WiiUConsts.DiscContentsOffset];
                _stream.Read(header, 0, -header.Length); //read and rewind
                _header = new ImageHeader(header);

                _info.ContainerType = input is WuxAsIso ? ContainerType.Wux : ContainerType.Iso;
                _info.IsFullImage = _info.ContainerType == ContainerType.Wud || _info.ContainerType == ContainerType.Iso;
            }

            _context.Header = _header;
            _context.ImageInfo = _info;
            ((ImageInfo)_context.ImageInfo).SystemType = this.SystemType;

            _info.ImageSize = this.Size;
            _info.Type = this.Type;
            _info.MediaType = _mediaType;
            _info.SourceSupportsEncryption = true;
            if (_isIdx && _context.SourceFile?.IndexFile != null)
                _info.Tracks = _context.SourceFile.IndexFile.Items;

            _context.Scan = new Scan(this.SystemType, context.SourceFile.Name);

            _position = 0;

            _currentCntHeader = null;
            _preProcessor = new BufferPreProcessor(_context);
        }

        public void Setup()
        {
            byte[] key = _context.SourceFile.Key; //may already be set if not step 1 - datastore can set this also

            if (key == null)
                key = _context.Settings.GetKey(_context.SourceFile.CleanName, _context.SourceFile.BasePath);

            int idxFstSize = 0;
            if (_isIdx)
            {
                IndexFile idx = _context.SourceFile?.IndexFile;
                if (idx == null)
                    throw new HandledException("WiiU TMD Index file not found or could not be reconstructed");

                idxFstSize = (int)_context.SourceFile.ImageFiles[0].Size;
                byte[] idxFst = new byte[Math.Min(0x8000, idxFstSize)]; //0x8000 max as it's only used to test keys
                //_stream.Read(idxFst, 0, -idxFst.Length); //read and rewind
                long oldPos = _stream.Position;
                _stream.Read(idxFst, 0, idxFst.Length); //read and rewind
                _stream.Position = oldPos;
                SiData si = new SiData(key, idx, idxFst) { AppIndex = 0 };

                _header.SiData.Add(si);

                si.Complete(_header);
                // Ensure si.KeyTitle is populated from SourceFile.Key (decrypted Title Key).
                // Reconstructed DataStore images provide the decrypted key in SourceFile.Key.
                // We prefer it over what si.Complete() might have derived from the Ticket using Common Keys.
                if (key != null && key.Length == 16)
                    si.KeyTitle = (byte[])key.Clone();

                _isWiped = si.TmdInfo.IsWiped;

                key ??= si.Key;

                if (si.TmdInfo.Version == 0)
                    throw new HandledException($"TMD version 0 (vWii - {si.TmdInfo.TitleId:X16}) is not currently support as it does not have a WiiU filesystem");

                //if (key == null)
                //    throw new HandledException("No Key available");

                //_iso.Read((int)(Math.Min(WiiUConsts.DefaultSectorSize, si.TmdInfo.Content[0].Size - pos)), _cache);
                _header.Update(0, si);
                _header.EncryptedNoKeyMode = key == null && _context.Settings.AllKeys.Length == 0; //don't attempt to decrypt anything. Dumb read
                // For CDN (TmdApp) format the AllKeys disc-key cache is irrelevant — CDN uses
                // a per-title key from the cetk ticket. If that key wasn't found, there is nothing
                // to brute-force and we must treat it as no-key mode.
                if (_isIdx && si.KeyTitle == null)
                    _header.EncryptedNoKeyMode = true;

                // For CDN content that is a raw system binary (e.g. [BIOS] firmware blobs) the
                // first content does NOT begin with an FST header. Detect this early by probing
                // the decrypted first 16 bytes; if the content doesn't produce "FST" after
                // decryption treat it as raw passthrough (EncryptedNoKeyMode) so the encrypted
                // bytes are stored verbatim rather than crashing inside Image.Read.
                if (!_header.EncryptedNoKeyMode && si.KeyTitle != null && idxFst.Length >= 16)
                {
                    bool isPlainFst = Encoding.ASCII.GetString(idxFst, 0, 3) == "FST";
                    if (!isPlainFst)
                    {
                        byte[] probeKey = WiiUSecurityContext.GetActiveEncryptionKey(_fsInfo?.Type ?? PartitionType.Game, true, null, si.KeyTitle);
                        byte[] probeDec = WiiUSecurity.DecryptFst(idxFst, null, 16, probeKey);
                        if (Encoding.ASCII.GetString(probeDec, 0, 3) != "FST")
                            _header.EncryptedNoKeyMode = true; // raw system binary — store as-is
                    }
                }

                if (!_header.EncryptedNoKeyMode)
                {
                    _areas = _header.Partitions.OrderBy(a => a.ImageOffset).Select(a => (IImageArea)new ImageArea(a.ImageOffset, AreaType.PartitionHeader)).ToList();
                    _areas.Add(new ImageArea(_iso.Size, AreaType.None));
                    _area = AreaInfo.NextArea(-1, -1, -1, this.Size, 0, AreaType.FstBlock, null, ++_areaNumber);
                    _area = setAreaBlockInfo(_area, null, 0, idxFstSize);
                }
                else
                {
                    //_areas = _context.SourceFile.IndexFile.Items.Select(a => new ImageArea(a.ImageOffset, AreaType.RawKeyMissing)).ToList<IImageArea>();
                    _areas = new List<IImageArea>();
                    long l = 0;
                    foreach (Content c in si.Contents)
                    {
                        _areas.Add(new ImageArea(l, AreaType.RawKeyMissing));
                        l += _context.SourceFile.IndexFile.Items[c.Index].Size;
                    }
                    _areas.Add(new ImageArea(l, AreaType.None));
                    _area = AreaInfo.NextArea(-1, -1, -1, this.Size, 0, AreaType.RawKeyMissing, null, ++_areaNumber);
                    _area = setAreaBlockInfo(_area, null, 0, idxFstSize);
                    //_area = AreaInfo.NextArea(0, -1, -1, _info.ImageSize, _info.ImageSize, AreaType.None, _areas, _area.AreaNo);
                }

                _nextArea = _area;
            }
            else
            {
                _area = AreaInfo.NextArea(-1, -1, -1, this.Size, 0, AreaType.ImageHeader, null, ++_areaNumber);
                _area = setAreaBlockInfo(_area, null, 0, idxFstSize);
                _nextArea = AreaInfo.NextArea(0, -1, -1, this.Size, WiiUConsts.DiscContentsOffset, AreaType.PartitionTable, null, ++_areaNumber);
            }

            if (key != null)
                _context.SourceFile.Key = key; //SourceFile.Key may have the key already if this isn't the first task running
            _header.InitialKey = key;
            _header.Key = key;

            if (_isWiped)
            {
                byte[] iv = new byte[0x10];
                iv.WriteUInt64B(0, WiiGc.WiiConsts.NKitWipeIV);
                //Header.Key = WiiGc.WiiConsts.NKitWipeDiscKey;
                _header.KeyCommon = _header.KeyCommonDev = WiiGc.WiiConsts.NKitWipeCommonKey;
                _header.SiData[0].Key = _header.Key;
                _header.SiData[0].IvTitle = iv;
                _header.SiData[0].KeyTitle = WiiGc.WiiConsts.NKitWipeTitleKey;
            }

            if (_isIdx)
                _fsInfo = new FileSystemInfo(0, Size, _header, _header.SiData[0], idxFstSize);

            _info.SourceAreas = _areas?.ToArray();
            _info.IsFolderIndex = false;

            // [In] Detail: reader identity + resolved geometry (once per image).
            ILogScope inScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
            _info.SectionLog = inScope; // wire the per-section Trace log for the SectionProcessor
            if (inScope != null && inScope.IsEnabled(LogLevel.Detail))
                inScope.Log(LogLevel.Detail,
                    $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.WiiU)}{this.SystemType} media {_mediaType} size 0x{this.Size:X} areas {_areas?.Count ?? 0}");

            _info.StepImageInfo = new StepsImageInfo()
            {
                CustomChecksums = _iso.CustomChecksums(),
                Checksums = _iso.Checksums?.Clone() ?? new Checksums(),
                IsIndex = _context.SourceFile.IndexFile != null,
                ReqPatch = false,
                Size = this.Size
            };
        }

        public ImageType Type => ImageType.WiiU;
        public long Size { get; }

        public long CurrentAreaEndImageOffset => _nextArea?.ImageOffset ?? this.Size;
        public int SectionSize => _header.H2BlockSize; //just 1 H2 block. Too much memory for them all

        private int readBuf(byte[] b, ref int pos, int len, Stream stream = null)
        {
            int r = (stream ?? _stream).Read(b, pos, len);
            pos += r;
            return r;
        }

        public AreaType Read(IBuffer buffer, out IFileSystemInfo fsInfo)
        {
            byte[] buf = buffer.Decrypted;
            int maxSection = _area.SectionSize > 0 && _area.SectionSize < buffer.Decrypted.Length ? _area.SectionSize : buffer.Decrypted.Length;
            int length = (int)Math.Min(maxSection, _nextArea.ImageOffset - _position); //0 on first call - is updated

            try
            {
                int pos = 0;

                //check for WipePartition markers and grab WipePartition info to make reading seamless
                if (_position == _nextArea.ImageOffset)
                {
                    if (_isIdx && _position == 0) //read fst
                        readBuf(buf, ref pos, (int)_header.SiData[0].TmdInfo.Content[0].Size);

                    _area = setAreaBlockInfo(_nextArea, _fsInfo, _area.FsOffset, _position + pos);

                    // Ensure the source areas list reflects discovered areas so other
                    // components (eg. DataStoreWiiUFormatter) can consult it while
                    // processing sections.
                    ensureAreaExists(_area.ImageOffset, _area.Type);

                    long nextArea = -1; //break the image up in to it's main areas
                    AreaType nextAreaType = AreaType.None;

                    if (_area.Type == AreaType.PartitionTable)
                    {
                        readBuf(buf, ref pos, _header.BlockSize);
                        bool isEncrypted = buf.ReadUInt32B(0x0) != WiiUConsts.PartitionHeaderId;
                        if (!isEncrypted)
                            _area.SetSecurity(false, _area.IsEncryptionSupported, _area.HasSecurity); //force encryption to false - defaults to true for fst
                        else
                        {
                            _info.SourceHasEncryption = _info.SourceHasEncryptedHashes = true;

                            //test if wiped and set keys
                            byte[] b = WiiUSecurity.DecryptPartitionTable(buf, null, buf.Length, WiiGc.WiiConsts.NKitWipeDiscKey);
                            _isWiped = b.ReadUInt32B(0) == 0xcca6e67b;
                            if (_isWiped)
                            {
                                // A wiped disc's partition table is encrypted with the NKit wipe disc
                                // key, which is NOT in any real key set. Adopt it as the image key so
                                // the decrypt below succeeds instead of brute-forcing the key cache
                                // (which never contains the wipe key) and throwing.
                                _header.Key = WiiGc.WiiConsts.NKitWipeDiscKey;
                                _context.SourceFile.Key = _header.Key;
                                _header.KeyCommon = _header.KeyCommonDev = WiiGc.WiiConsts.NKitWipeCommonKey;
                            }
                        }

                        nextArea = _position + _header.BlockSize;
                        nextAreaType = AreaType.PartitionHeader;

                        if (isEncrypted)
                        {
                            if (_header.Key == null && _context.Settings.AllKeys.Length == 0)
                            {
                                _header.EncryptedNoKeyMode = true; //don't attempt to decrypt anything. Dumb read
                                _areas = new List<IImageArea>() { new ImageArea(_area.ImageOffset, AreaType.RawKeyMissing), new ImageArea(_iso.Size, AreaType.None) };
                                nextArea = _info.ImageSize;
                                nextAreaType = AreaType.None;
                                _area = AreaInfo.NextArea(0, -1, -1, _info.ImageSize, _info.ImageSize, AreaType.None, _areas, _area.AreaNo);
                            }
                            else
                            {
                                ILogScope keyScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                                byte[] dec = _header.Key == null ? null : WiiUSecurity.DecryptPartitionTable(buf, null, _header.BlockSize, _header.Key);
                                bool initialOk = dec != null && dec.ReadUInt32B(0x0) == WiiUConsts.PartitionHeaderId;
                                if (keyScope != null && keyScope.IsEnabled(LogLevel.Detail))
                                    keyScope.Log(LogLevel.Detail,
                                        $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Security)}key: initial {(_header.Key == null ? "none" : Nanook.NKit.LogScopes.MaskKey(_header.Key))}"
                                        + $" -> {(initialOk ? "valid" : "invalid")}"
                                        + (initialOk ? "" : $"; brute-forcing {_context.Settings.AllKeys.Length} cached key{(_context.Settings.AllKeys.Length == 1 ? "" : "s")}"));
                                if (dec == null || dec.ReadUInt32B(0x0) != WiiUConsts.PartitionHeaderId)
                                {
                                    // Try all keys in the cache if the initial key fails
                                    bool found = false;
                                    int tried = 0;
                                    System.Diagnostics.Stopwatch keySw = System.Diagnostics.Stopwatch.StartNew();
                                    // NOTE: no per-candidate log line — a key cache can hold 100k+
                                    // keys, so logging each attempt floods even Trace. The Detail
                                    // summary below (matched-at / no-match + count + timing) is the
                                    // useful signal.
                                    foreach (byte[] candidateKey in _context.Settings.AllKeys)
                                    {
                                        tried++;
                                        dec = WiiUSecurity.DecryptPartitionTable(buf, null, _header.BlockSize, candidateKey);
                                        if (dec.ReadUInt32B(0x0) == WiiUConsts.PartitionHeaderId)
                                        {
                                            _header.Key = candidateKey;
                                            _context.SourceFile.Key = candidateKey;
                                            found = true;
                                            break;
                                        }
                                    }
                                    keySw.Stop();
                                    if (keyScope != null && keyScope.IsEnabled(LogLevel.Detail))
                                        keyScope.Log(found ? LogLevel.Detail : LogLevel.Warning,
                                            found
                                                ? $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Security)}key: matched {Nanook.NKit.LogScopes.MaskKey(_header.Key)} at candidate {tried}/{_context.Settings.AllKeys.Length} in {keySw.ElapsedMilliseconds}ms"
                                                : $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Security)}key: no match after {tried}/{_context.Settings.AllKeys.Length} candidate{(tried == 1 ? "" : "s")} in {keySw.ElapsedMilliseconds}ms");
                                    if (!found)
                                        throw new HandledException("The key provided failed to decrypt the Partition Table");
                                }
                                _header.Update(dec);
                            }
                        }
                        else
                            _header.Update(buf.Read(0, _header.BlockSize));
                        if (!_header.EncryptedNoKeyMode)
                        {
                            _areas = _header.Partitions.OrderBy(a => a.ImageOffset).Select(a => (IImageArea)new ImageArea(a.ImageOffset, AreaType.PartitionHeader)).ToList();
                            _areas.Add(new ImageArea(_iso.Size, AreaType.None));
                            if (_areas.Count > 1)
                            {
                                long ptEnd = _position + _header.BlockSize;
                                if (_areas[0].ImageOffset > ptEnd)
                                {
                                    // Gap between partition table and first partition header
                                    ensureAreaExists(ptEnd, AreaType.Other);
                                    nextArea = ptEnd;
                                    nextAreaType = AreaType.Other;
                                }
                                else
                                    nextArea = _areas[0].ImageOffset;
                            }
                        }
                        _info.SourceAreas = _areas?.ToArray();
                    }
                    else if (_area.Type == AreaType.PartitionHeader)
                    {
                        readBuf(buf, ref pos, _header.BlockSize);
                        _fsInfo = new FileSystemInfo(_position, Size, _header, buf.Read(0, pos));

                        // Set partition properties on the area immediately so they're available
                        // during dedupe (ProcessSection) — not just in SetScanProperties().
                        PartitionInfo ptn = _header.GetPartition(_position);
                        if (ptn != null && _area.Properties != null)
                        {
                            _currentPartitionIndex++;
                            _area.Properties["PartitionType"] = ptn.Type.ToString();
                            _area.Properties["Partition"] = _currentPartitionIndex;
                        }

                        long fstImageOffset = _position + _fsInfo.FstOffset;
                        if (_fsInfo.FstOffset > pos)
                        {
                            // Gap between volume header and FST (e.g. repeated partition table data on update-only discs)
                            ensureAreaExists(_position + pos, AreaType.Other);
                            ensureAreaExists(fstImageOffset, AreaType.FstBlock);
                            nextArea = _position + pos;
                            nextAreaType = AreaType.Other;
                        }
                        else
                        {
                            nextArea = fstImageOffset;
                            nextAreaType = AreaType.FstBlock;
                        }
                    }
                    else if (_area.Type == AreaType.FstBlock)
                    {
                        // Set partition index on FstBlock area for dedupe routing
                        if (_area.Properties != null && _currentPartitionIndex >= 0)
                            _area.Properties["Partition"] = _currentPartitionIndex;

                        SiData si = _isIdx ? _fsInfo.SiData : _header.GetPartition(_fsInfo.ImageOffset)?.WiiUSiData;
                        byte[] key = WiiUSecurityContext.GetActiveEncryptionKey(_fsInfo.Type, _area.Type, _isIdx, _header.Key, si?.KeyTitle);
                        if (!_isIdx)
                            readBuf(buf, ref pos, _fsInfo.BlockSize);

                        bool isEncypted = buf.ReadString(0, 3) != "FST";
                        if (!isEncypted)
                            _area.SetSecurity(false, _area.IsEncryptionSupported, _area.HasSecurity); //force encryption to false - defaults to true for fst
                        byte[] dec = isEncypted ? WiiUSecurity.DecryptFst(buf, null, Math.Max(pos, _fsInfo.BlockSize), key) : buf.Read(0, _fsInfo.BlockSize); //handle system cdn with < 0x8000 fst
                                                                                                                                                              //if (Encoding.ASCII.GetString(dec, 0, 3) != "FST")
                                                                                                                                                              //    throw new HandledException("The key provided failed to decrypt the FST block");
                                                                                                                                                              //int fstSize;
                                                                                                                                                              //if (_isIdx)
                                                                                                                                                              //    fstSize = (int)_fsInfo.SiData.Contents[0].Size;
                                                                                                                                                              //else
                                                                                                                                                              //    fstSize = /*(int)((dec.ReadUInt32B(0x8) + 1) * 0x20) +*/ _fsInfo.FstSize; //0x8 = content headers + 1 (0x20 record size)
                                                                                                                                                              //if (fstSize > pos)  //get the end pos of the 
                                                                                                                                                              //{
                                                                                                                                                              //    fstSize += fstSize % _fsInfo.BlockSize == 0 ? 0 : _fsInfo.BlockSize - (fstSize % _fsInfo.BlockSize);
                                                                                                                                                              //    readBuf(buf, ref pos, fstSize - _fsInfo.BlockSize);
                                                                                                                                                              //    dec = isEncypted ? WiiUSecurity.DecryptFst(buf, null, fstSize, key) : buf.Read(0, fstSize);
                                                                                                                                                              //}
                                                                                                                                                              //_fsInfo.ProcessBlock(_position, dec, dec.Length, true, _isIdx ? _context.SourceFile.IndexFile : null); //IndexFile is null for discs (Important)

                        //prepopulateAreasFromFst(_fsInfo);

                        //nextArea = _fsInfo.FstBlock.GetContentHeader(_position + pos).ImageOffset;
                        //nextAreaType = AreaType.FileSystem;


                        if (Encoding.ASCII.GetString(dec, 0, 3) != "FST")
                        {
                            if (_isIdx && isEncypted && si != null && si.TmdInfo != null)
                            {
                                byte[] commonKey = si.TmdInfo.IsWiped ? WiiGc.WiiConsts.NKitWipeCommonKey : _header.KeyCommon;
                                byte[] encKey = TmdInfo.GenerateEncryptedKey(si.TmdInfo.TitleId.ToString("X16"), buf.Read(0, Math.Max(pos, _fsInfo.BlockSize)), commonKey);
                                if (encKey != null && encKey.Length == 0x10) // 16 bytes
                                {
                                    string issuer = si.TmdInfo.IsWiped ? WiiUConsts.TicketIssuer.Rot13Words() : WiiUConsts.TicketIssuer;
                                    si.FileTicket = TmdInfo.CreateTicket(si.TmdInfo.TitleId, encKey, issuer);
                                    si.GeneratedTicket = true;
                                    si.Key = encKey;
                                    si.Complete(_header);
                                    key = si.KeyTitle;
                                    _context.SourceFile.Key = key;
                                    dec = WiiUSecurity.DecryptFst(buf, null, Math.Max(pos, _fsInfo.BlockSize), key);
                                }
                            }
                            if (Encoding.ASCII.GetString(dec, 0, 3) != "FST")
                                throw new HandledException("The key provided failed to decrypt the FST block");
                        }
                        int fstSize;
                        if (_isIdx)
                            fstSize = (int)_fsInfo.SiData.Contents[0].Size;
                        else
                            fstSize = /*(int)((dec.ReadUInt32B(0x8) + 1) * 0x20) +*/ _fsInfo.FstSize; //0x8 = content headers + 1 (0x20 record size)
                        int fstEndPos = pos; // tracks the end of FST data for GetContentHeader lookup
                        if (fstSize > pos)  //get the end pos of the 
                        {
                            // Round up to the next 0x8000 boundary for read alignment
                            const int RoundUnit = WiiUConsts.DefaultSectorSize;
                            int paddedFstSize = (fstSize + RoundUnit - 1) / RoundUnit * RoundUnit;

                            if (paddedFstSize <= buf.Length)
                            {
                                // FST fits in the buffer — read directly (no replay needed)
                                readBuf(buf, ref pos, paddedFstSize - pos);
                                fstEndPos = paddedFstSize;
                                dec = isEncypted ? WiiUSecurity.DecryptFst(buf, null, paddedFstSize, key) : buf.Read(0, fstSize);
                                // The read above consumed paddedFstSize bytes to align for AES-CBC
                                // decryption, but the FstBlock area only covers fstSize bytes on disc.
                                // Without a seek-back, the stream position is ahead of _position by
                                // (paddedFstSize - fstSize), and the subsequent sequential read for
                                // the FileSystem area starts at the wrong physical offset, producing
                                // garbage in buffer.Decrypted. Only seek back for plaintext partitions
                                // (isEncypted=false) where the content starts at fstSize, not
                                // paddedFstSize. For encrypted partitions the content is sector-aligned
                                // at paddedFstSize so the stream position is already correct.
                                if (!isEncypted && paddedFstSize > fstSize)
                                    _stream.Position -= paddedFstSize - fstSize;
                            }
                            else
                            {
                                // FST larger than buffer — read into work buffer, seek back for engine replay
                                int originalPos = pos;
                                byte[] workBuf = new byte[paddedFstSize];
                                Array.Copy(buf, 0, workBuf, 0, originalPos);
                                readBuf(workBuf, ref pos, paddedFstSize - originalPos);
                                // Store for replay into subsequent engine buffers; first chunk is already in buf
                                _fstReadBuffer = workBuf;
                                _fstReadBufferOffset = originalPos;
                                fstEndPos = paddedFstSize;
                                pos = originalPos;

                                dec = isEncypted ? WiiUSecurity.DecryptFst(workBuf, null, paddedFstSize, key) : workBuf.Read(0, fstSize);
                            }
                        }
                        _fsInfo.ProcessBlock(_position, dec, dec.Length, true, _isIdx ? _context.SourceFile.IndexFile : null); //IndexFile is null for discs (Important)

                        prepopulateAreasFromFst(_fsInfo);

                        nextArea = _fsInfo.FstBlock.GetContentHeader(_position + fstEndPos).ImageOffset;
                        nextAreaType = AreaType.FileSystem;
                    }
                    else if (_area.Type == AreaType.FileSystem)
                    {
                        // Set partition index on FileSystem area for dedupe routing
                        if (_area.Properties != null && _currentPartitionIndex >= 0)
                            _area.Properties["Partition"] = _currentPartitionIndex;

                        if (_currentCntHeader?.RepeatedApp ?? false)
                        {
                            nextArea = _currentCntHeader.ImageOffset + _currentCntHeader.SizePaddedToBlock;
                            nextAreaType = AreaType.Other; //repeater gap
                        }
                        else
                        {
                            ContentHeader next = _fsInfo.FstBlock.GetNextContentHeader(_position);
                            if (next != null)
                            {
                                nextArea = next.ImageOffset;
                                nextAreaType = AreaType.FileSystem;
                            }
                            else
                                nextAreaType = AreaType.FstBlock;
                        }
                    }
                    else //gap or end of disc
                    {
                        ContentHeader next = (_header.EncryptedNoKeyMode || _fsInfo?.FstBlock == null) ? null : _fsInfo.FstBlock.GetNextContentHeader(_position);
                        if (next != null)
                        {
                            nextArea = next.ImageOffset;
                            nextAreaType = AreaType.FileSystem;
                        }
                        else if (_fsInfo?.FstBlock == null && _fsInfo != null)
                        {
                            // Gap before FstBlock (e.g. repeated data on update-only discs)
                            nextArea = _fsInfo.ImageOffset + _fsInfo.FstOffset;
                            nextAreaType = AreaType.FstBlock;
                        }
                        else
                            nextAreaType = AreaType.None;
                    }

                    _nextArea = AreaInfo.NextArea(_area.ImageOffset, -1, -1, _info.ImageSize, nextArea, nextAreaType, _areas, ++_areaNumber);

                    // [In] [WiiU] Detail: reader's own per-area progress (once per area transition).
                    ILogScope areaScope = _context.Log?.ScopeFor(Nanook.NKit.LogScopes.In);
                    if (areaScope != null && areaScope.IsEnabled(LogLevel.Detail))
                        areaScope.Log(LogLevel.Detail,
                            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.WiiU)}area {_area.AreaNo} [{_area.Type}] offset 0x{_area.ImageOffset:X} size 0x{_nextArea.ImageOffset - _area.ImageOffset:X}{(_area.IsEncrypted ? " enc" : "")}");

                    maxSection = _area.SectionSize > 0 && _area.SectionSize < buffer.Decrypted.Length ? _area.SectionSize : buffer.Decrypted.Length;
                    length = (int)Math.Min(maxSection, _nextArea.ImageOffset - _position);
                }
                else if (_area.Type == AreaType.FstBlock && _fstReadBuffer != null)
                {
                    int toCopy = Math.Min(length - pos, _fstReadBuffer.Length - _fstReadBufferOffset);
                    Array.Copy(_fstReadBuffer, _fstReadBufferOffset, buf, pos, toCopy);
                    pos += toCopy;
                    _fstReadBufferOffset += toCopy;
                    if (_fstReadBufferOffset >= _fstReadBuffer.Length)
                        _fstReadBuffer = null;
                }

                //trunc the end of a repeating section to ensure the next read starts at the begining (the IV needs to restart)
                if (length != 0 && _currentCntHeader != null && _currentCntHeader.RepeatedApp && _area.Type == AreaType.Other)
                {
                    long prg = (_position - _currentCntHeader.ImageOffset) % _currentCntHeader.SizePaddedToBlock;
                    length = (int)Math.Min(length, _currentCntHeader.SizePaddedToBlock - prg); //cause read to be set to 0 and exit loop
                }

                if (length - pos > 0)
                    pos = readBuf(buf, ref pos, length - pos);

                if (length != 0 && pos == 0)
                    throw new HandledException(string.Format("Image.Read - No data read at Position {0} ({1}) - Requested {2}", _position.ToString("X"), _iso.RealPosition.ToString("X"), length.ToString()));

                buffer.ReInitialise(_area, true);

                buffer.Update(_position, _position - _area.ImageOffset, length, _currentCntHeader?.Index ?? -1, _area.IsEncrypted, _skipped);

                _position += buffer.Size;

                if (buffer.Type == AreaType.FileSystem && _fsInfo != null && _fsInfo.Type == PartitionType.Si)
                {
                    if (SetSiInfoInImageHeader(buffer, _currentCntHeader, _header, _fsInfo, false) && _header.SiData.Last().TmdInfo.IsWiped)
                        _header.KeyCommon = _header.KeyCommonDev = WiiGc.WiiConsts.NKitWipeCommonKey;
                }

                fsInfo = _fsInfo;

                skip(); //after providing current data check if we can skip some of the current filesystem

                // Bound the shared cache: release cached blocks below the returned-data mark on
                // both layers — Layer-B (this _stream over the decoder) and, for a pass-through
                // container reading a raw archive entry, Layer-A (the raw source) via IReleasable.
                // Without the Layer-A release the raw-source cache grows to hold the whole image
                // (a WiiU image inside an archive OOM'd here).
                _stream.ReleaseTo(_position);
                (_iso as IReleasable)?.ReleaseTo(_position);

                return _area.Type;
            }
            catch (Exception ex)
            {
                if (ex is HandledException)
                    throw;
                throw new HandledException(ex, $"Image.Read: {ex.Message}");
            }
        }

        // Reposition to a skip target through the stream reads actually flow through (_stream), not
        // _iso directly — seeking _iso alone desyncs it from the BufferStream cursor and delivers
        // wrong bytes at the skipped-to boundary. Skips are always forward.
        private void skipStreamTo(long imageOffset) => _stream.SafeSeek(imageOffset, System.IO.SeekOrigin.Begin);

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
                if ((_area.Type == AreaType.FileSystem && (_fsInfo.InvalidFileSystem || _fsInfo.FileSystem != null)) || (_area.Type == AreaType.Other && _fsInfo?.FstBlock != null))
                {
                    bool toEnd = !getPartitionInfo(skipTo, out io); //end
                    if (toEnd)
                    {
                        _context.SkipType = SkipType.End; //main loop will exit
                        _skipped = true;
                    }
                    else if (io >= _nextArea.ImageOffset) //a;ways hit each section
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
            {
                ContentHeader c = _fsInfo.FstBlock.ContentHeaders.Last(a => a.ImageOffset <= imageOffset);
                if (c.Index != _currentCntHeader.Index)
                    startImageOffset = c.ImageOffset; //same WipePartition, switch content app
                else //skip within current fs
                    startImageOffset = ((startImageOffset - _area.ImageOffset) / this.SectionSize * this.SectionSize) + _area.ImageOffset; //section start
            }

            return true;
        }

        internal static bool SetSiInfoInImageHeader(IBuffer inBuffer, ContentHeader cntHeader, ImageHeader header, FileSystemInfo fsInfo, bool forceDecrypted)
        {
            if (cntHeader == null)
                return false;

            SiData si = header.SiData.FirstOrDefault(a => a.AppIndex == cntHeader.Index);
            if (si == null)
            {
                si = new SiData() { AppIndex = cntHeader.Index };
                header.SiData.Add(si);
            }
            else if (si.IsComplete)
                return true;

            if (fsInfo.Type == PartitionType.Si)
            {
                RangeResult rr = new RangeResult();
                if (inBuffer.AreaInfo.IsEncrypted && !forceDecrypted)
                {
                    WiiUSecurity sec = new WiiUSecurity(header);
                    sec.Populate(cntHeader, si, PartitionType.Si, inBuffer.AreaInfo.Type, inBuffer.Encrypted, inBuffer.Decrypted, inBuffer.Size, cntHeader.HasEncryption, false, inBuffer.AreaOffset);
                    sec.Decrypt();
                }

                foreach (FstFile f in fsInfo.FileSystem.Files)
                {
                    byte[] fileData = null;
                    if (f.Name.ToLower().EndsWith(".cert"))
                    {
                        if (si.FileCert == null)
                            si.FileCert = new byte[f.FsSize];
                        fileData = si.FileCert;
                    }
                    if (f.Name.ToLower().EndsWith(".tik"))
                    {
                        if (si.FileTicket == null)
                            si.FileTicket = new byte[f.FsSize];
                        fileData = si.FileTicket;
                    }
                    if (f.Name.ToLower().EndsWith(".tmd"))
                    {
                        if (si.FileTmd == null)
                            si.FileTmd = new byte[f.FsSize];
                        fileData = si.FileTmd;
                    }
                    if (fileData == null)
                        continue;
                    inBuffer.TestFsRange(f.FsOffset, f.FsSize, rr);
                    if (rr.IsMatch)
                        inBuffer.ReadFs(rr.BufferOffset, fileData, (int)rr.RangeOffset, WiiUConsts.DefaultSectorSize, 0, WiiUConsts.DefaultSectorSize, rr.Size);
                }
            }

            bool complete = inBuffer.ImageOffset + inBuffer.Size >= Buffer.FsOffsetToOffset(fsInfo.FileSystem?.Files?.Last()?.PostGapFsOffset ?? 0, cntHeader.BlockSize, cntHeader.BlockFsOffset, cntHeader.BlockFsSize, false);
            if (complete)
                si.Complete(header);
            return complete;
        }


        public SystemType SystemType { get; internal set; }

        private AreaInfo setAreaBlockInfo(AreaInfo ai, IFileSystemInfo fsInfo, long fsOffset, long imageOffset)
        {
            if (ai.Type == AreaType.FileSystem)
                _currentCntHeader = ((FileSystemInfo)fsInfo).FstBlock.GetContentHeader(imageOffset);
            else if (ai.Type == AreaType.PartitionHeader)
                _currentCntHeader = null;

            ai.SetBlock(_currentCntHeader?.BlockSize ?? WiiUConsts.DefaultSectorSize, _currentCntHeader?.BlockFsOffset ?? 0, _currentCntHeader?.BlockFsSize ?? WiiUConsts.DefaultSectorSize, WiiUConsts.DefaultSectionSize);

            bool encryptableArea = !_header.EncryptedNoKeyMode && ai.Type != AreaType.ImageHeader && ai.Type != AreaType.PartitionHeader;
            bool encryptable = !_header.EncryptedNoKeyMode && encryptableArea && ((_currentCntHeader?.HasEncryption ?? true) || (_fsInfo?.EncryptType ?? 0) != 0);
            ai.SetSecurity(encryptable, encryptableArea, _currentCntHeader?.HasHashes ?? false);

            // For gap areas with no content header: the correct security flags depend on context.
            // A true pre-partition gap (before any FST has been parsed) contains repeated
            // ImageHeader+PartitionTable blocks and has no encryption. An inter-partition gap
            // (between the end of one partition's content and the next PartitionHeader) is inside
            // an encrypted partition and must be treated as encrypted-on-disc so downstream
            // processing (TheOverseer.wiiUTests) does not reject it.
            if (ai.Type == AreaType.Other && _currentCntHeader == null)
            {
                if (_fsInfo == null || _fsInfo.EncryptType == 0)
                {
                    // Pre-partition or unencrypted gap: repeated ImageHeader+PartitionTable data.
                    int repeatUnit = (int)WiiUConsts.DiscContentsOffset + _header.BlockSize;
                    ai.SetBlock(repeatUnit, 0, repeatUnit, repeatUnit);
                    ai.SetSecurity(false, false, false);
                }
                else
                    ai.SetSecurity(false, true, false); // inter-partition encrypted gap
            }

            switch (ai.Type)
            {
                case AreaType.ImageHeader:
                    ai.SetProperties("ID");
                    break;
                case AreaType.PartitionTable:
                    ai.SetProperties("Partitions", "Encrypted", "KeyCrc");
                    break;
                case AreaType.PartitionHeader:
                    ai.SetProperties("Partition", "PartitionType", "VolumeId", "TitleId", "Signed");
                    break;
                case AreaType.FstBlock:
                    ai.SetProperties("Partition", "TmdVersion", "ContentHeaders", "App", "Filename", "TitleId", "Signed", "Encrypted", "CommonKeyCrc", "TitleKeyCrc", "MissingFiles");
                    break;
                case AreaType.FileSystem:
                    ai.SetProperties("Partition", "ContentIndex", "App", "Filename", "Encrypted", "BlockSize", "HashSize", "HashRoot", "CommonKeyCrc", "TitleKeyCrc", "TitleKeyMissing", "SiTitleId");
                    break;
                case AreaType.Other:
                    ai.SetProperties("Partition", "RepeatedContentIndex", "Encrypted", "Hashes", "BlockSize", "HashSize");
                    break;
                case AreaType.RawKeyMissing:
                    ai.SetProperties("ContentIndex", "App", "Filename", "ContentHeaders", "TmdVersion", "MissingFiles");
                    break;
                default:
                    break;
            }
            return ai;
        }

        // Populate _areas with FileSystem and Other entries based on FST content headers.
        private void prepopulateAreasFromFst(FileSystemInfo fsInfo)
        {
            try
            {
                if (fsInfo == null || fsInfo.FstBlock == null)
                    return;

                if (_areas == null)
                    _areas = new List<IImageArea>();

                ContentHeader[] headers = fsInfo.FstBlock.ContentHeaders.Where(h => h.Size > 0).OrderBy(h => h.ImageOffset).ToArray();
                for (int hi = 0; hi < headers.Length; hi++)
                {
                    ContentHeader h = headers[hi];
                    long hdrOffset = h.ImageOffset;

                    // Ensure FileSystem area exists for this content header
                    ensureAreaExists(hdrOffset, AreaType.FileSystem);

                    // If there's a gap between this header's padded end and the next
                    // boundary, insert an Other area at the padded end to represent
                    // the gap.  For intermediate headers the next boundary is the
                    // following content header; for the last header we use the first
                    // existing area beyond this header (e.g. the next partition header
                    // or the trailing None sentinel).
                    long end = hdrOffset + h.SizePaddedToBlock;
                    if (hi + 1 < headers.Length)
                    {
                        long nextHdr = headers[hi + 1].ImageOffset;
                        if (nextHdr > end && !_isIdx)
                            ensureAreaExists(end, AreaType.Other);
                    }
                    else
                    {
                        // Last content header – look for the next known area boundary.
                        IImageArea following = _areas.FirstOrDefault(a => a.ImageOffset > hdrOffset);
                        if (following != null && following.ImageOffset > end && !_isIdx)
                            ensureAreaExists(end, AreaType.Other);
                    }
                }

                _info.SourceAreas = _areas?.ToArray(); // keep ImageInfo in sync
            }
            catch { }
        }

        // Ensure an area exists at the given offset and of the given type.
        private void ensureAreaExists(long offset, AreaType type)
        {
            try
            {
                if (_areas == null)
                    _areas = new List<IImageArea>();

                bool exists = _areas.Any(a => a.ImageOffset == offset && a.AreaType == type);
                if (!exists)
                {
                    int insert = _areas.FindIndex(a => a.ImageOffset > offset);
                    IImageArea imgArea = (IImageArea)new ImageArea(offset, type);
                    if (insert < 0)
                        _areas.Add(imgArea);
                    else
                        _areas.Insert(insert, imgArea);

                    _info.SourceAreas = _areas?.ToArray(); // keep ImageInfo in sync
                }
            }
            catch { }
        }

        public void SetScanProperties()
        {
            int partition = -1;
            PartitionInfo ptn = null;
            ContentHeader cnt = null;
            FileSystemInfo fsInfo = null;
            Scan result = _context.Scan;

            bool isRetail = ((ImageHeader)_context.Header).SiData.All(a => a.TmdInfo?.IsRetail ?? true);

            foreach (ScanArea sra in _context.Scan.Areas)
            {
                switch (sra.Type)
                {
                    case AreaType.ImageHeader:
                        sra.AreaInfo.Properties["ID"] = _header.Data.ReadStringToNull(0) ?? "";
                        break;
                    case AreaType.PartitionTable:
                        sra.AreaInfo.Properties["Partitions"] = ((ImageHeader)_context.Header).Partitions.Count;
                        sra.AreaInfo.Properties["Encrypted"] = sra.AreaInfo.IsEncrypted;
                        if (sra.AreaInfo.IsEncrypted)
                            sra.AreaInfo.Properties["KeyCrc"] = ((ImageHeader)_context.Header) == null ? (uint)0 : Crc.Compute(((ImageHeader)_context.Header).Key);
                        break;
                    case AreaType.PartitionHeader:
                        fsInfo = (FileSystemInfo)sra.FsInfo;
                        ptn = _header.GetPartition(sra.ImageOffset);
                        sra.AreaInfo.Properties["Partition"] = ++partition;
                        sra.AreaInfo.Properties["PartitionType"] = ptn.Type.ToString();
                        sra.AreaInfo.Properties["VolumeId"] = ptn.Id;
                        sra.AreaInfo.Properties["TitleId"] = ptn.WiiUTitleId.ToString("X16");
                        if (ptn.WiiUSiData != null && ptn.WiiUSiData.SignedStatus != SignedStatus.None)
                            sra.AreaInfo.Properties["Signed"] = ptn.WiiUSiData.SignedStatus.ToString();
                        break;
                    case AreaType.FstBlock:
                        fsInfo = (FileSystemInfo)sra.FsInfo;
                        if (!_isIdx)
                            sra.AreaInfo.Properties["Partition"] = partition;
                        else
                        {
                            sra.AreaInfo.Properties["TitleId"] = _header.SiData[0].TitleId.ToString("X16");
                            sra.AreaInfo.Properties["TmdVersion"] = _header.SiData[0].TmdInfo.TitleVersion.ToString();
                            sra.AreaInfo.Properties["Signed"] = _header.SiData[0].SignedStatus.ToString();
                        }
                        sra.AreaInfo.Properties["ContentHeaders"] = fsInfo.FstBlock.ContentHeaders.Length;
                        if (fsInfo.FstBlock.ContentHeaders.Any(a => a.Content != null))
                            sra.AreaInfo.Properties["App"] = $"{fsInfo.FstBlock.ContentHeaders[0].Content.ContentId:x8}.app";
                        if (_isIdx && _context.SourceFile?.IndexFile?.Items != null && _context.SourceFile.IndexFile.Items.Length > 0)
                            sra.AreaInfo.Properties["Filename"] = _context.SourceFile.IndexFile.Items[0].FileName;
                        sra.AreaInfo.Properties["Encrypted"] = sra.AreaInfo.IsEncrypted;
                        if (sra.AreaInfo.IsEncrypted)
                        {
                            byte[] key = isRetail ? _header.KeyCommon : _header.KeyCommonDev;
                            sra.AreaInfo.Properties["CommonKeyCrc"] = key == null ? (uint)0 : Crc.Compute(key);
                            if (fsInfo.Type == PartitionType.Game && sra.AreaInfo.IsEncrypted)
                            {
                                if (fsInfo?.SiData?.KeyTitle != null)
                                    sra.AreaInfo.Properties["TitleKeyCrc"] = Crc.Compute(fsInfo.SiData.KeyTitle);
                                else
                                    sra.AreaInfo.Properties["TitleKeyMissing"] = true;
                            }
                        }
                        if (_isIdx)
                        {
                            string missing = String.Join("|", _context.SourceFile.IndexFile.Items.Where(a => a.FileIsMissing).Select(a => a.FileName));
                            if (!string.IsNullOrEmpty(missing))
                                sra.AreaInfo.Properties["MissingFiles"] = missing;
                        }
                        break;
                    case AreaType.FileSystem:
                        cnt = fsInfo.FstBlock.GetContentHeader(sra.ImageOffset);
                        if (!_isIdx)
                            sra.AreaInfo.Properties["Partition"] = partition;
                        sra.AreaInfo.Properties["ContentIndex"] = cnt.Index;
                        if (cnt.Content != null)
                            sra.AreaInfo.Properties["App"] = $"{cnt.Content.ContentId:x8}.app";
                        if (_isIdx && _context.SourceFile?.IndexFile?.Items != null && cnt.Index < _context.SourceFile.IndexFile.Items.Length)
                            sra.AreaInfo.Properties["Filename"] = _context.SourceFile.IndexFile.Items[cnt.Index].FileName;
                        sra.AreaInfo.Properties["Encrypted"] = cnt.HasEncryption;
                        sra.AreaInfo.Properties["BlockSize"] = (uint)cnt.BlockSize;
                        sra.AreaInfo.Properties["HashSize"] = (uint)cnt.BlockFsOffset;
                        if (cnt.BlockFsOffset != 0)
                            sra.AreaInfo.Properties["HashRoot"] = cnt.IsValid ? "Valid" : (cnt.H3Hashes.Length == 0 ? "MissingH3" : "Invalid");
                        if (cnt.HasEncryption)
                        {
                            byte[] key = isRetail ? _header.KeyCommon : _header.KeyCommonDev;
                            sra.AreaInfo.Properties["CommonKeyCrc"] = key == null ? (uint)0 : Crc.Compute(key);
                            if (fsInfo.Type == PartitionType.Game)
                            {
                                if (fsInfo?.SiData?.KeyTitle != null)
                                    sra.AreaInfo.Properties["TitleKeyCrc"] = Crc.Compute(fsInfo.SiData.KeyTitle);
                                else
                                    sra.AreaInfo.Properties["TitleKeyMissing"] = true;
                            }
                        }
                        if (fsInfo.Type == PartitionType.Si)
                        {
                            List<SiData> allSiData = ((ImageHeader)_context.Header).SiData;
                            SiData si = allSiData?.FirstOrDefault(a => a.AppIndex == cnt.Index && a.TitleId != 0);
                            if (si != null)
                                sra.AreaInfo.Properties["SiTitleId"] = si.TitleId.ToString("X16");
                        }
                        break;
                    case AreaType.RawKeyMissing:
                        if (_isIdx)
                        {
                            sra.AreaInfo.Properties["ContentIndex"] = sra.AreaInfo.AreaNo;
                            string fn = _context.SourceFile.IndexFile.Items[sra.AreaInfo.AreaNo].FileName;
                            if (!Path.HasExtension(fn))
                                fn += ".app";
                            sra.AreaInfo.Properties["App"] = fn;
                            sra.AreaInfo.Properties["Filename"] = _context.SourceFile.IndexFile.Items[sra.AreaInfo.AreaNo].FileName;
                            if (sra.AreaInfo.AreaNo == 0)
                            {
                                sra.AreaInfo.Properties["ContentHeaders"] = _context.SourceFile.IndexFile.Items.Count();
                                sra.AreaInfo.Properties["TmdVersion"] = _header.SiData[0].TmdInfo.TitleVersion.ToString();

                                string missing = String.Join("|", _context.SourceFile.IndexFile.Items.Where(a => a.FileIsMissing).Select(a => a.FileName));
                                if (!string.IsNullOrEmpty(missing))
                                    sra.AreaInfo.Properties["MissingFiles"] = missing;
                            }
                        }
                        break;
                    case AreaType.Other:
                        if (!_isIdx)
                            sra.AreaInfo.Properties["Partition"] = partition;
                        if (cnt != null)
                        {
                            sra.AreaInfo.Properties["RepeatedContentIndex"] = cnt.Index;
                            sra.AreaInfo.Properties["Encrypted"] = cnt.HasEncryption;
                            sra.AreaInfo.Properties["Hashes"] = cnt.HasHashes;
                            sra.AreaInfo.Properties["BlockSize"] = (uint)cnt.BlockSize;
                            sra.AreaInfo.Properties["HashSize"] = (uint)cnt.BlockFsOffset;
                        }
                        break;
                    default:
                        break;
                }
            }

            result.Properties["System"] = this.SystemType.ToString();
            result.Properties["Media"] = _mediaType.ToString();
            result.Properties["Type"] = isRetail ? "Retail" : "Dev";
            result.Properties["Size"] = (ulong)result.Size;
            result.Properties["CRC"] = result.Crc;
            result.Properties["DecryptedCRC"] = result.CrcDecrypted;
        }


        public ISectionProcessor CreateSectionProcessor() => new SectionProcessor(_header, _context.ImageInfo.Mode, _info);

        public IBufferPreProcessor GetPreProcessor() => _preProcessor;

        public ISectionProcessor PatchSection(ScanSection s) => null;

        public IBuffer CreateBuffer() => new Buffer(SectionSize, true);

    }
}
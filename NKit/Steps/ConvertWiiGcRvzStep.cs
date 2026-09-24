using Nanook.GrindCore;
using Nanook.NKit.Configuration;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    public enum RvzEncodingType { None = 0, Purge = 1, BZip2 = 2, Lzma = 3, Lzma2 = 4, ZStd = 5 }
    internal class ConvertWiiGcRvzStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outFilename;
        private string _outName;
        private string _outExt;
        private string[] _fmt;
        private byte[] _compBytes;

        private class JunkMatch
        {
            public bool HasJunk;
            public byte[] JunkSector = new byte[WiiConsts.WiiSectorSize];
            public byte[] Data = new byte[WiiConsts.WiiSectorSize];
            public int Size;
            public uint[] BaseJunk;
            public int Blocks;
            public uint JunkMask = 0;
            public uint NullMask = 0;
            public uint ScrubMask = 0;
            public bool this[int idx, bool isNull]
            {
                get => ((1u << idx) & (!isNull ? JunkMask : NullMask)) != 0;
                set
                {
                    uint v = 1u << idx;
                    if (value) JunkMask |= v; else JunkMask &= ~v;
                    if (isNull)
                    {
                        if (value) NullMask |= v; else NullMask &= ~v;
                    }
                }
            }

            internal void Reset()
            {
                HasJunk = false;
                JunkMask = 0;
                NullMask = 0;
                ScrubMask = 0;
            }
        }
        private class ChunkBuffer
        {
            public ChunkBuffer(int chunkSize, bool useCompression)
            {
                Buffer = new byte[chunkSize + Math.Max(0x400, chunkSize << 2)];
                CompBuff = !useCompression ? null : new byte[Buffer.Length];
            }
            public int GrpIdx;
            public byte[] Buffer;
            public byte[] CompBuff;
            public bool IsComp;
            public int CompSize;
            public int ExOff;
            public int ExSize;
            public int PackedSz;
            public bool IsPacked;
            public ulong XxHash;
            public ImageBlockCached CacheBlock;
            public bool FromCache;
        }

        private int _maxThreads; // = 16;
        private int _chunkSize; // = 0x20000; //128KiB
        private const int _HdrCopySize = 0x80;
        private RvzEncodingType _compressionType; // = 5; //0=None, 5=zstd
        private int _compressionLevel; // = 19;
        private const float _CompressedHeaderRatio = 1F; //100% for any compression to ensure that the table fits
        private const int _PartEntryLen = 0x30;
        private const int _ReservedLen = 0x8 + 0x4 + 0x10 + 0x14 + 0x8; //'NKIT  v1' CRC32, MD5, SHA1, XXHash64

        private byte[] _hdr;
        private byte[] _disc;
        private List<WiaItem> _items;
        private byte[] _parts;
        private byte[] _raw;
        private byte[] _groups;
        private int _headerSize;

        private int _lastIdx;
        private WiaPartition _currPartn;
        private byte[] _scrubBlock;
        private byte[] _scrubBlockFF;
        private byte[] _scrubBlockFFHash;
        private byte[] _scrubBlock55;
        private byte[] _scrubBlock55Hash;


        private ChecksumStream _strm;
        private long _position;
        private NKitHasher _hasher;
        // Make exception storage atomic/visible across threads.
        private volatile Exception _rvzException;

        private bool _isWii;
        private int _partitionCount;
        private ImageBlockCache _cache;
        private CircularSequenceQueue<ChunkBuffer> _threads;
        private JunkMatch[] _junkMatches;
        private ImageHeader _discHeader;
        private CompressionBlock _compressor;
        private long _size;
        private bool _isRvtH;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => true;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertWiiGcRvz;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ConvertWiiGcRvzStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Use configuration service to validate the entire format string
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(context.SystemType, context.StepConfig);
            if (!validationResult.IsValid)
                throw new HandledException($"Convert - {validationResult.ErrorMessage}");

            // Parse format configuration using unified configuration parser
            object formatConfig = ConfigSettingsFormatParser.ParseFormatConfiguration(context.StepConfig, context.SystemType);

            if (formatConfig is not RvzFormatConfiguration rvzConfig)
                throw new HandledException($"Convert format configuration type '{formatConfig.GetType().Name}' is not supported by this step. Expected RVZ format.");

            // Set basic properties using parsed configuration
            _outName = context.SourceImageName;
            _outExt = ConfigSettingsConstants.FormatRvz;

            // Apply parsed configuration - no more manual parsing needed
            _compressionType = rvzConfig.Encoding;
            _compressionLevel = rvzConfig.CompressionLevel;
            _chunkSize = rvzConfig.BlockSizeBytes;
            _maxThreads = rvzConfig.Parallelism;

            // Build the format array for compatibility with existing code
            if (rvzConfig.Encoding == RvzEncodingType.None)
            {
                _fmt = new[] {
                    ConfigSettingsConstants.FormatRvz,
                    ConfigSettingsConstants.EncodingNone,
                    "0", // No compression level for none encoding
                    ConfigSettingsFormatParser.ParseBlockSizeToString(rvzConfig.BlockSizeBytes),
                    rvzConfig.Parallelism.ToString()
                };
                context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} (Encoding:{_fmt[1]}, BlockSize:{_fmt[3]}, parallelism:{_fmt[4]})");
            }
            else
            {
                _fmt = new[] {
                    ConfigSettingsConstants.FormatRvz,
                    rvzConfig.Encoding.ToString().ToLower(),
                    rvzConfig.CompressionLevel.ToString(),
                    ConfigSettingsFormatParser.ParseBlockSizeToString(rvzConfig.BlockSizeBytes),
                    rvzConfig.Parallelism.ToString()
                };
                context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} (Encoding:{_fmt[1]}, Level:{_fmt[2]}, BlockSize:{_fmt[3]}, parallelism:{_fmt[4]})");
            }

            // Log configuration warnings from centralized parser
            foreach (string warning in rvzConfig.Warnings)
            {
                context.Log?.Info(() => $"Warning: {warning}");
            }
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;

            _strm = base.OutStream;

            // Values are already parsed by configuration service, so use them directly
            initRvz(_outFilename, _context.ImageSize, _context.SystemType == SystemType.Wii, _chunkSize, _compressionType, _compressionLevel, _maxThreads);
        }

        public void Patched(ISection section)
        {
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.ImageOffset == 0)
                base.OutStream.NewPart(_outName, _outExt, true);

            processRvz(section);
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            finaliseRvz(this.Context.Scan);
            base.ProcessResults();
        }

        private void initRvz(string fn, long imageSize, bool isWii, int chunkSize, RvzEncodingType compressionType, int compressionLevel, int workerThreads)
        {
            _rvzException = null;
            _hasher = new NKitHasher();

            _chunkSize = chunkSize;
            _compressionType = compressionType;
            _compressionLevel = compressionLevel;
            _maxThreads = workerThreads;

            _size = imageSize;
            _hdr = new byte[0x48]; //header size
            _disc = new byte[0xDC]; //header size
            _isWii = isWii;
            _isRvtH = false;
            _cache = new ImageBlockCache(false);

            _position = 0;
            _lastIdx = 0;
            _scrubBlock = new byte[WiiConsts.WiiSectorBlockSize];
            _scrubBlockFF = new byte[WiiConsts.WiiSectorBlockSize];
            _scrubBlockFFHash = new byte[WiiConsts.WiiSectorBlockSize];
            _scrubBlock55 = new byte[WiiConsts.WiiSectorBlockSize];
            _scrubBlock55Hash = new byte[WiiConsts.WiiSectorBlockSize];

            _items = new List<WiaItem>();
            _groups = null;

            if (compressionType == RvzEncodingType.Lzma)
                _compressor = CompressionBlockFactory.Create(CompressionAlgorithm.Lzma, new CompressionOptions() { Type = (CompressionType)compressionLevel, BlockSize = _chunkSize });
            else if (compressionType == RvzEncodingType.Lzma2)
                _compressor = CompressionBlockFactory.Create(CompressionAlgorithm.Lzma2, new CompressionOptions() { Type = (CompressionType)compressionLevel, BlockSize = _chunkSize });
            else
                _compressor = CompressionBlockFactory.Create(CompressionAlgorithm.ZStd, new CompressionOptions() { Type = (CompressionType)compressionLevel, BlockSize = _chunkSize, Version = CompressionVersion.ZStd(ZStdVersion.v1_5_2) });

            ChunkBuffer[] buffs = new ChunkBuffer[_maxThreads + 2]; //plus 2, 1 for filling, 1 for writing
            for (int i = 0; i < buffs.Length; i++)
                buffs[i] = new ChunkBuffer(_chunkSize, _compressionType != RvzEncodingType.None);
            _threads = new CircularSequenceQueue<ChunkBuffer>(buffs, cb => rvzParallelProcessChunk(cb), cb => rvzWriteChunk(cb));
            _junkMatches = new JunkMatch[WiiConsts.WiiGroupSize / WiiConsts.WiiSectorSize]; //the junk seed changes every 0x8000
            for (int i = 0; i < _junkMatches.Length; i++)
                _junkMatches[i] = new JunkMatch();

        }

        private void processRvz(ISection section)
        {
            if (_rvzException != null)
                throw _rvzException;

            int grps = _lastIdx;
            _hasher.Process(section.Encrypted, 0, (int)section.Size);

            if (section.AreaOffset == 0)
            {
                if (section.Type != AreaType.PartitionHeader && section.Type != AreaType.FileSystem)
                    _currPartn = null;

                if (!_isWii)
                {
                    _partitionCount = 0;
                    _parts = new byte[0];
                    _raw = new byte[0x200]; //reserve enough room in case of rvt-h (if not update WipePartition then add 1)
                    _groups = new byte[(2 + (_size / _chunkSize)) * 12]; //12 bytes per entry. Add 1 entry for headers and data that might not be full groups

                    writeHeader(section);
                    _items.Add(new WiaItem() { Index = _items.Count, Offset = _HdrCopySize, Size = section.Size - _HdrCopySize, GroupIndex = grps }); //remove 0x80 bytes - lame
                }
                else if (_isRvtH)
                {
                    _items.Last().Groups = grps - _items.Last().GroupIndex;
                    _items.Last().Size = section.ImageOffset - _items.Last().Offset;
                    _items.Add(new WiaItem() { Index = _items.Count, Offset = section.ImageOffset, GroupIndex = grps });
                }
                else if (section.Type == AreaType.FileSystem)
                {
                    AreaInfo ai = section.AreaInfo;
                    WiaPartition ptn = _currPartn; //(WiaPartition)_items.Last();

                    _items[_items.Count - 2].Groups = grps - _items[_items.Count - 2].GroupIndex;
                    _items[_items.Count - 2].Size = section.ImageOffset - _items[_items.Count - 2].Offset;

                    long fstEnd = (long)(section.Decrypted.ReadUInt32B((_isWii ? WiiConsts.WiiSectorHashSize : 0) + WiiConsts.FstPtrOffset) + section.Decrypted.ReadUInt32B((_isWii ? WiiConsts.WiiSectorHashSize : 0) + WiiConsts.FstSizeOffset)) << (_isWii ? 2 : 0);
                    fstEnd = Buffer.FsOffsetToOffset(fstEnd, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, true);

                    //set the WipePartition segments
                    long fstGrpSize = Math.Min(ptn.Size, (fstEnd / WiiConsts.WiiGroupSize * WiiConsts.WiiGroupSize) + (fstEnd % WiiConsts.WiiGroupSize == 0 ? 0 : WiiConsts.WiiGroupSize));

                    fillDecryptedScrub(ptn.DecryptedScrub, 0, 16, _scrubBlock, 0, _scrubBlock.Length);
                    fillDecryptedScrub(ptn.DecryptedScrubFF, 0, 16, _scrubBlockFF, 0, _scrubBlock.Length);
                    fillDecryptedScrub(ptn.DecryptedScrubFFHash, 0, 16, _scrubBlockFFHash, 0, 16);
                    fillDecryptedScrub(ptn.DecryptedScrubFF, 0, 16, _scrubBlockFFHash, 16, _scrubBlockFFHash.Length - 16);
                    fillDecryptedScrub(ptn.DecryptedScrub55, 0, 16, _scrubBlock55, 0, _scrubBlock.Length);
                    fillDecryptedScrub(ptn.DecryptedScrub55Hash, 0, 16, _scrubBlock55Hash, 0, 16);
                    fillDecryptedScrub(ptn.DecryptedScrub55, 0, 16, _scrubBlock55Hash, 16, _scrubBlock55Hash.Length - 16);
                    ptn.Segments[0].GroupIndex = grps;
                    ptn.Segments[0].Groups = (int)(fstGrpSize / _chunkSize) + (fstGrpSize % _chunkSize == 0 ? 0 : 1);
                    ptn.Segments[0].Offset = (int)(section.ImageOffset / WiiConsts.WiiSectorSize);
                    ptn.Segments[0].Size = (int)(fstGrpSize / WiiConsts.WiiSectorSize);

                    ptn.Segments[1].GroupIndex = ptn.Segments[0].GroupIndex + ptn.Segments[0].Groups;
                    ptn.Segments[1].Groups = (int)(((ptn.Size - fstGrpSize) / _chunkSize) + ((ptn.Size - fstGrpSize) % _chunkSize == 0 ? 0 : 1));
                    ptn.Segments[1].Offset = ptn.Segments[0].Offset + ptn.Segments[0].Size;
                    ptn.Segments[1].Size = (int)(((ptn.Size - fstGrpSize) / WiiConsts.WiiSectorSize) + ((ptn.Size - fstGrpSize) % WiiConsts.WiiSectorSize == 0 ? 0 : 1));
                }
                else if (section.Type == AreaType.ImageHeader)
                {
                    _discHeader = new ImageHeader((byte[])section.Decrypted.Clone(), true);
                    _partitionCount = _discHeader.Partitions.Length;
                    _parts = new byte[(uint)_partitionCount * _PartEntryLen];
                    _raw = new byte[(1 + (_discHeader.HasUpdatePartition ? 0 : 1) + ((uint)_discHeader.Partitions.Length * 3)) * 0x20]; //reserve enough room in case of rvt-h (if not update WipePartition then add 1)
                    _groups = new byte[((_size / _chunkSize) + (_size % _chunkSize == 0 ? 0 : 1) + 1 + (_discHeader.Partitions.Length * 2)) * 12]; //12 bytes per entry. Add 1 entry for headers and data that might not be full groups

                    writeHeader(section);
                    _items.Add(new WiaItem() { Index = _items.Count, Offset = _HdrCopySize, Size = section.Size - _HdrCopySize, GroupIndex = grps }); //remove 0x80 bytes - lame
                }
                else if (section.Type == AreaType.PartitionHeader)
                {
                    _items.Last().Groups = grps - _items.Last().GroupIndex;
                    _items.Last().Size = section.ImageOffset - _items.Last().Offset;
                    _items.Add(new WiaItem() { Index = _items.Count, Offset = section.ImageOffset, Size = section.Size, GroupIndex = grps });

                    WiaPartition ptn = CreateWiaPartition(section.Decrypted, section.ImageOffset, section.Size, _discHeader, _isWii, _size, out _isRvtH);

                    ptn.Index = _items.Count;
                    ptn.Offset = section.ImageOffset + section.Size;
                    ptn.GroupIndex = grps;
                    _currPartn = ptn;
                    _items.Add(_currPartn);
                }
                else if (section.Type == AreaType.Other)
                {
                    _items.Last().Groups = grps - _items.Last().GroupIndex;
                    _items.Last().Size = section.ImageOffset - _items.Last().Offset;
                    _items.Add(new WiaItem() { Index = _items.Count, Offset = section.ImageOffset, GroupIndex = grps });
                }
            }

            int junkSectors = junkScanAndDehash(section);
            int sectors = (int)(section.Size / _chunkSize) + (section.Size % _chunkSize == 0 ? 0 : 1);

            //create the rvz chunk - process the items
            grps += rvzEncode(section, junkSectors, grps, _compressionType == RvzEncodingType.None);

            _lastIdx += sectors;
            if (_lastIdx != grps)
                throw new Exception("RVZ group count mismatch");
            _hasher.EndProcess();

            if (_rvzException != null)
                throw _rvzException;
        }

        private void writeHeader(ISection section)
        {
            _disc.Write(0x10, section.Decrypted, 0x0, _HdrCopySize); //copy header
            _headerSize = _hdr.Length + _disc.Length + _ReservedLen + _parts.Length + _raw.Length + _groups.Length;
            if (_compressionType != RvzEncodingType.None)
            {
                _headerSize = (int)(_headerSize * _CompressedHeaderRatio);
                int r = _headerSize % 4;
                if (r != 0)
                    _headerSize += 4 - r;
            }
            ByteStream.Zeros.Copy(_strm, _headerSize);
            _strm.CrcSplit();
            _position += _headerSize;
        }

        private int junkScanAndDehash(ISection section)
        {
            AreaInfo ai = section.AreaInfo;
            int junkSectors = (int)(section.FsSize / ai.BlockSize) + (section.FsSize % ai.BlockSize != 0 ? 1 : 0); //junk is in 0x8000 blocks

            for (int i = 0; i < junkSectors; i++) //can be paralleled, but no gain
            {
                int fsOffset = i * ai.BlockSize; //fs offset
                JunkMatch jm = _junkMatches[i];
                jm.Reset();
                jm.Size = (int)Math.Min(ai.BlockSize, section.FsSize - fsOffset);
                jm.Blocks = (jm.Size / WiiConsts.WiiSectorBlockSize) + (jm.Size % WiiConsts.WiiSectorBlockSize != 0 ? 1 : 0);

                Buffer.Copy(section.Decrypted, fsOffset, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, jm.Data, 0, ai.BlockSize, 0, ai.BlockSize, jm.Size);
                byte[] junk = null;
                int lastSect = -1;
                int blkOffset = 0;

                for (int bi = 0; bi < jm.Blocks; bi++, blkOffset += WiiConsts.WiiSectorBlockSize)
                {
                    int sz = Math.Min(WiiConsts.WiiSectorBlockSize, jm.Size - blkOffset);
                    int sect = (fsOffset + blkOffset) / ai.BlockFsSize;
                    if (ai.IsEncrypted && section.State[sect]) //if scrubbed encryption
                    {
                        byte scrubByte = section.Encrypted.Read8((sect * ai.BlockSize) + ai.BlockFsOffset);
                        if (section.State[64]) //64 means hashes are scrubbed also
                            Array.Copy(scrubByte == 0xff ? _scrubBlockFF : (scrubByte == 0x55 ? _scrubBlock55 : _scrubBlock), 0, jm.Data, blkOffset, sz);
                        else //handle scrubbed data but not scrubbed hashes
                        {
                            if (junk == null || sect != lastSect)
                                junk = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetDecryptedBytes(_currPartn.Key, section.Encrypted.Read((sect * ai.BlockSize) + 0x3d0, 16), scrubByte, 0x20);
                            fillDecryptedScrub(junk, 16, 16, jm.Data, blkOffset, sz);
                            if ((fsOffset + blkOffset) % ai.BlockFsSize == 0)
                                fillDecryptedScrub(junk, 0, 16, jm.Data, blkOffset, 16);
                        }
                    }
                    else if (jm.Data.Equals(blkOffset, sz, 0))
                        jm[bi, true] = true; //set null block
                    lastSect = sect;
                }

                if (jm.HasJunk = NJunk.Shrink(jm.Data, 0, section.ImageOffset, jm.Size, ref jm.BaseJunk, false))
                {
                    NJunk.Expand((uint[])jm.BaseJunk.Clone(), jm.JunkSector, 0, ai.BlockSize); //clone else it gets modified
                    blkOffset = 0;
                    for (int bi = 0; bi < jm.Blocks; bi++, blkOffset += WiiConsts.WiiSectorBlockSize)
                    {
                        if (jm.Data.Equals(blkOffset, jm.JunkSector, blkOffset, Math.Min(WiiConsts.WiiSectorBlockSize, jm.Size - blkOffset)))
                            jm[bi, false] = true; //set junk block
                    }
                }
            }
            return junkSectors;
        }

        private void fillDecryptedScrub(byte[] decBytes, int decOff, int decSize, byte[] data, int offset, int size)
        {
            int s = decOff;
            int e = decOff + decSize;
            for (int i = 0; i < size; i++)
            {
                data[offset + i] = decBytes[s++];
                if (s >= e)
                    s = decOff;
            }
        }

        private int rvzEncode(ISection section, int junksectors, int grpIdx, bool exceptionPadding) //byte[], int, int, int, int, bool> chunk)
        {
            int chunkIdx = 0;
            int chunkPos = 0;
            int fsSize = (int)section.FsSize;
            bool lastBlockIsJunk = !_junkMatches[0][0, false]; //init to the opposite
            bool lastBlockIsNull = !_junkMatches[0][0, true]; //init to the opposite
            int sz = 0;
            int chunkSize = (int)Buffer.OffsetToFsOffset(_chunkSize, section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize);
            ChunkBuffer cb = rvzInitChunk(_threads.FillItem, section, chunkIdx, exceptionPadding);
            int sizeIdx = cb.PackedSz;

            for (int ji = 0; ji < junksectors; ji++) //junk item index
            {
                JunkMatch jm = _junkMatches[ji];

                for (int bi = 0, boff = 0; bi < jm.Blocks; bi++, boff += WiiConsts.WiiSectorBlockSize) //junk 1k block index
                {
                    int s = Math.Min(WiiConsts.WiiSectorBlockSize, jm.Size - boff);
                    bool isJunk = jm[bi, false];
                    bool isNull = jm[bi, true];

                    if (isJunk != lastBlockIsJunk || isNull != lastBlockIsNull || chunkPos == 0 || (isJunk && bi == 0 && !(isNull && lastBlockIsNull))) //new pack item when data/junk changes or new sector (allow nulls to span sectors)
                    {
                        if (sz != 0) //not the first item
                            cb.Buffer.WriteUInt32B(cb.ExOff + sizeIdx, lastBlockIsJunk ? 0x80000000u | (uint)sz : (uint)sz);
                        sz = 0; //reset
                        sizeIdx = cb.PackedSz;
                        cb.PackedSz += 4;
                        if (isJunk)
                        {
                            if (!isNull)
                            {
                                for (int i = 0; i < NJunk.JunkBaseJunkInts; i++, cb.PackedSz += 4)
                                    cb.Buffer.WriteUInt32B(cb.ExOff + cb.PackedSz, jm.BaseJunk[i]);
                            }
                            else
                            {
                                for (int i = 0; i < NJunk.JunkBaseJunkInts; i++, cb.PackedSz += 4)
                                    cb.Buffer.WriteUInt32B(cb.ExOff + cb.PackedSz, 0); //null junk seed
                            }
                        }
                    }

                    sz += s;
                    cb.IsPacked |= isJunk;

                    if (!isJunk)
                    {
                        cb.Buffer.Write(cb.ExOff + cb.PackedSz, jm.Data, boff, s);
                        cb.PackedSz += s;
                    }

                    lastBlockIsJunk = isJunk;
                    lastBlockIsNull = isNull;

                    chunkPos += s;
                    if (chunkPos == chunkSize || sz == fsSize)
                    {
                        cb.Buffer.WriteUInt32B(cb.ExOff + sizeIdx, lastBlockIsJunk ? 0x80000000u | (uint)sz : (uint)sz);
                        rvzPackedChunk(cb, sizeIdx, chunkIdx + grpIdx, chunkPos); //chunkPos is the last block size
                        _threads.ItemComplete();
                        chunkIdx++;
                        chunkPos = 0;
                        sz = 0; //reset
                        if (ji + 1 == junksectors && bi + 1 == jm.Blocks) //end of for loop - prevent initing new chunk
                            break;

                        cb = rvzInitChunk(_threads.FillItem, section, chunkIdx, exceptionPadding);
                    }
                }
            }
            if (sz != 0)
            {
                cb.Buffer.WriteUInt32B(cb.ExOff + sizeIdx, lastBlockIsJunk ? 0x80000000u | (uint)sz : (uint)sz);
                rvzPackedChunk(cb, sizeIdx, chunkIdx + grpIdx, chunkPos); //chunkPos is the last block size
                _threads.ItemComplete();
                chunkIdx++;
            }
            return chunkIdx; //return count of chunks
        }

        private int writeExceptions(byte[] buff, ISection section, int chunk, bool exceptionPadding, int exOff)
        {
            ushort cnt = 0;
            int c = 2; //skip count

            //just write them all rather than using time to analyse then or introduce ambiguety.
            if ((section.AreaInfo.HasSecurity && !section.IsCreatable) || !section.State.IsClear()) //write out the exceptions
            {
                int sectors = _chunkSize / WiiConsts.WiiSectorSize;
                int sectOff = chunk * _chunkSize;
                int sectBase = sectOff / WiiConsts.WiiSectorSize;
                ushort hashOff = 0;
                ushort off;
                byte[] scrub;
                for (int i = 0; i < sectors; i++)
                {
                    off = 0;
                    bool scrubbed = section.AreaInfo.IsEncrypted && section.State[64] && section.State[sectBase + i]; //64 means hashes are scrubbed ((int)WiiConsts.WiiGroupSize / WiiConsts.WiiSectorSize)
                    scrub = !scrubbed ? null : section.Decrypted[sectOff] == 0xff ? _scrubBlockFFHash : (section.Decrypted[sectOff] == 0x55 ? _scrubBlock55Hash : _scrubBlock);

                    for (int h = 0; h < 31; h++)
                        copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);

                    copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);

                    for (int h = 0; h < 8; h++)
                        copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);

                    copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);
                    off -= 8; //adjust for hash gap
                    copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);

                    for (int h = 0; h < 8; h++)
                        copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);

                    copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);
                    off -= 8; //adjust for hash gap
                    copyHash(buff, section, scrub, exOff, ref c, sectOff, hashOff, ref off, ref cnt);

                    hashOff += WiiConsts.WiiSectorHashSize;
                    sectOff += WiiConsts.WiiSectorSize;
                }
            }

            buff.WriteUInt16B(exOff, cnt); //write count

            if (exceptionPadding)
            {
                int p = c % 4;
                if (p != 0)
                {
                    p = 4 - p;
                    Array.Clear(buff, exOff + c, p);
                    c += p;
                }
            }

            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void copyHash(byte[] buff, ISection section, byte[] scrub, int exOff, ref int c, int sectOff, ushort hashOff, ref ushort off, ref ushort cnt)
        {
            buff.WriteUInt16B(exOff + c, (ushort)(hashOff + off));
            c += 2;
            if (scrub != null)
                Array.Copy(scrub, off, buff, exOff + c, 20);
            else
                Array.Copy(section.Decrypted, sectOff + off, buff, exOff + c, 20);
            c += 20;
            off += 20;
            cnt++;
        }

        private void finaliseRvz(Scan scan)
        {
            long headerSize = 0;
            try
            {
                _threads.Complete(); //block until complete

                _items.Last().Groups = _lastIdx - _items.Last().GroupIndex;
                _items.Last().Size = _size - _items.Last().Offset;

                //calculate partitions and raw sections
                int prtOff = 0;
                int rawOff = 0;
                int rawItems = 0;
                foreach (WiaItem itm in _items)
                {
                    WiaPartition ptn = itm as WiaPartition;
                    if (ptn != null)
                    {
                        _parts.Write(prtOff, ptn.Key);
                        prtOff += 0x10;
                        foreach (WiaItem wi in ptn.Segments)
                        {
                            _parts.WriteUInt32B(prtOff + 0x0, (uint)wi.Offset);
                            _parts.WriteUInt32B(prtOff + 0x4, (uint)wi.Size);
                            _parts.WriteUInt32B(prtOff + 0x8, (uint)wi.GroupIndex);
                            _parts.WriteUInt32B(prtOff + 0xc, (uint)wi.Groups);
                            prtOff += 0x10;
                        }
                    }
                    else //raw
                    {
                        _raw.WriteUInt64B(rawOff + 0x0, (ulong)itm.Offset);
                        _raw.WriteUInt64B(rawOff + 0x8, (ulong)itm.Size);
                        _raw.WriteUInt32B(rawOff + 0x10, (uint)itm.GroupIndex);
                        _raw.WriteUInt32B(rawOff + 0x14, (uint)itm.Groups);
                        rawOff += 0x18;
                        rawItems++;
                    }
                }

                uint rawSize = (uint)rawOff;
                uint groupsSize = (uint)(_lastIdx * 12);
                int partsLen = prtOff;

                if (_compressionType != RvzEncodingType.None)
                {
                    byte[] b = new byte[_groups.Length];
                    int sz = Math.Min(b.Length, compress(_groups, 0, 0xc * _lastIdx, b, 0, b.Length));
                    _groups = b;
                    groupsSize = (uint)sz; //group data size

                    b = new byte[_raw.Length];
                    sz = Math.Min(b.Length, compress(_raw, 0, rawOff, b, 0, b.Length));
                    if (_compressionType == RvzEncodingType.Lzma)
                        _compBytes = (byte[])_compressor.Properties.Clone();
                    _raw = b;
                    rawSize = (uint)sz; //raw data size
                }

                int rawSizePad = (int)(rawSize % 4);
                if (rawSizePad != 0)
                    rawSizePad = 4 - rawSizePad;
                long groupsOffset = _hdr.Length + _disc.Length + _ReservedLen + partsLen + rawSize + rawSizePad;

                _disc.WriteUInt32B(0x0, _isWii ? 2u : 1u); //2Wii, 1gc
                _disc.WriteUInt32B(0x4, (uint)_compressionType); //Compression, 0=None
                _disc.WriteUInt32B(0x8, (uint)_compressionLevel); //CompressionLevel
                _disc.WriteUInt32B(0xc, (uint)_chunkSize); //chunk size
                                                           //_disc.Write(0x10, <image header>, 0x0, _hdrCopySize); //already written
                _disc.WriteUInt32B(0x90, (uint)_partitionCount); //partitions
                _disc.WriteUInt32B(0x94, _PartEntryLen); //part data size
                _disc.WriteUInt64B(0x98, (ulong)(_hdr.Length + _disc.Length + _ReservedLen)); //part data offset
                using (SHA1 sha = SHA1.Create())
                    _disc.Write(0xa0, sha.ComputeHash(_parts, 0, partsLen)); //part table sha1
                _disc.WriteUInt32B(0xb4, (uint)rawItems); //raw data struct count
                _disc.WriteUInt64B(0xb8, (ulong)(_hdr.Length + _disc.Length + _ReservedLen + partsLen)); //raw data offset
                _disc.WriteUInt32B(0xc0, rawSize); //raw data size
                _disc.WriteUInt32B(0xc4, (uint)_lastIdx); //group data struct count
                _disc.WriteUInt64B(0xc8, (ulong)groupsOffset); //group data offset
                _disc.WriteUInt32B(0xd0, groupsSize); //group data size
                if (_compBytes != null)
                {
                    _disc.Write8(0xd4, (byte)_compBytes.Length);
                    _disc.Write(0xd5, _compBytes); //compression bytes
                }
                //finalise the rest at the end
                _hdr.WriteString(0x0, 0x4, "RVZ\x1");
                _hdr.WriteUInt32B(0x4, 0x01000000u); //format version
                _hdr.WriteUInt32B(0x8, 0x00030000u); //version_compatible - taken from dolphin created image
                _hdr.WriteUInt32B(0xc, (uint)_disc.Length); //disc struct len
                using (SHA1 sha = SHA1.Create())
                    _hdr.Write(0x10, sha.ComputeHash(_disc)); //disc struct sha1
                _hdr.WriteUInt64B(0x24, (ulong)_size); //disc size
                _hdr.WriteUInt64B(0x2C, (ulong)_position); //RVZ size
                using (SHA1 sha = SHA1.Create())
                    _hdr.Write(0x34, sha.ComputeHash(_hdr, 0, 0x34)); //header sha1

                _hasher.Complete();
                byte[] reserved = new byte[_ReservedLen];
                reserved.WriteString(0, 0x8, "NKIT  v1");

                Checksums chk = this.Context.Result.InFileParts[0].Checksums;
                reserved.WriteUInt32B(0x8, chk.Crc);
                reserved.Write(0x8 + 0x4, chk.Md5);
                reserved.Write(0x8 + 0x4 + 0x10, chk.Sha1);
                reserved.Write(0x8 + 0x4 + 0x10 + 0x14, chk.XxHash.ToBytesBE());

                headerSize = _strm.CrcPatch(0, ms =>
                {
                    ms.Write(_hdr, 0, _hdr.Length);
                    ms.Write(_disc, 0, _disc.Length);
                    ms.Write(reserved, 0, reserved.Length);
                    ms.Write(_parts, 0, partsLen);
                    ms.Write(_raw, 0, (int)rawSize);
                    ByteStream.Zeros.Copy(ms, rawSizePad);
                    ms.Write(_groups, 0, (int)groupsSize);
                });
            }
            catch (Exception ex)
            {
                // Record the first exception from finalisation and rethrow so callers see the real error.
                System.Threading.Interlocked.CompareExchange(ref _rvzException, ex, null);
                throw;
            }
            finally
            {
                try { _compressor.Dispose(); } catch { }
            }
            if (headerSize > _headerSize)
                throw new Exception("RVZ header does not fit in allocated space");

        }


        private ChunkBuffer rvzInitChunk(ChunkBuffer cb, ISection section, int chunkIdx, bool exceptionPadding)
        {
            bool isPtn = section.AreaInfo.BlockSize != section.AreaInfo.BlockFsSize;

            cb.ExOff = !isPtn || exceptionPadding ? 0 : 2;
            cb.ExSize = !isPtn ? 0 : writeExceptions(cb.Buffer, section, chunkIdx, exceptionPadding, cb.ExOff);
            cb.IsPacked = false;
            cb.PackedSz = cb.ExSize;
            cb.IsComp = false;
            cb.CompSize = 0;
            cb.FromCache = false;
            cb.CacheBlock = null;
            return cb;
        }

        private void rvzPackedChunk(ChunkBuffer cb, int sizeIdx, int grpIdx, int chunkSize) //byte[], int, int, int, int, bool> chunk)
        {
            if (cb.PackedSz - 4 == sizeIdx)
                cb.PackedSz -= 4; //remove trailing size

            if (!cb.IsPacked && sizeIdx == cb.ExSize && cb.PackedSz - cb.ExSize - 4 == chunkSize) //no packing - slide the ex byte forward 4 bytes
            {
                for (int i = cb.ExOff + cb.ExSize + 4 - 1; i >= cb.ExOff + 4; i--)
                    cb.Buffer[i] = cb.Buffer[i - 4];
                cb.ExOff += 4;
                cb.PackedSz -= 4;
            }
            cb.GrpIdx = grpIdx;
            cb.XxHash = XXHash64.Compute(cb.Buffer, cb.ExOff, cb.PackedSz);
            bool added;
            cb.CacheBlock = _cache.Register(cb.XxHash, out added);
            cb.FromCache = !added;
        }


        private void rvzParallelProcessChunk(ChunkBuffer cb)
        {
            if (_rvzException != null)
                return;

            try
            {
                if (!cb.FromCache)
                {
                    if (cb.PackedSz != 0 && _compressionType != RvzEncodingType.None)
                    {
                        cb.CompSize = compress(cb.Buffer, cb.ExOff, cb.PackedSz, cb.CompBuff, cb.ExOff, cb.CompBuff.Length - cb.ExOff);
                        cb.IsComp = cb.CompSize < cb.PackedSz && cb.CompSize > 0;
                    }
                    //cater for stange exception byte alignment if not % 4 and not compressed
                    if (!cb.IsComp)
                    {
                        int exAlign = cb.ExSize % 4;
                        if (exAlign != 0)
                        {
                            exAlign = 4 - exAlign;
                            for (int i = 0; i < cb.ExSize; i++)
                                cb.Buffer[cb.ExOff + i - exAlign] = cb.Buffer[cb.ExOff + i];
                            cb.ExOff -= exAlign;
                            Array.Clear(cb.Buffer, cb.ExOff + cb.ExSize, exAlign); //clear the gap
                            cb.ExSize += exAlign;
                            cb.PackedSz += exAlign;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Atomically record the first exception from worker threads.
                System.Threading.Interlocked.CompareExchange(ref _rvzException, ex, null);
            }
        }

        private void rvzWriteChunk(ChunkBuffer cb)
        {
            if (_rvzException != null)
                return;

            try
            {
                if (cb.CacheBlock?.Tag == null)
                {
                    int dataSize = cb.IsComp ? cb.CompSize : cb.PackedSz;
                    byte[] buff = cb.IsComp ? cb.CompBuff : cb.Buffer;

                    int align = dataSize % 4;
                    if (align != 0)
                    {
                        align = 4 - align;
                        Array.Clear(buff, cb.ExOff + dataSize, align);
                    }
                    int off = cb.GrpIdx * 12;
                    cb.CacheBlock.Tag = off;
                    _groups.WriteUInt32B(off, (uint)(_position >> 2));
                    _groups.WriteUInt32B(off + 4, cb.IsComp ? (uint)(0x80000000 | dataSize) : (uint)dataSize);
                    _groups.WriteUInt32B(off + 8, cb.IsPacked ? (uint)(cb.PackedSz - cb.ExSize) : 0u);
                    if (cb.PackedSz != 0)
                    {
                        _strm.Write(buff, cb.ExOff, dataSize + align);
                        _position += dataSize + (long)align;
                    }
                }
                else
                {
                    int off = cb.GrpIdx * 12;
                    int clone = (int)cb.CacheBlock.Tag; //chunks have to be written in order so the following offsets will have been set
                    _groups.WriteUInt32B(off, _groups.ReadUInt32B(clone));
                    _groups.WriteUInt32B(off + 4, _groups.ReadUInt32B(clone + 4));
                    _groups.WriteUInt32B(off + 8, _groups.ReadUInt32B(clone + 8));
                }
            }
            catch (Exception ex)
            {
                // Atomically record the first exception from writer thread.
                System.Threading.Interlocked.CompareExchange(ref _rvzException, ex, null);
            }
        }

        private int compress(byte[] src, int srcOff, int srcSize, byte[] dst, int dstOff, int dstSize)
        {
            if (_compressionType != RvzEncodingType.None)
            {
                if (_compressor.Compress(src, srcOff, srcSize, dst, dstOff, ref dstSize) != CompressionResultCode.Success)
                    throw new HandledException("RVZ Block Compression Failure");
                return dstSize;
            }
            return 0;
        }

        internal static WiaPartition CreateWiaPartition(byte[] prtHeader, long imageOffset, long size, ImageHeader discHeader, bool isWii, long imageSize, out bool isRvtH)
        {
            isRvtH = false;
            if (prtHeader == null)
                return null;

            try
            {
                // Determine the common key to use.
                bool isRvt = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetIssuer(prtHeader) == WiiConsts.RvtIssuer; //Use the RVT-R key.
                bool isKorean = !isRvt && prtHeader.Read8(WiiConsts.WiiPrtHdrKoreanOffset) == 1; //Use the Korean Key
                isRvtH = (int)(prtHeader.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) << 2) == 0;

                Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetKeys(prtHeader, isRvt, isKorean, out byte[] common, out byte[] titleKey, out byte[] iv);
                long partSize = (long)prtHeader.ReadUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset) << (isWii ? 2 : 0);

                if (partSize == 0 && discHeader != null)
                    partSize = discHeader.GetPartitionSize(imageOffset, imageSize) - size;

                return new WiaPartition(0, titleKey, partSize)
                {
                    DecryptedScrub = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetDecryptedBytes(titleKey, 0x0, 0x0),
                    DecryptedScrubFF = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetDecryptedBytes(titleKey, 0xFF, 0xFF),
                    DecryptedScrubFFHash = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetDecryptedBytes(titleKey, 0x0, 0xFF),
                    DecryptedScrub55 = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetDecryptedBytes(titleKey, 0x55, 0x55),
                    DecryptedScrub55Hash = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetDecryptedBytes(titleKey, 0x0, 0x55)
                };
            }
            catch
            {
                isRvtH = false;
                return null;
            }
        }

    }
}
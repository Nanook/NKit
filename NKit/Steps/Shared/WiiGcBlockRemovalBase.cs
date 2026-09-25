using Nanook.NKit.Nintendo.WiiGc;
using System;

namespace Nanook.NKit.Steps.Shared
{
    internal abstract class WiiGcBlockRemovalBase : StepBase
    {
        private byte[] _buff;
        private int _buffOffset;
        private long _buffImageOffset;
        private DataType? _buffType;
        protected int BlockSize { get; set; }
        public BitState GapType { get; private set; }
        public BitState Gaps { get; private set; }
        public long ImageSize { get; private set; }
        private int _hdrSize;
        private bool _blockRemovalAllowed;
        private bool _blockRemovalChecked;

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            ImageSize = 0;

            long blocks = (WiiConsts.FullSizeWii9 / BlockSize) + (WiiConsts.FullSizeWii9 % BlockSize != 0 ? 1 : 0);
            int bytes = (int)((blocks / 8) + (blocks % 8 != 0 ? 1 : 0));
            GapType = new BitState(bytes);
            Gaps = new BitState(bytes);
            _buff = new byte[BlockSize];
            _buffOffset = 0;
            _buffImageOffset = 0;
            _buffType = null;
            _blockRemovalAllowed = true;
            _blockRemovalChecked = false;
        }

        public abstract byte[] GetHeader();
        public abstract byte[] FinalHeader(int blocks, Scan scan, bool lossless, Checksums chk);
        public abstract byte[] PostData(int blocks, Scan scan, bool lossless, Checksums chk);

        private int writeBuf(byte[] src, int srcOffset, byte[] dst, ref int dstOffset, int len)
        {
            dst.Write(dstOffset, src, srcOffset, len);
            dstOffset += len;
            return len;
        }

        //relies on one section spanning 2 blocks at most
        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.ImageOffset == 0)
            {
                byte[] hdr = GetHeader();
                _hdrSize = hdr.Length;
                base.OutStream.Write(hdr, 0, _hdrSize);
                base.OutStream.CrcSplit();
            }

            // On the first FileSystem section, determine if block removal is allowed.
            // Only encrypted partitions can have blocks removed in lossless mode.
            if (!_blockRemovalChecked && section.AreaInfo.Type == AreaType.FileSystem)
            {
                _blockRemovalChecked = true;
                // Lossless block removal is allowed for encrypted-on-disc partitions. Use the
                // disc-encryption fact (IsEncryptionSupported) not the current source-byte state
                // (IsEncrypted): an RVZ source stores the partition decrypted, so IsEncrypted is
                // false even though the disc partition IS encrypted and its junk gaps are removable.
                _blockRemovalAllowed = section.AreaInfo.IsEncryptionSupported;
            }

            ImageSize = section.ImageOffset + section.Size;
            DataType type;
            int start = analyseGap(section, true, out type);
            int sz = (int)Math.Min(section.Size, BlockSize - _buffOffset); //remaining

            writeBuf(section.Encrypted, 0, _buff, ref _buffOffset, sz);

            if (_buffType != DataType.Data)
            {
                if (ContractIsLossy)
                    _buffType = sz <= start && type != DataType.Data ? DataType.Fill : DataType.Data;
                else
                    _buffType = sz <= start && (type == DataType.Fill || type == DataType.NJunk) && (_buffType == null || _buffType == type) ? type : DataType.Data;
            }

            if (_buffOffset == BlockSize)
            {
                writeBuffer();

                int off = sz;
                if (off < section.Size) //end of section remaining
                {
                    sz = (int)(section.Size - sz);
                    writeBuf(section.Encrypted, off, _buff, ref _buffOffset, sz);
                    int end = start == section.Size ? start : analyseGap(section, false, out type);
                    if (_buffType != DataType.Data)
                    {
                        if (ContractIsLossy)
                            _buffType = sz <= end && type != DataType.Data ? DataType.Fill : DataType.Data;
                        else
                            _buffType = sz <= end && (type == DataType.Fill || type == DataType.NJunk) && (_buffType == null || _buffType == type) ? type : DataType.Data;
                    }
                }
            }
        }

        private void writeBuffer()
        {
            int blockIdx = (int)(_buffImageOffset / BlockSize);
            if (_buffType == DataType.Data)
            {
                base.OutStream.Write(_buff, 0, (int)_buffOffset);
                Gaps[blockIdx] = false;
            }
            else
            {
                Gaps[blockIdx] = true;
                GapType[blockIdx] = _buffType == DataType.NJunk;
                //Trace.WriteLine($"{_buff.ImageOffset:X9} - {blockIdx} - {_buffType}");
            }

            //write
            _buffImageOffset += _buffOffset;
            _buffOffset = 0;
            _buffType = null;
        }

        private int analyseGap(ISection section, bool start, out DataType type)
        {
            int size = 0;
            type = DataType.Data;

            // In lossless mode, only allow block removal for encrypted partition data (Wii only).
            // GameCube images are never encrypted, so always allow block removal for them.
            if (!ContractIsLossy && !_blockRemovalAllowed && section.AreaInfo.Type == AreaType.FileSystem && Context.SystemType != SystemType.GameCube)
                return size;

            bool hashOk = section.AreaInfo.BlockFsSize == section.AreaInfo.BlockSize || section.IsCreatable;

            if ((ContractIsLossy || hashOk) && section.Items.Count != 0)
            {
                ISectionItem f = section.Items[start ? 0 : section.Items.Count - 1];

                if (f.Gap != null && (!start || f.File == null))
                {
                    if (ContractIsLossy)
                    {
                        type = DataType.Fill;
                        size = (int)(f.Gap.FsSize / section.AreaInfo.BlockFsSize * section.AreaInfo.BlockSize);
                    }
                    else if (f.Gap != null && ((f.Gap.DataType == DataType.NJunk && (!start || f.Gap.DataNulls == 0)) || (f.Gap.DataType == DataType.Fill && f.Gap.FillByte == 0) || (f.Gap.DataType == DataType.Other && f.GapInfo.Count != 0)))
                    {
                        ISectionData gap = f.Gap.DataType != DataType.Other ? f.Gap : f.GapInfo[start ? 0 : f.GapInfo.Count - 1];
                        if (f.Gap.DataType == DataType.NJunk || (f.Gap.DataType == DataType.Fill && f.Gap.FillByte == 0))
                        {
                            bool nulls = !start && f.Gap.DataType == DataType.NJunk && f.Gap.DataNulls != 0;
                            size = (int)Buffer.FsOffsetToOffset(gap.FsSize - (nulls ? f.Gap.DataNulls : 0), section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize, true);
                            type = gap.DataType;
                        }
                    }
                }
            }
            return size;
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            writeBuffer();

            long dataSize = base.OutStream.Position - _hdrSize;

            if (dataSize % BlockSize != 0)
            {
                ByteStream.Zeros.Copy(base.OutStream, BlockSize - (dataSize % BlockSize));
                dataSize = base.OutStream.Position - _hdrSize;
            }

            byte[] hdr;
            Scan scan = Context.Scan;
            Checksums chk = Context.Result.InFileParts?[0]?.Checksums;
            if (ContractIsLossy)
                hdr = FinalHeader((int)(dataSize / BlockSize), scan, false, chk);
            else
            {
                hdr = FinalHeader((int)(dataSize / BlockSize), scan, true, chk);
                byte[] post = PostData((int)(dataSize / BlockSize), scan, true, chk);
                if (post != null)
                    base.OutStream.Write(post, 0, post.Length);
            }
            base.OutStream.CrcPatch(0, s =>
            {
                s.Write(hdr, 0, hdr.Length);
            });
            base.ProcessResults();
        }
    }
}
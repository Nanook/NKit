using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    internal class GdRomWriter : IDisposable
    {
        private SourceFileTrack[] _tracks;
        private bool _tosecOut;
        private ChecksumStream _strm;
        private bool _isDisposed;
        private SourceFileTrack _curr;
        private IndexGdType _type;

        private byte[] _cache;
        private int _cacheSz;
        private int _cacheComplete; //0=no, 1=yesGap, 2=noGap
        private int _requiredGapSz;
        private long _processedBytes;
        private DreamcastFixData _fixData;
        private DreamcastAudioOffsets _fixAudioOffset;
        private int _fixIdx;
        private int _fixAdjust; //positive is removed gap, negative is added gap
        private bool _type3SplitAudio;
        private bool _isRedump;
        private bool _isChd;
        private long _newSz;

        internal DreamcastImageType InFormat { get; private set; }
        internal DreamcastImageType OutFormat { get; private set; }
        public uint FullCrc { get; private set; }
        public long FullSize { get; private set; }
        internal bool FixItemFound => _fixIdx != -1;

        public GdRomWriter(DreamcastFixData fixData, IEnumerable<SourceFileTrack> tracks, bool isTosec, ChecksumStream outStream)
        {
            _isRedump = false;
            _fixIdx = -1;
            _tracks = tracks.ToArray();
            _fixData = fixData;
            _type = GetGdRomType(_tracks);
            _tosecOut = isTosec;
            _cache = new byte[2448 * 225]; //max size
            _cacheSz = 0;
            _processedBytes = 0;
            _fixAudioOffset = _fixData?.Image;
            _strm = outStream;
            InFormat = DreamcastImageType.Unknown;
            OutFormat = _tosecOut ? DreamcastImageType.Tosec : DreamcastImageType.Redump;
        }

        public long Position => _strm.Position;
        public long ProcessedBytes => _processedBytes;

        public void OpenTrack(string filename, int trackIdx)
        {
            _curr = _tracks[trackIdx];
            _curr.FileName = filename;
            _fixAdjust = 0;
            _type3SplitAudio = _type == IndexGdType.Type3Split && _curr.TrackType == IndexTrackType.Audio && _curr.TrackIndex >= 4 && _tracks.Length >= 5;
            _strm.NewPart(Path.GetFileNameWithoutExtension(filename), Path.GetExtension(filename), this.OutFormat == DreamcastImageType.Redump);
            _cacheComplete = setGapSize() ? 0 : 2; //incomplete : no match
            _cacheSz = 0;
            _processedBytes = 0;
            _isChd = _curr.ChdMediaType == MediaType.GD;
        }


        public void WriteData(byte[] data, int offset, int size, int readBlockSize)
        {
            _processedBytes += size;
            int onesec = _curr.BlockSize * 75;
            int twosec = onesec << 1;

            if (_curr.BlockSize != readBlockSize) //convert it
                convertDataBlocks(ref data, ref offset, ref size, readBlockSize);

            int cached = 0;
            if (_cacheComplete == 0)
            {
                int testGap = (int)Math.Min(_curr.Size, Math.Max(twosec, _requiredGapSz)); //2 secs

                cached = Math.Min(testGap - _cacheSz, size);

                Array.Copy(data, offset, _cache, _cacheSz, cached);

                if (_cacheSz + cached == testGap)
                {
                    bool isGap;
                    if (_type == IndexGdType.Type3Split && _curr.TrackIndex == _tracks.Length)
                        isGap = IsGap(_cache, 0, onesec, _curr.BlockSize, true) && IsGap(_cache, onesec, twosec, _curr.BlockSize, false); //1 sec, 2 secs
                    else
                        isGap = IsGap(_cache, 0, testGap, _curr.BlockSize, _curr.TrackType == IndexTrackType.Audio);
                    if (isGap)
                        _cacheSz += cached;
                    else
                        cached = 0;

                    _cacheComplete = isGap ? 1 : 2;

                    if (isGap && cached == size) //no bytes left
                        return; //we've cached the data don't write yet
                }
                else if (cached != 0)
                    _cacheSz += cached;
            }

            int off = offset + cached;
            int sz = size - cached;

            if (_cacheComplete != 0) //if complete
            {
                if (_strm.Position == 0) //work out the gaps
                {
                    processGap(data, off);

                    if (_fixAdjust < 0)
                        ByteStream.Zeros.Copy(_strm, -_fixAdjust); //insert the fixGap
                    else
                    {
                        off += _fixAdjust; //move past the removed gap
                        sz -= _fixAdjust;
                    }
                }
                long pos = _strm.Position;
                sz = (int)Math.Min(_newSz - pos, sz);
                if (sz > 0)
                {
                    //chdman 264/5 fix for MoHo (data track sectors appended to audio)
                    if (_isChd && _curr.TrackType == IndexTrackType.Audio && _newSz > twosec && pos + sz > _newSz - twosec) //if last 1 second then check for data markers and blank them
                    {
                        for (int i = Math.Max(0, off + (int)(_newSz - twosec - pos)); i < off + sz; i += _curr.BlockSize)
                        {
                            if (data.ReadUInt64B(i) == 0x00FFFFFFFFFFFFFF && data.ReadUInt32B(i + 8) == 0xFFFFFF00)
                                Array.Clear(data, i, _curr.BlockSize);
                        }
                    }
                    _strm.Write(data, off, sz); //decrypted if no encryption
                }
            }
        }

        public bool CloseTrack()
        {
            if (_curr != null)
            {
                if (_strm.Position < _newSz)
                    ByteStream.Zeros.Copy(_strm, _newSz - _strm.Position); //insert the fixGap

                _curr.Size = _strm.Position;
                //_strm.Close();
                return true;
            }
            return false;
        }

        private bool setGapSize()
        {
            bool test = false;
            _requiredGapSz = 0;
            if (!_tosecOut && _type == IndexGdType.Type3 && _curr.TrackIndex == _tracks.Length)
                _requiredGapSz = _curr.BlockSize * 225;
            else if (_curr.TrackIndex != 1 && _curr.TrackIndex != 3) //tracks 1 and 3 have no gap
            {
                if (_tosecOut && _type3SplitAudio && _curr.TrackIndex >= 5)
                {
                    _requiredGapSz = 0; //TOSEC has no gap for audio tracks up to last audio track for type3 split
                    test = true;
                }
                else
                    _requiredGapSz = _curr.BlockSize * 150; //scan this to detect pregap

                if (_type == IndexGdType.Type3Split && _curr.TrackIndex == _tracks.Length)
                    _requiredGapSz += _curr.BlockSize * 75;
            }
            if (!test)
                test = _requiredGapSz != 0;
            return test;
        }

        private void setNewSz()
        {
            int oneSec = 75 * _curr.BlockSize;
            int twoSec = oneSec << 1;
            bool isLast = _curr.TrackIndex == _tracks.Length;
            bool hasNext = _curr.TrackIndex + 1 <= _tracks.Length;
            IndexTrackType nextType = IndexTrackType.Unknown;
            IndexTrackType lastType = _tracks[_tracks.Length - 1].TrackType;
            if (_curr.TrackIndex < _tracks.Length)
                nextType = _tracks[_curr.TrackIndex].TrackType;

            _newSz = _curr.Size;

            if (_isRedump == _tosecOut) //isredump is false until track 2, which is fine as track1 is same for redump and tosec
            {
                if (_curr.TrackIndex == 2)
                    _newSz += _tosecOut ? -twoSec : twoSec; //redump has pregap
                else if (_curr.TrackIndex >= 4)
                {
                    if (_curr.TrackIndex == _tracks.Length - 1 && _curr.TrackType == IndexTrackType.Audio && lastType != IndexTrackType.Audio)
                        _newSz += _tosecOut ? -oneSec : oneSec; //redump missing onesec post gap
                    else if (isLast)
                    {
                        if (_type != IndexGdType.Type3) //not ends with data, data
                        {
                            _newSz += _tosecOut ? -twoSec : twoSec; //redump has last data track with 2 sec pregap
                            if (_tracks[_tracks.Length - 2].TrackType == IndexTrackType.Audio && lastType != IndexTrackType.Audio) //2nd last track
                                _newSz += _tosecOut ? -oneSec : oneSec; //last track redump has onesec post pregap of audio too
                        }
                    }
                }
            }
            //chdman 264/5 fix where pad is not set for DC Cue conversions
            if (_isChd && _curr.Pad == 0 && hasNext && _curr.TrackType != nextType)
                _newSz -= twoSec;
        }

        public void processGap(byte[] data, int offset)
        {
            int oneSec = 75 * _curr.BlockSize;
            int twoSec = oneSec << 1;

            bool forceNoGap = _isChd && (_type != IndexGdType.Type3 || _curr.TrackIndex != _tracks.Length);
            bool hasGap = !forceNoGap && _cacheComplete == 1 && _cacheSz != 0;
            bool needGap = !_tosecOut && _requiredGapSz != 0;
            bool type2Adjust = _type == IndexGdType.Type2 && _curr.TrackIndex == _tracks.Length;
            bool splitData3 = _type == IndexGdType.Type3Split && _curr.TrackIndex == _tracks.Length;

            if (_curr.TrackIndex == 2)
            {
                _isRedump = hasGap; //make assumption that the gaps are standard for redump/tosec
                InFormat = _isRedump ? DreamcastImageType.Redump : DreamcastImageType.Tosec;
                bool isChdType3Pad = _isChd && _type == IndexGdType.Type3 && _tracks[_tracks.Length - 2].Pad != 0;
                if (isChdType3Pad || ((!_isChd || !_tosecOut) && _isRedump == _tosecOut && _type == IndexGdType.Type3))
                {
                    int idx = _tracks.Length - 2;
                    int threeSec = _tosecOut ? 225 : -225;
                    long bytes = threeSec * _tracks[idx].BlockSize;
                    _tracks[idx].Blocks += threeSec;
                    _tracks[idx].Size += bytes;
                    _tracks[idx + 1].ImageOffset += bytes;
                    _tracks[idx + 1].Blocks -= threeSec;
                    _tracks[idx + 1].Size -= bytes;
                }
            }
            setNewSz();

            if (_curr.TrackType == IndexTrackType.Audio)
            {
                int actual = getAudioOffset(data, offset, oneSec); //only test first second
                int gapSize = getAudioFixOffset(_curr.TrackIndex, actual);
                _fixAdjust = actual - gapSize; //positive is removed gap, negative is added gap
            }

            if (hasGap == needGap) //no conversion
            {
                if (hasGap && _requiredGapSz != 0) //redump to redump
                    _strm.Write(_cache, 0, _requiredGapSz);
            }
            else if (hasGap) //(hasGap && !needGap) / Redump to Tosec
            {
            }
            else //(!hasGap && needGap) / Tosec to Redump
            {
                if (type2Adjust || splitData3) //remove last second of previous track (if blank)
                {
                    Array.Clear(_cache, 0, oneSec + twoSec);
                    if (_curr.TrackType != IndexTrackType.Audio)
                        generateCacheSectorHeaders(oneSec, 150, _curr.BlockSize, data, offset);
                }
                else if (_curr.TrackType == IndexTrackType.Audio)
                    Array.Clear(_cache, 0, _requiredGapSz);
                else
                    generateCacheSectorHeaders(0, 150, _curr.BlockSize, data, offset);

                _strm.Write(_cache, 0, _requiredGapSz); //output the data that was cached and not 
            }

            _curr.PreGapDataSize = _requiredGapSz; //use the expected gap to correct the pregap sizes for the index file
            if (_tosecOut)
                _curr.PreGapDataSize -= splitData3 ? oneSec : type2Adjust && _curr.TrackIndex > 4 ? twoSec : 0; //type2Adjust only if last track is > 4
            _curr.PreGap = _curr.PreGapDataSize / _curr.BlockSize;
        }


        private void generateCacheSectorHeaders(int cacheOffset, int sectors, int sectorSize, byte[] data, int offset)
        {
            long lba = Ecm.SectorToLba(data, offset) - sectors;
            for (int i = cacheOffset; i < cacheOffset + (sectors * sectorSize); i += sectorSize, lba++)
            {
                Ecm.ReconstructPrefix(_cache, i, true, lba);
                Ecm.ReconstructEcc(_cache, i, true, false, false);
            }
        }

        private int getAudioOffset(byte[] data, int offset, int maxLength)
        {
            for (int i = 0; i < maxLength; i += 4)
            {
                if (data.ReadUInt32B(offset + i) != 0)
                    return i;
            }
            return 0; //all silence, no processing
        }

        private int getAudioFixOffset(int trackNo, int currentOffset)
        {
            int dflt = _tosecOut ? currentOffset : 0; //if not fix entry, default redump to 0
            if (_fixAudioOffset == null)
                return dflt;

            if (trackNo == 2)
                _fixIdx = _fixAudioOffset.FindDisc(trackNo - 1, currentOffset, _tosecOut);
            if (_fixIdx == -1)
                return dflt;
            return _fixAudioOffset.GetTrackOffset(_fixIdx, trackNo, _tosecOut);
        }

        private void convertDataBlocks(ref byte[] data, ref int offset, ref int size, int readBlockSize)
        {
            //limited support
            if (readBlockSize == 0x800 && _curr.BlockSize == 0x930) //assume data to audio no sector headers
            {
                int bOff = 0;
                byte[] buff = new byte[((size / readBlockSize) + 1) * _curr.BlockSize];
                for (int i = offset; i < offset + size; i += readBlockSize, bOff += _curr.BlockSize)
                {
                    Array.Clear(buff, bOff, 0x10);
                    Array.Copy(data, i, buff, bOff + 0x10, readBlockSize);
                    Array.Clear(buff, bOff + 0x10 + readBlockSize, _curr.BlockSize - (0x10 + readBlockSize));
                }
                data = buff;
                offset = 0;
                size = bOff;
            }
            else if (readBlockSize == 0x930 && _curr.BlockSize == 0x800) //assume data to audio no sector headers
            {
                int bOff = 0;
                byte[] buff = new byte[((size / readBlockSize) + 1) * _curr.BlockSize];
                for (int i = offset; i < offset + size; i += readBlockSize, bOff += _curr.BlockSize)
                    Array.Copy(data, i + 0x10, buff, bOff, _curr.BlockSize);
                data = buff;
                offset = 0;
                size = bOff;
            }
            else
                throw new Exception("Unsupported GDRom DataBlock Size conversion");
        }

        public void Finalise()
        {
            int blockIdx = 0;
            long off = 0;

            _strm.Close();
            FullCrc = _strm.FullCrc;
            FullSize = _strm.FullSize;

            bool isGdrom = IsGdrom(SystemType.Dreamcast, _tracks);

            if (isGdrom)
            {
                _tracks[0].Comment = "SINGLE-DENSITY AREA";
                _tracks[2].Comment = "HIGH-DENSITY AREA";
            }

            foreach (SourceFileTrack trk in _tracks)
            {
                trk.Blocks = (int)(trk.Size / trk.BlockSize);
                if (_tosecOut)
                {
                    off += trk.PreGapDataSize; //move the offset on by the pregap size
                    blockIdx += trk.PreGap;
                }
                trk.BlockIdx = blockIdx;
                trk.Pad = 0;
                trk.ImageOffset = off;
                off += trk.Size;
                blockIdx += trk.Blocks;
                if (isGdrom && trk.TrackIndex == 2) //pad end of SD area to HD area
                {
                    off = (45000 - blockIdx) * trk.BlockSize;
                    blockIdx = 45000; //track 3 pos
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    if (_strm != null)
                        _strm.Dispose();
                    _strm = null;
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


        public static bool IsGap(byte[] data, int offset, int size, int sectorSize, bool isAudio)
        {
            if (size == 0)
                return false;
            else if (isAudio)
                return data.Equals(offset, size, 0);
            else
            {
                for (int i = offset; i < offset + size; i += sectorSize)
                {
                    if (!data.Equals(i + (sectorSize == 0x930 ? 0x10 : 0), 0x800, 0))
                        return false;
                }
            }
            return true;
        }



        //https://github.com/mamedev/mame/blob/d55e9bff7ca8c66c7cbcde5b3a31ea36af9c752a/src/lib/util/cdrom.cpp
        //https://github.com/mamedev/mame/blob/d55e9bff7ca8c66c7cbcde5b3a31ea36af9c752a/src/lib/util/chd.cpp
        //https://github.com/mamedev/mame/blob/d55e9bff7ca8c66c7cbcde5b3a31ea36af9c752a/src/tools/chdman.cpp#L2403 //gdrom_convert_toc
        //https://github.com/mamedev/mame/blob/d55e9bff7ca8c66c7cbcde5b3a31ea36af9c752a/src/lib/util/chdcd.cpp#L1196


        /**  - from a non-merged commit
         * Dreamcast GDROM patterns are identified by track types and number of tracks
         *
         *   Pattern I - (SD) DATA + AUDIO, (HD) DATA
         *   Pattern II - (SD) DATA + AUDIO, (HD) DATA + ... + AUDIO
         *   Pattern III - (SD) DATA + AUDIO, (HD) DATA + ... + DATA
         *
         * And a III variant when two HD DATA tracks are split by one or more AUDIO tracks.
         */

        public static IndexGdType GetGdRomType(SourceFileTrack[] trks)
        {
            if (trks.Length > 4 && trks[trks.Length - 1].TrackType == IndexTrackType.Mode1Raw)
            {
                if (trks[trks.Length - 2].TrackType == IndexTrackType.Audio)
                    return IndexGdType.Type3Split;
                else
                    return IndexGdType.Type3;
            }
            else if (trks.Length > 3)
            {
                if (trks[trks.Length - 1].TrackType == IndexTrackType.Audio)
                    return IndexGdType.Type2;
                else
                    return IndexGdType.Type3;
            }

            else if (trks.Length == 3)
                return IndexGdType.Type1;

            return IndexGdType.Unknown;
        }

        public static bool IsGdrom(SystemType systemType, IImageInfo imageInfo, SourceFile sourceFile)
        {
            if (imageInfo == null)
                return false;
            return IsGdrom(systemType, imageInfo.Tracks);
        }

        public static bool IsGdrom(SystemType type, SourceFileTrack[] tracks)
        {
            return type == SystemType.Dreamcast && tracks != null && tracks.Length >= 3 &&
                (tracks[0].TrackType == IndexTrackType.Mode1Raw || tracks[0].TrackType == IndexTrackType.Mode1) &&
                (tracks[2].TrackType == IndexTrackType.Mode1Raw || tracks[2].TrackType == IndexTrackType.Mode1);
        }

    }
}
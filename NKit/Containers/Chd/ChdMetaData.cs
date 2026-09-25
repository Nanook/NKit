using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Chd
{
    internal class ChdMetaData
    {
        public static ChdMetaData Parse(string name, IEnumerable<string> metaData, int chdBlockSize, long imageSize) => new ChdMetaData(name, metaData, chdBlockSize, imageSize);

        public List<SourceFileTrack> Tracks { get; internal set; }

        public MediaType MediaType { get; private set; }
        public CdType CdDiscType { get; private set; }
        public IndexGdType GdDiscType { get; private set; }

        public bool IsFolderIndex => this.MediaType == MediaType.GD || (this.MediaType == MediaType.CD && this.Tracks.Any(a => a.BlockSize != 0x800));

        private ChdMetaData()
        {
        }

        private ChdMetaData(string name, IEnumerable<string> metaData, int chdBlockSize, long imageSize)
        {
            //https://github.com/mamedev/mame/blob/master/src/lib/util/cdrom.cpp#L890
            //https://github.com/mamedev/mame/blob/8518da9e35ad5b1baf94ac07312d0d3300363a75/src/tools/chdman.cpp#L1319

            //const char* HARD_DISK_METADATA_FORMAT = "CYLS:%d,HEADS:%d,SECS:%d,BPS:%d";
            //const char* CDROM_TRACK_METADATA_FORMAT = "TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d";
            //const char* CDROM_TRACK_METADATA2_FORMAT = "TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d PREGAP:%d PGTYPE:%s PGSUB:%s POSTGAP:%d";
            //const char* GDROM_TRACK_METADATA_FORMAT = "TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d PAD:%d PREGAP:%d PGTYPE:%s PGSUB:%s POSTGAP:%d";
            //const char* AV_METADATA_FORMAT = "FPS:%d.%06d WIDTH:%d HEIGHT:%d INTERLACED:%d CHANNELS:%d SAMPLERATE:%d";

            this.Tracks = new List<SourceFileTrack>();

            long framesSum = 0;
            long framesPadSum = 0; //all tracks padded to next 4 frames
            long discSizeSum = 0;
            long fullDiscSizeSum = 0;

            bool mode1 = false;
            bool mode2 = false;
            bool cdda = false;

            int tracks = metaData.Count();
            int pad = tracks.ToString().Length;

            foreach (string s in metaData)
            {
                //Debug.WriteLine(s);
                SourceFileTrack trk = new SourceFileTrack();
                MatchCollection mc = Regex.Matches(s, "([^ :]+):([^ ]+)");
                if (mc.Count != 0)
                {
                    trk.RawItems = new Dictionary<string, string>();
                    foreach (Match m in mc)
                        trk.RawItems.Add(m.Groups[1].Value, m.Groups[2].Value);

                    if (trk.RawItems.ContainsKey("TAG"))
                    {
                        trk.ChdTag = trk.RawItems["TAG"];
                        trk.ChdMediaType = trk.ChdTag == "GDDD" || trk.ChdTag == "IDNT" || trk.ChdTag == "KEY" || trk.ChdTag == "CIS" ? MediaType.HD
                                      : (trk.ChdTag == "CHCD" || trk.ChdTag == "CHTR" || trk.ChdTag == "CHT2" ? MediaType.CD
                                      : (trk.ChdTag == "CHGD" || trk.ChdTag == "CHGT" ? MediaType.GD
                                      : (trk.ChdTag == "DVD" ? MediaType.DVD
                                      : (trk.ChdTag == "AVAV" || trk.ChdTag == "AVLD" ? MediaType.AV
                                      : MediaType.Unknown
                                      ))));
                    }

                    if (trk.ChdMediaType == MediaType.CD || trk.ChdMediaType == MediaType.GD)
                    {
                        this.Tracks.Add(trk);
                        IndexTrackType type;
                        CdSubType stype;
                        int sz;
                        processType(trk.RawItems["TYPE"], out sz, out type);
                        trk.BlockSize = sz;
                        trk.TrackType = type;
                        processSubType(trk.RawItems["SUBTYPE"], out sz, out stype);
                        trk.SubSize = sz;
                        trk.ChdSubType = stype;
                        trk.TrackIndex = int.Parse(trk.RawItems["TRACK"]);
                        string ext = trk.TrackType == IndexTrackType.Audio ? (MediaType == MediaType.GD ? ".raw" : ".bin") : ".bin";
                        trk.FileName = (MediaType == MediaType.GD ? $"track{trk.TrackIndex:00}" : (tracks <= 1 ? name : $"{name} (Track {trk.TrackIndex.ToString().PadLeft(pad, '0')})")) + ext;
                        trk.Blocks = int.Parse(trk.RawItems["FRAMES"]);
                        trk.Pad = trk.RawItems.ContainsKey("PAD") ? int.Parse(trk.RawItems["PAD"]) : 0; //newer GD only
                        trk.PadSize = trk.Pad * trk.BlockSize;
                        trk.PreGap = int.Parse(trk.RawItems["PREGAP"]);
                        trk.PostGap = int.Parse(trk.RawItems["POSTGAP"]);
                        trk.BlockIdx = framesSum; //source Data Offset
                        trk.ImageOffset = discSizeSum; //source data offset with padding
                        trk.Size = trk.Blocks * (long)(trk.BlockSize + trk.SubSize); //source data size
                        trk.LogicalOffset = fullDiscSizeSum; //chd Data offset
                        trk.LogicalSize = trk.Blocks * (long)chdBlockSize; //chd data size - chd5 pads block with blank sub
                        if (trk.PreGap > 0)
                        {
                            if (trk.RawItems["PGTYPE"][0] == 'V')
                            {
                                processType(trk.RawItems["PGTYPE"].Substring(1), out sz, out type);
                                trk.PreGapDataSize = sz;
                                trk.PreGapType = type;
                            }

                            processPreGap(trk.RawItems["PGSUB"], out sz, out _);
                            trk.PreGapSubSize = sz;
                        }
                        switch (trk.TrackType)
                        {
                            case IndexTrackType.Mode1:
                            case IndexTrackType.Mode1Raw:
                                trk.BasicType = IndexTrackBasicType.Mode1;
                                mode1 = true;
                                break;

                            case IndexTrackType.Mode2:
                            case IndexTrackType.Mode2Form1:
                            case IndexTrackType.Mode2Form2:
                            case IndexTrackType.Mode2FormMix:
                            case IndexTrackType.Mode2Raw:
                                trk.BasicType = IndexTrackBasicType.Mode2;
                                mode2 = true;
                                break;

                            case IndexTrackType.Audio:
                                trk.BasicType = IndexTrackBasicType.Audio;
                                cdda = true;
                                break;
                        }

                        framesSum += trk.Blocks;
                        framesPadSum += trk.Blocks % 4L == 0 ? 0 : 4L - (trk.Blocks % 4L);
                        discSizeSum += trk.Size;
                        fullDiscSizeSum = (framesSum + framesPadSum) * (long)chdBlockSize;
                    }
                    else if (trk.ChdMediaType == MediaType.DVD)
                    {
                        trk.BlockSize = 0x800;
                        trk.Size = imageSize;
                        trk.LogicalSize = imageSize;
                        trk.Blocks = (int)(imageSize / trk.BlockSize);
                        this.Tracks.Add(trk);

                    }

                }
            }

            //Work out the mediatype for the disc
            if (this.Tracks.Any(a => a.ChdMediaType == MediaType.DVD))
                this.MediaType = MediaType.DVD;
            else if (this.Tracks.Any(a => a.ChdMediaType == MediaType.GD))
            {
                this.MediaType = MediaType.GD;
                this.GdDiscType = GdRomWriter.GetGdRomType(this.Tracks.ToArray());
            }
            else if (this.Tracks.Any(a => a.ChdMediaType == MediaType.AV))
                this.MediaType = MediaType.AV;
            else if (this.Tracks.Any(a => a.ChdMediaType == MediaType.CD))
                this.MediaType = MediaType.CD;
            else
                this.MediaType = MediaType.Unknown; //not supported

            if (mode2)
                this.CdDiscType = CdType.Cdxa;
            else if (cdda && !mode1)
                this.CdDiscType = CdType.Cdda;
            else
                this.CdDiscType = CdType.Cdrom;

        }

        private void processPreGap(string typestring, out int pgsubsize, out CdSubType pgsub)
        {
            //SUBTYPE
            pgsub = CdSubType.None;
            pgsubsize = 0;
            //PREGAP
            if (typestring == "RW")
            {
                pgsub = CdSubType.Normal;
                pgsubsize = 96;
            }
            else if (typestring == "RW_RAW")
            {
                pgsub = CdSubType.Raw;
                pgsubsize = 96;
            }

        }
        private void processSubType(string typestring, out int subsize, out CdSubType subtype)
        {
            //SUBTYPE
            subtype = CdSubType.None;
            subsize = 0;
            if (typestring == "RW")
            {
                subtype = CdSubType.Normal;
                subsize = 96;
            }
            else if (typestring == "RW_RAW")
            {
                subtype = CdSubType.Raw;
                subsize = 96;
            }
        }


        private void processType(string typestring, out int datasize, out IndexTrackType trktype)
        {
            /*
                AUDIO          Audio (sector size: 2352)
                CDG            Karaoke CD+G (sector size: 2448)
                MODE1_RAW      CD-ROM Mode 1 data (raw) (sector size: 2352), used by cdrdao
                MODE1/2048     CD-ROM Mode 1 data (cooked) (sector size: 2048)
                MODE1/2352     CD-ROM Mode 1 data (raw) (sector size: 2352)
                MODE2_RAW      CD-ROM Mode 2 data (raw) (sector size: 2352), used by cdrdao
                MODE2/2048     CD-ROM Mode 2 XA form-1 data (sector size: 2048)
                MODE2/2324     CD-ROM Mode 2 XA form-2 data (sector size: 2324)
                MODE2/2336     CD-ROM Mode 2 data (sector size: 2336)
                MODE2/2352     CD-ROM Mode 2 data (raw) (sector size: 2352)
                CDI/2336       CDI Mode 2 data
                CDI/2352       CDI Mode 2 data
            */

            trktype = IndexTrackType.Unknown;
            datasize = 0;

            switch (typestring)
            {
                case "MODE1":
                case "MODE1/2048":
                    trktype = IndexTrackType.Mode1; //2048
                    break;
                case "MODE2_FORM1":
                case "MODE2/2048":
                    trktype = IndexTrackType.Mode2Form1; //2048
                    break;
                case "MODE1_RAW":
                case "MODE1/2352":
                    trktype = IndexTrackType.Mode1Raw; //2352
                    break;
                case "MODE2":
                case "MODE2/2336":
                    trktype = IndexTrackType.Mode2; //2336
                    break;
                case "MODE2_FORM_MIX":
                    trktype = IndexTrackType.Mode2FormMix; //2336
                    break;
                case "MODE2_FORM2":
                case "MODE2/2324":
                    trktype = IndexTrackType.Mode2Form2; //2324
                    break;
                case "MODE2_RAW":
                case "MODE2/2352":
                case "CDI/2352":
                    trktype = IndexTrackType.Mode2Raw; //2352
                    break;
                case "AUDIO":
                    trktype = IndexTrackType.Audio; //2352
                    break;
            }

            switch (trktype)
            {
                case IndexTrackType.Mode1:
                case IndexTrackType.Mode2Form1:
                    datasize = 2048;
                    break;
                case IndexTrackType.Mode1Raw:
                case IndexTrackType.Mode2Raw:
                case IndexTrackType.Audio:
                    datasize = 2352;
                    break;
                case IndexTrackType.Mode2:
                case IndexTrackType.Mode2FormMix:
                    datasize = 2336;
                    break;
                case IndexTrackType.Mode2Form2:
                    datasize = 2324;
                    break;
            }
        }

        public ChdMetaData Clone()
        {
            ChdMetaData clone = new ChdMetaData();
            clone.CdDiscType = this.CdDiscType;
            clone.GdDiscType = this.GdDiscType;
            clone.MediaType = this.MediaType;
            clone.Tracks = this.Tracks.Select(a => a.Clone()).ToList();
            return clone;
        }

    }

}
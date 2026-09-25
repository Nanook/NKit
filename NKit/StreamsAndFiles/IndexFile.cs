using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
/// <summary>
/// GDI - Maddog
/// .gdi was a poorly thought out/defined standard. Basically it was a quick and dirty job by Raziel to accomodate the needs of his emu, which was the first to support full GD-ROM images.
/// There's nothing else except 0 and 4 for Audio and Data respectively. The 0 at the end is "offset" of the file compared to the LBA address in the GDI.For some weird reason (probably
/// misunderstanding of the way DC or the GD-ROM drive worked) he thought that some files needed to be offset by certain bytes in order to be read properly.So there were files with 0 and
/// -8 offset, IIRC correctly.In later development, he discovered that no such thing existed, so nowadays everything is 0 offset.
/// 
/// Between tracks there are small spaces of blank, these allow the reading mechanicm to align itself properly to the start of a track when asked to read a given track (or something
/// like that). These are called gaps and they are defined in the CD-ROM standards. Sometimes due to mastering errors, gaps can contain non-zero data. If properly done, they should contain
/// absolutely nothing. With Dreamcast, we have dumped virtually every retail GD-ROM and no gaps with data were found, ever.
/// 
/// The standard is 2 seconds between the audio tracks for DC. This becomes 3 seconds when transiting from audio to data. 2 seconds encoded as data and one as audio. Something confusing like that.
/// </summary>
namespace Nanook.NKit
{
    public enum IndexFileType { None, Cue, Gdi, TmdApp }
    public enum IndexFileFormat { Unknown, Binary, Wave, Mp3, Aiff }
    public enum IndexTrackBasicType { Unknown, Audio, Mode1, Mode2, Cdi }
    public enum IndexTrackType { Unknown, Audio, Mode1, Mode1Raw, Mode2, Mode2Form1, Mode2Form2, Mode2FormMix, Mode2Raw }
    public enum IndexGdType { Unknown, Type1, Type2, Type3, Type3Split }

    public class IndexFile : SourceFileItem
    {
        private static Regex _GdiRegex;
        private static Regex _CueRegex;
        public byte[] Data { get; internal set; }

        private IndexFile(string path, string fileName, string extension, string postFix, long offset, long size, uint crc, bool isTemp, bool isArchived, byte[] data) : base(path, fileName, extension, postFix, offset, size, crc, isArchived, isTemp)
        {
            this.Data = data;
        }


        static IndexFile()
        {
            _GdiRegex = new Regex(@"^\s*([0-9]+)\s+([0-9]+)\s+([0-9]+)\s+([0-9]+)\s+(.+)\s+([0-9]+)\s*$", RegexOptions.Compiled); //3 45000 4 2352 "track03.bin" 0  (optional quotes)
            _CueRegex = new Regex(@"^\s*(" +
                                          @"(PREGAP)\s+([0-9]+):([0-9]+):([0-9]+)|" + //2,3,4,5
                                          @"(INDEX)\s+([0-9]+)\s+([0-9]+):([0-9]+):([0-9]+)|" + //6,7,8,9,10
                                          @"(TRACK)\s+([0-9]+)\s+([^ ]+?)|" + //11,12,13
                                          @"(FILE)\s+(.+)\s+([^ ]+?)|" +  //14,15,16
                                          @"(?:REM )?(SESSION)\s+([0-9]+)" +  //17,18
                                    @")\s*$", RegexOptions.Compiled);
        }

        internal static IndexFile Parse(string fileName, string extension, byte[] content) => Parse("", fileName, extension, null, content, false, false, null);

        internal static IndexFile Parse(string path, string fileName, string extension, string postFix, byte[] content, bool isTemp, bool isArchived, FileItem[] folderFiles)
        {
            if (folderFiles == null)
                folderFiles = new FileItem[0];
            List<SourceFileTrack> items = new List<SourceFileTrack>();
            SourceFileTrack entry = null;
            List<FileItem> addFiles = new List<FileItem>();

            string l;
            if (Regex.IsMatch(extension, @$"\.gdi{ResultOutFiles.TempChar}?", RegexOptions.IgnoreCase))
            {
                using (StreamReader sr = new StreamReader(new MemoryStream(content)))
                {
                    while ((l = sr.ReadLine()) != null)
                    {
                        Match m = _GdiRegex.Match(l);
                        if (m.Success)
                        {
                            entry = new SourceFileTrack()
                            {
                                TrackIndex = int.Parse(m.Groups[1].Value),
                                BlockSize = int.Parse(m.Groups[4].Value),
                                BasicType = m.Groups[3].Value == "0" ? IndexTrackBasicType.Audio : IndexTrackBasicType.Mode1,
                                TrackType = IndexTrackType.Audio, //default
                                FileFormat = m.Groups[3].Value == "0" ? IndexFileFormat.Wave : IndexFileFormat.Binary,
                                FileName = m.Groups[5].Value
                            };
                            if (entry.BasicType == IndexTrackBasicType.Mode1)
                                entry.TrackType = entry.BlockSize == 0x800 ? IndexTrackType.Mode1 : IndexTrackType.Mode1Raw;
                            if (entry.FileName.Length >= 2 && entry.FileName.StartsWith("\"") && entry.FileName.EndsWith("\""))
                                entry.FileName = entry.FileName.Substring(1, entry.FileName.Length - 2).Replace("\"\"", "\"");
                            entry.LogicalOffset = long.Parse(m.Groups[2].Value) * entry.BlockSize;
                            items.Add(entry);
                        }
                    }
                    if (items.Count != 0)
                        return new IndexFile(path, fileName, extension, postFix, 0, content.Length, Nanook.NKit.Crc.Compute(content), isTemp, isArchived, content) { FileType = IndexFileType.Gdi, Items = items.ToArray(), Additional = addFiles };
                }
            }
            else if (fileName.ToLower().StartsWith("tmd") || extension.ToLower() == ".tmd")
            {
                int tmdVer = extension.ToLower() == ".tmd" || fileName.ToLower() == "tmd" ? -1 : int.Parse(fileName.Substring(4));
                NKit.Nintendo.WiiU.TmdInfo tmd = new Nintendo.WiiU.TmdInfo(content);
                for (int i = 0; i < tmd.TotalContents; i++)
                {
                    entry = new SourceFileTrack()
                    {
                        TrackIndex = i,
                        BasicType = IndexTrackBasicType.Unknown,
                        TrackType = IndexTrackType.Unknown,
                        FileName = $"{tmd.Content[i].ContentId:x8}"
                    };
                    if (!folderFiles.Any(a => a.FileName == entry.FileName) && folderFiles.Any(a => a.FileName == entry.FileName + ".app"))
                        entry.FileName += ".app";

                    FileItem h3 = folderFiles.FirstOrDefault(a => a.FileName.Equals(entry.FileName.Replace(".app", "") + ".h3", StringComparison.OrdinalIgnoreCase)); //may not have .app appended
                    if (h3 != null && !addFiles.Any(f => f.FileName.Equals(h3.FileName, StringComparison.OrdinalIgnoreCase)))
                        addFiles.Add(h3);

                    items.Add(entry);
                }

                if (tmdVer == -1)
                    addFiles.AddRange(folderFiles.Where(a => a.FileName.Contains("tik", StringComparison.OrdinalIgnoreCase) || a.FileName.Contains("cert", StringComparison.OrdinalIgnoreCase) || a.FileName.Contains("cetk", StringComparison.OrdinalIgnoreCase)));
                else
                {
                    addFiles.AddRange(folderFiles.Where(a => a.FileName.Equals($"tik.{tmdVer}", StringComparison.OrdinalIgnoreCase) || a.FileName.Equals($"cetk.{tmdVer}", StringComparison.OrdinalIgnoreCase)));
                    // If no version-specific cetk was found, fall back to any cetk in the folder.
                    // Multiple TMD versions of the same title share the same title key — the cetk
                    // from any version can decrypt the title key (same TitleId, same common-key IV).
                    if (!addFiles.Any(a => a.FileName.StartsWith("cetk", StringComparison.OrdinalIgnoreCase)))
                    {
                        FileItem fallbackCetk = folderFiles.FirstOrDefault(a => a.FileName.StartsWith("cetk", StringComparison.OrdinalIgnoreCase));
                        if (fallbackCetk != null)
                            addFiles.Add(fallbackCetk);
                    }
                }

                if (items.Count != 0)
                    return new IndexFile(path, fileName, extension, postFix, 0, content.Length, Nanook.NKit.Crc.Compute(content), isTemp, isArchived, content) { FileType = IndexFileType.TmdApp, Items = items.ToArray(), Additional = addFiles };
            }
            else //assume cue
            {
                using (StreamReader sr = new StreamReader(new MemoryStream(content)))
                {
                    // NOTE: CUE handling assumes EITHER a single backing file holding all tracks, OR
                    // the fully-split layout (one file per track). The parser reads multiple FILE/TRACK
                    // pairs, but the INDEX offset maths and the ToCue/ToGdi emit side model those two
                    // shapes only — a MIXED layout (multiple files each containing multiple tracks) is
                    // not supported. (The old "1 track per file" wording was inaccurate — it is the
                    // reverse.) Tracked in NKitVault/10 Refactor/Warning Cleanup.md.
                    string fn = null;
                    int fileBlockSize = 2352; //default
                    IndexFileFormat fileFormat = IndexFileFormat.Unknown;
                    int session = 0;
                    while ((l = sr.ReadLine()) != null)
                    {
                        Match m = _CueRegex.Match(l);

                        if (m.Success)
                        {
                            if (m.Groups[2].Value == "PREGAP") //2,3,4,5
                            {
                                //if (entry == null)
                                //    continue;
                                //long mins = long.Parse(m.Groups[4].Value) * 60 * 75 * entry.BlockSize;
                                //long secs = long.Parse(m.Groups[5].Value) * 75 * entry.BlockSize;
                                //long frames = long.Parse(m.Groups[6].Value) * entry.BlockSize;
                                //entry.OffsetIndexes.Add(mins + secs + frames);
                            }
                            else if (m.Groups[6].Value == "INDEX") //6,7,8,9,10
                            {
                                if (entry == null)
                                    continue;
                                long mins = long.Parse(m.Groups[8].Value) * 60 * 75 * entry.BlockSize;
                                long secs = long.Parse(m.Groups[9].Value) * 75 * entry.BlockSize;
                                long frames = long.Parse(m.Groups[10].Value) * entry.BlockSize;
                                if (entry.OffsetIndexes.Count == 0)
                                {
                                    entry.LogicalOffset = mins + secs + frames;
                                    entry.ImageOffset = mins + secs + frames;
                                }
                                entry.OffsetIndexes.Add(mins + secs + frames);
                            }
                            else if (m.Groups[11].Value == "TRACK") //11,12,13
                            {
                                if (entry != null)
                                    items.Add(entry);
                                entry = new SourceFileTrack() { FileName = fn, BlockSize = fileBlockSize, FileFormat = fileFormat, LogicalOffset = 0, ImageOffset = 0, Session = session };
                                entry.TrackIndex = int.Parse(m.Groups[12].Value);
                                string[] trackType = m.Groups[13].Value.Split('/');
                                if (trackType.Length == 1)
                                    entry.BasicType = trackType[0] == "AUDIO" ? IndexTrackBasicType.Audio : IndexTrackBasicType.Unknown;
                                else if (trackType.Length > 1) //if size is specified
                                {
                                    entry.BasicType = trackType[0] == "MODE1" ? IndexTrackBasicType.Mode1 :
                                                      (trackType[0] == "MODE2" ? IndexTrackBasicType.Mode2 :
                                                      (trackType[0] == "CDI" ? IndexTrackBasicType.Cdi :
                                                      (trackType[0] == "AUDIO" ? IndexTrackBasicType.Audio :
                                                                                 IndexTrackBasicType.Unknown)));
                                    entry.BlockSize = int.Parse(trackType[1]);
                                }
                                if (entry.BasicType == IndexTrackBasicType.Audio)
                                    entry.TrackType = IndexTrackType.Audio;
                                else if (entry.BasicType == IndexTrackBasicType.Mode1)
                                    entry.TrackType = entry.BlockSize == 2048 ? IndexTrackType.Mode1 : IndexTrackType.Mode1Raw;
                                else //mode2 + cdi
                                {
                                    if (entry.BlockSize == 2048)
                                        entry.TrackType = IndexTrackType.Mode2Form1;
                                    else if (entry.BlockSize == 2336)
                                        entry.TrackType = IndexTrackType.Mode2; //could be Mode2FormMix also
                                    else if (entry.BlockSize == 2324)
                                        entry.TrackType = IndexTrackType.Mode2Form2;
                                    else
                                        entry.TrackType = IndexTrackType.Mode2Raw;
                                }
                            }
                            else if (m.Groups[14].Value == "FILE") //14,15,16
                            {
                                fileBlockSize = 2352; //default

                                fileFormat = m.Groups[16].Value == "BINARY" ? IndexFileFormat.Binary :
                                             (m.Groups[16].Value == "WAVE" ? IndexFileFormat.Wave :
                                             (m.Groups[16].Value == "MP3" ? IndexFileFormat.Mp3 :
                                             (m.Groups[16].Value == "AIFF" ? IndexFileFormat.Aiff :
                                                                             IndexFileFormat.Unknown)));
                                fn = m.Groups[15].Value;
                                if (fn.Length >= 2 && fn.StartsWith("\"") && fn.EndsWith("\""))
                                    fn = fn.Substring(1, fn.Length - 2).Replace("\"\"", "\"");
                            }
                            else if (m.Groups[17].Value == "SESSION") //17,18
                                session = int.Parse(m.Groups[18].Value);
                        }
                    }
                    if (entry != null)
                    {
                        entry.OffsetIndexes.Sort();
                        items.Add(entry);
                    }

                    if (items.Count != 0)
                        return new IndexFile(path, fileName, extension, postFix, 0, content.Length, Nanook.NKit.Crc.Compute(content), isTemp, isArchived, content) { FileType = IndexFileType.Cue, Items = items.ToArray(), Additional = addFiles };
                }
            }
            return null;
        }

        public bool WiiUFstMismatch { get; set; } //make more generic

        public IndexFileType FileType { get; set; }

        public SourceFileTrack[] Items { get; internal set; }
        internal List<FileItem> Additional { get; private set; }

        //https://github.com/mamedev/mame/blob/8518da9e35ad5b1baf94ac07312d0d3300363a75/src/tools/chdman.cpp#L1319
        //void output_track_metadata(int mode, util::core_file &file, int tracknum, const trackType.track_info &info, const std::string &filename, uint32_t frameoffs, uint64_t discoffs)

        public static string ToGdi(List<string> fileNames, SourceFileTrack[] tracks)
        {
            StringBuilder sb = new StringBuilder();
            string qt = "";
            foreach (SourceFileTrack track in tracks)
            {
                if (track.TrackIndex == 1)
                    sb.Append($"{tracks.Length}\r\n"); //GDI starts with no. of tracks

                int mode = track.TrackType == IndexTrackType.Audio ? 0 : 4; //audio is .raw else .bin
                qt = fileNames[track.TrackIndex - 1].Contains(' ') ? "\"" : "";
                sb.Append($"{track.TrackIndex} {track.BlockIdx} {mode} {track.BlockSize} {qt}{fileNames[track.TrackIndex - 1]}{qt} 0\r\n");
            }
            return sb.ToString();
        }

        public static string ToCue(List<string> fileNames, bool split, SourceFileTrack[] tracks)
        {
            StringBuilder sb = new StringBuilder();
            foreach (SourceFileTrack track in tracks)
            {
                if (!string.IsNullOrEmpty(track.Comment))
                    sb.Append($"REM {track.Comment}\r\n");

                // first track specifies the file
                if (track.TrackIndex == 1 || split)
                    sb.Append($"FILE \"{fileNames[track.TrackIndex - 1]}\" BINARY\r\n");

                // determine submode
                switch (track.TrackType)
                {
                    case IndexTrackType.Mode1:
                    case IndexTrackType.Mode1Raw:
                        sb.Append($"  TRACK {track.TrackIndex:00} MODE1/{track.BlockSize}\r\n");
                        break;

                    case IndexTrackType.Mode2:
                    case IndexTrackType.Mode2Form1:
                    case IndexTrackType.Mode2Form2:
                    case IndexTrackType.Mode2FormMix:
                    case IndexTrackType.Mode2Raw:
                        sb.Append($"  TRACK {track.TrackIndex:00} MODE2/{track.BlockSize}\r\n");
                        break;

                    case IndexTrackType.Audio:
                        sb.Append($"  TRACK {track.TrackIndex:00} AUDIO\r\n");
                        break;
                }

                long adjust = 0;
                if (track.PreGap > 0)
                {
                    if (track.PreGapDataSize == 0)
                        sb.Append($"    PREGAP {msf(split ? 0 : track.PreGap)}\r\n");
                    else
                    {
                        sb.Append($"    INDEX 00 {msf(split ? 0 : track.BlockIdx)}\r\n");
                        adjust = track.PreGap;
                    }
                }
                sb.Append($"    INDEX 01 {msf((split ? 0 : track.BlockIdx) + adjust)}\r\n");

                // output POSTGAP
                if (track.PostGap > 0)
                    sb.Append($"    POSTGAP {msf(track.PostGap)}\r\n");

            }
            // If this is bin/cue output and the CHD contains subdata, warn the user and don't include
            // the subdata size in the CiBuffer calculation.
            //uint32_t output_frame_size = trackinfo.datasize + ((trackinfo.subtype != trackType.CD_SUB_NONE) ? trackinfo.subsize : 0);
            //if (trackinfo.subtype != trackType.CD_SUB_NONE && ((mode == MODE_CUEBIN) || (mode == MODE_GDI)))
            //{
            //    printf("Warning: Track %d has subcode data.  bin/cue and gdi formats cannot contain subcode data and it will be omitted.\r\n", tracknum + 1);
            //    printf("       : This may affect usage of the output image.  Use bin/toc output to keep all data.\r\n");
            //    output_frame_size = trackinfo.datasize;
            //}
            return sb.ToString();
        }

        public static string ToToc(List<string> fileNames, CdType cdType, SourceFileTrack[] tracks)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{cdType}\r\n\r\n\r\n");

            foreach (SourceFileTrack track in tracks)
            {
                sb.Append($"// Track {track.TrackIndex}\r\n");

                // write out the track type
                string modesubmode;
                if (track.ChdSubType != CdSubType.None)
                    modesubmode = $"{GetIsoTypeString(track.TrackType)} {GetIsoSubTypeString(track.ChdSubType)}";
                else
                    modesubmode = GetIsoTypeString(track.TrackType);
                sb.Append($"TRACK {modesubmode}\r\n");

                // write out the attributes
                sb.Append("NO COPY\r\n");
                if (track.TrackType == IndexTrackType.Audio)
                {
                    sb.Append("NO PRE_EMPHASIS\r\n");
                    sb.Append("TWO_CHANNEL_AUDIO\r\n");
                }

                // output pregap
                if (track.PreGap > 0)
                    sb.Append($"ZERO {modesubmode} {msf(track.PreGap)}\r\n");

                if (track.TrackIndex == 1) // all tracks but the first one have a file offset
                    sb.Append($"DATAFILE \"<filename>.bin\" {msf(track.Blocks)} // length in bytes: {track.Size}\r\n");
                else
                    sb.Append($"DATAFILE \"<filename>.bin\" #{track.ImageOffset} {msf(track.Blocks)} // length in bytes: {track.Size}\r\n");

                if (track.PreGap > 0) // tracks with pregaps get a START marker too
                    sb.Append($"START {msf(track.PreGap)}\r\n");

                sb.Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        public static string GetIsoTypeString(IndexTrackType trktype)
        {
            switch (trktype)
            {
                case IndexTrackType.Mode1: return "MODE1";
                case IndexTrackType.Mode1Raw: return "MODE1_RAW";
                case IndexTrackType.Mode2: return "MODE2";
                case IndexTrackType.Mode2Form1: return "MODE2_FORM1";
                case IndexTrackType.Mode2Form2: return "MODE2_FORM2";
                case IndexTrackType.Mode2FormMix: return "MODE2_FORM_MIX";
                case IndexTrackType.Mode2Raw: return "MODE2_RAW";
                case IndexTrackType.Audio: return "AUDIO";
                default: return "UNKNOWN";
            }
        }

        public static string GetIsoSubTypeString(CdSubType subtype)
        {
            switch (subtype)
            {
                case CdSubType.Normal: return "RW";
                case CdSubType.Raw: return "RW_RAW";
                default: return "NONE";
            }
        }

        private static string msf(long frames) => $"{frames / (75 * 60):00}:{frames / 75 % 60:00}:{frames % 75:00}";
    }


}
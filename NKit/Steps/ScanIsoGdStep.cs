using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{

    internal class ScanIsoGdStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private string _outExt;
        private string[] _fmt;
        private bool _readData;

        private SourceFileTrack[] _tracks;
        private long _size;
        private ContainerType _container;
        private bool _split;
        private string _binaryType;
        private string _audioType;
        private string _subType;
        private string _dataSize;
        private bool _multiTrackSplit;
        private List<string> _fileNames;

        private Queue<Tuple<long, long>> _skipBlocks; //padding added to CHD to be skipped
        private Tuple<long, long> _skipNext; //current padding to be skipped

        private GdRomWriter _writer;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FolderIndex;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepScanIsoGd;

        public override string ProposedName() => _outName;

        internal ScanIsoGdStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            string[] fmt = context.StepConfig.Split(':');
            _outName = context.SourceImageName;

            //if (fmt[0] == "gdi")
            //{
            if (context.SystemType != SystemType.Dreamcast)
                throw new HandledException($"Convert to '{fmt[0] ?? ""}' format is not recognised or not supported for System {context.SystemType}");

            _outExt = fmt[0];
            _fmt = new[] { fmt[0], "split", "bin", "raw", "", "" };
            //    context.AddSettingsInfo("ConvertTo", $"{_fmt[0]}");
            //}
            //else if (fmt[0] == "cue" || fmt[0] == "toc")
            //{
            //    _outExt = fmt[0];
            //    _fmt = new[] {
            //        fmt[0],
            //        fmt.Length < 2 || string.IsNullOrWhiteSpace(fmt[1]) ? "split" : fmt[1],  //split / joined
            //        fmt.Length < 3 || string.IsNullOrWhiteSpace(fmt[2]) ? "bin" : fmt[2], //binary type
            //        fmt.Length < 4 || string.IsNullOrWhiteSpace(fmt[3]) ? "bin" : fmt[3], //audio type
            //        fmt.Length < 5 || string.IsNullOrWhiteSpace(fmt[4]) ? "sub" : fmt[4], //sub type
            //        fmt.Length < 6 || string.IsNullOrWhiteSpace(fmt[5]) ? "" : fmt[5]  //force data size - empty is keep source
            //    };

            //    if (context.SystemType == SystemType.Dreamcast)
            //        _fmt = new[] { fmt[0], "split", "bin", "bin", "", "" };

            //    if (!Regex.IsMatch(_fmt[1], "^(split|joined)$"))
            //        throw new HandledException($"Convert - {_fmt[0]} format requires settings 'type' to be split|joined - e.g. '{fmt[0]}:split:bin:bin'");
            //    if (!Regex.IsMatch(_fmt[2], "^(bin|img|iso)$"))
            //        throw new HandledException($"Convert - {_fmt[0]} format requires settings 'binaryType' to be bin|img|iso - e.g. '{_fmt[0]}:split:bin:bin'");
            //    if (!Regex.IsMatch(_fmt[3], "^(bin|raw)$"))
            //        throw new HandledException($"Convert - {_fmt[0]} format requires settings 'audioType' to be bin|raw - e.g. '{_fmt[0]}:split:bin:bin'");
            //    //if (!Regex.IsMatch(_fmt[3], "^(bin|raw|wav|flac)$"))
            //    //    throw new HandledException($"Convert - {fmt[0]} format requires settings 'audioType' to be bin|raw|wav|flac - e.g. '{fmt[0]}:split:bin:bin:sub:'");
            //    //if (!Regex.IsMatch(_fmt[4], "^(sub)$"))
            //    //    throw new HandledException($"Convert - {_fmt[0]} format requires settings 'subType' to be sub - e.g. '{_fmt[0]}:split:bin:bin'");
            //    //if (!Regex.IsMatch(_fmt[5], "^(|2352)$"))
            //    //    throw new HandledException($"Convert - {_fmt[0]} format requires settings 'dataSize' to be empty or 2352 - e.g. '{_fmt[0]}:split:bin:bin'");

            //    context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} (Type:{_fmt[1]}, BinaryType:{_fmt[2]}, AudioType:{_fmt[3]}"); //, SubType:{_fmt[4]}, DataSize:{_fmt[5]})");
            //}
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            _size = _context.ImageSize;
            _container = ContainerType.Cue; //(ContainerType)Enum.Parse(typeof(ContainerType), _fmt[0], true);
            _split = _fmt[1] == "split";
            _binaryType = _fmt[2];
            _audioType = _fmt[3];
            _subType = _fmt[4];
            _dataSize = _fmt[5];
            _tracks = _context.ImageInfo.Tracks?.Select(a => a.Clone()).ToArray();

            _multiTrackSplit = _split && (_tracks?.Length ?? 0) > 1;
            _fileNames = new List<string>();
        }

        public override void Process(ISection section)
        {
            if (!_readData)
            {
                _readData = true;
                DreamcastFixData fix = _context.Settings.FixData<DreamcastFixData>();

                _writer = new GdRomWriter(fix, _tracks, _container == ContainerType.Gdi, base.OutStream);

                _skipBlocks = new Queue<Tuple<long, long>>(); //currently only used by chd

                adjustChdTracks();

                bool toTosec = _container == ContainerType.Gdi;
                bool found = fix.YamlLoaded && ((toTosec && (fix?.Image?.TosecDiscs?.Count ?? 0) != 0) || (!toTosec && (fix?.Image?.RedumpDiscs?.Count ?? 0) != 0));
                string ms = toTosec ? " - audio alignment unchanged" : " - audio alignment set to sample 0";
                string msg = $"Dreamcast fix yaml {(fix.YamlLoaded ? "item " : "")}{(found ? "" : "not ")}found [{(toTosec ? "tosec" : "redump")}:{fix.Image.Id}]{(found ? "" : ms)}";
                _context.Log?.Info(() => msg);
            }

            base.Process(section);

            if (_skipNext != null) //currently only chd
            {
                int lead = (int)Math.Min(section.Size, _skipNext.Item1 - section.ImageOffset);
                if (lead > 0)
                    writeData(section.Encrypted, 0, lead, section.AreaInfo.BlockSize); //decrypted if no encryption

                int post = (int)Math.Max(0, section.ImageOffset + section.Size - _skipNext.Item2);
                if (post > 0)
                    writeData(section.Encrypted, (int)(section.Size - post), post, section.AreaInfo.BlockSize); //decrypted if no encryption

                if (section.ImageOffset + section.Size >= _skipNext.Item2)
                    _skipNext = _skipBlocks.Count == 0 ? null : _skipBlocks.Dequeue();
            }
            else
                writeData(section.Encrypted, 0, (int)section.Size, section.AreaInfo.BlockSize); //decrypted if no encryption
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            _writer.CloseTrack();
            _writer.Finalise(); //forces the track data to be updated

            //string fn = Path.Combine(base.Context.WritePath, $"{Path.GetFileName(_context.SourceImageName)}.{_container.ToString().ToLower()}");

            //if (_container == ContainerType.Cue)
            //    File.WriteAllText(fn, IndexFile.ToCue(_fileNames, _split, _tracks));
            //else if (_container == ContainerType.Gdi)
            //    File.WriteAllText(fn, IndexFile.ToGdi(_fileNames, _tracks));
            //else if (_container == ContainerType.Toc)
            //    File.WriteAllText(fn, IndexFile.ToToc(_fileNames, _context.ImageInfo.CdDiscType ?? CdType.Cdrom, _tracks));

            //base.OutStream.AdditionalFiles.Add(new Part() { FileName = fn, IsIndex = true, IsImageName = true });

            base.ProcessResults();
        }

        private void writeData(byte[] buff, int offset, int sz, int readBlockSize)
        {
            SourceFileTrack t = _fileNames.Count == 0 ? null : _tracks[_fileNames.Count - 1];
            int total = 0;
            int pad = _tracks.Length.ToString().Length;
            if (_container == ContainerType.Gdi && pad < 2)
                pad = 2;

            while (total != sz)
            {

                if (t == null || (_split && t.Size == _writer.ProcessedBytes))
                {
                    _writer.CloseTrack();

                    t = _tracks[_fileNames.Count];

                    string ext = t.TrackType == IndexTrackType.Audio ? _audioType : _binaryType;
                    string name;
                    if (_container != ContainerType.Gdi)
                        name = _multiTrackSplit ? $"{_context.SourceImageName} (Track {t.TrackIndex.ToString().PadLeft(pad, '0')})" : _context.SourceImageName;
                    else
                        name = $"track{t.TrackIndex.ToString().PadLeft(pad, '0')}";
                    _fileNames.Add($"{name}.{ext}");
                    string fullName = Path.Combine(base.Context.WritePath, _fileNames[_fileNames.Count - 1]);

                    _writer.OpenTrack(fullName, t.TrackIndex - 1);
                }

                int write = !_split ? sz : (int)Math.Min(sz - total, t.Size - _writer.ProcessedBytes);
                _writer.WriteData(buff, offset + total, write, readBlockSize);
                total += write;
            }
        }

        private void adjustChdTracks()
        {
            if (_tracks.Any(a => a.Pad != 0)) //pad is currently only used by chd
            {
                int remFrm = 0;
                long remByte = 0;

                foreach (SourceFileTrack trk in _tracks)
                {
                    trk.BlockIdx -= remFrm; //adjust for removed gaps
                    trk.ImageOffset -= remByte;

                    if (trk.Pad != 0) //remove pad areas
                    {
                        int b = trk.Pad;
                        long sz = b * trk.BlockSize;
                        _skipBlocks.Enqueue(new Tuple<long, long>(trk.ImageOffset + trk.Size + remByte - sz, trk.ImageOffset + trk.Size + remByte));
                        trk.Blocks -= b;
                        trk.Size -= sz;
                        remFrm += b;
                        remByte += sz;
                    }
                }
                if (_skipBlocks.Count != 0)
                    _skipNext = _skipBlocks.Dequeue();
            }
        }
    }
}
using Nanook.NKit.Configuration;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{

    internal class WipeIsoGdChdStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private string _outExt;
        private string[] _fmt;
        private bool _readData;

        private long _lastAreaOffset;

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
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FolderIndex;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepWipeIsoGdChd;

        public override string ProposedName() => _outName;

        private string rot13(string name) => Regex.Replace(name, Regex.Escape(_context.SourceImageName), _outName, RegexOptions.IgnoreCase);

        internal WipeIsoGdChdStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Use configuration provider for validation and parsing instead of hardcoded values
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(context.SystemType, context.StepConfig ?? "cue");
            if (!validationResult.IsValid)
                throw new HandledException($"Wipe - {validationResult.ErrorMessage}");

            _outName = context.SourceImageName.Rot13Words();

            // Parse format configuration using unified configuration provider
            string configToUse = string.IsNullOrEmpty(context.StepConfig) ? "cue" : context.StepConfig;
            object formatConfig = ConfigSettingsFormatParser.ParseFormatConfiguration(configToUse, context.SystemType);

            switch (formatConfig)
            {
                case GdiFormatConfiguration gdiConfig:
                    if (context.SystemType != SystemType.Dreamcast)
                        throw new HandledException($"Convert to '{gdiConfig.FormatType}' format is not recognised or not supported for System {context.SystemType}");

                    _outExt = gdiConfig.FormatType;
                    _fmt = new[] { gdiConfig.FormatType, ConfigSettingsConstants.CueTypeSplit, ConfigSettingsConstants.BinaryExtensionBin, ConfigSettingsConstants.AudioExtensionRaw, "", "" };
                    context.AddSettingsInfo("ConvertTo", $"{_fmt[0]}");
                    break;

                case CueFormatConfiguration cueConfig:
                    _outExt = cueConfig.FormatType;

                    // Build the format array for compatibility with existing code - all defaults applied by provider
                    _fmt = new[] {
                        cueConfig.FormatType,
                        cueConfig.CueType,
                        cueConfig.BinaryExtension,
                        cueConfig.AudioExtension,
                        cueConfig.SubType,
                        cueConfig.DataSize
                    };

                    // Log configuration warnings from centralized provider
                    foreach (string warning in cueConfig.Warnings)
                    {
                        context.Log?.Info(() => $"Warning: {warning}");
                    }

                    context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} (Type:{_fmt[1]}, BinaryType:{_fmt[2]}, AudioType:{_fmt[3]})");
                    break;

                default:
                    throw new HandledException($"Wipe format configuration type '{formatConfig.GetType().Name}' is not supported by this step");
            }
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;

            _lastAreaOffset = -1;
            _size = _context.ImageSize;
            _container = ContainerType.Cue; //(ContainerType)Enum.Parse(typeof(ContainerType), _fmt[0], true);
            _split = _fmt[1] == "split";
            _binaryType = _fmt[2];
            _audioType = _fmt[3];
            _subType = _fmt[4];
            _dataSize = _fmt[5];
            _tracks = _context.ImageInfo.Tracks?.Select(a => a.Clone()).ToArray();
            if (_tracks != null)
            {
                foreach (SourceFileTrack t in _tracks)
                    t.FileName = rot13(t.FileName);
            }
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

            //don't do when not new 0 base FS
            if (section.AreaInfo.BaseOffset == 0 && section.AreaInfo.ImageOffset != _lastAreaOffset)
                _lastAreaOffset = section.AreaOffset;

            if (section.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in section.Items)
                {
                    if (si.File != null && !si.FsFile.IsSystemFile)
                    {
                        if (!section.AreaInfo.IsEncrypted || _context.SourceFile.Key != null) //don't wipe encrypted files when no key
                            section.Write((int)si.File.FsOffset, ByteStream.Zeros, (int)si.File.FsSize);
                    }
                    //don't clear gaps
                }

                int blockSize = section.AreaInfo.BlockSize;
                for (int i = 0; i < section.Size; i += blockSize)
                    Ecm.ReconstructEcc(section.Decrypted, i, blockSize);
            }

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

            string fn = $"{_outName}.{_container.ToString().ToLower()}";
            string file;
            if (_container == ContainerType.Gdi)
                file = IndexFile.ToGdi(_fileNames, _tracks);
            else
                file = IndexFile.ToCue(_fileNames, _split, _tracks);

            base.OutStream.WriteAdditionalFile(Encoding.Default.GetBytes(file), 0, -1, fn, true, true);

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
                    _fileNames.Add(t.FileName);
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
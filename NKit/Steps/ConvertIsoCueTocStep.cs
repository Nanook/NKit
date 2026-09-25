using Nanook.NKit.Configuration;
using Nanook.NKit.Steps.Shared;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit
{

    internal class ConvertIsoCueTocStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private string _outExt;
        private string[] _fmt;

        private SourceFileTrack[] _tracks;
        private long _size;
        private ContainerType _container;
        private bool _split;
        private string _binaryType;
        private string _audioType;
        //private string _subType;
        //private string _dataSize;
        //private byte[] _key;
        //private int _shift;
        //private int _align;
        private bool _multiTrackSplit;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsFix => false;
        internal override bool ContractIsExpand => true;
        internal override OutputType ContractOutputType => OutputType.FolderIndex;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertIsoCueToc;

        public override string ProposedName() => _outName;

        internal ConvertIsoCueTocStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Use configuration service to validate the entire format string first
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(context.SystemType, context.StepConfig);
            if (!validationResult.IsValid)
                throw new HandledException($"Convert - {validationResult.ErrorMessage}");

            _outName = context.SourceImageName;

            // Parse format configuration using unified configuration parser - no manual parsing needed
            object formatConfig = ConfigSettingsFormatParser.ParseFormatConfiguration(context.StepConfig, context.SystemType);

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

                    // Build the format array for compatibility with existing code - all defaults applied by parser
                    _fmt = new[] {
                        cueConfig.FormatType,
                        cueConfig.CueType,
                        cueConfig.BinaryExtension,
                        cueConfig.AudioExtension,
                        cueConfig.SubType,
                        cueConfig.DataSize
                    };

                    // Log configuration warnings from centralized parser
                    foreach (string warning in cueConfig.Warnings)
                    {
                        context.Log?.Info(() => $"Warning: {warning}");
                    }

                    context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} (Type:{_fmt[1]}, BinaryType:{_fmt[2]}, AudioType:{_fmt[3]})");
                    break;

                default:
                    throw new HandledException($"Convert format configuration type '{formatConfig.GetType().Name}' is not supported by this step");
            }
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            //_key = _context.Key;
            _size = _context.ImageSize;
            _container = (ContainerType)Enum.Parse(typeof(ContainerType), _fmt[0], true);
            _split = _fmt[1] == "split";
            _binaryType = _fmt[2];
            _audioType = _fmt[3];
            //_subType = _fmt[4];
            //_dataSize = _fmt[5];
            _tracks = _context.ImageInfo.Tracks?.Select(a => a.Clone()).ToArray();

            _multiTrackSplit = _split && (_tracks?.Length ?? 0) > 1;

            int pad = _tracks.Length.ToString().Length;
            for (int i = 1; i <= _tracks.Length; i++)
            {
                string tn = "";
                if (_multiTrackSplit)
                    tn = $" (Track {i.ToString().PadLeft(pad, '0')})";
                string ext = _tracks[i - 1].TrackType == IndexTrackType.Audio ? _audioType : _binaryType;
                _tracks[i - 1].FileName = $"{_context.SourceImageName}{tn}.{ext}";
            }

            if (_container == ContainerType.Gdi)
            {
                _container = ContainerType.Cue;
                _context.Log?.Info(() => "GDI not supported for non-GDRom images, CUE will be used");
            }
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.ImageOffset == 0)
                this.OutStream.NewPart(Path.GetFileNameWithoutExtension(_tracks[0].FileName), Path.GetExtension(_tracks[0].FileName), true);
            writeData(section.Encrypted, 0, (int)section.Size); //decrypted if no encryption
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            string fn = $"{Path.GetFileName(_context.SourceImageName)}.{_container.ToString().ToLower()}";

            int blockIdx = 0;
            long off = 0;

            foreach (SourceFileTrack trk in _tracks)
            {
                trk.Blocks = (int)(trk.Size / trk.BlockSize);
                trk.BlockIdx = blockIdx;
                trk.Pad = 0;
                trk.ImageOffset = off;
                off += trk.Size;
                blockIdx += trk.Blocks;
            }

            string file;
            if (_container == ContainerType.Toc)
                file = IndexFile.ToToc(_tracks.Select(a => Path.GetFileName(a.FileName)).ToList(), _context.ImageInfo.CdDiscType ?? CdType.Cdrom, _tracks);
            else
                file = IndexFile.ToCue(_tracks.Select(a => Path.GetFileName(a.FileName)).ToList(), _split, _tracks);

            base.OutStream.WriteAdditionalFile(Encoding.Default.GetBytes(file), 0, -1, fn, true, true);

            base.ProcessResults();
        }

        private void writeData(byte[] buff, int offset, int sz)
        {
            int files = this.OutStream.ChecksummedFiles.Count;
            SourceFileTrack t = _tracks[files];
            int total = 0;

            while (total != sz)
            {
                if (_split && t.Size == base.OutStream.Position)
                {
                    bool cached = false;

                    t = _tracks[files + 1];
                    this.OutStream.NewPart(Path.GetFileNameWithoutExtension(t.FileName), Path.GetExtension(t.FileName), true);
                    if (cached)
                        return; //don't write next track
                }

                int write = !_split ? sz : (int)Math.Min(sz - total, t.Size - base.OutStream.Position);
                base.OutStream.Write(buff, offset + total, write); //decrypted if no encryption
                total += write;
            }
        }

    }
}
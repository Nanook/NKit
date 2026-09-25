using Nanook.NKit.Configuration;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{

    internal class ConvertWiiUWuxStep : StepBase, IStep
    {
        private IStepContext _context;
        //private string _outFilename;
        private string _outName;
        private string _outExt;
        //private string[] _fmt;

        private const int _blkSz = 0x8000;
        private const int _hdrSize = 0x20;

        private long _size;
        private byte[] _hdr;
        private Dictionary<ulong, int> _blocks;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertWiiUWux;

        public override string ProposedName() => $"{_outName}.{_outExt}"; //return null if not accurate - used for skipping existing images

        internal ConvertWiiUWuxStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            string[] fmt = context.StepConfig.Split(':');

            // Validate that this format is supported for the system
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(context.SystemType);
            if (!supportedFormats.Contains(ConfigSettingsConstants.FormatWux, StringComparer.OrdinalIgnoreCase))
            {
                throw new HandledException($"Convert to '{ConfigSettingsConstants.FormatWux}' format is not supported for System {context.SystemType}");
            }

            _outName = context.SourceImageName;
            _outExt = fmt[0];
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            SourceImageType sourceType = _context.SourceFile.ImageType;
            context.AddSettingsInfo("ConvertTo", _outExt);


            if (_context.SystemType == SystemType.WiiU && sourceType == SourceImageType.TmdApp)
            {
                if (_context.SourceFile.IndexFile.Items.Any(a => a.FileIsMissing))
                    throw new Exception("Cannot convert from App/CDN when files are missing");
            }

            _size = _context.ImageSize;
            _blocks = new Dictionary<ulong, int>();

            int hdrSize = (int)(_hdrSize + (((_size / _blkSz) + (_size % _blkSz != 0 ? 1 : 0)) * 4));
            if (hdrSize % _blkSz != 0)
                hdrSize += _blkSz - (hdrSize % _blkSz);

            _hdr = new byte[hdrSize];

            _hdr.WriteString(0, 4, "WUX0");
            _hdr.WriteUInt32L(0x04, 0x1099D02E);
            _hdr.WriteUInt32L(0x08, _blkSz);
            _hdr.WriteUInt64L(0x10, (ulong)_size);
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.ImageOffset == 0)
            {
                base.OutStream.NewPart(_outName, _outExt, true);
                base.OutStream.Write(_hdr, 0, _hdr.Length);
                base.OutStream.CrcSplit();
            }

            if (section.Size % _blkSz != 0)
            {
                if (section.ImageOffset + section.Size != _size)
                    throw new HandledException($"Section not divisable by {_blkSz:X} implemented for WiiU/Wux");
                else
                    Array.Clear(section.Encrypted, (int)section.Size, section.Decrypted.Length - (int)section.Size);
            }

            for (int i = 0; i < section.Size; i += _blkSz)
            {
                ulong hash = XXHash64.Compute(section.Encrypted, i, _blkSz);
                int idx;
                if (!_blocks.TryGetValue(hash, out idx))
                {
                    idx = _blocks.Count;
                    _blocks.Add(hash, idx);
                    base.OutStream.Write(section.Encrypted, i, _blkSz); //store
                }
                _hdr.WriteUInt32L(_hdrSize + (int)(((section.ImageOffset + i) / _blkSz) << 2), (uint)idx);
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            base.OutStream.CrcPatch(0, s =>
            {
                s.Write(_hdr, 0, _hdr.Length);
            });
            base.ProcessResults();
        }

    }
}
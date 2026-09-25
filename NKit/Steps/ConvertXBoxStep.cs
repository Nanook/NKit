using Nanook.NKit.Microsoft.XBox;
using Nanook.NKit.Steps.Shared;
using System;
using System.IO;

namespace Nanook.NKit
{
    internal class ConvertXBoxStep : StepBase, IStep
    {
        private bool _writeFiller = false;
        private bool _wipeFiller = true;
        private bool _xisoMode = true;

        private bool _imageWrite = false;
        private bool _isXDvdFs = false;


        private IStepContext _context;
        private string _outName;
        private string _outExt = "xiso";
        private FileStream _fillerFs;
        private string _fillerFileName;
        private const int SectorSize = 2048;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertXBox;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ConvertXBoxStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
            context.AddSettingsInfo("ConvertTo", _outExt);
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context = context;

            // prepare filler file (overwrite if exists)
            if (_writeFiller)
            {
                _fillerFileName = Path.Combine(_context.WritePath, $"{_outName}.filler");
                _fillerFs = new FileStream(_fillerFileName, FileMode.Create, FileAccess.Write, FileShare.None, 0x200000);
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void Process(ISection section)
        {
            if (section.AreaOffset == 0)
            {
                _isXDvdFs = section.FullAreaFileSystem is Fst;

                // create output part for the image on first section
                _imageWrite = !_xisoMode || _isXDvdFs;
                if (_imageWrite)
                    base.OutStream.NewPart(_outName, _outExt, true);
            }

            base.Process(section);

            // Only process filesystem areas that are XDVDFS (game partition) - skip video partitions
            if (section.Type == AreaType.FileSystem && _isXDvdFs)
            {
                foreach (SectionItem si in section.Items)
                {
                    if (si?.Gap != null)
                    {
                        int offset = (int)si.Gap.Offset;
                        int sz = (int)si.Gap.FsSize;
                        int r = offset % SectorSize;
                        if (r != 0)
                        {
                            r = SectorSize - r;
                            offset += r;
                            sz -= r;
                        }
                        sz -= sz % SectorSize;

                        if (sz > 0)
                        {
                            if (_fillerFs != null)
                                _fillerFs.Write(section.Encrypted, offset, sz);
                            if (_wipeFiller)
                                Array.Clear(section.Encrypted, offset, sz);
                        }
                    }
                }
            }

            if (_imageWrite)
                base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
        }

        public override void ProcessResults()
        {
            // close filler file if opened
            try
            {
                if (_fillerFs != null)
                {
                    _fillerFs.Flush();
                    _fillerFs.Close();
                    _fillerFs.Dispose();
                    _fillerFs = null;
                }
            }
            catch { }

            base.ProcessingComplete();
            base.ProcessResults();
        }
    }
}
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    internal class ExpandWiiUAppTmdStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private string _outExt;
        private List<SourceFileItem> _files;
        private int _fileIndex;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FolderIndex;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExpandWiiUAppTmd;

        public override string ProposedName()
        {
            // For folder/index outputs we should not append an extension - the
            // output is a folder (e.g., a TMD/APP folder) and adding ".tmd"
            // or similar is incorrect for multipart folder-based items.
            if (this.Context?.StepInfo?.OutputType == OutputType.FolderIndex)
                return _outName;
            return $"{_outName}.{_outExt}";
        }

        internal ExpandWiiUAppTmdStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
            _outExt = context.StepConfig ?? "iso";
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context = context;

            // Gather all files from the source image context (primarily those from the linear stream)
            _files = _context.SourceFile.ImageFiles.Where(a => a.Offset != -1).OrderBy(a => a.Offset).ToList();
            _fileIndex = 0;

            // Handle metadata files from the DataStore file table (stored in Additional list)
            if (_context.SourceFile.IndexFile?.Additional != null)
            {
                foreach (FileItem fi in _context.SourceFile.IndexFile.Additional)
                {
                    // Only write if it's not already in the main ImageFiles list with a valid image stream offset (to avoid duplicates)
                    if (fi.Data != null && !_files.Any(a => a.FileName.Equals(fi.FileName, StringComparison.OrdinalIgnoreCase)))
                        base.OutStream.WriteAdditionalFile(fi.Data, 0, fi.Data.Length, fi.FileName, false, false);
                }
            }

            // Also write the index file (TMD) itself
            if (_context.SourceFile.IndexFile?.Data != null)
                base.OutStream.WriteAdditionalFile(_context.SourceFile.IndexFile.Data, 0, _context.SourceFile.IndexFile.Data.Length, _context.SourceFile.IndexFile.FileName, true, false);
        }

        public override void Process(ISection section)
        {
            if (_files != null)
            {
                // Check if this section marks the start of a new area/file in the linear stream
                while (_fileIndex < _files.Count && _files[_fileIndex].Offset <= section.ImageOffset)
                {
                    SourceFileItem fi = _files[_fileIndex];
                    if (fi.Offset == section.ImageOffset && section.AreaOffset == 0)
                    {
                        // Start new output part with original name and extension
                        base.OutStream.NewPart(fi.NameOnly, fi.Extension, false);
                    }
                    else if (fi.Offset < section.ImageOffset)
                    {
                        // Already skipped/passed this file
                    }
                    else
                    {
                        // File starts after this section
                        break;
                    }
                    _fileIndex++;
                }
            }

            base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            base.ProcessResults();
        }

        public void Patched(ISection section) { }
    }
}
using Nanook.NKit.Steps.Shared;
using System;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Converts a WUA (ZArchive) file to a Loadiine folder by writing each section's
    /// raw bytes to the corresponding file under <c>WritePath</c>.
    ///
    /// Source:  <see cref="FolderFilesAsIso"/> → <see cref="FolderFilesImage"/> feeds one
    ///          <see cref="AreaType.Other"/> area per file, section by section.
    /// Output:  Flat folder tree matching the WUA's internal directory layout.
    ///
    /// Each section carries:
    ///   <c>section.AreaInfo.Properties["FileName"]</c> — relative file path
    ///   <c>section.AreaOffset</c> — byte offset within the current file (0 = file start)
    ///   <c>section.Decrypted</c>  — raw file bytes for this section
    /// </summary>
    internal class ConvertWuaLoadiineStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private int _extracted;
        private string _currentFile;
        private FileStream _currentStream;

        internal override bool ContractIsLossy    => true;
        internal override bool ContractIsExpand   => false;
        internal override bool ContractIsFix      => false;
        internal override bool ContractReqPatch   => false;
        internal override bool ContractReqChk     => false;
        internal override bool ContractFullScan   => true;
        internal override OutputType ContractOutputType => OutputType.FolderFiles;
        internal override bool ContractCanCrc     => false;
        internal override bool ContractCanHash    => false;
        internal override string ComponentTag     => LogScopes.StepConvertWuaLoadiine;

        public override string ProposedName() => _outName;

        internal ConvertWuaLoadiineStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context   = context;
            _extracted = 0;
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.Type != AreaType.Other)
                return;

            string relPath = section.AreaInfo?.Properties?["FileName"]?.ToXmlValue() ?? "";
            if (string.IsNullOrEmpty(relPath))
                return;

            long areaOffset = section.AreaOffset;

            // New file starts when AreaOffset == 0 or filename changed
            if (areaOffset == 0 || relPath != _currentFile)
            {
                closeCurrentFile();
                _currentFile = relPath;

                string destPath = Path.Combine(_context.WritePath,
                    relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                _currentStream = new FileStream(destPath, FileMode.Create,
                    FileAccess.Write, FileShare.None, 0x80000);
                _extracted++;
            }

            _currentStream?.Write(section.Decrypted, 0, (int)section.Size);
        }

        public void Patched(ISection section) { }

        public override void ProcessResults()
        {
            closeCurrentFile();
            base.ProcessingComplete();
            Context.Result.ProgressSummary = $"Extracted {_extracted} file{_extracted.s()}";
            base.ProcessResults();
        }

        private void closeCurrentFile()
        {
            if (_currentStream != null)
            {
                try { _currentStream.Dispose(); } catch { }
                _currentStream = null;
            }
            _currentFile = null;
        }
    }
}

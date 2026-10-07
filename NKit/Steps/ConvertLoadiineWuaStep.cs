using Nanook.NKit.Container.ZArchive;
using Nanook.NKit.Steps.Shared;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    /// <summary>
    /// Converts a Loadiine folder to a WUA (ZArchive) file by packing each section's
    /// raw bytes into the archive at the corresponding relative path.
    ///
    /// Source:  <see cref="FolderFilesAsIso"/> → <see cref="FolderFilesImage"/> feeds one
    ///          <see cref="AreaType.Other"/> area per file, section by section.
    /// Output:  A single <c>.wua</c> ZArchive containing the title folder tree.
    ///
    /// Each section carries:
    ///   <c>section.AreaInfo.Properties["FileName"]</c> — relative path (e.g. "code/game.rpx")
    ///   <c>section.AreaOffset</c> — byte offset within the current file (0 = file start)
    ///   <c>section.Decrypted</c>  — raw file bytes for this section
    /// </summary>
    internal class ConvertLoadiineWuaStep : StepBase, IStep
    {
        private static readonly Regex _titleIdRegex =
            new Regex(@"[0-9a-fA-F]{16}", RegexOptions.Compiled);

        private IStepContext _context;
        private readonly string _outName;
        private ZArchiveWriter _writer;
        private FileStream _wuaStream;
        private string _titleFolder;
        private string _currentFile;
        private int _written;

        internal override bool ContractIsLossy    => true;
        internal override bool ContractIsExpand   => false;
        internal override bool ContractIsFix      => false;
        internal override bool ContractReqPatch   => false;
        internal override bool ContractReqChk     => false;
        internal override bool ContractFullScan   => true;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc     => false;
        internal override bool ContractCanHash    => false;
        internal override string ComponentTag     => LogScopes.StepConvertLoadiineWua;

        public override string ProposedName() => _outName + ".wua";

        internal ConvertLoadiineWuaStep(IStepContextConstruct context) : base()
        {
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context = context;
            _written = 0;

            // Derive title folder name from the source folder name
            FolderGroupInfo group = _context.SourceFile.SyntheticFolderGroup;
            string sourceFolder   = group?.SourceFolder ?? _context.SourceFile.BasePath;
            string folderName     = Path.GetFileName(
                sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Match m = _titleIdRegex.Match(folderName ?? "");
            _titleFolder = m.Success
                ? $"{m.Value.ToLowerInvariant()}_v0"
                : $"{(folderName ?? _outName).ToLowerInvariant()}_v0";

            // Open the output WUA stream
            string outPath = Path.Combine(_context.WritePath, ProposedName());
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            _wuaStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 0x100000);
            _writer    = new ZArchiveWriter(_wuaStream);
            _writer.MakeDir(_titleFolder);
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

            if (areaOffset == 0 || relPath != _currentFile)
            {
                // Ensure the parent directory exists in the archive
                string dir = $"{_titleFolder}/{Path.GetDirectoryName(relPath)?.Replace(Path.DirectorySeparatorChar, '/')}";
                if (!string.IsNullOrEmpty(dir) && dir != _titleFolder)
                    _writer.MakeDir(dir);

                // Start a new file in the archive
                _writer.StartFile($"{_titleFolder}/{relPath}");
                _currentFile = relPath;
                _written++;
            }

            _writer.Write(section.Decrypted, 0, (int)section.Size);
        }

        public void Patched(ISection section) { }

        public override void ProcessResults()
        {
            try
            {
                _writer.FinalizeArchive();
                _writer.Dispose();
                _writer = null;
                _wuaStream?.Dispose();
                _wuaStream = null;
            }
            catch (Exception ex)
            {
                try { _writer?.Dispose(); } catch { }
                _writer = null;
                throw new HandledException(ex, "ConvertLoadiineWua: failed to finalise ZArchive");
            }

            base.ProcessingComplete();
            Context.Result.ProgressSummary = $"Wrote {_written} file{_written.s()}";
            base.ProcessResults();
        }
    }
}

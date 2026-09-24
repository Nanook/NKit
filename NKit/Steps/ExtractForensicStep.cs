using Nanook.NKit.Configuration;
using Nanook.NKit.Steps.Shared;
using Path = System.IO.Path;

namespace Nanook.NKit
{

    internal class ExtractForensicStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private FileMask _mask;
        private int _extracted;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FolderFiles;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExtractForensic;

        public override string ProposedName() => _outName;

        internal ExtractForensicStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Preserve original behavior: ExtractForensic step used hardcoded ".*" pattern
            // Only use configuration provider for advanced configuration when explicitly provided
            if (string.IsNullOrEmpty(context.StepConfig))
            {
                // Original hardcoded behavior for backwards compatibility
                _mask = new FileMask(".*", false);
            }
            else
            {
                // Use configuration provider only when explicit config is provided
                ExtractConfiguration config = ConfigSettingsFormatParser.ParseExtractConfiguration(context.StepConfig);
                _mask = new FileMask(config.Pattern ?? ".*", !config.IsCaseInsensitive);

                // Log configuration details for debugging
                context.Log?.Detail(() => $"Forensic Extract configuration - Pattern:'{config.Pattern}', CaseInsensitive:{config.IsCaseInsensitive}");
                context.AddSettingsInfo("ExtractPattern", config.Pattern);
            }

            _outName = context.SourceImageName;
            context.AddSettingsInfo("ExtractType", "Forensic");
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context = context;
        }

        public override void Process(ISection section)
        {
            base.Process(section);
            saveFileData(section);
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            _context.Log.Info(() => $"Extracted {_extracted} file{_extracted.s()}");
            base.ProcessResults();
        }

        private void saveFileData(ISection section)
        {
            if (section.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in section.Items)
                {
                    if (si.File != null && _mask.IsMatch(si.FsFile.FullName))
                    {
                        string path = getPath(section, si.FsFile.Path);

                        base.ExtractHandler.CreateDirectory(_context.WritePath, path);

                        string imagePath = Path.Combine(path, si.FsFile.Name);

                        if (base.ExtractHandler.WriteFs(section, _context.WritePath, imagePath, si.File.OffsetInItem, (int)si.File.FsOffset, (int)si.File.FsSize, si.FsFile.FsSize, false))
                            _extracted++;
                    }
                }
            }
            else if (section.Type != AreaType.Other)
            {
                string path = getPath(section, "");
                base.ExtractHandler.CreateDirectory(_context.WritePath, path);

                if (base.ExtractHandler.WriteFs(section, _context.WritePath, Path.Combine(path, "Data.bin"), section.AreaOffset, 0, (int)section.Size, -1, false))
                    _extracted++;
            }
        }

        private string getPath(ISection section, string path)
        {
            string extra = string.Format("{0}_{1}", section.AreaInfo.ImageOffset.ToString("X9"), section.AreaInfo.Type.ToString());
            return Path.Combine(extra, path.Trim('\\', '/'));
        }

    }
}
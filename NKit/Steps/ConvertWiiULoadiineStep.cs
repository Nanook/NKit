using Nanook.NKit.Container.ZArchive;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Steps.Shared;
using System;

namespace Nanook.NKit
{
    /// <summary>
    /// Converts a WiiU image (WUD, WUX, TmdApp) to Loadiine folder format or WUA (ZArchive).
    ///
    /// Loadiine is the decrypted WiiU game filesystem laid out as code/content/meta directories.
    /// Treated as a Convert (lossy) operation rather than an Extract, since the original disc
    /// structure (partition table, FST binary, content encryption and hash trees) cannot be
    /// reconstructed from the output.
    ///
    /// Inherits the file-extraction engine directly from <see cref="WiiUFileExtractBase"/>,
    /// keeping a clean separation from <see cref="ExtractWiiUStep"/> which is the Extract task.
    ///
    /// Format strings:
    ///   "loadiine"  — write decrypted files to a folder (code/content/meta)
    ///   "wua"       — write decrypted files into a ZArchive (.wua)
    /// </summary>
    internal class ConvertWiiULoadiineStep : WiiUFileExtractBase
    {
        private readonly bool _isWua;
        private ZArchiveWriter _archiveWriter;
        private ZArchiveExtractHandler _archiveHandler;
        private IStepContext _convertContext;
        private readonly Scan _srcScan;

        // ── Contracts ────────────────────────────────────────────────────────
        internal override bool ContractIsLossy   => true;
        internal override bool ContractIsExpand  => false;
        internal override bool ContractIsFix     => false;
        internal override bool ContractReqPatch  => false;
        internal override bool ContractReqChk    => false;
        internal override bool ContractFullScan  => true;  // required for FST pre-processing
        internal override OutputType ContractOutputType => _isWua ? OutputType.Image : OutputType.FolderFiles;
        internal override bool ContractCanCrc    => false;
        internal override bool ContractCanHash   => false;
        internal override string ComponentTag    =>
            _isWua ? LogScopes.StepConvertWiiUWua : LogScopes.StepConvertWiiULoadiine;

        public override string ProposedName() =>
            _isWua ? $"{base.ProposedName()}.wua" : base.ProposedName();

        internal ConvertWiiULoadiineStep(IStepContextConstruct context)
            : base(new FileMask(".*", false), context.SourceImageName)
        {
            string fmt = context.StepConfig?.ToLowerInvariant() ?? "loadiine";
            _isWua   = fmt.StartsWith("wua", StringComparison.OrdinalIgnoreCase);
            _srcScan = context.StepInfo?.SrcScan;
            context.AddSettingsInfo("ConvertTo", _isWua ? "wua" : "loadiine");
            base.CheckContract(context.StepInfo);
        }

        public override void Initialise(IStepContext context)
        {
            _convertContext = context;
            base.Initialise(context);

            if (_isWua)
            {
                base.OutStream.NewPart(ProposedName().Replace(".wua", ""), ".wua", true);
                _archiveWriter  = new ZArchiveWriter(base.OutStream);
                _archiveHandler = new ZArchiveExtractHandler(_archiveWriter);
                base.ExtractHandler = _archiveHandler;
            }
            else
            {
                if (base.ExtractHandler == null)
                    base.ExtractHandler = new ExtractFileHandler();
            }
        }

        public override void Process(ISection section)
        {
            // For WUA output: intercept PartitionHeader to assign the title folder in the archive.
            if (_isWua && _archiveHandler != null && section.Type == AreaType.PartitionHeader)
            {
                string ptnTypeStr = section.AreaInfo.Properties?.Get<string>("PartitionType", null);
                string volumeId   = section.AreaInfo.Properties?.Get<string>("VolumeId",      null);
                string titleIdStr = section.AreaInfo.Properties?.Get<string>("TitleId",       null);

                bool isGame = ptnTypeStr != null
                    && Enum.TryParse<PartitionType>(ptnTypeStr, out PartitionType ptnType)
                    && ptnType == PartitionType.Game;

                if (isGame)
                {
                    if (titleIdStr == null && volumeId != null && volumeId.Length >= 18)
                        titleIdStr = volumeId.Substring(2, 16);
                    if (titleIdStr == null)
                        titleIdStr = getScanTitleId(section.ImageOffset);
                    titleIdStr ??= "0000000000000000";
                    _archiveHandler.SetTitleFolder($"{titleIdStr.ToLowerInvariant()}_v0");
                }
                else
                {
                    _archiveHandler.SetSkip();
                }
            }

            base.Process(section);
        }

        public override void ProcessResults()
        {
            if (_isWua && _archiveWriter != null)
            {
                try   { _archiveWriter.FinalizeArchive(); }
                finally
                {
                    _archiveWriter.Dispose();
                    _archiveWriter  = null;
                    _archiveHandler = null;
                }
            }
            base.ProcessResults();
        }

        // ── WiiUFileExtractBase overrides ─────────────────────────────────────

        /// <summary>
        /// Called when the title ID is first known from the TmdApp FstBlock.
        /// Sets the WUA archive title folder if not already set by PartitionHeader.
        /// </summary>
        protected override void OnTitleIdentified(string titleId, string version)
        {
            if (!_isWua || _archiveHandler == null || _archiveHandler.TitleFolder != null) return;
            _archiveHandler.SetTitleFolder($"{titleId.ToLowerInvariant()}_v{version}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string getScanTitleId(long imageOffset)
        {
            try
            {
                Scan scan = _srcScan ?? _convertContext?.Scan;
                if (scan == null) return null;
                foreach (ScanArea area in scan.Areas)
                {
                    if (area.Type == AreaType.PartitionHeader && area.ImageOffset == imageOffset)
                    {
                        string tid = area.AreaInfo?.Properties?.Get<string>("TitleId", null);
                        if (tid != null) return tid;
                        string volId = area.AreaInfo?.Properties?.Get<string>("VolumeId", null);
                        if (volId != null && volId.Length >= 18)
                            return volId.Substring(2, 16);
                    }
                }
            }
            catch { }
            return null;
        }
    }
}

using Nanook.NKit.Configuration;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiU;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

namespace Nanook.NKit
{

    internal class ConvertWiiUAppTmdStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;

        private PartitionInfo _ptn;
        private ImageHeader _header;
        private FileSystemInfo _fsInfo;
        private ContentHeader _cntHeader;
        private SHA1 _sha;
        private Dictionary<string, List<byte[]>> _h3;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FolderIndex;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertWiiUAppTmd;

        public override string ProposedName() => _outName; //return null if not accurate - used for skipping existing images

        internal ConvertWiiUAppTmdStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            string[] fmt = context.StepConfig.Split(':');

            // Validate that this format is supported for the system
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(context.SystemType);
            if (!supportedFormats.Contains(ConfigSettingsConstants.FormatApp, StringComparer.OrdinalIgnoreCase))
            {
                throw new HandledException($"Convert to '{ConfigSettingsConstants.FormatApp}' format is not supported for System {context.SystemType}");
            }

            _outName = context.SourceImageName;
            _h3 = new Dictionary<string, List<byte[]>>();

            context.AddSettingsInfo("ConvertTo", ConfigSettingsConstants.FormatApp);
        }

        private void setFinalName(string name, string titleId, string version)
        {
            if (!name.ToLower().Contains(titleId.ToLower()))
                name += $" [{titleId}]";

            string v = version == null ? "" : $"[rev{version}]";

            if (!name.ToLower().Contains(v.ToLower()))
                name += v;

            base.SetFinalName(name);
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context = context;
            _context.SkipBlockTaskEnable();

            if (_context.SystemType == SystemType.WiiU && _context.SourceFile.ImageType == SourceImageType.TmdApp)
            {
                // Only validate missing files when the source is a genuine TmdApp/CDN
                // folder image. Disc images stored in the DataStore (Format=Iso) may
                // have a reconstructed IndexFile from metadata, but the actual content
                // is in the disc stream — missing loose app files are expected in that
                // case and the Process method handles disc sections directly.
                bool isDiscImage = _context.SourceFile.ArchiveFiles?.FirstOrDefault()?.Extension?.Equals(
                    NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase) == true
                    && _context.SourceFile.ImageFiles?.Length == 1
                    && _context.SourceFile.ImageFiles[0].Extension?.Equals(".iso", StringComparison.OrdinalIgnoreCase) == true;

                if (!isDiscImage && _context.SourceFile.IndexFile.Items.Any(a => a.FileIsMissing))
                    throw new Exception("Cannot convert from App/CDN when files are missing");
            }
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.Type != AreaType.FileSystem || section.AreaOffset == 0)
                closeAppStream();

            if (_context.SourceFile.ImageType == SourceImageType.TmdApp && section.ImageOffset == 0)
            {
                if (section.Type == AreaType.FstBlock) //source is app files
                {
                    _header = new ImageHeader(null);
                    if (_context.SourceFile.IndexFile.WiiUFstMismatch)
                        throw new Exception("Convert from App/CDN Error - TMD items do not match FST items");

                    IndexFile idx = _context.SourceFile.IndexFile;
                    SiData si = new SiData(null, idx, section.Encrypted) { AppIndex = 0, Key = Nintendo.WiiGc.WiiConsts.NKitWipeCommonKey };
                    _header.SiData.Add(si);

                    si.Complete(_header);
                    _header.Update(0, si);

                    _fsInfo = new Nanook.NKit.Nintendo.WiiU.FileSystemInfo(0, _context.ImageSize, _header, _header.SiData[0], (int)section.Size);

                    _fsInfo.ProcessBlock(0, section.Decrypted, (int)section.Size, true, _context.SourceFile.IndexFile);

                    _ptn = new PartitionInfo(PartitionType.Game, 0, 0, 0);
                    _ptn.Id = si.TmdInfo.TitleId.ToString("X16");
                    _ptn.IsMainContent = true;

                    // Use the step's output name (which is based on the reconstructed
                    // image name) when creating the final folder name. Using
                    // _context.SourceFile.Name here can capture the datastore
                    // filename if reconstruction hasn't updated the SourceFile yet.
                    setFinalName(_outName, si.TmdInfo.TitleId.ToString("X16"), si.TmdInfo.TitleVersion.ToString());
                    Directory.CreateDirectory(_context.WritePath);

                    base.OutStream.WriteAdditionalFile(si.FileCert, 0, si.FileCert.Length, "title.cert", false, false);
                    base.OutStream.WriteAdditionalFile(si.FileTicket, 0, si.FileTicket.Length, "title.tik", false, false);
                    base.OutStream.WriteAdditionalFile(si.FileTmd, 0, si.FileTmd.Length, "title.tmd", true, false);
                    base.OutStream.NewPart(si.Contents[0].ContentId.ToString("x8"), "app", false);
                    base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
                }
            }
            else if (section.Type == AreaType.ImageHeader)
                _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size)); //clone
            else if (section.Type == AreaType.RawKeyMissing)
                throw new Exception("Missing Key required to decrypt source data for Convert to App");
            else if (section.Type == AreaType.PartitionTable)
                _header.Update(section.Decrypted.Read(0, (int)section.Size)); //clone
            else if (section.Type == AreaType.PartitionHeader)
            {
                _ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset);
                if (_ptn != null && _ptn.ImageOffset == section.ImageOffset) //if not our WipePartition then skip will forward
                {
                    _fsInfo = new FileSystemInfo(section.ImageOffset, _context.ImageSize, _header, section.Decrypted);
                    if (_ptn.IsMainContent)
                    {
                        // Prefer the previously-determined output name rather than
                        // probing the SourceFile name which may be stale for datastore
                        // reconstructions.
                        setFinalName(_outName, _ptn.WiiUTitleId.ToString("X16"), null);
                        Directory.CreateDirectory(_context.WritePath);
                    }
                }
            }
            else if ((_ptn != null && _ptn.Type == PartitionType.Si) || _ptn.IsMainContent)
            {
                if (section.Type == AreaType.FstBlock)
                {
                    _fsInfo.ProcessBlock(section.ImageOffset, section.Decrypted, (int)section.Size, true, null);
                    if (_ptn.Type == PartitionType.Game)
                    {
                        base.OutStream.WriteAdditionalFile(_fsInfo.SiData.FileCert, 0, _fsInfo.SiData.FileCert.Length, "title.cert", false, false);
                        base.OutStream.WriteAdditionalFile(_fsInfo.SiData.FileTicket, 0, _fsInfo.SiData.FileTicket.Length, "title.tik", false, false);
                        base.OutStream.WriteAdditionalFile(_fsInfo.SiData.FileTmd, 0, _fsInfo.SiData.FileTmd.Length, "title.tmd", true, false);
                        base.OutStream.NewPart(_fsInfo.SiData.Contents[0].ContentId.ToString("x8"), "app", false);
                        base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
                    }
                }
                else if (section.Type == AreaType.FileSystem)
                {
                    _cntHeader = _fsInfo.FstBlock.GetContentHeader(section.ImageOffset);

                    if (_ptn.Type == PartitionType.Si)
                        saveFileData(section);
                    else if (_ptn.IsMainContent && _cntHeader.Content != null)
                    {
                        if (section.AreaOffset == 0)
                        {
                            if (_cntHeader.HasHashes)
                            {
                                string h3Name = $"{_cntHeader.Content.ContentId:x8}.h3";
                                _h3.Add(h3Name, new List<byte[]>());
                                if ((_cntHeader.H3Hashes?.Length ?? 0) != 0)
                                {
                                    for (int i = 0; i < _cntHeader.H3Hashes.Length; i += 0x14)
                                        _h3.Last().Value.Add(_cntHeader.H3Hashes.Read(i, 0x14));
                                }
                                else
                                    _sha = SHA1.Create();
                            }

                            if (_sha != null && section.AreaOffset % (section.AreaInfo.BlockSize * WiiUConsts.H0Count * WiiUConsts.H1Count * WiiUConsts.H2Count) == 0)
                                _h3.Last().Value.Add(_sha.ComputeHash(section.Decrypted.Read(WiiUConsts.H2Offset, WiiUConsts.H2Len)));

                            base.OutStream.NewPart(_cntHeader.Content.ContentId.ToString("x8"), "app", false);
                        }
                        base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
                    }
                }
            }

            skipToNext(section);
        }

        private void writeH3()
        {
            foreach (KeyValuePair<string, List<byte[]>> kv in _h3)
            {
                byte[] h3 = new byte[kv.Value.Count * 0x14];
                for (int i = 0; i < kv.Value.Count; i++)
                    Array.Copy(kv.Value[i], 0, h3, i * 0x14, 0x14);
                base.OutStream.WriteAdditionalFile(h3, 0, h3.Length, kv.Key, false, false);
            }
        }

        private void skipToNext(ISection section)
        {
            if (_ptn == null || _ptn.ImageOffset == section.ImageOffset)
                return;

            if (_ptn.Type == PartitionType.Game && _ptn.IsMainContent)
            {
                if (_cntHeader != null && _cntHeader.RepeatedApp && _fsInfo.FstBlock.ContentHeaders.Length > _cntHeader.Index + 1)
                {
                    _context.SkipToImageOffsetSet(_fsInfo.FstBlock.ContentHeaders[_cntHeader.Index + 1].ImageOffset);
                    return;
                }
            }
            else if (section.Type == AreaType.Other || _ptn.Type != PartitionType.Si)
            {
                PartitionInfo ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset && a.IsMainContent); // && (a.Type == PartitionType.Si || (a.Type == PartitionType.Game && a.Id.StartsWith("GM00050000"))));
                if (ptn != null)
                    _context.SkipToImageOffsetSet(ptn.ImageOffset);
                else
                    _context.SkipToImageOffsetSet(long.MaxValue);
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            closeAppStream();
            writeH3();
            base.ProcessResults();
        }

        private void saveFileData(ISection section)
        {
            //hack in to IBuffer to use the same code Image uses to read SI data
            IBuffer buff = new Buffer(section.AreaInfo.IsEncryptionSupported, section.Decrypted);
            buff.ReInitialise(section.AreaInfo, false);
            buff.Update(section.ImageOffset, section.AreaOffset, (int)section.Size, -1, false, false);
            Image.SetSiInfoInImageHeader(buff, _cntHeader, _header, _fsInfo, true);
        }

        private void createEmptyFolders(string path, IFsFolder dir)
        {
            if (dir.Files.Count == 0 && dir.Folders.Count == 0)
                Directory.CreateDirectory(Path.Combine(path, dir.Path.Substring(1)));

            foreach (IFsFolder d in dir.Folders)
                createEmptyFolders(path, d);
        }

        private void closeAppStream()
        {
            if (_sha != null)
                try { _sha.Dispose(); } catch { } finally { _sha = null; }
        }
    }
}
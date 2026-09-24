using Nanook.NKit;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiU;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    internal class DataStoreWiiUFormatter : IDataStoreSystemFormatter, IIndexNameDisambiguator
    {
        // IIndexNameDisambiguator implementation
        public bool SupportsIndexDisambiguation => true;

        public string DisambiguateImageName(string baseName, string indexFileName)
        {
            if (string.IsNullOrEmpty(indexFileName))
                return baseName;

            string suffix = $" [{indexFileName}]";
            if (baseName.EndsWith(suffix, global::System.StringComparison.OrdinalIgnoreCase))
                return baseName;

            return $"{baseName}{suffix}";
        }

        public string RestoreBaseName(string disambiguatedName) => RestoreBaseNameStatic(disambiguatedName);

        /// <summary>
        /// Static convenience for RestoreBaseName — can be called without
        /// constructing a full DataStoreWiiUFormatter instance.
        /// </summary>
        internal static string RestoreBaseNameStatic(string disambiguatedName)
        {
            if (string.IsNullOrEmpty(disambiguatedName))
                return disambiguatedName;

            // Find the last " [" — the disambiguation suffix is always at the end
            int bracketStart = disambiguatedName.LastIndexOf(" [", global::System.StringComparison.Ordinal);
            if (bracketStart < 0 || disambiguatedName[disambiguatedName.Length - 1] != ']')
                return disambiguatedName;

            // Verify the content between brackets matches the tmd.X pattern
            string content = disambiguatedName.Substring(bracketStart + 2, disambiguatedName.Length - bracketStart - 3);
            if (!content.StartsWith("tmd.", global::System.StringComparison.Ordinal))
                return disambiguatedName;

            string digits = content.Substring(4);
            if (digits.Length == 0)
                return disambiguatedName;

            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9')
                    return disambiguatedName;
            }

            return disambiguatedName.Substring(0, bracketStart);
        }

        private readonly IImageWriter _imageWriter;
        private readonly IStepContext _context;
        private readonly IDataStore _dataStore;
        private readonly Dictionary<long, Stream> _activeFileStreams = new Dictionary<long, Stream>();

        // Aux DataStore and writer for routing update partition blocks to a sidecar store.
        // Opened when auxSetName is provided (convention-discovered by DedupeStep).
        private readonly IDataStore _auxDataStore;
        private readonly IImageWriter _auxWriter;

        public string ImageFileName { get; }

        public bool HasAux => _auxWriter != null;

        // Lazily-built mapping from partition index → PartitionType string,
        // populated from PartitionHeader scan areas on first access.
        // Used by GetWriterForSection to route update-partition sections to _auxWriter.
        private Dictionary<int, string> _partitionTypeMap;

        // Lazily-built list of update partition image offset ranges for routing
        // file writes (via BeginFileWrite) to the aux writer by offset alone.
        private List<(long Start, long End)> _updatePartitionRanges;

        // Tracks whether the current section being processed belongs to an update partition.
        // Set by ProcessSection/GetWriterForSection, used by BeginFileWrite for file routing.
        private bool _currentSectionIsUpdate;

        private long _fsContentAreaImageOffset = -1;
        private readonly List<uint> _fsSectionCacheList = new List<uint>();
        // Accumulates ImageHeader + PartitionTable data so its CRC can be
        // compared against Other gap sections that repeat these two areas.
        private byte[] _headerPtBuffer;
        private int _headerPtBufferPos;

        private struct CachedSectionData
        {
            public DataStride Stride;
            public uint DecryptedCrc;
            // Decrypted buffer for the cached section (copied when caching). Allows
            // quick comparisons of partial ranges without re-reading source.
            public byte[] Decrypted;
            // If the following area is an Other area whose final section is a partial
            // block that maps into this cached section, these fields record the CRC
            // and size of that trailing window so Other processing can match it
            // without extra reads.
            public uint? LastOtherTailCrc;
            public int LastOtherTailSize;
            public long LastOtherTailImageOffset;
            // DiscOffset: offset within the section where the item was found
            // FsOffset/FsSize: data within the section to read
            // BlockType: File/FileSystem/Other
            // GroupStart: image offset of the logical file group (offsetStart) or null for non-file items
            public List<(long DiscOffset, long FsOffset, long FsSize, BlockType BlockType, long? GroupStart)> Items;
        }

        // NOTE: zero-range checks are performed on the section.Decrypted buffer when available.
        // If Decrypted is not present we conservatively assume the range is not all-zero to avoid
        // accidentally skipping real data.
        private bool isSectionRangeAllZero(ISection section, int offset, int count)
        {
            if (section == null || count <= 0)
                return true;
            if (section.Decrypted == null || offset < 0 || offset + count > section.Decrypted.Length)
                return false;

            AreaInfo aInfo = section.AreaInfo;
            return Buffer.ProcessFsData(section.Decrypted, offset, count, aInfo.BlockSize, aInfo.BlockFsOffset, aInfo.BlockFsSize,
                (d, dOff, fsOff, sz) => d.Equals(dOff, sz, (byte)0));
        }

        public DataStoreWiiUFormatter(string dedupePath, string imageName, long shardSize, IStepContext context, int blockSize = 0, string setName = null, string auxSetName = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(dedupePath)) throw new ArgumentNullException(nameof(dedupePath));

            if (!Directory.Exists(dedupePath))
                Directory.CreateDirectory(dedupePath);

            string resolvedSetName = setName ?? context.SystemType.ToString();
            _dataStore = new DataStore(dedupePath);
            if (_dataStore.GetSetInfo(resolvedSetName) == null)
                _dataStore.CreateSet(resolvedSetName, shardSize, blockSize);

            // Apply index-based name disambiguation when an index file is present
            string indexFileName = context.SourceFile?.IndexFile?.NameOnly;
            if (SupportsIndexDisambiguation && !string.IsNullOrEmpty(indexFileName))
                imageName = DisambiguateImageName(imageName, indexFileName);

            // Choose image format based on the IndexFile. WiiU content with a TMD
            // index file is CDN app content (ImageFormat.App). Everything else — WUD,
            // WUX, ISO disc images that have no index file — is ImageFormat.Iso.
            // We check IndexFile directly rather than IsFolderMode or ImageType because
            // those derived properties can be stale when context is reused across batch items.
            ImageFileName = imageName;

            ImageFormat chosenFormat = ImageFormat.Iso;
            if (_context.SourceFile?.IndexFile?.FileType == IndexFileType.TmdApp)
            {
                // Use Cdn for CDN media type (tmd.X versioned files), App for title-style (title.tmd).
                // Both map to ContainerType.TmdApp on reconstruct; Cdn distinguishes raw system binaries
                // (EncryptedNoKeyMode) from regular FST-based app content stored with full deduplication.
                chosenFormat = _context.ImageInfo?.MediaType == MediaType.CDN ? ImageFormat.Cdn : ImageFormat.App;
            }

            _imageWriter = _dataStore.AddImage(resolvedSetName, imageName, context.SystemType.ToString(), chosenFormat);
            _imageWriter.CompressionParallelism = 16;

            // Aux writer setup — aux set discovered by convention in DedupeStep
            if (!string.IsNullOrEmpty(auxSetName))
            {
                _auxDataStore = new DataStore(dedupePath);
                if (_auxDataStore.GetSetInfo(auxSetName) == null)
                    _auxDataStore.CreateSet(auxSetName, 50L * 1024 * 1024 * 1024, blockSize);
                _auxWriter = _auxDataStore.AddImage(auxSetName, imageName, context.SystemType.ToString(), chosenFormat);
                _auxWriter.CompressionParallelism = 16;
            }
        }

        public DataStride GetStrideForPartition(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem && a.AreaInfo?.Properties != null && a.AreaInfo.Properties["Partition"] is int p && p == partitionId);
            if (area == null)
                return null;

            AreaInfo ai = area.AreaInfo;
            if (ai.BlockSize != ai.BlockFsSize)
            {
                return new DataStride
                {
                    SourceBlockSize = ai.BlockSize,
                    DataOffset = ai.BlockFsOffset,
                    DataLength = ai.BlockFsSize
                };
            }

            return null;
        }

        public long GetPartitionImageOffset(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem && a.AreaInfo?.Properties != null && a.AreaInfo.Properties["Partition"] is int p && p == partitionId);
            return area?.ImageOffset ?? 0;
        }

        public AreaMetadata BuildAreaMetadata(ScanArea scanArea)
        {
            if (scanArea == null)
                return null;

            Properties props = scanArea.AreaInfo?.Properties;
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, scanArea.Type.ToString());

            try
            {
                switch (scanArea.Type)
                {
                    case AreaType.ImageHeader:
                        if (props != null)
                            metadata.Set(AreaValueType.ID, (props["ID"] as string) ?? "");
                        if (_context.SourceFile?.Key != null)
                            metadata.Set(AreaValueType.TitleKey, _context.SourceFile.Key.ToHexString());
                        break;
                    case AreaType.PartitionTable:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partitions, props["Partitions"] is int pt ? pt : 0);
                            metadata.Set(AreaValueType.Encrypted, props["Encrypted"] is bool encpt ? encpt : false);
                            if (props["KeyCrc"] != null)
                                metadata.Set(AreaValueType.KeyCrc, props["KeyCrc"] is uint kc ? (long)kc : 0);
                        }
                        break;
                    case AreaType.PartitionHeader:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int p ? p : 0);
                            metadata.Set(AreaValueType.PartitionType, (props["PartitionType"] as string) ?? "");
                            metadata.Set(AreaValueType.VolumeId, (props["VolumeId"] as string) ?? "");
                            if (props["TitleId"] != null)
                                metadata.Set(AreaValueType.TitleId, props["TitleId"] is string tid ? tid : props["TitleId"].ToString());
                            if (props["Signed"] != null)
                                metadata.Set(AreaValueType.Signed, (props["Signed"] as string) ?? "");
                        }
                        break;
                    case AreaType.FstBlock:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int p2 ? p2 : 0);
                            metadata.Set(AreaValueType.ContentHeaders, props["ContentHeaders"] is int ch ? ch : 0);
                            // Only persist App metadata when an explicit non-empty value exists
                            string appVal = props["App"] as string;
                            if (!string.IsNullOrEmpty(appVal))
                                metadata.Set(AreaValueType.App, appVal);
                            string fnVal = props["Filename"] as string;
                            if (!string.IsNullOrEmpty(fnVal))
                                metadata.Set(AreaValueType.FileName, fnVal);
                            metadata.Set(AreaValueType.Encrypted, props["Encrypted"] is bool e ? e : false);
                            if (props["TitleKeyCrc"] != null)
                                metadata.Set(AreaValueType.TitleKeyCrc, props["TitleKeyCrc"] is uint tk ? (long)tk : 0);
                            if (props["CommonKeyCrc"] != null)
                                metadata.Set(AreaValueType.CommonKeyCrc, props["CommonKeyCrc"] is uint ck ? (long)ck : 0);
                            // Persist the title key on FST blocks for APP-style images.
                            // Use the decrypted title key (SiData.KeyTitle) for Game partitions,
                            // matching the SectionProcessor key selection pattern:
                            //   Game → SiData.KeyTitle (decrypted AES key)
                            //   Other → Header.Key / SourceFile.Key
                            // SourceFile.Key may hold the encrypted ticket key, so we prefer
                            // the decrypted key from FsInfo when available.
                            try
                            {
                                Nintendo.WiiU.FileSystemInfo fstFsInfo = scanArea.FsInfo as Nanook.NKit.Nintendo.WiiU.FileSystemInfo;
                                byte[] decKey = fstFsInfo?.SiData?.KeyTitle;
                                if (decKey != null)
                                    metadata.Set(AreaValueType.TitleKey, decKey.ToHexString());
                                else if (_context?.SourceFile?.Key != null)
                                    metadata.Set(AreaValueType.TitleKey, _context.SourceFile.Key.ToHexString());
                            }
                            catch { }
                        }
                        break;
                    case AreaType.FileSystem:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int p3 ? p3 : 0);
                            metadata.Set(AreaValueType.ContentIndex, props["ContentIndex"] is int ci ? ci : 0);
                            metadata.Set(AreaValueType.Encrypted, props["Encrypted"] is bool enc ? enc : false);
                            metadata.Set(AreaValueType.BlockSize, props["BlockSize"] is uint bs ? (long)bs : 0);
                            metadata.Set(AreaValueType.HashSize, props["HashSize"] is uint hs ? (long)hs : 0);
                            if (props["HashRoot"] != null)
                                metadata.Set(AreaValueType.HashRoot, props["HashRoot"] is string hr ? hr : props["HashRoot"].ToString());
                            // Propagate explicit App property from scan if present
                            string fsApp = props["App"] as string;
                            if (!string.IsNullOrEmpty(fsApp))
                                metadata.Set(AreaValueType.App, fsApp);
                            string fsFn = props["Filename"] as string;
                            if (!string.IsNullOrEmpty(fsFn))
                                metadata.Set(AreaValueType.FileName, fsFn);
                        }
                        if (scanArea.Sections != null && scanArea.Sections.Any(s => s.SeekIv != null))
                        {
                            string seekIvs = string.Join("|", scanArea.Sections.Select(s => s.SeekIv?.ToHexString() ?? ""));
                            metadata.Set(AreaValueType.SeekIv, seekIvs);
                        }
                        break;
                    case AreaType.Other:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int p4 ? p4 : 0);
                            int repeatedContentIndex = props["RepeatedContentIndex"] is int rci ? rci : 0;
                            metadata.Set(AreaValueType.ContentIndex, repeatedContentIndex);
                            metadata.Set(AreaValueType.RepeatedContentIndex, repeatedContentIndex);
                            metadata.Set(AreaValueType.Encrypted, props["Encrypted"] is bool encOth ? encOth : false);
                            metadata.Set(AreaValueType.BlockSize, props["BlockSize"] is uint bsOth ? (long)bsOth : 0);
                            metadata.Set(AreaValueType.HashSize, props["HashSize"] is uint hsOth ? (long)hsOth : 0);
                        }
                        break;
                    case AreaType.RawKeyMissing:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.ContentIndex, props["ContentIndex"] is int rkmCi ? rkmCi : 0);
                            string rkmApp = props["App"] as string;
                            if (!string.IsNullOrEmpty(rkmApp))
                                metadata.Set(AreaValueType.App, rkmApp);
                            string rkmFn = props["Filename"] as string;
                            if (!string.IsNullOrEmpty(rkmFn))
                                metadata.Set(AreaValueType.FileName, rkmFn);

                            if (props["ContentHeaders"] != null)
                                metadata.Set(AreaValueType.ContentHeaders, props["ContentHeaders"] is int rkmCh ? rkmCh : 0);
                            if (props["TmdVersion"] != null)
                                metadata.Set(AreaValueType.TmdVersion, props["TmdVersion"] is string rkmTv ? rkmTv : props["TmdVersion"].ToString());
                            if (props["MissingFiles"] != null)
                                metadata.Set(AreaValueType.MissingFiles, props["MissingFiles"] is string rkmMf ? rkmMf : props["MissingFiles"].ToString());
                        }
                        break;
                    default:
                        break;
                }
            }
            catch
            {
                // ignore
            }

            return metadata;
        }

        // Interface implementation (legacy signature)
        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, long? strideOriginOffset = null)
        {
            // Route file writes to aux when the current section belongs to an update partition
            IImageWriter writer = (_auxWriter != null && _currentSectionIsUpdate)
                ? _auxWriter
                : _imageWriter;
            return writer.BeginWriteStream(offset: imageOffset, type: type, offsetStart: imageOffset, stride: stride, strideOriginOffset: strideOriginOffset);
        }

        /// <summary>
        /// Begins a file write using the specified writer (primary or aux).
        /// </summary>
        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, IImageWriter writer, long? strideOriginOffset = null)
            => writer.BeginWriteStream(offset: imageOffset, type: type, offsetStart: imageOffset, stride: stride, strideOriginOffset: strideOriginOffset);

        // New overload to allow explicit offsetStart grouping
        private Stream beginFileWriteInternal(long imageOffset, BlockType type, DataStride stride, long? offsetStart = null, long? strideOriginOffset = null)
            => _imageWriter.BeginWriteStream(offset: imageOffset, type: type, offsetStart: offsetStart ?? imageOffset, stride: stride, strideOriginOffset: strideOriginOffset);

        public void FinalizeFileWrite(long imageOffset, Stream stream) => stream?.Dispose();

        public long ToImageOffsetFromFsOffsets(long partitionImageOffset, long fsOffset)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem && a.ImageOffset == partitionImageOffset);
            if (area == null)
                return partitionImageOffset + fsOffset;

            AreaInfo ai = area.AreaInfo;
            if (ai.BlockSize == ai.BlockFsSize)
                return partitionImageOffset + fsOffset;

            long fullBlocks = fsOffset / ai.BlockFsSize;
            int rem = (int)(fsOffset % ai.BlockFsSize);
            long imageOffset = partitionImageOffset + (fullBlocks * ai.BlockSize) + ai.BlockFsOffset + rem;
            return imageOffset;
        }

        // --- New persistence helpers ---
        public void PersistImageSection(long imageOffset, ISection section, ISectionData sectionData, bool isFs, DataStride stride)
        {
            if (section == null || sectionData == null) return;
            Stream writeStream = null;
            try
            {
                int off = (int)(isFs ? sectionData.FsOffset : sectionData.Offset);
                int sz = (int)sectionData.FsSize;

                // Prefer using the decrypted buffer when available (isSectionRangeAllZero
                // will inspect section.Decrypted and handle strided/hash layouts).
                if (sz > 0 && !isSectionRangeAllZero(section, off, sz))
                {
                    writeStream = BeginFileWrite(imageOffset, BlockType.Other, stride);
                    section.Read(off, sz, writeStream);
                    FinalizeFileWrite(imageOffset, writeStream);
                    writeStream = null;
                }
            }
            catch (Exception ex)
            {
                try { writeStream?.Dispose(); } catch { }
                _context.Log?.Error(() => $"PersistImageSection failed at {imageOffset:X}: {ex.Message}");
            }
        }

        public void PersistFiller(long imageOffset, ISection section, ISectionData sectionData, bool isFs, DataStride stride)
        {
            if (section == null || sectionData == null) return;
            Stream writeStream = null;
            try
            {
                int offF = (int)(isFs ? sectionData.FsOffset : sectionData.Offset);
                int szF = (int)sectionData.FsSize;
                if (!isSectionRangeAllZero(section, offF, szF))
                {
                    writeStream = BeginFileWrite(imageOffset, BlockType.Other, stride);
                    section.Read(offF, szF, writeStream);
                    FinalizeFileWrite(imageOffset, writeStream);
                    writeStream = null;
                }
            }
            catch (Exception ex)
            {
                try { writeStream?.Dispose(); } catch { }
                _context.Log?.Error(() => $"PersistFiller failed at {imageOffset:X}: {ex.Message}");
            }
        }

        public void FinaliseSectionAndPersistBlockPadding(long imageOffset, ISection section, bool isFs, DataStride stride)
        {
            if (section == null)
                return;

            try
            {
                AreaInfo ai = section.AreaInfo;
                if (ai == null || ai.BlockSize == ai.BlockFsSize || ai.BlockFsSize == 0)
                    return;

                // WiiU has no disc-scrub state - State.Bytes is never populated by the WiiU SectionProcessor.
                // H2 is always non-creatable so unconditionally store the full hash block per hashed block.
                int blocks = (int)(section.Size / ai.BlockSize);
                if (blocks == 0)
                    return;

                byte[] outb = new byte[1 + (blocks * WiiUConsts.HashSize)];
                outb[0] = 0; // stateLen=0, no scrub state for WiiU

                int sectBase = (int)((imageOffset - section.ImageOffset) / ai.BlockSize);
                int srcOff = sectBase * ai.BlockSize;
                for (int i = 0; i < blocks; i++)
                {
                    Array.Copy(section.Decrypted, srcOff, outb, 1 + (i * WiiUConsts.HashSize), WiiUConsts.HashSize);
                    srcOff += ai.BlockSize;
                }

                // Route block padding to the same writer as the section's data
                IImageWriter writer = getWriterForSection(section);
                writer.WriteData(imageOffset, outb, 0, outb.Length, BlockType.BlockPadding, null, imageOffset);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"PersistBlockPadding(ISection) failed at {imageOffset:X}: {ex.Message}");
            }
        }

        // --- File lifecycle helpers ---
        public void BeginFile(long imageOffsetStart, IFsFile file, DataStride stride)
        {
            if (!_activeFileStreams.ContainsKey(imageOffsetStart))
            {
                try
                {
                    BlockType blockType = file?.IsSystemFile == true ? BlockType.FileSystem : BlockType.File;
                    Stream s = BeginFileWrite(imageOffsetStart, blockType, stride);
                    _activeFileStreams[imageOffsetStart] = s;
                }
                catch (Exception ex)
                {
                    _context.Log?.Error(() => $"BeginFile failed for offset {imageOffsetStart:X}: {ex.Message}");
                    throw;
                }
            }
        }

        public void AppendFileData(long imageOffsetStart, ISection section, ISectionData sectionData, bool isFs)
        {
            if (!_activeFileStreams.TryGetValue(imageOffsetStart, out Stream s) || section == null || sectionData == null)
                return;

            try
            {
                section.Read((int)(isFs ? sectionData.FsOffset : sectionData.Offset), (int)sectionData.FsSize, s);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"AppendFileData failed for offset {imageOffsetStart:X}: {ex.Message}");
                throw;
            }
        }

        public void CompleteFile(long imageOffsetStart, IFsFile file)
        {
            if (!_activeFileStreams.TryGetValue(imageOffsetStart, out Stream s))
                return;

            try
            {
                FinalizeFileWrite(imageOffsetStart, s);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"CompleteFile failed for offset {imageOffsetStart:X}: {ex.Message}");
                try { s?.Dispose(); } catch { }
                throw;
            }
            finally
            {
                _activeFileStreams.Remove(imageOffsetStart);
            }
        }

        public void CreateAreas(IEnumerable<ScanArea> areas)
        {
            if (_imageWriter == null || areas == null) return;
            foreach (ScanArea sra in areas)
            {
                try
                {
                    AreaMetadata meta = BuildAreaMetadata(sra);
                    uint crc = sra.Crc;
                    ulong xx = 0;
                    AreaInfo ai = sra.AreaInfo;
                    int sectionSize = ai?.SectionSize ?? WiiUConsts.DefaultSectionSize; // Default to 2MB if not specified

                    if (ai != null && ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                        _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, sectionSize, meta);
                    else
                        _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);

                    // Write area to aux store so it has complete image records
                    if (_auxWriter != null)
                    {
                        try
                        {
                            if (ai != null && ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                                _auxWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, sectionSize, meta);
                            else
                                _auxWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);
                        }
                        catch (Exception auxEx)
                        {
                            _context?.Log?.Info(() => $"CreateArea (aux) failed for offset {sra.ImageOffset:X}: {auxEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _context?.Log?.Info(() => $"CreateArea failed for offset {sra.ImageOffset:X}: {ex.Message}");
                }
            }
        }

        public void FinalizeImage(long size, uint crc, ulong xxHash)
        {
            try
            {
                _imageWriter?.FinalizeImage(size, crc, xxHash);
            }
            catch (Exception ex)
            {
                _context?.Log?.Error(() => $"FinalizeImage failed: {ex.Message}");
                throw;
            }

            // Finalize aux writer so the aux store has matching image metadata
            try
            {
                _auxWriter?.FinalizeImage(size, crc, xxHash);
            }
            catch (Exception ex)
            {
                _context?.Log?.Error(() => $"FinalizeImage (aux) failed: {ex.Message}");
            }
        }

        public bool AlreadyExists => _imageWriter?.AlreadyExists ?? false;

        public void BuildFileSystemYaml(Scan scan)
        {
            if (_imageWriter == null || scan == null)
                return;

            // Build partition index → (VolumeId, PartitionType, TitleId) mapping from PartitionHeader areas
            Dictionary<int, (string VolumeId, string PartitionType, string TitleId)> partitionInfo = new Dictionary<int, (string VolumeId, string PartitionType, string TitleId)>();
            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type == AreaType.PartitionHeader && area.AreaInfo.Properties != null)
                {
                    object ptnObj = area.AreaInfo.Properties["Partition"];
                    object volObj = area.AreaInfo.Properties["VolumeId"];
                    string ptnType = area.AreaInfo.Properties["PartitionType"] as string;
                    string titleId = area.AreaInfo.Properties["TitleId"] as string;
                    if (ptnObj is int ptnIdx && volObj is string volId)
                        partitionInfo[ptnIdx] = (volId, ptnType, titleId ?? "");
                }
            }

            // Identify the main game partition — VolumeId starts with GameTitlePrefix ("GM00050000")
            int mainGamePartitionIdx = -1;
            foreach (KeyValuePair<int, (string VolumeId, string PartitionType, string TitleId)> kvp in partitionInfo)
            {
                if (kvp.Value.PartitionType?.Equals("Game", StringComparison.OrdinalIgnoreCase) == true
                    && kvp.Value.VolumeId.StartsWith(WiiUConsts.GameTitlePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    mainGamePartitionIdx = kvp.Key;
                    break;
                }
            }

            // Build TitleId → formatted directory name ("XXXXXXXX-XXXXXXXX") for non-main, non-SI, non-Update partitions
            Dictionary<int, string> partitionDirNames = new Dictionary<int, string>();
            // Also build TitleId (16-char hex) → partition index for SI content routing
            Dictionary<string, int> titleIdToPartitionIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, (string VolumeId, string PartitionType, string TitleId)> kvp in partitionInfo)
            {
                string tid = kvp.Value.TitleId;
                if (!string.IsNullOrEmpty(tid) && tid.Length == 16 && tid != "0000000000000000")
                    titleIdToPartitionIdx[tid] = kvp.Key;

                string pt = kvp.Value.PartitionType ?? "";
                if (kvp.Key == mainGamePartitionIdx)
                    continue;
                if (pt.Equals("Si", StringComparison.OrdinalIgnoreCase) || pt.Equals("Update", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Non-main game partitions (GameUpdate, Channel, additional Game, etc.) get a TitleId directory
                if (!string.IsNullOrEmpty(tid) && tid.Length == 16 && tid != "0000000000000000")
                    partitionDirNames[kvp.Key] = $"{tid.Substring(0, 8)}-{tid.Substring(8, 8)}";
            }

            // Collect FileSystem areas with their partition metadata
            List<(ScanArea Area, int PartitionIdx, string PartitionType, bool IsSystem)> fsAreas = new List<(ScanArea Area, int PartitionIdx, string PartitionType, bool IsSystem)>();
            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type != AreaType.FileSystem)
                    continue;

                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                int partitionIdx = area.AreaInfo.Properties?.Get<int>("Partition", -1) ?? -1;
                string ptnType = "";
                if (partitionIdx >= 0 && partitionInfo.TryGetValue(partitionIdx, out (string VolumeId, string PartitionType, string TitleId) info))
                    ptnType = info.PartitionType ?? "";

                bool isSystem = ptnType.Equals("Si", StringComparison.OrdinalIgnoreCase)
                             || ptnType.Equals("Update", StringComparison.OrdinalIgnoreCase);

                fsAreas.Add((area, partitionIdx, ptnType, isSystem));
            }

            // Sort: main game first, then other game partitions, then system (SI/Update)
            fsAreas.Sort((a, b) =>
            {
                int aOrder = a.IsSystem ? 2 : (a.PartitionIdx == mainGamePartitionIdx ? 0 : 1);
                int bOrder = b.IsSystem ? 2 : (b.PartitionIdx == mainGamePartitionIdx ? 0 : 1);
                return aOrder.CompareTo(bOrder);
            });

            // Determine strip prefix per SI content area — each area may have its own
            // root directory (e.g. "02", "03") representing the content index that should
            // be stripped so the SI files (title.cert, title.tik, title.tmd) appear directly
            // under their target partition root.
            Dictionary<long, string> siStripPrefix = new Dictionary<long, string>();
            foreach ((ScanArea Area, int PartitionIdx, string PartitionType, bool IsSystem) item in fsAreas.Where(a => a.PartitionType.Equals("Si", StringComparison.OrdinalIgnoreCase)))
            {
                int contentIndex = item.Area.AreaInfo.Properties?.Get<int>("ContentIndex", -1) ?? -1;
                HashSet<string> rootDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IFsFile file in item.Area.FsInfo.FileSystem.Files)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;
                    if (contentIndex >= 0 && file is FstFile ff && ff.WiiUAppIndex != contentIndex)
                        continue;
                    string dir = file.Path?.Trim('/') ?? "";
                    int slash = dir.IndexOf('/');
                    string rootDir = slash > 0 ? dir.Substring(0, slash) : dir;
                    if (!string.IsNullOrEmpty(rootDir))
                        rootDirs.Add(rootDir);
                }
                if (rootDirs.Count == 1)
                    siStripPrefix[item.Area.ImageOffset] = rootDirs.First();
            }

            FsYaml yaml = new FsYaml();
            FsYamlNode inlineRoot = null;

            // Base system directory names
            HashSet<string> systemDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((ScanArea area, int partitionIdx, string ptnType, bool isSystem) in fsAreas)
            {
                IFileSystem fs = area.FsInfo.FileSystem;
                int contentIndex = area.AreaInfo.Properties?.Get<int>("ContentIndex", -1) ?? -1;

                if (inlineRoot == null)
                    inlineRoot = yaml.AddFileSystem(".", area.ImageOffset);
                FsYamlNode partitionNode = inlineRoot;

                bool isSi = ptnType.Equals("Si", StringComparison.OrdinalIgnoreCase);
                bool isUpdate = ptnType.Equals("Update", StringComparison.OrdinalIgnoreCase);

                // Determine target directory for this area's files
                string targetDir = null;
                if (isSi)
                {
                    // SI files: route to matching partition's directory via SiTitleId
                    string siTitleId = area.AreaInfo.Properties?["SiTitleId"] as string;
                    if (!string.IsNullOrEmpty(siTitleId) && titleIdToPartitionIdx.TryGetValue(siTitleId, out int targetPtnIdx))
                    {
                        if (targetPtnIdx != mainGamePartitionIdx && partitionDirNames.TryGetValue(targetPtnIdx, out string dirName))
                            targetDir = dirName;
                        // else: main game partition SI files go to root
                    }
                }
                else if (partitionIdx != mainGamePartitionIdx && !isUpdate)
                {
                    // Non-main, non-Update partition: use TitleId directory
                    partitionDirNames.TryGetValue(partitionIdx, out targetDir);
                }

                // Get strip prefix for this SI content area (if applicable)
                string stripPrefix = null;
                if (isSi && siStripPrefix.TryGetValue(area.ImageOffset, out string prefix))
                    stripPrefix = prefix;

                AreaInfo ai = area.AreaInfo;
                DataStride stride = null;
                if (ai.BlockSize > 0 && ai.BlockFsSize > 0 && ai.BlockSize != ai.BlockFsSize)
                    stride = new DataStride { SourceBlockSize = ai.BlockSize, DataOffset = ai.BlockFsOffset, DataLength = ai.BlockFsSize };

                IEnumerable<IFsFile> fileSource = area.FsInfo?.FidelityFiles?.Entries
                    ?? (IEnumerable<IFsFile>)fs.Files;

                foreach (IFsFile file in fileSource)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    if (contentIndex >= 0 && file is FstFile fstFile && fstFile.WiiUAppIndex != contentIndex)
                        continue;

                    long imageOffset = stride != null
                        ? area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)
                        : area.ImageOffset + file.FsOffset;

                    string fileName = file.IsSystemFile ? file.Name.Substring(2) : file.Name;
                    string dirPath = file.Path?.Trim('/') ?? "";

                    if (stripPrefix != null)
                    {
                        if (dirPath.Equals(stripPrefix, StringComparison.OrdinalIgnoreCase))
                            dirPath = "";
                        else if (dirPath.StartsWith(stripPrefix + "/", StringComparison.OrdinalIgnoreCase))
                            dirPath = dirPath.Substring(stripPrefix.Length + 1);
                    }

                    // Prepend target directory for non-main partitions
                    if (targetDir != null)
                    {
                        dirPath = string.IsNullOrEmpty(dirPath)
                            ? targetDir
                            : $"{targetDir}/{dirPath}";
                    }

                    string fullPath = string.IsNullOrEmpty(dirPath)
                        ? $"/{fileName}"
                        : $"/{dirPath}/{fileName}";

                    // Files are never individually marked as system — system visibility is
                    // controlled at the directory level via the '/' prefix in the YAML
                    partitionNode.AddFileByPath(fullPath, imageOffset, file.FsSize, file.XxHash, file.Crc);

                    // Track root directory names from Update partition files as system
                    if (isUpdate && !string.IsNullOrEmpty(dirPath))
                    {
                        int slash = dirPath.IndexOf('/');
                        string rootDir = slash > 0 ? dirPath.Substring(0, slash) : dirPath;
                        if (!string.IsNullOrEmpty(rootDir))
                            systemDirNames.Add(rootDir);
                    }
                }
            }

            // Mark top-level directories that represent non-game partitions as system
            // We previously marked all title id folders as system, but those should remain visible.
            if (inlineRoot?.Children != null)
            {
                foreach (FsYamlNode child in inlineRoot.Children)
                {
                    if (child.IsDirectory && systemDirNames.Contains(child.Name))
                        child.IsSystem = true;
                }
            }

            // Determine source auxiliary file format: Title (title.tik/tmd/cert) vs CDN (tmd.X/tik.X/cetk.X)
            // IndexFile.cs: extension ".tmd" or filename "tmd" → Title (tmdVer=-1);
            // filename starts with "tmd." with numeric suffix → CDN (tmdVer>=0)
            bool isCdnFormat = false;
            int cdnVersion = -1;
            try
            {
                IndexFile idx = _context.SourceFile?.IndexFile;
                if (idx != null && idx.FileType == IndexFileType.TmdApp)
                {
                    string idxFileName = idx.FileName?.ToLower() ?? "";
                    string idxExt = idx.Extension?.ToLower() ?? "";
                    if (idxExt != ".tmd" && idxFileName != "tmd" && idxFileName.StartsWith("tmd."))
                    {
                        isCdnFormat = true;
                        if (int.TryParse(idxFileName.Substring(4), out int ver))
                            cdnVersion = ver;
                    }
                }
            }
            catch { }

            // Persist auxiliary files (.tmd/.tik/.cert) and .h3 hash data to the data store
            try
            {
                HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach ((ScanArea area, int partitionIdx, string ptnType, bool isSystem) in fsAreas)
                {
                    Nintendo.WiiU.FileSystemInfo fsInfo = area.FsInfo as Nanook.NKit.Nintendo.WiiU.FileSystemInfo;
                    SiData si = fsInfo?.SiData;

                    // Write auxiliary files for areas that have SiData
                    if (si != null)
                    {
                        int tmdVer = cdnVersion;
                        if (isCdnFormat && tmdVer < 0)
                        {
                            try { tmdVer = si.TmdInfo?.TitleVersion ?? 0; } catch { tmdVer = 0; }
                        }

                        List<(byte[] Data, string Name)> auxFiles = new List<(byte[] Data, string Name)>();

                        if (isCdnFormat)
                        {
                            auxFiles.Add((si.FileTmd, $"tmd.{tmdVer}"));
                            if (si.FileCetk != null)
                            {
                                // Source had a combined cetk (ticket+cert) — store as-is
                                auxFiles.Add((si.FileCetk, $"cetk.{tmdVer}"));
                            }
                            else
                            {
                                // Source already had separate tik and cetk files
                                // Only save the ticket/cert if they weren't generated in memory.
                                if (!si.GeneratedTicket)
                                    auxFiles.Add((si.FileTicket, $"tik.{tmdVer}"));
                                if (!si.GeneratedCert)
                                    auxFiles.Add((si.FileCert, $"cetk.{tmdVer}"));
                            }
                        }
                        else
                        {
                            if (!si.GeneratedTicket)
                                auxFiles.Add((si.FileTicket, "title.tik"));
                            auxFiles.Add((si.FileTmd, "title.tmd"));
                            if (!si.GeneratedCert)
                                auxFiles.Add((si.FileCert, "title.cert"));
                        }

                        foreach ((byte[] Data, string Name) f in auxFiles)
                        {
                            if (f.Data == null || written.Contains(f.Name))
                                continue;

                            try
                            {
                                uint crc = Crc.Compute(f.Data, 0, f.Data.Length);
                                ulong xx = 0;
                                try
                                {
                                    using (XXHash64 h = Nanook.NKit.XXHash64.Create())
                                    {
                                        byte[] hb = h.ComputeHash(f.Data);
                                        if (hb != null && hb.Length >= 8)
                                            xx = BitConverter.ToUInt64(hb, 0);
                                    }
                                }
                                catch { }

                                inlineRoot?.AddFileByPath($"/{f.Name}", 0, f.Data.Length, xx, crc);
                                _imageWriter.WriteFile(f.Name, f.Data);
                                written.Add(f.Name);
                            }
                            catch (Exception ex)
                            {
                                _context?.Log?.Info(() => $"Failed to write auxiliary file {f.Name}: {ex.Message}");
                            }
                        }
                    }

                    // Persist .h3 hash data from FstBlock content headers to the data store.
                    // .app and .h3 entries are NOT added to filesystem.yaml — .app files are
                    // structural content areas exposed in Image mode via area records and the
                    // FileName metadata property, not filesystem entries.
                    if (fsInfo?.FstBlock?.ContentHeaders != null)
                    {
                        foreach (ContentHeader ch in fsInfo.FstBlock.ContentHeaders)
                        {
                            if (ch?.Content == null)
                                continue;

                            // .h3 data — only for hashed content with hash data
                            if (ch.HasHashes && ch.H3Hashes != null && ch.H3Hashes.Length > 0)
                            {
                                string h3Name = $"{ch.Content.ContentId:x8}.h3";
                                if (!written.Contains(h3Name))
                                {
                                    try
                                    {
                                        _imageWriter.WriteFile(h3Name, ch.H3Hashes);
                                        written.Add(h3Name);
                                    }
                                    catch (Exception ex)
                                    {
                                        _context?.Log?.Info(() => $"Failed to write h3 file {h3Name}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                // For raw CDN binary images (EncryptedNoKeyMode / RawKeyMissing areas), there are no
                // FileSystem scan areas so the SiData-based loop above writes nothing. Write the TMD
                // and cetk directly from the IndexFile so Construct() can find and parse them on verify.
                if (isCdnFormat && scan?.Areas?.Any(a => a.Type == AreaType.RawKeyMissing) == true)
                {
                    IndexFile rawIdx = _context.SourceFile?.IndexFile;
                    if (rawIdx?.Data != null && cdnVersion >= 0 && !written.Contains($"tmd.{cdnVersion}"))
                    {
                        try { _imageWriter.WriteFile($"tmd.{cdnVersion}", rawIdx.Data); written.Add($"tmd.{cdnVersion}"); }
                        catch (Exception ex) { _context?.Log?.Info(() => $"Failed to write raw CDN tmd.{cdnVersion}: {ex.Message}"); }
                    }
                    // Write cetk from IndexFile.Additional (ticket + cert combined, as supplied in the source zip)
                    if (rawIdx?.Additional != null)
                    {
                        foreach (FileItem addFile in rawIdx.Additional)
                        {
                            if (addFile?.Data == null || written.Contains(addFile.FileName))
                                continue;
                            if (addFile.FileName.StartsWith("cetk", StringComparison.OrdinalIgnoreCase) || addFile.FileName.StartsWith("tik", StringComparison.OrdinalIgnoreCase))
                            {
                                try { _imageWriter.WriteFile(addFile.FileName, addFile.Data); written.Add(addFile.FileName); }
                                catch (Exception ex) { _context?.Log?.Info(() => $"Failed to write raw CDN {addFile.FileName}: {ex.Message}"); }
                            }
                        }
                    }
                }
            }
            catch { }

            byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(yaml).ToBytes();
            _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);

            // Write filesystem data to aux store so it has complete image records
            try
            {
                _auxWriter?.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
            }
            catch (Exception ex)
            {
                _context?.Log?.Error(() => $"BuildFileSystemYaml (aux) failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try { _auxWriter?.Dispose(); } catch { }
            try { _auxDataStore?.Dispose(); } catch { }
            try { _imageWriter?.Dispose(); } catch { }
            try { _dataStore?.Dispose(); } catch { }
        }

        /// <summary>
        /// Determines the target writer for a section based on its partition type.
        /// Update partition sections are routed to the aux writer when available.
        /// </summary>
        private IImageWriter getWriterForSection(ISection section)
        {
            if (_auxWriter == null)
            {
                _currentSectionIsUpdate = false;
                return _imageWriter;
            }

            // Try direct PartitionType property first (available on PartitionHeader sections)
            string partitionType = section.AreaInfo?.Properties?["PartitionType"] as string;

            // If not directly available, look up by partition index (for FileSystem/FstBlock sections)
            if (partitionType == null)
            {
                int partitionIdx = section.AreaInfo?.Properties?.Get<int>("Partition", -1) ?? -1;
                if (partitionIdx >= 0)
                {
                    ensurePartitionTypeMap();

                    if (_partitionTypeMap.TryGetValue(partitionIdx, out string mappedType))
                        partitionType = mappedType;
                }
            }

            _currentSectionIsUpdate = partitionType != null && partitionType.Equals("Update", StringComparison.OrdinalIgnoreCase);

            if (_currentSectionIsUpdate)
                return _auxWriter;

            return _imageWriter;
        }

        /// <summary>
        /// Lazily builds the partition type map from scan areas.
        /// </summary>
        private void ensurePartitionTypeMap()
        {
            if (_partitionTypeMap != null)
                return;

            _partitionTypeMap = new Dictionary<int, string>();

            // Try scan areas first
            if (_context.Scan?.Areas != null)
            {
                foreach (ScanArea area in _context.Scan.Areas)
                {
                    if (area.Type == AreaType.PartitionHeader && area.AreaInfo?.Properties != null)
                    {
                        object ptnObj = area.AreaInfo.Properties["Partition"];
                        string ptnType = area.AreaInfo.Properties["PartitionType"] as string;
                        if (ptnObj is int idx && ptnType != null)
                            _partitionTypeMap[idx] = ptnType;
                    }
                }
            }

            // If scan areas didn't have the info, try ImageInfo.SourceAreas
            if (_partitionTypeMap.Count == 0 && _context.ImageInfo?.SourceAreas != null)
            {
                foreach (IImageArea srcArea in _context.ImageInfo.SourceAreas)
                {
                    if (srcArea.AreaType == AreaType.PartitionHeader)
                    {
                        // SourceAreas don't have Properties directly — skip
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether the given image offset falls within any update partition range.
        /// Lazily builds the range list from scan areas on first call.
        /// The range spans from the start of the update partition to the start of the next partition.
        /// </summary>
        private bool isWithinUpdatePartitionRange(long imageOffset)
        {
            if (_updatePartitionRanges == null)
            {
                _updatePartitionRanges = new List<(long Start, long End)>();
                ensurePartitionTypeMap();

                if (_context.Scan?.Areas != null)
                {
                    // Find the update partition index(es)
                    HashSet<int> updatePartitionIndexes = new HashSet<int>();
                    foreach (KeyValuePair<int, string> kvp in _partitionTypeMap)
                    {
                        if (kvp.Value.Equals("Update", StringComparison.OrdinalIgnoreCase))
                            updatePartitionIndexes.Add(kvp.Key);
                    }

                    if (updatePartitionIndexes.Count > 0)
                    {
                        // Find the first area belonging to an update partition (its start)
                        // and the first area belonging to a non-update partition that comes after it (its end)
                        ScanArea[] orderedAreas = _context.Scan.Areas.OrderBy(a => a.ImageOffset).ToArray();

                        for (int i = 0; i < orderedAreas.Length; i++)
                        {
                            ScanArea area = orderedAreas[i];
                            if (area.AreaInfo?.Properties == null)
                                continue;

                            int ptnIdx = area.AreaInfo.Properties.Get<int>("Partition", -1);
                            if (ptnIdx < 0 || !updatePartitionIndexes.Contains(ptnIdx))
                                continue;

                            // Found start of an update partition area
                            long start = area.ImageOffset;

                            // Find the end: the start of the next area that belongs to a different (non-update) partition
                            long end = _context.ImageInfo?.ImageSize ?? long.MaxValue;
                            for (int j = i + 1; j < orderedAreas.Length; j++)
                            {
                                ScanArea nextArea = orderedAreas[j];
                                if (nextArea.AreaInfo?.Properties == null)
                                    continue;

                                int nextPtnIdx = nextArea.AreaInfo.Properties.Get<int>("Partition", -1);
                                if (nextPtnIdx >= 0 && !updatePartitionIndexes.Contains(nextPtnIdx))
                                {
                                    end = nextArea.ImageOffset;
                                    break;
                                }
                            }

                            if (end > start)
                            {
                                _updatePartitionRanges.Add((start, end));
                                break; // Only one update partition range expected
                            }
                        }
                    }
                }
            }

            return _updatePartitionRanges.Any(r => imageOffset >= r.Start && imageOffset < r.End);
        }

        /// <summary>
        /// Writes data using the specified writer. If the writer is the aux writer and the
        /// write fails, logs the error and falls back to writing to the primary writer.
        /// </summary>
        private void writeDataWithAuxFallback(IImageWriter writer, long imageOffset, byte[] data, int offset, int size, BlockType blockType)
        {
            if (writer == _auxWriter)
            {
                try
                {
                    writer.WriteData(imageOffset, data, offset, size, blockType);
                }
                catch (Exception ex)
                {
                    _context.Log?.Error(() => $"Aux write failed at {imageOffset:X}, falling back to primary: {ex.Message}");
                    _imageWriter.WriteData(imageOffset, data, offset, size, blockType);
                }
            }
            else
            {
                writer.WriteData(imageOffset, data, offset, size, blockType);
            }
        }

        public void ProcessSection(ISection section)
        {
            // Determine if this section belongs to an update partition — used by BeginFileWrite
            // to route file writes from saveFileData to the aux writer.
            if (_auxWriter != null)
            {
                string ptnType = section.AreaInfo?.Properties?["PartitionType"] as string;
                if (ptnType == null)
                {
                    int ptnIdx = section.AreaInfo?.Properties?.Get<int>("Partition", -1) ?? -1;
                    if (ptnIdx >= 0 && _partitionTypeMap != null && _partitionTypeMap.TryGetValue(ptnIdx, out string mapped))
                        ptnType = mapped;
                }
                _currentSectionIsUpdate = ptnType != null && ptnType.Equals("Update", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                _currentSectionIsUpdate = false;
            }

            if (section.AreaOffset == 0 && section.Type != AreaType.Other)
                _fsSectionCacheList.Clear();

            // Raw areas: written directly to the store as-is (no stride, no gap analysis)
            if (section.Type == AreaType.ImageHeader ||
                section.Type == AreaType.PartitionTable ||
                section.Type == AreaType.PartitionHeader ||
                section.Type == AreaType.FstBlock)
            {
                if (section.Type == AreaType.ImageHeader || section.Type == AreaType.PartitionTable)
                {
                    // Headers go to primary writer only
                    _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
                }
                else
                {
                    // PartitionHeader and FstBlock: route via GetWriterForSection
                    IImageWriter writer = getWriterForSection(section);
                    writeDataWithAuxFallback(writer, section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);

                    // Build partition type map incrementally as PartitionHeader sections are processed
                    if (section.Type == AreaType.PartitionHeader && _auxWriter != null)
                    {
                        string ptnType = section.AreaInfo?.Properties?["PartitionType"] as string;
                        int ptnIdx = section.AreaInfo?.Properties?.Get<int>("Partition", -1) ?? -1;
                        if (ptnIdx >= 0 && ptnType != null)
                        {
                            if (_partitionTypeMap == null)
                                _partitionTypeMap = new Dictionary<int, string>();
                            _partitionTypeMap[ptnIdx] = ptnType;
                            // Invalidate cached ranges so they're rebuilt with the new partition info
                            _updatePartitionRanges = null;
                        }
                    }
                }

                // Accumulate ImageHeader + PartitionTable into a combined buffer.
                // Other gap sections repeat these two areas joined together, so we
                // need the combined CRC for dedup matching.
                if (section.Type == AreaType.ImageHeader)
                {
                    _headerPtBuffer = null;
                    _headerPtBufferPos = 0;
                }
                if (section.Type == AreaType.ImageHeader || section.Type == AreaType.PartitionTable)
                {
                    int sz = (int)section.Size;
                    if (_headerPtBuffer == null)
                        _headerPtBuffer = new byte[(int)WiiUConsts.DiscContentsOffset + WiiUConsts.DefaultSectorSize];
                    if (_headerPtBufferPos + sz <= _headerPtBuffer.Length)
                    {
                        Array.Copy(section.Decrypted, 0, _headerPtBuffer, _headerPtBufferPos, sz);
                        _headerPtBufferPos += sz;
                    }
                    // Once both ImageHeader and PartitionTable are accumulated, cache the CRC
                    if (_headerPtBufferPos == _headerPtBuffer.Length)
                        _fsSectionCacheList.Add(Crc.Compute(_headerPtBuffer, 0, _headerPtBuffer.Length));
                }

                return;
            }


            if (section.Type != AreaType.Other)
            {
                // If the following ImageArea is an Other area, compute the size of
                // the Other area and the tail length of its final section. If that
                // tail maps into the current section we compute and stash its CRC so
                // the Other processing can compare without an extra read.
                try
                {
                    _fsSectionCacheList.Add(section.CrcDecrypted);
                    IImageArea[] srcAreas = _context.ImageInfo?.SourceAreas;
                    if (srcAreas != null)
                    {
                        long currentSize = 0;
                        // find the area that contains this section (area.ImageOffset <= section.ImageOffset < nextArea.ImageOffset)
                        int areaIdx = -1;
                        for (int aiIndex = 0; aiIndex < srcAreas.Length; aiIndex++)
                        {
                            long start = srcAreas[aiIndex].ImageOffset;
                            long end = (aiIndex + 1 < srcAreas.Length) ? srcAreas[aiIndex + 1].ImageOffset : _context.ImageInfo.ImageSize;
                            if (section.ImageOffset >= start && section.ImageOffset < end)
                            {
                                currentSize = end - start;
                                areaIdx = aiIndex;
                                break;
                            }
                        }
                        if (areaIdx >= 0 && areaIdx + 1 < srcAreas.Length)
                        {
                            IImageArea nextArea = srcAreas[areaIdx + 1];
                            if (nextArea.AreaType == AreaType.Other)
                            {
                                long otherStart = nextArea.ImageOffset;
                                long otherEnd = (areaIdx + 2 < srcAreas.Length) ? srcAreas[areaIdx + 2].ImageOffset : _context.ImageInfo.ImageSize;
                                long otherSize = otherEnd - otherStart;
                                AreaInfo ai = section.AreaInfo;
                                // Use the area's SectionSize (logical section length) to calculate final-section tail
                                int tailLen = (int)(otherSize % currentSize);
                                if (tailLen != 0 && section.AreaOffset == tailLen - (tailLen % section.AreaInfo.SectionSize))
                                    _fsSectionCacheList.Add(Crc.Compute(section.Decrypted, 0, tailLen % section.AreaInfo.SectionSize));
                            }
                        }
                    }
                }
                catch { }
            }

            // Raw CDN binary areas (no FST, EncryptedNoKeyMode): write the encrypted bytes verbatim.
            // No stride, no gap analysis, no dedup — each section maps directly at its ImageOffset.
            if (section.Type == AreaType.RawKeyMissing)
            {
                _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
                return;
            }

            if (section.Type == AreaType.FileSystem)
            {
                IImageWriter writer = getWriterForSection(section);
                AreaInfo ai = section.AreaInfo;
                // Build per-content stride; non-hashed content has BlockSize == BlockFsSize (flat stride)
                DataStride stride = new DataStride
                {
                    SourceBlockSize = ai.BlockSize,
                    DataOffset = ai.BlockFsOffset,
                    DataLength = ai.BlockFsSize
                };

                List<GapRange> storedRanges = DataStoreWiiFormatter.GetGapsAndNonCreatableDataRanges(section, stride).ToList();

                if (storedRanges.Any(a => a.ImageOffset < section.ImageOffset || a.ImageOffset + a.Size > section.ImageOffset + section.Size))
                {
                    _context.Log?.Error(() => $"ProcessSection: Calculated gap/non-creatable data ranges exceed section bounds at {section.ImageOffset:X}");
                    return;
                }

                bool hasPreservedFiles = section.Items.Any(si => si.File != null && ShouldPreserveFile(section, si.FsFile));
                if (!hasPreservedFiles && storedRanges.Count == 0 && section.FsSize > 0)
                {
                    long cleanStart = section.ImageOffset + stride.CleanToOffset(0, false);
                    storedRanges.Add(new GapRange(cleanStart, 0, (int)section.FsSize, DataType.Other, 0));
                }

                if (ai.ImageOffset != _fsContentAreaImageOffset)
                    _fsContentAreaImageOffset = ai.ImageOffset;

                List<(long DiscOffset, long FsOffset, long FsSize, BlockType BlockType, long? GroupStart)> cachedItems = new List<(long DiscOffset, long FsOffset, long FsSize, BlockType BlockType, long? GroupStart)>();
                foreach (GapRange gap in storedRanges)
                    cachedItems.Add((gap.ImageOffset - section.ImageOffset, gap.FsOffset, gap.Size, BlockType.Other, null));
                foreach (ISectionItem si in section.Items)
                {
                    if (si.File == null || !ShouldPreserveFile(section, si.FsFile))
                        continue;
                    BlockType bt = si.FsFile.IsSystemFile ? BlockType.FileSystem : BlockType.File;
                    // GroupStart is the absolute image offset of the file start (used as offsetStart when writing)
                    long groupStart = section.ImageOffset + si.File.Offset;
                    cachedItems.Add((si.File.Offset, si.File.FsOffset, si.File.FsSize, bt, groupStart));
                }

                foreach (GapRange persist in storedRanges)
                {
                    if (!isSectionRangeAllZero(section, (int)persist.FsOffset, (int)persist.Size))
                    {
                        if (writer == _auxWriter)
                        {
                            try
                            {
                                Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride, writer);
                                section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                                FinalizeFileWrite(persist.ImageOffset, writeStream);
                            }
                            catch (Exception ex)
                            {
                                _context.Log?.Error(() => $"Aux write failed at {persist.ImageOffset:X}, falling back to primary: {ex.Message}");
                                Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride, _imageWriter);
                                section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                                FinalizeFileWrite(persist.ImageOffset, writeStream);
                            }
                        }
                        else
                        {
                            Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride, writer);
                            section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                            FinalizeFileWrite(persist.ImageOffset, writeStream);
                        }
                    }
                }
            }
            else if (section.Type == AreaType.Other)
            {
                // Unencrypted Other areas always store every section — their content may differ
                // from the canonical FileSystem area and cannot be recovered via the fold.
                // Encrypted Other areas use CRC dedup; the fold correctly re-derives any skipped sections.
                bool skipDedup = !section.AreaInfo.IsEncrypted;
                if (skipDedup || !_fsSectionCacheList.Contains(section.CrcDecrypted))
                {
                    IImageWriter writer = getWriterForSection(section);
                    AreaInfo ai = section.AreaInfo;
                    int blockSize = ai?.BlockSize > 0 ? ai.BlockSize : WiiUConsts.DefaultSectorSize;
                    if (writer == _auxWriter)
                    {
                        try
                        {
                            Stream writeStream = BeginFileWrite(section.ImageOffset, BlockType.Other, new DataStride() { DataLength = blockSize, DataOffset = 0, SourceBlockSize = blockSize }, writer);
                            writeStream.Write(section.Decrypted, 0, (int)section.Size);
                            FinalizeFileWrite(section.ImageOffset, writeStream);
                        }
                        catch (Exception ex)
                        {
                            _context.Log?.Error(() => $"Aux write failed at {section.ImageOffset:X}, falling back to primary: {ex.Message}");
                            Stream writeStream = BeginFileWrite(section.ImageOffset, BlockType.Other, new DataStride() { DataLength = blockSize, DataOffset = 0, SourceBlockSize = blockSize }, _imageWriter);
                            writeStream.Write(section.Decrypted, 0, (int)section.Size);
                            FinalizeFileWrite(section.ImageOffset, writeStream);
                        }
                    }
                    else
                    {
                        Stream writeStream = BeginFileWrite(section.ImageOffset, BlockType.Other, new DataStride() { DataLength = blockSize, DataOffset = 0, SourceBlockSize = blockSize }, writer);
                        writeStream.Write(section.Decrypted, 0, (int)section.Size);
                        FinalizeFileWrite(section.ImageOffset, writeStream);
                    }
                    _fsSectionCacheList.Add(section.CrcDecrypted);
                }
            }
        }

        public bool ShouldPreserveFile(ISection section, IFsFile file)
        {
            // Skip junk files and zero-length files - they should be invisible to gap checking
            if (file == null)
                return false;

            return !file.IsMissing && file.FsSize > 0;
        }
    }
}
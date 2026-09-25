using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// Basic formatter for GameCube images. GameCube has simple area metadata and no striding.
    /// </summary>
    internal class DataStoreGameCubeFormatter : IDataStoreSystemFormatter
    {
        private readonly IImageWriter _imageWriter;
        private readonly IStepContext _context;
        private readonly IDataStore _dataStore;
        private List<GapRange> _storedRanges;

        public string ImageFileName { get; }

        public DataStoreGameCubeFormatter(string dedupePath, string imageName, long shardSize, IStepContext context, int blockSize = 0, string setName = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (!Directory.Exists(dedupePath))
                Directory.CreateDirectory(dedupePath);

            string resolvedSetName = setName ?? context.SystemType.ToString();
            _dataStore = new DataStore(dedupePath);
            if (_dataStore.GetSetInfo(resolvedSetName) == null)
                _dataStore.CreateSet(resolvedSetName, shardSize, blockSize);

            ImageFileName = Container.DataStoreAsIso.GetImageFileName(imageName, ImageFormat.Iso);
            _imageWriter = _dataStore.AddImage(resolvedSetName, imageName, context.SystemType.ToString(), ImageFormat.Iso);
            _imageWriter.CompressionParallelism = 16;
        }

        public DataStride GetStrideForPartition(int partitionId) => null; // GameCube does not use striding

        public long GetPartitionImageOffset(int partitionId) => 0; // GameCube has a single image; partitions not applicable. Return 0 by default.

        public AreaMetadata BuildAreaMetadata(ScanArea scanArea)
        {
            if (scanArea == null)
                return null;

            Properties props = scanArea.AreaInfo.Properties;

            // Check if properties are actually populated (not just allocated)
            if (props == null || props.Keys.Length == 0)
                return null;

            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, scanArea.Type.ToString());
            metadata.Set(AreaValueType.ID, props["ID"] as string ?? "");
            metadata.Set(AreaValueType.DiscNo, props["DiscNo"] != null ? (int)props["DiscNo"] : 0);
            metadata.Set(AreaValueType.Revision, props["Revision"] != null ? (int)props["Revision"] : 0);
            metadata.Set(AreaValueType.Region, props["Region"] as string ?? "");
            metadata.Set(AreaValueType.Title, props["Title"] as string ?? "");
            metadata.Set(AreaValueType.SystemDataCrc, props["SystemDataCrc"] != null ? (uint)props["SystemDataCrc"] : 0);
            metadata.Set(AreaValueType.JunkID, props["JunkID"] as string ?? "");
            metadata.Set(AreaValueType.JunkLeadingNulls, props["JunkLeadingNulls"] != null ? (long)(ulong)props["JunkLeadingNulls"] : 0);
            return metadata;
        }

        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, long? strideOriginOffset = null) =>
            // Use imageOffset as offsetStart to group writes by absolute image offset
            _imageWriter.BeginWriteStream(offset: imageOffset, type: type, offsetStart: imageOffset, strideOriginOffset: strideOriginOffset);

        public void FinalizeFileWrite(long imageOffset, Stream stream) =>
            // For now just dispose the stream; ImageWriter handles storage/finalization on dispose
            stream?.Dispose();

        public long ToImageOffsetFromFsOffsets(long partitionImageOffset, long fsOffset) =>
            // For GameCube, filesystem offsets are absolute within the image (no partition base), so map directly
            partitionImageOffset + fsOffset;

        // --- New persistence helpers ---
        public void PersistImageSection(long imageOffset, ISection section, ISectionData sectionData, bool isFs, DataStride stride)
        {
            if (section == null || sectionData == null) return;
            Stream writeStream = null;
            try
            {
                writeStream = BeginFileWrite(imageOffset, BlockType.Other, stride);
                section.Read((int)(isFs ? sectionData.FsOffset : sectionData.Offset), (int)sectionData.FsSize, writeStream);
                FinalizeFileWrite(imageOffset, writeStream);
                writeStream = null;
            }
            catch (Exception ex)
            {
                try { writeStream?.Dispose(); } catch { }
                _context?.Log?.Error(() => $"PersistImageSection failed at {imageOffset:X}: {ex.Message}");
            }
        }

        public void FinaliseSectionAndPersistBlockPadding(long imageOffset, ISection section, bool isFs, DataStride stride)
        {
        }

        public void ProcessSection(ISection section)
        {
            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            _storedRanges = DataStoreWiiFormatter.GetGapsAndNonCreatableDataRanges(section, stride).ToList();

            if (_storedRanges.Any(a => a.ImageOffset < section.ImageOffset || a.ImageOffset + a.Size > section.ImageOffset + section.Size))
            {
                _context.Log?.Error(() => $"ProcessSection: Calculated gap/non-creatable data ranges exceed section bounds at {section.ImageOffset:X}");
                return;
            }

            foreach (GapRange persist in _storedRanges)
            {
                Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride);
                section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                FinalizeFileWrite(persist.ImageOffset, writeStream);
                //_imageWriter.WriteData(persist.ImageOffset, section.Decrypted, (int)(persist.ImageOffset - section.ImageOffset), persist.Size, DataType.Other, stride);
            }
        }

        public bool ShouldPreserveFile(ISection section, IFsFile file)
        {
            // Skip junk files and zero-length files - they should be invisible to gap checking
            if (file == null)
                return false;

            if (file.FsSize == 0 && (file.PostGapSize == 0 || _storedRanges.Any(a => section.FsOffset + a.FsOffset <= file.FsOffset && section.FsOffset + a.FsOffset + a.Size >= file.FsOffset)))
                return false; // we must not store this as it is covered or matchines another file

            return !file.IsMissing && (file.FsSize > 0 || file.PostGapSize > 0); //do not preserve zero-length files without gaps
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
                    int sectionSize = ai.SectionSize;

                    _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);
                }
                catch (System.Exception ex)
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
        }

        public bool AlreadyExists => _imageWriter?.AlreadyExists ?? false;

        public void BuildFileSystemYaml(Scan scan)
        {
            if (_imageWriter == null || scan == null)
                return;

            FsYaml yaml = new FsYaml();
            FsYamlNode inlineRoot = null;

            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type != AreaType.FileSystem)
                    continue;

                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                // GameCube has a single game partition � use inline root
                if (inlineRoot == null)
                    inlineRoot = yaml.AddFileSystem(".", area.ImageOffset);

                IEnumerable<IFsFile> fileSource = area.FsInfo?.FidelityFiles?.Entries
                    ?? (IEnumerable<IFsFile>)fs.Files;

                foreach (IFsFile file in fileSource)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    long imageOffset = area.ImageOffset + file.FsOffset;

                    // System files (!!name) go under sys/, regular files under files/
                    string subDir = file.IsSystemFile ? "sys" : "files";
                    string fileName = file.IsSystemFile ? file.Name.Substring(2) : file.Name;
                    string dirPath = file.Path?.Trim('/') ?? "";
                    string fullPath = string.IsNullOrEmpty(dirPath)
                        ? $"/{subDir}/{fileName}"
                        : $"/{subDir}/{dirPath}/{fileName}";

                    inlineRoot.AddFileByPath(fullPath, imageOffset, file.FsSize, file.XxHash, file.Crc);
                }
            }

            // Mark the sys directory as system (boot.bin, bi2.bin, apploader.img, main.dol, fst.bin)
            if (inlineRoot?.Children != null)
            {
                foreach (FsYamlNode child in inlineRoot.Children)
                {
                    if (child.IsDirectory && child.Name.Equals("sys", StringComparison.OrdinalIgnoreCase))
                        child.IsSystem = true;
                }
            }

            byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(yaml).ToBytes();
            _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
        }

        public void Dispose()
        {
            try { _imageWriter?.Dispose(); } catch { }
            try { _dataStore?.Dispose(); } catch { }
        }
    }
}
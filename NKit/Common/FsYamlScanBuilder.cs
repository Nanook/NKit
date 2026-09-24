using NKitDataStore;

namespace Nanook.NKit
{
    /// <summary>
    /// Converts a <see cref="Scan"/> into an <see cref="FsYaml"/> tree by extracting filesystem
    /// files from each <see cref="ScanArea"/> and reconstructing the directory hierarchy from
    /// file paths. Image offsets are computed using the area's stride information so that the
    /// resulting tree maps directly back to the datastore offset table.
    /// </summary>
    internal static class FsYamlScanBuilder
    {
        /// <summary>
        /// Builds an <see cref="FsYaml"/> from a <see cref="Scan"/>.
        /// Each area with a filesystem becomes a filesystem root node. Files are placed
        /// into a directory tree derived from their <see cref="IFsFile.FullName"/> paths.
        /// </summary>
        public static FsYaml Build(Scan scan)
        {
            FsYaml yaml = new FsYaml();

            foreach (ScanArea area in scan.Areas)
            {
                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                AreaInfo ai = area.AreaInfo;

                // Mirror the naming convention used by Scan.VirtualFs
                string fsName = $"{ai.AreaNo + 1:D2} {ai.Type}";
                FsYamlNode fsNode = yaml.AddFileSystem(fsName, area.ImageOffset);

                // Build a DataStride when the area uses strided blocks (e.g., Wii)
                DataStride stride = null;
                if (ai.BlockSize > 0 && ai.BlockFsSize > 0 && ai.BlockSize != ai.BlockFsSize)
                    stride = new DataStride { SourceBlockSize = ai.BlockSize, DataOffset = ai.BlockFsOffset, DataLength = ai.BlockFsSize };

                foreach (IFsFile file in fs.Files)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    long imageOffset = stride != null
                        ? area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)
                        : area.ImageOffset + file.FsOffset;

                    fsNode.AddFileByPath(file.FullName, imageOffset, file.FsSize, file.XxHash, file.Crc);
                }
            }

            return yaml;
        }
    }
}
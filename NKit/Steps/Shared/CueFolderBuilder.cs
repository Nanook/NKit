using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    internal class CueFolderBuilder
    {
        /// <summary>
        /// Builds a CueFolder image from the processed child CUE/GDI images and source folder.
        /// </summary>
        /// <param name="dedupePath">DataStore directory path</param>
        /// <param name="setName">Target set name</param>
        /// <param name="folderName">Display name for the CueFolder</param>
        /// <param name="childImages">List of child image records (the individual CUE/GDI images)</param>
        /// <param name="sourceFolder">Path to the original source folder</param>
        /// <param name="shardSize">Shard size for the set</param>
        /// <param name="blockSize">Block size for the set</param>
        /// <param name="system">System identifier</param>
        public void Build(string dedupePath, string setName, string folderName,
            List<ChildImageInfo> childImages, string sourceFolder,
            long shardSize, int blockSize, string system = "Directories")
        {
            // Soft-delete any existing CueFolder with the same name
            using (IDataStore dataStore = new DataStore(dedupePath))
            {
                softDeleteExistingCueFolder(dataStore, setName, folderName);
            }

            // Collect all ifs file names from child images so we can exclude them from fs
            HashSet<string> ifsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChildImageInfo child in childImages)
            {
                if (child.Files != null)
                {
                    foreach (ChildImageFile file in child.Files)
                        ifsFileNames.Add(file.FileName);
                }
            }

            // Create a new CueFolder image via DataStoreFolderFormatter
            using (DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(dedupePath, folderName, shardSize, blockSize, setName, ImageFormat.CueFolder, system))
            {
                // Store non-track auxiliary files from source folder (.m3u, .nfo, .txt, etc.)
                if (Directory.Exists(sourceFolder))
                {
                    storeAuxiliaryFiles(formatter, sourceFolder, ifsFileNames);
                }

                // Build filesystem.yaml with fs + ifs sections
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);

                // Add fs entries from formatter's tracked files
                foreach (FolderFileEntry entry in formatter.FileEntries)
                {
                    root.AddFileByPath(entry.RelativePath, entry.OffsetStart, entry.Size,
                        entry.XxHash64, entry.Crc32);
                }

                // Add ifs entries from child images
                foreach (ChildImageInfo child in childImages)
                {
                    if (child.Files == null)
                        continue;

                    foreach (ChildImageFile file in child.Files)
                    {
                        fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size);
                    }
                }

                // Write the custom filesystem.yaml and finalize
                formatter.WriteFileSystemYaml(fsYaml);
                formatter.FinalizeImage(formatter.CurrentOffset, 0, 0);
            }
        }

        /// <summary>
        /// Stores non-track auxiliary files from the source folder into the formatter.
        /// Skips track data files (.bin, .raw, .wav, .iso, .img) and files in the ifs section.
        /// Only stores auxiliary files like .m3u, .nfo, .txt, .jpg, .png, etc.
        /// </summary>
        private void storeAuxiliaryFiles(DataStoreFolderFormatter formatter, string sourceFolder, HashSet<string> ifsFileNames)
        {
            // Track data file extensions to exclude
            HashSet<string> trackExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bin", ".raw", ".wav", ".iso", ".img"
            };

            // Index file extensions to exclude (stored separately as loose files)
            HashSet<string> indexExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cue", ".gdi"
            };

            foreach (FileInfo fi in new DirectoryInfo(sourceFolder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                string relativePath = Path.GetRelativePath(sourceFolder, fi.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/');

                // Skip files that are in the ifs section (child image files)
                if (ifsFileNames.Contains(fi.Name))
                    continue;

                // Skip track data files
                if (trackExtensions.Contains(fi.Extension))
                    continue;

                // Skip index files (CUE/GDI) — stored as loose files on child images
                if (indexExtensions.Contains(fi.Extension))
                    continue;

                using (FileStream stream = fi.OpenRead())
                {
                    formatter.StoreFile(relativePath, stream, fi.Length);
                }
            }
        }

        /// <summary>
        /// Soft-deletes any existing CueFolder for the same name in the set.
        /// </summary>
        private void softDeleteExistingCueFolder(IDataStore dataStore, string setName, string folderName)
        {
            foreach (ImageRecord image in dataStore.ListAllImages(img =>
                img.SetName == setName &&
                !img.Removed &&
                img.Format == ImageFormat.CueFolder &&
                img.Name == folderName))
            {
                dataStore.DeleteImage(new GlobalImageKey(setName, image.Id));
            }
        }
    }
}
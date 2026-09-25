using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    internal class TmdAppFolderBuilder
    {
        /// <summary>
        /// Builds a TmdAppFolder image from the processed child images and source folder.
        /// </summary>
        /// <param name="dedupePath">DataStore directory path</param>
        /// <param name="setName">Target set name</param>
        /// <param name="folderName">Display name for the TmdAppFolder (base image name without index suffix)</param>
        /// <param name="childImages">List of child image records (the individual TmdApp images)</param>
        /// <param name="sourceFolder">Path to the original source folder, or archive path when isArchived is true</param>
        /// <param name="shardSize">Shard size for the set</param>
        /// <param name="blockSize">Block size for the set</param>
        /// <param name="system">System identifier</param>
        /// <param name="isArchived">True if sourceFolder is an archive path</param>
        /// <param name="archiveFiles">Archive file items for opening the archive (required when isArchived is true)</param>
        public void Build(string dedupePath, string setName, string folderName,
            List<ChildImageInfo> childImages, string sourceFolder,
            long shardSize, int blockSize, string system = "Directories",
            bool isArchived = false, SourceFileItem[] archiveFiles = null)
        {
            // Soft-delete any existing TmdAppFolder with the same base name
            using (IDataStore dataStore = new DataStore(dedupePath))
            {
                softDeleteExistingTmdAppFolder(dataStore, setName, folderName);
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

            // Create a new TmdAppFolder image via DataStoreFolderFormatter
            using (DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(dedupePath, folderName, shardSize, blockSize, setName, ImageFormat.TmdAppFolder, system))
            {
                // Store real files from sourceFolder (non-.app files that aren't in ifs)
                if (isArchived && archiveFiles != null)
                {
                    storeFilesFromArchive(formatter, archiveFiles, ifsFileNames);
                }
                else if (Directory.Exists(sourceFolder))
                {
                    storeFilesFromDirectory(formatter, sourceFolder, ifsFileNames);
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
        /// Stores files from a local directory into the formatter, skipping ifs and .app files.
        /// </summary>
        private void storeFilesFromDirectory(DataStoreFolderFormatter formatter, string sourceFolder, HashSet<string> ifsFileNames)
        {
            foreach (FileInfo fi in new DirectoryInfo(sourceFolder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                string relativePath = Path.GetRelativePath(sourceFolder, fi.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/');

                if (ifsFileNames.Contains(fi.Name))
                    continue;

                if (fi.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase))
                    continue;

                using (FileStream stream = fi.OpenRead())
                {
                    formatter.StoreFile(relativePath, stream, fi.Length);
                }
            }
        }

        /// <summary>
        /// Stores files from an archive into the formatter using NKit's SourceFileSystem,
        /// skipping ifs and .app files.
        /// </summary>
        private void storeFilesFromArchive(DataStoreFolderFormatter formatter, SourceFileItem[] archiveFiles, HashSet<string> ifsFileNames)
        {
            string archivePath = Path.Combine(archiveFiles[0].Path, archiveFiles[0].FileName);
            SourceFileSystem sfs = new SourceFileSystem(archiveFiles, null);
            FileMask mask = FileMask.CreateLocalMask(archivePath, true);
            List<FileItem> files = sfs.GetFiles(mask, null);

            using (SourceFileSystemReader reader = sfs.CreateReader(null))
            {
                foreach (FileItem file in files.OrderBy(f => f.PathFileName, StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(file.PathFileName);

                    // Skip files that are in the ifs section (child image files like .app)
                    if (ifsFileNames.Contains(fileName))
                        continue;

                    // Skip .app files — these are stored as child images
                    string ext = Path.GetExtension(fileName);
                    if (ext.Equals(".app", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Use the archive-relative path
                    string relativePath = file.PathFileName.Replace(Path.DirectorySeparatorChar, '/');

                    using (Stream stream = reader.OpenRead(file))
                    {
                        if (stream != null)
                            formatter.StoreFile(relativePath, stream, file.Size);
                    }
                }
            }
        }

        /// <summary>
        /// Soft-deletes any existing TmdAppFolder for the same base name in the set.
        /// </summary>
        private void softDeleteExistingTmdAppFolder(IDataStore dataStore, string setName, string folderName)
        {
            foreach (ImageRecord image in dataStore.ListAllImages(img =>
                img.SetName == setName &&
                !img.Removed &&
                img.Format == ImageFormat.TmdAppFolder &&
                img.Name == folderName))
            {
                dataStore.DeleteImage(new GlobalImageKey(setName, image.Id));
            }
        }
    }
}
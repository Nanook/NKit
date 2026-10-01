using Nanook.NKit.Container.ZArchive;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// Stores the contents of a WUA (ZArchive) file into the DataStore as a TmdAppFolder image.
    ///
    /// A WUA file is a ZArchive containing one or more decrypted WiiU Loadiine title dumps, each
    /// in a subfolder named &lt;16-digit-titleId&gt;_v&lt;version&gt;. Each title subfolder contains the
    /// game filesystem directly (code/, content/, meta/) with no .app / tmd / cetk structure.
    ///
    /// The ZArchive is transparent — the title folder is treated exactly as a Loadiine folder on
    /// disk would be, and its files are stored via DataStoreFolderFormatter in the same way
    /// `nkds adddir` stores a folder.
    /// </summary>
    internal class WuaFolderBuilder
    {
        private static readonly Regex _titleFolderRegex =
            new Regex(@"^[0-9a-fA-F]{16}_v[0-9]+$", RegexOptions.Compiled);

        /// <summary>
        /// Stores the first title in the WUA archive into the DataStore.
        /// </summary>
        public void Build(
            Stream wuaStream,
            string wuaName,
            string dedupePath,
            string setName,
            long shardSize,
            int blockSize,
            string system = "WiiU",
            Action<string, LogLevel> log = null)
        {
            ZArchiveReader archive;
            try
            {
                archive = ZArchiveReader.Open(wuaStream);
            }
            catch (HandledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"WuaFolderBuilder: failed to open ZArchive '{wuaName}'");
            }

            using (archive)
            {
                // Find the first matching title folder in the archive root
                string titleFolder = archive.GetRootDirectories()
                    .FirstOrDefault(d => _titleFolderRegex.IsMatch(d));

                if (titleFolder == null)
                    throw new HandledException(
                        $"WuaFolderBuilder: no valid title folder found in '{wuaName}' " +
                        $"(expected <16-digit-titleId>_v<version>).");

                // Use the WUA filename (without extension) as the image name
                string imageName = wuaName;

                log?.Invoke(Log.Section, LogLevel.Info);
                log?.Invoke(InfoPrefix.Stamp(InfoPrefix.Title, $"[WuaFolder/{system}]  {imageName}"), LogLevel.Info);
                log?.Invoke(Log.Divider, LogLevel.Info);
                log?.Invoke($"{LogScopes.Tag(LogScopes.Input)}title folder: {titleFolder}", LogLevel.Info);

                var files = archive.EnumerateFiles(titleFolder)
                    .OrderBy(f => f.path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                log?.Invoke($"{LogScopes.Tag(LogScopes.Input)}files: {files.Count}", LogLevel.Detail);

                using (DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(
                    dedupePath, imageName, shardSize, blockSize, setName,
                    ImageFormat.TmdAppFolder, system))
                {
                    long totalSize = 0;
                    int fileIndex = 0;

                    foreach ((string relPath, long size) in files)
                    {
                        string archivePath = titleFolder + "/" + relPath;
                        log?.Invoke($"  [{fileIndex + 1}/{files.Count}] {relPath} ({size:N0} bytes)", LogLevel.Detail);

                        if (size == 0)
                        {
                            formatter.StoreFile(relPath, Stream.Null, 0);
                        }
                        else
                        {
                            using (Stream fileStream = archive.OpenFile(archivePath))
                                formatter.StoreFile(relPath, fileStream, size);
                            totalSize += size;
                        }

                        fileIndex++;
                    }

                    formatter.BuildFileSystemYaml();
                    formatter.FinalizeImage(totalSize, 0, 0);

                    log?.Invoke($"{LogScopes.Tag(LogScopes.Input)}stored {fileIndex:N0} file(s), {totalSize:N0} bytes", LogLevel.Info);
                }
            }
        }
    }
}

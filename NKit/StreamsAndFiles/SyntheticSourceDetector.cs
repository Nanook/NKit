using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Detects groups of source files that should produce a synthetic folder image.
    /// Supports TmdApp groups (multiple tmd.X files in the same directory) and
    /// CueFolder groups (2+ CUE or GDI images in the same directory).
    /// </summary>
    internal static class SyntheticSourceDetector
    {
        /// <summary>
        /// Analyzes scanned source files and returns folder group descriptors
        /// for groups that need a synthetic folder SourceFile.
        /// </summary>
        /// <param name="realSources">The scanned real SourceFiles</param>
        /// <returns>List of folder groups needing synthetic sources, sorted by BaseName</returns>
        public static List<FolderGroupInfo> DetectFolderGroups(IEnumerable<SourceFile> realSources)
        {
            List<FolderGroupInfo> result = new List<FolderGroupInfo>();

            // Step 1: Group valid sources by their source container.
            // For archived sources, BasePath is the directory containing the archive,
            // NOT the archive itself. Multiple archives in the same directory would
            // all share the same BasePath, incorrectly merging their tmd files into
            // one group. We need to include the archive filename in the grouping key
            // so each archive produces its own folder group.
            IEnumerable<IGrouping<string, SourceFile>> byContainer = realSources
                .Where(sf => sf.Status == SourceFileResult.Valid)
                .GroupBy(sf => GetContainerKey(sf), StringComparer.OrdinalIgnoreCase);

            // Step 2: Within each container, find TmdApp groups
            foreach (IGrouping<string, SourceFile> dirGroup in byContainer)
            {
                List<SourceFile> tmdAppSources = dirGroup
                    .Where(sf => sf.IndexFile?.FileType == IndexFileType.TmdApp)
                    .OrderBy(sf => sf.IndexFile?.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Create a folder group for any container with 1+ tmd files.
                // Even single-tmd folders get a TmdAppFolder so that mount
                // names are uniform across all WiiU content.
                if (tmdAppSources.Count >= 1)
                {
                    SourceFile firstChild = tmdAppSources[0];
                    // For archived sources, SourceFolder is the full archive path
                    // (directory + archive filename) so TmdAppFolderBuilder can
                    // enumerate files from the archive. For directory sources,
                    // SourceFolder is the directory path (same as BasePath).
                    string sourceFolder = firstChild.IsArchived
                        ? System.IO.Path.Combine(firstChild.ArchiveFiles[0].Path, firstChild.ArchiveFiles[0].FileName)
                        : dirGroup.Key;

                    result.Add(new FolderGroupInfo
                    {
                        GroupType = FolderGroupType.TmdAppFolder,
                        BaseName = firstChild.Name,
                        SourceFolder = sourceFolder,
                        ChildSources = tmdAppSources,
                        SystemType = SystemType.WiiU,
                        IsArchived = firstChild.IsArchived,
                        ArchiveFiles = firstChild.IsArchived ? firstChild.ArchiveFiles : null,
                    });
                }

                // Step 2b: Within each container, find CUE/GDI groups.
                // A CueFolder is only created when 2+ CUE or GDI images exist
                // in the same container (single-image containers are stored standalone).
                List<SourceFile> cuegdiSources = dirGroup
                    .Where(sf => sf.IndexFile?.FileType == IndexFileType.Cue || sf.IndexFile?.FileType == IndexFileType.Gdi)
                    .OrderBy(sf => sf.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (cuegdiSources.Count >= 2)
                {
                    SourceFile firstChild = cuegdiSources[0];
                    string sourceFolder = firstChild.IsArchived
                        ? System.IO.Path.Combine(firstChild.ArchiveFiles[0].Path, firstChild.ArchiveFiles[0].FileName)
                        : dirGroup.Key;

                    result.Add(new FolderGroupInfo
                    {
                        GroupType = FolderGroupType.CueFolder,
                        BaseName = firstChild.Name,
                        SourceFolder = sourceFolder,
                        ChildSources = cuegdiSources,
                        SystemType = firstChild.SystemType,
                        IsArchived = firstChild.IsArchived,
                        ArchiveFiles = firstChild.IsArchived ? firstChild.ArchiveFiles : null,
                    });
                }
            }

            // Step 3: Sort by base name for deterministic ordering
            result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.BaseName, b.BaseName));

            return result;
        }

        /// <summary>
        /// Returns a grouping key that uniquely identifies the source container.
        /// For archived sources, this is the full archive path (directory + filename)
        /// so that different archives in the same directory produce separate groups.
        /// For directory sources, this is the directory path (BasePath).
        /// </summary>
        private static string GetContainerKey(SourceFile sf)
        {
            if (sf.IsArchived && sf.ArchiveFiles != null && sf.ArchiveFiles.Length > 0)
                return System.IO.Path.Combine(sf.ArchiveFiles[0].Path, sf.ArchiveFiles[0].FileName);

            return sf.BasePath;
        }
    }
}
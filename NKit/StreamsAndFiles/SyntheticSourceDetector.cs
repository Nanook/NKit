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

                // Step 2b (WUA): each .wua file becomes a standalone WuaFolder group.
                // WUA files are self-contained ZArchives with Loadiine content — one image per file.
                foreach (SourceFile wua in dirGroup.Where(sf => sf.ImageType == SourceImageType.Wua && !sf.IsArchived))
                {
                    result.Add(new FolderGroupInfo
                    {
                        GroupType    = FolderGroupType.WuaFolder,
                        BaseName     = wua.Name,
                        SourceFolder = System.IO.Path.Combine(wua.ImageFiles[0].Path, wua.ImageFiles[0].FileName),
                        ChildSources = new List<SourceFile> { wua },
                        SystemType   = SystemType.WiiU,
                        IsArchived   = false,
                    });
                }

                // Step 2c: Within each container, find CUE/GDI groups.
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
        /// Probes the given scan directories for Loadiine game folders (code/ + content/ or meta/).
        /// Called from ScanGrouped() after normal scan detection so already-claimed folders
        /// (those with a tmd or .wua source) are excluded.
        /// </summary>
        public static List<FolderGroupInfo> DetectLoadiineFolders(
            IEnumerable<string> scanPaths,
            IEnumerable<FolderGroupInfo> alreadyClaimed)
        {
            var result = new List<FolderGroupInfo>();

            // Build a set of directories already claimed by other group types
            var claimedDirs = new System.Collections.Generic.HashSet<string>(
                alreadyClaimed.Select(g => g.SourceFolder),
                StringComparer.OrdinalIgnoreCase);

            foreach (string scanPath in scanPaths)
            {
                if (!System.IO.Directory.Exists(scanPath)) continue;

                // Check the scan path itself, then each immediate subdirectory
                foreach (string candidate in getCandidateDirs(scanPath))
                {
                    if (claimedDirs.Contains(candidate)) continue;
                    if (!IsLoadiineFolder(candidate)) continue;

                    string name = System.IO.Path.GetFileName(
                        candidate.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                          System.IO.Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(name)) continue;

                    result.Add(new FolderGroupInfo
                    {
                        GroupType    = FolderGroupType.LoadiineFolder,
                        BaseName     = name,
                        SourceFolder = candidate,
                        ChildSources = new System.Collections.Generic.List<SourceFile>(),
                        SystemType   = SystemType.WiiU,
                        IsArchived   = false,
                    });

                    claimedDirs.Add(candidate); // prevent double-adding
                }
            }

            return result;
        }

        /// <summary>
        /// Returns directories to probe: the path itself if it looks like a game folder,
        /// otherwise each immediate subdirectory (for scanning a collection folder).
        /// </summary>
        private static System.Collections.Generic.IEnumerable<string> getCandidateDirs(string path)
        {
            if (IsLoadiineFolder(path))
            {
                yield return path;
                yield break;
            }
            // Walk one level of subdirectories
            foreach (string sub in System.IO.Directory.EnumerateDirectories(path))
                yield return sub;
        }

        /// <summary>
        /// Returns true if the directory looks like a Loadiine game folder.
        /// Criterion: has a code/ subdirectory AND at least one of content/ or meta/,
        /// AND no tmd / tmd.* / .wua file at the top level (to avoid false positives).
        /// </summary>
        internal static bool IsLoadiineFolder(string path)
        {
            if (!System.IO.Directory.Exists(path)) return false;

            bool hasCode    = System.IO.Directory.Exists(System.IO.Path.Combine(path, "code"));
            bool hasContent = System.IO.Directory.Exists(System.IO.Path.Combine(path, "content"));
            bool hasMeta    = System.IO.Directory.Exists(System.IO.Path.Combine(path, "meta"));

            if (!hasCode || (!hasContent && !hasMeta)) return false;

            // Reject if any tmd or .wua file exists — that means it's a TmdApp/WUA source
            foreach (string f in System.IO.Directory.EnumerateFiles(path))
            {
                string fn = System.IO.Path.GetFileName(f).ToLowerInvariant();
                if (fn.StartsWith("tmd") || fn.EndsWith(".wua"))
                    return false;
            }

            return true;
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
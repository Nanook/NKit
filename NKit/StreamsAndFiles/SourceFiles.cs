using SharpCompress.Archives;
using SharpCompress.Archives.GZip;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit
{

    public static class SourceFiles
    {
        internal readonly static string[] _NonDataKnownExts;
        private readonly static string[] _ImgExts;
        private readonly static string[] _ArcExts;
        // Streamable archive extensions (zip/rar/7z/gz). Excludes .nkds, which is a DataStore
        // database (read via the DataStore API), NOT a normal forward-streamable archive. The
        // Wii archive-backed fix-file convention only accepts streamable archives.
        private readonly static string[] _StreamArcExts;
        private readonly static string[] _NkdsExts;
        private readonly static string[] _ArcRegex;
        private readonly static string[] _SplitRegex;
        private readonly static string[] _IdxExts;
        private readonly static Dictionary<string, string> _SortExts;
        private readonly static Regex _FileMask;

        /// <summary>
        /// Image file extensions as file picker patterns (e.g. "*.iso").
        /// </summary>
        public static IReadOnlyList<string> ImageFilePatterns { get; }

        /// <summary>
        /// Archive file extensions as file picker patterns (e.g. "*.zip").
        /// </summary>
        public static IReadOnlyList<string> ArchiveFilePatterns { get; }

        /// <summary>
        /// All supported file extensions (images + archives) as file picker patterns.
        /// </summary>
        public static IReadOnlyList<string> SupportedFilePatterns { get; }

        static SourceFiles()
        {
            //Name parsing with group comments - used by FileItem.Populate

            _NonDataKnownExts = new[] { ".cue", ".gdi", ".tmd", ".cert", ".tik", ".h3", ".cetk" };

            //[Group 3] index extensions
            _IdxExts = new[] { ".cue", ".gdi", ".tmd" };       //ccd, mds, wiiu tmd
            //[Group 4] image extensions
            _ImgExts = new[] { ".nkit.iso", ".nkit.gcz", ".iso.dec", ".dec.iso", ".iso", ".xiso", ".360", ".bin", ".raw", ".ciso", ".wbfs", ".gcz", ".gcm", ".wia", ".rvz", ".wud", ".wux", ".cso", ".zso", ".dax", ".jso", ".chd", ".app", ".tik", ".cert", ".h3", ".cetk", "." + NKitTask.ScanExt }; //mdf, sub, ordered for regex matching
            //[Group 5] is split
            string isSplitImage = @"(\.[0-9]{3,})?";           //is split image
            //multipart archives (may or may not include part 1) - aligns to SourceSplitType
            _ArcRegex = new[] {
                @"\.(?:part[0-9]+|[0-9]{2})(\.rar)",           //[Group 6] 
                @"(\.(?:[r-y]|zx|z)[0-9]{2,})",                //[Group 7]
                @"_[0-9]+(\.nkds)" };                          //[Group 8] nkds shard
            //[Group 9] archive extensions
            // Streamable archives (forward-readable via SharpCompress) vs the .nkds DataStore
            // database (read via the DataStore API). _ArcExts stays the full set so every existing
            // consumer (regex/_SortExts/ArchiveFilePatterns/CreateSortKey) is unchanged.
            _StreamArcExts = new[] { ".zipx", ".zip", ".rar", ".7z", ".gz" };
            _NkdsExts = new[] { ".nkds" };
            _ArcExts = _StreamArcExts.Concat(_NkdsExts).ToArray();
            //[Group 10] is split
            string isSplitArc = @"(\.[0-9]{3,})?";             //is split image

            //split file parts (may or may not include part 1)
            _SplitRegex = new[] {
                @"(\.wbf[1-9])",                               //Group 11=wbf1
                @"(?<=Track) ?[0-9]+(\..{3,})",                //Group 12=Track1.bin
                @"\s\(Track ?[0-9]+\)(\..{3,})",               //Group 13=(Track 1.bin)
                @"((?<!^(?:tmd|cetk|cert|tik))\.[0-9]{3,})" }; //Group 14=UnknownExt (not CDN format)

            string _otherRegex = @"(?<![^\/])(?:(?:tmd|cetk|tik)(?:\.[0-9]+)?.*|ps3_disc.sfb)$";

            //[Group 1=Name, 2=PostFix (everything after filename)]
            _FileMask = new Regex(string.Concat(@"^(?:(.*?)(?:(",
                @"(", string.Join("|", _IdxExts).Replace(".", @"\."), @")|",
                @"(?:(", string.Join("|", _ImgExts).Replace(".", @"\."), @")", isSplitImage, ")|",
                string.Join("|", _ArcRegex), @"|",
                @"(?:(", string.Join("|", _ArcExts).Replace(".", @"\."), @")" + isSplitArc + ")|",
                string.Join("|", _SplitRegex),
            $"))|({_otherRegex}))$"), RegexOptions.Compiled | RegexOptions.IgnoreCase); //Concat() rather than Format() to simply escaping {2} items


            _SortExts = new Dictionary<string, string>();
            foreach (string e in _IdxExts.Concat(_ImgExts).Concat(_ArcExts))
                _SortExts.Add(e, e.Replace(".", ""));
            //update extensions != 3 chars. Add postfixes with nums removed
            _SortExts[".nkit.iso"] = "nki";
            _SortExts[".nkit.gcz"] = "nkg";
            _SortExts[".iso.dec"] = "idc";
            _SortExts[".dec.iso"] = "dci";
            _SortExts[".cert"] = "crt";
            _SortExts[".cetk"] = "ctk";
            _SortExts[".h3"] = "ah3";
            _SortExts[".nkit"] = "nkt";
            _SortExts["." + NKitTask.ScanExt] = "nky";
            _SortExts[".wbfs"] = "wbf";
            _SortExts[".ciso"] = "cis";
            _SortExts[".xiso"] = "xis";
            _SortExts[".zipx"] = "zpx";
            _SortExts[".nkds"] = "nkd";
            _SortExts[".r"] = "rar";    //r00
            _SortExts[".s"] = "ras";    //s00
            _SortExts[".t"] = "rat";    //t00
            _SortExts[".u"] = "rau";    //u00
            _SortExts[".v"] = "rav";    //v00
            _SortExts[".w"] = "raw";    //w00
            _SortExts[".x"] = "rax";    //x00
            _SortExts[".y"] = "ray";    //y00
            _SortExts[".part"] = "prr"; //part01.rar
            _SortExts[".z"] = "zip";    //z01
            _SortExts[".zx"] = "zpx";   //zx01
            _SortExts[".wbf"] = "wbf";  //wbf1
            _SortExts[".bin"] = "bin";  //used, but not a key file
            _SortExts["(track).bin"] = "tbn";
            _SortExts["track.bin"] = "tbx";
            _SortExts["."] = "   ";     //001
            _SortExts["_"] = "nkd";     //_0000.nkds (shard)

            // Expose file picker patterns for UI consumption
            ImageFilePatterns = _IdxExts.Concat(_ImgExts).Distinct().Select(e => "*" + e).ToArray();
            ArchiveFilePatterns = _ArcExts.Select(e => "*" + e).ToArray();
            SupportedFilePatterns = ImageFilePatterns.Concat(ArchiveFilePatterns).ToArray();
        }

        /// <summary>
        /// True when <paramref name="extension"/> is a normal forward-streamable archive
        /// (zip/rar/7z/gz). Excludes the .nkds DataStore database. Extension is matched
        /// case-insensitively and may be with or without the leading dot.
        /// </summary>
        internal static bool IsStreamableArchiveExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;
            if (extension[0] != '.')
                extension = "." + extension;
            return _StreamArcExts.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        internal static IEnumerable<SourceFile> GroupFiles(FileInfo[] inFiles, FileMask mask, bool validOnly, ILogScope log)
        {
            SourceFileSystem sfs = new SourceFileSystem(log); //handles multipart archives);
            foreach (FileInfo f in inFiles)
                sfs.AddFile(f, 0, mask);
            SortedList<string, List<FileItem>> temp = merge(sfs.GetFiles(new FileMask[] { mask }, null));
            foreach (List<FileItem> items in temp.Values)
            {
                process(items, log); //group, and merge temp to list
                foreach (FileItem item in items)
                {
                    SourceFile sf = CreateSourceFile(item);
                    if (sf.Status == SourceFileResult.Valid)
                        yield return sf;
                }

            }
        }

        public static List<SourceFile> ScanGrouped(string[] masks, bool scanSubfolders, bool scanArchives, bool validOnly, ILogScope log, CancellationToken? cancel)
        {
            List<SourceFile> images = Scan(masks, scanSubfolders, scanArchives, validOnly, log, cancel)
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Only create synthetic TmdAppFolder sources when the user specified a folder
            // (wildcard mask) OR when multiple files are passed (e.g. from the UI selecting
            // multiple TMD files from the same folder). A single specific tmd file should
            // not be wrapped in a folder group.
            bool isFolderScan = masks.Length > 1;
            if (!isFolderScan)
            {
                foreach (string mask in masks)
                {
                    FileMask fm = FileMask.CreateLocalMask(mask, scanSubfolders);
                    if (fm.Mask.Contains('*') || fm.Mask.Contains('?'))
                    {
                        isFolderScan = true;
                        break;
                    }
                }
            }

            if (isFolderScan)
            {
                List<FolderGroupInfo> folderGroups = SyntheticSourceDetector.DetectFolderGroups(images);
                SyntheticSourceFactory.InsertSyntheticSources(images, folderGroups);
            }
            return images;
        }

        public static IEnumerable<SourceFile> Scan(string[] masks, bool scanSubfolders, bool scanArchives, bool validOnly, ILogScope log, CancellationToken? cancel)
        {

            foreach (string mask in masks)
                log?.Detail(() => $"Scanning Mask [{mask}]");
            //log?.Info(() => string.Format("Scanning for files [Recursive:{0}, Archives:{1}, ValidOnly:{2}]...", scanSubfolders ? "Y" : "N", scanArchives ? "Y" : "N", validOnly ? "Y" : "N"));
            log?.Info(() => string.Format("Scanning for files [Recursive:{0}, Archives:{1}]...", scanSubfolders ? "Y" : "N", scanArchives ? "Y" : "N"));

            FileMask[] fileMasks = createMasks(masks, scanSubfolders).ToArray();
            Exception exception = null;

            //scan the folders and get a unique list of files
            foreach (IGrouping<string, FileMask> maskGroup in fileMasks.GroupBy(a => a.Path))
            {
                if (cancel?.IsCancellationRequested ?? false)
                    throw new HandledException("File Scanning Cancelled!");

                int maskCount = maskGroup.Count();

                BlockingCollection<SourceFile> collection = new BlockingCollection<SourceFile>();

                log?.Detail(() => $"Scanning [{maskGroup.Key}] for matches [{(maskCount == 1 ? maskGroup.First().OriginalMask : $"{maskCount} mask{maskCount.s()}")}]...");

                Task.Run(() =>
                {
                    try
                    {
                        Parallel.ForEach(readLocalFiles(log, maskGroup.ToArray(), cancel), fileItems =>
                        {
                            string path = fileItems[0].Path; //copy now as may be no items once processed
                            process(fileItems, log); //group items in to a single item with parts
                            log?.Detail(() =>
                            {
                                int arcs = fileItems.Count(a => a.IsArchive);
                                int files = fileItems.Count - arcs;
                                return $"Found [Files: {files}] and [Archives: {arcs}] in [{path}]";
                            });
                            if (fileItems.Count != 0)
                            {
                                Parallel.ForEach(fileItems, fileItem =>
                                {
                                    if (!fileItem.IsArchive)
                                    {
                                        SourceFile sf = CreateSourceFile(fileItem);
                                        if (!validOnly || sf.Status == SourceFileResult.Valid)
                                            collection.Add(sf);
                                    }
                                    else if (scanArchives || fileItem.Mask.MaskInArc != null)
                                    {
                                        //foreach (List<FileItem> archiveItems in readArchive(fileItem, log, cancel)) //scan the folders and get a unique list of files
                                        Parallel.ForEach(readArchive(fileItem, log, cancel), archiveItems => //scan the folders and get a unique list of files
                                        {
                                            if (cancel?.IsCancellationRequested ?? false)
                                                throw new HandledException("File Scanning Cancelled!");

                                            string outerPath = archiveItems[0].Archive.PathFileName; //copy now as may be no items once processed
                                            string innerPath = archiveItems[0].Path;
                                            process(archiveItems, log); //group, and merge temp to list
                                            log?.Detail(() =>
                                            {
                                                int arcs = archiveItems.Count(a => a.IsArchive);
                                                int files = archiveItems.Count - arcs;
                                                return $"Found [Files: {files}] and [Archives: {arcs}] in Archive [{outerPath}//{innerPath.TrimStart('/')}]";
                                            });
                                            if (archiveItems.Count != 0)
                                            {
                                                foreach (FileItem arcItem in archiveItems)
                                                {
                                                    SourceFile sf = CreateSourceFile(arcItem);
                                                    if (!validOnly || sf.Status == SourceFileResult.Valid)
                                                        collection.Add(sf);
                                                }
                                            }
                                        });
                                    }
                                });
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                        // Log full exception to help diagnose nested/aggregate failures during scanning
                        try { log?.Error(() => ex.ToString()); } catch { }
                    }
                    finally
                    {
                        collection.CompleteAdding();
                    }
                });


                SourceFile sf;
                while (exception == null)
                {
                    if (collection.TryTake(out sf, 100)) //exceptions internally, it's swallowed. Unlike when enumerator is used
                    {
                        logSourceFile(sf, log);
                        yield return sf;
                    }
                    else if (collection.IsAddingCompleted)
                        break;
                }
                collection.Dispose();

                if (exception != null)
                    throw exception;
                else if (cancel?.IsCancellationRequested ?? false)
                    throw new HandledException("Cancelled!");
            }

            log?.Detail(() => Log.Divider);
        }

        private static void logSourceFile(SourceFile sf, ILogScope log)
        {
            log?.Trace(() => Log.Divider);

            // Detail: one summary line per source file — status, format/archive type, total size and
            // the make-up (image parts / index / archive parts). Tagged [SourceFile] so the scan is
            // filterable. The per-part breakdown below is ALSO Detail now (was Debug/Trace) so a
            // detail run shows exactly which files/parts compose the source and their sizes — the
            // information needed when a split/archived/indexed source is picked up wrongly.
            // Prefer sf.Length (sum of image-part sizes); fall back to summing the parts directly in
            // case it has not been populated for this source shape.
            long totalSize = sf.Length != 0 ? sf.Length : (sf.ImageFiles?.Sum(f => f.Size) ?? 0);
            log?.Detail(() => LogScopes.Tag(LogScopes.SourceFile)
                + $"{sf.Name} - [Status:{sf.Status}, Type:{sf.ImageType}"
                + $"{(sf.IsArchived ? $"/{sf.ArchiveType}" : "")}, Size:0x{totalSize:X}"
                + $", ArchiveParts:{sf.ArchiveFiles?.Length ?? 0}, FileParts:{sf.ImageFiles.Length}"
                + $", IndexType:{(sf.IndexFile == null ? "None" : sf.IndexFile.Extension)}, File:{sf.FriendlyFullPath}]");

            if (sf.ArchiveFiles != null)
            {
                foreach (SourceFileItem f in sf.ArchiveFiles)
                    log?.Detail(() => LogScopes.Tag(LogScopes.SourceFile) + $"{sf.Name} - Archive part: {f.FileName} size 0x{f.Size:X}");
            }
            if (sf.IndexFile != null)
                log?.Detail(() => LogScopes.Tag(LogScopes.SourceFile) + $"{sf.Name} - Index: {sf.IndexFile.FileName} ({sf.IndexFile.Items?.Length ?? 0} track{((sf.IndexFile.Items?.Length ?? 0) == 1 ? "" : "s")})");
            foreach (SourceFileItem f in sf.ImageFiles)
                log?.Detail(() => LogScopes.Tag(LogScopes.SourceFile) + $"{sf.Name} - Image part: {f.FileName} size 0x{f.Size:X}");
        }

        private static IEnumerable<List<FileItem>> readLocalFiles(ILogScope log, FileMask[] masks, CancellationToken? cancel)
        {
            //masks should all have the same path
            SortedList<string, List<FileItem>> temp = null;
            try
            {
                SourceFileSystem sfs = new SourceFileSystem(new DirectoryInfo(masks[0].Path), log); //handles multipart archives);
                temp = merge(sfs.GetFiles(masks, cancel));
                readIndexAndAdditionalFiles(sfs, temp, log, cancel);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "Error: SourceFiles.Scan failed scanning");
            }

            if (temp != null)
            {
                foreach (List<FileItem> items in temp.Values)
                    yield return items;
            }
        }

        private static IEnumerable<List<FileItem>> readArchive(FileItem arcItem, ILogScope log, CancellationToken? cancel)
        {
            if (arcItem.IsArchive)
            {
                SourceFileSystem sfs = new SourceFileSystem(arcItem, log); //handles multipart archives);
                List<FileItem> files = sfs.GetFiles(new FileMask[] { arcItem.Mask }, cancel);
                SortedList<string, List<FileItem>> merged = merge(files);
                int indexes = readIndexAndAdditionalFiles(sfs, merged, log, cancel);
                log?.Detail(() => $"Archive Read: [Folders: {merged.Count}] containing [Files: {files.Count(a => !a.IsArchive)}] [Ignored Archives: {files.Count(a => a.IsArchive)}] [Indexes: {indexes}] in [{arcItem.PathFileName}]");
                foreach (List<FileItem> fi in merged.Values)
                    yield return fi;
            }
        }

        private static SortedList<string, List<FileItem>> merge(IEnumerable<FileItem> files)
        {
            SortedList<string, List<FileItem>> temp = new SortedList<string, List<FileItem>>();
            List<FileItem> l;

            foreach (FileItem file in files)
            {
                if (!temp.TryGetValue(file.Parent, out l))
                    temp.Add(file.Parent, l = new List<FileItem>() { file });
                else if (!l.Any(a => a.ToString() == file.ToString()))
                    l.Add(file); //add full path with original casing as value
            }
            return temp;
        }

        private static int readIndexAndAdditionalFiles(SourceFileSystem sfs, SortedList<string, List<FileItem>> temp, ILogScope log, CancellationToken? cancel)
        {

            List<FileItem> pass1Files = new List<FileItem>(sfs.Files.Where(f => f.Type == FileItemType.Index && f.IsMatch).OrderBy(a => a.SourceOrder)); //get all matching index files - ordered by added order (arc location if applicable)
            List<FileItem> pass2Files = new List<FileItem>();
            int parsed = 0;

            if (pass1Files.Count == 0)
                return parsed;

            //read all files in a forward manner. Additional files may be added (in cue file and not part of our mask)
            using (SourceFileSystemReader reader = sfs.CreateReader(cancel))
            {
                FileItem file = pass1Files[0];

                //pass1
                do
                {
                    if (file.Type == FileItemType.Index)
                    {
                        log?.Trace(() => $"Parsing Index [{file}]");
                        file.IndexFile = IndexFile.Parse(file.Path, file.FileName, file.Extension, file.Postfix, reader.OpenRead(file).ReadBytes(file.Size), false, false, sfs.Files.Where(a => a.Parent == file.Parent).ToArray());
                        parsed++;
                        List<FileItem> parent = temp[file.Parent];
                        consolidateIndexFiles(file, parent);
                        if (file.IndexFile.Additional != null)
                        {
                            foreach (FileItem af in file.IndexFile.Additional)
                                mergeIndexAdditional(af, parent, af.SourceOrder >= file.SourceOrder ? pass1Files : pass2Files);
                        }
                    }
                    else
                        file.Data = reader.OpenRead(file).ReadBytes(file.Size);

                    pass1Files.RemoveAt(0);
                    file = pass1Files.FirstOrDefault();
                }
                while (file != null);

                //pass2 - added to support rar SOLID mode where (everything uses the same forward only method - internally they seek to load file where possible)
                foreach (FileItem p2 in pass2Files)
                    p2.Data = reader.OpenRead(p2).ReadBytes(p2.Size);
            }
            return parsed;
        }

        private static void mergeIndexAdditional(FileItem additional, List<FileItem> allItems, List<FileItem> mergeTo)
        {
            if (!allItems.Contains(additional))
                allItems.Add(additional); //didn't match the mask - so add it

            if (mergeTo.Count == 0 || additional.SourceOrder > mergeTo.Last().SourceOrder)
            {
                mergeTo.Add(additional);
                return;
            }

            for (int i = mergeTo.Count - 1; i >= 0; i--)
            {
                if (i == 0 || mergeTo[i].SourceOrder < additional.SourceOrder)
                {
                    mergeTo.Insert(i + 1, additional);
                    return;
                }
            }
        }


        internal static SourceFile CreateSourceFile(FileItem f)
        {
            SourceFile sf = new SourceFile();
            if (f.IsArchived)
                sf.ArchiveFiles = f.Archive.AllPartItems().Select(a => new SourceFileItem(a.Path, a.FileName, a.Extension, a.Postfix, 0, a.Size, a.Crc, a.IsArchived, false)).ToArray();

            if (f.Type == FileItemType.Index)
                sf.ImageFiles = f.Parts.Select(a => new SourceFileItem(a.Path, a.FileName, a.Extension, a.Postfix, 0, a.Size, a.Crc, a.IsArchived, false)).ToArray();
            else
                sf.ImageFiles = f.AllPartItems().Select(a => new SourceFileItem(a.Path, a.FileName, a.Extension, a.Postfix, 0, a.Size, a.Crc, a.IsArchived, false)).ToArray();
            sf.IndexFile = f.IndexFile;
            sf.IsArchive = f.IsArchive;
            sf.SystemType = f.SystemType;
            sf.Initialised();

            // If this SourceFile came from a .nkds DataStore archive entry that
            // had an appended ID (e.g. "Name (123)"), preserve the numeric id
            // for direct datastore lookup while keeping a canonical Name for
            // general use. Also preserve an inner filename (index/part) for
            // probing when needed.
            try
            {
                if (f.IsArchived && f.Archive != null &&
                    string.Equals(f.Archive.Extension, NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // A DataStore listing appends the image id in an unambiguous "{id}" marker to
                    // disambiguate a genuine duplicate name (see DataStoreAsIso.FormatDuplicateName).
                    // Because "{id}" cannot collide with a real dump name (unlike a bare " (123)",
                    // which matches legitimate year/rev suffixes such as "... (2003)"), we can parse
                    // it back directly with no datastore validation — a real name ending in "{n}" is
                    // never produced by any dump convention.
                    if (Container.DataStoreAsIso.TryParseDuplicateName(sf.Name, out string baseName, out long dsid))
                    {
                        sf.DataStoreImageId = dsid;

                        // Prefer an internal filename (index or first image part) to
                        // be used as a probe when selecting the correct image record.
                        string inner = f.IndexFile?.FileName ?? sf.ImageFiles?.FirstOrDefault()?.FileName ?? sf.Name;
                        sf.OriginalFileName = inner;

                        // Keep canonical name for consumers
                        sf.Name = baseName;
                        sf.CleanName = Regex.Replace(sf.Name, "_[0-9]+$", "");
                    }
                }
            }
            catch { }

            return sf;
        }

        private static void process(List<FileItem> items, ILogScope log)
        {
            Comparer<FileItem> cmp = Comparer<FileItem>.Create((a, b) => string.Compare(a.SortKey, b.SortKey));
            //populate the files and sort them by folder
            items.Sort(cmp);
            cleanUpIndexFiles(items);

            // Remove redundant GDI index files when a CUE index in the same container
            // references the same track files. Redump GDI files are not valid — CUE is
            // the authoritative index for these archives.
            removeRedundantGdiFiles(items);

            //add extra files to first file
            items.Sort(cmp);
            consolidateMultipartFiles(items);

            items.RemoveAll(a => !a.IsMatch || !a.IsKnownFileType);

            // Remove standalone cetk/tik files that weren't consumed as Additional
            // by a tmd index file. These are companion files (tickets/certs) and are
            // never valid standalone images. A bare "cetk" or "cetk.X" / "tik.X"
            // that wasn't paired to a tmd.X during cleanUpIndexFiles would otherwise
            // appear as a failed source file in the processing queue.
            items.RemoveAll(a => a.Type == FileItemType.File && isOrphanedCompanionFile(a));
        }

        /// <summary>
        /// Returns true if the file is a cetk/tik companion file that should
        /// never be processed as a standalone image. These files are only meaningful
        /// when paired with a tmd index file.
        /// Matches: cetk, cetk.0, cetk.123, tik, tik.0, tik.123
        /// Does NOT match: tiktoken.iso, cetkeyfile.bin, etc.
        /// </summary>
        private static bool isOrphanedCompanionFile(FileItem f)
        {
            string name = f.FileName;
            if (name == null)
                return false;

            // Match exact "cetk" or "cetk.{digits}" and "tik" or "tik.{digits}"
            if (name.Equals("cetk", StringComparison.OrdinalIgnoreCase)
                || name.Equals("tik", StringComparison.OrdinalIgnoreCase))
                return true;

            if ((name.StartsWith("cetk.", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("tik.", StringComparison.OrdinalIgnoreCase))
                && name.Length > name.IndexOf('.') + 1)
            {
                // Verify the suffix after the dot is all digits (CDN format: cetk.0, tik.1, etc.)
                string suffix = name.Substring(name.IndexOf('.') + 1);
                for (int i = 0; i < suffix.Length; i++)
                {
                    if (suffix[i] < '0' || suffix[i] > '9')
                        return false;
                }
                return true;
            }

            return false;
        }

        private static IEnumerable<FileMask> createMasks(string[] masks, bool scanSubfolders)
        {
            foreach (string mask in masks)
            {
                string m = mask;
                if (m.EndsWith("\"") && !m.StartsWith("\"")) //weird scenario if param ends with \ e.g. "c:\test\"  the last " is preserved
                    m = m.Substring(0, m.Length - 1).Replace("\"\"", "\"");
                yield return FileMask.CreateLocalMask(m, scanSubfolders);
            }
        }

        public static string CleanseFileName(string name)
        {
            if (name == null)
                return null;

            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return Regex.Replace(name, invalidRegStr, " ").Trim();
        }

        public static string GetKnownFileExtension(string filename)
        {
            Match m = _FileMask.Match(filename);
            if (m.Success)
                return m.Groups[2].Value;
            else
                return "";
        }
        internal static Match ParseFileName(string fileName) => _FileMask.Match(Path.GetFileName(fileName));

        internal static string CreateSortKey(string name, string extension, string postfix)
        {
            bool forceNotSplit = _IdxExts.Concat(_ImgExts).Contains(extension); //added to prevent .h3 picked up as a split file
            string pf = _ArcExts.Contains(extension) ? postfix.Replace(extension, "") : postfix;
            string postNums = forceNotSplit ? "" : Regex.Replace(pf, "[^0-9]*", "");
            string postNoNums = forceNotSplit ? postfix : Regex.Replace(pf, "[0-9]*", "");
            string k;

            if (postNums.Length != 0) //split parts
            {
                if (!_SortExts.TryGetValue(postNoNums.Replace(" ", "").ToLower(), out k)) //split files with numerics remove from postfix
                    throw new HandledException($"Split file with postfix {postfix} not found in dictionary");
            }
            else if (!_SortExts.TryGetValue(extension, out k))
                k = extension.PadRight(3).Substring(0, 3);

            string val = $"{name}|{k}|{postNums.PadLeft(4)}";
            return val;
        }

        public static string RemoveExtension(string filename, out string extension)
        {
            string ext = GetKnownFileExtension(filename);
            if (ext.Length != 0)
                extension = ext;
            else
                extension = Path.GetExtension(filename);

            return filename.Substring(0, filename.Length - extension.Length);
        }

        public static string RemoveExtension(string filename) => RemoveExtension(filename, out string ext);

        public static string ChangeExtension(string filename, string newExtension) => string.Format("{0}.{1}", RemoveExtension(filename, out string ext), newExtension.TrimStart('.'));

        public static string GetUniqueFoldername(string path, string name, string ext, bool isTemp)
        {
            int i = 0;
            string tmp = isTemp ? ResultOutFiles.TempChar : "";
            name = name == null ? "" : Regex.Replace(name, "(.*?)(_[0-9]+)*", "$1"); //name is null for dedupe
            if (!string.IsNullOrWhiteSpace(ext))
                ext = "." + ext;
            while (true)
            {
                string outName = string.Concat(Path.Combine(path, name), i == 0 ? "" : ("_" + i.ToString()), (ext ?? "").Trim(), tmp);
                if (!Directory.Exists(outName))
                    return outName;
                i++;
            }
        }

        public static string GetUniqueFilename(string path, string name, string ext, bool isTemp)
        {
            int i = 0;
            string tmp = isTemp ? ResultOutFiles.TempChar : "";
            name = Regex.Replace(name, "(.*?)(_[0-9]{1,7})*$", "$1"); //1-7 so it doesn't match the crc on the end of wii fix files (recovery partitions)
            if (!string.IsNullOrWhiteSpace(ext))
                ext = "." + ext;
            while (true)
            {
                string outName = string.Concat(Path.Combine(path, name), i == 0 ? "" : ("_" + i.ToString()), (ext ?? "").Trim(), tmp);
                if (!File.Exists(outName))
                    return outName;
                i++;
            }
        }

        internal static IArchive ArchiveOpen(FileItem item)
        {
            string type = item.Extension.ToLower();
            IReadOnlyList<FileInfo> parts = item.AllParts(true).Select(a => new FileInfo(a)).ToList();
            switch (type)
            {
                case ".zipx":
                case ".zip": return ZipArchive.OpenArchive(parts);
                case ".rar": return RarArchive.OpenArchive(parts);
                case ".7z": return SevenZipArchive.OpenArchive(parts);
                case ".gz": return GZipArchive.OpenArchive(parts);
                default: return ArchiveFactory.OpenArchive(parts);
            }
        }

        /// <summary>
        /// Remove the _1 ... from a filename
        /// </summary>
        public static void GetFileNameParts(string fullFilename, out string path, out string filename, out string unique, out string ext)
        {
            if (fullFilename == null)
                fullFilename = "";

            path = Path.GetDirectoryName(fullFilename);
            filename = RemoveExtension(fullFilename, out ext);
            filename = filename.Substring(path?.Length ?? 0);
            string sep = Regex.Escape($"{Path.DirectorySeparatorChar}{Path.AltDirectorySeparatorChar}");
            Match m = Regex.Match(filename, @$"^[{sep}]*(.*?)(_[0-9]{{1,7}})?{ResultOutFiles.TempChar}?$");
            unique = "";
            if (m.Success)
            {
                filename = m.Groups[1].Value;
                unique = m.Groups[2].Value ?? "";
            }
        }

        public static string GetPathRemoveMasks(string path)
        {
            Match m = Regex.Match(path, @$"^((?:[^\*\?]*)(?:$|[\{Path.DirectorySeparatorChar}\{Path.AltDirectorySeparatorChar}$]))*(.*?[\*\?].*?)?$");
            if (m.Success)
                return m.Groups[1].Value.Length == 0 ? m.Groups[0].Value : m.Groups[1].Value;
            return path;
        }

        public static bool TrySplitArchivePath(string path, out string archivePath, out string innerPath)
        {
            archivePath = null;
            innerPath = null;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            FileMask mask = FileMask.CreateLocalMask(path, false);
            if (string.IsNullOrWhiteSpace(mask.MaskInArc))
                return false;

            archivePath = Path.Combine(mask.Path, mask.Mask);
            innerPath = mask.MaskInArc;
            return true;
        }

        private static void consolidateMultipartFiles(List<FileItem> scanTemp)
        {
            FileItem outer;
            int o = -1;

            while (++o < scanTemp.Count)
            {
                outer = scanTemp[o];
                if (outer.Type == FileItemType.Index)
                    continue;

                int i = o;
                while (++i < scanTemp.Count)
                {
                    FileItem inner = scanTemp[i];
                    if (outer.Name != inner.Name)
                        break;
                    if (outer.IsPart(inner))
                        outer.Parts.Add(inner);
                }

                foreach (FileItem f in outer.Parts)
                    scanTemp.Remove(f); //remove the files the cue/gdi references

                if (outer.Parts.Count > 0)
                    outer.IsSplit = true;
            }
        }

        private static void consolidateIndexFiles(FileItem indexFile, List<FileItem> files)
        {
            foreach (string filename in indexFile.IndexFile.Items.Select(a => a.FileName).Distinct())
            {
                FileItem found = files.FirstOrDefault(a => string.Compare(a.FileName, filename, StringComparison.OrdinalIgnoreCase) == 0);
                if (found != null)
                    indexFile.Parts.Add(found);
            }
        }
        /// <summary>
        /// Removes GDI index files that are redundant with a CUE index file in the
        /// same container. Two index files are considered redundant when any filenames
        /// in their Parts lists intersect (case-insensitive). Redump GDI files are not
        /// valid — CUE is the authoritative index for these archives.
        /// </summary>
        private static void removeRedundantGdiFiles(List<FileItem> files)
        {
            List<FileItem> indexFiles = files.Where(a => a.Type == FileItemType.Index && a.IndexFile != null).ToList();
            if (indexFiles.Count < 2)
                return;

            // Group index files by their container (Parent)
            IEnumerable<IGrouping<string, FileItem>> groups = indexFiles.GroupBy(a => a.Parent ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            List<FileItem> gdiToRemove = new List<FileItem>();

            foreach (IGrouping<string, FileItem> group in groups)
            {
                List<FileItem> gdiFiles = group.Where(a => a.IndexFile.FileType == IndexFileType.Gdi).ToList();
                List<FileItem> cueFiles = group.Where(a => a.IndexFile.FileType == IndexFileType.Cue).ToList();

                if (gdiFiles.Count == 0 || cueFiles.Count == 0)
                    continue;

                // For each GDI, check if any CUE in the same container shares track files
                foreach (FileItem gdi in gdiFiles)
                {
                    HashSet<string> gdiPartNames = new HashSet<string>(
                        gdi.Parts.Select(p => p.FileName),
                        StringComparer.OrdinalIgnoreCase);

                    if (gdiPartNames.Count == 0)
                        continue;

                    foreach (FileItem cue in cueFiles)
                    {
                        bool sharesTrackFiles = cue.Parts.Any(p => gdiPartNames.Contains(p.FileName));
                        if (sharesTrackFiles)
                        {
                            gdiToRemove.Add(gdi);
                            break; // This GDI is redundant, no need to check more CUE files
                        }
                    }
                }
            }

            foreach (FileItem gdi in gdiToRemove)
                files.Remove(gdi);
        }

        private static void cleanUpIndexFiles(List<FileItem> files)
        {
            List<FileItem> idxs = files.Where(a => a.Type == FileItemType.Index).ToList();
            foreach (FileItem idx in idxs)
            {
                if (idx.Parts != null)
                {
                    foreach (FileItem f in idx.Parts) //remove all the items referenced by the index files
                        files.Remove(f); //remove the files the cue/gdis references
                }
                if (idx.IndexFile?.Additional != null)
                {
                    foreach (FileItem f in idx.IndexFile.Additional) //remove all the items referenced by the index files
                        files.Remove(f); //remove the files the cue/gdis references
                }
            }
        }

    }
}
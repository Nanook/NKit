using NKitDataStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    public enum SourceFileResult { Valid, NoData, IndexOnly, BinRawNoIndex, MissingFile, ArchiveReadError, ArchiveEmpty, ArchiveInArchive, NeedsSeekInArchive }
    public enum SourceArchiveType { None, Zip, Rar, SevenZip, Gzip, DataStore }
    public enum SourceImageType { Cue, Gdi, NKitIso, NKitGcz, IsoDec, Cdi, DecIso, Iso, XIso, IsoMode1, IsoMode2, Chd, CIso, Wbfs, Gcz, Gcm, Wia, Rvz, Wud, Wux, TmdApp, Sfb, Sfo }

    /// <summary>
    /// Supports Multiple file archives and split (.001 .002 / wbfs wbf1 etc) files. Split files in archives is not supported. Archives within archives is not supported
    /// </summary>
    public class SourceFile
    {
        static SourceFile()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// Name without unique _1 on the end - used for looking up keys etc
        /// </summary>
        public string CleanName { get; internal set; }

        /// <summary>
        /// File name (may be within an archive)
        /// </summary>
        public string Name { get; internal set; }
        /// <summary>
        /// Path of file (archive or file)
        /// </summary>
        public string BasePath => this.IsArchived ? ArchiveFiles[0].Path : (IndexFile?.Path ?? ImageFiles[0].Path);
        /// <summary>
        /// File name of archive
        /// </summary>
        //public string FilePath { get { return this.IsArchive ? ArchiveFileNames[0].Path : (IndexFile?.Path ?? ImageFileNames[0].Path); } }

        public SystemType SystemType { get; internal set; }

        /// <summary>
        /// All path and filenames for multipart and split sets
        /// </summary>
        public SourceFileItem[] ImageFiles { get; internal set; }

        public SourceFileItem[] ArchiveFiles { get; internal set; }

        public IndexFile IndexFile { get; internal set; }

        /// <summary>
        /// True if the file is split (not multipart). This library will preset the files as one stream
        /// </summary>
        public bool IsSplitImage => (ImageFiles?.Length ?? 0) > 1;
        public bool IsSplitArchive => (ArchiveFiles?.Length ?? 0) > 1;

        public bool IsDeleted { get; private set; }

        public bool IsArchive { get; internal set; }

        public bool IsDataStore => this.ArchiveType == SourceArchiveType.DataStore;

        public bool IsFolderMode { get; private set; }

        /// <summary>
        /// Is the Name and Path inside an archive (making FilePath the physical file)
        /// </summary>
        public bool IsArchived => (ArchiveFiles?.Length ?? 0) != 0;

        public long Length { get; internal set; }

        public SourceFileResult Status { get; internal set; }
        public SourceImageType ImageType { get; private set; }
        public SourceArchiveType ArchiveType { get; private set; }
        public byte[] Key { get; internal set; }

        // When the source is a DataStore proxy created after dedupe, this field
        // can hold the original input image filename (e.g., tmd.0/tmd.1) so callers
        // can disambiguate datastore images by contained files.
        public string OriginalFileName { get; set; }
        // When the source is a DataStore proxy created after dedupe, this field
        // can hold the original input image id from the datastore listing (e.g. the
        // numeric id appended in "Name (123)") so callers can disambiguate
        // datastore images when necessary.
        public long? DataStoreImageId { get; set; }

        public Dictionary<string, string> DatItems { get; internal set; }
        public string Scan { get; internal set; }

        public int Index { get; internal set; }

        /// <summary>
        /// True if this SourceFile is a synthetic folder source created by
        /// SyntheticSourceFactory. When true, the pipeline intercepts this
        /// source and routes it to FolderImageProcessor instead of the
        /// normal NKitTask/step pipeline.
        /// </summary>
        public bool IsSyntheticFolder { get; internal set; }

        /// <summary>
        /// The folder group info for synthetic folder sources.
        /// Null for real SourceFiles.
        /// </summary>
        internal FolderGroupInfo SyntheticFolderGroup { get; set; }


        internal static SourceFile CreateFromTemp(string path, IParts files)
        {
            IPart index = files.FirstOrDefault(a => ((Part)a).IsIndex);
            IEnumerable<IPart> parts = files.Where(a => !((Part)a).IsIndex);
            return new SourceFile(index == null ? null : new FileInfo(Path.Combine(path, index.FileName)), parts.Select(a => new FileInfo(Path.Combine(path, a.FileName))).ToArray(), true);
        }

        internal SourceFile(FileInfo fi)
        {
            this.ImageFiles = new[] { new SourceFileItem(fi.DirectoryName, fi.Name, fi.Extension, fi.Extension, 0, fi.Length, 0, false, false) };
        }

        private SourceFile(FileInfo index, FileInfo[] files, bool isTemp) //from temp file
        {
            if (index != null)
                this.IndexFile = IndexFile.Parse(index.DirectoryName, index.Name, index.Extension, index.Extension, File.ReadAllBytes(index.FullName), isTemp, false, null);
            this.ImageFiles = files.Select(fi => new SourceFileItem(fi.DirectoryName, fi.Name, fi.Extension, fi.Extension, 0, fi.Length, 0, false, isTemp)).ToArray();
        }

        internal SourceFile()
        {
            this.Status = SourceFileResult.Valid;
            this.DatItems = new Dictionary<string, string>();
        }

        internal void Initialised()
        {
            //set the result, imageType and archiveType
            if (this.IndexFile != null)
            {
                if (this.IndexFile.FileType == IndexFileType.Gdi)
                    this.ImageType = SourceImageType.Gdi;
                else
                {
                    IndexTrackBasicType[] modes = this.IndexFile.Items.Select(a => a.BasicType).Distinct().ToArray();
                    if (modes.Contains(IndexTrackBasicType.Cdi))
                        this.ImageType = SourceImageType.Cdi;
                    else if (modes.Length == 1 && modes[0] == IndexTrackBasicType.Mode1)
                        this.ImageType = SourceImageType.IsoMode1;
                    else if (modes.Length == 1 && modes[0] == IndexTrackBasicType.Mode2)
                        this.ImageType = SourceImageType.IsoMode2;
                    else if (this.IndexFile.FileType == IndexFileType.TmdApp)
                        this.ImageType = SourceImageType.TmdApp;
                    else
                        this.ImageType = SourceImageType.Cue;
                }
            }
            else if (this.ImageFiles != null && this.ImageFiles.Length != 0)
            {
                string ext = this.ImageFiles[0].Extension.ToLower().TrimEnd(ResultOutFiles.TempChar[0]);
                this.ImageType = ext == ".wbfs" ? SourceImageType.Wbfs :
                                 (ext == ".ciso" ? SourceImageType.CIso :
                                 (ext == ".xiso" ? SourceImageType.XIso :
                                 (ext == ".gcm" ? SourceImageType.Gcm :
                                 (ext == ".gcz" ? SourceImageType.Gcz :
                                 (ext == ".iso.dec" ? SourceImageType.IsoDec :
                                 (ext == ".nkit.iso" ? SourceImageType.NKitIso :
                                 (ext == ".nkit.gcz" ? SourceImageType.NKitGcz :
                                 (ext == ".rvz" ? SourceImageType.Rvz :
                                 (ext == ".wia" ? SourceImageType.Wia :
                                 (ext == ".wud" ? SourceImageType.Wud :
                                 (ext == ".wux" ? SourceImageType.Wux :
                                 (ext == ".dec.iso" ? SourceImageType.DecIso :
                                 (ext == ".chd" ? SourceImageType.Chd :
                                 (ext == ".sfb" ? SourceImageType.Sfb :
                                                  SourceImageType.Iso))))))))))))));
            }

            if (this.ArchiveFiles != null && this.ArchiveFiles.Length != 0)
            {
                string ext = this.ArchiveFiles[0].Extension.ToLower();
                this.ArchiveType = ext == DataStore.DatabaseFileExtension ? SourceArchiveType.DataStore :
                                 (ext == ".gz" ? SourceArchiveType.Gzip :
                                 (ext == ".7z" ? SourceArchiveType.SevenZip :
                                 (ext == ".rar" ? SourceArchiveType.Rar :
                                                   SourceArchiveType.Zip))); //inc zipx

            }
            if (this.ImageType == SourceImageType.Sfb)
            {
                if ((this.ArchiveFiles?.Length ?? 0) != 0)
                    this.Name = this.ArchiveFiles[0].NameOnly;
                else
                    this.Name = Path.GetFileName(this.BasePath.TrimEnd(Path.DirectorySeparatorChar));
            }
            else if (this.IndexFile != null)
            {
                if (this.ImageType == SourceImageType.TmdApp)
                {
                    if ((this.ArchiveFiles?.Length ?? 0) != 0)
                        this.Name = this.ArchiveFiles[0].NameOnly;
                    else
                        this.Name = Path.GetFileName(this.BasePath.TrimEnd(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }));
                }
                else
                    this.Name = this.IndexFile.NameOnly;
            }

            if (this.Name == null)
                this.Name = ImageFiles[0].NameOnly;

            this.CleanName = Regex.Replace(this.Name, @$"_[0-9]+$", "");

            this.IsFolderMode = this.IndexFile != null;


            if (this.Status == SourceFileResult.Valid)
            {
                this.Status = SourceFileResult.Valid; //default
                if (this.IndexFile == null && this.ImageFiles.All(a => a.Extension.ToLower() == ".bin" || a.Extension.ToLower() == ".raw"))
                    this.Status = SourceFileResult.BinRawNoIndex;
                else if (this.IndexFile != null && this.ImageFiles.Length == 0)
                    this.Status = SourceFileResult.IndexOnly;
                else if (this.IsArchived && (this.ImageFiles == null || this.ImageFiles.Length == 0))
                    this.Status = SourceFileResult.ArchiveEmpty;
                else if (this.IsArchived && this.IsArchive)
                {
                    //test for file within the archive being 000... without another extension. Treat as iso
                    if (this.ImageFiles.Length != 0 && this.ImageFiles[0].Extension == "" && this.ImageFiles[0].Postfix == ".000") // && this.IsSplitImage)
                        this.IsArchive = false; //switch to iso
                    else
                        this.Status = SourceFileResult.ArchiveInArchive;
                }
                //else if (this.ImageType == SourceImageType.TmdApp && this.IndexFile != null && tmdAppHasMissingFiles(this.IndexFile.Items, this.ImageFiles))
                //    this.Status = SourceFileResult.MissingFile;
                else if (this.IndexFile != null && this.IndexFile.Items.Length < this.ImageFiles.Length)
                    this.Status = SourceFileResult.MissingFile;
                else if (this.IsArchived && this.ImageFiles.Any(a => a.Extension.ToLower() == ".wia")) //might rework this to check headers etc - might apply to rvz also
                    this.Status = SourceFileResult.NeedsSeekInArchive;
                else if (this.ImageFiles.All(a => SourceFiles._NonDataKnownExts.Contains(a.Extension.ToLower())))
                    this.Status = SourceFileResult.NoData;
            }

            setTrackInfo();
        }

        /// <summary>
        /// Returns true if any content declared in the TMD index is absent from the
        /// found image files. Comparison is normalised by stripping the optional
        /// ".app" suffix so that IndexFile.Parse's filename-detection heuristic
        /// (which omits ".app" for files that are absent on disk) does not produce
        /// false positives for complete folders.
        /// </summary>
        private static bool tmdAppHasMissingFiles(SourceFileTrack[] indexItems, SourceFileItem[] imageFiles)
        {
            static string baseName(string fn) =>
                fn.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? fn[..^4] : fn;

            HashSet<string> foundBaseNames = new HashSet<string>(
                imageFiles.Select(f => baseName(f.FileName)),
                StringComparer.OrdinalIgnoreCase);

            return indexItems
                .Select(t => baseName(t.FileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(bn => !foundBaseNames.Contains(bn));
        }

        /// <summary>
        /// Returns true when this is a TmdApp image that has at least one content
        /// file declared in the TMD but absent from the found image files.
        /// Uses normalised (case-insensitive, .app-agnostic) comparison so it is
        /// not fooled by the filename heuristic in IndexFile.Parse.
        /// </summary>
        public bool HasMissingTmdContent =>
            this.ImageType == SourceImageType.TmdApp &&
            this.IndexFile != null &&
            tmdAppHasMissingFiles(this.IndexFile.Items, this.ImageFiles);

        /// <summary>
        /// For TmdApp sources with some missing content files, removes the missing
        /// index entries and recalculates image offsets so processing continues with
        /// only the files that are actually present on disk.
        /// Returns the removed (missing) tracks so callers can log them.
        /// </summary>
        internal SourceFileTrack[] PruneMissingTmdContent()
        {
            if (this.IndexFile == null || this.ImageType != SourceImageType.TmdApp)
                return Array.Empty<SourceFileTrack>();

            SourceFileTrack[] missing = this.IndexFile.Items.Where(t => t.FileIsMissing).ToArray();
            this.IndexFile.Items = this.IndexFile.Items.Where(t => !t.FileIsMissing).ToArray();

            // Recalculate sequential image offsets for the surviving entries
            long offset = 0;
            foreach (SourceFileTrack t in this.IndexFile.Items)
            {
                t.ImageOffset = offset;
                offset += t.Size;
            }

            this.Length = this.ImageFiles.Sum(f => f.Size);

            if (this.Status == SourceFileResult.MissingFile)
                this.Status = SourceFileResult.Valid;

            return missing;
        }

        private void setTrackInfo()
        {
            if (this.IndexFile == null)
                return;

            SourceFileTrack last = null;

            for (int i = 0; i < this.IndexFile.Items.Length; i++)
            {
                SourceFileTrack curr = this.IndexFile.Items[i];
                // Use normalised lookup: strip optional .app from both sides so that
                // differences introduced by IndexFile.Parse's filename heuristic
                // (omits .app for absent files) don't cause false "missing" flags.
                string currBase = curr.FileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? curr.FileName[..^4] : curr.FileName;
                SourceFileItem fi = this.ImageFiles.FirstOrDefault(a =>
                {
                    string aBase = a.FileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                        ? a.FileName[..^4] : a.FileName;
                    return string.Equals(aBase, currBase, StringComparison.OrdinalIgnoreCase);
                });

                if (fi != null)
                {
                    long off;
                    if (last != null && curr.OffsetIndexes?.Count != 0 && (off = curr.OffsetIndexes[0]) != 0)
                    {
                        curr.Size = last.Size - off; //split the size
                        last.Size -= curr.Size; //recalc
                        last.Blocks = (int)(last.Size / last.BlockSize);
                    }
                    else
                        curr.Size = fi.Size;
                    curr.Blocks = curr.BlockSize == 0 ? 0 : (int)(curr.Size / curr.BlockSize);
                }
                else //no file found
                    curr.FileIsMissing = true;

                if (last != null)
                    curr.ImageOffset = last.ImageOffset + last.Size;

                last = curr;
            }

        }
        public Stream OpenFilePart(SourceFileItem part)
        {
            if (this.IsArchived)
            {
                return new BufferStream(SourceStream.OpenArchive(this.ArchiveFiles.Select(a => new FileInfo(Path.Combine(a.Path, a.FileName))).ToArray(),
                                                new[] { Path.Combine(part.Path, part.FileName) },
                                                new[] { part.Size }));
            }
            else
                return new BufferStream(SourceStream.Open(i => File.OpenRead(Path.Combine(part.Path, part.FileName)),
                                         new[] { part.Size }, true));
        }

        public Stream OpenFileStream(ILogScope log = null)
        {
            if (this.IsArchived)
            {
                return new BufferStream(SourceStream.OpenArchive(this.ArchiveFiles.Select(a => new FileInfo(Path.Combine(a.Path, a.FileName))).ToArray(),
                                                this.ImageFiles.Select(a => Path.Combine(a.Path, a.FileName)).ToArray(),
                                                this.ImageFiles.Select(a => a.Size).ToArray(), log), log);
            }
            else
                return new BufferStream(SourceStream.Open(i => File.OpenRead(Path.Combine(this.ImageFiles[i].Path, this.ImageFiles[i].FileName)),
                                         this.ImageFiles.Select(a => a.Size).ToArray(), true, log), log);
        }

        public bool Delete()
        {
            if (!this.IsArchived && !this.IsDeleted)
            {
                if (IndexFile != null && File.Exists(IndexFile.FileName))
                    File.Delete(IndexFile.FileName);
                foreach (SourceFileItem f in this.ImageFiles)
                {
                    if (File.Exists(Path.Combine(f.Path, f.FileName)))
                        File.Delete(Path.Combine(f.Path, f.FileName));
                }
                this.IsDeleted = true;
                return true;
            }
            else
                return false;
        }


        public string FriendlyFullPath
        {
            get
            {
                StringBuilder sb = new StringBuilder(100);

                if (this.IsArchived)
                {
                    sb.Append(Path.Combine(this.ArchiveFiles[0].Path, this.ArchiveFiles[0].FileName));
                    sb.Append("//");
                    if (this.IndexFile != null)
                    {
                        if (this.IndexFile.Path.Length != 0)
                            sb.Append(this.IndexFile.Path + "/");
                        sb.Append(this.IndexFile.FileName);
                    }
                    else
                    {
                        if (ImageFiles[0].Path.Length != 0)
                            sb.Append(ImageFiles[0].Path + "/");
                        sb.Append(ImageFiles[0].FileName);
                    }
                }
                else if (this.IndexFile != null)
                    sb.Append(Path.Combine(this.IndexFile.Path, this.IndexFile.FileName));
                else
                    sb.Append(Path.Combine(this.BasePath, this.ImageFiles[0].FileName));

                if (this.ImageFiles.Length != 1)
                    sb.Append($" +{this.ImageFiles.Length - (this.IndexFile != null ? 0 : 1)}");

                return sb.ToString();
            }
        }

        public override string ToString()
        {
            return string.Format("Name:{0}, BasePath:{1}, Parts:{2}, Archives:{3}, HasIndex:{4}, ImageType:{5}, ArchiveType:{6}, Status:{7}",
                this.Name, this.BasePath, (this.ImageFiles?.Length ?? 0).ToString(), (this.ArchiveFiles?.Length ?? 0).ToString(), (this.IndexFile != null).ToString(), this.ImageType.ToString(), this.ArchiveType.ToString(), this.Status.ToString());
        }

    }
}
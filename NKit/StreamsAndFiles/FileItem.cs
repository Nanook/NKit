using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    internal enum FileItemType { Unknown = 0, Index, File, Archive, Split, SplitWbfs, MultiArchive }
    internal class FileItem
    {
        internal FileItem(string pathFileName)
        {
            this.PathFileName = pathFileName;
            this.FileName = System.IO.Path.GetFileName(pathFileName);
            this.Path = pathFileName.Substring(0, pathFileName.Length - this.FileName.Length); //preserve / or \ (zips etc use / )
            //this.Path = System.IO.Path.GetDirectoryName(pathFileName);
            this.Parts = new List<FileItem>();
            this.ArchiveFiles = new SortedList<string, List<FileItem>>();
        }

        // True when this FileItem was created from a DataStore file table entry
        // (i.e., reconstructed from the datastore rather than from the local folder/archive)
        internal bool IsFromDataStore { get; set; }

        // If this FileItem was created from a DataStore archive listing, this
        // optionally stores the numeric DataStore image id parsed from the
        // displayed listing (e.g. "Name (123)"). Consumers can use this to
        // directly open the matching image record.
        internal long? DataStoreImageId { get; set; }
        internal int SourceOrder { get; set; } //order read from root folder / archive
        internal FileItem Archive { get; set; }
        internal string Parent { get; set; }
        internal string PathFileName { get; set; }
        internal string Name { get; set; }
        internal string FileName { get; }
        internal string Path { get; }
        internal long Size { get; set; }
        internal long Crc { get; set; }
        internal string Extension { get; set; }
        internal string Postfix { get; set; }
        internal FileItemType Type { get; set; }
        internal bool IsSplit { get; set; }
        internal bool IsArchive => this.Type == FileItemType.Archive || this.Type == FileItemType.MultiArchive;
        internal bool IsArchived => this.Archive != null;
        internal int Group { get; set; }
        internal SystemType SystemType { get; set; }
        internal IndexFile IndexFile { get; set; }
        internal string SortKey { get; set; }
        internal byte[] Data { get; set; }

        public override string ToString()
        {
            if (this.Archive != null)
                return $"{Archive.PathFileName}//{this.PathFileName}";
            return this.PathFileName;
        }

        internal List<FileItem> Parts { get; }

        internal IEnumerable<string> AllParts(bool fullPath)
        {
            yield return fullPath ? this.PathFileName : this.FileName;

            foreach (FileItem p in this.Parts)
                yield return fullPath ? p.PathFileName : p.FileName;
        }

        internal IEnumerable<FileItem> AllPartItems()
        {
            yield return this;

            foreach (FileItem p in this.Parts)
                yield return p;
        }

        internal SortedList<string, List<FileItem>> ArchiveFiles { get; }
        internal bool InvalidArchive { get; set; }
        public bool IsMatch { get; set; }
        internal FileMask Mask { get; set; }
        public bool IsKnownFileType { get; internal set; }

        public bool IsPart(FileItem file)
        {
            //multipart zips >= z100 / zx100
            if (this.FileName.Length != file.FileName.Length && file.Type == FileItemType.MultiArchive && file.Extension.StartsWith(".z") && this.Extension.Length > 2 && file.Extension.StartsWith(this.Extension.Substring(0, 2)))
                return Regex.IsMatch(this.Extension, @"[0-9]{3,}$");

            //nkds shard files (file.nkds + file_0000.nkds etc)
            if (this.FileName.Length != file.FileName.Length && file.Type == FileItemType.MultiArchive &&
                this.IsArchive && !this.IsSplit && this.Extension == ".nkds" && file.Extension == ".nkds")
                return true;

            //check for matches
            return this.FileName.Length == file.FileName.Length && this.Name == file.Name && //must have same filename length AND any from below
               (
                   (this.Extension == file.Extension && this.IsSplit && file.IsSplit) || //file.iso.001, file,iso.002 or file.rar.001, file.rar.002 etc
                   (file.Type == FileItemType.SplitWbfs && this.Extension.StartsWith(file.Extension.Substring(0, file.Extension.Length - 1))) || //file.wbfs, file.wbf1 etc
                   (this.IsArchive && !this.IsSplit && file.Type == FileItemType.MultiArchive && //file.part01.rar, file.part02.rar or file.z01, file.z02 etc
                       !file.IsSplit && this.Extension.Length > 2 &&
                       (file.Extension.StartsWith(this.Extension.Substring(0, 2)) || //check starts with . and same first char OR
                        (this.Extension.StartsWith(".r") && file.Extension[1] >= 's' && file.Extension[1] <= 'y')))
               );
        }

        internal Stream OpenFileStream()
        {
            Stream s;

            //join the file together?
            if (this.Parts.Count != 0 && this.Parts[0].IsSplit)
            {
                string[] fn = this.AllParts(true).ToArray();
                long[] sz = fn.Select(a => new FileInfo(a).Length).ToArray();
                s = SourceStream.Open(i => File.OpenRead(fn[i]), sz, true);
            }
            else
                s = File.OpenRead(this.PathFileName);
            return s;
        }

        internal bool Populate()
        {
            Match m = SourceFiles.ParseFileName(this.FileName);
            this.IsKnownFileType = m.Success;
            this.Name = m.Success ? m.Groups[1].Value : System.IO.Path.GetFileNameWithoutExtension(this.FileName);
            this.Extension = m.Success ? m.Groups[2].Value : System.IO.Path.GetExtension(this.FileName);

            if (m.Success)
            {
                //verbose for debugging
                if (m.Groups[3].Success) //*.cue / gdi / tmd
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[3].Value;
                    this.Type = FileItemType.Index;
                    this.Group = 0;
                }
                else if (m.Groups[4].Success) //*.wbfs / iso
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[4].Value;
                    this.Type = FileItemType.File;
                    this.IsSplit = m.Groups[5].Success;
                    this.Group = 1;
                }
                else if (m.Groups[9].Success) //*.zip / rar / gz / 7z / nkds
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[9].Value;
                    this.Type = FileItemType.Archive;
                    this.IsSplit = m.Groups[10].Success;
                    this.Group = 2;
                }
                else if (m.Groups[11].Success) //*.wbf1
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[11].Value;
                    this.Type = FileItemType.SplitWbfs;
                    this.IsSplit = true;
                    this.Group = 3;
                }
                else if (m.Groups[7].Success) //*.r00 / z01
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[7].Value;
                    this.Type = FileItemType.MultiArchive;
                    this.Group = 4;
                }
                else if (m.Groups[6].Success) //*.part01.rar
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[6].Value;
                    this.Type = FileItemType.MultiArchive;
                    this.Group = 5;
                }
                else if (m.Groups[8].Success) //*_0000.nkds (nkds shard)
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[8].Value;
                    this.Type = FileItemType.MultiArchive;
                    this.Group = 10;
                }
                else if (m.Groups[12].Success) //Track 1.bin
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[12].Value;
                    this.Type = FileItemType.Split;
                    this.Group = 6;
                }
                else if (m.Groups[13].Success) //* (Track 1).bin
                {
                    this.Postfix = m.Groups[2].Value;
                    this.Extension = m.Groups[13].Value;
                    this.Type = FileItemType.Split;
                    this.Group = 7;
                }
                else if (m.Groups[14].Success) //*.001
                {
                    this.Postfix = m.Groups[14].Value;
                    this.Extension = ""; //unknown extension
                    this.IsSplit = true;
                    this.Type = FileItemType.MultiArchive;
                    this.Group = 8;
                }
                else if (m.Groups[15].Success) //currently tmd.0 / cetk.0
                {
                    this.Postfix = m.Groups[14].Value;
                    this.Extension = ""; //unknown extension - will assume zip just split
                    if (m.Groups[15].Value.ToLower().StartsWith("tmd"))
                        this.Type = FileItemType.Index;
                    else
                        this.Type = FileItemType.File;
                    if (m.Groups[15].Value.ToLower().EndsWith(".sfb"))
                        this.Extension = System.IO.Path.GetExtension(m.Groups[15].Value);
                    this.Group = 9;
                }
                this.SortKey = SourceFiles.CreateSortKey(this.Name, this.Extension, this.Postfix);
                return true;
            }
            else
            {
                // Some nkds datastore entries do not include recognizable extensions or
                // don't match the filename mask. If this item comes from an nkds archive
                // treat it as a file image so it will be accepted by the scanner.
                if (this.Archive != null && string.Equals(this.Archive.Extension, ".nkds", System.StringComparison.OrdinalIgnoreCase))
                {
                    this.IsKnownFileType = true;
                    this.Type = FileItemType.File;
                    this.Extension = "";
                    this.Postfix = "";
                    this.SortKey = SourceFiles.CreateSortKey(this.Name, this.Extension, this.Postfix);
                    return true;
                }

                return false;
            }
        }


    }
}
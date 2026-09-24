using System;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    internal class FixFileItem
    {
        public FixFileItem(string filename, string type, long length, uint crc)
        {
            this.Filename = filename;
            this.Length = length;
            this.Type = type;
            this.Crc = crc;
        }
        public string Filename { get; }
        public string Type { get; set; }
        public long Length { get; }
        public uint Crc { get; protected set; }

        /// <summary>Inner archive entry name when this fix file is stored inside a streamable
        /// archive (convention: single entry named as the archive without its extension). Null for
        /// a plain on-disk file — then <see cref="Filename"/> is the data path. When set,
        /// <see cref="Filename"/> is the archive path.</summary>
        public string InnerEntryName { get; set; }

        /// <summary>True when the fix-file data lives inside a streamable archive.</summary>
        public bool IsArchived => this.InnerEntryName != null;

        /// <summary>The fix file's logical name for display/logging — the base name with no archive
        /// extension. Archive-backed: the inner entry name; plain file: the file name.</summary>
        public string DisplayName => this.InnerEntryName ?? (this.Filename == null ? null : Path.GetFileName(this.Filename));

        /// <summary>
        /// Read the fix file's full DATA into a byte[]: a plain on-disk file
        /// (<see cref="InnerEntryName"/> null) or the single named entry inside a streamable archive.
        /// Uses the same scan/read primitives as the DatManager. Returns null when unavailable.
        /// Full-load (not streamed): fix files that use this are small (FST / apploader).
        /// </summary>
        public byte[] ReadAllData(ILogScope log)
        {
            if (this.Filename == null || !File.Exists(this.Filename))
                return null;

            if (this.InnerEntryName == null)
                return File.ReadAllBytes(this.Filename);

            FileMask mask = FileMask.CreateLocalMask($"{this.Filename}//{this.InnerEntryName}", false);
            FileItem entry = SourceFileSystem.GetLocalArchiveFiles(mask, false, log, null)
                .FirstOrDefault(a => string.Equals(a.FileName, this.InnerEntryName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;

            using (SourceFileSystemReader rdr = SourceFileSystem.CreateReader(entry, log, null))
            {
                Stream s = rdr.OpenRead(entry);
                if (s == null)
                    return null;
                return s.ReadBytes(entry.Size);
            }
        }
    }

}
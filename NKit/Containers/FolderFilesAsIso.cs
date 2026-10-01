using Nanook.NKit.Container.ZArchive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// Presents a Loadiine folder or WUA ZArchive as a flat concatenated byte stream so
    /// the normal NKit pipeline can process it.  Each file is appended in path order with
    /// no gaps.  File boundaries are available via <see cref="Files"/> so downstream
    /// consumers (the Image layer) can expose per-file <see cref="AreaInfo"/> properties.
    ///
    /// Detection:
    ///   WUA       — created by <see cref="Create(SourceImageType)"/> when the source
    ///               image type is <see cref="SourceImageType.Wua"/>.
    ///   Loadiine  — created when the source is a synthetic <see cref="FolderGroupType.LoadiineFolder"/>.
    /// Both are detected before the normal container chain in <c>NKitInput.createImageContainer</c>
    /// because they are flagged on the SourceFile before open, not by magic bytes.
    /// </summary>
    internal sealed class FolderFilesAsIso : Stream, IAsIso
    {
        private static readonly Regex _titleFolderRegex =
            new Regex(@"^[0-9a-fA-F]{16}_v[0-9]+$", RegexOptions.Compiled);

        // Entry represents one file in the virtual stream
        internal sealed class FileEntry
        {
            public string RelPath;     // e.g. "code/game.rpx"
            public long   StreamOffset; // byte offset in the flat virtual stream
            public long   Size;
        }

        private Stream _stream;           // underlying raw file stream (seeked into by Read)
        private long   _position;         // current virtual stream position

        public override long Position
        {
            get => _position;
            set { _position = value; }
        }
        private long   _size;             // total virtual stream length
        private List<FileEntry> _files;   // all files in path order

        // Per-file stream openers (populated in Construct for WUA; filesystem seeks for Loadiine)
        private Func<int, Stream> _openFile;   // index → seekable stream of that file's bytes
        private int    _currentFileIdx = -1;
        private Stream _currentFileStream;

        public List<FileEntry> Files => _files;

        // ── IAsIso ────────────────────────────────────────────────────────────────────

        public ContainerType Format => ContainerType.Wua; // treated as folder-format source
        public bool Seekable        => true;
        public bool SeekRequired    => false;
        public bool SizeEstimated   => false;
        public long RealPosition    => _position;
        public long RealSize        => _size;
        public long Size            => _size;
        public NKitHeader NKitHeader => null;
        public Checksums Checksums  => null;
        public Checksums CustomChecksums() => null;
        public void SetRemovedBlock(Action<MetaData> setBlock) { }
        public void Complete() { _currentFileStream?.Dispose(); _currentFileStream = null; }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;  // not used directly — files opened per-entry
            return 0;
        }

        // Call instead of Construct when using explicit file-opener lambdas (WUA path)
        internal void Populate(List<FileEntry> files, Func<int, Stream> openFile)
        {
            _files   = files;
            _openFile = openFile;
            _size     = files.Count > 0 ? files[files.Count - 1].StreamOffset + files[files.Count - 1].Size : 0;
            _position = 0;
        }

        // Call instead of Construct when using the filesystem directly (Loadiine path)
        internal void Populate(List<FileEntry> files, string baseFolder)
        {
            _files    = files;
            _openFile = idx => File.OpenRead(Path.Combine(baseFolder, files[idx].RelPath.Replace('/', Path.DirectorySeparatorChar)));
            _size     = files.Count > 0 ? files[files.Count - 1].StreamOffset + files[files.Count - 1].Size : 0;
            _position = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (count > 0 && _position < _size)
            {
                // Find which file contains _position
                int idx = findFileIndex(_position);
                if (idx < 0 || idx >= _files.Count) break;

                FileEntry f   = _files[idx];
                long fileOff  = _position - f.StreamOffset;
                long fileRem  = f.Size - fileOff;
                int  toRead   = (int)Math.Min(count, fileRem);

                // Open / reuse the current file stream
                if (_currentFileIdx != idx)
                {
                    _currentFileStream?.Dispose();
                    _currentFileStream = _openFile(idx);
                    _currentFileIdx    = idx;
                }
                _currentFileStream.Seek(fileOff, SeekOrigin.Begin);

                int got = 0;
                while (got < toRead)
                {
                    int n = _currentFileStream.Read(buffer, offset + total + got, toRead - got);
                    if (n == 0) break;
                    got += n;
                }

                total     += got;
                _position += got;
                count     -= got;
                if (got < toRead) break; // short read — EOF of this file
            }
            return total;
        }

        // Binary search for the file that owns virtualOffset
        private int findFileIndex(long virtualOffset)
        {
            int lo = 0, hi = _files.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                long start = _files[mid].StreamOffset;
                long end   = start + _files[mid].Size;
                if (virtualOffset < start)      hi = mid - 1;
                else if (virtualOffset >= end)  lo = mid + 1;
                else                            return mid;
            }
            return lo < _files.Count ? lo : -1;
        }

        // ── Factory helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a <see cref="FolderFilesAsIso"/> when the source is a WUA file or a
        /// Loadiine synthetic folder; otherwise returns null so the normal detection chain
        /// continues.  Called from <c>NKitInput.createImageContainer</c> before byte-pattern
        /// detection.
        /// </summary>
        internal static FolderFilesAsIso Create(SourceFile sf, ILogScope log)
        {
            // Real WUA file
            if (sf.ImageType == SourceImageType.Wua)
            {
                SourceFileItem item = sf.ImageFiles?[0];
                if (item == null) return null;
                string path = System.IO.Path.Combine(item.Path, item.FileName);
                if (!System.IO.File.Exists(path)) return null;
                Stream wuaStream = System.IO.File.OpenRead(path);
                return FromWua(wuaStream, log);
            }

            // Loadiine synthetic folder
            if (sf.IsSyntheticFolder &&
                sf.SyntheticFolderGroup?.GroupType == FolderGroupType.LoadiineFolder)
            {
                string folder = sf.SyntheticFolderGroup.SourceFolder ?? sf.BasePath;
                if (!System.IO.Directory.Exists(folder)) return null;
                return FromFolder(folder);
            }

            return null;
        }

        /// <summary>
        /// Builds a <see cref="FolderFilesAsIso"/> from a WUA ZArchive stream.
        /// The stream is rewound after the header is read for the archive open.
        /// </summary>
        internal static FolderFilesAsIso FromWua(Stream wuaStream, ILogScope log)
        {
            var iso = new FolderFilesAsIso();
            ZArchiveReader archive = ZArchiveReader.Open(wuaStream);

            string titleFolder = archive.GetRootDirectories()
                .FirstOrDefault(d => _titleFolderRegex.IsMatch(d));

            if (titleFolder == null)
                throw new HandledException("FolderFilesAsIso: no valid title folder in WUA");

            var entries = archive.EnumerateFiles(titleFolder)
                .OrderBy(f => f.path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            long offset = 0;
            var files = entries.Select(e =>
            {
                var fe = new FileEntry { RelPath = e.path, StreamOffset = offset, Size = e.size };
                offset += e.size;
                return fe;
            }).ToList();

            iso.Populate(files, idx => archive.OpenFile($"{titleFolder}/{files[idx].RelPath}"));
            iso._stream = wuaStream; // keep reference so archive stream lives
            return iso;
        }

        /// <summary>Builds a <see cref="FolderFilesAsIso"/> from a Loadiine folder path.</summary>
        internal static FolderFilesAsIso FromFolder(string folder)
        {
            var iso = new FolderFilesAsIso();

            var rawFiles = Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();

            long offset = 0;
            var files = rawFiles.Select(rel =>
            {
                long sz = new FileInfo(Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar))).Length;
                var fe  = new FileEntry { RelPath = rel, StreamOffset = offset, Size = sz };
                offset += sz;
                return fe;
            }).ToList();

            iso.Populate(files, folder);
            return iso;
        }

        // ── Stream boilerplate ────────────────────────────────────────────────────────

        public override bool CanRead  => true;
        public override bool CanSeek  => true;
        public override bool CanWrite => false;
        public override long Length   => _size;
        public override void Flush()  { }
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin   => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End     => _size + offset,
                _                  => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            _position = Math.Clamp(_position, 0, _size);
            return _position;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _currentFileStream?.Dispose(); _currentFileStream = null; }
            base.Dispose(disposing);
        }
    }
}

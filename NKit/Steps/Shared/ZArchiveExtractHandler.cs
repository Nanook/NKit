using Nanook.NKit.Container.ZArchive;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// IExtractFileHandler implementation that writes extracted WiiU files directly into a
    /// ZArchiveWriter, producing a .wua output instead of writing to disk.
    ///
    /// The title folder name (e.g. "000500001012d000_v0") must be set via
    /// <see cref="SetTitleFolder"/> before any files are written. The path structure inside
    /// the archive is: titleFolder/code|content|meta/filename
    ///
    /// Non-game partitions (SI, Update) are skipped — call <see cref="SetSkip"/> to suppress
    /// all writes until the next <see cref="SetTitleFolder"/> call.
    /// </summary>
    internal class ZArchiveExtractHandler : IExtractFileHandler
    {
        private readonly ZArchiveWriter _writer;
        private string _currentPath;
        private long _currentFsSize;
        private readonly HashSet<string> _createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _titleFolder; // archive-level title subfolder (e.g. "000500001012d000_v0")
        private bool _skip;          // when true, suppress all writes (non-game partition)

        public string TitleFolder => _titleFolder;

        public ZArchiveExtractHandler(ZArchiveWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>
        /// Sets the archive-level title folder for the next partition.
        /// Must be called before any file writes for each partition.
        /// Clears any active skip state.
        /// </summary>
        public void SetTitleFolder(string titleFolderName)
        {
            _titleFolder = titleFolderName;
            _skip = false;
            _currentPath = null;
            // Pre-create the title folder in the archive
            if (_createdDirs.Add(_titleFolder))
                _writer.MakeDir(_titleFolder);
        }

        /// <summary>
        /// Marks the current partition as skipped — all writes are suppressed until
        /// the next <see cref="SetTitleFolder"/> call.
        /// </summary>
        public void SetSkip()
        {
            _skip = true;
            _titleFolder = null;
            _currentPath = null;
        }

        // ── IExtractFileHandler ───────────────────────────────────────────────

        public void CreateDirectory(string rootPath, string imagePath)
        {
            if (_skip || _titleFolder == null) return;
            string archivePath = buildArchivePath(imagePath);
            if (_createdDirs.Add(archivePath))
                _writer.MakeDir(archivePath);
        }

        public void WriteBytes(string rootPath, string imagePath, byte[] data)
        {
            if (_skip || _titleFolder == null) return;
            string archivePath = buildArchivePath(imagePath);
            ensureParentDirs(archivePath);
            _writer.StartFile(archivePath);
            if (data.Length > 0)
                _writer.Write(data, 0, data.Length);
        }

        public bool WriteFs(ISection section, string rootPath, string imagePath, long pos, int fsOffset, int fsSize, long fullFsSize, bool replace)
        {
            if (_skip || _titleFolder == null) return false;

            string archivePath = buildArchivePath(imagePath);
            ensureParentDirs(archivePath);

            bool newFile = false;

            if (_currentPath == null || !string.Equals(_currentPath, archivePath, StringComparison.OrdinalIgnoreCase) || pos == 0)
            {
                CloseFs();
                _currentPath = archivePath;
                _currentFsSize = fullFsSize;
                newFile = true;
                _writer.StartFile(archivePath);
            }

            // Write the section's FS bytes directly to the ZArchive
            using var tmp = new MemoryStream();
            section.Read(fsOffset, fsSize, tmp);
            byte[] tmpBytes = tmp.GetBuffer();
            _writer.Write(tmpBytes, 0, (int)tmp.Length);

            if (fullFsSize != -1 && pos + fsSize >= fullFsSize)
                CloseFs();

            return newFile;
        }

        public void CloseFs()
        {
            _currentPath   = null;
            _currentFsSize = 0;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string buildArchivePath(string imagePath)
        {
            // imagePath comes in as a rooted filesystem path relative to WritePath,
            // e.g. "GM0005000010102000\code\app.xml" or just "code\app.xml".
            // We want: titleFolder/code/app.xml
            string rel = imagePath.TrimStart('/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // If the path starts with the partition ID prefix (e.g. "GM001012d000") strip it.
            // The title folder is always the archive-level folder; partition sub-path is not needed.
            // The imagePath from ExtractWiiUStep is: _ptnPath + type + filepath  
            // where _ptnPath = ptn.Id (e.g. "GM0005000010102000").
            // Strip the leading partition segment if present so we get code/content/meta directly.
            string[] parts = rel.Replace('\\', '/').Split('/');
            int start = 0;
            // Heuristic: if the first segment looks like a WiiU partition ID (starts with GM/SI/UP/GI
            // followed by hex digits, or is a short alphanumeric token that is not code/content/meta),
            // skip it.
            if (parts.Length > 1)
            {
                string first = parts[0];
                bool isKnownDir = string.Equals(first, "code",    StringComparison.OrdinalIgnoreCase)
                               || string.Equals(first, "content", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(first, "meta",    StringComparison.OrdinalIgnoreCase);
                if (!isKnownDir)
                    start = 1; // skip the partition ID prefix
            }

            string stripped = string.Join("/", parts, start, parts.Length - start);
            return _titleFolder + "/" + stripped;
        }

        private void ensureParentDirs(string archivePath)
        {
            string[] parts = archivePath.Split('/');
            string current = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current = i == 0 ? parts[i] : current + "/" + parts[i];
                if (_createdDirs.Add(current))
                    _writer.MakeDir(current);
            }
        }
    }
}

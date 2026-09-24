using NKitDataStore;
using NKitDataStore.Interfaces;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Nanook.NKit
{
    /// <summary>
    /// Provides access to file streams in the local filesystem and within archives. It's a bit clunky in order to support SOLID rar
    /// </summary>
    internal class SourceFileSystemReader : IDisposable
    {
        private DirectoryInfo _path;
        private FileItem _arcItem;
        private ILogScope _log;
        private int _currArcEntryIndex;
        private IArchive _archive;

        private Stream _stream;
        private List<FileItem> _files;
        private CancellationToken? _cancel;
        private bool _seekMessageOutput;


        internal SourceFileSystemReader(FileItem archive, List<FileItem> files, ILogScope log, CancellationToken? cancel)
        {
            _arcItem = archive;
            _log = log;
            _files = files; //must be in the order they appear in the archive
            _currArcEntryIndex = -1;
            _cancel = cancel;
        }

        public SourceFileSystemReader(DirectoryInfo path, List<FileItem> files, ILogScope log, CancellationToken? cancel)
        {
            _path = path;
            _log = log;
            _files = files;
            _cancel = cancel;
        }

        public Stream OpenRead(FileItem file)
        {
            closeStream();

            int idx = _files.IndexOf(file);
            if (idx == -1)
                return null;

            if (_arcItem == null) //local access
                return _stream = file.OpenFileStream();
            else if (string.Equals(_arcItem.Extension, DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Read file contents from the DataStore image when available.
                // FileItem.PathFileName may be of the form "ImageName/InnerFile" for stored files.
                try
                {
                    string dsPath = Path.GetFullPath(_arcItem.PathFileName);
                    string dsRoot = Path.GetDirectoryName(dsPath);
                    string setName = Path.GetFileNameWithoutExtension(dsPath);

                    // Prefer explicit DataStore image id on the FileItem, fallback to archive-level id
                    long? imageId = file.DataStoreImageId ?? _arcItem.DataStoreImageId;

                    if (imageId.HasValue)
                    {
                        using (DataStore ds = new DataStore(dsRoot))
                        using (IImageReader reader = ds.OpenImageReader(new GlobalImageKey(setName, imageId.Value)))
                        {
                            string inner = file.PathFileName;
                            // inner may be like "ImageName/filename" when we enumerated files earlier
                            int sep = inner.IndexOf('/');
                            if (sep >= 0)
                                inner = inner.Substring(sep + 1);

                            byte[] data = null;
                            try { data = reader.ReadFile(inner); } catch { }
                            if (data != null)
                                return _stream = new MemoryStream(data, false);
                        }
                    }
                }
                catch { }

                // Fall back to returning the path bytes so callers get something (avoids nulls)
                return _stream = new MemoryStream(Encoding.UTF8.GetBytes(file.PathFileName), false);
            }
            else
                return _stream = arcOpen(idx);
        }

        private Stream arcOpen(int index)
        {
            bool canSeek = true;

            if (_archive != null)
                canSeek = _archive.Type != ArchiveType.Rar || !_archive.IsSolid; //7zip has solid, but will seek

            if (_archive == null || (!canSeek && index <= _currArcEntryIndex)) //open OR open if not seekable and file is previous to ours
            {
                closeArchive();
                _archive = SourceFiles.ArchiveOpen(_arcItem); //handles multipart archives
                canSeek = _archive.Type != ArchiveType.Rar || !_archive.IsSolid; //7zip has solid, but will seek
                _currArcEntryIndex = -1;
            }

            if (!canSeek) //read to our file
            {
                if (!_seekMessageOutput)
                    _log?.Info(() => $"Solid mode archive, there may be long seek delays for {_arcItem.Name}");
                _seekMessageOutput = true;
                if (_stream != null && _stream.Position != _stream.Length) //we already returned a stream - did the caller read to the end - if not we must finish
                    _stream.SafeSeek(_stream.Length - _stream.Position, SeekOrigin.Current, _cancel); //current is safe - it will not read the base Position

                //skip entries to read forward to the one we need
                while (++_currArcEntryIndex < index)
                {
                    string fullName = _files[_currArcEntryIndex].PathFileName;
                    _log?.Trace(() => $"Solid mode file skip: {fullName}");
                    using (Stream sr = _archive.Entries.FirstOrDefault(a => a.Key == fullName).OpenEntryStream())
                        sr.SafeSeek(_files[_currArcEntryIndex].Size, SeekOrigin.Current, _cancel);
                }
            }

            _currArcEntryIndex = index; //index will be equal if !seek mode as we will have read to this position
            return SourceStream.Open(i => _archive.Entries.FirstOrDefault(a => a.Key == _files[index].PathFileName).OpenEntryStream(), new long[] { _files[index].Size }, false); //SourceStream lets us safely check position and seek forward
        }

        private void closeStream()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }

        private void closeArchive()
        {
            try { _archive?.Dispose(); } catch { }
            _archive = null;
        }

        public void Dispose()
        {
            closeStream();
            closeArchive();
        }
    }
}
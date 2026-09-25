using NKitDataStore;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit
{
    /// <summary>
    /// Source stream to wrap split file and make them look like one, seek supported
    /// </summary>
    internal class SourceStream : Stream
    {
        private readonly long[] _sizes;
        private int _idx;
        private long _size;
        private bool _isSplit;
        private long _prevSizeTotal;
        private bool _canSeek;
        private long _partPos; //keep position here as _stream.Position can be unreliable if archives are used
        private Func<int, Stream> _openStream;
        private Stream _stream;
        private IDisposable _container; //keep the archive alive or any other provider of streams
        private ILogScope _log;               //optional; enables [SplitStream]/[Archive] Detail+Trace logging

        /// <summary>
        /// Open split files as a single stream
        /// </summary>
        public static SourceStream Open(Func<int, Stream> openStream, long[] fileSizes, bool canSeek, ILogScope log = null)
            => new SourceStream(openStream, fileSizes, canSeek, log);

        /// <summary>
        /// Open single/multipart/split files within an archive. Split are archives that are chopped up. SharpCompress can't handle them
        /// </summary>
        public static SourceStream OpenArchive(FileInfo[] arcs, string[] fileNames, long[] fileSizes, ILogScope log = null)
        {
            if (arcs.Length >= 1 && string.Equals(arcs[0].Extension, DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                return new SourceStream(i => new MemoryStream(Encoding.UTF8.GetBytes(fileNames[i]), false), fileSizes, false, log);

            IArchive archive = ArchiveFactory.OpenArchive(arcs);

            // [Input] [Archive] Detail: what archive was opened, once — type, solidity, part count.
            ILogScope arcScope = log?.ScopeFor(LogScopes.Input);
            if (arcScope != null && arcScope.IsEnabled(LogLevel.Detail))
                arcScope.Log(LogLevel.Detail,
                    $"{LogScopes.Tag(LogScopes.Archive)}opened {archive.Type} solid:{(archive.IsSolid ? "y" : "n")}"
                    + $" arcParts {arcs.Length} entries {fileNames.Length}");

            //read files from within the archive, seamlesly joining split files together (bins in a cue etc)
            //return new SourceStream(i => archive.Entries.FirstOrDefault(a => a.Key == fileNames[i]).OpenEntryStream(), fileSizes, false) { _container = archive };
            IArchiveEntry item = null;
            int currIdx = -1;
            SourceStream src = new SourceStream(i =>
            {
                if (archive.Type == SharpCompress.Common.ArchiveType.Rar && archive.IsSolid)
                {
                    currIdx++;
                    List<IArchiveEntry> ents = archive.Entries.Where(a => !a.IsDirectory).ToList();
                    int idx = ents.FindIndex(a => a.Key == fileNames[i]);
                    if (currIdx > idx)
                        currIdx = 0; //item is earlier in the stream. go back to the start

                    while (currIdx < idx) //skip any items
                    {
                        using (Stream s = ents[currIdx++].OpenEntryStream())
                            s.CopyTo(ByteStream.Null);
                    }
                    item = ents[currIdx];
                }
                else
                    item = archive.Entries.FirstOrDefault(a => a.Key == fileNames[i]);

                return item.OpenEntryStream();
            }, fileSizes, false, log)
            { _container = archive };
            return src;
        }

        private SourceStream(Func<int, Stream> openStream, long[] sizes, bool canSeek, ILogScope log = null)
        {
            _openStream = openStream;
            _sizes = sizes;
            _canSeek = canSeek;
            _isSplit = sizes.Length > 1;
            _size = sizes.Sum(a => a);
            _idx = 0;
            _prevSizeTotal = 0;
            _log = log;

            // [Input] [SplitStream] Detail: report a multi-part / split source once — part count and
            // total size. Single-part local files are unremarkable, so only log when split.
            if (_isSplit)
            {
                ILogScope s = _log?.ScopeFor(LogScopes.Input);
                if (s != null && s.IsEnabled(LogLevel.Detail))
                    s.Log(LogLevel.Detail,
                        $"{LogScopes.Tag(LogScopes.SplitStream)}{sizes.Length} parts total 0x{_size:X}");
            }

            _stream = openStream(0);
        }

        private Stream openStream(int idx)
        {
            if (_stream != null)
                _stream.Dispose();

            _stream = _openStream(idx);
            _idx = idx;
            _partPos = 0;

            // [Input] [SplitStream] Trace: each part switch — chatty on a many-part split, so Trace
            // (only visible at --console-level debug), not Detail.
            if (_isSplit && _log != null)
            {
                ILogScope s = _log.ScopeFor(LogScopes.Input);
                if (s != null && s.IsEnabled(LogLevel.Trace))
                    s.Log(LogLevel.Trace,
                        $"{LogScopes.Tag(LogScopes.SplitStream)}part {idx + 1}/{_sizes.Length} opened (size 0x{(idx < _sizes.Length ? _sizes[idx] : 0):X})");
            }

            return _stream;
        }

        public override bool CanRead => true;

        public override bool CanSeek => _canSeek;

        public override bool CanWrite => false;

        public override long Length => _size;

        public override long Position
        {
            get => _prevSizeTotal + _partPos;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() => _stream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = (int)Math.Min(count, _size - this.Position);

            if (count <= 0)
                return 0;


            int total = count;
            int r = -1;

            while (count != 0 && r != 0)
            {
                r = _stream.Read(buffer, offset, count);
                _partPos += (long)r;
                count -= r;
                offset += r;

                if (_isSplit && _idx < _sizes.Length && _partPos == _sizes[_idx])
                    Seek(0, SeekOrigin.Current); //will load next file
            }

            return total - count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long fullPos = this.Position;
            switch (origin)
            {
                case SeekOrigin.Begin: fullPos = offset; break;
                case SeekOrigin.Current: fullPos += offset; break;
                case SeekOrigin.End: fullPos = Length + offset; break;
            }

            if (fullPos > this.Length)
                fullPos = this.Length;

            if (_isSplit)
            {
                _prevSizeTotal = 0;
                int i;
                for (i = 0; i < _sizes.Length; i++)
                {
                    if (_prevSizeTotal + _sizes[i] > fullPos)
                    {
                        if (_idx != i)
                        {
                            _stream = openStream(i);
                            _partPos = 0;
                        }
                        break;
                    }
                    _prevSizeTotal += _sizes[i];
                }
                _idx = i;
            }

            long newPartPos = fullPos - _prevSizeTotal;
            if (_idx == _sizes.Length)
                _partPos = 0;
            else if (newPartPos != _partPos && fullPos != this.Length)
            {
                _stream.SafeSeek(newPartPos - _partPos, SeekOrigin.Current);
                _partPos = newPartPos;

                //_partPos = fullPos - _prevSizeTotal;
            }

            return fullPos;
        }

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public override void Close()
        {
            try
            {
                if (_stream != null)
                    _stream.Close();
            }
            catch { }
            base.Close();
            _stream = null;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_stream != null)
                    _stream.Dispose();
            }
            catch { }
            _stream = null;

            try
            {
                if (_container != null)
                    _container.Dispose();
            }
            catch { }
            _container = null;

            base.Dispose(disposing);
        }
    }
}
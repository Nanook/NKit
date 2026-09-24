using System;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class FixPartition : FixFileItem
    {
        public FixPartition(string filename, string type, long length, uint crc, string id, string subId) : base(filename, type, length, crc)
        {
            this.Id = id;
            this.SubId = subId;
        }

        public string Id { get; set; }
        public string SubId { get; set; }

        // InnerEntryName / IsArchived / DisplayName are inherited from FixFileItem.

        /// <summary>
        /// Open the recovery partition's DATA as a forward-readable stream: a plain on-disk file
        /// (<see cref="InnerEntryName"/> null) or the single named entry inside a streamable archive
        /// (per the Wii archive fix-file convention). Uses the same scan/read primitives as the
        /// DatManager. The returned stream owns any archive reader and disposes it on Close/Dispose.
        /// Returns null when the data cannot be opened.
        ///
        /// NOTE: archive-entry streams are forward-only (CanSeek == false) but report Length; callers
        /// read them sequentially (WiiGc.Image, WiiFixAsIso, FixWiiGcStep all do).
        /// </summary>
        public Stream OpenDataStream(ILogScope log)
        {
            if (this.Filename == null || !File.Exists(this.Filename))
                return null;

            if (this.InnerEntryName == null)
                return File.OpenRead(this.Filename);

            FileMask mask = FileMask.CreateLocalMask($"{this.Filename}//{this.InnerEntryName}", false);
            FileItem entry = SourceFileSystem.GetLocalArchiveFiles(mask, false, log, null)
                .FirstOrDefault(a => string.Equals(a.FileName, this.InnerEntryName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;

            SourceFileSystemReader rdr = SourceFileSystem.CreateReader(entry, log, null);
            Stream s = rdr.OpenRead(entry);
            if (s == null)
            {
                try { rdr.Dispose(); } catch { }
                return null;
            }
            return new OwningStream(s, rdr);
        }

        /// <summary>Wraps a stream and an owned IDisposable (the archive reader), disposing both.
        /// Forward-only pass-through so WiiGc.Image's incremental reads + EOF check work on an
        /// archive entry the same as on a FileStream.</summary>
        private sealed class OwningStream : Stream
        {
            private readonly Stream _s;
            private IDisposable _owned;
            public OwningStream(Stream s, IDisposable owned) { _s = s; _owned = owned; }
            public override bool CanRead => _s.CanRead;
            public override bool CanSeek => _s.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _s.Length;
            public override long Position { get => _s.Position; set => _s.Position = value; }
            public override void Flush() => _s.Flush();
            // Fill the requested count (loop) so callers that assume a single Read fills the buffer
            // (WiiGc.Image.readBuf and its raw insert-read) behave the same on a forward-only archive
            // entry stream as on a FileStream. Returns a short count only at real EOF.
            public override int Read(byte[] buffer, int offset, int count)
            {
                int total = 0;
                while (total < count)
                {
                    int n = _s.Read(buffer, offset + total, count - total);
                    if (n == 0)
                        break;
                    total += n;
                }
                return total;
            }
            public override long Seek(long offset, SeekOrigin origin) => _s.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    try { _s.Dispose(); } catch { }
                    try { _owned?.Dispose(); } catch { }
                    _owned = null;
                }
                base.Dispose(disposing);
            }
        }
    }

}
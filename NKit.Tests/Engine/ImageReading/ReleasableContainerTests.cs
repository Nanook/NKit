using Nanook.NKit;
using Nanook.NKit.Container;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Tests the Layer-A cache release mechanism that the ISO9660 / WiiGc / XBox image readers rely
    /// on: a pass-through container (<see cref="DefaultAsIso"/>) reads the raw source (a
    /// <see cref="BufferStream"/> over a possibly forward-only archive entry) directly, and exposes
    /// <see cref="IReleasable.ReleaseTo"/> so the consuming Image can bound that raw-source cache as
    /// it reads forward. Without this the raw cache grows to hold the whole image (the PS3/WiiU/XBox
    /// archive OOM). The release must be clamped to the container's OWN read position so it never
    /// frees blocks the pass-through source still has to read through to reach a skipped-to target.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class ReleasableContainerTests
    {
        private const int Size = 0x100000;

        private static byte[] numberData()
        {
            byte[] d = new byte[Size];
            for (int i = 0; i < d.Length; i += 4)
                d.WriteUInt32B(i, (uint)i);
            return d;
        }

        // Forward-only source (CanSeek=false) modelling an archive entry — its BufferStream caches.
        private sealed class ForwardOnlyStream : Stream
        {
            private readonly byte[] _data;
            private long _pos;
            public ForwardOnlyStream(byte[] data)
            {
                _data = data;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Min(count, _data.Length - _pos);
                if (n <= 0) return 0;
                Array.Copy(_data, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static DefaultAsIso makeContainer(out BufferStream layerA)
        {
            layerA = new BufferStream(new ForwardOnlyStream(numberData()));
            // .iso id — DefaultAsIso maps a non-.SFB id to ContainerType.Iso.
            DefaultAsIso iso = new DefaultAsIso(new byte[0x400], IndexFileType.None);
            iso.Construct(layerA, true);
            return iso;
        }

        [Fact]
        public void DefaultAsIso_IsReleasable()
        {
            DefaultAsIso iso = makeContainer(out _);
            Assert.IsAssignableFrom<IReleasable>(iso);
        }

        [Fact]
        public void ReleaseTo_ForwardsToLayerA_FreesBelowFloor()
        {
            DefaultAsIso iso = makeContainer(out BufferStream layerA);
            byte[] buf = new byte[0x1000];

            // Read forward through the container (advances its _position and the Layer-A frontier).
            iso.Read(buf, 0, 0x800);
            Assert.Equal(0x800, iso.Position);

            // Release Layer-A up to the container's read position.
            ((IReleasable)iso).ReleaseTo(0x800);

            // The Layer-A BufferStream floor advanced: a read below it now throws.
            layerA.Seek(0x100, SeekOrigin.Begin);
            Assert.Throws<InvalidOperationException>(() => layerA.Read(buf, 0, 0x100));
        }

        [Fact]
        public void ReleaseTo_ClampsToOwnReadPosition_DoesNotFreeUnreadForwardData()
        {
            // The consuming Image may release to an IMAGE-space position that runs AHEAD of what the
            // pass-through source has physically read (e.g. Iso9660 skip() advances over a skipped
            // region and releases to the skip target, but the source must still be read forward
            // THROUGH that region). The clamp to _position prevents freeing not-yet-read blocks.
            DefaultAsIso iso = makeContainer(out BufferStream layerA);
            byte[] buf = new byte[0x1000];

            iso.Read(buf, 0, 0x400);           // container has physically read only up to 0x400
            Assert.Equal(0x400, iso.Position);

            // Ask to release far ahead (as a skip target would). Must clamp to 0x400.
            ((IReleasable)iso).ReleaseTo(0x40000);

            // Data at/after the container's own read position is still readable (not freed):
            // continue reading forward through the region a skip would traverse.
            layerA.Seek(0x400, SeekOrigin.Begin);
            Assert.Equal(0x400, layerA.Read(buf, 0, 0x400));
            testBytes(0x400, buf, 0, 0x400);
        }

        private static void testBytes(long pos, byte[] data, int offset, int size)
        {
            for (int i = 0; i < size; i += 4)
                Assert.Equal((uint)(pos + i), data.ReadUInt32B(offset + i));
        }
    }
}
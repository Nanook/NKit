using Nanook.NKit;
using Nanook.NKit.Container;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Detection tests for the IAsIso container factories. Each container is identified by its
    /// static <c>Create(byte[] header)</c> from a small header buffer — no instantiation of the
    /// heavy decode path, no stream reads beyond the header. These tests pin the magic-byte
    /// detection and the precedence edges (WBFS vs the 0x200 NKit marker; CISO vs CSO) that the
    /// production chain in <c>NKitInput.createImageContainer</c> relies on.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class AsIsoDetectionTests
    {
        // A 1 MiB header buffer, matching the size NKitInput peeks for detection.
        private static byte[] Header() => new byte[0x400 * 0x400];

        private static byte[] WithAscii(int offset, string magic)
        {
            byte[] h = Header();
            for (int i = 0; i < magic.Length; i++)
                h[offset + i] = (byte)magic[i];
            return h;
        }

        private static byte[] WithU32L(int offset, uint value)
        {
            byte[] h = Header();
            h[offset + 0] = (byte)(value & 0xFF);
            h[offset + 1] = (byte)((value >> 8) & 0xFF);
            h[offset + 2] = (byte)((value >> 16) & 0xFF);
            h[offset + 3] = (byte)((value >> 24) & 0xFF);
            return h;
        }

        // ── Raw magic-string containers ─────────────────────────────────────────

        [Fact]
        public void Rvz_DetectedByMagic()
        {
            Assert.NotNull(RvzAsIso.Create(WithAscii(0, "RVZ\x1")));
            Assert.Null(RvzAsIso.Create(Header()));
        }

        [Fact]
        public void Wia_DetectedByMagic()
        {
            Assert.NotNull(WiaAsIso.Create(WithAscii(0, "WIA\u0001")));
            Assert.Null(WiaAsIso.Create(Header()));
        }

        [Fact]
        public void Wux_DetectedByMagic()
        {
            Assert.NotNull(WuxAsIso.Create(WithAscii(0, "WUX0")));
            Assert.Null(WuxAsIso.Create(Header()));
        }

        [Fact]
        public void Wbfs_DetectedByMagic()
        {
            Assert.NotNull(WbfsAsIso.Create(WithAscii(0, "WBFS")));
            Assert.Null(WbfsAsIso.Create(Header()));
        }

        [Fact]
        public void Jso_DetectedByMagic()
        {
            Assert.NotNull(JsoAsIso.Create(WithAscii(0, "JISO")));
            Assert.Null(JsoAsIso.Create(Header()));
        }

        [Fact]
        public void Dax_DetectedByMagic()
        {
            Assert.NotNull(DaxAsIso.Create(WithAscii(0, "DAX\0")));
            Assert.Null(DaxAsIso.Create(Header()));
        }

        [Fact]
        public void IsoDec_DetectedByMagic()
        {
            Assert.NotNull(IsoDecAsIso.Create(WithAscii(0, "WII5")));
            Assert.NotNull(IsoDecAsIso.Create(WithAscii(0, "WII9")));
            Assert.NotNull(IsoDecAsIso.Create(WithAscii(0, "GCML")));
            Assert.Null(IsoDecAsIso.Create(Header()));
        }

        [Fact]
        public void Gcz_DetectedByMagic()
        {
            // GCZ magic 0x01C00BB1, stored little-endian (0xB10BC001).
            Assert.NotNull(GczAsIso.Create(WithU32L(0, 0xB10BC001)));
            Assert.Null(GczAsIso.Create(Header()));
        }

        // ── CISO vs CSO precedence ──────────────────────────────────────────────

        [Fact]
        public void Ciso_DetectedOnlyWhenBlockSizeIsIsoScale()
        {
            // CISO requires "CISO" magic AND ReadUInt32L(0x4) >= 0x100; below that it's CSO.
            byte[] ciso = WithAscii(0, "CISO");
            ciso[0x4] = 0x00; ciso[0x5] = 0x01; // 0x100 => CISO
            Assert.NotNull(CisoAsIso.Create(ciso));

            byte[] cso = WithAscii(0, "CISO");
            cso[0x4] = 0x10; // 0x10 (< 0x100) => not CISO (CSO territory)
            Assert.Null(CisoAsIso.Create(cso));

            Assert.Null(CisoAsIso.Create(Header()));
        }

        // ── NKit detection + precedence ─────────────────────────────────────────

        [Fact]
        public void NKit_DetectedByRawMarkerAt0x200() => Assert.NotNull(NKitAsIso.Create(WithAscii(0x200, "NKIT")));

        [Fact]
        public void NKit_DetectedByGczMagicWhenGczWrapped() => Assert.NotNull(NKitAsIso.Create(WithU32L(0, 0xB10BC001)));

        [Fact]
        public void NKit_NotClaimedWhenWbfsMagicPresentAtZero()
        {
            // A WBFS that stores an NKit-encoded image also has the 0x200 NKit marker, but the
            // "WBFS" magic at offset 0 must take precedence — NKitAsIso must NOT claim it.
            byte[] wbfsWithNkitMarker = WithAscii(0, "WBFS");
            for (int i = 0; i < 4; i++)
                wbfsWithNkitMarker[0x200 + i] = (byte)"NKIT"[i];

            Assert.Null(NKitAsIso.Create(wbfsWithNkitMarker));
            Assert.NotNull(WbfsAsIso.Create(wbfsWithNkitMarker));
        }

        [Fact]
        public void NKit_NotDetectedForPlainHeader() => Assert.Null(NKitAsIso.Create(Header()));

        // ── Detection precedence chain (mirrors NKitInput.createImageContainer) ──

        [Fact]
        public void DetectionChain_PicksTheCorrectContainerFirst()
        {
            // First matching factory in precedence wins. Spot-check the notable ones.
            IAsIso Chain(byte[] id) =>
                NKitAsIso.Create(id) ??
                WuxAsIso.Create(id) ??
                WiaAsIso.Create(id) ??
                RvzAsIso.Create(id) ??
                CisoAsIso.Create(id) ??
                JsoAsIso.Create(id) ??
                DaxAsIso.Create(id) ??
                CsoZsoAsIso.Create(id) ??
                WbfsAsIso.Create(id) ??
                IsoDecAsIso.Create(id) ??
                GczAsIso.Create(id);

            Assert.IsType<RvzAsIso>(Chain(WithAscii(0, "RVZ\x1")));
            Assert.IsType<WuxAsIso>(Chain(WithAscii(0, "WUX0")));
            Assert.IsType<NKitAsIso>(Chain(WithAscii(0x200, "NKIT")));
            // WBFS + NKit marker => WBFS wins (NKit factory declines).
            byte[] wbfs = WithAscii(0, "WBFS");
            for (int i = 0; i < 4; i++) wbfs[0x200 + i] = (byte)"NKIT"[i];
            Assert.IsType<WbfsAsIso>(Chain(wbfs));
        }
    }
}
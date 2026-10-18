using Nanook.NKit;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using Xunit;

namespace NKit.Tests.Steps
{
    /// <summary>
    /// Unit tests for <see cref="GcNkitGapEncoder"/>.
    /// </summary>
    [Trait("Area", "Unit")]
    [Trait("Group", "GcNkitGapEncoder")]
    public class GcNkitGapEncoderTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────────

        private static readonly byte[] _discId = new byte[] { (byte)'G', (byte)'B', (byte)'Y', (byte)'E' };
        private const byte _discNo = 0;

        private static byte[] JunkAt(long discOffset, int length)
        {
            byte[] result = new byte[length];
            long blockStart = (discOffset / NJunk.JunkBlockSize) * NJunk.JunkBlockSize;
            byte[] cache = new byte[NJunk.JunkBlockSize];
            NJunk.Fill(_discId, _discNo, blockStart, WiiConsts.FullSizeGameCube, blockStart, cache);
            int junkOff = (int)(discOffset - blockStart);
            for (int i = 0; i < length; i++)
            {
                if (junkOff + i >= NJunk.JunkBlockSize)
                {
                    blockStart += NJunk.JunkBlockSize;
                    NJunk.Fill(_discId, _discNo, blockStart, WiiConsts.FullSizeGameCube, blockStart, cache);
                    junkOff -= NJunk.JunkBlockSize;
                }
                result[i] = cache[junkOff + i];
            }
            return result;
        }

        private static byte[] ZerosOf(int length) => new byte[length];

        private static byte[] NonJunkOf(int length)
        {
            byte[] b = new byte[length];
            for (int i = 0; i < length; i++)
                b[i] = (byte)((i ^ 0xAB) | 0x01);
            return b;
        }

        private static uint ReadU32BE(byte[] buf, int off)
        {
            return ((uint)buf[off] << 24) | ((uint)buf[off + 1] << 16)
                 | ((uint)buf[off + 2] <<  8) |  buf[off + 3];
        }

        /// <summary>Encode a gap from an array of pre-built blocks.</summary>
        private static byte[] Encode(byte[][] blocks, long discStart, long gapLength,
                                     int leadingNulls = 0, long dolStart = -1, long dolEnd = 0)
        {
            // Collect streamed output into a MemoryStream via the write callback.
            System.Collections.Generic.List<byte> output = new System.Collections.Generic.List<byte>();
            void write(byte[] buf, int off, int cnt) { for (int i = off; i < off + cnt; i++) output.Add(buf[i]); }

            GcNkitGapEncoder enc = new GcNkitGapEncoder();
            enc.Begin(discStart, gapLength, leadingNulls, _discId, _discNo, dolStart, dolEnd, write);
            for (int i = 0; i < blocks.Length; i++)
            {
                long discOff = discStart + (long)i * GcNkitGapEncoder.BlockSize;
                enc.Feed(blocks[i], 0, blocks[i].Length, discOff);
            }
            enc.Complete();
            return output.ToArray();
        }

        // ── Test 1: AllJunk ───────────────────────────────────────────────────────────

        [Fact]
        public void AllJunkGap_WritesHeaderOnly()
        {
            long discStart  = 0x200000L;
            int  blockCount = 4;
            long gapLength  = blockCount * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = new byte[blockCount][];
            for (int i = 0; i < blockCount; i++)
                blocks[i] = JunkAt(discStart + i * GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(blocks, discStart, gapLength);

            Assert.Equal(4, enc.Length);
            uint header = ReadU32BE(enc, 0);
            Assert.Equal(0u, header & 0x3u);
            Assert.Equal((uint)(gapLength & ~3L), header & ~3u);
        }

        // ── Test 2: AllScrub ──────────────────────────────────────────────────────────

        [Fact]
        public void AllScrubGap_WritesHeaderOnly()
        {
            long discStart = 0x300000L;
            int  blockCount = 3;
            long gapLength  = blockCount * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = new byte[blockCount][];
            for (int i = 0; i < blockCount; i++)
                blocks[i] = ZerosOf(GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(blocks, discStart, gapLength);

            Assert.Equal(4, enc.Length);
            uint header = ReadU32BE(enc, 0);
            Assert.Equal(1u, header & 0x3u);
            Assert.Equal((uint)(gapLength & ~3L), header & ~3u);
        }

        // ── Test 3: Mixed NonJunk first ────────────────────────────────────────────────

        [Fact]
        public void Mixed_NonJunkFirst_WritesHeaderThenDescriptorThenRawBytes()
        {
            long discStart = 0x400000L;
            long gapLength = GcNkitGapEncoder.BlockSize;
            byte[] nj      = NonJunkOf(GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(new[] { nj }, discStart, gapLength);

            // [header:4][nj-descriptor:4][raw:256]
            Assert.Equal(4 + 4 + GcNkitGapEncoder.BlockSize, enc.Length);
            uint header = ReadU32BE(enc, 0);
            Assert.Equal(2u, header & 0x3u);                          // Mixed
            Assert.Equal((uint)(gapLength & ~3L), header & ~3u);
            uint desc = ReadU32BE(enc, 4);
            Assert.Equal(1u << 30, desc & (3u << 30));                // NonJunk top bits
            Assert.Equal(1u, desc & 0x3FFFFFFFu);                     // count=1
            Assert.Equal(nj, enc[8..(8 + GcNkitGapEncoder.BlockSize)]);
        }

        // ── Test 4: Mixed leading Junk then NonJunk ────────────────────────────────────

        [Fact]
        public void Mixed_LeadingJunkThenNonJunk_CorrectOrder()
        {
            long discStart  = 0x500000L;
            int  junkBlocks = 3;
            long gapLength  = (junkBlocks + 1) * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = new byte[junkBlocks + 1][];
            for (int i = 0; i < junkBlocks; i++)
                blocks[i] = JunkAt(discStart + i * GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize);
            blocks[junkBlocks] = NonJunkOf(GcNkitGapEncoder.BlockSize);
            long njOff = discStart + junkBlocks * GcNkitGapEncoder.BlockSize;

            byte[] enc = Encode(blocks, discStart, gapLength);

            // [header:4][junk-desc:4][nj-desc:4][raw:256]
            Assert.Equal(4 + 4 + 4 + GcNkitGapEncoder.BlockSize, enc.Length);
            Assert.Equal(2u, ReadU32BE(enc, 0) & 3u);                             // Mixed
            uint jD = ReadU32BE(enc, 4);
            Assert.Equal(0u, jD >> 30);                                           // Junk
            Assert.Equal((uint)junkBlocks, jD & 0x3FFFFFFFu);
            uint njD = ReadU32BE(enc, 8);
            Assert.Equal(1u << 30, njD & (3u << 30));                            // NonJunk
            Assert.Equal(1u, njD & 0x3FFFFFFFu);
            Assert.Equal(blocks[junkBlocks], enc[12..(12 + GcNkitGapEncoder.BlockSize)]);
        }

        // ── Test 5: Consecutive Junk merged ───────────────────────────────────────────

        [Fact]
        public void ConsecutiveJunkBlocks_MergedIntoOneDescriptor()
        {
            long discStart  = 0x600000L;
            int  blockCount = 8;
            long gapLength  = blockCount * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = new byte[blockCount][];
            blocks[0] = NonJunkOf(GcNkitGapEncoder.BlockSize); // force Mixed
            for (int i = 1; i < blockCount; i++)
                blocks[i] = JunkAt(discStart + i * GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(blocks, discStart, gapLength);

            int afterNj = 4 + 4 + GcNkitGapEncoder.BlockSize; // header + nj-desc + raw
            uint jD = ReadU32BE(enc, afterNj);
            Assert.Equal(0u, jD >> 30);                                           // Junk
            Assert.Equal((uint)(blockCount - 1), jD & 0x3FFFFFFFu);              // merged
        }

        // ── Test 6: Consecutive Scrub merged ──────────────────────────────────────────

        [Fact]
        public void ConsecutiveScrubBlocks_MergedIntoOneDescriptor()
        {
            long discStart  = 0x700000L;
            int  blockCount = 5;
            long gapLength  = (1 + blockCount) * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = new byte[1 + blockCount][];
            blocks[0] = NonJunkOf(GcNkitGapEncoder.BlockSize); // force Mixed
            for (int i = 1; i <= blockCount; i++)
                blocks[i] = ZerosOf(GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(blocks, discStart, gapLength);

            int afterNj = 4 + 4 + GcNkitGapEncoder.BlockSize;
            uint sD = ReadU32BE(enc, afterNj);
            Assert.Equal(2u, sD >> 30);                                           // ByteFill
            Assert.Equal(0x00u, sD & 0xFFu);                                     // fill=0
            Assert.Equal((uint)blockCount, (sD >> 8) & 0x3FFFFFu);               // merged count
        }

        // ── Test 7: All three types ────────────────────────────────────────────────────

        [Fact]
        public void Mixed_AllThreeBlockTypes_CorrectOutput()
        {
            long discStart = 0x800000L;
            long gapLength = 3 * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = {
                NonJunkOf(GcNkitGapEncoder.BlockSize),
                JunkAt(discStart + GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize),
                ZerosOf(GcNkitGapEncoder.BlockSize)
            };

            byte[] enc = Encode(blocks, discStart, gapLength);

            // [header:4][nj-desc:4][raw:256][junk-desc:4][scrub-desc:4]
            Assert.Equal(4 + 4 + GcNkitGapEncoder.BlockSize + 4 + 4, enc.Length);
            Assert.Equal(2u, ReadU32BE(enc, 0) & 3u);
            Assert.Equal(1u << 30, ReadU32BE(enc, 4) & (3u << 30));              // NonJunk
            int afterNj = 4 + 4 + GcNkitGapEncoder.BlockSize;
            Assert.Equal(0u, ReadU32BE(enc, afterNj) >> 30);                     // Junk
            Assert.Equal(2u, ReadU32BE(enc, afterNj + 4) >> 30);                 // Scrub
        }

        // ── Test 8: Leading nulls ─────────────────────────────────────────────────────

        [Fact]
        public void LeadingNulls_BlockStartingWithNullsThenJunk_ClassifiedAsJunk()
        {
            long discStart    = 0x21340L;
            long gapLength    = GcNkitGapEncoder.BlockSize;
            int  leadingNulls = 28;

            byte[] block = JunkAt(discStart, GcNkitGapEncoder.BlockSize);
            for (int i = 0; i < leadingNulls; i++)
                block[i] = 0x00;

            byte[] enc = Encode(new[] { block }, discStart, gapLength, leadingNulls);

            Assert.Equal(4, enc.Length);
            Assert.Equal(0u, ReadU32BE(enc, 0) & 3u); // AllJunk
        }

        // ── Test 9: DOL overlap forced NonJunk ────────────────────────────────────────

        [Fact]
        public void DolOverlapBlock_ForcedToNonJunk_EvenIfBytesMatchJunk()
        {
            long discStart = 0x40000L;
            long gapLength = GcNkitGapEncoder.BlockSize;
            byte[] block   = JunkAt(discStart, GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(new[] { block }, discStart, gapLength,
                                 0, discStart, discStart + gapLength + 1);

            Assert.Equal(2u, ReadU32BE(enc, 0) & 3u); // Mixed
            Assert.Equal(1u << 30, ReadU32BE(enc, 4) & (3u << 30)); // NonJunk
        }

        // ── Test 10: JunkBlockSize boundary — exact junk across boundary ───────────────

        [Fact]
        public void JunkCacheBoundary_BlockStraddle_ClassifiedCorrectly()
        {
            long boundary  = NJunk.JunkBlockSize;
            long discStart = boundary - 64;
            long gapLength = GcNkitGapEncoder.BlockSize;
            byte[] block   = JunkAt(discStart, GcNkitGapEncoder.BlockSize);

            byte[] enc = Encode(new[] { block }, discStart, gapLength);

            Assert.Equal(4, enc.Length);
            Assert.Equal(0u, ReadU32BE(enc, 0) & 3u); // AllJunk
        }

        // ── Test 11: JunkBlockSize boundary — leading zeros then real data ────────────

        [Fact]
        public void JunkCacheBoundary_LeadingZerosThenRealData_IsNonJunk()
        {
            long boundary  = NJunk.JunkBlockSize;
            long discStart = boundary - 64;
            long gapLength = GcNkitGapEncoder.BlockSize;

            byte[] block = new byte[GcNkitGapEncoder.BlockSize];
            // bytes 64..255 are non-zero and not junk
            for (int i = 64; i < GcNkitGapEncoder.BlockSize; i++)
                block[i] = (byte)(0x28 + i);

            byte[] enc = Encode(new[] { block }, discStart, gapLength);

            Assert.Equal(2u, ReadU32BE(enc, 0) & 3u); // Mixed (allZero=false → NonJunk)
        }

        // ── Test 12: Partial last block ────────────────────────────────────────────────

        [Fact]
        public void PartialLastBlock_HandledCorrectly()
        {
            long discStart  = 0xA00000L;
            int  partialLen = 128;
            long gapLength  = partialLen;
            byte[] partial  = NonJunkOf(partialLen);

            byte[] result = Encode(new[] { partial }, discStart, gapLength);

            Assert.Equal(4 + 4 + partialLen, result.Length);
            Assert.Equal(2u, ReadU32BE(result, 0) & 3u);
            Assert.Equal((uint)(gapLength & ~3L), ReadU32BE(result, 0) & ~3u);
        }

        // ── Test 13: Multi-feed same result ────────────────────────────────────────────

        [Fact]
        public void MultipleFeeds_GapSpansMultipleCalls_SameResult()
        {
            long discStart  = 0xB00000L;
            int  blockCount = 6;
            long gapLength  = blockCount * GcNkitGapEncoder.BlockSize;
            byte[][] blocks = {
                NonJunkOf(GcNkitGapEncoder.BlockSize),
                JunkAt(discStart + 1 * GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize),
                JunkAt(discStart + 2 * GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize),
                ZerosOf(GcNkitGapEncoder.BlockSize),
                NonJunkOf(GcNkitGapEncoder.BlockSize),
                JunkAt(discStart + 5 * GcNkitGapEncoder.BlockSize, GcNkitGapEncoder.BlockSize),
            };

            byte[] resultA = Encode(blocks, discStart, gapLength);

            // Encode the same gap feeding blocks in two batches — result must be identical.
            System.Collections.Generic.List<byte> outputB = new System.Collections.Generic.List<byte>();
            void write(byte[] buf, int off, int cnt) { for (int i = off; i < off + cnt; i++) outputB.Add(buf[i]); }
            GcNkitGapEncoder enc = new GcNkitGapEncoder();
            enc.Begin(discStart, gapLength, 0, _discId, _discNo, -1, 0, write);
            for (int i = 0; i < 3; i++)
                enc.Feed(blocks[i], 0, GcNkitGapEncoder.BlockSize, discStart + i * GcNkitGapEncoder.BlockSize);
            for (int i = 3; i < blockCount; i++)
                enc.Feed(blocks[i], 0, GcNkitGapEncoder.BlockSize, discStart + i * GcNkitGapEncoder.BlockSize);
            enc.Complete();
            byte[] resultB = outputB.ToArray();

            Assert.Equal(resultA, resultB);
        }

        // ── Test 14: Large AllJunk gap — 4 bytes output ────────────────────────────────

        [Fact]
        public void LargeAllJunkGap_ProducesExactly4BytesOutput()
        {
            long discStart = 0x200000L;
            int  blocks    = 4 * 1024; // 1 MB of junk
            long gapLength = blocks * GcNkitGapEncoder.BlockSize;
            byte[][] junkBlocks = new byte[blocks][];
            for (int i = 0; i < blocks; i++)
            {
                long discOff = discStart + (long)i * GcNkitGapEncoder.BlockSize;
                junkBlocks[i] = JunkAt(discOff, GcNkitGapEncoder.BlockSize);
            }
            byte[] result = Encode(junkBlocks, discStart, gapLength);

            Assert.Equal(4, result.Length);
            Assert.Equal(0u, ReadU32BE(result, 0) & 3u);                        // AllJunk
            Assert.Equal((uint)(gapLength & ~3L), ReadU32BE(result, 0) & ~3u);
        }

        // ── Test 15: Remaining tracks correctly ────────────────────────────────────────

        [Fact]
        public void Remaining_DecreasesWithEachFeed_ReachesZeroWhenDone()
        {
            long discStart  = 0xC00000L;
            int  blockCount = 4;
            long gapLength  = blockCount * GcNkitGapEncoder.BlockSize;
            GcNkitGapEncoder enc = new GcNkitGapEncoder();
            enc.Begin(discStart, gapLength, 0, _discId, _discNo, -1, 0, (_, __, ___) => { });

            Assert.Equal(gapLength, enc.Remaining);

            for (int i = 0; i < blockCount; i++)
            {
                long discOff = discStart + i * GcNkitGapEncoder.BlockSize;
                enc.Feed(JunkAt(discOff, GcNkitGapEncoder.BlockSize), 0,
                         GcNkitGapEncoder.BlockSize, discOff);
                long expected = gapLength - ((long)(i + 1) * GcNkitGapEncoder.BlockSize);
                Assert.Equal(expected, enc.Remaining);
            }

            enc.Complete();
            Assert.Equal(0L, enc.Remaining);
        }
    }
}

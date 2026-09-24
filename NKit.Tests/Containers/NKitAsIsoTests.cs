using Nanook.NKit;
using Nanook.NKit.Container;
using System;
using System.IO;
using System.Text;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // decodes real NKit images — excluded from the fast CI gate (run locally)
    public class NKitAsIsoTests
    {
        // Real GC NKit test image — resolved via TestPaths.Resolve() for cross-platform compatibility
        private static readonly string TestImage = TestPaths.Resolve(@"D:\NKitFiles\GC_Iso\GC\Super Bubble Pop (USA).nkit.iso");
        private const long GcFullSize = 0x57058000L; // 1,459,978,240 bytes

        static NKitAsIsoTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static IAsIso CreateAndConstruct(out FileStream fs)
        {
            fs = File.OpenRead(TestImage);
            byte[] header = new byte[0x400];
            fs.Read(header, 0, header.Length);
            fs.Position = 0;

            IAsIso iso = NKitAsIso.Create(header);
            iso.Construct(fs, false);
            return iso;
        }

        [Fact]
        public void Create_DetectsNKitHeader()
        {
            byte[] header = new byte[0x400];
            using (FileStream fs = File.OpenRead(TestImage))
                fs.Read(header, 0, header.Length);

            IAsIso iso = NKitAsIso.Create(header);
            Assert.NotNull(iso);
        }

        [Fact]
        public void Create_ReturnsNullForNonNKit()
        {
            byte[] header = new byte[0x400]; // all zeros — no "NKIT" at 0x200
            IAsIso iso = NKitAsIso.Create(header);
            Assert.Null(iso);
        }

        [Fact]
        public void Construct_SetsCorrectImageSize()
        {
            using (FileStream fs = File.OpenRead(TestImage))
            {
                byte[] header = new byte[0x400];
                fs.Read(header, 0, header.Length);
                fs.Position = 0;

                IAsIso iso = NKitAsIso.Create(header);
                iso.Construct(fs, false);

                // GC NKit should decode to full GC disc size
                Assert.Equal(GcFullSize, iso.Size);
                Assert.False(iso.SizeEstimated);
            }
        }

        [Fact]
        public void Decode_OutputHasValidGcMagic()
        {
            IAsIso iso = CreateAndConstruct(out FileStream fs);
            using (fs)
            {
                byte[] discHdr = new byte[0x440];
                int read = iso.Read(discHdr, 0, discHdr.Length);
                Assert.Equal(0x440, read);

                // GC magic at 0x1C
                uint gcMagic = discHdr.ReadUInt32B(0x1c);
                Assert.Equal(0xc2339f3du, gcMagic);

                // Wii magic should NOT be present
                uint wiiMagic = discHdr.ReadUInt32B(0x18);
                Assert.NotEqual(0x5d1c9ea3u, wiiMagic);
            }
        }

        [Fact]
        public void Decode_NKitHeaderFieldsCleared()
        {
            IAsIso iso = CreateAndConstruct(out FileStream fs);
            using (fs)
            {
                byte[] discHdr = new byte[0x440];
                iso.Read(discHdr, 0, discHdr.Length);

                // NKit header area (0x200..0x21F) should be zeroed in decoded output
                for (int i = 0x200; i < 0x220; i++)
                    Assert.Equal(0, discHdr[i]);
            }
        }

        [Fact]
        public void Decode_FstPointerAndDataValid()
        {
            IAsIso iso = CreateAndConstruct(out FileStream fs);
            using (fs)
            {
                byte[] discHdr = new byte[0x440];
                iso.Read(discHdr, 0, discHdr.Length);

                uint fstOffset = discHdr.ReadUInt32B(0x424);
                uint fstSize = discHdr.ReadUInt32B(0x428);

                // FST offset should be within the image and reasonable
                Assert.True(fstOffset > 0x440, "FST offset should be past disc header");
                Assert.True(fstOffset < GcFullSize, "FST offset should be within image");
                Assert.True(fstSize > 0, "FST size should be non-zero");
                Assert.True(fstSize < 0x100000, "FST size should be reasonable (< 1MB)");

                // Seek to FST and read it
                iso.Position = fstOffset;
                byte[] fstData = new byte[Math.Min(fstSize, 0x1000)];
                int read = iso.Read(fstData, 0, fstData.Length);
                Assert.True(read > 12);

                // First byte of root FST entry should be 0x01 (directory type)
                Assert.Equal(0x01, fstData[0]);

                // Root entry's "next offset" (number of entries) at offset 0x08 should be > 0
                uint numEntries = fstData.ReadUInt32B(0x08);
                Assert.True(numEntries > 0, "FST should have at least one entry");
            }
        }

        [Fact]
        public void Decode_OutputSizeMatchesImageSize()
        {
            IAsIso iso = CreateAndConstruct(out FileStream fs);
            using (fs)
            {
                // Read the entire decoded image and verify total bytes
                byte[] buf = new byte[0x200000]; // 2MB buffer
                long totalRead = 0;
                int r;
                while ((r = iso.Read(buf, 0, buf.Length)) > 0)
                    totalRead += r;

                Assert.Equal(GcFullSize, totalRead);
            }
        }

        [Fact]
        public void Decode_DolPointerValid()
        {
            IAsIso iso = CreateAndConstruct(out FileStream fs);
            using (fs)
            {
                byte[] discHdr = new byte[0x440];
                iso.Read(discHdr, 0, discHdr.Length);

                uint dolOffset = discHdr.ReadUInt32B(0x420);
                uint fstOffset = discHdr.ReadUInt32B(0x424);

                // DOL offset should be within the image, past the disc header
                Assert.True(dolOffset > 0x440, "DOL offset should be past disc header");
                Assert.True(dolOffset < GcFullSize, "DOL offset should be within image");

                // DOL should start before FST (typical GC layout)
                Assert.True(dolOffset < fstOffset, "DOL typically precedes FST");
            }
        }

        [Fact]
        public void Decode_JunkGapsContainNonZeroData()
        {
            IAsIso iso = CreateAndConstruct(out FileStream fs);
            using (fs)
            {
                // Read disc header to find FST end (where first gap likely starts)
                byte[] discHdr = new byte[0x440];
                iso.Read(discHdr, 0, discHdr.Length);
                uint fstOffset = discHdr.ReadUInt32B(0x424);
                uint fstSize = discHdr.ReadUInt32B(0x428);
                uint fstSizeAligned = fstSize + (fstSize % 4 == 0 ? 0 : 4 - (fstSize % 4));

                // Read a chunk well past the last file — this is typically junk-filled gap area
                // The trailing area of a GC disc (after all files) should have junk pattern
                long trailingOffset = GcFullSize - 0x40000; // last 256KB of disc
                iso.Position = trailingOffset;
                byte[] trailingData = new byte[0x40000];
                int read = iso.Read(trailingData, 0, trailingData.Length);
                Assert.Equal(0x40000, read);

                // At least some of this should be non-zero (junk pattern)
                bool hasNonZero = false;
                for (int i = 0; i < trailingData.Length; i++)
                {
                    if (trailingData[i] != 0)
                    {
                        hasNonZero = true;
                        break;
                    }
                }
                Assert.True(hasNonZero, "Trailing disc area should contain junk pattern (non-zero bytes)");
            }
        }

        [Fact]
        public void Decode_FullImageCrcMatchesNKitHeader()
        {
            // Read the stored NKit CRC from the source file header at offset 0x208
            uint expectedCrc;
            using (FileStream fs = File.OpenRead(TestImage))
            {
                byte[] rawHdr = new byte[0x440];
                fs.Read(rawHdr, 0, rawHdr.Length);
                expectedCrc = rawHdr.ReadUInt32B(0x208);
            }

            Assert.NotEqual(0u, expectedCrc); // sanity: NKit should have a stored CRC

            // Decode the full image and compute CRC32 incrementally
            IAsIso iso = CreateAndConstruct(out FileStream fs2);
            using (fs2)
            {
                Crc crc = new Crc();
                byte[] buf = new byte[0x200000]; // 2MB read buffer
                long totalRead = 0;
                int r;
                while ((r = iso.Read(buf, 0, buf.Length)) > 0)
                {
                    crc.Sum(buf, 0, r);
                    totalRead += r;
                }

                Assert.Equal(GcFullSize, totalRead);
                Assert.Equal(expectedCrc, crc.Value);
            }
        }
    }

    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // decodes a real NKit GCZ image — excluded from the fast CI gate (run locally)
    public class NKitGczAsIsoTests
    {
        private static readonly string TestGczImage = TestPaths.Resolve(@"D:\NKitFiles\GC_Iso\GC\Sega Soccer Slam (Japan).nkit.gcz");
        private const long GcFullSize = 0x57058000L;

        static NKitGczAsIsoTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void Create_DetectsGczNKit()
        {
            byte[] header = new byte[0x400];
            using (FileStream fs = File.OpenRead(TestGczImage))
                fs.Read(header, 0, header.Length);

            // Should detect the GCZ magic (0x01C00BB1)
            IAsIso iso = NKitAsIso.Create(header);
            Assert.NotNull(iso);
        }

        [Fact]
        public void Decode_GczNKit_FullImageCrcMatches()
        {
            using (FileStream fs = File.OpenRead(TestGczImage))
            {
                byte[] header = new byte[0x400];
                fs.Read(header, 0, header.Length);
                fs.Position = 0;

                IAsIso iso = NKitAsIso.Create(header);
                Assert.NotNull(iso);

                iso.Construct(fs, true);

                // Should decode to full GC disc size
                Assert.Equal(GcFullSize, iso.Size);

                // Read the NKit CRC from the decompressed header
                // (first 0x440 of decoded output contains the disc header with NKit CRC cleared,
                //  but we need the CRC from the NKit source — read via the GCZ first)
                // Re-read the GCZ to get the NKit header's stored CRC
                fs.Position = 0;
                GczAsIso gczForCrc = new GczAsIso();
                gczForCrc.Construct(fs, true);
                byte[] nkitHdr = new byte[0x440];
                gczForCrc.Read(nkitHdr, 0, nkitHdr.Length);
                uint expectedCrc = nkitHdr.ReadUInt32B(0x208);
                gczForCrc.Dispose();

                Assert.NotEqual(0u, expectedCrc);

                // Now decode and compute CRC
                fs.Position = 0;
                IAsIso iso2 = NKitAsIso.Create(header);
                iso2.Construct(fs, true);

                Crc crc = new Crc();
                byte[] buf = new byte[0x200000];
                long totalRead = 0;
                int r;
                while ((r = iso2.Read(buf, 0, buf.Length)) > 0)
                {
                    crc.Sum(buf, 0, r);
                    totalRead += r;
                }

                Assert.Equal(GcFullSize, totalRead);
                Assert.Equal(expectedCrc, crc.Value);
            }
        }
    }

    /// <summary>
    /// Tests for NKit images with JunkFile entries (files removed and replaced with junk).
    /// XGIII has audio files that are disc junk — the NKit format stores them as zero-length
    /// with JunkFile gap encoding to restore the junk data on decode.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // full junk-file decode over a real image — excluded from the fast CI gate (run locally)
    public class NKitJunkFileTests
    {
        private const long GcFullSize = 0x57058000L;

        static NKitJunkFileTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Theory]
        [InlineData(
            @"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (USA) (9CFCAC40).nkit.iso",
            @"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (USA) (9CFCAC40).iso")]
        [InlineData(
            @"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (Europe) (En,Fr,De,Es) (9D77760D).nkit.iso",
            @"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (Europe) (En,Fr,De,Es) (9D77760D).iso")]
        public void Decode_JunkFileImage_CrcMatches(string nkitPath, string isoPath)
        {
            nkitPath = TestPaths.Resolve(nkitPath);
            isoPath = TestPaths.Resolve(isoPath);
            using (FileStream nkitFs = File.OpenRead(nkitPath))
            using (FileStream isoFs = File.OpenRead(isoPath))
            {
                byte[] header = new byte[0x400];
                nkitFs.Read(header, 0, header.Length);
                nkitFs.Position = 0;

                IAsIso iso = NKitAsIso.Create(header);
                Assert.NotNull(iso);
                iso.Construct(nkitFs, false);

                byte[] nkitBuf = new byte[0x10000];
                byte[] isoBuf = new byte[0x10000];
                long pos = 0;

                while (pos < GcFullSize)
                {
                    int toRead = (int)Math.Min(nkitBuf.Length, GcFullSize - pos);
                    int nkitRead = iso.Read(nkitBuf, 0, toRead);
                    int isoRead = isoFs.Read(isoBuf, 0, toRead);
                    Assert.Equal(isoRead, nkitRead);

                    for (int i = 0; i < nkitRead; i++)
                    {
                        if (nkitBuf[i] != isoBuf[i])
                            Assert.Fail($"Mismatch at 0x{pos + i:X8}: ISO=0x{isoBuf[i]:X2} NKit=0x{nkitBuf[i]:X2}");
                    }
                    pos += nkitRead;
                }
            }
        }
    }

    /// <summary>
    /// Batch CRC validation for NKit images. Add any .nkit.iso or .nkit.gcz path below.
    /// Each image is decoded fully and its CRC32 is verified against the stored NKit header CRC.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // batch-decodes real NKit images + CRC-checks each (the heaviest class) — excluded from the fast CI gate (run locally)
    public class NKitBatchCrcTests
    {
        private const long GcFullSize = 0x57058000L;
        private readonly ITestOutputHelper _output;

        static NKitBatchCrcTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public NKitBatchCrcTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(@"D:\NKitFiles\GC_Iso\GC\Super Bubble Pop (USA).nkit.iso")]
        [InlineData(@"D:\NKitFiles\GC_Iso\GC\Sega Soccer Slam (Japan).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (USA) (9CFCAC40).nkit.iso")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (Europe) (En,Fr,De,Es) (9D77760D).nkit.iso")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\2 Games in 1 - Nickelodeon SpongeBob Schwammkopf - Der Film + Nickelodeon Tak 2 - Der Stab der Traeume (Germany) (Disc 2).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Advance Game Port (USA) (Unl) (Rev 1).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Advance Game Port (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Advance Game Port (USA, Europe) (Unl) (Rev 2).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Aggressive Inline (Europe) (En,Fr,De,Es).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\All-Star Baseball 2002 (USA).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Backyard Baseball (USA).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Battalion Wars (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Big Mutha Truckers (USA).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Bonus Powersaves (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\CD avec les Codes Action Replay Exclusivement pour le Jeu The Legend of Zelda - The Wind Waker (France) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\CD avec les Codes Exclusifs et Inedits pour le Jeu Metroid Prime (France) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\CD Exclusif avec les Codes pour les Jeux Resident Evil et Resident Evil Zero (France) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Dairantou Smash Brothers DX (Japan) (Taikenban).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Dakar 2 (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\FreeLoader for GameCube (Europe) (Unl) (Version 1.06).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\FreeLoader for GameCube (Europe) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\GoldenEye - Agente Corrupto (Spain) (Disc 2).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Interactive Multi-Game Demo Disc Version 25 (USA).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Looney Tunes - Back in Action (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Max Drive (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Max Drive Pro (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\MaxPlay (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\MaxPlay Volume 01 (Europe) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Micro Machines (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Monster House (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Need for Speed - Underground (Europe) (Alt).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Nickelodeon SpongeBob SquarePants - The Movie (Europe) (Fr,Nl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Nintendo GameCube Preview Disc - May 2003 (USA).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Pikmin 2 (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Powerpuff Girls, The - Relish Rampage - Pickled Edition (Europe) (En,Fr,De,Es).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Resident Evil 4 (USA) (Disc 2).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Resident Evil 4 (USA) (Preview Disc).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\SRS - Street Racing Syndicate (USA).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Star Wars - Rogue Squadron II (Japan) (Jitsuen-you Sample).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Star Wars - Rogue Squadron III - Rebel Strike (USA) (Limited Edition Preview Disc).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Terminator 3 - The Redemption (Europe) (En,Fr,De,Es,It).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Totsugeki!! Famicom Wars (Japan).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Tower of Druaga, The (Japan).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Cheats for Use with Metroid Prime (UK) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Cheats for Use with The Legend of Zelda - The Wind Waker (UK) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Cheats fuer Splinter Cell (Germany) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Cheats fuer The Legend of Zelda - The Wind Waker (Germany) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Codes for Use with Animal Crossing (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Codes for Use with Metroid Prime (USA) (Unl).nkit.gcz")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\Skipped\Okay\Ultimate Codes for Use with The Legend of Zelda - The Wind Waker (USA) (Unl).nkit.gcz")]
        public void Decode_CrcMatchesStoredValue(string nkitPath)
        {
            nkitPath = TestPaths.Resolve(nkitPath);
            string name = Path.GetFileName(nkitPath);
            _output.WriteLine($"Testing: {name}");

            byte[] header = new byte[0x400];
            using (FileStream fs = File.OpenRead(nkitPath))
                fs.Read(header, 0, header.Length);

            bool isGcz = header.ReadUInt32L(0) == 0xB10BC001;

            // Determine the stored NKit CRC
            uint storedCrc;
            if (isGcz)
            {
                using (FileStream fs = File.OpenRead(nkitPath))
                {
                    GczAsIso gcz = new GczAsIso();
                    gcz.Construct(fs, true);
                    byte[] nkitHdr = new byte[0x440];
                    gcz.Read(nkitHdr, 0, nkitHdr.Length);
                    storedCrc = nkitHdr.ReadUInt32B(0x208);
                    gcz.Dispose();
                }
            }
            else
            {
                storedCrc = header.ReadUInt32B(0x208);
            }
            Assert.NotEqual(0u, storedCrc);

            // Cached mode — direct CRC of decoded output
            uint cachedCrc = DecodeCrc(nkitPath, header, out long cachedSize);
            _output.WriteLine($"  Cached:    size=0x{cachedSize:X}  CRC=0x{cachedCrc:X8}");
            Assert.Equal(storedCrc, cachedCrc);

            // Streaming (non-caching, forward-only) mode. The body is emitted with the
            // pre-patch FST; overlaying HeaderBlock reconstructs the final image.
            // Note: raw .nkit.iso sources support the fine-grained sequential reads the
            // streaming decoder makes. GCZ sources decompress block-by-block and are
            // validated via cached mode; streaming is verified for raw sources here.
            if (!isGcz)
            {
                uint streamCrc = DecodeStreamingReconstructedCrc(nkitPath, header, out long streamSize);
                _output.WriteLine($"  Streaming: size=0x{streamSize:X}  CRC=0x{streamCrc:X8}");
                Assert.Equal(storedCrc, streamCrc);
                Assert.Equal(cachedCrc, streamCrc);
                _output.WriteLine($"  Result: PASS (stored=0x{storedCrc:X8}, cached==streaming)");
            }
            else
            {
                _output.WriteLine($"  Streaming: skipped for GCZ (validated via cached)");
                _output.WriteLine($"  Result: PASS (stored=0x{storedCrc:X8}, cached)");
            }
        }

        private static uint DecodeCrc(string nkitPath, byte[] header, out long size)
        {
            using (FileStream fs = File.OpenRead(nkitPath))
            {
                IAsIso iso = NKitAsIso.Create(header, false);
                Assert.NotNull(iso);
                iso.Construct(fs, false);
                size = iso.Size;

                Crc crc = new Crc();
                byte[] buf = new byte[0x200000];
                long totalRead = 0;
                int r;
                while ((r = iso.Read(buf, 0, buf.Length)) > 0)
                {
                    crc.Sum(buf, 0, r);
                    totalRead += r;
                }
                Assert.Equal(iso.Size, totalRead);
                return crc.Value;
            }
        }

        private static uint DecodeStreamingReconstructedCrc(string nkitPath, byte[] header, out long size)
        {
            using (FileStream fs = File.OpenRead(nkitPath))
            {
                NKitAsIso iso = (NKitAsIso)NKitAsIso.Create(header, true);
                Assert.NotNull(iso);
                iso.Construct(fs, false);
                size = iso.Size;

                byte[] full = new byte[size];
                long pos = 0;
                byte[] buf = new byte[0x200000];
                int r;
                while ((r = iso.Read(buf, 0, buf.Length)) > 0)
                {
                    Array.Copy(buf, 0, full, pos, r);
                    pos += r;
                }
                Assert.Equal(size, pos);

                // Overlay the patched header block (header + hdrToFst + FST) at the start
                byte[] hb = iso.HeaderBlock;
                Assert.NotNull(hb);
                Array.Copy(hb, 0, full, 0, hb.Length);

                return Crc.Compute(full);
            }
        }
    }

    /// <summary>
    /// Validates the non-caching streaming mode: decodes forward without caching the
    /// full source, producing byte-identical output to cached mode, and exposes the
    /// patched header/FST block after the read completes.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // streams a real image in fix mode — excluded from the fast CI gate (run locally)
    public class NKitStreamingModeTests
    {
        private readonly ITestOutputHelper _output;

        static NKitStreamingModeTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public NKitStreamingModeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(@"D:\NKitFiles\GC_Iso\GC\Super Bubble Pop (USA).nkit.iso")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (USA) (9CFCAC40).nkit.iso")]
        [InlineData(@"D:\NKitFiles\GC_Iso\QuickTest\ScanFailGC\XGIII - Extreme G Racing (Europe) (En,Fr,De,Es) (9D77760D).nkit.iso")]
        public void StreamingMode_ReconstructsCorrectImage(string nkitPath)
        {
            nkitPath = TestPaths.Resolve(nkitPath);
            string name = Path.GetFileName(nkitPath);
            _output.WriteLine($"Testing forward-only streaming: {name}");

            byte[] header = new byte[0x400];
            using (FileStream fs = File.OpenRead(nkitPath))
                fs.Read(header, 0, header.Length);

            bool isGcz = header.ReadUInt32L(0) == 0xB10BC001;

            // Stored NKit CRC
            uint storedCrc;
            if (isGcz)
            {
                using (FileStream fs = File.OpenRead(nkitPath))
                {
                    GczAsIso gcz = new GczAsIso();
                    gcz.Construct(fs, true);
                    byte[] nkitHdr = new byte[0x440];
                    gcz.Read(nkitHdr, 0, nkitHdr.Length);
                    storedCrc = nkitHdr.ReadUInt32B(0x208);
                    gcz.Dispose();
                }
            }
            else
            {
                storedCrc = header.ReadUInt32B(0x208);
            }

            // Streaming decode: forward-only. The body is emitted with the FST/header
            // holding compact (pre-patch) values; the caller overlays HeaderBlock at
            // the start once the read completes. Reconstruct that and verify CRC.
            byte[] full;
            byte[] headerBlock;
            long size;
            using (FileStream fs = File.OpenRead(nkitPath))
            {
                NKitAsIso iso = (NKitAsIso)NKitAsIso.Create(header, true);
                Assert.True(iso.IsStreaming);
                iso.Construct(fs, false);
                size = iso.Size;
                full = new byte[size];
                long pos = 0;
                byte[] buf = new byte[0x200000];
                int r;
                while ((r = iso.Read(buf, 0, buf.Length)) > 0)
                {
                    Array.Copy(buf, 0, full, pos, r);
                    pos += r;
                }
                Assert.Equal(size, pos);
                headerBlock = iso.HeaderBlock;
            }

            Assert.NotNull(headerBlock);

            // Overlay the patched header block over the start of the streamed body
            Array.Copy(headerBlock, 0, full, 0, headerBlock.Length);

            uint crc = Crc.Compute(full);
            Assert.Equal(storedCrc, crc);
            _output.WriteLine($"  Reconstructed CRC 0x{crc:X8} == stored 0x{storedCrc:X8}, headerBlock=0x{headerBlock.Length:X} bytes: PASS");
        }
    }

    /// <summary>
    /// Structural validation for the Wii NKit decoder. Because hash generation and
    /// encryption are deferred to the parallel stage, the decoded output does NOT match
    /// the stored (encrypted) image CRC. Instead we validate the reconstructed structure:
    /// correct size, disc header magic, a valid partition table, each partition header
    /// parsing, and the (decrypted, FS-space) partition FST parsing to files.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // full Wii partition/FST decode over a real image — excluded from the fast CI gate (run locally)
    public class NKitWiiStructureTests
    {
        private readonly ITestOutputHelper _output;

        static NKitWiiStructureTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public NKitWiiStructureTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Wii NKit multi-partition decode: validate structurally (size, disc magic, NKit fields
        // cleared, partition table valid, full decode runs to Size bytes without error).
        [Theory]
        [InlineData(@"D:\NKitFiles\Wii_Formats\Super Mario All-Stars (Europe).nkit.iso")]
        [InlineData(@"D:\NKitFiles\Wii_Formats\Super Mario All-Stars (Europe)_NoUpdate.nkit.iso")]
        public void Decode_WiiStructureIsValid(string nkitPath)
        {
            nkitPath = TestPaths.Resolve(nkitPath);
            if (!File.Exists(nkitPath))
            {
                _output.WriteLine($"SKIP (missing): {nkitPath}");
                return;
            }

            string name = Path.GetFileName(nkitPath);
            _output.WriteLine($"Testing Wii structure: {name}");

            byte[] header = new byte[0x400];
            using (FileStream fs = File.OpenRead(nkitPath))
                fs.Read(header, 0, header.Length);

            using (FileStream fs = File.OpenRead(nkitPath))
            {
                IAsIso iso = NKitAsIso.Create(header, false);
                Assert.NotNull(iso);
                iso.Construct(fs, false);

                long size = iso.Size;
                _output.WriteLine($"  Decoded size: 0x{size:X}");
                Assert.True(size > 0);

                // Read just the disc header region (0x50000) — enough for structural checks.
                // Full image can be multi-GB so we don't materialize it.
                byte[] discHdr = new byte[0x50000];
                iso.Position = 0;
                int hpos = 0;
                while (hpos < discHdr.Length)
                {
                    int hr = iso.Read(discHdr, hpos, discHdr.Length - hpos);
                    if (hr <= 0) break;
                    hpos += hr;
                }
                Assert.Equal(discHdr.Length, hpos);

                // Wii disc magic at 0x18
                Assert.Equal(0x5d1c9ea3u, discHdr.ReadUInt32B(0x18));

                // NKit fields cleared in disc header
                Assert.NotEqual("NKIT", discHdr.ReadString(0x200, 4));

                // Partition table at 0x40000 — at least one partition
                int partitionCount = 0;
                for (int t = 0; t < 4; t++)
                {
                    uint c = discHdr.ReadUInt32B(0x40000 + (t * 8));
                    partitionCount += (int)c;
                }
                _output.WriteLine($"  Partitions: {partitionCount}");
                Assert.True(partitionCount > 0);

                // Stream the full image to verify the whole decode runs without error
                // and produces exactly Size bytes (no allocation of the full array).
                iso.Position = 0;
                long total = 0;
                byte[] buf = new byte[0x200000];
                int r;
                while ((r = iso.Read(buf, 0, buf.Length)) > 0)
                    total += r;
                Assert.Equal(size, total);

                _output.WriteLine("  Result: PASS (size, magic, NKit cleared, partition table valid, full decode ran)");
            }
        }

        // [Fact] driver over both Wii images. The in-process xUnit v3 runner used in this repo
        // does not enumerate [Theory] InlineData cases, so this Fact ensures the multi-partition
        // Wii decode is exercised (both the update+game and the no-update image) by a single
        // discoverable test. Failures propagate (missing files are skipped inside the method).
        [Fact]
        public void Decode_WiiStructureIsValid_BothImages()
        {
            Decode_WiiStructureIsValid(TestPaths.Resolve(@"D:\NKitFiles\Wii_Formats\Super Mario All-Stars (Europe).nkit.iso"));
            Decode_WiiStructureIsValid(TestPaths.Resolve(@"D:\NKitFiles\Wii_Formats\Super Mario All-Stars (Europe)_NoUpdate.nkit.iso"));
        }

        // GCZ-compressed NKit Wii image: exercises the GCZ decompressor feeding the Wii
        // on-demand decoder.
        [Fact]
        public void Decode_WiiStructureIsValid_Gcz() => Decode_WiiStructureIsValid(TestPaths.Resolve(@"D:\NKitFiles\Wii_Iso\101-in-1 Party Megamix Wii (Europe) (En,Fr,De,Es,It,Nl) (Rev 1).nkit.gcz"));






    }
}
using Nanook.NKit;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.IO;
using System.Text;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class WiiTests
    {
        [Theory]
        [InlineData("AAAA", 0, 0x200000)]
        [InlineData("AAAA", 1, 0x200000)]
        [InlineData("AAAA", 0, 0x000000)]
        [InlineData("AAAA", 0, 0x8000)]
        [InlineData("AAA1", 0, 0x200000)]
        [InlineData("AAA2", 1, 0x100000)]
        [InlineData("XXXX", 0, 0x200000)]
        [InlineData("1234", 1, 0x200000)]
        public void JunkWindBackTests(string discId, int discNo, long fsOffset)
        {
            byte[] junk = new byte[NJunk.JunkBlockSize];
            byte[] junk2 = new byte[NJunk.JunkBlockSize];
            uint[] baseInts = null;
            //create the junk from disc seed
            NJunk.Fill(Encoding.ASCII.GetBytes(discId), discNo, 0, WiiConsts.FullSizeGameCube, fsOffset, junk);

            //wind it back to seed ints
            NJunk.Shrink(junk, 0, fsOffset, NJunk.JunkBlockSize, ref baseInts, false);

            //only first 17 are required, blank the rest
            for (int i = 17; i < baseInts.Length; i++)
                baseInts[i] = 0;

            NJunk.Expand(baseInts, junk2, 0, NJunk.JunkBlockSize);

            //compare junk matches
            Assert.True(junk.Equals(0, junk2, 0, WiiConsts.WiiSectorSize)); //junk blocks are 0x8000 bytes
        }

        [Fact]
        public void SigningValidationTest()
        {
            byte[] hdr = new byte[0x200000];
            TestImageBuilder.WiiHdr.CopyTo(hdr, 0);
            hdr.Write(0x8000, _H3);

            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, null, WiiConsts.PublicKeyExponent, hdr, 0);
            SignedStatus valid = ph.CertValidator.Validate(true, false);
            Assert.Equal(SignedStatus.Valid, valid);
        }

        [Fact]
        public void SigningValidationBadTicketTest()
        {
            byte[] hdr = new byte[0x200000];
            TestImageBuilder.WiiHdr.CopyTo(hdr, 0);
            hdr.Write(0x8000, _H3);
            hdr[0x1B0] = 0xFF;

            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, null, WiiConsts.PublicKeyExponent, hdr, 0);
            SignedStatus valid = ph.CertValidator.Validate(true, false);
            Assert.Equal(SignedStatus.InvalidTicket, valid);
        }

        [Fact]
        public void SigningValidationBadTmdHashTest()
        {
            byte[] hdr = new byte[0x200000];
            TestImageBuilder.WiiHdr.CopyTo(hdr, 0);
            hdr.Write(0x8000, _H3);
            hdr[0x4BE] = 0xFF;

            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, null, WiiConsts.PublicKeyExponent, hdr, 0);
            SignedStatus valid = ph.CertValidator.Validate(true, false);
            Assert.Equal(SignedStatus.InvalidTmd, valid);
        }

        [Fact]
        public void SigningValidationBadTmdTest()
        {
            byte[] hdr = new byte[0x200000];
            TestImageBuilder.WiiHdr.CopyTo(hdr, 0);
            hdr.Write(0x8000, _H3);
            hdr[0x450] = 0xFF;

            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, null, WiiConsts.PublicKeyExponent, hdr, 0);
            SignedStatus valid = ph.CertValidator.Validate(true, false);
            Assert.Equal(SignedStatus.InvalidTmd, valid);
        }


        [Fact]
        public void SigningValidationBadH3Test()
        {
            byte[] hdr = new byte[0x200000];
            TestImageBuilder.WiiHdr.CopyTo(hdr, 0);

            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, null, WiiConsts.PublicKeyExponent, hdr, 0);
            SignedStatus valid = ph.CertValidator.Validate(true, false);
            Assert.Equal(SignedStatus.InvalidH3Hash, valid);
        }

        [Fact]
        public void SigningValidationFakeTest()
        {
            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(".", $"{nameof(SigningValidationFakeTest)}_{Guid.NewGuid():N}"));
            TestImageBuilder.CreateImageWiiBasic(Path.Combine(basePath.FullName, "WiiBasic.iso"));

            long baseOffset = 0x50000;

            using (FileStream fs = new FileStream(Path.Combine(basePath.FullName, "WiiBasic.iso"), FileMode.Open))
            {
                fs.Position = baseOffset;
                PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, null, WiiConsts.PublicKeyExponent, fs.ReadBytes(0x20000), 0);
                SignedStatus valid = ph.CertValidator.Validate(true, false);
                Assert.Equal(SignedStatus.FakeSigned, valid);
            }

            basePath.Delete(true);
        }
        private static byte[] _H3 = new byte[] { 0xAF, 0xD0, 0xDA, 0x11, 0xB8, 0x70, 0xD4, 0xDA, 0x8B, 0xE7, 0x1B, 0xA2, 0x98, 0x4B, 0x92, 0x06, 0x3C, 0x2F, 0xB2, 0x3E };
    }
}
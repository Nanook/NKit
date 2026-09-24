using Nanook.NKit;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class ContainerTests
    {

        [Fact]
        public void TestWiiRvz()
        {
            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(".", $"{nameof(TestWiiRvz)}_{Guid.NewGuid():N}"));

            string isoPath = Path.Combine(basePath.FullName, "WiiBasic.iso");
            string isoCopyPath = Path.Combine(basePath.FullName, "WiiBasic.iso.copy");
            string rvzPath = Path.Combine(basePath.FullName, "WiiBasic.rvz");
            TestImageBuilder.CreateImageWiiBasic(isoPath);
            TestImageBuilder.ConvertImage(isoPath, "rvz:zstd:19:128k:4");

            using (BufferStream stream = new BufferStream(new FileStream(rvzPath, FileMode.Open, FileAccess.Read, FileShare.Read, 0x200000, false)))
            {
                byte[] id = new byte[4];
                stream.Read(id, 0, -4);
                IAsIso iso = RvzAsIso.Create(id);

                stream.Seek(0, SeekOrigin.Begin);
                readAll(stream, id, iso, isoCopyPath);
            }

            //rvz returns data decrypted (encryption performed in parallel process sections)
            Assert.Equal(0x45D16231u, Crc.Compute(File.ReadAllBytes(isoPath))); //encrypted CRC
            Assert.Equal(0xA0CCB444u, Crc.Compute(File.ReadAllBytes(isoCopyPath))); //decrypted CRC

            basePath.Delete(true);
        }

        private static void readAll(BufferStream stream, byte[] id, IAsIso iso, string writeFile)
        {
            int requestedBuffSize = iso.Construct(stream, iso is WiaAsIso);

            int r;
            using (Stream s = writeFile == null ? Stream.Null : File.OpenWrite(writeFile))
            {
                byte[] buf = new byte[0x200000];
                long pos = 0;
                while (pos < iso.Size)
                {
                    r = iso.Read(buf, 0, (int)Math.Min((int)buf.Length, iso.Size - iso.Position));
                    s.Write(buf, 0, r);
                    pos += (long)r; //first 4 bytes of plain iso format has already been read
                }
            }
        }
    }
}
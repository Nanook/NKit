using Nanook.NKit;
using System;
using System.IO;
using System.IO.Compression;

namespace NKit.Tests
{
    internal class TestUtils
    {
        internal static void FillBlock(long startValue, byte[] data, int blockSize, int blockFsOffset, int blockFsSize, int blocks)
        {
            uint x = (uint)startValue;
            for (int b = 0; b < blocks; b++)
            {
                int offset = b * blockSize;
                for (int i = 0; i < blockFsSize; i += 4)
                {
                    data.WriteUInt32B(offset + blockFsOffset + i, x);
                    x += 4;
                }
            }
        }

        internal static FileInfo[] CreateZip(string filepath, int splitParts, int fileSplitParts, int fileSplitPartSize, out string[] filenames)
        {
            byte[][] parts = CreateZip(splitParts, fileSplitParts, fileSplitPartSize, out filenames);
            FileInfo[] infos = new FileInfo[parts.Length];
            int i = 0;
            foreach (byte[] part in parts)
            {
                infos[i] = new FileInfo($"{filepath}.{i + 1:D3}");
                File.WriteAllBytes(infos[i].FullName, part);
                i++;
            }
            return infos;
        }

        /// <summary>
        /// Create a file with offsets written in to it. Split it and zip it. Split the zip.
        /// </summary>
        internal static byte[][] CreateZip(int splitParts, int fileSplitParts, int fileSplitPartSize, out string[] filenames)
        {
            byte[] data = new byte[fileSplitParts * fileSplitPartSize];
            byte[][] ret = new byte[splitParts][];
            filenames = new string[fileSplitParts];
            byte[] buffer = new byte[fileSplitPartSize];
            FillBlock(0, data, data.Length, 0, data.Length, 1); //just fill as one large block
            using (MemoryStream inData = new MemoryStream(data))
            {

                using (MemoryStream ms = new MemoryStream())
                {
                    using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        for (int i = 0; i < fileSplitParts; i++)
                        {
                            filenames[i] = "Part." + (i + 1).ToString().PadLeft(3, '0');
                            ZipArchiveEntry zipItem = zip.CreateEntry(filenames[i]);
                            using (Stream entryStream = zipItem.Open())
                            {
                                int sz = inData.Read(buffer, 0, buffer.Length);
                                entryStream.Write(buffer, 0, sz);
                            }
                        }
                    }
                    ms.Position = 0;

                    for (int i = 0; i < splitParts; i++)
                    {
                        ret[i] = new byte[Math.Min(ms.Length - ms.Position, (ms.Length / splitParts) + (ms.Length % splitParts != 0 ? 1 : 0))];
                        ms.Read(ret[i], 0, ret[i].Length);
                    }
                }
            }
            return ret;
        }


        internal static void RecurseDirectory(DirectoryInfo p, Action<FileInfo> file)
        {
            foreach (FileInfo fi in p.GetFiles())
                file(fi);
            foreach (DirectoryInfo di in p.GetDirectories())
                RecurseDirectory(di, file);
        }
    }
}
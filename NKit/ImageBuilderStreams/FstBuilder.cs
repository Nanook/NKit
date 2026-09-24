using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Builder
{
    internal class FstItem
    {
        public string Name;
        public FstDirectory Parent;
        public int Index;
        internal uint Val1;
        internal uint Val2;
        internal uint Val3;
        internal byte[] NameBytes => Encoding.GetEncoding("Shift-JIS").GetBytes(Name ?? "");
    }

    internal class FstDirectory : FstItem
    {
        private readonly List<FstDirectory> _directories;
        private readonly List<FstFile> _files;
        private readonly List<FstItem> _items;
        public IEnumerable<FstDirectory> Directories => _directories;
        public IEnumerable<FstFile> Files => _files;
        public IEnumerable<FstItem> Items => _items;
        internal FstDirectory()
        {
            _directories = new List<FstDirectory>();
            _files = new List<FstFile>();
            _items = new List<FstItem>();
        }

        public void AddFile(long offset, string name, string path, long size)
        {
            FstDirectory d = this;
            path = path?.Trim('/');
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string s in path.Split('/'))
                {
                    FstDirectory di = d.Directories.FirstOrDefault(a => a.Name == s);
                    if (di == null)
                        di = d.AddDirectory(s);

                    d = di;
                }
            }
            d.AddFile(offset, name, size);
        }

        public void AddFile(long offset, string name, long size)
        {
            _files.Add(new FstFile() { Name = name, Offset = offset, Size = size, Parent = this });
            _items.Add(_files.Last());
        }

        public FstDirectory AddDirectory(string name)
        {
            FstDirectory d = new FstDirectory() { Name = name, Parent = this };
            _directories.Add(d);
            _items.Add(_directories.Last());
            return d;
        }
    }
    internal class FstFile : FstItem
    {
        public long Size;
        public long Offset;
    }

    internal class FstBuilder : FstDirectory
    {
        public FstBuilder() : base()
        {
        }

        public static FstBuilder BuilderFromPath(string path)
        {
            FstBuilder fst = new FstBuilder();
            recursePath(new DirectoryInfo(path), fst);
            return fst;
        }

        private static void recursePath(DirectoryInfo d, FstDirectory fstD)
        {
            foreach (FileInfo fi in d.GetFiles())
                fstD.AddFile(0, fi.Name, fi.Length);

            foreach (DirectoryInfo di in d.GetDirectories())
                recursePath(di, fstD.AddDirectory(di.Name));
        }


        public byte[] ToArray(int nulls, int shift)
        {
            List<FstItem> files = new List<FstItem>();
            int size = 0;

            using (MemoryStream write = new MemoryStream(1024))
            {
                using (MemoryStream ms = new MemoryStream(1024))
                {
                    recurseToFst(this, files, ms, shift); //list in grouped order
                    size += (int)ms.Length;
                    ms.Position = 0;

                    foreach (FstItem f in files)
                    {
                        writeUInt32B(write, f.Val1);
                        writeUInt32B(write, f.Val2);
                        writeUInt32B(write, f.Val3);
                        size += 3 * 4;
                    }
                    ms.CopyTo(write);
                }

                if (write.Length % 4 != 0)
                    ByteStream.Zeros.Copy(write, 4 - (int)(write.Length % 4));

                if (nulls != 0)
                    ByteStream.Zeros.Copy(write, nulls);
                return write.ToArray();
            }
        }

        private void writeUInt32B(Stream stream, uint value) => stream.Write(BitConverter.GetBytes(bigEndian(value)), 0, 4);
        private uint bigEndian(uint x)
        {
            if (!BitConverter.IsLittleEndian) //don't swap on big endian CPUs
                return x;

            x = (x >> 16) | (x << 16);
            return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
        }

        private void recurseToFst(FstDirectory p, List<FstItem> files, MemoryStream names, int shift)
        {
            p.Index = files.Count;
            files.Add(p);
            p.Val1 = (0x01 << 24) | (uint)names.Position; //name offset to be added
            byte[] nm = p.NameBytes;
            names.Write(nm, 0, nm.Length);
            if (nm.Length != 0)
                names.WriteByte(0x00);

            //mix folders and files ordered case insensitive
            foreach (FstItem i in p.Items.OrderBy(a => (a.Name ?? "").ToLowerInvariant()))
            {
                i.Index = files.Count;
                i.Val1 = (uint)names.Position; //0 based offset temp
                if (i is FstFile)
                {
                    i.Val2 = (uint)((FstFile)i).Offset >> shift;
                    i.Val3 = (uint)((FstFile)i).Size;
                    files.Add(i);
                    nm = i.NameBytes;
                    names.Write(nm, 0, nm.Length);
                    if (nm.Length != 0)
                        names.WriteByte(0x00);
                }
                else
                    recurseToFst(i as FstDirectory, files, names, shift);
            }

            if (p.Parent != null)
                p.Val2 = (uint)p.Parent?.Index; //parent item

            p.Val3 = (uint)files.Count;  //total number of contained items
        }
    }

}
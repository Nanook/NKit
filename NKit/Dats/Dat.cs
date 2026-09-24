using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Nanook.NKit.Dats
{
    public class Dat
    {
        public static Dat ReadLogixDat(DatCollectionType collectionType, SystemType systemType, Stream dat, string fileName) => new Dat(collectionType, systemType, dat, fileName);

        private Dat(DatCollectionType collectionType, SystemType systemType, Stream dat, string fileName)
        {
            this.CollectionType = collectionType;
            this.SystemType = systemType;
            this.FileName = fileName;
            try
            {
                //Logiqx format only for now
                Items = new List<DatItem>();
                XDocument matchDoc = XDocument.Load(dat);
                XmlNamespaceManager namespaceManager = new XmlNamespaceManager(new NameTable());
                namespaceManager.AddNamespace("empty", "http://demo.com/2011/demo-schema");

                Name = matchDoc.Root.XPathSelectElement("header/name").Value;

                foreach (XElement e in matchDoc.Root.XPathSelectElements("machine|game", namespaceManager))
                {
                    IEnumerable<DatItemPart> roms = e.Elements("rom").Select(rom =>
                    {
                        uint crc = rom.Attribute("crc") == null ? 0 : uint.Parse(rom.Attribute("crc").Value, NumberStyles.HexNumber);
                        byte[] md5 = rom.Attribute("md5")?.Value.HexToBytes();
                        byte[] sha1 = rom.Attribute("sha1")?.Value.HexToBytes();
                        long size = rom.Attribute("size").Value == "" ? 0 : long.Parse(rom.Attribute("size").Value);
                        return new DatItemPart(rom.Attribute("name").Value, crc, (md5?.Length ?? 0) == 0 ? null : md5, (sha1?.Length ?? 0) == 0 ? null : sha1, size);
                    });
                    Items.Add(new DatItem(e.Element("description").Value, roms));
                }
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Dat Read Error - '{fileName ?? ""}'");
            }
        }

        public string Name { get; set; }
        public List<DatItem> Items { get; set; }
        public DatCollectionType CollectionType { get; }
        public SystemType SystemType { get; }
        public string FileName { get; internal set; }

        internal DatItem GetItem(string name) => Items.FirstOrDefault(a => string.Compare(a.Name, name, StringComparison.OrdinalIgnoreCase) == 0);

        public override string ToString() => $"[{CollectionType}/{SystemType}] {FileName} ({Items.Count} items)";

    }

    public class DatItem : IParts
    {
        //private uint? _crc;
        private long? _size;
        private DatItemPart[] _bins;

        internal DatItem(string name, IEnumerable<DatItemPart> parts)
        {
            Name = name;
            Parts = parts.ToArray(); //.OrderBy(a => a.FileName).ToArray();
            _bins = null;
            foreach (DatItemPart dip in parts)
                dip.Parent = this;
            //_crc = null;
            _size = null;
            _bins = null;
            setChecksums();
        }

        private void setChecksums()
        {
            //if (_crc == null && Parts != null)
            if (Parts != null)
            {
                //_crc = 0;
                _size = 0;
                if (Parts.Length == 0)
                    return;
                //if (Parts.Length == 1)
                //{
                //    //Md5 = Parts[0].Md5;
                //    //Sha1 = Parts[0].Sha1;
                //    //_crc = Parts[0].Crc;
                //    _size = Parts[0].Size;
                //    return;
                //}
                _bins = Parts.Where(a => !SourceFiles._NonDataKnownExts.Any(b => string.Compare(b, Path.GetExtension(a.FileName), true) == 0)).ToArray();
                if (_bins.Length == 0)
                    return;
                //if (_bins.Length == 1)
                //{
                //    //Md5 = _bins[0].Md5;
                //    //Sha1 = _bins[0].Sha1;
                //    //_crc = _bins[0].Crc;
                //    _size = _bins[0].Size;
                //}
                //_crc = _bins[0].Crc;
                _size = _bins[0].Size;
                for (int x = 1; x < _bins.Length; x++)
                {
                    //    _crc = ~Nanook.NKit.Crc.Combine(~Crc, ~_bins[x].Crc, _bins[x].Size);
                    _size += _bins[x].Size;
                }
            }
        }
        public bool IsMultiPart => (Bins?.Length ?? 0) > 1;
        public DatItemPart[] Bins
        {
            get
            {
                if (_bins == null)
                    setChecksums();
                return _bins;
            }
        }

        //public bool IsMatch(IParts find, IEnumerable<byte[]> checksums)
        //{
        //    bool match = true;
        //    int i = 0;
        //    bool isMd5 = checksumType == ChecksumType.Md5;
        //    bool isSha1 = !isMd5 && checksumType == ChecksumType.Sha1;
        //    foreach (byte[] chk in checksums)
        //    {
        //        if (isMd5)
        //            match = this.Bins[i].Checksums.Md5.Equals(0, chk, 0, chk.Length);
        //        else if (isSha1)
        //            match = this.Bins[i].Checksums.Sha1.Equals(0, chk, 0, chk.Length);
        //        i++;
        //        if (!match)
        //            break;
        //    }
        //    return match && i == this.Bins.Length;
        //}

        public string Name { get; }
        public string FileName => Parts[0].FileName;
        public string FileNameExt => Parts[0].FileNameExt;
        public long Size
        {
            get
            {
                if (_size == null)
                    setChecksums();
                return _size ?? 0;
            }
        }
        public ChecksumType? GetHashType(bool incXxHash, out bool hasCrc)
        {
            if (Bins != null)
            {
                hasCrc = Bins.All(a => a.Checksums.HasCrc);
                if (incXxHash && Bins.All(a => a.Checksums.Exists(ChecksumType.XxHash)))
                    return ChecksumType.XxHash;
                if (Bins.All(a => a.Checksums.Exists(ChecksumType.Md5)))
                    return ChecksumType.Md5;
                if (Bins.All(a => a.Checksums.Exists(ChecksumType.Sha1)))
                    return ChecksumType.Sha1;
            }
            else
                hasCrc = false;
            return null;
        }
        public DatItemPart[] Parts { get; }

        public int Length => Bins.Length;

        public IPart this[int index] => Bins == null || index >= Bins.Length ? null : Bins[index];

        public IEnumerator<IPart> GetEnumerator() => Bins.Cast<IPart>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Bins.GetEnumerator();

        public override string ToString() => string.Format("{0} [Bins: {1}]", Name, Bins.Length);
    }

    public class DatItemPart : IPart
    {
        internal DatItemPart(string fileName, uint crc, byte[] md5, byte[] sha1, long size)
        {
            FileName = fileName;
            string ext;
            SourceFiles.GetFileNameParts(fileName, out _, out _, out _, out ext);
            this.Checksums = new Checksums()
            {
                Crc = crc,
                Md5 = md5,
                Sha1 = sha1
            };
            FileNameExt = ext;
            Size = size;
        }

        public byte[] this[ChecksumType index] => this.Checksums[index];

        public DatItem Parent { get; internal set; }
        public string FileName { get; }
        public string FileNameExt { get; }
        public long Size { get; }
        public Checksums Checksums { get; }

        public override string ToString() => string.Format("{0} [{1}]", FileName, this.Checksums.ToString(true));
    }
}
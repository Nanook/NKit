using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    internal class ImageDataStore
    {
        private List<Tuple<byte[], bool>> _data; //indexed data items
        private Dictionary<ulong, int> _hashes; //xxHash lookup to index
        private SortedList<long, int> _offsets; //image offsets to index

        public ImageDataStore()
        {
            _data = new List<Tuple<byte[], bool>>();
            _hashes = new Dictionary<ulong, int>();
            _offsets = new SortedList<long, int>();
        }

        public int Items => _offsets.Count;

        public void AddData(long imageOffset, byte[] data, int offset, int size)
        {
            XXHash64 xx = XXHash64.Create();
            using (Stream strm = new CryptoStream(Stream.Null, xx, CryptoStreamMode.Write))
                strm.Write(data, offset, size); //test before creating a copy
            ulong hash = xx.HashUInt64;

            int idx;
            if (!_hashes.TryGetValue(hash, out idx))
            {
                idx = _data.Count;
                _hashes.Add(hash, idx);
                _data.Add(new Tuple<byte[], bool>(data.Read(offset, size), false)); //copy
            }
            _offsets.Add(imageOffset, idx);
        }

        public void ProcessToSectionLookup(Scan scan, Action<ScanSection, int, bool, byte[]> process)
        {
            bool hasSection = true;

            using (IEnumerator<ScanSection> sections = scan.Areas.SelectMany(a => a.Sections).GetEnumerator())
            {
                if (sections.MoveNext())
                {
                    foreach (KeyValuePair<long, int> kv in _offsets)
                    {
                        while (hasSection && sections.Current.ImageOffset + sections.Current.Size <= kv.Key)
                            hasSection = sections.MoveNext();
                        if (!hasSection)
                            break;

                        process(sections.Current, (int)(kv.Key - sections.Current.ImageOffset), _data[kv.Value].Item2, _data[kv.Value].Item1);
                    }
                }
            }
        }

        public void Serialise(Stream outStream)
        {
            byte[] data = new byte[4];

            //write image offsets count
            data.WriteUInt32B(0, (uint)_offsets.Count);
            outStream.Write(data, 0, data.Length);

            //write image offsets and data pointers
            data = new byte[_offsets.Count * 12];
            int i = 0;
            foreach (KeyValuePair<long, int> kv in _offsets)
            {
                data.WriteUInt64B(i, (ulong)kv.Key);
                data.WriteUInt32B(i + 8, (uint)kv.Value);
                i += 12;
            }
            outStream.Write(data, 0, data.Length);

            //write data item lengths
            data = new byte[_data.Count * 4];
            for (i = 0; i < _data.Count; i++)
                data.WriteUInt32B(i << 2, ((uint)_data[i].Item1.Length) | (_data[i].Item2 ? 0x80000000 : 0)); //set scrubbed flag
            outStream.Write(data, 0, data.Length);

            //write data items
            for (i = 0; i < _data.Count; i++)
                outStream.Write(_data[i].Item1, 0, _data[i].Item1.Length);

        }

        public void Deserialise(Stream dataStream)
        {
            uint items = dataStream.ReadBytes(4).ReadUInt32B(0);
            byte[] data = dataStream.ReadBytes((int)(12 * items));
            int idx;
            for (int i = 0; i < data.Length; i += 12)
            {
                idx = (int)data.ReadUInt32B(i + 8);
                _offsets.Add((long)data.ReadUInt64B(i), idx);
                if (idx >= _data.Count)
                    _data.Add(null);
            }

            //load the lengths of all the unique data
            data = dataStream.ReadBytes(_data.Count * 4);
            for (int i = 0; i < _data.Count; i++)
            {
                uint len = data.ReadUInt32B(i << 2);
                _data[i] = new Tuple<byte[], bool>(dataStream.ReadBytes((int)len & 0x7FFFFFFF), (len & 0x80000000) != 0);
            }
        }

    }
}
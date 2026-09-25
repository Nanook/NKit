using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nanook.NKit
{
    public class Checksums
    {
        private Dictionary<ChecksumType, byte[]> _chk;

        public Checksums()
        {
            _chk = new Dictionary<ChecksumType, byte[]>();
            this.Size = 0;
        }

        public byte[] this[ChecksumType checksum]
        {
            get => _chk.ContainsKey(checksum) ? _chk[checksum] : null;
            set
            {
                if (_chk.ContainsKey(checksum))
                {
                    if (value == null)
                        _chk.Remove(checksum);
                    else
                        _chk[checksum] = value;
                }
                else if (value != null)
                    _chk.Add(checksum, value);
            }
        }

        public void Merge(Checksums checksums)
        {
            if (checksums == null)
                return;
            foreach (KeyValuePair<ChecksumType, byte[]> chk in checksums._chk)
                this[chk.Key] = chk.Value;
        }

        public void MergeSafe(Checksums checksums)
        {
            if (checksums == null || _chk.Count == 0)
                return;
            //ensure all existing match
            int c = 0;
            if (_chk.All(kv =>
            {
                c++;
                return checksums.Exists(kv.Key) && kv.Value.Length == checksums._chk[kv.Key].Length && kv.Value.Equals(0, checksums._chk[kv.Key], 0, kv.Value.Length);
            }))
            {
                if (c > 0)
                {
                    foreach (KeyValuePair<ChecksumType, byte[]> chk in checksums._chk)
                        this[chk.Key] = chk.Value;
                }
            }
        }

        public Checksums Clone() => new Checksums() { _chk = new Dictionary<ChecksumType, byte[]>(_chk) };

        public bool Exists(ChecksumType checksum) => _chk.ContainsKey(checksum);

        internal KeyValuePair<ChecksumType, byte[]>? First() => _chk.FirstOrDefault();

        public Dictionary<ChecksumType, byte[]> ToDictionary() => _chk;

        internal VerifyResult Compare(Checksums compareTo, out ChecksumType[] compared, out ChecksumType[] skipped)
        {
            compared = compareTo._chk.Where(a => _chk.ContainsKey(a.Key)).Select(a => a.Key).OrderByDescending(a => (int)a).ToArray();
            if (compared.Length == 0)
            {
                skipped = null;
                return VerifyResult.VerifyFailed;
            }
            skipped = compareTo._chk.Where(a => !_chk.ContainsKey(a.Key)).Select(a => a.Key).OrderByDescending(a => (int)a).ToArray();
            foreach (ChecksumType k in compared) //]?.Equals(0, a.Value, 0, a.Value.Length) ?? false).Select(a => a.Key).ToList();
            {
                byte[] v = _chk[k];
                if (!compareTo[k].Equals(0, v, 0, v.Length))
                    return VerifyResult.VerifyFailed;
            }
            return VerifyResult.VerifySuccess;
        }

        public bool HasCrc => _chk.ContainsKey(ChecksumType.Crc32);

        public bool HasHash => _chk.Any(a => a.Key != ChecksumType.Crc32);

        public int Count => _chk.Count;

        public uint Crc
        {
            get
            {
                if (this.Exists(ChecksumType.Crc32))
                    return this[ChecksumType.Crc32].ReadUInt32B(0);
                else
                    return 0;
            }

            set => this[ChecksumType.Crc32] = value.ToBytesBE();
        }

        public ulong XxHash
        {
            get
            {
                if (this.Exists(ChecksumType.XxHash))
                    return this[ChecksumType.XxHash].ReadUInt64B(0);
                else
                    return 0;
            }

            set => this[ChecksumType.XxHash] = value.ToBytesBE();
        }

        public byte[] Md5
        {
            get => this[ChecksumType.Md5];
            set => this[ChecksumType.Md5] = value;
        }

        public byte[] Sha1
        {
            get => this[ChecksumType.Sha1];
            set => this[ChecksumType.Sha1] = value;
        }

        public long Size { get; set; }

        public override string ToString() => this.ToString(false);

        public string ToString(bool present)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ChecksumType c in new[] { ChecksumType.Crc32, ChecksumType.Md5, ChecksumType.Sha1, ChecksumType.XxHash })
            {
                if (!present || _chk.ContainsKey(c))
                {
                    if (sb.Length != 0)
                        sb.Append(", ");
                    sb.Append(c.ToString());
                }
            }
            return sb.ToString();
        }

        public string ToString(bool present, bool withValue)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ChecksumType c in new[] { ChecksumType.Crc32, ChecksumType.Md5, ChecksumType.Sha1, ChecksumType.XxHash })
            {
                bool exists = _chk.ContainsKey(c);
                if (!present || exists)
                {
                    if (sb.Length != 0)
                        sb.Append(", ");
                    sb.Append(c.ToString());
                    if (withValue)
                    {
                        if (exists)
                            sb.Append($":{this[c].ToHexString()}");
                        else
                            sb.Append($":Empty");
                    }
                }
            }
            return sb.ToString();
        }
    }
}
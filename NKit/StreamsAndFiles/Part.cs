using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    internal class Part : IPart
    {
        public string FileName { get; internal set; }
        public Checksums Checksums { get; internal set; }
        public long Size { get; internal set; }
        public bool IsIndex { get; internal set; }
        public bool IsImageName { get; internal set; }
        public bool DeleteExisting { get; internal set; }


        public Part()
        {
            this.Checksums = new Checksums();
        }

        public Part(Checksums checksums)
        {
            this.Checksums = checksums;
            this.Size = checksums.Size;
        }
        public byte[] this[ChecksumType index] => this.Checksums[index];
    }

    internal class Parts : IParts, IPartsGlobalHash
    {
        private IPart[] _parts;
        public int Length => _parts.Length;
        public IPart this[int index] => _parts == null || index >= _parts.Length ? null : _parts[index];

        public uint GlobalCrc { get; set; }
        public ulong GlobalXxHash { get; set; }
        public byte[] GlobalMd5 { get; set; }
        public byte[] GlobalSha1 { get; set; }
        public bool HasGlobalHashes => GlobalCrc != 0 || GlobalXxHash != 0 || GlobalMd5 != null || GlobalSha1 != null;

        public Parts(int parts)
        {
            _parts = new Part[parts];
        }
        public Parts(IEnumerable<IPart> parts)
        {
            _parts = parts.ToArray();
        }

        public bool IsMultiPart => _parts.Length > 1;

        public ChecksumType? GetHashType(bool incXxHash, out bool hasCrc)
        {
            hasCrc = _parts.All(a => a.Checksums.HasCrc);
            if (incXxHash && _parts.All(a => a.Checksums.Exists(ChecksumType.XxHash)))
                return ChecksumType.XxHash;
            if (_parts.All(a => a.Checksums.Exists(ChecksumType.Md5)))
                return ChecksumType.Md5;
            if (_parts.All(a => a.Checksums.Exists(ChecksumType.Sha1)))
                return ChecksumType.Sha1;
            return null;
        }

        public Parts(Checksums imageChecksums)
        {
            _parts = new[] { new Part(imageChecksums) };
        }

        public IEnumerator<IPart> GetEnumerator() => _parts.Cast<IPart>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _parts.GetEnumerator();
    }
}
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Nanook.NKit
{
    internal class NKitHasher
    {

        private AutoResetEvent _hasherLock;
        private CryptoStream _sha1Stream;
        private HashAlgorithm _sha1;
        private CryptoStream _md5Stream;
        private HashAlgorithm _md5;
        private CryptoStream _xxhStream;
        private HashAlgorithm _xxh;

        public byte[] Md5 { get; private set; }
        public byte[] Sha1 { get; private set; }
        public byte[] XxHash { get; private set; }

        public NKitHasher() : this(true, true, true)
        {

        }

        public NKitHasher(bool createMd5, bool createSha1, bool createXxhash)
        {
            _md5 = null;
            _sha1 = null;
            _xxh = null;

            _hasherLock = new AutoResetEvent(false);
            if (createMd5)
            {
                _md5 = MD5.Create();
                _md5Stream = new CryptoStream(Stream.Null, _md5, CryptoStreamMode.Write);
            }
            if (createSha1)
            {
                _sha1 = SHA1.Create();
                _sha1Stream = new CryptoStream(Stream.Null, _sha1, CryptoStreamMode.Write);
            }
            if (createXxhash)
            {
                _xxh = XXHash64.Create();
                _xxhStream = new CryptoStream(Stream.Null, _xxh, CryptoStreamMode.Write);
            }
        }

        public void Process(byte[] buffer, int offset, int size)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                _sha1Stream?.Write(buffer, offset, size);
                _md5Stream?.Write(buffer, offset, size);
                _xxhStream?.Write(buffer, offset, size);
                _hasherLock.Set();
            });
        }

        public void EndProcess() => _hasherLock.WaitOne();

        public void Complete()
        {
            if (_md5 != null)
            {
                _md5Stream.Dispose();
                Md5 = _md5.Hash;
                _md5.Dispose();
            }

            if (_sha1 != null)
            {
                _sha1Stream.Dispose();
                Sha1 = _sha1.Hash;
                _sha1.Dispose();
            }
            if (_xxh != null)
            {
                _xxhStream.Dispose();
                XxHash = _xxh.Hash;
                _xxh.Dispose();
            }
            _hasherLock.Dispose();
        }
    }
}
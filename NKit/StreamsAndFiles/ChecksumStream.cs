using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    internal class ChecksumStream : Stream
    {
        private class CrcPart
        {
            public long Offset;
            public uint Crc;
            public long Size;
        }
        private List<CrcPart> _crcParts;
        private CryptoStream _cryptoStream;
        private HashAlgorithm _crc;
        private Stream _strm;
        private NKitHasher _hasher;
        private NKitHasher _fullHasher;
        private long _size;
        private bool CreateCrc { get; set; }
        private bool CreateMd5 { get; }
        private bool CreateSha1 { get; }
        private bool CreateXxHash { get; }
        private bool WriteFile { get; set; }
        private bool _init;
        private bool _testMode;
        private bool _disposed;

        public List<Part> ChecksummedFiles { get; private set; }
        private List<Part> _additionalFiles { get; set; }
        private Part _curr;

        public string BasePath { get; private set; }
        public bool IsTemp { get; private set; }
        public long FullSize { get; private set; }
        public uint FullCrc { get; private set; }
        public ulong FullXxHash { get; private set; }
        public byte[] FullMd5 { get; private set; }
        public byte[] FullSha1 { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => !(CreateCrc || CreateMd5 || CreateSha1 || CreateXxHash);

        public override bool CanWrite => true;

        public override long Length => _size;

        public override long Position { get => _strm is FileStream ? _strm.Position : _size; set => throw new NotImplementedException(); }

        public ChecksumStream(IEnumerable<ChecksumType> checksumTypes, bool writeFile, string basePath, bool isTemp)
        {
            _disposed = false;
            _curr = null;
            CreateCrc = checksumTypes.Contains(ChecksumType.Crc32);
            CreateMd5 = checksumTypes.Contains(ChecksumType.Md5);
            CreateSha1 = checksumTypes.Contains(ChecksumType.Sha1);
            CreateXxHash = checksumTypes.Contains(ChecksumType.XxHash);
            WriteFile = writeFile;
            if (WriteFile)
                this.BasePath = basePath;
            if (CreateCrc)
                _crcParts = new List<CrcPart>();
            this.IsTemp = isTemp;
            _additionalFiles = new List<Part>();
            _size = 0;
            this.ChecksummedFiles = new List<Part>();
            if (!writeFile && (CreateCrc || CreateMd5 || CreateSha1 || CreateXxHash))
                _init = true;

            if (CreateMd5 || CreateSha1 || CreateXxHash)
                _fullHasher = new NKitHasher(CreateMd5, CreateSha1, CreateXxHash);
        }

        internal void EnableTestMode(bool crc)
        {
            _testMode = true;
            this.CreateCrc = crc;
            if (!WriteFile && (CreateCrc || CreateMd5 || CreateSha1 || CreateXxHash))
                _init = true;
            if (this.CreateCrc && _crcParts == null)
                _crcParts = new List<CrcPart>();
        }

        public void WriteAdditionalFile(byte[] buffer, int offset, int count, string fileName, bool isIndex, bool isImageName)
        {
            if (count == -1)
                count = buffer.Length;
            uint chk = Crc.Compute(buffer, offset, count);
            if (!_testMode && this.WriteFile)
            {
                Stream strm;
                if (!_testMode && WriteFile && !string.IsNullOrWhiteSpace(fileName))
                    strm = new FileStream(Path.Combine(this.BasePath, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None, 0x200000);
                else
                    strm = new HackStream(Stream.Null, false); //will calculate the position / length rather than return 0
                strm.Write(buffer, offset, count);
                strm.Close();
            }
            Part part = new Part() { FileName = fileName, IsIndex = isIndex, IsImageName = isImageName, Checksums = new Checksums() { Crc = chk }, Size = count };
            _additionalFiles.Add(part);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_init)
            {
                _init = false;
                setHasher(null, false, false);
            }

            if (_hasher != null)
                _hasher.Process(buffer, offset, count);
            if (_fullHasher != null)
                _fullHasher.Process(buffer, offset, count);
            (_cryptoStream ?? _strm)?.Write(buffer, offset, count);
            if (_hasher != null)
                _hasher.EndProcess();
            if (_fullHasher != null)
                _fullHasher.EndProcess();
            _size += count;
        }

        public override void Close()
        {
            if (_disposed)
                return;

            try
            {
                closePart();

                if (_fullHasher != null)
                {
                    _fullHasher.Complete();
                    if (CreateMd5) FullMd5 = _fullHasher.Md5;
                    if (CreateSha1) FullSha1 = _fullHasher.Sha1;
                    if (CreateXxHash) FullXxHash = _fullHasher.XxHash.ReadUInt64B(0);
                }

                if (this.ChecksummedFiles.Count != 0)
                {
                    this.FullCrc = this.ChecksummedFiles[0].Checksums.Crc;
                    this.FullSize = this.ChecksummedFiles[0].Size;

                    for (int x = 1; x < this.ChecksummedFiles.Count; x++)
                    {
                        this.FullCrc = ~Nanook.NKit.Crc.Combine(~this.FullCrc, ~this.ChecksummedFiles[x].Checksums.Crc, this.ChecksummedFiles[x].Size);
                        this.FullSize += this.ChecksummedFiles[x].Size;
                    }
                }
            }
            finally
            {
                _disposed = true;
            }
        }

        public void NewPart(string name, string ext, bool isImageName) => this.NewPart(name, ext, isImageName, false);

        public void NewPart(string name, string ext, bool isImageName, bool deleteExisting)
        {
            closePart();
            if (this.ChecksummedFiles.Count == 1 && this.ChecksummedFiles[0].Size == 0)
                this.ChecksummedFiles.Clear();
            _size = 0;
            if (name != null && WriteFile)
                name = Path.GetFileName(SourceFiles.GetUniqueFilename(this.BasePath, name, ext.TrimStart('.'), this.IsTemp));
            setHasher(name, isImageName, deleteExisting);
        }

        public void CrcSplit()
        {
            if (_crcParts != null)
            {
                _crcParts[_crcParts.Count - 1].Size = this.Position - _crcParts[_crcParts.Count - 1].Offset;
                _crcParts[_crcParts.Count - 1].Crc = ((Crc)_crc).Value;
                _crcParts.Add(new CrcPart() { Offset = this.Position });
                ((Crc)_crc).Clear();
            }
        }

        internal long CrcPatch(long partOffset, Action<Stream> writer)
        {
            long sz = 0;
            _strm.Seek(partOffset, SeekOrigin.Begin);

            if (_crcParts == null)
            {
                writer(_strm);
                sz = _strm.Position - partOffset;
            }
            else
            {
                CrcPart p = _crcParts.FirstOrDefault(a => a.Offset == partOffset);
                if (p == null)
                    throw new Exception($"OutStream Crc Patch not found for offset: 0x{partOffset:x}");

                Crc crc = new Crc();
                using (CryptoStream cryptoStream = new CryptoStream(new HackStream(_strm, true), crc, CryptoStreamMode.Write))
                {
                    writer(cryptoStream);
                    sz = _strm.Position - partOffset;
                    if (sz > p.Size)
                        throw new Exception($"OutStream Crc Patch wrote too many bytes: 0x{sz:x} - Max is 0x{p.Size:x}");
                    else if (sz < p.Size)
                        ByteStream.Zeros.Copy(cryptoStream, p.Size - sz);
                }
                p.Crc = crc.Hash.ReadUInt32B(0);
                sz = p.Size;
            }

            return sz;
        }

        private void setHasher(string fileName, bool isImageName, bool deleteExisting)
        {
            if (!_testMode && WriteFile && !string.IsNullOrWhiteSpace(fileName))
                _strm = new FileStream(Path.Combine(this.BasePath, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None, 0x200000);
            else
            {
                if (_testMode) //create a zero byte file
                    File.Create(Path.Combine(this.BasePath, fileName));
                _strm = new HackStream(Stream.Null, false); //will calculate the position / length rather than return 0
            }

            if (CreateCrc)
            {
                _crc = new Crc();
                _crcParts = new List<CrcPart>();
                _crcParts.Add(new CrcPart() { Offset = 0 });
                _cryptoStream = new CryptoStream(_strm, _crc, CryptoStreamMode.Write);
            }
            else
            {
                _crc = null;
                _cryptoStream = null;
            }
            if (CreateMd5 || CreateSha1 || CreateXxHash)
                _hasher = new NKitHasher(CreateMd5, CreateSha1, CreateXxHash);
            else
                _hasher = null;

            _curr = new Part() { FileName = fileName, IsImageName = isImageName, DeleteExisting = deleteExisting };
        }

        private void closePart()
        {
            if (_curr == null)
                return;

            if (_cryptoStream != null)
            {
                _crcParts[_crcParts.Count - 1].Size = this.Length - _crcParts[_crcParts.Count - 1].Offset;
                _crcParts[_crcParts.Count - 1].Crc = ((Crc)_crc).Value;
                _cryptoStream.Close();
                _curr.Checksums.Crc = _crcParts[0].Crc;
                for (int i = 1; i < _crcParts.Count; i++)
                    _curr.Checksums.Crc = ~Crc.Combine(~_curr.Checksums.Crc, ~_crcParts[i].Crc, _crcParts[i].Size);
            }
            if (_hasher != null)
            {
                _hasher.Complete();
                _curr.Checksums.Md5 = _hasher.Md5;
                _curr.Checksums.Sha1 = _hasher.Sha1;
                if (_hasher.XxHash != null)
                    _curr.Checksums.XxHash = _hasher.XxHash.ReadUInt64B(0);
            }
            _curr.Size = _size;
            try
            {
                _strm.Close();
            }
            catch { }
            this.ChecksummedFiles.Add(_curr);
        }

        protected override void Dispose(bool disposing)
        {
            this.Close();
            base.Dispose(disposing);
        }

        public override void Flush()
        {
            //throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_testMode)
                return 0;
            if (this.CanSeek)
                return _strm.Seek(offset, origin);
            else
                throw new HandledException("Seek not supported for writing with Checksum calculations.");
        }

        public override void SetLength(long value) => throw new NotImplementedException();

        internal IParts GetAllParts()
        {
            Parts p = new Parts(this.ChecksummedFiles.Concat(_additionalFiles));
            if (_fullHasher != null)
            {
                p.GlobalXxHash = FullXxHash;
                p.GlobalMd5 = FullMd5;
                p.GlobalSha1 = FullSha1;
            }
            p.GlobalCrc = FullCrc;
            return p;
        }
    }
}
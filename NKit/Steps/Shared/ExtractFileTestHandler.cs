using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nanook.NKit.Steps.Shared
{
    internal class ExtractFileTestItem
    {
        public string FileName;
        public uint Crc;
        public long Size;
        public override string ToString() => $"Size:{Size:x9}, Crc:{Crc:x8}, Name:{FileName}";
    }

    internal class ExtractFileTestHandler : IExtractFileHandler
    {
        private ExtractFileTestItem _curr;
        private string _currName;
        private Crc _crc;
        private bool _replace;
        private CryptoStream _stream;
        private long _currStartPos;

        public List<string> Directories;
        public Dictionary<string, ExtractFileTestItem> Files;

        public ExtractFileTestHandler()
        {
            this.Directories = new List<string>();
            this.Files = new Dictionary<string, ExtractFileTestItem>();
        }

        public void CreateDirectory(string rootPath, string imagePath)
        {
            imagePath = imagePath.Replace('\\', '/');
            if (!this.Directories.Contains(imagePath))
                this.Directories.Add(imagePath);
        }

        public void WriteBytes(string rootPath, string imagePath, byte[] data) => this.Files.Add(imagePath, new ExtractFileTestItem() { FileName = imagePath, Crc = Crc.Compute(data), Size = data.Length });

        public bool WriteFs(ISection section, string rootPath, string imagePath, long pos, int fsOffset, int fsSize, long fullFsSize, bool replace)
        {
            bool newFile = false;
            bool replaced = false;

            // Close current file if a different file is being written or if pos==0 (new file start)
            // This matches ExtractFileHandler behavior for interleaved overlap writes.
            if (_curr != null && (pos == 0 || !string.Equals(_currName, imagePath, System.StringComparison.OrdinalIgnoreCase)))
                CloseFs();

            if (_curr == null)
            {
                _currName = imagePath;
                _curr = new ExtractFileTestItem() { FileName = imagePath };
                _crc = new Crc();
                _replace = replace;
                _stream = new CryptoStream(Stream.Null, _crc, CryptoStreamMode.Write);
                _currStartPos = _curr.Size;

                newFile = true;
            }

            section.Read(fsOffset, fsSize, _stream);
            _curr.Size += fsSize;

            if (newFile && replace && Files.ContainsKey(_curr.FileName))
            {
                Files.Remove(_curr.FileName);
                replaced = true; //replaced
            }

            if (fullFsSize != -1 && _curr.Size - _currStartPos == fullFsSize)
                CloseFs();

            return newFile && !replaced; //inc saved file counter
        }

        public void CloseFs()
        {
            if (_curr != null)
            {
                _curr.Crc = _crc.Value;
                _stream.Dispose();
                _crc.Dispose();
                _stream = null;
                _crc = null;

                if (Files.TryGetValue(_curr.FileName, out ExtractFileTestItem itm)) //multi extent (iso9660 split file)
                {
                    itm.Crc = ~Crc.Combine(~itm.Crc, ~_curr.Crc, _curr.Size);
                    itm.Size += _curr.Size;
                }
                else
                    Files.Add(_curr.FileName, _curr);

                _curr = null;
                _currName = null;
            }
        }
    }
}
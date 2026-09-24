using System.IO;

namespace Nanook.NKit.Steps.Shared
{
    internal class ExtractFileHandler : IExtractFileHandler
    {
        private string _name;
        private FileStream _curr;
        private long _currStartPos;

        public void CreateDirectory(string rootPath, string imagePath)
        {
            string path = Path.Combine(rootPath, imagePath);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        public void WriteBytes(string rootPath, string imagePath, byte[] data)
        {
            string fullPath = Path.Combine(rootPath, imagePath);
            File.WriteAllBytes(fullPath, data);
        }

        public bool WriteFs(ISection section, string rootPath, string imagePath, long pos, int fsOffset, int fsSize, long fullFsSize, bool replace)
        {
            bool newFile = false;
            bool replaced = false;

            string fullPath = Path.Combine(rootPath, imagePath);

            // Close current file if a different file is being written or if pos==0 (new file start)
            if (_curr != null && (pos == 0 || !string.Equals(_name, imagePath, System.StringComparison.OrdinalIgnoreCase)))
                CloseFs();

            if (_curr == null)
            {
                _name = imagePath;
                _curr = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 0x200000);
                _currStartPos = _curr.Length;

                newFile = true;

                if (_currStartPos != 0)
                {
                    if (replace)
                    {
                        _curr.SetLength(0);
                        replaced = true;
                    }
                    else
                        _curr.Seek(0, SeekOrigin.End); //iso multi extent (split files)
                }

            }

            section.Read(fsOffset, fsSize, _curr);

            if (fullFsSize != -1 && _curr.Position - _currStartPos == fullFsSize)
                CloseFs();

            return newFile && !replaced; //new file
        }

        public void CloseFs()
        {
            if (_curr != null)
            {
                _curr.Close();
                _curr = null;
            }
        }
    }
}
namespace Nanook.NKit
{
    internal interface IExtractFileHandler
    {
        void CloseFs();
        void CreateDirectory(string rootPath, string imagePath);
        void WriteBytes(string rootPath, string imagePath, byte[] data);
        bool WriteFs(ISection section, string rootPath, string imagePath, long pos, int fsOffset, int fsSize, long fullFsSize, bool replace);
    }
}
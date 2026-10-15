namespace Nanook.NKit
{
    /// <summary>
    /// File-extraction sink used by NKit's internal extract steps. The extract step uses
    /// <c>SkipToImageOffsetSet</c> to jump to every FST file's section, giving 100%
    /// coverage of all disc files.
    /// </summary>
    internal interface IExtractFileHandler
    {
        void CloseFs();
        void CreateDirectory(string rootPath, string imagePath);
        void WriteBytes(string rootPath, string imagePath, byte[] data);
        bool WriteFs(IReadOnlySection section, string rootPath, string imagePath, long pos, int fsOffset, int fsSize, long fullFsSize, bool replace);
    }
}
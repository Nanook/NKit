#if WINDOWS
using DokanNet;

namespace Nanook.NKit.Vfs
{
    internal static class Extensions
    {
        private static DateTime _Dt;


        static Extensions()
        {
            _Dt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        }

        internal static FileInformation ToFileInfo(this IFsItem item, bool isWritable = false)
        {
            if (item is IFsFolder)
            {
                return new FileInformation { FileName = item.Name, CreationTime = _Dt, LastAccessTime = _Dt, LastWriteTime = _Dt, Attributes = FileAttributes.Directory | (isWritable ? 0 : FileAttributes.ReadOnly) };
            }
            else //IFsFile
            {
                return new FileInformation { FileName = item.Name, Length = ((IFsFile)item).FsSize, CreationTime = _Dt, LastAccessTime = _Dt, LastWriteTime = _Dt, Attributes = isWritable ? FileAttributes.Normal : FileAttributes.ReadOnly };
            }
        }


    }
}
#endif
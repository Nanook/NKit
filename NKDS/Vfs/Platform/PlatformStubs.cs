#if !WINDOWS && !LINUX && !MACOS
// Lightweight platform stubs for non-Windows/non-Linux builds
using System;
using System.IO;

namespace Nanook.NKit.Vfs
{
    [Flags]
    internal enum FileAccess
    {
        None = 0,
        ReadData = 1,
        WriteData = 2,
        AppendData = 4,
        Execute = 8,
        GenericExecute = 16,
        GenericWrite = 32,
        GenericRead = 64
    }

    // Minimal FileInformation used by Extensions.ToFileInfo on non-Windows
    internal class FileInformation
    {
        public string FileName { get; set; }
        public long Length { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public FileAttributes Attributes { get; set; }
    }

    [Flags]
    internal enum FileSystemFeatures
    {
        None = 0
    }
}
#endif
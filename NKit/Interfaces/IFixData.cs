using System.IO;

namespace Nanook.NKit
{
    internal interface IFixData
    {
        void Load(SystemType system, FileInfo info, string filesPath);
    }
}
using System.Collections.Generic;

namespace Nanook.NKit.Iso.Iso9660
{

    internal class IrdFileResult
    {
        public IFsFile File;
        public string FixFileName;
        public byte[] Md5;
        public bool IsMissing;
        public bool IsMd5Valid;
        public bool SizeIsValid;
    }

    internal class IrdFileResults : List<IrdFileResult>
    {
    }
}
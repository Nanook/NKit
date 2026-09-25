using System;
using System.Collections.Generic;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FstLink
    {
        internal FstLink(FstFolder parent, string encodedChildName, FsType fsType) : this(parent, encodedChildName, fsType, -1) { }

        internal FstLink(FstFolder parent, string encodedChildName, FsType fsType, long fsSize)
        {
            this.FsType = fsType;
            this.Parent = parent;
            this.EncodedChildName = encodedChildName;
            this.FsSize = fsSize;
        }

        public FsType FsType { get; internal set; }
        public long FsSize { get; internal set; }

        public string EncodedChildName { get; internal set; }
        public FstFolder Parent { get; internal set; }

        public override string ToString() => String.Format("EncodedChildName:{0}, FsType:{1} - FstFolder:{2}", this.EncodedChildName, this.FsType.ToString(), this.Parent == null ? "" : this.Parent.ToString());

        internal static void Merge(IEnumerable<FstLink> merge, OrderedList<FstLink> mergeInto)
        {
            foreach (FstLink l in merge)
                mergeInto.InsertIfMissing(l, out bool existed);
        }

    }

}
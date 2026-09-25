using System.Collections.Generic;

namespace Tmds.Fuse
{
    public class MountOptions
    {
        public bool SingleThread { get; set; } = false;
        public bool AllowOther { get; set; } = false;
        public uint? Uid { get; set; }
        public uint? Gid { get; set; }

        /// <summary>
        /// Additional raw -o option values passed to FUSE (e.g., "volname=MyVolume", "nobrowse").
        /// Each entry is the value portion after -o.
        /// </summary>
        public List<string> AdditionalOptions { get; set; } = new List<string>();
    }
}
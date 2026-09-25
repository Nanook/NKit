using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    public class SourceFileTrack : ICloneable
    {
        public Dictionary<string, string> RawItems;

        public SourceFileTrack()
        {
            OffsetIndexes = new List<long>();
        }
        public int TrackIndex { get; set; }
        public long ImageOffset { get; set; }
        public long PhysicalOffset { get; set; } //read from the mode1/2 header info
        public long LogicalOffset { get; internal set; }
        public long LogicalSize { get; internal set; }
        public long Size { get; set; }
        public int BlockSize { get; set; }
        public int Blocks { get; set; }
        public long BlockIdx { get; set; }


        public IndexFileFormat FileFormat { get; set; }
        public IndexTrackBasicType BasicType { get; set; }
        public IndexTrackType TrackType { get; set; }
        public string FileName { get; set; }
        public List<long> OffsetIndexes { get; set; }
        public int Session { get; internal set; }
        public bool FileIsMissing { get; set; }

        public string ChdTag { get; set; }
        public MediaType ChdMediaType { get; set; }
        public int SubSize { get; set; }
        public int PreGap { get; set; }
        public int Pad { get; set; }
        public int PadSize { get; set; }
        public int PreGapSubSize { get; set; }
        public int PreGapDataSize { get; set; }
        public int PostGap { get; set; }
        public CdSubType ChdSubType { get; set; }
        public IndexTrackType PreGapType { get; set; }
        public string Comment { get; set; }

        public override string ToString() => $"Type:{TrackType}({ChdTag}), Frame:{BlockIdx}, Frames:{Blocks}, PreGap:{PreGap}, Pad:{Pad}, ImageOffset:{ImageOffset:X9}, Size:{Size:X8}";

        object ICloneable.Clone() => Clone();

        public SourceFileTrack Clone()
        {
            SourceFileTrack clone = (SourceFileTrack)MemberwiseClone();
            if (RawItems != null)
                clone.RawItems = new Dictionary<string, string>(RawItems);
            if (OffsetIndexes != null)
                clone.OffsetIndexes = new List<long>(OffsetIndexes);
            return clone;
        }
    }
}
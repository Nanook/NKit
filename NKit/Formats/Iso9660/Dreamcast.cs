namespace Nanook.NKit.Iso.Iso9660
{
    internal enum DreamcastImageType { Unknown, Redump, Tosec }

    internal static class Dreamcast
    {
        private const int Session2TrackIdx = 2;

        internal static int SessionNo(SystemType system, int trackIdx, int defaultNo)
        {
            if (system == SystemType.Dreamcast)
                return trackIdx < Session2TrackIdx ? 0 : 1;
            return defaultNo;
        }

        internal static bool IsNewSession(SystemType system, int areaNo) => system == SystemType.Dreamcast && areaNo == 2;

        internal static long CalculateBaseOffset(SystemType system, AreaType type, SourceFileTrack[] items, int trackIndex, long defaultAreaBase)
        {
            if (system == SystemType.Dreamcast && trackIndex > 2 && (items?.Length ?? 0) >= trackIndex && type == AreaType.FileSystem && items[trackIndex].LogicalOffset != 0)
                return items[trackIndex].LogicalOffset - items[2].LogicalOffset;
            return defaultAreaBase;
        }
    }
}
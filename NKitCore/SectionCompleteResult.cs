namespace NKitCore
{
    public sealed class SectionCompleteResult
    {
        public bool HasSeek { get; set; }
        public SeekRequest Seek { get; set; }

        public static SectionCompleteResult Continue() => new SectionCompleteResult { HasSeek = false, Seek = null };
        public static SectionCompleteResult RequestSeek(SeekRequest req) => new SectionCompleteResult { HasSeek = true, Seek = req };
    }

    public sealed class SeekRequest
    {
        public long TargetOffset { get; set; }
        public SeekMode Mode { get; set; }
        public bool AlignToSectionBoundary { get; set; }

        public SeekRequest() { AlignToSectionBoundary = true; }
    }

    public enum SeekMode { WaitForOutstanding, CancelOutstanding }
}
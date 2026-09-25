namespace Nanook.NKit
{
    internal class StepsImageInfo : IStepsImageInfo
    {
        public bool ReqPatch { get; internal set; }

        public Checksums CustomChecksums { get; set; } //hash can only be calculated here - chd hash is for encoded data not source image

        public Checksums Checksums { get; internal set; }

        public bool HasCrc => Checksums.HasCrc;

        public bool HasHash => Checksums.HasHash;

        public long Size { get; internal set; }

        public bool IsIndex { get; internal set; }
    }
}
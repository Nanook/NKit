namespace Nanook.NKit
{
    internal interface IStepsImageInfo
    {
        bool ReqPatch { get; }
        Checksums CustomChecksums { get; set; } //hash can only be calculated here - chd hash is for encoded data not source image
        Checksums Checksums { get; }
        bool HasCrc { get; }
        bool HasHash { get; } //noncrc32
        long Size { get; }
        bool IsIndex { get; } //multi track/apps etc (not split images)
    }
}
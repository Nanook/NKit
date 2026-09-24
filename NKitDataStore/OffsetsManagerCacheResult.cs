namespace NKitDataStore
{
    /// <summary>
    /// Immutable bundle of pre-built OffsetsManager data cached per image.
    /// All three components are always provided together.
    /// </summary>
    public sealed class OffsetsManagerCacheResult
    {
        public OffsetsManager OffsetsManager { get; }
        public List<AreaRecord> Areas { get; }
        public Dictionary<long, OffsetRecord> OffsetsByPosition { get; }

        public OffsetsManagerCacheResult(
            OffsetsManager offsetsManager,
            List<AreaRecord> areas,
            Dictionary<long, OffsetRecord> offsetsByPosition)
        {
            OffsetsManager = offsetsManager ?? throw new ArgumentNullException(nameof(offsetsManager));
            Areas = areas ?? throw new ArgumentNullException(nameof(areas));
            OffsetsByPosition = offsetsByPosition ?? throw new ArgumentNullException(nameof(offsetsByPosition));
        }
    }
}
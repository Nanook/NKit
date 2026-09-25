namespace Nanook.NKit
{
    public enum NonCreatableDataType { ImageSection, Security, Filler }
    internal class NonCreatableData
    {
        internal NonCreatableData()
        {

        }

        public NonCreatableDataType Type { get; internal set; }
        public long ImageSectionOffset { get; internal set; }
        public int Offset { get; internal set; }
        public int Size { get; internal set; }
        public bool IsFs { get; internal set; } //from the filesystem, excludes hashes etc
    }
}
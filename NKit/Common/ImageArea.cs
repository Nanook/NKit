namespace Nanook.NKit
{
    internal class ImageArea : IImageArea
    {
        public ImageArea(long imageOffset, AreaType areaType)
        {
            this.ImageOffset = imageOffset;
            this.AreaType = areaType;
        }

        public long ImageOffset { get; set; }
        public AreaType AreaType { get; set; }

        public override string ToString() => $"ImageOffset: {ImageOffset:X}, AreaType: {AreaType}";
    }
}
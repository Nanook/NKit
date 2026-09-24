namespace NKitDataStore
{
    /// <summary>
    /// Represents a child image and its files for TmdAppFolder building.
    /// </summary>
    internal struct ChildImageInfo
    {
        public long ImageId;
        public string ImageName;        // e.g., "Game Title [tmd.0]"
        public string IndexFileName;    // e.g., "tmd.0"
        public List<ChildImageFile> Files; // Files from this child's filesystem.yaml
    }

    /// <summary>
    /// Represents a single file within a child image's filesystem.yaml.
    /// </summary>
    internal struct ChildImageFile
    {
        public string FileName;    // e.g., "00000001.app"
        public long ImageId;       // Parent image ID
        public long Size;          // File size
    }
}
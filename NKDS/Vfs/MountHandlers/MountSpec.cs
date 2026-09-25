namespace Nanook.NKit.Vfs
{
    internal enum MountKind
    {
        Image,
        FileSystem,
        AppFolder
    }

    internal class MountSpec
    {
        public MountKind Kind { get; set; }
        // Root name under the system folder. Empty string means direct under system (no extra subfolder)
        public string Root { get; set; } = string.Empty;
        // Optional label/description
        public string Label { get; set; }
    }
}
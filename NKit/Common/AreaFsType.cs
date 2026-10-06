namespace Nanook.NKit
{
    /// <summary>
    /// Canonical string values for the disc-area <c>FsType</c> metadata (and related area context
    /// item keys) shared by every system's DataStore formatter and ImageBuilder stream.
    ///
    /// <para>
    /// The DataStore formatters write <c>AreaValueType.FsType</c> as <c>ScanArea.Type.ToString()</c>
    /// — i.e. the name of the <see cref="AreaType"/> enum member. The ImageBuilder streams then read
    /// that string back and branch on it. These consts are the single source of truth for those
    /// string values so a reader and a writer can never drift apart, and so the values are NOT tied
    /// to any one system (they were previously duplicated as raw literals in the Wii/GC/Xbox builders
    /// and defined, system-specifically, on WiiUConsts).
    /// </para>
    ///
    /// <para>
    /// Each value is derived from <c>nameof(AreaType.*)</c> so it stays in lock-step with the enum.
    /// </para>
    /// </summary>
    internal static class AreaFsType
    {
        public const string ImageHeader = nameof(AreaType.ImageHeader);
        public const string PartitionTable = nameof(AreaType.PartitionTable);
        public const string PartitionHeader = nameof(AreaType.PartitionHeader);
        public const string FstBlock = nameof(AreaType.FstBlock);
        public const string FileSystem = nameof(AreaType.FileSystem);
        public const string Other = nameof(AreaType.Other);
        public const string RawKeyMissing = nameof(AreaType.RawKeyMissing);
    }

    /// <summary>
    /// Well-known keys for the per-buffer <c>BufferContext.Items</c> bag used by the ImageBuilder
    /// streams to carry Wii/WiiU partition reconstruction state between the section-populate and
    /// buffer-complete callbacks. Shared (not system-specific) so every stream agrees on the keys.
    /// </summary>
    internal static class AreaContextItem
    {
        public const string Creatable = "Creatable";
        public const string State = "State";
    }
}

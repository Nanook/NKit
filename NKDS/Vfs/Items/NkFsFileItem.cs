using NKitDataStore;

namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Wraps an NkFsEntry file as an IFsFile for VFS navigation.
    /// Reads name from the NkFs string table on demand.
    /// For multi-extent files, exposes total size via FsSize and extent chain via SplitParts.
    /// </summary>
    internal class NkFsFileItem : IFsFile
    {
        private readonly NKitDataStore.NkFs _nkfs;
        private readonly int _entryIndex;
        private readonly NKitDataStore.NkFsEntry _entry;
        private readonly bool _isExtentPart;
        private IFsFileParts _splitParts;
        private bool _splitPartsResolved;

        /// <summary>
        /// Creates an NkFsFileItem representing the full logical file (resolves multi-extent chain).
        /// </summary>
        public NkFsFileItem(NKitDataStore.NkFs nkfs, int entryIndex, NKitDataStore.NkFsEntry entry)
            : this(nkfs, entryIndex, entry, isExtentPart: false)
        {
        }

        /// <summary>
        /// Creates an NkFsFileItem. When isExtentPart is true, FsSize returns the individual extent size
        /// and SplitParts is always null (prevents recursive chain resolution).
        /// </summary>
        private NkFsFileItem(NKitDataStore.NkFs nkfs, int entryIndex, NKitDataStore.NkFsEntry entry, bool isExtentPart)
        {
            _nkfs = nkfs;
            _entryIndex = entryIndex;
            _entry = entry;
            _isExtentPart = isExtentPart;
        }

        public string Name
        {
            get
            {
                string name = _nkfs.GetEntryName(_entryIndex);
                return name;
            }
        }

        /// <summary>
        /// For multi-extent files (when not an extent part), returns the total file size across all extents.
        /// For single-extent files or individual extent parts, returns the individual extent size.
        /// </summary>
        public long FsSize => _isExtentPart ? _nkfs.GetFileSize(_entryIndex) : _nkfs.GetTotalFileSize(_entryIndex);

        public long FsOffset => _entry.FileOffset;
        public IFsFolder Parent { get; set; }
        public string Path => "";
        public string FullName => Name;
        public bool IsMissing => false;
        public bool IsLastFile => false;
        public bool IsSystemFile => _entry.SystemFlag;

        // Checksums: stored in string table prefix but not needed for VFS navigation.
        public ulong XxHash { get => 0; set { } }
        public uint Crc { get => 0; set { } }
        public uint GapCrc { get => 0; set { } }
        public long PostGapSize => 0;
        public long PostGapFsOffset => 0;
        public int SplitIndex => 0;

        /// <summary>
        /// For multi-extent files, returns NkFsFileParts with one NkFsFilePart per extent.
        /// For single-extent files, returns null (existing behavior).
        /// Individual extent parts always return null to prevent recursion.
        /// </summary>
        public IFsFileParts SplitParts
        {
            get
            {
                if (!_splitPartsResolved)
                {
                    _splitParts = ResolveSplitParts();
                    _splitPartsResolved = true;
                }
                return _splitParts;
            }
        }

        private IFsFileParts ResolveSplitParts()
        {
            if (_isExtentPart)
                return null;

            IReadOnlyList<(long offset, long size)> extents = _nkfs.GetExtents(_entryIndex);
            if (extents.Count <= 1)
                return null;

            // Multi-extent file: build NkFsFileParts from the extent chain
            // We need to find the entry indices for each extent in the chain.
            // GetExtents returns (offset, size) pairs in chain order starting from the first extent.
            // We need to map these back to entry indices to create per-extent NkFsFileItems.
            IReadOnlyList<int> extentEntryIndices = _nkfs.GetExtentEntryIndices(_entryIndex);

            List<IFsFilePart> parts = new List<IFsFilePart>(extents.Count);
            long cumulativeOffset = 0;

            for (int i = 0; i < extents.Count; i++)
            {
                int extentIndex = extentEntryIndices[i];
                NkFsEntry extentEntry = _nkfs.GetEntry(extentIndex);
                NkFsFileItem extentFile = new NkFsFileItem(_nkfs, extentIndex, extentEntry, isExtentPart: true);
                parts.Add(new NkFsFilePart(i, cumulativeOffset, extentFile));
                cumulativeOffset += extents[i].size;
            }

            return new NkFsFileParts(parts, cumulativeOffset);
        }

        public IFsFile Clone() => this;
        public override string ToString() => Name ?? "";
    }
}
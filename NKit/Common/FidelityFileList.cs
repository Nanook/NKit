using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Stores every declared filesystem entry regardless of offset collisions.
    /// Used by the FsYaml output path to produce complete filesystem views.
    /// </summary>
    public class FidelityFileList
    {
        private readonly List<IFsFile> _entries;

        public FidelityFileList()
        {
            _entries = new List<IFsFile>();
        }

        public FidelityFileList(int capacity)
        {
            _entries = new List<IFsFile>(capacity);
        }

        /// <summary>Total declared entries (including collision duplicates).</summary>
        public int Count => _entries.Count;

        /// <summary>Append a file entry. No deduplication is performed.</summary>
        public void Add(IFsFile file) => _entries.Add(file);

        /// <summary>Enumerate all entries in parse order.</summary>
        public List<IFsFile> Entries => _entries;
    }
}
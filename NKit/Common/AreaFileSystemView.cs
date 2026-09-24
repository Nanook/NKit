using Nanook.NKit.Iso.Iso9660;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Concrete <see cref="IAreaFileSystemView"/> built from an immutable snapshot of an area's
    /// file list. Constructed once the area's file system is fully parsed (see the reader/publish
    /// contract), so the snapshot never changes and parallel section processors can read it safely.
    ///
    /// <para>
    /// The snapshot is a frozen <see cref="IReadOnlyList{IFsFile}"/> captured at publish. The
    /// <see cref="Primary"/>, <see cref="System"/> and per-file-system views are projections over
    /// that one list (index maps), not copies of the file data.
    /// </para>
    /// </summary>
    internal sealed class AreaFileSystemView : IAreaFileSystemView
    {
        private readonly IReadOnlyList<IFsFile> _files; // frozen snapshot
        private readonly FileSystemListView _primary;
        private readonly FileSystemListView _system;
        private readonly IReadOnlyList<IFileSystemView> _fileSystems;

        // Append-only list of blocks identified during the read but NOT in the folder tree (UDF
        // backup anchor / partition mirror, mkisofs signature). The read thread appends via
        // AddDiscovered as the sequential read streams past the block; consumers snapshot the count
        // and index. Kept OUTSIDE the frozen Primary/System snapshot so it never shifts a published
        // file index. A lock guards the rare append against concurrent readers.
        private readonly object _discoveredSync = new object();
        private readonly List<IFsFile> _discovered = new List<IFsFile>();

        public AreaFileSystemView(
            long imageOffset, long size, PartitionType type, bool invalidFileSystem,
            IReadOnlyList<IFsFile> files, IFsFolder root, FileSystemKind kind)
        {
            ImageOffset = imageOffset;
            Size = size;
            Type = type;
            InvalidFileSystem = invalidFileSystem;
            Kind = kind;
            _files = files ?? Array.Empty<IFsFile>();

            // Primary is the frozen whole-list snapshot EXTENDED at its tail by DiscoveredFiles
            // (Option A): late markers append to the live FST at the highest offsets, so they sit
            // past the frozen count and never shift a published index. Primary surfaces them so the
            // preprocessor's GetFiles (single read thread) computes a FileEndIndex that reaches them
            // and the section processor's live-FST index read lands on the same entry.
            // Primary (merged) and System are cross-FS projections — no single logical kind. The
            // area-level XDVDFS discriminator lives on Kind; per-FS kinds are set in buildFileSystemViews.
            FileSystemKind primaryKind = kind == FileSystemKind.XDvdFs ? FileSystemKind.XDvdFs : FileSystemKind.Unknown;
            _primary = new FileSystemListView(_files, root, primaryKind, this);
            _system = new FileSystemListView(projectIndices(i => _files[i].IsSystemFile), _files, null, FileSystemKind.System);
            _fileSystems = buildFileSystemViews(root);
        }

        /// <summary>
        /// Build a view from a fully-parsed <see cref="IFileSystemData"/>. Returns null until the
        /// file system is fully parsed (<see cref="IFileSystemData.AllFoldersParsed"/>), so the
        /// snapshot captured here is complete and immutable. Snapshots the current
        /// <c>FileSystem.Files</c> into a frozen array so later reads are stable regardless of the
        /// live collection.
        /// </summary>
        public static IAreaFileSystemView TryBuild(IFileSystemData data)
        {
            if (data == null || !data.AllFoldersParsed)
                return null;
            IFileSystem fs = data.FileSystem;
            IReadOnlyList<IFsFile> snapshot = fs?.Files == null
                ? (IReadOnlyList<IFsFile>)Array.Empty<IFsFile>()
                : fs.Files.ToArray(); // frozen copy of the list (references shared, list immutable)
            // Centralise the ONE internal-format check here so the out-stage steps consume a public
            // FileSystemKind and never cast to Microsoft.XBox.Fst (or any other internal FS type).
            FileSystemKind areaKind = fs is Microsoft.XBox.Fst ? FileSystemKind.XDvdFs : FileSystemKind.Unknown;
            return new AreaFileSystemView(data.ImageOffset, data.Size, data.Type, data.InvalidFileSystem, snapshot, fs?.Root, areaKind);
        }

        public long ImageOffset { get; }
        public long Size { get; }
        public PartitionType Type { get; }
        public bool InvalidFileSystem { get; }
        public FileSystemKind Kind { get; }

        public IFileSystemView Primary => _primary;
        public IFileSystemView System => _system;
        public IReadOnlyList<IFileSystemView> FileSystems => _fileSystems;

        public IReadOnlyList<IFsFile> DiscoveredFiles
        {
            get
            {
                lock (_discoveredSync)
                    return _discovered.ToArray(); // stable snapshot for the caller
            }
        }

        /// <summary>
        /// Append a block identified during the read that is not part of the folder tree (a UDF
        /// backup sector, mkisofs marker, etc.). Deduplicated by FsOffset so re-reading the block
        /// (e.g. a section boundary) does not double-report it. The file is marked system.
        /// </summary>
        internal void AddDiscovered(IFsFile file)
        {
            if (file == null)
                return;

            // Already in the frozen snapshot (discovered during the up-front parse)? Then it is
            // part of Primary already — not a late discovery, so do not tail-append it.
            for (int i = 0; i < _files.Count; i++)
            {
                if (_files[i].FsOffset == file.FsOffset)
                    return;
            }

            lock (_discoveredSync)
            {
                for (int i = 0; i < _discovered.Count; i++)
                {
                    if (_discovered[i].FsOffset == file.FsOffset)
                        return; // already reported
                }
                _discovered.Add(file);
            }
        }

        // Group the snapshot by logical file system (ISO9660 / Joliet / UDF / XDVDFS via FstFile
        // links). Nintendo files carry no FsType, so they fall into a single default view. Each view
        // is an index map over the shared snapshot — no file data is copied.
        private IReadOnlyList<IFileSystemView> buildFileSystemViews(IFsFolder root)
        {
            // Determine each file's primary FsType (highest-priority link) when available.
            Dictionary<FsType, List<int>> byType = null;
            List<int> untyped = null;

            for (int i = 0; i < _files.Count; i++)
            {
                if (_files[i] is FstFile ff && ff.Links != null && ff.Links.Count != 0)
                {
                    FsType t = ff.Links.Select(a => a.FsType).OrderByDescending(a => a).First();
                    byType ??= new Dictionary<FsType, List<int>>();
                    if (!byType.TryGetValue(t, out List<int> list))
                        byType[t] = list = new List<int>();
                    list.Add(i);
                }
                else
                {
                    (untyped ??= new List<int>()).Add(i);
                }
            }

            // No per-FS typing available (Nintendo, or empty) — expose a single view over all files.
            // Carry the area-level Kind (e.g. XDVDFS) so single-FS formats still name themselves.
            if (byType == null)
                return new IFileSystemView[] { new FileSystemListView(_files, root, Kind) };

            List<IFileSystemView> views = new List<IFileSystemView>();
            foreach (KeyValuePair<FsType, List<int>> kv in byType.OrderByDescending(a => a.Key))
                views.Add(new FileSystemListView(kv.Value, _files, root, mapKind(kv.Key)));
            if (untyped != null && untyped.Count != 0)
                views.Add(new FileSystemListView(untyped, _files, root, FileSystemKind.Unknown));
            return views;
        }

        // Map the internal per-link FsType to the public out-stage FileSystemKind. Keeps the
        // internal enum from leaking onto the public IFileSystemView surface.
        private static FileSystemKind mapKind(FsType t)
        {
            switch (t)
            {
                case FsType.Iso9660: return FileSystemKind.Iso9660;
                case FsType.Romeo: return FileSystemKind.Romeo;
                case FsType.RockRidge: return FileSystemKind.RockRidge;
                case FsType.Joliet: return FileSystemKind.Joliet;
                case FsType.Udf: return FileSystemKind.Udf;
                case FsType.Cdi: return FileSystemKind.Cdi;
                case FsType.Cdxa: return FileSystemKind.Cdxa;
                case FsType.ElTorito: return FileSystemKind.ElTorito;
                case FsType.System: return FileSystemKind.System;
                default: return FileSystemKind.Other;
            }
        }

        private List<int> projectIndices(Func<int, bool> predicate)
        {
            List<int> idx = new List<int>();
            for (int i = 0; i < _files.Count; i++)
            {
                if (predicate(i))
                    idx.Add(i);
            }
            return idx;
        }

        // Snapshot of the tail-appended discovered files, in append (offset) order. Used by the
        // Primary view to extend past the frozen snapshot count.
        private IFsFile[] discoveredSnapshot()
        {
            lock (_discoveredSync)
                return _discovered.ToArray();
        }

        /// <summary>
        /// A view over the whole snapshot (Primary) or a subset by index map (System / per-FS).
        /// Indexed access; the enumerable iterates by index over the frozen list. When constructed
        /// with an owning <see cref="AreaFileSystemView"/> (the Primary view) it also surfaces the
        /// owner's tail-appended <see cref="DiscoveredFiles"/> past the frozen count.
        /// </summary>
        private sealed class FileSystemListView : IFileSystemView
        {
            private readonly IReadOnlyList<IFsFile> _all;
            private readonly IReadOnlyList<int> _indices; // null => whole list (Primary)
            private readonly AreaFileSystemView _owner;   // non-null only for the tail-extensible Primary view

            // Whole-list view (Primary / single-FS). Pass the owner to make it tail-extensible.
            public FileSystemListView(IReadOnlyList<IFsFile> all, IFsFolder root, FileSystemKind kind, AreaFileSystemView owner = null)
            {
                _all = all;
                _indices = null;
                _owner = owner;
                Root = root;
                Kind = kind;
            }

            // Subset view over an index map (System / per-FS projection).
            public FileSystemListView(IReadOnlyList<int> indices, IReadOnlyList<IFsFile> all, IFsFolder root, FileSystemKind kind)
            {
                _all = all;
                _indices = indices;
                _owner = null;
                Root = root;
                Kind = kind;
            }

            public FileSystemKind Kind { get; }

            public int FileCount
            {
                get
                {
                    if (_indices != null)
                        return _indices.Count;
                    int c = _all.Count;
                    if (_owner != null)
                        c += _owner.discoveredSnapshot().Length;
                    return c;
                }
            }

            public IFsFile File(int index)
            {
                if (_indices != null)
                    return _all[_indices[index]];
                if (index < _all.Count)
                    return _all[index];
                // tail-extended discovered file
                return _owner.discoveredSnapshot()[index - _all.Count];
            }

            public IFsFolder Root { get; }

            public IEnumerable<IFsFile> Files
            {
                get
                {
                    int count = FileCount; // capture the (immutable) count
                    for (int i = 0; i < count; i++)
                        yield return File(i);
                }
            }
        }
    }
}
using Nanook.NKit.Builder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiU
{
    /// <summary>
    /// Builds a complete WiiU NUS FST binary (used as content 0 of an AppTmd title).
    ///
    /// Layout:
    ///   0x00           FST header (0x20 bytes)
    ///   0x20           Content table records (N × 0x20 bytes)
    ///   (N+1)*0x20     File/directory entries (16 bytes each — WiiU extends Wii/GC 12-byte format)
    ///   After entries  Name string table (null-terminated Shift-JIS)
    ///
    /// Each file entry carries a <c>sectionNo</c> (uint16 at entry+0x0E) that identifies
    /// which content index the file belongs to.  Directories that contain files from only one
    /// content inherit that content's index; mixed-content directories use the index of their
    /// first child.
    ///
    /// The binary is not encrypted here — call
    /// <see cref="WiiUSecurity.EncryptFst"/> with the title key before writing to disk.
    /// </summary>
    internal sealed class WiiUFstBuilder
    {
        /// <summary>Describes one NUS content entry for the FST content table.</summary>
        internal sealed class ContentEntry
        {
            public int      Index;      // 0-based content index (matches TMD index)
            public AppType  Type;       // Fst=0, Code=1, Files=2
            public long     FsSize;     // total plaintext data bytes in this content
            public long     EncryptedSize; // actual .app file size on disk (set after encryption)
            public ulong    TitleId;    // title ID (same for all contents)
            public int      GroupId;    // group ID
            public ushort   Permission; // FST entry permission flags derived from GroupId
        }

        private readonly List<ContentEntry>     _contents  = new();
        private readonly FstBuilder              _tree      = new();
        // maps content index → ordered list of (relPath, size) for files in that content
        private readonly Dictionary<int, List<(string RelPath, long Size)>> _contentFiles = new();

        // ── Public API ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a content entry.  Must be added in index order (0, 1, 2 …).
        /// </summary>
        public ContentEntry AddContent(int index, AppType type, long fsSize, ulong titleId, int groupId = 0)
        {
            // Derive FST entry permission from GroupId:
            //   0x0000 (code/FST) → 0x0000 (system access only)
            //   0x0400 (meta)     → 0x0040 (boot/meta process)
            //   other  (content)  → 0x0400 (game process)
            ushort perm = groupId == 0x0000 ? (ushort)0x0000
                        : groupId == 0x0400 ? (ushort)0x0040
                        :                     (ushort)0x0400;
            var entry = new ContentEntry
            {
                Index      = index,
                Type       = type,
                FsSize     = fsSize,
                TitleId    = titleId,
                GroupId    = groupId,
                Permission = perm,
            };
            _contents.Add(entry);
            _contentFiles[index] = new List<(string, long)>();
            return entry;
        }

        /// <summary>
        /// Adds a file to the FST tree and associates it with a content index.
        /// <paramref name="relPath"/> uses '/' as separator (e.g. "code/game.rpx").
        /// <paramref name="offsetInContent"/> is the byte offset of this file within its
        /// content's data stream (needed for the FST file-offset field).
        /// </summary>
        public void AddFile(string relPath, long offsetInContent, long size, int contentIndex)
        {
            string dir  = relPath.Contains('/') ? relPath.Substring(0, relPath.LastIndexOf('/')) : "";
            string name = relPath.Contains('/') ? relPath.Substring(relPath.LastIndexOf('/') + 1) : relPath;
            _tree.AddFile(offsetInContent, name, dir, size);
            _contentFiles[contentIndex].Add((relPath, size));
        }

        /// <summary>
        /// Sets the encrypted size for a content entry after encryption is complete.
        /// Used to populate the FST content record's rawSz field (= encryptedSize / blockSize).
        /// </summary>
        public void SetEncryptedSize(int index, long encryptedSize)
        {
            ContentEntry ce = _contents.FirstOrDefault(c => c.Index == index);
            if (ce != null) ce.EncryptedSize = encryptedSize;
        }

        /// <summary>Sets the plaintext FsSize for a content entry (e.g. after lazy computation).</summary>
        public void SetFsSize(int index, long fsSize)
        {
            ContentEntry ce = _contents.FirstOrDefault(c => c.Index == index);
            if (ce != null) ce.FsSize = fsSize;
        }

        /// <summary>
        /// Serialises the complete NUS FST binary.
        /// <paramref name="multiplier"/> is stored in the FST header and divides all
        /// file data offsets in the entry table (use 1 for byte-addressed offsets).
        /// </summary>
        public byte[] ToArray(int multiplier = 1)
        {
            // ── 1. Build Wii/GC-style 12-byte entry section via FstBuilder ────────────
            // FstBuilder produces: [entries 12-byte each] [name table]
            // We need to expand each 12-byte entry to 16 bytes and add sectionNo.
            byte[] wiiTree = _tree.ToArray(0, multiplier == 1 ? 0 : (int)Math.Log(multiplier, 2));

            // Parse the Wii tree to extract entries + name table
            int entryCount = (int)wiiTree.ReadUInt32B(8); // Val3 of root = total entries
            int namesBase  = entryCount * 12; // 12 bytes per Wii entry
            byte[] nameTable = wiiTree.Skip(namesBase).ToArray();

            // ── 2. Build sectionNo map: for each file, which content does it belong to? ─
            // Build relPath→contentIndex lookup from the tree structure
            var sectionMap = buildSectionMap();

            // Build contentIndex→permission lookup from registered contents
            var permMap = _contents.ToDictionary(c => c.Index, c => c.Permission);

            // ── 3. Content table header ───────────────────────────────────────────────
            int n          = _contents.Count;
            int headerSize = (n + 1) * WiiUConsts.FstRecordSize;  // 1 FST header + N content records

            using var ms = new MemoryStream();
            byte[] _w4 = new byte[4];
            byte[] _w8 = new byte[8];
            byte[] _w2 = new byte[2];

            // FST header record (0x00..0x1F)
            _w4.WriteUInt32B(0, WiiUConsts.FstMagic); ms.Write(_w4, 0, 4);      // 'FST\0' magic — required by Image.cs reader
            _w4.WriteUInt32B(0, (uint)multiplier); ms.Write(_w4, 0, 4);          // multiplier
            _w4.WriteUInt32B(0, (uint)n); ms.Write(_w4, 0, 4);                   // content count
            ms.WriteByte(0);                                                      // hashDisabled = 0 (hashes enabled)
            ms.Write(new byte[0x13], 0, 0x13);                                   // padding to 0x20

            // Content records (one per content, sorted by index)
            // rawSz = ceil(encryptedSize / (multiplier * blockSize)) — multiplier=32 is standard for WiiU
            // The console uses rawSz * multiplier * blockSize as the content's footprint.
            // rawOff = 0 for all CDN contents (sequential layout).
            int effectiveBlock = multiplier * WiiUConsts.DefaultSectorSize;
            foreach (ContentEntry ce in _contents.OrderBy(c => c.Index))
            {
                // Content[0] is the FST itself — its rawSz in the FST content table is always 0
                // (the console reads the FST size from the TMD, not from this self-referential record).
                // All other contents use ceil(encryptedSize / effectiveBlock).
                uint rawSz = (ce.Index == 0) ? 0u
                    : ce.EncryptedSize > 0
                        ? (uint)((ce.EncryptedSize + effectiveBlock - 1) / effectiveBlock)
                        : 0;
                _w4.WriteUInt32B(0, 0);                    ms.Write(_w4, 0, 4); // offset in blocks (0 = CDN layout, sequential)
                _w4.WriteUInt32B(0, rawSz);                ms.Write(_w4, 0, 4); // fsSize in blocks
                _w8.WriteUInt64B(0, ce.TitleId);           ms.Write(_w8, 0, 8); // title ID (non-zero for content[9] game data)
                _w4.WriteUInt32B(0, (uint)ce.GroupId);     ms.Write(_w4, 0, 4); // group ID
                ms.WriteByte((byte)ce.Type);   // AppType
                ms.Write(new byte[0xB], 0, 0xB); // padding to 0x20
            }

            // ── 4. File/directory entries (16 bytes each, WiiU format) ─────────────────
            // Expand each 12-byte Wii entry to 16 bytes and patch in sectionNo.
            // Entry[0] is always the root directory — write it directly (sectionNo=0, permission=0)
            // and start path tracking from entry[1] so the root's name offset (which aliases
            // the first real name in the table) is never mistaken for a real directory name.
            var entryPath = new Stack<(string path, int endIdx)>();
            entryPath.Push(("", int.MaxValue));
            string currentPath = "";

            // Write root entry (i=0) without path tracking
            {
                uint val1 = wiiTree.ReadUInt32B(0);
                uint val2 = wiiTree.ReadUInt32B(4);
                uint val3 = wiiTree.ReadUInt32B(8);
                _w4.WriteUInt32B(0, val1); ms.Write(_w4, 0, 4);
                _w4.WriteUInt32B(0, val2); ms.Write(_w4, 0, 4);
                _w4.WriteUInt32B(0, val3); ms.Write(_w4, 0, 4);
                _w2.WriteUInt16B(0, 0);    ms.Write(_w2, 0, 2); // permission
                _w2.WriteUInt16B(0, 0);    ms.Write(_w2, 0, 2); // sectionNo
            }

            for (int i = 1; i < entryCount; i++)
            {
                int srcOff = i * 12;
                uint val1 = wiiTree.ReadUInt32B(srcOff);
                uint val2 = wiiTree.ReadUInt32B(srcOff + 4);
                uint val3 = wiiTree.ReadUInt32B(srcOff + 8);

                bool isDir        = (val1 >> 24) == 0x01;
                int  nameOff      = (int)(val1 & 0x00FFFFFF);
                string entryName  = readStringFromTable(nameTable, nameOff);

                // Track path for sectionNo lookup
                while (entryPath.Count > 1 && i >= entryPath.Peek().endIdx)
                {
                    entryPath.Pop();
                    currentPath = entryPath.Count > 1 ? entryPath.Peek().path : "";
                }

                string entryRelPath = currentPath.Length == 0 ? entryName
                                                               : $"{currentPath}/{entryName}";

                ushort sectionNo = 0;
                ushort permission = 0;
                if (!isDir && entryName.Length > 0)
                {
                    bool found = sectionMap.TryGetValue(entryRelPath, out int ci);
                    sectionNo  = (ushort)(found ? ci : 0);
                    permission = found ? permMap[ci] : (ushort)0;
                }
                else if (isDir && entryName.Length > 0)
                {
                    sectionNo  = (ushort)(getFirstContentInDir(entryRelPath, sectionMap));
                    permission = permMap.TryGetValue(sectionNo, out ushort dp) ? dp : (ushort)0;
                    entryPath.Push((entryRelPath, (int)val3));
                    currentPath = entryRelPath;
                }

                // Write 16-byte WiiU entry
                _w4.WriteUInt32B(0, val1); ms.Write(_w4, 0, 4);
                _w4.WriteUInt32B(0, val2); ms.Write(_w4, 0, 4);
                _w4.WriteUInt32B(0, val3); ms.Write(_w4, 0, 4);
                _w2.WriteUInt16B(0, permission); ms.Write(_w2, 0, 2); // permission flags derived from content GroupId
                _w2.WriteUInt16B(0, sectionNo);  ms.Write(_w2, 0, 2); // content index
            }

            // ── 5. Name table ─────────────────────────────────────────────────────────
            ms.Write(nameTable, 0, nameTable.Length);

            // Pad to 0x20-byte boundary
            long rem = ms.Length % 0x20;
            if (rem != 0)
                ms.Write(new byte[0x20 - rem], 0, (int)(0x20 - rem));

            return ms.ToArray();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private Dictionary<string, int> buildSectionMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _contentFiles)
                foreach (var (relPath, _) in kv.Value)
                    map[relPath] = kv.Key;
            return map;
        }

        private int getFirstContentInDir(string dirPath, Dictionary<string, int> sectionMap)
        {
            string prefix = dirPath.Length == 0 ? "" : dirPath + "/";
            // Use the highest content index in the directory — this matches real WiiU disc FSTs
            // where the primary executable (RPX, content[8]) determines the code/ directory section.
            int best = -1;
            foreach (var kv in sectionMap)
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    if (kv.Value > best) best = kv.Value;
            return best >= 0 ? best : 0;
        }

        private static string readStringFromTable(byte[] table, int offset)
        {
            if (offset >= table.Length) return "";
            int end = offset;
            while (end < table.Length && table[end] != 0) end++;
            return Encoding.GetEncoding("Shift-JIS").GetString(table, offset, end - offset);
        }
    }
}

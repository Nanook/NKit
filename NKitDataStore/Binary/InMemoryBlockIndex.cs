using NKitDataStore.Binary.Serialization;
using System.Collections.Concurrent;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Flat sorted-array block index for O(log N) deduplication lookups during writes.
    ///
    /// Memory design — 28 bytes per loaded entry, zero per-entry overhead:
    /// The loaded-from-disk portion is a contiguous sorted <see cref="BlockIndexEntry"/> struct
    /// array. A small <see cref="ConcurrentDictionary{TKey,TValue}"/> holds only blocks added
    /// in the current write session (typically thousands of entries, not millions).
    ///
    /// This replaces the previous design that loaded everything into a single
    /// ConcurrentDictionary (~72 bytes per entry) — a ~60 % reduction for large stores.
    ///
    /// Lookup strategy:
    ///   1. Session entries (O(1) hash lookup) — checked first, wins over sorted array.
    ///   2. Sorted loaded array (O(log N) binary search) — the main index loaded from disk.
    ///
    /// The Block_Index is loaded only during write operations; readers never access it.
    /// </summary>
    internal class InMemoryBlockIndex
    {
        // Fully merged sorted array — main block index + all persisted deltas merged in.
        // Immutable after construction. 28 bytes per entry, no per-entry overhead.
        private readonly BlockIndexEntry[] _sortedEntries;

        // Only blocks written in the current write session.
        // Stays small — cleared/serialized as a delta on commit and not accumulated cross-session.
        private readonly ConcurrentDictionary<BlockKey, BlockIndexEntry> _sessionEntries;

        /// <summary>
        /// Total entry count including potential key overlaps between sorted array and session dict.
        /// In steady state (end of a session) this equals the number of unique blocks.
        /// </summary>
        public int Count => this._sortedEntries.Length + this._sessionEntries.Count;

        /// <summary>
        /// Creates a new InMemoryBlockIndex from a fully-merged sorted entry array.
        /// </summary>
        /// <param name="sortedEntries">
        /// Pre-sorted array of <see cref="BlockIndexEntry"/> (sorted by XxHash64 then Crc32
        /// ascending). Pass null or empty to create an empty index.
        /// </param>
        public InMemoryBlockIndex(BlockIndexEntry[]? sortedEntries = null)
        {
            this._sortedEntries = sortedEntries != null && sortedEntries.Length > 0
                ? sortedEntries
                : Array.Empty<BlockIndexEntry>();
            this._sessionEntries = new ConcurrentDictionary<BlockKey, BlockIndexEntry>();
        }

        private InMemoryBlockIndex(BlockIndexEntry[] sortedEntries, ConcurrentDictionary<BlockKey, BlockIndexEntry> sessionEntries)
        {
            this._sortedEntries = sortedEntries;
            this._sessionEntries = sessionEntries;
        }

        /// <summary>
        /// Looks up a block by key. Checks the session dict first (O(1)) then binary-searches
        /// the sorted loaded array (O(log N), ~23 comparisons for 5 M entries).
        /// </summary>
        public bool TryGetBlock(BlockKey key, out int fileId, out long offset, out int size)
        {
            if (this._sessionEntries.TryGetValue(key, out BlockIndexEntry se))
            {
                fileId = se.FileId;
                offset = se.Offset;
                size = se.Size;
                return true;
            }

            int idx = binarySearch(this._sortedEntries, key);
            if (idx >= 0)
            {
                ref readonly BlockIndexEntry e = ref this._sortedEntries[idx];
                fileId = e.FileId;
                offset = e.Offset;
                size = e.Size;
                return true;
            }

            fileId = 0;
            offset = 0;
            size = 0;
            return false;
        }

        /// <summary>
        /// Merges delta entries into the session dict. Session entries win over the sorted array.
        /// Used for backward compatibility; prefer <see cref="WithMergedDeltas"/> at load time.
        /// </summary>
        public void MergeDelta(BlockIndexDelta delta)
        {
            if (delta == null || delta.Entries == null || delta.Entries.Length == 0)
                return;

            BlockIndexEntry[] deltaEntries = delta.Entries;
            for (int i = 0; i < deltaEntries.Length; i++)
                this._sessionEntries[deltaEntries[i].Key] = deltaEntries[i];
        }

        /// <summary>
        /// Adds a block written in the current write session. Session entries win over the sorted
        /// array on lookup. Thread-safe — called concurrently during parallel block compression.
        /// </summary>
        public void AddEntry(BlockIndexEntry entry) => this._sessionEntries[entry.Key] = entry;

        /// <summary>
        /// Returns a new <see cref="InMemoryBlockIndex"/> with all delta runs merged into the
        /// sorted array. Delta entries win over sorted entries on key conflict.
        /// Delta runs must be ordered newest-first (chain head first) so that among multiple
        /// runs sharing a key the head entry wins.
        /// Called once at load time from <see cref="BinaryIndexFile.LoadBlockIndex"/> after
        /// collecting all delta entry arrays from the delta chain.
        /// </summary>
        internal InMemoryBlockIndex WithMergedDeltas(List<BlockIndexEntry[]> deltaRunsNewestFirst)
        {
            if (deltaRunsNewestFirst == null || deltaRunsNewestFirst.Count == 0)
                return this;

            // Deduplicate across all delta runs — first occurrence wins (newest run first).
            HashSet<BlockKey> seen = new HashSet<BlockKey>();
            List<BlockIndexEntry> allDeltaEntries = new List<BlockIndexEntry>();

            for (int r = 0; r < deltaRunsNewestFirst.Count; r++)
            {
                BlockIndexEntry[] run = deltaRunsNewestFirst[r];
                for (int i = 0; i < run.Length; i++)
                {
                    if (seen.Add(run[i].Key))
                        allDeltaEntries.Add(run[i]);
                }
            }

            if (allDeltaEntries.Count == 0)
                return this;

            BlockIndexEntry[] deltaArr = allDeltaEntries.ToArray();
            Array.Sort(deltaArr);

            BlockIndexEntry[] merged = mergeSortedWithOverride(this._sortedEntries, deltaArr);
            return new InMemoryBlockIndex(merged, new ConcurrentDictionary<BlockKey, BlockIndexEntry>());
        }

        /// <summary>
        /// Returns a sorted copy of all entries (sorted array merged with session entries).
        /// Session entries win on key conflict. Used when serializing the block index to disk.
        /// </summary>
        public BlockIndexEntry[] GetEntries()
        {
            if (this._sessionEntries.IsEmpty)
                return this._sortedEntries.Length > 0
                    ? (BlockIndexEntry[])this._sortedEntries.Clone()
                    : Array.Empty<BlockIndexEntry>();

            BlockIndexEntry[] sessionArr = this._sessionEntries.Values.ToArray();
            Array.Sort(sessionArr);
            return mergeSortedWithOverride(this._sortedEntries, sessionArr);
        }

        /// <summary>
        /// Serializes the full block index (loaded + session) to a byte array.
        /// </summary>
        public byte[] Serialize(int sectionSize = 65536) => BlockIndexSerializer.Serialize(this.GetEntries(), sectionSize);

        /// <summary>
        /// Deserializes a Block_Index from a <see cref="LargeByteArray"/> (> 2 GiB capable).
        /// </summary>
        public static InMemoryBlockIndex Deserialize(LargeByteArray data)
        {
            BlockIndexEntry[] entries = Serialization.BlockIndexSerializer.Deserialize(data);
            return new InMemoryBlockIndex(entries);
        }

        /// <summary>
        /// Deserializes a Block_Index from its binary representation.
        /// </summary>
        public static InMemoryBlockIndex Deserialize(ReadOnlySpan<byte> data)
        {
            BlockIndexEntry[] entries = BlockIndexSerializer.Deserialize(data);
            return new InMemoryBlockIndex(entries);
        }

        /// <summary>
        /// Deserializes a Block_Index and a sequence of Block_Index_Deltas, merging them into
        /// a single contiguous sorted array. Delta entries win on key conflict (newer wins).
        /// Deltas must be ordered newest-first so that among multiple deltas with the same key
        /// the head (newest) entry is retained.
        /// </summary>
        public static InMemoryBlockIndex DeserializeWithDeltas(
            ReadOnlySpan<byte> mainData,
            IEnumerable<ReadOnlyMemory<byte>>? deltas)
        {
            BlockIndexEntry[] mainEntries = BlockIndexSerializer.Deserialize(mainData);
            InMemoryBlockIndex index = new InMemoryBlockIndex(mainEntries);

            if (deltas == null)
                return index;

            List<BlockIndexEntry[]> deltaRuns = new List<BlockIndexEntry[]>();
            foreach (ReadOnlyMemory<byte> deltaData in deltas)
                deltaRuns.Add(BlockIndexSerializer.Deserialize(deltaData.Span));

            return index.WithMergedDeltas(deltaRuns);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Binary search on a sorted <see cref="BlockIndexEntry"/> array by <see cref="BlockKey"/>.
        /// Returns the index if found, or a negative number if not found.
        /// </summary>
        private static int binarySearch(BlockIndexEntry[] entries, BlockKey key)
        {
            int lo = 0;
            int hi = entries.Length - 1;
            ulong xx = key.XxHash64;
            uint crc = key.Crc32;

            while (lo <= hi)
            {
                int mid = (int)((uint)(lo + hi) >> 1);
                ref readonly BlockIndexEntry e = ref entries[mid];

                if (e.Key.XxHash64 == xx && e.Key.Crc32 == crc)
                    return mid;

                if (e.Key.XxHash64 < xx || (e.Key.XxHash64 == xx && e.Key.Crc32 < crc))
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return ~lo;
        }

        /// <summary>
        /// Two-pointer merge of two pre-sorted <see cref="BlockIndexEntry"/> arrays.
        /// When the same key appears in both, the <paramref name="overrides"/> entry wins.
        /// </summary>
        private static BlockIndexEntry[] mergeSortedWithOverride(
            BlockIndexEntry[] sorted,
            BlockIndexEntry[] overrides)
        {
            if (sorted.Length == 0)
                return overrides.Length > 0 ? (BlockIndexEntry[])overrides.Clone() : Array.Empty<BlockIndexEntry>();
            if (overrides.Length == 0)
                return (BlockIndexEntry[])sorted.Clone();

            List<BlockIndexEntry> result = new List<BlockIndexEntry>(sorted.Length + overrides.Length);
            int si = 0;
            int oi = 0;

            while (si < sorted.Length && oi < overrides.Length)
            {
                int cmp = sorted[si].CompareTo(overrides[oi]);
                if (cmp < 0)
                {
                    result.Add(sorted[si++]);
                }
                else if (cmp > 0)
                {
                    result.Add(overrides[oi++]);
                }
                else
                {
                    // Same key — override (delta / session) wins
                    result.Add(overrides[oi++]);
                    si++; // skip the sorted entry
                }
            }

            while (si < sorted.Length)
                result.Add(sorted[si++]);
            while (oi < overrides.Length)
                result.Add(overrides[oi++]);

            return result.ToArray();
        }
    }
}
using NKitDataStore.Binary.Serialization;
using System.Collections.Concurrent;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Dictionary-backed block index for O(1) deduplication lookups during writes.
    /// The Block_Index is only needed for deduplication during writes — readers never touch it.
    /// Uses a ConcurrentDictionary for thread-safe O(1) lookups and insertions.
    /// Entries are sorted by (XxHash64, Crc32) ascending only when serialized to disk.
    /// </summary>
    internal class InMemoryBlockIndex
    {
        private readonly ConcurrentDictionary<BlockKey, BlockIndexEntry> _entries;

        /// <summary>
        /// Gets the number of entries in the block index.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Creates a new InMemoryBlockIndex with the given entries.
        /// </summary>
        /// <param name="sortedEntries">
        /// An array of BlockIndexEntry to populate the index. If null, an empty index is created.
        /// </param>
        public InMemoryBlockIndex(BlockIndexEntry[]? sortedEntries = null)
        {
            if (sortedEntries == null || sortedEntries.Length == 0)
            {
                _entries = new ConcurrentDictionary<BlockKey, BlockIndexEntry>();
            }
            else
            {
                _entries = new ConcurrentDictionary<BlockKey, BlockIndexEntry>(
                    Environment.ProcessorCount,
                    sortedEntries.Length);

                for (int i = 0; i < sortedEntries.Length; i++)
                    _entries[sortedEntries[i].Key] = sortedEntries[i];
            }
        }

        /// <summary>
        /// Looks up the given BlockKey and returns the block's shard location if found.
        /// O(1) average-case lookup.
        /// </summary>
        /// <param name="key">The BlockKey to search for.</param>
        /// <param name="fileId">The shard file ID where the block is stored (if found).</param>
        /// <param name="offset">The byte offset within the shard file (if found).</param>
        /// <param name="size">The compressed size of the block in the shard (if found).</param>
        /// <returns>True if the block was found; false otherwise.</returns>
        public bool TryGetBlock(BlockKey key, out int fileId, out long offset, out int size)
        {
            if (_entries.TryGetValue(key, out BlockIndexEntry entry))
            {
                fileId = entry.FileId;
                offset = entry.Offset;
                size = entry.Size;
                return true;
            }

            fileId = 0;
            offset = 0;
            size = 0;
            return false;
        }

        /// <summary>
        /// Merges a BlockIndexDelta into the main index.
        /// When the same BlockKey exists in both, the delta entry wins (newer entry wins).
        /// </summary>
        /// <param name="delta">The delta containing entries to merge.</param>
        public void MergeDelta(BlockIndexDelta delta)
        {
            if (delta == null || delta.Entries == null || delta.Entries.Length == 0)
                return;

            BlockIndexEntry[] deltaEntries = delta.Entries;
            for (int i = 0; i < deltaEntries.Length; i++)
                _entries[deltaEntries[i].Key] = deltaEntries[i];
        }

        /// <summary>
        /// Adds a single entry into the index.
        /// Used for adding new blocks during a write operation.
        /// If the same BlockKey already exists, the new entry unconditionally replaces it (newer wins).
        /// O(1) average-case insertion.
        /// </summary>
        /// <param name="entry">The entry to insert.</param>
        public void AddEntry(BlockIndexEntry entry) => _entries[entry.Key] = entry;

        /// <summary>
        /// Returns a sorted copy of all entries as an array.
        /// Sorted by (XxHash64, Crc32) ascending for serialization compatibility.
        /// </summary>
        /// <returns>A sorted copy of all block index entries.</returns>
        public BlockIndexEntry[] GetEntries()
        {
            BlockIndexEntry[] result = _entries.Values.ToArray();
            Array.Sort(result);
            return result;
        }

        /// <summary>
        /// Serializes the block index to a byte array using <see cref="BlockIndexSerializer"/>.
        /// </summary>
        /// <param name="sectionSize">
        /// The uncompressed section size in bytes. Default is 65536 (2048 entries at 32 bytes each).
        /// </param>
        /// <returns>The serialized byte array.</returns>
        public byte[] Serialize(int sectionSize = 65536) => BlockIndexSerializer.Serialize(GetEntries(), sectionSize);

        /// <summary>
        /// Deserializes a Block_Index from its binary representation and wraps it in a new InMemoryBlockIndex.
        /// </summary>
        /// <param name="data">The raw Block_Index bytes.</param>
        /// <returns>A new <see cref="InMemoryBlockIndex"/> populated with the deserialized entries.</returns>
        public static InMemoryBlockIndex Deserialize(ReadOnlySpan<byte> data)
        {
            BlockIndexEntry[] entries = BlockIndexSerializer.Deserialize(data);
            return new InMemoryBlockIndex(entries);
        }

        /// <summary>
        /// Deserializes a main Block_Index and a sequence of Block_Index_Deltas,
        /// merging them into a single InMemoryBlockIndex.
        /// Deltas are merged in order, with duplicate BlockKeys resolved by newer entry wins.
        /// </summary>
        /// <param name="mainData">The raw bytes of the main Block_Index.</param>
        /// <param name="deltas">The raw bytes of each Block_Index_Delta to merge.</param>
        /// <returns>A new <see cref="InMemoryBlockIndex"/> with all deltas merged.</returns>
        public static InMemoryBlockIndex DeserializeWithDeltas(ReadOnlySpan<byte> mainData,
                                                               IEnumerable<ReadOnlyMemory<byte>> deltas)
        {
            InMemoryBlockIndex index = Deserialize(mainData);

            if (deltas != null)
            {
                foreach (ReadOnlyMemory<byte> deltaData in deltas)
                {
                    BlockIndexEntry[] deltaEntries = BlockIndexSerializer.Deserialize(deltaData.Span);
                    BlockIndexDelta delta = new BlockIndexDelta { Entries = deltaEntries };
                    index.MergeDelta(delta);
                }
            }

            return index;
        }
    }
}
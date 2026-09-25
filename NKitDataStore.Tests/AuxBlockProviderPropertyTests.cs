using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for AuxBlockProvider read resolution.
    ///
    /// Feature: sidecar-datastore
    /// Property 4: Read Resolution Completeness
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    public class AuxBlockProviderPropertyTests
    {
        /// <summary>
        /// A stub IBlockProvider that returns a BlockRecord for any key in its known set,
        /// and null otherwise. Tracks which keys were queried.
        /// </summary>
        private class StubBlockProvider : IBlockProvider
        {
            private readonly Dictionary<BlockKey, BlockRecord> _blocks;
            private readonly List<BlockKey> _queriedKeys = new();

            public IReadOnlyList<BlockKey> QueriedKeys => _queriedKeys;

            public StubBlockProvider(Dictionary<BlockKey, BlockRecord> blocks)
            {
                _blocks = blocks;
            }

            public BlockRecord GetBlock(BlockKey key)
            {
                _queriedKeys.Add(key);
                return _blocks.TryGetValue(key, out BlockRecord record) ? record : null;
            }

            public Task<BlockRecord> GetBlockAsync(BlockKey key) => Task.FromResult(GetBlock(key));

            public BlockRecord GetBlock(OffsetRecord record, int blockIndex)
            {
                BlockKey key = record.GetBlockAt(blockIndex);
                return GetBlock(key);
            }
        }

        /// <summary>
        /// A stub IImageReader that returns a BlockRecord for any key in its known set,
        /// and null otherwise. Only GetBlock(BlockKey) is used by AuxBlockProvider;
        /// other members throw NotImplementedException.
        /// </summary>
        private class StubImageReader : IImageReader
        {
            private readonly Dictionary<BlockKey, BlockRecord> _blocks;
            private readonly List<BlockKey> _queriedKeys = new();

            public IReadOnlyList<BlockKey> QueriedKeys => _queriedKeys;

            public StubImageReader(Dictionary<BlockKey, BlockRecord> blocks)
            {
                _blocks = blocks;
            }

            public BlockRecord GetBlock(BlockKey key)
            {
                _queriedKeys.Add(key);
                return _blocks.TryGetValue(key, out BlockRecord record) ? record : null;
            }

            // Unused members — AuxBlockProvider only calls GetBlock(BlockKey)
            public ImageRecord Image => throw new NotImplementedException();
            public InfoRecord Info => throw new NotImplementedException();
            public Stream OpenStream(long offsetStart) => throw new NotImplementedException();
            public Stream OpenStream(DataStride stride, long offsetStart) => throw new NotImplementedException();
            public IEnumerable<OffsetRecord> GetOffsets() => throw new NotImplementedException();
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => throw new NotImplementedException();
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => throw new NotImplementedException();
            public Stream OpenBlockStream(BlockKey key) => throw new NotImplementedException();
            public IEnumerable<AreaRecord> GetAreas() => throw new NotImplementedException();
            public byte[] ReadFile(string name) => throw new NotImplementedException();
            public IEnumerable<FileRecord> ListFiles() => throw new NotImplementedException();
            public void Dispose() { }
        }

        /// <summary>
        /// Creates a BlockKey from a seed value. Each unique seed produces a unique key.
        /// </summary>
        private static BlockKey MakeKey(ulong seed) => new BlockKey(seed, (uint)(seed & 0xFFFFFFFF));

        /// <summary>
        /// Creates a BlockRecord for a given key with minimal test data.
        /// </summary>
        private static BlockRecord MakeRecord(BlockKey key) => new BlockRecord(key, CompressionType.None, new byte[] { (byte)(key.XxHash64 & 0xFF) });

        /// <summary>
        /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
        ///
        /// Property 4: Read Resolution Completeness (GetBlock by BlockKey).
        /// For any block key K, AuxBlockProvider.GetBlock(K) returns the block record
        /// if K exists in the primary store OR the aux store, and returns null if K
        /// exists in neither. Primary is always consulted first.
        ///
        /// We generate N keys and distribute them across three categories:
        ///   - primary-only: key exists in primary but not aux
        ///   - aux-only: key exists in aux but not primary
        ///   - neither: key exists in neither store
        ///
        /// For each key we verify:
        ///   1. Primary-only keys → returns the primary block record
        ///   2. Aux-only keys → returns the aux block record
        ///   3. Neither keys → returns null
        ///   4. Primary keys are never looked up in aux (short-circuit)
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ReadResolutionCompleteness_GetBlockByKey(
            NonNegativeInt primaryCountSeed,
            NonNegativeInt auxCountSeed,
            NonNegativeInt neitherCountSeed)
        {
            // Constrain counts to reasonable ranges (1–20 each)
            int primaryCount = (primaryCountSeed.Get % 20) + 1;
            int auxCount = (auxCountSeed.Get % 20) + 1;
            int neitherCount = (neitherCountSeed.Get % 20) + 1;

            // Generate unique keys for each category using non-overlapping seed ranges
            ulong offset = 1; // start at 1 to avoid default BlockKey
            List<BlockKey> primaryKeys = Enumerable.Range(0, primaryCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)primaryCount;
            List<BlockKey> auxKeys = Enumerable.Range(0, auxCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)auxCount;
            List<BlockKey> neitherKeys = Enumerable.Range(0, neitherCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            // Build block dictionaries
            Dictionary<BlockKey, BlockRecord> primaryBlocks = primaryKeys.ToDictionary(k => k, k => MakeRecord(k));
            Dictionary<BlockKey, BlockRecord> auxBlocks = auxKeys.ToDictionary(k => k, k => MakeRecord(k));

            // Create stubs
            StubBlockProvider primaryProvider = new StubBlockProvider(primaryBlocks);
            StubImageReader auxReader = new StubImageReader(auxBlocks);

            // Create AuxBlockProvider under test
            AuxBlockProvider provider = new AuxBlockProvider(primaryProvider, auxReader);

            // Verify primary-only keys: should return primary record, aux NOT consulted
            foreach (BlockKey key in primaryKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result == null) return false;
                if (result.Key != key) return false;
            }

            // Requirement 4.1: Primary is always consulted first.
            // After querying primary-only keys, aux should NOT have been queried for those keys.
            if (auxReader.QueriedKeys.Any(k => primaryKeys.Contains(k)))
                return false;

            // Verify aux-only keys: should return aux record
            foreach (BlockKey key in auxKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result == null) return false;
                if (result.Key != key) return false;
            }

            // Verify neither keys: should return null
            foreach (BlockKey key in neitherKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result != null) return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
        ///
        /// Property 4: Read Resolution Completeness (GetBlock by OffsetRecord + blockIndex).
        /// Same property as above but exercised through the OffsetRecord overload.
        /// For any block key K embedded in an OffsetRecord, AuxBlockProvider.GetBlock(record, index)
        /// returns the block record if K exists in primary OR aux, and null if in neither.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ReadResolutionCompleteness_GetBlockByOffsetRecord(
            NonNegativeInt primaryCountSeed,
            NonNegativeInt auxCountSeed,
            NonNegativeInt neitherCountSeed)
        {
            int primaryCount = (primaryCountSeed.Get % 10) + 1;
            int auxCount = (auxCountSeed.Get % 10) + 1;
            int neitherCount = (neitherCountSeed.Get % 10) + 1;

            ulong offset = 1;
            List<BlockKey> primaryKeys = Enumerable.Range(0, primaryCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)primaryCount;
            List<BlockKey> auxKeys = Enumerable.Range(0, auxCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)auxCount;
            List<BlockKey> neitherKeys = Enumerable.Range(0, neitherCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            Dictionary<BlockKey, BlockRecord> primaryBlocks = primaryKeys.ToDictionary(k => k, k => MakeRecord(k));
            Dictionary<BlockKey, BlockRecord> auxBlocks = auxKeys.ToDictionary(k => k, k => MakeRecord(k));

            StubBlockProvider primaryProvider = new StubBlockProvider(primaryBlocks);
            StubImageReader auxReader = new StubImageReader(auxBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(primaryProvider, auxReader);

            // Helper: wrap a single key in an OffsetRecord at blockIndex 0
            OffsetRecord MakeOffset(BlockKey key) => new OffsetRecord
            {
                Offset = 0,
                Size = 65536,
                Type = BlockType.File,
                OffsetStart = 0,
                Blocks = new List<BlockKey> { key }
            };

            // Verify primary-only keys via OffsetRecord overload
            foreach (BlockKey key in primaryKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord result = provider.GetBlock(record, 0);
                if (result == null) return false;
                if (result.Key != key) return false;
            }

            // Verify aux-only keys via OffsetRecord overload
            foreach (BlockKey key in auxKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord result = provider.GetBlock(record, 0);
                if (result == null) return false;
                if (result.Key != key) return false;
            }

            // Verify neither keys via OffsetRecord overload
            foreach (BlockKey key in neitherKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord result = provider.GetBlock(record, 0);
                if (result != null) return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
        ///
        /// Property 4 (supplementary): Primary-first resolution order.
        /// When a key exists in BOTH primary and aux, the primary record is returned
        /// and the aux store is never consulted. This confirms the resolution chain
        /// always prefers primary.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ReadResolution_PrimaryTakesPrecedenceOverAux(NonNegativeInt countSeed)
        {
            int count = (countSeed.Get % 20) + 1;

            // Generate keys that exist in BOTH stores with distinguishable records
            List<BlockKey> keys = Enumerable.Range(0, count).Select(i => MakeKey((ulong)(i + 1))).ToList();

            Dictionary<BlockKey, BlockRecord> primaryBlocks = keys.ToDictionary(k => k, k =>
                new BlockRecord(k, CompressionType.None, new byte[] { 0xAA }));
            Dictionary<BlockKey, BlockRecord> auxBlocks = keys.ToDictionary(k => k, k =>
                new BlockRecord(k, CompressionType.None, new byte[] { 0xBB }));

            StubBlockProvider primaryProvider = new StubBlockProvider(primaryBlocks);
            StubImageReader auxReader = new StubImageReader(auxBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(primaryProvider, auxReader);

            foreach (BlockKey key in keys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result == null) return false;

                // Must be the primary record (0xAA), not the aux record (0xBB)
                if (result.Data[0] != 0xAA) return false;
            }

            // Aux should never have been consulted since primary always hit
            if (auxReader.QueriedKeys.Count != 0) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 5.1, 5.2, 11.1, 11.2**
        ///
        /// Property 5: Backward Compatibility (No-Aux Fallback).
        /// For any block key K, when aux reader is null, AuxBlockProvider delegates
        /// entirely to the primary provider with no exceptions or behavioral changes.
        ///
        /// We generate N keys, some present in the primary store and some not.
        /// With a null aux reader:
        ///   1. Keys in primary → returns the primary block record (identical to calling primary directly)
        ///   2. Keys not in primary → returns null (no exception, no behavioral change)
        ///   3. No exceptions are thrown at any point
        /// This proves that when aux is null, the system behaves identically to the
        /// pre-aux implementation.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BackwardCompatibility_NullAux_DelegatesToPrimaryOnly(
            NonNegativeInt presentCountSeed,
            NonNegativeInt absentCountSeed)
        {
            // Constrain counts to reasonable ranges (1–20 each)
            int presentCount = (presentCountSeed.Get % 20) + 1;
            int absentCount = (absentCountSeed.Get % 20) + 1;

            // Generate unique keys: some present in primary, some absent
            ulong offset = 1;
            List<BlockKey> presentKeys = Enumerable.Range(0, presentCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)presentCount;
            List<BlockKey> absentKeys = Enumerable.Range(0, absentCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            // Build primary store with only the present keys
            Dictionary<BlockKey, BlockRecord> primaryBlocks = presentKeys.ToDictionary(k => k, k => MakeRecord(k));

            // Create a primary-only provider (used as baseline) and a stub for AuxBlockProvider
            StubBlockProvider baselinePrimary = new StubBlockProvider(primaryBlocks);
            StubBlockProvider auxWrappedPrimary = new StubBlockProvider(primaryBlocks);

            // Create AuxBlockProvider with null aux reader
            AuxBlockProvider provider = new AuxBlockProvider(auxWrappedPrimary, null);

            // Verify present keys: AuxBlockProvider returns the same record as primary directly
            foreach (BlockKey key in presentKeys)
            {
                BlockRecord auxResult = provider.GetBlock(key);
                BlockRecord baselineResult = baselinePrimary.GetBlock(key);

                // Both must return non-null
                if (auxResult == null || baselineResult == null) return false;

                // Must be the same block (same key, same data)
                if (auxResult.Key != baselineResult.Key) return false;
                if (!auxResult.Data.SequenceEqual(baselineResult.Data)) return false;
            }

            // Verify absent keys: AuxBlockProvider returns null, same as primary directly
            foreach (BlockKey key in absentKeys)
            {
                BlockRecord auxResult = provider.GetBlock(key);
                BlockRecord baselineResult = baselinePrimary.GetBlock(key);

                // Both must return null
                if (auxResult != null) return false;
                if (baselineResult != null) return false;
            }

            // Verify the primary provider inside AuxBlockProvider was queried for every key
            int expectedQueryCount = presentCount + absentCount;
            if (auxWrappedPrimary.QueriedKeys.Count != expectedQueryCount) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 5.1, 5.2, 11.1, 11.2**
        ///
        /// Property 5 (supplementary): Backward Compatibility via OffsetRecord overload.
        /// Same property as above but exercised through the GetBlock(OffsetRecord, int) overload.
        /// When aux reader is null, the OffsetRecord-based lookup delegates entirely to the
        /// primary provider with no exceptions.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BackwardCompatibility_NullAux_OffsetRecordOverload(
            NonNegativeInt presentCountSeed,
            NonNegativeInt absentCountSeed)
        {
            int presentCount = (presentCountSeed.Get % 10) + 1;
            int absentCount = (absentCountSeed.Get % 10) + 1;

            ulong offset = 1;
            List<BlockKey> presentKeys = Enumerable.Range(0, presentCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)presentCount;
            List<BlockKey> absentKeys = Enumerable.Range(0, absentCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            Dictionary<BlockKey, BlockRecord> primaryBlocks = presentKeys.ToDictionary(k => k, k => MakeRecord(k));

            StubBlockProvider baselinePrimary = new StubBlockProvider(primaryBlocks);
            StubBlockProvider auxWrappedPrimary = new StubBlockProvider(primaryBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(auxWrappedPrimary, null);

            // Helper: wrap a single key in an OffsetRecord at blockIndex 0
            OffsetRecord MakeOffset(BlockKey key) => new OffsetRecord
            {
                Offset = 0,
                Size = 65536,
                Type = BlockType.File,
                OffsetStart = 0,
                Blocks = new List<BlockKey> { key }
            };

            // Verify present keys via OffsetRecord overload
            foreach (BlockKey key in presentKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord auxResult = provider.GetBlock(record, 0);
                BlockRecord baselineResult = baselinePrimary.GetBlock(key);

                if (auxResult == null || baselineResult == null) return false;
                if (auxResult.Key != baselineResult.Key) return false;
            }

            // Verify absent keys via OffsetRecord overload
            foreach (BlockKey key in absentKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord auxResult = provider.GetBlock(record, 0);

                if (auxResult != null) return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 5.1, 5.2, 11.1, 11.2**
        ///
        /// Property 5 (supplementary): Backward Compatibility async path.
        /// When aux reader is null, GetBlockAsync delegates entirely to the primary
        /// provider with no exceptions, matching synchronous behavior exactly.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BackwardCompatibility_NullAux_AsyncDelegatesToPrimaryOnly(
            NonNegativeInt presentCountSeed,
            NonNegativeInt absentCountSeed)
        {
            int presentCount = (presentCountSeed.Get % 20) + 1;
            int absentCount = (absentCountSeed.Get % 20) + 1;

            ulong offset = 1;
            List<BlockKey> presentKeys = Enumerable.Range(0, presentCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)presentCount;
            List<BlockKey> absentKeys = Enumerable.Range(0, absentCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            Dictionary<BlockKey, BlockRecord> primaryBlocks = presentKeys.ToDictionary(k => k, k => MakeRecord(k));
            StubBlockProvider auxWrappedPrimary = new StubBlockProvider(primaryBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(auxWrappedPrimary, null);

            // Verify present keys via async path
            foreach (BlockKey key in presentKeys)
            {
                BlockRecord result = provider.GetBlockAsync(key).GetAwaiter().GetResult();
                if (result == null) return false;
                if (result.Key != key) return false;
            }

            // Verify absent keys via async path — must return null, no exception
            foreach (BlockKey key in absentKeys)
            {
                BlockRecord result = provider.GetBlockAsync(key).GetAwaiter().GetResult();
                if (result != null) return false;
            }

            return true;
        }
    }
}
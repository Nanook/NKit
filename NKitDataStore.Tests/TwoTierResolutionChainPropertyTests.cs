using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for AuxBlockProvider two-tier resolution chain (Wii/WiiU).
    ///
    /// Feature: aux-split-mode
    /// Property 9: Two-Tier Resolution Chain Order (Wii)
    /// **Validates: Requirements 5.6**
    ///
    /// For any Wii/WiiU image (splitReader=null), the resolution chain SHALL remain:
    /// Primary → Shared Aux → null (no split reader involved).
    /// The addition of three-tier for Xbox must not break Wii's two-tier path.
    /// </summary>
    public class TwoTierResolutionChainPropertyTests
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
        /// and null otherwise. Only GetBlock(BlockKey) is used by AuxBlockProvider.
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
        /// Creates a BlockRecord for a given key with a distinguishing marker byte.
        /// </summary>
        private static BlockRecord MakeRecord(BlockKey key, byte marker) => new BlockRecord(key, CompressionType.None, new byte[] { marker });

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 9: Two-Tier Resolution Chain Order (Wii) — GetBlock by BlockKey.
        /// For any Wii/WiiU image (splitReader=null), the resolution chain remains:
        /// Primary → Shared Aux → null. No split reader is involved.
        ///
        /// We generate keys distributed across four categories:
        ///   - primary-only: exists in primary but not aux
        ///   - aux-only: exists in aux but not primary
        ///   - both: exists in both primary and aux (primary should win)
        ///   - neither: exists in neither store
        ///
        /// For each key we verify:
        ///   1. Primary-only → returns the primary record
        ///   2. Aux-only → returns the aux record (fallback works)
        ///   3. Both → returns the primary record (primary takes precedence)
        ///   4. Neither → returns null
        ///   5. Aux is only queried when primary misses (short-circuit preserved)
        /// </summary>
        [Property(MaxTest = 100)]
        public bool TwoTierResolution_GetBlockByKey_PrimaryThenAuxThenNull(
            NonNegativeInt primaryOnlyCountSeed,
            NonNegativeInt auxOnlyCountSeed,
            NonNegativeInt bothCountSeed,
            NonNegativeInt neitherCountSeed)
        {
            int primaryOnlyCount = (primaryOnlyCountSeed.Get % 15) + 1;
            int auxOnlyCount = (auxOnlyCountSeed.Get % 15) + 1;
            int bothCount = (bothCountSeed.Get % 10) + 1;
            int neitherCount = (neitherCountSeed.Get % 10) + 1;

            // Generate unique keys for each category using non-overlapping seed ranges
            ulong offset = 1;
            List<BlockKey> primaryOnlyKeys = Enumerable.Range(0, primaryOnlyCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)primaryOnlyCount;
            List<BlockKey> auxOnlyKeys = Enumerable.Range(0, auxOnlyCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)auxOnlyCount;
            List<BlockKey> bothKeys = Enumerable.Range(0, bothCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)bothCount;
            List<BlockKey> neitherKeys = Enumerable.Range(0, neitherCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            // Build block dictionaries with distinguishing markers
            const byte PRIMARY_MARKER = 0xAA;
            const byte AUX_MARKER = 0xBB;

            Dictionary<BlockKey, BlockRecord> primaryBlocks = new Dictionary<BlockKey, BlockRecord>();
            foreach (BlockKey k in primaryOnlyKeys) primaryBlocks[k] = MakeRecord(k, PRIMARY_MARKER);
            foreach (BlockKey k in bothKeys) primaryBlocks[k] = MakeRecord(k, PRIMARY_MARKER);

            Dictionary<BlockKey, BlockRecord> auxBlocks = new Dictionary<BlockKey, BlockRecord>();
            foreach (BlockKey k in auxOnlyKeys) auxBlocks[k] = MakeRecord(k, AUX_MARKER);
            foreach (BlockKey k in bothKeys) auxBlocks[k] = MakeRecord(k, AUX_MARKER);

            // Create stubs
            StubBlockProvider primaryProvider = new StubBlockProvider(primaryBlocks);
            StubImageReader auxReader = new StubImageReader(auxBlocks);

            // Create AuxBlockProvider in Wii two-tier mode: splitReader=null
            AuxBlockProvider provider = new AuxBlockProvider(primaryProvider, splitReader: null, auxReader: auxReader);

            // Verify: SplitReader should be null (Wii mode)
            if (provider.SplitReader != null) return false;

            // 1. Primary-only keys → primary record returned
            foreach (BlockKey key in primaryOnlyKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != PRIMARY_MARKER) return false;
            }

            // Primary-only keys should NOT have been queried on aux
            if (auxReader.QueriedKeys.Any(k => primaryOnlyKeys.Contains(k)))
                return false;

            // 2. Aux-only keys → aux record returned (fallback works)
            foreach (BlockKey key in auxOnlyKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != AUX_MARKER) return false;
            }

            // 3. Both keys → primary record wins
            foreach (BlockKey key in bothKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != PRIMARY_MARKER) return false;
            }

            // Both-keys should NOT have been queried on aux (primary hit short-circuits)
            if (auxReader.QueriedKeys.Any(k => bothKeys.Contains(k)))
                return false;

            // 4. Neither keys → null
            foreach (BlockKey key in neitherKeys)
            {
                BlockRecord result = provider.GetBlock(key);
                if (result != null) return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 9: Two-Tier Resolution Chain Order (Wii) — GetBlock by OffsetRecord.
        /// Same property exercised through the GetBlock(OffsetRecord, int) overload.
        /// For Wii/WiiU (splitReader=null), chain is: Primary → Shared Aux → null.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool TwoTierResolution_GetBlockByOffsetRecord_PrimaryThenAuxThenNull(
            NonNegativeInt primaryOnlyCountSeed,
            NonNegativeInt auxOnlyCountSeed,
            NonNegativeInt neitherCountSeed)
        {
            int primaryOnlyCount = (primaryOnlyCountSeed.Get % 10) + 1;
            int auxOnlyCount = (auxOnlyCountSeed.Get % 10) + 1;
            int neitherCount = (neitherCountSeed.Get % 10) + 1;

            ulong offset = 1;
            List<BlockKey> primaryOnlyKeys = Enumerable.Range(0, primaryOnlyCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)primaryOnlyCount;
            List<BlockKey> auxOnlyKeys = Enumerable.Range(0, auxOnlyCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)auxOnlyCount;
            List<BlockKey> neitherKeys = Enumerable.Range(0, neitherCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            const byte PRIMARY_MARKER = 0xAA;
            const byte AUX_MARKER = 0xBB;

            Dictionary<BlockKey, BlockRecord> primaryBlocks = primaryOnlyKeys.ToDictionary(k => k, k => MakeRecord(k, PRIMARY_MARKER));
            Dictionary<BlockKey, BlockRecord> auxBlocks = auxOnlyKeys.ToDictionary(k => k, k => MakeRecord(k, AUX_MARKER));

            StubBlockProvider primaryProvider = new StubBlockProvider(primaryBlocks);
            StubImageReader auxReader = new StubImageReader(auxBlocks);

            // Wii two-tier mode: splitReader=null
            AuxBlockProvider provider = new AuxBlockProvider(primaryProvider, splitReader: null, auxReader: auxReader);

            // Helper: wrap a single key in an OffsetRecord at blockIndex 0
            OffsetRecord MakeOffset(BlockKey key) => new OffsetRecord
            {
                Offset = 0,
                Size = 65536,
                Type = BlockType.File,
                OffsetStart = 0,
                Blocks = new List<BlockKey> { key }
            };

            // Primary-only keys via OffsetRecord overload
            foreach (BlockKey key in primaryOnlyKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord result = provider.GetBlock(record, 0);
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != PRIMARY_MARKER) return false;
            }

            // Aux-only keys via OffsetRecord overload
            foreach (BlockKey key in auxOnlyKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord result = provider.GetBlock(record, 0);
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != AUX_MARKER) return false;
            }

            // Neither keys via OffsetRecord overload
            foreach (BlockKey key in neitherKeys)
            {
                OffsetRecord record = MakeOffset(key);
                BlockRecord result = provider.GetBlock(record, 0);
                if (result != null) return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 9: Two-Tier Resolution Chain Order (Wii) — Async GetBlockAsync.
        /// For Wii/WiiU (splitReader=null), the async path also resolves as:
        /// Primary → Shared Aux → null, preserving the same two-tier behavior.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool TwoTierResolution_GetBlockAsync_PrimaryThenAuxThenNull(
            NonNegativeInt primaryOnlyCountSeed,
            NonNegativeInt auxOnlyCountSeed,
            NonNegativeInt neitherCountSeed)
        {
            int primaryOnlyCount = (primaryOnlyCountSeed.Get % 10) + 1;
            int auxOnlyCount = (auxOnlyCountSeed.Get % 10) + 1;
            int neitherCount = (neitherCountSeed.Get % 10) + 1;

            ulong offset = 1;
            List<BlockKey> primaryOnlyKeys = Enumerable.Range(0, primaryOnlyCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)primaryOnlyCount;
            List<BlockKey> auxOnlyKeys = Enumerable.Range(0, auxOnlyCount).Select(i => MakeKey(offset + (ulong)i)).ToList();
            offset += (ulong)auxOnlyCount;
            List<BlockKey> neitherKeys = Enumerable.Range(0, neitherCount).Select(i => MakeKey(offset + (ulong)i)).ToList();

            const byte PRIMARY_MARKER = 0xAA;
            const byte AUX_MARKER = 0xBB;

            Dictionary<BlockKey, BlockRecord> primaryBlocks = primaryOnlyKeys.ToDictionary(k => k, k => MakeRecord(k, PRIMARY_MARKER));
            Dictionary<BlockKey, BlockRecord> auxBlocks = auxOnlyKeys.ToDictionary(k => k, k => MakeRecord(k, AUX_MARKER));

            StubBlockProvider primaryProvider = new StubBlockProvider(primaryBlocks);
            StubImageReader auxReader = new StubImageReader(auxBlocks);

            // Wii two-tier mode: splitReader=null
            AuxBlockProvider provider = new AuxBlockProvider(primaryProvider, splitReader: null, auxReader: auxReader);

            // Primary-only keys via async path
            foreach (BlockKey key in primaryOnlyKeys)
            {
                BlockRecord result = provider.GetBlockAsync(key).GetAwaiter().GetResult();
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != PRIMARY_MARKER) return false;
            }

            // Aux-only keys via async path
            foreach (BlockKey key in auxOnlyKeys)
            {
                BlockRecord result = provider.GetBlockAsync(key).GetAwaiter().GetResult();
                if (result == null) return false;
                if (result.Key != key) return false;
                if (result.Data[0] != AUX_MARKER) return false;
            }

            // Neither keys via async path — must return null
            foreach (BlockKey key in neitherKeys)
            {
                BlockRecord result = provider.GetBlockAsync(key).GetAwaiter().GetResult();
                if (result != null) return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 9 (supplementary): Two-tier backward-compatible constructor equivalence.
        /// The two-parameter constructor AuxBlockProvider(primary, auxReader) produces
        /// identical behavior to the three-parameter constructor with splitReader=null.
        /// This ensures the Wii/WiiU code path using the backward-compatible API
        /// gets the same two-tier resolution chain.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool TwoTierResolution_BackwardCompatConstructor_EquivalentBehavior(
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

            Dictionary<BlockKey, BlockRecord> primaryBlocks = primaryKeys.ToDictionary(k => k, k => MakeRecord(k, 0xAA));
            Dictionary<BlockKey, BlockRecord> auxBlocks = auxKeys.ToDictionary(k => k, k => MakeRecord(k, 0xBB));

            // Create two providers: one with 2-param ctor, one with 3-param ctor (splitReader=null)
            StubBlockProvider primaryProvider1 = new StubBlockProvider(new Dictionary<BlockKey, BlockRecord>(primaryBlocks));
            StubImageReader auxReader1 = new StubImageReader(new Dictionary<BlockKey, BlockRecord>(auxBlocks));
            AuxBlockProvider twoParamProvider = new AuxBlockProvider(primaryProvider1, auxReader1);

            StubBlockProvider primaryProvider2 = new StubBlockProvider(new Dictionary<BlockKey, BlockRecord>(primaryBlocks));
            StubImageReader auxReader2 = new StubImageReader(new Dictionary<BlockKey, BlockRecord>(auxBlocks));
            AuxBlockProvider threeParamProvider = new AuxBlockProvider(primaryProvider2, splitReader: null, auxReader: auxReader2);

            // Both should have SplitReader == null
            if (twoParamProvider.SplitReader != null) return false;
            if (threeParamProvider.SplitReader != null) return false;

            // Both should produce identical results for all keys
            List<BlockKey> allKeys = primaryKeys.Concat(auxKeys).Concat(neitherKeys).ToList();
            foreach (BlockKey key in allKeys)
            {
                BlockRecord result1 = twoParamProvider.GetBlock(key);
                BlockRecord result2 = threeParamProvider.GetBlock(key);

                if (result1 == null && result2 == null) continue;
                if (result1 == null || result2 == null) return false;
                if (result1.Key != result2.Key) return false;
                if (!result1.Data.SequenceEqual(result2.Data)) return false;
            }

            return true;
        }
    }
}
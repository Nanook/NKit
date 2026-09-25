using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AuxBlockProvider"/> edge cases and error handling.
    /// Validates Requirements 4.1, 4.2, 4.3, 4.4, 5.2.
    /// </summary>
    public class AuxBlockProviderUnitTests
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

            public Task<BlockRecord> GetBlockAsync(BlockKey key)
                => Task.FromResult(GetBlock(key));

            public BlockRecord GetBlock(OffsetRecord record, int blockIndex)
            {
                BlockKey key = record.GetBlockAt(blockIndex);
                return GetBlock(key);
            }
        }

        /// <summary>
        /// A stub IImageReader that returns a BlockRecord for any key in its known set.
        /// Only GetBlock(BlockKey) is used by AuxBlockProvider.
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

        private static BlockKey makeKey(ulong seed) => new BlockKey(seed, (uint)(seed & 0xFFFFFFFF));
        private static BlockRecord makeRecord(BlockKey key) =>
            new BlockRecord(key, CompressionType.None, new byte[] { (byte)(key.XxHash64 & 0xFF) });

        // ── 7.1: GetBlock_PrimaryHit_ReturnsWithoutAuxLookup ─────────────

        /// <summary>
        /// Validates Requirement 4.1: Primary store is always consulted first.
        /// When the primary provider has the block, the aux reader is never queried.
        /// </summary>
        [Fact]
        public void GetBlock_PrimaryHit_ReturnsWithoutAuxLookup()
        {
            // Arrange
            BlockKey key = makeKey(42);
            BlockRecord primaryRecord = makeRecord(key);

            Dictionary<BlockKey, BlockRecord> primaryBlocks = new Dictionary<BlockKey, BlockRecord> { { key, primaryRecord } };
            Dictionary<BlockKey, BlockRecord> auxBlocks = new Dictionary<BlockKey, BlockRecord> { { key, makeRecord(key) } };

            StubBlockProvider primary = new StubBlockProvider(primaryBlocks);
            StubImageReader aux = new StubImageReader(auxBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(primary, aux);

            // Act
            BlockRecord result = provider.GetBlock(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(key, result!.Key);
            Assert.Single(primary.QueriedKeys);   // primary was consulted
            Assert.Empty(aux.QueriedKeys);         // aux was NOT consulted
        }

        // ── 7.1: GetBlock_PrimaryMiss_AuxHit_ReturnsAuxBlock ─────────────

        /// <summary>
        /// Validates Requirements 4.2, 4.3: When primary misses, aux is consulted
        /// and its block record is returned.
        /// </summary>
        [Fact]
        public void GetBlock_PrimaryMiss_AuxHit_ReturnsAuxBlock()
        {
            // Arrange
            BlockKey key = makeKey(99);
            BlockRecord auxRecord = makeRecord(key);

            Dictionary<BlockKey, BlockRecord> primaryBlocks = new Dictionary<BlockKey, BlockRecord>(); // empty primary
            Dictionary<BlockKey, BlockRecord> auxBlocks = new Dictionary<BlockKey, BlockRecord> { { key, auxRecord } };

            StubBlockProvider primary = new StubBlockProvider(primaryBlocks);
            StubImageReader aux = new StubImageReader(auxBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(primary, aux);

            // Act
            BlockRecord result = provider.GetBlock(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(key, result!.Key);
            Assert.Single(primary.QueriedKeys);    // primary was consulted first
            Assert.Single(aux.QueriedKeys);        // aux was consulted as fallback
        }

        // ── 7.1: GetBlock_BothMiss_ReturnsNull ──────────────────────────

        /// <summary>
        /// Validates Requirement 4.4: When block is absent from both stores,
        /// GetBlock returns null.
        /// </summary>
        [Fact]
        public void GetBlock_BothMiss_ReturnsNull()
        {
            // Arrange
            BlockKey key = makeKey(777);

            StubBlockProvider primary = new StubBlockProvider(new Dictionary<BlockKey, BlockRecord>());
            StubImageReader aux = new StubImageReader(new Dictionary<BlockKey, BlockRecord>());
            AuxBlockProvider provider = new AuxBlockProvider(primary, aux);

            // Act
            BlockRecord result = provider.GetBlock(key);

            // Assert
            Assert.Null(result);
            Assert.Single(primary.QueriedKeys);    // primary was consulted
            Assert.Single(aux.QueriedKeys);        // aux was consulted
        }

        // ── 7.1: GetBlock_NullAuxReader_ReturnsNullOnPrimaryMiss ─────────

        /// <summary>
        /// Validates Requirement 5.2: When aux reader is null and primary misses,
        /// GetBlock returns null gracefully (no NullReferenceException).
        /// </summary>
        [Fact]
        public void GetBlock_NullAuxReader_ReturnsNullOnPrimaryMiss()
        {
            // Arrange
            BlockKey key = makeKey(123);

            StubBlockProvider primary = new StubBlockProvider(new Dictionary<BlockKey, BlockRecord>());
            AuxBlockProvider provider = new AuxBlockProvider(primary, auxReader: null);

            // Act
            BlockRecord result = provider.GetBlock(key);

            // Assert
            Assert.Null(result);
            Assert.Single(primary.QueriedKeys);    // primary was consulted
        }

        // ── Supplementary: OffsetRecord overload mirrors BlockKey behavior ──

        /// <summary>
        /// Validates Requirements 4.1, 4.2: The OffsetRecord overload follows
        /// the same primary → aux resolution chain as the BlockKey overload.
        /// </summary>
        [Fact]
        public void GetBlock_OffsetRecord_PrimaryMiss_AuxHit_ReturnsAuxBlock()
        {
            // Arrange
            BlockKey key = makeKey(55);
            BlockRecord auxRecord = makeRecord(key);

            Dictionary<BlockKey, BlockRecord> primaryBlocks = new Dictionary<BlockKey, BlockRecord>();
            Dictionary<BlockKey, BlockRecord> auxBlocks = new Dictionary<BlockKey, BlockRecord> { { key, auxRecord } };

            StubBlockProvider primary = new StubBlockProvider(primaryBlocks);
            StubImageReader aux = new StubImageReader(auxBlocks);
            AuxBlockProvider provider = new AuxBlockProvider(primary, aux);

            OffsetRecord offsetRecord = new OffsetRecord
            {
                Offset = 0,
                Size = 65536,
                Type = BlockType.File,
                OffsetStart = 0,
                Blocks = new List<BlockKey> { key }
            };

            // Act
            BlockRecord result = provider.GetBlock(offsetRecord, 0);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(key, result!.Key);
        }

        /// <summary>
        /// Validates Requirement 4.1: Constructor rejects null primary provider.
        /// </summary>
        [Fact]
        public void Constructor_NullPrimaryProvider_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() => new AuxBlockProvider(null!, null));
    }
}
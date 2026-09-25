using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for AuxBlockProvider selective exception handling.
    ///
    /// Feature: aux-split-mode
    /// Property 13: Selective Exception Handling in GetBlock
    /// **Validates: Requirements 9.1, 9.2, 9.3**
    /// </summary>
    public class AuxBlockProviderSelectiveExceptionPropertyTests
    {
        /// <summary>
        /// A stub IBlockProvider that throws a configurable exception from GetBlock(OffsetRecord, int).
        /// The GetBlock(BlockKey) overload returns null to simulate "not found."
        /// </summary>
        private class ThrowingBlockProvider : IBlockProvider
        {
            private readonly Exception _exceptionToThrow;

            public ThrowingBlockProvider(Exception exceptionToThrow)
            {
                _exceptionToThrow = exceptionToThrow;
            }

            public BlockRecord GetBlock(BlockKey key) => null;

            public Task<BlockRecord> GetBlockAsync(BlockKey key) => Task.FromResult<BlockRecord>(null);

            public BlockRecord GetBlock(OffsetRecord record, int blockIndex) => throw _exceptionToThrow;
        }

        /// <summary>
        /// A stub IImageReader that returns a BlockRecord for any key in its known set,
        /// and null otherwise. Tracks which keys were queried.
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

        private static BlockKey makeKey(ulong seed) => new BlockKey(seed, (uint)(seed & 0xFFFFFFFF));

        private static BlockRecord makeRecord(BlockKey key) =>
            new BlockRecord(key, CompressionType.None, new byte[] { (byte)(key.XxHash64 & 0xFF) });

        private static OffsetRecord makeOffset(BlockKey key) => new OffsetRecord
        {
            Offset = 0,
            Size = 65536,
            Type = BlockType.File,
            OffsetStart = 0,
            Blocks = new List<BlockKey> { key }
        };

        /// <summary>
        /// **Validates: Requirements 9.1, 9.2**
        ///
        /// Property 13a: KeyNotFoundException from primary is caught, split/aux lookup attempted.
        /// For any block key present in the split reader, when the primary provider throws
        /// KeyNotFoundException from GetBlock(OffsetRecord, int), the AuxBlockProvider SHALL
        /// catch the exception and attempt split lookup, returning the split reader's block.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool KeyNotFoundException_Caught_SplitLookupAttempted(NonNegativeInt keySeed)
        {
            ulong seed = (ulong)(keySeed.Get % 10000) + 1;
            BlockKey key = makeKey(seed);
            BlockRecord expectedRecord = makeRecord(key);

            ThrowingBlockProvider throwingPrimary = new ThrowingBlockProvider(new KeyNotFoundException("Block not found"));
            Dictionary<BlockKey, BlockRecord> splitBlocks = new Dictionary<BlockKey, BlockRecord> { { key, expectedRecord } };
            StubImageReader splitReader = new StubImageReader(splitBlocks);
            StubImageReader auxReader = new StubImageReader(new Dictionary<BlockKey, BlockRecord>());

            AuxBlockProvider provider = new AuxBlockProvider(throwingPrimary, splitReader, auxReader);
            OffsetRecord offsetRecord = makeOffset(key);

            // Act
            BlockRecord result = provider.GetBlock(offsetRecord, 0);

            // Assert: exception was caught, split reader was queried, and result returned
            if (result == null) return false;
            if (result.Key != key) return false;
            if (splitReader.QueriedKeys.Count == 0) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 9.1, 9.2**
        ///
        /// Property 13b: InvalidOperationException from primary is caught, split/aux lookup attempted.
        /// For any block key present in the aux reader (but NOT split), when the primary provider throws
        /// InvalidOperationException from GetBlock(OffsetRecord, int), the AuxBlockProvider SHALL
        /// catch the exception and fall through to aux lookup, returning the aux reader's block.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool InvalidOperationException_Caught_AuxLookupAttempted(NonNegativeInt keySeed)
        {
            ulong seed = (ulong)(keySeed.Get % 10000) + 1;
            BlockKey key = makeKey(seed);
            BlockRecord expectedRecord = makeRecord(key);

            ThrowingBlockProvider throwingPrimary = new ThrowingBlockProvider(new InvalidOperationException("Block missing from shard"));
            StubImageReader splitReader = new StubImageReader(new Dictionary<BlockKey, BlockRecord>()); // empty split
            Dictionary<BlockKey, BlockRecord> auxBlocks = new Dictionary<BlockKey, BlockRecord> { { key, expectedRecord } };
            StubImageReader auxReader = new StubImageReader(auxBlocks);

            AuxBlockProvider provider = new AuxBlockProvider(throwingPrimary, splitReader, auxReader);
            OffsetRecord offsetRecord = makeOffset(key);

            // Act
            BlockRecord result = provider.GetBlock(offsetRecord, 0);

            // Assert: exception was caught, aux reader was queried, and result returned
            if (result == null) return false;
            if (result.Key != key) return false;
            // Split was attempted first (no hit), then aux was attempted
            if (splitReader.QueriedKeys.Count == 0) return false;
            if (auxReader.QueriedKeys.Count == 0) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirement 9.3**
        ///
        /// Property 13c: Other exception types propagate to caller.
        /// For any exception type that is NOT KeyNotFoundException or InvalidOperationException,
        /// the AuxBlockProvider SHALL allow it to propagate (not catch it). The split/aux readers
        /// should NOT be queried.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool OtherExceptions_Propagate_NoFallbackAttempted(NonNegativeInt exceptionTypeSeed, NonNegativeInt keySeed)
        {
            // Select from various non-handled exception types
            int typeIndex = exceptionTypeSeed.Get % 5;
            Exception exceptionToThrow = typeIndex switch
            {
                0 => new ArgumentException("Unexpected argument"),
                1 => new IOException("Disk read error"),
                2 => new NullReferenceException("Null ref"),
                3 => new IndexOutOfRangeException("Index out of range"),
                _ => new InvalidDataException("Corrupt data")
            };

            ulong seed = (ulong)(keySeed.Get % 10000) + 1;
            BlockKey key = makeKey(seed);
            BlockRecord record = makeRecord(key);

            ThrowingBlockProvider throwingPrimary = new ThrowingBlockProvider(exceptionToThrow);
            // Put the block in both split and aux so if they were (incorrectly) queried, they'd return it
            StubImageReader splitReader = new StubImageReader(new Dictionary<BlockKey, BlockRecord> { { key, record } });
            StubImageReader auxReader = new StubImageReader(new Dictionary<BlockKey, BlockRecord> { { key, record } });

            AuxBlockProvider provider = new AuxBlockProvider(throwingPrimary, splitReader, auxReader);
            OffsetRecord offsetRecord = makeOffset(key);

            // Act & Assert: exception should propagate
            bool exceptionPropagated = false;
            try
            {
                provider.GetBlock(offsetRecord, 0);
            }
            catch (Exception ex) when (ex.GetType() == exceptionToThrow.GetType())
            {
                exceptionPropagated = true;
            }

            if (!exceptionPropagated) return false;

            // Neither split nor aux should have been consulted
            if (splitReader.QueriedKeys.Count != 0) return false;
            if (auxReader.QueriedKeys.Count != 0) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 9.1, 9.2**
        ///
        /// Property 13d: KeyNotFoundException caught with split reader null, aux lookup still attempted.
        /// When split reader is null and primary throws KeyNotFoundException, the provider catches
        /// the exception and falls through directly to shared aux lookup.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool KeyNotFoundException_NullSplit_AuxLookupAttempted(NonNegativeInt keySeed)
        {
            ulong seed = (ulong)(keySeed.Get % 10000) + 1;
            BlockKey key = makeKey(seed);
            BlockRecord expectedRecord = makeRecord(key);

            ThrowingBlockProvider throwingPrimary = new ThrowingBlockProvider(new KeyNotFoundException("Block not found"));
            Dictionary<BlockKey, BlockRecord> auxBlocks = new Dictionary<BlockKey, BlockRecord> { { key, expectedRecord } };
            StubImageReader auxReader = new StubImageReader(auxBlocks);

            // No split reader — simulates Wii/WiiU two-tier config
            AuxBlockProvider provider = new AuxBlockProvider(throwingPrimary, splitReader: null, auxReader: auxReader);
            OffsetRecord offsetRecord = makeOffset(key);

            // Act
            BlockRecord result = provider.GetBlock(offsetRecord, 0);

            // Assert: exception was caught, aux reader was queried, and result returned
            if (result == null) return false;
            if (result.Key != key) return false;
            if (auxReader.QueriedKeys.Count == 0) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 9.1, 9.2, 9.3**
        ///
        /// Property 13e: InvalidOperationException caught returns null when neither split nor aux has block.
        /// When primary throws InvalidOperationException and the block is NOT in split or aux,
        /// the provider catches the exception and returns null (no exception propagation).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool InvalidOperationException_Caught_BothMiss_ReturnsNull(NonNegativeInt keySeed)
        {
            ulong seed = (ulong)(keySeed.Get % 10000) + 1;
            BlockKey key = makeKey(seed);

            ThrowingBlockProvider throwingPrimary = new ThrowingBlockProvider(new InvalidOperationException("Block missing"));
            StubImageReader splitReader = new StubImageReader(new Dictionary<BlockKey, BlockRecord>()); // empty
            StubImageReader auxReader = new StubImageReader(new Dictionary<BlockKey, BlockRecord>());   // empty

            AuxBlockProvider provider = new AuxBlockProvider(throwingPrimary, splitReader, auxReader);
            OffsetRecord offsetRecord = makeOffset(key);

            // Act
            BlockRecord result = provider.GetBlock(offsetRecord, 0);

            // Assert: exception was caught (no propagation), both readers queried, null returned
            if (result != null) return false;
            if (splitReader.QueriedKeys.Count == 0) return false;
            if (auxReader.QueriedKeys.Count == 0) return false;

            return true;
        }
    }
}
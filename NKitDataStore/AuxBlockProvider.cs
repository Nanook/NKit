using NKitDataStore.Interfaces;

namespace NKitDataStore
{
    /// <summary>
    /// Implements IBlockProvider with a three-tier resolution chain (primary → split → aux).
    /// Wraps an existing IBlockProvider for the primary store and adds split and aux fallback
    /// via optional IImageReader instances for per-game split and shared aux stores.
    /// </summary>
    public class AuxBlockProvider : IBlockProvider, IDisposable
    {
        private readonly IBlockProvider _primaryProvider;
        private readonly IImageReader? _splitReader;  // per-game split (checked first)
        private readonly IImageReader? _auxReader;    // shared aux (checked second)

        /// <summary>
        /// Gets the split image reader used for per-game fallback lookups. May be null.
        /// </summary>
        public IImageReader? SplitReader => _splitReader;

        /// <summary>
        /// Gets the aux image reader used for shared aux fallback lookups. May be null.
        /// </summary>
        public IImageReader? AuxReader => _auxReader;

        /// <summary>
        /// Creates an AuxBlockProvider with three-tier resolution: primary → split → aux.
        /// </summary>
        /// <param name="primaryProvider">The primary block provider (must not be null).</param>
        /// <param name="splitReader">Optional per-game split image reader (checked before aux). May be null.</param>
        /// <param name="auxReader">Optional shared aux image reader for fallback lookups. May be null.</param>
        public AuxBlockProvider(IBlockProvider primaryProvider, IImageReader? splitReader, IImageReader? auxReader)
        {
            _primaryProvider = primaryProvider ?? throw new ArgumentNullException(nameof(primaryProvider));
            _splitReader = splitReader;
            _auxReader = auxReader;
        }

        /// <summary>
        /// Backward-compatible constructor: creates an AuxBlockProvider with two-tier resolution (primary → aux).
        /// Equivalent to calling the three-parameter constructor with splitReader: null.
        /// </summary>
        /// <param name="primaryProvider">The primary block provider (must not be null).</param>
        /// <param name="auxReader">Optional aux image reader for fallback lookups. May be null.</param>
        public AuxBlockProvider(IBlockProvider primaryProvider, IImageReader? auxReader)
            : this(primaryProvider, splitReader: null, auxReader: auxReader)
        {
        }

        /// <summary>
        /// Resolves a block by key using the three-tier chain: Primary → Split → Shared Aux → null.
        /// </summary>
        public BlockRecord? GetBlock(BlockKey key)
        {
            // 1. Try primary provider first
            BlockRecord? result = _primaryProvider.GetBlock(key);
            if (result != null)
                return result;

            // 2. Try split reader (per-game filler)
            if (_splitReader != null)
            {
                result = _splitReader.GetBlock(key);
                if (result != null)
                    return result;
            }

            // 3. Try shared aux reader (video partition)
            if (_auxReader != null)
                return _auxReader.GetBlock(key);

            return null;
        }

        /// <summary>
        /// Async variant: resolves a block by key using the three-tier chain: Primary → Split → Shared Aux → null.
        /// </summary>
        public async Task<BlockRecord?> GetBlockAsync(BlockKey key)
        {
            // 1. Try primary provider first
            BlockRecord? result = await _primaryProvider.GetBlockAsync(key);
            if (result != null)
                return result;

            // 2. Try split reader (per-game filler; IImageReader.GetBlock is synchronous)
            if (_splitReader != null)
            {
                result = await Task.Run(() => _splitReader.GetBlock(key));
                if (result != null)
                    return result;
            }

            // 3. Try shared aux reader (video partition)
            if (_auxReader == null)
                return null;

            return await Task.Run(() => _auxReader.GetBlock(key));
        }

        /// <summary>
        /// Resolves a block by offset record and block index using the three-tier chain:
        /// Primary (with selective exception handling) → Split → Shared Aux → null.
        /// </summary>
        public BlockRecord? GetBlock(OffsetRecord record, int blockIndex)
        {
            // 1. Try primary provider first.
            // The offset record may have come from the aux store (via MergeAuxOffsets),
            // in which case the primary provider won't find it — that's expected.
            BlockRecord? result = null;
            try
            {
                result = _primaryProvider.GetBlock(record, blockIndex);
            }
            catch (KeyNotFoundException)
            {
                // Expected: offset record references aux data, primary doesn't have it
            }
            catch (InvalidOperationException)
            {
                // Expected: block missing from primary shard
            }
            // All other exceptions propagate to caller

            if (result != null)
                return result;

            // 2. Try split reader (per-game filler)
            if (_splitReader != null)
            {
                BlockKey key = record.GetBlockAt(blockIndex);
                result = _splitReader.GetBlock(key);
                if (result != null)
                    return result;
            }

            // 3. Try shared aux reader (video partition)
            if (_auxReader != null)
            {
                BlockKey key = record.GetBlockAt(blockIndex);
                return _auxReader.GetBlock(key);
            }

            return null;
        }

        /// <summary>
        /// Disposes both the split and aux IImageReader instances if they implement IDisposable.
        /// </summary>
        public void Dispose()
        {
            (_splitReader as IDisposable)?.Dispose();
            (_auxReader as IDisposable)?.Dispose();
        }
    }
}
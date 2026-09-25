using System.Collections.Concurrent;

namespace NKitDataStore
{

    /// <summary>
    /// Storage- and compression-agnostic block writer.
    /// Responsibilities:
    /// - Avoid duplicate work by checking a small in-memory cache of known existing blocks
    /// - Serialize compression+write for the same BlockKey so callers concurrently requesting the same block
    ///   wait for the single background work to complete and then observe the block exists
    /// - Bound concurrent background compression using pre-allocated buffer pools
    ///
    /// All storage and compression work is provided via delegates so this class is easy to unit-test.
    /// </summary>
    public class BlockWriter : IDisposable
    {
        // Known existing blocks cache
        private readonly ConcurrentDictionary<(ulong xxHash, uint crc), bool> _existsCache = new();

        // Lock objects per block key so callers can wait for the single work item
        private readonly ConcurrentDictionary<(ulong xxHash, uint crc), object> _locks = new();

        // Shutdown flag to prevent scheduling of new work during disposal
        private volatile bool _isShuttingDown = false;

        // Track work completion for WaitAll
        private int _pendingWorkCount = 0;

        // Pre-allocated buffer pools for source and destination buffers
        private readonly ConcurrentBag<byte[]> _srcBufferPool = new();
        private readonly ConcurrentBag<byte[]> _dstBufferPool = new();
        private readonly SemaphoreSlim _bufferSemaphore;
        private readonly int _blockSize;
        private readonly int _maxCompressedSize;
        private readonly int _compressionParallelism;

        public BlockWriter(int compressionParallelism, int blockSize)
        {
            if (compressionParallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(compressionParallelism));
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockSize));

            _compressionParallelism = compressionParallelism;
            _blockSize = blockSize;
            // Worst case: compressed size could be larger than input (rare but possible with incompressible data + header)
            _maxCompressedSize = blockSize + 1024;

            // Pre-allocate 4x buffers to prevent convoy effect with 1 shard and hold headroom
            // Compression parallelism limits CPU-bound work, but extra buffers allow writes to proceed
            // without blocking new compressions from starting. Increase multiplier to reduce stalls.
            int bufferPoolSize = compressionParallelism * 4;
            for (int i = 0; i < bufferPoolSize; i++)
            {
                _srcBufferPool.Add(new byte[blockSize]);
                _dstBufferPool.Add(new byte[_maxCompressedSize]);
            }

            // Semaphore to limit concurrent compression operations (not buffer count)
            _bufferSemaphore = new SemaphoreSlim(compressionParallelism, compressionParallelism);
        }

        /// <summary>
        /// Prevents scheduling of new work and waits for pending work to complete.
        /// Blocks up to timeoutMs milliseconds.
        /// </summary>
        public void Shutdown(int timeoutMs = 30000)
        {
            _isShuttingDown = true;
            WaitAll(timeoutMs);
        }

        /// <summary>
        /// Wait for all background work to complete.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds. Use -1 to wait indefinitely.</param>
        public void WaitAll(int timeoutMs = 30000)
        {
            if (timeoutMs < 0)
            {
                // Wait indefinitely
                while (Interlocked.CompareExchange(ref _pendingWorkCount, 0, 0) > 0)
                    Thread.Sleep(100);
            }
            else
            {
                // Wait with timeout
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (Interlocked.CompareExchange(ref _pendingWorkCount, 0, 0) > 0)
                {
                    int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0)
                        break;
                    Thread.Sleep(Math.Min(100, remaining));
                }
            }
        }

        /// <summary>
        /// Disposes the BlockWriter and waits for all background work to complete.
        /// </summary>
        public void Dispose()
        {
            Shutdown(30000);
            _bufferSemaphore?.Dispose();
        }

        /// <summary>
        /// Insert a block if it does not exist. Uses delegates for existence check, compression and persistence.
        /// If another caller is already processing the same block, this method will wait for that work and return.
        ///
        /// Compression is performed on a background thread using pre-allocated buffers to bound parallelism.
        /// The shard-level lock provided by <paramref name="acquireShardWrite"/> is acquired only immediately
        /// before the write, so compression can run in parallel and writes are serialized per-shard.
        /// </summary>
        /// <param name="setName">Set name</param>
        /// <param name="key">Block key (xxHash, crc)</param>
        /// <param name="data">Source data buffer</param>
        /// <param name="offset">Offset into source buffer</param>
        /// <param name="length">Length of data</param>
        /// <param name="existsFunc">Synchronous predicate to determine whether the block already exists.</param>
        /// <param name="acquireShardWrite">Synchronous delegate called immediately before writing. Returns IDisposable that releases the shard write lock when disposed.</param>
        /// <param name="compressFunc">Synchronous compress function. Takes (srcBuffer, srcOffset, srcLength, dstBuffer) and returns (dstLength, compressionType).</param>
        /// <param name="writeFunc">Synchronous write function to persist compressed bytes.</param>
        /// <param name="getShardId">Optional function to get shard ID for diagnostics.</param>
        public void InsertBlockWithCompression(
            string setName,
            (ulong xxHash, uint crc) key,
            byte[] data,
            int offset,
            int length,
            Func<string, (ulong xxHash, uint crc), bool> existsFunc,
            Func<string, (ulong xxHash, uint crc), IDisposable> acquireShardWrite,
            Func<byte[], int, int, byte[], (int length, byte compressionType)> compressFunc,
            Action<string, (ulong xxHash, uint crc), byte[], int, byte> writeFunc,
            Func<string, (ulong xxHash, uint crc), int?>? getShardId = null)
        {
            if (_isShuttingDown)
                return;

            // Quick positive cache check
            (ulong xxHash, uint crc) dictKey = (key.xxHash, key.crc);
            if (_existsCache.TryGetValue(dictKey, out bool known) && known)
                return;

            if (setName == null)
                throw new ArgumentNullException(nameof(setName));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (existsFunc == null)
                throw new ArgumentNullException(nameof(existsFunc));
            if (acquireShardWrite == null)
                throw new ArgumentNullException(nameof(acquireShardWrite));
            if (compressFunc == null)
                throw new ArgumentNullException(nameof(compressFunc));
            if (writeFunc == null)
                throw new ArgumentNullException(nameof(writeFunc));

            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "length must be > 0");
            if (offset < 0 || offset + length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "offset/length outside bounds of source array");

            // Get or create the lock object for this block
            object lockObj = _locks.GetOrAdd(dictKey, _ => new object());

            // Try to acquire lock without blocking
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(lockObj, 0, ref lockTaken);

                if (lockTaken)
                {
                    _existsCache[dictKey] = true; // stop future checks

                    bool existsInStore = existsFunc(setName, key);
                    if (existsInStore)
                        return;

                    // Increment pending work counter
                    Interlocked.Increment(ref _pendingWorkCount);

                    // Wait for buffers to become available
                    _bufferSemaphore.Wait();

                    // Copy data on main thread to avoid buffer corruption as passed buffer is reused
                    if (!_srcBufferPool.TryTake(out byte[]? srcBufx) || !_dstBufferPool.TryTake(out byte[]? dstBufx))
                        throw new Exception("Not enough buffers allocated");

                    byte[] srcBuf = srcBufx!;
                    byte[] dstBuf = dstBufx!;

                    // Copy data into source buffer allowing data to be reused by caller
                    Buffer.BlockCopy(data, offset, srcBuf, 0, length);

                    // Start compression and write on ThreadPool
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            try
                            {
                                // Compress the data into destination buffer
                                (int compressedLength, byte compressionType) = compressFunc(srcBuf, 0, length, dstBuf);

                                // Acquire shard-level write lock and write
                                // Buffers are held during write, but extra buffers in pool allow new compressions
                                using (IDisposable shardLock = acquireShardWrite(setName, key))
                                    writeFunc(setName, key, dstBuf, compressedLength, compressionType);
                            }
                            finally
                            {
                                // Return buffers to pool after write completes
                                try
                                {
                                    if (srcBuf != null) _srcBufferPool.Add(srcBuf);
                                    if (dstBuf != null) _dstBufferPool.Add(dstBuf);
                                    _bufferSemaphore.Release();
                                }
                                catch { }
                            }
                        }
                        catch { }
                        finally
                        {
                            _locks.TryRemove(dictKey, out _);
                            Interlocked.Decrement(ref _pendingWorkCount);
                        }
                    });
                }
                else // block is being processed now, assume it will be stored by the other thread
                {
                    return;
                }
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(lockObj);
            }
        }
    }
}
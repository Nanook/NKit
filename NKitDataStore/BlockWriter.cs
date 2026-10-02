using System.Collections.Concurrent;
using System.Diagnostics;

namespace NKitDataStore
{
    /// <summary>
    /// Storage- and compression-agnostic block writer.
    /// Responsibilities:
    /// - Avoid duplicate work by checking a small in-memory cache of known existing blocks
    /// - Serialize compression+write for the same BlockKey so callers concurrently requesting the same block
    ///   wait for the single background work to complete and then observe the block exists
    /// - Bound concurrent background compression using a semaphore, providing natural backpressure
    ///   so the caller slows to the rate compression can consume (preventing unbounded ThreadPool queuing)
    ///
    /// All storage and compression work is provided via delegates so this class is easy to unit-test.
    ///
    /// Threading model:
    ///   InsertBlockWithCompression acquires a compression slot (semaphore) on the CALLER thread,
    ///   but only AFTER releasing the per-key lockObj. This preserves the original backpressure
    ///   behaviour (caller waits when all slots are busy) while eliminating the original deadlock risk
    ///   (which was caused by waiting while still holding lockObj, preventing the slot from ever being freed).
    ///   WaitAll uses a ManualResetEventSlim — no polling, no CPU burn.
    /// </summary>
    public class BlockWriter : IDisposable
    {
        // Known existing blocks cache
        private readonly ConcurrentDictionary<(ulong xxHash, uint crc), bool> _existsCache = new();

        // Lock objects per block key so callers can detect a duplicate in-flight operation
        private readonly ConcurrentDictionary<(ulong xxHash, uint crc), object> _locks = new();

        // Shutdown flag to prevent scheduling of new work during disposal
        private volatile bool _isShuttingDown = false;

        // Pending work count + event so WaitAll wakes immediately when all work drains
        private int _pendingWorkCount = 0;
        private readonly ManualResetEventSlim _idleEvent = new ManualResetEventSlim(true); // starts signalled (idle)

        // Pre-allocated buffer pools for source and destination buffers.
        // Pool size matches the semaphore count so every slot always has a buffer waiting.
        private readonly ConcurrentBag<byte[]> _srcBufferPool = new();
        private readonly ConcurrentBag<byte[]> _dstBufferPool = new();

        // Semaphore limits concurrent compression operations.
        // Acquired on the CALLER thread (after lockObj is released) to provide backpressure —
        // the caller naturally throttles to the rate compression can consume without flooding the ThreadPool.
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

            // Pre-allocate buffers 1:1 with semaphore slots — every acquired slot has a buffer ready.
            // The original 4× multiplier was intended to reduce stalls but is unnecessary when the
            // semaphore is acquired before TryTake, because a slot acquisition always precedes buffer use.
            for (int i = 0; i < compressionParallelism; i++)
            {
                _srcBufferPool.Add(new byte[blockSize]);
                _dstBufferPool.Add(new byte[_maxCompressedSize]);
            }

            _bufferSemaphore = new SemaphoreSlim(compressionParallelism, compressionParallelism);
        }

        /// <summary>
        /// Prevents scheduling of new work and waits for all pending work to complete.
        /// </summary>
        public void Shutdown(int timeoutMs = -1)
        {
            _isShuttingDown = true;
            WaitAll(timeoutMs);
        }

        /// <summary>
        /// Waits for all background work to complete.
        /// Uses event signalling — no polling, no CPU burn.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds, or -1 to wait indefinitely.</param>
        /// <returns>True if all work completed, false if timeout elapsed with work still pending.</returns>
        public bool WaitAll(int timeoutMs = -1)
        {
            if (Interlocked.CompareExchange(ref _pendingWorkCount, 0, 0) == 0)
                return true;
            return _idleEvent.Wait(timeoutMs);
        }

        /// <summary>
        /// Disposes the BlockWriter and waits for all background work to complete.
        /// </summary>
        public void Dispose()
        {
            Shutdown();
            _bufferSemaphore?.Dispose();
            _idleEvent?.Dispose();
        }

        private void BeginWork()
        {
            if (Interlocked.Increment(ref _pendingWorkCount) == 1)
                _idleEvent.Reset();
        }

        private void EndWork()
        {
            if (Interlocked.Decrement(ref _pendingWorkCount) == 0)
                _idleEvent.Set();
        }

        /// <summary>
        /// Insert a block if it does not exist. Uses delegates for existence check, compression and persistence.
        /// If another caller is already processing the same block, this method returns immediately.
        ///
        /// The caller blocks waiting for a compression slot to become available (backpressure), then
        /// queues compression + write on a ThreadPool thread and returns. lockObj is always released
        /// before the semaphore wait, so no deadlock is possible even if slot acquisition blocks.
        /// </summary>
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

            (ulong xxHash, uint crc) dictKey = (key.xxHash, key.crc);
            if (_existsCache.TryGetValue(dictKey, out bool known) && known)
                return;

            if (setName == null) throw new ArgumentNullException(nameof(setName));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (existsFunc == null) throw new ArgumentNullException(nameof(existsFunc));
            if (acquireShardWrite == null) throw new ArgumentNullException(nameof(acquireShardWrite));
            if (compressFunc == null) throw new ArgumentNullException(nameof(compressFunc));
            if (writeFunc == null) throw new ArgumentNullException(nameof(writeFunc));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be > 0");
            if (offset < 0 || offset + length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "offset/length outside bounds of source array");

            object lockObj = _locks.GetOrAdd(dictKey, _ => new object());

            bool lockTaken = false;
            Monitor.TryEnter(lockObj, 0, ref lockTaken);

            if (!lockTaken)
                return; // another task is already processing this block

            bool shouldQueue = false;
            try
            {
                _existsCache[dictKey] = true;

                if (existsFunc(setName, key))
                    return;

                // Mark work in-flight before releasing lockObj so WaitAll can't race to idle
                BeginWork();
                shouldQueue = true;

                // Release lockObj NOW — before any wait — so other callers on different keys
                // are not blocked, and so that in-flight tasks can release their semaphore slots
                // without needing to acquire lockObj first. This is the key fix: the original code
                // waited on the semaphore while still holding lockObj, which could deadlock if all
                // pool threads were themselves waiting to acquire lockObj on different code paths.
                Monitor.Exit(lockObj);
                lockTaken = false;
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(lockObj);
                    lockTaken = false;
                }
            }

            if (!shouldQueue)
                return;

            // --- lockObj is released here ---
            // Wait for a compression slot on the CALLER thread. This provides backpressure:
            // the caller naturally throttles to compression throughput, keeping ThreadPool queue depth
            // bounded at compressionParallelism rather than growing without limit.
            _bufferSemaphore.Wait();

            // Slot acquired — grab pre-allocated buffers (always available: 1:1 with slots)
            if (!_srcBufferPool.TryTake(out byte[]? srcBuf) || !_dstBufferPool.TryTake(out byte[]? dstBuf))
            {
                _bufferSemaphore.Release();
                EndWork();
                throw new InvalidOperationException("Buffer pool exhausted — pool size must match semaphore count");
            }

            // Copy caller data into owned buffer before queuing (caller's buffer may be reused)
            Buffer.BlockCopy(data, offset, srcBuf, 0, length);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    (int compressedLength, byte compressionType) = compressFunc(srcBuf, 0, length, dstBuf);

                    using (IDisposable shardLock = acquireShardWrite(setName, key))
                        writeFunc(setName, key, dstBuf, compressedLength, compressionType);
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"BlockWriter: unhandled error for key ({key.xxHash:X16},{key.crc:X8}) in set '{setName}': {ex}");
                }
                finally
                {
                    if (srcBuf != null) _srcBufferPool.Add(srcBuf);
                    if (dstBuf != null) _dstBufferPool.Add(dstBuf);
                    _bufferSemaphore.Release();
                    _locks.TryRemove(dictKey, out _);
                    EndWork();
                }
            });
        }
    }
}

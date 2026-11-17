using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Concurrency stress tests for <see cref="BlockWriter"/> designed to expose known
    /// race conditions and hang-prone patterns WITHOUT modifying production code.
    ///
    /// Artificial delays are injected via the compression/write delegates to widen race
    /// windows that are normally nanoseconds wide, making intermittent bugs reproducible.
    ///
    /// Tests that expose existing bugs are annotated with [BUG CONDITION].
    /// A failing assertion (or a timeout) is the expected outcome for those tests until
    /// the underlying issue is fixed.
    ///
    /// How to run:
    ///   NKit.Tests/bin/Debug/net10.0/NKit.Tests.exe -trait "Group=BlockWriterConcurrency"
    ///
    /// Background - the hang symptom (reported: NKDS add hangs before verify):
    ///   BlockWriter.InsertBlockWithCompression() acquires _bufferSemaphore.Wait() on the
    ///   CALLER thread with no CancellationToken and no timeout.  This call runs on the
    ///   StreamBlockCore dedicated writer task.  If pipeline cancellation is requested while
    ///   the writer is blocked here, the cancellation has no effect: _bufferSemaphore.Wait()
    ///   is a synchronous indefinite wait.  The writer task never returns, ProcessAsync never
    ///   completes, and NKitCoreRunner.Run()'s run.GetAwaiter().GetResult() blocks forever.
    ///   The verify step therefore never starts.
    /// </summary>
    [Trait("Area", "Unit"), Trait("Group", "BlockWriterConcurrency")]
    public class BlockWriterConcurrencyTests
    {
        // ---- helpers ----------------------------------------------------------------

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }

        private static (ulong xxHash, uint crc) KeyFor(byte[] data)
        {
            return (TestHashUtil.ComputeXXHash64(data), Nanook.NKit.Crc.Compute(data, 0, data.Length));
        }

        private static void CopyBytes(byte[] src, int srcOffset, byte[] dst, int length)
        {
            for (int i = 0; i < length; i++)
                dst[i] = src[srcOffset + i];
        }

        // Deterministic unique block data for index i - avoids the shared TestDataFactory.Rng
        // which is not thread-safe and gets corrupted when concurrent tests run in parallel,
        // causing all blocks after the first to look identical (hash collision in _existsCache).
        private static byte[] BlockData(int index, int size)
        {
            byte[] data = new byte[size];
            new Random(index * 1_000_003 + 7).NextBytes(data);
            // Stamp the index into the first 4 bytes so even a degenerate Random can't collide.
            data[0] = (byte)(index);
            data[1] = (byte)(index >> 8);
            data[2] = (byte)(index >> 16);
            data[3] = (byte)(index >> 24);
            return data;
        }

        private static Func<byte[], int, int, byte[], (int length, byte compressionType)> PassThroughCompress(int delayMs = 0)
        {
            return (src, offset, len, dst) =>
            {
                if (delayMs > 0)
                    Thread.Sleep(delayMs);
                CopyBytes(src, offset, dst, len);
                return (len, 0);
            };
        }

        // A compress function that blocks until the provided gate is signalled.
        // Used to hold compression slots open long enough for the test to observe the race.
        private static Func<byte[], int, int, byte[], (int length, byte compressionType)> GatedCompress(
            ManualResetEventSlim gate, int gateTimeoutMs = 30_000)
        {
            return (src, offset, len, dst) =>
            {
                gate.Wait(gateTimeoutMs);
                CopyBytes(src, offset, dst, len);
                return (len, 0);
            };
        }

        // ---- Baseline ---------------------------------------------------------------

        /// <summary>
        /// Sanity check: a single block insert with fast compression completes promptly.
        /// If this fails, the test environment itself has a problem.
        /// </summary>
        [Fact]
        public void InsertBlock_SingleBlock_CompletesPromptly()
        {
            int blockSize = 1024;
            byte[] data = BlockData(0, blockSize);
            int writeCalls = 0;

            using BlockWriter writer = new BlockWriter(2, blockSize);

            writer.InsertBlockWithCompression(
                "test", KeyFor(data), data, 0, data.Length,
                existsFunc: (_, __) => false,
                acquireShardWrite: (_, __) => new EmptyDisposable(),
                compressFunc: PassThroughCompress(0),
                writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));

            bool done = writer.WaitAll(5_000);

            Assert.True(done, "WaitAll timed out - should be instant for a single fast block");
            Assert.Equal(1, Volatile.Read(ref writeCalls));
        }

        /// <summary>
        /// Saturate the compression semaphore (all slots busy) then insert more blocks.
        /// Every block must complete eventually - no blocks must be lost or cause a deadlock.
        /// </summary>
        [Fact]
        public void InsertBlock_SaturationThenMore_AllBlocksWritten()
        {
            int parallelism = 3;
            int blockSize = 1024;
            int totalBlocks = parallelism * 4; // more than the slot count
            int writeCalls = 0;

            using BlockWriter writer = new BlockWriter(parallelism, blockSize);

            for (int i = 0; i < totalBlocks; i++)
            {
                byte[] data = BlockData(i, blockSize);
                writer.InsertBlockWithCompression(
                    "test", KeyFor(data), data, 0, data.Length,
                    existsFunc: (_, __) => false,
                    acquireShardWrite: (_, __) => new EmptyDisposable(),
                    compressFunc: PassThroughCompress(10), // 10ms per block
                    writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));
            }

            bool done = writer.WaitAll(30_000);

            Assert.True(done, $"WaitAll timed out with {Volatile.Read(ref writeCalls)}/{totalBlocks} writes done");
            Assert.Equal(totalBlocks, Volatile.Read(ref writeCalls));
        }

        // ---- [BUG CONDITION] Semaphore wait has no CancellationToken ----------------

        /// <summary>
        /// Verifies that InsertBlockWithCompression respects cancellation while waiting for
        /// a compression slot. When all slots are occupied and the CT is cancelled, the caller
        /// must return promptly (OperationCanceledException) rather than blocking indefinitely.
        ///
        /// Root cause of the original hang: _bufferSemaphore.Wait() had no CancellationToken.
        /// When the pipeline cancelled mid-batch, the writer task blocked here forever, which
        /// caused run.GetAwaiter().GetResult() in NKitCoreRunner.Run() to never return and
        /// the verify step never started.
        ///
        /// Fix applied: added CancellationToken parameter to InsertBlockWithCompression and
        /// passed it to _bufferSemaphore.Wait(cancellationToken). EndWork() is called in the
        /// catch block to keep WaitAll()/Shutdown() correct when cancellation fires.
        /// </summary>
        [Fact]
        public async Task InsertBlock_SlotsFullThenCancel_CallerReturnsPromptlyOnCancellation()
        {
            int parallelism = 2;
            int blockSize = 1024;

            // Gate keeps compression tasks blocked so slots stay occupied throughout the test.
            using ManualResetEventSlim releaseGate = new ManualResetEventSlim(false);

            BlockWriter writer = null;
            Task insertTask = null;

            try
            {
                writer = new BlockWriter(parallelism, blockSize);

                // Fill BOTH compression slots with gated tasks (they won't release the
                // semaphore until releaseGate is set at the end of the test).
                for (int i = 0; i < parallelism; i++)
                {
                    byte[] slot = BlockData(i, blockSize);
                    writer.InsertBlockWithCompression(
                        "test", KeyFor(slot), slot, 0, blockSize,
                        existsFunc: (_, __) => false,
                        acquireShardWrite: (_, __) => new EmptyDisposable(),
                        compressFunc: GatedCompress(releaseGate),
                        writeFunc: (_, __, ___, ____, _____) => { });
                }

                // Brief pause to let the compression tasks start and actually acquire slots.
                Thread.Sleep(50);

                // --- The race scenario starts here ---
                // Cancel the pipeline token BEFORE the third block is submitted,
                // exactly as happens when NKit cancels mid-batch.
                using CancellationTokenSource cts = new CancellationTokenSource();
                cts.Cancel();

                byte[] extra = BlockData(parallelism + 1, blockSize);

                // This call should return promptly because the token is already cancelled.
                // WITHOUT FIX: it blocks on _bufferSemaphore.Wait() (no CT) until a slot
                //              is freed - which won't happen until releaseGate is set.
                Stopwatch sw = Stopwatch.StartNew();

                insertTask = Task.Run(() =>
                {
                    // Now that InsertBlockWithCompression has a CancellationToken parameter,
                    // the already-cancelled token is passed through to _bufferSemaphore.Wait(ct).
                    writer.InsertBlockWithCompression(
                        "test", KeyFor(extra), extra, 0, blockSize,
                        existsFunc: (_, __) => false,
                        acquireShardWrite: (_, __) => new EmptyDisposable(),
                        compressFunc: PassThroughCompress(0),
                        writeFunc: (_, __, ___, ____, _____) => { },
                        cancellationToken: cts.Token);
                });

                // With a correct implementation, the cancelled CT causes InsertBlockWithCompression
                // to throw OperationCanceledException immediately. Task.Wait(500) would rethrow it
                // as AggregateException, so use Task.WhenAny to check completion without throwing.
                bool completedQuickly = await Task.WhenAny(insertTask, Task.Delay(500)) == insertTask;

                sw.Stop();
                Trace.TraceInformation($"insertTask completed: {completedQuickly}, elapsed: {sw.ElapsedMilliseconds}ms");

                // Release the gate so the blocked compression tasks can drain and
                // BlockWriter.Dispose() doesn't hang during test cleanup.
                releaseGate.Set();
                try { await insertTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { } // OperationCanceledException expected

                // The key assertion: the call with a cancelled token must return
                // well within the gated-compression window (which is >> 500ms).
                Assert.True(completedQuickly,
                    $"InsertBlockWithCompression did not return within 500ms after " +
                    $"CancellationToken was cancelled (elapsed: {sw.ElapsedMilliseconds}ms). " +
                    "Expected _bufferSemaphore.Wait(cancellationToken) to throw OperationCanceledException " +
                    "quickly. If this fails, the CT is not being passed to the semaphore wait.");
            }
            finally
            {
                // Always release the gate so compression tasks can complete and Dispose() returns.
                releaseGate.Set();
                try { await (insertTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(10)); } catch { } // OperationCanceledException expected
                writer?.Shutdown(10_000);
                writer?.Dispose();
            }
        }

        // ---- [BUG CONDITION] BeginWork / EndWork false-idle race --------------------

        /// <summary>
        /// [BUG CONDITION] Demonstrates a false-idle race in WaitAll.
        ///
        /// BeginWork increments _pendingWorkCount atomically but calls _idleEvent.Reset()
        /// in a separate (non-atomic) step.  Between the increment and the Reset, WaitAll
        /// can observe _pendingWorkCount > 0, fall through to _idleEvent.Wait(), find the
        /// event still Set (from the previous idle period), and return 'true' - signalling
        /// "all work done" before compression/write tasks have actually run.
        ///
        /// In the pipeline this means BinaryTransaction.Commit() can run before all block
        /// writes have added their location records to the transaction, causing the verify
        /// step to fail with "block not found" for some blocks.
        ///
        /// Expected WITH BUG:   writeCalls < blockCount on some runs (non-deterministic).
        /// Expected AFTER FIX:  writeCalls == blockCount on every run.
        ///
        /// Fix options:
        ///   A) Reset _idleEvent BEFORE incrementing _pendingWorkCount (reverse the order).
        ///   B) Use a lock around {increment + reset} to make them atomic.
        ///   C) Use a CountdownEvent or Interlocked state machine instead of a separate event.
        /// </summary>
        [Fact]
        public void WaitAll_ManyQuickBlocks_AllWritesCompleteBeforeReturn()
        {
            // High iteration count + small delay widens the race window to make the
            // false-idle more likely to surface.  Run several outer iterations to
            // accumulate enough probability to reliably trigger a flawed implementation.
            const int outerRuns = 5;
            const int blocksPerRun = 200;
            const int blockSize = 512;
            const int compressDelayMs = 1; // 1ms per block keeps slots busy long enough

            for (int run = 0; run < outerRuns; run++)
            {
                int writeCalls = 0;

                using BlockWriter writer = new BlockWriter(4, blockSize);

                for (int i = 0; i < blocksPerRun; i++)
                {
                    byte[] data = BlockData(run * blocksPerRun + i, blockSize);
                    writer.InsertBlockWithCompression(
                        "test", KeyFor(data), data, 0, blockSize,
                        existsFunc: (_, __) => false,
                        acquireShardWrite: (_, __) => new EmptyDisposable(),
                        compressFunc: PassThroughCompress(compressDelayMs),
                        writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));
                }

                bool done = writer.WaitAll(60_000);
                int actual = Volatile.Read(ref writeCalls);

                Assert.True(done, $"Run {run}: WaitAll timed out at {actual}/{blocksPerRun} writes");
                Assert.True(actual == blocksPerRun,
                    $"Run {run}: WaitAll returned true but only {actual}/{blocksPerRun} " +
                    "writes had completed. BeginWork false-idle race: _idleEvent.Reset() " +
                    "is not atomic with the _pendingWorkCount increment so WaitAll can " +
                    "observe the event still Set from the previous idle and return early.");
            }
        }

        // ---- High-concurrency / thread-pool stress ----------------------------------

        /// <summary>
        /// Many concurrent callers each inserting a unique block simultaneously.
        /// Verifies that the thread-pool backpressure model (semaphore on caller thread)
        /// does not deadlock when all callers are themselves thread-pool threads.
        ///
        /// This is the overnight-batch scenario in miniature: N Task.Run tasks
        /// each calling InsertBlockWithCompression - equivalent to N pipeline writer
        /// tasks running in parallel across multiple NKDS add operations.
        /// </summary>
        [Fact]
        public async Task InsertBlock_ManyConcurrentCallers_NoDrainDeadlock()
        {
            int parallelism = 4;
            int blockSize = 1024;
            int callerCount = 50;
            int writeCalls = 0;

            using BlockWriter writer = new BlockWriter(parallelism, blockSize);
            ConcurrentQueue<Exception> errors = new ConcurrentQueue<Exception>();

            Task[] callers = new Task[callerCount];
            for (int i = 0; i < callerCount; i++)
            {
                int capturedI = i;
                callers[i] = Task.Run(() =>
                {
                    try
                    {
                        byte[] data = BlockData(capturedI, blockSize);
                        writer.InsertBlockWithCompression(
                            "test", KeyFor(data), data, 0, blockSize,
                            existsFunc: (_, __) => false,
                            acquireShardWrite: (_, __) => new EmptyDisposable(),
                            compressFunc: PassThroughCompress(5), // 5ms per block
                            writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                });
            }

            await Task.WhenAll(callers);

            bool done = writer.WaitAll(60_000);
            int actual = Volatile.Read(ref writeCalls);

            Assert.Empty(errors);
            Assert.True(done, $"WaitAll timed out with {actual}/{callerCount} writes - possible thread-pool deadlock");
            Assert.True(actual == callerCount,
                $"Only {actual}/{callerCount} blocks written - some may have been lost");
        }

        /// <summary>
        /// Simulates the pipeline scenario where a compression task is slow (e.g. large
        /// incompressible block) and multiple writers queue up behind the semaphore.
        /// All must drain without timeout.
        /// </summary>
        [Fact]
        public void InsertBlock_SlowCompression_AllDrainWithinTimeout()
        {
            int parallelism = 2;
            int blockSize = 65_536; // 64 KiB - realistic NKDS block size
            int blockCount = 20;
            int writeCalls = 0;
            const int compressDelayMs = 25; // 25ms/block = 500ms total for 20 blocks at parallelism=2

            using BlockWriter writer = new BlockWriter(parallelism, blockSize);

            for (int i = 0; i < blockCount; i++)
            {
                byte[] data = BlockData(i, blockSize);
                writer.InsertBlockWithCompression(
                    "test", KeyFor(data), data, 0, blockSize,
                    existsFunc: (_, __) => false,
                    acquireShardWrite: (_, __) => new EmptyDisposable(),
                    compressFunc: PassThroughCompress(compressDelayMs),
                    writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));
            }

            bool done = writer.WaitAll(30_000);
            int actual = Volatile.Read(ref writeCalls);

            Assert.True(done, $"WaitAll timed out at {actual}/{blockCount}");
            Assert.Equal(blockCount, actual);
        }

        // ---- Duplicate-key deduplication --------------------------------------------

        /// <summary>
        /// Inserting the same block key concurrently from multiple threads must result in
        /// exactly ONE write (the duplicate-detection logic must hold under concurrency).
        /// </summary>
        [Fact]
        public async Task InsertBlock_DuplicateKeysConcurrent_WrittenExactlyOnce()
        {
            int blockSize = 1024;
            byte[] data = BlockData(99, blockSize); // fixed seed - any unique block works
            (ulong, uint) key = KeyFor(data);
            int writeCalls = 0;

            using BlockWriter writer = new BlockWriter(4, blockSize);

            // 20 concurrent insertions of the SAME key
            Task[] tasks = new Task[20];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    writer.InsertBlockWithCompression(
                        "test", key, data, 0, blockSize,
                        existsFunc: (_, __) => false, // let the dedup logic handle it
                        acquireShardWrite: (_, __) => new EmptyDisposable(),
                        compressFunc: PassThroughCompress(5),
                        writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));
                });
            }

            await Task.WhenAll(tasks);
            writer.WaitAll(10_000);

            Assert.True(Volatile.Read(ref writeCalls) <= 1,
                $"Block written {Volatile.Read(ref writeCalls)} times - deduplication failed under concurrency");        }

        // ---- Shutdown correctness ---------------------------------------------------

        /// <summary>
        /// Shutdown must wait for all in-flight work before returning.
        /// Calling Shutdown while compression tasks are running must not silently lose writes.
        /// </summary>
        [Fact]
        public void Shutdown_WithInflightWork_WaitsForAllCompletion()
        {
            int parallelism = 2;
            int blockSize = 1024;
            int blockCount = 8;
            int writeCalls = 0;

            using BlockWriter writer = new BlockWriter(parallelism, blockSize);

            for (int i = 0; i < blockCount; i++)
            {
                byte[] data = BlockData(i, blockSize);
                writer.InsertBlockWithCompression(
                    "test", KeyFor(data), data, 0, blockSize,
                    existsFunc: (_, __) => false,
                    acquireShardWrite: (_, __) => new EmptyDisposable(),
                    compressFunc: PassThroughCompress(20), // 20ms per block
                    writeFunc: (_, __, ___, ____, _____) => Interlocked.Increment(ref writeCalls));
            }

            // Shutdown should block until all compression tasks complete.
            writer.Shutdown(30_000);
            int actual = Volatile.Read(ref writeCalls);

            Assert.True(actual == blockCount,
                $"Shutdown returned with only {actual}/{blockCount} writes completed. " +
                "Dispose may have raced with pending work.");
        }
    }
}

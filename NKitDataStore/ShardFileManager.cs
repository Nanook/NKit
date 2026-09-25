using System.Collections.Concurrent;
using System.Diagnostics;

namespace NKitDataStore
{
    /// <summary>
    /// Manages binary shard files for block storage in the hybrid design.
    /// Handles shard rotation, writing, reading, and rollback for the data store.
    /// Thread-safe through external locking (caller must synchronize access).
    /// </summary>
    public class ShardFileManager : IDisposable
    {
        private readonly string _baseDirectory;
        private readonly string _setName;
        private readonly long _shardSize;
        private readonly string? _sourceDirectory;
        private readonly int _writeBufferSize;
        private readonly int _readBufferSize;

        private long _shardBoundary; // 0 = no boundary (separate mode or not yet set)

        private int _currentFileId;
        private FileStream? _currentWriteStream;
        private BufferedStream? _currentBufferedStream;
        private long _currentShardSize;
        private readonly object _writeLock;
        // Read side: keep long-lived FileStreams for shards to avoid expensive open/close per block
        private readonly ConcurrentDictionary<int, FileStream> _readStreams = new ConcurrentDictionary<int, FileStream>();
        private readonly ConcurrentDictionary<int, object> _readLocks = new ConcurrentDictionary<int, object>();

        /// <summary>
        /// Creates a ShardFileManager for the specified set.
        /// </summary>
        /// <param name="baseDirectory">Base directory where shard files are stored.</param>
        /// <param name="setName">Name of the data set.</param>
        /// <param name="shardSize">Maximum size per shard file in bytes (0 = unlimited, no rotation).</param>
        /// <param name="sourceDirectory">Optional original directory for read-only fallback (embedded format on read-only media).</param>
        /// <param name="blockSize">Block size in bytes, used to size the I/O buffers (0 = use defaults).</param>
        public ShardFileManager(string baseDirectory, string setName, long shardSize, string? sourceDirectory = null, int blockSize = 0)
        {
            _currentFileId = 0;
            _currentShardSize = 0;
            _writeLock = new object();
            _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
            _setName = setName ?? throw new ArgumentNullException(nameof(setName));

            _shardSize = shardSize;
            _sourceDirectory = sourceDirectory;
            _writeBufferSize = Math.Max(262144, blockSize); // >= 256 KB, covers a full block in one kernel call
            _readBufferSize = Math.Max(65536, blockSize);   // >= 64 KB, matches typical block size

            // Find the highest existing shard file
            findCurrentShard();

            // Pre-open existing shard files for read to amortise file open costs
            try
            {
                openExistingShardReadStreams();
            }
            catch
            {
                // Best-effort - if pre-open fails we will lazily open on demand
            }
        }

        /// <summary>
        /// Writes a block to the current shard, rotating if necessary.
        /// Returns (fileId, offset, length) for index storage.
        /// NO SPILLOVER: Rotates to new shard BEFORE writing if block would exceed limit.
        /// Thread-safe through internal locking.
        /// In embedded mode, blocks are written during the separate-mode phase (after extraction),
        /// so the shard boundary does not apply during writes. The advancing write position
        /// (_currentShardSize) tracks where the next block will be written, which becomes
        /// the new shard boundary after re-embedding.
        /// </summary>
        /// <param name="data">Block data to write.</param>
        /// <param name="offset">Offset within data array.</param>
        /// <param name="length">Length of data to write.</param>
        /// <returns>Tuple of (fileId, offset within shard, length written).</returns>
        /// <exception cref="IOException">If write fails.</exception>
        public (int fileId, long offset, int length) WriteBlock(byte[] data, int offset, int length)
        {
            lock (_writeLock)
            {
                // NO SPILLOVER: Check if we need to rotate BEFORE writing
                // shardSize 0 means unlimited single shard (no rotation)
                if (_shardSize > 0 && _currentShardSize + length > _shardSize)
                    rotateShard();

                // Ensure current shard is open
                if (_currentWriteStream == null)
                    openCurrentShard();

                long writeOffset = _currentShardSize;

                // Write the block via the buffered stream to reduce syscalls.
                // Ensure a BufferedStream wrapper exists and use it for all writes
                // so that the underlying FileStream is consistently buffered.
                if (_currentBufferedStream == null && _currentWriteStream != null)
                {
                    try
                    {
                        _currentBufferedStream = new BufferedStream(_currentWriteStream, _writeBufferSize);
                    }
                    catch
                    {
                        _currentBufferedStream = null;
                    }
                }

                if (_currentBufferedStream != null)
                {
                    _currentBufferedStream.Write(data, offset, length);
                }
                else
                {
                    // Fallback: write directly to file stream if buffer couldn't be created
                    _currentWriteStream!.Write(data, offset, length);
                }

                // Update size tracking
                _currentShardSize += length;

                return (_currentFileId, writeOffset, length);
            }
        }

        /// <summary>
        /// Rolls back the current shard to the specified size.
        /// Used when a write fails or block already exists (dedup).
        /// Thread-safe through internal locking.
        /// </summary>
        /// <param name="fileId">File ID to rollback (must match current shard).</param>
        /// <param name="targetSize">Target size to truncate to.</param>
        public void RollbackToSize(int fileId, long targetSize)
        {
            lock (_writeLock)
            {
                if (fileId > _currentFileId)
                    return; // Should not happen since fileId comes from a write that occurred

                // If we've rotated shards during the transaction, delete all subsequent shards
                while (_currentFileId > fileId)
                {
                    try
                    {
                        _currentWriteStream?.Dispose();
                        _currentWriteStream = null;

                        string currentPath = getShardPath(_currentFileId);
                        if (File.Exists(currentPath))
                            File.Delete(currentPath);

                        // Also remove from read streams cache
                        if (_readStreams.TryRemove(_currentFileId, out FileStream? rs))
                            try { rs.Dispose(); } catch { }
                        _readLocks.TryRemove(_currentFileId, out _);
                    }
                    catch { }

                    _currentFileId--;
                    _currentShardSize = 0; // Will be set correctly when we reach/open target shard
                }

                // Now at the target fileId shard. Truncate it.
                if (_currentFileId == fileId)
                {
                    if (_currentWriteStream == null)
                        openCurrentShard();

                    if (_currentWriteStream != null && _currentWriteStream.CanSeek)
                    {
                        // Flush and dispose buffered writer so SetLength operates on the
                        // underlying FileStream without buffered state.
                        try { _currentBufferedStream?.Flush(); } catch { }
                        try { _currentBufferedStream?.Dispose(); } catch { }
                        _currentBufferedStream = null;

                        // Disposing the BufferedStream may have closed the underlying FileStream.
                        // Ensure we have a valid writable stream by reopening if necessary.
                        try { if (_currentWriteStream == null || !_currentWriteStream.CanWrite) { try { _currentWriteStream?.Dispose(); } catch { } _currentWriteStream = null; openCurrentShard(); } } catch { }

                        if (_currentWriteStream != null && _currentWriteStream.CanSeek)
                        {
                            _currentWriteStream.SetLength(targetSize);
                            try { _currentWriteStream.Flush(); } catch { }
                            _currentShardSize = targetSize;

                            // Recreate buffered writer wrapping the underlying FileStream so subsequent
                            // writes continue to use the buffered path. Seek to end for append.
                            try
                            {
                                _currentWriteStream.Seek(0, SeekOrigin.End);
                                _currentBufferedStream = new BufferedStream(_currentWriteStream, _writeBufferSize);
                            }
                            catch { /* best-effort */ }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reads a block from the specified shard file.
        /// Thread-safe - uses independent read-only file handle.
        /// If the cached file handle is invalidated (e.g., shard replaced during compaction),
        /// the read is retried once after reopening the shard file.
        /// </summary>
        /// <param name="fileId">Shard file identifier.</param>
        /// <param name="offset">Byte offset within shard file.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>Block data.</returns>
        /// <exception cref="FileNotFoundException">If shard file doesn't exist.</exception>
        /// <exception cref="IOException">If read fails after retry.</exception>
        public byte[] ReadBlock(int fileId, long offset, int length)
        {
            // Validate block offset against shard boundary in embedded mode
            if (_shardBoundary > 0 && offset >= _shardBoundary)
                throw new InvalidOperationException(
                    $"Block offset {offset} is outside the valid data region (shard boundary: {_shardBoundary}). " +
                    $"Block offsets must be less than the shard boundary in embedded mode.");

            string shardPath = resolveShardPath(fileId);

            byte[] buffer = new byte[length];

            try
            {
                readFromShard(fileId, shardPath, offset, length, buffer);
            }
            catch (IOException ex) when (ex is not FileNotFoundException)
            {
                // The shard file handle may have been invalidated because the shard was
                // replaced during compaction. Invalidate the cached stream, reopen, and
                // retry the read once.
                Trace.TraceWarning(
                    $"ShardFileManager: IOException reading shard {fileId} at offset {offset} " +
                    $"(path: '{shardPath}'). Invalidating cached handle and retrying. Error: {ex.Message}");

                InvalidateReadStream(fileId);

                // Re-resolve the shard path in case the file was replaced
                shardPath = resolveShardPath(fileId);

                // Retry once — if this also fails, let the IOException propagate
                readFromShard(fileId, shardPath, offset, length, buffer);
            }

            return buffer;
        }

        /// <summary>
        /// Resolves the shard file path, handling fallback for embedded format.
        /// </summary>
        private string resolveShardPath(int fileId)
        {
            string shardPath = getShardPath(fileId);

            // Fallback for embedded format (shardSize=0): when the shard file doesn't exist,
            // the block data lives at the start of the main set file (before the appended compressed DB).
            // Block offsets are relative to the start of the file, so reading works correctly.
            if (!File.Exists(shardPath))
            {
                if (_sourceDirectory != null)
                {
                    // When a source directory is set, the base directory contains the extracted
                    // SQLite DB (not block data). Block data lives in the source (packed) file.
                    string sourceMain = Path.Combine(_sourceDirectory, $"{_setName}{DataStore.DatabaseFileExtension}");
                    if (File.Exists(sourceMain))
                        shardPath = sourceMain;
                    else
                        throw new FileNotFoundException($"Shard file not found: {shardPath}");
                }
                else
                {
                    string mainPath = Path.Combine(getShardDirectory(), $"{getSetBaseName()}{DataStore.DatabaseFileExtension}");
                    if (File.Exists(mainPath))
                        shardPath = mainPath;
                    else
                        throw new FileNotFoundException($"Shard file not found: {shardPath}");
                }
            }

            return shardPath;
        }

        /// <summary>
        /// Performs the actual shard read I/O into the provided buffer.
        /// Uses a cached read stream if available, otherwise opens a new one.
        /// </summary>
        private void readFromShard(int fileId, string shardPath, long offset, int length, byte[] buffer)
        {
            // Try to use a long-lived read stream for this shard
            FileStream? rs = null;

            if (!_readStreams.TryGetValue(fileId, out rs) || rs == null)
            {
                // If shard file exists, open and cache a read stream for reuse
                if (File.Exists(shardPath))
                {
                    try
                    {
                        rs = openAndCacheReadStream(fileId, shardPath);
                    }
                    catch
                    {
                        // Fall through - will attempt on-demand open below
                        rs = null;
                    }
                }
            }

            try
            {
                if (rs != null)
                {
#if NET6_0_OR_GREATER
                    // RandomAccess.Read is position-independent: the OS reads at the given offset
                    // without moving the file pointer, making it inherently thread-safe.
                    // No lock or Seek() required - concurrent reads on the same handle are safe.
                    int total = 0;
                    while (total < length)
                    {
                        int read = RandomAccess.Read(rs.SafeFileHandle, buffer.AsSpan(total, length - total), offset + total);
                        if (read == 0)
                            throw new IOException($"Expected to read {length} bytes from '{shardPath}', but read {total}");
                        total += read;
                    }
#else
                    // .NET Standard 2.1: Seek+Read must be atomic - serialize per shard
                    object lockObj = _readLocks.GetOrAdd(fileId, _ => new object());
                    lock (lockObj)
                    {
                        rs.Seek(offset, SeekOrigin.Begin);
                        int total = 0;
                        while (total < length)
                        {
                            int read = rs.Read(buffer, total, length - total);
                            if (read == 0)
                                throw new IOException($"Expected to read {length} bytes from '{shardPath}', but read {total}");
                            total += read;
                        }
                    }
#endif
                }
                else
                {
                    // No cached stream available - fall back to synchronous open/read/close (rare)
                    using FileStream fs = new FileStream(shardPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    fs.Seek(offset, SeekOrigin.Begin);
                    int total = 0;
                    while (total < length)
                    {
                        int read = fs.Read(buffer, total, length - total);
                        if (read == 0)
                            throw new IOException($"Expected to read {length} bytes from '{shardPath}', but read {total}");
                        total += read;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                // File was deleted between Exists check and Open - fail fast
                throw new FileNotFoundException($"Shard file disappeared: {shardPath}");
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied - fail fast
                throw new IOException($"Access denied to shard file: {shardPath}");
            }
        }

        /// <summary>
        /// Reads a contiguous byte range from a shard file in a single I/O call.
        /// Used by batch block reads to coalesce multiple adjacent blocks into one read.
        /// </summary>
        /// <param name="fileId">The shard file ID.</param>
        /// <param name="offset">The starting byte offset within the shard.</param>
        /// <param name="totalLength">Total number of bytes to read.</param>
        /// <returns>A byte array containing the requested range.</returns>
        public byte[] ReadRange(int fileId, long offset, int totalLength) =>
            // ReadRange is functionally identical to ReadBlock but named distinctly
            // to clarify intent (reading a multi-block contiguous range).
            ReadBlock(fileId, offset, totalLength);

        /// <summary>
        /// Invalidates and disposes the cached read stream for the specified shard file.
        /// The next read will reopen the file, picking up any replacement that occurred
        /// during compaction. Thread-safe.
        /// </summary>
        /// <param name="fileId">The shard file ID whose cached stream should be invalidated.</param>
        public void InvalidateReadStream(int fileId)
        {
            if (_readStreams.TryRemove(fileId, out FileStream? rs))
            {
                try { rs.Dispose(); } catch { }
            }
        }

        private FileStream openAndCacheReadStream(int fileId, string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, _readBufferSize, FileOptions.RandomAccess);
            if (!_readStreams.TryAdd(fileId, fs))
            {
                // Another thread added concurrently - dispose ours and use existing
                try { fs.Dispose(); } catch { }
                return _readStreams[fileId];
            }
            _readLocks.GetOrAdd(fileId, _ => new object());
            return fs;
        }

        private void openExistingShardReadStreams()
        {
            string dir = getShardDirectory();
            if (!Directory.Exists(dir))
                return;

            string baseName = getSetBaseName();
            foreach (string file in Directory.EnumerateFiles(dir, $"{baseName}_*.nkds"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string[] parts = fileName.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[^1], out int fileId))
                {
                    try
                    {
                        openAndCacheReadStream(fileId, file);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Gets the current file ID (useful for tracking which shard is active).
        /// </summary>
        public int CurrentFileId => _currentFileId;

        /// <summary>
        /// Gets the current shard size (useful for rollback tracking).
        /// </summary>
        public long CurrentShardSize
        {
            get
            {
                lock (_writeLock)
                    return _currentShardSize;
            }
        }

        /// <summary>
        /// Sets the shard boundary for embedded mode validation.
        /// When set to a value greater than 0, ReadBlock will validate that block offsets
        /// are within the valid data region (before the boundary where the embedded index begins).
        /// Set to 0 to disable boundary checking (separate mode or not yet set).
        /// </summary>
        /// <param name="boundary">The byte offset where block data ends and the embedded index begins. 0 = no boundary.</param>
        public void SetShardBoundary(long boundary) => _shardBoundary = boundary;

        /// <summary>
        /// Gets the current shard boundary. 0 means no boundary is set (separate mode).
        /// </summary>
        public long ShardBoundary => _shardBoundary;

        public void Dispose()
        {
            lock (_writeLock)
            {
                try { _currentBufferedStream?.Flush(); } catch { }
                try { _currentBufferedStream?.Dispose(); } catch { }
                _currentBufferedStream = null;

                _currentWriteStream?.Dispose();
                _currentWriteStream = null;
            }
            // Dispose any cached read streams
            foreach (KeyValuePair<int, FileStream> kv in _readStreams)
                try { kv.Value?.Dispose(); } catch { }
            _readStreams.Clear();
            _readLocks.Clear();
        }

        /// <summary>
        /// Flushes the write buffer to disk, ensuring all written data is visible to readers.
        /// Call this after completing a batch of writes (e.g., after transaction commit)
        /// so that subsequent ReadBlock calls can see the data.
        /// Thread-safe through internal locking.
        /// </summary>
        public void FlushWrite()
        {
            lock (_writeLock)
            {
                try { _currentBufferedStream?.Flush(); } catch { }
                try { _currentWriteStream?.Flush(); } catch { }
            }
        }

        /// <summary>
        /// Finds the highest numbered shard file that exists.
        /// Sets _currentFileId and _currentShardSize accordingly.
        /// </summary>
        private void findCurrentShard()
        {
            int maxFileId = -1;

            string shardDirectory = getShardDirectory();
            if (!Directory.Exists(shardDirectory))
                return;

            foreach (string file in Directory.EnumerateFiles(shardDirectory, $"{getSetBaseName()}_*.nkds"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string[] parts = fileName.Split('_');

                if (parts.Length >= 2 && int.TryParse(parts[^1], out int fileId))
                    maxFileId = Math.Max(maxFileId, fileId);
            }

            if (maxFileId >= 0)
            {
                _currentFileId = maxFileId;
                string shardPath = getShardPath(_currentFileId);
                if (File.Exists(shardPath))
                    _currentShardSize = new FileInfo(shardPath).Length;
            }
        }

        /// <summary>
        /// Opens the current shard for writing (append mode).
        /// FileShare.ReadWrite allows simultaneous reads while writing (required for GetBlockData during active writes).
        /// </summary>
        private void openCurrentShard()
        {
            string shardPath = getShardPath(_currentFileId);
            Directory.CreateDirectory(getShardDirectory());

            _currentWriteStream = new FileStream(
                shardPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite,
                _writeBufferSize); // Allow reads while writing!

            _currentWriteStream.Seek(0, SeekOrigin.End);
            _currentShardSize = _currentWriteStream.Position;
            // Wrap with a BufferedStream for additional managed buffering to reduce
            // syscall frequency and improve throughput.
            try
            {
                _currentBufferedStream = new BufferedStream(_currentWriteStream, _writeBufferSize);
            }
            catch { _currentBufferedStream = null; }
        }

        /// <summary>
        /// Closes current shard and opens a new one.
        /// Called when current shard would exceed size limit.
        /// </summary>
        private void rotateShard()
        {
            // Close current shard: flush and dispose buffered writer then underlying stream
            try { _currentBufferedStream?.Flush(); } catch { }
            try { _currentBufferedStream?.Dispose(); } catch { }
            _currentBufferedStream = null;
            try { _currentWriteStream?.Dispose(); } catch { }
            _currentWriteStream = null;

            // Increment file ID
            _currentFileId++;
            _currentShardSize = 0;

            // Open new shard
            openCurrentShard();
        }

        /// <summary>
        /// Gets the file path for a specific shard ID.
        /// Format: {setName}_{fileId:D4}.nkds
        /// </summary>
        private string getShardPath(int fileId) => Path.Combine(getShardDirectory(), $"{getSetBaseName()}_{fileId:D4}.nkds");

        private string getShardDirectory() => Path.GetDirectoryName(Path.Combine(_baseDirectory, _setName)) ?? _baseDirectory;

        private string getSetBaseName() => Path.GetFileName(_setName);
    }
}
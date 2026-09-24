namespace NKitDataStore.Interfaces
{
    /// <summary>
    /// Manages shared mount-time resources (ImageReaders and buffer caches) with
    /// reference counting, TTL-based disposal, and deterministic shutdown.
    /// Thread-safe for concurrent acquire/release from multiple FUSE/Dokan threads.
    /// </summary>
    internal interface IMountResourceManager : IDisposable
    {
        /// <summary>
        /// Acquires a shared ImageReader for the given image. Creates one on first access.
        /// Thread-safe. Caller must call ReleaseReader when done.
        /// </summary>
        /// <param name="setName">The set name containing the image.</param>
        /// <param name="imageId">The unique image identifier.</param>
        /// <returns>A shared IImageReader instance for the specified image.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if called after Shutdown.</exception>
        IImageReader AcquireReader(string setName, long imageId);

        /// <summary>
        /// Releases a previously acquired reader. When ref count hits zero,
        /// starts a TTL timer before actual disposal.
        /// </summary>
        /// <param name="setName">The set name containing the image.</param>
        /// <param name="imageId">The unique image identifier.</param>
        void ReleaseReader(string setName, long imageId);

        /// <summary>
        /// Acquires a shared buffer cache for the given image. Creates one on first access.
        /// Thread-safe. Caller must call ReleaseBufferCache when done.
        /// </summary>
        /// <param name="imageId">The unique image identifier.</param>
        /// <param name="bufferSize">The size of each buffer in the cache.</param>
        /// <param name="maxCachedBuffers">The maximum number of buffers to cache.</param>
        /// <returns>A shared ImageBufferCache instance for the specified image.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if called after Shutdown.</exception>
        ImageBufferCache AcquireBufferCache(long imageId, int bufferSize, int maxCachedBuffers);

        /// <summary>
        /// Releases a previously acquired buffer cache reference.
        /// </summary>
        /// <param name="imageId">The unique image identifier.</param>
        void ReleaseBufferCache(long imageId);

        /// <summary>
        /// Acquires a cached OffsetsManager bundle for the given image. Creates one on first access
        /// by querying the reader for areas/offsets and building the OffsetsManager.
        /// Thread-safe. Caller must call ReleaseOffsetsManager when done.
        /// </summary>
        /// <param name="setName">The set name containing the image.</param>
        /// <param name="imageId">The unique image identifier.</param>
        /// <param name="sectionSize">Section size for OffsetsManager construction.</param>
        /// <param name="storeBlockSize">Store block size for OffsetsManager construction.</param>
        /// <returns>A cached result containing OffsetsManager, areas list, and offsets dictionary.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if called after Shutdown.</exception>
        OffsetsManagerCacheResult AcquireOffsetsManager(string setName, long imageId, int sectionSize, int storeBlockSize);

        /// <summary>
        /// Releases a previously acquired OffsetsManager cache reference.
        /// When ref count hits zero, starts a TTL timer before removal.
        /// </summary>
        /// <param name="setName">The set name containing the image.</param>
        /// <param name="imageId">The unique image identifier.</param>
        void ReleaseOffsetsManager(string setName, long imageId);

        /// <summary>
        /// Synchronous forced shutdown. Disposes ALL readers and buffer caches immediately,
        /// bypassing TTL. Signals any waiting threads to abort. Must complete before returning.
        /// </summary>
        void Shutdown();
    }
}
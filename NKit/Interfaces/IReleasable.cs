namespace Nanook.NKit
{
    /// <summary>
    /// Optional capability implemented by an <see cref="IAsIso"/> container that reads directly
    /// from a <see cref="BufferStream"/> source (e.g. <see cref="Container.DefaultAsIso"/>). It lets
    /// a consumer that drives the container through the <see cref="IAsIso"/> interface — rather than
    /// through the BufferStream itself — bound the shared cache by declaring how far it has consumed.
    ///
    /// <para>
    /// This exists for the ISO/PS3 path: a container (e.g. DefaultAsIso over a forward-only archive
    /// entry) reads the raw image at its own position, so the consuming Image releases the raw
    /// source cache by calling <c>(_iso as IReleasable)?.ReleaseTo(position)</c> after each forward
    /// read (alongside its own <see cref="BufferStream.ReleaseTo"/>). Without this the raw source
    /// cache would grow to hold the whole (40+ GiB) image and OOM. Containers that do not wrap a
    /// releasable BufferStream simply do not implement this, and consumers treat a missing
    /// implementation as "nothing to release".
    /// </para>
    /// </summary>
    internal interface IReleasable
    {
        /// <summary>
        /// Release cached source data below <paramref name="position"/> (the container's own
        /// forward read position). Whole cache blocks below it are freed. Safe to call after every
        /// forward read; a no-op when the underlying source is seekable (uncached).
        /// </summary>
        void ReleaseTo(long position);
    }
}
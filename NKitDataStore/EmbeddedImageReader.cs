using NKitDataStore.Compression;
using NKitDataStore.Interfaces;

namespace NKitDataStore
{
    /// <summary>
    /// An ImageReader that runs a cleanup action on dispose.
    /// Used when reading from embedded (shardSize=0) sets where the file layout
    /// is temporarily unpacked for the duration of the read and must be re-packed on close.
    /// Re-implements IDisposable to ensure cleanup runs even when disposed through interface references.
    /// </summary>
    internal class EmbeddedImageReader : ImageReader, IDisposable
    {
        private readonly Action _cleanupAction;

        internal EmbeddedImageReader(IDataStoreDataAccess dataAccess, ImageRecord image, IBlockCompressor compressor, Action cleanupAction, bool ownsDataAccess = true)
            : base(dataAccess, image, compressor, ownsDataAccess: ownsDataAccess)
        {
            _cleanupAction = cleanupAction;
        }

        void IDisposable.Dispose()
        {
            try
            {
                base.Dispose();
            }
            finally
            {
                try { _cleanupAction(); } catch { }
            }
        }
    }
}
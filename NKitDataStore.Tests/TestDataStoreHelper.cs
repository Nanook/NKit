using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    internal static class TestDataStoreHelper
    {
        public static IImageWriter AddImage(DataStore store, string setName, string imageName, long shardSize = 50L * 1024 * 1024 * 1024, string system = null, ImageFormat format = ImageFormat.Unknown, int blockSize = 0)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.GetSetInfo(setName) == null)
            {
                store.CreateSet(setName, shardSize, blockSize);
            }

            return store.AddImage(setName, imageName, system, format);
        }

        /// <summary>
        /// Best-effort wait for background work related to a set to complete.
        /// This helper is test-only and does not change production behaviour.
        /// It proxies to the new `internal DataStore.WaitForSetIdle` method which
        /// calls into the underlying data-access synchronization APIs (e.g.,
        /// `WaitForCompressionTasks` and `FlushSet`) when available. The
        /// test assembly can call this internal API because `InternalsVisibleTo`
        /// is configured for `NKitDataStore.Tests`.
        /// </summary>
        public static void WaitForSetIdle(DataStore store, string setName, int timeoutMs = 30000)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (setName == null) throw new ArgumentNullException(nameof(setName));

            // Call the new internal API on DataStore directly for deterministic waits.
            store.WaitForSetIdle(setName, timeoutMs);
        }
    }
}
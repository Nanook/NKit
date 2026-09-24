namespace NKitDataStore.Binary
{
    /// <summary>
    /// Provides atomic file replacement with platform fallback.
    /// </summary>
    internal static class AtomicFileOps
    {
        /// <summary>
        /// Atomically replaces destinationPath with sourcePath.
        /// Uses File.Replace on supported platforms, falls back to Delete+Move otherwise.
        /// </summary>
        /// <param name="sourcePath">The path of the file that will replace the destination.</param>
        /// <param name="destinationPath">The path of the file to be replaced.</param>
        /// <param name="logWarning">Optional callback invoked with a warning message when falling back to non-atomic replacement.</param>
        public static void ReplaceFile(string sourcePath, string destinationPath, Action<string>? logWarning = null)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, destinationBackupFileName: null);
            }
            catch (PlatformNotSupportedException)
            {
                logWarning?.Invoke(
                    $"File.Replace not supported on this platform. Falling back to Delete+Move for '{destinationPath}'.");
                fallbackReplace(sourcePath, destinationPath);
            }
            catch (IOException)
            {
                logWarning?.Invoke(
                    $"File.Replace failed with IOException. Falling back to Delete+Move for '{destinationPath}'.");
                fallbackReplace(sourcePath, destinationPath);
            }
        }

        private static void fallbackReplace(string sourcePath, string destinationPath)
        {
            File.Delete(destinationPath);
            File.Move(sourcePath, destinationPath);
        }
    }
}
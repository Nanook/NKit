using System;
using System.IO;

namespace NKit.Tests
{
    /// <summary>
    /// Resolves paths to external test asset directories that are not part of the repo.
    ///
    /// On Windows the original hardcoded paths (D:\NKitFiles, etc.) are used as-is.
    /// On Linux (or in Docker), the paths are remapped via environment variables or
    /// known container mount points so the same tests can run without copying files.
    ///
    /// Environment variables (set by build/test.sh):
    ///   NKIT_FILES_CONTAINER_PATH  — root mount for D:\NKitFiles (default: /NKitFiles)
    /// </summary>
    public static class TestPaths
    {
        /// <summary>
        /// Translates a path that begins with "D:\NKitFiles" to the platform-appropriate location.
        /// On Windows: returned unchanged (backslashes preserved).
        /// On Linux/macOS: D:\NKitFiles is replaced with $NKIT_FILES_CONTAINER_PATH (default /NKitFiles),
        /// and all remaining backslashes are converted to forward slashes.
        /// </summary>
        public static string Resolve(string path)
        {
            if (path == null) return null;

            if (System.IO.Path.DirectorySeparatorChar == '/')
            {
                // Linux / macOS / Docker
                string nkitFilesRoot = Environment.GetEnvironmentVariable("NKIT_FILES_CONTAINER_PATH")
                                       ?? "/NKitFiles";

                // Replace the Windows drive+root prefix with the container mount point
                path = path.Replace(@"D:\NKitFiles", nkitFilesRoot)
                           .Replace(@"D:/NKitFiles", nkitFilesRoot);

                // Normalise remaining backslashes
                path = path.Replace('\\', '/');
            }

            return path;
        }

        /// <summary>Checks whether the resolved path exists. Use to skip tests gracefully when
        /// the asset directory is not available in the current environment.</summary>
        public static bool Exists(string path) => File.Exists(Resolve(path)) || Directory.Exists(Resolve(path));
    }
}

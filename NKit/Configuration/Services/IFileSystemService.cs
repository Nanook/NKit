using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Abstraction for file system operations to enable unit testing
    /// </summary>
    public interface IFileSystemService
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        void CopyFile(string sourcePath, string destinationPath);
        void WriteAllText(string path, string content);
        string ReadAllText(string path);

        /// <summary>
        /// Gets files in a directory with optional search pattern and search option
        /// </summary>
        IEnumerable<string> GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);

        /// <summary>
        /// Combines path segments using appropriate path separators for the target platform
        /// Allows mock implementations to normalize path separators for testing
        /// </summary>
        string CombinePath(params string[] paths);

        /// <summary>
        /// Normalizes a path to use the OS-appropriate directory separators.
        /// This converts NKit's internal forward-slash paths to the proper OS format for file system operations.
        /// </summary>
        string NormalizePath(string path);

        /// <summary>
        /// Gets the parent directory of a path in a cross-platform manner.
        /// This method correctly handles both Unix and Windows paths regardless of the host OS.
        /// </summary>
        /// <param name="path">The path to get the parent directory from</param>
        /// <returns>The parent directory path, or null if the path has no parent</returns>
        string GetParentDirectory(string path);
    }

    /// <summary>
    /// Default implementation of file system service
    /// </summary>
    internal class FileSystemService : IFileSystemService
    {
        public bool FileExists(string path) => File.Exists(NormalizePath(path));
        public bool DirectoryExists(string path) => Directory.Exists(NormalizePath(path));

        public void CreateDirectory(string path) => Directory.CreateDirectory(NormalizePath(path));

        public void CopyFile(string sourcePath, string destinationPath) => File.Copy(NormalizePath(sourcePath), NormalizePath(destinationPath));

        public void WriteAllText(string path, string content) => File.WriteAllText(NormalizePath(path), content);

        public string ReadAllText(string path) => File.ReadAllText(NormalizePath(path));

        public IEnumerable<string> GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly) => Directory.GetFiles(NormalizePath(path), searchPattern, searchOption);

        /// <summary>
        /// Combines path segments using the system's native Path.Combine method
        /// </summary>
        public string CombinePath(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return string.Empty;

            if (paths.Length == 1)
                return NormalizePath(paths[0]);

            // Use native Path.Combine which automatically uses OS-appropriate separators
            return Path.Combine(paths);
        }

        /// <summary>
        /// Normalizes a path to use the OS-appropriate directory separators.
        /// This converts NKit's internal forward-slash paths to the proper OS format for file system operations.
        /// </summary>
        public string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Handle NKit's internal forward slash paths and convert to OS-appropriate separators
            // but preserve archive separators (//) for NKit's internal use
            if (path.Contains("//"))
            {
                // This is an archive path pattern - don't modify it
                return path;
            }

            // Convert forward slashes to the OS-appropriate separator for file system operations
            if (Path.DirectorySeparatorChar == '\\')
            {
                // Windows: convert forward slashes to backslashes for file system operations
                return path.Replace('/', '\\');
            }
            else
            {
                // Unix-like systems: ensure forward slashes (already correct)
                return path.Replace('\\', '/');
            }
        }

        /// <summary>
        /// Gets the parent directory of a path using Path.GetDirectoryName.
        /// For cross-platform testing, use the mock implementation which handles both path formats.
        /// </summary>
        public string GetParentDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Use standard .NET Path.GetDirectoryName for production code
            return Path.GetDirectoryName(NormalizePath(path));
        }
    }
}

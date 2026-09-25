using Nanook.NKit.Configuration;

namespace NkdsUi.Services;

/// <summary>
/// Converts between relative and absolute paths using the NKit configuration directory as the base.
/// In portable mode this is the executable directory; in system mode it's the platform config directory
/// (e.g. %APPDATA%/nkit on Windows). Relative paths are used for storage and display when the path
/// is under the config directory, making the config portable when the application directory is moved.
/// </summary>
public static class PathResolver
{
    private static string? _appDirectory;

    /// <summary>
    /// Gets the configuration directory used as the base for relative path resolution.
    /// Resolved once from the NKit ConfigurationManager on first access.
    /// </summary>
    internal static string AppDirectory
    {
        get
        {
            if (_appDirectory == null)
                _appDirectory = ResolveConfigDirectory();
            return _appDirectory;
        }
    }

    /// <summary>
    /// Resolves the NKit configuration directory. In portable mode this is the executable directory;
    /// in system mode it's the platform-specific config directory (e.g. %APPDATA%/nkit on Windows).
    /// Falls back to AppContext.BaseDirectory if resolution fails.
    /// </summary>
    private static string ResolveConfigDirectory()
    {
        try
        {
            using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
            ConfigurationInfo info = configManager.GetConfigurationInfo();
            if (!string.IsNullOrEmpty(info.ConfigDirectory))
                return info.ConfigDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch
        {
            // Fall back to executable directory
        }
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Resolves a stored path to an absolute path.
    /// If the path is already absolute, returns it unchanged.
    /// If the path is relative, resolves it against the App_Directory.
    /// </summary>
    public static string ToAbsolute(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return storedPath;

        if (Path.IsPathRooted(storedPath))
            return Path.GetFullPath(storedPath);

        return Path.GetFullPath(Path.Combine(AppDirectory, storedPath));
    }

    /// <summary>
    /// Converts an absolute path to a relative path if it is located under the App_Directory.
    /// Returns the absolute path unchanged if it is outside the App_Directory.
    /// If the path is already relative, returns it as-is.
    /// </summary>
    public static string ToStorable(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return absolutePath;

        // If not rooted, it's already relative — return as-is
        if (!Path.IsPathRooted(absolutePath))
            return absolutePath;

        string normalizedPath = Path.GetFullPath(absolutePath);
        string normalizedBase = Path.GetFullPath(AppDirectory);

        // Ensure the base directory ends with a separator for correct prefix matching
        if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
            normalizedBase += Path.DirectorySeparatorChar;

        if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(normalizedBase, normalizedPath);

        return absolutePath;
    }

    /// <summary>
    /// Returns the display string for a stored path:
    /// relative if the resolved path is under the App_Directory, absolute otherwise.
    /// </summary>
    public static string ToDisplay(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return storedPath;

        // If already relative, it's under App_Directory by convention — display as relative
        if (!Path.IsPathRooted(storedPath))
            return storedPath;

        // Absolute path: check if it's under App_Directory
        string normalizedPath = Path.GetFullPath(storedPath);
        string normalizedBase = Path.GetFullPath(AppDirectory);

        if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
            normalizedBase += Path.DirectorySeparatorChar;

        if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(normalizedBase, normalizedPath);

        return storedPath;
    }
}
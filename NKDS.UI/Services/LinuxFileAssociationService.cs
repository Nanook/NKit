using NkdsUi.Models;
using System.Diagnostics;

namespace NkdsUi.Services;

/// <summary>
/// Linux implementation of file association service using freedesktop.org standards.
/// Installs .desktop files for file manager context menu entries and file associations,
/// and registers MIME types for .nkds files.
/// </summary>
public class LinuxFileAssociationService : IFileAssociationService
{
    private const string MimeType = "application/x-nkds";
    private const string MimeXmlFileName = "application-x-nkds.xml";

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public string DirectoryContextMenuLabel => "File Manager Context Menu";

    /// <inheritdoc />
    public Task<AssociationQueryResult> QueryStateAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        try
        {
            string desktopFilePath = GetDesktopFilePath(entry);
            if (!File.Exists(desktopFilePath))
            {
                return Task.FromResult(new AssociationQueryResult
                {
                    State = AssociationState.NotRegistered
                });
            }

            string currentExePath = GetExecutablePath();
            string? embeddedPath = ParseExecPath(desktopFilePath);

            if (embeddedPath == null)
            {
                return Task.FromResult(new AssociationQueryResult
                {
                    State = AssociationState.NotRegistered
                });
            }

            AssociationState state = string.Equals(embeddedPath, currentExePath, StringComparison.Ordinal)
                ? AssociationState.Registered
                : AssociationState.Stale;

            return Task.FromResult(new AssociationQueryResult { State = state });
        }
        catch (IOException ex)
        {
            return Task.FromResult(new AssociationQueryResult
            {
                State = AssociationState.Unknown,
                ErrorMessage = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(new AssociationQueryResult
            {
                State = AssociationState.Unknown,
                ErrorMessage = ex.Message
            });
        }
    }

    /// <inheritdoc />
    public Task<AssociationOperationResult> RegisterAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        try
        {
            string desktopFilePath = GetDesktopFilePath(entry);
            string directory = Path.GetDirectoryName(desktopFilePath)!;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string exePath = GetExecutablePath();
            string content = GenerateDesktopFileContent(entry, exePath);
            File.WriteAllText(desktopFilePath, content);

            // For double-click handler, also ensure MIME type is registered
            if (entry.Category == AssociationCategory.DoubleClickHandler)
            {
                EnsureMimeTypeRegistered(exePath);
            }

            // For file context menu entries, ensure MIME type is registered so .nkds files are recognized
            if (entry.Category == AssociationCategory.FileContextMenu)
            {
                EnsureMimeTypeRegistered(exePath);
            }

            RunUpdateDatabases();

            return Task.FromResult(new AssociationOperationResult { Success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RequiresElevation = true
            });
        }
        catch (IOException ex)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    /// <inheritdoc />
    public Task<AssociationOperationResult> UnregisterAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        try
        {
            string desktopFilePath = GetDesktopFilePath(entry);

            if (File.Exists(desktopFilePath))
            {
                File.Delete(desktopFilePath);
            }

            RunUpdateDatabases();

            return Task.FromResult(new AssociationOperationResult { Success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RequiresElevation = true
            });
        }
        catch (IOException ex)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    /// <summary>
    /// Gets the full path to the .desktop file for the given entry.
    /// </summary>
    private string GetDesktopFilePath(AssociationEntry entry)
    {
        string localShare = GetLocalSharePath();

        return entry.Id switch
        {
            "dir-open" => Path.Combine(localShare, "kio", "servicemenus", "nkds-open.desktop"),
            "dir-mount" => Path.Combine(localShare, "kio", "servicemenus", "nkds-mount.desktop"),
            "dir-adddir" => Path.Combine(localShare, "kio", "servicemenus", "nkds-adddir.desktop"),
            "dir-adddir-new" => Path.Combine(localShare, "kio", "servicemenus", "nkds-adddir-new.desktop"),
            "file-open-set" => Path.Combine(localShare, "applications", "nkds-ui-open-set.desktop"),
            "file-mount-set" => Path.Combine(localShare, "applications", "nkds-ui-mount-set.desktop"),
            "file-add" => Path.Combine(localShare, "kio", "servicemenus", "nkds-add.desktop"),
            "file-add-new" => Path.Combine(localShare, "kio", "servicemenus", "nkds-add-new.desktop"),
            "dblclick-open" => Path.Combine(localShare, "applications", "nkds-ui-dblclick-open.desktop"),
            _ => throw new ArgumentException($"Unknown association entry ID: {entry.Id}", nameof(entry))
        };
    }

    /// <summary>
    /// Gets the ~/.local/share path. Virtual to allow test overrides.
    /// </summary>
    protected virtual string GetLocalSharePath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share");
    }

    /// <summary>
    /// Resolves the current executable path via Environment.ProcessPath or /proc/self/exe.
    /// Virtual to allow test overrides.
    /// </summary>
    internal virtual string GetExecutablePath()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        // Fallback to /proc/self/exe
        const string procSelfExe = "/proc/self/exe";
        if (File.Exists(procSelfExe))
        {
            // ReadLink equivalent - the file itself is a symlink to the actual executable
            return Path.GetFullPath(procSelfExe);
        }

        throw new InvalidOperationException("Unable to determine the executable path.");
    }

    /// <summary>
    /// Generates the .desktop file content for the given entry.
    /// </summary>
    internal static string GenerateDesktopFileContent(AssociationEntry entry, string exePath)
    {
        // Format the command argument template: replace {0} with %f (freedesktop file placeholder)
        string args = entry.CommandArgTemplate.Replace("\"{0}\"", "%f", StringComparison.Ordinal);

        return entry.Category switch
        {
            AssociationCategory.DirectoryContextMenu => GenerateDirectoryActionContent(entry, exePath, args),
            AssociationCategory.FileContextMenu => GenerateFileAssociationContent(entry, exePath, args),
            AssociationCategory.DoubleClickHandler => GenerateFileAssociationContent(entry, exePath, args),
            _ => throw new ArgumentException($"Unknown category: {entry.Category}", nameof(entry))
        };
    }

    private static string GenerateDirectoryActionContent(AssociationEntry entry, string exePath, string args)
    {
        // KDE Service Menu format (works with Dolphin/KDE 6+)
        string actionId = entry.Id.Replace("-", "", StringComparison.Ordinal);
        return $"""
            [Desktop Entry]
            Type=Service
            MimeType=inode/directory
            Actions={actionId}

            [Desktop Action {actionId}]
            Name={entry.Label}
            Icon=nkds
            Exec={exePath} {args}
            """.Replace("            ", "", StringComparison.Ordinal);
    }

    private static string GenerateFileAssociationContent(AssociationEntry entry, string exePath, string args)
    {
        return $"""
            [Desktop Entry]
            Type=Application
            Name={entry.Label}
            Exec={exePath} {args}
            MimeType={MimeType}
            NoDisplay=true
            """.Replace("            ", "", StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the application/x-nkds MIME type is registered.
    /// </summary>
    private void EnsureMimeTypeRegistered(string exePath)
    {
        string localShare = GetLocalSharePath();
        string mimePackagesDir = Path.Combine(localShare, "mime", "packages");

        if (!Directory.Exists(mimePackagesDir))
        {
            Directory.CreateDirectory(mimePackagesDir);
        }

        string mimeXmlPath = Path.Combine(mimePackagesDir, MimeXmlFileName);

        string mimeXmlContent = """
            <?xml version="1.0" encoding="UTF-8"?>
            <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
              <mime-type type="application/x-nkds">
                <comment>NKit DataStore Set</comment>
                <glob pattern="*.nkds"/>
              </mime-type>
            </mime-info>
            """.Replace("            ", "", StringComparison.Ordinal);

        File.WriteAllText(mimeXmlPath, mimeXmlContent);
    }

    /// <summary>
    /// Parses the Exec= line from a .desktop file and extracts the executable path.
    /// </summary>
    internal static string? ParseExecPath(string desktopFilePath)
    {
        try
        {
            foreach (string line in File.ReadLines(desktopFilePath))
            {
                if (line.StartsWith("Exec=", StringComparison.Ordinal))
                {
                    string execValue = line.Substring("Exec=".Length).Trim();
                    // The executable path is the first token before any arguments
                    int spaceIndex = execValue.IndexOf(' ');
                    return spaceIndex >= 0 ? execValue.Substring(0, spaceIndex) : execValue;
                }
            }
        }
        catch (IOException)
        {
            // File read error - treat as not parseable
        }
        catch (UnauthorizedAccessException)
        {
            // Permission error - treat as not parseable
        }

        return null;
    }

    /// <summary>
    /// Runs update-mime-database and update-desktop-database to refresh system caches.
    /// Fire and forget - does not fail if commands are not found.
    /// Virtual to allow test overrides.
    /// </summary>
    protected virtual void RunUpdateDatabases()
    {
        string localShare = GetLocalSharePath();

        TryRunCommand("update-mime-database", Path.Combine(localShare, "mime"));
        TryRunCommand("update-desktop-database", Path.Combine(localShare, "applications"));
    }

    /// <summary>
    /// Attempts to run a command with the given argument. Does not throw on failure.
    /// </summary>
    private static void TryRunCommand(string command, string argument)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = argument,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(startInfo);
            // Fire and forget - don't wait for completion or check exit code
        }
        catch
        {
            // Silently ignore if command is not found or fails to start
        }
    }
}
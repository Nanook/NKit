using Microsoft.Win32;
using NkdsUi.Models;
using System.Runtime.Versioning;

namespace NkdsUi.Services;

/// <summary>
/// Windows implementation of <see cref="IFileAssociationService"/> using HKCU registry entries.
/// All operations target HKEY_CURRENT_USER\Software\Classes to avoid requiring elevation.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsFileAssociationService : IFileAssociationService
{
    private const string ClassesRoot = @"Software\Classes";

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public string DirectoryContextMenuLabel => "Explorer Context Menu";

    /// <inheritdoc />
    public Task<AssociationQueryResult> QueryStateAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        try
        {
            (string? keyPath, string _) = GetRegistryPaths(entry);
            string commandKeyPath = keyPath + @"\command";

            using RegistryKey? commandKey = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\" + commandKeyPath);
            if (commandKey == null)
            {
                return Task.FromResult(new AssociationQueryResult
                {
                    State = AssociationState.NotRegistered
                });
            }

            string? commandValue = commandKey.GetValue(null) as string;
            if (string.IsNullOrEmpty(commandValue))
            {
                return Task.FromResult(new AssociationQueryResult
                {
                    State = AssociationState.NotRegistered
                });
            }

            string? currentExePath = Environment.ProcessPath;
            if (currentExePath != null && commandValue.Contains(currentExePath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new AssociationQueryResult
                {
                    State = AssociationState.Registered
                });
            }

            return Task.FromResult(new AssociationQueryResult
            {
                State = AssociationState.Stale
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new AssociationQueryResult
            {
                State = AssociationState.Unknown,
                ErrorMessage = "Access denied. Elevated permissions may be required."
            });
        }
        catch (Exception ex)
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
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine current executable path.");

            (string? keyPath, string? label) = GetRegistryPaths(entry);
            string fullKeyPath = ClassesRoot + @"\" + keyPath;
            string commandKeyPath = fullKeyPath + @"\command";

            // Build the command string with the appropriate placeholder
            string placeholder = entry.Category == AssociationCategory.DirectoryContextMenu ? "%V" : "%1";
            string commandValue = $"\"{exePath}\" {entry.CommandArgTemplate.Replace("{0}", placeholder)}";

            // Check if already registered with the same command (idempotent)
            using (RegistryKey? existingCommandKey = Registry.CurrentUser.OpenSubKey(commandKeyPath))
            {
                if (existingCommandKey != null)
                {
                    string? existingValue = existingCommandKey.GetValue(null) as string;
                    if (string.Equals(existingValue, commandValue, StringComparison.OrdinalIgnoreCase))
                    {
                        // Already registered with the same command — no-op
                        return Task.FromResult(new AssociationOperationResult { Success = true });
                    }
                }
            }

            // For double-click handler, also set up the .nkds -> NkdsFile mapping
            if (entry.Category == AssociationCategory.DoubleClickHandler)
            {
                RegisterDoubleClickHandler(exePath, entry, commandValue);
            }
            else if (entry.Category == AssociationCategory.FileContextMenu)
            {
                // File context menu entries live under NkdsFile ProgID — ensure .nkds maps to it
                EnsureNkdsExtensionKey();
                RegisterShellEntry(fullKeyPath, label, commandValue);
            }
            else
            {
                RegisterShellEntry(fullKeyPath, label, commandValue);
            }

            return Task.FromResult(new AssociationOperationResult { Success = true });
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = "Access denied. Run the application as administrator to register this association.",
                RequiresElevation = true
            });
        }
        catch (Exception ex)
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
            (string? keyPath, string _) = GetRegistryPaths(entry);

            if (entry.Category == AssociationCategory.DoubleClickHandler)
            {
                UnregisterDoubleClickHandler();
            }
            else
            {
                string fullKeyPath = ClassesRoot + @"\" + keyPath;
                DeleteRegistryKeyTree(fullKeyPath);
            }

            return Task.FromResult(new AssociationOperationResult { Success = true });
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = "Access denied. Run the application as administrator to unregister this association.",
                RequiresElevation = true
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AssociationOperationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    /// <summary>
    /// Maps an entry to its registry key path (relative to Software\Classes) and display label.
    /// File context menu entries are placed under the NkdsFile ProgID (not under .nkds\shell)
    /// because Windows resolves shell entries via the ProgID when one is set on the extension.
    /// </summary>
    private static (string KeyPath, string Label) GetRegistryPaths(AssociationEntry entry)
    {
        return entry.Id switch
        {
            "dir-open" => (@"Directory\shell\NkdsOpen", entry.Label),
            "dir-mount" => (@"Directory\shell\NkdsMount", entry.Label),
            "dir-adddir" => (@"Directory\shell\NkdsAddDir", entry.Label),
            "dir-adddir-new" => (@"Directory\shell\NkdsAddDirNew", entry.Label),
            "file-open-set" => (@"NkdsFile\shell\NkdsOpenSet", entry.Label),
            "file-mount-set" => (@"NkdsFile\shell\NkdsMountSet", entry.Label),
            "file-add" => (@"*\shell\NkdsAdd", entry.Label),
            "file-add-new" => (@"*\shell\NkdsAddNew", entry.Label),
            "dblclick-open" => (@"NkdsFile\shell\open", entry.Label),
            _ => throw new ArgumentException($"Unknown association entry ID: {entry.Id}", nameof(entry))
        };
    }

    /// <summary>
    /// Creates a shell entry key with a default label and a command subkey.
    /// </summary>
    private static void RegisterShellEntry(string fullKeyPath, string label, string commandValue, bool isDirectoryEntry = false)
    {
        using RegistryKey shellKey = Registry.CurrentUser.CreateSubKey(fullKeyPath);
        shellKey.SetValue(null, label);

        using RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(fullKeyPath + @"\command");
        commandKey.SetValue(null, commandValue);
    }

    /// <summary>
    /// Ensures the .nkds extension key exists with the NkdsFile progid mapping.
    /// </summary>
    private static void EnsureNkdsExtensionKey()
    {
        string extensionKeyPath = ClassesRoot + @"\.nkds";
        using RegistryKey extensionKey = Registry.CurrentUser.CreateSubKey(extensionKeyPath);
        // Set default value to NkdsFile if not already set
        string? currentDefault = extensionKey.GetValue(null) as string;
        if (string.IsNullOrEmpty(currentDefault))
        {
            extensionKey.SetValue(null, "NkdsFile");
        }
    }

    /// <summary>
    /// Registers the double-click handler: sets .nkds default to NkdsFile and creates NkdsFile\shell\open\command.
    /// </summary>
    private static void RegisterDoubleClickHandler(string exePath, AssociationEntry entry, string commandValue)
    {
        // Set .nkds extension default to "NkdsFile"
        string extensionKeyPath = ClassesRoot + @"\.nkds";
        using (RegistryKey extensionKey = Registry.CurrentUser.CreateSubKey(extensionKeyPath))
        {
            extensionKey.SetValue(null, "NkdsFile");
        }

        // Create NkdsFile\shell\open\command
        string progIdCommandPath = ClassesRoot + @"\NkdsFile\shell\open\command";
        using RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(progIdCommandPath);
        commandKey.SetValue(null, commandValue);
    }

    /// <summary>
    /// Unregisters the double-click handler by removing the NkdsFile\shell\open key tree.
    /// Does not remove the .nkds extension key itself (other shell entries may still reference it).
    /// </summary>
    private static void UnregisterDoubleClickHandler()
    {
        // Remove NkdsFile\shell\open\command and NkdsFile\shell\open
        string openKeyPath = ClassesRoot + @"\NkdsFile\shell\open";
        DeleteRegistryKeyTree(openKeyPath);

        // If NkdsFile\shell is now empty, clean it up
        string shellKeyPath = ClassesRoot + @"\NkdsFile\shell";
        using RegistryKey? shellKey = Registry.CurrentUser.OpenSubKey(shellKeyPath);
        if (shellKey != null && shellKey.SubKeyCount == 0)
        {
            DeleteRegistryKeyTree(ClassesRoot + @"\NkdsFile");
        }
    }

    /// <summary>
    /// Recursively deletes a registry key tree. Treats non-existent keys as success.
    /// </summary>
    private static void DeleteRegistryKeyTree(string keyPath) => Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}
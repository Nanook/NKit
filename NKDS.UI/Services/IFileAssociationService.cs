using NkdsUi.Models;

namespace NkdsUi.Services;

/// <summary>
/// Platform-specific service for registering and querying OS-level file/directory associations.
/// </summary>
public interface IFileAssociationService
{
    /// <summary>
    /// Whether the current platform supports file associations.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// The platform-specific label for the directory context menu group
    /// (e.g., "Explorer Context Menu" on Windows, "File Manager Context Menu" on Linux).
    /// </summary>
    string DirectoryContextMenuLabel { get; }

    /// <summary>
    /// Queries the current registration state of a specific association entry.
    /// </summary>
    Task<AssociationQueryResult> QueryStateAsync(AssociationEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Registers an association entry with the operating system.
    /// </summary>
    Task<AssociationOperationResult> RegisterAsync(AssociationEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Unregisters an association entry from the operating system.
    /// </summary>
    Task<AssociationOperationResult> UnregisterAsync(AssociationEntry entry, CancellationToken ct = default);
}
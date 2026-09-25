using NkdsUi.Models;

namespace NkdsUi.Services;

/// <summary>
/// File association service for unsupported platforms (e.g., macOS).
/// Reports that file associations are not supported and returns failure for all operations.
/// </summary>
public class NullFileAssociationService : IFileAssociationService
{
    private const string NotSupportedMessage = "Platform not supported";

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public string DirectoryContextMenuLabel => string.Empty;

    /// <inheritdoc />
    public Task<AssociationQueryResult> QueryStateAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        return Task.FromResult(new AssociationQueryResult
        {
            State = AssociationState.Unknown,
            ErrorMessage = NotSupportedMessage
        });
    }

    /// <inheritdoc />
    public Task<AssociationOperationResult> RegisterAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        return Task.FromResult(new AssociationOperationResult
        {
            Success = false,
            ErrorMessage = NotSupportedMessage
        });
    }

    /// <inheritdoc />
    public Task<AssociationOperationResult> UnregisterAsync(AssociationEntry entry, CancellationToken ct = default)
    {
        return Task.FromResult(new AssociationOperationResult
        {
            Success = false,
            ErrorMessage = NotSupportedMessage
        });
    }
}
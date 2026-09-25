using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Computes similarity groups from the Hash_Cache and exports them as YAML.
/// </summary>
public interface ISimilarityGroupService
{
    /// <summary>
    /// Computes similarity groups from the Hash_Cache.
    /// Builds an undirected graph where images are nodes and edges exist between images
    /// sharing at least one BlockKey. Extracts connected components as groups.
    /// Applies scope restrictions to partition images before graph construction.
    /// Excludes singleton images (no shared BlockKeys with any other image).
    /// </summary>
    /// <param name="cache">The in-memory Hash_Cache mapping images to their BlockKey sets.</param>
    /// <param name="scopeRestriction">Scope restriction to partition images before graph construction.</param>
    /// <param name="scopeReferenceImage">Optional reference image for scope restriction context.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <returns>Similarity groups sorted by image count descending, then by shared BlockKey count descending.</returns>
    Task<List<SimilarityGroupModel>> ComputeGroupsAsync(
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        ScopeRestriction scopeRestriction,
        ImageRecord? scopeReferenceImage,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null);

    /// <summary>
    /// Writes similarity groups to a YAML file.
    /// Groups are sorted by image count descending, then by shared BlockKey count descending.
    /// </summary>
    /// <param name="groups">The similarity groups to write.</param>
    /// <param name="filePath">The target file path for the YAML output.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task WriteYamlAsync(
        List<SimilarityGroupModel> groups,
        string filePath,
        CancellationToken cancellationToken);
}
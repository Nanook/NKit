using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Performs block-level comparison between images using only index metadata (BlockKey pairs from OffsetRecords).
/// </summary>
public interface IBlockComparisonService
{
    /// <summary>
    /// Compares a reference image against candidate images by computing the Match_Percentage
    /// (shared BlockKeys / reference BlockKeys * 100) for each candidate.
    /// Returns the top 10 results ordered by match percentage descending, excluding 0% matches.
    /// </summary>
    /// <param name="referenceImage">The image to compare against all candidates.</param>
    /// <param name="candidateImages">The set of images to compare with the reference.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <returns>Up to 10 comparison results ordered by match percentage descending.</returns>
    Task<List<ComparisonResultModel>> CompareImageAsync(
        ImageRecord referenceImage,
        IEnumerable<ImageRecord> candidateImages,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null);

    /// <summary>
    /// Finds files (OffsetRecord groups by offset_start) whose BlockKeys appear in all selected images.
    /// Computes the intersection of BlockKey sets across all images and groups results by OffsetStart.
    /// </summary>
    /// <param name="selectedImages">Two or more images to find common files between.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <returns>Common file groups sorted by total size descending.</returns>
    Task<List<CommonFileModel>> FindCommonFilesAsync(
        IReadOnlyList<ImageRecord> selectedImages,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null);
}
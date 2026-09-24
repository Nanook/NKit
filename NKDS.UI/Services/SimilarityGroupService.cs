using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Computes similarity groups from the Hash_Cache using connected components
/// via an inverted index, and exports them as YAML.
/// </summary>
public class SimilarityGroupService : ISimilarityGroupService
{
    /// <inheritdoc />
    public Task<List<SimilarityGroupModel>> ComputeGroupsAsync(
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        ScopeRestriction scopeRestriction,
        ImageRecord? scopeReferenceImage,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Step 1: Partition images by scope restriction
            Dictionary<string, List<ImageRecord>> partitions = PartitionByScope(cache, scopeRestriction);
            List<SimilarityGroupModel> allGroups = new List<SimilarityGroupModel>();
            int totalPartitions = partitions.Count;
            int completedPartitions = 0;

            // Step 2: For each partition, build inverted index and find connected components
            foreach (KeyValuePair<string, List<ImageRecord>> partition in partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<ImageRecord> partitionImages = partition.Value;
                if (partitionImages.Count < 2)
                {
                    completedPartitions++;
                    progress?.Report((double)completedPartitions / totalPartitions);
                    continue;
                }

                // Build inverted index: BlockKey -> List<ImageRecord>
                Dictionary<BlockKey, List<ImageRecord>> invertedIndex = BuildInvertedIndex(partitionImages, cache);

                // Build adjacency from inverted index
                Dictionary<ImageRecord, HashSet<ImageRecord>> adjacency = BuildAdjacency(invertedIndex, partitionImages);

                cancellationToken.ThrowIfCancellationRequested();

                // Find connected components via BFS
                List<List<ImageRecord>> components = FindConnectedComponents(adjacency, partitionImages);

                // Step 3: For each component with >1 image, build a SimilarityGroupModel
                foreach (List<ImageRecord> component in components)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (component.Count < 2)
                        continue;

                    SimilarityGroupModel group = BuildGroup(component, cache);
                    allGroups.Add(group);
                }

                completedPartitions++;
                progress?.Report((double)completedPartitions / totalPartitions);
            }

            // Step 4: Sort groups by image count desc, then SharedBlockKeyCount desc
            allGroups.Sort((a, b) =>
            {
                int countCompare = b.Images.Count.CompareTo(a.Images.Count);
                if (countCompare != 0) return countCompare;
                return b.SharedBlockKeyCount.CompareTo(a.SharedBlockKeyCount);
            });

            progress?.Report(1.0);
            return allGroups;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteYamlAsync(
        List<SimilarityGroupModel> groups,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Write to a temporary file first, then move to the target path
        // to avoid leaving partial files on error.
        string directory = Path.GetDirectoryName(filePath) ?? ".";
        string tempFilePath = Path.Combine(directory, Path.GetRandomFileName());

        try
        {
            using (StreamWriter writer = new StreamWriter(tempFilePath, append: false, encoding: System.Text.Encoding.UTF8))
            {
                await writer.WriteLineAsync("similarity_groups:").ConfigureAwait(false);

                foreach (SimilarityGroupModel group in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await writer.WriteLineAsync("  - images:").ConfigureAwait(false);

                    foreach (ImageRecord image in group.Images)
                    {
                        await writer.WriteLineAsync($"      - \"{EscapeYamlString(image.Name)}\"").ConfigureAwait(false);
                    }

                    await writer.WriteLineAsync($"    shared_block_count: {group.SharedBlockKeyCount}").ConfigureAwait(false);
                    await writer.WriteLineAsync("    pairwise_matches:").ConfigureAwait(false);

                    foreach (PairwiseMatch match in group.PairwiseMatches)
                    {
                        await writer.WriteLineAsync($"      - image_a: \"{EscapeYamlString(match.ImageA)}\"").ConfigureAwait(false);
                        await writer.WriteLineAsync($"        image_b: \"{EscapeYamlString(match.ImageB)}\"").ConfigureAwait(false);
                        await writer.WriteLineAsync($"        match_percentage: {match.MatchPercentage:F2}").ConfigureAwait(false);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Move the temp file to the target path (atomic on same volume).
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            // Clean up temp file on cancellation.
            TryDeleteFile(tempFilePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDeleteFile(tempFilePath);
            throw new IOException(
                $"Cannot write similarity groups to '{filePath}': access denied. {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            TryDeleteFile(tempFilePath);
            throw new IOException(
                $"Cannot write similarity groups to '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Escapes special characters in a YAML string value (backslash and double-quote).
    /// </summary>
    private static string EscapeYamlString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Attempts to delete a file, suppressing any exceptions.
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; ignore failures.
        }
    }

    /// <summary>
    /// Partitions images by scope restriction.
    /// </summary>
    private static Dictionary<string, List<ImageRecord>> PartitionByScope(
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        ScopeRestriction scopeRestriction)
    {
        Dictionary<string, List<ImageRecord>> partitions = new Dictionary<string, List<ImageRecord>>();

        if (scopeRestriction == ScopeRestriction.None)
        {
            // All images in one partition
            partitions["__all__"] = cache.Keys.ToList();
        }
        else
        {
            foreach (ImageRecord image in cache.Keys)
            {
                string key = GetPartitionKey(image, scopeRestriction);
                if (!partitions.TryGetValue(key, out List<ImageRecord>? list))
                {
                    list = new List<ImageRecord>();
                    partitions[key] = list;
                }
                list.Add(image);
            }
        }

        return partitions;
    }

    /// <summary>
    /// Gets the partition key for an image based on the scope restriction.
    /// </summary>
    private static string GetPartitionKey(ImageRecord image, ScopeRestriction scopeRestriction)
    {
        bool sameSet = (scopeRestriction & ScopeRestriction.SameSet) != 0;
        bool sameSystem = (scopeRestriction & ScopeRestriction.SameSystem) != 0;

        if (sameSet && sameSystem)
        {
            // Both: group by (SetName, System)
            return $"{image.SetName}\0{image.System ?? string.Empty}";
        }
        else if (sameSet)
        {
            return image.SetName;
        }
        else // sameSystem
        {
            return image.System ?? string.Empty;
        }
    }

    /// <summary>
    /// Builds an inverted index mapping each BlockKey to the list of images that contain it.
    /// Only includes entries where 2+ images share the BlockKey.
    /// </summary>
    private static Dictionary<BlockKey, List<ImageRecord>> BuildInvertedIndex(
        List<ImageRecord> partitionImages,
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache)
    {
        Dictionary<BlockKey, List<ImageRecord>> invertedIndex = new Dictionary<BlockKey, List<ImageRecord>>();

        foreach (ImageRecord image in partitionImages)
        {
            if (!cache.TryGetValue(image, out HashSet<BlockKey>? blockKeys))
                continue;

            foreach (BlockKey blockKey in blockKeys)
            {
                if (!invertedIndex.TryGetValue(blockKey, out List<ImageRecord>? list))
                {
                    list = new List<ImageRecord>();
                    invertedIndex[blockKey] = list;
                }
                list.Add(image);
            }
        }

        return invertedIndex;
    }

    /// <summary>
    /// Builds adjacency from the inverted index. Two images are adjacent if they share at least one BlockKey.
    /// </summary>
    private static Dictionary<ImageRecord, HashSet<ImageRecord>> BuildAdjacency(
        Dictionary<BlockKey, List<ImageRecord>> invertedIndex,
        List<ImageRecord> partitionImages)
    {
        Dictionary<ImageRecord, HashSet<ImageRecord>> adjacency = new Dictionary<ImageRecord, HashSet<ImageRecord>>();

        // Initialize adjacency for all images in the partition
        foreach (ImageRecord image in partitionImages)
        {
            adjacency[image] = new HashSet<ImageRecord>();
        }

        // For each BlockKey shared by 2+ images, add edges between all pairs
        foreach (KeyValuePair<BlockKey, List<ImageRecord>> kvp in invertedIndex)
        {
            List<ImageRecord> images = kvp.Value;
            if (images.Count < 2)
                continue;

            for (int i = 0; i < images.Count; i++)
            {
                for (int j = i + 1; j < images.Count; j++)
                {
                    adjacency[images[i]].Add(images[j]);
                    adjacency[images[j]].Add(images[i]);
                }
            }
        }

        return adjacency;
    }

    /// <summary>
    /// Finds connected components via BFS. Returns only components with more than one image.
    /// </summary>
    private static List<List<ImageRecord>> FindConnectedComponents(
        Dictionary<ImageRecord, HashSet<ImageRecord>> adjacency,
        List<ImageRecord> partitionImages)
    {
        HashSet<ImageRecord> visited = new HashSet<ImageRecord>();
        List<List<ImageRecord>> components = new List<List<ImageRecord>>();

        foreach (ImageRecord image in partitionImages)
        {
            if (visited.Contains(image))
                continue;

            // BFS from this image
            List<ImageRecord> component = new List<ImageRecord>();
            Queue<ImageRecord> queue = new Queue<ImageRecord>();
            queue.Enqueue(image);
            visited.Add(image);

            while (queue.Count > 0)
            {
                ImageRecord current = queue.Dequeue();
                component.Add(current);

                if (!adjacency.TryGetValue(current, out HashSet<ImageRecord>? neighbors))
                    continue;

                foreach (ImageRecord neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Only include non-singleton components
            if (component.Count > 1)
            {
                components.Add(component);
            }
        }

        return components;
    }

    /// <summary>
    /// Builds a SimilarityGroupModel from a connected component.
    /// Sorts images alphabetically, computes SharedBlockKeyCount, and computes pairwise matches.
    /// </summary>
    private static SimilarityGroupModel BuildGroup(
        List<ImageRecord> component,
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache)
    {
        // Sort images alphabetically by Name
        List<ImageRecord> sortedImages = component
            .OrderBy(img => img.Name, StringComparer.Ordinal)
            .ToList();

        // Compute SharedBlockKeyCount: BlockKeys appearing in ≥2 images in the group
        Dictionary<BlockKey, int> blockKeyOccurrences = new Dictionary<BlockKey, int>();
        foreach (ImageRecord? image in sortedImages)
        {
            if (!cache.TryGetValue(image, out HashSet<BlockKey>? blockKeys))
                continue;

            foreach (BlockKey blockKey in blockKeys)
            {
                blockKeyOccurrences.TryGetValue(blockKey, out int count);
                blockKeyOccurrences[blockKey] = count + 1;
            }
        }

        int sharedBlockKeyCount = blockKeyOccurrences.Count(kvp => kvp.Value >= 2);

        // Compute pairwise match percentages for all pairs
        List<PairwiseMatch> pairwiseMatches = new List<PairwiseMatch>();
        for (int i = 0; i < sortedImages.Count; i++)
        {
            for (int j = i + 1; j < sortedImages.Count; j++)
            {
                ImageRecord imageA = sortedImages[i];
                ImageRecord imageB = sortedImages[j];

                // Images are already sorted alphabetically, so imageA.Name < imageB.Name
                if (!cache.TryGetValue(imageA, out HashSet<BlockKey>? blockKeysA) ||
                    !cache.TryGetValue(imageB, out HashSet<BlockKey>? blockKeysB))
                    continue;

                // Compute intersection count
                int intersectionCount = 0;
                foreach (BlockKey key in blockKeysB)
                {
                    if (blockKeysA.Contains(key))
                        intersectionCount++;
                }

                // Match percentage from A's perspective: |A ∩ B| / |A| * 100
                double matchPercentage = blockKeysA.Count > 0
                    ? (double)intersectionCount / blockKeysA.Count * 100.0
                    : 0.0;

                pairwiseMatches.Add(new PairwiseMatch
                {
                    ImageA = imageA.Name,
                    ImageB = imageB.Name,
                    MatchPercentage = matchPercentage
                });
            }
        }

        return new SimilarityGroupModel
        {
            Images = sortedImages,
            SharedBlockKeyCount = sharedBlockKeyCount,
            PairwiseMatches = pairwiseMatches
        };
    }
}
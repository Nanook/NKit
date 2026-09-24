using NkdsUi.Models;
using NKitDataStore;
using System.Collections.Concurrent;

namespace NkdsUi.Services;

/// <summary>
/// Computes threshold-based similarity groups from the Hash_Cache.
/// Uses symmetric match percentage: |A ∩ B| / max(|A|, |B|) * 100.
/// </summary>
public class ThresholdGroupingService : IThresholdGroupingService
{
    /// <inheritdoc />
    public Task<ThresholdGroupingResult> ComputeGroupsAsync(
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        double threshold,
        ScopeRestriction scopeRestriction,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null,
        int maxDegreeOfParallelism = 1)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Partition images by scope restriction before pairwise computation
            Dictionary<string, List<ImageRecord>> partitions = PartitionByScope(cache, scopeRestriction);

            List<ThresholdGroupModel> allGroups = new List<ThresholdGroupModel>();
            int totalPairsComputed = 0;
            List<(string ImageA, string ImageB, double MatchPercent)> allPairResults = new List<(string ImageA, string ImageB, double MatchPercent)>();
            List<string> allImageNames = new List<string>();

            foreach (List<ImageRecord> partition in partitions.Values)
            {
                if (partition.Count < 2)
                    continue;

                ImageRecord[] images = partition.ToArray();

                // Collect image names from this partition
                foreach (ImageRecord img in images)
                    allImageNames.Add(img.Name);

                // Build a sub-cache for this partition
                Dictionary<ImageRecord, HashSet<BlockKey>> partitionCache = new Dictionary<ImageRecord, HashSet<BlockKey>>();
                foreach (ImageRecord img in images)
                    partitionCache[img] = cache[img];

                // All-pairs pairwise computation: branch on maxDegreeOfParallelism
                List<(ImageRecord A, ImageRecord B, double MatchPercent)> pairResults;
                if (maxDegreeOfParallelism == 1)
                {
                    pairResults = ComputeAllPairs(images, partitionCache, cancellationToken, progress);
                }
                else
                {
                    pairResults = ComputeAllPairsParallel(images, partitionCache, cancellationToken, progress, maxDegreeOfParallelism);
                }

                totalPairsComputed += pairResults.Count;

                // Convert pair results to string-based tuples for AllPairResults
                foreach ((ImageRecord? a, ImageRecord? b, double matchPercent) in pairResults)
                    allPairResults.Add((a.Name, b.Name, matchPercent));

                // Build threshold-filtered graph and extract connected components
                List<ThresholdGroupModel> groups = BuildThresholdGroups(pairResults, threshold, images);
                allGroups.AddRange(groups);
            }

            // Final sort across all partitions: by image count desc, then avg match desc
            allGroups.Sort((a, b) =>
            {
                int countCompare = b.Images.Count.CompareTo(a.Images.Count);
                if (countCompare != 0) return countCompare;
                return b.Summary.AvgMatchPercentage.CompareTo(a.Summary.AvgMatchPercentage);
            });

            return new ThresholdGroupingResult
            {
                Groups = allGroups,
                Threshold = threshold,
                TotalGroupedImages = allGroups.Sum(g => g.Images.Count),
                TotalPairsComputed = totalPairsComputed,
                AllPairResults = allPairResults,
                ScopeRestriction = scopeRestriction,
                ImageNames = allImageNames
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Computes the symmetric match percentage between two sets of BlockKeys.
    /// Formula: |A ∩ B| / max(|A|, |B|) * 100.0
    /// Returns 0.0 when both sets are empty.
    /// </summary>
    internal static double ComputeSymmetricMatchPercentage(HashSet<BlockKey> a, HashSet<BlockKey> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0.0;

        int denominator = Math.Max(a.Count, b.Count);

        // Iterate the smaller set against the larger for O(min(|A|,|B|)) intersection
        HashSet<BlockKey> smallerSet = a.Count <= b.Count ? a : b;
        HashSet<BlockKey> largerSet = a.Count <= b.Count ? b : a;

        int intersectionCount = 0;
        foreach (BlockKey key in smallerSet)
        {
            if (largerSet.Contains(key))
                intersectionCount++;
        }

        return (double)intersectionCount / denominator * 100.0;
    }

    /// <summary>
    /// Computes match percentages for all unique pairs of images.
    /// Skips pairs where both images have zero BlockKeys.
    /// Reports progress every 1000 pairs (throttled).
    /// </summary>
    internal static List<(ImageRecord A, ImageRecord B, double MatchPercent)> ComputeAllPairs(
        ImageRecord[] images,
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        long totalPairs = (long)images.Length * (images.Length - 1) / 2;
        List<(ImageRecord A, ImageRecord B, double MatchPercent)> pairResults = new List<(ImageRecord A, ImageRecord B, double MatchPercent)>();
        long pairsComputed = 0;

        for (int i = 0; i < images.Length - 1; i++)
        {
            HashSet<BlockKey> blockKeysI = cache[images[i]];

            for (int j = i + 1; j < images.Length; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                HashSet<BlockKey> blockKeysJ = cache[images[j]];

                // Skip pairs where both images have zero BlockKeys
                if (blockKeysI.Count == 0 && blockKeysJ.Count == 0)
                {
                    pairsComputed++;
                    if (pairsComputed % 1000 == 0 && totalPairs > 0)
                        progress?.Report((double)pairsComputed / totalPairs);
                    continue;
                }

                double matchPercent = ComputeSymmetricMatchPercentage(blockKeysI, blockKeysJ);
                pairResults.Add((images[i], images[j], matchPercent));

                pairsComputed++;
                if (pairsComputed % 1000 == 0 && totalPairs > 0)
                    progress?.Report((double)pairsComputed / totalPairs);
            }
        }

        // Final progress report
        if (totalPairs > 0)
            progress?.Report(1.0);

        return pairResults;
    }

    /// <summary>
    /// Computes match percentages for all unique pairs of images using parallel execution.
    /// Uses Partitioner.Create for outer loop partitioning with thread-local list accumulation.
    /// Reports progress every 5000 pairs using Interlocked.Increment.
    /// Merges thread-local lists with a lock after completion.
    /// </summary>
    internal static List<(ImageRecord A, ImageRecord B, double MatchPercent)> ComputeAllPairsParallel(
        ImageRecord[] images,
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        CancellationToken cancellationToken,
        IProgress<double>? progress,
        int maxDegreeOfParallelism)
    {
        long totalPairs = (long)images.Length * (images.Length - 1) / 2;
        List<(ImageRecord A, ImageRecord B, double MatchPercent)> mergedResults = new List<(ImageRecord A, ImageRecord B, double MatchPercent)>();
        int sharedPairsComputed = 0;

        OrderablePartitioner<Tuple<int, int>> partitioner = Partitioner.Create(0, images.Length - 1);

        Parallel.ForEach(
            partitioner,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            // Thread-local init
            () => new List<(ImageRecord A, ImageRecord B, double MatchPercent)>(),
            // Body
            (range, loopState, localList) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    HashSet<BlockKey> blockKeysI = cache[images[i]];

                    for (int j = i + 1; j < images.Length; j++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        HashSet<BlockKey> blockKeysJ = cache[images[j]];

                        // Skip pairs where both images have zero BlockKeys
                        if (blockKeysI.Count == 0 && blockKeysJ.Count == 0)
                        {
                            int count = Interlocked.Increment(ref sharedPairsComputed);
                            if (count % 5000 == 0 && totalPairs > 0)
                                progress?.Report((double)count / totalPairs);
                            continue;
                        }

                        double matchPercent = ComputeSymmetricMatchPercentage(blockKeysI, blockKeysJ);
                        localList.Add((images[i], images[j], matchPercent));

                        int pairsCount = Interlocked.Increment(ref sharedPairsComputed);
                        if (pairsCount % 5000 == 0 && totalPairs > 0)
                            progress?.Report((double)pairsCount / totalPairs);
                    }
                }

                return localList;
            },
            // LocalFinally: merge thread-local list into shared results
            (localList) =>
            {
                lock (mergedResults)
                {
                    mergedResults.AddRange(localList);
                }
            });

        // Final progress report
        if (totalPairs > 0)
            progress?.Report(1.0);

        return mergedResults;
    }

    /// <summary>
    /// Builds threshold-filtered groups from string-based pairwise results.
    /// Creates lightweight ImageRecord instances from names for graph construction.
    /// Used by the dialog for re-filtering at different thresholds without recomputing.
    /// </summary>
    internal static List<ThresholdGroupModel> BuildThresholdGroups(
        List<(string ImageA, string ImageB, double MatchPercent)> pairResults,
        double threshold)
    {
        // Build a name→ImageRecord lookup from all unique names in pair results
        Dictionary<string, ImageRecord> nameLookup = new Dictionary<string, ImageRecord>();
        foreach ((string? imageA, string? imageB, double _) in pairResults)
        {
            if (!nameLookup.ContainsKey(imageA))
                nameLookup[imageA] = new ImageRecord { Name = imageA };
            if (!nameLookup.ContainsKey(imageB))
                nameLookup[imageB] = new ImageRecord { Name = imageB };
        }

        // Convert to ImageRecord-based tuples
        List<(ImageRecord, ImageRecord, double MatchPercent)> imageRecordPairs = pairResults
            .Select(p => (nameLookup[p.ImageA], nameLookup[p.ImageB], p.MatchPercent))
            .ToList();

        ImageRecord[] images = nameLookup.Values.ToArray();
        return BuildThresholdGroups(imageRecordPairs, threshold, images);
    }

    /// <summary>
    /// Builds threshold-filtered groups from pairwise results.
    /// Constructs an adjacency graph from pairs meeting the threshold,
    /// extracts connected components via BFS, excludes singletons,
    /// computes summary statistics, and sorts groups.
    /// </summary>
    internal static List<ThresholdGroupModel> BuildThresholdGroups(
        List<(ImageRecord A, ImageRecord B, double MatchPercent)> pairResults,
        double threshold,
        ImageRecord[] images)
    {
        // Step 1: Build adjacency from threshold-filtered pairs
        Dictionary<ImageRecord, HashSet<ImageRecord>> adjacency = new Dictionary<ImageRecord, HashSet<ImageRecord>>();
        foreach ((ImageRecord? a, ImageRecord? b, double matchPercent) in pairResults)
        {
            if (matchPercent >= threshold)
            {
                if (!adjacency.TryGetValue(a, out HashSet<ImageRecord>? neighborsA))
                {
                    neighborsA = new HashSet<ImageRecord>();
                    adjacency[a] = neighborsA;
                }
                neighborsA.Add(b);

                if (!adjacency.TryGetValue(b, out HashSet<ImageRecord>? neighborsB))
                {
                    neighborsB = new HashSet<ImageRecord>();
                    adjacency[b] = neighborsB;
                }
                neighborsB.Add(a);
            }
        }

        // Step 2: Find connected components via BFS (exclude singletons)
        List<List<ImageRecord>> components = FindConnectedComponents(adjacency, images);

        // Step 3: Build ThresholdGroupModel for each component
        List<ThresholdGroupModel> groups = new List<ThresholdGroupModel>();
        foreach (List<ImageRecord> component in components)
        {
            // Sort images alphabetically by Name
            List<ImageRecord> sortedImages = component
                .OrderBy(img => img.Name, StringComparer.Ordinal)
                .ToList();

            // Collect all pairwise matches within the component
            HashSet<ImageRecord> componentSet = new HashSet<ImageRecord>(component);
            List<PairwiseMatch> pairwiseMatches = new List<PairwiseMatch>();
            foreach ((ImageRecord? a, ImageRecord? b, double matchPercent) in pairResults)
            {
                if (componentSet.Contains(a) && componentSet.Contains(b))
                {
                    // Order pair so ImageA.Name < ImageB.Name alphabetically
                    string imageA, imageB;
                    if (StringComparer.Ordinal.Compare(a.Name, b.Name) <= 0)
                    {
                        imageA = a.Name;
                        imageB = b.Name;
                    }
                    else
                    {
                        imageA = b.Name;
                        imageB = a.Name;
                    }

                    pairwiseMatches.Add(new PairwiseMatch
                    {
                        ImageA = imageA,
                        ImageB = imageB,
                        MatchPercentage = matchPercent
                    });
                }
            }

            // Compute summary statistics
            ThresholdGroupSummary summary = ComputeGroupSummary(pairwiseMatches);

            groups.Add(new ThresholdGroupModel
            {
                Images = sortedImages,
                PairwiseMatches = pairwiseMatches,
                Summary = summary
            });
        }

        // Step 4: Sort groups by image count desc, then avg match percentage desc
        groups.Sort((a, b) =>
        {
            int countCompare = b.Images.Count.CompareTo(a.Images.Count);
            if (countCompare != 0) return countCompare;
            return b.Summary.AvgMatchPercentage.CompareTo(a.Summary.AvgMatchPercentage);
        });

        return groups;
    }

    /// <summary>
    /// Finds connected components via BFS. Returns only components with more than one image.
    /// Only visits images that have entries in the adjacency dictionary (i.e., have at least one edge).
    /// </summary>
    internal static List<List<ImageRecord>> FindConnectedComponents(
        Dictionary<ImageRecord, HashSet<ImageRecord>> adjacency,
        ImageRecord[] images)
    {
        HashSet<ImageRecord> visited = new HashSet<ImageRecord>();
        List<List<ImageRecord>> components = new List<List<ImageRecord>>();

        foreach (ImageRecord image in images)
        {
            if (visited.Contains(image))
                continue;

            // Only start BFS from images that have edges in the adjacency
            if (!adjacency.ContainsKey(image))
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
    /// Computes summary statistics (min, max, avg) for a group's pairwise matches.
    /// Returns zeroes if the list is empty.
    /// </summary>
    internal static ThresholdGroupSummary ComputeGroupSummary(List<PairwiseMatch> pairwiseMatches)
    {
        if (pairwiseMatches.Count == 0)
        {
            return new ThresholdGroupSummary
            {
                MinMatchPercentage = 0.0,
                MaxMatchPercentage = 0.0,
                AvgMatchPercentage = 0.0
            };
        }

        double min = pairwiseMatches[0].MatchPercentage;
        double max = pairwiseMatches[0].MatchPercentage;
        double sum = 0.0;

        foreach (PairwiseMatch match in pairwiseMatches)
        {
            if (match.MatchPercentage < min) min = match.MatchPercentage;
            if (match.MatchPercentage > max) max = match.MatchPercentage;
            sum += match.MatchPercentage;
        }

        return new ThresholdGroupSummary
        {
            MinMatchPercentage = min,
            MaxMatchPercentage = max,
            AvgMatchPercentage = sum / pairwiseMatches.Count
        };
    }

    /// <summary>
    /// Partitions images by scope restriction.
    /// Reuses the same partitioning logic as SimilarityGroupService.
    /// </summary>
    internal static Dictionary<string, List<ImageRecord>> PartitionByScope(
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
    internal static string GetPartitionKey(ImageRecord image, ScopeRestriction scopeRestriction)
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
}
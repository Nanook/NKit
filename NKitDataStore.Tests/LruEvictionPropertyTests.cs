using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for LRU eviction correctness in the filesystem.yaml cache.
    ///
    /// Feature: lazy-filesystem-yaml-loading, Property 4: LRU eviction correctness
    ///
    /// The VfsModel uses a LinkedList + Dictionary LRU cache to bound the number of
    /// loaded filesystem.yaml entries. This test replicates the exact algorithm from
    /// VfsModel.TrackFsYamlLoaded() and TouchFsYamlCache() and verifies the LRU
    /// invariants hold across arbitrary access sequences.
    /// </summary>
    public class LruEvictionPropertyTests
    {
        private const int TestCacheCapacity = 8;
        private const int IdPoolSize = 50;

        /// <summary>
        /// Mirrors the LRU cache state from VfsModel: LinkedList for ordering (MRU at tail,
        /// LRU at head) and Dictionary for O(1) node lookup. Tracks evicted items and their
        /// loaded state, matching the VfsModel behavior exactly.
        /// </summary>
        private class LruCache
        {
            public readonly int Capacity;
            public readonly LinkedList<long> CacheOrder = new LinkedList<long>();
            public readonly Dictionary<long, LinkedListNode<long>> CacheNodes = new Dictionary<long, LinkedListNode<long>>();

            /// <summary>
            /// Tracks the loaded state of each image ID. True means FileSystemYaml is loaded;
            /// false means it was evicted (FileSystemYaml = null, FileSystemYamlLoaded = false).
            /// </summary>
            public readonly Dictionary<long, bool> LoadedState = new Dictionary<long, bool>();

            /// <summary>
            /// Records each eviction event: (evictedId, lruOrderAtEviction).
            /// The lruOrderAtEviction is the cache order just before the eviction.
            /// </summary>
            public readonly List<(long EvictedId, List<long> CacheSnapshot)> EvictionLog = new List<(long, List<long>)>();

            public LruCache(int capacity)
            {
                Capacity = capacity;
            }

            /// <summary>
            /// Mirrors VfsModel.TrackFsYamlLoaded(). Registers an image in the LRU cache.
            /// If already tracked, promotes to MRU. If at capacity, evicts LRU entry.
            /// </summary>
            public void TrackFsYamlLoaded(long imageId)
            {
                // Move to MRU position if already tracked
                if (CacheNodes.TryGetValue(imageId, out LinkedListNode<long> existingNode))
                {
                    CacheOrder.Remove(existingNode);
                    CacheOrder.AddLast(existingNode);
                    return;
                }

                // Evict LRU if at capacity
                while (CacheOrder.Count >= Capacity)
                {
                    long evictId = CacheOrder.First!.Value;

                    // Record the eviction with a snapshot of the cache before removal
                    EvictionLog.Add((evictId, CacheOrder.Select(id => id).ToList()));

                    CacheOrder.RemoveFirst();
                    CacheNodes.Remove(evictId);

                    // Reset the evicted item (mirrors setting FileSystemYaml = null, FileSystemYamlLoaded = false)
                    LoadedState[evictId] = false;
                }

                // Add new entry at MRU position
                LinkedListNode<long> node = CacheOrder.AddLast(imageId);
                CacheNodes[imageId] = node;
                LoadedState[imageId] = true;
            }

            /// <summary>
            /// Mirrors VfsModel.TouchFsYamlCache(). Promotes an existing entry to MRU position.
            /// </summary>
            public void TouchFsYamlCache(long imageId)
            {
                if (CacheNodes.TryGetValue(imageId, out LinkedListNode<long> node))
                {
                    CacheOrder.Remove(node);
                    CacheOrder.AddLast(node);
                }
            }
        }

        /// <summary>
        /// Generates a deterministic access sequence from two seed values.
        /// Produces a sequence of length 50–200 with IDs from a pool of 50.
        /// </summary>
        private static int[] GenerateAccessSequence(int lengthSeed, int contentSeed)
        {
            int length = 50 + (Math.Abs(lengthSeed) % 151); // 50–200
            Random rng = new Random(contentSeed);
            int[] sequence = new int[length];
            for (int i = 0; i < length; i++)
            {
                sequence[i] = rng.Next(1, IdPoolSize + 1); // IDs 1–50
            }
            return sequence;
        }

        /// <summary>
        /// **Validates: Requirements 5.2, 5.4**
        ///
        /// Property 4: LRU eviction correctness.
        /// For any random access sequence of image IDs (length 50–200, IDs from a pool of 50),
        /// with cache capacity set to 8:
        /// 1. The cache never exceeds capacity after any access.
        /// 2. Evicted items are always the LRU (least recently used) ones.
        /// 3. After eviction, the evicted item's loaded state is false.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool LruEviction_NeverExceedsCapacity_And_EvictsLruItems(
            NonNegativeInt lengthSeed,
            NonNegativeInt contentSeed)
        {
            int[] accessSequence = GenerateAccessSequence(lengthSeed.Get, contentSeed.Get);
            LruCache cache = new LruCache(TestCacheCapacity);

            // Reference model: tracks the true LRU order as a simple list.
            // Most recently used at the end, least recently used at the front.
            List<long> referenceOrder = new List<long>();

            foreach (int rawId in accessSequence)
            {
                long imageId = rawId;

                // Simulate the access: TrackFsYamlLoaded (handles both new and existing entries)
                cache.TrackFsYamlLoaded(imageId);

                // Update reference model
                referenceOrder.Remove(imageId);
                // If reference exceeds capacity, the LRU item (index 0) should have been evicted
                while (referenceOrder.Count >= TestCacheCapacity)
                {
                    referenceOrder.RemoveAt(0); // Remove LRU
                }
                referenceOrder.Add(imageId); // Add as MRU

                // INVARIANT 1: Cache never exceeds capacity
                if (cache.CacheOrder.Count > TestCacheCapacity)
                    return false;

                // INVARIANT 2: Cache contents and order match the reference model
                List<long> cacheContents = cache.CacheOrder.ToList();
                if (!cacheContents.SequenceEqual(referenceOrder))
                    return false;
            }

            // INVARIANT 3: Every eviction targeted the LRU item (head of the list)
            foreach ((long evictedId, List<long> cacheSnapshot) in cache.EvictionLog)
            {
                // The evicted ID must be the first element (LRU) in the snapshot
                if (cacheSnapshot.Count == 0 || cacheSnapshot[0] != evictedId)
                    return false;
            }

            // INVARIANT 4: Evicted items that are not re-cached have loaded state = false
            foreach ((long evictedId, List<long> _) in cache.EvictionLog)
            {
                // If the item is not currently in the cache, its loaded state must be false
                if (!cache.CacheNodes.ContainsKey(evictedId))
                {
                    if (cache.LoadedState.TryGetValue(evictedId, out bool loaded) && loaded)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.2, 5.4**
        ///
        /// Property 4 (supplementary): TouchFsYamlCache promotes entries correctly.
        /// Accessing (touching) an item in the cache should prevent it from being evicted
        /// when new items are added, as long as there are older untouched items.
        /// With mixed Track and Touch operations, the cache order must always match
        /// the expected LRU ordering and never exceed capacity.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool TouchFsYamlCache_PromotesToMru_PreventsEviction(
            NonNegativeInt lengthSeed,
            NonNegativeInt contentSeed,
            NonNegativeInt touchSeed)
        {
            int[] accessSequence = GenerateAccessSequence(lengthSeed.Get, contentSeed.Get);
            Random touchRng = new Random(touchSeed.Get);
            LruCache cache = new LruCache(TestCacheCapacity);
            List<long> referenceOrder = new List<long>();

            foreach (int rawId in accessSequence)
            {
                long imageId = rawId;

                // Decide whether to Touch (promote existing only) or Track (add/promote)
                // Touch only if the item is already in the cache and random says so
                bool shouldTouch = referenceOrder.Contains(imageId) && touchRng.Next(4) == 0;

                if (shouldTouch)
                {
                    cache.TouchFsYamlCache(imageId);

                    // Update reference: promote to MRU
                    referenceOrder.Remove(imageId);
                    referenceOrder.Add(imageId);
                }
                else
                {
                    cache.TrackFsYamlLoaded(imageId);

                    // Update reference model
                    referenceOrder.Remove(imageId);
                    while (referenceOrder.Count >= TestCacheCapacity)
                    {
                        referenceOrder.RemoveAt(0);
                    }
                    referenceOrder.Add(imageId);
                }

                // INVARIANT: Cache never exceeds capacity
                if (cache.CacheOrder.Count > TestCacheCapacity)
                    return false;

                // INVARIANT: Cache order matches reference
                List<long> cacheContents = cache.CacheOrder.ToList();
                if (!cacheContents.SequenceEqual(referenceOrder))
                    return false;
            }

            return true;
        }
    }
}
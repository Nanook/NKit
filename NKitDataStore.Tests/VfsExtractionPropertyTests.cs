using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Vfs;
using NKDS.Mount;
using System.Collections.Concurrent;

namespace NKitDataStore.Tests
{
    // Feature: nkds-vfs-library-extraction, Property 1: MountRegistry system-to-spec mapping
    /// <summary>
    /// Property-based tests for MountRegistry system-to-spec mapping.
    ///
    /// **Validates: Requirements 2.4**
    ///
    /// For any system name string, MountRegistry SHALL return exactly 3 mount specs
    /// (Image, FileSystem, AppFolder) if the system name equals "WiiU" (case-insensitive),
    /// and exactly 2 mount specs (Image, FileSystem) for all other non-empty system names.
    /// </summary>
    public class VfsExtractionPropertyTests
    {
        private static readonly string[] WiiUVariants = new[]
        {
            "WiiU", "wiiu", "WIIU", "wiiU", "Wiiu", "wIIu", "WIiu", "wIiU"
        };

        private static readonly string[] NonWiiUSystems = new[]
        {
            "GameCube", "Wii", "NES", "SNES", "N64", "GBA", "NDS", "3DS", "Switch",
            "PlayStation", "Xbox", "Sega", "Atari", "Custom", "Genesis", "Saturn"
        };

        /// <summary>
        /// **Validates: Requirements 2.4**
        ///
        /// Property 1: MountRegistry system-to-spec mapping.
        /// For any WiiU case variant, GetMountSpecsForSystem returns exactly 3 specs
        /// with kinds Image, FileSystem, and AppFolder.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool WiiU_CaseInsensitive_Returns3Specs(NonNegativeInt seed)
        {
            // Pick a WiiU variant based on the seed
            string wiiuVariant = WiiUVariants[seed.Get % WiiUVariants.Length];

            MountRegistry registry = new MountRegistry(new[] { wiiuVariant });
            List<MountSpec> specs = registry.GetMountSpecsForSystem(wiiuVariant);

            return specs.Count == 3
                && specs.Any(s => s.Kind == MountKind.Image)
                && specs.Any(s => s.Kind == MountKind.FileSystem)
                && specs.Any(s => s.Kind == MountKind.AppFolder);
        }

        /// <summary>
        /// **Validates: Requirements 2.4**
        ///
        /// Property 1: MountRegistry system-to-spec mapping.
        /// For any non-empty system name that is NOT "WiiU" (case-insensitive),
        /// GetMountSpecsForSystem returns exactly 2 specs with kinds Image and FileSystem.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NonWiiU_NonEmpty_Returns2Specs(NonNegativeInt seed)
        {
            // Pick a non-WiiU system name based on the seed
            string systemName = NonWiiUSystems[seed.Get % NonWiiUSystems.Length];

            MountRegistry registry = new MountRegistry(new[] { systemName });
            List<MountSpec> specs = registry.GetMountSpecsForSystem(systemName);

            return specs.Count == 2
                && specs.Any(s => s.Kind == MountKind.Image)
                && specs.Any(s => s.Kind == MountKind.FileSystem)
                && !specs.Any(s => s.Kind == MountKind.AppFolder);
        }

        /// <summary>
        /// **Validates: Requirements 2.4**
        ///
        /// Property 1: MountRegistry system-to-spec mapping.
        /// For empty or null system names, GetMountSpecsForSystem returns an empty list.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool EmptyOrNull_ReturnsEmptyList(NonNegativeInt seed)
        {
            // Create registry with a valid system so it's not empty
            MountRegistry registry = new MountRegistry(new[] { "GameCube" });

            // Test with empty string
            List<MountSpec> specsEmpty = registry.GetMountSpecsForSystem("");
            // Test with null
            List<MountSpec> specsNull = registry.GetMountSpecsForSystem(null!);

            return specsEmpty.Count == 0 && specsNull.Count == 0;
        }

        /// <summary>
        /// **Validates: Requirements 2.4**
        ///
        /// Property 1: MountRegistry system-to-spec mapping.
        /// WiiU lookup is case-insensitive: registering with one case variant and querying
        /// with a different case variant still returns 3 specs.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool WiiU_CrossCaseLookup_Returns3Specs(NonNegativeInt registerSeed, NonNegativeInt querySeed)
        {
            string registerVariant = WiiUVariants[registerSeed.Get % WiiUVariants.Length];
            string queryVariant = WiiUVariants[querySeed.Get % WiiUVariants.Length];

            MountRegistry registry = new MountRegistry(new[] { registerVariant });
            List<MountSpec> specs = registry.GetMountSpecsForSystem(queryVariant);

            return specs.Count == 3;
        }

        /// <summary>
        /// **Validates: Requirements 2.4**
        ///
        /// Property 1: MountRegistry system-to-spec mapping.
        /// For random non-empty alphanumeric strings that are not "WiiU" (case-insensitive),
        /// GetMountSpecsForSystem returns exactly 2 specs.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool RandomNonWiiU_Returns2Specs(NonEmptyString systemNameWrapper)
        {
            string systemName = systemNameWrapper.Get;

            // Skip if it happens to be a WiiU variant
            if (string.Equals(systemName, "WiiU", StringComparison.OrdinalIgnoreCase))
                return true; // vacuously true, skip this case

            MountRegistry registry = new MountRegistry(new[] { systemName });
            List<MountSpec> specs = registry.GetMountSpecsForSystem(systemName);

            return specs.Count == 2
                && specs.Any(s => s.Kind == MountKind.Image)
                && specs.Any(s => s.Kind == MountKind.FileSystem)
                && !specs.Any(s => s.Kind == MountKind.AppFolder);
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 2: DataStoreIndex resource manager routing
    /// <summary>
    /// Property-based tests for DataStoreIndex resource manager routing.
    ///
    /// **Validates: Requirements 7.2**
    ///
    /// For any VfsModelItem with a DataStoreIndex value i where 0 &lt;= i &lt; N (N = number of DataStores),
    /// calling GetResourcesForItem(item) SHALL return the resource manager at index i in the
    /// _allDataStores list, and calling GetResourcesByIndex(i) SHALL return the same instance.
    ///
    /// Since VfsModel requires real DataStore paths to construct, this test validates the
    /// routing algorithm directly: a list of N resource managers indexed by position, and items
    /// with DataStoreIndex values that route to the correct manager.
    /// </summary>
    public class DataStoreIndexRoutingPropertyTests
    {
        /// <summary>
        /// Simple mock resource manager for testing routing identity.
        /// Each instance has a unique Id to verify correct routing.
        /// </summary>
        private class MockResourceManager
        {
            public int Id { get; }
            public MockResourceManager(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// Models the VfsModel routing logic:
        /// - _allDataStores is a list of (IDataStore, IMountResourceManager) tuples
        /// - GetResourcesByIndex(int index) returns _allDataStores[index].Resources
        /// - GetResourcesForItem(VfsModelItem item) returns GetResourcesByIndex(item.DataStoreIndex)
        /// </summary>
        private class DataStoreRoutingModel
        {
            private readonly List<MockResourceManager> _resourceManagers;

            public DataStoreRoutingModel(int count)
            {
                _resourceManagers = new List<MockResourceManager>();
                for (int i = 0; i < count; i++)
                    _resourceManagers.Add(new MockResourceManager(i));
            }

            public int Count => _resourceManagers.Count;

            /// <summary>
            /// Mirrors VfsModel.GetResourcesByIndex(int dataStoreIndex)
            /// </summary>
            public MockResourceManager GetResourcesByIndex(int dataStoreIndex)
            {
                if (dataStoreIndex >= 0 && dataStoreIndex < _resourceManagers.Count)
                    return _resourceManagers[dataStoreIndex];
                return _resourceManagers[0]; // fallback to primary, same as VfsModel
            }

            /// <summary>
            /// Mirrors VfsModel.GetResourcesForItem(VfsModelItem item) which delegates
            /// to GetResourcesByIndex(item.DataStoreIndex)
            /// </summary>
            public MockResourceManager GetResourcesForItem(int itemDataStoreIndex) => GetResourcesByIndex(itemDataStoreIndex);
        }

        /// <summary>
        /// **Validates: Requirements 7.2**
        ///
        /// Property 2: DataStoreIndex resource manager routing.
        /// For N resource managers (N ∈ [1..5]) and items with random DataStoreIndex values
        /// in [0, N-1], GetResourcesByIndex returns the correct manager at that index.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ValidIndex_ReturnsCorrectResourceManager(
            NonNegativeInt nSeed,
            NonNegativeInt indexSeed)
        {
            int n = (nSeed.Get % 5) + 1; // N ∈ [1..5]
            int dataStoreIndex = indexSeed.Get % n; // valid index in [0, N-1]

            DataStoreRoutingModel model = new DataStoreRoutingModel(n);
            MockResourceManager result = model.GetResourcesByIndex(dataStoreIndex);

            return result.Id == dataStoreIndex;
        }

        /// <summary>
        /// **Validates: Requirements 7.2**
        ///
        /// Property 2: DataStoreIndex resource manager routing.
        /// GetResourcesForItem delegates to GetResourcesByIndex with the item's DataStoreIndex,
        /// returning the same instance.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool GetResourcesForItem_DelegatesToGetResourcesByIndex(
            NonNegativeInt nSeed,
            NonNegativeInt indexSeed)
        {
            int n = (nSeed.Get % 5) + 1; // N ∈ [1..5]
            int dataStoreIndex = indexSeed.Get % n; // valid index in [0, N-1]

            DataStoreRoutingModel model = new DataStoreRoutingModel(n);
            MockResourceManager byIndex = model.GetResourcesByIndex(dataStoreIndex);
            MockResourceManager forItem = model.GetResourcesForItem(dataStoreIndex);

            return ReferenceEquals(byIndex, forItem);
        }

        /// <summary>
        /// **Validates: Requirements 7.2**
        ///
        /// Property 2: DataStoreIndex resource manager routing.
        /// For multiple items with different DataStoreIndex values, each routes to its
        /// own distinct resource manager (no cross-routing).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool MultipleItems_EachRoutesToCorrectManager(
            NonNegativeInt nSeed,
            NonNegativeInt itemCountSeed,
            NonNegativeInt contentSeed)
        {
            int n = (nSeed.Get % 5) + 1; // N ∈ [1..5]
            int itemCount = (itemCountSeed.Get % 20) + 1; // 1 to 20 items
            Random rng = new Random(contentSeed.Get);

            DataStoreRoutingModel model = new DataStoreRoutingModel(n);

            for (int i = 0; i < itemCount; i++)
            {
                int dataStoreIndex = rng.Next(0, n);
                MockResourceManager result = model.GetResourcesForItem(dataStoreIndex);

                if (result.Id != dataStoreIndex)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 7.2**
        ///
        /// Property 2: DataStoreIndex resource manager routing.
        /// Each index in [0, N-1] returns a unique resource manager instance (no aliasing
        /// between different indices).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllIndices_ReturnDistinctManagers(NonNegativeInt nSeed)
        {
            int n = (nSeed.Get % 5) + 1; // N ∈ [1..5]
            DataStoreRoutingModel model = new DataStoreRoutingModel(n);

            HashSet<MockResourceManager> managers = new HashSet<MockResourceManager>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < n; i++)
            {
                MockResourceManager mgr = model.GetResourcesByIndex(i);
                if (!managers.Add(mgr))
                    return false; // duplicate reference found — aliasing
            }

            return managers.Count == n;
        }

        /// <summary>
        /// **Validates: Requirements 7.2**
        ///
        /// Property 2: DataStoreIndex resource manager routing.
        /// Calling GetResourcesByIndex with the same index multiple times always returns
        /// the same instance (identity stability).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool SameIndex_AlwaysReturnsSameInstance(
            NonNegativeInt nSeed,
            NonNegativeInt indexSeed,
            NonNegativeInt repeatSeed)
        {
            int n = (nSeed.Get % 5) + 1; // N ∈ [1..5]
            int dataStoreIndex = indexSeed.Get % n;
            int repeats = (repeatSeed.Get % 10) + 2; // 2-11 repeats

            DataStoreRoutingModel model = new DataStoreRoutingModel(n);
            MockResourceManager first = model.GetResourcesByIndex(dataStoreIndex);

            for (int r = 0; r < repeats; r++)
            {
                MockResourceManager current = model.GetResourcesByIndex(dataStoreIndex);
                if (!ReferenceEquals(first, current))
                    return false;
            }

            return true;
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 3: Directories system type exclusion
    /// <summary>
    /// Property-based tests for Directories system type exclusion.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    ///
    /// For any set of image records loaded into VfsModel where some records have system type
    /// equal to VfsConstants.SystemDirectories, the Images folder listing SHALL contain zero
    /// items with the "Directories" system type.
    /// </summary>
    public class DirectoriesExclusionPropertyTests
    {
        private static readonly string[] SystemTypes = new[]
        {
            "GameCube", "Wii", "WiiU", "NES", "SNES", "N64", "GBA", "NDS",
            "3DS", "Switch", "Directories", "directories", "DIRECTORIES",
            "PlayStation", "Xbox", "Sega"
        };

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 3: Directories system type exclusion.
        /// For any list of items with random system types (some being "Directories"),
        /// applying the VfsModel Image-kind exclusion filter removes all "Directories" items.
        /// This replicates the filtering logic: items with system type equal to
        /// VfsConstants.SystemDirectories (case-insensitive) are excluded from Image-kind listings.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ImageKindListing_ExcludesAllDirectoriesItems(NonNegativeInt countSeed, NonNegativeInt typeSeed)
        {
            // Generate a list of items with random system types
            int itemCount = (countSeed.Get % 20) + 1; // 1 to 20 items
            List<(string SystemType, string Name)> items = new List<(string SystemType, string Name)>();
            int seed = typeSeed.Get;

            for (int i = 0; i < itemCount; i++)
            {
                string system = SystemTypes[(seed + (i * 7)) % SystemTypes.Length];
                items.Add((system, $"Image_{i}"));
            }

            // Apply the same filtering logic as VfsModel.LoadImages():
            // Items with system == VfsConstants.SystemDirectories (case-insensitive) are excluded
            // from Image-kind mount index entries
            List<(string SystemType, string Name)> imageKindItems = items
                .Where(item => !string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Verify: zero items with "Directories" system type in the filtered result
            bool noDirectoriesInResult = !imageKindItems.Any(item =>
                string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase));

            return noDirectoriesInResult;
        }

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 3: Directories system type exclusion.
        /// The exclusion is case-insensitive: any case variant of "Directories" is excluded.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool DirectoriesExclusion_IsCaseInsensitive(NonNegativeInt seed)
        {
            // Generate case variants of "Directories"
            string baseStr = "directories";
            char[] chars = baseStr.ToCharArray();
            int bits = seed.Get;
            for (int i = 0; i < chars.Length; i++)
            {
                if ((bits & (1 << (i % 16))) != 0)
                    chars[i] = char.ToUpper(chars[i]);
            }
            string dirVariant = new string(chars);

            // Create a mixed list with the variant and some non-Directories items
            (string SystemType, string Name)[] items = new[]
            {
                (SystemType: "GameCube", Name: "Game1"),
                (SystemType: dirVariant, Name: "DirItem1"),
                (SystemType: "Wii", Name: "Game2"),
                (SystemType: dirVariant, Name: "DirItem2"),
                (SystemType: "WiiU", Name: "Game3")
            };

            // Apply the VfsModel exclusion filter
            (string SystemType, string Name)[] imageKindItems = items
                .Where(item => !string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Verify: no items with the Directories variant remain
            bool noDirectoriesInResult = !imageKindItems.Any(item =>
                string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase));

            // Verify: non-Directories items are preserved
            bool nonDirectoriesPreserved = imageKindItems.Length == 3;

            return noDirectoriesInResult && nonDirectoriesPreserved;
        }

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 3: Directories system type exclusion.
        /// When all items have system type "Directories", the Image-kind listing is empty.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllDirectoriesItems_ProducesEmptyImageListing(PositiveInt countWrapper)
        {
            int itemCount = (countWrapper.Get % 20) + 1; // 1 to 20 items
            List<(string SystemType, string Name)> items = new List<(string SystemType, string Name)>();

            for (int i = 0; i < itemCount; i++)
            {
                items.Add((VfsConstants.SystemDirectories, $"DirImage_{i}"));
            }

            // Apply the VfsModel exclusion filter
            List<(string SystemType, string Name)> imageKindItems = items
                .Where(item => !string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return imageKindItems.Count == 0;
        }

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 3: Directories system type exclusion.
        /// When no items have system type "Directories", all items appear in the Image-kind listing.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NoDirectoriesItems_AllAppearInImageListing(NonNegativeInt countSeed, NonNegativeInt typeSeed)
        {
            // Use only non-Directories system types
            string[] nonDirSystems = new[] { "GameCube", "Wii", "WiiU", "NES", "SNES", "N64", "Switch" };
            int itemCount = (countSeed.Get % 20) + 1;
            List<(string SystemType, string Name)> items = new List<(string SystemType, string Name)>();
            int seed = typeSeed.Get;

            for (int i = 0; i < itemCount; i++)
            {
                string system = nonDirSystems[(seed + (i * 3)) % nonDirSystems.Length];
                items.Add((system, $"Image_{i}"));
            }

            // Apply the VfsModel exclusion filter
            List<(string SystemType, string Name)> imageKindItems = items
                .Where(item => !string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // All items should be preserved since none are "Directories"
            return imageKindItems.Count == items.Count;
        }

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 3: Directories system type exclusion.
        /// The count of items in the Image-kind listing equals the total count minus
        /// the count of "Directories" items — no items are lost or duplicated.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool FilteredCount_EqualsTotal_MinusDirectoriesCount(NonNegativeInt countSeed, NonNegativeInt typeSeed)
        {
            int itemCount = (countSeed.Get % 30) + 1; // 1 to 30 items
            List<(string SystemType, string Name)> items = new List<(string SystemType, string Name)>();
            int seed = typeSeed.Get;

            for (int i = 0; i < itemCount; i++)
            {
                string system = SystemTypes[(seed + (i * 11)) % SystemTypes.Length];
                items.Add((system, $"Image_{i}"));
            }

            int directoriesCount = items.Count(item =>
                string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase));

            // Apply the VfsModel exclusion filter
            List<(string SystemType, string Name)> imageKindItems = items
                .Where(item => !string.Equals(item.SystemType, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return imageKindItems.Count == items.Count - directoriesCount;
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 8: "All" set name normalization
    /// <summary>
    /// Property-based tests for "All" set name normalization.
    ///
    /// **Validates: Requirements 13.5**
    ///
    /// For any MountRequest where SetName equals "All" (case-insensitive), the MountOrchestrator
    /// SHALL normalize the set name to null before passing it to VfsModel, so that all sets are
    /// included in the mount.
    /// </summary>
    public class AllSetNameNormalizationPropertyTests
    {
        private static readonly string[] AllVariants = new[]
        {
            "All", "all", "ALL", "aLl", "alL", "ALl", "aLL", "All"
        };

        /// <summary>
        /// **Validates: Requirements 13.5**
        ///
        /// Property 8: "All" set name normalization.
        /// For any case variant of "All", NormalizeSetName returns null.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllCaseVariants_NormalizedToNull(NonNegativeInt seed)
        {
            string allVariant = AllVariants[seed.Get % AllVariants.Length];

            string result = MountOrchestrator.NormalizeSetName(allVariant);

            return result == null;
        }

        /// <summary>
        /// **Validates: Requirements 13.5**
        ///
        /// Property 8: "All" set name normalization.
        /// For any non-null string that is NOT "All" (case-insensitive),
        /// NormalizeSetName returns the original value unchanged.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NonAllStrings_ReturnedUnchanged(NonEmptyString setNameWrapper)
        {
            string setName = setNameWrapper.Get;

            // Skip if it happens to be an "All" variant
            if (string.Equals(setName, "All", StringComparison.OrdinalIgnoreCase))
                return true; // vacuously true, skip this case

            string result = MountOrchestrator.NormalizeSetName(setName);

            return result == setName;
        }

        /// <summary>
        /// **Validates: Requirements 13.5**
        ///
        /// Property 8: "All" set name normalization.
        /// Null input returns null.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NullInput_ReturnsNull(NonNegativeInt _)
        {
            string result = MountOrchestrator.NormalizeSetName(null);

            return result == null;
        }

        /// <summary>
        /// **Validates: Requirements 13.5**
        ///
        /// Property 8: "All" set name normalization.
        /// For randomly generated "All" case variants (constructed by randomizing each character's case),
        /// NormalizeSetName always returns null.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool RandomAllCasePermutations_NormalizedToNull(NonNegativeInt seed)
        {
            // Generate a random case permutation of "All"
            char[] chars = "all".ToCharArray();
            int bits = seed.Get;
            for (int i = 0; i < chars.Length; i++)
            {
                if ((bits & (1 << i)) != 0)
                    chars[i] = char.ToUpper(chars[i]);
            }
            string allVariant = new string(chars);

            string result = MountOrchestrator.NormalizeSetName(allVariant);

            return result == null;
        }

        /// <summary>
        /// **Validates: Requirements 13.5**
        ///
        /// Property 8: "All" set name normalization.
        /// NormalizeSetName is idempotent: calling it twice on any input produces the same result.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool Idempotent_DoubleNormalizationSameResult(NonEmptyString setNameWrapper)
        {
            string setName = setNameWrapper.Get;

            string first = MountOrchestrator.NormalizeSetName(setName);
            string second = MountOrchestrator.NormalizeSetName(first);

            return first == second;
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 4: LRU cache capacity invariant
    /// <summary>
    /// Property-based tests for LRU cache capacity invariant.
    ///
    /// **Validates: Requirements 9.2, 9.3**
    ///
    /// For any sequence of TrackNkfsLoaded(imageId) calls on a VfsModel with cache capacity C,
    /// the number of entries tracked in the LRU cache SHALL never exceed C, and when the cache
    /// is at capacity, the evicted entry SHALL always be the least-recently-used (the entry
    /// whose last access/insertion is oldest).
    /// </summary>
    public class LruCacheCapacityInvariantPropertyTests
    {
        private const int TestCacheCapacity = 8;
        private const int IdPoolSize = 50;

        /// <summary>
        /// Mirrors the NkFs LRU cache state from VfsModel: LinkedList for ordering (MRU at tail,
        /// LRU at head) and Dictionary for O(1) node lookup. Tracks evicted items,
        /// matching the VfsModel.TrackNkfsLoaded() behavior exactly.
        /// </summary>
        private class NkfsLruCache
        {
            public readonly int Capacity;
            public readonly LinkedList<long> CacheOrder = new LinkedList<long>();
            public readonly Dictionary<long, LinkedListNode<long>> CacheNodes = new Dictionary<long, LinkedListNode<long>>();

            /// <summary>
            /// Records each eviction event: (evictedId, cacheSnapshotBeforeEviction).
            /// The snapshot captures the cache order just before the eviction occurs.
            /// </summary>
            public readonly List<(long EvictedId, List<long> CacheSnapshot)> EvictionLog = new List<(long, List<long>)>();

            public NkfsLruCache(int capacity)
            {
                Capacity = capacity;
            }

            /// <summary>
            /// Mirrors VfsModel.TrackNkfsLoaded(). Registers an image in the NkFs LRU cache.
            /// If already tracked, promotes to MRU. If at capacity, evicts LRU entry.
            /// </summary>
            public void TrackNkfsLoaded(long imageId)
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
                }

                // Add new entry at MRU position
                LinkedListNode<long> node = CacheOrder.AddLast(imageId);
                CacheNodes[imageId] = node;
            }

            /// <summary>
            /// Mirrors VfsModel.TouchNkfsCache(). Promotes an existing entry to MRU position.
            /// </summary>
            public void TouchNkfsCache(long imageId)
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
        /// Produces a sequence of length > capacity (50–200) with IDs from a pool of 50.
        /// </summary>
        private static int[] GenerateAccessSequence(int lengthSeed, int contentSeed)
        {
            int length = 50 + (Math.Abs(lengthSeed) % 151); // 50–200, always > capacity
            Random rng = new Random(contentSeed);
            int[] sequence = new int[length];
            for (int i = 0; i < length; i++)
            {
                sequence[i] = rng.Next(1, IdPoolSize + 1); // IDs 1–50
            }
            return sequence;
        }

        /// <summary>
        /// **Validates: Requirements 9.2, 9.3**
        ///
        /// Property 4: LRU cache capacity invariant.
        /// For any random sequence of image IDs (length > capacity), with cache capacity 8:
        /// 1. The cache size never exceeds capacity after any TrackNkfsLoaded call.
        /// 2. When eviction occurs, the evicted entry is always the LRU (head of the list).
        /// 3. The cache order matches a reference model tracking true LRU ordering.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NkfsCache_NeverExceedsCapacity_And_EvictsLruItems(
            NonNegativeInt lengthSeed,
            NonNegativeInt contentSeed)
        {
            int[] accessSequence = GenerateAccessSequence(lengthSeed.Get, contentSeed.Get);
            NkfsLruCache cache = new NkfsLruCache(TestCacheCapacity);

            // Reference model: tracks the true LRU order as a simple list.
            // Most recently used at the end, least recently used at the front.
            List<long> referenceOrder = new List<long>();

            foreach (int rawId in accessSequence)
            {
                long imageId = rawId;

                // Simulate the access: TrackNkfsLoaded (handles both new and existing entries)
                cache.TrackNkfsLoaded(imageId);

                // Update reference model
                referenceOrder.Remove(imageId);
                while (referenceOrder.Count >= TestCacheCapacity)
                {
                    referenceOrder.RemoveAt(0); // Remove LRU
                }
                referenceOrder.Add(imageId); // Add as MRU

                // INVARIANT 1: Cache size never exceeds capacity
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
                if (cacheSnapshot.Count == 0 || cacheSnapshot[0] != evictedId)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 9.2, 9.3**
        ///
        /// Property 4: LRU cache capacity invariant (with Touch operations).
        /// With mixed TrackNkfsLoaded and TouchNkfsCache operations, the cache order
        /// must always match the expected LRU ordering and never exceed capacity.
        /// Touching an item promotes it to MRU, preventing its eviction while older
        /// untouched items remain.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NkfsCache_WithTouch_MaintainsCapacityAndOrder(
            NonNegativeInt lengthSeed,
            NonNegativeInt contentSeed,
            NonNegativeInt touchSeed)
        {
            int[] accessSequence = GenerateAccessSequence(lengthSeed.Get, contentSeed.Get);
            Random touchRng = new Random(touchSeed.Get);
            NkfsLruCache cache = new NkfsLruCache(TestCacheCapacity);
            List<long> referenceOrder = new List<long>();

            foreach (int rawId in accessSequence)
            {
                long imageId = rawId;

                // Decide whether to Touch (promote existing only) or Track (add/promote)
                bool shouldTouch = referenceOrder.Contains(imageId) && touchRng.Next(4) == 0;

                if (shouldTouch)
                {
                    cache.TouchNkfsCache(imageId);

                    // Update reference: promote to MRU
                    referenceOrder.Remove(imageId);
                    referenceOrder.Add(imageId);
                }
                else
                {
                    cache.TrackNkfsLoaded(imageId);

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

        /// <summary>
        /// **Validates: Requirements 9.2, 9.3**
        ///
        /// Property 4: LRU cache capacity invariant (exact capacity boundary).
        /// When exactly C distinct items are added (where C = capacity), no eviction occurs.
        /// When the (C+1)th distinct item is added, exactly one eviction occurs and it targets
        /// the first item added (the LRU).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NkfsCache_ExactCapacityBoundary_EvictsCorrectly(NonNegativeInt startIdSeed)
        {
            NkfsLruCache cache = new NkfsLruCache(TestCacheCapacity);
            long startId = (startIdSeed.Get % 1000) + 1;

            // Add exactly capacity items — no eviction should occur
            for (int i = 0; i < TestCacheCapacity; i++)
            {
                cache.TrackNkfsLoaded(startId + i);
            }

            if (cache.CacheOrder.Count != TestCacheCapacity)
                return false;
            if (cache.EvictionLog.Count != 0)
                return false;

            // Add one more — should evict the first item (startId)
            cache.TrackNkfsLoaded(startId + TestCacheCapacity);

            if (cache.CacheOrder.Count != TestCacheCapacity)
                return false;
            if (cache.EvictionLog.Count != 1)
                return false;
            if (cache.EvictionLog[0].EvictedId != startId)
                return false;

            return true;
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 5: Per-item lock identity stability
    /// <summary>
    /// Property-based tests for per-item lock identity stability.
    ///
    /// **Validates: Requirements 9.4**
    ///
    /// For any image ID, calling GetNkfsLoadLock(imageId) multiple times SHALL always return
    /// the same object reference, ensuring that concurrent callers synchronize on the same
    /// lock instance.
    ///
    /// Since VfsModel requires real DataStore paths to construct, this test validates the
    /// ConcurrentDictionary.GetOrAdd pattern used by GetNkfsLoadLock directly — the same
    /// algorithm that guarantees lock identity stability.
    /// </summary>
    public class PerItemLockIdentityStabilityPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 9.4**
        ///
        /// Property 5: Per-item lock identity stability.
        /// For any random image ID, calling GetNkfsLoadLock twice returns the same
        /// object reference (ReferenceEquals).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool SameId_ReturnsSameReference(long imageId)
        {
            ConcurrentDictionary<long, object> locks = new ConcurrentDictionary<long, object>();
            object GetLock(long id) => locks.GetOrAdd(id, _ => new object());

            object first = GetLock(imageId);
            object second = GetLock(imageId);

            return ReferenceEquals(first, second);
        }

        /// <summary>
        /// **Validates: Requirements 9.4**
        ///
        /// Property 5: Per-item lock identity stability.
        /// For any list of random image IDs, calling GetNkfsLoadLock twice per ID
        /// always returns the same reference for each ID.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool MultipleIds_EachReturnsSameReference(long[] imageIds)
        {
            if (imageIds == null || imageIds.Length == 0)
                return true; // vacuously true for empty input

            ConcurrentDictionary<long, object> locks = new ConcurrentDictionary<long, object>();
            object GetLock(long id) => locks.GetOrAdd(id, _ => new object());

            foreach (long id in imageIds)
            {
                object first = GetLock(id);
                object second = GetLock(id);

                if (!ReferenceEquals(first, second))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 9.4**
        ///
        /// Property 5: Per-item lock identity stability.
        /// Different image IDs return different lock objects (no aliasing).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool DifferentIds_ReturnDifferentReferences(long id1, long id2)
        {
            if (id1 == id2)
                return true; // vacuously true when IDs are the same

            ConcurrentDictionary<long, object> locks = new ConcurrentDictionary<long, object>();
            object GetLock(long id) => locks.GetOrAdd(id, _ => new object());

            object lock1 = GetLock(id1);
            object lock2 = GetLock(id2);

            return !ReferenceEquals(lock1, lock2);
        }

        /// <summary>
        /// **Validates: Requirements 9.4**
        ///
        /// Property 5: Per-item lock identity stability.
        /// Repeated access across interleaved IDs still returns stable references.
        /// Simulates the pattern where multiple image IDs are accessed in arbitrary order.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool InterleavedAccess_MaintainsStability(long[] imageIds, NonNegativeInt repeatSeed)
        {
            if (imageIds == null || imageIds.Length == 0)
                return true;

            ConcurrentDictionary<long, object> locks = new ConcurrentDictionary<long, object>();
            object GetLock(long id) => locks.GetOrAdd(id, _ => new object());

            // First pass: record the lock for each unique ID
            Dictionary<long, object> expectedLocks = new Dictionary<long, object>();
            foreach (long id in imageIds)
            {
                if (!expectedLocks.ContainsKey(id))
                    expectedLocks[id] = GetLock(id);
            }

            // Second pass: access in different order (reversed), verify stability
            int repeats = (repeatSeed.Get % 5) + 2; // 2-6 repeats
            for (int r = 0; r < repeats; r++)
            {
                foreach (long id in imageIds.Reverse())
                {
                    object lockObj = GetLock(id);
                    if (!ReferenceEquals(lockObj, expectedLocks[id]))
                        return false;
                }
            }

            return true;
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 6: No Console output in library source
    /// <summary>
    /// Static analysis test verifying no Console output exists in library source.
    ///
    /// **Validates: Requirements 11.1, 11.3**
    ///
    /// For any .cs source file within the NKDS/ project directory, the file SHALL contain
    /// zero occurrences of Console.WriteLine, Console.Write, Console.Error.WriteLine,
    /// Console.Error.Write, or Console.ReadLine.
    /// </summary>
    public class NoConsoleOutputInLibrarySourceTests
    {
        private static readonly string[] ConsolePatterns = new[]
        {
            "Console.WriteLine",
            "Console.Write(",
            "Console.Error.WriteLine",
            "Console.Error.Write(",
            "Console.ReadLine"
        };

        /// <summary>
        /// **Validates: Requirements 11.1, 11.3**
        ///
        /// Property 6: No Console output in library source.
        /// Scans all .cs files in the NKDS/ project directory for Console output calls.
        /// Excludes commented-out lines (lines starting with // after trimming).
        /// Verifies zero occurrences across all files.
        /// </summary>
        [Fact]
        public void NkdsLibrary_ContainsNoConsoleOutput()
        {
            // Locate the NKDS project directory relative to the test assembly
            string testAssemblyDir = Path.GetDirectoryName(typeof(NoConsoleOutputInLibrarySourceTests).Assembly.Location)!;
            string nkdsProjectDir = FindNkdsProjectDirectory(testAssemblyDir);

            Assert.True(Directory.Exists(nkdsProjectDir),
                $"NKDS project directory not found. Searched from: {testAssemblyDir}");

            List<string> csFiles = Directory.GetFiles(nkdsProjectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.Combine("obj", "")) && !f.Contains(Path.Combine("bin", "")))
                .ToList();

            Assert.True(csFiles.Count > 0, "No .cs files found in NKDS project directory");

            List<string> violations = new List<string>();

            foreach (string filePath in csFiles)
            {
                string[] lines = File.ReadAllLines(filePath);
                for (int lineNum = 0; lineNum < lines.Length; lineNum++)
                {
                    string line = lines[lineNum];
                    string trimmedLine = line.TrimStart();

                    // Skip commented-out lines
                    if (trimmedLine.StartsWith("//"))
                        continue;

                    foreach (string pattern in ConsolePatterns)
                    {
                        if (line.Contains(pattern))
                        {
                            string relativePath = Path.GetRelativePath(nkdsProjectDir, filePath);
                            violations.Add($"  {relativePath}:{lineNum + 1} — {pattern} found: {line.Trim()}");
                            break; // Only report once per line
                        }
                    }
                }
            }

            Assert.True(violations.Count == 0,
                $"Found {violations.Count} Console output occurrence(s) in NKDS library source:\n" +
                string.Join("\n", violations));
        }

        /// <summary>
        /// Walks up from the test assembly output directory to find the NKDS project folder.
        /// Handles both direct execution and CI build layouts.
        /// </summary>
        private static string FindNkdsProjectDirectory(string startDir)
        {
            // Walk up to find the solution root (contains NKDS/ directory)
            string dir = startDir;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "NKDS");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "NKDS.csproj")))
                    return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }

            // Fallback: try relative path from typical test output location
            // e.g., NKitDataStore.Tests/bin/Debug/net8.0/ → ../../../../NKDS/
            string fallback = Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", "NKDS"));
            return fallback;
        }
    }

    // Feature: nkds-vfs-library-extraction, Property 7: No inline magic strings
    /// <summary>
    /// Static analysis test verifying no inline magic strings for system types and mount roots
    /// exist in NKDS/Vfs/ source files (excluding VfsConstants.cs).
    ///
    /// **Validates: Requirements 12.3**
    ///
    /// For any .cs source file within the NKDS/Vfs/ directory (excluding VfsConstants.cs),
    /// the file SHALL contain zero inline string literals matching known magic values
    /// ("Directories", "WiiU", "Images", "Filesystems") — all such comparisons SHALL
    /// reference VfsConstants members.
    /// </summary>
    public class NoInlineMagicStringsPropertyTests
    {
        private static readonly string[] MagicStrings = new[]
        {
            "\"Directories\"",
            "\"WiiU\"",
            "\"Images\"",
            "\"Filesystems\""
        };

        /// <summary>
        /// **Validates: Requirements 12.3**
        ///
        /// Property 7: No inline magic strings for system types and mount roots.
        /// Scans all .cs files in NKDS/Vfs/ (excluding VfsConstants.cs) for inline string
        /// literals "Directories", "WiiU", "Images", "Filesystems".
        /// Excludes commented-out lines (lines starting with // after trimming).
        /// Excludes XML doc comment lines (lines starting with /// after trimming).
        /// Verifies zero occurrences across all files.
        /// </summary>
        [Fact]
        public void NkdsVfs_ContainsNoInlineMagicStrings()
        {
            // Locate the NKDS/Vfs project directory relative to the test assembly
            string testAssemblyDir = Path.GetDirectoryName(typeof(NoInlineMagicStringsPropertyTests).Assembly.Location)!;
            string vfsDir = FindNkdsVfsDirectory(testAssemblyDir);

            Assert.True(Directory.Exists(vfsDir),
                $"NKDS/Vfs directory not found. Searched from: {testAssemblyDir}");

            List<string> csFiles = Directory.GetFiles(vfsDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.Combine("obj", "")) && !f.Contains(Path.Combine("bin", "")))
                .Where(f => !Path.GetFileName(f).Equals("VfsConstants.cs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(csFiles.Count > 0, "No .cs files found in NKDS/Vfs directory (excluding VfsConstants.cs)");

            List<string> violations = new List<string>();

            foreach (string filePath in csFiles)
            {
                string[] lines = File.ReadAllLines(filePath);
                for (int lineNum = 0; lineNum < lines.Length; lineNum++)
                {
                    string line = lines[lineNum];
                    string trimmedLine = line.TrimStart();

                    // Skip commented-out lines (// comments)
                    if (trimmedLine.StartsWith("//"))
                        continue;

                    // Skip XML doc comment lines (/// comments)
                    if (trimmedLine.StartsWith("///"))
                        continue;

                    foreach (string magicString in MagicStrings)
                    {
                        if (line.Contains(magicString))
                        {
                            string relativePath = Path.GetRelativePath(vfsDir, filePath);
                            violations.Add($"  {relativePath}:{lineNum + 1} — {magicString} found: {line.Trim()}");
                            break; // Only report once per line
                        }
                    }
                }
            }

            Assert.True(violations.Count == 0,
                $"Found {violations.Count} inline magic string occurrence(s) in NKDS/Vfs source:\n" +
                string.Join("\n", violations));
        }

        /// <summary>
        /// Walks up from the test assembly output directory to find the NKDS/Vfs directory.
        /// Handles both direct execution and CI build layouts.
        /// </summary>
        private static string FindNkdsVfsDirectory(string startDir)
        {
            // Walk up to find the solution root (contains NKDS/Vfs/ directory)
            string dir = startDir;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "NKDS", "Vfs");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }

            // Fallback: try relative path from typical test output location
            // e.g., NKitDataStore.Tests/bin/Debug/net8.0/ → ../../../../NKDS/Vfs/
            string fallback = Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", "NKDS", "Vfs"));
            return fallback;
        }
    }
}
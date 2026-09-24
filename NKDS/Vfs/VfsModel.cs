using NKitDataStore;
using NKitDataStore.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Nanook.NKit.Vfs
{
    public class VfsModel : IDisposable, IVfsProvider
    {
        private readonly Action<string> _statusCallback;

        /// <summary>
        /// Reports a status message via the callback (if provided) or Trace.WriteLine.
        /// </summary>
        private void reportStatus(string message)
        {
            if (_statusCallback != null)
                _statusCallback(message);
            else
                Trace.WriteLine(message);
        }

        // ... existing code ...
        // Shared mount resource manager (readers + buffer caches)
        private readonly IMountResourceManager _resources;
        internal List<VfsModelItem> Images { get; }
        public string DataStorePath { get; }
        public string SetName { get; }
        private IDataStore _dataStore;
        // Additional DataStores for multi-path mount support
        private readonly List<(IDataStore Store, IMountResourceManager Resources)> _allDataStores = new();
        private IFsFolder _root;
        private char _separator;

        private readonly bool _showImage;
        private readonly bool _showFileSystem;
        private readonly bool _showSystem;
        public bool UpdateMode { get; }

        /// <summary>
        /// Shared mount resource manager for readers and buffer caches.
        /// </summary>
        internal IMountResourceManager Resources => _resources;

        /// <summary>
        /// Gets the resource manager for a specific VfsModelItem (routes to the correct DataStore).
        /// </summary>
        internal IMountResourceManager GetResourcesForItem(VfsModelItem item) => GetResourcesByIndex(item.DataStoreIndex);

        /// <summary>
        /// Gets the resource manager by DataStore index.
        /// </summary>
        internal IMountResourceManager GetResourcesByIndex(int dataStoreIndex)
        {
            if (dataStoreIndex >= 0 && dataStoreIndex < _allDataStores.Count)
                return _allDataStores[dataStoreIndex].Resources;
            return _resources;
        }

        // --- Lazy filesystem loading fields ---
        private readonly int _maxFileSystemYamlSizeKiB;

        // LRU cache for lazily-loaded NkFs data
        private readonly object _nkfsCacheLock = new object();
        private readonly LinkedList<long> _nkfsCacheOrder = new LinkedList<long>(); // image IDs, MRU at tail
        private readonly Dictionary<long, LinkedListNode<long>> _nkfsCacheNodes = new Dictionary<long, LinkedListNode<long>>();
        private readonly int _nkfsCacheCapacity = VfsConstants.DefaultNkfsCacheCapacity; // max loaded NkFs entries (configurable)

        // Optional total connection budget for adaptive pool sizing
        private readonly int? _totalConnectionBudget;

        // Per-item locks for concurrent lazy loading
        private readonly ConcurrentDictionary<long, object> _nkfsLoadLocks = new ConcurrentDictionary<long, object>();

        // Shard database paths collected during LoadImages() for pre-warming
        private List<string> _lastLoadedShardPaths = new List<string>();

        // Phase timing fields populated by LoadImages() and the constructor
        private long _discoveryMs;
        private long _imageLoadingMs;
        private long _nkfsParsingMs;
        private int _lastLoadedSetCount;
        private int _lastLoadedImageCount;
        private int _lastLoadedEagerNkfsCount;
        private int _lastLoadedDeferredNkfsCount;

        public VfsModel(string dataStorePath, char separator, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, int maxFileSystemYamlSizeKiB = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB, int nkfsCacheCapacity = VfsConstants.DefaultNkfsCacheCapacity, int imageReaderCacheCapacity = VfsConstants.DefaultImageReaderCacheCapacity, int? totalConnectionBudget = null, Action<string> statusCallback = null)
            : this(new[] { dataStorePath }, separator, setName, showImage, showFileSystem, showSystem, updateMode, maxFileSystemYamlSizeKiB, nkfsCacheCapacity, imageReaderCacheCapacity, totalConnectionBudget, statusCallback)
        {
        }

        public VfsModel(IEnumerable<string> dataStorePaths, char separator, string setName = null, bool showImage = true, bool showFileSystem = true, bool showSystem = false, bool updateMode = false, int maxFileSystemYamlSizeKiB = NKitDataStore.DataStore.DefaultMaxFileSystemSizeKiB, int nkfsCacheCapacity = VfsConstants.DefaultNkfsCacheCapacity, int imageReaderCacheCapacity = VfsConstants.DefaultImageReaderCacheCapacity, int? totalConnectionBudget = null, Action<string> statusCallback = null)
        {
            _statusCallback = statusCallback;
            List<string> pathList = dataStorePaths.ToList();
            if (pathList.Count == 0)
                throw new ArgumentException("At least one DataStore path is required.", nameof(dataStorePaths));

            Stopwatch initSw = System.Diagnostics.Stopwatch.StartNew();
            // Enable mount debug tracing for DataStore lifecycle visibility
            NKitDataStore.DataStore.MountDebug = true;
            _nkfsCacheCapacity = nkfsCacheCapacity;
            _totalConnectionBudget = totalConnectionBudget;
            UpdateMode = updateMode;
            _maxFileSystemYamlSizeKiB = maxFileSystemYamlSizeKiB;
            _separator = separator;
            _showImage = showImage;
            _showFileSystem = showFileSystem;
            _showSystem = showSystem;
            this.Images = new List<VfsModelItem>();
            this.DataStorePath = pathList[0];
            this.SetName = setName;

            // Initialize all data stores
            foreach (string path in pathList)
            {
                DataStore ds = new NKitDataStore.DataStore(path);
                MountResourceManager rm = new MountResourceManager(ds, readerCapacity: 32, ttlMs: 30000);
                _allDataStores.Add((ds, rm));
            }
            // Primary DataStore for backward compatibility
            _dataStore = _allDataStores[0].Store;
            // Initialize the shared mount resource manager (primary)
            _resources = _allDataStores[0].Resources;

            _root = new FsFolder() { Name = "\\", Parent = null };
            reportStatus("Loading Images...");
            // Build initial image/index state so named roots are known immediately
            // This also populates the system folders by scanning the loaded images.
            try
            {
                LoadImages();
            }
            catch (Exception)
            {
                // LoadImages failed - diagnostics available via Trace
            }

            // Phase timing variables for mount time report
            long warmingMs = 0;
            int warmingDbCount = 0;
            int warmingConnCount = 0;
            long preloadNkfsMs = 0;
            int preloadNkfsCount = 0;

            try
            {
                // Phase 2: Connection pre-warming
                Stopwatch warmingSw = System.Diagnostics.Stopwatch.StartNew();
                List<string> allShardPaths = _lastLoadedShardPaths;

                // Apply adaptive pool sizing if budget specified
                if (_totalConnectionBudget.HasValue)
                {
                    // No-op: binary index format doesn't use connection pooling
                }

                // Graceful degradation: cap at MaxPreWarmDatabases databases
                if (allShardPaths.Count > VfsConstants.MaxPreWarmDatabases)
                {
                    Trace.WriteLine($"[Mount] Warning: {allShardPaths.Count} shard databases exceeds limit of {VfsConstants.MaxPreWarmDatabases}, capping pre-warming");
                    allShardPaths = allShardPaths.Take(VfsConstants.MaxPreWarmDatabases).ToList();
                }

                warmingDbCount = allShardPaths.Count;

                // Pre-warm with timeout
                using CancellationTokenSource warmCts = new CancellationTokenSource(TimeSpan.FromSeconds(VfsConstants.PreWarmTimeoutSeconds));
                preWarmConnections(allShardPaths, warmCts.Token);

                warmingSw.Stop();
                warmingMs = warmingSw.ElapsedMilliseconds;
                warmingConnCount = allShardPaths.Count; // one connection per database

                // Task 9.3: Emit connection pre-warming summary via Trace.WriteLine
                Trace.WriteLine($"[Mount] Connection pre-warming completed in {warmingMs}ms ({warmingDbCount} databases, {warmingConnCount} connections)");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Mount] Connection pre-warming failed: {ex.Message}");
            }

            // Phase 3: NkFs pre-loading for TmdAppFolder images
            try
            {
                Stopwatch preloadSw = System.Diagnostics.Stopwatch.StartNew();
                int preloadBefore = Images.Count(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder && i.FileSystemNkfsLoaded);

                using CancellationTokenSource preloadCts = new CancellationTokenSource(TimeSpan.FromSeconds(VfsConstants.PreLoadNkfsTimeoutSeconds));
                preLoadTmdAppFolderNkfs(preloadCts.Token);

                preloadSw.Stop();
                preloadNkfsMs = preloadSw.ElapsedMilliseconds;
                int preloadAfter = Images.Count(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder && i.FileSystemNkfsLoaded);
                preloadNkfsCount = preloadAfter - preloadBefore;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Mount] NkFs pre-loading failed: {ex.Message}");
            }

            // Phase 4: Async stored files pre-loading for TmdAppFolder children (fire-and-forget)
            try
            {
                preLoadStoredFilesForTmdAppFolderChildren();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Mount] Stored files pre-loading launch failed: {ex.Message}");
            }

            // Report mount summary
            try
            {
                // Log Mount Info
                reportStatus("");
                reportStatus($"Images: {Images.Count:N0}");

                //List the images
                //var grouped = Images.Where(i => i.ImageRecord != null)
                //                    .GroupBy(i => i.ImageRecord.SetName)
                //                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

                //foreach (var group in grouped)
                //{
                //    Trace.WriteLine($"{group.Key} ({group.Count():N0})");
                //    foreach (var img in group.OrderBy(i => i.System ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                //                             .ThenBy(i => i.ImageRecord.Name, StringComparer.OrdinalIgnoreCase))
                //    {
                //        var rec = img.ImageRecord;
                //        Trace.WriteLine($"  {rec.Id,-5} {(rec.Name + rec.Format.GetFileExtension())} {rec.System ?? "Unknown"} {rec.Format} {formatBytes(rec.Size)}{(rec.Removed ? " [REMOVED]" : "")}");
                //    }
                //    Trace.WriteLine("");
                //}

                // Task 9.2: Emit mount time report after image listing
                initSw.Stop();
                long totalMs = initSw.ElapsedMilliseconds;
                int setCount = _lastLoadedSetCount;
                int imageCount = _lastLoadedImageCount;
                int eagerCount = _lastLoadedEagerNkfsCount;
                int deferredCount = _lastLoadedDeferredNkfsCount;

                reportStatus("");
                reportStatus($"Mount completed in {totalMs}ms");
                reportStatus($"  Database discovery: {_discoveryMs}ms");
                reportStatus($"  Image loading:      {_imageLoadingMs}ms ({imageCount} images across {setCount} sets)");
                reportStatus($"  NkFs parsing:       {_nkfsParsingMs}ms ({eagerCount} eager, {deferredCount} deferred)");
                reportStatus($"  Connection warming:  {warmingMs}ms ({warmingDbCount} databases, {warmingConnCount} connections)");
                reportStatus($"  NkFs pre-loading:   {preloadNkfsMs}ms ({preloadNkfsCount} TmdAppFolders pre-loaded)");
            }
            catch { }
        }

        private static string formatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";

            int exp = (int)(Math.Log(bytes) / Math.Log(1024));
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

            return $"{bytes / Math.Pow(1024, exp):F2} {units[exp]}";
        }

        /// <summary>
        /// Registers an image's NkFs as loaded in the LRU cache.
        /// Evicts the least recently used entry if the cache is full.
        /// </summary>
        internal void TrackNkfsLoaded(long imageId)
        {
            lock (_nkfsCacheLock)
            {
                // Move to MRU position if already tracked
                if (_nkfsCacheNodes.TryGetValue(imageId, out LinkedListNode<long> existingNode))
                {
                    _nkfsCacheOrder.Remove(existingNode);
                    _nkfsCacheOrder.AddLast(existingNode);
                    return;
                }

                // Evict LRU if at capacity
                while (_nkfsCacheOrder.Count >= _nkfsCacheCapacity)
                {
                    long evictId = _nkfsCacheOrder.First.Value;
                    _nkfsCacheOrder.RemoveFirst();
                    _nkfsCacheNodes.Remove(evictId);

                    // Find and reset the evicted item
                    VfsModelItem evicted = Images.FirstOrDefault(i => i.ImageRecord?.Id == evictId);
                    if (evicted != null)
                    {
                        evicted.FileSystemNkfs = null;
                        evicted.FileSystemNkfsLoaded = false;
                        Trace.WriteLine($"[LazyLoad] Evicted NkFs for '{evicted.ImageRecord.Name}' (LRU)");
                    }
                }

                // Add new entry at MRU position
                LinkedListNode<long> node = _nkfsCacheOrder.AddLast(imageId);
                _nkfsCacheNodes[imageId] = node;
            }
        }

        /// <summary>
        /// Marks an image as recently accessed in the LRU cache (promotes to MRU).
        /// Called by MountRegistry.LoadNkfs() after a successful load or cache hit.
        /// </summary>
        internal void TouchNkfsCache(long imageId)
        {
            lock (_nkfsCacheLock)
            {
                if (_nkfsCacheNodes.TryGetValue(imageId, out LinkedListNode<long> node))
                {
                    _nkfsCacheOrder.Remove(node);
                    _nkfsCacheOrder.AddLast(node);
                }
            }
        }

        /// <summary>
        /// Returns a per-item lock object for thread-safe lazy loading.
        /// </summary>
        internal object GetNkfsLoadLock(long imageId) => _nkfsLoadLocks.GetOrAdd(imageId, _ => new object());

        /// <summary>
        /// Pre-warms connection pools for all shard databases discovered during mount.
        /// Executes in parallel via Task.WhenAll. Respects the provided CancellationToken
        /// for timeout control. Catches and logs any aggregate exceptions without throwing.
        /// </summary>
        private void preWarmConnections(List<string> shardDbPaths, CancellationToken cancellation)
        {
            // No-op: binary index format doesn't use connection pooling
        }

        /// <summary>
        /// Pre-loads NkFs for TmdAppFolder images that were not eagerly loaded.
        /// Stops when cache is full or the CancellationToken is cancelled.
        /// Logs trace on per-image failure and continues with remaining images.
        /// </summary>
        private void preLoadTmdAppFolderNkfs(CancellationToken cancellation)
        {
            List<VfsModelItem> candidates = Images
                .Where(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                          && !i.FileSystemNkfsLoaded)
                .ToList();

            foreach (VfsModelItem item in candidates)
            {
                if (cancellation.IsCancellationRequested)
                {
                    Trace.WriteLine("[Mount] NkFs pre-loading aborted (timeout)");
                    break;
                }

                lock (_nkfsCacheLock)
                {
                    if (_nkfsCacheOrder.Count >= _nkfsCacheCapacity)
                    {
                        Trace.WriteLine("[Mount] NkFs pre-loading stopped (cache full)");
                        break;
                    }
                }

                try
                {
                    _mountRegistry.LoadNkfs(item, this);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Mount] NkFs pre-load failed for '{item.ImageRecord.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Asynchronously pre-loads stored file metadata for child images referenced by
        /// TmdAppFolder NkFs ifs entries. Fires off a Task.Run so the constructor is not blocked.
        /// Logs trace on per-child failure and continues with remaining children.
        /// </summary>
        private void preLoadStoredFilesForTmdAppFolderChildren()
        {
            // Identify TmdAppFolder images that have their NkFs loaded
            List<VfsModelItem> tmdAppFolders = Images
                .Where(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                          && i.FileSystemNkfsLoaded
                          && i.FileSystemNkfs != null)
                .ToList();

            if (tmdAppFolders.Count == 0)
                return;

            // Build a lookup of image ID -> VfsModelItem for quick child resolution
            Dictionary<long, VfsModelItem> imageById = new Dictionary<long, VfsModelItem>();
            foreach (VfsModelItem img in Images)
            {
                if (img.ImageRecord != null)
                    imageById[img.ImageRecord.Id] = img;
            }

            // Collect all child image IDs from all TmdAppFolder NkFs ifs entries
            List<VfsModelItem> childItems = new List<VfsModelItem>();
            foreach (VfsModelItem folder in tmdAppFolders)
            {
                NKitDataStore.NkFs nkfs = folder.FileSystemNkfs;
                for (int i = 0; i < nkfs.EntryCount; i++)
                {
                    try
                    {
                        NKitDataStore.NkFsEntry entry = nkfs.GetEntry(i);
                        if (entry.IsImageFile)
                        {
                            long childImageId = nkfs.GetImageIndex(i);
                            if (imageById.TryGetValue(childImageId, out VfsModelItem childItem)
                                && !childItem.StoredFilesLoaded)
                            {
                                childItems.Add(childItem);
                            }
                        }
                    }
                    catch { } // skip malformed entries
                }
            }

            if (childItems.Count == 0)
                return;

            // Fire-and-forget: load stored files asynchronously without blocking the constructor
            MountRegistry registry = _mountRegistry;
            VfsModel model = this;
            Task.Run(() =>
            {
                foreach (VfsModelItem childItem in childItems)
                {
                    try
                    {
                        registry.LoadStoredFiles(childItem, model);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[Mount] Stored files pre-load failed for child image '{childItem.ImageRecord?.Name}' (ID {childItem.ImageRecord?.Id}): {ex.Message}");
                    }
                }
            });
        }

        public void LoadImages()
        {
            // var sw = System.Diagnostics.Stopwatch.StartNew();
            Images.Clear();

            // Phase timing: database discovery
            Stopwatch discoverySw = System.Diagnostics.Stopwatch.StartNew();

            // Single-pass extraction of configs and images to avoid duplicate DB reads
            // If SetName is constrained due to a Mount filter, only that set is queried
            // Convert KiB threshold to bytes for the DataStore API
            long? maxFsYamlBytes = (_showFileSystem || _showSystem)
                ? (long?)(_maxFileSystemYamlSizeKiB * 1024L)
                : null;


            List<((SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData) Set, int DataStoreIndex)> sets = _allDataStores.SelectMany((entry, idx) =>
                entry.Store.DescribeSetsWithImages(SetName, _showFileSystem || _showSystem, maxFsYamlBytes)
                    .Select(s => (Set: s, DataStoreIndex: idx)))
                .ToList();


            List<(ImageRecord Record, byte[] FileSystemYamlData, int DataStoreIndex)> imageRecords = new List<(ImageRecord Record, byte[] FileSystemYamlData, int DataStoreIndex)>();
            List<string> shardPaths = new List<string>();

            foreach (((SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData) set, int dsIndex) in sets)
            {
                // Collect ONLY the main SQLite database path for connection pre-warming.
                // In the hybrid design (shardSize > 0), _NNNN.nkds files are binary shard
                // data (raw block storage), NOT SQLite databases — never pre-warm those.
                // When a specific .nkds file is specified (scoped set), only that file needs warming.
                if (set.Info?.FilePaths != null && set.Info.FilePaths.Count > 0)
                    shardPaths.Add(set.Info.FilePaths[0]); // First entry is always the main SQLite DB

                int removedCount = 0;
                int setNameMismatchCount = 0;
                foreach (ImageRecord img in set.Images)
                {
                    bool removed = img.Removed;
                    bool setMatch = string.IsNullOrWhiteSpace(SetName) || string.Equals(img.SetName, SetName, StringComparison.OrdinalIgnoreCase);
                    if (removed) removedCount++;
                    if (!setMatch) setNameMismatchCount++;
                    if (matchesSet(img))
                    {
                        set.FileSystemYamlData.TryGetValue(img.Id, out byte[] fsData);
                        imageRecords.Add((img, fsData, dsIndex));
                    }
                }
            }

            // Store shard paths for pre-warming in the constructor
            _lastLoadedShardPaths = shardPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            discoverySw.Stop();
            _discoveryMs = discoverySw.ElapsedMilliseconds;
            _lastLoadedSetCount = sets.Count;
            _lastLoadedImageCount = imageRecords.Count;

            // Phase timing: NkFs parsing
            Stopwatch parsingSw = System.Diagnostics.Stopwatch.StartNew();

            // Parse NkFs data in parallel for all image records (CPU-bound work)
            ConcurrentBag<(ImageRecord Record, NkFs Nkfs, int DataStoreIndex)> parsedItems = new ConcurrentBag<(ImageRecord Record, NkFs Nkfs, int DataStoreIndex)>();

            Parallel.ForEach(
                imageRecords,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                recordWithYaml =>
                {
                    NkFs nkfs = null;
                    byte[] fsData = recordWithYaml.FileSystemYamlData;
                    if (fsData != null)
                    {
                        try
                        {
                            // Detect NkFs binary format by magic number (0x4E4B4653 = "NKFS")
                            if (fsData.Length >= 4
                                && fsData[0] == 0x4E && fsData[1] == 0x4B
                                && fsData[2] == 0x46 && fsData[3] == 0x53)
                            {
                                nkfs = NkFs.FromBytes(fsData);
                            }
                            else
                            {
                                // YAML text — convert to NkFs
                                FsYaml fsYaml = FsYaml.FromBytes(fsData);
                                nkfs = NkFs.FromFsYaml(fsYaml);
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[Mount] NkFs parse error for '{recordWithYaml.Record.Name}': {ex.Message}");
                        }
                    }
                    parsedItems.Add((recordWithYaml.Record, nkfs, recordWithYaml.DataStoreIndex));
                });

            parsingSw.Stop();
            _nkfsParsingMs = parsingSw.ElapsedMilliseconds;

            // Phase timing: image loading (building model items and mount index)
            Stopwatch loadingSw = System.Diagnostics.Stopwatch.StartNew();

            // Build a lookup from the parallel results for O(1) access by (DataStoreIndex, ImageId)
            Dictionary<(int DsIdx, long Id), NkFs> parsedLookup = new Dictionary<(int DsIdx, long Id), NkFs>();
            foreach ((ImageRecord Record, NkFs Nkfs, int DataStoreIndex) parsed in parsedItems)
            {
                parsedLookup[(parsed.DataStoreIndex, parsed.Record.Id)] = parsed.Nkfs;
            }

            // Create model items sequentially using the original imageRecords order
            // to maintain the same VfsModelItem list output as the sequential code
            foreach ((ImageRecord Record, byte[] FileSystemYamlData, int DataStoreIndex) recordWithYaml in imageRecords)
            {
                ImageRecord imageRecord = recordWithYaml.Record;
                int itemDsIndex = recordWithYaml.DataStoreIndex;
                // ImageRecord.Name is always stored without extension; ImageFormat provides it
                string fileExtension = imageRecord.Format.GetFileExtension();
                string mountName = imageRecord.Name + fileExtension;
                string folderName = imageRecord.Name;

                parsedLookup.TryGetValue((itemDsIndex, imageRecord.Id), out NkFs nkfs);

                Images.Add(new VfsModelItem()
                {
                    ImageRecord = imageRecord,
                    DataStoreIndex = itemDsIndex,
                    NameAsIso = mountName,
                    NameAsFolder = folderName,
                    ImageSize = imageRecord.Size,
                    System = imageRecord.System ?? "Unknown",
                    FileSystemNkfs = nkfs,
                    FileSystemNkfsLoaded = nkfs != null
                });

                // Track eagerly-loaded items in the LRU cache
                if (nkfs != null)
                    TrackNkfsLoaded(imageRecord.Id);
            }

            // Count eager and deferred NkFs for mount time report
            _lastLoadedEagerNkfsCount = Images.Count(i => i.FileSystemNkfsLoaded);
            _lastLoadedDeferredNkfsCount = Images.Count(i => !i.FileSystemNkfsLoaded);

            // Collect base names that have a TmdAppFolder — used later in mount index
            // to hide individual [tmd.X] App images from Image-kind listings.
            HashSet<string> tmdAppFolderBaseNames = new HashSet<string>(
                Images.Where(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                      .Select(i => i.ImageRecord.Name),
                StringComparer.OrdinalIgnoreCase);

            if (_root != null)
            {
                _root.Folders.Clear();
                HashSet<string> systemFolders = new HashSet<string>(Images.Where(i => !string.IsNullOrEmpty(i.System)).Select(i => i.System));
                foreach (string system in systemFolders.OrderBy(s => s))
                {
                    _root.Folders.Add(new FsFolder() { Name = system, Parent = _root });
                }
                _mountRegistry = new MountRegistry(systemFolders);
            }

            // Detect duplicate image names within the same system and disambiguate by appending the image id.
            // This ensures mounted VFS listings are unique when multiple images share the same base name.
            // Special case: when a handler declares MergesDuplicates, same-set duplicates are kept in the
            // Images list but marked as IsMergedSecondary. The primary gets MergedImageRecords so the
            // handler can aggregate content. Secondaries receive an ID suffix so filesystem views that
            // do NOT merge can display them individually.
            IEnumerable<IGrouping<string, VfsModelItem>> duplicateGroups = Images.GroupBy(i => (i.System ?? string.Empty).ToLowerInvariant() + "\0" + (i.ImageRecord?.Name ?? string.Empty).ToLowerInvariant())
                                        .Where(g => g.Count() > 1);

            HashSet<VfsModelItem> mergedSecondaries = new HashSet<VfsModelItem>();

            foreach (IGrouping<string, VfsModelItem> group in duplicateGroups)
            {
                // Partition the group into per-set sub-groups for potential merging
                IEnumerable<IGrouping<string, VfsModelItem>> bySet = group.GroupBy(i => i.ImageRecord?.SetName ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                foreach (IGrouping<string, VfsModelItem> setGroup in bySet)
                {
                    // Check if the handler for this system merges duplicates
                    VfsModelItem firstItem = setGroup.FirstOrDefault();
                    bool handlerMerges = false;
                    if (firstItem != null)
                    {
                        try
                        {
                            IMountHandler handler = getHandlerForSystem(firstItem.System);
                            handlerMerges = handler.MergesDuplicates;
                        }
                        catch { }
                    }

                    if (handlerMerges)
                    {
                        // Merge eligible duplicates within the same set
                        List<VfsModelItem> appItems = setGroup.Where(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                                                        && string.Equals(i.System, VfsConstants.SystemWiiU, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (appItems.Count > 1)
                        {
                            VfsModelItem primary = appItems[0];
                            primary.MergedImageRecords = appItems.Select(i => i.ImageRecord).ToList();
                            // Sum merged image sizes for display
                            primary.ImageSize = appItems.Sum(i => i.ImageSize);
                            // Apply ID suffix to ALL items so filesystem views have
                            // unique names. Merged Image/AppFolder views use
                            // ImageRecord.Name (base name) instead.
                            foreach (VfsModelItem appItem in appItems)
                            {
                                mergedSecondaries.Add(appItem);
                                try
                                {
                                    string ext = appItem.ImageRecord.Format.GetFileExtension();
                                    string disambig = Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName(appItem.ImageRecord.Name, appItem.ImageRecord.Id);
                                    appItem.NameAsFolder = disambig;
                                    appItem.NameAsIso = disambig + ext;
                                }
                                catch { }
                            }
                            // Mark secondaries � the primary represents the merged group
                            for (int mi = 1; mi < appItems.Count; mi++)
                                appItems[mi].IsMergedSecondary = true;
                        }
                    }
                }

                // Disambiguate any remaining duplicates that weren't merged
                // (cross-set duplicates, non-APP formats, or handlers that don't merge)
                List<VfsModelItem> remaining = group.Where(i => !mergedSecondaries.Contains(i)).ToList();
                if (remaining.Count > 1)
                {
                    // Skip disambiguation when the group is a WiiU App/AppFolder + disc image pair.
                    // These are naturally distinct: one mounts as a folder, the other as an ISO file.
                    bool isAppPlusImagePair = remaining.Count == 2
                        && remaining.Any(i => i.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                                           || i.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder)
                        && remaining.Any(i => i.ImageRecord?.Format != NKitDataStore.ImageFormat.App
                                           && i.ImageRecord?.Format != NKitDataStore.ImageFormat.TmdAppFolder);

                    if (!isAppPlusImagePair)
                    {
                        int distinctSets = remaining.Select(i => i.ImageRecord?.SetName ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count();

                        foreach (VfsModelItem item in remaining)
                        {
                            try
                            {
                                string ext = item.ImageRecord.Format.GetFileExtension();

                                // Cross-set duplicates carry the set name too so the same title in
                                // different sets stays distinct; same-set duplicates use just the id.
                                // Both flow through the centralized {id} / {set_id} formatter so all
                                // virtual disambiguation shares one unambiguous marker.
                                string disambig = distinctSets > 1
                                    ? Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName(item.ImageRecord.Name, item.ImageRecord.SetName ?? string.Empty, item.ImageRecord.Id)
                                    : Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName(item.ImageRecord.Name, item.ImageRecord.Id);

                                item.NameAsFolder = disambig;
                                item.NameAsIso = disambig + ext;
                            }
                            catch
                            {
                                // If anything goes wrong, leave the original names intact
                            }
                        }
                    }
                }
            }

            // sw.Restart();

            // Build central lightweight mount index for fast lookups without triggering readers
            try
            {
                _indexBySystem.Clear();
                IEnumerable<string> systems = Images.Select(i => i.System).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (string system in systems)
                {
                    List<MountIndexEntry> entries = new List<MountIndexEntry>();
                    // Get mount specs (including any virtual named roots provided by the registry)
                    List<MountSpec> augmentedSpecs = _mountRegistry?.GetAugmentedMountSpecs(system, ShowImageFlag, ShowFolderFlag)
                                         ?? new List<MountSpec>(GetMountSpecsForSystem(system) ?? Enumerable.Empty<MountSpec>());

                    // Add named roots
                    List<MountSpec> namedRoots = augmentedSpecs.Where(s => !string.IsNullOrEmpty(s.Root)).ToList();
                    foreach (string rs in namedRoots.Select(s => s.Root).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        MountKind kindForRoot = namedRoots.First(s => string.Equals(s.Root, rs, StringComparison.OrdinalIgnoreCase)).Kind;
                        entries.Add(new MountIndexEntry { System = system, Root = rs, Name = rs, Kind = kindForRoot, Item = null });
                    }

                    // Add per-image entries under root=="" and also under any named roots that map to image/filesystem kinds
                    foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, system, StringComparison.OrdinalIgnoreCase)))
                    {
                        // TmdAppFolder and CueFolder images are image-mode concepts only; they must
                        // never appear in filesystem-kind listings (Req 10.4).
                        bool isTmdAppFolder = img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder;
                        bool isFolder = img.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi;

                        // When a TmdAppFolder exists for a base name, hide the individual
                        // [tmd.X] App images from Image-kind listings (the TmdAppFolder
                        // represents them). They still appear in Filesystem-kind listings.
                        bool isCoveredByTmdAppFolder = img.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                            && NKitDataStore.DataStore.IsTmdDisambiguatedName(img.ImageRecord.Name)
                            && tmdAppFolderBaseNames.Contains(NKitDataStore.DataStore.ExtractBaseName(img.ImageRecord.Name));

                        // Image-kind entry (iso) under root==""
                        // Skip merged secondaries for Image kind — the primary represents the group
                        // Skip [tmd.X] App images when a TmdAppFolder covers them
                        // Skip Image-kind entries entirely for the "Directories" system (folder-based storage has no ISO images)
                        bool isDirectoriesSystem = string.Equals(system, VfsConstants.SystemDirectories, StringComparison.OrdinalIgnoreCase);
                        if (ShowImageFlag && !isDirectoriesSystem && !img.IsMergedSecondary && !isCoveredByTmdAppFolder)
                        {
                            // Folder and TmdAppFolder images are presented as folders in
                            // image mode (routed through FolderMountHandler), not as .iso files.
                            if (isFolder || isTmdAppFolder)
                            {
                                string name = img.ImageRecord.Name;
                                MountIndexEntry existing = entries.FirstOrDefault(e => string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                                if (existing == null)
                                    entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.AppFolder, Item = img });
                                else if (existing.Kind == MountKind.Image)
                                {
                                    entries.Remove(existing);
                                    entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.AppFolder, Item = img });
                                }
                            }
                            else
                            {
                                // Merge primaries use the unsuffixed base name; the merge
                                // ensures uniqueness. Filesystem views use NameAsIso which
                                // carries the ID suffix for individual disambiguation.
                                string name = img.MergedImageRecords != null
                                    ? img.ImageRecord.Name
                                    : img.NameAsIso;
                                MountIndexEntry existing = entries.FirstOrDefault(e => string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                                if (existing == null)
                                    entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.Image, Item = img });
                            }
                        }

                        // Filesystem / AppFolder entry (folder) under root==""
                        // Merged AppFolder items (WiiU) appear as base name and skip secondaries.
                        // All other items (including secondaries) appear in the filesystem view.
                        // TmdAppFolder images are excluded from filesystem mode (Req 10.4).
                        if (ShowFolderFlag && !isTmdAppFolder)
                        {
                            if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App && augmentedSpecs.Any(s => string.IsNullOrEmpty(s.Root) && s.Kind == MountKind.AppFolder))
                            {
                                if (!img.IsMergedSecondary)
                                {
                                    string name = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                                    MountIndexEntry existing = entries.FirstOrDefault(e => string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                                    if (existing == null)
                                        entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.AppFolder, Item = img });
                                    else if (existing.Kind == MountKind.Image)
                                    {
                                        entries.Remove(existing);
                                        entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.AppFolder, Item = img });
                                    }
                                }
                            }
                            else
                            {
                                string name = img.NameAsFolder;
                                MountIndexEntry existing = entries.FirstOrDefault(e => string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                                if (existing == null)
                                    entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.FileSystem, Item = img });
                                else
                                {
                                    // prefer filesystem view over image when colliding
                                    if (existing.Kind == MountKind.Image)
                                    {
                                        entries.Remove(existing);
                                        entries.Add(new MountIndexEntry { System = system, Root = "", Name = name, Kind = MountKind.FileSystem, Item = img });
                                    }
                                }
                            }
                        }

                        // Also add entries for each named root that applies to this kind
                        foreach (MountSpec spec in augmentedSpecs.Where(s => !string.IsNullOrEmpty(s.Root)))
                        {
                            // Skip merged secondaries for Image/AppFolder kinds under named roots
                            // Skip [tmd.X] App images when a TmdAppFolder covers them
                            // Skip Image-kind entries entirely for the "Directories" system
                            if (spec.Kind == MountKind.Image && ShowImageFlag && !isDirectoriesSystem && !img.IsMergedSecondary && !isCoveredByTmdAppFolder)
                            {
                                // Folder and TmdAppFolder images appear as folders under
                                // the Images root (routed through FolderMountHandler).
                                if (isFolder || isTmdAppFolder)
                                {
                                    string name = img.ImageRecord.Name;
                                    if (!entries.Any(e => string.Equals(e.Root, spec.Root, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                                        entries.Add(new MountIndexEntry { System = system, Root = spec.Root, Name = name, Kind = MountKind.AppFolder, Item = img });
                                }
                                else
                                {
                                    string name = img.MergedImageRecords != null
                                        ? img.ImageRecord.Name
                                        : img.NameAsIso;
                                    if (!entries.Any(e => string.Equals(e.Root, spec.Root, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                                        entries.Add(new MountIndexEntry { System = system, Root = spec.Root, Name = name, Kind = MountKind.Image, Item = img });
                                }
                            }
                            else if (spec.Kind == MountKind.AppFolder && ShowFolderFlag && !img.IsMergedSecondary && !isTmdAppFolder)
                            {
                                string name = img.MergedImageRecords != null
                                    ? img.ImageRecord.Name
                                    : img.NameAsFolder;
                                if (!entries.Any(e => string.Equals(e.Root, spec.Root, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                                    entries.Add(new MountIndexEntry { System = system, Root = spec.Root, Name = name, Kind = MountKind.AppFolder, Item = img });
                            }
                            else if (spec.Kind == MountKind.FileSystem && ShowFolderFlag && !isTmdAppFolder)
                            {
                                string name = img.NameAsFolder;
                                if (!entries.Any(e => string.Equals(e.Root, spec.Root, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                                    entries.Add(new MountIndexEntry { System = system, Root = spec.Root, Name = name, Kind = MountKind.FileSystem, Item = img });
                            }
                        }
                    }

                    // If named roots exist for this system, do not keep per-image entries under the empty root
                    // to avoid exposing images/folders at the system root when virtual named roots (Images/Filesystems)
                    // are present.
                    if (entries.Any(e => !string.IsNullOrEmpty(e.Root)))
                    {
                        entries = entries.Where(e => !string.IsNullOrEmpty(e.Root) || string.IsNullOrEmpty(e.Root) == false).ToList();
                        // The above filter keeps only entries with non-empty Root. (Remove root=="" entries)
                        entries = entries.Where(e => !string.IsNullOrEmpty(e.Root)).ToList();
                    }

                    _indexBySystem[system] = entries;
                }
            }
            catch { }

            loadingSw.Stop();
            _imageLoadingMs = loadingSw.ElapsedMilliseconds;
        }

        private bool matchesSet(ImageRecord imageRecord) =>
            !imageRecord.Removed &&
            (string.IsNullOrWhiteSpace(SetName) ||
            string.Equals(imageRecord.SetName, SetName, StringComparison.OrdinalIgnoreCase));

        // Loader methods removed from model: handlers are responsible for lazy-loading filesystem.yaml and stored files.

        private bool showFolder => _showFileSystem || _showSystem;

        internal IDataStore DataStore => _dataStore;

        /// <summary>
        /// Gets the DataStore for a specific index (for multi-DataStore mount support).
        /// </summary>
        internal IDataStore GetDataStoreByIndex(int index)
        {
            if (index >= 0 && index < _allDataStores.Count)
                return _allDataStores[index].Store;
            return _dataStore;
        }

        // Expose flags for mount handlers
        internal bool ShowSystemFlag => _showSystem;
        internal bool ShowFileSystemFlag => _showFileSystem;
        internal bool ShowImageFlag => _showImage;
        internal bool ShowFolderFlag => _showFileSystem || _showSystem;

        private IMountHandler getHandlerForSystem(string systemName)
        {
            // Use MountRegistry to obtain (and cache) handlers so all callers get the
            // same handler instances and consistent behavior.
            try
            {
                return _mountRegistry?.GetHandlerForSystem(systemName, this) ?? (string.Equals(systemName, VfsConstants.SystemWiiU, StringComparison.OrdinalIgnoreCase) ? (IMountHandler)new WiiUMountHandler(this) : new BaseMountHandler(this));
            }
            catch
            {
                // Fallback to simple creation to avoid throwing during lookups
                if (string.Equals(systemName, VfsConstants.SystemWiiU, StringComparison.OrdinalIgnoreCase))
                    return new WiiUMountHandler(this);
                return new BaseMountHandler(this);
            }
        }

        // Create handler by mount kind
        private IMountHandler getHandlerForKind(string systemName, MountKind kind)
        {
            // Delegate to MountRegistry which caches handlers by kind/system.
            try
            {
                return _mountRegistry?.GetHandlerForKind(systemName, kind, this) ?? (kind == MountKind.FileSystem ? (IMountHandler)new FilesystemMountHandler(this) : getHandlerForSystem(systemName));
            }
            catch
            {
                if (kind == MountKind.FileSystem)
                    return new FilesystemMountHandler(this);
                return getHandlerForSystem(systemName);
            }
        }

        // Create handler by mount kind, taking image format into account for Folder/TmdAppFolder routing
        private IMountHandler getHandlerForKind(string systemName, MountKind kind, NKitDataStore.ImageFormat format)
        {
            try
            {
                return _mountRegistry?.GetHandlerForKind(systemName, kind, format, this) ?? getHandlerForKind(systemName, kind);
            }
            catch
            {
                return getHandlerForKind(systemName, kind);
            }
        }

        private MountRegistry _mountRegistry;
        internal MountRegistry GetMountRegistry() => _mountRegistry;

        public bool TryRenameImage(string oldName, string newName)
        {
            if (!UpdateMode) return false;

            string[] oldParts = oldName.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string[] newParts = newName.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (oldParts.Length < 2 || newParts.Length < 2)
                return false;

            string systemMatch = oldParts[0];
            string oldBase = oldParts[1];
            string newBase = newParts[1];

            VfsModelItem item = null;
            bool isAppMergedFolder = false;

            foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemMatch, StringComparison.OrdinalIgnoreCase)))
            {
                if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App)
                {
                    if (!img.IsMergedSecondary)
                    {
                        string name = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                        if (name.Equals(oldBase, StringComparison.OrdinalIgnoreCase))
                        {
                            item = img;
                            isAppMergedFolder = true;
                            break;
                        }
                    }
                }
                else
                {
                    if (img.NameAsIso.Equals(oldBase, StringComparison.OrdinalIgnoreCase))
                    {
                        item = img;
                        break;
                    }
                }
            }

            if (item == null)
                return false;

            // Strip extension for new name if it was an ISO
            string newImageName = newBase;
            if (!isAppMergedFolder)
            {
                string ext = item.ImageRecord.Format.GetFileExtension();
                if (newBase.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    newImageName = newBase.Substring(0, newBase.Length - ext.Length);
            }

            try
            {
                // Rename the whole TITLE via the set-aware primitive. It handles every case from a
                // single identified record: a plain image renames just itself; a WiiU multi-TMD title
                // (TmdAppFolder umbrella + "[tmd.N]" children) renames the umbrella to the new base AND
                // every child to "{base} [tmd.N]" (preserving each index) in one transaction, so the
                // mount recombine stays intact. This replaces the old per-part loop that wrote the same
                // base name to every child (dropping [tmd.N]) and the single-id path that renamed one
                // child but left its siblings + the umbrella stale.
                _dataStore.RenameImageTitle(new NKitDataStore.GlobalImageKey(item.ImageRecord.SetName, item.ImageRecord.Id), newImageName);

                // Refresh model
                LoadImages();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryDeleteImage(string name)
        {
            if (!UpdateMode) return false;

            string[] parts = name.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                return false;

            string systemMatch = parts[0];
            string baseName = parts[1];

            VfsModelItem item = null;
            bool isAppMergedFolder = false;

            foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemMatch, StringComparison.OrdinalIgnoreCase)))
            {
                if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App)
                {
                    if (!img.IsMergedSecondary)
                    {
                        string imgName = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                        if (imgName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            item = img;
                            isAppMergedFolder = true;
                            break;
                        }
                    }
                }
                else
                {
                    if (img.NameAsIso.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        item = img;
                        break;
                    }
                }
            }

            if (item == null)
                return false;

            try
            {
                if (isAppMergedFolder && item.MergedImageRecords != null)
                {
                    foreach (ImageRecord part in item.MergedImageRecords)
                    {
                        _dataStore.DeleteImage(new NKitDataStore.GlobalImageKey(part.SetName, part.Id));
                    }
                }
                else
                {
                    _dataStore.DeleteImage(new NKitDataStore.GlobalImageKey(item.ImageRecord.SetName, item.ImageRecord.Id));
                }

                // Refresh model
                LoadImages();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Returns true if the central index contains any named roots for the system
        internal bool HasNamedRoots(string systemName)
        {
            if (string.IsNullOrEmpty(systemName))
                return false;
            if (_indexBySystem.TryGetValue(systemName, out List<MountIndexEntry> list) && list != null)
                return list.Any(e => !string.IsNullOrEmpty(e.Root));
            return false;
        }

        // Central mount index: system -> list of entries for quick lookup without touching DB/readers
        private readonly Dictionary<string, List<MountIndexEntry>> _indexBySystem = new Dictionary<string, List<MountIndexEntry>>(StringComparer.OrdinalIgnoreCase);
        // Cached canonical mount-root and image-folder objects to keep Parents/Paths stable
        private readonly Dictionary<string, IFsFolder> _mountRootFolders = new Dictionary<string, IFsFolder>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IFsFolder> _imageFolderCache = new Dictionary<string, IFsFolder>(StringComparer.OrdinalIgnoreCase);

        // Helper to get the canonical system folder object created during construction
        private IFsFolder getSystemFolder(string systemName)
        {
            if (string.IsNullOrEmpty(systemName)) return null;
            return _root?.Folders?.FirstOrDefault(f => string.Equals(f.Name, systemName, StringComparison.OrdinalIgnoreCase));
        }

        // Expose canonical system folder to mount handlers (internal API)
        internal IFsFolder GetCanonicalSystemFolder(string systemName) => getSystemFolder(systemName);

        // Return or create a canonical mount-root folder for a system (e.g. "Images", "Filesystems").
        internal IFsFolder GetOrCreateMountRootFolder(string systemName, string rootName)
        {
            if (string.IsNullOrEmpty(systemName)) return null;
            IFsFolder systemFolder = getSystemFolder(systemName);
            if (string.IsNullOrEmpty(rootName) || systemFolder == null)
                return systemFolder;

            string key = systemName + "\0" + rootName;
            lock (_mountRootFolders)
            {
                if (_mountRootFolders.TryGetValue(key, out IFsFolder f) && f != null)
                    return f;
                FsFolder nf = new FsFolder() { Name = rootName, Parent = systemFolder };
                _mountRootFolders[key] = nf;
                return nf;
            }
        }

        // Return or create a canonical image-folder under an optional mount-root.
        internal IFsFolder GetOrCreateImageFolder(string systemName, string mountRoot, string imageName)
        {
            if (string.IsNullOrEmpty(systemName) || string.IsNullOrEmpty(imageName))
                return null;

            string key = systemName + "\0" + (mountRoot ?? "") + "\0" + imageName;
            lock (_imageFolderCache)
            {
                if (_imageFolderCache.TryGetValue(key, out IFsFolder f) && f != null)
                    return f;

                IFsFolder parent = string.IsNullOrEmpty(mountRoot) ? getSystemFolder(systemName) : GetOrCreateMountRootFolder(systemName, mountRoot);
                FsFolder nf = new FsFolder() { Name = imageName, Parent = parent };
                _imageFolderCache[key] = nf;
                return nf;
            }
        }

        private class MountIndexEntry
        {
            public string System { get; set; }
            public string Root { get; set; } // empty == direct under system
            public string Name { get; set; } // visible name (iso or folder or root name)
            public MountKind Kind { get; set; }
            public VfsModelItem Item { get; set; } // null for named roots
        }

        // Get the mount specs declared for a given system
        internal List<MountSpec> GetMountSpecsForSystem(string systemName) => _mountRegistry?.GetMountSpecsForSystem(systemName) ?? new List<MountSpec>();

        /// <summary>
        /// Resolve a mount entry from the central index without triggering any expensive loads.
        /// Returns the mount kind and the associated VfsModelItem (may be null for named roots).
        /// Also writes a small lookup trace to c:\temp\nkit_vfs_lookup.log for diagnostics.
        /// </summary>
        internal bool TryResolveMountEntry(string system, string root, string name, out MountKind kind, out VfsModelItem item)
        {
            kind = default;
            item = null;
            try
            {
                if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(name))
                    return false;

                if (!_indexBySystem.TryGetValue(system, out List<MountIndexEntry> list) || list == null || list.Count == 0)
                    return false;

                string r = root ?? string.Empty;

                MountIndexEntry entry = list.FirstOrDefault(e => string.Equals(e.Root ?? string.Empty, r, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

                if (entry == null && string.IsNullOrEmpty(r))
                    entry = list.FirstOrDefault(e => string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    kind = entry.Kind;
                    item = entry.Item;
                }

                return entry != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Central resolver that returns the exact index entry for the given system/root/name.
        /// Applies the rule: when a system has named roots (e.g., "Images"/"Filesystems"),
        /// do not resolve per-image entries under the system root (root==empty).
        /// </summary>
        private MountIndexEntry resolveMountEntry(string system, string root, string name)
        {
            if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(name))
                return null;

            if (!_indexBySystem.TryGetValue(system, out List<MountIndexEntry> list) || list == null || list.Count == 0)
                return null;

            string r = root ?? string.Empty;

            // If asking for the system root and named roots exist, only match named-root sentinel (Name==Root)
            if (string.IsNullOrEmpty(r) && HasNamedRoots(system))
            {
                MountIndexEntry rootEntry = list.FirstOrDefault(e => !string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                if (rootEntry != null)
                    return rootEntry;
                return null;
            }

            // Exact match for provided root
            MountIndexEntry entry = list.FirstOrDefault(e => string.Equals(e.Root ?? string.Empty, r, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
                return entry;

            // Fallback: when root is empty and no named roots exist, allow matching root==""
            if (string.IsNullOrEmpty(r))
                return list.FirstOrDefault(e => string.IsNullOrEmpty(e.Root) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        private IFsItem getItem(string path, out FsItemType type, out ImageRecord imageRecord)
        {
            string[] pth = path.Split(_separator);
            imageRecord = null;
            type = FsItemType.PreVfs;
            IFsItem itm = null;

            if (UpdateMode)
            {
                if (pth.Length <= 1 || (path.Length == 1 && path[0] == _separator))
                    return _root;
                else if (pth.Length == 2)
                {
                    return _root.Folders.FirstOrDefault(a => string.Equals(a.Name, pth[1], StringComparison.OrdinalIgnoreCase));
                }
                else if (pth.Length == 3)
                {
                    string systemName = pth[1];
                    string itemName = pth[2];

                    foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi)
                        {
                            if (!img.IsMergedSecondary)
                            {
                                string name = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                                if (name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    imageRecord = img.ImageRecord;
                                    type = FsItemType.FileSystemFs;
                                    return GetOrCreateImageFolder(img.System, null, name);
                                }
                            }
                        }
                        else
                        {
                            if (img.NameAsIso.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                            {
                                imageRecord = img.ImageRecord;
                                type = FsItemType.IsoFs;
                                return new FsImage() { Name = itemName, FsSize = img.ImageSize, Parent = getSystemFolder(systemName) };
                            }
                        }
                    }
                }
                else if (pth.Length == 4)
                {
                    string systemName = pth[1];
                    string itemName = pth[2];
                    string childName = pth[3];

                    foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi)
                        {
                            if (!img.IsMergedSecondary)
                            {
                                string name = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                                if (name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Use format-aware handler for Folder/TmdAppFolder routing
                                    NKitDataStore.ImageFormat fmt = img.ImageRecord?.Format ?? NKitDataStore.ImageFormat.Unknown;
                                    IMountHandler handler = (fmt == NKitDataStore.ImageFormat.Folder || fmt == NKitDataStore.ImageFormat.TmdAppFolder || fmt == NKitDataStore.ImageFormat.CueFolder)
                                        ? (_mountRegistry?.GetHandlerForFormat(fmt, img.System, this) ?? getHandlerForSystem(img.System))
                                        : getHandlerForSystem(img.System);
                                    IFsItem child = handler.FindChild(img, string.Empty, childName, out type, out imageRecord);
                                    if (child != null)
                                        return child;
                                }
                            }
                        }
                    }
                }
                else if (pth.Length > 4)
                {
                    string systemName = pth[1];
                    string itemName = pth[2];

                    foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi)
                        {
                            if (!img.IsMergedSecondary)
                            {
                                string name = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                                if (name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Use format-aware handler for Folder/TmdAppFolder routing
                                    NKitDataStore.ImageFormat fmt = img.ImageRecord?.Format ?? NKitDataStore.ImageFormat.Unknown;
                                    IMountHandler handler = (fmt == NKitDataStore.ImageFormat.Folder || fmt == NKitDataStore.ImageFormat.TmdAppFolder || fmt == NKitDataStore.ImageFormat.CueFolder)
                                        ? (_mountRegistry?.GetHandlerForFormat(fmt, img.System, this) ?? getHandlerForSystem(img.System))
                                        : getHandlerForSystem(img.System);
                                    IFsItem nodeItem = handler.FindInFolder(img, string.Empty, pth, 3, out type, out imageRecord);
                                    if (nodeItem != null)
                                        return nodeItem;
                                }
                            }
                        }
                    }
                }

                return null;
            }

            if (pth.Length <= 1 || (path.Length == 1 && path[0] == _separator)) // Root folder
                itm = _root;
            else if (pth.Length == 2) // System folder (e.g., GameCube, Wii)
                itm = _root.Folders.FirstOrDefault(a => a.Name == pth[1]);
            else if (pth.Length == 3)
            {
                // Image file (e.g., /GameCube/image.iso), image folder (e.g., /GameCube/imageName)
                // or a configured mount root (e.g., /WiiU/Images)
                string systemName = pth[1];
                string itemName = pth[2];

                // Use central index for quick lookup of named roots and image/folder entries
                IFsFolder systemFolder = getSystemFolder(systemName);
                MountIndexEntry entry = resolveMountEntry(systemName, null, itemName);
                if (entry != null)
                {
                    try
                    {
                        string action = string.Empty;

                        if (!string.IsNullOrEmpty(entry.Root))
                        {
                            if (entry.Item == null)
                            {
                                // Named root (Images/Filesystems)
                                type = FsItemType.FileSystemFs;
                                imageRecord = null;
                                action = "ReturnNamedRoot";
                                return GetOrCreateMountRootFolder(systemName, itemName);
                            }
                            else
                            {
                                // The entry exists but is mapped under a named root (e.g., Filesystems/Name)
                                // Do not expose the image/folder at the system root when named roots are present.
                                action = "MappedUnderNamedRoot";
                                return null;
                            }
                        }

                        if (string.IsNullOrEmpty(entry.Root))
                        {
                            if (entry.Kind == MountKind.Image && entry.Item != null)
                            {
                                type = FsItemType.IsoFs;
                                imageRecord = entry.Item.ImageRecord;
                                action = "ReturnIso";
                                return new FsImage() { Name = itemName, FsSize = entry.Item.ImageSize, Parent = systemFolder };
                            }
                            else if ((entry.Kind == MountKind.FileSystem || entry.Kind == MountKind.AppFolder) && entry.Item != null)
                            {
                                type = FsItemType.FileSystemFs;
                                imageRecord = entry.Item.ImageRecord;
                                action = "ReturnFolderFromIndex";
                                return GetOrCreateImageFolder(systemName, null, itemName);
                            }
                        }
                    }
                    catch { }
                }

                // Fallback: legacy linear search
                // Try as image file first (directly under system)
                if (_showImage)
                {
                    VfsModelItem image = Images.FirstOrDefault(i =>
                        i.System == systemName &&
                        i.NameAsIso.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                    if (image != null)
                    {
                        type = FsItemType.IsoFs;
                        imageRecord = image.ImageRecord;
                        return new FsImage() { Name = itemName, FsSize = image.ImageSize, Parent = systemFolder };
                    }
                }

                // Try as image folder (directly under system)
                if (showFolder)
                {
                    VfsModelItem folderImage = Images.FirstOrDefault(i =>
                        i.System == systemName &&
                        i.NameAsFolder.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                    if (folderImage != null)
                    {
                        type = FsItemType.FileSystemFs;
                        imageRecord = folderImage.ImageRecord;
                        return new FsFolder() { Name = itemName, Parent = systemFolder };
                    }
                }
            }
            else if (pth.Length >= 4)
            {
                // Filesystem tree or stored file navigation: \System\[Root\]imageName\item\...
                string systemName = pth[1];

                // Determine if a mount root segment is present at pth[2]
                List<MountSpec> augmentedSpecsForSystem = _mountRegistry?.GetAugmentedMountSpecs(systemName, ShowImageFlag, ShowFolderFlag)
                                               ?? new List<MountSpec>(GetMountSpecsForSystem(systemName) ?? Enumerable.Empty<MountSpec>());
                string rootSegment = null;
                int imageNameIndex = 2;
                if (pth.Length > 2 && augmentedSpecsForSystem.Any(s => !string.IsNullOrEmpty(s.Root) && string.Equals(s.Root, pth[2], StringComparison.OrdinalIgnoreCase)))
                {
                    rootSegment = pth[2];
                    imageNameIndex = 3;
                }

                if (pth.Length <= imageNameIndex)
                    return null;

                string folderName = pth[imageNameIndex];

                // Resolve the image item using the central index when possible. This
                // handles named roots (Images -> NameAsIso, Filesystems -> NameAsFolder)
                VfsModelItem folderImage = null;
                MountIndexEntry resolvedEntry = null;
                MountKind? resolvedKind = null;
                try
                {
                    resolvedEntry = resolveMountEntry(systemName, rootSegment, folderName);
                    if (resolvedEntry != null && resolvedEntry.Item != null)
                    {
                        folderImage = resolvedEntry.Item;
                        resolvedKind = resolvedEntry.Kind;
                    }
                }
                catch { }

                // Fallback: try folder-name lookup (legacy behavior)
                if (folderImage == null)
                {
                    folderImage = Images.FirstOrDefault(i =>
                        i.System == systemName &&
                        i.NameAsFolder.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                }

                // If the path points directly to the image/folder itself (no child segment),
                // return the appropriate Fs item so selecting entries under named roots works.
                if (folderImage != null && pth.Length == imageNameIndex + 1)
                {
                    // Determine kind: prefer the resolved index kind when available,
                    // otherwise infer from mount specs for the root, default to FileSystem.
                    MountKind kindToReturn = resolvedKind ?? MountKind.FileSystem;
                    if (resolvedKind == null && !string.IsNullOrEmpty(rootSegment))
                    {
                        MountSpec specForRoot = augmentedSpecsForSystem.FirstOrDefault(s => string.Equals(s.Root ?? string.Empty, rootSegment ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                        if (specForRoot != null)
                            kindToReturn = specForRoot.Kind;
                    }

                    // Special-case: APP-format, Folder, and TmdAppFolder images are indexed as Image-kind
                    // under the "Images" root, but they should be presented as folders so their
                    // areas/stored files/filesystem.yaml contents can be listed.
                    if (kindToReturn == MountKind.Image && (folderImage?.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                        || folderImage?.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                        || folderImage?.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                        || folderImage?.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                        || folderImage?.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                        || folderImage?.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi))
                        kindToReturn = MountKind.AppFolder;

                    if (kindToReturn == MountKind.Image)
                    {
                        type = FsItemType.IsoFs;
                        imageRecord = folderImage.ImageRecord;
                        return new FsImage() { Name = folderName, FsSize = folderImage.ImageSize, Parent = GetOrCreateMountRootFolder(systemName, rootSegment) };
                    }
                    else // FileSystem or AppFolder -> return folder view
                    {
                        type = FsItemType.FileSystemFs;
                        imageRecord = folderImage.ImageRecord;
                        return GetOrCreateImageFolder(systemName, rootSegment, folderName);
                    }
                }

                if (folderImage != null)
                {
                    // Check stored files first (flat stored files are one segment after the image name)
                    // Skip for merged items � the mount handler's FindChild correctly tracks the
                    // source image record per stored file so ReadFile uses the right GlobalImageKey.
                    if (_showSystem && pth.Length == imageNameIndex + 2 && folderImage.MergedImageRecords == null)
                    {
                        _mountRegistry.LoadStoredFiles(folderImage, this);
                        if (folderImage.StoredFiles != null)
                        {
                            StoredFileEntry stored = folderImage.StoredFiles.FirstOrDefault(
                                f => f.Name.Equals(pth[imageNameIndex + 1], StringComparison.OrdinalIgnoreCase));
                            if (stored != null)
                            {
                                type = FsItemType.StoredFileFs;
                                imageRecord = folderImage.ImageRecord;
                                return new FsStoredFile { Name = stored.Name, FsSize = stored.Size, StoredFileName = stored.StoredFileName ?? stored.Name };
                            }
                        }
                    }

                    // Delegate APP-area single-child and filesystem resolution to mount handlers
                    // Use augmented specs so virtual roots (Images/Filesystems) are considered
                    List<MountSpec> applicableSpecs = augmentedSpecsForSystem.Where(s => string.Equals(s.Root ?? string.Empty, rootSegment ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();

                    // Determine image format for format-aware handler routing
                    NKitDataStore.ImageFormat imgFormat = folderImage.ImageRecord?.Format ?? NKitDataStore.ImageFormat.Unknown;

                    // Single child under image (could be APP area or a file/folder)
                    if (pth.Length == imageNameIndex + 2)
                    {
                        foreach (MountSpec spec in applicableSpecs)
                        {
                            IMountHandler handler = getHandlerForKind(systemName, spec.Kind, imgFormat);
                            IFsItem child = handler.FindChild(folderImage, spec.Root, pth[imageNameIndex + 1], out type, out imageRecord);
                            if (child != null)
                                return child;
                        }
                    }

                    // Deeper filesystem path - ask handlers to resolve the node
                    int startIndex = imageNameIndex + 1;
                    foreach (MountSpec spec in applicableSpecs)
                    {
                        IMountHandler handler = getHandlerForKind(systemName, spec.Kind, imgFormat);
                        IFsItem nodeItem = handler.FindInFolder(folderImage, spec.Root, pth, startIndex, out type, out imageRecord);
                        if (nodeItem != null)
                            return nodeItem;
                    }
                }
            }

            return itm;
        }

        // moved to FilesystemMountHandler

        VfsContext IVfsProvider.GetScanFsItemAsContext(string path) => GetScanFsItemAsContext(path);

        internal VfsContext GetScanFsItemAsContext(string path)
        {
            FsItemType type;
            ImageRecord imageRecord;

            if (path.EndsWith("\\*"))
                path = path.Substring(0, path.Length - 2);

            IFsItem itm = getItem(path, out type, out imageRecord);

            if (itm == null)
                return null;
            VfsContext ctx = new VfsContext()
            {
                ImageRecord = imageRecord,
                Type = type,
                VfsFullName = path,
                DataStore = _dataStore,
                FsItem = itm,
                DataStoreIndex = imageRecord != null
                    ? (Images.FirstOrDefault(i => i.ImageRecord == imageRecord)?.DataStoreIndex ?? 0)
                    : 0
            };

            try
            {
                // Delegate stream creation to the mount handler to keep platform-specific
                // logic out of the VFS core and avoid reflection. Handlers return true
                // when they successfully create a stream and an associated reader.
                //
                // EXCEPTION: ifs entries (FsIfsFileItem) are deferred to VfsStreamHelper.EnsureStream()
                // so that CreateFile returns instantly during directory listing. The expensive
                // child-image open + area query + crypto stream setup only happens on first Read.
                if (imageRecord != null && itm is IFsFile fsItem && !(itm is FsIfsFileItem))
                {
                    try
                    {
                        // Use format-aware handler routing so Folder and TmdAppFolder
                        // images are handled by FolderMountHandler regardless of system.
                        IMountHandler handler = _mountRegistry?.GetHandlerForFormat(imageRecord.Format, imageRecord.System, this)
                            ?? getHandlerForSystem(imageRecord.System);
                        // Find the VfsModelItem corresponding to this imageRecord so handlers can access image metadata
                        VfsModelItem folderImage = Images.FirstOrDefault(ii => ii.ImageRecord != null && ii.ImageRecord.Id == imageRecord.Id && string.Equals(ii.ImageRecord.SetName, imageRecord.SetName, StringComparison.OrdinalIgnoreCase));
                        if (handler != null && handler.TryCreateAreaStream(folderImage, fsItem, out Stream stream, out IImageReader imgReader))
                        {
                            ctx.FileStream = stream;
                            ctx.ImageReader = imgReader;
                            // When imgReader is null, the handler used shared resources from the manager.
                            // Track the image for CloseContext to release.
                            if (imgReader == null && imageRecord != null)
                            {
                                ctx.ResourceSetName = imageRecord.SetName;
                                ctx.ResourceImageId = imageRecord.Id;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return ctx;
        }

        public IFsItem GetScanFsItem(string path, char separator)
        {
            FsItemType type;
            ImageRecord imageRecord;

            if (path.EndsWith("\\*"))
                path = path.Substring(0, path.Length - 2);

            IFsItem itm = getItem(path, out type, out imageRecord);
            return itm; // Return the item found
        }

        public IEnumerable<IFsItem> GetScanFsItems(string path, string mask, char separator)
        {
            if (UpdateMode)
            {
                string[] pth = path.Split(_separator);
                if (pth.Length == 1 || (pth.Length == 2 && string.IsNullOrEmpty(pth[1])))
                {
                    foreach (IFsItem i in _root.Folders.Where(a => BaseMountHandler.NameMatches(mask, a.Name)))
                        yield return i;
                }
                else if (pth.Length == 2)
                {
                    string systemName = pth[1];
                    foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (img.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                            || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi)
                        {
                            if (!img.IsMergedSecondary)
                            {
                                string name = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                                if (BaseMountHandler.NameMatches(mask, name))
                                    yield return GetOrCreateImageFolder(img.System, null, name);
                            }
                        }
                        else
                        {
                            if (BaseMountHandler.NameMatches(mask, img.NameAsIso))
                                yield return new FsImage() { Name = img.NameAsIso, FsSize = img.ImageSize, Parent = getSystemFolder(systemName) };
                        }
                    }
                }
                else if (pth.Length >= 3)
                {
                    string systemName = pth[1];
                    string itemName = pth[2];

                    FsItemType localType;
                    ImageRecord localImageRecord;
                    IFsItem localItm = getItem(path, out localType, out localImageRecord);

                    if (localItm != null)
                    {
                        foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemName, StringComparison.OrdinalIgnoreCase)))
                        {
                            bool isFolderFormat = img.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                                || img.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                                || img.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                                || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                                || img.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi;

                            if ((img.ImageRecord?.Format == NKitDataStore.ImageFormat.App || isFolderFormat) && !img.IsMergedSecondary)
                            {
                                string folderName = img.MergedImageRecords != null ? img.ImageRecord.Name : img.NameAsFolder;
                                if (folderName.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Use format-aware handler for Folder/TmdAppFolder routing
                                    NKitDataStore.ImageFormat fmt = img.ImageRecord?.Format ?? NKitDataStore.ImageFormat.Unknown;
                                    IMountHandler handler = isFolderFormat
                                        ? (_mountRegistry?.GetHandlerForFormat(fmt, img.System, this) ?? getHandlerForSystem(img.System))
                                        : getHandlerForSystem(img.System);

                                    if (pth.Length == 3)
                                    {
                                        foreach (IFsItem child in handler.ListFolder(img, string.Empty, mask))
                                            yield return child;
                                    }
                                    else if (pth.Length >= 4)
                                    {
                                        IFsItem nodeItem = pth.Length == 4
                                            ? handler.FindChild(img, string.Empty, pth[3], out _, out _)
                                            : handler.FindInFolder(img, string.Empty, pth, 3, out _, out _);

                                        if (nodeItem is IFsFolder nodeFolder)
                                        {
                                            if (nodeFolder.Folders != null)
                                            {
                                                foreach (IFsFolder child in nodeFolder.Folders.Where(c => BaseMountHandler.NameMatches(mask, c.Name)))
                                                    yield return child;
                                            }
                                            if (nodeFolder.Files != null)
                                            {
                                                foreach (IFsFile child in nodeFolder.Files.Where(c => BaseMountHandler.NameMatches(mask, (c as IFsFile)?.Name ?? c.ToString())))
                                                    yield return child;
                                            }
                                        }
                                        else if (nodeItem is IFsFile nodeFile && BaseMountHandler.NameMatches(mask, nodeFile.Name))
                                        {
                                            yield return nodeFile;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                yield break;
            }

            FsItemType type;
            ImageRecord imageRecord;
            IFsItem itm = getItem(path, out type, out imageRecord);

            if (itm != null)
            {
                string[] pth = path.Split(_separator);

                if (pth.Length == 1 || (pth.Length == 2 && string.IsNullOrEmpty(pth[1])))
                {
                    // Root - list system folders
                    foreach (IFsItem i in _root.Folders.Where(a => BaseMountHandler.NameMatches(mask, a.Name)))
                        yield return i;
                }
                else if (pth.Length == 2)
                {
                    // System folder - list configured mount roots and items directly under system (root == "")
                    string systemName = pth[1];
                    IFsFolder systemFolder = getSystemFolder(systemName);

                    // First, list configured roots (e.g., "Images", "Apps").
                    // Augment mount specs with virtual "Images"/"Filesystems" roots when both views are enabled
                    List<MountSpec> augmentedSpecs = _mountRegistry?.GetAugmentedMountSpecs(systemName, ShowImageFlag, ShowFolderFlag)
                                         ?? new List<MountSpec>(GetMountSpecsForSystem(systemName) ?? Enumerable.Empty<MountSpec>());

                    try
                    {
                        string roots = string.Join(",", augmentedSpecs.Where(s => !string.IsNullOrEmpty(s.Root)).Select(s => s.Root).Distinct(StringComparer.OrdinalIgnoreCase));
                        //string msg = $"[VfsModel] GetScanFsItems system={systemName} augmentedRoots={roots} showImage={ShowImageFlag} showFolder={ShowFolderFlag}";
                        //try { Trace.WriteLine(msg); } catch { }
                    }
                    catch { }

                    // Prefer using the central index if available
                    if (_indexBySystem.TryGetValue(systemName, out List<MountIndexEntry> indexList))
                    {
                        // Only select the mount-root names (the Root field) so we don't
                        // accidentally return per-image entries that also have Root != "".
                        IEnumerable<string> namedRoots = indexList.Where(e => !string.IsNullOrEmpty(e.Root)).Select(e => e.Root).Distinct(StringComparer.OrdinalIgnoreCase);
                        foreach (string rootSpec in namedRoots)
                        {
                            if (BaseMountHandler.NameMatches(mask, rootSpec))
                                yield return GetOrCreateMountRootFolder(systemName, rootSpec);
                        }
                        if (namedRoots.Any())
                            yield break;
                    }
                    else
                    {
                        foreach (string rootSpec in augmentedSpecs.Where(s => !string.IsNullOrEmpty(s.Root)).Select(s => s.Root).Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            if (BaseMountHandler.NameMatches(mask, rootSpec))
                                yield return GetOrCreateMountRootFolder(systemName, rootSpec);
                        }

                        if (augmentedSpecs.Any(s => !string.IsNullOrEmpty(s.Root)))
                            yield break;
                    }

                    // Next, list items that are mounted directly under the system (Root == "")
                    // Call each mount-kind's handler once (avoid duplicate results when multiple specs share root=="")
                    // Collect root items from each applicable mount kind, then de-duplicate
                    // so we yield at most one entry per image. Preference: filesystem/appfolder
                    // entries win over image entries when both are available for the same name.
                    List<MountSpec> rootEmptySpecs = augmentedSpecs.Where(s => string.IsNullOrEmpty(s.Root)).ToList();
                    HashSet<MountKind> handledKinds = new HashSet<MountKind>();

                    // Temporary map of name -> (item, kind)
                    Dictionary<string, (IFsItem Item, MountKind Kind)> collected = new Dictionary<string, (IFsItem Item, MountKind Kind)>(StringComparer.OrdinalIgnoreCase);

                    foreach (MountSpec spec in rootEmptySpecs)
                    {
                        if (!handledKinds.Add(spec.Kind))
                            continue;

                        // Respect model view flags: skip kinds that aren't requested
                        if (spec.Kind == MountKind.Image && !ShowImageFlag)
                            continue;
                        if ((spec.Kind == MountKind.FileSystem || spec.Kind == MountKind.AppFolder) && !ShowFolderFlag)
                            continue;

                        IMountHandler handlerForKind = getHandlerForKind(systemName, spec.Kind);
                        foreach (IFsItem item in handlerForKind.ListRoot(systemName, spec.Root, mask))
                        {
                            string name = item is IFsFile f ? f.Name : (item is IFsFolder fo ? fo.Name : item.ToString());
                            if (string.IsNullOrEmpty(name))
                                continue;

                            if (collected.TryGetValue(name, out (IFsItem Item, MountKind Kind) existing))
                            {
                                // If existing is Image and new is FileSystem/AppFolder, replace (prefer folder view)
                                if ((existing.Kind == MountKind.Image) && (spec.Kind == MountKind.FileSystem || spec.Kind == MountKind.AppFolder))
                                    collected[name] = (item, spec.Kind);
                                // otherwise keep the first (preserve priority/order)
                            }
                            else
                                collected[name] = (item, spec.Kind);
                        }
                    }

                    // If nothing was collected (handlers returned no root items),
                    // fall back to system handler behavior to avoid empty listings.
                    if (collected.Count == 0)
                    {
                        IMountHandler fallback = getHandlerForSystem(systemName);
                        foreach (IFsItem it in fallback.ListRoot(systemName, string.Empty, mask))
                            yield return it;
                        yield break;
                    }

                    // Yield items in canonical image order so listing is predictable
                    foreach (VfsModelItem img in Images.Where(i => string.Equals(i.System, systemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Defensive: if this system has named roots, avoid yielding per-image items at system root
                        // when the central index maps them under a named root (Images/Filesystems).
                        if (HasNamedRoots(systemName))
                        {
                            try
                            {
                                // If the image is mapped under any named root (provided by the registry), skip yielding here
                                List<MountSpec> augmented = _mountRegistry?.GetAugmentedMountSpecs(systemName, ShowImageFlag, ShowFolderFlag)
                                                ?? GetMountSpecsForSystem(systemName);

                                bool mapped = false;
                                if (ShowImageFlag)
                                {
                                    foreach (string rs in augmented.Where(s => s.Kind == MountKind.Image && !string.IsNullOrEmpty(s.Root)).Select(s => s.Root).Distinct(StringComparer.OrdinalIgnoreCase))
                                    {
                                        if (resolveMountEntry(systemName, rs, img.NameAsIso) != null)
                                            mapped = true; break;
                                    }
                                }

                                if (!mapped && ShowFolderFlag)
                                {
                                    foreach (string rs in augmented.Where(s => (s.Kind == MountKind.FileSystem || s.Kind == MountKind.AppFolder) && !string.IsNullOrEmpty(s.Root)).Select(s => s.Root).Distinct(StringComparer.OrdinalIgnoreCase))
                                    {
                                        if (resolveMountEntry(systemName, rs, img.NameAsFolder) != null)
                                            mapped = true; break;
                                    }
                                }

                                if (mapped)
                                    continue;
                            }
                            catch { }
                        }

                        // Prefer folder name first, then iso name
                        if (collected.TryGetValue(img.NameAsFolder, out (IFsItem Item, MountKind Kind) v1))
                            yield return v1.Item;
                        else if (collected.TryGetValue(img.NameAsIso, out (IFsItem Item, MountKind Kind) v2))
                            yield return v2.Item;
                    }
                }
                else if ((pth.Length == 3 || pth.Length >= 4) && type == FsItemType.FileSystemFs)
                {
                    // Image folder or deeper path - delegate listing to mount handlers based on mount specs
                    string systemName = pth[1];
                    IFsFolder systemFolder = getSystemFolder(systemName);

                    // Detect optional root segment and image name index
                    List<MountSpec> specsForSystem = GetMountSpecsForSystem(systemName);
                    List<MountSpec> augmentedSpecsForSystem = new List<MountSpec>(specsForSystem ?? Enumerable.Empty<MountSpec>());
                    if (ShowImageFlag && !augmentedSpecsForSystem.Any(s => !string.IsNullOrEmpty(s.Root) && string.Equals(s.Root, VfsConstants.RootImages, StringComparison.OrdinalIgnoreCase)))
                        augmentedSpecsForSystem.Add(new MountSpec { Kind = MountKind.Image, Root = VfsConstants.RootImages });
                    if (ShowFolderFlag && !augmentedSpecsForSystem.Any(s => !string.IsNullOrEmpty(s.Root) && string.Equals(s.Root, VfsConstants.RootFilesystems, StringComparison.OrdinalIgnoreCase)))
                        augmentedSpecsForSystem.Add(new MountSpec { Kind = MountKind.FileSystem, Root = VfsConstants.RootFilesystems });
                    string rootSegment = null;
                    int imageNameIndex = 2;
                    if (pth.Length > 2 && augmentedSpecsForSystem.Any(s => !string.IsNullOrEmpty(s.Root) && string.Equals(s.Root, pth[2], StringComparison.OrdinalIgnoreCase)))
                    {
                        rootSegment = pth[2];
                        imageNameIndex = 3;
                    }

                    if (pth.Length < imageNameIndex)
                        yield break;

                    // If listing directly under the mount-root (e.g. /WiiU/Images or /WiiU/Filesystems)
                    // we should enumerate items for the matching mount kinds. Collect items from
                    // each applicable handler and yield filtered results (images or folders) to
                    // match the intended root semantics.
                    if (pth.Length == imageNameIndex)
                    {
                        List<MountSpec> applicableSpecsRoot = augmentedSpecsForSystem.Where(s => string.Equals(s.Root ?? string.Empty, rootSegment ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();

                        // If we have a central index, prefer it for listing items under the mount-root
                        if (_indexBySystem.TryGetValue(systemName, out List<MountIndexEntry> indexList))
                        {
                            List<MountIndexEntry> entries = indexList.Where(e => string.Equals(e.Root ?? string.Empty, rootSegment ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();
                            // Exclude the sentinel named-root entry (where Name == Root) so the root folder
                            // does not list itself as a child inside the same folder.
                            if (!string.IsNullOrEmpty(rootSegment))
                                entries = entries.Where(e => !string.Equals(e.Name, rootSegment, StringComparison.OrdinalIgnoreCase)).ToList();
                            HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            // mountRootFolder is the parent to expose for items listed under this mount-root
                            IFsFolder mountRootFolder = GetOrCreateMountRootFolder(systemName, rootSegment);

                            foreach (MountSpec spec in applicableSpecsRoot)
                            {
                                foreach (MountIndexEntry entry in entries.Where(e => e.Kind == spec.Kind
                                        || (spec.Kind == MountKind.FileSystem && e.Kind == MountKind.AppFolder)
                                        || (spec.Kind == MountKind.Image && e.Kind == MountKind.AppFolder && ShowFolderFlag)))
                                {
                                    string name = entry.Name;
                                    if (string.IsNullOrEmpty(name) || yielded.Contains(name) || !BaseMountHandler.NameMatches(mask, name))
                                        continue;

                                    if (entry.Kind == MountKind.Image)
                                    {
                                        if (entry.Item != null)
                                        {
                                            // For APP-format, Folder, and TmdAppFolder images,
                                            // prefer exposing a folder so contents can be listed inside.
                                            if (entry.Item.ImageRecord?.Format == NKitDataStore.ImageFormat.App
                                                || entry.Item.ImageRecord?.Format == NKitDataStore.ImageFormat.Folder
                                                || entry.Item.ImageRecord?.Format == NKitDataStore.ImageFormat.TmdAppFolder
                                                || entry.Item.ImageRecord?.Format == NKitDataStore.ImageFormat.CueFolder
                                                || entry.Item.ImageRecord?.Format == NKitDataStore.ImageFormat.Cue
                                                || entry.Item.ImageRecord?.Format == NKitDataStore.ImageFormat.Gdi)
                                            {
                                                string folderDisplayName = entry.Item.MergedImageRecords != null
                                                    ? entry.Item.ImageRecord.Name
                                                    : entry.Item.NameAsFolder;
                                                yielded.Add(name);
                                                yield return GetOrCreateImageFolder(systemName, rootSegment, folderDisplayName);
                                            }
                                            else
                                            {
                                                yielded.Add(name);
                                                yield return new FsImage() { Name = name, FsSize = entry.Item.ImageSize, Parent = mountRootFolder };
                                            }
                                        }
                                    }
                                    else if (entry.Kind == MountKind.FileSystem || entry.Kind == MountKind.AppFolder)
                                    {
                                        // FileSystem views always use NameAsFolder (carries ID suffix
                                        // for individual disambiguation). AppFolder merged primaries
                                        // use the clean base name since the merge ensures uniqueness.
                                        string folderDisplayName = entry.Kind == MountKind.AppFolder && entry.Item.MergedImageRecords != null
                                            ? entry.Item.ImageRecord.Name
                                            : entry.Item.NameAsFolder;
                                        yielded.Add(name);
                                        yield return GetOrCreateImageFolder(systemName, rootSegment, folderDisplayName);
                                    }
                                }
                            }

                            yield break;
                        }

                        // Fallback to handler-based listing when index not available
                        HashSet<string> yieldedFallback = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (MountSpec spec in applicableSpecsRoot)
                        {
                            IMountHandler handler = getHandlerForKind(systemName, spec.Kind);
                            foreach (IFsItem item in handler.ListRoot(systemName, string.Empty, mask))
                            {
                                string name = item is IFsFile f ? f.Name : (item is IFsFolder fo ? fo.Name : item.ToString());
                                if (string.IsNullOrEmpty(name) || yieldedFallback.Contains(name))
                                    continue;

                                if (spec.Kind == MountKind.Image && item is FsImage)
                                {
                                    yieldedFallback.Add(name);
                                    yield return item;
                                }
                                else if (spec.Kind == MountKind.FileSystem && item is FsFolder)
                                {
                                    yieldedFallback.Add(name);
                                    yield return item;
                                }
                                else if (spec.Kind == MountKind.AppFolder)
                                {
                                    yieldedFallback.Add(name);
                                    yield return item;
                                }
                            }
                        }

                        yield break;
                    }

                    string folderName = pth[imageNameIndex];

                    // Try mount index first - merged primaries use a clean base name
                    // that differs from the suffixed NameAsFolder.
                    VfsModelItem folderImage = null;
                    MountIndexEntry resolvedListEntry = resolveMountEntry(systemName, rootSegment, folderName);
                    if (resolvedListEntry?.Item != null)
                        folderImage = resolvedListEntry.Item;

                    // Fallback: legacy NameAsFolder lookup
                    folderImage ??= Images.FirstOrDefault(i =>
                        i.System == systemName &&
                        i.NameAsFolder.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                    if (folderImage == null)
                        yield break;

                    HashSet<string> yieldedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Select specs that apply to this root segment (null => root == "")
                    List<MountSpec> applicableSpecs = augmentedSpecsForSystem.Where(s => string.Equals(s.Root ?? string.Empty, rootSegment ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();

                    // Determine image format for format-aware handler routing
                    NKitDataStore.ImageFormat folderImgFormat = folderImage.ImageRecord?.Format ?? NKitDataStore.ImageFormat.Unknown;

                    foreach (MountSpec spec in applicableSpecs)
                    {
                        IMountHandler handler = getHandlerForKind(systemName, spec.Kind, folderImgFormat);

                        if (pth.Length == imageNameIndex + 1)
                        {
                            // Listing directly under image -> call ListFolder
                            foreach (IFsItem item in handler.ListFolder(folderImage, spec.Root, mask))
                            {
                                string name = item is IFsFile f ? f.Name : (item is IFsFolder fo ? fo.Name : item.ToString());
                                if (string.IsNullOrEmpty(name))
                                    continue;
                                if (yieldedNames.Contains(name))
                                    continue;
                                yieldedNames.Add(name);
                                yield return item;
                            }
                        }
                        else
                        {
                            // Deeper path - resolve the node and enumerate children if it's a folder
                            int startIndex = imageNameIndex + 1;
                            IFsItem nodeItem = handler.FindInFolder(folderImage, spec.Root, pth, startIndex, out FsItemType foundType, out ImageRecord foundImageRecord);
                            if (nodeItem is IFsFolder nodeFolder)
                            {
                                foreach (IFsFolder child in nodeFolder.Folders.Where(c => BaseMountHandler.NameMatches(mask, c.Name)))
                                {
                                    // Filter system folders when not in system mode
                                    if (!ShowSystemFlag && child is NkFsFolderItem nkFsFolder && nkFsFolder.IsSystem)
                                        continue;
                                    if (yieldedNames.Add(child.ToString()))
                                        yield return child;
                                }
                                foreach (IFsFile file in nodeFolder.Files.Where(c => BaseMountHandler.NameMatches(mask, (c as IFsFile)?.Name ?? c.ToString())))
                                {
                                    // Filter system files when not in system mode
                                    if (!ShowSystemFlag && (file as IFsFile)?.IsSystemFile == true)
                                        continue;
                                    string name = (file as IFsFile)?.Name ?? file.ToString();
                                    if (yieldedNames.Add(name))
                                        yield return file;
                                }
                            }
                            else if (nodeItem is IFsFile nodeFile)
                            {
                                string name = nodeFile.Name;
                                if (!string.IsNullOrEmpty(name) && !yieldedNames.Contains(name) && BaseMountHandler.NameMatches(mask, name))
                                {
                                    yield return nodeFile;
                                }
                            }
                        }
                    }
                }
            }
        }



        public void Dispose()
        {
            // Shutdown all resource managers and dispose all DataStores
            foreach ((IDataStore store, IMountResourceManager resources) in _allDataStores)
            {
                try { resources.Shutdown(); } catch { }
                try { store.Dispose(); } catch { }
            }

            // Disable mount debug after DataStore is fully disposed
            NKitDataStore.DataStore.MountDebug = false;
        }

        // Moved Fs* wrapper types to dedicated files to reduce VfsModel size.

        internal class FsImage : IFsFile
        {
            public bool IsMissing => false;
            public bool IsLastFile => false;

            public string FullName
            {
                get
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(Path))
                            return Path + "/" + (Name ?? "");
                        if (Parent != null)
                            return (Parent.Path ?? "") + "/" + (Name ?? "");
                    }
                    catch { }
                    return Name ?? "";
                }
            }

            public long FsSize { get; set; }

            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }

            public bool IsSystemFile => false;

            public long FsOffset { get; set; }

            public long PostGapSize { get; set; }

            public long PostGapFsOffset { get; set; }

            public int SplitIndex { get; set; }

            public IFsFileParts SplitParts => null;

            public string Name { get; set; }

            public IFsFolder Parent { get; set; }

            public string Path { get; set; }

            public IFsFile Clone()
            {
                return new FsImage
                {
                    Name = this.Name,
                    FsSize = this.FsSize,
                    Parent = this.Parent,
                    Path = this.Path,
                    XxHash = this.XxHash,
                    Crc = this.Crc,
                    GapCrc = this.GapCrc,
                    FsOffset = this.FsOffset,
                    PostGapSize = this.PostGapSize,
                    PostGapFsOffset = this.PostGapFsOffset,
                    SplitIndex = this.SplitIndex
                };
            }
        }

        /// <summary>
        /// Wraps a FsYamlNode directory as an IFsFolder for VFS navigation.
        /// </summary>
        internal class FsYamlFolderItem : IFsFolder
        {
            private readonly FsYamlNode _node;

            public FsYamlFolderItem(FsYamlNode node)
            {
                _node = node;
            }

            public string Name => _node.Name;
            public IFsFolder Parent { get; set; }
            public string Path
            {
                get
                {
                    try
                    {
                        if (this.Parent == null || this.Parent.Parent == null)
                            return "/" + (this.Name ?? "");
                        return this.Parent?.Path + "/" + (this.Name ?? "");
                    }
                    catch { return this.Name ?? ""; }
                }
            }

            public List<IFsFile> Files => _node.Children?
                .Where(c => c.IsFile)
                .Select(c =>
                {
                    FsYamlFileItem f = new FsYamlFileItem(c) { Parent = this };
                    return (IFsFile)f;
                })
                .ToList() ?? new List<IFsFile>();

            public List<IFsFolder> Folders => _node.Children?
                .Where(c => c.IsDirectory)
                .Select(c =>
                {
                    FsYamlFolderItem fo = new FsYamlFolderItem(c) { Parent = this };
                    return (IFsFolder)fo;
                })
                .ToList() ?? new List<IFsFolder>();

            public override string ToString() => Name ?? "";
        }

        /// <summary>
        /// Wraps a FsYamlNode file as an IFsFile for VFS navigation.
        /// </summary>
        internal class FsYamlFileItem : IFsFile
        {
            private readonly FsYamlNode _node;

            public FsYamlFileItem(FsYamlNode node)
            {
                _node = node;
            }

            public string Name => _node.Name;
            public long FsSize => _node.Size;
            public IFsFolder Parent { get; set; }
            public string Path
            {
                get
                {
                    try
                    {
                        if (this.Parent == null || this.Parent.Parent == null)
                            return "/" + (this.Name ?? "");
                        return this.Parent?.Path + "/" + (this.Name ?? "");
                    }
                    catch { return this.Name ?? ""; }
                }
            }
            public string FullName => (Parent != null ? (Parent.Path ?? "") + "/" : "") + (Name ?? "");
            public bool IsMissing => false;
            public bool IsLastFile => false;
            public ulong XxHash { get => _node.XxHash64; set { } }
            public uint Crc { get => _node.Crc32; set { } }
            public uint GapCrc { get => 0; set { } }
            public bool IsSystemFile => _node.IsSystem;
            public long FsOffset => _node.Offset;
            public long PostGapSize => 0;
            public long PostGapFsOffset => 0;
            public int SplitIndex => 0;
            public IFsFileParts SplitParts => null;

            public IFsFile Clone() => this;
            public override string ToString() => Name ?? "";
        }

        /// <summary>
        /// Represents an area within an APP-format image exposed as a readable file (e.g. "00000004.app").
        /// </summary>
        internal class FsImageAreaFile : IFsFile
        {
            public string Name { get; set; }
            public long FsSize { get; set; }
            public long FsOffset { get; set; }
            public IFsFolder Parent { get; set; }
            public string Path
            {
                get
                {
                    try
                    {
                        if (this.Parent == null || this.Parent.Parent == null)
                            return "/" + (this.Name ?? "");
                        return this.Parent?.Path + "/" + (this.Name ?? "");
                    }
                    catch { return this.Name ?? ""; }
                }
            }
            public string FullName => (Parent != null ? (Parent.Path ?? "") + "/" : "") + (Name ?? "");
            public bool IsMissing => false;
            public bool IsLastFile => false;
            public ulong XxHash { get => 0; set { } }
            public uint Crc { get => 0; set { } }
            public uint GapCrc { get => 0; set { } }
            public bool IsSystemFile => false;
            public long PostGapSize => 0;
            public long PostGapFsOffset => 0;
            public int SplitIndex => 0;
            public IFsFileParts SplitParts => null;

            public IFsFile Clone() => this;
            public override string ToString() => Name ?? "";
        }

        /// <summary>
        /// Wraps a stored file (from the files table) as an IFsFile for VFS navigation.
        /// </summary>
        internal class FsStoredFile : IFsFile
        {
            public string Name { get; set; }
            public long FsSize { get; set; }
            public string StoredFileName { get; set; }
            public IFsFolder Parent { get; set; }
            public string Path
            {
                get
                {
                    try
                    {
                        if (this.Parent == null || this.Parent.Parent == null)
                            return "/" + (this.Name ?? "");
                        return this.Parent?.Path + "/" + (this.Name ?? "");
                    }
                    catch { return this.Name ?? ""; }
                }
            }
            public string FullName => (Parent != null ? (Parent.Path ?? "") + "/" : "") + (Name ?? "");
            public bool IsMissing => false;
            public bool IsLastFile => false;
            public ulong XxHash { get => 0; set { } }
            public uint Crc { get => 0; set { } }
            public uint GapCrc { get => 0; set { } }
            public bool IsSystemFile => false;
            public long FsOffset => 0;
            public long PostGapSize => 0;
            public long PostGapFsOffset => 0;
            public int SplitIndex => 0;
            public IFsFileParts SplitParts => null;

            public IFsFile Clone() => this;
            public override string ToString() => Name ?? "";
        }
    }
}
using NKitDataStore.Binary;
using NKitDataStore.Compression;
using NKitDataStore.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NKitDataStore
{
    /// <summary>
    /// The main entry point for managing NKit data sets in a directory.
    /// This class implements the factory for creating image readers and writers.
    /// </summary>
    public class DataStore : IDataStore
    {
        /// <summary>
        /// The file extension used for NKit Data Store database files.
        /// </summary>
        public const string DatabaseFileExtension = ".nkds";

        /// <summary>
        /// The full filename suffix for an auxiliary set's index database, e.g. "gamename.aux.nkds".
        /// Exposed as a property (not a const) so consuming assemblies read the live value rather
        /// than compiling it in. Use this to discover or name aux sets without reaching into internals.
        /// </summary>
        public static string AuxSetSuffix => _AuxSetSuffix + DatabaseFileExtension;

        /// <summary>
        /// The primary path used for storing the filesystem YAML file.
        /// </summary>
        public const string FileSystemYamlRootPath = "filesystem.yaml";

        /// <summary>
        /// The legacy (unprefixed) filename for the filesystem YAML file.
        /// </summary>
        public const string FileSystemYamlName = "filesystem.yaml";

        /// <summary>
        /// The primary path used for storing the binary NkFs filesystem file.
        /// </summary>
        public const string FileSystemNkfsRootPath = "filesystem.nkfs";

        /// <summary>
        /// The unprefixed filename for the binary NkFs filesystem file.
        /// </summary>
        public const string FileSystemNkfsName = "filesystem.nkfs";

        /// <summary>
        /// Default maximum stored (compressed) size in KiB for eager filesystem loading.
        /// Matches the default block size (64 KiB), so filesystem data that fits in a
        /// single block is eagerly loaded while larger data is lazy-loaded on demand.
        /// </summary>
        public const int DefaultMaxFileSystemSizeKiB = 0;

        /// <summary>
        /// Extracts the filesystem type name from a per-type nkfs filename.
        /// Returns true for "filesystem.{type}.nkfs" where type is not empty.
        /// Returns false for "filesystem.nkfs" (unified format) or non-matching filenames.
        /// </summary>
        /// <param name="fileName">The filename to parse (e.g., "filesystem.iso9660.nkfs").</param>
        /// <param name="typeName">When successful, the extracted lowercase filesystem type name; otherwise null.</param>
        /// <returns>True if a per-type filesystem name was extracted; false otherwise.</returns>
        internal static bool TryExtractFsTypeName(string fileName, out string? typeName)
        {
            typeName = null;
            const string prefix = "filesystem.";
            const string suffix = ".nkfs";

            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

            int middleLength = fileName.Length - prefix.Length - suffix.Length;
            if (middleLength <= 0)
                return false;

            string middle = fileName.Substring(prefix.Length, middleLength);

            typeName = middle.ToLowerInvariant();
            return true;
        }

        private readonly IDataStoreDataAccess _dataAccess;
        private readonly string _baseDirectory;
        private readonly string? _scopedSetName;

        // --- Mount debug tracing ---
        // When enabled, key lifecycle events (open/close DataStore, open/close readers) are logged to console.
        // Enable via DataStore.MountDebug = true before creating the DataStore instance (e.g., in VfsModel).
        private static bool _MountDebug;

        /// <summary>
        /// When true, DataStore emits console debug lines for instance and reader lifecycle events.
        /// Intended for use with the 'nkds mount' command only.
        /// </summary>
        public static bool MountDebug
        {
            get => _MountDebug;
            set => _MountDebug = value;
        }

        // Unique instance ID for distinguishing multiple DataStore instances in debug output
        private static int _NextInstanceId;
        private readonly int _instanceId = Interlocked.Increment(ref _NextInstanceId);

        /// <summary>
        /// Gets the underlying data access instance for internal consumers (e.g. NkdsUi block comparison).
        /// </summary>
        internal IDataStoreDataAccess DataAccess => _dataAccess;

        /// <summary>
        /// Initializes a new instance of the DataStore for a specific directory.
        /// </summary>
        /// <param name="baseDirectory">The root directory containing the data sets.</param>
        public DataStore(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory cannot be null or empty", nameof(baseDirectory));

            if (baseDirectory.EndsWith(DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(baseDirectory);
                _baseDirectory = Path.GetDirectoryName(fullPath)
                    ?? throw new ArgumentException("The datastore file path must have a parent directory.", nameof(baseDirectory));
                _scopedSetName = Path.GetFileNameWithoutExtension(fullPath);
            }
            else
            {
                _baseDirectory = baseDirectory;
                _scopedSetName = null;
            }

            // In a real application with dependency injection, this would be injected.
            // For now, we instantiate it directly.
            _dataAccess = new BinaryDataStoreDataAccess(_baseDirectory);

            if (_MountDebug)
                Console.WriteLine($"[MountDebug] DataStore #{_instanceId} OPENED: baseDir={_baseDirectory} scoped={_scopedSetName ?? "(none)"}");
        }

        /// <summary>
        /// Test-only helper to wait for background work related to a set to complete.
        /// Exposed as internal so tests can call it deterministically using InternalsVisibleTo.
        /// This does not change runtime behaviour; it merely proxies to the underlying
        /// data access implementation's synchronization APIs when available.
        /// </summary>
        internal void WaitForSetIdle(string setName, int timeoutMs = 30000)
        {
            if (string.IsNullOrWhiteSpace(setName)) throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            try
            {
                _dataAccess.WaitForCompressionTasks(setName, timeoutMs);
            }
            catch { }
        }

        public IImageReader OpenImageReader(GlobalImageKey key)
        {
            bool profiling = shouldProfile();
            Stopwatch? sw = null;
            if (profiling)
            {
                sw = Stopwatch.StartNew();
                Console.WriteLine($"[PROFILE] OpenImageReader START: set={key.SetName} id={key.ImageId} baseDir={_baseDirectory}");
            }

            ImageRecord? imageRecord = _dataAccess.GetImage(key.SetName, key.ImageId);
            if (imageRecord == null)
                throw new FileNotFoundException($"Image with key '{key}' not found.");

            // Ensure any background compression tasks for this set have completed so
            // that block metadata written by recent writers is visible when the
            // reader opens. This avoids tests and callers observing missing blocks
            // due to background flush timing. Use a best-effort wait.
            try { _dataAccess.WaitForCompressionTasks(key.SetName, 30000); } catch { }

            // Create a compressor instance for this reader
            InfoRecord info = _dataAccess.GetSetInfo(key.SetName);
            IBlockCompressor compressor = new BlockCompressor(info.BlockSize);

            ImageReader img = new ImageReader(_dataAccess, imageRecord, compressor, ownsDataAccess: false);
            if (profiling)
            {
                sw?.Stop();
                Console.WriteLine($"[PROFILE] OpenImageReader END: set={key.SetName} id={key.ImageId} result=ImageReader time_ms={sw?.ElapsedMilliseconds}");
            }
            if (_MountDebug)
                Console.WriteLine($"[MountDebug] DataStore #{_instanceId} OpenImageReader: set={key.SetName} id={key.ImageId} name={imageRecord.Name}");
            return img;
        }

        // OpenImageReader profiling. Off by default; flip this constant to true to log open timings
        // (matches the code-controlled MountDebug pattern — no environment variable).
        private const bool _profile = false;

        private static bool shouldProfile() => _profile;

        public IImageWriter AddImage(string setName, string imageName, string? system = null, ImageFormat format = ImageFormat.Unknown)
        {
            if (string.IsNullOrWhiteSpace(setName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            if (string.IsNullOrWhiteSpace(imageName))
                throw new ArgumentException("Image name cannot be null or empty", nameof(imageName));

            string setDbPath = Path.Combine(_baseDirectory, $"{setName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{setName}' does not exist. Call CreateSet before AddImage.");

            InfoRecord info = _dataAccess.GetSetInfo(setName);
            IDataStoreTransaction transaction = _dataAccess.BeginTransaction(setName);

            try
            {
                long imageId = _dataAccess.InsertImage(setName, transaction, imageName, system, format);
                if (imageId <= 0)
                {
                    transaction.Rollback();
                    throw new InvalidOperationException("Failed to create new image record.");
                }

                ImageRecord imageRecord = new ImageRecord
                {
                    Id = imageId,
                    SetName = setName,
                    Name = imageName,
                    Size = 0,
                    Crc32 = 0,
                    XxHash64 = 0,
                    System = system,
                    Format = format
                };

                // Create a compressor instance for this writer
                IBlockCompressor compressor = new BlockCompressor(info.BlockSize);

                return new ImageWriter(_dataAccess, imageRecord, transaction, compressor);
            }
            catch
            {
                transaction.Rollback();
                transaction.Dispose();
                throw;
            }
        }

        public ImageRecord RenameImage(GlobalImageKey key, string newImageName)
        {
            if (string.IsNullOrWhiteSpace(key.SetName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(key));

            if (string.IsNullOrWhiteSpace(newImageName))
                throw new ArgumentException("Image name cannot be null or empty", nameof(newImageName));

            string setDbPath = Path.Combine(_baseDirectory, $"{key.SetName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{key.SetName}' does not exist.");

            ImageRecord? existingImage = _dataAccess.GetImage(key.SetName, key.ImageId);
            if (existingImage == null)
                throw new FileNotFoundException($"Image with key '{key}' not found.");

            IDataStoreTransaction transaction = _dataAccess.BeginTransaction(key.SetName);
            try
            {
                _dataAccess.UpdateImageName(key.SetName, transaction, key.ImageId, newImageName);
                transaction.Commit();

                return _dataAccess.GetImage(key.SetName, key.ImageId)
                    ?? throw new InvalidOperationException($"Failed to reload renamed image '{key}'.");
            }
            catch
            {
                if (!transaction.IsCompleted)
                    transaction.Rollback();

                throw;
            }
            finally
            {
                transaction.Dispose();
            }
        }

        /// <summary>
        /// Renames an image TITLE, handling the WiiU multi-TMD case as one atomic unit.
        ///
        /// A WiiU digital title is stored as a <see cref="ImageFormat.TmdAppFolder"/> umbrella
        /// (Name = the plain base title) plus one <see cref="ImageFormat.App"/> child per TMD, each
        /// named "<c>{base} [tmd.N]</c>". The mount recombines them by matching
        /// <see cref="ExtractBaseName"/>(child) against the umbrella Name — so a rename MUST update
        /// the umbrella AND every child together, preserving each child's <c>[tmd.N]</c> index, or
        /// the recombine breaks. A plain single image is renamed as-is.
        ///
        /// <paramref name="newBaseName"/> is the new BASE title (no <c>[tmd.N]</c> suffix). The
        /// image identified by <paramref name="key"/> may be the umbrella OR any child — the base
        /// title is derived from it and the whole group is renamed in ONE transaction.
        /// Returns the number of image records renamed.
        /// </summary>
        public int RenameImageTitle(GlobalImageKey key, string newBaseName)
        {
            if (string.IsNullOrWhiteSpace(key.SetName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(key));
            if (string.IsNullOrWhiteSpace(newBaseName))
                throw new ArgumentException("Image name cannot be null or empty", nameof(newBaseName));

            string setDbPath = Path.Combine(_baseDirectory, $"{key.SetName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{key.SetName}' does not exist.");

            ImageRecord? target = _dataAccess.GetImage(key.SetName, key.ImageId);
            if (target == null)
                throw new FileNotFoundException($"Image with key '{key}' not found.");

            // Determine the current base title. If the identified image is itself a tmd child, the
            // base is ExtractBaseName; otherwise the image's own Name IS the base (umbrella or plain).
            string currentBase = ExtractBaseName(target.Name) ?? target.Name;

            // Collect the whole title group in this set: the TmdAppFolder umbrella whose Name == base,
            // plus every App child whose ExtractBaseName == base. For a plain image this list is just
            // the target itself (no umbrella, no tmd children), so it renames exactly one record.
            List<(long Id, string NewName)> renames = new List<(long, string)>();
            foreach (ImageRecord img in ListAllImages(r => r.SetName == key.SetName && !r.Removed))
            {
                if (img.Format == ImageFormat.TmdAppFolder && string.Equals(img.Name, currentBase, StringComparison.OrdinalIgnoreCase))
                    renames.Add((img.Id, newBaseName)); // umbrella -> new base (no suffix)
                else if (string.Equals(ExtractBaseName(img.Name), currentBase, StringComparison.OrdinalIgnoreCase))
                {
                    // tmd child -> "{newBase} [tmd.N]" preserving its index
                    string? index = ExtractIndexFileName(img.Name); // e.g. "tmd.0"
                    renames.Add((img.Id, index != null ? $"{newBaseName} [{index}]" : newBaseName));
                }
            }

            // If the identified image wasn't captured above (plain image with no base match — e.g. a
            // name that isn't tmd-disambiguated and has no umbrella), rename just it.
            if (renames.Count == 0)
                renames.Add((key.ImageId, newBaseName));

            IDataStoreTransaction transaction = _dataAccess.BeginTransaction(key.SetName);
            try
            {
                foreach ((long id, string newName) in renames)
                    _dataAccess.UpdateImageName(key.SetName, transaction, id, newName);
                transaction.Commit();
                return renames.Count;
            }
            catch
            {
                if (!transaction.IsCompleted)
                    transaction.Rollback();
                throw;
            }
            finally
            {
                transaction.Dispose();
            }
        }

        public void UpdateImageFormat(GlobalImageKey key, ImageFormat format)
        {
            if (string.IsNullOrWhiteSpace(key.SetName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(key));

            string setDbPath = Path.Combine(_baseDirectory, $"{key.SetName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{key.SetName}' does not exist.");

            IDataStoreTransaction transaction = _dataAccess.BeginTransaction(key.SetName);
            try
            {
                _dataAccess.UpdateImageFormat(key.SetName, transaction, key.ImageId, format);
                transaction.Commit();
            }
            catch
            {
                if (!transaction.IsCompleted)
                    transaction.Rollback();
                throw;
            }
            finally
            {
                transaction.Dispose();
            }
        }

        /// <summary>
        /// Scans all WiiU images stored with <see cref="ImageFormat.App"/> and repairs
        /// any that are disc images (WUD/WUX/ISO) by updating them to <see cref="ImageFormat.Iso"/>.
        /// CDN app images keep a tmd-disambiguated name (e.g. "Title [tmd.0]") or have
        /// zero areas — disc images have areas and no tmd-style bracket suffix.
        /// </summary>
        public int RepairWiiUImageFormats()
        {
            int repaired = 0;
            foreach (ImageRecord img in ListAllImages(r => r.System != null &&
                r.System.Equals("WiiU", StringComparison.OrdinalIgnoreCase) &&
                r.Format == ImageFormat.App &&
                !r.Removed))
            {
                // CDN app images have a tmd-disambiguated name like "Title [tmd.0]"
                bool isCdnName = img.Name != null
                    && img.Name.LastIndexOf(" [", StringComparison.Ordinal) >= 0
                    && img.Name.EndsWith("]");
                if (isCdnName)
                    continue;

                // Verify the image actually has area records (disc images have areas)
                try
                {
                    IImageReader reader = OpenImageReader(new GlobalImageKey(img.SetName, img.Id));
                    bool hasAreas = false;
                    try
                    {
                        IEnumerable<AreaRecord> areas = reader.GetAreas();
                        hasAreas = areas != null && areas.Any();
                    }
                    finally
                    {
                        reader.Dispose();
                    }

                    if (!hasAreas)
                        continue;
                }
                catch
                {
                    continue;
                }

                UpdateImageFormat(new GlobalImageKey(img.SetName, img.Id), ImageFormat.Iso);
                repaired++;
            }
            return repaired;
        }

        public void DeleteImage(GlobalImageKey key)
        {
            if (string.IsNullOrWhiteSpace(key.SetName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(key));

            string setDbPath = Path.Combine(_baseDirectory, $"{key.SetName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{key.SetName}' does not exist.");

            _dataAccess.DeleteImage(key.SetName, key.ImageId);
        }

        public void RestoreImage(GlobalImageKey key)
        {
            if (string.IsNullOrWhiteSpace(key.SetName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(key));

            string setDbPath = Path.Combine(_baseDirectory, $"{key.SetName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{key.SetName}' does not exist.");

            _dataAccess.RestoreImage(key.SetName, key.ImageId);
        }

        public void CompactSet(string setName, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(setName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            string setDbPath = Path.Combine(_baseDirectory, $"{setName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{setName}' does not exist.");

            _dataAccess.CompactSet(setName, progress);
        }

        /// <summary>
        /// Inspects the file state of a set and reports its health status.
        /// This is a read-only operation that does not modify any files.
        /// </summary>
        public RecoveryState CheckSetHealth(string setName)
        {
            if (string.IsNullOrWhiteSpace(setName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            return _dataAccess.CheckSetHealth(setName);
        }

        public void RollbackImage(GlobalImageKey key, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(key.SetName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(key));

            string setDbPath = Path.Combine(_baseDirectory, $"{key.SetName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                throw new InvalidOperationException($"Set '{key.SetName}' does not exist.");

            _dataAccess.Rollback(key.SetName, key.ImageId, progress);
        }

        [Obsolete("Use CreateSet followed by AddImage. This legacy helper will be removed in a future version.")]
        public IImageWriter CreateImage(string setName, string imageName, long shardSize = 50L * 1024 * 1024 * 1024, string? system = null, ImageFormat format = ImageFormat.Unknown, int blockSize = 0)
        {
            if (GetSetInfo(setName) == null)
                CreateSet(setName, shardSize, blockSize);

            return AddImage(setName, imageName, system, format);
        }

        public SetInfo CreateSet(string setName, long shardSize = 50L * 1024 * 1024 * 1024, int blockSize = 0)
        {
            if (string.IsNullOrWhiteSpace(setName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            if (GetSetInfo(setName) != null)
                throw new InvalidOperationException($"Set '{setName}' already exists.");

            // Enforce aux block size consistency (skip for aux sets to avoid circular dependency)
            if (!setName.EndsWith(".aux", StringComparison.OrdinalIgnoreCase))
            {
                int? auxBlockSize = GetAuxBlockSize(_baseDirectory);
                if (auxBlockSize != null && blockSize != auxBlockSize.Value)
                {
                    if (blockSize != 0)
                    {
                        string msg = $"ERROR: Block size {blockSize} conflicts with aux store block size {auxBlockSize.Value} in '{_baseDirectory}'. Forcing block size to {auxBlockSize.Value}.";
                        Trace.TraceError(msg);
                        Console.Error.WriteLine(msg);
                    }
                    blockSize = auxBlockSize.Value;
                }
            }

            _dataAccess.EnsureSetExists(setName, shardSize, blockSize);
            return GetSetInfo(setName) ?? throw new InvalidOperationException($"Failed to create set '{setName}'.");
        }

        public IEnumerable<string> ListSetNames()
        {
            if (!string.IsNullOrWhiteSpace(_scopedSetName))
            {
                // Never list aux or split sets — they are internal and should not be mounted
                if (_scopedSetName.EndsWith(_AuxSetSuffix, StringComparison.OrdinalIgnoreCase)
                    || _scopedSetName.EndsWith(_SplitSetSuffix, StringComparison.OrdinalIgnoreCase))
                    return Enumerable.Empty<string>();

                string scopedSetPath = Path.Combine(_baseDirectory, $"{_scopedSetName}{DatabaseFileExtension}");
                return File.Exists(scopedSetPath)
                    ? new[] { _scopedSetName }
                    : Enumerable.Empty<string>();
            }

            // Exclude shard files named like "setname_<hex>" (e.g. MySet_0a.nkds), but ONLY
            // when the corresponding base set file also exists. This prevents false-positive
            // shard detection for legitimately named files like "fullset_49.nkds" where
            // "fullset.nkds" does not exist.
            Regex shardSuffix = new Regex("_[0-9a-fA-F]{2,4}$", RegexOptions.Compiled);

            List<string> allNames = Directory.EnumerateFiles(_baseDirectory, $"*{DatabaseFileExtension}", SearchOption.AllDirectories)
                .Select(file => Path.ChangeExtension(Path.GetRelativePath(_baseDirectory, file), null))
                .Select(name => name?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .Distinct()
                .ToList();

            HashSet<string> nameSet = new HashSet<string>(allNames, StringComparer.OrdinalIgnoreCase);

            return allNames.Where(name =>
            {
                // Exclude aux and split sets — they are internal implementation details
                // and should never be listed for mounting or user-facing operations.
                if (name.EndsWith(_AuxSetSuffix, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(_SplitSetSuffix, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!shardSuffix.IsMatch(name))
                    return true;
                // Only treat as a shard if the base set file (without the _hex suffix) also exists
                string baseName = name.Substring(0, name.LastIndexOf('_'));
                return !nameSet.Contains(baseName);
            });
        }

        public IEnumerable<ImageRecord> ListAllImages(Func<ImageRecord, bool>? predicate = null)
        {
            List<string> setNames = ListSetNames().ToList();
            ConcurrentBag<ImageRecord> allImages = new ConcurrentBag<ImageRecord>();
            Parallel.ForEach(setNames, setName =>
            {
                List<ImageRecord> images = withReadableSet(setName, da => da.GetAllImagesInSet(setName).ToList());
                foreach (ImageRecord? img in images)
                    allImages.Add(img);
            });

            List<ImageRecord> result = allImages.ToList();
            return predicate == null ? result : result.Where(predicate).ToList();
        }

        /// <summary>
        /// Lists all images across all sets, including those marked as removed.
        /// </summary>
        public IEnumerable<ImageRecord> ListAllImagesIncludingRemoved()
        {
            List<string> setNames = ListSetNames().ToList();
            ConcurrentBag<ImageRecord> allImages = new ConcurrentBag<ImageRecord>();
            Parallel.ForEach(setNames, setName =>
            {
                List<ImageRecord> images = withReadableSet(setName, da => da.GetAllImagesInSetIncludingRemoved(setName).ToList());
                foreach (ImageRecord? img in images)
                    allImages.Add(img);
            });

            return allImages.ToList();
        }

        /// <summary>
        /// Lists all images in a single named set without opening other sets.
        /// </summary>
        public List<ImageRecord> ListImagesInSet(string setName) => withReadableSet(setName, da => da.GetAllImagesInSet(setName).ToList());

        /// <summary>
        /// Lists all images in a single named set, including those marked as removed.
        /// </summary>
        public List<ImageRecord> ListImagesInSetIncludingRemoved(string setName) => withReadableSet(setName, da => da.GetAllImagesInSetIncludingRemoved(setName).ToList());

        public IEnumerable<SetInfo> DescribeSets()
        {
            IEnumerable<string> setNames = ListSetNames();
            foreach (string setName in setNames)
            {
                SetInfo? info = GetSetInfo(setName, false);
                if (info != null)
                    yield return info;
            }
        }

        /// <summary>
        /// Retrieves descriptive information and all images for all available data sets in a single pass.
        /// </summary>
        public IEnumerable<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> DescribeSetsWithImages(string? filterSetName = null, bool includeFileSystemYaml = false, long? maxFileSystemYamlSize = null)
        {
            // Console.WriteLine($"[Debug] DataStore opened and listing sets: {_baseDirectory}");
            IEnumerable<string> sets = ListSetNames();
            if (!string.IsNullOrWhiteSpace(filterSetName))
                sets = sets.Where(s => string.Equals(s, filterSetName, StringComparison.OrdinalIgnoreCase));

            return sets
                .AsParallel()
                .Select(setName =>
                {
                    string setDbPath = Path.Combine(_baseDirectory, $"{setName}{DatabaseFileExtension}");
                    if (!File.Exists(setDbPath)) return default;

                    return withReadableSet(setName, da =>
                    {
                        InfoRecord info = da.GetSetInfo(setName);
                        if (info == null) return default;

                        List<ImageRecord> images = da.GetAllImagesInSet(setName).ToList();

                        List<string> filePaths = new List<string> { setDbPath };
                        string setDirectory = Path.GetDirectoryName(setDbPath) ?? _baseDirectory;
                        string setBaseName = Path.GetFileNameWithoutExtension(setDbPath);

                        if (info.ShardSize > 0)
                        {
                            try
                            {
                                IEnumerable<string> shardFiles = Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds");
                                filePaths.AddRange(shardFiles);
                            }
                            catch { } // If directory access fails, just return main DB
                        }

                        SetInfo setInfo = new SetInfo
                        {
                            SetName = setName,
                            FilePaths = filePaths,
                            ShardSize = info.ShardSize,
                            BlockSize = info.BlockSize,
                            MaxOffsetBlocks = info.MaxOffsetBlocks,
                            ImageCount = images.Count,
                            TotalSize = images.Sum(img => img.Size),
                            BlockStorage = new BlockStorageInfo()
                        };

                        Dictionary<long, byte[]> fsYamlData = new Dictionary<long, byte[]>();
                        if (includeFileSystemYaml)
                        {
                            foreach (ImageRecord img in images)
                            {
                                FileRecord? fileRecord = da.GetFile(setName, img.Id, FileSystemNkfsRootPath)
                                    ?? da.GetFile(setName, img.Id, FileSystemNkfsName)
                                    ?? da.GetFile(setName, img.Id, FileSystemYamlRootPath)
                                    ?? da.GetFile(setName, img.Id, FileSystemYamlName);
                                if (fileRecord != null)
                                {
                                    if (maxFileSystemYamlSize.HasValue && fileRecord.Size > maxFileSystemYamlSize.Value)
                                    {
                                        Trace.WriteLine($"[LazyLoad] Skipping filesystem.yaml for '{img.Name}' "
                                            + $"(stored size {fileRecord.Size} bytes exceeds threshold {maxFileSystemYamlSize.Value} bytes)");
                                        continue;
                                    }

                                    byte[]? data = da.ReadFileData(setName, fileRecord);
                                    if (data != null)
                                    {
                                        fsYamlData[img.Id] = data;
                                    }
                                }
                            }
                        }

                        return (Info: setInfo, Images: images, FileSystemYamlData: fsYamlData);
                    });
                })
                .Where(x => x.Info != null)
                .ToList();
        }

        public SetInfo? GetSetInfo(string setName, bool includeStats = false)
        {
            if (string.IsNullOrWhiteSpace(setName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            string setDbPath = Path.Combine(_baseDirectory, $"{setName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                return null;

            InfoRecord info = withReadableSet(setName, da => da.GetSetInfo(setName));
            long shardSize = info.ShardSize;
            int blockSize = info.BlockSize;
            int maxOffsetBlocks = info.MaxOffsetBlocks;

            int imageCount = 0;
            long totalSize = 0;
            if (includeStats)
            {
                // Get image count and total size
                List<ImageRecord> images = withReadableSet(setName, da => da.GetAllImagesInSet(setName).ToList());
                imageCount = images.Count;
                totalSize = images.Sum(img => img.Size);
            }

            // Get list of all database and shard files for this set
            List<string> filePaths = new List<string> { setDbPath };
            string setDirectory = Path.GetDirectoryName(setDbPath) ?? _baseDirectory;
            string setBaseName = Path.GetFileNameWithoutExtension(setDbPath);

            // In hybrid design, find shard files alongside the main index database.
            if (shardSize > 0)
            {
                try
                {
                    IEnumerable<string> shardFiles = Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds");
                    filePaths.AddRange(shardFiles);
                }
                catch { } // If directory access fails, just return main DB
            }

            return new SetInfo
            {
                SetName = setName,
                FilePaths = filePaths,
                ShardSize = shardSize,
                BlockSize = blockSize,
                MaxOffsetBlocks = maxOffsetBlocks,
                ImageCount = imageCount,
                TotalSize = totalSize,
                BlockStorage = new BlockStorageInfo() // Empty for now, could be populated from statistics if needed
            };
        }

        public DataStoreStatistics? GetSetStatistics(string setName, bool includePerImageStats = false, Action<int, int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(setName))
                throw new ArgumentException("Set name cannot be null or empty", nameof(setName));

            string setDbPath = Path.Combine(_baseDirectory, $"{setName}{DatabaseFileExtension}");
            if (!File.Exists(setDbPath))
                return null;

            return _dataAccess.GetSetStatistics(setName, includePerImageStats, progress, cancellationToken);
        }

        /// <summary>
        /// Executes an operation against a readable DataAccess for the specified set.
        /// </summary>
        private T withReadableSet<T>(string setName, Func<IDataStoreDataAccess, T> operation) => operation(_dataAccess);

        public byte[]? ReadFile(GlobalImageKey key, string name)
        {
            FileRecord? record = _dataAccess.GetFile(key.SetName, key.ImageId, name);
            if (record == null)
                return null;
            return _dataAccess.ReadFileData(key.SetName, record);
        }

        /// <summary>
        /// The naming suffix used for auxiliary (aux) data sets.
        /// An aux set for primary set "wii" is named "wii.aux", producing files
        /// like "wii.aux.nkds" (index) and "wii.aux_0000.nkds" (shards).
        /// </summary>
        internal const string _AuxSetSuffix = ".aux";

        /// <summary>
        /// The naming suffix for per-game split sidecar stores.
        /// A split set for primary set "gamename" is named "gamename.split", producing
        /// a single embedded file "gamename.split.nkds" (shardSize=0, no shards).
        /// </summary>
        internal const string _SplitSetSuffix = ".split";

        /// <summary>
        /// Given a primary set path <c>{dir}/{name}.nkds</c>, checks whether a
        /// matching aux set file <c>{dir}/{name}.aux.nkds</c> exists.
        /// Returns the aux set name (e.g. "wii.aux") when the aux index file is
        /// present, or <c>null</c> when no aux store is found.
        /// Aux shard files follow the pattern <c>{name}.aux_NNNN.nkds</c>.
        /// </summary>
        /// <param name="primarySetPath">
        /// Full path to the primary set index file, e.g. <c>D:\NKitData\wii.nkds</c>.
        /// May also be just a directory + set name without extension; the method
        /// normalises both forms.
        /// </param>
        /// <returns>
        /// The aux set name (e.g. "wii.aux") when the aux index file exists in the
        /// same directory as the primary set, or <c>null</c> otherwise.
        /// </returns>
        public static string? ResolveAuxSetName(string primarySetPath)
        {
            if (string.IsNullOrWhiteSpace(primarySetPath))
                return null;

            string fullPath = Path.GetFullPath(primarySetPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory == null || !Directory.Exists(directory))
                return null;

            // Find ANY .aux.nkds file in the same directory (there should only be one)
            string auxSuffix = _AuxSetSuffix + DatabaseFileExtension; // ".aux.nkds"
            foreach (string file in Directory.EnumerateFiles(directory, $"*{auxSuffix}"))
            {
                string fileName = Path.GetFileName(file);

                // Defensive: skip split sidecar files
                if (fileName.Contains(_SplitSetSuffix + "."))
                    continue;

                // Extract set name: strip the .nkds extension to get "xxx.aux"
                string auxSetName = fileName.Substring(0, fileName.Length - DatabaseFileExtension.Length);
                return auxSetName;
            }

            return null;
        }

        /// <summary>
        /// Checks whether a per-game split sidecar file <c>{setName}.split.nkds</c>
        /// exists in the specified base directory.
        /// Uses exact filename match (not glob) for deterministic, non-ambiguous discovery.
        /// </summary>
        /// <param name="baseDirectory">
        /// The directory to search for the split file.
        /// </param>
        /// <param name="setName">
        /// The per-game set name (e.g. "gamename").
        /// </param>
        /// <returns>
        /// The split set name (e.g. "gamename.split") when the split file exists,
        /// or <c>null</c> otherwise.
        /// </returns>
        internal static string? ResolveSplitSetName(string baseDirectory, string setName)
        {
            string splitFileName = setName + _SplitSetSuffix + DatabaseFileExtension;
            string splitPath = Path.Combine(baseDirectory, splitFileName);
            return File.Exists(splitPath) ? (setName + _SplitSetSuffix) : null;
        }

        /// <summary>
        /// Resolves the aux set name for a primary set, validating that the aux
        /// store's block size matches the primary store's block size.
        /// Returns the aux set name when the aux index file exists and block sizes
        /// match, or <c>null</c> when no aux store is found or block sizes differ.
        /// </summary>
        /// <param name="primarySetPath">
        /// Full path to the primary set index file, e.g. <c>D:\NKitData\wii.nkds</c>.
        /// </param>
        /// <param name="primaryBlockSize">
        /// The block size of the primary store (from its <see cref="SetInfo"/>).
        /// </param>
        /// <returns>
        /// The aux set name (e.g. "wii.aux") when the aux index file exists and
        /// its block size matches <paramref name="primaryBlockSize"/>, or <c>null</c>
        /// otherwise.
        /// </returns>
        public static string? ResolveAuxSetNameWithBlockSizeValidation(string primarySetPath, int primaryBlockSize)
        {
            string? auxSetName = ResolveAuxSetName(primarySetPath);
            if (auxSetName == null)
                return null;

            string fullPath = Path.GetFullPath(primarySetPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory == null)
                return null;

            try
            {
                using DataStore auxStore = new DataStore(directory);
                SetInfo? auxInfo = auxStore.GetSetInfo(auxSetName);
                if (auxInfo == null)
                {
                    Trace.TraceWarning(
                        "Aux store '{0}' exists but could not read set info. Falling back to primary-only mode.",
                        auxSetName);
                    return null;
                }

                if (auxInfo.BlockSize != primaryBlockSize)
                {
                    Trace.TraceWarning(
                        "Aux store '{0}' block size ({1}) does not match primary block size ({2}). Falling back to primary-only mode.",
                        auxSetName, auxInfo.BlockSize, primaryBlockSize);
                    return null;
                }

                return auxSetName;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to validate aux store '{0}': {1}. Falling back to primary-only mode.",
                    auxSetName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Returns the block size of the aux store in the specified directory, or
        /// <c>null</c> if no aux store exists or its metadata cannot be read.
        /// </summary>
        /// <param name="directoryPath">
        /// The directory to probe for an aux store (e.g. a <c>*.aux.nkds</c> file).
        /// </param>
        /// <returns>
        /// The aux store's block size when an aux store exists and is readable,
        /// or <c>null</c> otherwise.
        /// </returns>
        public static int? GetAuxBlockSize(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return null;

            try
            {
                // Build a synthetic primary path to reuse ResolveAuxSetName
                string syntheticPath = Path.Combine(directoryPath, "probe" + DatabaseFileExtension);
                string? auxSetName = ResolveAuxSetName(syntheticPath);
                if (auxSetName == null)
                    return null;

                using DataStore auxStore = new DataStore(directoryPath);
                SetInfo? auxInfo = auxStore.GetSetInfo(auxSetName);
                return auxInfo?.BlockSize;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the shard size for the set identified by the given primary set path,
        /// or -1 if the set does not exist or cannot be read.
        /// </summary>
        public static long GetShardSize(string primarySetPath)
        {
            if (string.IsNullOrWhiteSpace(primarySetPath))
                return -1;

            try
            {
                string fullPath = Path.GetFullPath(primarySetPath);
                string? directory = Path.GetDirectoryName(fullPath);
                string fileName = Path.GetFileName(fullPath);
                string setName = fileName.EndsWith(DatabaseFileExtension, StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - DatabaseFileExtension.Length)
                    : fileName;

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(setName))
                    return -1;

                using DataStore ds = new DataStore(directory);
                SetInfo? info = ds.GetSetInfo(setName);
                return info?.ShardSize ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Returns (shardSize, blockSize) for the set identified by the given primary set path,
        /// or null if the set does not exist or cannot be read.
        /// </summary>
        public static (long ShardSize, int BlockSize)? GetSetSizes(string primarySetPath)
        {
            if (string.IsNullOrWhiteSpace(primarySetPath))
                return null;

            try
            {
                string fullPath = Path.GetFullPath(primarySetPath);
                string? directory = Path.GetDirectoryName(fullPath);
                string fileName = Path.GetFileName(fullPath);
                string setName = fileName.EndsWith(DatabaseFileExtension, StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - DatabaseFileExtension.Length)
                    : fileName;

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(setName))
                    return null;

                using DataStore ds = new DataStore(directory);
                SetInfo? info = ds.GetSetInfo(setName);
                if (info == null)
                    return null;
                return (info.ShardSize, info.BlockSize);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates an <see cref="IBlockProvider"/> for the given primary image reader,
        /// automatically discovering and attaching an aux store when one exists by
        /// naming convention. When an aux store is found and contains a matching image,
        /// returns an <see cref="AuxBlockProvider"/> that wraps the primary
        /// <see cref="ReaderBlockProvider"/> with aux fallback. Otherwise returns a
        /// plain <see cref="ReaderBlockProvider"/>.
        /// </summary>
        /// <param name="primaryReader">The primary image reader (must not be null).</param>
        /// <param name="baseDirectory">
        /// The directory containing the primary set's <c>.nkds</c> file.
        /// </param>
        /// <param name="setName">
        /// The primary set name (e.g. "wii"). Used to derive the aux set name
        /// via the <c>{setName}.aux</c> convention.
        /// </param>
        /// <returns>
        /// An <see cref="AuxBlockProvider"/> when an aux store with a matching image
        /// is found, or a <see cref="ReaderBlockProvider"/> otherwise.
        /// </returns>
        internal static IBlockProvider CreateBlockProvider(IImageReader primaryReader, string baseDirectory, string setName)
        {
            IBlockProvider primaryProvider = new ReaderBlockProvider(primaryReader);

            try
            {
                ImageRecord? primaryImage = primaryReader.Image;
                if (primaryImage == null)
                    return primaryProvider;

                // 1. Discover split store (per-game, exact filename match using image name)
                // Split stores are named after the game: "{imageName}.split.nkds"
                // Use the image name (without extension) as the split discovery key.
                IImageReader? splitReader = null;
                string imageBaseName = Path.GetFileNameWithoutExtension(primaryImage.Name ?? "");
                if (!string.IsNullOrEmpty(imageBaseName))
                {
                    string? splitSetName = ResolveSplitSetName(baseDirectory, imageBaseName);
                    if (splitSetName != null && primaryImage.Name != null)
                    {
                        splitReader = OpenAuxReaderForImage(baseDirectory, splitSetName, primaryImage.Name);
                    }
                }

                // 2. Discover shared aux store (pattern match *.aux.nkds)
                IImageReader? auxReader = null;
                string primarySetPath = Path.Combine(baseDirectory, setName + DatabaseFileExtension);
                string? auxSetName = ResolveAuxSetNameWithBlockSizeValidation(primarySetPath, primaryReader.Info.BlockSize);
                if (auxSetName != null && primaryImage.Name != null)
                {
                    auxReader = OpenAuxReaderForImage(baseDirectory, auxSetName, primaryImage.Name);
                }

                // 3. If either exists, wrap in AuxBlockProvider for multi-tier resolution
                if (splitReader != null || auxReader != null)
                    return new AuxBlockProvider(primaryProvider, splitReader, auxReader);

                return primaryProvider;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to attach aux/split store for set '{0}': {1}. Using primary-only mode.",
                    setName, ex.Message);
                return primaryProvider;
            }
        }

        /// <summary>
        /// Opens an image reader from an auxiliary (aux or split) store for the specified image name.
        /// Returns the reader if the image is found in the store, or <c>null</c> otherwise.
        /// The returned reader and its underlying DataStore are intentionally not disposed here —
        /// their lifetime is tied to the consuming AuxBlockProvider.
        /// </summary>
        private static IImageReader? OpenAuxReaderForImage(string baseDirectory, string auxSetName, string imageName)
        {
            DataStore store = new DataStore(baseDirectory);
            try
            {
                List<ImageRecord> images = store.ListImagesInSet(auxSetName)
                    .Where(img => string.Equals(img.Name, imageName, StringComparison.OrdinalIgnoreCase)
                                  && !img.Removed)
                    .ToList();

                if (images.Count == 0)
                {
                    store.Dispose();
                    return null;
                }

                return store.OpenImageReader(new GlobalImageKey(auxSetName, images[0].Id));
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to open aux/split store '{0}' for image '{1}': {2}",
                    auxSetName, imageName, ex.Message);
                store.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Extracts the base name from a disambiguated image name of the form
        /// "{baseName} [tmd.{N}]" where N is a non-negative integer.
        /// Returns null if the name does not match the pattern.
        /// </summary>
        internal static string? ExtractBaseName(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
                return null;

            // Find the last " [" in the string
            int bracketStart = imageName.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracketStart < 0)
                return null;

            // The string must end with "]"
            if (imageName[imageName.Length - 1] != ']')
                return null;

            // Extract content between brackets: should be "tmd.{digits}"
            string content = imageName.Substring(bracketStart + 2, imageName.Length - bracketStart - 3);

            if (!content.StartsWith("tmd.", StringComparison.Ordinal))
                return null;

            string digits = content.Substring(4);
            if (digits.Length == 0)
                return null;

            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9')
                    return null;
            }

            return imageName.Substring(0, bracketStart);
        }

        /// <summary>
        /// Returns true if the image name ends with " [tmd.X]" where X is a non-negative integer.
        /// </summary>
        internal static bool IsTmdDisambiguatedName(string imageName) => ExtractBaseName(imageName) != null;

        /// <summary>
        /// Recursively counts all file nodes across a list of FsYamlNode trees.
        /// </summary>
        internal static int CountFilesRecursive(List<FsYamlNode> nodes)
        {
            int count = 0;
            if (nodes == null)
                return 0;
            foreach (FsYamlNode node in nodes)
            {
                if (node.IsFile)
                {
                    count++;
                }
                else if (node.Children != null)
                {
                    count += CountFilesRecursive(node.Children);
                }
            }
            return count;
        }

        /// <summary>
        /// Extracts the index file name (e.g., "tmd.0") from a disambiguated image name
        /// like "Game Title [tmd.0]". Returns null if the name doesn't match the pattern.
        /// </summary>
        internal static string? ExtractIndexFileName(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
                return null;

            int bracketStart = imageName.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracketStart < 0)
                return null;

            if (imageName[imageName.Length - 1] != ']')
                return null;

            // Extract content between brackets: e.g., "tmd.0"
            string content = imageName.Substring(bracketStart + 2, imageName.Length - bracketStart - 3);

            if (!content.StartsWith("tmd.", StringComparison.Ordinal))
                return null;

            string digits = content.Substring(4);
            if (digits.Length == 0)
                return null;

            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9')
                    return null;
            }

            return content;
        }

        /// <summary>
        /// Recursively collects all file nodes from an FsYamlNode tree into a flat list
        /// of ChildImageFile entries associated with the given image ID.
        /// </summary>
        internal static void CollectChildFiles(FsYamlNode node, long imageId, List<ChildImageFile> childFiles)
        {
            if (node == null)
                return;

            if (node.IsFile)
            {
                childFiles.Add(new ChildImageFile
                {
                    FileName = node.Name,
                    ImageId = imageId,
                    Size = node.Size
                });
            }
            else if (node.Children != null)
            {
                foreach (FsYamlNode child in node.Children)
                    CollectChildFiles(child, imageId, childFiles);
            }
        }

        public void Dispose()
        {
            if (_MountDebug)
                Console.WriteLine($"[MountDebug] DataStore #{_instanceId} DISPOSE START: baseDir={_baseDirectory}");

            _dataAccess?.Dispose();

            if (_MountDebug)
                Console.WriteLine($"[MountDebug] DataStore #{_instanceId} DISPOSE COMPLETE");
        }
    }

}
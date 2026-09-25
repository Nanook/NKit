using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Services;
using System.Text.Json;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for ConfigService.
///
/// Feature: nkds-ui-config-persistence, Property 1: Configuration Serialization Round-Trip
/// Feature: nkds-ui-config-persistence, Property 2: MRU List Add Semantics
/// Feature: nkds-ui-config-persistence, Property 3: MRU List Capacity Invariant
/// Feature: nkds-ui-config-persistence, Property 4: Export Format Preference Round-Trip
/// Feature: nkds-ui-config-persistence, Property 5: Window State Round-Trip
/// Feature: nkds-ui-config-persistence, Property 6: Corrupt Config Graceful Fallback
/// </summary>
public sealed class ConfigServicePropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigServicePropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ConfigServicePropTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// **Validates: Requirements 1.3, 7.2**
    ///
    /// Property 1: Configuration Serialization Round-Trip.
    /// For any valid AppConfig instance, serializing to JSON and then deserializing back
    /// SHALL produce an equivalent AppConfig with identical values for all fields.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SerializationRoundTrip_ProducesEquivalentConfig(
        NonNegativeInt mountCountSeed,
        NonNegativeInt storeCountSeed,
        NonNegativeInt exportCountSeed,
        NonNegativeInt contentSeed,
        bool hasWindowState)
    {
        Random rng = new Random(contentSeed.Get);
        int mountCount = mountCountSeed.Get % 16; // 0-15
        int storeCount = storeCountSeed.Get % 16; // 0-15
        int exportCount = exportCountSeed.Get % 21; // 0-20

        AppConfig config = new AppConfig
        {
            MountPathHistory = Enumerable.Range(0, mountCount)
                .Select(_ => $@"{(char)('C' + rng.Next(5))}:\Dir{rng.Next(100)}\Sub{rng.Next(100)}")
                .ToList(),
            DataStoreHistory = Enumerable.Range(0, storeCount)
                .Select(_ => $@"{(char)('C' + rng.Next(5))}:\Store{rng.Next(100)}\data{rng.Next(100)}.nkds")
                .ToList(),
            ExportFormatDefaults = Enumerable.Range(0, exportCount)
                .ToDictionary(
                    i => $"System{i}|Format{rng.Next(50)}",
                    i => $"Target{rng.Next(20)}"),
            WindowState = hasWindowState
                ? new WindowStateConfig
                {
                    Width = rng.Next(100, 3000),
                    Height = rng.Next(100, 2000),
                    X = rng.Next(-500, 3000),
                    Y = rng.Next(-500, 2000)
                }
                : null
        };

        // Serialize
        string json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);

        // Deserialize
        AppConfig deserialized = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig);

        if (deserialized == null)
            return false;

        // Assert equality of all fields
        if (!config.MountPathHistory.SequenceEqual(deserialized.MountPathHistory))
            return false;
        if (!config.DataStoreHistory.SequenceEqual(deserialized.DataStoreHistory))
            return false;
        if (config.ExportFormatDefaults.Count != deserialized.ExportFormatDefaults.Count)
            return false;
        foreach (KeyValuePair<string, string> kvp in config.ExportFormatDefaults)
        {
            if (!deserialized.ExportFormatDefaults.TryGetValue(kvp.Key, out string val) || val != kvp.Value)
                return false;
        }

        if (config.WindowState == null && deserialized.WindowState != null)
            return false;
        if (config.WindowState != null && deserialized.WindowState == null)
            return false;
        if (config.WindowState != null && deserialized.WindowState != null)
        {
            if (config.WindowState.Width != deserialized.WindowState.Width ||
                config.WindowState.Height != deserialized.WindowState.Height ||
                config.WindowState.X != deserialized.WindowState.X ||
                config.WindowState.Y != deserialized.WindowState.Y)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    ///
    /// Property 5: Window State Round-Trip.
    /// For any valid integer tuple (width, height, x, y), after calling SetWindowState,
    /// the WindowState property SHALL return a WindowStateConfig with those exact values.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WindowState_RoundTrip_ReturnsExactValues(int width, int height, int x, int y)
    {
        string configPath = Path.Combine(_tempDir, $"cfg-ws-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-ws-{Guid.NewGuid():N}.json");

        try
        {
            ConfigService service = new ConfigService(configPath, legacyPath);
            service.SetWindowState(width, height, x, y);

            WindowStateConfig state = service.WindowState;
            if (state == null)
                return false;

            return state.Width == width &&
                   state.Height == height &&
                   state.X == x &&
                   state.Y == y;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    // Pool of path strings for MRU capacity testing
    private static readonly string[] PathPool = new[]
    {
        @"C:\Games\Wii\Mount",
        @"D:\Backup\Store",
        @"E:\Data\Images",
        @"F:\Roms\GC",
        @"G:\Archive\Wii",
        @"H:\Temp\Mount",
        @"I:\NKit\Output",
        @"J:\Collection\ISO",
        @"K:\Dumps\Raw",
        @"L:\Sorted\Final",
        @"M:\Extra\Path1",
        @"N:\Extra\Path2",
        @"O:\Extra\Path3",
        @"P:\Extra\Path4",
        @"Q:\Extra\Path5"
    };

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3, 2.4, 3.1, 3.3, 3.4**
    ///
    /// Property 2: MRU List Add Semantics.
    /// For any initial list of 0-10 unique path strings and any path string to add,
    /// after calling AddToMruList:
    /// (a) the path SHALL appear at index 0,
    /// (b) no duplicate of that path SHALL exist in the list (case-insensitive), and
    /// (c) all other entries SHALL maintain their relative order.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MruAdd_PathAtIndex0_NoDuplicates_RelativeOrderPreserved(
        NonNegativeInt listSizeSeed,
        NonNegativeInt contentSeed,
        NonNegativeInt pathSeed,
        bool addExistingEntry)
    {
        // Generate a random list of 0-10 unique path strings
        int listSize = listSizeSeed.Get % 11; // 0-10
        Random rng = new Random(contentSeed.Get);
        List<string> initialList = new List<string>();
        HashSet<string> usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < listSize; i++)
        {
            string path = GenerateUniquePath(rng, usedPaths);
            initialList.Add(path);
            usedPaths.Add(path);
        }

        // Generate the path to add
        string pathToAdd;
        if (addExistingEntry && initialList.Count > 0)
        {
            // Pick an existing entry (possibly with different casing to test case-insensitivity)
            int idx = pathSeed.Get % initialList.Count;
            pathToAdd = initialList[idx].ToUpperInvariant();
        }
        else
        {
            // Generate a new unique path not already in the list
            pathToAdd = GenerateUniquePath(rng, usedPaths);
        }

        // Capture the original list (excluding the entry being added) for relative order check
        List<string> originalOtherEntries = initialList
            .Where(e => !string.Equals(e, pathToAdd, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Act: call AddToMruList
        List<string> list = new List<string>(initialList);
        ConfigService.AddToMruList(list, pathToAdd, maxEntries: 10);

        // Assert (a): path is at index 0
        if (!string.Equals(list[0], pathToAdd, StringComparison.Ordinal))
            return false;

        // Assert (b): no duplicates (case-insensitive)
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in list)
        {
            if (!seen.Add(entry))
                return false; // Duplicate found
        }

        // Assert (c): relative order of other entries is preserved
        // Get the entries in the result that are not the added path
        List<string> resultOtherEntries = list
            .Where(e => !string.Equals(e, pathToAdd, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // The result other entries must be a prefix-subsequence of the original other entries
        // (entries may be evicted from the end due to capacity, but relative order is preserved)
        int origIdx = 0;
        for (int resIdx = 0; resIdx < resultOtherEntries.Count; resIdx++)
        {
            while (origIdx < originalOtherEntries.Count &&
                   !string.Equals(originalOtherEntries[origIdx], resultOtherEntries[resIdx], StringComparison.Ordinal))
            {
                origIdx++;
            }

            if (origIdx >= originalOtherEntries.Count)
                return false; // Entry not found or order violated

            origIdx++;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 2.2, 3.2**
    ///
    /// Property 3: MRU List Capacity Invariant.
    /// For any sequence of 1-50 add operations on an MRU list starting from empty,
    /// the list length shall never exceed 10 entries. The most recently added path
    /// shall be at index 0 (last entry evicted when at capacity).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MruList_NeverExceedsCapacity_And_LastAddedIsAtFront(
        NonNegativeInt sequenceSeed,
        NonNegativeInt contentSeed)
    {
        const int maxEntries = 10;

        // Generate a sequence of 1-50 paths using the seeds
        int sequenceLength = 1 + (sequenceSeed.Get % 50);
        Random rng = new Random(contentSeed.Get);
        List<string> list = new List<string>();

        string lastPath = string.Empty;
        for (int i = 0; i < sequenceLength; i++)
        {
            string path = PathPool[rng.Next(PathPool.Length)];
            ConfigService.AddToMruList(list, path, maxEntries);
            lastPath = path;
        }

        // After all additions, list length must not exceed capacity
        if (list.Count > maxEntries)
            return false;

        // The most recently added path should be at index 0
        if (list.Count == 0)
            return false;

        if (!string.Equals(list[0], lastPath, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 7.3, 7.4**
    ///
    /// Property 6: Corrupt Config Graceful Fallback (random bytes).
    /// For any random byte array written to the config file path, the ConfigService SHALL
    /// initialize with default values: empty MountPathHistory, empty DataStoreHistory,
    /// null WindowState, and GetExportFormat returns null for any key.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CorruptConfig_RandomBytes_FallsBackToDefaults(byte[] randomBytes, NonEmptyString queryKey)
    {
        string configPath = Path.Combine(_tempDir, $"corrupt-bytes-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-{Guid.NewGuid():N}.json");

        try
        {
            // Write random bytes to the config file
            byte[] content = randomBytes ?? Array.Empty<byte>();
            File.WriteAllBytes(configPath, content);

            // Construct ConfigService — should gracefully fall back to defaults
            ConfigService service = new ConfigService(configPath, legacyPath);

            // Assert defaults
            if (service.MountPathHistory.Count != 0)
                return false;
            if (service.DataStoreHistory.Count != 0)
                return false;
            if (service.WindowState != null)
                return false;
            if (service.GetExportFormat("AnySystem", queryKey.Get) != null)
                return false;

            return true;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    /// <summary>
    /// **Validates: Requirements 7.3, 7.4**
    ///
    /// Property 6: Corrupt Config Graceful Fallback (invalid JSON strings).
    /// For any random string that is not valid AppConfig JSON, when written to the config path,
    /// the ConfigService SHALL initialize with default values.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CorruptConfig_InvalidStrings_FallsBackToDefaults(NonEmptyString randomContent, NonNegativeInt seed)
    {
        // Corrupt the content to ensure it's not valid AppConfig JSON
        // by prepending garbage characters
        string[] corruptPrefixes = new[] { "{{{{", "GARBAGE:", "[[[", "<!DOCTYPE", "\x00\x01\x02", "null", "12345", "\"str\"" };
        string prefix = corruptPrefixes[seed.Get % corruptPrefixes.Length];
        string invalidContent = prefix + randomContent.Get;

        string configPath = Path.Combine(_tempDir, $"corrupt-str-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-str-{Guid.NewGuid():N}.json");

        try
        {
            // Write invalid content to the config file
            File.WriteAllText(configPath, invalidContent);

            // Construct ConfigService — should gracefully fall back to defaults
            ConfigService service = new ConfigService(configPath, legacyPath);

            // Assert defaults
            if (service.MountPathHistory.Count != 0)
                return false;
            if (service.DataStoreHistory.Count != 0)
                return false;
            if (service.WindowState != null)
                return false;
            if (service.GetExportFormat("AnySystem", randomContent.Get) != null)
                return false;

            return true;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    // Pool of system names for generating export format test data
    private static readonly string[] Systems = { "GameCube", "Wii", "WiiU", "Switch", "N64", "SNES" };

    // Pool of source format names
    private static readonly string[] SourceFormats = { "ISO", "NKit", "WBFS", "CISO", "RVZ", "WUX", "APP", "GCZ" };

    // Pool of target format names
    private static readonly string[] TargetFormats = { "ISO", "NKit", "WBFS", "CISO", "RVZ", "WUX", "APP", "GCZ", "WIA" };

    /// <summary>
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 9.2**
    ///
    /// Property 4: Export Format Preference Round-Trip.
    /// For any set of (system, sourceFormat, targetFormat) triples where each (system, sourceFormat)
    /// pair is unique, after setting all preferences via SetExportFormat, calling GetExportFormat
    /// with each (system, sourceFormat) key returns the corresponding targetFormat.
    /// Keys that were never set return null.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ExportFormatPreference_RoundTrip_ReturnsCorrectValues(
        NonNegativeInt countSeed,
        NonNegativeInt contentSeed)
    {
        // Generate 1-20 entries with unique (system, sourceFormat) keys
        int entryCount = 1 + (countSeed.Get % 20);
        Random rng = new Random(contentSeed.Get);

        // Generate unique (system, sourceFormat) pairs with random target formats
        List<(string System, string SourceFormat, string TargetFormat)> triples = new List<(string System, string SourceFormat, string TargetFormat)>();
        HashSet<string> usedKeys = new HashSet<string>();

        for (int i = 0; i < entryCount; i++)
        {
            string system;
            string sourceFormat;
            string key;

            // Keep generating until we get a unique (system, sourceFormat) pair
            int attempts = 0;
            do
            {
                system = Systems[rng.Next(Systems.Length)];
                sourceFormat = SourceFormats[rng.Next(SourceFormats.Length)];
                key = $"{system}|{sourceFormat}";
                attempts++;
                if (attempts > 100) break; // Safety valve
            } while (usedKeys.Contains(key));

            if (usedKeys.Contains(key))
                break; // Exhausted unique combinations for this seed

            usedKeys.Add(key);
            string targetFormat = TargetFormats[rng.Next(TargetFormats.Length)];
            triples.Add((system, sourceFormat, targetFormat));
        }

        if (triples.Count == 0)
            return true; // Degenerate case, trivially passes

        // Create isolated service with temp paths
        string configPath = Path.Combine(_tempDir, $"cfg-export-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-export-{Guid.NewGuid():N}.json");
        ConfigService service = new ConfigService(configPath, legacyPath);

        // Set all export format preferences
        foreach ((string system, string sourceFormat, string targetFormat) in triples)
        {
            service.SetExportFormat(system, sourceFormat, targetFormat);
        }

        // Assert: GetExportFormat returns the correct targetFormat for each set key
        foreach ((string system, string sourceFormat, string targetFormat) in triples)
        {
            string result = service.GetExportFormat(system, sourceFormat);
            if (result != targetFormat)
                return false;
        }

        // Assert: GetExportFormat for a key that was never set returns null
        string unsetResult = service.GetExportFormat("NeverSetSystem", "NeverSetFormat");
        if (unsetResult != null)
            return false;

        return true;
    }

    /// <summary>
    /// Generates a unique path string that is not already in the usedPaths set.
    /// </summary>
    private static string GenerateUniquePath(Random rng, HashSet<string> usedPaths)
    {
        string path;
        do
        {
            char drive = (char)('C' + rng.Next(0, 5)); // C-G
            int depth = rng.Next(1, 4);
            string[] segments = new string[depth];
            for (int d = 0; d < depth; d++)
            {
                segments[d] = $"Dir{rng.Next(0, 100)}";
            }
            path = $@"{drive}:\{string.Join(@"\", segments)}";
        } while (usedPaths.Contains(path));

        return path;
    }
}
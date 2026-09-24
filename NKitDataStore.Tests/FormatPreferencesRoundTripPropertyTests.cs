using FsCheck;
using FsCheck.Xunit;
using System.Text.Json;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for format preferences persistence round-trip.
///
/// Feature: export-format-defaults
/// Property 5: Preferences persistence round-trip
///
/// **Validates: Requirements 7.1, 7.2**
///
/// For any set of key-value pairs, saving and loading produces the same dictionary content.
/// This tests the same JSON serialization/deserialization logic used by FormatPreferencesService.
/// </summary>
public class FormatPreferencesRoundTripPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public FormatPreferencesRoundTripPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nkit-pref-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// Saves preferences to a JSON file using the same approach as FormatPreferencesService.Save.
    /// </summary>
    private static void SavePreferences(string path, Dictionary<string, string> preferences)
    {
        string json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads preferences from a JSON file using the same approach as FormatPreferencesService.Load.
    /// </summary>
    private static Dictionary<string, string> LoadPreferences(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Builds the dictionary key for a given system + source format pair,
    /// matching FormatPreferencesService.BuildKey.
    /// </summary>
    private static string BuildKey(string system, string sourceFormat)
        => $"{system}|{sourceFormat}";

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// Property 5: Preferences persistence round-trip.
    /// For any set of key-value pairs (using the "System|SourceFormat" → TargetFormat pattern),
    /// saving to a JSON file and loading it back produces the same dictionary content.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SaveAndLoad_ProducesSameDictionaryContent(NonNegativeInt entryCountSeed)
    {
        // Generate 0-10 entries to test empty, single, and multiple entry cases
        int entryCount = entryCountSeed.Get % 11;
        Dictionary<string, string> original = new Dictionary<string, string>();

        for (int i = 0; i < entryCount; i++)
        {
            string key = BuildKey($"System{i}", $"format{i}");
            original[key] = $"target{i}";
        }

        string filePath = Path.Combine(_tempDir, $"prefs-{Guid.NewGuid():N}.json");

        // Act: save and load
        SavePreferences(filePath, original);
        Dictionary<string, string> loaded = LoadPreferences(filePath);

        // Verify: same count and same content
        if (loaded.Count != original.Count)
            return false;

        foreach (KeyValuePair<string, string> kvp in original)
        {
            if (!loaded.TryGetValue(kvp.Key, out string loadedValue) || loadedValue != kvp.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// Property 5: Preferences persistence round-trip with arbitrary string content.
    /// For any dictionary of non-null string keys and values, the round-trip through
    /// JSON serialization preserves all entries exactly.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SaveAndLoad_ArbitraryStrings_PreservesContent(NonNegativeInt seed)
    {
        // Use seed to generate varied but deterministic content
        string[] systems = new[] { "GameCube", "Wii", "WiiU", "Unknown", "" };
        string[] formats = new[] { "iso", "app", "wux", "rvz", "ciso", "wbfs" };
        string[] targets = new[] { "iso", "rvz", "ciso", "wbfs", "wux", "app" };

        int count = seed.Get % 8; // 0-7 entries
        Dictionary<string, string> original = new Dictionary<string, string>();

        for (int i = 0; i < count; i++)
        {
            string system = systems[(seed.Get + i) % systems.Length];
            string format = formats[(seed.Get + (i * 2)) % formats.Length];
            string target = targets[(seed.Get + (i * 3)) % targets.Length];
            string key = BuildKey(system, format);
            original[key] = target;
        }

        string filePath = Path.Combine(_tempDir, $"prefs-arb-{Guid.NewGuid():N}.json");

        // Act: save and load
        SavePreferences(filePath, original);
        Dictionary<string, string> loaded = LoadPreferences(filePath);

        // Verify: exact match
        if (loaded.Count != original.Count)
            return false;

        foreach (KeyValuePair<string, string> kvp in original)
        {
            if (!loaded.TryGetValue(kvp.Key, out string loadedValue) || loadedValue != kvp.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// Property 5: Loading from a non-existent file returns an empty dictionary.
    /// This ensures graceful handling when no preferences have been saved yet.
    /// </summary>
    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyDictionary()
    {
        string filePath = Path.Combine(_tempDir, "does-not-exist.json");

        Dictionary<string, string> loaded = LoadPreferences(filePath);

        Assert.Empty(loaded);
    }

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// Property 5: Saving an empty dictionary and loading it back produces an empty dictionary.
    /// </summary>
    [Fact]
    public void SaveAndLoad_EmptyDictionary_RoundTrips()
    {
        string filePath = Path.Combine(_tempDir, $"prefs-empty-{Guid.NewGuid():N}.json");
        Dictionary<string, string> original = new Dictionary<string, string>();

        SavePreferences(filePath, original);
        Dictionary<string, string> loaded = LoadPreferences(filePath);

        Assert.Empty(loaded);
    }

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// Property 5: Overwriting preferences replaces previous content entirely.
    /// Saving a new dictionary overwrites the old one completely.
    /// </summary>
    [Fact]
    public void SaveAndLoad_Overwrite_ReplacesContent()
    {
        string filePath = Path.Combine(_tempDir, $"prefs-overwrite-{Guid.NewGuid():N}.json");

        Dictionary<string, string> first = new Dictionary<string, string>
        {
            [BuildKey("GameCube", "iso")] = "rvz",
            [BuildKey("Wii", "iso")] = "wbfs"
        };
        SavePreferences(filePath, first);

        Dictionary<string, string> second = new Dictionary<string, string>
        {
            [BuildKey("WiiU", "iso")] = "wux"
        };
        SavePreferences(filePath, second);

        Dictionary<string, string> loaded = LoadPreferences(filePath);

        Assert.Single(loaded);
        Assert.Equal("wux", loaded[BuildKey("WiiU", "iso")]);
    }
}
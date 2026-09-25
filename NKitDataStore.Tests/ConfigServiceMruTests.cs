using NkdsUi.Services;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for ConfigService MRU list behavior.
/// Tests the internal static AddToMruList helper directly.
///
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4
/// </summary>
public class ConfigServiceMruTests
{
    [Fact]
    public void AddToMruList_EmptyList_PutsEntryAtIndexZero()
    {
        List<string> list = new List<string>();

        ConfigService.AddToMruList(list, @"C:\Games\Mount", maxEntries: 10);

        Assert.Single(list);
        Assert.Equal(@"C:\Games\Mount", list[0]);
    }

    [Fact]
    public void AddToMruList_DuplicateEntry_MovesToFrontWithoutDuplicates()
    {
        List<string> list = new List<string> { @"C:\First", @"C:\Second", @"C:\Third" };

        ConfigService.AddToMruList(list, @"C:\Third", maxEntries: 10);

        Assert.Equal(3, list.Count);
        Assert.Equal(@"C:\Third", list[0]);
        Assert.Equal(@"C:\First", list[1]);
        Assert.Equal(@"C:\Second", list[2]);
    }

    [Fact]
    public void AddToMruList_BeyondCapacity_EvictsOldestEntry()
    {
        List<string> list = new List<string>();
        for (int i = 1; i <= 10; i++)
            list.Add($@"C:\Path{i}");

        // List is at capacity (10). Adding a new unique entry should evict the oldest (Path10).
        ConfigService.AddToMruList(list, @"C:\NewPath", maxEntries: 10);

        Assert.Equal(10, list.Count);
        Assert.Equal(@"C:\NewPath", list[0]);
        Assert.DoesNotContain(@"C:\Path10", list);
    }

    [Fact]
    public void AddToMruList_RelativeOrderPreserved_WhenNewEntryAdded()
    {
        List<string> list = new List<string> { @"C:\A", @"C:\B", @"C:\C", @"C:\D" };

        ConfigService.AddToMruList(list, @"C:\New", maxEntries: 10);

        // New entry at front, existing entries maintain relative order
        Assert.Equal(@"C:\New", list[0]);
        Assert.Equal(@"C:\A", list[1]);
        Assert.Equal(@"C:\B", list[2]);
        Assert.Equal(@"C:\C", list[3]);
        Assert.Equal(@"C:\D", list[4]);
    }

    [Fact]
    public void AddToMruList_RelativeOrderPreserved_WhenDuplicateMovedToFront()
    {
        List<string> list = new List<string> { @"C:\A", @"C:\B", @"C:\C", @"C:\D" };

        ConfigService.AddToMruList(list, @"C:\C", maxEntries: 10);

        // C moved to front, A, B, D maintain relative order
        Assert.Equal(@"C:\C", list[0]);
        Assert.Equal(@"C:\A", list[1]);
        Assert.Equal(@"C:\B", list[2]);
        Assert.Equal(@"C:\D", list[3]);
        Assert.Equal(4, list.Count);
    }

    [Fact]
    public void AddToMruList_CaseInsensitiveDeduplication()
    {
        List<string> list = new List<string> { @"C:\Path", @"D:\Other" };

        // Add same path with different casing — should deduplicate
        ConfigService.AddToMruList(list, @"c:\path", maxEntries: 10);

        Assert.Equal(2, list.Count);
        Assert.Equal(@"c:\path", list[0]); // New casing is used
        Assert.Equal(@"D:\Other", list[1]);
    }

    [Fact]
    public void AddToMruList_CaseInsensitiveDeduplication_MixedCase()
    {
        List<string> list = new List<string> { @"C:\Games\Wii", @"D:\Backup", @"E:\Data" };

        // "c:\games\wii" should match "C:\Games\Wii" case-insensitively
        ConfigService.AddToMruList(list, @"c:\games\wii", maxEntries: 10);

        Assert.Equal(3, list.Count);
        Assert.Equal(@"c:\games\wii", list[0]);
        Assert.DoesNotContain(@"C:\Games\Wii", list);
    }

    [Fact]
    public void AddToMruList_CapacityEviction_RemovesMultipleIfNeeded()
    {
        // Start with a list already over capacity (edge case for robustness)
        List<string> list = new List<string>();
        for (int i = 1; i <= 12; i++)
            list.Add($@"C:\Path{i}");

        ConfigService.AddToMruList(list, @"C:\Fresh", maxEntries: 10);

        Assert.Equal(10, list.Count);
        Assert.Equal(@"C:\Fresh", list[0]);
    }

    [Fact]
    public void AddToMruList_SingleCapacity_AlwaysReplacesEntry()
    {
        List<string> list = new List<string> { @"C:\Old" };

        ConfigService.AddToMruList(list, @"C:\New", maxEntries: 1);

        Assert.Single(list);
        Assert.Equal(@"C:\New", list[0]);
    }
}
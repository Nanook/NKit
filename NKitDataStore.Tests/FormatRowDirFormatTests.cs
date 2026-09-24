using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for FormatRowViewModel behavior when SelectedTargetFormat is "dir".
///
/// Feature: directory-export
///
/// **Validates: Requirements 2.1, 2.2, 3.1**
/// </summary>
public class FormatRowDirFormatTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public FormatRowDirFormatTests()
    {
        EnsureReactiveUIInitialized();
    }

    private static void EnsureReactiveUIInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            try
            {
                RxAppBuilder.CreateReactiveUIBuilder()
                    .WithCoreServices()
                    .BuildApp();
            }
            catch (InvalidOperationException)
            {
                // Already initialized — safe to ignore
            }
            _initialized = true;
        }
    }

    /// <summary>
    /// Creates a FormatRowViewModel configured for the Directories/folder combination
    /// with "dir" as the only target format (matching FormatMappings output).
    /// </summary>
    private static FormatRowViewModel CreateDirFormatRow()
    {
        IReadOnlyList<string> targetFormats = FormatMappings.GetTargetFormats("Directories", "folder");
        return new FormatRowViewModel("Directories", "folder", targetFormats);
    }

    /// <summary>
    /// **Validates: Requirement 2.1**
    ///
    /// When SelectedTargetFormat is "dir", HasFormatOptions should be false
    /// because "dir" is not a format with configurable options (not rvz/ciso/wbfs).
    /// </summary>
    [Fact]
    public void DirFormat_HasFormatOptions_IsFalse()
    {
        FormatRowViewModel row = CreateDirFormatRow();

        Assert.False(row.HasFormatOptions);
    }

    /// <summary>
    /// **Validates: Requirement 2.2**
    ///
    /// When SelectedTargetFormat is "dir", IsRvzOptionsVisible should be false
    /// because "dir" is not the "rvz" format.
    /// </summary>
    [Fact]
    public void DirFormat_IsRvzOptionsVisible_IsFalse()
    {
        FormatRowViewModel row = CreateDirFormatRow();

        Assert.False(row.IsRvzOptionsVisible);
    }

    /// <summary>
    /// **Validates: Requirement 2.2**
    ///
    /// When SelectedTargetFormat is "dir", IsLosslessVisible should be false
    /// because "dir" is not "ciso" or "wbfs".
    /// </summary>
    [Fact]
    public void DirFormat_IsLosslessVisible_IsFalse()
    {
        FormatRowViewModel row = CreateDirFormatRow();

        Assert.False(row.IsLosslessVisible);
    }

    /// <summary>
    /// **Validates: Requirement 3.1**
    ///
    /// When SelectedTargetFormat is "dir", GetConvertFormatString() should return "dir"
    /// because it falls through to the default case which returns SelectedTargetFormat unchanged.
    /// </summary>
    [Fact]
    public void DirFormat_GetConvertFormatString_ReturnsDir()
    {
        FormatRowViewModel row = CreateDirFormatRow();

        string result = row.GetConvertFormatString();

        Assert.Equal("dir", result);
    }

    /// <summary>
    /// **Validates: Requirements 3.1**
    ///
    /// Property 1: Non-special format string pass-through
    /// For any format string that is NOT "rvz", "wbfs", or "ciso",
    /// calling GetConvertFormatString() on a FormatRowViewModel with that format
    /// as SelectedTargetFormat should return the format string unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonSpecialFormat_GetConvertFormatString_ReturnsUnchanged(NonEmptyString formatWrapper)
    {
        string formatString = formatWrapper.Get.ToLowerInvariant();

        // Exclude the special formats that have custom handling
        if (formatString is "rvz" or "wbfs" or "ciso")
            return true; // vacuously true for special formats

        FormatRowViewModel row = new FormatRowViewModel("TestSystem", "testformat", new[] { formatString });

        string result = row.GetConvertFormatString();

        return result == formatString;
    }

    /// <summary>
    /// **Validates: Requirements 1.2, 9.1, 9.2**
    ///
    /// Property 5: Format row isolation
    /// For any set of (system, sourceFormat) pairs that includes ("Directories", "folder"),
    /// when FormatRowViewModels are created for each pair using FormatMappings.GetTargetFormats(),
    /// the "dir" format should ONLY appear in the row for ("Directories", "folder") and not in
    /// any other row. Additionally, each row's TargetFormats must match FormatMappings.GetTargetFormats() exactly.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FormatRowIsolation_DirOnlyAppearsInDirectoriesRow(PositiveInt extraPairCount)
    {
        // Known systems and formats to generate realistic random pairs (excluding "Directories"/"folder")
        string[] knownSystems = new[] { "GameCube", "Wii", "WiiU", "PlayStation", "Xbox", "Sega" };
        string[] knownFormats = new[] { "iso", "app", "bin", "cue", "nrg", "mdf" };

        Random random = new Random(extraPairCount.Get);
        List<(string System, string SourceFormat)> pairs = new List<(string System, string SourceFormat)>();

        // Always include the Directories/folder pair
        pairs.Add(("Directories", "folder"));

        // Add random extra pairs (1 to extraPairCount, capped at 10 for reasonable test size)
        int count = Math.Min(extraPairCount.Get, 10);
        for (int i = 0; i < count; i++)
        {
            string system = knownSystems[random.Next(knownSystems.Length)];
            string format = knownFormats[random.Next(knownFormats.Length)];
            pairs.Add((system, format));
        }

        // Deduplicate pairs (Export Dialog shows one row per distinct pair)
        List<(string System, string SourceFormat)> distinctPairs = pairs
            .GroupBy(p => (p.System.ToLowerInvariant(), p.SourceFormat.ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();

        // Create FormatRowViewModels for each distinct pair
        List<FormatRowViewModel> rows = distinctPairs.Select(p =>
        {
            IReadOnlyList<string> targetFormats = FormatMappings.GetTargetFormats(p.System, p.SourceFormat);
            return new FormatRowViewModel(p.System, p.SourceFormat, targetFormats);
        }).ToList();

        // Verify: each row's TargetFormats matches FormatMappings.GetTargetFormats() exactly
        foreach (FormatRowViewModel row in rows)
        {
            IReadOnlyList<string> expected = FormatMappings.GetTargetFormats(row.System, row.SourceFormat);
            if (!row.TargetFormats.SequenceEqual(expected))
                return false;
        }

        // Verify: "dir" only appears in the Directories/folder row
        foreach (FormatRowViewModel row in rows)
        {
            bool isDirectoriesRow = string.Equals(row.System, "Directories", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(row.SourceFormat, "folder", StringComparison.OrdinalIgnoreCase);
            bool containsDir = row.TargetFormats.Contains("dir");

            if (isDirectoriesRow && !containsDir)
                return false; // Directories row must contain "dir"

            if (!isDirectoriesRow && containsDir)
                return false; // Non-Directories rows must NOT contain "dir"
        }

        return true;
    }
}
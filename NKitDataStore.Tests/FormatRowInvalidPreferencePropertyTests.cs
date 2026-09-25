using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for FormatRowViewModel invalid persisted preference fallback.
///
/// Feature: export-format-defaults, Property 6: Invalid persisted preference falls back to first option
/// **Validates: Requirements 7.3**
/// </summary>
public class FormatRowInvalidPreferencePropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public FormatRowInvalidPreferencePropertyTests()
    {
        EnsureReactiveUIInitialized();
    }

    private static void EnsureReactiveUIInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _initialized = true;
        }
    }

    /// <summary>
    /// Known valid format strings used in the application.
    /// </summary>
    private static readonly string[] KnownFormats = ["iso", "rvz", "ciso", "wbfs", "wux", "app", "gcz", "nkit"];

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// When defaultSelection is a string not in targetFormats,
    /// SelectedTargetFormat equals targetFormats[0].
    /// </summary>
    [Fact]
    public void DefaultSelection_NotInList_FallsBackToFirstItem()
    {
        List<string> targetFormats = new List<string> { "iso", "rvz", "ciso", "wbfs" };
        string invalidDefault = "nkit";

        FormatRowViewModel vm = new FormatRowViewModel("GameCube", "iso", targetFormats, invalidDefault);

        Assert.Equal("iso", vm.SelectedTargetFormat);
    }

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// When defaultSelection is an empty string (not in targetFormats),
    /// SelectedTargetFormat equals targetFormats[0].
    /// </summary>
    [Fact]
    public void DefaultSelection_EmptyString_FallsBackToFirstItem()
    {
        List<string> targetFormats = new List<string> { "iso", "rvz", "ciso", "wbfs" };

        FormatRowViewModel vm = new FormatRowViewModel("Wii", "iso", targetFormats, "");

        Assert.Equal("iso", vm.SelectedTargetFormat);
    }

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// Property 6: For any non-empty targetFormats list and any defaultSelection NOT in that list,
    /// SelectedTargetFormat equals the first element.
    ///
    /// Uses deterministic generation: picks a subset of known formats as the target list,
    /// then generates a default selection string guaranteed not to be in that list.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool InvalidPersistedPreference_AlwaysFallsBackToFirstOption(
        PositiveInt listSizeSeed, NonNegativeInt offsetSeed, NonEmptyString invalidSuffix)
    {
        // Build a non-empty target formats list from known formats
        int listSize = (listSizeSeed.Get % KnownFormats.Length) + 1; // 1..KnownFormats.Length
        int offset = offsetSeed.Get % KnownFormats.Length;
        List<string> targetFormats = new List<string>();
        for (int i = 0; i < listSize; i++)
        {
            string format = KnownFormats[(offset + i) % KnownFormats.Length];
            if (!targetFormats.Contains(format))
                targetFormats.Add(format);
        }

        if (targetFormats.Count == 0)
            return true; // vacuously true

        // Generate an invalid default by appending a suffix that ensures it's not in the list
        string invalidDefault = "INVALID_" + invalidSuffix.Get;

        // Ensure it's truly not in the list (it shouldn't be since we prefix with INVALID_)
        if (targetFormats.Contains(invalidDefault))
            return true; // vacuously true for this edge case

        FormatRowViewModel vm = new FormatRowViewModel("TestSystem", "testformat", targetFormats, invalidDefault);

        return vm.SelectedTargetFormat == targetFormats[0];
    }

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// Property 6 (supplementary): When the persisted preference is null,
    /// SelectedTargetFormat equals the first element (null is treated as no preference).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NullPersistedPreference_FallsBackToFirstOption(
        PositiveInt listSizeSeed, NonNegativeInt offsetSeed)
    {
        int listSize = (listSizeSeed.Get % KnownFormats.Length) + 1;
        int offset = offsetSeed.Get % KnownFormats.Length;
        List<string> targetFormats = new List<string>();
        for (int i = 0; i < listSize; i++)
        {
            string format = KnownFormats[(offset + i) % KnownFormats.Length];
            if (!targetFormats.Contains(format))
                targetFormats.Add(format);
        }

        if (targetFormats.Count == 0)
            return true;

        FormatRowViewModel vm = new FormatRowViewModel("TestSystem", "testformat", targetFormats, null);

        return vm.SelectedTargetFormat == targetFormats[0];
    }
}
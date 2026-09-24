using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for FormatRowViewModel default selection behavior.
///
/// Feature: export-format-defaults
/// Property 3: Default selection is first option
///
/// **Validates: Requirements 4.1**
///
/// For any FormatRow created without a valid persisted preference,
/// SelectedTargetFormat equals the first element of TargetFormats.
/// </summary>
public class FormatRowDefaultSelectionPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public FormatRowDefaultSelectionPropertyTests()
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

    private static readonly string[] KnownSystems = ["GameCube", "Wii", "WiiU"];
    private static readonly string[] KnownSourceFormats = ["iso", "app"];

    /// <summary>
    /// **Validates: Requirements 4.1**
    ///
    /// Property 3: When defaultSelection is null, SelectedTargetFormat equals targetFormats[0].
    /// For any known (System, SourceFormat) pair, creating a FormatRowViewModel with
    /// defaultSelection = null results in SelectedTargetFormat being the first target format.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NullDefault_SelectsFirstOption(NonNegativeInt systemIndex, NonNegativeInt formatIndex)
    {
        string system = KnownSystems[systemIndex.Get % KnownSystems.Length];
        string sourceFormat = KnownSourceFormats[formatIndex.Get % KnownSourceFormats.Length];
        IReadOnlyList<string> targetFormats = FormatMappings.GetTargetFormats(system, sourceFormat);

        FormatRowViewModel row = new FormatRowViewModel(system, sourceFormat, targetFormats, defaultSelection: null);

        return row.SelectedTargetFormat == targetFormats[0];
    }

    /// <summary>
    /// **Validates: Requirements 4.1**
    ///
    /// Property 3: When defaultSelection is a value NOT in the targetFormats list,
    /// SelectedTargetFormat equals targetFormats[0].
    /// For any arbitrary string that is not contained in the target formats,
    /// the ViewModel falls back to the first option.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool InvalidDefault_SelectsFirstOption(NonEmptyString invalidDefault, NonNegativeInt systemIndex)
    {
        string system = KnownSystems[systemIndex.Get % KnownSystems.Length];
        string sourceFormat = "iso";
        IReadOnlyList<string> targetFormats = FormatMappings.GetTargetFormats(system, sourceFormat);

        // Only test when the generated string is NOT in the target formats list
        if (targetFormats.Contains(invalidDefault.Get))
            return true; // vacuously true — skip known-valid values

        FormatRowViewModel row = new FormatRowViewModel(system, sourceFormat, targetFormats, defaultSelection: invalidDefault.Get);

        return row.SelectedTargetFormat == targetFormats[0];
    }

    /// <summary>
    /// **Validates: Requirements 4.1**
    ///
    /// Property 3: For any non-empty list of target formats and a null default,
    /// the selected format is always the first element.
    /// Uses arbitrary target format lists to verify the property holds universally.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ArbitraryTargetFormats_NullDefault_SelectsFirst(NonEmptyString first, NonNegativeInt extraCount)
    {
        // Build a target formats list with 1-5 elements
        int count = (extraCount.Get % 5) + 1;
        List<string> formats = new List<string> { first.Get };
        for (int i = 1; i < count; i++)
            formats.Add($"format{i}");

        FormatRowViewModel row = new FormatRowViewModel("TestSystem", "testformat", formats, defaultSelection: null);

        return row.SelectedTargetFormat == formats[0];
    }

    /// <summary>
    /// **Validates: Requirements 4.1**
    ///
    /// Property 3: For any non-empty list of target formats and a defaultSelection
    /// that is not in the list, the selected format is always the first element.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ArbitraryTargetFormats_NotInListDefault_SelectsFirst(
        NonEmptyString first, NonNegativeInt extraCount, NonEmptyString defaultVal)
    {
        int count = (extraCount.Get % 5) + 1;
        List<string> formats = new List<string> { first.Get };
        for (int i = 1; i < count; i++)
            formats.Add($"format{i}");

        // Only test when the default is NOT in the list
        if (formats.Contains(defaultVal.Get))
            return true; // vacuously true

        FormatRowViewModel row = new FormatRowViewModel("TestSystem", "testformat", formats, defaultSelection: defaultVal.Get);

        return row.SelectedTargetFormat == formats[0];
    }
}
using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for ExportDialogViewModel.InitializeFromImages FormatRow computation.
///
/// Feature: export-format-defaults
/// Property 1: FormatRow computation produces exactly the distinct pairs
///
/// **Validates: Requirements 1.1, 2.1, 2.2**
///
/// For any list of selected images, FormatRows contains exactly one row per unique
/// (System, SourceFormat) combination present in the input, and no additional rows.
/// </summary>
public class FormatRowComputationPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public FormatRowComputationPropertyTests()
    {
        EnsureReactiveUIInitialized();
    }

    /// <summary>
    /// Ensures ReactiveUI is initialized once for the test assembly.
    /// </summary>
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
    private static readonly ImageFormat[] KnownFormats = [ImageFormat.Iso, ImageFormat.App];

    /// <summary>
    /// Helper to create an ImageRowViewModel with the given system and format.
    /// </summary>
    private static ImageRowViewModel CreateImage(string system, ImageFormat format, long id = 1)
    {
        ImageRecord record = new ImageRecord
        {
            Id = id,
            Name = $"test-{id}",
            System = system,
            Format = format,
            SetName = "TestSet",
            Size = 1024
        };
        return new ImageRowViewModel(record, "test-session");
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: For any list of images drawn from known systems and formats,
    /// InitializeFromImages produces exactly one FormatRow per unique (System, SourceFormat) pair.
    /// The count of FormatRows equals the count of distinct pairs in the input.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FormatRows_CountEqualsDistinctPairs(NonNegativeInt imageCount, NonNegativeInt seed)
    {
        int count = imageCount.Get % 20; // 0-19 images
        Random rng = new Random(seed.Get);

        List<ImageRowViewModel> images = new List<ImageRowViewModel>();
        for (int i = 0; i < count; i++)
        {
            string system = KnownSystems[rng.Next(KnownSystems.Length)];
            ImageFormat format = KnownFormats[rng.Next(KnownFormats.Length)];
            images.Add(CreateImage(system, format, id: i + 1));
        }

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(images);

        List<(string System, string SourceFormat)> expectedPairs = images
            .Select(img => (System: img.System ?? "Unknown", SourceFormat: img.Format.ToString().ToLowerInvariant()))
            .Distinct()
            .ToList();

        return vm.FormatRows.Count == expectedPairs.Count;
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: For any list of images, every distinct (System, SourceFormat) pair
    /// from the input has exactly one corresponding FormatRow.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FormatRows_ContainsAllDistinctPairs(NonNegativeInt imageCount, NonNegativeInt seed)
    {
        int count = imageCount.Get % 20;
        Random rng = new Random(seed.Get);

        List<ImageRowViewModel> images = new List<ImageRowViewModel>();
        for (int i = 0; i < count; i++)
        {
            string system = KnownSystems[rng.Next(KnownSystems.Length)];
            ImageFormat format = KnownFormats[rng.Next(KnownFormats.Length)];
            images.Add(CreateImage(system, format, id: i + 1));
        }

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(images);

        HashSet<(string System, string SourceFormat)> expectedPairs = images
            .Select(img => (System: img.System ?? "Unknown", SourceFormat: img.Format.ToString().ToLowerInvariant()))
            .Distinct()
            .ToHashSet();

        HashSet<(string System, string SourceFormat)> actualPairs = vm.FormatRows
            .Select(r => (System: r.System, SourceFormat: r.SourceFormat))
            .ToHashSet();

        return expectedPairs.SetEquals(actualPairs);
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: FormatRows contains no duplicate (System, SourceFormat) pairs.
    /// Each row is unique.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FormatRows_NoDuplicatePairs(NonNegativeInt imageCount, NonNegativeInt seed)
    {
        int count = (imageCount.Get % 20) + 1; // 1-20 images (at least 1)
        Random rng = new Random(seed.Get);

        List<ImageRowViewModel> images = new List<ImageRowViewModel>();
        for (int i = 0; i < count; i++)
        {
            string system = KnownSystems[rng.Next(KnownSystems.Length)];
            ImageFormat format = KnownFormats[rng.Next(KnownFormats.Length)];
            images.Add(CreateImage(system, format, id: i + 1));
        }

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(images);

        List<(string System, string SourceFormat)> pairs = vm.FormatRows
            .Select(r => (r.System, r.SourceFormat))
            .ToList();

        return pairs.Count == pairs.Distinct().Count();
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: When all images share the same (System, SourceFormat),
    /// exactly one FormatRow is produced regardless of image count.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AllSameSystemFormat_ProducesOneRow(
        NonNegativeInt systemIndex, NonNegativeInt formatIndex, NonNegativeInt imageCount)
    {
        int count = (imageCount.Get % 10) + 1; // 1-10 images
        string system = KnownSystems[systemIndex.Get % KnownSystems.Length];
        ImageFormat format = KnownFormats[formatIndex.Get % KnownFormats.Length];

        List<ImageRowViewModel> images = Enumerable.Range(1, count)
            .Select(i => CreateImage(system, format, id: i))
            .ToList();

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(images);

        return vm.FormatRows.Count == 1
            && vm.FormatRows[0].System == system
            && vm.FormatRows[0].SourceFormat == format.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: When no images are provided, FormatRows is empty.
    /// </summary>
    [Fact]
    public void EmptyImages_ProducesNoRows()
    {
        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages([]);

        Assert.Empty(vm.FormatRows);
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: Images with null System are treated as "Unknown" system.
    /// Multiple null-system images with the same format produce one row with System = "Unknown".
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NullSystem_TreatedAsUnknown(NonNegativeInt imageCount)
    {
        int count = (imageCount.Get % 5) + 1;
        List<ImageRowViewModel> images = Enumerable.Range(1, count)
            .Select(i => CreateImage(null, ImageFormat.Iso, id: i))
            .ToList();

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(images);

        return vm.FormatRows.Count == 1
            && vm.FormatRows[0].System == "Unknown";
    }
}
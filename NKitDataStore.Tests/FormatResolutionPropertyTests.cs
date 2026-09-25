using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for ExportDialogViewModel.ResolveFormatForImage.
///
/// Feature: export-format-defaults
/// Property 4: Image-to-format resolution uses matching row
///
/// **Validates: Requirements 5.1, 5.2, 5.3**
///
/// For any set of images and FormatRow selections, ResolveFormatForImage returns
/// the SelectedTargetFormat of the FormatRow that matches the image's (System, SourceFormat).
/// </summary>
public class FormatResolutionPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public FormatResolutionPropertyTests()
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

    /// <summary>
    /// Helper to create an ImageRowViewModel with the given system and format.
    /// </summary>
    private static ImageRowViewModel CreateImage(long id, string system, ImageFormat format)
    {
        ImageRecord record = new ImageRecord
        {
            Id = id,
            Name = $"image_{id}",
            System = system,
            Format = format,
            SetName = "TestSet"
        };
        return new ImageRowViewModel(record, "test-session");
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    ///
    /// Property 4: For any known system with iso format, after InitializeFromImages,
    /// ResolveFormatForImage returns the SelectedTargetFormat of the matching FormatRow.
    /// The SelectedTargetFormat defaults to the first target format option.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ResolveFormatForImage_ReturnsMatchingRowSelectedFormat(
        NonNegativeInt systemIndex, NonNegativeInt imageCount)
    {
        string system = KnownSystems[systemIndex.Get % KnownSystems.Length];
        ImageFormat format = ImageFormat.Iso;
        int count = (imageCount.Get % 5) + 1;

        // Create multiple images with the same system/format
        List<ImageRowViewModel> images = new List<ImageRowViewModel>();
        for (int i = 0; i < count; i++)
            images.Add(CreateImage(i + 1, system, format));

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(images);

        // The matching row should exist
        string expectedFormat = vm.FormatRows
            .First(r => string.Equals(r.System, system, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.SourceFormat, format.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            .SelectedTargetFormat;

        // Every image should resolve to that row's SelectedTargetFormat
        return images.All(img => vm.ResolveFormatForImage(img) == expectedFormat);
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    ///
    /// Property 4: After changing a FormatRow's SelectedTargetFormat, ResolveFormatForImage
    /// returns the updated value for images matching that row.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ResolveFormatForImage_ReflectsChangedSelection(
        NonNegativeInt systemIndex, NonNegativeInt targetIndex)
    {
        string system = KnownSystems[systemIndex.Get % KnownSystems.Length];
        ImageFormat format = ImageFormat.Iso;

        ImageRowViewModel image = CreateImage(1, system, format);
        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(new[] { image });

        // Find the matching row and change its selection
        FormatRowViewModel row = vm.FormatRows.First(r =>
            string.Equals(r.System, system, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.SourceFormat, "iso", StringComparison.OrdinalIgnoreCase));

        // Pick a target format from the available options
        string newTarget = row.TargetFormats[targetIndex.Get % row.TargetFormats.Count];
        row.SelectedTargetFormat = newTarget;

        // ResolveFormatForImage should return the updated selection
        return vm.ResolveFormatForImage(image) == newTarget;
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    ///
    /// Property 4: With multiple distinct (System, SourceFormat) pairs, each image resolves
    /// to the SelectedTargetFormat of its own matching row, not another row's.
    /// </summary>
    [Property(MaxTest = 50)]
    public bool ResolveFormatForImage_MultipleRows_EachImageMatchesOwnRow(
        NonNegativeInt targetIdx1, NonNegativeInt targetIdx2)
    {
        // Create images for two different systems
        ImageRowViewModel gcImage = CreateImage(1, "GameCube", ImageFormat.Iso);
        ImageRowViewModel wiiImage = CreateImage(2, "Wii", ImageFormat.Iso);

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(new[] { gcImage, wiiImage });

        // Set different selections on each row
        FormatRowViewModel gcRow = vm.FormatRows.First(r => r.System == "GameCube");
        FormatRowViewModel wiiRow = vm.FormatRows.First(r => r.System == "Wii");

        string gcTarget = gcRow.TargetFormats[targetIdx1.Get % gcRow.TargetFormats.Count];
        string wiiTarget = wiiRow.TargetFormats[targetIdx2.Get % wiiRow.TargetFormats.Count];

        gcRow.SelectedTargetFormat = gcTarget;
        wiiRow.SelectedTargetFormat = wiiTarget;

        // Each image should resolve to its own row's selection
        return vm.ResolveFormatForImage(gcImage) == gcTarget &&
               vm.ResolveFormatForImage(wiiImage) == wiiTarget;
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    ///
    /// Property 4: WiiU/app images resolve to the WiiU/app row's SelectedTargetFormat,
    /// distinct from WiiU/iso images which resolve to the WiiU/iso row.
    /// </summary>
    [Fact]
    public void ResolveFormatForImage_WiiU_DistinguishesIsoFromApp()
    {
        ImageRowViewModel wiiuIsoImage = CreateImage(1, "WiiU", ImageFormat.Iso);
        ImageRowViewModel wiiuAppImage = CreateImage(2, "WiiU", ImageFormat.App);

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(new[] { wiiuIsoImage, wiiuAppImage });

        FormatRowViewModel isoRow = vm.FormatRows.First(r => r.System == "WiiU" && r.SourceFormat == "iso");
        FormatRowViewModel appRow = vm.FormatRows.First(r => r.System == "WiiU" && r.SourceFormat == "app");

        // WiiU/iso has options: iso, wux, app — select "wux"
        isoRow.SelectedTargetFormat = "wux";

        // WiiU/app has only: app — stays as "app"
        Assert.Equal("wux", vm.ResolveFormatForImage(wiiuIsoImage));
        Assert.Equal("app", vm.ResolveFormatForImage(wiiuAppImage));
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    ///
    /// Property 4: Images with null System are treated as "Unknown" and still resolve
    /// to the matching FormatRow's SelectedTargetFormat.
    /// </summary>
    [Fact]
    public void ResolveFormatForImage_NullSystem_ResolvesToUnknownRow()
    {
        ImageRowViewModel image = CreateImage(1, null, ImageFormat.Iso);

        ExportDialogViewModel vm = new ExportDialogViewModel(new NullConfigService());
        vm.InitializeFromImages(new[] { image });

        // Should have a row for "Unknown" / "iso"
        FormatRowViewModel row = vm.FormatRows.First(r => r.System == "Unknown");
        string resolved = vm.ResolveFormatForImage(image);

        Assert.Equal(row.SelectedTargetFormat, resolved);
    }
}
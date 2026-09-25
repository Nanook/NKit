namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for ImageFormat.CueFolder enum value and its GetFileExtension behavior.
///
/// Feature: cue-gdi-folder-storage
/// **Validates: Requirements 10.3**
/// </summary>
public class ImageFormatCueFolderTests
{
    /// <summary>
    /// CueFolder enum value must be 9, following the TmdAppFolder = 7, Cue = 8 pattern.
    /// </summary>
    [Fact]
    public void ImageFormat_CueFolder_HasValue9() => Assert.Equal(9, (int)ImageFormat.CueFolder);

    /// <summary>
    /// CueFolder should return empty string from GetFileExtension, matching TmdAppFolder and Folder behavior.
    /// </summary>
    [Fact]
    public void GetFileExtension_CueFolder_ReturnsEmptyString()
    {
        string result = ImageFormat.CueFolder.GetFileExtension();

        Assert.Equal("", result);
    }

    /// <summary>
    /// Verify all existing enum values remain unchanged after adding CueFolder.
    /// This guards against accidental reordering or value changes.
    /// </summary>
    [Theory]
    [InlineData(ImageFormat.Unknown, 0)]
    [InlineData(ImageFormat.Iso, 1)]
    [InlineData(ImageFormat.Bin, 2)]
    [InlineData(ImageFormat.App, 3)]
    [InlineData(ImageFormat.Cdn, 4)]
    [InlineData(ImageFormat.Gdi, 5)]
    [InlineData(ImageFormat.Folder, 6)]
    [InlineData(ImageFormat.TmdAppFolder, 7)]
    [InlineData(ImageFormat.Cue, 8)]
    [InlineData(ImageFormat.CueFolder, 9)]
    public void ImageFormat_ExistingEnumValues_RemainUnchanged(ImageFormat format, int expectedValue) => Assert.Equal(expectedValue, (int)format);

    /// <summary>
    /// Verify GetFileExtension still returns correct values for all existing formats.
    /// </summary>
    [Theory]
    [InlineData(ImageFormat.Unknown, ".iso")]
    [InlineData(ImageFormat.Iso, ".iso")]
    [InlineData(ImageFormat.Bin, ".bin")]
    [InlineData(ImageFormat.App, ".app")]
    [InlineData(ImageFormat.Cdn, ".cdn")]
    [InlineData(ImageFormat.Gdi, ".gdi")]
    [InlineData(ImageFormat.Cue, ".cue")]
    [InlineData(ImageFormat.Folder, "")]
    [InlineData(ImageFormat.TmdAppFolder, "")]
    [InlineData(ImageFormat.CueFolder, "")]
    public void GetFileExtension_AllFormats_ReturnExpectedExtensions(ImageFormat format, string expectedExtension) => Assert.Equal(expectedExtension, format.GetFileExtension());
}
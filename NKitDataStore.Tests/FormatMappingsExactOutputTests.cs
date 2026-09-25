using NkdsUi.Models;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for FormatMappings.GetTargetFormats exact outputs.
/// Tests each new (system, sourceFormat) pair returns the exact expected list.
///
/// **Validates: Requirements 7.1-7.19**
/// </summary>
public class FormatMappingsExactOutputTests
{
    #region Xbox/Xbox360 ISO → ["xiso", "iso"] (Requirements 7.4, 7.5)

    [Fact]
    public void Xbox_Iso_ReturnsXisoAndIso()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("xbox", "iso");
        Assert.Equal(new[] { "xiso", "iso" }, result);
    }

    [Fact]
    public void Xbox360_Iso_ReturnsXisoAndIso()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("xbox360", "iso");
        Assert.Equal(new[] { "xiso", "iso" }, result);
    }

    #endregion

    #region ISO9660 systems - ISO source → ["iso", "cue"] (Requirements 7.6-7.13)

    [Fact]
    public void Ps1_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("ps1", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void Ps2_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("ps2", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void Saturn_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("saturn", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void SegaCd_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("segacd", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void Psp_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("psp", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void Cdi_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("cdi", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void PcEngine_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("pcengine", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Fact]
    public void Default_Iso_ReturnsIsoAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("default", "iso");
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    #endregion

    #region CUE sources → ["cue"] (Requirements 7.1, 7.2, 7.14-7.19)

    [Fact]
    public void Default_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("default", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    [Fact]
    public void Dreamcast_Cue_ReturnsCueAndGdi()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("dreamcast", "cue");
        Assert.Equal(new[] { "cue", "gdi" }, result);
    }

    [Fact]
    public void Ps1_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("ps1", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    [Fact]
    public void Ps2_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("ps2", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    [Fact]
    public void Saturn_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("saturn", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    [Fact]
    public void SegaCd_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("segacd", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    [Fact]
    public void Cdi_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("cdi", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    [Fact]
    public void PcEngine_Cue_ReturnsCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("pcengine", "cue");
        Assert.Equal(new[] { "cue" }, result);
    }

    #endregion

    #region GDI source → ["gdi"] (Requirement 7.3)

    [Fact]
    public void Dreamcast_Gdi_ReturnsGdiAndCue()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("dreamcast", "gdi");
        Assert.Equal(new[] { "gdi", "cue" }, result);
    }

    #endregion

    #region CHD source (Dreamcast GD-ROM) → ["cue", "gdi"]

    [Fact]
    public void Dreamcast_Chd_ReturnsCueAndGdi()
    {
        // A GD-ROM CHD is stored verbatim (ImageFormat.Chd) with its CHD metadata; export
        // reconstructs either cue or gdi on demand, so both must be offered (cue first).
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("dreamcast", "chd");
        Assert.Equal(new[] { "cue", "gdi" }, result);
    }

    [Theory]
    [InlineData("Dreamcast", "chd")]
    [InlineData("DREAMCAST", "CHD")]
    [InlineData("dreamcast", "Chd")]
    public void CaseInsensitivity_Dreamcast_Chd(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);
        Assert.Equal(new[] { "cue", "gdi" }, result);
    }

    #endregion

    #region Case-insensitivity tests

    [Theory]
    [InlineData("Xbox", "iso")]
    [InlineData("XBOX", "iso")]
    [InlineData("xbox", "iso")]
    [InlineData("xBox", "ISO")]
    [InlineData("XBOX", "ISO")]
    public void CaseInsensitivity_Xbox_Iso(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);
        Assert.Equal(new[] { "xiso", "iso" }, result);
    }

    [Theory]
    [InlineData("Xbox360", "iso")]
    [InlineData("XBOX360", "ISO")]
    [InlineData("xbox360", "iso")]
    [InlineData("XBox360", "Iso")]
    public void CaseInsensitivity_Xbox360_Iso(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);
        Assert.Equal(new[] { "xiso", "iso" }, result);
    }

    [Theory]
    [InlineData("PS1", "ISO")]
    [InlineData("ps1", "iso")]
    [InlineData("Ps1", "Iso")]
    public void CaseInsensitivity_Ps1_Iso(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);
        Assert.Equal(new[] { "iso", "cue" }, result);
    }

    [Theory]
    [InlineData("DEFAULT", "CUE")]
    [InlineData("default", "cue")]
    [InlineData("Default", "Cue")]
    public void CaseInsensitivity_Default_Cue(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);
        Assert.Equal(new[] { "cue" }, result);
    }

    [Theory]
    [InlineData("DREAMCAST", "GDI")]
    [InlineData("dreamcast", "gdi")]
    [InlineData("Dreamcast", "Gdi")]
    public void CaseInsensitivity_Dreamcast_Gdi(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);
        Assert.Equal(new[] { "gdi", "cue" }, result);
    }

    #endregion

    #region Null system/sourceFormat defaults to fallback behavior

    [Fact]
    public void NullSystem_ReturnsLowercasedSourceFormat()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(null!, "SomeFormat");
        Assert.Equal(new[] { "someformat" }, result);
    }

    [Fact]
    public void NullSourceFormat_ReturnsIsoFallback()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("unknownsystem", null!);
        Assert.Equal(new[] { "iso" }, result);
    }

    [Fact]
    public void BothNull_ReturnsIsoFallback()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(null!, null!);
        Assert.Equal(new[] { "iso" }, result);
    }

    #endregion

    #region Unknown pair returns [sourceFormat.ToLowerInvariant()]

    [Fact]
    public void UnknownSystem_ReturnsLowercasedSourceFormat()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("unknownsystem", "SomeFormat");
        Assert.Equal(new[] { "someformat" }, result);
    }

    [Fact]
    public void UnknownFormat_ReturnsLowercasedSourceFormat()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("xbox", "unknownformat");
        Assert.Equal(new[] { "unknownformat" }, result);
    }

    [Fact]
    public void UnknownPair_PreservesLowercasing()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("MYSTERY", "BIN");
        Assert.Equal(new[] { "bin" }, result);
    }

    #endregion
}
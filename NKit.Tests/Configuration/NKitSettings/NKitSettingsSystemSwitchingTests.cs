using Nanook.NKit;
using NKit.Tests.TestData;
using Xunit;


namespace NKit.Tests.Configuration.NKitSettings
{
    /// <summary>
    /// Tests for NKitSettings system switching scenarios.
    /// Validates that switching between systems applies correct defaults
    /// and handles format compatibility properly.
    /// Uses SystemCapabilitiesTestData as the single source of truth.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "NKitSettings")]
    public class NKitSettingsSystemSwitchingTests
    {
        #region Basic System Switching Tests

        [Theory(DisplayName = "System switching applies correct defaults")]
        [InlineData(SystemType.GameCube, SystemType.PSP)]
        [InlineData(SystemType.PSP, SystemType.Dreamcast)]
        [InlineData(SystemType.Dreamcast, SystemType.PS1)]
        [InlineData(SystemType.PS1, SystemType.GameCube)]
        [InlineData(SystemType.XBox, SystemType.PS3)]
        [InlineData(SystemType.PS3, SystemType.WiiU)]
        public void SystemSwitching_AppliesCorrectDefaults(SystemType fromSystem, SystemType toSystem)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(fromSystem, TaskType.Convert);
            SystemCapability fromTestData = SystemCapabilitiesTestData.GetSystemCapability(fromSystem);
            SystemCapability toTestData = SystemCapabilitiesTestData.GetSystemCapability(toSystem);

            // Verify initial state
            Assert.Equal(fromTestData.DefaultFormat, settings.Convert);

            // Act
            settings.System = toSystem;

            // Assert
            Assert.Equal(toTestData.DefaultFormat, settings.Convert);
        }

        #endregion

        #region Format Compatibility Tests

        [Theory(DisplayName = "System switching handles format compatibility correctly")]
        // Single format system to index-only system
        [InlineData(SystemType.GameCube, "rvz:zstd:19:128kb:16", SystemType.Dreamcast, "cue:split:bin:bin:sub")]
        [InlineData(SystemType.PSP, "cso:9:2kb:4", SystemType.Dreamcast, "cue:split:bin:bin:sub")]

        // Index-only system to single format system  
        [InlineData(SystemType.Dreamcast, "cue:split:bin:bin:sub", SystemType.GameCube, "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.Dreamcast, "cue:split:bin:bin:sub", SystemType.PSP, "cso:9:2kb:4")]

        // Dual format system to single format system
        [InlineData(SystemType.PS1, "cso:9:2kb:4/cue:split:bin:bin:sub", SystemType.GameCube, "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.PS3, "deciso/cue:split:bin:bin:sub", SystemType.PSP, "cso:9:2kb:4")]

        // Single format system to dual format system
        [InlineData(SystemType.GameCube, "rvz:zstd:19:128kb:16", SystemType.PS1, "cso:9:2kb:4/cue:split:bin:bin:sub")]
        [InlineData(SystemType.PSP, "cso:9:2kb:4", SystemType.PS3, "deciso/cue:split:bin:bin:sub")]
        public void SystemSwitching_HandlesFormatCompatibility(
            SystemType fromSystem, string fromFormat, SystemType toSystem, string expectedToFormat)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(fromSystem, TaskType.Convert);

            // Verify initial format matches test data default
            SystemCapability fromTestData = SystemCapabilitiesTestData.GetSystemCapability(fromSystem);
            Assert.Equal(fromTestData.DefaultFormat, settings.Convert);
            Assert.Equal(fromFormat, settings.Convert);

            // Act
            settings.System = toSystem;

            // Assert
            Assert.Equal(expectedToFormat, settings.Convert);
        }

        #endregion

        #region Component Property Tests

        [Theory(DisplayName = "System switching sets correct component properties")]
        [InlineData(SystemType.GameCube, "rvz", "", "zstd", "19", "128kb", "16")]
        [InlineData(SystemType.PSP, "cso", "", "", "9", "2kb", "4")]
        [InlineData(SystemType.Dreamcast, "", "cue", "", "", "", "")]
        [InlineData(SystemType.PS1, "cso", "cue", "", "9", "2kb", "4")]
        [InlineData(SystemType.PS3, "deciso", "cue", "", "", "", "")]
        public void SystemSwitching_SetsCorrectComponentProperties(
            SystemType system, string expectedSingle, string expectedIndexed,
            string expectedEncoding, string expectedLevel, string expectedBlockSize, string expectedParallelism)
        {
            // Arrange & Act
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(system, TaskType.Convert);

            // Assert
            Assert.Equal(expectedSingle, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(expectedIndexed, settings.ConvertIndexedFormat ?? string.Empty);
            Assert.Equal(expectedEncoding, settings.ConvertEncoding ?? string.Empty);
            Assert.Equal(expectedLevel, settings.ConvertLevel ?? string.Empty);
            Assert.Equal(expectedBlockSize, settings.ConvertBlockSize ?? string.Empty);
            Assert.Equal(expectedParallelism, settings.ConvertParallelism ?? string.Empty);
        }

        #endregion

        #region Invalid Format Handling Tests

        [Theory(DisplayName = "System switching with invalid formats falls back correctly")]
        [InlineData(SystemType.GameCube, "invalidformat", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.Dreamcast, "iso", "cue:split:bin:bin:sub")] // ISO not supported on Dreamcast
        [InlineData(SystemType.PSP, "wbfs:y", "cso:9:2kb:4")] // WBFS not supported on PSP
        [InlineData(SystemType.WiiU, "cue:split:bin:bin:sub", "wux")] // CUE not supported on WiiU
        public void SystemSwitchingWithInvalidFormats_FallsBackCorrectly(
            SystemType system, string invalidFormat, string expectedFallback)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(system, TaskType.Convert);

            // Act
            settings.Convert = invalidFormat;

            // Assert - Should fallback to system default
            Assert.Equal(expectedFallback, settings.Convert);
        }

        #endregion

        #region System Category Tests

        [Theory(DisplayName = "System categories have correct format support")]
        [InlineData(SystemType.GameCube, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.PSP, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.Dreamcast, SystemCategory.IndexOnly, false, true)]
        [InlineData(SystemType.PS1, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.PS3, SystemCategory.DualFormat, true, true)]
        public void SystemCategories_HaveCorrectFormatSupport(
            SystemType system, SystemCategory expectedCategory, bool expectedSingleSupport, bool expectedIndexSupport)
        {
            // Act
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(system);
            bool singleSupported = SystemCapabilitiesTestData.IsSingleFormatSupported(system);
            bool indexSupported = SystemCapabilitiesTestData.IsIndexedFormatSupported(system);

            // Assert
            Assert.Equal(expectedCategory, testData.Category);
            Assert.Equal(expectedSingleSupport, singleSupported);
            Assert.Equal(expectedIndexSupport, indexSupported);
        }

        #endregion

        #region Multi-System Chain Tests

        [Theory(DisplayName = "Multi-system switching maintains correct defaults")]
        [InlineData(SystemType.GameCube, SystemType.PSP, SystemType.Dreamcast, SystemType.PS1)]
        [InlineData(SystemType.Dreamcast, SystemType.PS3, SystemType.WiiU, SystemType.XBox)]
        public void MultiSystemSwitching_MaintainsCorrectDefaults(
            SystemType s1, SystemType s2, SystemType s3, SystemType s4)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(s1, TaskType.Convert);
            SystemCapability[] testData = new[]
            {
                SystemCapabilitiesTestData.GetSystemCapability(s1),
                SystemCapabilitiesTestData.GetSystemCapability(s2),
                SystemCapabilitiesTestData.GetSystemCapability(s3),
                SystemCapabilitiesTestData.GetSystemCapability(s4)
            };

            // Test forward switching
            Assert.Equal(testData[0].DefaultFormat, settings.Convert);

            settings.System = s2;
            Assert.Equal(testData[1].DefaultFormat, settings.Convert);

            settings.System = s3;
            Assert.Equal(testData[2].DefaultFormat, settings.Convert);

            settings.System = s4;
            Assert.Equal(testData[3].DefaultFormat, settings.Convert);

            // Test reverse switching
            settings.System = s3;
            Assert.Equal(testData[2].DefaultFormat, settings.Convert);

            settings.System = s2;
            Assert.Equal(testData[1].DefaultFormat, settings.Convert);

            settings.System = s1;
            Assert.Equal(testData[0].DefaultFormat, settings.Convert);
        }

        #endregion

        #region Format String Validation Tests

        [Theory(DisplayName = "System switching validates format strings correctly")]
        [InlineData(SystemType.GameCube, "rvz:zstd:19:128kb:16", true)]
        [InlineData(SystemType.GameCube, "cue:split:bin:bin:sub", false)] // CUE not supported
        [InlineData(SystemType.Dreamcast, "cue:split:bin:bin:sub", true)]
        [InlineData(SystemType.Dreamcast, "rvz:zstd:19:128kb:16", false)] // RVZ not supported
        [InlineData(SystemType.PS1, "cso:9:2kb:4/cue:split:bin:bin:sub", true)]
        [InlineData(SystemType.PS1, "wbfs:y", false)] // WBFS not supported
        public void SystemSwitching_ValidatesFormatStringsCorrectly(
            SystemType system, string formatString, bool shouldBeValid)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(system, TaskType.Convert);
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(system);

            // Act
            settings.Convert = formatString;

            // Assert
            if (shouldBeValid)
            {
                Assert.Equal(formatString, settings.Convert);
            }
            else
            {
                // Should fallback to system default
                Assert.Equal(testData.DefaultFormat, settings.Convert);
            }
        }

        #endregion
    }
}
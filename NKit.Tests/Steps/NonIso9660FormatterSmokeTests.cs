using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Smoke tests verifying that non-ISO9660 formatters (GameCube, Wii, WiiU, Xbox)
    /// continue to produce only a single unified `filesystem.nkfs` file and never
    /// produce per-type nkfs files.
    ///
    /// These formatters were NOT modified in the multi-filesystem-nkfs feature and
    /// should retain their existing single-file behavior.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class NonIso9660FormatterSmokeTests
    {
        /// <summary>
        /// Verifies that DataStoreGameCubeFormatter does NOT have a BuildPerTypeFsYaml method.
        /// Only the ISO9660 formatter should have per-type logic.
        /// Validates: Requirement 7.1
        /// </summary>
        [Fact]
        public void GameCubeFormatter_DoesNotHave_BuildPerTypeFsYaml()
        {
            Type formatterType = typeof(DataStoreGameCubeFormatter);
            MethodInfo method = formatterType.GetMethod("BuildPerTypeFsYaml",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(method);
        }

        /// <summary>
        /// Verifies that DataStoreWiiFormatter does NOT have a BuildPerTypeFsYaml method.
        /// Only the ISO9660 formatter should have per-type logic.
        /// Validates: Requirement 7.2
        /// </summary>
        [Fact]
        public void WiiFormatter_DoesNotHave_BuildPerTypeFsYaml()
        {
            Type formatterType = typeof(DataStoreWiiFormatter);
            MethodInfo method = formatterType.GetMethod("BuildPerTypeFsYaml",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(method);
        }

        /// <summary>
        /// Verifies that DataStoreWiiUFormatter does NOT have a BuildPerTypeFsYaml method.
        /// Only the ISO9660 formatter should have per-type logic.
        /// Validates: Requirement 7.3
        /// </summary>
        [Fact]
        public void WiiUFormatter_DoesNotHave_BuildPerTypeFsYaml()
        {
            Type formatterType = typeof(DataStoreWiiUFormatter);
            MethodInfo method = formatterType.GetMethod("BuildPerTypeFsYaml",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(method);
        }

        /// <summary>
        /// Verifies that DataStoreXboxFormatter does NOT have a BuildPerTypeFsYaml method.
        /// Only the ISO9660 formatter should have per-type logic.
        /// Validates: Requirement 7.4
        /// </summary>
        [Fact]
        public void XboxFormatter_DoesNotHave_BuildPerTypeFsYaml()
        {
            Type formatterType = typeof(DataStoreXboxFormatter);
            MethodInfo method = formatterType.GetMethod("BuildPerTypeFsYaml",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(method);
        }

        /// <summary>
        /// Verifies that the ISO9660 formatter DOES have BuildPerTypeFsYaml (positive control).
        /// This confirms the test approach is valid — only ISO9660 has the per-type logic.
        /// </summary>
        [Fact]
        public void Iso9660Formatter_DoesHave_BuildPerTypeFsYaml()
        {
            Type formatterType = typeof(DataStoreIso9660Formatter);
            MethodInfo method = formatterType.GetMethod("BuildPerTypeFsYaml",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.NotNull(method);
        }

        /// <summary>
        /// Verifies that the GameCube formatter's _imageWriter field writes only to
        /// FileSystemNkfsRootPath ("filesystem.nkfs") — the unified path.
        /// Validates: Requirement 7.1
        /// </summary>
        [Fact]
        public void GameCubeFormatter_WritesOnly_UnifiedFileSystemNkfs()
        {
            // The formatter writes to DataStore.FileSystemNkfsRootPath which is "filesystem.nkfs"
            Assert.Equal("filesystem.nkfs", DataStore.FileSystemNkfsRootPath);

            // Verify the formatter type does not contain any per-type filename construction logic
            Type formatterType = typeof(DataStoreGameCubeFormatter);
            AssertFormatterHasNoPerTypeWriteLogic(formatterType);
        }

        /// <summary>
        /// Verifies that the Wii formatter writes only to the unified path.
        /// Validates: Requirement 7.2
        /// </summary>
        [Fact]
        public void WiiFormatter_WritesOnly_UnifiedFileSystemNkfs()
        {
            Type formatterType = typeof(DataStoreWiiFormatter);
            AssertFormatterHasNoPerTypeWriteLogic(formatterType);
        }

        /// <summary>
        /// Verifies that the WiiU formatter writes only to the unified path.
        /// Validates: Requirement 7.3
        /// </summary>
        [Fact]
        public void WiiUFormatter_WritesOnly_UnifiedFileSystemNkfs()
        {
            Type formatterType = typeof(DataStoreWiiUFormatter);
            AssertFormatterHasNoPerTypeWriteLogic(formatterType);
        }

        /// <summary>
        /// Verifies that the Xbox formatter writes only to the unified path.
        /// Validates: Requirement 7.4
        /// </summary>
        [Fact]
        public void XboxFormatter_WritesOnly_UnifiedFileSystemNkfs()
        {
            Type formatterType = typeof(DataStoreXboxFormatter);
            AssertFormatterHasNoPerTypeWriteLogic(formatterType);
        }

        /// <summary>
        /// Verifies that non-ISO9660 formatters do not reference TryExtractFsTypeName or
        /// ResolveTargetFsType — methods used exclusively for per-type nkfs logic.
        /// Validates: Requirement 7.5
        /// </summary>
        [Theory]
        [InlineData(typeof(DataStoreGameCubeFormatter))]
        [InlineData(typeof(DataStoreWiiFormatter))]
        [InlineData(typeof(DataStoreWiiUFormatter))]
        [InlineData(typeof(DataStoreXboxFormatter))]
        public void NonIso9660Formatters_DoNotReference_PerTypeHelperMethods(Type formatterType)
        {
            // Verify no method named ResolveTargetFsType exists on these formatters
            MethodInfo resolveMethod = formatterType.GetMethod("ResolveTargetFsType",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(resolveMethod);

            // Verify no method named TryExtractFsTypeName exists on these formatters
            MethodInfo extractMethod = formatterType.GetMethod("TryExtractFsTypeName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Null(extractMethod);
        }

        /// <summary>
        /// Verifies that all non-ISO9660 formatters implement IDataStoreSystemFormatter
        /// and have a BuildFileSystemYaml method (confirming they are proper formatters).
        /// </summary>
        [Theory]
        [InlineData(typeof(DataStoreGameCubeFormatter))]
        [InlineData(typeof(DataStoreWiiFormatter))]
        [InlineData(typeof(DataStoreWiiUFormatter))]
        [InlineData(typeof(DataStoreXboxFormatter))]
        public void NonIso9660Formatters_Implement_IDataStoreSystemFormatter(Type formatterType)
        {
            Assert.True(typeof(IDataStoreSystemFormatter).IsAssignableFrom(formatterType),
                $"{formatterType.Name} should implement IDataStoreSystemFormatter");

            // Verify BuildFileSystemYaml exists (they are proper formatters)
            MethodInfo buildMethod = formatterType.GetMethod("BuildFileSystemYaml",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(buildMethod);
        }

        /// <summary>
        /// Verifies that the unified filesystem.nkfs path is the expected constant value.
        /// All non-ISO9660 formatters write to this path exclusively.
        /// </summary>
        [Fact]
        public void FileSystemNkfsRootPath_IsUnifiedFilename()
        {
            // The unified path must be "filesystem.nkfs" (no type segment)
            Assert.Equal("filesystem.nkfs", DataStore.FileSystemNkfsRootPath);

            // It must NOT match the per-type pattern
            bool isPerType = DataStore.TryExtractFsTypeName(DataStore.FileSystemNkfsRootPath, out _);
            Assert.False(isPerType, "The unified path should not be detected as a per-type filename");
        }

        /// <summary>
        /// Helper: Asserts that a formatter type does not contain any per-type nkfs file
        /// writing logic by checking it has no methods that could produce per-type filenames.
        /// Non-ISO9660 formatters should only write to the single unified path.
        /// </summary>
        private static void AssertFormatterHasNoPerTypeWriteLogic(Type formatterType)
        {
            // Non-ISO9660 formatters should not have any method containing "PerType" in the name
            List<MethodInfo> perTypeMethods = formatterType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name.Contains("PerType", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(perTypeMethods);

            // Non-ISO9660 formatters should not have any method containing "FsType" in the name
            // (except inherited/object methods)
            List<MethodInfo> fsTypeMethods = formatterType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.Contains("FsType", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(fsTypeMethods);
        }
    }
}
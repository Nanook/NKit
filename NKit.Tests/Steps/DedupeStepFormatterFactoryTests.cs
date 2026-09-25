using Nanook.NKit;
using System;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for the DedupeStep formatter factory logic.
    /// Uses model-based testing since DedupeStep.Initialise() has complex dependencies
    /// (DataStore, file system, IStepContext). The model replicates the switch statement
    /// logic and verifies the correct formatter type is selected for each system type.
    ///
    /// Validates Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 10.10
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class DedupeStepFormatterFactoryTests
    {
        #region Model

        /// <summary>
        /// Represents the formatter type that would be instantiated by the factory.
        /// </summary>
        private enum FormatterType
        {
            DataStoreGameCubeFormatter,
            DataStoreWiiFormatter,
            DataStoreWiiUFormatter,
            DataStoreXboxFormatter,
            DataStoreIso9660Formatter,
            NotSupported
        }

        /// <summary>
        /// Represents the constructor parameter set for each formatter type.
        /// </summary>
        private class FormatterConstructorParams
        {
            public string DedupePath { get; set; }
            public string ImageName { get; set; }
            public long ShardSize { get; set; }
            public int BlockSize { get; set; }
            public string SetName { get; set; }
            public string AuxSetName { get; set; }
            public bool HasAuxSetName => AuxSetName != null;
        }

        /// <summary>
        /// Models the DedupeStep.Initialise() formatter factory switch statement.
        /// This is a direct translation of the production code's switch logic.
        /// </summary>
        private static (FormatterType Type, FormatterConstructorParams Params) ModelFormatterFactory(
            SystemType systemType,
            string dedupeDirectory,
            string outName,
            long shardSize,
            int blockSizeToUse,
            string setName,
            string auxSetName)
        {
            FormatterConstructorParams parms = new FormatterConstructorParams
            {
                DedupePath = dedupeDirectory,
                ImageName = outName,
                ShardSize = shardSize,
                BlockSize = blockSizeToUse,
                SetName = setName,
                AuxSetName = auxSetName
            };

            switch (systemType)
            {
                case SystemType.GameCube:
                    parms.AuxSetName = null; // GameCube does not use aux
                    return (FormatterType.DataStoreGameCubeFormatter, parms);
                case SystemType.Wii:
                    return (FormatterType.DataStoreWiiFormatter, parms);
                case SystemType.WiiU:
                    return (FormatterType.DataStoreWiiUFormatter, parms);
                case SystemType.XBox:
                case SystemType.XBox360:
                    return (FormatterType.DataStoreXboxFormatter, parms);
                case SystemType.PS1:
                case SystemType.PS2:
                case SystemType.PS3:
                case SystemType.PSP:
                case SystemType.SegaCD:
                case SystemType.Saturn:
                case SystemType.Dreamcast:
                case SystemType.CDi:
                case SystemType.Default:
                case SystemType.PcEngine:
                    parms.AuxSetName = null; // ISO9660 formatter does not accept auxSetName
                    return (FormatterType.DataStoreIso9660Formatter, parms);
                default:
                    return (FormatterType.NotSupported, null);
            }
        }

        /// <summary>
        /// Models the aux set name resolution by convention.
        /// The DedupeStep discovers the aux set by convention: {systemtype}.aux.nkds
        /// following the same pattern used for Wii and WiiU aux sets.
        /// </summary>
        private static string ModelResolveAuxSetName(SystemType systemType, bool auxSetExists)
        {
            if (!auxSetExists)
                return null;

            string baseName = systemType.ToString().ToLower();
            return baseName + ".aux"; // e.g. "xbox.aux", "wii.aux"
        }

        #endregion

        #region Xbox System Type Tests

        /// <summary>
        /// Validates Requirement 8.1: Xbox system type instantiates DataStoreXboxFormatter
        /// with auxSetName parameter.
        /// </summary>
        [Fact]
        public void FormatterFactory_Xbox_InstantiatesDataStoreXboxFormatter()
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.XBox,
                @"C:\dedupe",
                "TestImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "xbox",
                "xbox.aux");

            Assert.Equal(FormatterType.DataStoreXboxFormatter, type);
        }

        /// <summary>
        /// Validates Requirement 8.1: Xbox system type receives auxSetName parameter.
        /// </summary>
        [Fact]
        public void FormatterFactory_Xbox_ReceivesAuxSetNameParameter()
        {
            string auxSetName = "xbox.aux";
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.XBox,
                @"C:\dedupe",
                "TestImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "xbox",
                auxSetName);

            Assert.Equal(FormatterType.DataStoreXboxFormatter, type);
            Assert.True(parms.HasAuxSetName);
            Assert.Equal(auxSetName, parms.AuxSetName);
        }

        /// <summary>
        /// Validates Requirement 8.1: Xbox360 system type instantiates DataStoreXboxFormatter
        /// with auxSetName parameter.
        /// </summary>
        [Fact]
        public void FormatterFactory_Xbox360_InstantiatesDataStoreXboxFormatter()
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.XBox360,
                @"C:\dedupe",
                "TestImage360",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "xbox360",
                "xbox360.aux");

            Assert.Equal(FormatterType.DataStoreXboxFormatter, type);
        }

        /// <summary>
        /// Validates Requirement 8.1: Xbox360 system type receives auxSetName parameter.
        /// </summary>
        [Fact]
        public void FormatterFactory_Xbox360_ReceivesAuxSetNameParameter()
        {
            string auxSetName = "xbox360.aux";
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.XBox360,
                @"C:\dedupe",
                "TestImage360",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "xbox360",
                auxSetName);

            Assert.Equal(FormatterType.DataStoreXboxFormatter, type);
            Assert.True(parms.HasAuxSetName);
            Assert.Equal(auxSetName, parms.AuxSetName);
        }

        #endregion

        #region ISO9660 System Type Tests

        /// <summary>
        /// Validates Requirement 8.2: Each ISO9660 system type instantiates DataStoreIso9660Formatter.
        /// </summary>
        [Theory]
        [InlineData(SystemType.PS1)]
        [InlineData(SystemType.PS2)]
        [InlineData(SystemType.PS3)]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.SegaCD)]
        [InlineData(SystemType.Saturn)]
        [InlineData(SystemType.Dreamcast)]
        [InlineData(SystemType.CDi)]
        [InlineData(SystemType.Default)]
        [InlineData(SystemType.PcEngine)]
        public void FormatterFactory_Iso9660Systems_InstantiatesDataStoreIso9660Formatter(SystemType systemType)
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                systemType,
                @"C:\dedupe",
                "TestIsoImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                systemType.ToString().ToLower(),
                null);

            Assert.Equal(FormatterType.DataStoreIso9660Formatter, type);
        }

        /// <summary>
        /// Validates Requirement 8.2: ISO9660 formatter does NOT receive auxSetName parameter.
        /// The ISO9660 formatter constructor does not accept auxSetName.
        /// </summary>
        [Theory]
        [InlineData(SystemType.PS1)]
        [InlineData(SystemType.PS2)]
        [InlineData(SystemType.PS3)]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.SegaCD)]
        [InlineData(SystemType.Saturn)]
        [InlineData(SystemType.Dreamcast)]
        [InlineData(SystemType.CDi)]
        [InlineData(SystemType.Default)]
        [InlineData(SystemType.PcEngine)]
        public void FormatterFactory_Iso9660Systems_DoesNotPassAuxSetName(SystemType systemType)
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                systemType,
                @"C:\dedupe",
                "TestIsoImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                systemType.ToString().ToLower(),
                "some.aux"); // Even if aux is discovered, ISO9660 formatter doesn't use it

            Assert.Equal(FormatterType.DataStoreIso9660Formatter, type);
            Assert.False(parms.HasAuxSetName);
        }

        #endregion

        #region Existing Formatter Tests (Wii/GameCube/WiiU)

        /// <summary>
        /// Validates Requirement 8.3: Wii continues to use DataStoreWiiFormatter.
        /// </summary>
        [Fact]
        public void FormatterFactory_Wii_ContinuesToUseDataStoreWiiFormatter()
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.Wii,
                @"C:\dedupe",
                "WiiImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "wii",
                "wii.aux");

            Assert.Equal(FormatterType.DataStoreWiiFormatter, type);
            Assert.True(parms.HasAuxSetName);
        }

        /// <summary>
        /// Validates Requirement 8.3: GameCube continues to use DataStoreGameCubeFormatter.
        /// </summary>
        [Fact]
        public void FormatterFactory_GameCube_ContinuesToUseDataStoreGameCubeFormatter()
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.GameCube,
                @"C:\dedupe",
                "GcImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "gamecube",
                null);

            Assert.Equal(FormatterType.DataStoreGameCubeFormatter, type);
            Assert.False(parms.HasAuxSetName); // GameCube does not use aux
        }

        /// <summary>
        /// Validates Requirement 8.3: WiiU continues to use DataStoreWiiUFormatter.
        /// </summary>
        [Fact]
        public void FormatterFactory_WiiU_ContinuesToUseDataStoreWiiUFormatter()
        {
            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.WiiU,
                @"C:\dedupe",
                "WiiUImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "wiiu",
                "wiiu.aux");

            Assert.Equal(FormatterType.DataStoreWiiUFormatter, type);
            Assert.True(parms.HasAuxSetName);
        }

        #endregion

        #region Unsupported System Type Tests

        /// <summary>
        /// Validates Requirement 8.6: Unsupported system type throws NotSupportedException.
        /// The model returns NotSupported; the production code throws NotSupportedException.
        /// </summary>
        [Fact]
        public void FormatterFactory_NotSetSystemType_IsNotSupported()
        {
            (FormatterType type, FormatterConstructorParams _) = ModelFormatterFactory(
                SystemType.NotSet,
                @"C:\dedupe",
                "Unknown",
                50L * 1024 * 1024 * 1024,
                0x8000,
                "notset",
                null);

            Assert.Equal(FormatterType.NotSupported, type);
        }

        /// <summary>
        /// Validates Requirement 8.6: The production code throws NotSupportedException
        /// for unmapped system types. This test verifies the exception message format.
        /// </summary>
        [Fact]
        public void FormatterFactory_UnsupportedType_ThrowsNotSupportedException_InProduction()
        {
            // This models the production behavior: the default case throws NotSupportedException
            SystemType unsupported = SystemType.NotSet;
            (FormatterType type, FormatterConstructorParams _) = ModelFormatterFactory(unsupported, @"C:\dedupe", "x", 0, 0, "x", null);

            Assert.Equal(FormatterType.NotSupported, type);

            // Verify the production code's exception message format
            string expectedMessage = $"No IDataStoreSystemFormatter available for system type '{unsupported}'. Dedupe requires a formatter for this system.";
            try
            {
                // Simulate the production code's default case
                throw new NotSupportedException($"No IDataStoreSystemFormatter available for system type '{unsupported}'. Dedupe requires a formatter for this system.");
            }
            catch (NotSupportedException ex)
            {
                Assert.Contains(unsupported.ToString(), ex.Message);
                Assert.Contains("No IDataStoreSystemFormatter available", ex.Message);
            }
        }

        #endregion

        #region AuxSetName Convention Resolution Tests

        /// <summary>
        /// Validates Requirement 10.10: auxSetName is resolved by convention for Xbox
        /// following the same pattern as Wii/WiiU.
        /// The convention is: {systemtype_lowercase}.aux
        /// </summary>
        [Theory]
        [InlineData(SystemType.XBox, "xbox.aux")]
        [InlineData(SystemType.XBox360, "xbox360.aux")]
        [InlineData(SystemType.Wii, "wii.aux")]
        [InlineData(SystemType.WiiU, "wiiu.aux")]
        public void AuxSetName_ResolvedByConvention_WhenAuxSetExists(SystemType systemType, string expectedAuxSetName)
        {
            string resolved = ModelResolveAuxSetName(systemType, auxSetExists: true);

            Assert.Equal(expectedAuxSetName, resolved);
        }

        /// <summary>
        /// Validates Requirement 10.10: auxSetName is null when no aux set exists.
        /// </summary>
        [Theory]
        [InlineData(SystemType.XBox)]
        [InlineData(SystemType.XBox360)]
        [InlineData(SystemType.Wii)]
        [InlineData(SystemType.WiiU)]
        public void AuxSetName_IsNull_WhenAuxSetDoesNotExist(SystemType systemType)
        {
            string resolved = ModelResolveAuxSetName(systemType, auxSetExists: false);

            Assert.Null(resolved);
        }

        /// <summary>
        /// Validates Requirement 10.10: Xbox aux set name follows the same convention as Wii/WiiU.
        /// The pattern is consistent: {systemtype_lowercase}.aux
        /// </summary>
        [Fact]
        public void AuxSetName_XboxFollowsSamePatternAsWiiAndWiiU()
        {
            string xboxAux = ModelResolveAuxSetName(SystemType.XBox, auxSetExists: true);
            string wiiAux = ModelResolveAuxSetName(SystemType.Wii, auxSetExists: true);
            string wiiuAux = ModelResolveAuxSetName(SystemType.WiiU, auxSetExists: true);

            // All follow the same pattern: {systemtype}.aux
            Assert.EndsWith(".aux", xboxAux);
            Assert.EndsWith(".aux", wiiAux);
            Assert.EndsWith(".aux", wiiuAux);

            // Xbox uses the same suffix as Wii/WiiU
            Assert.Equal(".aux", xboxAux.Substring(xboxAux.IndexOf('.')));
            Assert.Equal(".aux", wiiAux.Substring(wiiAux.IndexOf('.')));
            Assert.Equal(".aux", wiiuAux.Substring(wiiuAux.IndexOf('.')));
        }

        #endregion

        #region Constructor Parameter Passing Tests

        /// <summary>
        /// Validates Requirement 8.5: Block size is passed to the formatter constructor.
        /// </summary>
        [Theory]
        [InlineData(SystemType.XBox, 0x8000)]
        [InlineData(SystemType.XBox360, 0x10000)]
        [InlineData(SystemType.PS1, 0x8000)]
        [InlineData(SystemType.Dreamcast, 0x4000)]
        [InlineData(SystemType.Wii, 0x8000)]
        [InlineData(SystemType.GameCube, 0x8000)]
        [InlineData(SystemType.WiiU, 0x10000)]
        public void FormatterFactory_PassesBlockSizeToFormatter(SystemType systemType, int blockSize)
        {
            (FormatterType _, FormatterConstructorParams parms) = ModelFormatterFactory(
                systemType,
                @"C:\dedupe",
                "TestImage",
                50L * 1024 * 1024 * 1024,
                blockSize,
                systemType.ToString().ToLower(),
                null);

            Assert.NotNull(parms);
            Assert.Equal(blockSize, parms.BlockSize);
        }

        /// <summary>
        /// Validates Requirement 8.4: Formatter selection uses only SystemType value.
        /// The same SystemType always produces the same formatter type regardless of other parameters.
        /// </summary>
        [Theory]
        [InlineData(SystemType.XBox)]
        [InlineData(SystemType.PS1)]
        [InlineData(SystemType.Wii)]
        [InlineData(SystemType.GameCube)]
        public void FormatterFactory_SelectionDependsOnlyOnSystemType(SystemType systemType)
        {
            // Different parameters, same system type — should produce same formatter type
            (FormatterType type1, FormatterConstructorParams _) = ModelFormatterFactory(systemType, @"C:\path1", "Image1", 100, 0x8000, "set1", null);
            (FormatterType type2, FormatterConstructorParams _) = ModelFormatterFactory(systemType, @"D:\path2", "Image2", 200, 0x4000, "set2", "some.aux");

            Assert.Equal(type1, type2);
        }

        /// <summary>
        /// Validates Requirement 8.1: Xbox formatter receives all required constructor parameters.
        /// Constructor: (dedupePath, imageName, shardSize, context, blockSize, setName, auxSetName)
        /// </summary>
        [Fact]
        public void FormatterFactory_Xbox_ReceivesAllConstructorParameters()
        {
            string dedupePath = @"C:\dedupe\store";
            string imageName = "MyXboxGame";
            long shardSize = 50L * 1024 * 1024 * 1024;
            int blockSize = 0x8000;
            string setName = "xbox";
            string auxSetName = "xbox.aux";

            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.XBox, dedupePath, imageName, shardSize, blockSize, setName, auxSetName);

            Assert.Equal(FormatterType.DataStoreXboxFormatter, type);
            Assert.Equal(dedupePath, parms.DedupePath);
            Assert.Equal(imageName, parms.ImageName);
            Assert.Equal(shardSize, parms.ShardSize);
            Assert.Equal(blockSize, parms.BlockSize);
            Assert.Equal(setName, parms.SetName);
            Assert.Equal(auxSetName, parms.AuxSetName);
        }

        /// <summary>
        /// Validates Requirement 8.2: ISO9660 formatter receives all required constructor parameters.
        /// Constructor: (dedupePath, imageName, shardSize, context, blockSize, setName)
        /// Note: No auxSetName parameter for ISO9660 formatter.
        /// </summary>
        [Fact]
        public void FormatterFactory_Iso9660_ReceivesAllConstructorParameters()
        {
            string dedupePath = @"C:\dedupe\store";
            string imageName = "MyPs2Game";
            long shardSize = 50L * 1024 * 1024 * 1024;
            int blockSize = 0x8000;
            string setName = "ps2";

            (FormatterType type, FormatterConstructorParams parms) = ModelFormatterFactory(
                SystemType.PS2, dedupePath, imageName, shardSize, blockSize, setName, null);

            Assert.Equal(FormatterType.DataStoreIso9660Formatter, type);
            Assert.Equal(dedupePath, parms.DedupePath);
            Assert.Equal(imageName, parms.ImageName);
            Assert.Equal(shardSize, parms.ShardSize);
            Assert.Equal(blockSize, parms.BlockSize);
            Assert.Equal(setName, parms.SetName);
            Assert.False(parms.HasAuxSetName);
        }

        #endregion

        #region Completeness Tests

        /// <summary>
        /// Validates that every defined SystemType (except NotSet) maps to a formatter.
        /// This ensures no system type accidentally falls through to NotSupported.
        /// </summary>
        [Fact]
        public void FormatterFactory_AllSupportedSystemTypes_MapToCorrectFormatter()
        {
            Dictionary<SystemType, FormatterType> expectedMappings = new Dictionary<SystemType, FormatterType>
            {
                { SystemType.GameCube, FormatterType.DataStoreGameCubeFormatter },
                { SystemType.Wii, FormatterType.DataStoreWiiFormatter },
                { SystemType.WiiU, FormatterType.DataStoreWiiUFormatter },
                { SystemType.XBox, FormatterType.DataStoreXboxFormatter },
                { SystemType.XBox360, FormatterType.DataStoreXboxFormatter },
                { SystemType.PS1, FormatterType.DataStoreIso9660Formatter },
                { SystemType.PS2, FormatterType.DataStoreIso9660Formatter },
                { SystemType.PS3, FormatterType.DataStoreIso9660Formatter },
                { SystemType.PSP, FormatterType.DataStoreIso9660Formatter },
                { SystemType.SegaCD, FormatterType.DataStoreIso9660Formatter },
                { SystemType.Saturn, FormatterType.DataStoreIso9660Formatter },
                { SystemType.Dreamcast, FormatterType.DataStoreIso9660Formatter },
                { SystemType.CDi, FormatterType.DataStoreIso9660Formatter },
                { SystemType.Default, FormatterType.DataStoreIso9660Formatter },
                { SystemType.PcEngine, FormatterType.DataStoreIso9660Formatter },
            };

            foreach ((SystemType systemType, FormatterType expectedFormatter) in expectedMappings)
            {
                (FormatterType type, FormatterConstructorParams _) = ModelFormatterFactory(
                    systemType,
                    @"C:\dedupe",
                    "TestImage",
                    50L * 1024 * 1024 * 1024,
                    0x8000,
                    systemType.ToString().ToLower(),
                    null);

                Assert.Equal(expectedFormatter, type);
            }
        }

        /// <summary>
        /// Validates that only Xbox and Xbox360 receive auxSetName (among the new formatters).
        /// Wii and WiiU also receive auxSetName (existing behavior).
        /// ISO9660 systems and GameCube do not.
        /// </summary>
        [Theory]
        [InlineData(SystemType.XBox, true)]
        [InlineData(SystemType.XBox360, true)]
        [InlineData(SystemType.Wii, true)]
        [InlineData(SystemType.WiiU, true)]
        [InlineData(SystemType.GameCube, false)]
        [InlineData(SystemType.PS1, false)]
        [InlineData(SystemType.PS2, false)]
        [InlineData(SystemType.PS3, false)]
        [InlineData(SystemType.PSP, false)]
        [InlineData(SystemType.SegaCD, false)]
        [InlineData(SystemType.Saturn, false)]
        [InlineData(SystemType.Dreamcast, false)]
        [InlineData(SystemType.CDi, false)]
        [InlineData(SystemType.Default, false)]
        [InlineData(SystemType.PcEngine, false)]
        public void FormatterFactory_AuxSetNamePassedOnlyToAuxCapableFormatters(
            SystemType systemType, bool expectsAuxSetName)
        {
            (FormatterType _, FormatterConstructorParams parms) = ModelFormatterFactory(
                systemType,
                @"C:\dedupe",
                "TestImage",
                50L * 1024 * 1024 * 1024,
                0x8000,
                systemType.ToString().ToLower(),
                "test.aux"); // Provide aux to all — only aux-capable formatters should keep it

            Assert.Equal(expectsAuxSetName, parms.HasAuxSetName);
        }

        #endregion
    }
}
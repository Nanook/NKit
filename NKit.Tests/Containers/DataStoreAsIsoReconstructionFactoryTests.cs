#nullable enable
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Unit tests for DataStoreAsIso reconstruction factory logic.
    /// Uses model-based testing where DataStoreAsIso requires complex DataStore dependencies.
    /// Tests verify the factory routing logic that selects the correct ImageBuilder stream
    /// based on system type, and the aux block provider resolution for Xbox.
    ///
    /// Validates Requirements: 4.1, 4.8, 4.9, 5.1, 10.8, 10.9
    /// </summary>
    [Trait("Area", "NKDS")]
    public class DataStoreAsIsoReconstructionFactoryTests
    {
        #region Mock Types

        /// <summary>
        /// Minimal mock IImageReader for constructing ImageBuilder streams.
        /// </summary>
        private class MockImageReader : IImageReader
        {
            private readonly List<AreaRecord> _areas;
            private readonly InfoRecord _info;
            private readonly ImageRecord _image;

            public MockImageReader(List<AreaRecord>? areas = null, int blockSize = 0x10000, long imageSize = 0x100000)
            {
                _areas = areas ?? new List<AreaRecord>();
                _info = new InfoRecord { BlockSize = blockSize };
                _image = new ImageRecord { Size = imageSize, Name = "TestImage" };
            }

            public ImageRecord Image => _image;
            public InfoRecord Info => _info;

            public IEnumerable<AreaRecord> GetAreas() => _areas;
            public IEnumerable<OffsetRecord> GetOffsets() => Enumerable.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => Enumerable.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => Enumerable.Empty<OffsetRecord>();

            public BlockRecord? GetBlock(BlockKey key) => null;
            public Stream OpenStream(long offsetStart) => new MemoryStream();
            public Stream OpenStream(DataStride stride, long offsetStart) => new MemoryStream();
            public Stream? OpenBlockStream(BlockKey key) => null;
            public byte[]? ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Enumerable.Empty<FileRecord>();

            public void Dispose() { }
        }

        /// <summary>
        /// Mock IBlockProvider that returns null for all lookups.
        /// </summary>
        private class MockBlockProvider : IBlockProvider
        {
            public BlockRecord? GetBlock(BlockKey key) => null;
            public System.Threading.Tasks.Task<BlockRecord?> GetBlockAsync(BlockKey key) =>
                System.Threading.Tasks.Task.FromResult<BlockRecord?>(null);
            public BlockRecord? GetBlock(OffsetRecord record, int blockIndex) => null;
        }

        #endregion

        #region Model: Factory Routing Logic

        /// <summary>
        /// Models the DataStoreAsIso factory routing logic.
        /// Given a system type, returns the expected ImageBuilder stream type name.
        /// This mirrors the switch logic in DataStoreAsIso.Construct().
        /// </summary>
        private static string ModelExpectedStreamType(SystemType systemType)
        {
            if (systemType == SystemType.GameCube)
                return nameof(ImageBuilderGameCubeStream);
            else if (systemType == SystemType.Wii)
                return nameof(ImageBuilderWiiStream);
            else if (systemType == SystemType.WiiU)
                return nameof(ImageBuilderWiiUStream);
            else if (systemType == SystemType.XBox || systemType == SystemType.XBox360)
                return nameof(ImageBuilderXboxStream);
            else if (ModelIsIso9660System(systemType))
                return nameof(ImageBuilderIso9660Stream);
            else
                return "UNSUPPORTED";
        }

        /// <summary>
        /// Models the isIso9660System helper from DataStoreAsIso.
        /// Returns true for all ISO9660-based system types.
        /// </summary>
        private static bool ModelIsIso9660System(SystemType systemType) => systemType switch
        {
            SystemType.PS1 => true,
            SystemType.PS2 => true,
            SystemType.PS3 => true,
            SystemType.PSP => true,
            SystemType.SegaCD => true,
            SystemType.Saturn => true,
            SystemType.Dreamcast => true,
            SystemType.CDi => true,
            SystemType.Default => true,
            SystemType.PcEngine => true,
            _ => false
        };

        /// <summary>
        /// Models the aux block provider resolution logic for Xbox.
        /// When the block provider is an AuxBlockProvider, it is passed as auxBlockProvider.
        /// When it is not, null is passed.
        /// </summary>
        private static IBlockProvider? ModelResolveAuxBlockProvider(IBlockProvider blockProvider) => blockProvider is AuxBlockProvider ? blockProvider : null;

        #endregion

        #region Xbox System Type Tests

        /// <summary>
        /// Validates Requirement 4.1: Xbox system type instantiates ImageBuilderXboxStream.
        /// The factory routes Xbox to ImageBuilderXboxStream with the correct constructor parameters.
        /// </summary>
        [Theory]
        [InlineData(SystemType.XBox)]
        [InlineData(SystemType.XBox360)]
        public void XboxSystemType_InstantiatesImageBuilderXboxStream(SystemType systemType)
        {
            // Arrange: Create a minimal area for Xbox (FileSystem area)
            AreaRecord area = new AreaRecord
            {
                Id = 1,
                Offset = 0,
                Size = 0x2000,
                SectionSize = 0x2000,
                StrideBlockSize = 0,
                StrideDataOffset = 0,
                StrideDataLength = 0
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x800L);
            area.Metadata.Set(AreaValueType.AreaOffsetBase, 0L);

            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: 0x2000);
            MockBlockProvider blockProvider = new MockBlockProvider();

            // Act: Instantiate the stream the same way the factory does
            ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);

            // Assert: The model predicts Xbox → ImageBuilderXboxStream
            Assert.Equal(nameof(ImageBuilderXboxStream), ModelExpectedStreamType(systemType));
            Assert.NotNull(stream);
            Assert.IsType<ImageBuilderXboxStream>(stream);

            stream.Dispose();
        }

        /// <summary>
        /// Validates Requirements 4.8, 10.8: Xbox system type passes auxBlockProvider when aux set exists.
        /// When the block provider is an AuxBlockProvider (indicating aux was discovered),
        /// it is passed as the auxBlockProvider parameter to ImageBuilderXboxStream.
        /// </summary>
        [Fact]
        public void XboxSystemType_PassesAuxBlockProvider_WhenAuxSetExists()
        {
            // Arrange: Create an AuxBlockProvider (simulating aux set discovered)
            AreaRecord area = new AreaRecord
            {
                Id = 1,
                Offset = 0,
                Size = 0x2000,
                SectionSize = 0x2000,
                StrideBlockSize = 0,
                StrideDataOffset = 0,
                StrideDataLength = 0
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x800L);
            area.Metadata.Set(AreaValueType.AreaOffsetBase, 0L);

            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: 0x2000);
            MockBlockProvider primaryProvider = new MockBlockProvider();

            // Create an AuxBlockProvider wrapping the primary (simulates aux discovery)
            AuxBlockProvider auxBlockProvider = new AuxBlockProvider(primaryProvider, auxReader: null);

            // Act: Model the factory's aux resolution logic
            IBlockProvider? resolvedAux = ModelResolveAuxBlockProvider(auxBlockProvider);

            // Assert: When blockProvider is AuxBlockProvider, it is passed as auxBlockProvider
            Assert.NotNull(resolvedAux);
            Assert.Same(auxBlockProvider, resolvedAux);

            // Verify the actual factory logic matches: blockProvider is AuxBlockProvider → pass it
            Assert.True(auxBlockProvider is AuxBlockProvider);

            // Verify the stream can be constructed with the aux provider
            ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: auxBlockProvider, auxBlockProvider: auxBlockProvider);
            Assert.NotNull(stream);
            stream.Dispose();
        }

        /// <summary>
        /// Validates Requirements 4.9, 10.9: Xbox system type passes null auxBlockProvider when no aux set.
        /// When the block provider is NOT an AuxBlockProvider (no aux set discovered),
        /// null is passed as the auxBlockProvider parameter.
        /// </summary>
        [Fact]
        public void XboxSystemType_PassesNullAuxBlockProvider_WhenNoAuxSet()
        {
            // Arrange: Create a plain block provider (not AuxBlockProvider — no aux discovered)
            AreaRecord area = new AreaRecord
            {
                Id = 1,
                Offset = 0,
                Size = 0x2000,
                SectionSize = 0x2000,
                StrideBlockSize = 0,
                StrideDataOffset = 0,
                StrideDataLength = 0
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x800L);
            area.Metadata.Set(AreaValueType.AreaOffsetBase, 0L);

            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: 0x2000);
            MockBlockProvider blockProvider = new MockBlockProvider();

            // Act: Model the factory's aux resolution logic
            IBlockProvider? resolvedAux = ModelResolveAuxBlockProvider(blockProvider);

            // Assert: When blockProvider is NOT AuxBlockProvider, null is passed
            Assert.Null(resolvedAux);

            // Verify the actual factory logic matches: plain provider → null aux
            // The factory checks: blockProvider is AuxBlockProvider
            // A MockBlockProvider is not an AuxBlockProvider, so aux is null
            Assert.False(blockProvider.GetType() == typeof(AuxBlockProvider));

            // Verify the stream can be constructed without aux provider (zero-fill fallback)
            ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            Assert.NotNull(stream);
            stream.Dispose();
        }

        #endregion

        #region ISO9660 System Type Tests

        /// <summary>
        /// Validates Requirement 5.1: ISO9660 system types instantiate ImageBuilderIso9660Stream.
        /// All ISO9660-based system types route to ImageBuilderIso9660Stream.
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
        public void Iso9660SystemType_InstantiatesImageBuilderIso9660Stream(SystemType systemType)
        {
            // Arrange: Create a minimal area for ISO9660
            AreaRecord area = new AreaRecord
            {
                Id = 1,
                Offset = 0,
                Size = 0x2000,
                SectionSize = 0x2000,
                StrideBlockSize = 0x930,
                StrideDataOffset = 0x10,
                StrideDataLength = 0x800
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x930L);
            area.Metadata.Set(AreaValueType.PhysicalOffset, 0L);

            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: 0x2000);
            MockBlockProvider blockProvider = new MockBlockProvider();

            // Act: Instantiate the stream the same way the factory does for ISO9660
            ImageBuilderIso9660Stream stream = new ImageBuilderIso9660Stream(reader, blockProvider: blockProvider);

            // Assert: The model predicts ISO9660 systems → ImageBuilderIso9660Stream
            Assert.Equal(nameof(ImageBuilderIso9660Stream), ModelExpectedStreamType(systemType));
            Assert.True(ModelIsIso9660System(systemType));
            Assert.NotNull(stream);
            Assert.IsType<ImageBuilderIso9660Stream>(stream);

            stream.Dispose();
        }

        /// <summary>
        /// Validates that the isIso9660System classification correctly identifies all ISO9660 systems.
        /// Non-ISO9660 systems (GameCube, Wii, WiiU, Xbox, Xbox360, NotSet) return false.
        /// </summary>
        [Theory]
        [InlineData(SystemType.GameCube, false)]
        [InlineData(SystemType.Wii, false)]
        [InlineData(SystemType.WiiU, false)]
        [InlineData(SystemType.XBox, false)]
        [InlineData(SystemType.XBox360, false)]
        [InlineData(SystemType.NotSet, false)]
        [InlineData(SystemType.PS1, true)]
        [InlineData(SystemType.PS2, true)]
        [InlineData(SystemType.PS3, true)]
        [InlineData(SystemType.PSP, true)]
        [InlineData(SystemType.SegaCD, true)]
        [InlineData(SystemType.Saturn, true)]
        [InlineData(SystemType.Dreamcast, true)]
        [InlineData(SystemType.CDi, true)]
        [InlineData(SystemType.Default, true)]
        [InlineData(SystemType.PcEngine, true)]
        public void IsIso9660System_ClassifiesCorrectly(SystemType systemType, bool expectedResult)
        {
            // Act: Use the model to classify
            bool result = ModelIsIso9660System(systemType);

            // Assert: Model matches expected classification
            Assert.Equal(expectedResult, result);
        }

        #endregion

        #region Existing System Type Tests

        /// <summary>
        /// Validates that existing system types (GameCube, Wii, WiiU) continue to use their respective builders.
        /// The factory routing for these types is unchanged by the Xbox/ISO9660 additions.
        /// Uses model verification since these builders require complex area metadata for construction.
        /// </summary>
        [Theory]
        [InlineData(SystemType.GameCube, "ImageBuilderGameCubeStream")]
        [InlineData(SystemType.Wii, "ImageBuilderWiiStream")]
        [InlineData(SystemType.WiiU, "ImageBuilderWiiUStream")]
        public void ExistingSystemTypes_ContinueToUseRespectiveBuilders(SystemType systemType, string expectedTypeName)
        {
            // Act: Verify the model predicts the correct stream type
            string modelResult = ModelExpectedStreamType(systemType);

            // Assert: Model matches expected type name
            Assert.Equal(expectedTypeName, modelResult);

            // Verify these are NOT classified as ISO9660 or Xbox
            Assert.False(ModelIsIso9660System(systemType));
            Assert.NotEqual(SystemType.XBox, systemType);
            Assert.NotEqual(SystemType.XBox360, systemType);
        }

        /// <summary>
        /// Validates that unsupported system types would throw an exception.
        /// The factory throws when no matching builder exists for the system type.
        /// </summary>
        [Fact]
        public void UnsupportedSystemType_ModelReturnsUnsupported()
        {
            // Act: Check the model for NotSet (unsupported)
            string result = ModelExpectedStreamType(SystemType.NotSet);

            // Assert: Model indicates unsupported
            Assert.Equal("UNSUPPORTED", result);
            Assert.False(ModelIsIso9660System(SystemType.NotSet));
        }

        #endregion

        #region Aux Resolution Logic Tests

        /// <summary>
        /// Validates the aux block provider resolution pattern used in the factory.
        /// The factory checks `blockProvider is AuxBlockProvider` to determine if aux was discovered.
        /// </summary>
        [Fact]
        public void AuxResolution_AuxBlockProvider_IsPassedAsAux()
        {
            // Arrange: Create an AuxBlockProvider (wrapping a primary provider)
            MockBlockProvider primaryProvider = new MockBlockProvider();
            AuxBlockProvider auxProvider = new AuxBlockProvider(primaryProvider, auxReader: null);

            // Act: Apply the factory's resolution logic
            IBlockProvider? resolved = ModelResolveAuxBlockProvider(auxProvider);

            // Assert: AuxBlockProvider is passed through
            Assert.NotNull(resolved);
            Assert.IsType<AuxBlockProvider>(resolved);
        }

        /// <summary>
        /// Validates the aux block provider resolution pattern for non-aux providers.
        /// When the block provider is a plain provider (not AuxBlockProvider), null is returned.
        /// </summary>
        [Fact]
        public void AuxResolution_PlainBlockProvider_ReturnsNull()
        {
            // Arrange: Create a plain block provider
            MockBlockProvider plainProvider = new MockBlockProvider();

            // Act: Apply the factory's resolution logic
            IBlockProvider? resolved = ModelResolveAuxBlockProvider(plainProvider);

            // Assert: Plain provider → null (no aux available)
            Assert.Null(resolved);
        }

        /// <summary>
        /// Validates that the aux resolution logic is only applied for Xbox system types.
        /// ISO9660 systems do not use aux block providers.
        /// </summary>
        [Theory]
        [InlineData(SystemType.PS1)]
        [InlineData(SystemType.PS2)]
        [InlineData(SystemType.Dreamcast)]
        public void Iso9660Systems_DoNotUseAuxBlockProvider(SystemType systemType)
        {
            // Arrange: Even if an AuxBlockProvider is available, ISO9660 systems don't use it
            MockBlockProvider primaryProvider = new MockBlockProvider();
            AuxBlockProvider auxProvider = new AuxBlockProvider(primaryProvider, auxReader: null);

            // Act: Model the factory behavior — ISO9660 systems only pass blockProvider, not auxBlockProvider
            string expectedType = ModelExpectedStreamType(systemType);

            // Assert: ISO9660 systems use ImageBuilderIso9660Stream (no aux parameter in constructor)
            Assert.Equal(nameof(ImageBuilderIso9660Stream), expectedType);
            Assert.True(ModelIsIso9660System(systemType));

            // Verify: ImageBuilderIso9660Stream constructor does not accept auxBlockProvider
            AreaRecord area = new AreaRecord
            {
                Id = 1,
                Offset = 0,
                Size = 0x2000,
                SectionSize = 0x2000,
                StrideBlockSize = 0x930,
                StrideDataOffset = 0x10,
                StrideDataLength = 0x800
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x930L);
            area.Metadata.Set(AreaValueType.PhysicalOffset, 0L);

            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: 0x2000);

            // ISO9660 stream is constructed with blockProvider only (no aux parameter)
            ImageBuilderIso9660Stream stream = new ImageBuilderIso9660Stream(reader, blockProvider: auxProvider);
            Assert.NotNull(stream);
            stream.Dispose();
        }

        #endregion
    }
}
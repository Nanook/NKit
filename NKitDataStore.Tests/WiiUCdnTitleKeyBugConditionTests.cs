using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Bug condition exploration test for WiiU CDN VFS path title key discovery failure.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3**
    ///
    /// Property 1: Bug Condition — VFS CDN Title Key Discovery Failure
    ///
    /// The actual bug is that CodePagesEncodingProvider is not registered in the
    /// NKitDataStore/NKDS library, causing Encoding.GetEncoding("Shift-JIS") to throw
    /// during FST parsing in initializeFromAreas(). This exception is caught by the
    /// outer try/catch, but may prevent proper initialization of encryption state.
    ///
    /// The fix is to register CodePagesEncodingProvider.Instance in the NKitDataStore
    /// project so it's always available regardless of which host app loads the library.
    ///
    /// This test verifies that:
    /// 1. CodePagesEncodingProvider is registered (Shift-JIS encoding is available)
    /// 2. Key discovery works correctly on the VFS path
    /// 3. Encryption is applied to FileSystem area output
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class WiiUCdnTitleKeyBugConditionTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        public WiiUCdnTitleKeyBugConditionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose() { }

        /// <summary>
        /// Mock IImageReader that simulates VFS path behavior for a WiiU CDN image:
        /// - OpenStream throws (simulating pooled reader failure for metadata reads)
        /// - GetAreas returns areas with valid AreaValueType.TitleKey metadata
        /// - GetBlock returns plaintext data blocks for FileSystem areas
        /// - Image.Format is Cdn and Image.Size is set
        /// </summary>
        private class VfsCdnMockImageReader : IImageReader
        {
            private readonly List<AreaRecord> _areas;
            private readonly List<OffsetRecord> _offsets;
            private readonly string _titleKeyHex;
            private readonly Dictionary<BlockKey, BlockRecord> _blocks;
            private readonly byte[] _plaintextPattern;

            public ImageRecord Image { get; }
            public InfoRecord Info { get; }

            public VfsCdnMockImageReader(string titleKeyHex, long imageSize = 0x10000000, byte[] plaintextPattern = null)
            {
                _titleKeyHex = titleKeyHex;
                _plaintextPattern = plaintextPattern ?? GeneratePlaintextPattern();
                Image = new ImageRecord
                {
                    Id = 1,
                    Name = "TestCdnImage",
                    Size = imageSize,
                    Format = ImageFormat.Cdn,
                    System = "WiiU"
                };
                Info = new InfoRecord
                {
                    BlockSize = 0x10000,
                    MaxOffsetBlocks = 32
                };

                // Build areas that simulate a WiiU CDN image structure
                _areas = BuildCdnAreas(titleKeyHex, imageSize);
                _offsets = BuildOffsets(_areas, Info.BlockSize);
                _blocks = BuildBlocks(_offsets, _plaintextPattern, Info.BlockSize);
            }

            /// <summary>
            /// Gets the plaintext pattern used for block data, for comparison in assertions.
            /// </summary>
            public byte[] PlaintextPattern => _plaintextPattern;

            private static byte[] GeneratePlaintextPattern()
            {
                // Generate a recognizable plaintext pattern (0x41='A' repeated)
                byte[] pattern = new byte[0x10000];
                for (int i = 0; i < pattern.Length; i++)
                    pattern[i] = (byte)(0x41 + (i % 26)); // A-Z repeating
                return pattern;
            }

            private static List<AreaRecord> BuildCdnAreas(string titleKeyHex, long imageSize)
            {
                List<AreaRecord> areas = new List<AreaRecord>();
                long offset = 0;

                // ImageHeader area with TitleKey metadata
                AreaMetadata imageHeaderMeta = new AreaMetadata();
                imageHeaderMeta.Set(AreaValueType.FsType, "ImageHeader");
                imageHeaderMeta.Set(AreaValueType.TitleKey, titleKeyHex);
                imageHeaderMeta.Set(AreaValueType.Encrypted, false);
                areas.Add(new AreaRecord
                {
                    Id = 1,
                    ImageId = 1,
                    Offset = offset,
                    Size = 0x50000,
                    SectionSize = 0x200000,
                    Metadata = imageHeaderMeta
                });
                offset += 0x50000;

                // FstBlock area with TitleKey metadata
                AreaMetadata fstBlockMeta = new AreaMetadata();
                fstBlockMeta.Set(AreaValueType.FsType, "FstBlock");
                fstBlockMeta.Set(AreaValueType.TitleKey, titleKeyHex);
                fstBlockMeta.Set(AreaValueType.ContentIndex, 0L);
                fstBlockMeta.Set(AreaValueType.Encrypted, false);
                areas.Add(new AreaRecord
                {
                    Id = 2,
                    ImageId = 1,
                    Offset = offset,
                    Size = 0x10000,
                    SectionSize = 0x200000,
                    Metadata = fstBlockMeta
                });
                offset += 0x10000;

                // FileSystem area - encrypted content that should use the title key
                AreaMetadata fsMeta = new AreaMetadata();
                fsMeta.Set(AreaValueType.FsType, "FileSystem");
                fsMeta.Set(AreaValueType.TitleKey, titleKeyHex);
                fsMeta.Set(AreaValueType.Encrypted, true);
                fsMeta.Set(AreaValueType.Partition, 0L);
                fsMeta.Set(AreaValueType.ContentIndex, 0L);
                fsMeta.Set(AreaValueType.BlockSize, (long)0x10000);
                fsMeta.Set(AreaValueType.HashSize, 0L);
                fsMeta.Set(AreaValueType.PartitionType, "Game");
                long fsSize = 0x200000; // 2MiB section
                areas.Add(new AreaRecord
                {
                    Id = 3,
                    ImageId = 1,
                    Offset = offset,
                    Size = fsSize,
                    SectionSize = 0x200000,
                    Metadata = fsMeta
                });

                return areas;
            }

            private static List<OffsetRecord> BuildOffsets(List<AreaRecord> areas, int blockSize)
            {
                List<OffsetRecord> offsets = new List<OffsetRecord>();
                foreach (AreaRecord area in areas)
                {
                    int blockCount = (int)((area.Size + blockSize - 1) / blockSize);
                    List<BlockKey> blocks = new List<BlockKey>();
                    for (int i = 0; i < blockCount; i++)
                        blocks.Add(new BlockKey((ulong)(area.Offset + (i * blockSize)), (uint)i));

                    offsets.Add(new OffsetRecord
                    {
                        ImageId = 1,
                        Offset = area.Offset,
                        Size = area.Size,
                        OffsetStart = area.Offset,
                        Type = BlockType.File,
                        Blocks = blocks
                    });
                }
                return offsets;
            }

            private static Dictionary<BlockKey, BlockRecord> BuildBlocks(
                List<OffsetRecord> offsets, byte[] plaintextPattern, int blockSize)
            {
                Dictionary<BlockKey, BlockRecord> blocks = new Dictionary<BlockKey, BlockRecord>();
                foreach (OffsetRecord offset in offsets)
                {
                    if (offset.Blocks == null) continue;
                    for (int i = 0; i < offset.Blocks.Count; i++)
                    {
                        BlockKey key = offset.Blocks[i];
                        int size = (int)Math.Min(blockSize, offset.Size - ((long)i * blockSize));
                        byte[] data = new byte[size];
                        for (int j = 0; j < size; j++)
                            data[j] = plaintextPattern[j % plaintextPattern.Length];
                        blocks[key] = new BlockRecord(key, CompressionType.None, data);
                    }
                }
                return blocks;
            }

            public void Dispose() { }

            /// <summary>
            /// Simulates VFS path failure: OpenStream throws for metadata area offsets.
            /// </summary>
            public Stream OpenStream(long offsetStart)
            {
                throw new InvalidOperationException(
                    "Simulated VFS pooled reader failure: cannot serve OpenStream for metadata area");
            }

            public Stream OpenStream(DataStride stride, long offsetStart) => throw new InvalidOperationException("Simulated VFS pooled reader failure");

            public IEnumerable<AreaRecord> GetAreas() => _areas;
            public IEnumerable<OffsetRecord> GetOffsets() => _offsets;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart)
                => _offsets.Where(o => o.OffsetStart == offsetStart);
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length)
                => _offsets.Where(o => o.Offset >= startOffset && o.Offset < startOffset + length);
            public BlockRecord GetBlock(BlockKey key)
                => _blocks.TryGetValue(key, out BlockRecord block) ? block : null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public byte[] ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Bug Condition: CodePagesEncodingProvider must be registered in the NKitDataStore
        /// library so that Encoding.GetEncoding("Shift-JIS") works during FST parsing in
        /// initializeFromAreas(). Without this registration, the NKDS UI/VFS path fails
        /// because the UI app does not register the provider.
        ///
        /// On unfixed code, this FAILS because Shift-JIS encoding is not available.
        /// The fix registers CodePagesEncodingProvider in the NKitDataStore project.
        /// </summary>
        [Fact]
        public void ShiftJisEncoding_MustBeAvailable_ForFstParsing()
        {
            // This test verifies that the Shift-JIS encoding is available.
            // On unfixed code (without CodePagesEncodingProvider registered in NKitDataStore),
            // this will throw ArgumentException: 'Shift-JIS' is not a supported encoding name.
            //
            // The fix is to add Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            // to the NKitDataStore project so it's always available.

            Encoding shiftJis = Encoding.GetEncoding("Shift-JIS");

            Assert.NotNull(shiftJis);
            Assert.True(shiftJis.CodePage == 932,
                "Shift-JIS encoding should be code page 932 (Japanese)");
        }

        /// <summary>
        /// **Validates: Requirements 2.1, 2.2, 2.3**
        ///
        /// Property 1: Bug Condition — For any WiiU CDN image read through the VFS path
        /// (with cachedOffsets and sharedBufferCache) where readAreaBytes fails for metadata
        /// areas but AreaValueType.TitleKey exists in area metadata:
        /// 1. The Key property SHALL NOT be null after construction
        /// 2. Reading FileSystem area data SHALL produce encrypted output (not plaintext)
        ///
        /// On unfixed code, this may FAIL if the Shift-JIS encoding failure cascades
        /// to prevent proper encryption state initialization.
        ///
        /// The title key is varied using FsCheck to generate different 16-byte hex keys.
        /// </summary>
        [Property(MaxTest = 20)]
        public bool VfsCdnConstruction_Key_MustBeDiscoveredFromAreaMetadata(NonNegativeInt seedWrapper)
        {
            // Generate a deterministic 16-byte title key from the seed
            byte[] keyBytes = new byte[16];
            Random rng = new Random(seedWrapper.Get);
            rng.NextBytes(keyBytes);
            // Ensure key is non-zero (valid title key)
            if (keyBytes[0] == 0) keyBytes[0] = 1;
            string titleKeyHex = BitConverter.ToString(keyBytes).Replace("-", "");

            // Create mock reader simulating VFS path failure
            using VfsCdnMockImageReader reader = new VfsCdnMockImageReader(titleKeyHex);

            // Build cachedOffsets (non-null triggers VFS path in ImageBuilder base)
            List<AreaRecord> areas = reader.GetAreas().OrderBy(a => a.Offset).ToList();
            List<OffsetRecord> offsets = reader.GetOffsets().OrderBy(o => o.Offset).ToList();
            Dictionary<long, OffsetRecord> offsetsByPosition = offsets.ToDictionary(o => o.Offset);
            OffsetsManager offsetsManager = new OffsetsManager(reader);
            OffsetsManagerCacheResult cachedOffsets = new OffsetsManagerCacheResult(offsetsManager, areas, offsetsByPosition);

            // Create shared buffer cache (non-null triggers VFS path)
            int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0x200000).Max();
            using ImageBufferCache sharedBufferCache = new ImageBufferCache(reader.Image.Id, sectionSize, maxSize: 4);

            // Construct ImageBuilderWiiUStream with VFS path parameters
            ImageBuilderWiiUStream builder;
            try
            {
                builder = new ImageBuilderWiiUStream(
                    reader,
                    maxCachedBuffers: 4,
                    disposeReader: false,
                    encrypt: true,
                    blockProvider: null,
                    sharedBufferCache: sharedBufferCache,
                    cachedOffsets: cachedOffsets);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"COUNTEREXAMPLE: Construction threw {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            using (builder)
            {
                // Assert 1: Key must NOT be null
                bool keyDiscovered = builder.Key != null && builder.Key.Length > 0;

                if (!keyDiscovered)
                {
                    _output.WriteLine(
                        $"COUNTEREXAMPLE: Key is null/empty after VFS CDN construction. " +
                        $"TitleKey '{titleKeyHex}' exists in area metadata but was not discovered. " +
                        $"Bug confirmed: title key discovery failed on VFS path.");
                    return false;
                }

                // Assert 2: Key must equal the expected title key value
                string actualKeyHex = BitConverter.ToString(builder.Key).Replace("-", "");
                bool keyCorrect = string.Equals(actualKeyHex, titleKeyHex, StringComparison.OrdinalIgnoreCase);

                if (!keyCorrect)
                {
                    _output.WriteLine(
                        $"COUNTEREXAMPLE: Key was discovered but has wrong value. " +
                        $"Expected '{titleKeyHex}', got '{actualKeyHex}'.");
                    return false;
                }

                // Assert 3: Reading FileSystem area data must produce encrypted output
                AreaRecord fsArea = areas.First(a => a.Metadata.GetString(AreaValueType.FsType) == "FileSystem");
                builder.Position = fsArea.Offset;

                byte[] outputBuffer = new byte[0x10000];
                int bytesRead = builder.Read(outputBuffer, 0, outputBuffer.Length);

                if (bytesRead == 0)
                {
                    _output.WriteLine(
                        $"COUNTEREXAMPLE: Read returned 0 bytes from FileSystem area.");
                    return false;
                }

                // The output should NOT match the plaintext pattern if encryption was applied
                byte[] plaintextPattern = reader.PlaintextPattern;
                bool isPlaintext = true;
                for (int i = 0; i < Math.Min(bytesRead, plaintextPattern.Length); i++)
                {
                    if (outputBuffer[i] != plaintextPattern[i % plaintextPattern.Length])
                    {
                        isPlaintext = false;
                        break;
                    }
                }

                if (isPlaintext && bytesRead > 0)
                {
                    _output.WriteLine(
                        $"COUNTEREXAMPLE: FileSystem area output is PLAINTEXT (not encrypted). " +
                        $"Key '{titleKeyHex}' was discovered but encryption was not applied. " +
                        $"Bug confirmed: VFS CDN path outputs unencrypted data.");
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 2.3**
        ///
        /// Verifies that the VFS CDN path produces encrypted output for FileSystem areas.
        /// On unfixed code, the output may be plaintext because the Shift-JIS encoding
        /// failure prevents proper initialization of encryption state.
        /// </summary>
        [Fact]
        public void VfsCdnConstruction_FileSystemOutput_MustBeEncrypted()
        {
            string titleKeyHex = "0123456789ABCDEF0123456789ABCDEF";

            using VfsCdnMockImageReader reader = new VfsCdnMockImageReader(titleKeyHex);

            List<AreaRecord> areas = reader.GetAreas().OrderBy(a => a.Offset).ToList();
            List<OffsetRecord> offsets = reader.GetOffsets().OrderBy(o => o.Offset).ToList();
            Dictionary<long, OffsetRecord> offsetsByPosition = offsets.ToDictionary(o => o.Offset);
            OffsetsManager offsetsManager = new OffsetsManager(reader);
            OffsetsManagerCacheResult cachedOffsets = new OffsetsManagerCacheResult(offsetsManager, areas, offsetsByPosition);

            int sectionSize = areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0x200000).Max();
            using ImageBufferCache sharedBufferCache = new ImageBufferCache(reader.Image.Id, sectionSize, maxSize: 4);

            using ImageBuilderWiiUStream builder = new ImageBuilderWiiUStream(
                reader,
                maxCachedBuffers: 4,
                disposeReader: false,
                encrypt: true,
                blockProvider: null,
                sharedBufferCache: sharedBufferCache,
                cachedOffsets: cachedOffsets);

            // Verify key is discovered
            Assert.NotNull(builder.Key);
            Assert.True(builder.Key.Length > 0, "Key should have non-zero length");
            _output.WriteLine($"Key discovered: {BitConverter.ToString(builder.Key).Replace("-", "")}");

            // Seek to FileSystem area and read data
            AreaRecord fsArea = areas.First(a => a.Metadata.GetString(AreaValueType.FsType) == "FileSystem");
            builder.Position = fsArea.Offset;

            byte[] outputBuffer = new byte[0x10000];
            int bytesRead = builder.Read(outputBuffer, 0, outputBuffer.Length);

            _output.WriteLine($"Read {bytesRead} bytes from FileSystem area at offset 0x{fsArea.Offset:X}");
            if (bytesRead > 0)
                _output.WriteLine($"First 32 bytes: {BitConverter.ToString(outputBuffer, 0, Math.Min(32, bytesRead))}");

            Assert.True(bytesRead > 0, "Should read data from FileSystem area");

            // Verify output is NOT plaintext — encryption must have been applied
            byte[] plaintextPattern = reader.PlaintextPattern;
            bool isPlaintext = true;
            for (int i = 0; i < Math.Min(bytesRead, plaintextPattern.Length); i++)
            {
                if (outputBuffer[i] != plaintextPattern[i % plaintextPattern.Length])
                {
                    isPlaintext = false;
                    break;
                }
            }

            Assert.False(isPlaintext,
                "FileSystem area output should be ENCRYPTED, not plaintext. " +
                "Bug confirmed: VFS CDN path outputs unencrypted data.");
        }
    }
}
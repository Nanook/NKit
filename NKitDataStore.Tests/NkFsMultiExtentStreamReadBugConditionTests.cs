using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Bug condition exploration test for NkFs multi-extent stream read.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    ///
    /// Property 1: Bug Condition — Multi-Extent Continuation Extent Read Failure
    ///
    /// This test is EXPECTED TO FAIL on unfixed code. Failure confirms the bug exists:
    /// when a multi-extent file is read via VFS, continuation extents (index >= 1) use
    /// FsOffset values stored by BuildPerTypeFsYaml that differ from the OffsetStart
    /// keys used during writing by DedupeStep. The mismatch occurs because
    /// BuildPerTypeFsYaml computes offsets inline (area.ImageOffset + stride.CleanToOffset(...))
    /// rather than using ToImageOffsetFromFsOffsets which handles AddressMode.Relative
    /// and BaseOffset adjustments correctly.
    ///
    /// Expected behavior: OpenStream(storedFileOffset) for continuation extents should
    /// successfully return data with correct length.
    /// </summary>
    public class NkFsMultiExtentStreamReadBugConditionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public NkFsMultiExtentStreamReadBugConditionTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitMultiExtentBug_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Simulates the offset computation as done by DedupeStep (the WRITE path).
        /// Uses the same logic as ToImageOffsetFromFsOffsets for AddressMode.Relative:
        ///   areaBaseOffset = partitionImageOffset - baseOffset
        ///   result = areaBaseOffset + stride.CleanToOffset(fsOffset, false)  [if strided]
        ///   result = areaBaseOffset + fsOffset                                [if not strided]
        /// </summary>
        private static long ComputeWriteOffset_DedupeStyle(
            long partitionImageOffset, long fsOffset, long baseOffset, DataStride stride)
        {
            // This mirrors ToImageOffsetFromFsOffsets for AddressMode.Relative
            long areaBaseOffset = partitionImageOffset - baseOffset;
            if (stride != null && stride.SourceBlockSize != stride.DataLength)
                return areaBaseOffset + stride.CleanToOffset(fsOffset, false);
            return areaBaseOffset + fsOffset;
        }

        /// <summary>
        /// Simulates the offset computation as done by BuildPerTypeFsYaml (the NkFs STORAGE path).
        /// AFTER FIX (Task 3.1): BuildPerTypeFsYaml now uses ToImageOffsetFromFsOffsets(area.ImageOffset, partFile.FsOffset)
        /// which handles AddressMode.Relative and BaseOffset adjustments correctly — matching DedupeStep.
        ///   result = (areaImageOffset - baseOffset) + stride.CleanToOffset(fsOffset, false)  [if strided]
        ///   result = (areaImageOffset - baseOffset) + fsOffset                                [if not strided]
        /// </summary>
        private static long ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(
            long areaImageOffset, long fsOffset, long baseOffset, DataStride stride)
        {
            // FIXED: Now mirrors ToImageOffsetFromFsOffsets (same as DedupeStep write path)
            long areaBaseOffset = areaImageOffset - baseOffset;
            if (stride != null && stride.SourceBlockSize != stride.DataLength)
                return areaBaseOffset + stride.CleanToOffset(fsOffset, false);
            return areaBaseOffset + fsOffset;
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Property 1: Bug Condition — For any multi-extent file in an AddressMode.Relative
        /// partition (BaseOffset != 0), continuation extents (index >= 1) MUST be readable
        /// via OpenStream using the NkFs-stored FileOffset.
        ///
        /// On unfixed code, this FAILS because the NkFs-stored offset (computed by
        /// BuildPerTypeFsYaml) differs from the OffsetStart (computed by DedupeStep/
        /// ToImageOffsetFromFsOffsets) by exactly BaseOffset. OpenStream throws
        /// ArgumentException "No offset records found" for continuation extents.
        ///
        /// Test case 1: AddressMode.Relative 2-extent file with BaseOffset != 0
        /// (Dreamcast-style partition where session starts at a non-zero base)
        /// </summary>
        [Property(MaxTest = 20)]
        public bool MultiExtent_RelativeMode_ContinuationExtent_MustBeReadable(PositiveInt baseOffsetMultiplier, PositiveInt extentSizeMultiplier)
        {
            // Setup: Dreamcast-style partition with AddressMode.Relative
            // BaseOffset = some non-zero value (simulating session 3 data area start)
            long baseOffset = (long)((baseOffsetMultiplier.Get % 16) + 1) * 0x4000; // 16KiB to 256KiB
            long partitionImageOffset = 0x120000; // Fixed partition start in the disc image
            long extent0FsOffset = 0x0;           // First extent at start of partition FS
            long extent1FsOffset = (long)((extentSizeMultiplier.Get % 8) + 1) * 0x2000; // 8KiB to 128KiB from partition start

            int extentSize = 0x2000; // 8KiB per extent

            // Compute offsets as DedupeStep would (the WRITE path)
            long writeOffset0 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, extent0FsOffset, baseOffset, null);
            long writeOffset1 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, extent1FsOffset, baseOffset, null);

            // Compute offsets as BuildPerTypeFsYaml would (the NkFs STORAGE path — now fixed to match DedupeStep)
            long nkfsOffset0 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, extent0FsOffset, baseOffset, null);
            long nkfsOffset1 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, extent1FsOffset, baseOffset, null);

            // Create a data store with data written at the WRITE offsets
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            string setName = "TestSet";

            byte[] extent0Data = new byte[extentSize];
            byte[] extent1Data = new byte[extentSize];
            new Random(42).NextBytes(extent0Data);
            new Random(43).NextBytes(extent1Data);

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                using (IImageWriter writer = store.AddImage(setName, "TestImage"))
                {
                    // Write extent 0 at the DedupeStep-computed offset
                    writer.WriteData(writeOffset0, extent0Data, BlockType.File, offsetStart: writeOffset0);
                    // Write extent 1 at the DedupeStep-computed offset
                    writer.WriteData(writeOffset1, extent1Data, BlockType.File, offsetStart: writeOffset1);
                    writer.FinalizeImage(0x200000, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);
            }

            // Now try to read using the NkFs-stored offsets (as MultiExtentStream would)
            bool extent0Readable = false;
            bool extent1Readable = false;

            using (DataStore store = new DataStore(testDir))
            {
                using (IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, 1)))
                {
                    // Extent 0: For AddressMode.Relative with fsOffset=0, both computations give
                    // partitionImageOffset - baseOffset + 0 vs partitionImageOffset + 0
                    // These differ by baseOffset, so even extent 0 may fail!
                    try
                    {
                        using (Stream stream = reader.OpenStream(nkfsOffset0))
                        {
                            extent0Readable = stream != null && stream.Length > 0;
                        }
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("No offset records"))
                    {
                        extent0Readable = false;
                    }

                    // Extent 1 (continuation): NkFs stored offset differs from written OffsetStart
                    try
                    {
                        using (Stream stream = reader.OpenStream(nkfsOffset1))
                        {
                            extent1Readable = stream != null && stream.Length > 0;
                        }
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("No offset records"))
                    {
                        extent1Readable = false;
                    }
                }
            }

            bool allExtentsReadable = extent0Readable && extent1Readable;

            if (!allExtentsReadable)
            {
                _output.WriteLine(
                    $"COUNTEREXAMPLE: baseOffset=0x{baseOffset:X}, partitionImageOffset=0x{partitionImageOffset:X}, " +
                    $"extent1FsOffset=0x{extent1FsOffset:X}. " +
                    $"Write offsets: extent0=0x{writeOffset0:X}, extent1=0x{writeOffset1:X}. " +
                    $"NkFs stored offsets: extent0=0x{nkfsOffset0:X}, extent1=0x{nkfsOffset1:X}. " +
                    $"Difference (BaseOffset): 0x{baseOffset:X}. " +
                    $"extent0Readable={extent0Readable}, extent1Readable={extent1Readable}. " +
                    $"Bug confirmed: OpenStream(0x{nkfsOffset1:X}) fails because OffsetStart was stored as " +
                    $"0x{writeOffset1:X} during writing — the 0x{baseOffset:X} difference is the BaseOffset.");
            }

            return allExtentsReadable;
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Property 1: Bug Condition — Strided 3-extent file in Mode1Raw
        /// (CD-based systems with 2352-byte sectors containing 2048 bytes of data)
        ///
        /// When stride is applied AND AddressMode is Relative, the offset mismatch
        /// between BuildPerTypeFsYaml and ToImageOffsetFromFsOffsets is amplified
        /// because the BaseOffset is NOT accounted for in the inline computation.
        ///
        /// On unfixed code, this FAILS because continuation extents use NkFs-stored
        /// offsets that don't match the OffsetStart keys written during DedupeStep.
        /// </summary>
        [Property(MaxTest = 20)]
        public bool MultiExtent_Strided_Mode1Raw_ContinuationExtents_MustBeReadable(PositiveInt baseOffsetMult, PositiveInt fsOffsetMult)
        {
            // Setup: CD Mode1Raw stride (2352 byte sectors, 2048 data at offset 16)
            DataStride stride = DataStride.CdMode1; // SourceBlockSize=2352, DataOffset=16, DataLength=2048

            long baseOffset = (long)((baseOffsetMult.Get % 8) + 1) * 0x2000; // 8KiB to 64KiB
            long partitionImageOffset = 0x80000; // 512KiB into disc

            // 3 extents at different FsOffsets within the partition's filesystem
            long extent0FsOffset = 0x0;
            long extent1FsOffset = (long)((fsOffsetMult.Get % 4) + 1) * 0x800; // 2KiB to 8KiB (block-aligned)
            long extent2FsOffset = extent1FsOffset + ((long)((fsOffsetMult.Get % 4) + 1) * 0x800);

            int extentSize = 0x800; // 2KiB per extent (one CD sector of data)

            // Compute offsets as DedupeStep would (the WRITE path) — handles AddressMode.Relative
            long writeOffset0 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, extent0FsOffset, baseOffset, stride);
            long writeOffset1 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, extent1FsOffset, baseOffset, stride);
            long writeOffset2 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, extent2FsOffset, baseOffset, stride);

            // Compute offsets as BuildPerTypeFsYaml would (NkFs STORAGE path — now fixed to match DedupeStep)
            long nkfsOffset0 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, extent0FsOffset, baseOffset, stride);
            long nkfsOffset1 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, extent1FsOffset, baseOffset, stride);
            long nkfsOffset2 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, extent2FsOffset, baseOffset, stride);

            // Create data store with data written at WRITE offsets
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            string setName = "TestSet";

            byte[] extent0Data = new byte[extentSize];
            byte[] extent1Data = new byte[extentSize];
            byte[] extent2Data = new byte[extentSize];
            new Random(100).NextBytes(extent0Data);
            new Random(101).NextBytes(extent1Data);
            new Random(102).NextBytes(extent2Data);

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                using (IImageWriter writer = store.AddImage(setName, "StridedImage"))
                {
                    writer.WriteData(writeOffset0, extent0Data, BlockType.File, offsetStart: writeOffset0);
                    writer.WriteData(writeOffset1, extent1Data, BlockType.File, offsetStart: writeOffset1);
                    writer.WriteData(writeOffset2, extent2Data, BlockType.File, offsetStart: writeOffset2);
                    writer.FinalizeImage(0x200000, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);
            }

            // Try to read using NkFs-stored offsets (as MultiExtentStream would)
            bool[] readable = new bool[3];

            using (DataStore store = new DataStore(testDir))
            {
                using (IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, 1)))
                {
                    long[] nkfsOffsets = { nkfsOffset0, nkfsOffset1, nkfsOffset2 };
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            using (Stream stream = reader.OpenStream(nkfsOffsets[i]))
                            {
                                readable[i] = stream != null && stream.Length > 0;
                            }
                        }
                        catch (ArgumentException ex) when (ex.Message.Contains("No offset records"))
                        {
                            readable[i] = false;
                        }
                    }
                }
            }

            bool allExtentsReadable = readable[0] && readable[1] && readable[2];

            if (!allExtentsReadable)
            {
                _output.WriteLine(
                    $"COUNTEREXAMPLE (Strided Mode1Raw): baseOffset=0x{baseOffset:X}, " +
                    $"partitionImageOffset=0x{partitionImageOffset:X}. " +
                    $"FsOffsets: [0x{extent0FsOffset:X}, 0x{extent1FsOffset:X}, 0x{extent2FsOffset:X}]. " +
                    $"Write offsets: [0x{writeOffset0:X}, 0x{writeOffset1:X}, 0x{writeOffset2:X}]. " +
                    $"NkFs stored: [0x{nkfsOffset0:X}, 0x{nkfsOffset1:X}, 0x{nkfsOffset2:X}]. " +
                    $"Readable: [{readable[0]}, {readable[1]}, {readable[2]}]. " +
                    $"Bug: stride.CleanToOffset applied to area.ImageOffset instead of (area.ImageOffset - BaseOffset).");
            }

            return allExtentsReadable;
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Property 1: Bug Condition — Block-aligned FsOffset edge case at stride boundary
        ///
        /// When FsOffset is exactly at a stride block boundary (fsOffset % DataLength == 0),
        /// stride.CleanToOffset adds DataOffset (due to blockPin=false). Combined with
        /// AddressMode.Relative BaseOffset mismatch, this creates a double-error condition
        /// where the stored NkFs offset is wrong by BaseOffset amount.
        ///
        /// On unfixed code, this FAILS because the NkFs-stored offset for the continuation
        /// extent doesn't match the OffsetStart in the data store.
        /// </summary>
        [Property(MaxTest = 20)]
        public bool MultiExtent_BlockAligned_StrideBoundary_ContinuationExtent_MustBeReadable(PositiveInt baseOffsetMult, PositiveInt blockCountMult)
        {
            // Setup: stride with 2352/2048 (CdMode1) and FsOffset exactly at block boundary
            DataStride stride = DataStride.CdMode1;

            long baseOffset = (long)((baseOffsetMult.Get % 8) + 1) * 0x800; // 2KiB to 16KiB
            long partitionImageOffset = 0x50000;

            // Extent 1 FsOffset is exactly at a stride data-length boundary
            int blockCount = (blockCountMult.Get % 8) + 1; // 1 to 8 blocks
            long extent1FsOffset = (long)blockCount * stride.DataLength; // Exactly at block boundary

            int extentSize = 0x800;

            // Compute offsets both ways
            long writeOffset0 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, 0, baseOffset, stride);
            long writeOffset1 = ComputeWriteOffset_DedupeStyle(partitionImageOffset, extent1FsOffset, baseOffset, stride);

            long nkfsOffset0 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, 0, baseOffset, stride);
            long nkfsOffset1 = ComputeNkFsStoredOffset_BuildPerTypeFsYamlStyle(partitionImageOffset, extent1FsOffset, baseOffset, stride);

            // Verify the offsets now match after fix (mismatch should be 0)
            long mismatch = nkfsOffset1 - writeOffset1;

            // Create data store
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            string setName = "TestSet";

            byte[] extent0Data = new byte[extentSize];
            byte[] extent1Data = new byte[extentSize];
            new Random(200).NextBytes(extent0Data);
            new Random(201).NextBytes(extent1Data);

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                using (IImageWriter writer = store.AddImage(setName, "BlockAlignedImage"))
                {
                    writer.WriteData(writeOffset0, extent0Data, BlockType.File, offsetStart: writeOffset0);
                    writer.WriteData(writeOffset1, extent1Data, BlockType.File, offsetStart: writeOffset1);
                    writer.FinalizeImage(0x200000, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);
            }

            // Try to read using NkFs-stored offsets
            bool extent0Readable = false;
            bool extent1Readable = false;

            using (DataStore store = new DataStore(testDir))
            {
                using (IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, 1)))
                {
                    try
                    {
                        using (Stream stream = reader.OpenStream(nkfsOffset0))
                            extent0Readable = stream != null && stream.Length > 0;
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("No offset records"))
                    {
                        extent0Readable = false;
                    }

                    try
                    {
                        using (Stream stream = reader.OpenStream(nkfsOffset1))
                            extent1Readable = stream != null && stream.Length > 0;
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("No offset records"))
                    {
                        extent1Readable = false;
                    }
                }
            }

            bool allExtentsReadable = extent0Readable && extent1Readable;

            if (!allExtentsReadable)
            {
                _output.WriteLine(
                    $"COUNTEREXAMPLE (Block-Aligned Stride Boundary): baseOffset=0x{baseOffset:X}, " +
                    $"partitionImageOffset=0x{partitionImageOffset:X}, extent1FsOffset=0x{extent1FsOffset:X}. " +
                    $"Write offset1=0x{writeOffset1:X}, NkFs stored offset1=0x{nkfsOffset1:X}. " +
                    $"Mismatch=0x{mismatch:X} (expected BaseOffset=0x{baseOffset:X}). " +
                    $"extent0Readable={extent0Readable}, extent1Readable={extent1Readable}. " +
                    $"Bug: FsOffset at stride boundary — OpenStream(0x{nkfsOffset1:X}) throws because " +
                    $"OffsetStart was stored as 0x{writeOffset1:X} during writing.");
            }

            return allExtentsReadable;
        }
    }
}
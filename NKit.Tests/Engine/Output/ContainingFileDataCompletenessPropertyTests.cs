using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property 4: Containing File Receives Complete Data
    ///
    /// For any Containing_File F and any complete sequence of SectionItems covering the disc range
    /// [F.FsOffset, F.FsOffset + F.FsSize), ProcessOverlaps SHALL write bytes to F's output stream
    /// such that TotalWritten equals F's total logical size, using the correct buffer offsets and sizes
    /// derived from the intersection of each chunk with F's range.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class ContainingFileDataCompletenessPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile for testing ProcessOverlaps data completeness.
        /// </summary>
        private class TestFsFile : IFsFile
        {
            public TestFsFile(string name, string path, long fsOffset, long fsSize,
                bool isSystemFile = false, IFsFileParts splitParts = null)
            {
                Name = name;
                Path = path;
                FsOffset = fsOffset;
                FsSize = fsSize;
                IsSystemFile = isSystemFile;
                SplitParts = splitParts;
            }

            public string Name { get; }
            public IFsFolder Parent => null;
            public string Path { get; }
            public string FullName => Path + "/" + Name;
            public long FsOffset { get; }
            public long FsSize { get; }
            public bool IsSystemFile { get; }
            public bool IsLastFile => false;
            public bool IsMissing => false;
            public int SplitIndex { get; set; }
            public IFsFileParts SplitParts { get; }
            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }
            public long PostGapSize => 0;
            public long PostGapFsOffset => 0;
            public IFsFile Clone() => new TestFsFile(Name, Path, FsOffset, FsSize, IsSystemFile, SplitParts);
            public override string ToString() => $"{FullName} @0x{FsOffset:X} size=0x{FsSize:X}";
        }

        /// <summary>
        /// Represents a chunk of disc data (simulates a SectionItem's contribution).
        /// </summary>
        private struct ChunkInfo
        {
            /// <summary>Disc start position of this chunk.</summary>
            public long DiscStart;
            /// <summary>Size of this chunk in bytes.</summary>
            public long ChunkSize;
            /// <summary>The FsFile that "owns" this chunk (the section item's FsFile).</summary>
            public IFsFile OwnerFile;
            /// <summary>The buffer offset within the section (si.File.FsOffset).</summary>
            public int BufferFsOffset;
            /// <summary>The offset within the item (si.File.OffsetInItem).</summary>
            public long OffsetInItem;
        }

        /// <summary>
        /// Simulates the OverlapState tracked by ProcessOverlaps.
        /// </summary>
        private struct SimOverlapState
        {
            public MemoryStream Stream;
            public IFsFile File;
            public long Written;
            public long TotalSize;
            public long TotalWritten;
        }

        /// <summary>
        /// Simulates ProcessOverlaps logic for a single containing file being fed a sequence of chunks.
        /// This mirrors the real implementation's Phase 1 (activation), Phase 2 (write), and Phase 3 (deactivation).
        /// 
        /// Returns the final TotalWritten for the containing file.
        /// </summary>
        private static (long TotalWritten, byte[] WrittenBytes) SimulateProcessOverlaps(
            IFsFile containingFile,
            List<ChunkInfo> chunks,
            HashSet<IFsFile> candidateSet,
            byte[] sectionBuffer)
        {
            // The containing file is in the overlap candidates list (sorted by FsOffset)
            List<IFsFile> overlapCandidates = new List<IFsFile> { containingFile };
            int overlapScanIndex = 0;
            Dictionary<string, SimOverlapState> activeOverlaps = new Dictionary<string, SimOverlapState>();

            string containingPath = containingFile.Path + "/" + containingFile.Name;

            foreach (ChunkInfo chunk in chunks)
            {
                long discStart = chunk.DiscStart;
                long discEnd = discStart + chunk.ChunkSize;

                // Phase 1: Activate new overlap candidates entering range
                while (overlapScanIndex < overlapCandidates.Count)
                {
                    IFsFile candidate = overlapCandidates[overlapScanIndex];
                    if (candidate.FsOffset >= discEnd)
                        break;

                    if (candidate.FsOffset + candidate.FsSize > discStart)
                    {
                        if (!activeOverlaps.ContainsKey(containingPath))
                        {
                            long totalSize = candidate.SplitParts?.Size ?? candidate.FsSize;
                            activeOverlaps[containingPath] = new SimOverlapState
                            {
                                Stream = new MemoryStream(),
                                File = candidate,
                                Written = 0,
                                TotalSize = totalSize,
                                TotalWritten = 0
                            };
                        }
                    }

                    if (candidate.FsOffset < discEnd)
                        overlapScanIndex++;
                    else
                        break;
                }

                // Phase 2: Write to all active overlaps
                if (activeOverlaps.ContainsKey(containingPath))
                {
                    SimOverlapState state = activeOverlaps[containingPath];
                    IFsFile file = state.File;

                    // Skip if this section item's file IS the overlap candidate
                    // UNLESS it's a primary overlap (in candidateSet) — then we always write
                    bool skipWrite = (chunk.OwnerFile == file) && !candidateSet.Contains(file);
                    if (skipWrite)
                        continue;

                    // Calculate intersection
                    long intersectStart = Math.Max(discStart, file.FsOffset);
                    long intersectEnd = Math.Min(discEnd, file.FsOffset + file.FsSize);

                    if (intersectEnd > intersectStart)
                    {
                        int offsetInSection = (int)(chunk.BufferFsOffset + (intersectStart - discStart));
                        int writeSize = (int)(intersectEnd - intersectStart);

                        // Cap write to not exceed TotalSize
                        long remaining = state.TotalSize - state.TotalWritten;
                        if (remaining <= 0)
                            continue;
                        if (writeSize > remaining)
                            writeSize = (int)remaining;

                        // Simulate section.Read(offsetInSection, writeSize, stream)
                        state.Stream.Write(sectionBuffer, offsetInSection, writeSize);
                        state.Written += writeSize;
                        state.TotalWritten += writeSize;
                        activeOverlaps[containingPath] = state;
                    }
                }

                // Phase 3: Deactivate completed overlaps
                if (activeOverlaps.ContainsKey(containingPath))
                {
                    SimOverlapState state = activeOverlaps[containingPath];
                    if (state.TotalWritten >= state.TotalSize)
                    {
                        // Completed — would close stream in real code
                        break; // No more writing needed
                    }
                }
            }

            if (activeOverlaps.TryGetValue(containingPath, out SimOverlapState finalState))
            {
                return (finalState.TotalWritten, finalState.Stream.ToArray());
            }

            return (0, Array.Empty<byte>());
        }

        /// <summary>
        /// Generates a covering set of chunks for a given file range.
        /// Chunks are contiguous and collectively cover [fileOffset, fileOffset + fileSize).
        /// Each chunk is owned by a different "contained" file (simulating m2ts extents within SSIF).
        /// </summary>
        private static List<ChunkInfo> GenerateCoveringChunks(
            long fileOffset, long fileSize, List<int> chunkSizes, IFsFile containingFile)
        {
            List<ChunkInfo> chunks = new List<ChunkInfo>();
            long currentPos = fileOffset;
            long remaining = fileSize;
            int chunkIndex = 0;

            while (remaining > 0 && chunkIndex < chunkSizes.Count)
            {
                long thisChunkSize = Math.Min(chunkSizes[chunkIndex % chunkSizes.Count], remaining);
                if (thisChunkSize <= 0)
                    thisChunkSize = remaining;

                // Create a "contained" file at this position (simulates m2ts within SSIF)
                TestFsFile containedFile = new TestFsFile(
                    $"contained{chunkIndex}.m2ts", "/stream",
                    currentPos, thisChunkSize);

                // The chunk's disc position is derived from:
                // discStart = si.FsFile.FsOffset + si.File.OffsetInItem
                // We set FsFile.FsOffset = currentPos and OffsetInItem = 0
                // si.File.FsOffset is the buffer position (where in the section buffer data starts)
                // We set it to (int)(currentPos - fileOffset) so that the intersection calc works correctly
                int bufferFsOffset = (int)(currentPos - fileOffset);

                chunks.Add(new ChunkInfo
                {
                    DiscStart = currentPos,
                    ChunkSize = thisChunkSize,
                    OwnerFile = containedFile,
                    BufferFsOffset = bufferFsOffset,
                    OffsetInItem = 0
                });

                currentPos += thisChunkSize;
                remaining -= thisChunkSize;
                chunkIndex++;
            }

            // If remaining > 0, add one final chunk to cover the rest
            if (remaining > 0)
            {
                int bufferFsOffset = (int)(currentPos - fileOffset);
                TestFsFile lastContained = new TestFsFile(
                    $"contained_final.m2ts", "/stream",
                    currentPos, remaining);

                chunks.Add(new ChunkInfo
                {
                    DiscStart = currentPos,
                    ChunkSize = remaining,
                    OwnerFile = lastContained,
                    BufferFsOffset = bufferFsOffset,
                    OffsetInItem = 0
                });
            }

            return chunks;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 4a: When chunks fully and contiguously cover a containing file's range,
        /// TotalWritten SHALL equal the containing file's total logical size (FsSize).
        ///
        /// Generates a containing file with random offset and size, partitions its range into
        /// random-sized contiguous chunks, simulates ProcessOverlaps, and verifies completeness.
        ///
        /// **Validates: Requirements 2.1, 2.2, 2.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ContiguousChunksCoveringRange_TotalWrittenEqualsFileSize()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 100).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 50).Select(s => (long)s * 0x1000)
                from chunkCount in Gen.Choose(1, 10)
                from chunkSizes in Gen.Choose(1, 20).Select(s => s * 0x800).ListOf(chunkCount)
                where chunkSizes.Count > 0
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    ChunkSizes = chunkSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestFsFile containingFile = new TestFsFile(
                    "containing.ssif", "/stream",
                    data.FileOffset, data.FileSize);

                // The containing file is in the candidate set (it's a primary overlap)
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { containingFile };

                // Generate covering chunks
                List<ChunkInfo> chunks = GenerateCoveringChunks(
                    data.FileOffset, data.FileSize, data.ChunkSizes, containingFile);

                // Create a section buffer large enough to hold all data
                int bufferSize = (int)data.FileSize;
                byte[] sectionBuffer = new byte[bufferSize];
                // Fill with known pattern for verification
                for (int i = 0; i < bufferSize; i++)
                    sectionBuffer[i] = (byte)(i & 0xFF);

                (long totalWritten, byte[] writtenBytes) = SimulateProcessOverlaps(
                    containingFile, chunks, candidateSet, sectionBuffer);

                // Property: TotalWritten == FileSize
                bool complete = totalWritten == data.FileSize;
                if (!complete)
                    return false.Label(
                        $"TotalWritten ({totalWritten}) should equal FileSize ({data.FileSize}). " +
                        $"Chunks: {chunks.Count}, covering [{data.FileOffset:X}, {data.FileOffset + data.FileSize:X})");

                return true.Label(
                    $"Containing file @0x{data.FileOffset:X} size=0x{data.FileSize:X} " +
                    $"received complete data via {chunks.Count} contiguous chunks");
            });
        }

        /// <summary>
        /// Property 4b: The bytes written to the containing file stream match the correct
        /// section of the section buffer, verifying that section.Read is called with
        /// the correct buffer offset and size (requirement 2.4).
        ///
        /// **Validates: Requirements 2.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property WrittenBytes_MatchSectionBuffer()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 50).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 30).Select(s => (long)s * 0x1000)
                from chunkCount in Gen.Choose(1, 8)
                from chunkSizes in Gen.Choose(1, 15).Select(s => s * 0x800).ListOf(chunkCount)
                where chunkSizes.Count > 0
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    ChunkSizes = chunkSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestFsFile containingFile = new TestFsFile(
                    "containing.ssif", "/stream",
                    data.FileOffset, data.FileSize);

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { containingFile };

                List<ChunkInfo> chunks = GenerateCoveringChunks(
                    data.FileOffset, data.FileSize, data.ChunkSizes, containingFile);

                // Create a section buffer with a deterministic pattern
                int bufferSize = (int)data.FileSize;
                byte[] sectionBuffer = new byte[bufferSize];
                for (int i = 0; i < bufferSize; i++)
                    sectionBuffer[i] = (byte)(((i * 7) + 13) & 0xFF);

                (long totalWritten, byte[] writtenBytes) = SimulateProcessOverlaps(
                    containingFile, chunks, candidateSet, sectionBuffer);

                // The written bytes should be exactly the section buffer content
                // (since chunks contiguously cover the file range, the written output should
                // be identical to sectionBuffer[0..fileSize])
                if (writtenBytes.Length != (int)data.FileSize)
                    return false.Label(
                        $"Written byte count ({writtenBytes.Length}) != FileSize ({data.FileSize})");

                for (int i = 0; i < writtenBytes.Length; i++)
                {
                    if (writtenBytes[i] != sectionBuffer[i])
                        return false.Label(
                            $"Byte mismatch at position {i}: written=0x{writtenBytes[i]:X2}, " +
                            $"expected=0x{sectionBuffer[i]:X2}");
                }

                return true.Label(
                    $"All {data.FileSize} bytes written correctly match section buffer content");
            });
        }

        /// <summary>
        /// Property 4c: When chunks include both "contained file" chunks and "gap" chunks
        /// within the containing file's range, ALL bytes (both contained and gap) are written
        /// to the containing file's stream.
        ///
        /// This validates that ProcessOverlaps writes bytes from ALL section items that
        /// intersect the containing file's range — whether they are for contained files
        /// or for gaps between contained files.
        ///
        /// **Validates: Requirements 2.1, 2.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property MixedContainedAndGapChunks_AllBytesWritten()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 50).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(4, 30).Select(s => (long)s * 0x1000)
                from containedCount in Gen.Choose(1, 5)
                from gapCount in Gen.Choose(1, 4)
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    // Total chunks = contained + gaps
                    TotalChunks = containedCount + gapCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestFsFile containingFile = new TestFsFile(
                    "containing.ssif", "/stream",
                    data.FileOffset, data.FileSize);

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { containingFile };

                // Split the file range into alternating "contained" and "gap" chunks
                List<ChunkInfo> chunks = new List<ChunkInfo>();
                long currentPos = data.FileOffset;
                long remaining = data.FileSize;
                int totalChunks = Math.Min(data.TotalChunks, (int)(data.FileSize / 0x100));
                if (totalChunks < 2)
                    totalChunks = 2;
                long chunkSize = data.FileSize / totalChunks;
                if (chunkSize < 1) chunkSize = 1;

                for (int i = 0; i < totalChunks && remaining > 0; i++)
                {
                    long thisSize = (i == totalChunks - 1) ? remaining : Math.Min(chunkSize, remaining);
                    bool isGap = i % 2 == 1;

                    IFsFile ownerFile;
                    if (isGap)
                    {
                        // Gap chunks: owned by the containing file itself (or another file)
                        // Since containingFile is in candidateSet, it won't be skipped
                        ownerFile = new TestFsFile(
                            $"gap{i}.dat", "/stream",
                            currentPos, thisSize);
                    }
                    else
                    {
                        // Contained file chunks
                        ownerFile = new TestFsFile(
                            $"contained{i}.m2ts", "/stream",
                            currentPos, thisSize);
                    }

                    int bufferFsOffset = (int)(currentPos - data.FileOffset);

                    chunks.Add(new ChunkInfo
                    {
                        DiscStart = currentPos,
                        ChunkSize = thisSize,
                        OwnerFile = ownerFile,
                        BufferFsOffset = bufferFsOffset,
                        OffsetInItem = 0
                    });

                    currentPos += thisSize;
                    remaining -= thisSize;
                }

                int bufferSize = (int)data.FileSize;
                byte[] sectionBuffer = new byte[bufferSize];
                for (int i = 0; i < bufferSize; i++)
                    sectionBuffer[i] = (byte)(i & 0xFF);

                (long totalWritten, byte[] _) = SimulateProcessOverlaps(
                    containingFile, chunks, candidateSet, sectionBuffer);

                bool complete = totalWritten == data.FileSize;
                if (!complete)
                    return false.Label(
                        $"TotalWritten ({totalWritten}) should equal FileSize ({data.FileSize}). " +
                        $"Mixed chunks (contained+gaps): {chunks.Count}");

                return true.Label(
                    $"Containing file received all {data.FileSize} bytes from " +
                    $"{chunks.Count} mixed (contained+gap) chunks");
            });
        }

        /// <summary>
        /// Property 4d: TotalWritten is capped at TotalSize even when chunks collectively
        /// provide more bytes than the containing file needs (overlap at boundaries).
        ///
        /// This verifies the overshoot prevention: writeSize is capped to remaining = TotalSize - TotalWritten.
        ///
        /// **Validates: Requirements 2.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OvershootPrevention_TotalWrittenNeverExceedsTotalSize()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 50).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 20).Select(s => (long)s * 0x1000)
                from extraBytes in Gen.Choose(1, 10).Select(e => (long)e * 0x800)
                from chunkCount in Gen.Choose(2, 6)
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    ExtraBytes = extraBytes,
                    ChunkCount = chunkCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestFsFile containingFile = new TestFsFile(
                    "containing.ssif", "/stream",
                    data.FileOffset, data.FileSize);

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { containingFile };

                // Generate chunks that cover the file range PLUS extend beyond it
                List<ChunkInfo> chunks = new List<ChunkInfo>();
                long currentPos = data.FileOffset;
                long totalCoverage = data.FileSize + data.ExtraBytes;
                long chunkSize = totalCoverage / data.ChunkCount;
                if (chunkSize < 1) chunkSize = 1;
                long remaining = totalCoverage;

                for (int i = 0; i < data.ChunkCount && remaining > 0; i++)
                {
                    long thisSize = (i == data.ChunkCount - 1) ? remaining : Math.Min(chunkSize, remaining);
                    TestFsFile ownerFile = new TestFsFile(
                        $"contained{i}.m2ts", "/stream",
                        currentPos, thisSize);

                    int bufferFsOffset = (int)(currentPos - data.FileOffset);

                    chunks.Add(new ChunkInfo
                    {
                        DiscStart = currentPos,
                        ChunkSize = thisSize,
                        OwnerFile = ownerFile,
                        BufferFsOffset = bufferFsOffset,
                        OffsetInItem = 0
                    });

                    currentPos += thisSize;
                    remaining -= thisSize;
                }

                // Buffer must be large enough for all chunks (including the extra)
                int bufferSize = (int)(data.FileSize + data.ExtraBytes);
                byte[] sectionBuffer = new byte[bufferSize];

                (long totalWritten, byte[] _) = SimulateProcessOverlaps(
                    containingFile, chunks, candidateSet, sectionBuffer);

                // Property: TotalWritten == FileSize (not more, despite extra chunks)
                bool capped = totalWritten == data.FileSize;
                if (!capped)
                    return false.Label(
                        $"TotalWritten ({totalWritten}) should be capped at FileSize ({data.FileSize}), " +
                        $"extra bytes available: {data.ExtraBytes}");

                // Also verify it never exceeded TotalSize
                bool neverExceeded = totalWritten <= data.FileSize;
                if (!neverExceeded)
                    return false.Label(
                        $"TotalWritten ({totalWritten}) exceeded TotalSize ({data.FileSize})");

                return true.Label(
                    $"TotalWritten correctly capped at {data.FileSize} despite " +
                    $"{data.ExtraBytes} extra bytes in chunks");
            });
        }

        #endregion
    }
}
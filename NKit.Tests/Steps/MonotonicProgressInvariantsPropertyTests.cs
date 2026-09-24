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
    /// Property 8: Monotonic Progress Invariants
    ///
    /// For any sequence of ProcessOverlaps invocations on a given active overlap:
    /// TotalWritten SHALL monotonically increase, TotalWritten SHALL never exceed TotalSize,
    /// and _overlapScanIndex SHALL never decrease. All writes are append-only with no backward seeking.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class MonotonicProgressInvariantsPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile implementation for testing ProcessOverlaps progress tracking.
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
        /// Represents a simulated chunk that passes through a file's range,
        /// analogous to a SectionItem in the real code.
        /// </summary>
        private struct SimulatedChunk
        {
            public long DiscStart;
            public long ChunkSize;
            public long DiscEnd => DiscStart + ChunkSize;
        }

        /// <summary>
        /// Tracks the state of overlaps as ProcessOverlaps Phase 2 processes them.
        /// Mirrors the OverlapState struct from ExtractIsoStep.
        /// </summary>
        private struct OverlapState
        {
            public MemoryStream Stream;
            public IFsFile File;
            public long Written;
            public long TotalSize;
            public long TotalWritten;
        }

        /// <summary>
        /// Simulates the ProcessOverlaps logic for a sequence of chunks against
        /// a set of overlap candidates. Returns per-step snapshots of progress state
        /// for invariant verification.
        ///
        /// This mirrors the real implementation's three phases:
        /// Phase 1: Activate candidates whose range intersects the chunk
        /// Phase 2: Write intersection bytes to all active overlaps
        /// Phase 3: Deactivate completed overlaps
        /// </summary>
        private static List<ProgressSnapshot> SimulateProcessOverlaps(
            List<IFsFile> overlapCandidates,
            List<SimulatedChunk> chunks,
            HashSet<IFsFile> candidateSet)
        {
            List<ProgressSnapshot> snapshots = new List<ProgressSnapshot>();
            Dictionary<string, OverlapState> activeOverlaps = new Dictionary<string, OverlapState>();
            int overlapScanIndex = 0;

            foreach (SimulatedChunk chunk in chunks)
            {
                long discStart = chunk.DiscStart;
                long discEnd = chunk.DiscEnd;

                // Phase 1: Activate new overlap candidates entering range
                while (overlapScanIndex < overlapCandidates.Count)
                {
                    IFsFile candidate = overlapCandidates[overlapScanIndex];
                    if (candidate.FsOffset >= discEnd)
                        break;

                    if (candidate.FsOffset + candidate.FsSize > discStart)
                    {
                        string key = candidate.FullName;
                        if (!activeOverlaps.ContainsKey(key))
                        {
                            long totalSize = candidate.SplitParts?.Size ?? candidate.FsSize;
                            activeOverlaps[key] = new OverlapState
                            {
                                Stream = new MemoryStream(),
                                File = candidate,
                                Written = 0,
                                TotalSize = totalSize,
                                TotalWritten = 0
                            };
                        }
                        else
                        {
                            // Subsequent extent — update File reference, reset Written
                            OverlapState state = activeOverlaps[key];
                            state.File = candidate;
                            state.Written = 0;
                            if (!(candidateSet != null && candidateSet.Contains(candidate)))
                            {
                                state.TotalSize = candidate.FsSize;
                                state.TotalWritten = 0;
                            }
                            activeOverlaps[key] = state;
                        }
                    }

                    if (candidate.FsOffset < discEnd)
                        overlapScanIndex++;
                    else
                        break;
                }

                // Phase 2: Write to all active overlaps
                List<string> activeKeys = new List<string>(activeOverlaps.Keys);
                foreach (string key in activeKeys)
                {
                    OverlapState state = activeOverlaps[key];
                    IFsFile file = state.File;

                    // Calculate intersection
                    long intersectStart = Math.Max(discStart, file.FsOffset);
                    long intersectEnd = Math.Min(discEnd, file.FsOffset + file.FsSize);

                    if (intersectEnd > intersectStart)
                    {
                        int writeSize = (int)(intersectEnd - intersectStart);

                        // Cap write to not exceed TotalSize
                        long remaining = state.TotalSize - state.TotalWritten;
                        if (remaining <= 0)
                            continue;
                        if (writeSize > remaining)
                            writeSize = (int)remaining;

                        // Simulate the write (append-only to MemoryStream)
                        state.Stream.Write(new byte[writeSize], 0, writeSize);
                        state.Written += writeSize;
                        state.TotalWritten += writeSize;
                        activeOverlaps[key] = state;
                    }
                }

                // Phase 3: Deactivate completed overlaps
                List<string> completed = new List<string>();
                foreach (KeyValuePair<string, OverlapState> kvp in activeOverlaps)
                {
                    if (kvp.Value.TotalWritten >= kvp.Value.TotalSize)
                        completed.Add(kvp.Key);
                }
                foreach (string path in completed)
                {
                    OverlapState doneState = activeOverlaps[path];
                    doneState.Stream.Dispose();
                    activeOverlaps.Remove(path);
                }

                // Record snapshot after processing this chunk
                Dictionary<string, (long TotalWritten, long TotalSize)> overlapSnapshots = activeOverlaps.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (TotalWritten: kvp.Value.TotalWritten, TotalSize: kvp.Value.TotalSize));
                snapshots.Add(new ProgressSnapshot
                {
                    OverlapScanIndex = overlapScanIndex,
                    ActiveOverlapProgress = overlapSnapshots
                });
            }

            // Dispose any remaining streams
            foreach (KeyValuePair<string, OverlapState> kvp in activeOverlaps)
                kvp.Value.Stream.Dispose();

            return snapshots;
        }

        /// <summary>
        /// Snapshot of progress state after a single ProcessOverlaps invocation.
        /// </summary>
        private class ProgressSnapshot
        {
            public int OverlapScanIndex { get; set; }
            public Dictionary<string, (long TotalWritten, long TotalSize)> ActiveOverlapProgress { get; set; }
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
        ///
        /// Property 8: Monotonic Progress Invariants.
        /// For any sequence of ProcessOverlaps invocations on a given active overlap:
        /// - TotalWritten monotonically increases (non-decreasing)
        /// - TotalWritten never exceeds TotalSize
        /// - _overlapScanIndex never decreases
        ///
        /// Generates a containing file with random chunks that pass through its range,
        /// simulates ProcessOverlaps for each chunk, and verifies all three invariants.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property TotalWritten_MonotonicallyIncreases_NeverExceedsTotalSize_ScanIndexNeverDecreases()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 100).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 50).Select(s => (long)s * 0x10000)
                from chunkCount in Gen.Choose(2, 20)
                from chunkStarts in Gen.Choose(0, 150).Select(o => (long)o * 0x8000)
                    .ListOf(chunkCount)
                from chunkSizes in Gen.Choose(1, 10).Select(s => (long)s * 0x4000)
                    .ListOf(chunkCount)
                where chunkStarts.Count == chunkCount && chunkSizes.Count == chunkCount
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    ChunkStarts = chunkStarts.OrderBy(s => s).ToList(),
                    ChunkSizes = chunkSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create a containing file (overlap candidate)
                IFsFile overlapFile = (IFsFile)new TestFsFile(
                    "containing.ssif", "/BDMV/SSIF",
                    data.FileOffset, data.FileSize);

                List<IFsFile> overlapCandidates = new List<IFsFile> { overlapFile };
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { overlapFile };

                // Build chunks sorted by disc position (simulating section items arriving in order)
                List<SimulatedChunk> chunks = data.ChunkStarts.Zip(data.ChunkSizes, (start, size) =>
                    new SimulatedChunk { DiscStart = start, ChunkSize = size })
                    .ToList();

                // Simulate ProcessOverlaps
                List<ProgressSnapshot> snapshots = SimulateProcessOverlaps(overlapCandidates, chunks, candidateSet);

                // Verify invariant 1: _overlapScanIndex never decreases
                for (int i = 1; i < snapshots.Count; i++)
                {
                    if (snapshots[i].OverlapScanIndex < snapshots[i - 1].OverlapScanIndex)
                        return false.Label(
                            $"_overlapScanIndex decreased from {snapshots[i - 1].OverlapScanIndex} " +
                            $"to {snapshots[i].OverlapScanIndex} at step {i}");
                }

                // Verify invariant 2 & 3: For each active overlap, TotalWritten
                // is monotonically non-decreasing and never exceeds TotalSize
                Dictionary<string, long> previousTotalWritten = new Dictionary<string, long>();

                foreach (ProgressSnapshot snapshot in snapshots)
                {
                    foreach (KeyValuePair<string, (long TotalWritten, long TotalSize)> kvp in snapshot.ActiveOverlapProgress)
                    {
                        string key = kvp.Key;
                        long currentWritten = kvp.Value.TotalWritten;
                        long totalSize = kvp.Value.TotalSize;

                        // Check TotalWritten never exceeds TotalSize
                        if (currentWritten > totalSize)
                            return false.Label(
                                $"TotalWritten ({currentWritten}) exceeds TotalSize ({totalSize}) " +
                                $"for overlap '{key}'");

                        // Check TotalWritten is monotonically non-decreasing
                        if (previousTotalWritten.TryGetValue(key, out long prevWritten))
                        {
                            if (currentWritten < prevWritten)
                                return false.Label(
                                    $"TotalWritten decreased from {prevWritten} to {currentWritten} " +
                                    $"for overlap '{key}'");
                        }

                        previousTotalWritten[key] = currentWritten;
                    }
                }

                return true.Label(
                    $"All monotonic progress invariants hold over {snapshots.Count} steps " +
                    $"for file @0x{data.FileOffset:X} size=0x{data.FileSize:X}");
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
        ///
        /// Property 8 (supplementary): Monotonic invariants hold with multiple overlapping files.
        /// When multiple overlap candidates are active simultaneously, each one independently
        /// maintains its own monotonic progress guarantees.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property MultipleOverlaps_EachMaintainsMonotonicProgress()
        {
            var testGen =
                from overlapCount in Gen.Choose(2, 5)
                from baseOffset in Gen.Choose(0, 50).Select(o => (long)o * 0x10000)
                from spacing in Gen.Choose(1, 10).Select(s => (long)s * 0x10000)
                from fileSizes in Gen.Choose(2, 20).Select(s => (long)s * 0x8000)
                    .ListOf(overlapCount)
                from chunkCount in Gen.Choose(5, 30)
                from chunkSizes in Gen.Choose(1, 5).Select(s => (long)s * 0x4000)
                    .ListOf(chunkCount)
                where fileSizes.Count == overlapCount && chunkSizes.Count == chunkCount
                select new
                {
                    OverlapCount = overlapCount,
                    BaseOffset = baseOffset,
                    Spacing = spacing,
                    FileSizes = fileSizes.ToList(),
                    ChunkCount = chunkCount,
                    ChunkSizes = chunkSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create multiple overlap candidates at different offsets
                List<IFsFile> overlapCandidates = new List<IFsFile>();
                for (int i = 0; i < data.OverlapCount; i++)
                {
                    long offset = data.BaseOffset + (i * data.Spacing);
                    overlapCandidates.Add(new TestFsFile(
                        $"overlap_{i}.ssif", $"/BDMV/SSIF",
                        offset, data.FileSizes[i]));
                }

                // Sort by FsOffset (as BuildOverlapCandidates would)
                overlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(overlapCandidates);

                // Build chunks that sweep across the entire range of all overlaps
                long minOffset = overlapCandidates.Min(f => f.FsOffset);
                long maxEnd = overlapCandidates.Max(f => f.FsOffset + f.FsSize);
                long totalRange = maxEnd - minOffset;

                List<SimulatedChunk> chunks = new List<SimulatedChunk>();
                long pos = minOffset;
                int chunkIdx = 0;
                while (pos < maxEnd && chunkIdx < data.ChunkCount)
                {
                    long chunkSize = data.ChunkSizes[chunkIdx % data.ChunkSizes.Count];
                    chunks.Add(new SimulatedChunk { DiscStart = pos, ChunkSize = chunkSize });
                    pos += chunkSize;
                    chunkIdx++;
                }

                if (chunks.Count == 0)
                    return true.Label("No chunks generated (degenerate case)");

                // Simulate ProcessOverlaps
                List<ProgressSnapshot> snapshots = SimulateProcessOverlaps(overlapCandidates, chunks, candidateSet);

                // Verify invariant: _overlapScanIndex never decreases
                for (int i = 1; i < snapshots.Count; i++)
                {
                    if (snapshots[i].OverlapScanIndex < snapshots[i - 1].OverlapScanIndex)
                        return false.Label(
                            $"_overlapScanIndex decreased from {snapshots[i - 1].OverlapScanIndex} " +
                            $"to {snapshots[i].OverlapScanIndex} at step {i}");
                }

                // Verify per-overlap invariants
                Dictionary<string, long> previousTotalWritten = new Dictionary<string, long>();

                foreach (ProgressSnapshot snapshot in snapshots)
                {
                    foreach (KeyValuePair<string, (long TotalWritten, long TotalSize)> kvp in snapshot.ActiveOverlapProgress)
                    {
                        string key = kvp.Key;
                        long currentWritten = kvp.Value.TotalWritten;
                        long totalSize = kvp.Value.TotalSize;

                        // TotalWritten <= TotalSize
                        if (currentWritten > totalSize)
                            return false.Label(
                                $"TotalWritten ({currentWritten}) exceeds TotalSize ({totalSize}) " +
                                $"for overlap '{key}'");

                        // TotalWritten monotonically non-decreasing
                        if (previousTotalWritten.TryGetValue(key, out long prevWritten))
                        {
                            if (currentWritten < prevWritten)
                                return false.Label(
                                    $"TotalWritten decreased from {prevWritten} to {currentWritten} " +
                                    $"for overlap '{key}'");
                        }

                        previousTotalWritten[key] = currentWritten;
                    }
                }

                return true.Label(
                    $"All {data.OverlapCount} overlaps maintain monotonic progress over " +
                    $"{snapshots.Count} steps");
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.4**
        ///
        /// Property 8 (supplementary): TotalWritten is capped at TotalSize even when
        /// chunks provide more data than needed. The overshoot prevention logic ensures
        /// that the last write is truncated to fill exactly TotalSize bytes.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property TotalWritten_CappedAtTotalSize_EvenWithOversizedChunks()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 50).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 10).Select(s => (long)s * 0x4000)
                from chunkCount in Gen.Choose(1, 5)
                from chunkSizes in Gen.Choose(1, 20).Select(s => (long)s * 0x4000)
                    .ListOf(chunkCount)
                where chunkSizes.Count == chunkCount
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    ChunkSizes = chunkSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create a file with known TotalSize
                IFsFile overlapFile = (IFsFile)new TestFsFile(
                    "capped.ssif", "/BDMV/SSIF",
                    data.FileOffset, data.FileSize);

                List<IFsFile> overlapCandidates = new List<IFsFile> { overlapFile };
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { overlapFile };

                // Build chunks that start at or before the file and extend through and beyond it.
                // Use large chunk sizes to force the cap logic to trigger.
                List<SimulatedChunk> chunks = new List<SimulatedChunk>();
                long pos = data.FileOffset;
                foreach (long chunkSize in data.ChunkSizes)
                {
                    chunks.Add(new SimulatedChunk { DiscStart = pos, ChunkSize = chunkSize });
                    pos += chunkSize;
                }

                // Simulate ProcessOverlaps
                List<ProgressSnapshot> snapshots = SimulateProcessOverlaps(overlapCandidates, chunks, candidateSet);

                // Verify: at no point does TotalWritten exceed TotalSize
                foreach (ProgressSnapshot snapshot in snapshots)
                {
                    foreach (KeyValuePair<string, (long TotalWritten, long TotalSize)> kvp in snapshot.ActiveOverlapProgress)
                    {
                        if (kvp.Value.TotalWritten > kvp.Value.TotalSize)
                            return false.Label(
                                $"TotalWritten ({kvp.Value.TotalWritten}) exceeds " +
                                $"TotalSize ({kvp.Value.TotalSize}) for '{kvp.Key}'");
                    }
                }

                return true.Label(
                    $"TotalWritten correctly capped at TotalSize={data.FileSize} " +
                    $"even with potentially oversized chunks");
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.2, 5.3**
        ///
        /// Property 8 (supplementary): _overlapScanIndex advances monotonically and
        /// stream writes are append-only (stream position never decreases).
        ///
        /// Simulates a sequence of ProcessOverlaps calls with chunks arriving in disc order
        /// and verifies that:
        /// - _overlapScanIndex is non-decreasing
        /// - MemoryStream.Position (simulating FileStream) is non-decreasing (append-only)
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ScanIndex_MonotonicallyAdvances_StreamAppendOnly()
        {
            var testGen =
                from fileOffset in Gen.Choose(0, 50).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(5, 30).Select(s => (long)s * 0x8000)
                from chunkCount in Gen.Choose(3, 15)
                from chunkSizes in Gen.Choose(1, 5).Select(s => (long)s * 0x4000)
                    .ListOf(chunkCount)
                where chunkSizes.Count == chunkCount
                select new
                {
                    FileOffset = fileOffset,
                    FileSize = fileSize,
                    ChunkCount = chunkCount,
                    ChunkSizes = chunkSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create overlap candidate
                IFsFile overlapFile = (IFsFile)new TestFsFile(
                    "append.ssif", "/BDMV/SSIF",
                    data.FileOffset, data.FileSize);

                List<IFsFile> overlapCandidates = new List<IFsFile> { overlapFile };
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile> { overlapFile };

                // Build sequential non-overlapping chunks that cover the file range
                List<SimulatedChunk> chunks = new List<SimulatedChunk>();
                long pos = data.FileOffset;
                foreach (long chunkSize in data.ChunkSizes)
                {
                    chunks.Add(new SimulatedChunk { DiscStart = pos, ChunkSize = chunkSize });
                    pos += chunkSize;
                }

                // Track stream positions manually to verify append-only
                Dictionary<string, OverlapState> activeOverlaps = new Dictionary<string, OverlapState>();
                int overlapScanIndex = 0;
                int previousScanIndex = 0;
                Dictionary<string, long> previousStreamPositions = new Dictionary<string, long>();

                foreach (SimulatedChunk chunk in chunks)
                {
                    long discStart = chunk.DiscStart;
                    long discEnd = chunk.DiscEnd;

                    // Phase 1: Activate
                    while (overlapScanIndex < overlapCandidates.Count)
                    {
                        IFsFile candidate = overlapCandidates[overlapScanIndex];
                        if (candidate.FsOffset >= discEnd)
                            break;

                        if (candidate.FsOffset + candidate.FsSize > discStart)
                        {
                            string key = candidate.FullName;
                            if (!activeOverlaps.ContainsKey(key))
                            {
                                long totalSize = candidate.SplitParts?.Size ?? candidate.FsSize;
                                activeOverlaps[key] = new OverlapState
                                {
                                    Stream = new MemoryStream(),
                                    File = candidate,
                                    Written = 0,
                                    TotalSize = totalSize,
                                    TotalWritten = 0
                                };
                                previousStreamPositions[key] = 0;
                            }
                        }

                        if (candidate.FsOffset < discEnd)
                            overlapScanIndex++;
                        else
                            break;
                    }

                    // Verify: _overlapScanIndex never decreases
                    if (overlapScanIndex < previousScanIndex)
                        return false.Label(
                            $"_overlapScanIndex decreased from {previousScanIndex} to {overlapScanIndex}");
                    previousScanIndex = overlapScanIndex;

                    // Phase 2: Write
                    List<string> activeKeys = new List<string>(activeOverlaps.Keys);
                    foreach (string key in activeKeys)
                    {
                        OverlapState state = activeOverlaps[key];
                        IFsFile file = state.File;

                        long intersectStart = Math.Max(discStart, file.FsOffset);
                        long intersectEnd = Math.Min(discEnd, file.FsOffset + file.FsSize);

                        if (intersectEnd > intersectStart)
                        {
                            int writeSize = (int)(intersectEnd - intersectStart);
                            long remaining = state.TotalSize - state.TotalWritten;
                            if (remaining <= 0)
                                continue;
                            if (writeSize > remaining)
                                writeSize = (int)remaining;

                            state.Stream.Write(new byte[writeSize], 0, writeSize);
                            state.Written += writeSize;
                            state.TotalWritten += writeSize;
                            activeOverlaps[key] = state;

                            // Verify: stream position is non-decreasing (append-only)
                            long currentPos = state.Stream.Position;
                            if (currentPos < previousStreamPositions[key])
                                return false.Label(
                                    $"Stream position decreased for '{key}': " +
                                    $"was {previousStreamPositions[key]}, now {currentPos}");
                            previousStreamPositions[key] = currentPos;
                        }
                    }

                    // Phase 3: Deactivate completed
                    List<string> completed = activeOverlaps
                        .Where(kvp => kvp.Value.TotalWritten >= kvp.Value.TotalSize)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    foreach (string path in completed)
                    {
                        activeOverlaps[path].Stream.Dispose();
                        activeOverlaps.Remove(path);
                    }
                }

                // Cleanup
                foreach (KeyValuePair<string, OverlapState> kvp in activeOverlaps)
                    kvp.Value.Stream.Dispose();

                return true.Label(
                    $"_overlapScanIndex monotonically advances and stream writes are " +
                    $"append-only over {data.ChunkCount} chunks");
            });
        }

        #endregion
    }
}
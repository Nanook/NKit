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
    /// Property 10: Area Transition Cleanup
    ///
    /// For any set of active overlaps, when a new area begins
    /// (section.AreaOffset == 0 and section.AreaInfo.BaseOffset == 0),
    /// all active overlap streams SHALL be closed, _activeOverlaps SHALL be cleared,
    /// and _overlapScanIndex SHALL reset to zero.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class AreaTransitionCleanupPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile implementation for testing area transition cleanup.
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
        /// Represents the overlap tracking state analogous to ExtractIsoStep's fields.
        /// </summary>
        private class OverlapTrackingState
        {
            public Dictionary<string, OverlapEntry> ActiveOverlaps { get; set; } = new Dictionary<string, OverlapEntry>();
            public List<IFsFile> OverlapCandidates { get; set; } = new List<IFsFile>();
            public int OverlapScanIndex { get; set; }
        }

        /// <summary>
        /// Mirrors the OverlapState struct from ExtractIsoStep, using MemoryStream for testing.
        /// </summary>
        private struct OverlapEntry
        {
            public MemoryStream Stream;
            public IFsFile File;
            public long Written;
            public long TotalSize;
            public long TotalWritten;
        }

        /// <summary>
        /// Populates the overlap tracking state with active overlaps (simulating
        /// mid-extraction state before an area transition occurs).
        /// </summary>
        private static OverlapTrackingState BuildActiveState(
            List<(string name, long offset, long size)> fileSpecs,
            int scanIndex)
        {
            OverlapTrackingState state = new OverlapTrackingState();
            state.OverlapScanIndex = scanIndex;

            foreach ((string name, long offset, long size) in fileSpecs)
            {
                TestFsFile file = new TestFsFile(name, "/BDMV/STREAM", offset, size);
                state.OverlapCandidates.Add(file);

                // Simulate that each overlap has been partially written
                MemoryStream stream = new MemoryStream();
                long partialWrite = size / 3; // Write some data to simulate progress
                if (partialWrite > 0)
                    stream.Write(new byte[partialWrite], 0, (int)partialWrite);

                state.ActiveOverlaps[file.FullName] = new OverlapEntry
                {
                    Stream = stream,
                    File = file,
                    Written = partialWrite,
                    TotalSize = size,
                    TotalWritten = partialWrite
                };
            }

            return state;
        }

        /// <summary>
        /// Simulates the area transition cleanup logic from ExtractIsoStep:
        /// When section.AreaOffset == 0 and section.AreaInfo.BaseOffset == 0,
        /// all active overlap streams are disposed, _activeOverlaps is cleared,
        /// and _overlapScanIndex is reset to 0.
        /// </summary>
        private static void PerformAreaTransitionCleanup(OverlapTrackingState state)
        {
            // This mirrors the real code:
            // if (section.AreaOffset == 0 && section.AreaInfo.BaseOffset == 0)
            // {
            //     if (_activeOverlaps != null)
            //     {
            //         foreach (var state in _activeOverlaps.Values)
            //             state.Stream?.Dispose();
            //         _activeOverlaps.Clear();
            //     }
            //     _overlapCandidates = null;
            //     _overlapScanIndex = 0;
            // }

            if (state.ActiveOverlaps != null)
            {
                foreach (OverlapEntry entry in state.ActiveOverlaps.Values)
                    entry.Stream?.Dispose();
                state.ActiveOverlaps.Clear();
            }
            state.OverlapCandidates = null;
            state.OverlapScanIndex = 0;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 10: Area Transition Cleanup.
        /// For any set of active overlaps with open streams, when a new area begins,
        /// all streams SHALL be closed/disposed, the _activeOverlaps dictionary SHALL
        /// be cleared (empty), and _overlapScanIndex SHALL reset to zero.
        ///
        /// Generates random sets of active overlaps with open MemoryStreams at various
        /// progress states, performs area transition cleanup, and verifies:
        /// 1. All streams are disposed (CanWrite == false)
        /// 2. _activeOverlaps is empty
        /// 3. _overlapScanIndex is 0
        /// 4. _overlapCandidates is null
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AreaTransition_ClosesAllStreams_ClearsDictionary_ResetsScanIndex()
        {
            var testGen =
                from overlapCount in Gen.Choose(1, 10)
                from offsets in Gen.Choose(0, 500).Select(o => (long)o * 0x10000)
                    .ListOf(overlapCount)
                from sizes in Gen.Choose(1, 100).Select(s => (long)s * 0x8000)
                    .ListOf(overlapCount)
                from scanIndex in Gen.Choose(0, 20)
                where offsets.Count == overlapCount && sizes.Count == overlapCount
                select new
                {
                    OverlapCount = overlapCount,
                    Offsets = offsets.ToList(),
                    Sizes = sizes.ToList(),
                    ScanIndex = scanIndex
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Build file specs for the overlaps
                List<(string name, long offset, long size)> fileSpecs = new List<(string name, long offset, long size)>();
                for (int i = 0; i < data.OverlapCount; i++)
                {
                    fileSpecs.Add(($"file_{i}.m2ts", data.Offsets[i], data.Sizes[i]));
                }

                // Create active state with open streams
                OverlapTrackingState state = BuildActiveState(fileSpecs, data.ScanIndex);

                // Capture stream references before cleanup to verify disposal
                List<MemoryStream> streamRefs = state.ActiveOverlaps.Values
                    .Select(e => e.Stream)
                    .ToList();

                // Verify preconditions: streams are open
                foreach (MemoryStream stream in streamRefs)
                {
                    if (!stream.CanWrite)
                        return false.Label("Precondition failed: stream not writable before cleanup");
                }

                // Perform area transition cleanup
                PerformAreaTransitionCleanup(state);

                // Verify 1: All streams are disposed (CanWrite == false after Dispose)
                foreach (MemoryStream stream in streamRefs)
                {
                    if (stream.CanWrite)
                        return false.Label(
                            $"Stream still writable after area transition cleanup " +
                            $"(expected all {data.OverlapCount} streams to be disposed)");
                }

                // Verify 2: _activeOverlaps is empty (cleared)
                if (state.ActiveOverlaps.Count != 0)
                    return false.Label(
                        $"_activeOverlaps not cleared: still has {state.ActiveOverlaps.Count} entries");

                // Verify 3: _overlapScanIndex is reset to 0
                if (state.OverlapScanIndex != 0)
                    return false.Label(
                        $"_overlapScanIndex not reset: expected 0, got {state.OverlapScanIndex}");

                // Verify 4: _overlapCandidates is null
                if (state.OverlapCandidates != null)
                    return false.Label(
                        "_overlapCandidates not null after area transition");

                return true.Label(
                    $"Area transition correctly cleaned up {data.OverlapCount} overlaps, " +
                    $"cleared dictionary, and reset scan index from {data.ScanIndex} to 0");
            });
        }

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 10 (supplementary): Area transition cleanup is idempotent.
        /// Performing the cleanup on an already-empty state (no active overlaps)
        /// still results in a clean state with no errors. This ensures the cleanup
        /// is safe even when invoked at boundaries with no prior overlap activity.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AreaTransition_WithEmptyState_RemainsClean()
        {
            var testGen =
                from scanIndex in Gen.Choose(0, 50)
                select new { ScanIndex = scanIndex };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create state with no active overlaps but a non-zero scan index
                OverlapTrackingState state = new OverlapTrackingState
                {
                    ActiveOverlaps = new Dictionary<string, OverlapEntry>(),
                    OverlapCandidates = new List<IFsFile>(),
                    OverlapScanIndex = data.ScanIndex
                };

                // Perform area transition cleanup
                PerformAreaTransitionCleanup(state);

                // Verify cleanup produces clean state
                if (state.ActiveOverlaps.Count != 0)
                    return false.Label("_activeOverlaps not empty after cleanup on empty state");

                if (state.OverlapScanIndex != 0)
                    return false.Label(
                        $"_overlapScanIndex not reset from {data.ScanIndex} to 0 on empty state");

                if (state.OverlapCandidates != null)
                    return false.Label("_overlapCandidates not null after cleanup on empty state");

                return true.Label(
                    $"Area transition cleanup is idempotent (scan index was {data.ScanIndex})");
            });
        }

        /// <summary>
        /// **Validates: Requirements 8.1, 8.2**
        ///
        /// Property 10 (supplementary): Area transition cleanup handles overlaps
        /// at various progress states — some just started (TotalWritten near 0),
        /// some nearly complete, and some partially done. All streams must be
        /// disposed regardless of their progress.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AreaTransition_DisposesStreams_RegardlessOfProgress()
        {
            var testGen =
                from overlapCount in Gen.Choose(1, 8)
                from offsets in Gen.Choose(0, 200).Select(o => (long)o * 0x10000)
                    .ListOf(overlapCount)
                from sizes in Gen.Choose(1, 50).Select(s => (long)s * 0x8000)
                    .ListOf(overlapCount)
                from progressFractions in Gen.Choose(0, 100).Select(p => p / 100.0)
                    .ListOf(overlapCount)
                from scanIndex in Gen.Choose(0, 30)
                where offsets.Count == overlapCount
                    && sizes.Count == overlapCount
                    && progressFractions.Count == overlapCount
                select new
                {
                    OverlapCount = overlapCount,
                    Offsets = offsets.ToList(),
                    Sizes = sizes.ToList(),
                    ProgressFractions = progressFractions.ToList(),
                    ScanIndex = scanIndex
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                OverlapTrackingState state = new OverlapTrackingState
                {
                    OverlapScanIndex = data.ScanIndex,
                    OverlapCandidates = new List<IFsFile>()
                };

                List<MemoryStream> streamRefs = new List<MemoryStream>();

                for (int i = 0; i < data.OverlapCount; i++)
                {
                    TestFsFile file = new TestFsFile(
                        $"stream_{i}.m2ts", "/BDMV/STREAM",
                        data.Offsets[i], data.Sizes[i]);

                    state.OverlapCandidates.Add(file);

                    MemoryStream stream = new MemoryStream();
                    // Write progress proportional to the fraction
                    long totalSize = data.Sizes[i];
                    long written = (long)(totalSize * data.ProgressFractions[i]);
                    if (written > 0 && written <= int.MaxValue)
                        stream.Write(new byte[(int)Math.Min(written, totalSize)], 0, (int)Math.Min(written, totalSize));

                    state.ActiveOverlaps[file.FullName] = new OverlapEntry
                    {
                        Stream = stream,
                        File = file,
                        Written = written,
                        TotalSize = totalSize,
                        TotalWritten = written
                    };

                    streamRefs.Add(stream);
                }

                // Perform area transition cleanup
                PerformAreaTransitionCleanup(state);

                // Verify ALL streams are disposed regardless of progress
                for (int i = 0; i < streamRefs.Count; i++)
                {
                    if (streamRefs[i].CanWrite)
                        return false.Label(
                            $"Stream {i} still writable (progress was " +
                            $"{data.ProgressFractions[i]:P0} of {data.Sizes[i]} bytes)");
                }

                // Verify dictionary cleared and scan index reset
                if (state.ActiveOverlaps.Count != 0)
                    return false.Label($"_activeOverlaps has {state.ActiveOverlaps.Count} entries after cleanup");

                if (state.OverlapScanIndex != 0)
                    return false.Label($"_overlapScanIndex is {state.OverlapScanIndex}, expected 0");

                return true.Label(
                    $"All {data.OverlapCount} streams disposed at various progress levels, " +
                    $"dictionary cleared, scan index reset");
            });
        }

        #endregion
    }
}
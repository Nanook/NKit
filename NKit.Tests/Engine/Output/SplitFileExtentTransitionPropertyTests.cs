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
    /// Property 7: Split File Extent Transition Correctness
    ///
    /// For any Split_File in _overlapCandidates with N extents, when extent K completes
    /// and extent K+1 activates: the output FileStream SHALL remain open, the per-extent
    /// Written counter SHALL reset to zero, the cumulative TotalWritten SHALL preserve its
    /// value, and the File reference SHALL update to the new extent's IFsFile. Completion
    /// (stream close + removal) occurs only when TotalWritten reaches the full logical size.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class SplitFileExtentTransitionPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile for testing split file extent transitions.
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
        /// Simple split parts implementation for split file testing.
        /// </summary>
        private class TestSplitParts : IFsFileParts
        {
            public TestSplitParts(long totalSize)
            {
                Size = totalSize;
                Parts = new List<IFsFilePart>();
            }

            public List<IFsFilePart> Parts { get; }
            public long Size { get; }
            public ulong XxHash => 0;
            public uint Crc => 0;
        }

        /// <summary>
        /// Tracks the state of an active overlap during simulation.
        /// Mirrors the OverlapState struct in ExtractIsoStep.
        /// </summary>
        private class SimulatedOverlapState
        {
            public MemoryStream Stream { get; set; }
            public IFsFile File { get; set; }
            public long Written { get; set; }
            public long TotalSize { get; set; }
            public long TotalWritten { get; set; }
        }

        /// <summary>
        /// Records the state captured at each extent transition for verification.
        /// </summary>
        private class TransitionRecord
        {
            public int ExtentIndex { get; set; }
            public MemoryStream StreamBefore { get; set; }
            public MemoryStream StreamAfter { get; set; }
            public IFsFile FileAfter { get; set; }
            public long WrittenAfter { get; set; }
            public long TotalWrittenAfter { get; set; }
            public long TotalWrittenBefore { get; set; }
        }

        /// <summary>
        /// Simulates ProcessOverlaps Phase 1 extent activation/transition logic for a split file.
        ///
        /// This mirrors the exact Phase 1 code from ExtractIsoStep.ProcessOverlaps:
        /// - First extent: opens stream, initializes state with TotalSize from SplitParts.Size
        /// - Subsequent extents (same path, already in _activeOverlaps):
        ///   * Updates File reference to new extent
        ///   * Resets Written to 0
        ///   * For primary overlaps (in candidateSet): preserves TotalWritten and TotalSize
        ///   * For secondary overlaps (not in candidateSet): resets TotalSize and TotalWritten
        ///
        /// Then simulates Phase 2 writes for the extent's data.
        /// Then simulates Phase 3 deactivation when TotalWritten >= TotalSize.
        /// </summary>
        private static (List<TransitionRecord> Transitions, SimulatedOverlapState FinalState, bool WasCompleted)
            SimulateSplitFileExtentTransitions(
                List<IFsFile> extents,
                HashSet<IFsFile> candidateSet,
                int chunksPerExtent = 1)
        {
            Dictionary<string, SimulatedOverlapState> activeOverlaps = new Dictionary<string, SimulatedOverlapState>(StringComparer.OrdinalIgnoreCase);
            List<TransitionRecord> transitions = new List<TransitionRecord>();
            bool wasCompleted = false;

            // The output path is the same for all extents of a split file
            string fullPath = System.IO.Path.Combine(
                extents[0].Path.Trim('\\', '/'), extents[0].Name);

            int overlapScanIndex = 0;

            foreach (IFsFile extent in extents)
            {
                // Phase 1: Activation — mirrors the real code's while loop
                long discStart = extent.FsOffset;
                long discEnd = extent.FsOffset + extent.FsSize;

                if (!activeOverlaps.ContainsKey(fullPath))
                {
                    // First extent — open new stream
                    bool isPrimary = candidateSet.Contains(extent);
                    MemoryStream stream = new MemoryStream();
                    long totalSize = extent.SplitParts?.Size ?? extent.FsSize;
                    activeOverlaps[fullPath] = new SimulatedOverlapState
                    {
                        Stream = stream,
                        File = extent,
                        Written = 0,
                        TotalSize = totalSize,
                        TotalWritten = 0
                    };
                }
                else
                {
                    // Subsequent extent of same split file — record transition
                    SimulatedOverlapState state = activeOverlaps[fullPath];
                    TransitionRecord record = new TransitionRecord
                    {
                        ExtentIndex = extents.IndexOf(extent),
                        StreamBefore = state.Stream,
                        TotalWrittenBefore = state.TotalWritten
                    };

                    // Update File reference and reset Written
                    state.File = extent;
                    state.Written = 0;

                    // For primary overlaps, keep TotalSize/TotalWritten.
                    // For secondary, reset them.
                    if (!(candidateSet != null && candidateSet.Contains(extent)))
                    {
                        state.TotalSize = extent.FsSize;
                        state.TotalWritten = 0;
                    }

                    record.StreamAfter = state.Stream;
                    record.FileAfter = state.File;
                    record.WrittenAfter = state.Written;
                    record.TotalWrittenAfter = state.TotalWritten;

                    transitions.Add(record);
                }

                overlapScanIndex++;

                // Phase 2: Simulate writing for this extent
                // Write in chunks to mirror real behavior
                long extentRemaining = extent.FsSize;
                long chunkSize = Math.Max(1, extent.FsSize / chunksPerExtent);

                while (extentRemaining > 0)
                {
                    SimulatedOverlapState state = activeOverlaps[fullPath];
                    long writeSize = Math.Min(chunkSize, extentRemaining);

                    // Cap to remaining bytes (TotalSize - TotalWritten)
                    long remaining = state.TotalSize - state.TotalWritten;
                    if (remaining <= 0)
                        break;
                    if (writeSize > remaining)
                        writeSize = remaining;

                    // Simulate section.Read writing data to stream
                    byte[] data = new byte[writeSize];
                    state.Stream.Write(data, 0, (int)writeSize);
                    state.Written += writeSize;
                    state.TotalWritten += writeSize;
                    extentRemaining -= writeSize;
                }

                // Phase 3: Check for completion
                SimulatedOverlapState finalState = activeOverlaps[fullPath];
                if (finalState.TotalWritten >= finalState.TotalSize)
                {
                    finalState.Stream.Close();
                    activeOverlaps.Remove(fullPath);
                    wasCompleted = true;
                    break;
                }
            }

            // Return the final state (or null if completed and removed)
            SimulatedOverlapState resultState = null;
            if (activeOverlaps.ContainsKey(fullPath))
                resultState = activeOverlaps[fullPath];

            return (transitions, resultState, wasCompleted);
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 7a: Stream stays open across extent transitions for primary split files.
        ///
        /// For any split file in _candidateSet with N extents, when extent K completes and
        /// extent K+1 activates, the output stream SHALL be the SAME MemoryStream object
        /// (not closed and reopened).
        ///
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PrimarySplitFile_StreamStaysOpen_AcrossExtentTransitions()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 8)
                from extentSize in Gen.Choose(1, 50).Select(s => (long)s * 0x10000)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x100000)
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSize = extentSize,
                    BaseOffset = baseOffset,
                    TotalSize = (long)extentCount * extentSize
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                // Create N extents at sequential offsets, all same name/path (same output file)
                List<IFsFile> extents = Enumerable.Range(0, data.ExtentCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        "split.ssif", "/stream",
                        data.BaseOffset + ((long)i * data.ExtentSize * 2), // gaps between extents
                        data.ExtentSize,
                        splitParts: splitParts))
                    .ToList();

                // All extents are in candidateSet (primary split file)
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(extents);

                (List<TransitionRecord> transitions, SimulatedOverlapState finalState, bool wasCompleted) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet);

                // Verify: We have N-1 transitions (one per extent after the first)
                if (transitions.Count != data.ExtentCount - 1)
                    return false.Label(
                        $"Expected {data.ExtentCount - 1} transitions, got {transitions.Count}");

                // Verify: Stream reference is the SAME object before and after each transition
                foreach (TransitionRecord t in transitions)
                {
                    if (!ReferenceEquals(t.StreamBefore, t.StreamAfter))
                        return false.Label(
                            $"Extent {t.ExtentIndex}: Stream object changed during transition " +
                            "(should stay open, same reference)");
                }

                return true.Label(
                    $"Split file with {data.ExtentCount} extents: stream stays open across all {transitions.Count} transitions");
            });
        }

        /// <summary>
        /// Property 7b: Per-extent Written counter resets to zero on each transition.
        ///
        /// For any split file, when extent K+1 activates, the Written field SHALL be zero.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SplitFile_WrittenResetsToZero_OnExtentTransition()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 10)
                from extentSize in Gen.Choose(1, 30).Select(s => (long)s * 0x8000)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSize = extentSize,
                    BaseOffset = baseOffset,
                    TotalSize = (long)extentCount * extentSize
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                List<IFsFile> extents = Enumerable.Range(0, data.ExtentCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        "multi.ssif", "/bdmv",
                        data.BaseOffset + ((long)i * data.ExtentSize * 2),
                        data.ExtentSize,
                        splitParts: splitParts))
                    .ToList();

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(extents);

                (List<TransitionRecord> transitions, SimulatedOverlapState _, bool _) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet);

                // Verify: Written is 0 immediately after each transition
                foreach (TransitionRecord t in transitions)
                {
                    if (t.WrittenAfter != 0)
                        return false.Label(
                            $"Extent {t.ExtentIndex}: Written should be 0 after transition, " +
                            $"got {t.WrittenAfter}");
                }

                return true.Label(
                    $"Written resets to 0 on all {transitions.Count} extent transitions");
            });
        }

        /// <summary>
        /// Property 7c: TotalWritten is preserved across extent transitions for primary splits.
        ///
        /// For any primary split file (in candidateSet), when extent K+1 activates,
        /// TotalWritten SHALL equal the cumulative bytes written across all previous extents.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PrimarySplitFile_TotalWrittenPreserved_AcrossTransitions()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 8)
                from extentSize in Gen.Choose(1, 40).Select(s => (long)s * 0x10000)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                from chunksPerExtent in Gen.Choose(1, 4)
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSize = extentSize,
                    BaseOffset = baseOffset,
                    TotalSize = (long)extentCount * extentSize,
                    ChunksPerExtent = chunksPerExtent
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                List<IFsFile> extents = Enumerable.Range(0, data.ExtentCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        "big.ssif", "/stream",
                        data.BaseOffset + ((long)i * data.ExtentSize * 2),
                        data.ExtentSize,
                        splitParts: splitParts))
                    .ToList();

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(extents);

                (List<TransitionRecord> transitions, SimulatedOverlapState _, bool _) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet, data.ChunksPerExtent);

                // Verify: TotalWritten at each transition equals sum of all previous extents' sizes
                for (int i = 0; i < transitions.Count; i++)
                {
                    TransitionRecord t = transitions[i];
                    // Before extent i+1 activates, TotalWritten should be sum of extents 0..i
                    long expectedTotalWritten = extents.Take(i + 1).Sum(e => e.FsSize);

                    if (t.TotalWrittenBefore != expectedTotalWritten)
                        return false.Label(
                            $"Transition {i} (extent {t.ExtentIndex}): " +
                            $"TotalWritten before transition should be {expectedTotalWritten}, " +
                            $"got {t.TotalWrittenBefore}");

                    // After transition for primary overlaps, TotalWritten is preserved (same value)
                    if (t.TotalWrittenAfter != expectedTotalWritten)
                        return false.Label(
                            $"Transition {i} (extent {t.ExtentIndex}): " +
                            $"TotalWritten after transition should be preserved at {expectedTotalWritten}, " +
                            $"got {t.TotalWrittenAfter}");
                }

                return true.Label(
                    $"TotalWritten correctly preserved across {transitions.Count} transitions " +
                    $"(chunks/extent={data.ChunksPerExtent})");
            });
        }

        /// <summary>
        /// Property 7d: File reference updates to new extent's IFsFile on each transition.
        ///
        /// For any split file, when extent K+1 activates, the File field SHALL reference
        /// the new extent's IFsFile (not the previous extent's).
        ///
        /// **Validates: Requirements 4.3**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SplitFile_FileReferenceUpdated_OnExtentTransition()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 8)
                from extentSize in Gen.Choose(1, 20).Select(s => (long)s * 0x10000)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSize = extentSize,
                    BaseOffset = baseOffset,
                    TotalSize = (long)extentCount * extentSize
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                List<IFsFile> extents = Enumerable.Range(0, data.ExtentCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        "movie.ssif", "/stream",
                        data.BaseOffset + ((long)i * data.ExtentSize * 2),
                        data.ExtentSize,
                        splitParts: splitParts))
                    .ToList();

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(extents);

                (List<TransitionRecord> transitions, SimulatedOverlapState _, bool _) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet);

                // Verify: After each transition, File reference points to the correct new extent
                for (int i = 0; i < transitions.Count; i++)
                {
                    TransitionRecord t = transitions[i];
                    IFsFile expectedFile = extents[t.ExtentIndex]; // The newly activated extent

                    if (!ReferenceEquals(t.FileAfter, expectedFile))
                        return false.Label(
                            $"Transition {i}: File reference should point to extent {t.ExtentIndex} " +
                            $"@0x{expectedFile.FsOffset:X}, got file @0x{t.FileAfter.FsOffset:X}");
                }

                return true.Label(
                    $"File reference correctly updated on all {transitions.Count} transitions");
            });
        }

        /// <summary>
        /// Property 7e: Completion (stream close + removal) occurs ONLY when TotalWritten
        /// reaches the full logical size (SplitParts.Size).
        ///
        /// For any split file, the overlap entry SHALL remain active until TotalWritten >= TotalSize.
        /// Only after all extents' data has been written does completion trigger.
        ///
        /// **Validates: Requirements 4.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SplitFile_CompletionOnlyWhenTotalWrittenReachesFullSize()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 8)
                from extentSize in Gen.Choose(1, 30).Select(s => (long)s * 0x10000)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSize = extentSize,
                    BaseOffset = baseOffset,
                    TotalSize = (long)extentCount * extentSize
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                List<IFsFile> extents = Enumerable.Range(0, data.ExtentCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        "complete.ssif", "/stream",
                        data.BaseOffset + ((long)i * data.ExtentSize * 2),
                        data.ExtentSize,
                        splitParts: splitParts))
                    .ToList();

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(extents);

                (List<TransitionRecord> transitions, SimulatedOverlapState finalState, bool wasCompleted) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet);

                // After processing all extents, TotalWritten should equal TotalSize
                // and completion should have occurred
                if (!wasCompleted)
                    return false.Label(
                        $"Split file should be completed after all {data.ExtentCount} extents processed");

                // Verify that the file wasn't completed prematurely (before last extent)
                // Each transition's TotalWrittenBefore should be < TotalSize
                foreach (TransitionRecord t in transitions)
                {
                    if (t.TotalWrittenBefore >= data.TotalSize)
                        return false.Label(
                            $"Transition {t.ExtentIndex}: TotalWritten ({t.TotalWrittenBefore}) " +
                            $"should be < TotalSize ({data.TotalSize}) before final completion");
                }

                return true.Label(
                    $"Split file with {data.ExtentCount} extents completed only after " +
                    $"TotalWritten reached TotalSize=0x{data.TotalSize:X}");
            });
        }

        /// <summary>
        /// Property 7f: Secondary (non-candidate) split files reset TotalWritten on transition.
        ///
        /// For split files NOT in candidateSet, when a subsequent extent activates,
        /// TotalSize and TotalWritten are BOTH reset (different from primary behavior).
        /// This means each extent is treated independently.
        ///
        /// **Validates: Requirements 4.1, 4.2** (contrast with primary behavior)
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SecondarySplitFile_TotalWrittenResets_OnExtentTransition()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 6)
                from extentSize in Gen.Choose(1, 30).Select(s => (long)s * 0x10000)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSize = extentSize,
                    BaseOffset = baseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Secondary files have SplitParts but are NOT in candidateSet
                TestSplitParts splitParts = new TestSplitParts((long)data.ExtentCount * data.ExtentSize);

                List<IFsFile> extents = Enumerable.Range(0, data.ExtentCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        "secondary.dat", "/other",
                        data.BaseOffset + ((long)i * data.ExtentSize * 2),
                        data.ExtentSize,
                        splitParts: splitParts))
                    .ToList();

                // EMPTY candidateSet — these are secondary overlaps
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>();

                (List<TransitionRecord> transitions, SimulatedOverlapState _, bool _) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet);

                // Verify: TotalWritten resets to 0 on each transition for secondary files
                foreach (TransitionRecord t in transitions)
                {
                    if (t.TotalWrittenAfter != 0)
                        return false.Label(
                            $"Extent {t.ExtentIndex}: Secondary split file TotalWritten " +
                            $"should reset to 0, got {t.TotalWrittenAfter}");
                }

                return true.Label(
                    $"Secondary split file TotalWritten correctly resets on all {transitions.Count} transitions");
            });
        }

        /// <summary>
        /// Property 7g: Variable extent sizes — TotalWritten accumulates correctly
        /// even when extents have different sizes.
        ///
        /// For any primary split file with extents of varying sizes, TotalWritten at each
        /// transition shall equal the sum of all completed extents' actual FsSize values.
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.3**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PrimarySplitFile_VariableExtentSizes_TotalWrittenAccumulatesCorrectly()
        {
            var testGen =
                from extentCount in Gen.Choose(2, 6)
                from extentSizes in Gen.Choose(1, 50).Select(s => (long)s * 0x8000).ListOf(extentCount)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                where extentSizes.Count == extentCount
                select new
                {
                    ExtentCount = extentCount,
                    ExtentSizes = extentSizes.ToList(),
                    BaseOffset = baseOffset,
                    TotalSize = extentSizes.Sum()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                long currentOffset = data.BaseOffset;
                List<IFsFile> extents = data.ExtentSizes.Select(size =>
                {
                    IFsFile extent = (IFsFile)new TestFsFile(
                        "variable.ssif", "/stream",
                        currentOffset, size,
                        splitParts: splitParts);
                    currentOffset += size * 2; // gap between extents
                    return extent;
                }).ToList();

                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(extents);

                (List<TransitionRecord> transitions, SimulatedOverlapState _, bool wasCompleted) =
                    SimulateSplitFileExtentTransitions(extents, candidateSet);

                // Verify: TotalWritten accumulates correctly with variable sizes
                for (int i = 0; i < transitions.Count; i++)
                {
                    TransitionRecord t = transitions[i];
                    long expectedTotal = data.ExtentSizes.Take(i + 1).Sum();

                    if (t.TotalWrittenBefore != expectedTotal)
                        return false.Label(
                            $"Transition {i}: Expected TotalWritten={expectedTotal} " +
                            $"(sum of sizes {string.Join("+", data.ExtentSizes.Take(i + 1))}), " +
                            $"got {t.TotalWrittenBefore}");
                }

                // Verify: Completed after all extents
                if (!wasCompleted)
                    return false.Label("Split file should be completed after all extents processed");

                return true.Label(
                    $"Variable-size split file ({data.ExtentCount} extents, sizes=[" +
                    $"{string.Join(", ", data.ExtentSizes.Select(s => $"0x{s:X}"))}]) " +
                    $"accumulates TotalWritten correctly");
            });
        }

        #endregion
    }
}
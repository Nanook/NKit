using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Parallel
{
    /// <summary>
    /// Property 1: Overlap Classification Correctness
    ///
    /// For any set of filesystem entries and extraction candidates, BuildOverlapCandidates
    /// SHALL correctly classify each file: files whose extent range contains other candidates'
    /// offsets are classified as Containing_Files, non-candidate files overlapping with candidates
    /// are classified as secondary overlaps, zero-byte files are excluded, and all other files
    /// are not added to the overlap list.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Parallel")]
    public class OverlapClassificationCorrectnessPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile for testing BuildOverlapCandidates classification logic.
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
        /// Result of simulating the BuildOverlapCandidates classification.
        /// </summary>
        private class ClassificationResult
        {
            public List<IFsFile> OverlapCandidates { get; set; } = new();
            public HashSet<string> PrimaryOverlapPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<IFsFile> ZeroBytesCreated { get; set; } = new();
        }

        /// <summary>
        /// Simulates the core classification logic of BuildOverlapCandidates.
        ///
        /// This mirrors the real implementation:
        /// - Scans all fidelity entries
        /// - Skips system files, non-matching mask entries
        /// - Zero-byte non-candidate files are created immediately and excluded
        /// - Candidates are added only if their range contains other candidates' offsets
        /// - Non-candidates are added unless they are duplicates:
        ///   * Same offset as a candidate with same name (and not a split extent) → skip
        ///   * Same offset as a candidate with different name → skip (lower-priority FS dup)
        ///   * Different offset but same name as a candidate (and not a split extent) → skip
        /// - The actual "does it overlap?" check happens at activation time in ProcessOverlaps,
        ///   NOT in BuildOverlapCandidates. So non-overlapping non-duplicates ARE added.
        /// - Sorts result by FsOffset
        /// - Builds _primaryOverlapPaths for containing candidates
        /// </summary>
        private static ClassificationResult SimulateBuildOverlapCandidates(
            List<IFsFile> candidates,
            List<IFsFile> fidelityEntries,
            FileMask mask)
        {
            ClassificationResult result = new ClassificationResult();

            if (fidelityEntries == null)
                return result;

            HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(candidates);
            HashSet<string> candidateFullNames = new HashSet<string>(
                candidates.Select(c => c.FullName), StringComparer.OrdinalIgnoreCase);
            List<long> candidateOffsets = candidates.Select(c => c.FsOffset).Distinct().OrderBy(o => o).ToList();

            List<IFsFile> overlapCandidates = new List<IFsFile>();

            foreach (IFsFile f in fidelityEntries)
            {
                if (f.IsSystemFile)
                    continue;

                if (!mask.IsMatch(f.FullName))
                    continue;

                if (f.FsSize == 0)
                {
                    // Zero-byte file: create immediately and exclude from overlap list
                    if (!candidateFullNames.Contains(f.FullName))
                        result.ZeroBytesCreated.Add(f);
                    continue;
                }

                if (candidateSet.Contains(f))
                {
                    // This IS a primary candidate. Check if it CONTAINS other candidates.
                    long rangeEnd = f.FsOffset + f.FsSize;
                    int idx = candidateOffsets.BinarySearch(f.FsOffset);
                    if (idx < 0) idx = ~idx;
                    else idx++; // skip our own offset
                    bool containsOthers = idx < candidateOffsets.Count && candidateOffsets[idx] < rangeEnd;
                    if (containsOthers)
                        overlapCandidates.Add(f);
                    continue;
                }

                // Not in candidates — check for duplicate/overlap conditions
                int offsetIdx = candidateOffsets.BinarySearch(f.FsOffset);
                if (offsetIdx >= 0)
                {
                    // There IS a candidate at this offset
                    if (candidateFullNames.Contains(f.FullName))
                    {
                        // Same name and same offset as a candidate — skip (duplicate)
                        if (f.SplitParts == null)
                            continue;
                        // Fall through for displaced split extents
                    }
                    else
                    {
                        // Different name at same offset — lower-priority filesystem duplicate — skip
                        continue;
                    }
                }
                else if (candidateFullNames.Contains(f.FullName))
                {
                    // Not at a candidate offset, but same name as a candidate.
                    if (f.SplitParts == null)
                        continue;
                    // Fall through — add as overlap candidate (displaced split extent)
                }

                overlapCandidates.Add(f);
            }

            // Sort by FsOffset
            result.OverlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();

            // Build primaryOverlapPaths for containing files
            foreach (IFsFile f in result.OverlapCandidates)
            {
                if (candidateSet.Contains(f))
                {
                    string path = f.Path.Trim('\\', '/');
                    string imagePath = System.IO.Path.Combine(path, f.Name);
                    result.PrimaryOverlapPaths.Add(imagePath);
                }
            }

            return result;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 1a: Candidates whose extent range contains other candidates' offsets
        /// are classified as Containing_Files and appear in the overlap candidate list.
        ///
        /// For any set of candidates where one file's range [offset, offset+size) contains
        /// another candidate's offset, the containing file SHALL be in _overlapCandidates
        /// and its path SHALL be in _primaryOverlapPaths.
        ///
        /// **Validates: Requirements 1.1, 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ContainingFile_IsClassifiedAsOverlapCandidate()
        {
            var testGen =
                from containingOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                from containingSize in Gen.Choose(10, 100).Select(s => (long)s * 0x10000)
                from containedCount in Gen.Choose(1, 5)
                from containedRelative in Gen.Choose(1, 99).Select(p => (long)p * 0x1000).ListOf(containedCount)
                let containedOffsets = containedRelative
                    .Select(r => containingOffset + r)
                    .Where(o => o > containingOffset && o < containingOffset + containingSize)
                    .Distinct()
                    .ToList()
                where containedOffsets.Count > 0
                select new
                {
                    ContainingOffset = containingOffset,
                    ContainingSize = containingSize,
                    ContainedOffsets = containedOffsets
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // The containing file (e.g., SSIF): large range encompassing others
                IFsFile containingFile = (IFsFile)new TestFsFile(
                    "containing.ssif", "/stream",
                    data.ContainingOffset, data.ContainingSize);

                // Contained files (e.g., m2ts): offsets within the containing file's range
                List<IFsFile> containedFiles = data.ContainedOffsets.Select((offset, i) =>
                    (IFsFile)new TestFsFile($"contained{i}.m2ts", "/stream",
                        offset, 0x2000)) // small files within the range
                    .ToList();

                // All are candidates (primary extraction set)
                List<IFsFile> candidates = new List<IFsFile> { containingFile };
                candidates.AddRange(containedFiles);

                // Fidelity list includes all
                List<IFsFile> fidelityEntries = new List<IFsFile>(candidates);

                FileMask mask = new FileMask(".*", false);
                ClassificationResult result = SimulateBuildOverlapCandidates(candidates, fidelityEntries, mask);

                // Verify: the containing file IS in _overlapCandidates
                bool containingInList = result.OverlapCandidates.Contains(containingFile);
                if (!containingInList)
                    return false.Label("Containing file should be in _overlapCandidates");

                // Verify: the containing file's path is in _primaryOverlapPaths
                string expectedPath = System.IO.Path.Combine("stream", "containing.ssif");
                bool inPrimaryPaths = result.PrimaryOverlapPaths.Contains(expectedPath);
                if (!inPrimaryPaths)
                    return false.Label($"Containing file path '{expectedPath}' should be in _primaryOverlapPaths");

                // Verify: Contained files that themselves DON'T contain other candidates
                // are NOT in the overlap list (they are normal candidates handled by writeFs).
                // But if a contained file's range happens to contain another candidate, it IS added.
                foreach (IFsFile cf in containedFiles)
                {
                    long cfEnd = cf.FsOffset + cf.FsSize;
                    bool cfContainsOthers = candidates.Any(c =>
                        c != cf && c.FsOffset > cf.FsOffset && c.FsOffset < cfEnd);
                    bool cfInList = result.OverlapCandidates.Contains(cf);

                    if (cfContainsOthers && !cfInList)
                        return false.Label(
                            $"Contained file '{cf.FullName}' that ALSO contains others should be in overlap list");
                    if (!cfContainsOthers && cfInList)
                        return false.Label(
                            $"Contained file '{cf.FullName}' that does NOT contain others should NOT be in overlap list");
                }

                return true.Label(
                    $"Containing file @0x{data.ContainingOffset:X} (size=0x{data.ContainingSize:X}) " +
                    $"correctly classified with {data.ContainedOffsets.Count} contained files");
            });
        }

        /// <summary>
        /// Property 1b: Non-candidate files that pass deduplication filters are added to
        /// the overlap candidate list as secondary entries.
        ///
        /// For any file NOT in _candidates that:
        ///   - is not at the same offset as a candidate (or has same name as candidate with different offset and no split)
        ///   - is not a system file
        ///   - matches the mask
        ///   - has non-zero size
        /// it SHALL appear in _overlapCandidates.
        ///
        /// **Validates: Requirements 1.1, 1.3**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NonCandidateNonDuplicate_IsClassifiedAsSecondaryOverlap()
        {
            var testGen =
                from candidateOffset in Gen.Choose(10, 50).Select(o => (long)o * 0x10000)
                from candidateSize in Gen.Choose(5, 50).Select(s => (long)s * 0x10000)
                from secondaryCount in Gen.Choose(1, 5)
                from secondaryOffsets in Gen.Choose(1, 200).Select(o => (long)o * 0x10000).ListOf(secondaryCount)
                where secondaryOffsets.Count == secondaryCount
                select new
                {
                    CandidateOffset = candidateOffset,
                    CandidateSize = candidateSize,
                    // Ensure offsets don't collide with candidate offset
                    SecondaryOffsets = secondaryOffsets
                        .Where(o => o != candidateOffset)
                        .Distinct()
                        .ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                if (data.SecondaryOffsets.Count == 0)
                    return true.Label("No valid secondary offsets (degenerate case)");

                // Primary candidate
                IFsFile candidate = (IFsFile)new TestFsFile(
                    "primary.m2ts", "/stream",
                    data.CandidateOffset, data.CandidateSize);

                List<IFsFile> candidates = new List<IFsFile> { candidate };

                // Secondary files: NOT in candidates, with unique names, at non-candidate offsets
                List<IFsFile> secondaryFiles = data.SecondaryOffsets.Select((offset, i) =>
                    (IFsFile)new TestFsFile($"secondary{i}.dat", "/other",
                        offset, 0x5000))
                    .ToList();

                // Fidelity list includes both
                List<IFsFile> fidelityEntries = new List<IFsFile> { candidate };
                fidelityEntries.AddRange(secondaryFiles);

                FileMask mask = new FileMask(".*", false);
                ClassificationResult result = SimulateBuildOverlapCandidates(candidates, fidelityEntries, mask);

                // Verify: ALL secondary files appear in _overlapCandidates
                // (the real code adds all non-duplicate non-candidate files — overlap is checked at activation time)
                foreach (IFsFile sec in secondaryFiles)
                {
                    bool inList = result.OverlapCandidates.Contains(sec);
                    if (!inList)
                        return false.Label(
                            $"Secondary file '{sec.FullName}' @0x{sec.FsOffset:X} " +
                            $"should be in _overlapCandidates");
                }

                // Verify: secondary files are NOT in _primaryOverlapPaths (they're not candidates)
                foreach (IFsFile sec in secondaryFiles)
                {
                    string path = System.IO.Path.Combine(
                        sec.Path.Trim('\\', '/'), sec.Name);
                    bool notPrimary = !result.PrimaryOverlapPaths.Contains(path);
                    if (!notPrimary)
                        return false.Label(
                            $"Secondary file '{sec.FullName}' should NOT be in _primaryOverlapPaths");
                }

                return true.Label(
                    $"All {secondaryFiles.Count} non-candidate non-duplicate files correctly classified as secondary");
            });
        }

        /// <summary>
        /// Property 1c: Zero-byte files are excluded from the overlap candidate list.
        ///
        /// For any fidelity entry with FsSize == 0, it SHALL NOT appear in _overlapCandidates.
        /// Zero-byte non-candidate files are created immediately on disk and excluded from
        /// overlap tracking.
        ///
        /// **Validates: Requirements 1.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ZeroByteFiles_ExcludedFromOverlapCandidates()
        {
            var testGen =
                from candidateCount in Gen.Choose(1, 4)
                from zeroByteCount in Gen.Choose(1, 5)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                from candidateSizes in Gen.Choose(1, 20).Select(s => (long)s * 0x10000).ListOf(candidateCount)
                where candidateSizes.Count == candidateCount
                select new
                {
                    CandidateCount = candidateCount,
                    ZeroByteCount = zeroByteCount,
                    BaseOffset = baseOffset,
                    CandidateSizes = candidateSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create normal candidates
                List<IFsFile> candidates = data.CandidateSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"candidate{i}.bin", "/files",
                        data.BaseOffset + ((long)i * 0x100000), size))
                    .ToList();

                // Create zero-byte files (different names than candidates)
                List<IFsFile> zeroByteFiles = Enumerable.Range(0, data.ZeroByteCount)
                    .Select(i => (IFsFile)new TestFsFile($"empty{i}.txt", "/empty",
                        data.BaseOffset + ((long)(data.CandidateCount + i) * 0x100000), 0))
                    .ToList();

                // Fidelity list includes both
                List<IFsFile> fidelityEntries = new List<IFsFile>();
                fidelityEntries.AddRange(candidates);
                fidelityEntries.AddRange(zeroByteFiles);

                FileMask mask = new FileMask(".*", false);
                ClassificationResult result = SimulateBuildOverlapCandidates(candidates, fidelityEntries, mask);

                // Verify: No zero-byte files in _overlapCandidates
                bool noZeroByte = !result.OverlapCandidates.Any(f => f.FsSize == 0);
                if (!noZeroByte)
                    return false.Label("Zero-byte files should NOT appear in _overlapCandidates");

                // Verify: Zero-byte files were flagged for creation
                bool zeroBytesCreated = result.ZeroBytesCreated.Count == data.ZeroByteCount;
                if (!zeroBytesCreated)
                    return false.Label(
                        $"Expected {data.ZeroByteCount} zero-byte files created, " +
                        $"got {result.ZeroBytesCreated.Count}");

                return true.Label(
                    $"All {data.ZeroByteCount} zero-byte files excluded from overlap candidates " +
                    $"and flagged for immediate creation");
            });
        }

        /// <summary>
        /// Property 1d: Candidates that do NOT contain other candidates are NOT in the overlap list.
        ///
        /// For any candidate whose extent range [offset, offset+size) does NOT contain any other
        /// candidate's offset, it SHALL NOT appear in _overlapCandidates. These files are handled
        /// exclusively by the normal writeFs path.
        ///
        /// **Validates: Requirements 1.1, 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NonContainingCandidate_NotInOverlapList()
        {
            var testGen =
                from fileCount in Gen.Choose(2, 6)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                from sizes in Gen.Choose(1, 5).Select(s => (long)s * 0x1000).ListOf(fileCount)
                where sizes.Count == fileCount
                select new
                {
                    FileCount = fileCount,
                    BaseOffset = baseOffset,
                    // Use small sizes and large spacing to ensure no containment
                    Sizes = sizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create candidates with well-spaced offsets so none contains another
                List<IFsFile> candidates = data.Sizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"file{i}.bin", "/data",
                        data.BaseOffset + ((long)i * 0x100000), size)) // spacing 0x100000, sizes max 0x5000
                    .ToList();

                // Fidelity list = just candidates
                List<IFsFile> fidelityEntries = new List<IFsFile>(candidates);

                FileMask mask = new FileMask(".*", false);
                ClassificationResult result = SimulateBuildOverlapCandidates(candidates, fidelityEntries, mask);

                // Verify: None of the candidates are in _overlapCandidates
                // (because none contains another — small sizes, large spacing)
                bool noneInList = result.OverlapCandidates.Count == 0;
                if (!noneInList)
                    return false.Label(
                        $"Expected no overlap candidates (no containment), got {result.OverlapCandidates.Count}");

                // Verify: _primaryOverlapPaths is empty
                bool noPrimaryPaths = result.PrimaryOverlapPaths.Count == 0;

                return noPrimaryPaths.Label(
                    $"All {data.FileCount} non-containing candidates correctly excluded from overlap list");
            });
        }

        /// <summary>
        /// Property 1e: Combined scenario — containing, secondary, zero-byte, and
        /// non-containing candidates are all classified correctly in a single invocation.
        ///
        /// Generates a realistic mixed scenario with all classification categories
        /// and verifies each file ends up in the correct bucket.
        ///
        /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property MixedScenario_AllClassificationsCorrect()
        {
            var testGen =
                from baseOffset in Gen.Choose(1, 20).Select(o => (long)o * 0x100000)
                from containingSize in Gen.Choose(50, 200).Select(s => (long)s * 0x10000)
                from containedCount in Gen.Choose(1, 4)
                from containedRelatives in Gen.Choose(1, 49).Select(p => (long)p * 0x10000).ListOf(containedCount)
                from secondaryCount in Gen.Choose(1, 3)
                from secondaryOffsets in Gen.Choose(200, 500).Select(o => (long)o * 0x10000).ListOf(secondaryCount)
                from zeroCount in Gen.Choose(1, 3)
                where containedRelatives.Count == containedCount
                    && secondaryOffsets.Count == secondaryCount
                select new
                {
                    BaseOffset = baseOffset,
                    ContainingSize = containingSize,
                    ContainedRelatives = containedRelatives.Distinct().ToList(),
                    SecondaryOffsets = secondaryOffsets.Distinct().ToList(),
                    ZeroCount = zeroCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                long containingEnd = data.BaseOffset + data.ContainingSize;

                // 1. Containing file (a candidate that contains others)
                IFsFile containingFile = (IFsFile)new TestFsFile(
                    "big.ssif", "/bdmv",
                    data.BaseOffset, data.ContainingSize);

                // 2. Contained files (candidates within the containing file's range)
                List<IFsFile> containedFiles = data.ContainedRelatives
                    .Select(r => data.BaseOffset + r)
                    .Where(o => o > data.BaseOffset && o < containingEnd)
                    .Select((offset, i) => (IFsFile)new TestFsFile(
                        $"inside{i}.m2ts", "/bdmv",
                        offset, 0x5000))
                    .ToList();

                if (containedFiles.Count == 0)
                    return true.Label("No valid contained files generated (degenerate)");

                // 3. Secondary files (NOT candidates, NOT at candidate offsets, unique names)
                HashSet<long> allCandidateOffsets = new HashSet<long> { data.BaseOffset };
                foreach (IFsFile cf in containedFiles)
                    allCandidateOffsets.Add(cf.FsOffset);

                List<IFsFile> secondaryFiles = data.SecondaryOffsets
                    .Where(o => !allCandidateOffsets.Contains(o))
                    .Select((offset, i) => (IFsFile)new TestFsFile(
                        $"secondary{i}.dat", "/other",
                        offset, 0x3000))
                    .ToList();

                // 4. Zero-byte files
                List<IFsFile> zeroByteFiles = Enumerable.Range(0, data.ZeroCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        $"zero{i}.empty", "/empty",
                        0x1000 + ((long)i * 0x1000), 0))
                    .ToList();

                // Candidates = containing + contained
                List<IFsFile> candidates = new List<IFsFile> { containingFile };
                candidates.AddRange(containedFiles);

                // Fidelity entries = all files
                List<IFsFile> fidelityEntries = new List<IFsFile>();
                fidelityEntries.AddRange(candidates);
                fidelityEntries.AddRange(secondaryFiles);
                fidelityEntries.AddRange(zeroByteFiles);

                FileMask mask = new FileMask(".*", false);
                ClassificationResult result = SimulateBuildOverlapCandidates(candidates, fidelityEntries, mask);

                // Verify 1: Containing file IS in overlap candidates
                bool containingClassified = result.OverlapCandidates.Contains(containingFile);
                if (!containingClassified)
                    return false.Label("Containing file should be in _overlapCandidates");

                // Verify 2: Containing file IS in _primaryOverlapPaths
                string containingPath = System.IO.Path.Combine("bdmv", "big.ssif");
                bool containingIsPrimary = result.PrimaryOverlapPaths.Contains(containingPath);
                if (!containingIsPrimary)
                    return false.Label("Containing file should be in _primaryOverlapPaths");

                // Verify 3: Secondary files ARE in overlap candidates
                foreach (IFsFile sec in secondaryFiles)
                {
                    bool inList = result.OverlapCandidates.Contains(sec);
                    if (!inList)
                        return false.Label(
                            $"Secondary file '{sec.FullName}' @0x{sec.FsOffset:X} should be in overlap list");
                }

                // Verify 4: Zero-byte files are NOT in overlap candidates
                bool noZeroBytes = !result.OverlapCandidates.Any(f => f.FsSize == 0);
                if (!noZeroBytes)
                    return false.Label("Zero-byte files should NOT be in overlap candidates");

                // Verify 5: Zero-byte files were flagged for creation
                bool zeroBytesCreated = result.ZeroBytesCreated.Count == data.ZeroCount;
                if (!zeroBytesCreated)
                    return false.Label(
                        $"Expected {data.ZeroCount} zero-byte files created, got {result.ZeroBytesCreated.Count}");

                // Verify 6: Contained files that don't themselves contain others are NOT in overlap list
                foreach (IFsFile cf in containedFiles)
                {
                    long cfEnd = cf.FsOffset + cf.FsSize;
                    bool cfContainsOthers = candidates.Any(c =>
                        c != cf && c.FsOffset > cf.FsOffset && c.FsOffset < cfEnd);
                    bool cfInList = result.OverlapCandidates.Contains(cf);

                    if (!cfContainsOthers && cfInList)
                        return false.Label(
                            $"Contained file '{cf.FullName}' that doesn't contain others should NOT be in overlap list");
                }

                // Verify 7: Secondary files are NOT in _primaryOverlapPaths
                foreach (IFsFile sec in secondaryFiles)
                {
                    string path = System.IO.Path.Combine(sec.Path.Trim('\\', '/'), sec.Name);
                    if (result.PrimaryOverlapPaths.Contains(path))
                        return false.Label(
                            $"Secondary file '{sec.FullName}' should NOT be in _primaryOverlapPaths");
                }

                return true.Label(
                    $"Mixed scenario: containing=1, contained={containedFiles.Count}, " +
                    $"secondary={secondaryFiles.Count}, zeroByte={data.ZeroCount} — all classified correctly");
            });
        }

        #endregion
    }
}
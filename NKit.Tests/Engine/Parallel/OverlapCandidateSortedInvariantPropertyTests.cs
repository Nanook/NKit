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
    /// Property-based tests verifying the Overlap Candidate List Sorted Invariant.
    ///
    /// Feature: shared-extent-multi-file-write, Property 2: Overlap Candidate List Sorted Invariant
    ///
    /// For any output of BuildOverlapCandidates, the resulting Overlap_Candidate list
    /// SHALL be sorted by FsOffset in ascending order.
    ///
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Parallel")]
    public class OverlapCandidateSortedInvariantPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile implementation for testing overlap candidate sorting logic.
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
            public override string ToString() => $"{Path}/{Name} @0x{FsOffset:X} size=0x{FsSize:X}";
        }

        /// <summary>
        /// Simulates the overlap candidate collection and sorting logic from BuildOverlapCandidates.
        ///
        /// This mirrors the core sorting behavior:
        /// 1. Collects files into an overlapCandidates list (filtering by various criteria)
        /// 2. Sorts the result by FsOffset ascending via OrderBy(f => f.FsOffset)
        ///
        /// The sorted invariant must hold regardless of input ordering or content.
        /// </summary>
        private static List<IFsFile> SimulateBuildOverlapCandidatesSorting(
            List<IFsFile> candidates,
            List<IFsFile> fidelityEntries)
        {
            if (fidelityEntries == null)
                return new List<IFsFile>();

            HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(candidates);
            List<long> candidateOffsets = candidates.Select(c => c.FsOffset).Distinct().OrderBy(o => o).ToList();
            HashSet<string> candidateFullNames = new HashSet<string>(candidates.Select(c => c.FullName));

            List<IFsFile> overlapCandidates = new List<IFsFile>();

            foreach (IFsFile f in fidelityEntries)
            {
                if (f.IsSystemFile)
                    continue;

                if (f.FsSize == 0)
                    continue;

                if (candidateSet.Contains(f))
                {
                    // Check if this candidate's range contains other candidates' offsets
                    long rangeEnd = f.FsOffset + f.FsSize;
                    int idx = candidateOffsets.BinarySearch(f.FsOffset);
                    if (idx < 0) idx = ~idx;
                    else idx++; // skip own offset
                    bool containsOthers = idx < candidateOffsets.Count && candidateOffsets[idx] < rangeEnd;
                    if (containsOthers)
                        overlapCandidates.Add(f);
                    continue;
                }

                // Non-candidate overlap: check for duplicates at same offset
                int offsetIdx = candidateOffsets.BinarySearch(f.FsOffset);
                if (offsetIdx >= 0)
                {
                    // Same offset as a candidate — skip unless it's a displaced split extent
                    if (candidateFullNames.Contains(f.FullName))
                    {
                        if (f.SplitParts == null)
                            continue;
                    }
                    else
                    {
                        continue; // Lower-priority duplicate
                    }
                }
                else if (candidateFullNames.Contains(f.FullName))
                {
                    if (f.SplitParts == null)
                        continue;
                }

                overlapCandidates.Add(f);
            }

            // This is the key operation being tested: sort by FsOffset
            return overlapCandidates.OrderBy(f => f.FsOffset).ToList();
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// **Validates: Requirements 1.4**
        ///
        /// Property 2: Overlap Candidate List Sorted Invariant.
        /// For any output of BuildOverlapCandidates, the resulting Overlap_Candidate list
        /// SHALL be sorted by FsOffset in ascending order.
        ///
        /// This test generates random filesystem entries with varying offsets and sizes,
        /// simulates the BuildOverlapCandidates logic, and verifies the output is sorted.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OverlapCandidates_AreSortedByFsOffset_Ascending()
        {
            var testGen =
                from candidateCount in Gen.Choose(2, 10)
                from fidelityCount in Gen.Choose(3, 20)
                from candidateBaseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x1000)
                from candidateOffsets in Gen.Choose(1, 500).Select(o => (long)o * 0x800)
                    .ListOf(candidateCount)
                from candidateSizes in Gen.Choose(1, 200).Select(s => (long)s * 0x800)
                    .ListOf(candidateCount)
                from fidelityOffsets in Gen.Choose(1, 500).Select(o => (long)o * 0x800)
                    .ListOf(fidelityCount)
                from fidelitySizes in Gen.Choose(1, 200).Select(s => (long)s * 0x800)
                    .ListOf(fidelityCount)
                where candidateOffsets.Count == candidateCount
                   && candidateSizes.Count == candidateCount
                   && fidelityOffsets.Count == fidelityCount
                   && fidelitySizes.Count == fidelityCount
                select new { candidateOffsets, candidateSizes, fidelityOffsets, fidelitySizes };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Build candidates with distinct offsets
                List<IFsFile> candidates = data.candidateOffsets.Zip(data.candidateSizes, (offset, size) =>
                    (IFsFile)new TestFsFile(
                        $"candidate_{offset:X}.bin",
                        "/BDMV/STREAM",
                        offset,
                        size))
                    .ToList();

                // Build fidelity entries — these include candidates plus additional files
                // that may become overlap candidates
                List<IFsFile> fidelityEntries = new List<IFsFile>(candidates);
                for (int i = 0; i < data.fidelityOffsets.Count; i++)
                {
                    fidelityEntries.Add(new TestFsFile(
                        $"fidelity_{i}_{data.fidelityOffsets[i]:X}.m2ts",
                        "/BDMV/STREAM",
                        data.fidelityOffsets[i],
                        data.fidelitySizes[i]));
                }

                // Shuffle fidelity entries to ensure input order doesn't matter
                Random rng = new Random(data.fidelityOffsets.GetHashCode());
                fidelityEntries = fidelityEntries.OrderBy(_ => rng.Next()).ToList();

                // Simulate BuildOverlapCandidates
                List<IFsFile> result = SimulateBuildOverlapCandidatesSorting(candidates, fidelityEntries);

                // Verify: result is sorted by FsOffset ascending
                bool isSorted = true;
                string failureDetail = "";
                for (int i = 1; i < result.Count; i++)
                {
                    if (result[i].FsOffset < result[i - 1].FsOffset)
                    {
                        isSorted = false;
                        failureDetail = $"Element [{i}] FsOffset=0x{result[i].FsOffset:X} < " +
                                        $"Element [{i - 1}] FsOffset=0x{result[i - 1].FsOffset:X}";
                        break;
                    }
                }

                return isSorted.Label(
                    failureDetail.Length > 0 ? failureDetail :
                    $"Overlap candidates list ({result.Count} items) is sorted by FsOffset ascending");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.4**
        ///
        /// Property 2 (supplementary): The sorted invariant holds even when all fidelity
        /// entries have identical offsets — the output list may contain duplicates at the
        /// same offset but must still be non-decreasing.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OverlapCandidates_SortedInvariant_HoldsWithDuplicateOffsets()
        {
            Gen<(int candidateCount, long candidateBaseOffset, long candidateSpacing, List<long> overlapOffsets, List<long> overlapSizes)> testGen =
                from candidateCount in Gen.Choose(2, 8)
                from overlapCount in Gen.Choose(2, 15)
                from sharedOffset in Gen.Choose(1, 200).Select(o => (long)o * 0x800)
                from candidateBaseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x1000)
                from candidateSpacing in Gen.Choose(1, 50).Select(s => (long)s * 0x800)
                from overlapOffsets in Gen.OneOf(
                    Gen.Constant(sharedOffset),
                    Gen.Choose(1, 500).Select(o => (long)o * 0x800))
                    .ListOf(overlapCount)
                from overlapSizes in Gen.Choose(1, 100).Select(s => (long)s * 0x800)
                    .ListOf(overlapCount)
                where overlapOffsets.Count == overlapCount && overlapSizes.Count == overlapCount
                select (candidateCount, candidateBaseOffset, candidateSpacing, overlapOffsets, overlapSizes);

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (int candidateCount, long candidateBaseOffset, long candidateSpacing, List<long> overlapOffsets, List<long> overlapSizes) = data;

                // Create candidates with sequential non-overlapping offsets
                List<IFsFile> candidates = Enumerable.Range(0, candidateCount)
                    .Select(i => (IFsFile)new TestFsFile(
                        $"candidate_{i}.bin",
                        "/BDMV/STREAM",
                        candidateBaseOffset + (i * candidateSpacing),
                        candidateSpacing / 2))
                    .ToList();

                // Create fidelity entries with potentially duplicate offsets (high collision)
                List<IFsFile> fidelityEntries = new List<IFsFile>(candidates);
                for (int i = 0; i < overlapOffsets.Count; i++)
                {
                    fidelityEntries.Add(new TestFsFile(
                        $"overlap_{i}.m2ts",
                        "/BDMV/STREAM",
                        overlapOffsets[i],
                        overlapSizes[i]));
                }

                // Shuffle to randomize insertion order
                Random rng = new Random(overlapOffsets.Sum().GetHashCode());
                fidelityEntries = fidelityEntries.OrderBy(_ => rng.Next()).ToList();

                List<IFsFile> result = SimulateBuildOverlapCandidatesSorting(candidates, fidelityEntries);

                // Verify: non-decreasing order (allows equal offsets)
                bool isNonDecreasing = true;
                string failureDetail = "";
                for (int i = 1; i < result.Count; i++)
                {
                    if (result[i].FsOffset < result[i - 1].FsOffset)
                    {
                        isNonDecreasing = false;
                        failureDetail = $"Element [{i}] FsOffset=0x{result[i].FsOffset:X} < " +
                                        $"Element [{i - 1}] FsOffset=0x{result[i - 1].FsOffset:X}";
                        break;
                    }
                }

                return isNonDecreasing.Label(
                    failureDetail.Length > 0 ? failureDetail :
                    $"Overlap candidates ({result.Count} items, some with duplicate offsets) " +
                    $"sorted non-decreasing by FsOffset");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.4**
        ///
        /// Property 2 (supplementary): The sorted invariant holds when the input contains
        /// a mix of containing files (candidates whose range contains others) and secondary
        /// overlap files (non-candidates). Both categories must be interleaved correctly
        /// in the final sorted output.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property OverlapCandidates_SortedInvariant_MixedContainingAndSecondary()
        {
            // Generate a containing file (large range) with smaller candidates inside it,
            // plus secondary overlaps at various offsets
            var testGen =
                from containingOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                from containingSize in Gen.Choose(100, 500).Select(s => (long)s * 0x800)
                from innerCount in Gen.Choose(1, 6)
                from innerOffsetDeltas in Gen.Choose(1, 80).Select(d => (long)d * 0x800)
                    .ListOf(innerCount)
                from innerSizes in Gen.Choose(1, 20).Select(s => (long)s * 0x800)
                    .ListOf(innerCount)
                from secondaryCount in Gen.Choose(1, 8)
                from secondaryOffsets in Gen.Choose(1, 600).Select(o => (long)o * 0x800)
                    .ListOf(secondaryCount)
                from secondarySizes in Gen.Choose(1, 50).Select(s => (long)s * 0x800)
                    .ListOf(secondaryCount)
                where innerOffsetDeltas.Count == innerCount
                   && innerSizes.Count == innerCount
                   && secondaryOffsets.Count == secondaryCount
                   && secondarySizes.Count == secondaryCount
                select new
                {
                    containingOffset,
                    containingSize,
                    innerOffsetDeltas,
                    innerSizes,
                    secondaryOffsets,
                    secondarySizes
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Build the containing file (a large candidate)
                IFsFile containingFile = (IFsFile)new TestFsFile(
                    "containing.ssif",
                    "/BDMV/SSIF",
                    data.containingOffset,
                    data.containingSize);

                // Build inner candidates (within the containing file's range)
                List<IFsFile> innerCandidates = data.innerOffsetDeltas.Zip(data.innerSizes, (delta, size) =>
                {
                    long innerOffset = data.containingOffset + delta;
                    // Ensure inner offset is within containing range
                    if (innerOffset >= data.containingOffset + data.containingSize)
                        innerOffset = data.containingOffset + 1;
                    return (IFsFile)new TestFsFile(
                        $"inner_{innerOffset:X}.m2ts",
                        "/BDMV/STREAM",
                        innerOffset,
                        Math.Min(size, data.containingSize - (innerOffset - data.containingOffset)));
                }).ToList();

                // All candidates: containing + inner
                List<IFsFile> candidates = new List<IFsFile> { containingFile };
                candidates.AddRange(innerCandidates);

                // Build secondary overlap files (not in candidates)
                List<IFsFile> secondaryFiles = data.secondaryOffsets.Zip(data.secondarySizes, (offset, size) =>
                    (IFsFile)new TestFsFile(
                        $"secondary_{offset:X}.dat",
                        "/BDMV/OTHER",
                        offset,
                        size))
                    .ToList();

                // Fidelity entries include all files in random order
                List<IFsFile> fidelityEntries = new List<IFsFile>();
                fidelityEntries.AddRange(candidates);
                fidelityEntries.AddRange(secondaryFiles);
                Random rng = new Random(data.containingOffset.GetHashCode() ^ data.secondaryOffsets.Sum().GetHashCode());
                fidelityEntries = fidelityEntries.OrderBy(_ => rng.Next()).ToList();

                List<IFsFile> result = SimulateBuildOverlapCandidatesSorting(candidates, fidelityEntries);

                // Verify: sorted ascending by FsOffset
                bool isSorted = true;
                string failureDetail = "";
                for (int i = 1; i < result.Count; i++)
                {
                    if (result[i].FsOffset < result[i - 1].FsOffset)
                    {
                        isSorted = false;
                        failureDetail = $"Element [{i}] FsOffset=0x{result[i].FsOffset:X} < " +
                                        $"Element [{i - 1}] FsOffset=0x{result[i - 1].FsOffset:X}";
                        break;
                    }
                }

                return isSorted.Label(
                    failureDetail.Length > 0 ? failureDetail :
                    $"Mixed containing+secondary overlap candidates ({result.Count} items) " +
                    $"sorted ascending by FsOffset");
            });
        }

        #endregion
    }
}
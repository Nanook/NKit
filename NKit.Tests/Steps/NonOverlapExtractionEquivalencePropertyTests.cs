using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property 6: Non-Overlap Extraction Equivalence
    ///
    /// For any file that is neither a Containing_File nor an Overlap_Candidate, extraction
    /// SHALL proceed identically to the pre-feature code path: writeFs is called normally,
    /// ProcessOverlaps performs no writes for that file, and the output is byte-identical
    /// to what would be produced without the shared-extent feature.
    ///
    /// **Validates: Requirements 3.3, 10.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class NonOverlapExtractionEquivalencePropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile implementation for testing non-overlap extraction equivalence.
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
        /// Represents a simulated SectionItem for the saveFileData pipeline.
        /// </summary>
        private struct SimulatedSectionItem
        {
            /// <summary>The file this section item belongs to.</summary>
            public IFsFile FsFile;
            /// <summary>Offset within the section buffer.</summary>
            public int BufferFsOffset;
            /// <summary>Size of data in this section item.</summary>
            public int FsSize;
            /// <summary>Offset within the file (for multi-chunk files).</summary>
            public long OffsetInItem;
        }

        /// <summary>
        /// Tracks the state of overlaps during simulation.
        /// Mirrors the OverlapState struct from ExtractIsoStep.
        /// </summary>
        private struct OverlapState
        {
            public long Written;
            public long TotalSize;
            public long TotalWritten;
            public IFsFile File;
        }

        /// <summary>
        /// Records what happened to each file during extraction simulation.
        /// </summary>
        private class ExtractionRecord
        {
            /// <summary>Whether writeFs was called for this file.</summary>
            public bool WriteFsCalled { get; set; }
            /// <summary>Total bytes written via writeFs.</summary>
            public long WriteFsBytes { get; set; }
            /// <summary>Total bytes written by ProcessOverlaps to overlap streams for this chunk.</summary>
            public long OverlapBytesWritten { get; set; }
            /// <summary>The section item data (buffer offset + size).</summary>
            public int BufferOffset { get; set; }
            /// <summary>The section item size.</summary>
            public int Size { get; set; }
        }

        /// <summary>
        /// Simulates the saveFileData pipeline with the shared-extent feature active.
        /// Returns extraction records showing how each file was processed.
        ///
        /// This mirrors the real saveFileData logic:
        /// 1. For each section item, check if imagePath is in _primaryOverlapPaths
        /// 2. If NOT in _primaryOverlapPaths → call writeFs
        /// 3. Always call ProcessOverlaps afterward
        ///
        /// ProcessOverlaps behavior:
        /// - If _overlapCandidates is null or empty → return immediately (no-op)
        /// - Otherwise, activate candidates entering range and write intersections
        /// </summary>
        private static List<ExtractionRecord> SimulateExtraction(
            List<SimulatedSectionItem> sectionItems,
            List<IFsFile> overlapCandidates,
            HashSet<string> primaryOverlapPaths,
            HashSet<IFsFile> candidateSet)
        {
            List<ExtractionRecord> records = new List<ExtractionRecord>();
            Dictionary<string, OverlapState> activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            int overlapScanIndex = 0;

            foreach (SimulatedSectionItem si in sectionItems)
            {
                ExtractionRecord record = new ExtractionRecord
                {
                    BufferOffset = si.BufferFsOffset,
                    Size = si.FsSize
                };

                // Determine image path for this file
                string imagePath = System.IO.Path.Combine(
                    si.FsFile.Path.Trim('\\', '/'), si.FsFile.Name);

                // Step 1: writeFs routing — skip if in _primaryOverlapPaths
                if (primaryOverlapPaths == null || !primaryOverlapPaths.Contains(imagePath))
                {
                    record.WriteFsCalled = true;
                    record.WriteFsBytes = si.FsSize;
                }

                // Step 2: ProcessOverlaps
                long overlapBytesForThisItem = SimulateProcessOverlapsForItem(
                    si, overlapCandidates, ref overlapScanIndex, activeOverlaps, candidateSet);
                record.OverlapBytesWritten = overlapBytesForThisItem;

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// Simulates ProcessOverlaps for a single section item.
        /// Returns the total bytes written to overlap streams during this call.
        /// </summary>
        private static long SimulateProcessOverlapsForItem(
            SimulatedSectionItem si,
            List<IFsFile> overlapCandidates,
            ref int overlapScanIndex,
            Dictionary<string, OverlapState> activeOverlaps,
            HashSet<IFsFile> candidateSet)
        {
            // Early return: no overlap candidates
            if (overlapCandidates == null || overlapCandidates.Count == 0)
                return 0;

            long discStart = si.FsFile.FsOffset + si.OffsetInItem;
            long chunkSize = si.FsSize;
            long discEnd = discStart + chunkSize;

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
            long totalOverlapBytes = 0;
            List<string> activeKeys = new List<string>(activeOverlaps.Keys);
            foreach (string key in activeKeys)
            {
                OverlapState state = activeOverlaps[key];
                IFsFile file = state.File;

                // Skip if this section item's file IS the overlap candidate
                // UNLESS it's a primary overlap (in candidateSet)
                if (si.FsFile == file && !(candidateSet != null && candidateSet.Contains(file)))
                    continue;

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

                    state.Written += writeSize;
                    state.TotalWritten += writeSize;
                    activeOverlaps[key] = state;
                    totalOverlapBytes += writeSize;
                }
            }

            // Phase 3: Deactivate completed overlaps
            List<string> completed = activeOverlaps
                .Where(kvp => kvp.Value.TotalWritten >= kvp.Value.TotalSize)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (string path in completed)
                activeOverlaps.Remove(path);

            return totalOverlapBytes;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 6a: When _overlapCandidates is empty, ProcessOverlaps is a no-op.
        ///
        /// For any set of files being extracted, when there are no overlap candidates,
        /// ProcessOverlaps SHALL return immediately without performing any writes.
        /// This means the extraction proceeds identically to the pre-feature code path
        /// (writeFs is called for every file, no overlap writes occur).
        ///
        /// **Validates: Requirements 10.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property EmptyOverlapCandidates_ProcessOverlapsIsNoOp()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 10)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x10000)
                from fileSizes in Gen.Choose(1, 50).Select(s => (long)s * 0x1000).ListOf(fileCount)
                where fileSizes.Count == fileCount
                select new
                {
                    FileCount = fileCount,
                    BaseOffset = baseOffset,
                    FileSizes = fileSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create regular files (not overlap candidates)
                List<IFsFile> files = data.FileSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"regular{i}.bin", "/data",
                        data.BaseOffset + ((long)i * 0x100000), size))
                    .ToList();

                // Create section items for these files
                List<SimulatedSectionItem> sectionItems = files.Select(f => new SimulatedSectionItem
                {
                    FsFile = f,
                    BufferFsOffset = 0,
                    FsSize = (int)f.FsSize,
                    OffsetInItem = 0
                }).ToList();

                // No overlap candidates (empty list) — the graceful degradation case
                List<IFsFile> overlapCandidates = new List<IFsFile>();
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(files);

                List<ExtractionRecord> records = SimulateExtraction(
                    sectionItems, overlapCandidates, primaryOverlapPaths, candidateSet);

                // Verify: writeFs is called for every file
                for (int i = 0; i < records.Count; i++)
                {
                    if (!records[i].WriteFsCalled)
                        return false.Label(
                            $"writeFs should be called for file '{files[i].FullName}' " +
                            $"when no overlap candidates exist");
                }

                // Verify: ProcessOverlaps writes zero bytes (no-op)
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].OverlapBytesWritten != 0)
                        return false.Label(
                            $"ProcessOverlaps should write 0 bytes for file '{files[i].FullName}' " +
                            $"when _overlapCandidates is empty, but wrote {records[i].OverlapBytesWritten}");
                }

                return true.Label(
                    $"All {data.FileCount} files extracted via writeFs with zero overlap writes " +
                    $"(ProcessOverlaps is a no-op)");
            });
        }

        /// <summary>
        /// Property 6b: Files not in _primaryOverlapPaths always have writeFs called.
        ///
        /// For any file whose output path does NOT appear in _primaryOverlapPaths,
        /// writeFs SHALL be called regardless of whether overlap candidates exist.
        /// This ensures non-overlap files are always written through the normal path.
        ///
        /// **Validates: Requirements 3.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property NonPrimaryOverlapFile_AlwaysHasWriteFsCalled()
        {
            var testGen =
                from normalCount in Gen.Choose(1, 8)
                from containingCount in Gen.Choose(1, 3)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x100000)
                from normalSizes in Gen.Choose(1, 20).Select(s => (long)s * 0x1000).ListOf(normalCount)
                from containingSizes in Gen.Choose(50, 200).Select(s => (long)s * 0x10000).ListOf(containingCount)
                where normalSizes.Count == normalCount && containingSizes.Count == containingCount
                select new
                {
                    NormalCount = normalCount,
                    ContainingCount = containingCount,
                    BaseOffset = baseOffset,
                    NormalSizes = normalSizes.ToList(),
                    ContainingSizes = containingSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Normal files: NOT in _primaryOverlapPaths, not overlap candidates
                List<IFsFile> normalFiles = data.NormalSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"normal{i}.m2ts", "/stream",
                        data.BaseOffset + ((long)i * 0x200000), size))
                    .ToList();

                // Containing files: IN _primaryOverlapPaths (written only by ProcessOverlaps)
                List<IFsFile> containingFiles = data.ContainingSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"containing{i}.ssif", "/bdmv",
                        data.BaseOffset + ((long)(data.NormalCount + i) * 0x200000), size))
                    .ToList();

                // Build _primaryOverlapPaths for containing files only
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IFsFile cf in containingFiles)
                {
                    string path = System.IO.Path.Combine(
                        cf.Path.Trim('\\', '/'), cf.Name);
                    primaryOverlapPaths.Add(path);
                }

                // Overlap candidates include containing files (they need ProcessOverlaps)
                List<IFsFile> overlapCandidates = containingFiles.OrderBy(f => f.FsOffset).ToList();
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(normalFiles);
                foreach (IFsFile cf in containingFiles)
                    candidateSet.Add(cf);

                // Create section items for NORMAL files only (simulating their extraction)
                List<SimulatedSectionItem> sectionItems = normalFiles.Select(f => new SimulatedSectionItem
                {
                    FsFile = f,
                    BufferFsOffset = 0,
                    FsSize = (int)f.FsSize,
                    OffsetInItem = 0
                }).ToList();

                List<ExtractionRecord> records = SimulateExtraction(
                    sectionItems, overlapCandidates, primaryOverlapPaths, candidateSet);

                // Verify: writeFs is called for every normal file
                for (int i = 0; i < records.Count; i++)
                {
                    if (!records[i].WriteFsCalled)
                        return false.Label(
                            $"writeFs should be called for normal file '{normalFiles[i].FullName}' " +
                            $"which is NOT in _primaryOverlapPaths");
                }

                // Verify: writeFs bytes match file size (full data written through writeFs)
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].WriteFsBytes != normalFiles[i].FsSize)
                        return false.Label(
                            $"writeFs bytes ({records[i].WriteFsBytes}) should equal " +
                            $"file size ({normalFiles[i].FsSize}) for '{normalFiles[i].FullName}'");
                }

                return true.Label(
                    $"All {data.NormalCount} non-primary files have writeFs called " +
                    $"(even with {data.ContainingCount} containing files present)");
            });
        }

        /// <summary>
        /// Property 6c: ProcessOverlaps performs no writes for files that are neither
        /// containing nor overlap candidates.
        ///
        /// When a section item arrives for a file that does NOT overlap with any active
        /// overlap candidate's range, ProcessOverlaps SHALL write zero bytes to overlap
        /// streams for that section item. The output is byte-identical to what would be
        /// produced without the shared-extent feature.
        ///
        /// **Validates: Requirements 3.3, 10.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property NonOverlapFile_ProcessOverlapsWritesZeroBytes()
        {
            var testGen =
                from normalCount in Gen.Choose(1, 6)
                from overlapOffset in Gen.Choose(500, 1000).Select(o => (long)o * 0x10000)
                from overlapSize in Gen.Choose(10, 50).Select(s => (long)s * 0x10000)
                from normalBaseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                from normalSizes in Gen.Choose(1, 20).Select(s => (long)s * 0x1000).ListOf(normalCount)
                where normalSizes.Count == normalCount
                // Ensure normal files are well outside overlap candidate range
                where normalBaseOffset + (normalCount * 0x100000L) < overlapOffset
                select new
                {
                    NormalCount = normalCount,
                    OverlapOffset = overlapOffset,
                    OverlapSize = overlapSize,
                    NormalBaseOffset = normalBaseOffset,
                    NormalSizes = normalSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Normal files: at offsets well before the overlap candidate range
                List<IFsFile> normalFiles = data.NormalSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"normal{i}.dat", "/files",
                        data.NormalBaseOffset + ((long)i * 0x100000), size))
                    .ToList();

                // One overlap candidate at a far-away offset (doesn't intersect normal files)
                IFsFile overlapFile = (IFsFile)new TestFsFile(
                    "containing.ssif", "/bdmv",
                    data.OverlapOffset, data.OverlapSize);

                List<IFsFile> overlapCandidates = new List<IFsFile> { overlapFile };
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    System.IO.Path.Combine("bdmv", "containing.ssif")
                };
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(normalFiles);
                candidateSet.Add(overlapFile);

                // Create section items for normal files
                List<SimulatedSectionItem> sectionItems = normalFiles.Select(f => new SimulatedSectionItem
                {
                    FsFile = f,
                    BufferFsOffset = 0,
                    FsSize = (int)f.FsSize,
                    OffsetInItem = 0
                }).ToList();

                List<ExtractionRecord> records = SimulateExtraction(
                    sectionItems, overlapCandidates, primaryOverlapPaths, candidateSet);

                // Verify: writeFs is called for all normal files
                for (int i = 0; i < records.Count; i++)
                {
                    if (!records[i].WriteFsCalled)
                        return false.Label(
                            $"writeFs should be called for '{normalFiles[i].FullName}'");
                }

                // Verify: ProcessOverlaps writes zero bytes for normal files
                // (their disc ranges don't intersect the overlap candidate)
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].OverlapBytesWritten != 0)
                        return false.Label(
                            $"ProcessOverlaps should write 0 bytes for non-overlapping file " +
                            $"'{normalFiles[i].FullName}' at offset 0x{normalFiles[i].FsOffset:X}, " +
                            $"but wrote {records[i].OverlapBytesWritten}. " +
                            $"Overlap candidate is at 0x{data.OverlapOffset:X}");
                }

                return true.Label(
                    $"All {data.NormalCount} non-overlapping files: writeFs called, " +
                    $"ProcessOverlaps is a no-op (0 overlap bytes written)");
            });
        }

        /// <summary>
        /// Property 6d: Null _overlapCandidates results in ProcessOverlaps being a no-op.
        ///
        /// When _overlapCandidates is null (graceful degradation when FidelityFileList
        /// is unavailable), ProcessOverlaps SHALL return immediately without any processing,
        /// and extraction proceeds identically to the pre-feature code path.
        ///
        /// **Validates: Requirements 10.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property NullOverlapCandidates_ProcessOverlapsIsNoOp()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 8)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x10000)
                from fileSizes in Gen.Choose(1, 30).Select(s => (long)s * 0x1000).ListOf(fileCount)
                where fileSizes.Count == fileCount
                select new
                {
                    FileCount = fileCount,
                    BaseOffset = baseOffset,
                    FileSizes = fileSizes.ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create files
                List<IFsFile> files = data.FileSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"file{i}.bin", "/data",
                        data.BaseOffset + ((long)i * 0x100000), size))
                    .ToList();

                // Create section items
                List<SimulatedSectionItem> sectionItems = files.Select(f => new SimulatedSectionItem
                {
                    FsFile = f,
                    BufferFsOffset = 0,
                    FsSize = (int)f.FsSize,
                    OffsetInItem = 0
                }).ToList();

                // Null overlap candidates — graceful degradation
                List<IFsFile> overlapCandidates = null;
                HashSet<string> primaryOverlapPaths = null;
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(files);

                List<ExtractionRecord> records = SimulateExtraction(
                    sectionItems, overlapCandidates, primaryOverlapPaths, candidateSet);

                // Verify: writeFs called for every file
                for (int i = 0; i < records.Count; i++)
                {
                    if (!records[i].WriteFsCalled)
                        return false.Label(
                            $"writeFs should be called for '{files[i].FullName}' " +
                            $"when _overlapCandidates is null");
                }

                // Verify: zero overlap bytes written
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].OverlapBytesWritten != 0)
                        return false.Label(
                            $"ProcessOverlaps should write 0 bytes when _overlapCandidates is null, " +
                            $"but wrote {records[i].OverlapBytesWritten} for '{files[i].FullName}'");
                }

                return true.Label(
                    $"All {data.FileCount} files: writeFs called, ProcessOverlaps no-op " +
                    $"with null _overlapCandidates");
            });
        }

        /// <summary>
        /// Property 6e: Mixed scenario — non-overlap files extract identically to pre-feature
        /// path even when overlap candidates exist and active overlaps are present for other files.
        ///
        /// Generates a scenario with both overlap candidates (containing files) and normal
        /// non-overlapping files. Verifies that the normal files are unaffected by the
        /// shared-extent machinery operating on the containing files.
        ///
        /// **Validates: Requirements 3.3, 10.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property MixedScenario_NonOverlapFilesUnaffectedByActiveOverlaps()
        {
            var testGen =
                from normalCount in Gen.Choose(1, 5)
                from normalBaseOffset in Gen.Choose(1, 20).Select(o => (long)o * 0x10000)
                from normalSizes in Gen.Choose(1, 10).Select(s => (long)s * 0x1000).ListOf(normalCount)
                from overlapOffset in Gen.Choose(500, 800).Select(o => (long)o * 0x10000)
                from overlapSize in Gen.Choose(50, 200).Select(s => (long)s * 0x10000)
                from containedCount in Gen.Choose(1, 4)
                from containedOffsets in Gen.Choose(1, 49).Select(p => (long)p * 0x10000)
                    .ListOf(containedCount)
                where normalSizes.Count == normalCount && containedOffsets.Count == containedCount
                // Normal files must be well separated from the overlap region
                where normalBaseOffset + (normalCount * 0x100000L) < overlapOffset
                select new
                {
                    NormalCount = normalCount,
                    NormalBaseOffset = normalBaseOffset,
                    NormalSizes = normalSizes.ToList(),
                    OverlapOffset = overlapOffset,
                    OverlapSize = overlapSize,
                    ContainedOffsets = containedOffsets
                        .Select(r => overlapOffset + r)
                        .Where(o => o > overlapOffset && o < overlapOffset + overlapSize)
                        .Distinct()
                        .ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Normal (non-overlap) files at low offsets
                List<IFsFile> normalFiles = data.NormalSizes.Select((size, i) =>
                    (IFsFile)new TestFsFile($"normal{i}.dat", "/files",
                        data.NormalBaseOffset + ((long)i * 0x100000), size))
                    .ToList();

                // Containing file (overlap candidate) at high offset
                IFsFile containingFile = (IFsFile)new TestFsFile(
                    "containing.ssif", "/bdmv",
                    data.OverlapOffset, data.OverlapSize);

                // Contained files within the containing file's range
                List<IFsFile> containedFiles = data.ContainedOffsets.Select((offset, i) =>
                    (IFsFile)new TestFsFile($"contained{i}.m2ts", "/bdmv",
                        offset, 0x5000))
                    .ToList();

                // Build overlap candidates (just the containing file)
                List<IFsFile> overlapCandidates = new List<IFsFile> { containingFile };
                overlapCandidates.Sort((a, b) => a.FsOffset.CompareTo(b.FsOffset));

                // Primary overlap paths
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    System.IO.Path.Combine("bdmv", "containing.ssif")
                };

                // Candidate set includes normal files and containing file
                HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(normalFiles);
                candidateSet.Add(containingFile);
                foreach (IFsFile cf in containedFiles)
                    candidateSet.Add(cf);

                // Section items: process normal files first, then contained files
                // This simulates the disc-order processing where normal files
                // come before the overlap region
                List<SimulatedSectionItem> sectionItems = new List<SimulatedSectionItem>();

                // Normal files
                foreach (IFsFile f in normalFiles)
                {
                    sectionItems.Add(new SimulatedSectionItem
                    {
                        FsFile = f,
                        BufferFsOffset = 0,
                        FsSize = (int)f.FsSize,
                        OffsetInItem = 0
                    });
                }

                // Contained files (within overlap region)
                foreach (IFsFile f in containedFiles)
                {
                    sectionItems.Add(new SimulatedSectionItem
                    {
                        FsFile = f,
                        BufferFsOffset = 0,
                        FsSize = (int)f.FsSize,
                        OffsetInItem = 0
                    });
                }

                List<ExtractionRecord> records = SimulateExtraction(
                    sectionItems, overlapCandidates, primaryOverlapPaths, candidateSet);

                // Verify: normal files (first N records) have writeFs called
                for (int i = 0; i < normalFiles.Count; i++)
                {
                    if (!records[i].WriteFsCalled)
                        return false.Label(
                            $"writeFs should be called for normal file '{normalFiles[i].FullName}'");
                }

                // Verify: normal files have zero overlap bytes written
                // (their offsets don't intersect the overlap candidate's range)
                for (int i = 0; i < normalFiles.Count; i++)
                {
                    if (records[i].OverlapBytesWritten != 0)
                        return false.Label(
                            $"ProcessOverlaps should write 0 bytes for non-overlapping file " +
                            $"'{normalFiles[i].FullName}' at 0x{normalFiles[i].FsOffset:X}, " +
                            $"but wrote {records[i].OverlapBytesWritten}. " +
                            $"Overlap candidate at 0x{data.OverlapOffset:X}");
                }

                // Verify: writeFs bytes match file size for normal files
                for (int i = 0; i < normalFiles.Count; i++)
                {
                    if (records[i].WriteFsBytes != normalFiles[i].FsSize)
                        return false.Label(
                            $"writeFs bytes ({records[i].WriteFsBytes}) != file size " +
                            $"({normalFiles[i].FsSize}) for '{normalFiles[i].FullName}'");
                }

                return true.Label(
                    $"All {data.NormalCount} non-overlap files extract identically to pre-feature path " +
                    $"even with active overlap candidate at 0x{data.OverlapOffset:X}");
            });
        }

        #endregion
    }
}
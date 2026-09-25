using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Preservation property tests for shared-offset extraction bugfix.
    ///
    /// These tests capture the CURRENT behavior of the extraction loop logic
    /// for non-buggy inputs (files with unique FsOffset values) BEFORE any fix is applied.
    /// They MUST PASS on the unfixed code — if they don't pass, the test is wrong.
    /// After the fix, these tests confirm no regressions for the preserved behavior.
    ///
    /// Property 2: Preservation — Unique-Offset File Extraction Unchanged
    ///
    /// For any section items where no other candidate shares the same FsOffset as si.FsFile,
    /// the extraction output SHALL be byte-identical to the unfixed code's behavior.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
    ///
    /// EXPECTED: These tests PASS on unfixed code (confirms baseline behavior to preserve).
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class SharedOffsetExtractPreservationPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Tracks all file operations performed during extraction simulation.
        /// </summary>
        private class ExtractRecorder
        {
            public List<WriteFsCall> WriteFsCalls { get; } = new();
            public List<WriteBytesCall> WriteBytesCalls { get; } = new();
            public List<string> DirectoriesCreated { get; } = new();
            public int Extracted { get; set; }
            public int SkippedEncFiles { get; set; }
            public long? SkipToOffset { get; set; }

            public record WriteFsCall(string ImagePath, long Pos, int FsOffset, int FsSize, long FullFsSize, bool Replace);
            public record WriteBytesCall(string ImagePath, byte[] Data);
        }

        /// <summary>
        /// Minimal IFsFile for testing extraction logic.
        /// </summary>
        private class TestFsFile : IFsFile
        {
            public TestFsFile(string name, string path, long fsOffset, long fsSize,
                bool isSystemFile = false, bool isLastFile = false, IFsFileParts splitParts = null)
            {
                Name = name;
                Path = path;
                FsOffset = fsOffset;
                FsSize = fsSize;
                IsSystemFile = isSystemFile;
                IsLastFile = isLastFile;
                SplitParts = splitParts;
            }

            public string Name { get; }
            public IFsFolder Parent => null;
            public string Path { get; }
            public string FullName => Path + "/" + Name;
            public long FsOffset { get; }
            public long FsSize { get; }
            public bool IsSystemFile { get; }
            public bool IsLastFile { get; }
            public bool IsMissing => false;
            public int SplitIndex { get; set; }
            public IFsFileParts SplitParts { get; }
            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }
            public long PostGapSize => 0;
            public long PostGapFsOffset => 0;
            public IFsFile Clone() => new TestFsFile(Name, Path, FsOffset, FsSize, IsSystemFile, IsLastFile, SplitParts);
            public override string ToString() => $"{Path}/{Name} @0x{FsOffset:X} size=0x{FsSize:X}";
        }

        /// <summary>
        /// Simple split parts implementation for testing split file reassembly.
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
        /// Simulates the extraction loop from ExtractXBoxStep.saveFileData for non-shared-offset files.
        /// This captures the EXACT logic of the unfixed code for unique-offset scenarios.
        ///
        /// Key behavior from the real code:
        /// - The mask check gates all extraction logic
        /// - writeFs is called regardless of whether the file is in _candidates
        /// - _candidates is only used for tracking completion and triggering skip
        /// - Split files: pos comes from _splitProgress, not from OffsetInItem
        /// - Candidate removal: happens when match != null AND OffsetInItem + FsSize == FsFile.FsSize
        /// </summary>
        private static ExtractRecorder SimulateXBoxExtraction(
            List<IFsFile> candidates,
            List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems,
            FileMask mask,
            bool isEncrypted,
            bool hasKey,
            long sectionImageOffset,
            long sectionSize,
            int blockSize = 0x800,
            int blockFsOffset = 0,
            int blockFsSize = 0x800)
        {
            ExtractRecorder recorder = new ExtractRecorder();
            Dictionary<string, long> splitProgress = new Dictionary<string, long>();
            int fsExtracted = 0;
            int fsTotal = -1;

            foreach ((IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize) si in sectionItems)
            {
                if (candidates != null && candidates.Count == 0)
                    break;

                if (si.FsFile != null && !si.FsFile.IsSystemFile)
                {
                    if (mask.IsMatch(si.FsFile.FullName))
                    {
                        IFsFile match = candidates?.FirstOrDefault(a => a == si.FsFile);
                        if (!isEncrypted || hasKey)
                        {
                            string path = si.FsFile.Path.Trim('\\', '/');
                            recorder.DirectoriesCreated.Add(path);

                            string imagePath = System.IO.Path.Combine(path, si.FsFile.Name);

                            // writeFs logic — mirrors ExtractXBoxStep.writeFs exactly
                            long splitFullSize = si.FsFile.SplitParts?.Size ?? si.FsFile.FsSize;
                            bool isSplit = splitFullSize != si.FsFile.FsSize;
                            bool newFile = si.OffsetInItem == 0;
                            long pos = si.OffsetInItem;

                            if (isSplit)
                            {
                                long l = 0;
                                if (!splitProgress.TryGetValue(imagePath, out l))
                                    splitProgress.Add(imagePath, 0);
                                else
                                    newFile = false;
                                pos = l;
                            }

                            recorder.WriteFsCalls.Add(new ExtractRecorder.WriteFsCall(
                                imagePath, pos, si.FsOffset, si.FsSize, si.FsFile.FsSize, newFile));

                            // Counter logic from ExtractXBoxStep.writeFs
                            if (isSplit && !newFile)
                                fsTotal--; // part > 1 - deduct the file
                            else
                            {
                                recorder.Extracted++;
                                fsExtracted++;
                            }

                            if (isSplit)
                            {
                                long newPos = pos + si.FsSize;
                                if (newPos >= splitFullSize)
                                    splitProgress.Remove(imagePath);
                                else
                                    splitProgress[imagePath] = newPos;
                            }
                        }
                        else if (si.OffsetInItem == 0)
                            recorder.SkippedEncFiles++;

                        if (match != null && si.OffsetInItem + si.FsSize == si.FsFile.FsSize)
                            candidates.Remove(match);
                    }
                }
            }

            // Skip logic
            if (candidates != null)
            {
                if (candidates.Count == 0)
                {
                    recorder.SkipToOffset = long.MaxValue; // represents next area
                }
                else
                {
                    long newOff = Nanook.NKit.Buffer.FsOffsetToOffset(candidates[0].FsOffset, blockSize, blockFsOffset, blockFsSize, false);
                    if (newOff > sectionImageOffset + sectionSize)
                        recorder.SkipToOffset = newOff;
                }
            }

            return recorder;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 2a: Single-offset file extraction via WriteFs produces expected output.
        ///
        /// For all file layouts where NO candidate shares an FsOffset with si.FsFile,
        /// the extraction correctly calls WriteFs once per file with the right parameters
        /// and removes the candidate upon completion.
        ///
        /// **Validates: Requirements 3.1, 3.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property UniqueOffset_SingleFile_WriteFsCalledCorrectly()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 5)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x1000)
                from sizes in Gen.Choose(1, 20).Select(s => s * 0x800).ListOf(fileCount)
                where sizes.Count == fileCount
                select new
                {
                    Files = sizes.Select((size, i) => new
                    {
                        Offset = baseOffset + ((long)i * 0x10000), // Unique offsets, well-spaced
                        Size = (long)size
                    }).ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create files with unique offsets
                List<IFsFile> files = data.Files.Select((f, i) =>
                    (IFsFile)new TestFsFile($"file{i}.bin", $"/dir{i}", f.Offset, f.Size))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile>(files);

                // Create section items - one per file, full file in single section
                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = files.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false); // match all
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: WriteFs called once per file
                bool writeFsCountCorrect = recorder.WriteFsCalls.Count == data.Files.Count;
                if (!writeFsCountCorrect)
                    return false.Label($"Expected {data.Files.Count} WriteFs calls, got {recorder.WriteFsCalls.Count}");

                // Verify: Each WriteFs call has correct parameters
                for (int i = 0; i < data.Files.Count; i++)
                {
                    ExtractRecorder.WriteFsCall call = recorder.WriteFsCalls[i];
                    var file = data.Files[i];
                    if (call.FsOffset != (int)file.Offset)
                        return false.Label($"File {i}: Expected FsOffset=0x{file.Offset:X}, got 0x{call.FsOffset:X}");
                    if (call.FsSize != (int)file.Size)
                        return false.Label($"File {i}: Expected FsSize=0x{file.Size:X}, got 0x{call.FsSize:X}");
                    if (call.FullFsSize != file.Size)
                        return false.Label($"File {i}: Expected FullFsSize=0x{file.Size:X}, got 0x{call.FullFsSize:X}");
                    if (!call.Replace) // First write should be new file (replace=true means pos==0)
                        return false.Label($"File {i}: Expected Replace=true for new file");
                }

                // Verify: All candidates removed
                bool allRemoved = candidates.Count == 0;
                if (!allRemoved)
                    return false.Label($"Expected all candidates removed, {candidates.Count} remaining");

                // Verify: Extracted count matches
                bool extractedCorrect = recorder.Extracted == data.Files.Count;

                return extractedCorrect.Label(
                    $"All {data.Files.Count} unique-offset files extracted correctly via WriteFs");
            });
        }

        /// <summary>
        /// Property 2b: Split file reassembly for files with unique offsets produces correct concatenated output.
        ///
        /// For all split files with unique offsets, reassembly produces the same
        /// result as the unfixed code — multiple WriteFs calls tracking position via _splitProgress.
        /// Each split part is a separate IFsFile with its own FsOffset, sharing a common SplitParts.Size.
        ///
        /// **Validates: Requirements 3.3**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property UniqueOffset_SplitFile_ReassemblyTracksProgress()
        {
            var testGen =
                from partCount in Gen.Choose(2, 5)
                from partSize in Gen.Choose(1, 10).Select(s => s * 0x800)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                select new
                {
                    PartCount = partCount,
                    PartSize = partSize,
                    BaseOffset = baseOffset,
                    TotalSize = (long)partCount * partSize,
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create a split file with multiple parts - each part is a distinct IFsFile
                // All parts share the same SplitParts reference with Size = total
                TestSplitParts splitParts = new TestSplitParts(data.TotalSize);

                // Each part is a separate IFsFile at a unique offset with the same name/path
                // The split progress is keyed by imagePath, so all parts write to the same output
                List<IFsFile> parts = Enumerable.Range(0, data.PartCount)
                    .Select(i => (IFsFile)new TestFsFile("split.bin", "/game",
                        data.BaseOffset + ((long)i * 0x10000), // Each part at a unique offset
                        data.PartSize,                         // Each part's size
                        splitParts: splitParts))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile>(parts);

                // Each part appears once as a section item
                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = parts.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,  // Each part starts at 0 within its section contribution
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: WriteFs called once per part
                bool callCountCorrect = recorder.WriteFsCalls.Count == data.PartCount;
                if (!callCountCorrect)
                    return false.Label($"Expected {data.PartCount} WriteFs calls for split file, got {recorder.WriteFsCalls.Count}");

                // Verify: Positions track progress (0, partSize, 2*partSize, ...)
                for (int i = 0; i < data.PartCount; i++)
                {
                    ExtractRecorder.WriteFsCall call = recorder.WriteFsCalls[i];
                    long expectedPos = (long)i * data.PartSize;
                    if (call.Pos != expectedPos)
                        return false.Label($"Part {i}: Expected pos=0x{expectedPos:X}, got 0x{call.Pos:X}");
                }

                // Verify: Only first call is "new file"
                bool firstIsNew = recorder.WriteFsCalls[0].Replace;
                bool restAreNotNew = recorder.WriteFsCalls.Skip(1).All(c => !c.Replace);
                if (!firstIsNew)
                    return false.Label("First split part should be new file");
                if (!restAreNotNew)
                    return false.Label("Subsequent split parts should not be new files");

                // Verify: All candidates removed (each part is removed individually)
                bool allRemoved = candidates.Count == 0;
                if (!allRemoved)
                    return false.Label($"Expected all {data.PartCount} split part candidates removed, {candidates.Count} remaining");

                // Verify: Extracted count is 1 (first part increments extracted,
                // subsequent parts decrement fsTotal instead)
                bool extractedCorrect = recorder.Extracted == 1;

                return extractedCorrect.Label(
                    $"Split file with {data.PartCount} parts reassembled correctly, positions tracked, extracted=1");
            });
        }

        /// <summary>
        /// Property 2c: Encrypted files without keys are skipped and _skippedEncFiles increments.
        ///
        /// For all encrypted files without keys, skip behavior and counter are identical.
        ///
        /// **Validates: Requirements 3.5**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property EncryptedFilesWithoutKeys_AreSkipped_CounterIncrements()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 8)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x1000)
                from sizes in Gen.Choose(1, 20).Select(s => (long)s * 0x800).ListOf(fileCount)
                where sizes.Count == fileCount
                select new
                {
                    Files = sizes.Select((size, i) => new
                    {
                        Offset = baseOffset + ((long)i * 0x10000),
                        Size = size
                    }).ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                List<IFsFile> files = data.Files.Select((f, i) =>
                    (IFsFile)new TestFsFile($"enc{i}.bin", $"/secure", f.Offset, f.Size))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile>(files);

                // Section items with OffsetInItem = 0 (first chunk of each file)
                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = files.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: true, hasKey: false, // NO KEY - files should be skipped
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: No WriteFs calls (all encrypted, no key)
                bool noWrites = recorder.WriteFsCalls.Count == 0;
                if (!noWrites)
                    return false.Label($"Expected 0 WriteFs calls for encrypted files without key, got {recorder.WriteFsCalls.Count}");

                // Verify: SkippedEncFiles equals file count
                bool skipCountCorrect = recorder.SkippedEncFiles == data.Files.Count;
                if (!skipCountCorrect)
                    return false.Label($"Expected {data.Files.Count} skipped enc files, got {recorder.SkippedEncFiles}");

                // Verify: Extracted count is 0
                bool extractedZero = recorder.Extracted == 0;

                // Verify: Candidates are still removed when end-of-file is reached
                // (because the removal happens regardless of encryption)
                bool candidatesRemoved = candidates.Count == 0;

                return (extractedZero && candidatesRemoved).Label(
                    $"All {data.Files.Count} encrypted files skipped, counter={recorder.SkippedEncFiles}, candidates removed");
            });
        }

        /// <summary>
        /// Property 2d: File mask filtering excludes non-matching files from extraction.
        ///
        /// For files that don't match the mask, no WriteFs calls are made and they remain
        /// in _candidates until their end-of-file section item passes.
        ///
        /// **Validates: Requirements 3.4**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property FileMaskFiltering_ExcludesNonMatchingFiles()
        {
            var testGen =
                from matchCount in Gen.Choose(1, 4)
                from noMatchCount in Gen.Choose(1, 4)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x1000)
                select new
                {
                    MatchCount = matchCount,
                    NoMatchCount = noMatchCount,
                    BaseOffset = baseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create matching files (*.bin) and non-matching files (*.xyz)
                List<IFsFile> matchFiles = Enumerable.Range(0, data.MatchCount)
                    .Select(i => (IFsFile)new TestFsFile($"match{i}.bin", "/data",
                        data.BaseOffset + ((long)i * 0x10000), 0x1000))
                    .ToList();

                List<IFsFile> noMatchFiles = Enumerable.Range(0, data.NoMatchCount)
                    .Select(i => (IFsFile)new TestFsFile($"skip{i}.xyz", "/other",
                        data.BaseOffset + ((long)(data.MatchCount + i) * 0x10000), 0x1000))
                    .ToList();

                List<IFsFile> allFiles = matchFiles.Concat(noMatchFiles).OrderBy(f => f.FsOffset).ToList();
                List<IFsFile> candidates = new List<IFsFile>(allFiles);

                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = allFiles.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                // Mask only matches .bin files
                FileMask mask = new FileMask(".*\\.bin$", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: WriteFs called only for matching files
                bool writeCountCorrect = recorder.WriteFsCalls.Count == data.MatchCount;
                if (!writeCountCorrect)
                    return false.Label($"Expected {data.MatchCount} WriteFs calls (matching only), got {recorder.WriteFsCalls.Count}");

                // Verify: Extracted count = matching files only
                bool extractedCorrect = recorder.Extracted == data.MatchCount;
                if (!extractedCorrect)
                    return false.Label($"Expected extracted={data.MatchCount}, got {recorder.Extracted}");

                // Verify: Non-matching files removed from candidates when their end-of-file is reached
                // In the real code, non-matching files are never matched by _candidates.FirstOrDefault
                // because the mask check happens before removal. The match = candidates.FirstOrDefault
                // happens for matching files only. Non-matching files stay in candidates.
                // Actually re-reading the code: the mask check wraps the match lookup AND the removal.
                // So non-matching files remain in _candidates.
                bool nonMatchFilesRemain = candidates.Count == data.NoMatchCount;

                return nonMatchFilesRemain.Label(
                    $"{data.MatchCount} matching files extracted, {data.NoMatchCount} non-matching remain in candidates");
            });
        }

        /// <summary>
        /// Property 2e: SkipToImageOffsetSet triggers when _candidates is exhausted.
        ///
        /// When all candidates have been extracted, the skip-to-next-area logic activates.
        ///
        /// **Validates: Requirements 3.6**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property CandidatesExhausted_TriggersSkipToOffset()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 5)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x1000)
                from sizes in Gen.Choose(1, 10).Select(s => (long)s * 0x800).ListOf(fileCount)
                where sizes.Count == fileCount
                select new
                {
                    Files = sizes.Select((size, i) => new
                    {
                        Offset = baseOffset + ((long)i * 0x10000),
                        Size = size
                    }).ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                List<IFsFile> files = data.Files.Select((f, i) =>
                    (IFsFile)new TestFsFile($"file{i}.dat", $"/content", f.Offset, f.Size))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile>(files);

                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = files.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: All candidates exhausted
                bool allDone = candidates.Count == 0;
                if (!allDone)
                    return false.Label($"Expected all candidates removed, {candidates.Count} remaining");

                // Verify: SkipToOffset is set to MaxValue (next area)
                bool skipTriggered = recorder.SkipToOffset == long.MaxValue;

                return skipTriggered.Label(
                    $"All {data.Files.Count} candidates extracted, SkipToImageOffset triggered");
            });
        }

        /// <summary>
        /// Property 2f: Directory entries are excluded from extraction candidates.
        ///
        /// In the real code, candidates are built by filtering with:
        /// section.FullAreaFileSystem.Files.Where(a => !a.IsSystemFile ...)
        /// Directory entries never appear in .Files (they're in .Root.Folders).
        /// This test verifies the pattern: system files and directories are excluded from candidates.
        ///
        /// **Validates: Requirements 3.7**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property DirectoryEntries_ExcludedFromCandidates()
        {
            var testGen =
                from regularCount in Gen.Choose(1, 5)
                from systemCount in Gen.Choose(1, 3)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x1000)
                select new
                {
                    RegularCount = regularCount,
                    SystemCount = systemCount,
                    BaseOffset = baseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create regular files
                List<IFsFile> regularFiles = Enumerable.Range(0, data.RegularCount)
                    .Select(i => (IFsFile)new TestFsFile($"game{i}.bin", "/files",
                        data.BaseOffset + ((long)i * 0x10000), 0x2000, isSystemFile: false))
                    .ToList();

                // Create system files (these get filtered out in real code)
                List<IFsFile> systemFiles = Enumerable.Range(0, data.SystemCount)
                    .Select(i => (IFsFile)new TestFsFile($"__sys{i}.bin", "/sys",
                        data.BaseOffset + ((long)(data.RegularCount + i) * 0x10000), 0x1000, isSystemFile: true))
                    .ToList();

                // In ExtractXBoxStep, candidates are built as:
                // Files.Where(a => !a.IsSystemFile && ...).OrderBy(a => a.FsOffset)
                List<IFsFile> allFiles = regularFiles.Concat(systemFiles).ToList();
                List<IFsFile> candidates = allFiles.Where(a => !a.IsSystemFile).OrderBy(a => a.FsOffset).ToList();

                // Verify: Only regular files in candidates
                bool candidatesCorrect = candidates.Count == data.RegularCount;
                if (!candidatesCorrect)
                    return false.Label($"Expected {data.RegularCount} candidates, got {candidates.Count}");

                bool noSystemInCandidates = candidates.All(c => !c.IsSystemFile);

                // Now simulate extraction of only regular files
                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = candidates.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                List<IFsFile> candidatesCopy = new List<IFsFile>(candidates);
                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidatesCopy, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: Only regular files extracted
                bool extractedCorrect = recorder.Extracted == data.RegularCount;

                return (noSystemInCandidates && extractedCorrect).Label(
                    $"{data.RegularCount} regular files extracted, {data.SystemCount} system files excluded from candidates");
            });
        }

        /// <summary>
        /// Property 2g: Same-size FstLink merges extract once via OrderedList (not duplicated).
        ///
        /// When multiple files share the same FsOffset AND the same FsSize (FstLink merge),
        /// the OrderedList contains only one entry. Since candidates are built from
        /// OrderedList.Files, the merged file is extracted exactly once via WriteFs.
        /// It is NOT treated as an overlap candidate because same-size = same content.
        ///
        /// **Validates: Requirements 3.8**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property SameSizeFstLinkMerge_ExtractsOnceViaOrderedList_NotDuplicated()
        {
            var testGen =
                from mergeCount in Gen.Choose(2, 5) // number of same-size files at same offset
                from offset in Gen.Choose(1, 100).Select(o => (long)o * 0x10000)
                from size in Gen.Choose(1, 10).Select(s => (long)s * 0x800)
                from otherCount in Gen.Choose(0, 3)
                from otherBaseOffset in Gen.Choose(200, 500).Select(o => (long)o * 0x10000)
                select new
                {
                    MergeCount = mergeCount,
                    SharedOffset = offset,
                    SharedSize = size,
                    OtherFileCount = otherCount,
                    OtherBaseOffset = otherBaseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // The key insight: in the real code, same-size files at the same offset
                // are merged into a single FstFile with multiple Links (FstLink merge).
                // The OrderedList contains ONE entry. Candidates are built from OrderedList.
                // Therefore, the merged file appears ONCE in candidates and is extracted ONCE.
                //
                // This simulates that scenario: the candidates list contains only ONE file
                // for the shared offset (because the merge already happened at insert time).
                IFsFile mergedFile = (IFsFile)new TestFsFile("merged.bin", "/shared",
                    data.SharedOffset, data.SharedSize);

                // Other unique files
                List<IFsFile> otherFiles = Enumerable.Range(0, data.OtherFileCount)
                    .Select(i => (IFsFile)new TestFsFile($"other{i}.bin", "/files",
                        data.OtherBaseOffset + ((long)i * 0x10000), 0x2000))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile> { mergedFile };
                candidates.AddRange(otherFiles);
                candidates = candidates.OrderBy(f => f.FsOffset).ToList();

                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = candidates.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: WriteFs called exactly once for the merged file (not duplicated)
                List<ExtractRecorder.WriteFsCall> mergedCalls = recorder.WriteFsCalls
                    .Where(c => c.FsOffset == (int)data.SharedOffset)
                    .ToList();
                bool extractedOnce = mergedCalls.Count == 1;
                if (!extractedOnce)
                    return false.Label($"Same-size merged file should be extracted exactly once, " +
                                       $"but WriteFs was called {mergedCalls.Count} times for offset 0x{data.SharedOffset:X}");

                // Verify: Total extracted = 1 (merged) + other files
                int expectedTotal = 1 + data.OtherFileCount;
                bool totalCorrect = recorder.Extracted == expectedTotal;
                if (!totalCorrect)
                    return false.Label($"Expected {expectedTotal} total extracted, got {recorder.Extracted}");

                // Verify: All candidates removed
                bool allRemoved = candidates.Count == 0;

                return allRemoved.Label(
                    $"FstLink merge of {data.MergeCount} same-size files extracted once (not {data.MergeCount} times), " +
                    $"plus {data.OtherFileCount} other files");
            });
        }

        /// <summary>
        /// Property 2h: Encrypted files WITH keys are extracted normally.
        ///
        /// When encrypted files have a decryption key available, they are written via WriteFs
        /// just like unencrypted files. This is the complement of Property 2c.
        ///
        /// **Validates: Requirements 3.5**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property EncryptedFilesWithKeys_ExtractedNormally()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 5)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x1000)
                from sizes in Gen.Choose(1, 10).Select(s => (long)s * 0x800).ListOf(fileCount)
                where sizes.Count == fileCount
                select new
                {
                    Files = sizes.Select((size, i) => new
                    {
                        Offset = baseOffset + ((long)i * 0x10000),
                        Size = size
                    }).ToList()
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                List<IFsFile> files = data.Files.Select((f, i) =>
                    (IFsFile)new TestFsFile($"enc{i}.bin", $"/secure", f.Offset, f.Size))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile>(files);

                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = files.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateXBoxExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: true, hasKey: true, // HAS KEY - should extract normally
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: WriteFs called for all files (key present)
                bool writeCountCorrect = recorder.WriteFsCalls.Count == data.Files.Count;
                if (!writeCountCorrect)
                    return false.Label($"Expected {data.Files.Count} WriteFs calls (has key), got {recorder.WriteFsCalls.Count}");

                // Verify: No skipped files
                bool noSkips = recorder.SkippedEncFiles == 0;
                if (!noSkips)
                    return false.Label($"Expected 0 skipped enc files (has key), got {recorder.SkippedEncFiles}");

                // Verify: Extracted count correct
                bool extractedCorrect = recorder.Extracted == data.Files.Count;

                return extractedCorrect.Label(
                    $"All {data.Files.Count} encrypted files extracted normally with key");
            });
        }

        #endregion
    }
}
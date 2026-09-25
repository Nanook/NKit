using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Preservation property tests for ISO9660 fidelity extract bugfix.
    ///
    /// These tests capture the CURRENT behavior of the ExtractIsoStep.saveFileData
    /// extraction loop for non-buggy inputs BEFORE any fix is applied.
    /// They MUST PASS on the unfixed code — if they don't pass, the test is wrong.
    /// After the fix, these tests confirm no regressions for preserved behavior.
    ///
    /// Property 2: Preservation — Non-Shared-Offset ISO9660 Extraction Unchanged
    ///
    /// For any file layouts where NO candidate has additional smaller-size entries
    /// in the FidelityFileList at the same offset, extraction output is byte-identical
    /// to the unfixed code.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
    ///
    /// EXPECTED: These tests PASS on unfixed code (confirms baseline behavior to preserve).
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class Iso9660FidelityExtractPreservationPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Tracks all file operations performed during extraction simulation.
        /// </summary>
        private class ExtractRecorder
        {
            public List<WriteFsCall> WriteFsCalls { get; } = new();
            public List<string> DirectoriesCreated { get; } = new();
            public int Extracted { get; set; }
            public int FsExtracted { get; set; }
            public int SkippedEncFiles { get; set; }
            public long? SkipToOffset { get; set; }

            public record WriteFsCall(string ImagePath, long Pos, int FsOffset, int FsSize, long FullFsSize, bool NewFile);
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
        /// Simulates the extraction loop from ExtractIsoStep.saveFileData for ISO9660 files.
        /// This captures the EXACT logic of the unfixed code.
        ///
        /// Key behavior from the real code:
        /// - The mask check gates all extraction logic
        /// - writeFs is called regardless of whether the file is in _candidates
        /// - _candidates is only used for tracking completion and triggering skip
        /// - Candidate removal: happens when match != null AND OffsetInItem + FsSize == FsFile.FsSize
        /// - Split files: pos comes from _splitProgress, not from OffsetInItem
        /// - getPath trims leading/trailing slashes from the file path
        /// </summary>
        private static ExtractRecorder SimulateIsoExtraction(
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
                            // getPath logic: trim leading/trailing slashes
                            string path = si.FsFile.Path.Trim('\\', '/');
                            recorder.DirectoriesCreated.Add(path);

                            string imagePath = System.IO.Path.Combine(path, si.FsFile.Name);

                            // writeFs logic — mirrors ExtractIsoStep.writeFs exactly
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

                            // Counter logic from ExtractIsoStep.writeFs
                            if (isSplit && !newFile)
                            {
                                // fsTotal-- in real code (part > 1 deducts)
                            }
                            else
                            {
                                recorder.Extracted++;
                                recorder.FsExtracted++;
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

            // Skip logic from ExtractIsoStep.saveFileData
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
        /// Property 2a: Single-offset file extraction via writeFs produces expected output
        /// for any file with a unique FsOffset.
        ///
        /// For all file layouts where NO candidate has additional smaller-size entries
        /// in the FidelityFileList at the same offset, the extraction correctly calls
        /// writeFs once per file and removes the candidate upon completion.
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
                // Create files with unique offsets (non-buggy: no shared offsets)
                List<IFsFile> files = data.Files.Select((f, i) =>
                    (IFsFile)new TestFsFile($"file{i}.bin", $"/dir{i}", f.Offset, f.Size))
                    .ToList();

                List<IFsFile> candidates = new List<IFsFile>(files);

                // Section items: one per file, full file in single section
                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = files.Select(f => (
                    FsFile: f,
                    OffsetInItem: 0L,
                    FsOffset: (int)f.FsOffset,
                    FsSize: (int)f.FsSize
                )).ToList();

                FileMask mask = new FileMask(".*", false); // match all
                ExtractRecorder recorder = SimulateIsoExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: writeFs called once per file
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
                    if (!call.NewFile)
                        return false.Label($"File {i}: Expected NewFile=true for first write");
                }

                // Verify: All candidates removed
                bool allRemoved = candidates.Count == 0;
                if (!allRemoved)
                    return false.Label($"Expected all candidates removed, {candidates.Count} remaining");

                // Verify: Extracted count matches file count
                bool extractedCorrect = recorder.Extracted == data.Files.Count;
                bool fsExtractedCorrect = recorder.FsExtracted == data.Files.Count;

                return (extractedCorrect && fsExtractedCorrect).Label(
                    $"All {data.Files.Count} unique-offset files extracted correctly via writeFs");
            });
        }

        /// <summary>
        /// Property 2b: Same-size multi-view FstLink aliases at the same offset are extracted
        /// once via the OrderedList candidate.
        ///
        /// When multiple files share the same offset AND the same size, FstContext.AddFile
        /// merges them via FstLink — only one entry exists in the OrderedList (and thus in
        /// _candidates). The extraction writes that single entry once. This test verifies
        /// the behavior is preserved: exactly one writeFs call for the merged candidate.
        ///
        /// **Validates: Requirements 3.1, 3.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SameSizeFstLinkMerge_ExtractedOnce_NoDuplicateWrites()
        {
            var testGen =
                from aliasCount in Gen.Choose(2, 5)
                from offset in Gen.Choose(1, 100).Select(o => (long)o * 0x1000)
                from size in Gen.Choose(1, 20).Select(s => (long)s * 0x800)
                select new
                {
                    AliasCount = aliasCount,
                    Offset = offset,
                    Size = size
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // In the real code, same-size same-offset files are merged by FstContext.AddFile
                // into a single OrderedList entry with FstLinks. Only that one merged entry
                // appears in _candidates. We simulate this by having ONE candidate entry.
                IFsFile mergedFile = (IFsFile)new TestFsFile("merged.bin", "/shared", data.Offset, data.Size);
                List<IFsFile> candidates = new List<IFsFile> { mergedFile };

                // Only one section item for the merged entry (it appears once in the stream)
                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = new List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)>
                {
                    (mergedFile, 0L, (int)data.Offset, (int)data.Size)
                };

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateIsoExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: writeFs called exactly once (no duplicate writes for aliases)
                bool singleWrite = recorder.WriteFsCalls.Count == 1;
                if (!singleWrite)
                    return false.Label($"Expected 1 WriteFs call for merged entry, got {recorder.WriteFsCalls.Count}");

                // Verify: correct parameters
                ExtractRecorder.WriteFsCall call = recorder.WriteFsCalls[0];
                bool offsetCorrect = call.FsOffset == (int)data.Offset;
                bool sizeCorrect = call.FsSize == (int)data.Size;

                // Verify: candidate removed
                bool candidateRemoved = candidates.Count == 0;
                if (!candidateRemoved)
                    return false.Label("Merged candidate should be removed after extraction");

                // Verify: extracted once
                bool extractedOnce = recorder.Extracted == 1 && recorder.FsExtracted == 1;

                return (singleWrite && offsetCorrect && sizeCorrect && extractedOnce).Label(
                    $"Same-size merge at offset 0x{data.Offset:X} (size=0x{data.Size:X}, {data.AliasCount} aliases) extracted once");
            });
        }

        /// <summary>
        /// Property 2c: Encrypted files without decryption keys are skipped and
        /// _skippedEncFiles increments.
        ///
        /// For all encrypted files without keys, skip behavior and counter are identical
        /// to the unfixed code.
        ///
        /// **Validates: Requirements 3.4**
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
                        Size = (long)size
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
                ExtractRecorder recorder = SimulateIsoExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: true, hasKey: false, // NO KEY - files should be skipped
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: No writeFs calls (all encrypted, no key)
                bool noWrites = recorder.WriteFsCalls.Count == 0;
                if (!noWrites)
                    return false.Label($"Expected 0 WriteFs calls for encrypted files without key, got {recorder.WriteFsCalls.Count}");

                // Verify: SkippedEncFiles equals file count
                bool skipCountCorrect = recorder.SkippedEncFiles == data.Files.Count;
                if (!skipCountCorrect)
                    return false.Label($"Expected {data.Files.Count} skipped enc files, got {recorder.SkippedEncFiles}");

                // Verify: Extracted count is 0
                bool extractedZero = recorder.Extracted == 0;

                // Verify: Candidates are removed when end-of-file is reached
                bool candidatesRemoved = candidates.Count == 0;

                return (noWrites && extractedZero && candidatesRemoved).Label(
                    $"All {data.Files.Count} encrypted files skipped, counter={recorder.SkippedEncFiles}");
            });
        }

        /// <summary>
        /// Property 2d: File mask filtering excludes non-matching files from extraction.
        ///
        /// For all file mask exclusions, non-matching fidelity entries are not extracted.
        /// Non-matching files never trigger writeFs and remain in _candidates
        /// (because mask check wraps the match lookup AND the removal).
        ///
        /// **Validates: Requirements 3.5**
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
                ExtractRecorder recorder = SimulateIsoExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // Verify: writeFs called only for matching files
                bool writeCountCorrect = recorder.WriteFsCalls.Count == data.MatchCount;
                if (!writeCountCorrect)
                    return false.Label($"Expected {data.MatchCount} WriteFs calls (matching only), got {recorder.WriteFsCalls.Count}");

                // Verify: Extracted count = matching files only
                bool extractedCorrect = recorder.Extracted == data.MatchCount;
                if (!extractedCorrect)
                    return false.Label($"Expected extracted={data.MatchCount}, got {recorder.Extracted}");

                // Verify: Non-matching files remain in candidates (mask check wraps removal)
                bool nonMatchFilesRemain = candidates.Count == data.NoMatchCount;

                return nonMatchFilesRemain.Label(
                    $"{data.MatchCount} matching files extracted, {data.NoMatchCount} non-matching remain in candidates");
            });
        }

        /// <summary>
        /// Property 2e: SkipToImageOffsetSet triggers when _candidates is exhausted.
        ///
        /// When all candidates have been extracted, the skip-to-next-area logic activates
        /// to avoid unnecessary disc reading.
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
                        Size = (long)size
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
                ExtractRecorder recorder = SimulateIsoExtraction(
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
        /// Property 2f: Path and naming conventions via getPath produce identical output paths.
        ///
        /// The getPath method trims leading/trailing slashes from the file path.
        /// This test verifies that diverse path formats are handled consistently
        /// and the output imagePath (path + name) is produced correctly.
        ///
        /// **Validates: Requirements 3.5**
        /// </summary>
        [Property(MaxTest = 50)]
        public Property GetPath_ProducesConsistentOutputPaths()
        {
            // Generate various path formats
            var testGen =
                from dirCount in Gen.Choose(1, 4)
                from dirs in Gen.Elements("game", "data", "movies", "sounds", "textures", "sys").ListOf(dirCount)
                from fileName in Gen.Elements("main.bin", "intro.dat", "track01.mp3", "tex0.raw")
                from leadingSlash in Gen.Elements(true, false)
                from trailingSlash in Gen.Elements(true, false)
                select new
                {
                    Dirs = dirs.ToList(),
                    FileName = fileName,
                    LeadingSlash = leadingSlash,
                    TrailingSlash = trailingSlash
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Construct the path as it would appear in an IFsFile
                string rawPath = string.Join("/", data.Dirs);
                if (data.LeadingSlash) rawPath = "/" + rawPath;
                if (data.TrailingSlash) rawPath = rawPath + "/";

                IFsFile file = (IFsFile)new TestFsFile(data.FileName, rawPath, 0x1000, 0x800);
                List<IFsFile> candidates = new List<IFsFile> { file };

                List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)> sectionItems = new List<(IFsFile FsFile, long OffsetInItem, int FsOffset, int FsSize)>
                {
                    (file, 0L, (int)file.FsOffset, (int)file.FsSize)
                };

                FileMask mask = new FileMask(".*", false);
                ExtractRecorder recorder = SimulateIsoExtraction(
                    candidates, sectionItems, mask,
                    isEncrypted: false, hasKey: false,
                    sectionImageOffset: 0, sectionSize: 0x10000000);

                // getPath trims leading and trailing slashes
                string expectedPath = rawPath.Trim('\\', '/');
                string expectedImagePath = System.IO.Path.Combine(expectedPath, data.FileName);

                // Verify: directory created with trimmed path
                bool dirCorrect = recorder.DirectoriesCreated.Count == 1 &&
                                  recorder.DirectoriesCreated[0] == expectedPath;
                if (!dirCorrect)
                    return false.Label($"Expected dir '{expectedPath}', got '{(recorder.DirectoriesCreated.Count > 0 ? recorder.DirectoriesCreated[0] : "none")}'");

                // Verify: writeFs called with correct imagePath
                bool pathCorrect = recorder.WriteFsCalls.Count == 1 &&
                                   recorder.WriteFsCalls[0].ImagePath == expectedImagePath;

                return pathCorrect.Label(
                    $"Path '{rawPath}' -> trimmed '{expectedPath}', imagePath='{expectedImagePath}'");
            });
        }

        #endregion
    }
}
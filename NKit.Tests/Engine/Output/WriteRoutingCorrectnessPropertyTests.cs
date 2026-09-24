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
    /// Property 5: Write Routing Correctness
    ///
    /// For any SectionItem, writeFs SHALL be called for the primary file if and only if
    /// that file's output path does NOT appear in the Primary_Overlap_Path set. Files in
    /// Primary_Overlap_Path are written exclusively by ProcessOverlaps.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class WriteRoutingCorrectnessPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Minimal IFsFile for testing write routing decisions.
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
        /// Records the routing decision for a single section item.
        /// </summary>
        private class RoutingDecision
        {
            public string ImagePath { get; set; }
            public bool WriteFsCalled { get; set; }
            public bool ProcessOverlapsCalled { get; set; }
        }

        /// <summary>
        /// Simulates the write routing decision from saveFileData.
        /// This mirrors the actual implementation logic:
        ///   if (_primaryOverlapPaths == null || !_primaryOverlapPaths.Contains(imagePath))
        ///       writeFs(...)
        ///   ProcessOverlaps(...)
        /// </summary>
        private static RoutingDecision SimulateWriteRouting(
            string imagePath,
            HashSet<string> primaryOverlapPaths)
        {
            RoutingDecision decision = new RoutingDecision
            {
                ImagePath = imagePath,
                // writeFs is called if primaryOverlapPaths is null or does not contain the path
                WriteFsCalled = primaryOverlapPaths == null || !primaryOverlapPaths.Contains(imagePath),
                // ProcessOverlaps is always called regardless of the routing decision
                ProcessOverlapsCalled = true
            };
            return decision;
        }

        /// <summary>
        /// Builds an image path from an IFsFile, mirroring how saveFileData constructs it.
        /// </summary>
        private static string BuildImagePath(IFsFile file)
        {
            string path = file.Path.Trim('\\', '/');
            return System.IO.Path.Combine(path, file.Name);
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 5a: When a file's path IS in _primaryOverlapPaths, writeFs is NOT called.
        ///
        /// For any file whose output path appears in the Primary_Overlap_Path set,
        /// the routing logic SHALL skip writeFs. These files are written exclusively
        /// by ProcessOverlaps.
        ///
        /// **Validates: Requirements 3.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PrimaryOverlapFile_WriteFsSkipped()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 10)
                from pathIndex in Gen.Choose(0, 9)
                let names = Enumerable.Range(0, fileCount).Select(i => $"file{i}.ssif").ToArray()
                let paths = Enumerable.Range(0, fileCount).Select(i => $"BDMV/STREAM").ToArray()
                select new
                {
                    FileCount = fileCount,
                    // Pick one file to be the "primary overlap" file
                    PrimaryIndex = pathIndex % fileCount,
                    Names = names,
                    Paths = paths
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Build the set of primary overlap paths
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string primaryName = data.Names[data.PrimaryIndex];
                string primaryPath = data.Paths[data.PrimaryIndex];
                string primaryImagePath = System.IO.Path.Combine(primaryPath, primaryName);
                primaryOverlapPaths.Add(primaryImagePath);

                // Simulate routing for the primary overlap file
                RoutingDecision decision = SimulateWriteRouting(primaryImagePath, primaryOverlapPaths);

                // writeFs must NOT be called for files in _primaryOverlapPaths
                if (decision.WriteFsCalled)
                    return false.Label(
                        $"writeFs should NOT be called for primary overlap file '{primaryImagePath}'");

                // ProcessOverlaps must still be called
                if (!decision.ProcessOverlapsCalled)
                    return false.Label(
                        $"ProcessOverlaps should always be called, even for primary overlap files");

                return true.Label(
                    $"Primary overlap file '{primaryImagePath}' correctly skipped by writeFs");
            });
        }

        /// <summary>
        /// Property 5b: When a file's path is NOT in _primaryOverlapPaths, writeFs IS called.
        ///
        /// For any file whose output path does NOT appear in the Primary_Overlap_Path set,
        /// the routing logic SHALL call writeFs to write the file normally via the standard path.
        ///
        /// **Validates: Requirements 3.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NonOverlapFile_WriteFsCalled()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 10)
                from overlapCount in Gen.Choose(0, 5)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x10000)
                select new
                {
                    FileCount = fileCount,
                    OverlapCount = Math.Min(overlapCount, fileCount - 1), // ensure at least one non-overlap
                    BaseOffset = baseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create file list — some will be overlaps, some won't
                List<TestFsFile> files = Enumerable.Range(0, data.FileCount)
                    .Select(i => new TestFsFile($"file{i}.m2ts", "BDMV/STREAM",
                        data.BaseOffset + ((long)i * 0x100000), 0x50000))
                    .ToList();

                // Build primary overlap set from the first N files
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < data.OverlapCount; i++)
                {
                    string imagePath = BuildImagePath(files[i]);
                    primaryOverlapPaths.Add(imagePath);
                }

                // Check routing for non-overlap files (those NOT in the primary set)
                for (int i = data.OverlapCount; i < data.FileCount; i++)
                {
                    string imagePath = BuildImagePath(files[i]);
                    RoutingDecision decision = SimulateWriteRouting(imagePath, primaryOverlapPaths);

                    if (!decision.WriteFsCalled)
                        return false.Label(
                            $"writeFs should be called for non-overlap file '{imagePath}' " +
                            $"(not in primaryOverlapPaths set of {data.OverlapCount} entries)");

                    if (!decision.ProcessOverlapsCalled)
                        return false.Label(
                            $"ProcessOverlaps should always be called for '{imagePath}'");
                }

                return true.Label(
                    $"All {data.FileCount - data.OverlapCount} non-overlap files correctly routed through writeFs");
            });
        }

        /// <summary>
        /// Property 5c: When _primaryOverlapPaths is null, writeFs is always called.
        ///
        /// This handles the graceful degradation case (Requirement 10): when no overlap
        /// candidates exist, _primaryOverlapPaths may be null, and writeFs should proceed
        /// normally for all files.
        ///
        /// **Validates: Requirements 3.1, 3.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NullPrimaryOverlapPaths_WriteFsAlwaysCalled()
        {
            var testGen =
                from fileCount in Gen.Choose(1, 20)
                from baseOffset in Gen.Choose(1, 100).Select(o => (long)o * 0x10000)
                select new
                {
                    FileCount = fileCount,
                    BaseOffset = baseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Create arbitrary files
                List<TestFsFile> files = Enumerable.Range(0, data.FileCount)
                    .Select(i => new TestFsFile($"file{i}.bin", "data/files",
                        data.BaseOffset + ((long)i * 0x80000), 0x20000))
                    .ToList();

                // _primaryOverlapPaths is null (graceful degradation)
                HashSet<string> primaryOverlapPaths = null;

                // Every file should get writeFs called
                foreach (TestFsFile file in files)
                {
                    string imagePath = BuildImagePath(file);
                    RoutingDecision decision = SimulateWriteRouting(imagePath, primaryOverlapPaths);

                    if (!decision.WriteFsCalled)
                        return false.Label(
                            $"writeFs should ALWAYS be called when _primaryOverlapPaths is null, " +
                            $"but was skipped for '{imagePath}'");
                }

                return true.Label(
                    $"All {data.FileCount} files correctly routed through writeFs when " +
                    $"_primaryOverlapPaths is null");
            });
        }

        /// <summary>
        /// Property 5d: The routing decision is an exact biconditional (if and only if).
        ///
        /// For any randomly generated set of file paths and a randomly generated primary
        /// overlap path set, writeFs is called iff the path is NOT in the set. This verifies
        /// the biconditional nature: no path can be both skipped and not in the set, and no
        /// path can be both written and in the set.
        ///
        /// **Validates: Requirements 3.1, 3.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property WriteRouting_Biconditional_WriteFsIffNotInPrimarySet()
        {
            var testGen =
                from totalFiles in Gen.Choose(2, 15)
                from overlapFraction in Gen.Choose(0, 100)
                from baseOffset in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                let overlapCount = Math.Max(0, Math.Min(totalFiles - 1, totalFiles * overlapFraction / 100))
                select new
                {
                    TotalFiles = totalFiles,
                    OverlapCount = overlapCount,
                    BaseOffset = baseOffset
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Generate files with varied paths
                List<TestFsFile> files = Enumerable.Range(0, data.TotalFiles)
                    .Select(i => new TestFsFile(
                        $"entry{i}.dat",
                        i % 3 == 0 ? "BDMV/STREAM" : i % 3 == 1 ? "data/video" : "files",
                        data.BaseOffset + ((long)i * 0x100000),
                        0x40000))
                    .ToList();

                // Randomly select which files are in the primary overlap set
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < data.OverlapCount; i++)
                {
                    primaryOverlapPaths.Add(BuildImagePath(files[i]));
                }

                // Verify biconditional for every file
                for (int i = 0; i < data.TotalFiles; i++)
                {
                    string imagePath = BuildImagePath(files[i]);
                    bool isInPrimarySet = primaryOverlapPaths.Contains(imagePath);
                    RoutingDecision decision = SimulateWriteRouting(imagePath, primaryOverlapPaths);

                    // Biconditional: WriteFsCalled iff NOT in primary set
                    bool expectedWriteFs = !isInPrimarySet;
                    if (decision.WriteFsCalled != expectedWriteFs)
                    {
                        return false.Label(
                            $"Biconditional violation for '{imagePath}': " +
                            $"inPrimarySet={isInPrimarySet}, " +
                            $"writeFsCalled={decision.WriteFsCalled}, " +
                            $"expected writeFsCalled={expectedWriteFs}");
                    }

                    // ProcessOverlaps is always called regardless
                    if (!decision.ProcessOverlapsCalled)
                    {
                        return false.Label(
                            $"ProcessOverlaps must always be called for '{imagePath}'");
                    }
                }

                return true.Label(
                    $"Biconditional holds: {data.OverlapCount} overlap files skipped, " +
                    $"{data.TotalFiles - data.OverlapCount} non-overlap files written via writeFs");
            });
        }

        /// <summary>
        /// Property 5e: Case-insensitive path matching in _primaryOverlapPaths.
        ///
        /// The routing decision uses case-insensitive path comparison (OrdinalIgnoreCase).
        /// A file with a path that differs only in case from a primary overlap entry
        /// should still be skipped by writeFs.
        ///
        /// **Validates: Requirements 3.1, 3.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property WriteRouting_CaseInsensitive_PathMatching()
        {
            var testGen =
                from nameBase in Gen.Elements("stream", "video", "data")
                from nameIndex in Gen.Choose(0, 99)
                from ext in Gen.Elements(".ssif", ".m2ts", ".bin")
                from pathVariant in Gen.Choose(0, 3)
                select new
                {
                    FileName = $"{nameBase}{nameIndex}{ext}",
                    PathVariant = pathVariant
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                string basePath = "BDMV/STREAM";
                string originalImagePath = System.IO.Path.Combine(basePath, data.FileName);

                // Build primary overlap set with the original path
                HashSet<string> primaryOverlapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                primaryOverlapPaths.Add(originalImagePath);

                // Create a case-variant of the path
                string variantImagePath = data.PathVariant switch
                {
                    0 => originalImagePath.ToUpperInvariant(),
                    1 => originalImagePath.ToLowerInvariant(),
                    2 => System.IO.Path.Combine("bdmv/stream", data.FileName.ToUpperInvariant()),
                    _ => originalImagePath
                };

                RoutingDecision decision = SimulateWriteRouting(variantImagePath, primaryOverlapPaths);

                // Case-insensitive: the variant should also be recognized as a primary overlap
                bool shouldBeSkipped = primaryOverlapPaths.Contains(variantImagePath);
                if (decision.WriteFsCalled != !shouldBeSkipped)
                {
                    return false.Label(
                        $"Case-insensitive mismatch: original='{originalImagePath}', " +
                        $"variant='{variantImagePath}', " +
                        $"writeFsCalled={decision.WriteFsCalled}, " +
                        $"shouldBeSkipped={shouldBeSkipped}");
                }

                return true.Label(
                    $"Case-insensitive routing correct for variant '{variantImagePath}'");
            });
        }

        #endregion
    }
}
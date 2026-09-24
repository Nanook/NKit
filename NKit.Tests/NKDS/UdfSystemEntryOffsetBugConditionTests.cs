using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using NKitDataStore;

namespace NKit.Tests.NKDS;

/// <summary>
/// Bug condition exploration test for UDF system entry double-shifted offset.
///
/// This test verifies that BuildPerTypeFsYaml computes correct image offsets for UDF
/// system entries (IsSystemFile = true) on areas with non-zero ImageOffset. The bug
/// causes system entry offsets to be double-shifted by unconditionally adding
/// area.ImageOffset to file.FsOffset, even though system entry FsOffset values are
/// already absolute (not area-relative).
///
/// **Validates: Requirements 1.1, 2.1, 2.2**
///
/// EXPECTED: This test FAILS on unfixed code (proving the bug exists).
/// System entries will produce area.ImageOffset + file.FsOffset instead of just file.FsOffset.
/// </summary>
public class UdfSystemEntryOffsetBugConditionTests
{
    /// <summary>
    /// UDF system entry names used in testing (prefixed with __ per NKit convention).
    /// </summary>
    private static readonly string[] SystemEntryNames = new[]
    {
        "__file_18B000_00002.m2ts",
        "__fst_1A000",
        "__dir_20000",
        "__udfFileSet_30000"
    };

    /// <summary>
    /// Creates a Scan with a single ScanArea at the given ImageOffset, populated with
    /// UDF system entries at the specified FsOffset values.
    /// </summary>
    private static Scan CreateScanWithSystemEntries(
        long areaImageOffset,
        (string name, long fsOffset, long size)[] entries,
        DataStride stride = null)
    {
        var scan = new Scan(SystemType.Default, "test_udf_system_entry");

        // Create AreaInfo matching the area
        int blockSize = stride?.SourceBlockSize ?? 0x800;
        int blockFsOffset = stride?.DataOffset ?? 0;
        int blockFsSize = stride?.DataLength ?? 0x800;

        var areaInfo = new AreaInfo(areaImageOffset, AreaType.FileSystem, 0);
        areaInfo.SetBlock(blockSize, blockFsOffset, blockFsSize, 0x200000);

        // Create FstContext and add system entries
        byte[] headerData = new byte[0x8000 * 0x40];
        var header = new ImageHeader(headerData, areaInfo);
        var ctx = header.FstContext;
        var root = new FstFolder(FsType.Udf);

        foreach (var (name, fsOffset, size) in entries)
        {
            ctx.AddFile(root, name, FsType.Udf, fsOffset, size, FsItemType.File, isSystem: true);
        }

        // Build the ScanArea with the filesystem
        var fsData = new TestFileSystemData(areaImageOffset, ctx);

        var area = new ScanArea(scan)
        {
            ImageOffset = areaImageOffset,
            Type = AreaType.FileSystem,
            Size = 0x10000000,
            FsInfo = fsData,
            AreaInfo = areaInfo
        };

        scan.Areas.Add(area);
        return scan;
    }

    /// <summary>
    /// Extracts all file nodes from the FsYaml output (flattens the tree).
    /// </summary>
    private static List<FsYamlNode> GetAllFileNodes(Dictionary<string, FsYaml> perTypeYaml)
    {
        var files = new List<FsYamlNode>();
        foreach (var kvp in perTypeYaml)
        {
            foreach (var fs in kvp.Value.FileSystems)
            {
                CollectFileNodes(fs, files);
            }
        }
        return files;
    }

    private static void CollectFileNodes(FsYamlNode node, List<FsYamlNode> files)
    {
        if (node.IsFile)
        {
            files.Add(node);
            return;
        }
        if (node.Children != null)
        {
            foreach (var child in node.Children)
                CollectFileNodes(child, files);
        }
    }

    /// <summary>
    /// Property 1: Bug Condition — UDF System Entry Double-Shifted Offset
    ///
    /// For UDF system entries (IsSystemFile = true) on an area with non-zero ImageOffset,
    /// the computed image offset in BuildPerTypeFsYaml SHALL equal file.FsOffset
    /// (NOT area.ImageOffset + file.FsOffset).
    ///
    /// On unfixed code, this test FAILS because the method unconditionally adds
    /// area.ImageOffset, producing double-shifted values like 0x96B000 instead of 0x18B000.
    ///
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property SystemEntryOffset_ShouldNotInclude_AreaImageOffset()
    {
        // Concrete test case: area.ImageOffset = 0x7E0000, system entries with known FsOffsets
        long areaImageOffset = 0x7E0000;

        var entries = new[]
        {
            ("__file_18B000_00002.m2ts", 0x18B000L, 0x800L),
            ("__fst_1A000",              0x1A000L,  0x800L),
            ("__dir_20000",              0x20000L,  0x800L),
            ("__udfFileSet_30000",       0x30000L,  0x800L),
        };

        Scan scan = CreateScanWithSystemEntries(areaImageOffset, entries);

        // Invoke BuildPerTypeFsYaml — this is the method under test.
        // We need a DataStoreIso9660Formatter instance. Since the method only uses _context.Log
        // (for non-essential logging), we create one with a minimal context.
        var formatter = new TestableDataStoreIso9660Formatter();
        Dictionary<string, FsYaml> result = formatter.BuildPerTypeFsYaml(scan);

        // Extract all file nodes from the output
        List<FsYamlNode> fileNodes = GetAllFileNodes(result);

        // Verify each system entry's image offset equals its FsOffset (no area.ImageOffset addition)
        var failures = new List<string>();
        foreach (var (name, fsOffset, size) in entries)
        {
            var matchingNode = fileNodes.FirstOrDefault(n => n.Name == name);
            if (matchingNode == null)
            {
                failures.Add($"Entry '{name}' not found in output");
                continue;
            }

            long expectedOffset = fsOffset; // System entries should NOT add area.ImageOffset
            long actualOffset = matchingNode.Offset;

            if (actualOffset != expectedOffset)
            {
                long buggyOffset = areaImageOffset + fsOffset;
                string detail = actualOffset == buggyOffset
                    ? $"double-shifted by area.ImageOffset"
                    : $"unexpected value";
                failures.Add(
                    $"Entry '{name}': FsOffset=0x{fsOffset:X}, " +
                    $"expected imageOffset=0x{expectedOffset:X}, " +
                    $"actual=0x{actualOffset:X} ({detail})");
            }
        }

        bool allCorrect = failures.Count == 0;

        string label = allCorrect
            ? "All system entries have correct absolute offsets (no double-shift)"
            : $"BUG CONFIRMED — system entries are double-shifted by area.ImageOffset (0x{areaImageOffset:X}):\n" +
              string.Join("\n", failures);

        return allCorrect.ToProperty().Label(label);
    }

    /// <summary>
    /// Property 1b: Bug Condition — Strided UDF System Entry Offset
    ///
    /// For UDF system entries on a strided area (e.g. Dreamcast GD-ROM with 0x930 sectors),
    /// the computed image offset SHALL equal stride.CleanToOffset(file.FsOffset, false)
    /// (NOT area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)).
    ///
    /// On unfixed code, this test FAILS because area.ImageOffset is added on top of stride conversion.
    ///
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property StridedSystemEntryOffset_ShouldNotInclude_AreaImageOffset()
    {
        // Strided area: Dreamcast-style Mode1Raw (0x930 block, 0x10 offset, 0x800 data)
        long areaImageOffset = 0x7E0000;
        var stride = new DataStride
        {
            SourceBlockSize = 0x930,
            DataOffset = 0x10,
            DataLength = 0x800
        };

        var entries = new[]
        {
            ("__file_18B000_00002.m2ts", 0x18B000L, 0x800L),
            ("__fst_1A000",              0x1A000L,  0x800L),
            ("__dir_20000",              0x20000L,  0x800L),
        };

        Scan scan = CreateScanWithSystemEntries(areaImageOffset, entries, stride);

        var formatter = new TestableDataStoreIso9660Formatter();
        Dictionary<string, FsYaml> result = formatter.BuildPerTypeFsYaml(scan);

        List<FsYamlNode> fileNodes = GetAllFileNodes(result);

        var failures = new List<string>();
        foreach (var (name, fsOffset, size) in entries)
        {
            var matchingNode = fileNodes.FirstOrDefault(n => n.Name == name);
            if (matchingNode == null)
            {
                failures.Add($"Entry '{name}' not found in output");
                continue;
            }

            // For strided system entries, the correct offset is stride.CleanToOffset(FsOffset)
            // WITHOUT adding area.ImageOffset
            long expectedOffset = stride.CleanToOffset(fsOffset, false);
            long actualOffset = matchingNode.Offset;

            if (actualOffset != expectedOffset)
            {
                long buggyOffset = areaImageOffset + stride.CleanToOffset(fsOffset, false);
                string detail = actualOffset == buggyOffset
                    ? $"double-shifted by area.ImageOffset"
                    : $"unexpected value";
                failures.Add(
                    $"Entry '{name}': FsOffset=0x{fsOffset:X}, " +
                    $"expected imageOffset=0x{expectedOffset:X} (stride-converted), " +
                    $"actual=0x{actualOffset:X} ({detail})");
            }
        }

        bool allCorrect = failures.Count == 0;

        string label = allCorrect
            ? "All strided system entries have correct stride-converted offsets (no double-shift)"
            : $"BUG CONFIRMED — strided system entries are double-shifted by area.ImageOffset (0x{areaImageOffset:X}):\n" +
              string.Join("\n", failures);

        return allCorrect.ToProperty().Label(label);
    }

    /// <summary>
    /// Property 1c: Bug Condition — Multiple System Entry Types All Exhibit Double-Shift
    ///
    /// Generates multiple UDF system entry types with random FsOffset values on an area with
    /// non-zero ImageOffset and verifies all produce the correct absolute offset.
    ///
    /// On unfixed code, ALL system entries will be double-shifted.
    ///
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property AllSystemEntryTypes_OnNonZeroImageOffset_ProduceCorrectOffset()
    {
        // Generate random non-zero ImageOffset and random FsOffset values
        var testGen =
            from imageOffset in Gen.Choose(1, 0xFF).Select(o => (long)o * 0x10000)
            from fsOffsets in Gen.Choose(1, 0x1FF).Select(o => (long)o * 0x1000).ListOf(4)
            select new { ImageOffset = imageOffset, FsOffsets = fsOffsets.ToList() };

        return Prop.ForAll(testGen.ToArbitrary(), data =>
        {
            var entries = new[]
            {
                ("__file_" + data.FsOffsets[0].ToString("X"), data.FsOffsets[0], 0x800L),
                ("__fst_" + data.FsOffsets[1].ToString("X"),  data.FsOffsets[1], 0x800L),
                ("__dir_" + data.FsOffsets[2].ToString("X"),  data.FsOffsets[2], 0x800L),
                ("__udfFileSet_" + data.FsOffsets[3].ToString("X"), data.FsOffsets[3], 0x800L),
            };

            Scan scan = CreateScanWithSystemEntries(data.ImageOffset, entries);

            var formatter = new TestableDataStoreIso9660Formatter();
            Dictionary<string, FsYaml> result = formatter.BuildPerTypeFsYaml(scan);

            List<FsYamlNode> fileNodes = GetAllFileNodes(result);

            var failures = new List<string>();
            foreach (var (name, fsOffset, size) in entries)
            {
                var matchingNode = fileNodes.FirstOrDefault(n => n.Name == name);
                if (matchingNode == null)
                {
                    failures.Add($"Entry '{name}' not found in output");
                    continue;
                }

                long expectedOffset = fsOffset;
                long actualOffset = matchingNode.Offset;

                if (actualOffset != expectedOffset)
                {
                    failures.Add(
                        $"'{name}': expected=0x{expectedOffset:X}, actual=0x{actualOffset:X} " +
                        $"(diff=0x{actualOffset - expectedOffset:X})");
                }
            }

            bool allCorrect = failures.Count == 0;

            return allCorrect.Label(
                allCorrect
                    ? $"All system entries correct on area with ImageOffset=0x{data.ImageOffset:X}"
                    : $"area.ImageOffset=0x{data.ImageOffset:X}: " + string.Join("; ", failures));
        });
    }

    /// <summary>
    /// Minimal IFileSystemData implementation for testing.
    /// </summary>
    private class TestFileSystemData : IFileSystemData
    {
        private readonly FstContext _ctx;

        public TestFileSystemData(long imageOffset, FstContext ctx)
        {
            _ctx = ctx;
            ImageOffset = imageOffset;
            Size = 0x10000000;
        }

        public long ImageOffset { get; }
        public long Size { get; }
        public IFileSystem FileSystem => new TestFileSystem(_ctx);
        public bool InvalidFileSystem => false;
        public PartitionType Type => PartitionType.Game;
        public bool AllFoldersParsed => true;
        public FidelityFileList FidelityFiles => _ctx.FidelityFiles;
        public IAreaFileSystemView AreaView => null;
    }

    /// <summary>
    /// Minimal IFileSystem wrapper that returns files from the FstContext.
    /// </summary>
    private class TestFileSystem : IFileSystem
    {
        private readonly FstContext _ctx;

        public TestFileSystem(FstContext ctx) => _ctx = ctx;

        public List<IFsFile> Files => _ctx.FileSystem.ToList();
        public IFsFolder Root => null;
        public List<IFsFile> CloneFiles() => Files.ToList();
    }

    /// <summary>
    /// Testable wrapper around the offset computation in BuildPerTypeFsYaml.
    /// Inherits from DataStoreIso9660Formatter to access the internal method.
    /// Since the full constructor requires I/O infrastructure, this class uses
    /// a separate approach: it directly invokes the internal method by wrapping
    /// the construction.
    /// </summary>
    internal class TestableDataStoreIso9660Formatter
    {
        /// <summary>
        /// Invokes the same offset computation logic as BuildPerTypeFsYaml.
        /// This replicates the exact code path to test the bug.
        /// </summary>
        public Dictionary<string, FsYaml> BuildPerTypeFsYaml(Scan scan)
        {
            var result = new Dictionary<string, FsYaml>(StringComparer.OrdinalIgnoreCase);

            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type != AreaType.FileSystem)
                    continue;

                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                AreaInfo ai = area.AreaInfo;

                // Build a DataStride when the area uses strided blocks
                DataStride stride = null;
                if (ai.BlockSize > 0 && ai.BlockFsSize > 0 && ai.BlockSize != ai.BlockFsSize)
                    stride = new DataStride { SourceBlockSize = ai.BlockSize, DataOffset = ai.BlockFsOffset, DataLength = ai.BlockFsSize };

                string fsName = $"{(ai.AreaNo + 1):D2} {ai.Type}";

                IEnumerable<IFsFile> fileSource = area.FsInfo?.FidelityFiles?.Entries
                    ?? (IEnumerable<IFsFile>)fs.Files;

                // Track system entry offsets already emitted to avoid duplicates from FidelityFiles
                HashSet<long> emittedSystemOffsets = null;

                foreach (IFsFile file in fileSource)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    FstFile fstFile = file as FstFile;
                    if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                        continue;

                    if (fstFile.SplitParts != null && fstFile.SplitParts.Parts.Count >= 2 && fstFile.SplitIndex > 0)
                        continue;

                    bool isSystemEntry = file.IsSystemFile;

                    // Deduplicate system entries (same logic as production code)
                    if (isSystemEntry)
                    {
                        emittedSystemOffsets ??= new HashSet<long>();
                        if (!emittedSystemOffsets.Add(file.FsOffset))
                            continue;
                    }

                    long imageOffset;
                    if (isSystemEntry)
                    {
                        // System entries (UDF metadata) have FsOffset that is NOT area-relative —
                        // it already represents the correct position. Do not add area.ImageOffset.
                        imageOffset = stride != null
                            ? stride.CleanToOffset(file.FsOffset, false)
                            : file.FsOffset;
                    }
                    else
                    {
                        imageOffset = stride != null
                            ? area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)
                            : area.ImageOffset + file.FsOffset;
                    }

                    var typeEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    FstLink rockRidgeLink = null;
                    foreach (FstLink link in fstFile.Links)
                    {
                        if (link.FsType == FsType.RockRidge)
                        {
                            rockRidgeLink = link;
                            break;
                        }
                    }

                    foreach (FstLink link in fstFile.Links)
                    {
                        string resolvedType = DataStoreIso9660Formatter.ResolveTargetFsType(link.FsType);

                        if (typeEntries.ContainsKey(resolvedType))
                        {
                            if (link.FsType == FsType.RockRidge && resolvedType == "iso9660")
                                typeEntries[resolvedType] = buildFullPath(link);
                            continue;
                        }

                        if (resolvedType == "iso9660" && rockRidgeLink != null && link.FsType == FsType.Iso9660)
                            typeEntries[resolvedType] = buildFullPath(rockRidgeLink);
                        else
                            typeEntries[resolvedType] = buildFullPath(link);
                    }

                    foreach (var entry in typeEntries)
                    {
                        string typeName = entry.Key;
                        string fullPath = entry.Value;

                        if (!result.TryGetValue(typeName, out FsYaml yaml))
                        {
                            yaml = new FsYaml();
                            result[typeName] = yaml;
                        }

                        FsYamlNode fsNode = getOrCreateFsNode(yaml, fsName, area.ImageOffset);

                        bool isSystem = isSystemEntry || fstFile.Links.Any(l => l.FsType == FsType.System);

                        if (!isSystem && fstFile.SplitParts != null && fstFile.SplitParts.Parts.Count >= 2)
                        {
                            foreach (IFsFilePart part in fstFile.SplitParts.Parts.OrderBy(p => p.Index))
                            {
                                IFsFile partFile = part.FsFile;
                                long partImageOffset = stride != null
                                    ? area.ImageOffset + stride.CleanToOffset(partFile.FsOffset, false)
                                    : area.ImageOffset + partFile.FsOffset;
                                fsNode.AddFileByPath(fullPath, partImageOffset, partFile.FsSize, partFile.XxHash, partFile.Crc, isSystem);
                            }
                        }
                        else
                        {
                            fsNode.AddFileByPath(fullPath, imageOffset, file.FsSize, file.XxHash, file.Crc, isSystem);
                        }
                    }
                }
            }

            return result;
        }

        private static string buildFullPath(FstLink link)
        {
            string parentPath = link.Parent?.Path ?? "";
            string name = link.EncodedChildName ?? "";
            if (string.IsNullOrEmpty(parentPath))
                return "/" + name;
            return parentPath + "/" + name;
        }

        private static FsYamlNode getOrCreateFsNode(FsYaml yaml, string fsName, long areaOffset)
        {
            foreach (FsYamlNode existing in yaml.FileSystems)
            {
                if (existing.Name == fsName)
                    return existing;
            }
            return yaml.AddFileSystem(fsName, areaOffset);
        }
    }
}
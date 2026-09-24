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
/// Preservation property tests for UDF system entry offset bugfix.
///
/// These tests capture behavior that MUST remain unchanged after the fix is applied.
/// All tests PASS on unfixed code, confirming the baseline behavior to preserve.
///
/// The preservation property states: for all entries where NOT isBugCondition(entry, area)
/// (i.e. IsSystemFile = false OR area.ImageOffset = 0), the computed imageOffset equals:
///   - With stride: area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)
///   - Without stride: area.ImageOffset + file.FsOffset
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
///
/// EXPECTED: These tests PASS on unfixed code (confirms baseline behavior to preserve).
/// </summary>
public class UdfSystemEntryOffsetPreservationTests
{
    /// <summary>
    /// Reuse the same TestableDataStoreIso9660Formatter from bug condition tests.
    /// </summary>
    private readonly UdfSystemEntryOffsetBugConditionTests.TestableDataStoreIso9660Formatter _formatter = new();

    /// <summary>
    /// Creates a Scan with a single ScanArea populated with regular (non-system) file entries.
    /// </summary>
    private static Scan CreateScanWithRegularEntries(
        long areaImageOffset,
        (string name, long fsOffset, long size)[] entries,
        DataStride stride = null)
    {
        var scan = new Scan(SystemType.Default, "test_regular_file");

        int blockSize = stride?.SourceBlockSize ?? 0x800;
        int blockFsOffset = stride?.DataOffset ?? 0;
        int blockFsSize = stride?.DataLength ?? 0x800;

        var areaInfo = new AreaInfo(areaImageOffset, AreaType.FileSystem, 0);
        areaInfo.SetBlock(blockSize, blockFsOffset, blockFsSize, 0x10000000);

        byte[] headerData = new byte[0x8000 * 0x40];
        var header = new ImageHeader(headerData, areaInfo);
        var ctx = header.FstContext;
        var root = new FstFolder(FsType.Udf);

        foreach (var (name, fsOffset, size) in entries)
        {
            ctx.AddFile(root, name, FsType.Udf, fsOffset, size, FsItemType.File, isSystem: false);
        }

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
    /// Creates a Scan with a single ScanArea populated with system entries (ImageOffset = 0).
    /// This tests the case where system entries produce correct offsets because area.ImageOffset is zero.
    /// </summary>
    private static Scan CreateScanWithZeroOffsetSystemEntries(
        (string name, long fsOffset, long size)[] entries,
        DataStride stride = null)
    {
        long areaImageOffset = 0; // Zero ImageOffset — no bug observable

        var scan = new Scan(SystemType.Default, "test_zero_offset_system");

        int blockSize = stride?.SourceBlockSize ?? 0x800;
        int blockFsOffset = stride?.DataOffset ?? 0;
        int blockFsSize = stride?.DataLength ?? 0x800;

        var areaInfo = new AreaInfo(areaImageOffset, AreaType.FileSystem, 0);
        areaInfo.SetBlock(blockSize, blockFsOffset, blockFsSize, 0x10000000);

        byte[] headerData = new byte[0x8000 * 0x40];
        var header = new ImageHeader(headerData, areaInfo);
        var ctx = header.FstContext;
        var root = new FstFolder(FsType.Udf);

        foreach (var (name, fsOffset, size) in entries)
        {
            ctx.AddFile(root, name, FsType.Udf, fsOffset, size, FsItemType.File, isSystem: true);
        }

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
    /// Property 2a: Regular File Offset Computation — area.ImageOffset + FsOffset
    ///
    /// For regular file entries (IsSystemFile = false) with random FsOffset values on areas
    /// with random non-zero ImageOffset, the computed imageOffset SHALL equal
    /// area.ImageOffset + file.FsOffset.
    ///
    /// This preserves the existing correct behavior for regular files after the fix.
    ///
    /// **Validates: Requirements 3.1, 3.5**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property RegularFileOffset_Equals_AreaImageOffset_Plus_FsOffset()
    {
        var testGen =
            from imageOffset in Gen.Choose(0, 0xFF).Select(o => (long)o * 0x10000)
            from fsOffset in Gen.Choose(1, 0x1FF).Select(o => (long)o * 0x1000)
            select new { ImageOffset = imageOffset, FsOffset = fsOffset };

        return Prop.ForAll(testGen.ToArbitrary(), data =>
        {
            string fileName = $"regular_{data.FsOffset:X}.dat";
            var entries = new[] { (fileName, data.FsOffset, 0x2000L) };

            Scan scan = CreateScanWithRegularEntries(data.ImageOffset, entries);
            Dictionary<string, FsYaml> result = _formatter.BuildPerTypeFsYaml(scan);
            List<FsYamlNode> fileNodes = GetAllFileNodes(result);

            var matchingNode = fileNodes.FirstOrDefault(n => n.Name == fileName);
            if (matchingNode == null)
                return false.Label($"Regular file '{fileName}' not found in output");

            long expectedOffset = data.ImageOffset + data.FsOffset;
            long actualOffset = matchingNode.Offset;

            return (actualOffset == expectedOffset).Label(
                actualOffset == expectedOffset
                    ? $"Regular file offset correct: 0x{data.ImageOffset:X} + 0x{data.FsOffset:X} = 0x{expectedOffset:X}"
                    : $"Regular file offset WRONG: expected=0x{expectedOffset:X}, actual=0x{actualOffset:X}");
        });
    }

    /// <summary>
    /// Property 2b: Strided Regular File Offset — area.ImageOffset + stride.CleanToOffset(FsOffset)
    ///
    /// For regular file entries on a strided area (e.g. Mode1Raw 0x930 sectors),
    /// the computed imageOffset SHALL equal area.ImageOffset + stride.CleanToOffset(FsOffset, false).
    ///
    /// This preserves stride conversion for regular files after the fix.
    ///
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property StridedRegularFileOffset_Equals_AreaImageOffset_Plus_StrideConverted()
    {
        // Mode1Raw stride: 0x930 block, 0x10 data offset, 0x800 data length
        var stride = new DataStride
        {
            SourceBlockSize = 0x930,
            DataOffset = 0x10,
            DataLength = 0x800
        };

        var testGen =
            from imageOffset in Gen.Choose(1, 0xFF).Select(o => (long)o * 0x10000)
            from fsOffset in Gen.Choose(1, 0x1FF).Select(o => (long)o * 0x800)
            select new { ImageOffset = imageOffset, FsOffset = fsOffset };

        return Prop.ForAll(testGen.ToArbitrary(), data =>
        {
            string fileName = $"strided_{data.FsOffset:X}.dat";
            var entries = new[] { (fileName, data.FsOffset, 0x800L) };

            Scan scan = CreateScanWithRegularEntries(data.ImageOffset, entries, stride);
            Dictionary<string, FsYaml> result = _formatter.BuildPerTypeFsYaml(scan);
            List<FsYamlNode> fileNodes = GetAllFileNodes(result);

            var matchingNode = fileNodes.FirstOrDefault(n => n.Name == fileName);
            if (matchingNode == null)
                return false.Label($"Strided regular file '{fileName}' not found in output");

            long expectedOffset = data.ImageOffset + stride.CleanToOffset(data.FsOffset, false);
            long actualOffset = matchingNode.Offset;

            return (actualOffset == expectedOffset).Label(
                actualOffset == expectedOffset
                    ? $"Strided regular file offset correct: 0x{data.ImageOffset:X} + stride(0x{data.FsOffset:X}) = 0x{expectedOffset:X}"
                    : $"Strided regular file offset WRONG: expected=0x{expectedOffset:X}, actual=0x{actualOffset:X}");
        });
    }

    /// <summary>
    /// Property 2c: System Entry with Zero ImageOffset — Produces FsOffset Directly
    ///
    /// For system entries (IsSystemFile = true) on an area with ImageOffset = 0,
    /// the computed imageOffset SHALL equal 0 + file.FsOffset = file.FsOffset.
    /// This is NOT a bug condition (area.ImageOffset = 0), so the behavior is correct
    /// on both unfixed and fixed code and must be preserved.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SystemEntryWithZeroImageOffset_Produces_FsOffset()
    {
        var testGen =
            from fsOffset in Gen.Choose(1, 0x1FF).Select(o => (long)o * 0x1000)
            select fsOffset;

        return Prop.ForAll(testGen.ToArbitrary(), fsOffset =>
        {
            string entryName = $"__file_{fsOffset:X}_00001.dat";
            var entries = new[] { (entryName, fsOffset, 0x800L) };

            Scan scan = CreateScanWithZeroOffsetSystemEntries(entries);
            Dictionary<string, FsYaml> result = _formatter.BuildPerTypeFsYaml(scan);
            List<FsYamlNode> fileNodes = GetAllFileNodes(result);

            var matchingNode = fileNodes.FirstOrDefault(n => n.Name == entryName);
            if (matchingNode == null)
                return false.Label($"System entry '{entryName}' not found in output");

            // With ImageOffset = 0: offset = 0 + FsOffset = FsOffset
            long expectedOffset = fsOffset;
            long actualOffset = matchingNode.Offset;

            return (actualOffset == expectedOffset).Label(
                actualOffset == expectedOffset
                    ? $"Zero-ImageOffset system entry correct: offset=0x{actualOffset:X} == FsOffset=0x{fsOffset:X}"
                    : $"Zero-ImageOffset system entry WRONG: expected=0x{expectedOffset:X}, actual=0x{actualOffset:X}");
        });
    }

    /// <summary>
    /// Property 2d: Multiple Regular Files at Various Offsets — All Computed Correctly
    ///
    /// Generates multiple regular file entries with random FsOffset values on an area with
    /// non-zero ImageOffset and verifies ALL produce area.ImageOffset + FsOffset.
    ///
    /// **Validates: Requirements 3.1, 3.5**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property MultipleRegularFiles_AllOffsets_Correct()
    {
        var testGen =
            from imageOffset in Gen.Choose(1, 0xFF).Select(o => (long)o * 0x10000)
            from offsets in Gen.Choose(1, 0x1FF).Select(o => (long)o * 0x1000).ListOf(4)
            where offsets.Distinct().Count() == offsets.Count // ensure distinct offsets
            select new { ImageOffset = imageOffset, FsOffsets = offsets.OrderBy(x => x).ToList() };

        return Prop.ForAll(testGen.ToArbitrary(), data =>
        {
            var entries = data.FsOffsets.Select((o, i) =>
                ($"file_{i}_{o:X}.dat", o, 0x800L)).ToArray();

            Scan scan = CreateScanWithRegularEntries(data.ImageOffset, entries);
            Dictionary<string, FsYaml> result = _formatter.BuildPerTypeFsYaml(scan);
            List<FsYamlNode> fileNodes = GetAllFileNodes(result);

            var failures = new List<string>();
            foreach (var (name, fsOffset, size) in entries)
            {
                var matchingNode = fileNodes.FirstOrDefault(n => n.Name == name);
                if (matchingNode == null)
                {
                    failures.Add($"'{name}' not found in output");
                    continue;
                }

                long expectedOffset = data.ImageOffset + fsOffset;
                if (matchingNode.Offset != expectedOffset)
                {
                    failures.Add(
                        $"'{name}': expected=0x{expectedOffset:X}, actual=0x{matchingNode.Offset:X}");
                }
            }

            bool allCorrect = failures.Count == 0;
            return allCorrect.Label(
                allCorrect
                    ? $"All {entries.Length} regular files correct on area ImageOffset=0x{data.ImageOffset:X}"
                    : $"FAILURES: " + string.Join("; ", failures));
        });
    }

    /// <summary>
    /// Property 2e: Concrete Observation — Regular File with FsOffset=0x100000 on ImageOffset=0x7E0000
    ///
    /// Specific observation from task description: regular file entry with FsOffset=0x100000
    /// on area with ImageOffset=0x7E0000 produces imageOffset=0x8E0000.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property ConcreteObservation_RegularFile_0x100000_On_0x7E0000_Produces_0x8E0000()
    {
        long areaImageOffset = 0x7E0000;
        long fsOffset = 0x100000;
        long expectedImageOffset = 0x8E0000; // 0x7E0000 + 0x100000

        var entries = new[] { ("regular_file.dat", fsOffset, 0x2000L) };

        Scan scan = CreateScanWithRegularEntries(areaImageOffset, entries);
        Dictionary<string, FsYaml> result = _formatter.BuildPerTypeFsYaml(scan);
        List<FsYamlNode> fileNodes = GetAllFileNodes(result);

        var matchingNode = fileNodes.FirstOrDefault(n => n.Name == "regular_file.dat");
        if (matchingNode == null)
            return false.ToProperty().Label("Regular file 'regular_file.dat' not found in output");

        bool correct = matchingNode.Offset == expectedImageOffset;

        return correct.ToProperty().Label(
            correct
                ? $"Concrete observation verified: 0x{areaImageOffset:X} + 0x{fsOffset:X} = 0x{expectedImageOffset:X}"
                : $"Concrete observation FAILED: expected=0x{expectedImageOffset:X}, actual=0x{matchingNode.Offset:X}");
    }

    /// <summary>
    /// Minimal IFileSystemData implementation for testing.
    /// Same pattern as bug condition tests.
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
}
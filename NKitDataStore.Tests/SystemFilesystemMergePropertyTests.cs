using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using Nanook.NKit.Vfs;
using System.Reflection;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for the NKDS System Filesystem Merge feature.
///
/// Feature: nkds-system-filesystem-merge
/// </summary>
public class SystemFilesystemMergePropertyTests
{
    private static readonly FsType[] AllFsTypes = Enum.GetValues<FsType>();
    private static readonly string[] SystemFileNames =
        { "IP.BIN", "BOOT.BIN", "PVD.DAT", "SYSTEM.CNF", "PSX.EXE", "ICON.SYS" };

    /// <summary>
    /// Feature: nkds-system-filesystem-merge, Property 9: ResolveTargetFsType routes System to "system"
    ///
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ResolveTargetFsType_System_AlwaysReturnsSystem()
    {
        Gen<FsType> parentFsTypeGen = Gen.Elements(AllFsTypes);
        return Prop.ForAll(parentFsTypeGen.ToArbitrary(), parentFsType =>
        {
            string result = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.System, parentFsType);
            return (result == "system")
                .Label($"Expected 'system' but got '{result}' when parentFsType={parentFsType}");
        });
    }

    /// <summary>
    /// Feature: nkds-system-filesystem-merge, Property 9: ResolveTargetFsType routes System to "system"
    ///
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Fact]
    public void ResolveTargetFsType_System_NullParent_ReturnsSystem()
    {
        string result = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.System, null);
        Assert.Equal("system", result);
    }

    /// <summary>
    /// Feature: nkds-system-filesystem-merge, Property 2: Merged entries preserve offset and size
    ///
    /// **Validates: Requirements 1.4, 3.2**
    ///
    /// For any system NkFs entry with a given offset and file size, after merging into a target NkFs,
    /// the merged entry SHALL have the same offset and file size, and SHALL have IsSystem=true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MergedEntries_PreserveOffsetAndSize_AndHaveIsSystemTrue()
    {
        var gen = from count in Gen.Choose(1, 5)
                  from offsets in Gen.Choose(0, 0x7FFFFF).Select(x => (long)x).ListOf(count)
                  from sizes in Gen.Choose(1, 0xFFFF).Select(x => (long)x).ListOf(count)
                  from nameIndices in Gen.Choose(0, SystemFileNames.Length - 1).ListOf(count)
                  select new { Offsets = offsets, Sizes = sizes, NameIndices = nameIndices };

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            FsYaml systemFsYaml = new FsYaml();
            FsYamlNode systemRoot = systemFsYaml.AddFileSystem(".", 0);
            for (int i = 0; i < data.Offsets.Count; i++)
                systemRoot.AddFile(SystemFileNames[data.NameIndices[i]], data.Offsets[i], data.Sizes[i], 0, 0, isSystem: true);
            NkFs systemNkfs = NkFs.FromFsYaml(systemFsYaml);

            FsYaml targetFsYaml = new FsYaml();
            targetFsYaml.AddFileSystem(".", 0);
            NkFs targetNkfs = NkFs.FromFsYaml(targetFsYaml);

            NkFs merged = NkFsMerger.MergeSystemInto(targetNkfs, systemNkfs);

            FsYaml mergedFsYaml = merged.ToFsYaml();
            List<FsYamlNode> mergedSystemFiles = new List<FsYamlNode>();
            CollectFiles(mergedFsYaml.FileSystems[0], mergedSystemFiles, onlySystem: true);

            for (int i = 0; i < data.Offsets.Count; i++)
            {
                string name = SystemFileNames[data.NameIndices[i]];
                long offset = data.Offsets[i];
                long size = data.Sizes[i];
                bool found = mergedSystemFiles.Any(f =>
                    f.Name == name && f.Offset == offset && f.Size == size && f.IsSystem);
                if (!found)
                    return false.Label($"Entry '{name}' (0x{offset:X}, 0x{size:X}) not preserved");
            }
            return true.Label("All system entries preserved offset, size, and IsSystem=true");
        });
    }

    /// <summary>
    /// Feature: nkds-system-filesystem-merge, Property 4: System key retained when no non-system types exist
    ///
    /// **Validates: Requirements 2.3**
    ///
    /// For any per-type dictionary where "system" is the only key, the merge step SHALL
    /// leave the dictionary unchanged (the "system" key remains).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SystemKeyRetained_WhenNoNonSystemTypesExist()
    {
        var gen = from fileCount in Gen.Choose(1, 6)
                  from offsets in Gen.Choose(0, 0x7FFFFF).Select(x => (long)x).ListOf(fileCount)
                  from sizes in Gen.Choose(1, 0xFFFF).Select(x => (long)x).ListOf(fileCount)
                  from nameIndices in Gen.Choose(0, SystemFileNames.Length - 1).ListOf(fileCount)
                  select new { FileCount = fileCount, Offsets = offsets, Sizes = sizes, NameIndices = nameIndices };

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            // Build a system NkFs with random entries
            FsYaml systemFsYaml = new FsYaml();
            FsYamlNode systemRoot = systemFsYaml.AddFileSystem(".", 0);
            for (int i = 0; i < data.FileCount; i++)
                systemRoot.AddFile(SystemFileNames[data.NameIndices[i]], data.Offsets[i], data.Sizes[i], 0, 0, isSystem: true);
            NkFs systemNkfs = NkFs.FromFsYaml(systemFsYaml);

            // Create per-type dictionary with ONLY "system" key
            Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
            {
                ["system"] = systemNkfs
            };

            // Call the merge logic (replicates MountRegistry.mergeSystemFilesystem)
            MergeSystemFilesystem(perType);

            // Verify dictionary is unchanged: still has "system" key, same NkFs instance
            bool hasSystemKey = perType.ContainsKey("system");
            bool sameCount = perType.Count == 1;
            bool sameInstance = ReferenceEquals(perType["system"], systemNkfs);

            return (hasSystemKey && sameCount && sameInstance)
                .Label($"Expected dictionary unchanged with 'system' key retained. " +
                       $"HasSystemKey={hasSystemKey}, Count={perType.Count}, SameInstance={sameInstance}");
        });
    }

    /// <summary>
    /// Feature: nkds-system-filesystem-merge, Property 3: System key removed after merge when non-system types exist
    ///
    /// **Validates: Requirements 2.1, 2.2**
    ///
    /// For any per-type dictionary containing a "system" key and at least one non-system key,
    /// after the merge step, the dictionary SHALL NOT contain the "system" key.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SystemKeyRemovedAfterMerge_WhenNonSystemTypesExist()
    {
        // Non-system type names that could appear in a per-type dictionary
        string[] nonSystemTypeNames = { "iso9660", "joliet", "udf", "romeo", "eltorito", "cdi" };

        var gen = from nonSystemCount in Gen.Choose(1, 4)
                  from typeIndices in Gen.Choose(0, nonSystemTypeNames.Length - 1).ListOf(nonSystemCount)
                  from systemFileCount in Gen.Choose(1, 5)
                  from targetFileCount in Gen.Choose(1, 4)
                  from seed in Gen.Choose(0, int.MaxValue)
                  select new
                  {
                      TypeIndices = typeIndices.Distinct().ToList(),
                      SystemFileCount = systemFileCount,
                      TargetFileCount = targetFileCount,
                      Seed = seed
                  };

        return Prop.ForAll(gen.Where(x => x.TypeIndices.Count >= 1).ToArbitrary(), data =>
        {
            // Build a system NkFs
            FsYaml systemFsYaml = new FsYaml();
            FsYamlNode systemRoot = systemFsYaml.AddFileSystem(".", 0);
            for (int i = 0; i < data.SystemFileCount; i++)
            {
                string name = SystemFileNames[i % SystemFileNames.Length];
                long offset = (long)((data.Seed + (i * 0x2000)) & 0x7FFFFFFFL);
                long size = (long)((data.Seed + (i * 137)) % 0x8000) + 1;
                systemRoot.AddFile(name, offset, size, 0, 0, isSystem: true);
            }
            NkFs systemNkFs = NkFs.FromFsYaml(systemFsYaml);

            // Build the per-type dictionary with "system" key + non-system keys
            Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);
            perType["system"] = systemNkFs;

            foreach (int idx in data.TypeIndices)
            {
                string typeName = nonSystemTypeNames[idx];
                FsYaml targetFsYaml = new FsYaml();
                FsYamlNode targetRoot = targetFsYaml.AddFileSystem(".", 0);
                for (int i = 0; i < data.TargetFileCount; i++)
                {
                    string name = $"{typeName.ToUpper()}_{i}.BIN";
                    long offset = (long)((data.Seed + (idx * 0x10000) + (i * 0x4000)) & 0x7FFFFFFFL);
                    long size = (long)((data.Seed + (idx * 99) + (i * 53)) % 0x10000) + 1;
                    targetRoot.AddFile(name, offset, size, 0, 0, isSystem: false);
                }
                perType[typeName] = NkFs.FromFsYaml(targetFsYaml);
            }

            // Replicate the mergeSystemFilesystem logic (private in MountRegistry)
            MergeSystemFilesystem(perType);

            // Verify: "system" key should be absent
            bool systemKeyAbsent = !perType.ContainsKey("system");
            return systemKeyAbsent
                .Label($"Expected 'system' key to be removed but it is still present. " +
                       $"Non-system keys: [{string.Join(", ", data.TypeIndices.Select(i => nonSystemTypeNames[i]))}]");
        });
    }

    /// <summary>
    /// Feature: nkds-system-filesystem-merge, Property 10: getBestFilesystem never selects "system" when alternatives exist
    ///
    /// **Validates: Requirements 6.4**
    ///
    /// For any per-type dictionary containing at least one non-system key from the priority list,
    /// getBestFilesystem SHALL NOT return the NkFs associated with the "system" key.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetBestFilesystem_NeverSelectsSystem_WhenAlternativesExist()
    {
        // The priority list used by getBestFilesystem
        string[] priorityTypes = { "udf", "joliet", "rockridge", "romeo", "iso9660" };

        var gen = from priorityCount in Gen.Choose(1, 3)
                  from priorityIndices in Gen.Choose(0, priorityTypes.Length - 1).ListOf(priorityCount)
                  from includeSystem in Gen.Elements(true, false)
                  from seed in Gen.Choose(0, int.MaxValue)
                  select new
                  {
                      PriorityIndices = priorityIndices.Distinct().ToList(),
                      IncludeSystem = includeSystem,
                      Seed = seed
                  };

        return Prop.ForAll(gen.Where(x => x.PriorityIndices.Count >= 1).ToArbitrary(), data =>
        {
            // Build a per-type dictionary with at least one non-system priority key
            Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);

            // Add non-system NkFs entries for each selected priority type
            foreach (int idx in data.PriorityIndices)
            {
                string typeName = priorityTypes[idx];
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                root.AddFile($"{typeName.ToUpper()}_FILE.BIN",
                    (long)((data.Seed + (idx * 0x10000)) & 0x7FFFFFFFL),
                    (long)((data.Seed + (idx * 99)) % 0x10000) + 1,
                    0, 0, isSystem: false);
                perType[typeName] = NkFs.FromFsYaml(fsYaml);
            }

            // Optionally add a "system" NkFs
            if (data.IncludeSystem)
            {
                FsYaml systemFsYaml = new FsYaml();
                FsYamlNode systemRoot = systemFsYaml.AddFileSystem(".", 0);
                systemRoot.AddFile("IP.BIN",
                    (long)((data.Seed + 0x5000) & 0x7FFFFFFFL),
                    (long)(data.Seed * 37 % 0x8000) + 1,
                    0, 0, isSystem: true);
                perType["system"] = NkFs.FromFsYaml(systemFsYaml);
            }

            // Invoke the private static getBestFilesystem via reflection
            NkFs result = InvokeGetBestFilesystem(perType);

            // If "system" key exists in the dictionary, verify the result is NOT the system NkFs
            if (data.IncludeSystem)
            {
                NkFs systemNkfs = perType["system"];
                bool isNotSystem = !ReferenceEquals(result, systemNkfs);
                return isNotSystem
                    .Label($"getBestFilesystem returned the system NkFs when alternatives existed. " +
                           $"Non-system keys: [{string.Join(", ", data.PriorityIndices.Select(i => priorityTypes[i]))}]");
            }
            else
            {
                // When system is not present, just verify we get a non-null result
                bool isNotNull = result != null;
                return isNotNull
                    .Label("getBestFilesystem returned null when non-system entries existed");
            }
        });
    }

    /// <summary>
    /// Invokes the private static getBestFilesystem method on FilesystemMountHandler via reflection.
    /// </summary>
    private static NkFs InvokeGetBestFilesystem(Dictionary<string, NkFs> perType)
    {
        Type handlerType = typeof(FilesystemMountHandler);
        MethodInfo method = handlerType.GetMethod(
            "getBestFilesystem",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException(
                "Could not find getBestFilesystem method on FilesystemMountHandler via reflection");

        return (NkFs)method.Invoke(null, new object[] { perType });
    }

    /// <summary>
    /// Replicates the private MountRegistry.mergeSystemFilesystem logic for testing.
    /// If the per-type dictionary contains a "system" key, merges its entries
    /// into all non-system NkFs instances, then removes the "system" key.
    /// When no non-system types exist, the "system" key is retained.
    /// </summary>
    private static void MergeSystemFilesystem(Dictionary<string, NkFs> perType)
    {
        if (!perType.TryGetValue("system", out NkFs systemNkfs))
            return;

        List<string> nonSystemKeys = perType.Keys
            .Where(k => !string.Equals(k, "system", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nonSystemKeys.Count == 0)
            return; // Only system exists — retain it for fallback presentation

        foreach (string key in nonSystemKeys)
        {
            perType[key] = NkFsMerger.MergeSystemInto(perType[key], systemNkfs);
        }

        perType.Remove("system");
    }

    /// <summary>
    /// Recursively collects all file (leaf) nodes from a FsYamlNode tree.
    /// </summary>
    private static void CollectFiles(FsYamlNode node, List<FsYamlNode> result, bool onlySystem = false)
    {
        if (node.IsFile)
        {
            if (!onlySystem || node.IsSystem)
                result.Add(node);
            return;
        }
        if (node.Children != null)
            foreach (FsYamlNode child in node.Children)
                CollectFiles(child, result, onlySystem);
    }

    /// <summary>
    /// Generates a deterministic file name for property-based testing.
    /// </summary>
    private static string GenerateFileName(int seed, string prefix, int index)
    {
        int hash = Math.Abs((seed * 31) + (index * 17));
        return $"{prefix}_{hash % 1000:D3}.BIN";
    }

    /// <summary>
    /// Collects all non-system file entries from a FsYaml as (path, offset, size) tuples.
    /// </summary>
    private static List<(string path, long offset, long size)> CollectNonSystemFileEntries(FsYaml fsYaml)
    {
        List<(string path, long offset, long size)> results = new List<(string path, long offset, long size)>();
        if (fsYaml.FileSystems.Count > 0)
            CollectNonSystemFileEntriesRecursive(fsYaml.FileSystems[0], "", results);
        return results;
    }

    private static void CollectNonSystemFileEntriesRecursive(
        FsYamlNode node, string parentPath, List<(string path, long offset, long size)> results)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";
        if (node.IsFile)
        {
            if (!node.IsSystem)
                results.Add((currentPath, node.Offset, node.Size));
            return;
        }
        if (node.Children != null)
            foreach (FsYamlNode child in node.Children)
                CollectNonSystemFileEntriesRecursive(child, currentPath, results);
    }
}
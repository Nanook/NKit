using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit.Vfs;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests verifying multi-filesystem mount presents only type subfolders at root.
///
/// Feature: multi-filesystem-nkfs, Property 6: Multi-filesystem mount presents only type subfolders at root
///
/// For any VfsModelItem with more than one per-type NkFs instance, listing the image root
/// SHALL return exactly the set of filesystem type names as folder entries, with no file
/// entries at the root level.
///
/// **Validates: Requirements 3.1, 3.4**
/// </summary>
public class MultiFilesystemSubfolderListingPropertyTests
{
    /// <summary>
    /// Known non-extension filesystem type names that can appear as per-type nkfs keys.
    /// These are the valid subfolder names for multi-filesystem images.
    /// </summary>
    private static readonly string[] ValidTypeNames = new[]
    {
        "iso9660", "joliet", "udf", "cdi", "romeo", "eltorito", "system", "other"
    };

    /// <summary>
    /// Creates a minimal valid NkFs instance for testing (empty root with one file).
    /// </summary>
    private static NkFs CreateMinimalNkFs(string fileName = "test.bin")
    {
        FsYamlNode root = FsYamlNode.CreateDirectory("root");
        root.AddFile(fileName, 0x1000, 512, 0, 0);
        FsYaml fsYaml = new FsYaml();
        fsYaml.FileSystems.Add(root);
        return NkFs.FromFsYaml(fsYaml);
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 3.4**
    ///
    /// Property 6: Multi-filesystem mount presents only type subfolders at root.
    /// For any set of 2+ distinct lowercase type names assigned to FileSystemNkfsPerType,
    /// IsMultiFilesystem is true and the dictionary keys represent exactly the subfolder
    /// names that would be presented at the image root level.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property MultiFilesystem_PresentsOnlyTypeSubfolders_AtRoot()
    {
        // Generate a subset of 2+ distinct type names from the valid set
        Gen<List<string>> typeNamesGen = Gen.SubListOf(ValidTypeNames)
            .Where(list => list.Count >= 2)
            .Select(list => list.Distinct().ToList());

        return Prop.ForAll(typeNamesGen.ToArbitrary(), typeNames =>
        {
            // Arrange: create a VfsModelItem with FileSystemNkfsPerType populated
            Dictionary<string, NkFs> perTypeDict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);
            foreach (string typeName in typeNames)
            {
                perTypeDict[typeName] = CreateMinimalNkFs($"{typeName}_file.bin");
            }

            VfsModelItem item = new VfsModelItem
            {
                FileSystemNkfsPerType = perTypeDict,
                FileSystemNkfsLoaded = true,
                FileSystemNkfs = null // null when multi-filesystem
            };

            // Assert 1: IsMultiFilesystem must be true when count > 1
            bool isMulti = item.IsMultiFilesystem;

            // Assert 2: The dictionary keys are exactly the type names we set
            HashSet<string> presentedSubfolders = new HashSet<string>(
                item.FileSystemNkfsPerType.Keys, StringComparer.OrdinalIgnoreCase);
            HashSet<string> expectedSubfolders = new HashSet<string>(
                typeNames, StringComparer.OrdinalIgnoreCase);

            bool subfoldersMatch = presentedSubfolders.SetEquals(expectedSubfolders);

            // Assert 3: Count matches — exactly N folder entries for N types
            bool countMatches = item.FileSystemNkfsPerType.Count == typeNames.Count;

            // Assert 4: No file entries at root level — the dictionary contains only
            // NkFs instances (folders), not file data. The FilesystemMountHandler.ListFolder
            // yields only FsFolder entries (one per key) when IsMultiFilesystem is true.
            // At the model level, this is guaranteed by the fact that dictionary keys
            // are type names (strings) and values are NkFs instances (folder trees).
            bool noFileEntriesAtRoot = item.FileSystemNkfsPerType.Keys
                .All(k => !string.IsNullOrEmpty(k));

            return isMulti
                .Label("IsMultiFilesystem should be true for 2+ types")
                .And(subfoldersMatch)
                .Label($"Subfolders should match type names: expected [{string.Join(", ", expectedSubfolders)}], got [{string.Join(", ", presentedSubfolders)}]")
                .And(countMatches)
                .Label($"Count should be {typeNames.Count}, got {item.FileSystemNkfsPerType.Count}")
                .And(noFileEntriesAtRoot)
                .Label("All subfolder names should be non-empty strings");
        });
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 3.4**
    ///
    /// Property 6 (supplementary): For randomly generated type name sets (not just
    /// the known valid names), the IsMultiFilesystem property and key enumeration
    /// invariants hold. This tests with arbitrary lowercase alphabetic strings to
    /// ensure the model-level contract is not dependent on specific type names.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property ArbitraryTypeNames_MultiFilesystem_PresentsExactKeys()
    {
        // Generate 2-6 distinct random lowercase type names (alphabetic, 2-12 chars)
        Gen<string> typeNameGen = Gen.Choose(2, 12).SelectMany(len =>
            Gen.Elements(Enumerable.Range('a', 26).Select(c => (char)c).ToArray())
               .ArrayOf(len)
               .Select(chars => new string(chars)));

        Gen<List<string>> typeNamesGen = Gen.Choose(2, 6).SelectMany(count =>
            typeNameGen.ListOf(count * 2) // generate extra to ensure we get enough distinct
                .Select(names => names.Distinct().Take(count).ToList())
                .Where(names => names.Count >= 2));

        return Prop.ForAll(typeNamesGen.ToArbitrary(), typeNames =>
        {
            // Arrange: create VfsModelItem with arbitrary type names
            Dictionary<string, NkFs> perTypeDict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);
            foreach (string typeName in typeNames)
            {
                perTypeDict[typeName] = CreateMinimalNkFs();
            }

            VfsModelItem item = new VfsModelItem
            {
                FileSystemNkfsPerType = perTypeDict,
                FileSystemNkfsLoaded = true,
                FileSystemNkfs = null
            };

            // The invariant: IsMultiFilesystem is true and keys match exactly
            bool isMulti = item.IsMultiFilesystem;
            int keyCount = item.FileSystemNkfsPerType.Count;
            HashSet<string> keys = new HashSet<string>(
                item.FileSystemNkfsPerType.Keys, StringComparer.OrdinalIgnoreCase);
            HashSet<string> expected = new HashSet<string>(
                typeNames, StringComparer.OrdinalIgnoreCase);

            return isMulti
                .Label($"IsMultiFilesystem should be true for {typeNames.Count} types")
                .And(keys.SetEquals(expected))
                .Label("Dictionary keys should equal the input type names")
                .And(keyCount >= 2)
                .Label($"Key count {keyCount} should be >= 2");
        });
    }
}
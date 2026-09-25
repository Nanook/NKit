using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit.Vfs;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests verifying single-filesystem mount presents files directly.
///
/// Feature: multi-filesystem-nkfs, Property 7: Single-filesystem mount presents files directly
///
/// For any VfsModelItem with exactly one NkFs instance (unified or sole per-type),
/// listing the image root SHALL return the NkFs root children directly without any
/// filesystem-type subfolder indirection.
///
/// **Validates: Requirements 3.2**
/// </summary>
public class SingleFilesystemDirectListingPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 3.2**
    ///
    /// Property 7: Single-filesystem mount presents files directly.
    /// When FileSystemNkfsPerType is null, IsMultiFilesystem is false,
    /// confirming the handler uses the direct listing path.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property NullPerType_IsNotMultiFilesystem()
    {
        // Generate random file counts to create NkFs instances of varying sizes
        Gen<int> fileCountGen = Gen.Choose(1, 20);

        return Prop.ForAll(fileCountGen.ToArbitrary(), fileCount =>
        {
            // Create a VfsModelItem with a single unified NkFs (no per-type dictionary)
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            for (int i = 0; i < fileCount; i++)
            {
                root.AddFile($"file{i}.dat", i * 1000, 500 + i, (ulong)(i * 111), (uint)(i * 222), isSystem: false);
            }

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            VfsModelItem item = new VfsModelItem
            {
                FileSystemNkfs = nkfs,
                FileSystemNkfsPerType = null,
                FileSystemNkfsLoaded = true
            };

            // Key invariant: IsMultiFilesystem is false when PerType is null
            return (!item.IsMultiFilesystem)
                .Label("IsMultiFilesystem should be false when FileSystemNkfsPerType is null");
        });
    }

    /// <summary>
    /// **Validates: Requirements 3.2**
    ///
    /// Property 7: Single-filesystem mount presents files directly.
    /// When FileSystemNkfsPerType has exactly one entry, IsMultiFilesystem is false,
    /// confirming the handler uses the direct listing path (no subfolder indirection).
    /// </summary>
    [Property(MaxTest = 200)]
    public Property SingleEntryPerType_IsNotMultiFilesystem()
    {
        // Generate a random filesystem type name and file count
        Gen<string> typeNameGen = Gen.Elements("iso9660", "joliet", "udf", "cdi", "romeo", "eltorito", "system");
        Gen<int> fileCountGen = Gen.Choose(1, 20);

        return Prop.ForAll(typeNameGen.ToArbitrary(), fileCountGen.ToArbitrary(),
            (typeName, fileCount) =>
            {
                // Create a single NkFs instance
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                for (int i = 0; i < fileCount; i++)
                {
                    root.AddFile($"file{i}.dat", i * 1000, 500 + i, (ulong)(i * 111), (uint)(i * 222), isSystem: false);
                }

                NkFs nkfs = NkFs.FromFsYaml(fsYaml);

                // Create a VfsModelItem with exactly one per-type entry
                Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
                {
                    [typeName] = nkfs
                };

                VfsModelItem item = new VfsModelItem
                {
                    FileSystemNkfs = nkfs,
                    FileSystemNkfsPerType = perType,
                    FileSystemNkfsLoaded = true
                };

                // Key invariant: IsMultiFilesystem is false when PerType has count <= 1
                return (!item.IsMultiFilesystem)
                    .Label($"IsMultiFilesystem should be false when FileSystemNkfsPerType has exactly 1 entry (type: {typeName})");
            });
    }

    /// <summary>
    /// **Validates: Requirements 3.2**
    ///
    /// Property 7: Single-filesystem mount presents files directly.
    /// When FileSystemNkfsPerType has exactly one entry, the NkFs root children
    /// are directly accessible without subfolder indirection. Verifies that the
    /// single NkFs instance's root children count matches the generated file count.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property SingleFilesystem_RootChildrenAccessibleDirectly()
    {
        Gen<string> typeNameGen = Gen.Elements("iso9660", "joliet", "udf", "cdi", "romeo", "eltorito", "system");
        Gen<int> fileCountGen = Gen.Choose(1, 15);

        return Prop.ForAll(typeNameGen.ToArbitrary(), fileCountGen.ToArbitrary(),
            (typeName, fileCount) =>
            {
                // Create a single NkFs instance with known file count
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                for (int i = 0; i < fileCount; i++)
                {
                    root.AddFile($"file{i}.dat", i * 1000, 500 + i, (ulong)(i * 111), (uint)(i * 222), isSystem: false);
                }

                NkFs nkfs = NkFs.FromFsYaml(fsYaml);

                // Create a VfsModelItem with exactly one per-type entry
                Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
                {
                    [typeName] = nkfs
                };

                VfsModelItem item = new VfsModelItem
                {
                    FileSystemNkfs = nkfs,
                    FileSystemNkfsPerType = perType,
                    FileSystemNkfsLoaded = true
                };

                // Since IsMultiFilesystem is false, the handler presents root children directly.
                // Verify the NkFs root children are the files we added (no subfolder indirection).
                List<(int index, NkFsEntry entry)> rootChildren = nkfs.GetChildren(0).ToList();

                return (!item.IsMultiFilesystem && rootChildren.Count == fileCount)
                    .Label($"Single filesystem should present {fileCount} root children directly, got {rootChildren.Count}, IsMultiFilesystem={item.IsMultiFilesystem}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 3.2**
    ///
    /// Property 7: Single-filesystem mount presents files directly (contrast with multi).
    /// When FileSystemNkfsPerType has more than one entry, IsMultiFilesystem is true.
    /// This is the negative case confirming the boundary: only count > 1 triggers subfolder mode.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property MultipleEntryPerType_IsMultiFilesystem()
    {
        // Generate 2-5 distinct filesystem type names
        Gen<int> typeCountGen = Gen.Choose(2, 5);

        return Prop.ForAll(typeCountGen.ToArbitrary(), typeCount =>
        {
            string[] allTypes = ["iso9660", "joliet", "udf", "cdi", "romeo", "eltorito", "system"];
            string[] selectedTypes = allTypes.Take(typeCount).ToArray();

            // Create per-type NkFs instances
            Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);
            foreach (string typeName in selectedTypes)
            {
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                root.AddFile("file.dat", 1000, 500, 0xAA, 0xBB, isSystem: false);
                perType[typeName] = NkFs.FromFsYaml(fsYaml);
            }

            VfsModelItem item = new VfsModelItem
            {
                FileSystemNkfs = null,
                FileSystemNkfsPerType = perType,
                FileSystemNkfsLoaded = true
            };

            // Contrast: IsMultiFilesystem is true when count > 1
            return item.IsMultiFilesystem
                .Label($"IsMultiFilesystem should be true when FileSystemNkfsPerType has {typeCount} entries");
        });
    }
}
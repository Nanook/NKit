namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for NkFsMerger edge cases.
///
/// Feature: nkds-system-filesystem-merge
/// Requirements: 3.1, 3.4
/// </summary>
public class NkFsMergerUnitTests
{
    /// <summary>
    /// Helper: creates an NkFs from a FsYaml with a single filesystem root "." and no children.
    /// </summary>
    private static NkFs CreateEmptyNkFs()
    {
        FsYaml fsYaml = new FsYaml();
        fsYaml.AddFileSystem(".", 0);
        return NkFs.FromFsYaml(fsYaml);
    }

    #region Empty System NkFs Merge

    /// <summary>
    /// Merging an empty system NkFs into a target leaves the target unchanged.
    /// The target's original entries should all be present with identical offsets and sizes.
    /// Validates: Requirement 3.1
    /// </summary>
    [Fact]
    public void MergeSystemInto_EmptySystem_TargetUnchanged()
    {
        // Arrange: target with some files and directories
        FsYaml targetYaml = new FsYaml();
        FsYamlNode targetRoot = targetYaml.AddFileSystem(".", 0);
        targetRoot.AddFile("GAME.BIN", 0x1000, 0x5000, 0xAAAA, 0x1111);
        FsYamlNode dir = targetRoot.AddDirectory("DATA");
        dir.AddFile("README.TXT", 0x6000, 0x100, 0xBBBB, 0x2222);

        NkFs target = NkFs.FromFsYaml(targetYaml);
        NkFs emptySystem = CreateEmptyNkFs();

        // Capture original state
        string originalYaml = target.ToFsYaml().ToYaml();

        // Act
        NkFs result = NkFsMerger.MergeSystemInto(target, emptySystem);

        // Assert: result should be identical to original target
        string resultYaml = result.ToFsYaml().ToYaml();
        Assert.Equal(originalYaml, resultYaml);
    }

    #endregion

    #region Merge Into Empty Target

    /// <summary>
    /// Merging a system NkFs into an empty target produces a result containing
    /// only the system entries (all marked with IsSystem=true).
    /// Validates: Requirements 3.1 (empty target preserved — nothing to preserve)
    /// </summary>
    [Fact]
    public void MergeSystemInto_EmptyTarget_ResultContainsOnlySystemEntries()
    {
        // Arrange: empty target
        NkFs emptyTarget = CreateEmptyNkFs();

        // System NkFs with files
        FsYaml systemYaml = new FsYaml();
        FsYamlNode systemRoot = systemYaml.AddFileSystem(".", 0);
        systemRoot.AddFile("IP.BIN", 0x0, 0x8000, 0xDEAD, 0xBEEF, isSystem: true);
        FsYamlNode sysDir = systemRoot.AddDirectory("BOOT", isSystem: true);
        sysDir.AddFile("LOADER.BIN", 0x8000, 0x200, 0xCAFE, 0xBABE, isSystem: true);

        NkFs system = NkFs.FromFsYaml(systemYaml);

        // Act
        NkFs result = NkFsMerger.MergeSystemInto(emptyTarget, system);

        // Assert: result should contain the system entries
        FsYaml resultYaml = result.ToFsYaml();
        FsYamlNode resultRoot = resultYaml.FileSystems[0];

        Assert.NotNull(resultRoot.Children);
        Assert.Equal(2, resultRoot.Children!.Count); // IP.BIN + BOOT directory

        // Verify IP.BIN
        FsYamlNode ipBin = resultRoot.Children.First(c => c.Name == "IP.BIN");
        Assert.True(ipBin.IsFile);
        Assert.True(ipBin.IsSystem);
        Assert.Equal(0x0L, ipBin.Offset);
        Assert.Equal(0x8000L, ipBin.Size);

        // Verify BOOT directory and its child
        FsYamlNode bootDir = resultRoot.Children.First(c => c.Name == "BOOT");
        Assert.True(bootDir.IsDirectory);
        Assert.True(bootDir.IsSystem);
        Assert.NotNull(bootDir.Children);
        Assert.Single(bootDir.Children!);

        FsYamlNode loader = bootDir.Children![0];
        Assert.Equal("LOADER.BIN", loader.Name);
        Assert.True(loader.IsFile);
        Assert.True(loader.IsSystem);
        Assert.Equal(0x8000L, loader.Offset);
        Assert.Equal(0x200L, loader.Size);
    }

    #endregion

    #region Case-Insensitive Directory Matching

    /// <summary>
    /// When a system directory name differs in case from a target directory,
    /// the merge reuses the existing target directory (no duplicate created).
    /// E.g., system has "SYSTEM" and target has "system" — they should merge.
    /// Validates: Requirement 3.4
    /// </summary>
    [Fact]
    public void MergeSystemInto_CaseInsensitiveDirectoryMatch_NoDuplicate()
    {
        // Arrange: target has "system" directory (lowercase)
        FsYaml targetYaml = new FsYaml();
        FsYamlNode targetRoot = targetYaml.AddFileSystem(".", 0);
        FsYamlNode targetDir = targetRoot.AddDirectory("system");
        targetDir.AddFile("README.TXT", 0x6000, 0x100, 0xBBBB, 0x2222);

        NkFs target = NkFs.FromFsYaml(targetYaml);

        // System has "SYSTEM" directory (uppercase)
        FsYaml systemYaml = new FsYaml();
        FsYamlNode systemRoot = systemYaml.AddFileSystem(".", 0);
        FsYamlNode systemDir = systemRoot.AddDirectory("SYSTEM", isSystem: true);
        systemDir.AddFile("0.GDI", 0x8000, 0x200, 0xCAFE, 0xBABE, isSystem: true);

        NkFs system = NkFs.FromFsYaml(systemYaml);

        // Act
        NkFs result = NkFsMerger.MergeSystemInto(target, system);

        // Assert: only one "system" directory should exist (no duplicate)
        FsYaml resultYaml = result.ToFsYaml();
        FsYamlNode resultRoot = resultYaml.FileSystems[0];

        Assert.NotNull(resultRoot.Children);

        // Count directories with name matching "system" (case-insensitive)
        List<FsYamlNode> systemDirs = resultRoot.Children!
            .Where(c => c.IsDirectory && string.Equals(c.Name, "system", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(systemDirs); // Only one directory, not two

        // The merged directory should contain both original and system files
        FsYamlNode mergedDir = systemDirs[0];
        Assert.NotNull(mergedDir.Children);
        Assert.Equal(2, mergedDir.Children!.Count); // README.TXT + 0.GDI

        // Verify both files are present
        FsYamlNode readmeFile = mergedDir.Children.First(c => c.Name == "README.TXT");
        Assert.False(readmeFile.IsSystem);

        FsYamlNode gdiFile = mergedDir.Children.First(c => c.Name == "0.GDI");
        Assert.True(gdiFile.IsSystem);
    }

    /// <summary>
    /// Mixed case scenario: "System" in target, "sYsTEM" in system — should still merge.
    /// Validates: Requirement 3.4
    /// </summary>
    [Fact]
    public void MergeSystemInto_MixedCaseDirectoryMatch_MergesIntoExisting()
    {
        // Arrange
        FsYaml targetYaml = new FsYaml();
        FsYamlNode targetRoot = targetYaml.AddFileSystem(".", 0);
        FsYamlNode targetDir = targetRoot.AddDirectory("System");
        targetDir.AddFile("original.dat", 0x1000, 0x500, 0x1111, 0x2222);

        NkFs target = NkFs.FromFsYaml(targetYaml);

        FsYaml systemYaml = new FsYaml();
        FsYamlNode systemRoot = systemYaml.AddFileSystem(".", 0);
        FsYamlNode systemDir = systemRoot.AddDirectory("sYsTEM", isSystem: true);
        systemDir.AddFile("merged.dat", 0x2000, 0x300, 0x3333, 0x4444, isSystem: true);

        NkFs system = NkFs.FromFsYaml(systemYaml);

        // Act
        NkFs result = NkFsMerger.MergeSystemInto(target, system);

        // Assert
        FsYaml resultYaml = result.ToFsYaml();
        FsYamlNode resultRoot = resultYaml.FileSystems[0];

        // Should have exactly one directory child
        List<FsYamlNode> dirs = resultRoot.Children!.Where(c => c.IsDirectory).ToList();
        Assert.Single(dirs);

        // Original name is preserved (from target)
        Assert.Equal("System", dirs[0].Name);

        // Both files should be present
        Assert.Equal(2, dirs[0].Children!.Count);
        Assert.Contains(dirs[0].Children!, c => c.Name == "original.dat" && !c.IsSystem);
        Assert.Contains(dirs[0].Children!, c => c.Name == "merged.dat" && c.IsSystem);
    }

    #endregion

    #region Deep Nested Directory Merge

    /// <summary>
    /// Deep nested merge: system has "A/B/C/D/file.bin" and target has "A/B/C/D/existing.txt".
    /// The merge should walk all levels and merge at the leaf directory level.
    /// Validates: Requirements 3.1, 3.4
    /// </summary>
    [Fact]
    public void MergeSystemInto_DeepNestedDirectories_MergesAtAllLevels()
    {
        // Arrange: target has A/B/C/D/existing.txt
        FsYaml targetYaml = new FsYaml();
        FsYamlNode targetRoot = targetYaml.AddFileSystem(".", 0);
        FsYamlNode dirA = targetRoot.AddDirectory("A");
        FsYamlNode dirB = dirA.AddDirectory("B");
        FsYamlNode dirC = dirB.AddDirectory("C");
        FsYamlNode dirD = dirC.AddDirectory("D");
        dirD.AddFile("existing.txt", 0x1000, 0x100, 0x1111, 0x2222);

        NkFs target = NkFs.FromFsYaml(targetYaml);

        // System has A/B/C/D/system.bin
        FsYaml systemYaml = new FsYaml();
        FsYamlNode systemRoot = systemYaml.AddFileSystem(".", 0);
        FsYamlNode sysA = systemRoot.AddDirectory("A", isSystem: true);
        FsYamlNode sysB = sysA.AddDirectory("B", isSystem: true);
        FsYamlNode sysC = sysB.AddDirectory("C", isSystem: true);
        FsYamlNode sysD = sysC.AddDirectory("D", isSystem: true);
        sysD.AddFile("system.bin", 0x5000, 0x800, 0xAAAA, 0xBBBB, isSystem: true);

        NkFs system = NkFs.FromFsYaml(systemYaml);

        // Act
        NkFs result = NkFsMerger.MergeSystemInto(target, system);

        // Assert: Navigate to D directory in result - should have both files
        FsYaml resultYaml = result.ToFsYaml();
        FsYamlNode resultRoot = resultYaml.FileSystems[0];

        // Navigate: root -> A -> B -> C -> D
        FsYamlNode rA = resultRoot.Children!.Single(c => c.Name == "A");
        Assert.True(rA.IsDirectory);
        FsYamlNode rB = rA.Children!.Single(c => c.Name == "B");
        Assert.True(rB.IsDirectory);
        FsYamlNode rC = rB.Children!.Single(c => c.Name == "C");
        Assert.True(rC.IsDirectory);
        FsYamlNode rD = rC.Children!.Single(c => c.Name == "D");
        Assert.True(rD.IsDirectory);

        // D should have both files
        Assert.Equal(2, rD.Children!.Count);
        Assert.Contains(rD.Children, c => c.Name == "existing.txt" && !c.IsSystem);
        Assert.Contains(rD.Children, c => c.Name == "system.bin" && c.IsSystem);

        // Verify system.bin properties
        FsYamlNode sysBin = rD.Children.First(c => c.Name == "system.bin");
        Assert.Equal(0x5000L, sysBin.Offset);
        Assert.Equal(0x800L, sysBin.Size);

        // Verify no duplicate directories at any level (only 1 child per level)
        Assert.Single(resultRoot.Children!); // Only A
        Assert.Single(rA.Children!);         // Only B
        Assert.Single(rB.Children!);         // Only C
        Assert.Single(rC.Children!);         // Only D
    }

    /// <summary>
    /// Deep nested merge where system adds a new branch at an intermediate level.
    /// Target has A/B/file1.txt, system has A/C/file2.txt.
    /// After merge: A has both B and C as children.
    /// Validates: Requirements 3.1, 3.4
    /// </summary>
    [Fact]
    public void MergeSystemInto_DeepNested_NewBranchAtIntermediateLevel()
    {
        // Arrange: target has A/B/file1.txt
        FsYaml targetYaml = new FsYaml();
        FsYamlNode targetRoot = targetYaml.AddFileSystem(".", 0);
        FsYamlNode dirA = targetRoot.AddDirectory("A");
        FsYamlNode dirB = dirA.AddDirectory("B");
        dirB.AddFile("file1.txt", 0x1000, 0x100, 0x1111, 0x2222);

        NkFs target = NkFs.FromFsYaml(targetYaml);

        // System has A/C/file2.txt (new "C" directory under existing "A")
        FsYaml systemYaml = new FsYaml();
        FsYamlNode systemRoot = systemYaml.AddFileSystem(".", 0);
        FsYamlNode sysA = systemRoot.AddDirectory("A", isSystem: true);
        FsYamlNode sysC = sysA.AddDirectory("C", isSystem: true);
        sysC.AddFile("file2.txt", 0x3000, 0x200, 0x3333, 0x4444, isSystem: true);

        NkFs system = NkFs.FromFsYaml(systemYaml);

        // Act
        NkFs result = NkFsMerger.MergeSystemInto(target, system);

        // Assert
        FsYaml resultYaml = result.ToFsYaml();
        FsYamlNode resultRoot = resultYaml.FileSystems[0];

        // Root should have only one child: A (merged)
        Assert.Single(resultRoot.Children!);
        FsYamlNode rA = resultRoot.Children![0];
        Assert.Equal("A", rA.Name);

        // A should have two children: B (original) and C (from system)
        Assert.Equal(2, rA.Children!.Count);

        FsYamlNode rB = rA.Children.First(c => c.Name == "B");
        Assert.True(rB.IsDirectory);
        Assert.Single(rB.Children!);
        Assert.Equal("file1.txt", rB.Children![0].Name);
        Assert.False(rB.Children[0].IsSystem);

        FsYamlNode rC = rA.Children.First(c => c.Name == "C");
        Assert.True(rC.IsDirectory);
        Assert.True(rC.IsSystem); // New directory from system is marked as system
        Assert.Single(rC.Children!);
        Assert.Equal("file2.txt", rC.Children![0].Name);
        Assert.True(rC.Children[0].IsSystem);
    }

    #endregion
}
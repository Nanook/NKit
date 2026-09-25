using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for file association registration idempotence.
///
/// Feature: nkds-settings-panel, Property 1: Registration Idempotence
/// </summary>
public sealed class RegistrationIdempotencePropertyTests : IDisposable
{
    private readonly string _tempDir;

    public RegistrationIdempotencePropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RegIdempotence_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// A testable subclass of LinuxFileAssociationService that redirects all paths
    /// to a temp directory and uses a fixed executable path.
    /// </summary>
    private sealed class TestableLinuxFileAssociationService : LinuxFileAssociationService
    {
        private readonly string _localSharePath;
        private readonly string _exePath;

        public TestableLinuxFileAssociationService(string localSharePath, string exePath)
        {
            _localSharePath = localSharePath;
            _exePath = exePath;
        }

        protected override string GetLocalSharePath() => _localSharePath;

        internal override string GetExecutablePath() => _exePath;

        protected override void RunUpdateDatabases()
        {
            // No-op in tests — don't run system commands
        }
    }

    /// <summary>
    /// All known association entry definitions for generating test inputs.
    /// </summary>
    private static readonly AssociationEntry[] AllEntries =
    [
        AssociationEntries.DirectoryOpen,
        AssociationEntries.DirectoryMount,
        AssociationEntries.FileOpenSet,
        AssociationEntries.FileMountSet,
        AssociationEntries.DoubleClickOpen
    ];

    /// <summary>
    /// **Validates: Requirements 2.7**
    ///
    /// Property 1: Registration Idempotence.
    /// For any association entry that is already in the "Registered" state, calling RegisterAsync
    /// again SHALL leave the OS state unchanged and return success without modifying the existing entry.
    ///
    /// Strategy: Generate a random AssociationEntry + a fake exe path, register it once (simulating
    /// "already registered" state), then register again and verify:
    /// (a) The second call returns success
    /// (b) The .desktop file content is byte-for-byte identical after the second registration
    /// (c) The query state still reports "Registered"
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RegisterAsync_OnAlreadyRegisteredEntry_LeavesStateUnchanged_AndReturnsSuccess(
        NonNegativeInt entrySeed,
        NonNegativeInt pathSeed)
    {
        // Pick a random entry from the known set
        AssociationEntry entry = AllEntries[entrySeed.Get % AllEntries.Length];

        // Generate a random-ish executable path
        string exePath = GenerateExePath(pathSeed.Get);

        // Create isolated temp directory for this test iteration
        string iterDir = Path.Combine(_tempDir, $"iter_{entrySeed.Get}_{pathSeed.Get}");
        string localSharePath = Path.Combine(iterDir, ".local", "share");
        Directory.CreateDirectory(localSharePath);

        TestableLinuxFileAssociationService service = new TestableLinuxFileAssociationService(localSharePath, exePath);

        // First registration — establishes the "already registered" state
        AssociationOperationResult firstResult = service.RegisterAsync(entry).GetAwaiter().GetResult();
        if (!firstResult.Success)
            return false;

        // Capture the file content after first registration
        string desktopFilePath = GetExpectedDesktopFilePath(localSharePath, entry);
        if (!File.Exists(desktopFilePath))
            return false;

        string contentAfterFirst = File.ReadAllText(desktopFilePath);
        DateTime lastWriteAfterFirst = File.GetLastWriteTimeUtc(desktopFilePath);

        // Small delay to ensure filesystem timestamp would differ if file is rewritten
        // (not strictly needed since we compare content, but adds confidence)

        // Second registration — should be idempotent
        AssociationOperationResult secondResult = service.RegisterAsync(entry).GetAwaiter().GetResult();

        // (a) Second call returns success
        if (!secondResult.Success)
            return false;

        // (b) File content is identical after second registration
        string contentAfterSecond = File.ReadAllText(desktopFilePath);
        if (!string.Equals(contentAfterFirst, contentAfterSecond, StringComparison.Ordinal))
            return false;

        // (c) Query state still reports "Registered"
        AssociationQueryResult queryResult = service.QueryStateAsync(entry).GetAwaiter().GetResult();
        if (queryResult.State != AssociationState.Registered)
            return false;

        return true;
    }

    /// <summary>
    /// Generates a plausible Unix-style executable path from a seed.
    /// </summary>
    private static string GenerateExePath(int seed)
    {
        string[] prefixes = new[]
        {
            "/usr/bin/nkds-ui",
            "/opt/nkds/bin/nkds-ui",
            "/home/user/apps/nkds-ui",
            "/usr/local/bin/nkds-ui",
            "/snap/nkds-ui/current/bin/nkds-ui",
            "/app/bin/nkds-ui",
            "/home/testuser/.local/bin/nkds-ui"
        };

        return prefixes[seed % prefixes.Length];
    }

    /// <summary>
    /// Computes the expected .desktop file path for a given entry (mirrors the service logic).
    /// </summary>
    private static string GetExpectedDesktopFilePath(string localSharePath, AssociationEntry entry)
    {
        return entry.Id switch
        {
            "dir-open" => Path.Combine(localSharePath, "kio", "servicemenus", "nkds-open.desktop"),
            "dir-mount" => Path.Combine(localSharePath, "kio", "servicemenus", "nkds-mount.desktop"),
            "file-open-set" => Path.Combine(localSharePath, "applications", "nkds-ui-open-set.desktop"),
            "file-mount-set" => Path.Combine(localSharePath, "applications", "nkds-ui-mount-set.desktop"),
            "dblclick-open" => Path.Combine(localSharePath, "applications", "nkds-ui-dblclick-open.desktop"),
            _ => throw new ArgumentException($"Unknown entry ID: {entry.Id}")
        };
    }
}
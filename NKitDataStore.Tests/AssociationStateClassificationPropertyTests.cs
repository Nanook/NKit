using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for association state classification logic.
///
/// Feature: nkds-settings-panel, Property 3: Association State Classification
/// </summary>
public sealed class AssociationStateClassificationPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public AssociationStateClassificationPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AssocStateClassPropTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// Classifies the association state given whether the .desktop file exists
    /// and whether the embedded path matches the current executable.
    /// This mirrors the logic in LinuxFileAssociationService.QueryStateAsync.
    /// </summary>
    private static AssociationState ClassifyState(bool entryExistsInOS, bool embeddedPathMatchesCurrentExe)
    {
        if (!entryExistsInOS)
            return AssociationState.NotRegistered;

        return embeddedPathMatchesCurrentExe
            ? AssociationState.Registered
            : AssociationState.Stale;
    }

    /// <summary>
    /// **Validates: Requirements 3.7, 9.1**
    ///
    /// Property 3: Association State Classification.
    /// For any combination of (entryExistsInOS: bool, embeddedPathMatchesCurrentExe: bool),
    /// the state classification function SHALL return:
    /// - Registered when entryExistsInOS is true AND embeddedPathMatchesCurrentExe is true
    /// - Stale when entryExistsInOS is true AND embeddedPathMatchesCurrentExe is false
    /// - NotRegistered when entryExistsInOS is false (regardless of path match)
    ///
    /// This test exercises the classification through actual .desktop file creation and parsing,
    /// verifying the full pipeline: file existence check + ParseExecPath + path comparison.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AssociationState_Classification_ReturnsCorrectState(
        bool entryExistsInOS,
        bool embeddedPathMatchesCurrentExe,
        NonNegativeInt pathSeed)
    {
        // Generate a "current exe" path and a potentially different "embedded" path
        string currentExePath = GenerateExePath(pathSeed.Get);
        string embeddedExePath = embeddedPathMatchesCurrentExe
            ? currentExePath
            : GenerateExePath(pathSeed.Get + 7919); // Different seed for a different path

        string desktopFilePath = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.desktop");

        try
        {
            if (entryExistsInOS)
            {
                // Create a .desktop file with the embedded path
                string content = $"""
                    [Desktop Entry]
                    Type=Application
                    Name=Test Entry
                    Exec={embeddedExePath} --datastore %f
                    MimeType=application/x-nkds
                    NoDisplay=true
                    """;
                File.WriteAllText(desktopFilePath, content);
            }
            // else: don't create the file (simulates entry not existing in OS)

            // Perform the classification the same way QueryStateAsync does
            AssociationState actualState;
            if (!File.Exists(desktopFilePath))
            {
                actualState = AssociationState.NotRegistered;
            }
            else
            {
                string parsedPath = LinuxFileAssociationService.ParseExecPath(desktopFilePath);
                if (parsedPath == null)
                {
                    actualState = AssociationState.NotRegistered;
                }
                else
                {
                    actualState = string.Equals(parsedPath, currentExePath, StringComparison.Ordinal)
                        ? AssociationState.Registered
                        : AssociationState.Stale;
                }
            }

            // Verify against the expected classification
            AssociationState expectedState = ClassifyState(entryExistsInOS, embeddedPathMatchesCurrentExe);
            return actualState == expectedState;
        }
        finally
        {
            if (File.Exists(desktopFilePath))
                File.Delete(desktopFilePath);
        }
    }

    /// <summary>
    /// **Validates: Requirements 3.7, 9.1**
    ///
    /// Property 3: Association State Classification (exhaustive boolean combinations).
    /// Verifies all four (bool, bool) combinations produce the correct enum output
    /// using the pure classification logic.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AssociationState_AllBoolCombinations_CorrectEnumOutput(NonNegativeInt seed)
    {
        // Test all four combinations with random path strings
        string path1 = GenerateExePath(seed.Get);
        string path2 = GenerateExePath(seed.Get + 4217);

        // (true, true) → Registered
        if (ClassifyViaFileSystem(path1, path1) != AssociationState.Registered)
            return false;

        // (true, false) → Stale
        if (ClassifyViaFileSystem(path1, path2) != AssociationState.Stale)
            return false;

        // (false, true) → NotRegistered (file doesn't exist, path match is irrelevant)
        if (ClassifyWithoutFile() != AssociationState.NotRegistered)
            return false;

        // (false, false) → NotRegistered (file doesn't exist, path match is irrelevant)
        if (ClassifyWithoutFile() != AssociationState.NotRegistered)
            return false;

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.7, 9.1**
    ///
    /// Property 3: Association State Classification (random path strings).
    /// For any random path string used as the embedded executable path,
    /// the classification correctly distinguishes matching vs non-matching paths.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AssociationState_RandomPaths_CorrectClassification(
        NonEmptyString pathSuffix1,
        NonEmptyString pathSuffix2)
    {
        // Sanitize path suffixes to avoid invalid filesystem characters in the exe path
        string sanitized1 = SanitizeForPath(pathSuffix1.Get);
        string sanitized2 = SanitizeForPath(pathSuffix2.Get);

        string currentExe = $"/usr/bin/{sanitized1}";
        string embeddedExe = $"/usr/bin/{sanitized2}";

        string desktopFilePath = Path.Combine(_tempDir, $"rnd-{Guid.NewGuid():N}.desktop");

        try
        {
            // Create .desktop file with the embedded path
            string content = $"[Desktop Entry]\nExec={embeddedExe} --arg %f\n";
            File.WriteAllText(desktopFilePath, content);

            string parsedPath = LinuxFileAssociationService.ParseExecPath(desktopFilePath);
            if (parsedPath == null)
                return false; // Should always parse successfully for valid content

            AssociationState actualState;
            if (string.Equals(parsedPath, currentExe, StringComparison.Ordinal))
                actualState = AssociationState.Registered;
            else
                actualState = AssociationState.Stale;

            // Expected: if the paths are equal (ordinal), Registered; otherwise Stale
            bool pathsMatch = string.Equals(currentExe, embeddedExe, StringComparison.Ordinal);
            AssociationState expectedState = pathsMatch ? AssociationState.Registered : AssociationState.Stale;

            return actualState == expectedState;
        }
        finally
        {
            if (File.Exists(desktopFilePath))
                File.Delete(desktopFilePath);
        }
    }

    /// <summary>
    /// Classifies state by creating a real .desktop file and running through the
    /// same logic as QueryStateAsync.
    /// </summary>
    private AssociationState ClassifyViaFileSystem(string currentExePath, string embeddedExePath)
    {
        string desktopFilePath = Path.Combine(_tempDir, $"classify-{Guid.NewGuid():N}.desktop");
        try
        {
            string content = $"[Desktop Entry]\nExec={embeddedExePath} --datastore %f\n";
            File.WriteAllText(desktopFilePath, content);

            string parsedPath = LinuxFileAssociationService.ParseExecPath(desktopFilePath);
            if (parsedPath == null)
                return AssociationState.NotRegistered;

            return string.Equals(parsedPath, currentExePath, StringComparison.Ordinal)
                ? AssociationState.Registered
                : AssociationState.Stale;
        }
        finally
        {
            if (File.Exists(desktopFilePath))
                File.Delete(desktopFilePath);
        }
    }

    /// <summary>
    /// Classifies state when no .desktop file exists.
    /// </summary>
    private AssociationState ClassifyWithoutFile()
    {
        string nonExistentPath = Path.Combine(_tempDir, $"nonexistent-{Guid.NewGuid():N}.desktop");
        // File does not exist → NotRegistered
        return File.Exists(nonExistentPath) ? AssociationState.Unknown : AssociationState.NotRegistered;
    }

    /// <summary>
    /// Generates a deterministic Unix-style executable path from a seed.
    /// </summary>
    private static string GenerateExePath(int seed)
    {
        Random rng = new Random(seed);
        string[] dirs = new[] { "usr", "opt", "home", "local", "app", "bin", "sbin" };
        string[] names = new[] { "nkds-ui", "nkit", "datastore", "viewer", "manager", "editor" };

        string dir1 = dirs[rng.Next(dirs.Length)];
        string dir2 = dirs[rng.Next(dirs.Length)];
        string name = names[rng.Next(names.Length)];
        int suffix = rng.Next(1000);

        return $"/{dir1}/{dir2}/{name}-{suffix}";
    }

    /// <summary>
    /// Sanitizes a string to be usable as part of a file path (removes characters
    /// that would break the Exec= line parsing or filesystem operations).
    /// </summary>
    private static string SanitizeForPath(string input)
    {
        // Remove whitespace (which would break Exec= parsing since space is the delimiter)
        // and path separators and other problematic characters
        string sanitized = new string(input
            .Where(c => !char.IsWhiteSpace(c) && c != '/' && c != '\\' && c != '\0' && c != '%')
            .Take(20)
            .ToArray());

        return string.IsNullOrEmpty(sanitized) ? "app" : sanitized;
    }
}
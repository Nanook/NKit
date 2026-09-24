using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for Linux .desktop file generation.
///
/// Feature: nkds-settings-panel, Property 2: Desktop File Path Embedding
/// </summary>
public sealed class DesktopFilePathEmbeddingPropertyTests
{
    /// <summary>
    /// All defined association entries to test against.
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
    /// Generates valid Unix absolute paths with varying depth and segment names.
    /// Paths start with '/' and contain alphanumeric segments, dashes, underscores, and dots.
    /// </summary>
    private static Gen<string> GenValidUnixPath()
    {
        // Valid characters for path segments (alphanumeric + common special chars in filenames)
        Gen<char> segmentCharGen = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
            'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
            'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
            '-', '_', '.');

        // Generate 1-8 path segments, each 1-20 chars
        return from depth in Gen.Choose(1, 8)
               from segments in Gen.ArrayOf(
                   from len in Gen.Choose(1, 20)
                   from chars in segmentCharGen.ArrayOf(len)
                   select new string(chars),
                   depth)
               select "/" + string.Join("/", segments);
    }

    /// <summary>
    /// **Validates: Requirements 3.4**
    ///
    /// Property 2: Desktop File Path Embedding.
    /// For any valid absolute executable path, generating a .desktop file for any association entry
    /// SHALL produce file content where the Exec= line contains that exact executable path
    /// followed by the correct argument template.
    ///
    /// Generator Strategy: Generate random valid Unix paths (varying length, special chars);
    /// verify Exec= line correctness.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DesktopFileContent_ExecLine_ContainsExactPath()
    {
        Gen<(string exePath, int entryIndex)> gen = from exePath in GenValidUnixPath()
                                                    from entryIndex in Gen.Choose(0, AllEntries.Length - 1)
                                                    select (exePath, entryIndex);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            (string exePath, int entryIndex) = tuple;
            AssociationEntry entry = AllEntries[entryIndex];

            // Act: generate the .desktop file content
            string content = LinuxFileAssociationService.GenerateDesktopFileContent(entry, exePath);

            // Find the Exec= line (trim \r for Windows line endings)
            string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string execLine = lines
                .Select(l => l.TrimEnd('\r'))
                .FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));

            // Assert: Exec= line exists
            if (execLine == null)
                return false;

            // Extract the value after "Exec="
            string execValue = execLine.Substring("Exec=".Length);

            // Assert: the Exec value starts with the exact executable path
            if (!execValue.StartsWith(exePath, StringComparison.Ordinal))
                return false;

            // Assert: after the path, there is a space followed by the argument template
            // The argument template has "{0}" replaced with %f (without quotes)
            string expectedArgs = entry.CommandArgTemplate.Replace("\"{0}\"", "%f", StringComparison.Ordinal);
            string expectedExecValue = $"{exePath} {expectedArgs}";

            return execValue == expectedExecValue;
        });
    }
}
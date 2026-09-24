using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NkdsUi.Models;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for FormatMappings.GetTargetFormats — correct target formats for all defined pairs.
///
/// Feature: full-format-processing-parity
/// Property 1: FormatMappings Returns Correct Target Formats for All Defined Pairs
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 7.10, 7.11, 7.12, 7.13, 7.14, 7.15, 7.16, 7.17, 7.18, 7.19**
/// </summary>
public class FormatMappingsTargetFormatsPropertyTests
{
    /// <summary>
    /// All defined (system, sourceFormat) → expected target formats pairs from the specification.
    /// </summary>
    private static readonly (string System, string SourceFormat, string[] Expected)[] DefinedPairs =
    [
        ("gamecube", "iso", new[] { "iso", "rvz", "ciso", "wbfs" }),
        ("wii", "iso", new[] { "iso", "rvz", "ciso", "wbfs" }),
        ("wiiu", "iso", new[] { "iso", "wux", "app" }),
        ("wiiu", "app", new[] { "app" }),
        ("directories", "folder", new[] { "dir" }),
        ("xbox", "iso", new[] { "xiso", "iso" }),
        ("xbox360", "iso", new[] { "xiso", "iso" }),
        ("ps1", "iso", new[] { "iso", "cue" }),
        ("ps2", "iso", new[] { "iso", "cue" }),
        ("saturn", "iso", new[] { "iso", "cue" }),
        ("segacd", "iso", new[] { "iso", "cue" }),
        ("psp", "iso", new[] { "iso", "cue" }),
        ("cdi", "iso", new[] { "iso", "cue" }),
        ("pcengine", "iso", new[] { "iso", "cue" }),
        ("default", "iso", new[] { "iso", "cue" }),
        ("default", "cue", new[] { "cue" }),
        ("dreamcast", "cue", new[] { "cue", "gdi" }),
        ("ps1", "cue", new[] { "cue" }),
        ("ps2", "cue", new[] { "cue" }),
        ("saturn", "cue", new[] { "cue" }),
        ("segacd", "cue", new[] { "cue" }),
        ("cdi", "cue", new[] { "cue" }),
        ("pcengine", "cue", new[] { "cue" }),
        ("dreamcast", "gdi", new[] { "gdi", "cue" }),
    ];

    /// <summary>
    /// Generates a random casing variant of a string.
    /// For each character, randomly chooses upper or lower case.
    /// </summary>
    private static Gen<string> GenCaseVariant(string input)
    {
        return Gen.Elements(new[] { 0, 1 })
            .ArrayOf(input.Length)
            .Select(choices =>
            {
                char[] chars = new char[input.Length];
                for (int i = 0; i < input.Length; i++)
                {
                    chars[i] = choices[i] == 0
                        ? char.ToUpperInvariant(input[i])
                        : char.ToLowerInvariant(input[i]);
                }
                return new string(chars);
            });
    }

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 7.10, 7.11, 7.12, 7.13, 7.14, 7.15, 7.16, 7.17, 7.18, 7.19**
    ///
    /// Property 1: FormatMappings Returns Correct Target Formats for All Defined Pairs.
    /// For any (system, sourceFormat) pair from the defined mapping set, calling
    /// GetTargetFormats returns the exact expected list regardless of input casing
    /// (e.g., "Xbox", "XBOX", "xbox" all produce same result).
    /// </summary>
    [Property(MaxTest = 200)]
    public Property GetTargetFormats_ReturnsCorrectFormats_ForAllDefinedPairs_RegardlessOfCasing()
    {
        // Generate a random defined pair with random casing applied to system and sourceFormat
        Gen<(string CasedSystem, string CasedFormat, string[] Expected)> gen = Gen.Elements(DefinedPairs)
            .SelectMany(pair =>
                GenCaseVariant(pair.System).SelectMany(casedSystem =>
                    GenCaseVariant(pair.SourceFormat).Select(casedFormat =>
                        (CasedSystem: casedSystem, CasedFormat: casedFormat, pair.Expected))));

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            IReadOnlyList<string> result = FormatMappings.GetTargetFormats(testCase.CasedSystem, testCase.CasedFormat);

            // Verify exact match: same count and same elements in same order
            if (result.Count != testCase.Expected.Length)
                return false;

            for (int i = 0; i < result.Count; i++)
            {
                if (result[i] != testCase.Expected[i])
                    return false;
            }

            return true;
        });
    }
}
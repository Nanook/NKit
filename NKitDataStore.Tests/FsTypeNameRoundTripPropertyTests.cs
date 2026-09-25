using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for filesystem type name round-trip through filename parsing.
///
/// Feature: multi-filesystem-nkfs, Property 8: Filesystem type name round-trips through filename
/// </summary>
public class FsTypeNameRoundTripPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 8.1, 8.3**
    ///
    /// Property 8: Filesystem type name round-trips through filename.
    /// For any valid filesystem type name (lowercase, non-extension), constructing the filename
    /// "filesystem.{type}.nkfs" and then extracting the type via TryExtractFsTypeName
    /// SHALL produce the original type name.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property FsTypeName_RoundTrips_ThroughFilename()
    {
        // Generate random valid type names: non-empty lowercase alphabetic strings (no dots),
        // excluding extension filesystem types that should never appear as standalone filenames.
        Gen<string> typeNameGen = Gen.Choose(1, 20).SelectMany(len =>
            Gen.Elements(Enumerable.Range('a', 26).Select(c => (char)c).ToArray())
               .ArrayOf(len)
               .Select(chars => new string(chars)))
            .Where(name => name != "rockridge" && name != "cdxa");

        return Prop.ForAll(typeNameGen.ToArbitrary(), typeName =>
        {
            // Construct the per-type nkfs filename
            string fileName = $"filesystem.{typeName}.nkfs";

            // Extract via TryExtractFsTypeName
            bool extracted = DataStore.TryExtractFsTypeName(fileName, out string extractedName);

            // Verify round-trip: extraction succeeds and recovers the original name
            return extracted && extractedName == typeName;
        });
    }
}
using Nanook.NKit;
using Xunit;

namespace NKit.Tests.Settings
{
    /// <summary>
    /// Verifies AppSettings.NormalizeVersion drops the '+commitHash' build-metadata element and
    /// normalizes the numeric core to "major.minor.revision", while PRESERVING any '-prerelease'
    /// suffix (so an alpha/beta/rc build stays visibly distinct from a clean release).
    /// </summary>
    [Trait("Area", "Settings")]
    public class VersionNormalizeTests
    {
        [Theory]
        [InlineData("2.1.0", "2.1.0")]
        [InlineData("2.1.0+29f42543", "2.1.0")]                 // SDK-appended commit hash stripped
        [InlineData("2.1.0-alpha", "2.1.0-alpha")]              // pre-release suffix preserved
        [InlineData("3.0.0-alpha.1", "3.0.0-alpha.1")]          // dotted pre-release preserved
        [InlineData("2.1.0-rc.1+abcdef0", "2.1.0-rc.1")]        // metadata dropped, prerelease kept
        [InlineData("0.0.0-local", "0.0.0-local")]              // local dev default (prerelease kept)
        [InlineData("2.1", "2.1.0")]                            // missing revision → padded
        [InlineData("2", "2.0.0")]                              // major only → padded
        [InlineData("2-beta", "2.0.0-beta")]                    // padded core + prerelease
        [InlineData("", "0.0.0")]                               // empty → default
        [InlineData(null, "0.0.0")]                             // null → default
        public void NormalizeVersion_StripsMetadata_KeepsPrerelease(string raw, string expected) => Assert.Equal(expected, AppSettings.NormalizeVersion(raw));
    }
}
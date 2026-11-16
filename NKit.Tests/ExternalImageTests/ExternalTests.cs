using Xunit;


namespace NKit.Tests.Full.External
{

    [Trait("Area", "Full")]
    [Trait("Group", "External")]
    public class ExternalTests : ProcessingTestBase
    {
        // Input files are on a read-only mount in Docker - redirect test output to a writable temp dir.
        // NKIT_TEST_OUTPUT overrides the location (set it in Docker CI); on a local dev machine the
        // test binary's working directory is used, matching the wiped-image tests pattern.
        private static readonly string _outputPath =
            System.Environment.GetEnvironmentVariable("NKIT_TEST_OUTPUT")
            ?? System.Environment.CurrentDirectory;

        public static TheoryData<ProcessingTestItem> SettingsData => CreateData(
            "../../../../../NKitExternalTestFiles/Tests/ExternalTests.txt",
            _outputPath); //files must be 1 level below working folder

        [Theory]
        [MemberData(nameof(SettingsData))]
        public override void Tests(ProcessingTestItem Name) => base.Tests(Name); //called Name to Pretify for VS test window
    }
}
using Xunit;


namespace NKit.Tests.Full.TextDriven
{

    [Trait("Area", "Full")]
    [Trait("Group", "TextDriven")]
    public class WipedFullTests : ProcessingTestBase
    {
        public static TheoryData<ProcessingTestItem> SettingsData => CreateData("ProcessingTests/WipedFullTestsData.txt"); //files must be 1 level below working folder

        [Theory]
        [MemberData(nameof(SettingsData))]
        public override void Tests(ProcessingTestItem Name) => base.Tests(Name); //called Name to Pretify for VS test window
    }
}
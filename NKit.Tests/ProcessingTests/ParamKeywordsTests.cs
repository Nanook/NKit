using Xunit;


namespace NKit.Tests.Full.TextDriven
{

    [Trait("Area", "Full")]
    [Trait("Group", "TextDriven")]
    public class ParamKeywordsTests : ProcessingTestBase
    {
        public static TheoryData<ProcessingTestItem> SettingsData => CreateData("ProcessingTests/ParamKeywordsTestsData.txt");

        [Theory]
        [MemberData(nameof(SettingsData))]
        public override void Tests(ProcessingTestItem Name) => base.Tests(Name); //called Name to Pretify for VS test window
    }
}
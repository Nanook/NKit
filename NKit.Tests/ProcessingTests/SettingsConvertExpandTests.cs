using Xunit;


namespace NKit.Tests.Full.TextDriven
{

    [Trait("Area", "Full")]
    [Trait("Group", "TextDriven")]
    public class SettingsConvertExpandTests : ProcessingTestBase
    {
        public static TheoryData<ProcessingTestItem> SettingsData => CreateData("ProcessingTests/SettingsConvertExpandTestsData.txt");

        [Theory]
        [MemberData(nameof(SettingsData))]
        public override void Tests(ProcessingTestItem Name) => base.Tests(Name); //called Name to Pretify for VS test window
    }
}
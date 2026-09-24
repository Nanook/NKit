using Xunit;


namespace NKit.Tests.Full.TextDriven
{

    [Trait("Area", "Full")]
    [Trait("Group", "TextDriven")]
    public class ConvertFormatsTests : ProcessingTestBase
    {
        public static TheoryData<ProcessingTestItem> SettingsData => CreateData("ProcessingTests/ConvertFormatsTestsData.txt");

        [Theory]
        [MemberData(nameof(SettingsData))]
        public override void Tests(ProcessingTestItem Name) => base.Tests(Name);
    }
}
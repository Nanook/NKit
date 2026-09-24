using Xunit;


namespace NKit.Tests.Full.TextDriven
{

    [Trait("Area", "Full")]
    [Trait("Group", "TextDriven")]
    public class ProcessingTestBaseTests
    {
        [Fact]
        public void LoadFileTest() //called Name to Pretify for VS test window
        {
            TheoryData<ProcessingTestItem> data = SettingsScanTests.SettingsData;
            SettingsScanTests tst = new SettingsScanTests();
            foreach (TheoryDataRow<ProcessingTestItem> row in data)
            {
                tst.Tests(row.Data);
            }
        }
    }
}
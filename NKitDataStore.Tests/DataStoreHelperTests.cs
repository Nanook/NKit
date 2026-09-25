namespace NKitDataStore.Tests
{
    public class DataStoreHelperTests
    {
        #region ExtractBaseName Tests

        [Theory]
        [InlineData("Game [tmd.0]", "Game")]
        [InlineData("Game [tmd.12]", "Game")]
        [InlineData("Game [Special] [tmd.0]", "Game [Special]")]
        public void ExtractBaseName_ValidPatterns_ReturnsBaseName(string input, string expected)
        {
            string result = DataStore.ExtractBaseName(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Game [tmd.]")]
        [InlineData("Game [tmd.abc]")]
        [InlineData("Game [other]")]
        [InlineData("Game")]
        [InlineData("")]
        public void ExtractBaseName_InvalidPatterns_ReturnsNull(string input)
        {
            string result = DataStore.ExtractBaseName(input);
            Assert.Null(result);
        }

        #endregion

        #region IsTmdDisambiguatedName Tests

        [Theory]
        [InlineData("Game [tmd.0]")]
        [InlineData("Game [tmd.12]")]
        [InlineData("Game [Special] [tmd.0]")]
        public void IsTmdDisambiguatedName_ValidPatterns_ReturnsTrue(string input) => Assert.True(DataStore.IsTmdDisambiguatedName(input));

        [Theory]
        [InlineData("Game [tmd.]")]
        [InlineData("Game [tmd.abc]")]
        [InlineData("Game [other]")]
        [InlineData("Game")]
        [InlineData("")]
        public void IsTmdDisambiguatedName_InvalidPatterns_ReturnsFalse(string input) => Assert.False(DataStore.IsTmdDisambiguatedName(input));

        #endregion
    }
}
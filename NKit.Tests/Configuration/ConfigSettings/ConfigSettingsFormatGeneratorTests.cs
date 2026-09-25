using Nanook.NKit.Configuration;
using Xunit;


namespace NKit.Tests.Configuration.ConfigSettings
{
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigSettings")]
    public class ConfigSettingsFormatGeneratorTests
    {
        [Fact]
        public void GenerateWbfsFormatString_WithDefaultLossless_ReturnsWbfsY()
        {
            // Act
            string result = ConfigSettingsFormatGenerator.GenerateWbfsFormatString();

            // Assert
            Assert.Equal("wbfs:y", result);
        }

        [Fact]
        public void GenerateWbfsFormatString_WithLosslessTrue_ReturnsWbfsY()
        {
            // Act
            string result = ConfigSettingsFormatGenerator.GenerateWbfsFormatString(true);

            // Assert
            Assert.Equal("wbfs:y", result);
        }

        [Fact]
        public void GenerateWbfsFormatString_WithLosslessFalse_ReturnsWbfsN()
        {
            // Act
            string result = ConfigSettingsFormatGenerator.GenerateWbfsFormatString(false);

            // Assert
            Assert.Equal("wbfs:n", result);
        }

        [Fact]
        public void GenerateCisoFormatString_WithDefaultLossless_ReturnsCisoY()
        {
            // Act
            string result = ConfigSettingsFormatGenerator.GenerateCisoFormatString();

            // Assert
            Assert.Equal("ciso:y", result);
        }

        [Fact]
        public void GenerateCisoFormatString_WithLosslessTrue_ReturnsCisoY()
        {
            // Act
            string result = ConfigSettingsFormatGenerator.GenerateCisoFormatString(true);

            // Assert
            Assert.Equal("ciso:y", result);
        }

        [Fact]
        public void GenerateCisoFormatString_WithLosslessFalse_ReturnsCisoN()
        {
            // Act
            string result = ConfigSettingsFormatGenerator.GenerateCisoFormatString(false);

            // Assert
            Assert.Equal("ciso:n", result);
        }

        [Theory]
        [InlineData("wbfs:y", true)]
        [InlineData("wbfs:n", true)]
        [InlineData("wbfs", true)]
        [InlineData("wbfs:invalid", false)]
        [InlineData("", false)]
        public void ValidateWbfsFormat_ReturnsExpectedResult(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateWbfsFormat(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory]
        [InlineData("ciso:y", true)]
        [InlineData("ciso:n", true)]
        [InlineData("ciso", true)]
        [InlineData("ciso:invalid", false)]
        [InlineData("", false)]
        public void ValidateCisoFormat_ReturnsExpectedResult(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCisoFormat(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        #region WBFS Parser Tests

        [Fact]
        public void ParseWbfsFormat_WithDefaultFormat_ReturnsLosslessTrue()
        {
            // Arrange
            string formatString = "wbfs";

            // Act
            WbfsFormatConfiguration result = ConfigSettingsFormatParser.ParseWbfsFormat(formatString);

            // Assert
            Assert.True(result.Lossless);
        }

        [Fact]
        public void ParseWbfsFormat_WithLosslessY_ReturnsLosslessTrue()
        {
            // Arrange
            string formatString = "wbfs:y";

            // Act
            WbfsFormatConfiguration result = ConfigSettingsFormatParser.ParseWbfsFormat(formatString);

            // Assert
            Assert.True(result.Lossless);
        }

        [Fact]
        public void ParseWbfsFormat_WithLosslessN_ReturnsLosslessFalse()
        {
            // Arrange
            string formatString = "wbfs:n";

            // Act
            WbfsFormatConfiguration result = ConfigSettingsFormatParser.ParseWbfsFormat(formatString);

            // Assert
            Assert.False(result.Lossless);
        }

        [Theory]
        [InlineData("wbfs:true", false)]
        [InlineData("wbfs:false", false)]
        [InlineData("wbfs:Y", true)]  // Y -> y (true)
        [InlineData("wbfs:N", false)]  // N -> n (false)
        [InlineData("wbfs:invalid", false)]  // Invalid, returns false
        public void ParseWbfsFormat_WithVariousValues_ReturnsExpectedLossless(string formatString, bool expectedLossless)
        {
            // Act
            WbfsFormatConfiguration result = ConfigSettingsFormatParser.ParseWbfsFormat(formatString);

            // Assert
            Assert.Equal(expectedLossless, result.Lossless);
        }

        #endregion

        #region CISO Parser Tests

        [Fact]
        public void ParseCisoFormat_WithDefaultFormat_ReturnsLosslessTrue()
        {
            // Arrange
            string formatString = "ciso";

            // Act
            CisoFormatConfiguration result = ConfigSettingsFormatParser.ParseCisoFormat(formatString);

            // Assert
            Assert.True(result.Lossless);
        }

        [Fact]
        public void ParseCisoFormat_WithLosslessY_ReturnsLosslessTrue()
        {
            // Arrange
            string formatString = "ciso:y";

            // Act
            CisoFormatConfiguration result = ConfigSettingsFormatParser.ParseCisoFormat(formatString);

            // Assert
            Assert.True(result.Lossless);
        }

        [Fact]
        public void ParseCisoFormat_WithLosslessN_ReturnsLosslessFalse()
        {
            // Arrange
            string formatString = "ciso:n";

            // Act
            CisoFormatConfiguration result = ConfigSettingsFormatParser.ParseCisoFormat(formatString);

            // Assert
            Assert.False(result.Lossless);
        }

        [Theory]
        [InlineData("ciso:true", false)]
        [InlineData("ciso:false", false)]
        [InlineData("ciso:Y", true)]  // Y -> y (true)
        [InlineData("ciso:N", false)]  // N -> n (false)
        [InlineData("ciso:invalid", false)]  // Invalid, returns false
        public void ParseCisoFormat_WithVariousValues_ReturnsExpectedLossless(string formatString, bool expectedLossless)
        {
            // Act
            CisoFormatConfiguration result = ConfigSettingsFormatParser.ParseCisoFormat(formatString);

            // Assert
            Assert.Equal(expectedLossless, result.Lossless);
        }

        #endregion

        #region Generator-Parser Integration Tests

        [Fact]
        public void WbfsGeneratorParser_RoundTrip_PreservesLosslessTrue()
        {
            // Arrange
            string generated = ConfigSettingsFormatGenerator.GenerateWbfsFormatString(true);

            // Act
            WbfsFormatConfiguration parsed = ConfigSettingsFormatParser.ParseWbfsFormat(generated);

            // Assert
            Assert.True(parsed.Lossless);
        }

        [Fact]
        public void WbfsGeneratorParser_RoundTrip_PreservesLosslessFalse()
        {
            // Arrange
            string generated = ConfigSettingsFormatGenerator.GenerateWbfsFormatString(false);

            // Act
            WbfsFormatConfiguration parsed = ConfigSettingsFormatParser.ParseWbfsFormat(generated);

            // Assert
            Assert.False(parsed.Lossless);
        }

        [Fact]
        public void CisoGeneratorParser_RoundTrip_PreservesLosslessTrue()
        {
            // Arrange
            string generated = ConfigSettingsFormatGenerator.GenerateCisoFormatString(true);

            // Act
            CisoFormatConfiguration parsed = ConfigSettingsFormatParser.ParseCisoFormat(generated);

            // Assert
            Assert.True(parsed.Lossless);
        }

        [Fact]
        public void CisoGeneratorParser_RoundTrip_PreservesLosslessFalse()
        {
            // Arrange
            string generated = ConfigSettingsFormatGenerator.GenerateCisoFormatString(false);

            // Act
            CisoFormatConfiguration parsed = ConfigSettingsFormatParser.ParseCisoFormat(generated);

            // Assert
            Assert.False(parsed.Lossless);
        }

        #endregion
    }
}
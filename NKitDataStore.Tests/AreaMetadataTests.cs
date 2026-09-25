namespace NKitDataStore.Tests
{
    public class AreaMetadataTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_Empty_CreatesEmptyMetadata()
        {
            // Act
            AreaMetadata metadata = new AreaMetadata();

            // Assert
            Assert.Empty(metadata);
        }

        [Fact]
        public void Constructor_WithDictionary_CopiesValues()
        {
            // Arrange
            Dictionary<AreaValueType, string> dict = new Dictionary<AreaValueType, string>
            {
                [AreaValueType.Title] = "Test Title",
                [AreaValueType.Region] = "USA"
            };

            // Act
            AreaMetadata metadata = new AreaMetadata(dict);

            // Assert
            Assert.Equal(2, metadata.Count);
            Assert.Equal("Test Title", metadata[AreaValueType.Title]);
            Assert.Equal("USA", metadata[AreaValueType.Region]);
        }

        #endregion

        #region Set and Get Tests

        [Fact]
        public void Set_String_StoresValue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.Title, "My Title");

            // Assert
            Assert.Equal("My Title", metadata[AreaValueType.Title]);
        }

        [Fact]
        public void Set_Long_StoresAsString()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.PvdSectorCount, 12345L);

            // Assert
            Assert.Equal("12345", metadata[AreaValueType.PvdSectorCount]);
            Assert.Equal(12345L, metadata.GetLong(AreaValueType.PvdSectorCount));
        }

        [Fact]
        public void Set_Bool_StoresAsString()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.Encrypted, true);
            metadata.Set(AreaValueType.Signed, false);

            // Assert
            Assert.Equal("true", metadata[AreaValueType.Encrypted]);
            Assert.Equal("false", metadata[AreaValueType.Signed]);
            Assert.Equal(true, metadata.GetBool(AreaValueType.Encrypted));
            Assert.Equal(false, metadata.GetBool(AreaValueType.Signed));
        }

        [Fact]
        public void Set_NullString_ThrowsArgumentNullException()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => metadata.Set(AreaValueType.Title, null!));
        }

        [Fact]
        public void Indexer_Set_UpdatesValue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata[AreaValueType.Title] = "First Title";
            metadata[AreaValueType.Title] = "Second Title";

            // Assert
            Assert.Equal("Second Title", metadata[AreaValueType.Title]);
        }

        [Fact]
        public void Indexer_SetNull_RemovesValue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.Title] = "Test";

            // Act
            metadata[AreaValueType.Title] = null;

            // Assert
            Assert.Null(metadata[AreaValueType.Title]);
            Assert.False(metadata.ContainsKey(AreaValueType.Title));
        }

        [Fact]
        public void Indexer_GetNonExistent_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            string value = metadata[AreaValueType.Title];

            // Assert
            Assert.Null(value);
        }

        #endregion

        #region Type Conversion Tests

        [Fact]
        public void GetString_ExistingValue_ReturnsValue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");

            // Act
            string result = metadata.GetString(AreaValueType.Title);

            // Assert
            Assert.Equal("Test", result);
        }

        [Fact]
        public void GetString_NonExistent_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            string result = metadata.GetString(AreaValueType.Title);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetLong_ValidNumber_ReturnsValue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "12345";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(12345L, result);
        }

        [Fact]
        public void GetLong_InvalidNumber_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.Title] = "not a number";

            // Act
            long? result = metadata.GetLong(AreaValueType.Title);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetLong_NonExistent_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetBool_True_ReturnsTrue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.Encrypted] = "true";

            // Act
            bool? result = metadata.GetBool(AreaValueType.Encrypted);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void GetBool_False_ReturnsFalse()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.Encrypted] = "false";

            // Act
            bool? result = metadata.GetBool(AreaValueType.Encrypted);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetBool_CaseInsensitive_Works()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.Encrypted] = "TRUE";

            // Act
            bool? result = metadata.GetBool(AreaValueType.Encrypted);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void GetBool_InvalidValue_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.Title] = "not a bool";

            // Act
            bool? result = metadata.GetBool(AreaValueType.Title);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetBool_NonExistent_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            bool? result = metadata.GetBool(AreaValueType.Encrypted);

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region Collection Operations Tests

        [Fact]
        public void ContainsKey_ExistingKey_ReturnsTrue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");

            // Act & Assert
            Assert.True(metadata.ContainsKey(AreaValueType.Title));
        }

        [Fact]
        public void ContainsKey_NonExistentKey_ReturnsFalse()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act & Assert
            Assert.False(metadata.ContainsKey(AreaValueType.Title));
        }

        [Fact]
        public void TryGetValue_ExistingKey_ReturnsTrue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");

            // Act
            bool result = metadata.TryGetValue(AreaValueType.Title, out string value);

            // Assert
            Assert.True(result);
            Assert.Equal("Test", value);
        }

        [Fact]
        public void TryGetValue_NonExistentKey_ReturnsFalse()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            bool result = metadata.TryGetValue(AreaValueType.Title, out string value);

            // Assert
            Assert.False(result);
            Assert.Null(value);
        }

        [Fact]
        public void Remove_ExistingKey_ReturnsTrue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");

            // Act
            bool result = metadata.Remove(AreaValueType.Title);

            // Assert
            Assert.True(result);
            Assert.False(metadata.ContainsKey(AreaValueType.Title));
        }

        [Fact]
        public void Remove_NonExistentKey_ReturnsFalse()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            bool result = metadata.Remove(AreaValueType.Title);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");
            metadata.Set(AreaValueType.Region, "USA");

            // Act
            metadata.Clear();

            // Assert
            Assert.Empty(metadata);
        }

        [Fact]
        public void Enumeration_IteratesAllEntries()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");
            metadata.Set(AreaValueType.Region, "USA");

            // Act
            List<KeyValuePair<AreaValueType, string>> entries = metadata.ToList();

            // Assert
            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, e => e.Key == AreaValueType.Title && e.Value == "Test");
            Assert.Contains(entries, e => e.Key == AreaValueType.Region && e.Value == "USA");
        }

        #endregion

        #region Binary Serialization Tests

        [Fact]
        public void ToBlob_EmptyMetadata_ReturnsEmptyArray()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            byte[] blob = metadata.ToBlob();

            // Assert
            Assert.Empty(blob);
        }

        [Fact]
        public void ToBlob_SingleEntry_EncodesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");

            // Act
            byte[] blob = metadata.ToBlob();

            // Assert
            Assert.NotEmpty(blob);
            // First byte should be count = 1
            Assert.Equal(1, blob[0]);
        }

        [Fact]
        public void ToBlob_MultipleEntries_EncodesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test Title");
            metadata.Set(AreaValueType.Region, "USA");
            metadata.Set(AreaValueType.Encrypted, true);

            // Act
            byte[] blob = metadata.ToBlob();

            // Assert
            Assert.NotEmpty(blob);
            // First byte should be count = 3
            Assert.Equal(3, blob[0]);
        }

        [Fact]
        public void ToBlob_TooManyEntries_ThrowsInvalidOperationException()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            for (int i = 0; i < 256; i++)
            {
                metadata[(AreaValueType)i] = $"Value{i}";
            }

            // Act & Assert
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => metadata.ToBlob());
            Assert.Contains("Maximum 255 metadata entries", ex.Message);
        }

        [Fact]
        public void FromBlob_EmptyBlob_ReturnsEmptyMetadata()
        {
            // Act
            AreaMetadata metadata = AreaMetadata.FromBlob(Array.Empty<byte>());

            // Assert
            Assert.Empty(metadata);
        }

        [Fact]
        public void FromBlob_NullBlob_ReturnsEmptyMetadata()
        {
            // Act
            AreaMetadata metadata = AreaMetadata.FromBlob(null);

            // Assert
            Assert.Empty(metadata);
        }

        [Fact]
        public void RoundTrip_SingleEntry_PreservesData()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.Title, "Test Title");

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal(1, restored.Count);
            Assert.Equal("Test Title", restored[AreaValueType.Title]);
        }

        [Fact]
        public void RoundTrip_MultipleEntries_PreservesData()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.FileName, "test.bin");
            original.Set(AreaValueType.Title, "Test Area");
            original.Set(AreaValueType.Region, "USA");
            original.Set(AreaValueType.Encrypted, false);

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal(4, restored.Count);
            Assert.Equal("test.bin", restored[AreaValueType.FileName]);
            Assert.Equal("Test Area", restored[AreaValueType.Title]);
            Assert.Equal("USA", restored[AreaValueType.Region]);
            Assert.Equal("false", restored[AreaValueType.Encrypted]);
        }

        [Fact]
        public void RoundTrip_UnicodeCharacters_PreservesData()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.Title, "???????");
            original.Set(AreaValueType.Region, "???");

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal("???????", restored[AreaValueType.Title]);
            Assert.Equal("???", restored[AreaValueType.Region]);
        }

        [Fact]
        public void ToBlob_DeterministicOrdering_SameOrderEveryTime()
        {
            // Arrange
            AreaMetadata metadata1 = new AreaMetadata();
            metadata1.Set(AreaValueType.Region, "USA");
            metadata1.Set(AreaValueType.Title, "Test");
            metadata1.Set(AreaValueType.Encrypted, true);

            AreaMetadata metadata2 = new AreaMetadata();
            metadata2.Set(AreaValueType.Encrypted, true);
            metadata2.Set(AreaValueType.Title, "Test");
            metadata2.Set(AreaValueType.Region, "USA");

            // Act
            byte[] blob1 = metadata1.ToBlob();
            byte[] blob2 = metadata2.ToBlob();

            // Assert
            Assert.Equal(blob1, blob2);
        }

        #endregion

        #region GetValueFromBlob Tests

        [Fact]
        public void GetValueFromBlob_ExistingValue_ReturnsValue()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test Title");
            metadata.Set(AreaValueType.Region, "USA");
            byte[] blob = metadata.ToBlob();

            // Act
            string value = AreaMetadata.GetValueFromBlob(blob, AreaValueType.Title);

            // Assert
            Assert.Equal("Test Title", value);
        }

        [Fact]
        public void GetValueFromBlob_NonExistentValue_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test Title");
            byte[] blob = metadata.ToBlob();

            // Act
            string value = AreaMetadata.GetValueFromBlob(blob, AreaValueType.Region);

            // Assert
            Assert.Null(value);
        }

        [Fact]
        public void GetValueFromBlob_EmptyBlob_ReturnsNull()
        {
            // Act
            string value = AreaMetadata.GetValueFromBlob(Array.Empty<byte>(), AreaValueType.Title);

            // Assert
            Assert.Null(value);
        }

        [Fact]
        public void GetValueFromBlob_NullBlob_ReturnsNull()
        {
            // Act
            string value = AreaMetadata.GetValueFromBlob(null, AreaValueType.Title);

            // Assert
            Assert.Null(value);
        }

        [Fact]
        public void GetValueFromBlob_FirstValue_ReturnsWithoutFullDecoding()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FileName, "first");
            metadata.Set(AreaValueType.Title, "second");
            metadata.Set(AreaValueType.Region, "third");
            byte[] blob = metadata.ToBlob();

            // Act
            string value = AreaMetadata.GetValueFromBlob(blob, AreaValueType.FileName);

            // Assert
            Assert.Equal("first", value);
        }

        [Fact]
        public void GetValueFromBlob_LastValue_FindsCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FileName, "first");
            metadata.Set(AreaValueType.Title, "second");
            metadata.Set(AreaValueType.Region, "third");
            byte[] blob = metadata.ToBlob();

            // Act
            string value = AreaMetadata.GetValueFromBlob(blob, AreaValueType.Title);

            // Assert
            Assert.Equal("second", value);
        }

        #endregion

        #region Clone Tests

        [Fact]
        public void Clone_CreatesIndependentCopy()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.Title, "Original");

            // Act
            AreaMetadata clone = original.Clone();
            clone.Set(AreaValueType.Title, "Modified");

            // Assert
            Assert.Equal("Original", original[AreaValueType.Title]);
            Assert.Equal("Modified", clone[AreaValueType.Title]);
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ReturnsDescriptiveString()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.Title, "Test");
            metadata.Set(AreaValueType.Region, "USA");

            // Act
            string result = metadata.ToString();

            // Assert
            Assert.Contains("2 entries", result);
        }

        #endregion

        #region Numeric Edge Case Tests

        [Fact]
        public void Set_Long_NegativeValue_StoresAsString()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.PhysicalOffset, -12345L);

            // Assert
            Assert.Equal("-12345", metadata[AreaValueType.PhysicalOffset]);
            Assert.Equal(-12345L, metadata.GetLong(AreaValueType.PhysicalOffset));
        }

        [Fact]
        public void Set_Long_Zero_StoresAsString()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.PvdSectorCount, 0L);

            // Assert
            Assert.Equal("0", metadata[AreaValueType.PvdSectorCount]);
            Assert.Equal(0L, metadata.GetLong(AreaValueType.PvdSectorCount));
        }

        [Fact]
        public void Set_Long_MaxValue_StoresCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.PvdSectorCount, long.MaxValue);

            // Assert
            Assert.Equal(long.MaxValue.ToString(), metadata[AreaValueType.PvdSectorCount]);
            Assert.Equal(long.MaxValue, metadata.GetLong(AreaValueType.PvdSectorCount));
        }

        [Fact]
        public void Set_Long_MinValue_StoresCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();

            // Act
            metadata.Set(AreaValueType.PhysicalOffset, long.MinValue);

            // Assert
            Assert.Equal(long.MinValue.ToString(), metadata[AreaValueType.PhysicalOffset]);
            Assert.Equal(long.MinValue, metadata.GetLong(AreaValueType.PhysicalOffset));
        }

        [Fact]
        public void GetLong_NegativeString_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PhysicalOffset] = "-999999";

            // Act
            long? result = metadata.GetLong(AreaValueType.PhysicalOffset);

            // Assert
            Assert.Equal(-999999L, result);
        }

        [Fact]
        public void GetLong_MaxValueString_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = long.MaxValue.ToString();

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(long.MaxValue, result);
        }

        [Fact]
        public void GetLong_MinValueString_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PhysicalOffset] = long.MinValue.ToString();

            // Act
            long? result = metadata.GetLong(AreaValueType.PhysicalOffset);

            // Assert
            Assert.Equal(long.MinValue, result);
        }

        [Fact]
        public void GetLong_ZeroString_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "0";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(0L, result);
        }

        [Fact]
        public void GetLong_LeadingZeros_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "00012345";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(12345L, result);
        }

        [Fact]
        public void GetLong_LeadingWhitespace_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = " 12345";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(12345L, result); // long.TryParse handles leading whitespace
        }

        [Fact]
        public void GetLong_TrailingWhitespace_ParsesCorrectly()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "12345 ";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(12345L, result); // long.TryParse handles trailing whitespace
        }

        [Fact]
        public void GetLong_Overflow_ReturnsNull()
        {
            // Arrange - number too large for long
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "9223372036854775808"; // long.MaxValue + 1

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Null(result); // Overflow should fail parsing
        }

        [Fact]
        public void GetLong_Underflow_ReturnsNull()
        {
            // Arrange - number too small for long
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PhysicalOffset] = "-9223372036854775809"; // long.MinValue - 1

            // Act
            long? result = metadata.GetLong(AreaValueType.PhysicalOffset);

            // Assert
            Assert.Null(result); // Underflow should fail parsing
        }

        [Fact]
        public void GetLong_ScientificNotation_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "1e5"; // 100000 in scientific notation

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Null(result); // long.TryParse doesn't handle scientific notation
        }

        [Fact]
        public void GetLong_HexString_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "0x1234"; // Hex format

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Null(result); // long.TryParse doesn't handle hex by default
        }

        [Fact]
        public void GetLong_DecimalPoint_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "12345.67";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Null(result); // long.TryParse doesn't handle decimals
        }

        [Fact]
        public void GetLong_ThousandsSeparator_ReturnsNull()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            metadata[AreaValueType.PvdSectorCount] = "1,234,567";

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Null(result); // long.TryParse doesn't handle separators by default
        }

        [Fact]
        public void RoundTrip_NegativeLong_PreservesValue()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.PhysicalOffset, -987654321L);

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal(-987654321L, restored.GetLong(AreaValueType.PhysicalOffset));
        }

        [Fact]
        public void RoundTrip_LongMaxValue_PreservesValue()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.PvdSectorCount, long.MaxValue);

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal(long.MaxValue, restored.GetLong(AreaValueType.PvdSectorCount));
        }

        [Fact]
        public void RoundTrip_LongMinValue_PreservesValue()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.PhysicalOffset, long.MinValue);

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal(long.MinValue, restored.GetLong(AreaValueType.PhysicalOffset));
        }

        [Fact]
        public void RoundTrip_MixedNumericValues_PreservesAll()
        {
            // Arrange
            AreaMetadata original = new AreaMetadata();
            original.Set(AreaValueType.PvdSectorCount, 12345L);
            original.Set(AreaValueType.PhysicalOffset, -67890L);
            original[AreaValueType.HeaderSize] = "0";
            original.Set(AreaValueType.PartitionType, long.MaxValue);

            // Act
            byte[] blob = original.ToBlob();
            AreaMetadata restored = AreaMetadata.FromBlob(blob);

            // Assert
            Assert.Equal(12345L, restored.GetLong(AreaValueType.PvdSectorCount));
            Assert.Equal(-67890L, restored.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal(0L, restored.GetLong(AreaValueType.HeaderSize));
            Assert.Equal(long.MaxValue, restored.GetLong(AreaValueType.PartitionType));
        }

        [Fact]
        public void GetLong_VeryLargePositiveNumber_WithinRange_ParsesCorrectly()
        {
            // Arrange - test a very large but valid long
            AreaMetadata metadata = new AreaMetadata();
            long largeNumber = 9223372036854775806L; // long.MaxValue - 1
            metadata[AreaValueType.PvdSectorCount] = largeNumber.ToString();

            // Act
            long? result = metadata.GetLong(AreaValueType.PvdSectorCount);

            // Assert
            Assert.Equal(largeNumber, result);
        }

        [Fact]
        public void GetLong_VeryLargeNegativeNumber_WithinRange_ParsesCorrectly()
        {
            // Arrange - test a very large negative but valid long
            AreaMetadata metadata = new AreaMetadata();
            long largeNegative = -9223372036854775807L; // long.MinValue + 1
            metadata[AreaValueType.PhysicalOffset] = largeNegative.ToString();

            // Act
            long? result = metadata.GetLong(AreaValueType.PhysicalOffset);

            // Assert
            Assert.Equal(largeNegative, result);
        }

        [Fact]
        public void Set_Long_TypeicalDiscSizes_WorkCorrectly()
        {
            // Arrange - test typical disc image sizes
            AreaMetadata metadata = new AreaMetadata();

            // Act & Assert
            // CD: 700 MB = 734,003,200 bytes
            metadata.Set(AreaValueType.PvdSectorCount, 734003200L);
            Assert.Equal(734003200L, metadata.GetLong(AreaValueType.PvdSectorCount));

            // DVD-5: 4.7 GB = 4,700,372,992 bytes
            metadata.Set(AreaValueType.PvdSectorCount, 4700372992L);
            Assert.Equal(4700372992L, metadata.GetLong(AreaValueType.PvdSectorCount));

            // Blu-ray: 25 GB = 25,000,000,000 bytes
            metadata.Set(AreaValueType.PvdSectorCount, 25000000000L);
            Assert.Equal(25000000000L, metadata.GetLong(AreaValueType.PvdSectorCount));
        }

        #endregion

        #region Size Tests

        [Fact]
        public void ToBlob_TypicalMetadata_ReasonableSize()
        {
            // Arrange - typical metadata for a disc area
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FileName, "game.iso");
            metadata.Set(AreaValueType.Title, "Super Mario Bros");
            metadata.Set(AreaValueType.Region, "USA");
            metadata.Set(AreaValueType.Encrypted, false);

            // Act
            byte[] blob = metadata.ToBlob();

            // Assert
            // Should be compact: 1 byte count + (1+2+N per entry)
            // Approximately: 1 + (3+8) + (3+16) + (3+3) + (3+5) = ~42 bytes
            Assert.InRange(blob.Length, 30, 60);
        }

        [Fact]
        public void ToBlob_LongValue_ThrowsIfTooLarge()
        {
            // Arrange
            AreaMetadata metadata = new AreaMetadata();
            string largeValue = new string('X', 70000); // > ushort.MaxValue
            metadata[AreaValueType.Title] = largeValue;

            // Act & Assert
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => metadata.ToBlob());
            Assert.Contains("Value too large", ex.Message);
        }

        #endregion
    }
}
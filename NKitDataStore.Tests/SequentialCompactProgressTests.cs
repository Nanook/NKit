using NKitDataStore.Binary;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for the CompactProgressFile class.
    /// Tests serialization, deserialization, and state tracking.
    /// </summary>
    public class SequentialCompactProgressTests : IDisposable
    {
        private readonly string _tempDir;

        public SequentialCompactProgressTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitProgress_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void Write_ThenRead_RoundTripsAllFields()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            CompactProgressFile original = new CompactProgressFile
            {
                Version = 1,
                Created = new DateTime(2026, 11, 11, 14, 30, 0, DateTimeKind.Utc),
                SetName = "wii",
                ShardsToCompact = new List<int> { 0, 1, 3, 7, 12 },
                CompletedShards = new List<int> { 0, 1 },
                PendingShard = 3,
                ExpectedSizes = new Dictionary<int, long>
                {
                    { 0, 1073741824L },
                    { 1, 2147483648L },
                    { 3, 536870912L },
                    { 7, 1073741824L },
                    { 12, 268435456L }
                }
            };

            // Act
            original.Write(indexPath);
            CompactProgressFile? loaded = CompactProgressFile.Read(indexPath);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(original.Version, loaded.Version);
            Assert.Equal(original.SetName, loaded.SetName);
            Assert.Equal(original.ShardsToCompact, loaded.ShardsToCompact);
            Assert.Equal(original.CompletedShards, loaded.CompletedShards);
            Assert.Equal(original.PendingShard, loaded.PendingShard);
            Assert.Equal(original.ExpectedSizes, loaded.ExpectedSizes);
        }

        [Fact]
        public void Read_NonExistentFile_ReturnsNull()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "nonexistent.nkds");

            // Act
            CompactProgressFile? result = CompactProgressFile.Read(indexPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Exists_WhenFilePresent_ReturnsTrue()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            new CompactProgressFile { SetName = "test" }.Write(indexPath);

            // Act & Assert
            Assert.True(CompactProgressFile.Exists(indexPath));
        }

        [Fact]
        public void Exists_WhenFileMissing_ReturnsFalse()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "missing.nkds");

            // Act & Assert
            Assert.False(CompactProgressFile.Exists(indexPath));
        }

        [Fact]
        public void Delete_RemovesFile()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            new CompactProgressFile { SetName = "test" }.Write(indexPath);
            Assert.True(CompactProgressFile.Exists(indexPath));

            // Act
            CompactProgressFile.Delete(indexPath);

            // Assert
            Assert.False(CompactProgressFile.Exists(indexPath));
        }

        [Fact]
        public void SetPendingShard_UpdatesFileOnDisk()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            CompactProgressFile progress = new CompactProgressFile
            {
                SetName = "test",
                ShardsToCompact = new List<int> { 0, 1, 2 }
            };
            progress.Write(indexPath);

            // Act
            progress.SetPendingShard(1, indexPath);

            // Assert
            CompactProgressFile? reloaded = CompactProgressFile.Read(indexPath);
            Assert.NotNull(reloaded);
            Assert.Equal(1, reloaded.PendingShard);
        }

        [Fact]
        public void MarkShardComplete_UpdatesCompletedAndClearsPending()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            CompactProgressFile progress = new CompactProgressFile
            {
                SetName = "test",
                ShardsToCompact = new List<int> { 0, 1, 2 },
                PendingShard = 1
            };
            progress.Write(indexPath);

            // Act
            progress.MarkShardComplete(1, indexPath);

            // Assert
            CompactProgressFile? reloaded = CompactProgressFile.Read(indexPath);
            Assert.NotNull(reloaded);
            Assert.Null(reloaded.PendingShard);
            Assert.Contains(1, reloaded.CompletedShards);
        }

        [Fact]
        public void GetRemainingShardsToCompact_ReturnsOnlyIncomplete()
        {
            // Arrange
            CompactProgressFile progress = new CompactProgressFile
            {
                ShardsToCompact = new List<int> { 0, 1, 2, 3 },
                CompletedShards = new List<int> { 0, 2 }
            };

            // Act
            List<int> remaining = progress.GetRemainingShardsToCompact();

            // Assert
            Assert.Equal(new List<int> { 1, 3 }, remaining);
        }

        [Fact]
        public void IsComplete_WhenAllDone_ReturnsTrue()
        {
            // Arrange
            CompactProgressFile progress = new CompactProgressFile
            {
                ShardsToCompact = new List<int> { 0, 1, 2 },
                CompletedShards = new List<int> { 0, 1, 2 }
            };

            // Act & Assert
            Assert.True(progress.IsComplete());
        }

        [Fact]
        public void IsComplete_WhenNotAllDone_ReturnsFalse()
        {
            // Arrange
            CompactProgressFile progress = new CompactProgressFile
            {
                ShardsToCompact = new List<int> { 0, 1, 2 },
                CompletedShards = new List<int> { 0, 1 }
            };

            // Act & Assert
            Assert.False(progress.IsComplete());
        }

        [Fact]
        public void Read_CorruptFile_ReturnsNull()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            string progressPath = CompactProgressFile.GetPath(indexPath);
            File.WriteAllText(progressPath, "This is not valid progress file content\nNo version field");

            // Act
            CompactProgressFile? result = CompactProgressFile.Read(indexPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Read_FutureVersion_ReturnsNull()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            string progressPath = CompactProgressFile.GetPath(indexPath);
            File.WriteAllText(progressPath, "Version: 999\nSetName: test\nShardsToCompact: 0,1\nCompletedShards: \nPendingShard: \nExpectedSizes: 0:100,1:200");

            // Act
            CompactProgressFile? result = CompactProgressFile.Read(indexPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Read_EmptyShardsToCompact_ParsesCorrectly()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            CompactProgressFile original = new CompactProgressFile
            {
                SetName = "empty",
                ShardsToCompact = new List<int>(),
                CompletedShards = new List<int>(),
                ExpectedSizes = new Dictionary<int, long>()
            };
            original.Write(indexPath);

            // Act
            CompactProgressFile? loaded = CompactProgressFile.Read(indexPath);

            // Assert
            Assert.NotNull(loaded);
            Assert.Empty(loaded.ShardsToCompact);
            Assert.Empty(loaded.CompletedShards);
            Assert.Empty(loaded.ExpectedSizes);
        }

        [Fact]
        public void Read_NoPendingShard_ParsesAsNull()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            CompactProgressFile original = new CompactProgressFile
            {
                SetName = "test",
                ShardsToCompact = new List<int> { 0 },
                PendingShard = null
            };
            original.Write(indexPath);

            // Act
            CompactProgressFile? loaded = CompactProgressFile.Read(indexPath);

            // Assert
            Assert.NotNull(loaded);
            Assert.Null(loaded.PendingShard);
        }

        [Fact]
        public void GetPath_ReturnsCorrectPath()
        {
            // Arrange
            string indexPath = @"C:\Data\Sets\wii.nkds";

            // Act
            string progressPath = CompactProgressFile.GetPath(indexPath);

            // Assert
            Assert.Equal(@"C:\Data\Sets\wii.nkds.compact.progress", progressPath);
        }

        [Fact]
        public void Write_CreatesHumanReadableFile()
        {
            // Arrange
            string indexPath = Path.Combine(_tempDir, "test.nkds");
            CompactProgressFile progress = new CompactProgressFile
            {
                SetName = "wii",
                ShardsToCompact = new List<int> { 0, 1 },
                CompletedShards = new List<int> { 0 },
                PendingShard = 1,
                ExpectedSizes = new Dictionary<int, long> { { 0, 1000 }, { 1, 2000 } }
            };

            // Act
            progress.Write(indexPath);
            string content = File.ReadAllText(CompactProgressFile.GetPath(indexPath));

            // Assert
            Assert.Contains("# NKDS Compact Progress", content);
            Assert.Contains("DO NOT EDIT", content);
            Assert.Contains("Version: 1", content);
            Assert.Contains("SetName: wii", content);
            Assert.Contains("ShardsToCompact: 0,1", content);
            Assert.Contains("CompletedShards: 0", content);
            Assert.Contains("PendingShard: 1", content);
            Assert.Contains("0:1000", content);
            Assert.Contains("1:2000", content);
        }
    }
}

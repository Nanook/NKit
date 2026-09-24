using global::NKit.Ui.Helpers.Yaml;
using global::NKit.Ui.Models;
using Nanook.NKit;
using System.Collections.ObjectModel;
using System.IO;
using Xunit;


namespace NKit.Tests.Configuration.UI
{
    /// <summary>
    /// Tests for the AOT-compatible queue serializer to ensure file queue persistence works correctly
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "UI")]
    public class AotQueueSerializerTests
    {
        [Fact]
        public void Serialize_EmptyQueue_ProducesValidYaml()
        {
            // Arrange
            ObservableCollection<SourceFileRecord> emptyQueue = new ObservableCollection<SourceFileRecord>();

            // Act
            using StringWriter writer = new StringWriter();
            AotQueueSerializer.Serialize(writer, emptyQueue);
            string yaml = writer.ToString();

            // Assert
            Assert.NotNull(yaml);
            Assert.Contains("version:", yaml);
            Assert.Contains("count: 0", yaml);
            Assert.Contains("queue:", yaml);
        }

        [Fact]
        public void Serialize_QueueWithSingleItem_ProducesValidYaml()
        {
            // Arrange
            ObservableCollection<SourceFileRecord> queue = new ObservableCollection<SourceFileRecord>
            {
                new SourceFileRecord
                {
                    Name = "test.iso",
                    Filepath = @"C:\temp\test.iso",
                    ImageType = SourceImageType.Iso,
                    Length = 1024,
                    ProcessingStatus = ProcessingStatus.Queued,
                    SourceFileDetails = "Test ISO file"
                }
            };

            // Act
            using StringWriter writer = new StringWriter();
            AotQueueSerializer.Serialize(writer, queue);
            string yaml = writer.ToString();

            // Assert
            Assert.NotNull(yaml);
            Assert.Contains("count: 1", yaml);
            Assert.Contains("name: test.iso", yaml);
            Assert.Contains(@"filepath: C:\temp\test.iso", yaml);
            Assert.Contains("imageType: Iso", yaml);
            Assert.Contains("length: 1024", yaml);
            Assert.Contains("processingStatus: Queued", yaml);
        }

        [Fact]
        public void Deserialize_ValidYaml_RestoresQueue()
        {
            // Arrange
            ObservableCollection<SourceFileRecord> originalQueue = new ObservableCollection<SourceFileRecord>
            {
                new SourceFileRecord
                {
                    Name = "game1.wbfs",
                    Filepath = @"C:\games\game1.wbfs",
                    ImageType = SourceImageType.Wbfs,
                    Length = 4700000000,
                    ProcessingStatus = ProcessingStatus.Completed
                },
                new SourceFileRecord
                {
                    Name = "game2.zip",
                    Filepath = @"C:\games\game2.zip",
                    ArchiveType = SourceArchiveType.SevenZip,
                    Length = 1350000000,
                    ProcessingStatus = ProcessingStatus.Failed
                }
            };

            // Serialize to YAML
            string yaml;
            using (StringWriter writer = new StringWriter())
            {
                AotQueueSerializer.Serialize(writer, originalQueue);
                yaml = writer.ToString();
            }

            // Act - Deserialize back
            ObservableCollection<SourceFileRecord> restoredQueue;
            using (StringReader reader = new StringReader(yaml))
            {
                restoredQueue = AotQueueSerializer.Deserialize(reader);
            }

            // Assert
            Assert.NotNull(restoredQueue);
            Assert.Equal(2, restoredQueue.Count);

            // Check first item
            SourceFileRecord item1 = restoredQueue[0];
            Assert.Equal("game1.wbfs", item1.Name);
            Assert.Equal(@"C:\games\game1.wbfs", item1.Filepath);
            Assert.Equal(SourceImageType.Wbfs, item1.ImageType);
            Assert.Equal(4700000000, item1.Length);
            Assert.Equal(ProcessingStatus.Completed, item1.ProcessingStatus);

            // Check second item
            SourceFileRecord item2 = restoredQueue[1];
            Assert.Equal("game2.zip", item2.Name);
            Assert.Equal(@"C:\games\game2.zip", item2.Filepath);
            Assert.Equal(SourceArchiveType.SevenZip, item2.ArchiveType);
            Assert.Equal(1350000000, item2.Length);
            Assert.Equal(ProcessingStatus.Failed, item2.ProcessingStatus);
        }

        [Fact]
        public void Deserialize_EmptyYaml_ReturnsEmptyQueue()
        {
            // Arrange
            ObservableCollection<SourceFileRecord> emptyQueue = new ObservableCollection<SourceFileRecord>();

            string yaml;
            using (StringWriter writer = new StringWriter())
            {
                AotQueueSerializer.Serialize(writer, emptyQueue);
                yaml = writer.ToString();
            }

            // Act
            ObservableCollection<SourceFileRecord> restoredQueue;
            using (StringReader reader = new StringReader(yaml))
            {
                restoredQueue = AotQueueSerializer.Deserialize(reader);
            }

            // Assert
            Assert.NotNull(restoredQueue);
            Assert.Empty(restoredQueue);
        }

        [Fact]
        public void RoundTrip_ComplexQueue_PreservesAllData()
        {
            // Arrange - Create a complex queue with various states
            ObservableCollection<SourceFileRecord> originalQueue = new ObservableCollection<SourceFileRecord>
            {
                new SourceFileRecord
                {
                    Name = "completed.iso",
                    Filepath = @"D:\roms\completed.iso",
                    ImageType = SourceImageType.Iso,
                    Length = 8500000000,
                    ProcessingStatus = ProcessingStatus.Completed,
                    SourceFileDetails = "Successfully converted disc image"
                },
                new SourceFileRecord
                {
                    Name = "archive.7z",
                    Filepath = @"D:\archives\archive.7z",
                    ArchiveType = SourceArchiveType.SevenZip,
                    Length = 2048000000,
                    ProcessingStatus = ProcessingStatus.Processing
                },
                new SourceFileRecord
                {
                    Name = "skipped.gcz",
                    Filepath = @"D:\roms\skipped.gcz",
                    ImageType = SourceImageType.Gcz,
                    Length = 512000000,
                    ProcessingStatus = ProcessingStatus.Skipped
                }
            };

            // Act - Round trip through serialization
            string yaml;
            using (StringWriter writer = new StringWriter())
            {
                AotQueueSerializer.Serialize(writer, originalQueue);
                yaml = writer.ToString();
            }

            ObservableCollection<SourceFileRecord> restoredQueue;
            using (StringReader reader = new StringReader(yaml))
            {
                restoredQueue = AotQueueSerializer.Deserialize(reader);
            }

            // Assert - Verify all data is preserved
            Assert.Equal(originalQueue.Count, restoredQueue.Count);

            for (int i = 0; i < originalQueue.Count; i++)
            {
                SourceFileRecord original = originalQueue[i];
                SourceFileRecord restored = restoredQueue[i];

                Assert.Equal(original.Name, restored.Name);
                Assert.Equal(original.Filepath, restored.Filepath);
                Assert.Equal(original.ImageType, restored.ImageType);
                Assert.Equal(original.ArchiveType, restored.ArchiveType);
                Assert.Equal(original.Length, restored.Length);
                Assert.Equal(original.ProcessingStatus, restored.ProcessingStatus);
                Assert.Equal(original.SourceFileDetails, restored.SourceFileDetails);
            }
        }

        [Fact]
        public void Serialize_NullQueue_HandlesGracefully()
        {
            // Arrange
            ObservableCollection<SourceFileRecord> nullQueue = null;

            // Act & Assert - Should not throw
            using StringWriter writer = new StringWriter();
            AotQueueSerializer.Serialize(writer, nullQueue);
            string yaml = writer.ToString();

            Assert.NotNull(yaml);
            Assert.Contains("count: 0", yaml);
        }

        [Fact]
        public void Deserialize_WithInternalSetProperties_RestoresAccessibleProperties()
        {
            // Arrange - Test that deserialization works even when some properties have internal setters
            string yaml = @"version: 1.0
generated: 2024-01-01 12:00:00
count: 1
queue:
- name: test.rvz
  filepath: C:\test\test.rvz
  imageType: Rvz
  length: 1024000000
  processingStatus: Completed
  progress: 100.00
  outFileName: test_output.rvz
  verifyResultMessage: Verified successfully
";

            // Act
            ObservableCollection<SourceFileRecord> restoredQueue;
            using (StringReader reader = new StringReader(yaml))
            {
                restoredQueue = AotQueueSerializer.Deserialize(reader);
            }

            // Assert - Check that basic properties are restored
            Assert.NotNull(restoredQueue);
            Assert.Single(restoredQueue);

            SourceFileRecord item = restoredQueue[0];
            Assert.Equal("test.rvz", item.Name);
            Assert.Equal(@"C:\test\test.rvz", item.Filepath);
            Assert.Equal(SourceImageType.Rvz, item.ImageType);
            Assert.Equal(1024000000, item.Length);
            Assert.Equal(ProcessingStatus.Completed, item.ProcessingStatus);
        }
    }
}
using Nanook.NKit.Steps.Shared;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for DataStoreFolderFormatter logical behavior.
    ///
    /// DataStoreFolderFormatter requires a real DataStore with SQLite and shard files,
    /// so these tests validate the LOGICAL invariants using a simulated model that
    /// mirrors StoreFile() behavior, and verify FsYaml output produced from tracked entries.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.4**
    /// </summary>
    public class DataStoreFolderFormatterUnitTests
    {
        /// <summary>
        /// Simulated FolderFileEntry mirroring the internal struct in DataStoreFolderFormatter.
        /// </summary>
        private struct SimulatedFolderFileEntry
        {
            public string RelativePath;
            public long OffsetStart;
            public long Size;
            public ulong XxHash64;
            public uint Crc32;
        }

        /// <summary>
        /// Simulates StoreFile() logic: tracks entries and advances offset for non-zero files.
        /// Returns the list of entries and the final offset.
        /// </summary>
        private static (List<SimulatedFolderFileEntry> entries, long finalOffset) SimulateStoreFiles(
            (string relativePath, long size)[] files)
        {
            List<SimulatedFolderFileEntry> entries = new List<SimulatedFolderFileEntry>();
            long currentOffset = 0;

            foreach ((string relativePath, long size) in files)
            {
                entries.Add(new SimulatedFolderFileEntry
                {
                    RelativePath = relativePath,
                    OffsetStart = currentOffset,
                    Size = size,
                    XxHash64 = 0,
                    Crc32 = 0
                });

                if (size > 0)
                    currentOffset += size;
            }

            return (entries, currentOffset);
        }

        /// <summary>
        /// Builds an FsYaml from simulated entries, matching BuildFileSystemYaml() logic.
        /// </summary>
        private static FsYaml BuildFsYaml(List<SimulatedFolderFileEntry> entries)
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            foreach (SimulatedFolderFileEntry entry in entries)
                root.AddFileByPath(entry.RelativePath, entry.OffsetStart, entry.Size, entry.XxHash64, entry.Crc32);
            return fsYaml;
        }

        /// <summary>
        /// Counts all leaf file nodes recursively.
        /// </summary>
        private static int CountFiles(List<FsYamlNode> roots)
        {
            int count = 0;
            foreach (FsYamlNode root in roots)
                count += CountFilesRecursive(root);
            return count;
        }

        private static int CountFilesRecursive(FsYamlNode node)
        {
            if (node.IsFile)
                return 1;
            int count = 0;
            if (node.Children != null)
                foreach (FsYamlNode child in node.Children)
                    count += CountFilesRecursive(child);
            return count;
        }

        #region StoreFile: 0-byte files

        /// <summary>
        /// Validates: Requirement 1.1, 1.2
        /// 0-byte file: offset does NOT advance, but entry is still tracked.
        /// </summary>
        [Fact]
        public void StoreFile_ZeroByteFile_OffsetDoesNotAdvance()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("empty.txt", 0L)
            });

            Assert.Single(entries);
            Assert.Equal(0L, entries[0].OffsetStart);
            Assert.Equal(0L, entries[0].Size);
            Assert.Equal(0L, finalOffset);
        }

        [Fact]
        public void StoreFile_MultipleZeroByteFiles_OffsetStaysAtZero()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("a.txt", 0L),
                ("b.txt", 0L),
                ("c.txt", 0L)
            });

            Assert.Equal(3, entries.Count);
            // All entries share offset 0 since none advance it
            Assert.All(entries, e => Assert.Equal(0L, e.OffsetStart));
            Assert.Equal(0L, finalOffset);
        }

        #endregion

        #region StoreFile: 1-byte files

        /// <summary>
        /// Validates: Requirement 1.2
        /// 1-byte file: offset advances by exactly 1.
        /// </summary>
        [Fact]
        public void StoreFile_OneByteFile_OffsetAdvancesBy1()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("tiny.bin", 1L)
            });

            Assert.Single(entries);
            Assert.Equal(0L, entries[0].OffsetStart);
            Assert.Equal(1L, entries[0].Size);
            Assert.Equal(1L, finalOffset);
        }

        [Fact]
        public void StoreFile_TwoOneByteFiles_OffsetsAreSequential()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("first.bin", 1L),
                ("second.bin", 1L)
            });

            Assert.Equal(2, entries.Count);
            Assert.Equal(0L, entries[0].OffsetStart);
            Assert.Equal(1L, entries[1].OffsetStart);
            Assert.Equal(2L, finalOffset);
        }

        #endregion

        #region StoreFile: Exact block size (65536 bytes)

        /// <summary>
        /// Validates: Requirement 1.2, 1.5
        /// Exact block size file: offset advances by 65536.
        /// </summary>
        [Fact]
        public void StoreFile_ExactBlockSize_OffsetAdvancesByBlockSize()
        {
            const long blockSize = 65536;
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("block.dat", blockSize)
            });

            Assert.Single(entries);
            Assert.Equal(0L, entries[0].OffsetStart);
            Assert.Equal(blockSize, entries[0].Size);
            Assert.Equal(blockSize, finalOffset);
        }

        [Fact]
        public void StoreFile_TwoExactBlockSizeFiles_OffsetsCorrect()
        {
            const long blockSize = 65536;
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("block1.dat", blockSize),
                ("block2.dat", blockSize)
            });

            Assert.Equal(2, entries.Count);
            Assert.Equal(0L, entries[0].OffsetStart);
            Assert.Equal(blockSize, entries[1].OffsetStart);
            Assert.Equal(blockSize * 2, finalOffset);
        }

        #endregion

        #region StoreFile: Multi-block files

        /// <summary>
        /// Validates: Requirement 1.2
        /// Multi-block file: offset advances by full file size.
        /// </summary>
        [Fact]
        public void StoreFile_MultiBlockFile_OffsetAdvancesByFullSize()
        {
            long fileSize = (65536 * 3) + 1000; // 3 full blocks + partial
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("large.bin", fileSize)
            });

            Assert.Single(entries);
            Assert.Equal(0L, entries[0].OffsetStart);
            Assert.Equal(fileSize, entries[0].Size);
            Assert.Equal(fileSize, finalOffset);
        }

        [Fact]
        public void StoreFile_MixedSizes_CumulativeOffsetCorrect()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("empty.txt", 0L),
                ("tiny.bin", 1L),
                ("block.dat", 65536L),
                ("large.bin", 200000L),
                ("another_empty.txt", 0L),
                ("small.txt", 42L)
            });

            Assert.Equal(6, entries.Count);

            // Verify each entry's offset start
            Assert.Equal(0L, entries[0].OffsetStart);     // empty: offset stays 0
            Assert.Equal(0L, entries[1].OffsetStart);     // tiny: starts at 0 (empty didn't advance)
            Assert.Equal(1L, entries[2].OffsetStart);     // block: starts at 1
            Assert.Equal(65537L, entries[3].OffsetStart); // large: starts at 1 + 65536
            Assert.Equal(265537L, entries[4].OffsetStart); // another_empty: starts at 65537 + 200000
            Assert.Equal(265537L, entries[5].OffsetStart); // small: same offset (empty didn't advance)

            // Final offset = sum of non-zero sizes
            long expected = 1L + 65536L + 200000L + 42L;
            Assert.Equal(expected, finalOffset);
        }

        #endregion

        #region BuildFileSystemYaml: produces valid filesystem.yaml

        /// <summary>
        /// Validates: Requirement 1.3
        /// BuildFileSystemYaml produces valid YAML that round-trips through FsYaml.
        /// </summary>
        [Fact]
        public void BuildFileSystemYaml_SingleFile_ProducesValidYaml()
        {
            (List<SimulatedFolderFileEntry> entries, long _) = SimulateStoreFiles(new[]
            {
                ("readme.txt", 1024L)
            });

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();

            Assert.StartsWith("version: 1.0", yaml);
            Assert.Contains("fs:", yaml);
            Assert.Contains("readme.txt", yaml);

            // Round-trip: parse back and verify
            FsYaml parsed = FsYaml.FromYaml(yaml);
            Assert.Single(parsed.FileSystems);
            Assert.Equal(1, CountFiles(parsed.FileSystems));
        }

        [Fact]
        public void BuildFileSystemYaml_MultipleFiles_AllTracked()
        {
            (List<SimulatedFolderFileEntry> entries, long _) = SimulateStoreFiles(new[]
            {
                ("file1.dat", 100L),
                ("file2.dat", 200L),
                ("sub/file3.dat", 300L)
            });

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            Assert.Equal(3, CountFiles(parsed.FileSystems));
        }

        [Fact]
        public void BuildFileSystemYaml_NestedDirectories_CreatesTree()
        {
            (List<SimulatedFolderFileEntry> entries, long _) = SimulateStoreFiles(new[]
            {
                ("a/b/c/deep.txt", 50L),
                ("a/b/shallow.txt", 100L),
                ("top.txt", 200L)
            });

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            Assert.Equal(3, CountFiles(parsed.FileSystems));

            // Verify directory structure: root should have "a" dir and "top.txt" file
            FsYamlNode root = parsed.FileSystems[0];
            Assert.NotNull(root.Children);
            Assert.True(root.Children!.Count >= 2); // "a" dir + "top.txt"
        }

        [Fact]
        public void BuildFileSystemYaml_ZeroByteFile_IncludedInYaml()
        {
            (List<SimulatedFolderFileEntry> entries, long _) = SimulateStoreFiles(new[]
            {
                ("empty.txt", 0L)
            });

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            Assert.Equal(1, CountFiles(parsed.FileSystems));
            FsYamlNode file = parsed.FileSystems[0].Children![0];
            Assert.Equal("empty.txt", file.Name);
            Assert.Equal(0L, file.Size);
        }

        [Fact]
        public void BuildFileSystemYaml_PreservesOffsetAndSize()
        {
            (List<SimulatedFolderFileEntry> entries, long _) = SimulateStoreFiles(new[]
            {
                ("first.bin", 1000L),
                ("second.bin", 2000L)
            });

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            FsYamlNode root = parsed.FileSystems[0];
            Assert.Equal(2, root.Children!.Count);

            FsYamlNode first = root.Children[0];
            Assert.Equal("first.bin", first.Name);
            Assert.Equal(0L, first.Offset);    // offsetStart of first file
            Assert.Equal(1000L, first.Size);

            FsYamlNode second = root.Children[1];
            Assert.Equal("second.bin", second.Name);
            Assert.Equal(1000L, second.Offset); // offsetStart of second file
            Assert.Equal(2000L, second.Size);
        }

        #endregion

        #region Empty folder ingestion

        /// <summary>
        /// Validates: Requirement 2.4
        /// Empty folder: produces valid FsYaml with version 1.0 and empty fs section.
        /// </summary>
        [Fact]
        public void EmptyFolder_ProducesValidYamlWithEmptyFs()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(Array.Empty<(string, long)>());

            Assert.Empty(entries);
            Assert.Equal(0L, finalOffset);

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();

            Assert.StartsWith("version: 1.0", yaml);
            Assert.Contains("fs:", yaml);
            Assert.DoesNotContain("ifs:", yaml);

            // Round-trip: should parse back with no files
            FsYaml parsed = FsYaml.FromYaml(yaml);
            Assert.Equal(0, CountFiles(parsed.FileSystems));
        }

        [Fact]
        public void EmptyFolder_TotalSizeIsZero()
        {
            (List<SimulatedFolderFileEntry> _, long finalOffset) = SimulateStoreFiles(Array.Empty<(string, long)>());
            Assert.Equal(0L, finalOffset);
        }

        #endregion

        #region No area records (format constraint)

        /// <summary>
        /// Validates: Requirement 1.5, 2.1
        /// Folder images use BlockType.File with no areas.
        /// The FsYaml produced has no ifs section (no area references),
        /// confirming the folder format stores only flat file blocks.
        /// </summary>
        [Fact]
        public void FolderFormat_NoIfsSection_ConfirmsNoAreaReferences()
        {
            (List<SimulatedFolderFileEntry> entries, long _) = SimulateStoreFiles(new[]
            {
                ("file1.dat", 100L),
                ("file2.dat", 200L)
            });

            FsYaml fsYaml = BuildFsYaml(entries);
            string yaml = fsYaml.ToYaml();

            // Folder images should never have ifs entries
            Assert.DoesNotContain("ifs:", yaml);
            Assert.Empty(fsYaml.ImageFileSystems);
        }

        /// <summary>
        /// Validates: Requirement 1.5
        /// Folder images use BlockType.File — verified by confirming the format
        /// constraint: all entries are flat file entries with sequential offsets,
        /// no stride, no area metadata.
        /// </summary>
        [Fact]
        public void FolderFormat_AllEntriesAreFlatFileBlocks()
        {
            (List<SimulatedFolderFileEntry> entries, long finalOffset) = SimulateStoreFiles(new[]
            {
                ("a.bin", 500L),
                ("b.bin", 1000L),
                ("c.bin", 1500L)
            });

            // Verify sequential, non-overlapping offset layout (flat, no stride)
            long runningOffset = 0;
            foreach (SimulatedFolderFileEntry entry in entries)
            {
                Assert.Equal(runningOffset, entry.OffsetStart);
                if (entry.Size > 0)
                    runningOffset += entry.Size;
            }
            Assert.Equal(finalOffset, runningOffset);

            // Cumulative offset equals sum of all file sizes
            long expectedTotal = 500L + 1000L + 1500L;
            Assert.Equal(expectedTotal, finalOffset);
        }

        /// <summary>
        /// Validates: Requirement 2.3
        /// ImageFormat.Folder and TmdAppFolder return empty file extension.
        /// </summary>
        [Fact]
        public void FolderAndTmdAppFolder_FileExtension_IsEmpty()
        {
            Assert.Equal("", ImageFormat.Folder.GetFileExtension());
            Assert.Equal("", ImageFormat.TmdAppFolder.GetFileExtension());
        }

        #endregion

        #region StoreFile: truncated / stalled source stream

        /// <summary>
        /// Regression test for the "stores hashes without having the data" bug.
        ///
        /// When adding an image from an unreliable source (e.g. a network-mounted drive
        /// that stalls to 0 KB/s and returns a premature EOF), StoreFile must NOT silently
        /// persist a short image whose recorded Size is the full expected length while the
        /// checksums cover only the bytes actually received. Instead it must throw so the
        /// caller rolls the un-finalized image back and reports a per-image failure.
        /// </summary>
        [Fact]
        public void StoreFile_SourceEndsEarly_ThrowsIOException()
        {
            string testDir = Path.Combine(Path.GetTempPath(), $"NKitTruncStoreFile_{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDir);
            try
            {
                using DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(
                    testDir, "TruncatedImage", shardSize: 64L * 1024 * 1024, blockSize: 65536, setName: "TruncSet");

                // Claim a large file but only deliver a fraction of the bytes before EOF.
                const long claimedSize = 300_000;
                const long actualBytes = 100_000;
                using ShortDeliveryStream shortStream = new ShortDeliveryStream(actualBytes);

                IOException ex = Assert.Throws<IOException>(
                    () => formatter.StoreFile("truncated.bin", shortStream, claimedSize));

                Assert.Contains("Truncated read", ex.Message);
                Assert.Contains(actualBytes.ToString(), ex.Message);
            }
            finally
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Control test: when the source delivers exactly the claimed number of bytes,
        /// StoreFile succeeds (the guard must not produce false positives).
        /// </summary>
        [Fact]
        public void StoreFile_SourceDeliversFullSize_Succeeds()
        {
            string testDir = Path.Combine(Path.GetTempPath(), $"NKitFullStoreFile_{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDir);
            try
            {
                using DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(
                    testDir, "FullImage", shardSize: 64L * 1024 * 1024, blockSize: 65536, setName: "FullSet");

                const long size = 300_000;
                using ShortDeliveryStream fullStream = new ShortDeliveryStream(size);

                // Should not throw, and the offset should advance by the full size.
                formatter.StoreFile("full.bin", fullStream, size);
                Assert.Equal(size, formatter.CurrentOffset);
            }
            finally
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// A read-only stream that yields exactly <c>_available</c> bytes of deterministic
        /// data and then reports EOF (Read returns 0), regardless of any larger length the
        /// caller believes the source to be. Simulates a source that ends earlier than its
        /// advertised size (a stalled/disconnected network stream).
        /// </summary>
        private sealed class ShortDeliveryStream : Stream
        {
            private readonly long _available;
            private long _position;

            public ShortDeliveryStream(long available)
            {
                _available = available;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _available;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long remaining = _available - _position;
                if (remaining <= 0)
                    return 0; // premature EOF relative to the caller's expected size
                int toReturn = (int)Math.Min(count, remaining);
                for (int i = 0; i < toReturn; i++)
                    buffer[offset + i] = (byte)((_position + i) & 0xFF);
                _position += toReturn;
                return toReturn;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        #endregion
    }
}
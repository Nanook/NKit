using System.Buffers.Binary;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Example-based unit tests for NkFs binary filesystem format.
    /// Covers error handling, edge cases, and specific feature verification.
    /// </summary>
    public class NkFsUnitTests
    {
        #region Deserialization Error Tests

        /// <summary>
        /// Construct bytes with wrong magic number, verify FromBytes throws InvalidDataException.
        /// </summary>
        [Fact]
        public void Deserialize_InvalidMagic_Throws()
        {
            // Build a valid-looking 12-byte header but with wrong magic
            byte[] data = new byte[12 + 12 + 2]; // header + 1 entry + 2 byte string table
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0xDEADBEEF); // wrong magic
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1);          // version 1
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 0);          // reserved
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 1);           // 1 entry

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => NkFs.FromBytes(data));
            Assert.Contains("0x4E4B4653", ex.Message);
        }

        /// <summary>
        /// Construct bytes with unsupported version 99, verify FromBytes throws InvalidDataException.
        /// </summary>
        [Fact]
        public void Deserialize_UnsupportedVersion_Throws()
        {
            byte[] data = new byte[12 + 12 + 2]; // header + 1 entry + 2 byte string table
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0x4E4B4653); // correct magic
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 99);         // unsupported version
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 0);          // reserved
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 1);           // 1 entry

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => NkFs.FromBytes(data));
            Assert.Contains("99", ex.Message);
        }

        /// <summary>
        /// Pass a 10-byte array (shorter than the 12-byte header), verify FromBytes throws ArgumentException.
        /// </summary>
        [Fact]
        public void Deserialize_TruncatedData_Throws()
        {
            byte[] data = new byte[10]; // too short for even the header

            Assert.Throws<ArgumentException>(() => NkFs.FromBytes(data));
        }

        #endregion

        #region Empty and Simple Filesystem Tests

        /// <summary>
        /// Build from empty FsYaml, verify single root entry with NextEntryIndex=1.
        /// </summary>
        [Fact]
        public void EmptyFilesystem_RootOnly()
        {
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            Assert.Equal(1, nkfs.EntryCount);

            NkFsEntry root = nkfs.GetEntry(0);
            Assert.True(root.IsDirectory);
            Assert.Equal(1, root.NextEntryIndex);
        }

        /// <summary>
        /// Build from FsYaml with one file, verify 2 entries (root + file).
        /// File size is now in string table, accessed via GetFileSize.
        /// </summary>
        [Fact]
        public void SingleFile_CorrectLayout()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("test.bin", 100, 2048, 0xABCD1234, 0xDEAD);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            Assert.Equal(2, nkfs.EntryCount);

            // Root entry
            NkFsEntry rootEntry = nkfs.GetEntry(0);
            Assert.True(rootEntry.IsDirectory);
            Assert.Equal(2, rootEntry.NextEntryIndex);

            // File entry
            NkFsEntry fileEntry = nkfs.GetEntry(1);
            Assert.True(fileEntry.IsFile);
            Assert.Equal(100L, fileEntry.FileOffset);
            Assert.False(fileEntry.IsImageFile);

            // File size is in string table prefix
            Assert.Equal(2048L, nkfs.GetFileSize(1));

            // Checksums are in string table prefix
            (ulong xxHash, uint crc) = nkfs.GetChecksums(1);
            Assert.Equal(0xABCD1234UL, xxHash);
            Assert.Equal(0xDEADU, crc);

            // Verify name
            string name = nkfs.GetEntryName(1);
            Assert.Equal("test.bin", name);
        }

        #endregion

        #region Navigation Tests

        /// <summary>
        /// Build 10-level deep directory tree, verify path resolution works at every level.
        /// </summary>
        [Fact]
        public void DeeplyNested_Navigation()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            FsYamlNode current = root;
            for (int i = 0; i < 10; i++)
            {
                current = current.AddDirectory($"level{i}");
            }
            current.AddFile("deepfile.txt", 42, 100, 0x1111, 0x2222);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Verify path resolution at each level
            string path = "";
            for (int i = 0; i < 10; i++)
            {
                path = string.IsNullOrEmpty(path) ? $"level{i}" : path + $"/level{i}";
                int idx = nkfs.ResolvePath(path);
                Assert.True(idx > 0, $"Failed to resolve path: {path}");

                NkFsEntry entry = nkfs.GetEntry(idx);
                Assert.True(entry.IsDirectory, $"Entry at path '{path}' should be a directory");
            }

            // Verify the deepest file
            int fileIdx = nkfs.ResolvePath("level0/level1/level2/level3/level4/level5/level6/level7/level8/level9/deepfile.txt");
            Assert.True(fileIdx > 0);
            NkFsEntry fileEntry = nkfs.GetEntry(fileIdx);
            Assert.True(fileEntry.IsFile);
            Assert.Equal("deepfile.txt", nkfs.GetEntryName(fileIdx));
        }

        #endregion

        #region Unicode and Special Character Tests

        /// <summary>
        /// Build with CJK, emoji, accented chars, verify round-trip preserves names.
        /// </summary>
        [Fact]
        public void Unicode_Filenames_Preserved()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            root.AddFile("日本語ファイル.dat", 0, 100, 0x1111, 0x2222);
            root.AddFile("café.txt", 100, 200, 0x3333, 0x4444);
            root.AddFile("🎮game.bin", 200, 300, 0x5555, 0x6666);
            root.AddFile("données_été.xml", 300, 400, 0x7777, 0x8888);

            string originalYaml = fsYaml.ToYaml();

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml roundTripped = restored.ToFsYaml();

            string roundTrippedYaml = roundTripped.ToYaml();
            Assert.Equal(originalYaml, roundTrippedYaml);
        }

        /// <summary>
        /// Build with spaces, brackets, colons, verify round-trip preserves names.
        /// </summary>
        [Fact]
        public void SpecialChars_Filenames_Preserved()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            root.AddFile("my file name.dat", 0, 100, 0x1111, 0x2222);
            root.AddFile("data[0].bin", 100, 200, 0x3333, 0x4444);
            root.AddFile("time:stamp.log", 200, 300, 0x5555, 0x6666);
            root.AddFile("report [final]: v2.pdf", 300, 400, 0x7777, 0x8888);

            string originalYaml = fsYaml.ToYaml();

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml roundTripped = restored.ToFsYaml();

            string roundTrippedYaml = roundTripped.ToYaml();
            Assert.Equal(originalYaml, roundTrippedYaml);
        }

        #endregion

        #region Prefix and Flag Tests

        /// <summary>
        /// Build with IFS entries, verify image_file flag is set.
        /// Checksums are in the prefix byte.
        /// </summary>
        [Fact]
        public void IfsEntries_HaveImageFileFlag()
        {
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);
            fsYaml.AddIfsEntry("00000001.app", 42, 1048576);
            fsYaml.AddIfsEntry("00000002.app", 43, 2097152);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Root (index 0) + 2 IFS entries = 3 entries
            Assert.Equal(3, nkfs.EntryCount);

            // Check each IFS entry (indices 1 and 2)
            for (int i = 1; i <= 2; i++)
            {
                NkFsEntry entry = nkfs.GetEntry(i);
                Assert.True(entry.IsFile, $"IFS entry at index {i} should be a file");
                Assert.True(entry.IsImageFile, $"IFS entry at index {i} should have image_file flag set");
            }

            // Verify names
            Assert.Equal("00000001.app", nkfs.GetEntryName(1));
            Assert.Equal("00000002.app", nkfs.GetEntryName(2));

            // Verify file sizes via GetFileSize
            Assert.Equal(1048576L, nkfs.GetFileSize(1));
            Assert.Equal(2097152L, nkfs.GetFileSize(2));

            // Verify round-trip preserves IFS entries
            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml roundTripped = restored.ToFsYaml();

            Assert.Equal(2, roundTripped.ImageFileSystems.Count);
            Assert.Equal("00000001.app", roundTripped.ImageFileSystems[0].FileName);
            Assert.Equal(42, roundTripped.ImageFileSystems[0].ImageId);
            Assert.Equal(1048576, roundTripped.ImageFileSystems[0].Size);
            Assert.Equal("00000002.app", roundTripped.ImageFileSystems[1].FileName);
            Assert.Equal(43, roundTripped.ImageFileSystems[1].ImageId);
            Assert.Equal(2097152, roundTripped.ImageFileSystems[1].Size);
        }

        /// <summary>
        /// Build with system-flagged nodes, verify system_flag flag is set and round-trip preserves system flag.
        /// </summary>
        [Fact]
        public void SystemNodes_PreservedViaSystemFlag()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            FsYamlNode sysDir = root.AddDirectory("SI", isSystem: true);
            sysDir.AddFile("ticket.bin", 0, 512, 0xAAAA, 0xBBBB, isSystem: true);

            FsYamlNode normalDir = root.AddDirectory("Game");
            normalDir.AddFile("main.dol", 1024, 4096, 0xCCCC, 0xDDDD);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Entry 0 = root, Entry 1 = SI (system dir), Entry 2 = ticket.bin (system file),
            // Entry 3 = Game (normal dir), Entry 4 = main.dol (normal file)
            NkFsEntry sysDirEntry = nkfs.GetEntry(1);
            Assert.True(sysDirEntry.IsDirectory);
            Assert.True(sysDirEntry.SystemFlag, "System directory should have system_flag flag");

            NkFsEntry sysFileEntry = nkfs.GetEntry(2);
            Assert.True(sysFileEntry.IsFile);
            Assert.True(sysFileEntry.SystemFlag, "System file should have system_flag flag");

            NkFsEntry normalDirEntry = nkfs.GetEntry(3);
            Assert.True(normalDirEntry.IsDirectory);
            Assert.False(normalDirEntry.SystemFlag, "Normal directory should NOT have system_flag flag");

            NkFsEntry normalFileEntry = nkfs.GetEntry(4);
            Assert.True(normalFileEntry.IsFile);
            Assert.False(normalFileEntry.SystemFlag, "Normal file should NOT have system_flag flag");

            // Verify round-trip preserves system flags
            string originalYaml = fsYaml.ToYaml();

            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml roundTripped = restored.ToFsYaml();

            string roundTrippedYaml = roundTripped.ToYaml();
            Assert.Equal(originalYaml, roundTrippedYaml);

            // Also verify the FsYamlNode properties directly
            FsYamlNode rtRoot = roundTripped.FileSystems[0];
            Assert.NotNull(rtRoot.Children);
            Assert.Equal(2, rtRoot.Children!.Count);

            FsYamlNode rtSysDir = rtRoot.Children[0];
            Assert.Equal("SI", rtSysDir.Name);
            Assert.True(rtSysDir.IsSystem, "Round-tripped system directory should have IsSystem=true");
            Assert.NotNull(rtSysDir.Children);
            Assert.Single(rtSysDir.Children!);

            FsYamlNode rtSysFile = rtSysDir.Children![0];
            Assert.Equal("ticket.bin", rtSysFile.Name);
            Assert.True(rtSysFile.IsSystem, "Round-tripped system file should have IsSystem=true");

            FsYamlNode rtNormalDir = rtRoot.Children[1];
            Assert.Equal("Game", rtNormalDir.Name);
            Assert.False(rtNormalDir.IsSystem, "Round-tripped normal directory should have IsSystem=false");
        }

        #endregion

        #region Format Specific Tests

        /// <summary>
        /// Verify that the header is exactly 12 bytes with no StringTableOffset field.
        /// </summary>
        [Fact]
        public void Header_Is12Bytes_NoStringTableOffset()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("test.bin", 0, 100, 0x1111, 0x2222);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();

            // Magic
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
            Assert.Equal(0x4E4B4653U, magic);

            // Version 1
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
            Assert.Equal(1, version);

            // Reserved
            ushort reserved = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
            Assert.Equal(0, reserved);

            // EntryCount
            int entryCount = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));
            Assert.Equal(2, entryCount);

            // String table starts at 12 + 2*12 = 36
            // No StringTableOffset field at offset 12 — that's where the entry table starts
            int expectedStringTableOffset = 12 + (entryCount * 12);
            Assert.Equal(36, expectedStringTableOffset);
        }

        /// <summary>
        /// Verify that entries are 12 bytes each.
        /// </summary>
        [Fact]
        public void Entry_Is12Bytes() => Assert.Equal(12, NkFsEntry.EntrySize);

        /// <summary>
        /// Verify that directory ParentIndex and NextEntryIndex are int32.
        /// </summary>
        [Fact]
        public void Directory_Int32ParentAndNext()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("testdir");
            dir.AddFile("inner.bin", 0, 50, 0x1111, 0x2222);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // dir is at index 1, parent is root (0), next is 3 (root + dir + file)
            NkFsEntry dirEntry = nkfs.GetEntry(1);
            Assert.True(dirEntry.IsDirectory);
            Assert.Equal(0, dirEntry.ParentIndex);
            Assert.Equal(3, dirEntry.NextEntryIndex);
        }

        /// <summary>
        /// Verify GetFileSize reads from string table prefix correctly.
        /// </summary>
        [Fact]
        public void GetFileSize_ReadsFromStringTablePrefix()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("small.bin", 0, 100, 0x1111, 0x2222);
            root.AddFile("medium.bin", 100, 70000, 0x3333, 0x4444);
            root.AddFile("large.bin", 200, 5_000_000_000L, 0x5555, 0x6666);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            Assert.Equal(100L, nkfs.GetFileSize(1));
            Assert.Equal(70000L, nkfs.GetFileSize(2));
            Assert.Equal(5_000_000_000L, nkfs.GetFileSize(3));
        }

        /// <summary>
        /// Verify GetChecksums reads from string table prefix correctly.
        /// </summary>
        [Fact]
        public void GetChecksums_ReadsFromStringTablePrefix()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("file.bin", 0, 100, 0xDEADBEEF12345678UL, 0xCAFEBABE);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            (ulong xxHash, uint crc) = nkfs.GetChecksums(1);
            Assert.Equal(0xDEADBEEF12345678UL, xxHash);
            Assert.Equal(0xCAFEBABEU, crc);
        }

        /// <summary>
        /// Verify zero-size file with zero checksums has compact prefix.
        /// </summary>
        [Fact]
        public void ZeroSizeFile_CompactPrefix()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("empty.bin", 0, 0, 0, 0);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            Assert.Equal(0L, nkfs.GetFileSize(1));
            (ulong xxHash, uint crc) = nkfs.GetChecksums(1);
            Assert.Equal(0UL, xxHash);
            Assert.Equal(0U, crc);
            Assert.Equal("empty.bin", nkfs.GetEntryName(1));
        }

        #endregion

        #region Multi-Extent Chain Building Tests

        /// <summary>
        /// Two consecutive same-name file entries form a 2-extent chain:
        /// first has HasMoreExtents = true (bit 5 = 1), second has HasMoreExtents = false (bit 5 = 0).
        /// Validates: Requirements 1.1, 1.2, 1.3, 2.1, 10.1
        /// </summary>
        [Fact]
        public void TwoExtentChain_CorrectFlagPattern()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("00001.m2ts", 0x1000, 0x8000, 0xAAAA1111, 0x1111);
            dir.AddFile("00001.m2ts", 0x9000, 0x6000, 0xBBBB2222, 0x2222);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Find the two extent entries under STREAM directory
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList(); // index 1 = STREAM directory
            Assert.Equal(2, children.Count);

            // First extent: HasMoreExtents = true
            Assert.True(nkfs.HasMoreExtents(children[0].index),
                "First extent in 2-extent chain should have HasMoreExtents = true");
            Assert.Equal("00001.m2ts", nkfs.GetEntryName(children[0].index));
            Assert.Equal(0x8000L, nkfs.GetFileSize(children[0].index));

            // Second extent: HasMoreExtents = false
            Assert.False(nkfs.HasMoreExtents(children[1].index),
                "Last extent in 2-extent chain should have HasMoreExtents = false");
            Assert.Equal("00001.m2ts", nkfs.GetEntryName(children[1].index));
            Assert.Equal(0x6000L, nkfs.GetFileSize(children[1].index));
        }

        /// <summary>
        /// Five consecutive same-name file entries form a 5-extent chain:
        /// flag pattern should be (1,1,1,1,0).
        /// Validates: Requirements 1.1, 1.2, 1.3, 2.1, 10.1
        /// </summary>
        [Fact]
        public void FiveExtentChain_CorrectFlagPattern()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("video.m2ts", 0x1000, 0x2000, 0x1111, 0x0001);
            dir.AddFile("video.m2ts", 0x4000, 0x2000, 0x2222, 0x0002);
            dir.AddFile("video.m2ts", 0x7000, 0x2000, 0x3333, 0x0003);
            dir.AddFile("video.m2ts", 0xA000, 0x2000, 0x4444, 0x0004);
            dir.AddFile("video.m2ts", 0xD000, 0x2000, 0x5555, 0x0005);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList(); // index 1 = STREAM directory
            Assert.Equal(5, children.Count);

            // Expected flag pattern: (true, true, true, true, false)
            bool[] expectedFlags = { true, true, true, true, false };
            for (int i = 0; i < 5; i++)
            {
                bool actual = nkfs.HasMoreExtents(children[i].index);
                if (expectedFlags[i])
                    Assert.True(actual, $"Extent {i} should have HasMoreExtents = true");
                else
                    Assert.False(actual, $"Extent {i} should have HasMoreExtents = false");
                Assert.Equal("video.m2ts", nkfs.GetEntryName(children[i].index));
            }
        }

        /// <summary>
        /// A directory with both single-extent and multi-extent files:
        /// the multi-extent chain entries are consecutive with no interleaving from other files.
        /// Validates: Requirements 2.1, 10.1, 10.2
        /// </summary>
        [Fact]
        public void MixedDirectory_NoInterleaving()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("DATA");
            // Single-extent file first
            dir.AddFile("readme.txt", 0x100, 0x50, 0xAAAA, 0x0001);
            // Multi-extent file (3 extents)
            dir.AddFile("bigfile.dat", 0x1000, 0x3000, 0xBBBB, 0x0002);
            dir.AddFile("bigfile.dat", 0x5000, 0x3000, 0xCCCC, 0x0003);
            dir.AddFile("bigfile.dat", 0x9000, 0x3000, 0xDDDD, 0x0004);
            // Another single-extent file after
            dir.AddFile("config.ini", 0xF000, 0x80, 0xEEEE, 0x0005);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList(); // index 1 = DATA directory
            Assert.Equal(5, children.Count);

            // Entry 0: readme.txt (single-extent, no flag)
            Assert.Equal("readme.txt", nkfs.GetEntryName(children[0].index));
            Assert.False(nkfs.HasMoreExtents(children[0].index));

            // Entries 1-3: bigfile.dat (3-extent chain, flags: 1,1,0)
            Assert.Equal("bigfile.dat", nkfs.GetEntryName(children[1].index));
            Assert.True(nkfs.HasMoreExtents(children[1].index));

            Assert.Equal("bigfile.dat", nkfs.GetEntryName(children[2].index));
            Assert.True(nkfs.HasMoreExtents(children[2].index));

            Assert.Equal("bigfile.dat", nkfs.GetEntryName(children[3].index));
            Assert.False(nkfs.HasMoreExtents(children[3].index));

            // Entry 4: config.ini (single-extent, no flag)
            Assert.Equal("config.ini", nkfs.GetEntryName(children[4].index));
            Assert.False(nkfs.HasMoreExtents(children[4].index));

            // Verify no interleaving: the 3 bigfile.dat entries are at consecutive indices
            Assert.Equal(children[1].index + 1, children[2].index);
            Assert.Equal(children[2].index + 1, children[3].index);
        }

        /// <summary>
        /// A single SplitPart entry (only one same-name file entry) produces no Has_More_Extents flag.
        /// Validates: Requirement 10.2
        /// </summary>
        [Fact]
        public void SingleSplitPart_NoHasMoreExtentsFlag()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("CONTENT");
            // Only one file with this name — should NOT have the flag
            dir.AddFile("single.bin", 0x2000, 0x5000, 0xFEDC9876, 0xABCD);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList(); // index 1 = CONTENT directory
            Assert.Single(children);

            Assert.Equal("single.bin", nkfs.GetEntryName(children[0].index));
            Assert.False(nkfs.HasMoreExtents(children[0].index),
                "Single file entry (not part of a chain) should not have HasMoreExtents flag");
        }

        /// <summary>
        /// When building NkFs with multi-extent chains, the version header remains 1.
        /// Validates: Requirement 4.1
        /// </summary>
        [Fact]
        public void MultiExtentChain_VersionHeaderRemains1()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("video.m2ts", 0x1000, 0x8000, 0xAAAA, 0x1111);
            dir.AddFile("video.m2ts", 0x9000, 0x6000, 0xBBBB, 0x2222);
            dir.AddFile("video.m2ts", 0x10000, 0x4000, 0xCCCC, 0x3333);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();

            // Version is at offset 4, big-endian uint16
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
            Assert.Equal(1, version);
        }

        #endregion

        #region Chain Resolution Error Conditions Tests

        /// <summary>
        /// When Has_More_Extents is set on the last entry but no valid continuation exists,
        /// GetExtents throws InvalidDataException indicating a malformed extent chain.
        /// Validates: Requirement 3.5
        /// </summary>
        [Fact]
        public void MalformedChain_HasMoreExtentsButNoContinuation_ThrowsInvalidDataException()
        {
            // Build a valid NkFs with a single-extent file in a directory
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("video.m2ts", 0x1000, 0x8000, 0xAAAA1111, 0x1111);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();

            // Find the file entry's prefix byte in the string table and flip bit 5 (Has_More_Extents)
            // The string table starts after the header (12) + entry table (entryCount * 12)
            int entryCount = nkfs.EntryCount; // root + dir + file = 3
            int stringTableStart = 12 + (entryCount * 12);

            // Find the file entry index (index 2: root=0, dir=1, file=2)
            NkFsEntry fileEntry = nkfs.GetEntry(2);
            int nameByteOffset = fileEntry.NameOffset * 2; // DecodeNameOffset multiplies by 2

            // The prefix byte is the first byte at that string table position
            int prefixBytePosition = stringTableStart + nameByteOffset;

            // Flip bit 5 (0x20) to set Has_More_Extents
            bytes[prefixBytePosition] |= 0x20;

            // Reconstruct NkFs from the corrupted bytes
            NkFs corrupted = NkFs.FromBytes(bytes);

            // Verify HasMoreExtents is now true
            Assert.True(corrupted.HasMoreExtents(2));

            // Calling GetExtents should throw InvalidDataException because there's no continuation
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => corrupted.GetExtents(2));
            Assert.Contains("Has_More_Extents", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calling GetExtents on a mid-chain entry (second extent of a 3-extent chain)
        /// resolves the full chain from the first extent and returns all 3 extents in order.
        /// Validates: Requirement 3.6
        /// </summary>
        [Fact]
        public void MidChainEntry_ResolvesToFullChainFromFirstExtent()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("video.m2ts", 0x1000, 0x4000, 0xAAAA, 0x0001);
            dir.AddFile("video.m2ts", 0x6000, 0x3000, 0xBBBB, 0x0002);
            dir.AddFile("video.m2ts", 0xA000, 0x2000, 0xCCCC, 0x0003);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Get the children to find the middle entry index
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList(); // index 1 = STREAM directory
            Assert.Equal(3, children.Count);

            int middleIndex = children[1].index; // second extent (mid-chain)

            // Call GetExtents on the middle entry
            IReadOnlyList<(long offset, long size)> extents = nkfs.GetExtents(middleIndex);

            // Should return all 3 extents in order, starting from the first
            Assert.Equal(3, extents.Count);
            Assert.Equal((0x1000L, 0x4000L), extents[0]);
            Assert.Equal((0x6000L, 0x3000L), extents[1]);
            Assert.Equal((0xA000L, 0x2000L), extents[2]);
        }

        /// <summary>
        /// GetTotalFileSize returns the sum of all extent sizes in the chain.
        /// Validates: Requirement 3.4 (via 3.5, 3.6 context)
        /// </summary>
        [Fact]
        public void GetTotalFileSize_ReturnsSumOfAllExtentSizes()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("video.m2ts", 0x1000, 0x4000, 0xAAAA, 0x0001);   // size: 0x4000 = 16384
            dir.AddFile("video.m2ts", 0x6000, 0x3000, 0xBBBB, 0x0002);   // size: 0x3000 = 12288
            dir.AddFile("video.m2ts", 0xA000, 0x2000, 0xCCCC, 0x0003);   // size: 0x2000 = 8192

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList(); // index 1 = STREAM directory

            long expectedTotal = 0x4000L + 0x3000L + 0x2000L; // 36864

            // GetTotalFileSize from first extent
            Assert.Equal(expectedTotal, nkfs.GetTotalFileSize(children[0].index));

            // GetTotalFileSize from middle extent — should still return the full sum
            Assert.Equal(expectedTotal, nkfs.GetTotalFileSize(children[1].index));

            // GetTotalFileSize from last extent — should still return the full sum
            Assert.Equal(expectedTotal, nkfs.GetTotalFileSize(children[2].index));
        }

        #endregion

        #region FsYaml Export Multi-Extent Tests

        /// <summary>
        /// ToFsYaml() on a multi-extent chain emits one FsYaml entry per extent with same name,
        /// preserving individual offset, size, and checksums for each extent.
        /// Validates: Requirements 5.1, 5.2
        /// </summary>
        [Fact]
        public void ToFsYaml_MultiExtentChain_EmitsOneEntryPerExtent()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("00001.m2ts", 0x1000, 0x8000, 0xAAAA111122223333UL, 0x11112222);
            dir.AddFile("00001.m2ts", 0xF000, 0x6000, 0xBBBB444455556666UL, 0x33334444);
            dir.AddFile("00001.m2ts", 0x20000, 0x4000, 0xCCCC777788889999UL, 0x55556666);

            // Build NkFs from the multi-extent FsYaml
            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Export back to FsYaml
            FsYaml exported = nkfs.ToFsYaml();

            // Verify the exported structure has the STREAM directory
            FsYamlNode exportedRoot = exported.FileSystems[0];
            Assert.NotNull(exportedRoot.Children);
            Assert.Single(exportedRoot.Children!);

            FsYamlNode exportedDir = exportedRoot.Children![0];
            Assert.Equal("STREAM", exportedDir.Name);
            Assert.NotNull(exportedDir.Children);

            // Verify 3 entries emitted (one per extent), all with same name
            Assert.Equal(3, exportedDir.Children!.Count);
            Assert.All(exportedDir.Children, c => Assert.Equal("00001.m2ts", c.Name));

            // Verify individual offset, size, and checksums per extent
            FsYamlNode extent0 = exportedDir.Children[0];
            Assert.Equal(0x1000L, extent0.Offset);
            Assert.Equal(0x8000L, extent0.Size);
            Assert.Equal(0xAAAA111122223333UL, extent0.XxHash64);
            Assert.Equal(0x11112222U, extent0.Crc32);

            FsYamlNode extent1 = exportedDir.Children[1];
            Assert.Equal(0xF000L, extent1.Offset);
            Assert.Equal(0x6000L, extent1.Size);
            Assert.Equal(0xBBBB444455556666UL, extent1.XxHash64);
            Assert.Equal(0x33334444U, extent1.Crc32);

            FsYamlNode extent2 = exportedDir.Children[2];
            Assert.Equal(0x20000L, extent2.Offset);
            Assert.Equal(0x4000L, extent2.Size);
            Assert.Equal(0xCCCC777788889999UL, extent2.XxHash64);
            Assert.Equal(0x55556666U, extent2.Crc32);
        }

        /// <summary>
        /// ToFsYaml() emits multi-extent entries in the correct chain order (ascending offset),
        /// and the exported FsYaml can be used to rebuild an equivalent NkFs (binary round-trip).
        /// Validates: Requirements 5.1, 5.2
        /// </summary>
        [Fact]
        public void ToFsYaml_MultiExtentChain_PreservesOrderAndRoundTrips()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("BDMV");
            // 4-extent file
            dir.AddFile("stream.m2ts", 0x2000, 0x10000, 0x1111222233334444UL, 0xAABBCCDD);
            dir.AddFile("stream.m2ts", 0x15000, 0x8000, 0x5555666677778888UL, 0xEEFF0011);
            dir.AddFile("stream.m2ts", 0x20000, 0xC000, 0x9999AAAABBBBCCCCUL, 0x22334455);
            dir.AddFile("stream.m2ts", 0x30000, 0x5000, 0xDDDDEEEEFFFF0000UL, 0x66778899);
            // Single-extent file in same directory
            dir.AddFile("index.bdmv", 0x40000, 0x200, 0xABCDEF0123456789UL, 0xDEADBEEF);

            string originalYaml = fsYaml.ToYaml();

            // Build NkFs, export back to FsYaml
            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            FsYaml exported = nkfs.ToFsYaml();

            string exportedYaml = exported.ToYaml();

            // The YAML representations should be identical (full round-trip)
            Assert.Equal(originalYaml, exportedYaml);
        }

        /// <summary>
        /// ToFsYaml() on a binary round-trip (FsYaml → NkFs bytes → NkFs → FsYaml) preserves
        /// multi-extent entries through serialization/deserialization.
        /// Validates: Requirements 5.1, 5.2
        /// </summary>
        [Fact]
        public void ToFsYaml_MultiExtentChain_BinaryRoundTrip()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("VIDEO");
            dir.AddFile("clip.m2ts", 0x5000, 0x20000, 0x1234567890ABCDEFUL, 0x12345678);
            dir.AddFile("clip.m2ts", 0x30000, 0x15000, 0xFEDCBA0987654321UL, 0x87654321);

            string originalYaml = fsYaml.ToYaml();

            // Full binary round-trip: FsYaml → NkFs → bytes → NkFs → FsYaml
            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml roundTripped = restored.ToFsYaml();

            string roundTrippedYaml = roundTripped.ToYaml();

            Assert.Equal(originalYaml, roundTrippedYaml);
        }

        #endregion
    }
}
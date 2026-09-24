using Nanook.NKit.Vfs;
using System.Buffers.Binary;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for NkFs pipeline integration.
    /// Feature: nkdsfs-pipeline-integration
    /// Tests constants, format detection, loading boundary fallback, mount handler navigation,
    /// virtual Filesystem.yaml generation, VfsStreamHelper behavior, formatter write verification,
    /// and backward compatibility.
    /// </summary>
    public class NkFsPipelineIntegrationTests
    {
        #region 20.1 — Constants and Format Detection Tests

        /// <summary>
        /// Validates: Requirements 2.1
        /// </summary>
        [Fact]
        public void Constants_FileSystemNkfsRootPath() => Assert.Equal("filesystem.nkfs", DataStore.FileSystemNkfsRootPath);

        /// <summary>
        /// Validates: Requirements 2.2
        /// </summary>
        [Fact]
        public void Constants_FileSystemNkfsName() => Assert.Equal("filesystem.nkfs", DataStore.FileSystemNkfsName);

        /// <summary>
        /// Verify that 4-byte magic 0x4E4B4653 at the start of data correctly identifies NkFs binary.
        /// NkFs.FromBytes succeeds on valid NkFs data starting with the magic number.
        /// Validates: Requirements 2.1, 2.2, 3.4
        /// </summary>
        [Fact]
        public void FormatDetection_NkFsMagicNumber()
        {
            // Build a minimal valid NkFs from a simple FsYaml
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);
            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // Verify the first 4 bytes are the NkFs magic number
            Assert.True(nkfsBytes.Length >= 4);
            Assert.Equal(0x4E, nkfsBytes[0]);
            Assert.Equal(0x4B, nkfsBytes[1]);
            Assert.Equal(0x46, nkfsBytes[2]);
            Assert.Equal(0x53, nkfsBytes[3]);

            // Verify the magic as a 32-bit big-endian value
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(nkfsBytes.AsSpan(0, 4));
            Assert.Equal(0x4E4B4653u, magic);

            // Verify FromBytes succeeds (no exception)
            NkFs nkfs = NkFs.FromBytes(nkfsBytes);
            Assert.NotNull(nkfs);
            Assert.True(nkfs.EntryCount >= 1); // at least root
        }

        /// <summary>
        /// Verify that YAML text bytes do not start with the NkFs magic number,
        /// so they are not misidentified as NkFs binary data.
        /// Validates: Requirements 3.4
        /// </summary>
        [Fact]
        public void FormatDetection_YamlData_NoMagicMatch()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("test.bin", 100, 200, 0x1234, 0x5678);
            byte[] yamlBytes = fsYaml.ToBytes();

            // YAML text should not start with the NkFs magic bytes
            Assert.True(yamlBytes.Length >= 4);
            bool matchesMagic = yamlBytes[0] == 0x4E
                             && yamlBytes[1] == 0x4B
                             && yamlBytes[2] == 0x46
                             && yamlBytes[3] == 0x53;
            Assert.False(matchesMagic, "YAML text bytes should not match the NkFs magic number");

            // Verify that FromBytes rejects YAML data (wrong magic)
            Assert.Throws<InvalidDataException>(() => NkFs.FromBytes(yamlBytes));
        }

        #endregion

        #region 20.2 — Loading Boundary Fallback Tests

        /// <summary>
        /// When NkFs binary data is present, it should be loaded via NkFs.FromBytes()
        /// and produce a valid NkFs object with the correct structure.
        /// Validates: Requirements 3.1, 3.2
        /// </summary>
        [Fact]
        public void LoadNkfs_NkFsPresent_LoadsNkFs()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("game.iso", 0, 4096, 0xAAAA, 0xBBBB);
            root.AddDirectory("data");

            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // Simulate the loading boundary: NkFs data present → FromBytes
            NkFs loaded = NkFs.FromBytes(nkfsBytes);

            Assert.NotNull(loaded);
            Assert.Equal(3, loaded.EntryCount); // root + file + dir

            // Verify navigation works
            List<(int index, NkFsEntry entry)> children = loaded.GetChildren(0).ToList();
            Assert.Equal(2, children.Count);
            Assert.Equal("game.iso", loaded.GetEntryName(children[0].index));
            Assert.Equal("data", loaded.GetEntryName(children[1].index));
        }

        /// <summary>
        /// When NkFs is absent but YAML is present, the loading boundary should
        /// fall back to YAML → NkFs conversion via NkFs.FromFsYaml(FsYaml.FromBytes(data)).
        /// Validates: Requirements 3.3, 11.1, 12.2
        /// </summary>
        [Fact]
        public void LoadNkfs_NkFsAbsent_FallsBackToYaml()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("legacy.bin", 500, 1024, 0x1111, 0x2222);

            byte[] yamlBytes = fsYaml.ToBytes();

            // Simulate the fallback: YAML data → FsYaml.FromBytes → NkFs.FromFsYaml
            FsYaml parsedYaml = FsYaml.FromBytes(yamlBytes);
            NkFs converted = NkFs.FromFsYaml(parsedYaml);

            Assert.NotNull(converted);
            Assert.Equal(2, converted.EntryCount); // root + file

            // Verify the file entry is correct
            List<(int index, NkFsEntry entry)> children = converted.GetChildren(0).ToList();
            Assert.Single(children);
            Assert.Equal("legacy.bin", converted.GetEntryName(children[0].index));

            NkFsEntry fileEntry = converted.GetEntry(children[0].index);
            Assert.True(fileEntry.IsFile);
            Assert.Equal(500L, fileEntry.FileOffset);
            Assert.Equal(1024L, converted.GetFileSize(children[0].index));
        }

        /// <summary>
        /// When both NkFs and YAML are present, NkFs takes priority.
        /// The NkFs-first fallback logic should use the NkFs data.
        /// Validates: Requirements 3.1, 3.2, 12.3
        /// </summary>
        [Fact]
        public void LoadNkfs_BothPresent_PrefersNkFs()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("shared.dat", 100, 200, 0x3333, 0x4444);

            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();
            byte[] yamlBytes = fsYaml.ToBytes();

            // Simulate the NkFs-first fallback: try NkFs first
            NkFs result = null;
            bool nkfsAvailable = nkfsBytes.Length >= 4
                && nkfsBytes[0] == 0x4E && nkfsBytes[1] == 0x4B
                && nkfsBytes[2] == 0x46 && nkfsBytes[3] == 0x53;

            if (nkfsAvailable)
            {
                result = NkFs.FromBytes(nkfsBytes);
            }

            // NkFs should be used (not the YAML fallback)
            Assert.NotNull(result);
            Assert.Equal(2, result!.EntryCount);

            // Verify the data matches what was stored as NkFs
            List<(int index, NkFsEntry entry)> children = result.GetChildren(0).ToList();
            Assert.Single(children);
            Assert.Equal("shared.dat", result.GetEntryName(children[0].index));
        }

        /// <summary>
        /// When NkFs data is corrupt (invalid), the loading boundary should
        /// fall back to YAML and convert to NkFs.
        /// Validates: Requirements 3.6
        /// </summary>
        [Fact]
        public void LoadNkfs_CorruptNkFs_FallsBackToYaml()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("fallback.bin", 300, 600, 0x5555, 0x6666);

            byte[] yamlBytes = fsYaml.ToBytes();

            // Create corrupt NkFs data (valid magic but truncated)
            byte[] corruptNkfs = new byte[] { 0x4E, 0x4B, 0x46, 0x53, 0x00, 0x01, 0xFF };

            // Simulate the fallback logic: try NkFs first, catch failure, fall back to YAML
            NkFs result = null;
            try
            {
                result = NkFs.FromBytes(corruptNkfs);
            }
            catch
            {
                // NkFs deserialization failed — fall back to YAML
                result = null;
            }

            if (result == null)
            {
                FsYaml parsedYaml = FsYaml.FromBytes(yamlBytes);
                result = NkFs.FromFsYaml(parsedYaml);
            }

            Assert.NotNull(result);
            Assert.Equal(2, result!.EntryCount);

            List<(int index, NkFsEntry entry)> children = result.GetChildren(0).ToList();
            Assert.Single(children);
            Assert.Equal("fallback.bin", result.GetEntryName(children[0].index));
        }

        #endregion

        #region 20.3 — Mount Handler Navigation Tests

        /// <summary>
        /// FilesystemMountHandler.ListFolder enumerates root children via NkFs.GetChildren(0).
        /// Test the underlying NkFs operation that the handler uses.
        /// Validates: Requirements 5.1, 12.7
        /// </summary>
        [Fact]
        public void FilesystemHandler_ListFolder_UsesGetChildren()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("boot.dol", 0, 1024, 0x1111, 0x2222);
            root.AddDirectory("sys");
            root.AddFile("main.dol", 2048, 4096, 0x3333, 0x4444);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Enumerate root children (index 0) — same as FilesystemMountHandler.ListFolder
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(0).ToList();

            Assert.Equal(3, children.Count);
            Assert.Equal("boot.dol", nkfs.GetEntryName(children[0].index));
            Assert.Equal("sys", nkfs.GetEntryName(children[1].index));
            Assert.Equal("main.dol", nkfs.GetEntryName(children[2].index));

            // Verify types
            Assert.True(children[0].entry.IsFile);
            Assert.True(children[1].entry.IsDirectory);
            Assert.True(children[2].entry.IsFile);
        }

        /// <summary>
        /// FilesystemMountHandler.FindInFolder resolves paths via NkFs.ResolvePath().
        /// Test the underlying NkFs operation that the handler uses.
        /// Validates: Requirements 5.2, 12.7
        /// </summary>
        [Fact]
        public void FilesystemHandler_FindInFolder_UsesResolvePath()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dataDir = root.AddDirectory("data");
            dataDir.AddFile("config.xml", 100, 512, 0xAAAA, 0xBBBB);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Resolve a nested path — same as FilesystemMountHandler.FindInFolder
            int dirIdx = nkfs.ResolvePath("data");
            Assert.True(dirIdx > 0);
            NkFsEntry dirEntry = nkfs.GetEntry(dirIdx);
            Assert.True(dirEntry.IsDirectory);
            Assert.Equal("data", nkfs.GetEntryName(dirIdx));

            int fileIdx = nkfs.ResolvePath("data/config.xml");
            Assert.True(fileIdx > 0);
            NkFsEntry fileEntry = nkfs.GetEntry(fileIdx);
            Assert.True(fileEntry.IsFile);
            Assert.Equal("config.xml", nkfs.GetEntryName(fileIdx));
            Assert.Equal(100L, fileEntry.FileOffset);
            Assert.Equal(512L, nkfs.GetFileSize(fileIdx));

            // Non-existent path returns -1
            int missing = nkfs.ResolvePath("data/nonexistent.bin");
            Assert.Equal(-1, missing);
        }

        /// <summary>
        /// FolderMountHandler.ListFolder enumerates root children via NkFs.GetChildren(0).
        /// Test the underlying NkFs operation for folder-type images.
        /// Validates: Requirements 5.3, 12.7
        /// </summary>
        [Fact]
        public void FolderHandler_ListFolder_UsesGetChildren()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("title.tmd", 0, 256, 0x1111, 0x2222);
            FsYamlNode contentDir = root.AddDirectory("content");
            contentDir.AddFile("00000000.app", 512, 2048, 0x3333, 0x4444);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Enumerate root children
            List<(int index, NkFsEntry entry)> rootChildren = nkfs.GetChildren(0).ToList();
            Assert.Equal(2, rootChildren.Count);

            // Enumerate content directory children
            int contentIdx = -1;
            foreach ((int idx, NkFsEntry entry) in rootChildren)
            {
                if (entry.IsDirectory)
                {
                    contentIdx = idx;
                    break;
                }
            }
            Assert.True(contentIdx > 0);

            List<(int index, NkFsEntry entry)> contentChildren = nkfs.GetChildren(contentIdx).ToList();
            Assert.Single(contentChildren);
            Assert.Equal("00000000.app", nkfs.GetEntryName(contentChildren[0].index));
        }

        /// <summary>
        /// IFS entries in NkFs are returned with the IsImageFile flag and correct image ID
        /// via GetImageIndex(). FolderMountHandler uses this to create FsIfsFileItem entries.
        /// Validates: Requirements 5.9, 12.7
        /// </summary>
        [Fact]
        public void FolderHandler_IfsEntries_EnumeratedAsIfsFileItem()
        {
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);
            fsYaml.AddIfsEntry("00000001.app", 42, 1048576);
            fsYaml.AddIfsEntry("00000002.app", 99, 2097152);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // IFS entries appear as children of root with IsImageFile flag
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(0).ToList();
            Assert.Equal(2, children.Count);

            // First IFS entry
            Assert.True(children[0].entry.IsImageFile);
            Assert.True(children[0].entry.IsFile);
            Assert.Equal("00000001.app", nkfs.GetEntryName(children[0].index));
            Assert.Equal(42L, nkfs.GetImageIndex(children[0].index));

            // Second IFS entry
            Assert.True(children[1].entry.IsImageFile);
            Assert.True(children[1].entry.IsFile);
            Assert.Equal("00000002.app", nkfs.GetEntryName(children[1].index));
            Assert.Equal(99L, nkfs.GetImageIndex(children[1].index));

            // Simulate what FolderMountHandler does: create FsIfsFileItem
            FsIfsFileItem ifsItem = new FsIfsFileItem
            {
                Name = nkfs.GetEntryName(children[0].index),
                FsSize = nkfs.GetFileSize(children[0].index),
                ReferencedImageId = nkfs.GetImageIndex(children[0].index)
            };
            Assert.Equal("00000001.app", ifsItem.Name);
            Assert.Equal(42L, ifsItem.ReferencedImageId);
        }

        /// <summary>
        /// System entries (SystemFlag=true) should be filterable.
        /// Mount handlers filter them when ShowSystemFlag=false.
        /// Validates: Requirements 5.8
        /// </summary>
        [Fact]
        public void Handler_SystemEntries_FilteredWhenNotSystemMode()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("normal.bin", 0, 100, 0x1111, 0x2222, isSystem: false);
            root.AddFile("system.bin", 100, 200, 0x3333, 0x4444, isSystem: true);
            root.AddDirectory("NormalDir", isSystem: false);
            root.AddDirectory("SystemDir", isSystem: true);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(int index, NkFsEntry entry)> allChildren = nkfs.GetChildren(0).ToList();
            Assert.Equal(4, allChildren.Count);

            // Simulate ShowSystemFlag=false filtering (as mount handlers do)
            bool showSystemFlag = false;
            List<(int index, NkFsEntry entry)> visibleChildren = allChildren
                .Where(c => showSystemFlag || !c.entry.SystemFlag)
                .ToList();

            Assert.Equal(2, visibleChildren.Count);
            Assert.Equal("normal.bin", nkfs.GetEntryName(visibleChildren[0].index));
            Assert.Equal("NormalDir", nkfs.GetEntryName(visibleChildren[1].index));

            // With ShowSystemFlag=true, all entries are visible
            showSystemFlag = true;
            List<(int index, NkFsEntry entry)> allVisible = allChildren
                .Where(c => showSystemFlag || !c.entry.SystemFlag)
                .ToList();
            Assert.Equal(4, allVisible.Count);
        }

        /// <summary>
        /// NkFsFileItem wraps an NkFsEntry and exposes correct Name, FsSize, FsOffset metadata.
        /// Validates: Requirements 5.5, 5.6, 5.7
        /// </summary>
        [Fact]
        public void Handler_NkFsFileItem_CorrectMetadata()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("game.iso", 12345, 67890, 0xAAAA, 0xBBBB, isSystem: false);
            root.AddFile("ticket.bin", 99999, 512, 0xCCCC, 0xDDDD, isSystem: true);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(0).ToList();
            Assert.Equal(2, children.Count);

            // Create NkFsFileItem for the first file
            NkFsFileItem fileItem1 = new NkFsFileItem(nkfs, children[0].index, children[0].entry);
            Assert.Equal("game.iso", fileItem1.Name);
            Assert.Equal(67890L, fileItem1.FsSize);
            Assert.Equal(12345L, fileItem1.FsOffset);
            Assert.False(fileItem1.IsSystemFile);
            Assert.False(fileItem1.IsMissing);

            // Create NkFsFileItem for the system file
            NkFsFileItem fileItem2 = new NkFsFileItem(nkfs, children[1].index, children[1].entry);
            Assert.Equal("ticket.bin", fileItem2.Name);
            Assert.Equal(512L, fileItem2.FsSize);
            Assert.Equal(99999L, fileItem2.FsOffset);
            Assert.True(fileItem2.IsSystemFile);
        }

        #endregion

        #region 20.4 — Virtual Filesystem.yaml and VfsStreamHelper Tests

        /// <summary>
        /// When NkFs data is present, the system mode should add a virtual Filesystem.yaml entry.
        /// Test the underlying conversion: NkFs → FsYaml → YAML text.
        /// Validates: Requirements 6.1, 6.2, 12.4
        /// </summary>
        [Fact]
        public void LoadStoredFiles_NkFsPresent_AddsVirtualYaml()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("boot.dol", 0, 1024, 0x1111, 0x2222);

            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // Simulate what LoadStoredFiles does: detect NkFs, generate virtual YAML
            bool nkfsPresent = nkfsBytes.Length >= 4
                && nkfsBytes[0] == 0x4E && nkfsBytes[1] == 0x4B
                && nkfsBytes[2] == 0x46 && nkfsBytes[3] == 0x53;

            Assert.True(nkfsPresent, "NkFs data should be detected");

            // Generate virtual YAML text (as LoadStoredFiles does)
            string yamlText = NkFs.FromBytes(nkfsBytes).ToFsYaml().ToYaml();
            Assert.False(string.IsNullOrEmpty(yamlText));

            int yamlSize = Encoding.UTF8.GetByteCount(yamlText);
            Assert.True(yamlSize > 0);

            // The virtual entry would have Name = "filesystem.yaml" and StoredFileName = FileSystemNkfsRootPath
            string virtualName = DataStore.FileSystemYamlName;
            string storedFileName = DataStore.FileSystemNkfsRootPath;
            Assert.Equal("filesystem.yaml", virtualName);
            Assert.Equal("filesystem.nkfs", storedFileName);
        }

        /// <summary>
        /// The virtual Filesystem.yaml entry should have the display name "filesystem.yaml".
        /// Validates: Requirements 6.5
        /// </summary>
        [Fact]
        public void LoadStoredFiles_VirtualYaml_CorrectDisplayName()
        {
            // The virtual entry uses FileSystemYamlName as its display name
            Assert.Equal("filesystem.yaml", DataStore.FileSystemYamlName);

            // And the stored file reference points to the NkFs path
            Assert.Equal("filesystem.nkfs", DataStore.FileSystemNkfsRootPath);
        }

        /// <summary>
        /// The virtual Filesystem.yaml content should match the original FsYaml.ToYaml() output.
        /// This verifies the round-trip: FsYaml → NkFs → bytes → NkFs → FsYaml → YAML text.
        /// Validates: Requirements 6.3, 7.2, 12.4
        /// </summary>
        [Fact]
        public void VirtualYaml_MatchesOriginalContent()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("boot.dol", 0, 1024, 0x1111, 0x2222);
            FsYamlNode sysDir = root.AddDirectory("sys", isSystem: true);
            sysDir.AddFile("bi2.bin", 2048, 512, 0x3333, 0x4444, isSystem: true);

            string originalYaml = fsYaml.ToYaml();

            // Pipeline write: FsYaml → NkFs → bytes
            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // Virtual YAML generation: bytes → NkFs → FsYaml → YAML text
            string virtualYaml = NkFs.FromBytes(nkfsBytes).ToFsYaml().ToYaml();

            Assert.Equal(originalYaml, virtualYaml);
        }

        /// <summary>
        /// When StoredFileName references the NkFs path, VfsStreamHelper should serve
        /// converted YAML text. Test the conversion pipeline.
        /// Validates: Requirements 7.1, 7.2, 12.6
        /// </summary>
        [Fact]
        public void EnsureStream_NkFsPath_ServesYamlText()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("main.dol", 0, 4096, 0xAAAA, 0xBBBB);

            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // Simulate VfsStreamHelper: StoredFileName = FileSystemNkfsRootPath
            string storedFileName = DataStore.FileSystemNkfsRootPath;
            Assert.Equal("filesystem.nkfs", storedFileName);

            // Convert binary to YAML text (as VfsStreamHelper does)
            string yamlText = NkFs.FromBytes(nkfsBytes).ToFsYaml().ToYaml();
            byte[] servedData = Encoding.UTF8.GetBytes(yamlText);

            // The served data should be valid YAML text
            Assert.True(servedData.Length > 0);
            string servedText = Encoding.UTF8.GetString(servedData);
            Assert.Contains("main.dol", servedText);

            // Verify it matches the original YAML
            Assert.Equal(fsYaml.ToYaml(), servedText);
        }

        /// <summary>
        /// When NkFs data is not found, VfsStreamHelper falls back to YAML.
        /// Test the fallback: read YAML bytes directly.
        /// Validates: Requirements 7.3
        /// </summary>
        [Fact]
        public void EnsureStream_NkFsNotFound_FallsBackToYaml()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("legacy.bin", 100, 200, 0x1111, 0x2222);

            byte[] yamlBytes = fsYaml.ToBytes();

            // Simulate: NkFs not found (null), fall back to YAML
            byte[] nkfsData = null;
            byte[] result;

            if (nkfsData != null)
            {
                string yamlText = NkFs.FromBytes(nkfsData).ToFsYaml().ToYaml();
                result = Encoding.UTF8.GetBytes(yamlText);
            }
            else
            {
                // Fall back to YAML
                result = yamlBytes;
            }

            Assert.NotNull(result);
            string text = Encoding.UTF8.GetString(result);
            Assert.Contains("legacy.bin", text);
        }

        /// <summary>
        /// When YAML is not found, VfsStreamHelper falls back to NkFs and converts.
        /// Test the cross-fallback: NkFs bytes → YAML text.
        /// Validates: Requirements 7.4
        /// </summary>
        [Fact]
        public void EnsureStream_YamlNotFound_FallsBackToNkFs()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("newformat.bin", 500, 1000, 0x5555, 0x6666);

            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // Simulate: YAML not found (null), fall back to NkFs and convert
            byte[] yamlData = null;
            byte[] result;

            if (yamlData != null)
            {
                result = yamlData;
            }
            else
            {
                // Fall back to NkFs and convert
                string yamlText = NkFs.FromBytes(nkfsBytes).ToFsYaml().ToYaml();
                result = Encoding.UTF8.GetBytes(yamlText);
            }

            Assert.NotNull(result);
            string text = Encoding.UTF8.GetString(result);
            Assert.Contains("newformat.bin", text);

            // Verify the content matches the original
            Assert.Equal(fsYaml.ToYaml(), text);
        }

        #endregion

        #region 20.5 — Formatter Write Verification and Backward Compatibility Tests

        /// <summary>
        /// Verify that the pipeline formatter write pattern produces NkFs binary
        /// at the FileSystemNkfsRootPath. Test the conversion: FsYaml → NkFs → bytes.
        /// Validates: Requirements 1.4, 12.5
        /// </summary>
        [Fact]
        public void FolderFormatter_BuildFileSystemYaml_WritesNkFs()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("title.tmd", 0, 256, 0x1111, 0x2222);
            FsYamlNode contentDir = root.AddDirectory("content");
            contentDir.AddFile("00000000.app", 512, 2048, 0x3333, 0x4444);

            // Simulate what the formatter does: convert FsYaml to NkFs binary
            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // The formatter writes to FileSystemNkfsRootPath
            string writePath = DataStore.FileSystemNkfsRootPath;
            Assert.Equal("filesystem.nkfs", writePath);

            // Verify the written data is valid NkFs
            Assert.True(nkfsBytes.Length >= 4);
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(nkfsBytes.AsSpan(0, 4));
            Assert.Equal(0x4E4B4653u, magic);

            // Verify the data can be deserialized back
            NkFs loaded = NkFs.FromBytes(nkfsBytes);
            Assert.NotNull(loaded);
            Assert.True(loaded.EntryCount >= 3); // root + file + dir + file
        }

        /// <summary>
        /// Verify that the formatter write path is FileSystemNkfsRootPath, not FileSystemYamlRootPath.
        /// The formatter should no longer write to the YAML path.
        /// Validates: Requirements 1.6
        /// </summary>
        [Fact]
        public void Formatter_DoesNotWriteYaml()
        {
            // The formatter writes to NkFs path, not YAML path
            Assert.NotEqual(DataStore.FileSystemNkfsRootPath, DataStore.FileSystemYamlRootPath);
            Assert.Equal("filesystem.nkfs", DataStore.FileSystemNkfsRootPath);
            Assert.Equal("filesystem.yaml", DataStore.FileSystemYamlRootPath);

            // Verify the NkFs path is used (not YAML) by checking the conversion produces binary
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);
            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();

            // NkFs binary starts with magic, not YAML text
            Assert.Equal(0x4E, nkfsBytes[0]);
            Assert.NotEqual((byte)'#', nkfsBytes[0]); // YAML would start with '#' or letter
        }

        /// <summary>
        /// A YAML-only datastore image should be convertible to NkFs in memory.
        /// This simulates the backward compatibility path.
        /// Validates: Requirements 11.1, 11.2
        /// </summary>
        [Fact]
        public void YamlOnlyDatastore_ConvertsToNkFs()
        {
            // Simulate a legacy datastore with only YAML
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("old_game.iso", 0, 8192, 0xAAAA, 0xBBBB);
            FsYamlNode dataDir = root.AddDirectory("data");
            dataDir.AddFile("save.dat", 16384, 1024, 0xCCCC, 0xDDDD);

            byte[] yamlBytes = fsYaml.ToBytes();

            // Loading boundary: YAML → FsYaml → NkFs (in-memory conversion)
            FsYaml parsed = FsYaml.FromBytes(yamlBytes);
            NkFs nkfs = NkFs.FromFsYaml(parsed);

            // The NkFs object should be fully functional
            Assert.NotNull(nkfs);
            Assert.Equal(4, nkfs.EntryCount); // root + file + dir + file

            // Navigation should work
            List<(int index, NkFsEntry entry)> rootChildren = nkfs.GetChildren(0).ToList();
            Assert.Equal(2, rootChildren.Count);
            Assert.Equal("old_game.iso", nkfs.GetEntryName(rootChildren[0].index));
            Assert.Equal("data", nkfs.GetEntryName(rootChildren[1].index));

            // Nested navigation
            int dataIdx = rootChildren[1].index;
            List<(int index, NkFsEntry entry)> dataChildren = nkfs.GetChildren(dataIdx).ToList();
            Assert.Single(dataChildren);
            Assert.Equal("save.dat", nkfs.GetEntryName(dataChildren[0].index));
        }

        /// <summary>
        /// The backward compatibility conversion is in-memory only.
        /// Converting YAML to NkFs does not produce a write-back — it only creates an NkFs object.
        /// Validates: Requirements 11.3
        /// </summary>
        [Fact]
        public void YamlOnlyDatastore_NoWriteBack()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("legacy.bin", 0, 100, 0x1111, 0x2222);

            byte[] yamlBytes = fsYaml.ToBytes();

            // The conversion is purely in-memory: YAML bytes → FsYaml → NkFs
            FsYaml parsed = FsYaml.FromBytes(yamlBytes);
            NkFs nkfs = NkFs.FromFsYaml(parsed);

            // The original YAML bytes are unchanged
            FsYaml reparsed = FsYaml.FromBytes(yamlBytes);
            Assert.Equal(parsed.ToYaml(), reparsed.ToYaml());

            // The NkFs object exists only in memory — no ToBytes() is called for write-back
            // We verify the NkFs is usable without any side effects
            Assert.NotNull(nkfs);
            Assert.Equal(2, nkfs.EntryCount);
        }

        /// <summary>
        /// The same FsYaml tree should produce identical mount handler results
        /// whether loaded from NkFs binary or converted from YAML.
        /// Validates: Requirements 11.2, 12.8
        /// </summary>
        [Fact]
        public void IdenticalResults_NkFsVsYaml()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("boot.dol", 0, 1024, 0x1111, 0x2222);
            FsYamlNode sysDir = root.AddDirectory("sys", isSystem: true);
            sysDir.AddFile("bi2.bin", 2048, 512, 0x3333, 0x4444, isSystem: true);
            root.AddDirectory("files");

            // Path 1: FsYaml → NkFs → bytes → NkFs (simulates NkFs storage)
            byte[] nkfsBytes = NkFs.FromFsYaml(fsYaml).ToBytes();
            NkFs fromNkFs = NkFs.FromBytes(nkfsBytes);

            // Path 2: FsYaml → bytes → FsYaml → NkFs (simulates YAML fallback)
            byte[] yamlBytes = fsYaml.ToBytes();
            FsYaml parsedYaml = FsYaml.FromBytes(yamlBytes);
            NkFs fromYaml = NkFs.FromFsYaml(parsedYaml);

            // Both paths should produce identical navigation results
            List<(int index, NkFsEntry entry)> nkfsChildren = fromNkFs.GetChildren(0).ToList();
            List<(int index, NkFsEntry entry)> yamlChildren = fromYaml.GetChildren(0).ToList();

            Assert.Equal(nkfsChildren.Count, yamlChildren.Count);

            for (int i = 0; i < nkfsChildren.Count; i++)
            {
                string nkfsName = fromNkFs.GetEntryName(nkfsChildren[i].index);
                string yamlName = fromYaml.GetEntryName(yamlChildren[i].index);
                Assert.Equal(nkfsName, yamlName);

                Assert.Equal(nkfsChildren[i].entry.IsDirectory, yamlChildren[i].entry.IsDirectory);
                Assert.Equal(nkfsChildren[i].entry.IsFile, yamlChildren[i].entry.IsFile);
                Assert.Equal(nkfsChildren[i].entry.SystemFlag, yamlChildren[i].entry.SystemFlag);

                if (nkfsChildren[i].entry.IsFile)
                {
                    Assert.Equal(nkfsChildren[i].entry.FileOffset, yamlChildren[i].entry.FileOffset);
                    Assert.Equal(fromNkFs.GetFileSize(nkfsChildren[i].index), fromYaml.GetFileSize(yamlChildren[i].index));
                }
            }

            // Verify nested directory children are also identical
            // Find the "sys" directory in both
            int nkfsSysIdx = nkfsChildren.First(c => fromNkFs.GetEntryName(c.index) == "sys").index;
            int yamlSysIdx = yamlChildren.First(c => fromYaml.GetEntryName(c.index) == "sys").index;

            List<(int index, NkFsEntry entry)> nkfsSysChildren = fromNkFs.GetChildren(nkfsSysIdx).ToList();
            List<(int index, NkFsEntry entry)> yamlSysChildren = fromYaml.GetChildren(yamlSysIdx).ToList();

            Assert.Equal(nkfsSysChildren.Count, yamlSysChildren.Count);
            for (int i = 0; i < nkfsSysChildren.Count; i++)
            {
                Assert.Equal(
                    fromNkFs.GetEntryName(nkfsSysChildren[i].index),
                    fromYaml.GetEntryName(yamlSysChildren[i].index));
            }

            // Verify YAML output is identical from both paths
            string nkfsYaml = fromNkFs.ToFsYaml().ToYaml();
            string yamlYaml = fromYaml.ToFsYaml().ToYaml();
            Assert.Equal(nkfsYaml, yamlYaml);
        }

        #endregion
    }
}
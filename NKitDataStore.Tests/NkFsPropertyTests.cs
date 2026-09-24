using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Vfs;
using System.Buffers.Binary;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for NkFs binary filesystem format.
    /// Feature: nkds-binary-fs
    /// </summary>
    public class NkFsPropertyTests
    {
        /// <summary>
        /// Feature: nkds-binary-fs, Property 1: Entry encoding round-trip
        ///
        /// For any valid NkFsEntry (file or directory, with any combination of 3-bit flags
        /// and valid field values), encoding the entry to 12 bytes and decoding it back
        /// produces an entry with identical Flags, NameOffset, and either FileOffset (for files)
        /// or ParentIndex/NextEntryIndex (for directories).
        /// </summary>
        [Property]
        public bool EntryEncoding_RoundTrip(
            NonNegativeInt flagsRaw,
            NonNegativeInt nameOffsetRaw,
            long fileOffset,
            NonNegativeInt parentIndexRaw,
            NonNegativeInt nextEntryIndexRaw)
        {
            // Constrain flags to valid 3-bit range (0–7)
            byte flags = (byte)(flagsRaw.Get % 8);

            // Constrain nameOffset to valid 29-bit range (0–0x1FFFFFFF)
            int nameOffset = nameOffsetRaw.Get & 0x1FFFFFFF;

            bool isDirectory = (flags & 0x01) != 0;

            if (isDirectory)
            {
                int parentIndex = parentIndexRaw.Get;
                int nextEntryIndex = nextEntryIndexRaw.Get;

                NkFsEntry original = new NkFsEntry(flags, nameOffset, parentIndex, nextEntryIndex);
                byte[] encoded = NkFsEntry.Encode(original);

                if (encoded.Length != NkFsEntry.EntrySize)
                    return false;

                NkFsEntry decoded = NkFsEntry.Decode(encoded);

                return decoded.Flags == original.Flags
                    && decoded.NameOffset == original.NameOffset
                    && decoded.ParentIndex == original.ParentIndex
                    && decoded.NextEntryIndex == original.NextEntryIndex
                    && decoded.IsDirectory;
            }
            else
            {
                NkFsEntry original = new NkFsEntry(flags, nameOffset, fileOffset);
                byte[] encoded = NkFsEntry.Encode(original);

                if (encoded.Length != NkFsEntry.EntrySize)
                    return false;

                NkFsEntry decoded = NkFsEntry.Decode(encoded);

                return decoded.Flags == original.Flags
                    && decoded.NameOffset == original.NameOffset
                    && decoded.FileOffset == original.FileOffset
                    && decoded.IsFile;
            }
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 5: Header structure invariants
        ///
        /// For any valid NkFs, serialized bytes start with magic 0x4E4B4653, version 1,
        /// EntryCount matches, and string table starts at 12 + EntryCount * 12.
        /// </summary>
        [Property]
        public bool HeaderStructure_Invariants(
            NonNegativeInt fileCount,
            NonNegativeInt dirDepth,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            int nFiles = fileCount.Get % 6;
            int nDirs = dirDepth.Get % 4;
            int nIfs = ifsCount.Get % 4;
            int s = seed.Get;

            FsYaml fsYaml = BuildFsYaml(nFiles, nDirs, nIfs, s);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();

            // Header must be at least 12 bytes
            if (bytes.Length < 12)
                return false;

            // Bytes 0-3: magic 0x4E4B4653
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
            if (magic != 0x4E4B4653)
                return false;

            // Bytes 4-5: version 1
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
            if (version != 1)
                return false;

            // Bytes 6-7: reserved must be 0
            ushort reserved = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
            if (reserved != 0)
                return false;

            // Bytes 8-11: EntryCount matches NkFs.EntryCount
            int entryCount = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));
            if (entryCount != nkfs.EntryCount)
                return false;

            // No StringTableOffset field in header — it's computed: 12 + EntryCount * 12
            int expectedStringTableOffset = 12 + (entryCount * 12);

            // Verify total size is at least header + entries
            if (bytes.Length < expectedStringTableOffset)
                return false;

            return true;
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 3: Import equivalence
        /// </summary>
        [Property]
        public bool ImportEquivalence_FromFsYaml_Equals_Build(
            NonNegativeInt fileCount,
            NonNegativeInt dirDepth,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            int nFiles = fileCount.Get % 6;
            int nDirs = dirDepth.Get % 4;
            int nIfs = ifsCount.Get % 4;
            int s = seed.Get;

            FsYaml fsYaml = BuildFsYaml(nFiles, nDirs, nIfs, s);

            byte[] fromFsYamlBytes = NkFs.FromFsYaml(fsYaml).ToBytes();
            byte[] fromBuildBytes = NkFs.Build(fsYaml.FileSystems, fsYaml.ImageFileSystems).ToBytes();

            return fromFsYamlBytes.SequenceEqual(fromBuildBytes);
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 6: Child listing correctness
        /// </summary>
        [Property]
        public bool ChildListing_MatchesFsYamlChildren(
            NonNegativeInt fileCount,
            NonNegativeInt dirDepth,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            int nFiles = fileCount.Get % 6;
            int nDirs = dirDepth.Get % 4;
            int nIfs = ifsCount.Get % 4;
            int s = seed.Get;
            int nFilesPerDir = 1 + (s % 3);

            FsYaml fsYaml = BuildFsYaml(nFiles, nDirs, nIfs, s, nFilesPerDir);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            NkFsEntry root = nkfs.GetEntry(0);
            if (!root.IsDirectory)
                return false;

            if (root.NextEntryIndex != nkfs.EntryCount)
                return false;

            List<FsYamlNode> fsRootChildren = fsYaml.FileSystems.Count > 0 && fsYaml.FileSystems[0].Children != null
                ? fsYaml.FileSystems[0].Children!
                : new List<FsYamlNode>();

            int expectedRootChildCount = fsRootChildren.Count + fsYaml.ImageFileSystems.Count;

            List<(int index, NkFsEntry entry)> rootChildren = nkfs.GetChildren(0).ToList();
            if (rootChildren.Count != expectedRootChildCount)
                return false;

            for (int i = 0; i < fsRootChildren.Count; i++)
            {
                FsYamlNode expectedChild = fsRootChildren[i];
                (int childIndex, NkFsEntry childEntry) = rootChildren[i];

                if (!verifyChildMatch(nkfs, childIndex, childEntry, expectedChild))
                    return false;

                if (expectedChild.IsDirectory)
                {
                    if (!verifyDirectoryChildren(nkfs, childIndex, expectedChild))
                        return false;
                }
            }

            for (int i = 0; i < fsYaml.ImageFileSystems.Count; i++)
            {
                FsYamlIfsEntry expectedIfs = fsYaml.ImageFileSystems[i];
                (int childIndex, NkFsEntry childEntry) = rootChildren[fsRootChildren.Count + i];

                if (!childEntry.IsFile)
                    return false;

                string entryName = nkfs.GetEntryName(childIndex);
                if (entryName != expectedIfs.FileName)
                    return false;
            }

            return true;
        }

        private static bool verifyChildMatch(NkFs nkfs, int childIndex, NkFsEntry childEntry, FsYamlNode expected)
        {
            if (expected.IsDirectory != childEntry.IsDirectory)
                return false;

            string entryName = nkfs.GetEntryName(childIndex);
            if (entryName != expected.Name)
                return false;

            return true;
        }

        private static bool verifyDirectoryChildren(NkFs nkfs, int dirIndex, FsYamlNode expectedDir)
        {
            List<FsYamlNode> expectedChildren = expectedDir.Children ?? new List<FsYamlNode>();
            List<(int index, NkFsEntry entry)> actualChildren = nkfs.GetChildren(dirIndex).ToList();

            if (actualChildren.Count != expectedChildren.Count)
                return false;

            for (int i = 0; i < expectedChildren.Count; i++)
            {
                FsYamlNode expectedChild = expectedChildren[i];
                (int childIndex, NkFsEntry childEntry) = actualChildren[i];

                if (!verifyChildMatch(nkfs, childIndex, childEntry, expectedChild))
                    return false;

                if (expectedChild.IsDirectory)
                {
                    if (!verifyDirectoryChildren(nkfs, childIndex, expectedChild))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 7: Path resolution correctness
        /// </summary>
        [Property]
        public bool PathResolution_MatchesTreePaths(
            NonNegativeInt fileCount,
            NonNegativeInt dirDepth,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            int nFiles = fileCount.Get % 6;
            int nDirs = dirDepth.Get % 4;
            int nIfs = ifsCount.Get % 4;
            int s = seed.Get;
            int nFilesPerDir = 1 + (s % 3);

            FsYaml fsYaml = BuildFsYaml(nFiles, nDirs, nIfs, s, nFilesPerDir);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            List<(string path, int index)> expectedPaths = new List<(string path, int index)>();

            int currentIndex = 1;
            if (fsYaml.FileSystems.Count > 0 && fsYaml.FileSystems[0].Children != null)
            {
                foreach (FsYamlNode child in fsYaml.FileSystems[0].Children!)
                {
                    collectPaths(child, "", ref currentIndex, expectedPaths);
                }
            }

            for (int i = 0; i < fsYaml.ImageFileSystems.Count; i++)
            {
                FsYamlIfsEntry ifsEntry = fsYaml.ImageFileSystems[i];
                expectedPaths.Add((ifsEntry.FileName, currentIndex));
                currentIndex++;
            }

            foreach ((string path, int expectedIndex) in expectedPaths)
            {
                int resolved = nkfs.ResolvePath(path);
                if (resolved != expectedIndex)
                    return false;
            }

            if (nkfs.ResolvePath("nonexistent/path") != -1)
                return false;

            if (nkfs.ResolvePath("totally/bogus/deep/path") != -1)
                return false;

            if (nkfs.ResolvePath("") != 0)
                return false;

            if (nkfs.ResolvePath(null!) != 0)
                return false;

            return true;
        }

        private static void collectPaths(FsYamlNode node, string parentPath, ref int currentIndex, List<(string path, int index)> paths)
        {
            string fullPath = string.IsNullOrEmpty(parentPath)
                ? node.Name
                : parentPath + "/" + node.Name;

            int thisIndex = currentIndex;
            currentIndex++;

            paths.Add((fullPath, thisIndex));

            if (node.IsDirectory && node.Children != null)
            {
                foreach (FsYamlNode child in node.Children)
                {
                    collectPaths(child, fullPath, ref currentIndex, paths);
                }
            }
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 2: Full end-to-end round-trip
        /// </summary>
        [Property]
        public bool FullEndToEnd_RoundTrip(
            NonNegativeInt fileCount,
            NonNegativeInt dirDepth,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            int nFiles = fileCount.Get % 6;
            int nDirs = dirDepth.Get % 4;
            int nIfs = ifsCount.Get % 4;
            int s = seed.Get;
            int nFilesPerDir = 1 + (s % 3);

            FsYaml fsYaml = BuildFsYaml(nFiles, nDirs, nIfs, s, nFilesPerDir);

            string originalYaml = fsYaml.ToYaml();

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml roundTripped = restored.ToFsYaml();

            string roundTrippedYaml = roundTripped.ToYaml();

            return originalYaml == roundTrippedYaml;
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 4: Directory name deduplication
        /// </summary>
        [Property]
        public bool DirectoryNameDeduplication_SharedOffsets(
            NonNegativeInt dupCount,
            NonNegativeInt extraDirs,
            NonNegativeInt seed)
        {
            int nDuplicateDirs = 2 + (dupCount.Get % 4);
            int nExtraDirs = extraDirs.Get % 3;
            int s = seed.Get;

            FsYaml fsYaml = BuildFsYamlWithDuplicateNames(nDuplicateDirs, nExtraDirs, s);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            Dictionary<string, List<int>> dirNameOffsets = new Dictionary<string, List<int>>();
            Dictionary<string, List<int>> fileNameOffsets = new Dictionary<string, List<int>>();

            for (int i = 0; i < nkfs.EntryCount; i++)
            {
                NkFsEntry entry = nkfs.GetEntry(i);
                string name = nkfs.GetEntryName(i);

                if (entry.IsDirectory)
                {
                    if (!dirNameOffsets.ContainsKey(name))
                        dirNameOffsets[name] = new List<int>();
                    dirNameOffsets[name].Add(entry.NameOffset);
                }
                else
                {
                    if (!fileNameOffsets.ContainsKey(name))
                        fileNameOffsets[name] = new List<int>();
                    fileNameOffsets[name].Add(entry.NameOffset);
                }
            }

            // Directory entries with the same name share the same NameOffset
            foreach (KeyValuePair<string, List<int>> kvp in dirNameOffsets)
            {
                List<int> offsets = kvp.Value;
                if (offsets.Count < 2)
                    continue;

                int firstOffset = offsets[0];
                for (int i = 1; i < offsets.Count; i++)
                {
                    if (offsets[i] != firstOffset)
                        return false;
                }
            }

            // File entries with the same name do NOT share NameOffset
            foreach (KeyValuePair<string, List<int>> kvp in fileNameOffsets)
            {
                List<int> offsets = kvp.Value;
                if (offsets.Count < 2)
                    continue;

                HashSet<int> uniqueOffsets = new HashSet<int>(offsets);
                if (uniqueOffsets.Count != offsets.Count)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 3: Prefix byte field preservation under multi-extent encoding
        ///
        /// **Validates: Requirements 1.4**
        ///
        /// For any file entry (whether part of a multi-extent chain or not), the prefix byte
        /// SHALL encode bits 0 (has_checksums), 1-2 (size_width), and 3-4 (imageid_width)
        /// according to the entry's individual size value and checksum presence, and bits 6-7
        /// SHALL always be 0 — regardless of whether bit 5 is set.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool PrefixByteFieldPreservation_UnderMultiExtentEncoding(
            NonNegativeInt sizeClassRaw,
            bool hasChecksums,
            bool hasMoreExtents,
            NonNegativeInt seed)
        {
            // Choose a file size that exercises different size_width values (bits 1-2)
            int sizeClass = sizeClassRaw.Get % 4;
            long fileSize = sizeClass switch
            {
                0 => 0,                                         // size_width = 0 (0 bytes)
                1 => 1 + ((long)(seed.Get & 0xFFFF) % 0xFFFF), // size_width = 1 (2 bytes, 1..0xFFFF)
                2 => 0x10000L + ((long)(seed.Get & 0xFFFFFFF) % 0xEFFF0000L), // size_width = 2 (4 bytes)
                _ => 0x100000000L + ((long)(seed.Get & 0x7FFFFFFF)),           // size_width = 3 (8 bytes)
            };

            // Generate checksums based on the hasChecksums flag
            ulong xxHash64 = hasChecksums ? (ulong)(uint)(((seed.Get * 333) + 1) & 0x7FFFFFFF) : 0;
            uint crc32 = hasChecksums ? (uint)(((seed.Get * 555) + 1) & 0x7FFFFFFF) : 0;

            // Build an FsYaml with the target file.
            // If hasMoreExtents=true, create two same-name file entries (the first gets bit 5 set).
            // If hasMoreExtents=false, create a single file entry.
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("testdir");

            string fileName = "testfile.bin";
            long offset1 = 0x1000L + (seed.Get & 0xFFF);
            dir.AddFile(fileName, offset1, fileSize, xxHash64, crc32, false);

            if (hasMoreExtents)
            {
                // Add a second same-name entry to form a multi-extent chain.
                // The first entry should get HasMoreExtents = true.
                long offset2 = offset1 + 0x10000L;
                long size2 = 0x500L; // arbitrary second extent size
                dir.AddFile(fileName, offset2, size2, xxHash64, crc32, false);
            }

            // Build NkFs
            NkFs nkfs = NkFs.FromFsYaml(yaml);

            // Find the target file entry (first child of testdir)
            // Entry 0 = root, Entry 1 = testdir, Entry 2 = first file in testdir
            int targetIndex = 2;
            NkFsEntry targetEntry = nkfs.GetEntry(targetIndex);

            if (targetEntry.IsDirectory)
                return false; // Should be a file

            // Verify bit 0: has_checksums
            (ulong readXxHash, uint readCrc) = nkfs.GetChecksums(targetIndex);
            bool expectedHasChecksums = hasChecksums;
            bool actualHasChecksums = readXxHash != 0 || readCrc != 0;
            if (expectedHasChecksums != actualHasChecksums)
                return false;

            if (hasChecksums)
            {
                if (readXxHash != xxHash64 || readCrc != crc32)
                    return false;
            }

            // Verify bits 1-2: size_width (indirectly via file size round-trip)
            long readSize = nkfs.GetFileSize(targetIndex);
            if (readSize != fileSize)
                return false;

            // Verify bit 5: has_more_extents
            bool readHasMoreExtents = nkfs.HasMoreExtents(targetIndex);
            if (readHasMoreExtents != hasMoreExtents)
                return false;

            // Verify bits 3-4 (imageid_width) are 0 for regular files and bits 6-7 are 0
            // by reading the raw prefix byte from the serialized bytes
            byte[] allBytes = nkfs.ToBytes();
            int headerSize = 12;
            int entryTableSize = nkfs.EntryCount * 12;
            int stringTableStart = headerSize + entryTableSize;

            // Get the string table byte offset from the entry's NameOffset
            int encodedNameOffset = targetEntry.NameOffset;
            int byteOffset = encodedNameOffset * 2; // DecodeNameOffset
            byte prefixByte = allBytes[stringTableStart + byteOffset];

            // Verify bits 3-4 (imageid_width) are 0 for regular file entries
            int imageidWidth = (prefixByte >> 3) & 0x03;
            if (imageidWidth != 0)
                return false;

            // Verify bits 6-7 are always 0
            int reservedBits = (prefixByte >> 6) & 0x03;
            if (reservedBits != 0)
                return false;

            // Cross-verify: the prefix byte should be consistent with all the fields
            byte expectedPrefixByte = 0;
            if (hasChecksums) expectedPrefixByte |= 0x01;
            expectedPrefixByte |= (byte)(NkFs.EncodeWidth(fileSize) << 1);
            // imageid_width = 0 for regular files (bits 3-4 = 0)
            if (hasMoreExtents) expectedPrefixByte |= 0x20;
            // bits 6-7 = 0

            if (prefixByte != expectedPrefixByte)
                return false;

            return true;
        }

        /// <summary>
        /// Feature: nkds-binary-fs, Property 8: Binary compactness for large trees
        /// </summary>
        [Property]
        public bool BinaryCompactness_SmallerThanYaml(
            NonNegativeInt dirCount,
            NonNegativeInt filesPerDir,
            NonNegativeInt ifsCount,
            NonNegativeInt seed)
        {
            int nDirs = 5 + (dirCount.Get % 10);
            int nFilesPerDir = 5 + (filesPerDir.Get % 8);
            int nIfs = ifsCount.Get % 5;
            int s = seed.Get;

            FsYaml fsYaml = BuildLargeFsYaml(nDirs, nFilesPerDir, nIfs, s);

            int totalEntries = CountEntries(fsYaml);
            if (totalEntries < 100)
                return true;

            byte[] binaryBytes = NkFs.Build(fsYaml.FileSystems, fsYaml.ImageFileSystems).ToBytes();
            byte[] yamlBytes = fsYaml.ToBytes();

            return binaryBytes.Length < yamlBytes.Length;
        }

        // ---- Helper methods ----

        private static FsYaml BuildLargeFsYaml(int nDirs, int nFilesPerDir, int nIfs, int seed)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);

            long baseOffset = 1_000_000_000L + (long)(seed & 0x7FFFFFFF);
            long baseSize = 500_000_000L + (long)(seed & 0x3FFFFFFF);
            ulong baseXxHash = 10_000_000_000_000_000_000UL - (ulong)(uint)(seed & 0x7FFFFFFF);
            uint baseCrc = 3_000_000_000U + (uint)(seed & 0x3FFFFFFF);

            for (int d = 0; d < nDirs; d++)
            {
                string dirName = GenerateDirName(seed + 500, d);
                bool dirIsSystem = ShouldBeSystem(seed, 300 + d);
                FsYamlNode dir = root.AddDirectory(dirName, dirIsSystem);

                for (int f = 0; f < nFilesPerDir; f++)
                {
                    string name = GenerateFileName(seed + d + 500, f);
                    long offset = baseOffset + (d * 100_000_000L) + (f * 1_000_000L);
                    long size = baseSize + (d * 10_000_000L) + (f * 100_000L);
                    ulong xxhash = baseXxHash + (ulong)((d * 1_000_000) + (f * 10_000));
                    uint crc = baseCrc + (uint)((d * 100_000) + (f * 1_000));
                    bool fileIsSystem = ShouldBeSystem(seed, 400 + (d * 20) + f);
                    dir.AddFile(name, offset, size, xxhash, crc, fileIsSystem);
                }

                if (d % 3 == 0)
                {
                    bool subDirIsSystem = ShouldBeSystem(seed, 600 + d);
                    FsYamlNode subDir = dir.AddDirectory("sub", subDirIsSystem);
                    subDir.AddFile(GenerateFileName(seed + d + 700, d),
                        baseOffset + (d * 200_000_000L),
                        baseSize + (d * 20_000_000L),
                        baseXxHash + (ulong)(d * 2_000_000),
                        baseCrc + (uint)(d * 200_000),
                        ShouldBeSystem(seed, 700 + d));
                }
            }

            for (int i = 0; i < nIfs; i++)
            {
                string name = $"app{i}.bin";
                long imageId = 1_000_000L + (i * 100_000L) + (seed & 0xFFFF);
                long size = baseSize + (i * 50_000_000L);
                yaml.AddIfsEntry(name, imageId, size);
            }

            return yaml;
        }

        private static int CountEntries(FsYaml fsYaml)
        {
            int count = 1;
            foreach (FsYamlNode fs in fsYaml.FileSystems)
            {
                if (fs.Children != null)
                {
                    foreach (FsYamlNode child in fs.Children)
                        count += CountEntriesRecursive(child);
                }
            }
            count += fsYaml.ImageFileSystems.Count;
            return count;
        }

        private static int CountEntriesRecursive(FsYamlNode node)
        {
            int count = 1;
            if (node.Children != null)
            {
                foreach (FsYamlNode child in node.Children)
                    count += CountEntriesRecursive(child);
            }
            return count;
        }

        private static readonly string[] UnicodeFileNamePatterns =
        [
            "\u30C7\u30FC\u30BF{0}.dat",
            "\u6587\u4EF6{0}.bin",
            "caf\u00E9{0}.txt",
            "\u0444\u0430\u0439\u043B{0}.dat",
            "r\u00E9sum\u00E9{0}.doc",
            "has space {0}.app",
            "special[{0}].h3",
            "colon:{0}.tmd",
            "{0}starts_digit.txt",
        ];

        private static readonly string[] UnicodeDirNamePatterns =
        [
            "\u30C7\u30A3\u30EC\u30AF\u30C8\u30EA{0}",
            "\u76EE\u5F55{0}",
            "dossier{0}",
            "\u043F\u0430\u043F\u043A\u0430{0}",
        ];

        private static string GenerateFileName(int seed, int index)
        {
            int hash = Math.Abs((seed * 31) + (index * 17));
            if (hash % 3 == 0)
            {
                int patternIdx = hash % UnicodeFileNamePatterns.Length;
                return string.Format(UnicodeFileNamePatterns[patternIdx], index);
            }
            return $"file{seed}_{index}.dat";
        }

        private static string GenerateDirName(int seed, int depth)
        {
            int hash = Math.Abs((seed * 37) + (depth * 13));
            if (hash % 4 == 0)
            {
                int patternIdx = hash % UnicodeDirNamePatterns.Length;
                return string.Format(UnicodeDirNamePatterns[patternIdx], depth);
            }
            return $"dir{depth}";
        }

        private static bool ShouldBeSystem(int seed, int index) => Math.Abs((seed * 41) + (index * 23)) % 4 == 0;

        private static FsYaml BuildFsYaml(int nFiles, int nDirLevels, int nIfs, int seed, int nFilesPerDir = 1)
        {
            FsYaml yaml = new FsYaml();

            FsYamlNode root = yaml.AddFileSystem(".", 0);

            for (int i = 0; i < nFiles; i++)
            {
                string name = GenerateFileName(seed, i);
                long offset = (long)(seed + (i * 1000)) & 0x7FFFFFFF;
                long size = (long)((seed + (i * 777)) & 0x7FFFFFFF);
                ulong xxhash = (ulong)(uint)((seed + (i * 333)) & 0x7FFFFFFF);
                uint crc = (uint)((seed + (i * 555)) & 0x7FFFFFFF);
                bool isSystem = ShouldBeSystem(seed, i);
                root.AddFile(name, offset, size, xxhash, crc, isSystem);
            }

            FsYamlNode currentDir = root;
            for (int d = 0; d < nDirLevels; d++)
            {
                string dirName = GenerateDirName(seed, d);
                bool dirIsSystem = ShouldBeSystem(seed, 100 + d);
                currentDir = currentDir.AddDirectory(dirName, dirIsSystem);

                for (int f = 0; f < nFilesPerDir; f++)
                {
                    int fileIdx = (d * 100) + f;
                    string name = GenerateFileName(seed + d, fileIdx);
                    long offset = (long)(seed + (d * 2000) + (f * 500)) & 0x7FFFFFFF;
                    long size = (long)((seed + (d * 1500) + (f * 300)) & 0x7FFFFFFF);
                    ulong xxhash = (ulong)(uint)((seed + (d * 700) + (f * 200)) & 0x7FFFFFFF);
                    uint crc = (uint)((seed + (d * 900) + (f * 400)) & 0x7FFFFFFF);
                    bool isSystem = ShouldBeSystem(seed, 200 + (d * 10) + f);
                    currentDir.AddFile(name, offset, size, xxhash, crc, isSystem);
                }
            }

            for (int i = 0; i < nIfs; i++)
            {
                string name = $"app{seed}_{i}.bin";
                long imageId = (long)((seed + (i * 111)) & 0x7FFFFFFF);
                long size = (long)((seed + (i * 222)) & 0x7FFFFFFF);
                yaml.AddIfsEntry(name, imageId, size);
            }

            return yaml;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 2: Has_More_Extents flag encoding correctness
        ///
        /// For any NkFs built from FsYaml containing a mix of single-extent files and multi-extent
        /// files (chains of 2+ same-name entries), every non-final entry in a multi-extent chain
        /// SHALL have bit 5 of its prefix byte set to 1, and every final entry (or single-extent
        /// file) SHALL have bit 5 set to 0.
        ///
        /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 6.2, 10.6**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool HasMoreExtents_FlagEncoding_Correctness(
            NonNegativeInt chainCountSeed,
            NonNegativeInt singleCountSeed,
            NonNegativeInt chainLengthSeed,
            NonNegativeInt seed)
        {
            int s = seed.Get;
            int numChains = 1 + (chainCountSeed.Get % 4);  // 1-4 multi-extent chains
            int numSingles = 1 + (singleCountSeed.Get % 4); // 1-4 single-extent files
            int baseChainLength = 2 + (chainLengthSeed.Get % 4); // 2-5 extents per chain

            FsYaml fsYaml = BuildFsYamlWithMultiExtentFiles(numChains, numSingles, baseChainLength, s);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Walk the root's children and verify HasMoreExtents flag correctness
            return verifyHasMoreExtentsFlags(nkfs, 0);
        }

        /// <summary>
        /// Recursively verifies that HasMoreExtents flags are correctly set for all file entries
        /// within a directory: non-final chain entries have HasMoreExtents=true, final/single entries
        /// have HasMoreExtents=false.
        /// </summary>
        private static bool verifyHasMoreExtentsFlags(NkFs nkfs, int dirIndex)
        {
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(dirIndex).ToList();

            // Group consecutive file entries by name to identify chains
            int i = 0;
            while (i < children.Count)
            {
                (int childIndex, NkFsEntry childEntry) = children[i];

                if (childEntry.IsDirectory)
                {
                    // Recurse into subdirectories
                    if (!verifyHasMoreExtentsFlags(nkfs, childIndex))
                        return false;
                    i++;
                    continue;
                }

                // It's a file — identify the chain of consecutive same-name entries
                string name = nkfs.GetEntryName(childIndex);
                int chainStart = i;

                // Collect all consecutive entries with the same name
                while (i + 1 < children.Count)
                {
                    (int nextIndex, NkFsEntry nextEntry) = children[i + 1];
                    if (nextEntry.IsDirectory)
                        break;
                    string nextName = nkfs.GetEntryName(nextIndex);
                    if (nextName != name)
                        break;
                    i++;
                }

                int chainEnd = i; // inclusive
                int chainLength = chainEnd - chainStart + 1;

                // Verify HasMoreExtents flags for this chain
                for (int j = chainStart; j <= chainEnd; j++)
                {
                    (int entryIndex, NkFsEntry _) = children[j];
                    bool hasMore = nkfs.HasMoreExtents(entryIndex);

                    if (j < chainEnd)
                    {
                        // Non-final entry in chain: must have HasMoreExtents = true
                        if (!hasMore)
                            return false;
                    }
                    else
                    {
                        // Final entry (or single-extent): must have HasMoreExtents = false
                        if (hasMore)
                            return false;
                    }
                }

                i++;
            }

            return true;
        }

        /// <summary>
        /// Builds an FsYaml with a mix of single-extent files and multi-extent files (chains of
        /// consecutive same-name file entries) to test Has_More_Extents flag encoding.
        /// </summary>
        private static FsYaml BuildFsYamlWithMultiExtentFiles(int numChains, int numSingles, int baseChainLength, int seed)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);

            long baseOffset = 1_000_000L + (long)(seed & 0x7FFFFFFF);
            int offsetStep = 100_000;

            // Add some single-extent files at root level
            for (int i = 0; i < numSingles; i++)
            {
                string name = $"single_{seed}_{i}.bin";
                long offset = baseOffset + (i * offsetStep);
                long size = 50_000L + (i * 1000);
                ulong xxhash = (ulong)(uint)((seed + (i * 333)) & 0x7FFFFFFF);
                uint crc = (uint)((seed + (i * 555)) & 0x7FFFFFFF);
                root.AddFile(name, offset, size, xxhash, crc);
            }

            // Add multi-extent chains at root level
            for (int c = 0; c < numChains; c++)
            {
                string chainName = $"multi_{seed}_{c}.m2ts";
                int chainLength = baseChainLength + (c % 3); // vary chain lengths slightly

                for (int e = 0; e < chainLength; e++)
                {
                    long offset = baseOffset + ((numSingles + (c * 10) + e) * offsetStep);
                    long size = 80_000L + (e * 2000);
                    ulong xxhash = (ulong)(uint)((seed + (c * 1000) + (e * 100)) & 0x7FFFFFFF);
                    uint crc = (uint)((seed + (c * 700) + (e * 50)) & 0x7FFFFFFF);
                    root.AddFile(chainName, offset, size, xxhash, crc);
                }
            }

            // Add a subdirectory with both single and multi-extent files
            FsYamlNode subDir = root.AddDirectory("streams");

            for (int i = 0; i < numSingles; i++)
            {
                string name = $"sub_single_{seed}_{i}.dat";
                long offset = baseOffset + ((100 + i) * offsetStep);
                long size = 30_000L + (i * 500);
                ulong xxhash = (ulong)(uint)((seed + 5000 + (i * 111)) & 0x7FFFFFFF);
                uint crc = (uint)((seed + 5000 + (i * 222)) & 0x7FFFFFFF);
                subDir.AddFile(name, offset, size, xxhash, crc);
            }

            for (int c = 0; c < numChains; c++)
            {
                string chainName = $"sub_multi_{seed}_{c}.stream";
                int chainLength = baseChainLength + ((c + 1) % 3);

                for (int e = 0; e < chainLength; e++)
                {
                    long offset = baseOffset + ((200 + (c * 10) + e) * offsetStep);
                    long size = 60_000L + (e * 1500);
                    ulong xxhash = (ulong)(uint)((seed + 8000 + (c * 500) + (e * 80)) & 0x7FFFFFFF);
                    uint crc = (uint)((seed + 8000 + (c * 300) + (e * 40)) & 0x7FFFFFFF);
                    subDir.AddFile(chainName, offset, size, xxhash, crc);
                }
            }

            return yaml;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 5: Entry table structural integrity with multi-extent chains
        ///
        /// **Validates: Requirements 2.1, 2.2, 2.3, 4.3, 10.5**
        ///
        /// For any NkFs built from FsYaml containing multi-extent files, all entries in a
        /// multi-extent chain SHALL be consecutive siblings within their parent directory
        /// (no interleaving with other file entries), SHALL appear in ascending image-offset
        /// order, and every directory's NextEntryIndex SHALL correctly account for all extent
        /// entries as distinct children.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool EntryTableStructuralIntegrity_MultiExtentChains(
            NonNegativeInt chainCountSeed,
            NonNegativeInt singleCountSeed,
            NonNegativeInt chainLengthSeed,
            NonNegativeInt seed)
        {
            int s = seed.Get;
            int numChains = 1 + (chainCountSeed.Get % 4);    // 1-4 multi-extent chains
            int numSingles = 1 + (singleCountSeed.Get % 4);  // 1-4 single-extent files
            int baseChainLength = 2 + (chainLengthSeed.Get % 4); // 2-5 extents per chain

            FsYaml fsYaml = BuildFsYamlWithMultiExtentFiles(numChains, numSingles, baseChainLength, s);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Verify structural integrity for all directories
            return verifyEntryTableStructuralIntegrity(nkfs, 0);
        }

        /// <summary>
        /// Recursively verifies entry table structural integrity for a directory:
        /// 1. Multi-extent chain entries are consecutive siblings (no interleaving)
        /// 2. Chain entries appear in ascending image-offset (FileOffset) order
        /// 3. NextEntryIndex accounts for all extent entries as distinct children
        /// </summary>
        private static bool verifyEntryTableStructuralIntegrity(NkFs nkfs, int dirIndex)
        {
            NkFsEntry dirEntry = nkfs.GetEntry(dirIndex);
            if (!dirEntry.IsDirectory)
                return false;

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(dirIndex).ToList();

            // Count total direct children (each extent counts as a distinct child)
            int totalChildCount = children.Count;

            // Verify NextEntryIndex: for root (index 0), NextEntryIndex == EntryCount.
            // For other dirs, the subtree size must match.
            // We verify by counting: the children enumeration uses NextEntryIndex internally,
            // so if we can enumerate all expected children, NextEntryIndex is correct.
            // Additionally verify that iterating from dirIndex+1 to NextEntryIndex covers
            // exactly the number of entries in this subtree.
            int expectedNextEntry = dirEntry.NextEntryIndex;
            int subtreeCount = countSubtreeEntries(nkfs, children);
            int actualNextEntry = dirIndex + 1 + subtreeCount;
            if (actualNextEntry != expectedNextEntry)
                return false;

            // Walk children verifying chain consecutiveness and ascending image-offset order
            int i = 0;
            while (i < children.Count)
            {
                (int childIndex, NkFsEntry childEntry) = children[i];

                if (childEntry.IsDirectory)
                {
                    // Recurse into subdirectories
                    if (!verifyEntryTableStructuralIntegrity(nkfs, childIndex))
                        return false;
                    i++;
                    continue;
                }

                // It's a file — identify the chain of consecutive same-name entries
                string name = nkfs.GetEntryName(childIndex);
                List<int> chainIndices = new List<int> { childIndex };
                int chainStart = i;

                while (i + 1 < children.Count)
                {
                    (int nextIndex, NkFsEntry nextEntry) = children[i + 1];
                    if (nextEntry.IsDirectory)
                        break;
                    string nextName = nkfs.GetEntryName(nextIndex);
                    if (nextName != name)
                        break;
                    chainIndices.Add(nextIndex);
                    i++;
                }

                // Property: chain entries must be at consecutive entry table indices
                // (no gaps — they are immediate siblings in the entry table)
                for (int j = 1; j < chainIndices.Count; j++)
                {
                    if (chainIndices[j] != chainIndices[j - 1] + 1)
                        return false;
                }

                // Property: chain entries must appear in ascending image-offset (FileOffset) order
                for (int j = 1; j < chainIndices.Count; j++)
                {
                    NkFsEntry prevEntry = nkfs.GetEntry(chainIndices[j - 1]);
                    NkFsEntry currEntry = nkfs.GetEntry(chainIndices[j]);
                    if (currEntry.FileOffset <= prevEntry.FileOffset)
                        return false;
                }

                // Property: non-final entries have HasMoreExtents, final does not
                for (int j = 0; j < chainIndices.Count; j++)
                {
                    bool hasMore = nkfs.HasMoreExtents(chainIndices[j]);
                    if (j < chainIndices.Count - 1)
                    {
                        if (!hasMore) return false;
                    }
                    else
                    {
                        if (hasMore) return false;
                    }
                }

                i++;
            }

            return true;
        }

        /// <summary>
        /// Counts the total number of entries in a directory's subtree (including all nested entries).
        /// </summary>
        private static int countSubtreeEntries(NkFs nkfs, List<(int index, NkFsEntry entry)> children)
        {
            int count = 0;
            foreach ((int childIndex, NkFsEntry childEntry) in children)
            {
                count++; // count the child itself
                if (childEntry.IsDirectory)
                {
                    // Add all entries in this subdirectory's subtree
                    int subtreeSize = childEntry.NextEntryIndex - childIndex - 1;
                    count += subtreeSize;
                }
            }
            return count;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 4: Chain resolution completeness and consistency
        ///
        /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.6**
        ///
        /// For any NkFs containing multi-extent chains, calling GetExtents(index) on any entry
        /// index within a chain (first, middle, or last) SHALL return the same complete ordered
        /// list of (offset, size) pairs representing all extents in that chain, and for
        /// single-extent files SHALL return exactly one pair.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ChainResolution_CompletenessAndConsistency(
            NonNegativeInt chainCountSeed,
            NonNegativeInt singleCountSeed,
            NonNegativeInt chainLengthSeed,
            NonNegativeInt seed)
        {
            int s = seed.Get;
            int numChains = 1 + (chainCountSeed.Get % 4);    // 1-4 multi-extent chains
            int numSingles = 1 + (singleCountSeed.Get % 4);  // 1-4 single-extent files
            int baseChainLength = 2 + (chainLengthSeed.Get % 4); // 2-5 extents per chain

            FsYaml fsYaml = BuildFsYamlWithMultiExtentFiles(numChains, numSingles, baseChainLength, s);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Verify chain resolution completeness and consistency for all directories
            return verifyChainResolutionCompleteness(nkfs, 0);
        }

        /// <summary>
        /// Recursively verifies chain resolution completeness and consistency for all files
        /// in a directory:
        /// 1. For multi-extent chains: GetExtents returns the same result regardless of which
        ///    entry in the chain is queried (first, middle, or last).
        /// 2. For single-extent files: GetExtents returns exactly one (offset, size) pair.
        /// 3. The returned list is ordered (matches chain order) and complete.
        /// </summary>
        private static bool verifyChainResolutionCompleteness(NkFs nkfs, int dirIndex)
        {
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(dirIndex).ToList();

            int i = 0;
            while (i < children.Count)
            {
                (int childIndex, NkFsEntry childEntry) = children[i];

                if (childEntry.IsDirectory)
                {
                    // Recurse into subdirectories
                    if (!verifyChainResolutionCompleteness(nkfs, childIndex))
                        return false;
                    i++;
                    continue;
                }

                // It's a file — identify the chain of consecutive same-name entries
                string name = nkfs.GetEntryName(childIndex);
                List<int> chainEntryIndices = new List<int> { childIndex };

                while (i + 1 < children.Count)
                {
                    (int nextIndex, NkFsEntry nextEntry) = children[i + 1];
                    if (nextEntry.IsDirectory)
                        break;
                    string nextName = nkfs.GetEntryName(nextIndex);
                    if (nextName != name)
                        break;
                    chainEntryIndices.Add(nextIndex);
                    i++;
                }

                int chainLength = chainEntryIndices.Count;

                if (chainLength == 1)
                {
                    // Single-extent file: GetExtents must return exactly one pair
                    IReadOnlyList<(long offset, long size)> extents = nkfs.GetExtents(chainEntryIndices[0]);
                    if (extents.Count != 1)
                        return false;

                    // The single pair must match the entry's offset and size
                    NkFsEntry singleEntry = nkfs.GetEntry(chainEntryIndices[0]);
                    long expectedOffset = singleEntry.FileOffset;
                    long expectedSize = nkfs.GetFileSize(chainEntryIndices[0]);
                    if (extents[0].offset != expectedOffset || extents[0].size != expectedSize)
                        return false;
                }
                else
                {
                    // Multi-extent chain: GetExtents on any entry must return the same result.
                    // Get the canonical result from the first entry.
                    IReadOnlyList<(long offset, long size)> canonicalExtents = nkfs.GetExtents(chainEntryIndices[0]);

                    // Must have exactly chainLength extents
                    if (canonicalExtents.Count != chainLength)
                        return false;

                    // Each extent must match its corresponding entry's offset and size
                    for (int j = 0; j < chainLength; j++)
                    {
                        NkFsEntry extentEntry = nkfs.GetEntry(chainEntryIndices[j]);
                        long expectedOffset = extentEntry.FileOffset;
                        long expectedSize = nkfs.GetFileSize(chainEntryIndices[j]);
                        if (canonicalExtents[j].offset != expectedOffset || canonicalExtents[j].size != expectedSize)
                            return false;
                    }

                    // Verify calling GetExtents on middle and last entries returns the same list
                    // Test middle entry (for chains of length >= 3)
                    if (chainLength >= 3)
                    {
                        int midIndex = chainLength / 2;
                        IReadOnlyList<(long offset, long size)> midExtents = nkfs.GetExtents(chainEntryIndices[midIndex]);
                        if (!extentsEqual(canonicalExtents, midExtents))
                            return false;
                    }

                    // Test last entry
                    IReadOnlyList<(long offset, long size)> lastExtents = nkfs.GetExtents(chainEntryIndices[chainLength - 1]);
                    if (!extentsEqual(canonicalExtents, lastExtents))
                        return false;

                    // Also test every entry in the chain for full consistency
                    for (int j = 1; j < chainLength - 1; j++)
                    {
                        IReadOnlyList<(long offset, long size)> jExtents = nkfs.GetExtents(chainEntryIndices[j]);
                        if (!extentsEqual(canonicalExtents, jExtents))
                            return false;
                    }

                    // Verify GetTotalFileSize returns sum of all extent sizes
                    long expectedTotalSize = 0;
                    for (int j = 0; j < canonicalExtents.Count; j++)
                        expectedTotalSize += canonicalExtents[j].size;

                    long actualTotalSize = nkfs.GetTotalFileSize(chainEntryIndices[0]);
                    if (actualTotalSize != expectedTotalSize)
                        return false;

                    // GetTotalFileSize must also be consistent from any entry in the chain
                    for (int j = 1; j < chainLength; j++)
                    {
                        if (nkfs.GetTotalFileSize(chainEntryIndices[j]) != expectedTotalSize)
                            return false;
                    }
                }

                i++;
            }

            return true;
        }

        /// <summary>
        /// Compares two extent lists for equality (same count, same offset/size at each position).
        /// </summary>
        private static bool extentsEqual(IReadOnlyList<(long offset, long size)> a, IReadOnlyList<(long offset, long size)> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].offset != b[i].offset || a[i].size != b[i].size)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 6: Backward-compatible individual extent parsing
        ///
        /// **Validates: Requirements 4.2, 4.4**
        ///
        /// For any NkFs containing multi-extent chains, parsing the entry table with the standard
        /// reader methods (GetEntry, GetChildren, GetFileSize, GetEntryName) without using GetExtents
        /// SHALL yield each extent as a valid independent file entry: each has a decodable name,
        /// a valid individual size from its prefix, and a valid ImageOffset — meaning an older reader
        /// ignoring bit 5 would still produce a parseable (though duplicated-name) file listing.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BackwardCompatible_IndividualExtentParsing(
            NonNegativeInt chainCountSeed,
            NonNegativeInt singleCountSeed,
            NonNegativeInt chainLengthSeed,
            NonNegativeInt seed)
        {
            int s = seed.Get;
            int numChains = 1 + (chainCountSeed.Get % 4);    // 1-4 multi-extent chains
            int numSingles = 1 + (singleCountSeed.Get % 4);  // 1-4 single-extent files
            int baseChainLength = 2 + (chainLengthSeed.Get % 4); // 2-5 extents per chain

            FsYaml fsYaml = BuildFsYamlWithMultiExtentFiles(numChains, numSingles, baseChainLength, s);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // Verify backward-compatible parsing for all entries using only
            // standard methods (no GetExtents) — simulating an older reader
            return verifyBackwardCompatibleParsing(nkfs, 0);
        }

        /// <summary>
        /// Recursively verifies that all file entries (including multi-extent chain entries)
        /// are independently parseable using standard reader methods only:
        /// GetEntry, GetChildren, GetFileSize, GetEntryName.
        /// Each extent must be decodable as a standalone file entry.
        /// </summary>
        private static bool verifyBackwardCompatibleParsing(NkFs nkfs, int dirIndex)
        {
            NkFsEntry dirEntry = nkfs.GetEntry(dirIndex);
            if (!dirEntry.IsDirectory)
                return false;

            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(dirIndex).ToList();

            foreach ((int childIndex, NkFsEntry childEntry) in children)
            {
                if (childEntry.IsDirectory)
                {
                    // Recurse into subdirectories
                    if (!verifyBackwardCompatibleParsing(nkfs, childIndex))
                        return false;
                    continue;
                }

                // It's a file entry — verify it's independently parseable

                // 1. GetEntry returns a valid file entry (not directory)
                if (childEntry.IsFile != true)
                    return false;

                // 2. GetEntryName returns a valid non-empty string
                string name = nkfs.GetEntryName(childIndex);
                if (string.IsNullOrEmpty(name))
                    return false;

                // 3. GetFileSize returns the individual extent's size (non-negative)
                long size = nkfs.GetFileSize(childIndex);
                if (size < 0)
                    return false;

                // 4. The entry's FileOffset (ImageOffset) is > 0 (valid image offset)
                if (childEntry.FileOffset <= 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 1: Multi-extent round-trip preservation
        ///
        /// **Validates: Requirements 5.3, 6.1, 6.2, 6.3, 6.4, 5.1, 5.2**
        ///
        /// For any valid FsYaml containing directories with multi-extent files (multiple same-name
        /// file entries per directory, each with arbitrary offsets, sizes, and checksums), building
        /// NkFs from that FsYaml and then exporting back to FsYaml SHALL produce an equivalent
        /// structure: same number of extents per chain, same per-extent offsets, same per-extent
        /// sizes, same per-extent checksums, and same extent ordering.
        /// Also tests the full binary round-trip: FsYaml → NkFs → bytes → NkFs → FsYaml.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool MultiExtent_RoundTrip_Preservation(
            NonNegativeInt chainCountSeed,
            NonNegativeInt singleCountSeed,
            NonNegativeInt chainLengthSeed,
            NonNegativeInt seed)
        {
            int s = seed.Get;
            int numChains = 1 + (chainCountSeed.Get % 4);    // 1-4 multi-extent chains
            int numSingles = 1 + (singleCountSeed.Get % 4);  // 1-4 single-extent files
            int baseChainLength = 2 + (chainLengthSeed.Get % 4); // 2-5 extents per chain

            FsYaml fsYaml = BuildFsYamlWithMultiExtentFiles(numChains, numSingles, baseChainLength, s);

            string originalYaml = fsYaml.ToYaml();

            // Round-trip 1: FsYaml → NkFs → FsYaml (in-memory round-trip)
            NkFs nkfs = NkFs.FromFsYaml(fsYaml);
            FsYaml roundTripped = nkfs.ToFsYaml();
            string roundTrippedYaml = roundTripped.ToYaml();

            if (originalYaml != roundTrippedYaml)
                return false;

            // Round-trip 2: FsYaml → NkFs → bytes → NkFs → FsYaml (full binary round-trip)
            byte[] bytes = nkfs.ToBytes();
            NkFs restored = NkFs.FromBytes(bytes);
            FsYaml binaryRoundTripped = restored.ToFsYaml();
            string binaryRoundTrippedYaml = binaryRoundTripped.ToYaml();

            if (originalYaml != binaryRoundTrippedYaml)
                return false;

            // Structural verification: verify multi-extent chains are preserved correctly
            if (!verifyMultiExtentRoundTripStructure(fsYaml, roundTripped))
                return false;

            if (!verifyMultiExtentRoundTripStructure(fsYaml, binaryRoundTripped))
                return false;

            return true;
        }

        /// <summary>
        /// Verifies that the round-tripped FsYaml has the same multi-extent chain structure
        /// as the original: same number of extents per chain, same per-extent offsets, sizes,
        /// checksums, and ordering.
        /// </summary>
        private static bool verifyMultiExtentRoundTripStructure(FsYaml original, FsYaml roundTripped)
        {
            if (original.FileSystems.Count != roundTripped.FileSystems.Count)
                return false;

            for (int fsIdx = 0; fsIdx < original.FileSystems.Count; fsIdx++)
            {
                FsYamlNode origFs = original.FileSystems[fsIdx];
                FsYamlNode rtFs = roundTripped.FileSystems[fsIdx];

                if (!verifyNodeChildren(origFs, rtFs))
                    return false;
            }

            // Verify IFS entries match
            if (original.ImageFileSystems.Count != roundTripped.ImageFileSystems.Count)
                return false;

            for (int i = 0; i < original.ImageFileSystems.Count; i++)
            {
                FsYamlIfsEntry origIfs = original.ImageFileSystems[i];
                FsYamlIfsEntry rtIfs = roundTripped.ImageFileSystems[i];

                if (origIfs.FileName != rtIfs.FileName)
                    return false;
                if (origIfs.ImageId != rtIfs.ImageId)
                    return false;
                if (origIfs.Size != rtIfs.Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively verifies that two FsYamlNode trees have identical children including
        /// multi-extent chain structure (same-name file entries with per-extent data).
        /// </summary>
        private static bool verifyNodeChildren(FsYamlNode original, FsYamlNode roundTripped)
        {
            List<FsYamlNode> origChildren = original.Children ?? new List<FsYamlNode>();
            List<FsYamlNode> rtChildren = roundTripped.Children ?? new List<FsYamlNode>();

            if (origChildren.Count != rtChildren.Count)
                return false;

            for (int i = 0; i < origChildren.Count; i++)
            {
                FsYamlNode origChild = origChildren[i];
                FsYamlNode rtChild = rtChildren[i];

                // Name must match
                if (origChild.Name != rtChild.Name)
                    return false;

                // Type must match (directory vs file)
                if (origChild.IsDirectory != rtChild.IsDirectory)
                    return false;

                if (origChild.IsDirectory)
                {
                    // Recurse into directories
                    if (!verifyNodeChildren(origChild, rtChild))
                        return false;
                }
                else
                {
                    // File entry: verify per-extent data preservation
                    if (origChild.Offset != rtChild.Offset)
                        return false;
                    if (origChild.Size != rtChild.Size)
                        return false;
                    if (origChild.XxHash64 != rtChild.XxHash64)
                        return false;
                    if (origChild.Crc32 != rtChild.Crc32)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 8: VFS multi-extent byte-offset mapping
        ///
        /// **Validates: Requirements 12.1, 12.5**
        ///
        /// For any multi-extent file with K extents of known sizes, and for any logical byte offset
        /// within [0, total_size), the VFS extent-mapping logic SHALL identify the correct extent
        /// index and the correct position within that extent such that:
        /// sum(sizes[0..ext_idx-1]) <= byte_offset < sum(sizes[0..ext_idx]) and
        /// position_in_extent = byte_offset - sum(sizes[0..ext_idx-1]).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool VfsMultiExtent_ByteOffsetMapping(
            NonNegativeInt extentCountSeed,
            NonNegativeInt byteOffsetSeed,
            NonNegativeInt seed)
        {
            int s = seed.Get;
            int numExtents = 2 + (extentCountSeed.Get % 6); // 2-7 extents

            // Generate extent sizes: each between 1 and 500_000 bytes to keep test fast
            long[] extentSizes = new long[numExtents];
            long totalSize = 0;
            for (int i = 0; i < numExtents; i++)
            {
                // Ensure each extent has a positive size (at least 1 byte)
                extentSizes[i] = 1L + (long)(Math.Abs((s * 97) + (i * 1013)) % 500_000);
                totalSize += extentSizes[i];
            }

            // Build an FsYaml with a multi-extent file using the generated sizes
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("streams");

            string fileName = "video.m2ts";
            long baseOffset = 0x10000L;

            for (int i = 0; i < numExtents; i++)
            {
                long imageOffset = baseOffset + (i * 0x100000L); // Non-contiguous image offsets
                ulong xxhash = (ulong)(uint)((s + (i * 111)) & 0x7FFFFFFF);
                uint crc = (uint)((s + (i * 222)) & 0x7FFFFFFF);
                dir.AddFile(fileName, imageOffset, extentSizes[i], xxhash, crc);
            }

            // Build NkFs and create NkFsFileItem
            NkFs nkfs = NkFs.FromFsYaml(yaml);

            // The structure is: Entry 0 = root, Entry 1 = streams dir, Entry 2 = first extent
            int firstExtentIndex = 2;
            NkFsEntry firstEntry = nkfs.GetEntry(firstExtentIndex);
            NkFsFileItem fileItem = new NkFsFileItem(nkfs, firstExtentIndex, firstEntry);

            // Property 1: Total FsSize equals sum of all extent sizes
            if (fileItem.FsSize != totalSize)
                return false;

            // Property 2: SplitParts must be non-null for multi-extent files
            IFsFileParts splitParts = fileItem.SplitParts;
            if (splitParts == null)
                return false;

            // Property 3: SplitParts must have exactly numExtents parts
            if (splitParts.Parts.Count != numExtents)
                return false;

            // Property 4: Each part's OffsetInFile must equal sum(sizes[0..i-1])
            long cumulativeOffset = 0;
            for (int i = 0; i < numExtents; i++)
            {
                IFsFilePart part = splitParts.Parts[i];

                if (part.OffsetInFile != cumulativeOffset)
                    return false;

                if (part.Index != i)
                    return false;

                // Each part's FsFile.FsSize must equal the individual extent size
                if (part.FsFile.FsSize != extentSizes[i])
                    return false;

                cumulativeOffset += extentSizes[i];
            }

            // Property 5: For an arbitrary byte offset in [0, totalSize), verify correct extent mapping
            // Generate a logical byte offset using the seed
            long byteOffset = (long)((ulong)byteOffsetSeed.Get % (ulong)totalSize);

            // Find the expected extent index and position within that extent
            int expectedExtentIdx = -1;
            long expectedPositionInExtent = -1;
            long runningSum = 0;
            for (int i = 0; i < numExtents; i++)
            {
                if (byteOffset < runningSum + extentSizes[i])
                {
                    expectedExtentIdx = i;
                    expectedPositionInExtent = byteOffset - runningSum;
                    break;
                }
                runningSum += extentSizes[i];
            }

            if (expectedExtentIdx < 0)
                return false; // Should not happen since byteOffset < totalSize

            // Verify using SplitParts: find the part that contains the byte offset
            int actualExtentIdx = -1;
            long actualPositionInExtent = -1;
            for (int i = 0; i < splitParts.Parts.Count; i++)
            {
                IFsFilePart part = splitParts.Parts[i];
                if (byteOffset >= part.OffsetInFile && byteOffset < part.OffsetInFile + part.FsFile.FsSize)
                {
                    actualExtentIdx = i;
                    actualPositionInExtent = byteOffset - part.OffsetInFile;
                    break;
                }
            }

            if (actualExtentIdx != expectedExtentIdx)
                return false;

            if (actualPositionInExtent != expectedPositionInExtent)
                return false;

            // Verify the invariant: sum(sizes[0..ext_idx-1]) <= byte_offset < sum(sizes[0..ext_idx])
            long sumBefore = 0;
            for (int i = 0; i < expectedExtentIdx; i++)
                sumBefore += extentSizes[i];
            long sumIncluding = sumBefore + extentSizes[expectedExtentIdx];

            if (!(sumBefore <= byteOffset && byteOffset < sumIncluding))
                return false;

            // Verify position_in_extent = byte_offset - sum(sizes[0..ext_idx-1])
            if (actualPositionInExtent != byteOffset - sumBefore)
                return false;

            return true;
        }

        private static FsYaml BuildFsYamlWithDuplicateNames(int nDuplicateDirs, int nExtraDirs, int seed)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode root = yaml.AddFileSystem(".", 0);

            string dupDirName = "data";
            string dupFileName = "readme.dat";

            for (int i = 0; i < nDuplicateDirs; i++)
            {
                bool dirIsSystem = ShouldBeSystem(seed, 800 + i);
                FsYamlNode dir = root.AddDirectory(dupDirName, dirIsSystem);

                long offset = (long)((seed + (i * 1000)) & 0x7FFFFFFF);
                long size = (long)((seed + (i * 777)) & 0x7FFFFFFF);
                ulong xxhash = (ulong)(uint)((seed + (i * 333) + 1) & 0x7FFFFFFF);
                uint crc = (uint)((seed + (i * 555) + 1) & 0x7FFFFFFF);
                bool fileIsSystem = ShouldBeSystem(seed, 900 + i);
                dir.AddFile(dupFileName, offset, size, xxhash, crc, fileIsSystem);

                if (i == 0)
                {
                    FsYamlNode inner = dir.AddDirectory("inner", ShouldBeSystem(seed, 950));
                    inner.AddFile(dupFileName,
                        (long)((seed + 9999) & 0x7FFFFFFF),
                        (long)((seed + 8888) & 0x7FFFFFFF),
                        (ulong)(uint)((seed + 7777) & 0x7FFFFFFF),
                        (uint)((seed + 6666) & 0x7FFFFFFF),
                        ShouldBeSystem(seed, 960));
                }
            }

            for (int i = 0; i < nExtraDirs; i++)
            {
                string extraName = GenerateDirName(seed + 1000, i);
                FsYamlNode extraDir = root.AddDirectory(extraName, ShouldBeSystem(seed, 970 + i));
                FsYamlNode nestedDup = extraDir.AddDirectory(dupDirName, ShouldBeSystem(seed, 980 + i));
                nestedDup.AddFile(GenerateFileName(seed + 1000, i),
                    (long)((seed + (i * 3000)) & 0x7FFFFFFF),
                    (long)((seed + (i * 2500)) & 0x7FFFFFFF),
                    (ulong)(uint)((seed + (i * 1100)) & 0x7FFFFFFF),
                    (uint)((seed + (i * 1300)) & 0x7FFFFFFF),
                    ShouldBeSystem(seed, 990 + i));
            }

            return yaml;
        }
    }
}
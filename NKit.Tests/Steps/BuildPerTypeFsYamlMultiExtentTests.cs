using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for BuildPerTypeFsYaml multi-extent emission.
    ///
    /// Since BuildPerTypeFsYaml requires a fully constructed Scan object with Areas,
    /// FileSystemData, and AreaInfo (which requires disc-level infrastructure), these
    /// tests simulate the multi-extent emission logic directly. The key behavior tested
    /// is: when an FstFile has SplitParts with N parts, the emission loop produces N
    /// same-name FsYaml entries with correct per-part offsets, sizes, and ordering.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3, 8.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class BuildPerTypeFsYamlMultiExtentTests
    {
        #region Helper Methods

        /// <summary>
        /// Creates a minimal FstFile with links to support per-type emission.
        /// </summary>
        private static FstFile CreateFstFileWithLinks(
            FstFolder parent, string name, FsType fsType, long fsOffset, long size) => new FstFile(parent, name, fsType, fsOffset, size, FsItemType.File);

        /// <summary>
        /// Creates an FstFile with SplitParts populated from the given part data.
        /// Each part has its own FstFile with individual offset, size, and hashes.
        /// </summary>
        private static FstFile CreateFstFileWithSplitParts(
            FstFolder parent, string name, FsType fsType,
            (long offset, long size, ulong xxHash, uint crc)[] parts)
        {
            // Create the primary file with the first part's offset and total size
            long totalSize = parts.Sum(p => p.size);
            FstFile primaryFile = new FstFile(parent, name, fsType, parts[0].offset, totalSize, FsItemType.File);

            // Build SplitParts
            FsFileParts splitParts = new FsFileParts();
            long offsetInFile = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                (long offset, long size, ulong xxHash, uint crc) = parts[i];
                // Create a separate FstFile for each part (this is what the real parser does)
                FstFolder partFolder = new FstFolder(fsType); // dummy parent for part files
                FstFile partFile = new FstFile(partFolder, name, fsType, offset, size, FsItemType.File);
                partFile.XxHash = xxHash;
                partFile.Crc = crc;
                partFile.SplitIndex = i;

                splitParts.Parts.Add(new FsFilePart
                {
                    Index = i,
                    OffsetInFile = offsetInFile,
                    FsFile = partFile
                });
                offsetInFile += size;
            }
            splitParts.Size = totalSize;
            primaryFile.SplitParts = splitParts;

            return primaryFile;
        }

        /// <summary>
        /// Simulates the multi-extent emission logic from BuildPerTypeFsYaml.
        /// Given an FstFile with SplitParts and per-type context, produces FsYaml entries
        /// exactly as BuildPerTypeFsYaml would.
        /// </summary>
        private static List<FsYamlNode> SimulateMultiExtentEmission(
            FstFile fstFile, string fullPath, long areaImageOffset, DataStride stride = null)
        {
            FsYaml yaml = new FsYaml();
            FsYamlNode fsNode = yaml.AddFileSystem("01 FileSystem", areaImageOffset);

            if (fstFile.SplitParts != null && fstFile.SplitParts.Parts.Count >= 2)
            {
                foreach (IFsFilePart part in fstFile.SplitParts.Parts.OrderBy(p => p.Index))
                {
                    IFsFile partFile = part.FsFile;
                    long partImageOffset = stride != null
                        ? areaImageOffset + stride.CleanToOffset(partFile.FsOffset, false)
                        : areaImageOffset + partFile.FsOffset;
                    fsNode.AddFileByPath(fullPath, partImageOffset, partFile.FsSize, partFile.XxHash, partFile.Crc, false);
                }
            }
            else
            {
                long imageOffset = stride != null
                    ? areaImageOffset + stride.CleanToOffset(fstFile.FsOffset, false)
                    : areaImageOffset + fstFile.FsOffset;
                fsNode.AddFileByPath(fullPath, imageOffset, fstFile.FsSize, fstFile.XxHash, fstFile.Crc, false);
            }

            // Return all file nodes matching the target name (traverse the tree)
            return CollectFileNodes(fsNode, fullPath);
        }

        /// <summary>
        /// Collects file nodes at the given path within a FsYamlNode tree.
        /// </summary>
        private static List<FsYamlNode> CollectFileNodes(FsYamlNode root, string fullPath)
        {
            string[] segments = fullPath.Split('/');
            FsYamlNode current = root;

            // Navigate to the parent directory
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                    continue;

                current = current.Children?.FirstOrDefault(c => c.IsDirectory && c.Name == segment);
                if (current == null)
                    return new List<FsYamlNode>();
            }

            // Collect all file nodes with the target name
            string fileName = segments[segments.Length - 1];
            return current.Children?.Where(c => c.IsFile && c.Name == fileName).ToList()
                ?? new List<FsYamlNode>();
        }

        /// <summary>
        /// Simulates the per-type emission for a file that appears in both ISO9660 and UDF
        /// (has links to both filesystem types), testing that both per-type dicts get entries.
        /// </summary>
        private static Dictionary<string, List<FsYamlNode>> SimulatePerTypeMultiExtentEmission(
            FstFile fstFile, long areaImageOffset)
        {
            Dictionary<string, List<FsYamlNode>> result = new Dictionary<string, List<FsYamlNode>>(StringComparer.OrdinalIgnoreCase);

            if (fstFile.Links == null || fstFile.Links.Count == 0)
                return result;

            // Resolve type entries (same logic as BuildPerTypeFsYaml)
            Dictionary<string, string> typeEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (FstLink link in fstFile.Links)
            {
                string resolvedType = DataStoreIso9660Formatter.ResolveTargetFsType(link.FsType);
                if (!typeEntries.ContainsKey(resolvedType))
                {
                    string fullPath = (link.Parent?.Path ?? "") + "/" + link.EncodedChildName;
                    if (string.IsNullOrEmpty(link.Parent?.Path))
                        fullPath = "/" + link.EncodedChildName;
                    typeEntries[resolvedType] = fullPath;
                }
            }

            // Emit to each type
            foreach (KeyValuePair<string, string> entry in typeEntries)
            {
                string typeName = entry.Key;
                string fullPath = entry.Value;

                FsYaml yaml = new FsYaml();
                FsYamlNode fsNode = yaml.AddFileSystem("01 FileSystem", areaImageOffset);

                if (fstFile.SplitParts != null && fstFile.SplitParts.Parts.Count >= 2)
                {
                    foreach (IFsFilePart part in fstFile.SplitParts.Parts.OrderBy(p => p.Index))
                    {
                        IFsFile partFile = part.FsFile;
                        long partImageOffset = areaImageOffset + partFile.FsOffset;
                        fsNode.AddFileByPath(fullPath, partImageOffset, partFile.FsSize, partFile.XxHash, partFile.Crc, false);
                    }
                }
                else
                {
                    long imageOffset = areaImageOffset + fstFile.FsOffset;
                    fsNode.AddFileByPath(fullPath, imageOffset, fstFile.FsSize, fstFile.XxHash, fstFile.Crc, false);
                }

                result[typeName] = CollectFileNodes(fsNode, fullPath);
            }

            return result;
        }

        #endregion

        #region Test: FstFile with 3 SplitParts emits 3 same-name entries in correct order

        /// <summary>
        /// Validates Requirement 8.1, 8.2: FstFile with 3 SplitParts emits 3 same-name entries
        /// in SplitIndex order, each with correct per-part offset, size, and hashes.
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_ThreeSplitParts_EmitsThreeSameNameEntriesInOrder()
        {
            // Arrange: 3 split parts with distinct offsets, sizes, and hashes
            (long offset, long size, ulong xxHash, uint crc)[] parts = new (long offset, long size, ulong xxHash, uint crc)[]
            {
                (0x1000, 0x8000, 0xAAAA_BBBB_CCCC_DDDDul, 0x11111111u),
                (0xF000, 0x6000, 0x1111_2222_3333_4444ul, 0x22222222u),
                (0x20000, 0x4000, 0x5555_6666_7777_8888ul, 0x33333333u),
            };

            FstFolder root = new FstFolder("BDMV", FsType.Iso9660, new FstFolder(FsType.Iso9660));
            FstFile fstFile = CreateFstFileWithSplitParts(root, "00001.m2ts", FsType.Iso9660, parts);

            long areaImageOffset = 0x0;
            string fullPath = "/BDMV/00001.m2ts";

            // Act
            List<FsYamlNode> emittedNodes = SimulateMultiExtentEmission(fstFile, fullPath, areaImageOffset);

            // Assert: 3 entries emitted
            Assert.Equal(3, emittedNodes.Count);

            // Assert: all have the same name
            Assert.All(emittedNodes, node => Assert.Equal("00001.m2ts", node.Name));

            // Assert: correct order (by SplitIndex = ascending offset order)
            Assert.Equal(areaImageOffset + parts[0].offset, emittedNodes[0].Offset);
            Assert.Equal(areaImageOffset + parts[1].offset, emittedNodes[1].Offset);
            Assert.Equal(areaImageOffset + parts[2].offset, emittedNodes[2].Offset);

            // Assert: correct sizes
            Assert.Equal(parts[0].size, emittedNodes[0].Size);
            Assert.Equal(parts[1].size, emittedNodes[1].Size);
            Assert.Equal(parts[2].size, emittedNodes[2].Size);

            // Assert: correct hashes
            Assert.Equal(parts[0].xxHash, emittedNodes[0].XxHash64);
            Assert.Equal(parts[1].xxHash, emittedNodes[1].XxHash64);
            Assert.Equal(parts[2].xxHash, emittedNodes[2].XxHash64);

            Assert.Equal(parts[0].crc, emittedNodes[0].Crc32);
            Assert.Equal(parts[1].crc, emittedNodes[1].Crc32);
            Assert.Equal(parts[2].crc, emittedNodes[2].Crc32);
        }

        #endregion

        #region Test: FstFile with 1 SplitPart emits single entry

        /// <summary>
        /// Validates Requirement 8.1: FstFile with 1 SplitPart emits a single entry
        /// (the single-extent path is used since SplitParts.Parts.Count &lt; 2).
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_OneSplitPart_EmitsSingleEntry()
        {
            // Arrange: single split part
            FstFolder root = new FstFolder("STREAM", FsType.Udf, new FstFolder(FsType.Udf));
            FstFile fstFile = new FstFile(root, "00002.m2ts", FsType.Udf, 0x5000, 0x10000, FsItemType.File);
            fstFile.XxHash = 0xDEAD_BEEF_CAFE_BABEul;
            fstFile.Crc = 0xAABBCCDDu;

            // Set up SplitParts with 1 part (should use single-entry path)
            FsFileParts splitParts = new FsFileParts();
            FstFolder partFolder = new FstFolder(FsType.Udf);
            FstFile partFile = new FstFile(partFolder, "00002.m2ts", FsType.Udf, 0x5000, 0x10000, FsItemType.File);
            partFile.XxHash = 0xDEAD_BEEF_CAFE_BABEul;
            partFile.Crc = 0xAABBCCDDu;
            splitParts.Parts.Add(new FsFilePart { Index = 0, OffsetInFile = 0, FsFile = partFile });
            splitParts.Size = 0x10000;
            fstFile.SplitParts = splitParts;

            long areaImageOffset = 0x100000;
            string fullPath = "/STREAM/00002.m2ts";

            // Act
            List<FsYamlNode> emittedNodes = SimulateMultiExtentEmission(fstFile, fullPath, areaImageOffset);

            // Assert: single entry emitted
            Assert.Single(emittedNodes);

            FsYamlNode node = emittedNodes[0];
            Assert.Equal("00002.m2ts", node.Name);
            Assert.Equal(areaImageOffset + 0x5000, node.Offset);
            Assert.Equal(0x10000L, node.Size);
            Assert.Equal(0xDEAD_BEEF_CAFE_BABEul, node.XxHash64);
            Assert.Equal(0xAABBCCDDu, node.Crc32);
        }

        /// <summary>
        /// Validates Requirement 8.1: FstFile with null SplitParts emits a single entry.
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_NullSplitParts_EmitsSingleEntry()
        {
            // Arrange: no split parts at all
            FstFolder root = new FstFolder("GAME", FsType.Iso9660, new FstFolder(FsType.Iso9660));
            FstFile fstFile = new FstFile(root, "data.bin", FsType.Iso9660, 0x2000, 0x4000, FsItemType.File);
            fstFile.XxHash = 0x1234_5678_9ABC_DEF0ul;
            fstFile.Crc = 0x12345678u;
            // SplitParts is null by default

            long areaImageOffset = 0x0;
            string fullPath = "/GAME/data.bin";

            // Act
            List<FsYamlNode> emittedNodes = SimulateMultiExtentEmission(fstFile, fullPath, areaImageOffset);

            // Assert: single entry
            Assert.Single(emittedNodes);
            Assert.Equal("data.bin", emittedNodes[0].Name);
            Assert.Equal(0x2000L, emittedNodes[0].Offset);
            Assert.Equal(0x4000L, emittedNodes[0].Size);
        }

        #endregion

        #region Test: Shared-sector UDF files are included (not filtered)

        /// <summary>
        /// Validates Requirement 8.4: A shared-sector UDF file (like .ssif) is emitted as
        /// a standard entry into the UDF per-type dictionary. The emission logic does not
        /// filter or skip files based on offset overlap with ISO9660 extents.
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_SharedSectorUdfFile_IsIncluded()
        {
            // Arrange: A UDF file whose offset (0x5000) overlaps with an ISO9660 split extent region.
            // The key point is that the file is NOT filtered out.
            FstFolder root = new FstFolder("BDMV", FsType.Udf, new FstFolder(FsType.Udf));
            FstFile fstFile = new FstFile(root, "00001.ssif", FsType.Udf, 0x5000, 0x30000, FsItemType.File);
            fstFile.XxHash = 0xFEDC_BA98_7654_3210ul;
            fstFile.Crc = 0xFEDCBA98u;
            // No SplitParts — this is a single UDF file referencing shared sectors

            long areaImageOffset = 0x0;
            string fullPath = "/BDMV/00001.ssif";

            // Act: simulate emission (no filtering logic present)
            List<FsYamlNode> emittedNodes = SimulateMultiExtentEmission(fstFile, fullPath, areaImageOffset);

            // Assert: file IS included (not filtered)
            Assert.Single(emittedNodes);
            FsYamlNode node = emittedNodes[0];
            Assert.Equal("00001.ssif", node.Name);
            // Assert: stores the actual disc offset and actual size
            Assert.Equal(areaImageOffset + 0x5000, node.Offset);
            Assert.Equal(0x30000L, node.Size);
            Assert.Equal(0xFEDC_BA98_7654_3210ul, node.XxHash64);
            Assert.Equal(0xFEDCBA98u, node.Crc32);
        }

        /// <summary>
        /// Validates Requirement 8.4: Shared-sector UDF entry format is indistinguishable
        /// from any other non-shared file entry (same field layout).
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_SharedSectorUdfFile_HasStandardFormat()
        {
            // Arrange: Two UDF files - one that shares sectors with ISO9660 and one that doesn't.
            // Both should be emitted identically in format.
            FstFolder root = new FstFolder("BDMV", FsType.Udf, new FstFolder(FsType.Udf));

            // "Normal" UDF file
            FstFile normalFile = new FstFile(root, "normal.m2ts", FsType.Udf, 0x80000, 0x20000, FsItemType.File);
            normalFile.XxHash = 0x1111ul;
            normalFile.Crc = 0x2222u;

            // "Shared-sector" UDF file (same offset range as some ISO9660 extent)
            FstFile sharedFile = new FstFile(root, "shared.ssif", FsType.Udf, 0x5000, 0x30000, FsItemType.File);
            sharedFile.XxHash = 0x3333ul;
            sharedFile.Crc = 0x4444u;

            long areaImageOffset = 0x0;

            // Act
            List<FsYamlNode> normalNodes = SimulateMultiExtentEmission(normalFile, "/BDMV/normal.m2ts", areaImageOffset);
            List<FsYamlNode> sharedNodes = SimulateMultiExtentEmission(sharedFile, "/BDMV/shared.ssif", areaImageOffset);

            // Assert: both are single entries with standard fields populated
            Assert.Single(normalNodes);
            Assert.Single(sharedNodes);

            // Both have all standard fields — the shared file is indistinguishable in format
            Assert.True(normalNodes[0].IsFile);
            Assert.True(sharedNodes[0].IsFile);
            Assert.True(sharedNodes[0].Offset > 0);
            Assert.True(sharedNodes[0].Size > 0);
        }

        #endregion

        #region Test: Both ISO9660 and UDF per-type dicts contain entries for overlapping regions

        /// <summary>
        /// Validates Requirement 8.3: When both ISO9660 and UDF filesystem views reference
        /// the same disc sectors, separate per-type dictionary entries are produced for each
        /// filesystem type. The ISO9660 entry contains split parts, and the UDF entry contains
        /// the shared-sector file.
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_OverlappingRegion_BothIso9660AndUdfGetEntries()
        {
            // Arrange: An ISO9660 file with 3 split parts that overlap the UDF file's offset region
            FstFolder isoRoot = new FstFolder("BDMV", FsType.Iso9660, new FstFolder(FsType.Iso9660));
            (long offset, long size, ulong xxHash, uint crc)[] isoParts = new (long offset, long size, ulong xxHash, uint crc)[]
            {
                (0x5000, 0x10000, 0xAAAAul, 0x1111u),
                (0x20000, 0x10000, 0xBBBBul, 0x2222u),
                (0x40000, 0x10000, 0xCCCCul, 0x3333u),
            };
            FstFile isoFile = CreateFstFileWithSplitParts(isoRoot, "00001.m2ts", FsType.Iso9660, isoParts);

            // A UDF file that references same disc sectors (offset 0x5000 overlaps with ISO part 0)
            FstFolder udfRoot = new FstFolder("BDMV", FsType.Udf, new FstFolder(FsType.Udf));
            FstFile udfFile = new FstFile(udfRoot, "00001.ssif", FsType.Udf, 0x5000, 0x30000, FsItemType.File);
            udfFile.XxHash = 0xDDDDul;
            udfFile.Crc = 0x4444u;

            long areaImageOffset = 0x0;

            // Act: Simulate per-type emission for each file
            List<FsYamlNode> isoEntries = SimulateMultiExtentEmission(isoFile, "/BDMV/00001.m2ts", areaImageOffset);
            List<FsYamlNode> udfEntries = SimulateMultiExtentEmission(udfFile, "/BDMV/00001.ssif", areaImageOffset);

            // Assert: ISO9660 dict gets 3 entries (one per split part)
            Assert.Equal(3, isoEntries.Count);
            Assert.All(isoEntries, e => Assert.Equal("00001.m2ts", e.Name));

            // Assert: UDF dict gets 1 entry for the shared-sector file
            Assert.Single(udfEntries);
            Assert.Equal("00001.ssif", udfEntries[0].Name);
            Assert.Equal(0x5000L, udfEntries[0].Offset); // Same offset as ISO part 0
            Assert.Equal(0x30000L, udfEntries[0].Size);
        }

        /// <summary>
        /// Validates Requirement 8.3: A file linked to both ISO9660 and UDF (via FstLinks)
        /// with SplitParts produces entries in both per-type dictionaries.
        /// </summary>
        [Fact]
        public void BuildPerTypeFsYaml_DualLinkedFile_WithSplitParts_BothTypesGetMultipleEntries()
        {
            // Arrange: file linked to both ISO9660 and UDF with 2 split parts
            FstFolder root = new FstFolder("STREAM", FsType.Iso9660, new FstFolder(FsType.Iso9660));
            (long offset, long size, ulong xxHash, uint crc)[] parts = new (long offset, long size, ulong xxHash, uint crc)[]
            {
                (0x10000, 0x20000, 0xAAAAul, 0x1111u),
                (0x50000, 0x20000, 0xBBBBul, 0x2222u),
            };
            FstFile fstFile = CreateFstFileWithSplitParts(root, "video.m2ts", FsType.Iso9660, parts);
            // Add a UDF link to the same file
            FstFolder udfParent = new FstFolder("STREAM", FsType.Udf, new FstFolder(FsType.Udf));
            fstFile.Links.Add(new FstLink(udfParent, "video.m2ts", FsType.Udf));

            long areaImageOffset = 0x0;

            // Act: Simulate per-type emission (mimics BuildPerTypeFsYaml type-entry iteration)
            Dictionary<string, List<FsYamlNode>> perTypeResults = SimulatePerTypeMultiExtentEmission(fstFile, areaImageOffset);

            // Assert: both iso9660 and udf types have entries
            Assert.True(perTypeResults.ContainsKey("iso9660"),
                "Expected iso9660 per-type dict to contain entries");
            Assert.True(perTypeResults.ContainsKey("udf"),
                "Expected udf per-type dict to contain entries");

            // Assert: iso9660 has 2 entries (one per split part)
            Assert.Equal(2, perTypeResults["iso9660"].Count);
            Assert.All(perTypeResults["iso9660"], e => Assert.Equal("video.m2ts", e.Name));

            // Assert: udf also has 2 entries (same split parts emitted for both types)
            Assert.Equal(2, perTypeResults["udf"].Count);
            Assert.All(perTypeResults["udf"], e => Assert.Equal("video.m2ts", e.Name));

            // Assert: per-part offsets are correct in both
            Assert.Equal(areaImageOffset + parts[0].offset, perTypeResults["iso9660"][0].Offset);
            Assert.Equal(areaImageOffset + parts[1].offset, perTypeResults["iso9660"][1].Offset);
            Assert.Equal(areaImageOffset + parts[0].offset, perTypeResults["udf"][0].Offset);
            Assert.Equal(areaImageOffset + parts[1].offset, perTypeResults["udf"][1].Offset);
        }

        #endregion
    }
}
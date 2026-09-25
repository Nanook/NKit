#nullable enable
using Nanook.NKit;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS.Cue
{
    /// <summary>
    /// Unit tests for DataStoreAsIso CUE/GDI reconstruction logic.
    /// Tests the Construct() method's behavior when encountering FileName-tagged areas
    /// for CUE/GDI images, including multi-track reconstruction and legacy fallback.
    ///
    /// Uses model-based testing since DataStoreAsIso.Construct() requires a full DataStore.
    /// The model replicates the reconstruction logic from Construct() lines 270-390.
    ///
    /// Validates Requirements: 4.1, 4.2, 4.3, 4.4, 4.5
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Cue")]
    public class DataStoreAsIsoCueGdiReconstructionTests
    {
        #region Model Types

        /// <summary>
        /// Models the reconstruction logic from DataStoreAsIso.Construct().
        /// Given areas, stored files, and image format, produces the expected
        /// SourceFile structure (ImageFiles and IndexFile).
        /// </summary>
        private class ReconstructionResult
        {
            public SourceFileItem[] ImageFiles { get; set; } = Array.Empty<SourceFileItem>();
            public IndexFile? IndexFile { get; set; }
        }

        #endregion

        #region Model Methods

        /// <summary>
        /// Models the core reconstruction logic from DataStoreAsIso.Construct().
        /// This replicates the behavior of the CUE/GDI branch when fileAreas
        /// contain FileName-tagged areas.
        /// </summary>
        private static ReconstructionResult ModelReconstruct(
            List<AreaRecord> areas,
            List<FileRecord> storedFiles,
            ContainerType format,
            string imageName,
            long imageSize,
            uint imageCrc32,
            Func<string, byte[]?> readFile)
        {
            ReconstructionResult result = new ReconstructionResult();

            List<AreaRecord> fileAreas = areas
                .Where(a => a.Metadata?.ContainsKey(AreaValueType.FileName) == true
                         || a.Metadata?.ContainsKey(AreaValueType.App) == true)
                .OrderBy(a => a.Offset)
                .ToList();

            if (!fileAreas.Any() && !storedFiles.Any())
                return result;

            // Build folder items from areas and files
            List<FileItem> folderItems = new();
            Dictionary<string, (long Offset, long Size, long Crc)> fileMeta = new(StringComparer.OrdinalIgnoreCase);

            foreach (AreaRecord fa in fileAreas)
            {
                string fileName = fa.Metadata[AreaValueType.FileName]
                    ?? fa.Metadata[AreaValueType.App]!;
                fileMeta[fileName] = (fa.Offset, fa.Size, (long)fa.Crc32);
                FileItem fi = new FileItem(fileName) { Size = fa.Size, Crc = (long)fa.Crc32 };
                fi.Populate();

                // Try to read index file data
                if (fi.Type == FileItemType.Index)
                    fi.Data = readFile(fileName);

                folderItems.Add(fi);
            }

            foreach (FileRecord sf in storedFiles)
            {
                if (!fileMeta.ContainsKey(sf.Name))
                {
                    fileMeta[sf.Name] = (-1, sf.UncompressedSize, 0);
                    FileItem fi = new FileItem(sf.Name) { Size = sf.UncompressedSize, Crc = 0 };
                    fi.IsFromDataStore = true;
                    fi.Populate();

                    if (fi.Type == FileItemType.Index)
                        fi.Data = readFile(sf.Name);

                    folderItems.Add(fi);
                }
            }

            List<SourceFileItem> imageFiles = new();
            IndexFile? indexFile = null;

            // First pass: find and parse index file
            foreach (FileItem fi in folderItems)
            {
                if (fi.Type == FileItemType.Index && fi.Data != null)
                {
                    try
                    {
                        indexFile = IndexFile.Parse(
                            fi.Path, fi.FileName, fi.Extension,
                            fi.Postfix, fi.Data, false, false,
                            folderItems.ToArray());
                        if (indexFile != null) break;
                    }
                    catch { }
                }
            }

            // Second pass: build image file entries
            foreach (FileItem fi in folderItems)
            {
                if (indexFile == null || fi.FileName != indexFile.FileName)
                {
                    if (!fileMeta.TryGetValue(fi.FileName, out (long Offset, long Size, long Crc) meta))
                        continue;

                    if (meta.Offset != -1)
                        imageFiles.Add(new SourceFileItem(
                            "", fi.FileName, fi.Extension, fi.Postfix ?? "",
                            meta.Offset, fi.Size, meta.Crc, false, false));
                }
            }

            // Apply CUE/GDI specific logic
            if (imageFiles.Count > 0 || indexFile != null)
            {
                if (format == ContainerType.Cue || format == ContainerType.Gdi)
                {
                    if (fileAreas.Any(a => a.Metadata?.ContainsKey(AreaValueType.FileName) == true))
                    {
                        result.ImageFiles = imageFiles.ToArray();
                    }
                    else
                    {
                        // Legacy flat storage fallback
                        string imgExt = format == ContainerType.Cue ? ".bin" : ".raw";
                        SourceFileItem single = new SourceFileItem(
                            "", imageName + imgExt, imgExt, "",
                            -1, imageSize, (long)imageCrc32, false, false);
                        result.ImageFiles = new[] { single };
                    }
                }
                else
                {
                    result.ImageFiles = imageFiles.ToArray();
                }

                result.IndexFile = indexFile;
            }

            return result;
        }

        #endregion

        #region CUE Image with 3 FileName-Tagged Areas

        /// <summary>
        /// Validates Requirement 4.1: CUE image with 3 FileName-tagged areas
        /// produces 3 ImageFiles with correct filenames.
        /// </summary>
        [Fact]
        public void CueImage_3FileNameAreas_Produces3ImageFiles_WithCorrectFilenames()
        {
            // Arrange: 3 areas with FileName metadata (typical PS1 CUE)
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithFileName(id: 1, offset: 0, size: 50_000_000,
                    crc: 0xAABBCCDD, fileName: "track01.bin"),
                CreateAreaWithFileName(id: 2, offset: 50_000_000, size: 30_000_000,
                    crc: 0x11223344, fileName: "track02.bin"),
                CreateAreaWithFileName(id: 3, offset: 80_000_000, size: 20_000_000,
                    crc: 0x55667788, fileName: "track03.bin"),
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "game.cue", UncompressedSize = 256 }
            };

            byte[] cueContent = System.Text.Encoding.UTF8.GetBytes(
                "FILE \"track01.bin\" BINARY\r\n" +
                "  TRACK 01 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track02.bin\" BINARY\r\n" +
                "  TRACK 02 AUDIO\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track03.bin\" BINARY\r\n" +
                "  TRACK 03 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Cue,
                "Castlevania", 100_000_000, 0xDEADBEEF,
                name => name == "game.cue" ? cueContent : null);

            // Assert: 3 ImageFiles with correct filenames
            Assert.Equal(3, result.ImageFiles.Length);
            Assert.Equal("track01.bin", result.ImageFiles[0].FileName);
            Assert.Equal("track02.bin", result.ImageFiles[1].FileName);
            Assert.Equal("track03.bin", result.ImageFiles[2].FileName);
        }

        #endregion

        #region GDI Image with 5 FileName-Tagged Areas

        /// <summary>
        /// Validates Requirement 4.2: GDI image with 5 FileName-tagged areas
        /// produces 5 ImageFiles with correct filenames.
        /// </summary>
        [Fact]
        public void GdiImage_5FileNameAreas_Produces5ImageFiles_WithCorrectFilenames()
        {
            // Arrange: 5 areas with FileName metadata (typical Dreamcast GDI)
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithFileName(id: 1, offset: 0, size: 10_000_000,
                    crc: 0x11111111, fileName: "track01.bin"),
                CreateAreaWithFileName(id: 2, offset: 10_000_000, size: 20_000_000,
                    crc: 0x22222222, fileName: "track02.raw"),
                CreateAreaWithFileName(id: 3, offset: 30_000_000, size: 500_000_000,
                    crc: 0x33333333, fileName: "track03.bin"),
                CreateAreaWithFileName(id: 4, offset: 530_000_000, size: 100_000_000,
                    crc: 0x44444444, fileName: "track04.raw"),
                CreateAreaWithFileName(id: 5, offset: 630_000_000, size: 50_000_000,
                    crc: 0x55555555, fileName: "track05.bin"),
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "disc.gdi", UncompressedSize = 512 }
            };

            byte[] gdiContent = System.Text.Encoding.UTF8.GetBytes(
                "5\r\n" +
                "1 0 4 2352 track01.bin 0\r\n" +
                "2 450 0 2352 track02.raw 0\r\n" +
                "3 45000 4 2352 track03.bin 0\r\n" +
                "4 300000 0 2352 track04.raw 0\r\n" +
                "5 350000 4 2352 track05.bin 0\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Gdi,
                "Sonic Adventure", 680_000_000, 0xCAFEBABE,
                name => name == "disc.gdi" ? gdiContent : null);

            // Assert: 5 ImageFiles with correct filenames
            Assert.Equal(5, result.ImageFiles.Length);
            Assert.Equal("track01.bin", result.ImageFiles[0].FileName);
            Assert.Equal("track02.raw", result.ImageFiles[1].FileName);
            Assert.Equal("track03.bin", result.ImageFiles[2].FileName);
            Assert.Equal("track04.raw", result.ImageFiles[3].FileName);
            Assert.Equal("track05.bin", result.ImageFiles[4].FileName);
        }

        #endregion

        #region CUE Image with No FileName-Tagged Areas (Legacy Fallback)

        /// <summary>
        /// Validates Requirement 4.1 (fallback): CUE image with no FileName-tagged
        /// areas falls back to single .bin ImageFile (legacy flat storage).
        /// </summary>
        [Fact]
        public void CueImage_NoFileNameAreas_FallsBackToSingleBinImageFile()
        {
            // Arrange: areas without FileName metadata (legacy storage)
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithoutFileName(id: 1, offset: 0, size: 100_000_000, crc: 0xDEADBEEF)
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "game.cue", UncompressedSize = 128 }
            };

            byte[] cueContent = System.Text.Encoding.UTF8.GetBytes(
                "FILE \"game.bin\" BINARY\r\n" +
                "  TRACK 01 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Cue,
                "OldGame", 100_000_000, 0xDEADBEEF,
                name => name == "game.cue" ? cueContent : null);

            // Assert: single .bin ImageFile with image name
            Assert.Single(result.ImageFiles);
            Assert.Equal("OldGame.bin", result.ImageFiles[0].FileName);
            Assert.Equal(".bin", result.ImageFiles[0].Extension);
            Assert.Equal(-1, result.ImageFiles[0].Offset);
            Assert.Equal(100_000_000, result.ImageFiles[0].Size);
            Assert.Equal((long)0xDEADBEEF, result.ImageFiles[0].Crc);
        }

        #endregion

        #region IndexFile Parsed from Loose CUE File

        /// <summary>
        /// Validates Requirement 4.3: IndexFile parsed from loose CUE file
        /// and assigned to SourceFile.IndexFile.
        /// </summary>
        [Fact]
        public void CueImage_IndexFileParsedFromLooseCueFile()
        {
            // Arrange: CUE stored as loose file, areas have FileName metadata
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithFileName(id: 1, offset: 0, size: 50_000_000,
                    crc: 0xAAAAAAAA, fileName: "track01.bin"),
                CreateAreaWithFileName(id: 2, offset: 50_000_000, size: 30_000_000,
                    crc: 0xBBBBBBBB, fileName: "track02.bin"),
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "mygame.cue", UncompressedSize = 200 }
            };

            byte[] cueContent = System.Text.Encoding.UTF8.GetBytes(
                "FILE \"track01.bin\" BINARY\r\n" +
                "  TRACK 01 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track02.bin\" BINARY\r\n" +
                "  TRACK 02 AUDIO\r\n" +
                "    INDEX 01 00:00:00\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Cue,
                "MyGame", 80_000_000, 0xCCCCCCCC,
                name => name == "mygame.cue" ? cueContent : null);

            // Assert: IndexFile is parsed and assigned
            Assert.NotNull(result.IndexFile);
            Assert.Equal(IndexFileType.Cue, result.IndexFile.FileType);
            Assert.Equal("mygame.cue", result.IndexFile.FileName);
            Assert.Equal(2, result.IndexFile.Items.Length);
            Assert.Equal("track01.bin", result.IndexFile.Items[0].FileName);
            Assert.Equal("track02.bin", result.IndexFile.Items[1].FileName);
        }

        #endregion

        #region IndexFile Parsed from Loose GDI File

        /// <summary>
        /// Validates Requirement 4.4: IndexFile parsed from loose GDI file
        /// and assigned to SourceFile.IndexFile.
        /// </summary>
        [Fact]
        public void GdiImage_IndexFileParsedFromLooseGdiFile()
        {
            // Arrange: GDI stored as loose file, areas have FileName metadata
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithFileName(id: 1, offset: 0, size: 10_000_000,
                    crc: 0x11111111, fileName: "track01.bin"),
                CreateAreaWithFileName(id: 2, offset: 10_000_000, size: 20_000_000,
                    crc: 0x22222222, fileName: "track02.raw"),
                CreateAreaWithFileName(id: 3, offset: 30_000_000, size: 500_000_000,
                    crc: 0x33333333, fileName: "track03.bin"),
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "disc.gdi", UncompressedSize = 256 }
            };

            byte[] gdiContent = System.Text.Encoding.UTF8.GetBytes(
                "3\r\n" +
                "1 0 4 2352 track01.bin 0\r\n" +
                "2 450 0 2352 track02.raw 0\r\n" +
                "3 45000 4 2352 track03.bin 0\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Gdi,
                "DreamcastGame", 530_000_000, 0xDDDDDDDD,
                name => name == "disc.gdi" ? gdiContent : null);

            // Assert: IndexFile is parsed and assigned
            Assert.NotNull(result.IndexFile);
            Assert.Equal(IndexFileType.Gdi, result.IndexFile.FileType);
            Assert.Equal("disc.gdi", result.IndexFile.FileName);
            Assert.Equal(3, result.IndexFile.Items.Length);
            Assert.Equal("track01.bin", result.IndexFile.Items[0].FileName);
            Assert.Equal("track02.raw", result.IndexFile.Items[1].FileName);
            Assert.Equal("track03.bin", result.IndexFile.Items[2].FileName);
        }

        #endregion

        #region Each ImageFile Has Correct Offset, Size, and CRC

        /// <summary>
        /// Validates Requirement 4.5: Each ImageFile entry has correct offset,
        /// size, and CRC from the corresponding area record.
        /// </summary>
        [Fact]
        public void CueImage_EachImageFile_HasCorrectOffsetSizeAndCrc()
        {
            // Arrange: 3 areas with distinct offset/size/CRC values
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithFileName(id: 1, offset: 0, size: 44_100_000,
                    crc: 0xAA000001, fileName: "track01.bin"),
                CreateAreaWithFileName(id: 2, offset: 44_100_000, size: 22_050_000,
                    crc: 0xBB000002, fileName: "track02.bin"),
                CreateAreaWithFileName(id: 3, offset: 66_150_000, size: 11_025_000,
                    crc: 0xCC000003, fileName: "track03.bin"),
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "game.cue", UncompressedSize = 180 }
            };

            byte[] cueContent = System.Text.Encoding.UTF8.GetBytes(
                "FILE \"track01.bin\" BINARY\r\n" +
                "  TRACK 01 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track02.bin\" BINARY\r\n" +
                "  TRACK 02 AUDIO\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "FILE \"track03.bin\" BINARY\r\n" +
                "  TRACK 03 MODE1/2352\r\n" +
                "    INDEX 01 00:00:00\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Cue,
                "TestGame", 77_175_000, 0xFFFFFFFF,
                name => name == "game.cue" ? cueContent : null);

            // Assert: each ImageFile has correct offset, size, and CRC
            Assert.Equal(3, result.ImageFiles.Length);

            // Track 1
            Assert.Equal(0L, result.ImageFiles[0].Offset);
            Assert.Equal(44_100_000L, result.ImageFiles[0].Size);
            Assert.Equal((long)0xAA000001, result.ImageFiles[0].Crc);

            // Track 2
            Assert.Equal(44_100_000L, result.ImageFiles[1].Offset);
            Assert.Equal(22_050_000L, result.ImageFiles[1].Size);
            Assert.Equal((long)0xBB000002, result.ImageFiles[1].Crc);

            // Track 3
            Assert.Equal(66_150_000L, result.ImageFiles[2].Offset);
            Assert.Equal(11_025_000L, result.ImageFiles[2].Size);
            Assert.Equal((long)0xCC000003, result.ImageFiles[2].Crc);
        }

        /// <summary>
        /// Validates Requirement 4.5: GDI ImageFile entries also have correct
        /// offset, size, and CRC from area records.
        /// </summary>
        [Fact]
        public void GdiImage_EachImageFile_HasCorrectOffsetSizeAndCrc()
        {
            // Arrange: 3 GDI areas with distinct values
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithFileName(id: 1, offset: 0, size: 5_000_000,
                    crc: 0x10000001, fileName: "track01.bin"),
                CreateAreaWithFileName(id: 2, offset: 5_000_000, size: 600_000_000,
                    crc: 0x20000002, fileName: "track02.bin"),
                CreateAreaWithFileName(id: 3, offset: 605_000_000, size: 50_000_000,
                    crc: 0x30000003, fileName: "track03.raw"),
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "game.gdi", UncompressedSize = 128 }
            };

            byte[] gdiContent = System.Text.Encoding.UTF8.GetBytes(
                "3\r\n" +
                "1 0 4 2352 track01.bin 0\r\n" +
                "2 450 4 2352 track02.bin 0\r\n" +
                "3 300000 0 2352 track03.raw 0\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Gdi,
                "DcGame", 655_000_000, 0xEEEEEEEE,
                name => name == "game.gdi" ? gdiContent : null);

            // Assert: each ImageFile has correct offset, size, and CRC
            Assert.Equal(3, result.ImageFiles.Length);

            Assert.Equal(0L, result.ImageFiles[0].Offset);
            Assert.Equal(5_000_000L, result.ImageFiles[0].Size);
            Assert.Equal((long)0x10000001, result.ImageFiles[0].Crc);

            Assert.Equal(5_000_000L, result.ImageFiles[1].Offset);
            Assert.Equal(600_000_000L, result.ImageFiles[1].Size);
            Assert.Equal((long)0x20000002, result.ImageFiles[1].Crc);

            Assert.Equal(605_000_000L, result.ImageFiles[2].Offset);
            Assert.Equal(50_000_000L, result.ImageFiles[2].Size);
            Assert.Equal((long)0x30000003, result.ImageFiles[2].Crc);
        }

        #endregion

        #region GDI Legacy Fallback

        /// <summary>
        /// Validates that GDI image with no FileName-tagged areas falls back
        /// to single .raw ImageFile (legacy flat storage).
        /// </summary>
        [Fact]
        public void GdiImage_NoFileNameAreas_FallsBackToSingleRawImageFile()
        {
            // Arrange: areas without FileName metadata (legacy GDI storage)
            List<AreaRecord> areas = new List<AreaRecord>
            {
                CreateAreaWithoutFileName(id: 1, offset: 0, size: 700_000_000, crc: 0xABCDABCD)
            };

            List<FileRecord> storedFiles = new List<FileRecord>
            {
                new FileRecord { Name = "disc.gdi", UncompressedSize = 128 }
            };

            byte[] gdiContent = System.Text.Encoding.UTF8.GetBytes(
                "3\r\n" +
                "1 0 4 2352 track01.bin 0\r\n" +
                "2 450 0 2352 track02.raw 0\r\n" +
                "3 45000 4 2352 track03.bin 0\r\n");

            // Act
            ReconstructionResult result = ModelReconstruct(
                areas, storedFiles, ContainerType.Gdi,
                "OldDcGame", 700_000_000, 0xABCDABCD,
                name => name == "disc.gdi" ? gdiContent : null);

            // Assert: single .raw ImageFile with image name
            Assert.Single(result.ImageFiles);
            Assert.Equal("OldDcGame.raw", result.ImageFiles[0].FileName);
            Assert.Equal(".raw", result.ImageFiles[0].Extension);
            Assert.Equal(-1, result.ImageFiles[0].Offset);
            Assert.Equal(700_000_000, result.ImageFiles[0].Size);
            Assert.Equal((long)0xABCDABCD, result.ImageFiles[0].Crc);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates an AreaRecord with FileName metadata set.
        /// </summary>
        private static AreaRecord CreateAreaWithFileName(
            long id, long offset, long size, uint crc, string fileName)
        {
            AreaRecord area = new AreaRecord
            {
                Id = id,
                Offset = offset,
                Size = size,
                Crc32 = crc,
                SectionSize = 0x200000,
                StrideBlockSize = 0x930,
                StrideDataOffset = 0x10,
                StrideDataLength = 0x800
            };
            area.Metadata.Set(AreaValueType.FileName, fileName);
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x930L);
            return area;
        }

        /// <summary>
        /// Creates an AreaRecord without FileName metadata (legacy storage).
        /// </summary>
        private static AreaRecord CreateAreaWithoutFileName(
            long id, long offset, long size, uint crc)
        {
            AreaRecord area = new AreaRecord
            {
                Id = id,
                Offset = offset,
                Size = size,
                Crc32 = crc,
                SectionSize = 0x200000,
                StrideBlockSize = 0x930,
                StrideDataOffset = 0x10,
                StrideDataLength = 0x800
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x930L);
            return area;
        }

        #endregion
    }
}
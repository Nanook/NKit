using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;


namespace NKit.Tests.Settings.SourceFiles
{
    /// <summary>
    /// Unit tests for SourceFileSystem folder entry enumeration.
    /// When the scanner enters a CUE/GDI folder in the DataStore, it should enumerate
    /// track files from FileName-tagged areas and loose files (index/auxiliary) as folder contents.
    ///
    /// Feature: cue-gdi-folder-storage
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    /// </summary>
    [Trait("Area", "Settings")]
    [Trait("Group", "SourceFiles")]
    public class SourceFileSystemFolderEnumerationTests : IDisposable
    {
        private readonly string _tempDir;

        public SourceFileSystemFolderEnumerationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_FolderEnum_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ─── CUE folder enumeration tests ───────────────────────────────────────

        [Fact]
        public void CueFolder_EnumeratesTrackFilesFromFileNameTaggedAreas()
        {
            // Arrange: create a DataStore with a CUE image that has FileName-tagged areas
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                using (IImageWriter writer = ds.AddImage(setName, "Crash Bandicoot (USA)", "PS1", ImageFormat.Cue))
                {
                    // Create areas with FileName metadata (simulating track files)
                    AreaMetadata meta1 = new AreaMetadata();
                    meta1[AreaValueType.FileName] = "track01.bin";
                    writer.CreateArea(0, 300000, 0x11111111, 0, 0x10000, meta1);

                    AreaMetadata meta2 = new AreaMetadata();
                    meta2[AreaValueType.FileName] = "track02.bin";
                    writer.CreateArea(300000, 150000, 0x22222222, 0, 0x10000, meta2);

                    AreaMetadata meta3 = new AreaMetadata();
                    meta3[AreaValueType.FileName] = "track03.bin";
                    writer.CreateArea(450000, 100000, 0x33333333, 0, 0x10000, meta3);

                    // Store a loose CUE index file
                    writer.WriteFile("Crash Bandicoot (USA).cue", Encoding.UTF8.GetBytes("FILE \"track01.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n"));

                    writer.FinalizeImage(550000, 0xAABBCCDD, 0);
                }
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: should contain the folder entry plus track files and the index file
            // The folder entry itself: "Crash Bandicoot (USA)"
            // Track files: "Crash Bandicoot (USA)/track01.bin", "Crash Bandicoot (USA)/track02.bin", "Crash Bandicoot (USA)/track03.bin"
            // Index file: "Crash Bandicoot (USA)/Crash Bandicoot (USA).cue"
            List<FileItem> trackFiles = files.Where(f => f.PathFileName.Contains("/track")).ToList();
            Assert.Equal(3, trackFiles.Count);
            Assert.Contains(files, f => f.PathFileName == "Crash Bandicoot (USA)/track01.bin");
            Assert.Contains(files, f => f.PathFileName == "Crash Bandicoot (USA)/track02.bin");
            Assert.Contains(files, f => f.PathFileName == "Crash Bandicoot (USA)/track03.bin");
        }

        [Fact]
        public void GdiFolder_EnumeratesTrackFilesFromFileNameTaggedAreas()
        {
            // Arrange: create a DataStore with a GDI image that has FileName-tagged areas
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                using (IImageWriter writer = ds.AddImage(setName, "Sonic Adventure (USA)", "Dreamcast", ImageFormat.Gdi))
                {
                    // Create areas with FileName metadata (simulating track files)
                    AreaMetadata meta1 = new AreaMetadata();
                    meta1[AreaValueType.FileName] = "track01.bin";
                    writer.CreateArea(0, 500000, 0x11111111, 0, 0x10000, meta1);

                    AreaMetadata meta2 = new AreaMetadata();
                    meta2[AreaValueType.FileName] = "track02.raw";
                    writer.CreateArea(500000, 200000, 0x22222222, 0, 0x10000, meta2);

                    AreaMetadata meta3 = new AreaMetadata();
                    meta3[AreaValueType.FileName] = "track03.bin";
                    writer.CreateArea(700000, 800000, 0x33333333, 0, 0x10000, meta3);

                    AreaMetadata meta4 = new AreaMetadata();
                    meta4[AreaValueType.FileName] = "track04.raw";
                    writer.CreateArea(1500000, 100000, 0x44444444, 0, 0x10000, meta4);

                    AreaMetadata meta5 = new AreaMetadata();
                    meta5[AreaValueType.FileName] = "track05.bin";
                    writer.CreateArea(1600000, 300000, 0x55555555, 0, 0x10000, meta5);

                    // Store a loose GDI index file
                    writer.WriteFile("Sonic Adventure (USA).gdi", Encoding.UTF8.GetBytes("5\n1 0 4 2352 track01.bin 0\n2 500 0 2352 track02.raw 0\n3 700 4 2352 track03.bin 0\n4 1500 0 2352 track04.raw 0\n5 1600 4 2352 track05.bin 0\n"));

                    writer.FinalizeImage(1900000, 0xDDEEFF00, 0);
                }
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: should contain track files from FileName-tagged areas
            List<FileItem> trackFiles = files.Where(f => f.PathFileName.Contains("/track")).ToList();
            Assert.Equal(5, trackFiles.Count);
            Assert.Contains(files, f => f.PathFileName == "Sonic Adventure (USA)/track01.bin");
            Assert.Contains(files, f => f.PathFileName == "Sonic Adventure (USA)/track02.raw");
            Assert.Contains(files, f => f.PathFileName == "Sonic Adventure (USA)/track03.bin");
            Assert.Contains(files, f => f.PathFileName == "Sonic Adventure (USA)/track04.raw");
            Assert.Contains(files, f => f.PathFileName == "Sonic Adventure (USA)/track05.bin");
        }

        // ─── Loose index file tests ─────────────────────────────────────────────

        [Fact]
        public void CueFolder_LooseIndexFileListedAsFolderContent()
        {
            // Arrange: create a DataStore with a CUE image that has a loose CUE index file
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                using (IImageWriter writer = ds.AddImage(setName, "Final Fantasy VII (USA)", "PS1", ImageFormat.Cue))
                {
                    // Create one area with FileName metadata
                    AreaMetadata meta1 = new AreaMetadata();
                    meta1[AreaValueType.FileName] = "track01.bin";
                    writer.CreateArea(0, 500000, 0x11111111, 0, 0x10000, meta1);

                    // Store the loose CUE index file
                    string cueContent = "FILE \"track01.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n";
                    writer.WriteFile("Final Fantasy VII (USA).cue", Encoding.UTF8.GetBytes(cueContent));

                    writer.FinalizeImage(500000, 0xAABBCCDD, 0);
                }
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: the loose CUE index file should be listed as folder content
            Assert.Contains(files, f => f.PathFileName == "Final Fantasy VII (USA)/Final Fantasy VII (USA).cue");
        }

        [Fact]
        public void GdiFolder_LooseIndexFileListedAsFolderContent()
        {
            // Arrange: create a DataStore with a GDI image that has a loose GDI index file
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                using (IImageWriter writer = ds.AddImage(setName, "Jet Set Radio (USA)", "Dreamcast", ImageFormat.Gdi))
                {
                    // Create one area with FileName metadata
                    AreaMetadata meta1 = new AreaMetadata();
                    meta1[AreaValueType.FileName] = "track01.bin";
                    writer.CreateArea(0, 400000, 0x11111111, 0, 0x10000, meta1);

                    // Store the loose GDI index file
                    string gdiContent = "1\n1 0 4 2352 track01.bin 0\n";
                    writer.WriteFile("Jet Set Radio (USA).gdi", Encoding.UTF8.GetBytes(gdiContent));

                    writer.FinalizeImage(400000, 0xBBCCDDEE, 0);
                }
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: the loose GDI index file should be listed as folder content
            Assert.Contains(files, f => f.PathFileName == "Jet Set Radio (USA)/Jet Set Radio (USA).gdi");
        }

        // ─── Track file name from FileName metadata ─────────────────────────────

        [Fact]
        public void TrackFileEntry_UsesStoredFileNameMetadataAsName()
        {
            // Arrange: create a DataStore with a CUE image where areas have specific FileName metadata
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                using (IImageWriter writer = ds.AddImage(setName, "Ridge Racer (Japan)", "PS1", ImageFormat.Cue))
                {
                    // Create areas with custom FileName metadata (not just "trackNN.bin")
                    AreaMetadata meta1 = new AreaMetadata();
                    meta1[AreaValueType.FileName] = "Ridge Racer (Japan) (Track 01).bin";
                    writer.CreateArea(0, 200000, 0x11111111, 0, 0x10000, meta1);

                    AreaMetadata meta2 = new AreaMetadata();
                    meta2[AreaValueType.FileName] = "Ridge Racer (Japan) (Track 02).bin";
                    writer.CreateArea(200000, 150000, 0x22222222, 0, 0x10000, meta2);

                    AreaMetadata meta3 = new AreaMetadata();
                    meta3[AreaValueType.FileName] = "Ridge Racer (Japan) (Track 03).bin";
                    writer.CreateArea(350000, 100000, 0x33333333, 0, 0x10000, meta3);

                    // Store a loose CUE index file
                    writer.WriteFile("Ridge Racer (Japan).cue", Encoding.UTF8.GetBytes("FILE \"Ridge Racer (Japan) (Track 01).bin\" BINARY\n"));

                    writer.FinalizeImage(450000, 0xCCDDEEFF, 0);
                }
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: each track file entry should use the stored FileName metadata as its name
            Assert.Contains(files, f => f.PathFileName == "Ridge Racer (Japan)/Ridge Racer (Japan) (Track 01).bin");
            Assert.Contains(files, f => f.PathFileName == "Ridge Racer (Japan)/Ridge Racer (Japan) (Track 02).bin");
            Assert.Contains(files, f => f.PathFileName == "Ridge Racer (Japan)/Ridge Racer (Japan) (Track 03).bin");

            // Verify the track file entries have the correct sizes from the area records
            FileItem track1 = files.First(f => f.PathFileName == "Ridge Racer (Japan)/Ridge Racer (Japan) (Track 01).bin");
            FileItem track2 = files.First(f => f.PathFileName == "Ridge Racer (Japan)/Ridge Racer (Japan) (Track 02).bin");
            FileItem track3 = files.First(f => f.PathFileName == "Ridge Racer (Japan)/Ridge Racer (Japan) (Track 03).bin");
            Assert.Equal(200000, track1.Size);
            Assert.Equal(150000, track2.Size);
            Assert.Equal(100000, track3.Size);
        }

        // ─── Helper methods ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a SourceFileSystem pointing at the DataStore set file and lists all files
        /// (including folder contents).
        /// </summary>
        private List<FileItem> ListDataStoreFiles(string setName, string arcMask)
        {
            string nkdsPath = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            FileItem archive = new FileItem(nkdsPath) { Size = new FileInfo(nkdsPath).Length };
            archive.Populate();

            SourceFileSystem sfs = new SourceFileSystem(archive, null);
            FileMask mask = FileMask.CreateLocalMask($"{nkdsPath}//{arcMask}", false);
            return sfs.GetFiles(mask, null);
        }
    }
}
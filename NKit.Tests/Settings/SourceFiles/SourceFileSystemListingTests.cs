using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;


namespace NKit.Tests.Settings.SourceFiles
{
    /// <summary>
    /// Unit tests for SourceFileSystem listing changes: getDataStoreEntryExtension
    /// and addDataStoreFiles behavior for CUE/GDI folder presentation.
    ///
    /// Feature: cue-gdi-folder-storage
    /// **Validates: Requirements 2.1, 2.2, 2.3, 3.1, 3.2, 10.4**
    /// </summary>
    [Trait("Area", "Settings")]
    [Trait("Group", "SourceFiles")]
    public class SourceFileSystemListingTests : IDisposable
    {
        private readonly string _tempDir;
        private static readonly MethodInfo _GetDataStoreEntryExtensionMethod;

        static SourceFileSystemListingTests()
        {
            // Resolve the private static method via reflection
            _GetDataStoreEntryExtensionMethod = typeof(SourceFileSystem).GetMethod(
                "getDataStoreEntryExtension",
                BindingFlags.NonPublic | BindingFlags.Static);
        }

        public SourceFileSystemListingTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_Listing_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Invokes the private static getDataStoreEntryExtension method via reflection.
        /// </summary>
        private static string invokeGetDataStoreEntryExtension(ImageFormat format)
        {
            Assert.NotNull(_GetDataStoreEntryExtensionMethod);
            return (string)_GetDataStoreEntryExtensionMethod.Invoke(null, new object[] { format })!;
        }

        // ─── getDataStoreEntryExtension tests ───────────────────────────────────

        [Fact]
        public void GetDataStoreEntryExtension_Cue_ReturnsEmptyString()
        {
            string result = invokeGetDataStoreEntryExtension(ImageFormat.Cue);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void GetDataStoreEntryExtension_Gdi_ReturnsEmptyString()
        {
            string result = invokeGetDataStoreEntryExtension(ImageFormat.Gdi);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void GetDataStoreEntryExtension_Iso_ReturnsDotIso()
        {
            string result = invokeGetDataStoreEntryExtension(ImageFormat.Iso);
            Assert.Equal(".iso", result);
        }

        [Fact]
        public void GetDataStoreEntryExtension_CueFolder_ReturnsEmptyString()
        {
            string result = invokeGetDataStoreEntryExtension(ImageFormat.CueFolder);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void GetDataStoreEntryExtension_App_ReturnsEmptyString()
        {
            string result = invokeGetDataStoreEntryExtension(ImageFormat.App);
            Assert.Equal(string.Empty, result);
        }

        // ─── addDataStoreFiles integration tests ────────────────────────────────

        [Fact]
        public void AddDataStoreFiles_SkipsCueFolderImages()
        {
            // Arrange: create a DataStore with a CueFolder image and a normal Cue image
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                IImageWriter cueFolderWriter = ds.AddImage(setName, "Multi-Disc Game", "PS1", ImageFormat.CueFolder);
                cueFolderWriter.FinalizeImage(100000, 0x11111111, 0);
                cueFolderWriter.Dispose();

                IImageWriter cueWriter = ds.AddImage(setName, "Single Disc Game", "PS1", ImageFormat.Cue);
                cueWriter.FinalizeImage(200000, 0x22222222, 0);
                cueWriter.Dispose();
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = listDataStoreFiles(setName, "*");

            // Assert: CueFolder image should be skipped entirely.
            // The Cue image's folder entry is non-matching (it's a container, not a processable image).
            // Only the contents of the Cue folder would be matching, but since no content was written,
            // there are no matching items.
            Assert.Empty(files);
        }

        [Fact]
        public void AddDataStoreFiles_CueImageEntryName_FolderEntryIsNonMatching()
        {
            // Arrange: create a DataStore with a CUE image (no content written)
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                IImageWriter writer = ds.AddImage(setName, "Chessmaster, The (Japan)", "PS1", ImageFormat.Cue);
                writer.FinalizeImage(100000, 0x12345678, 0);
                writer.Dispose();
            }

            // Act: list ALL files (including non-matching) through SourceFileSystem
            string nkdsPath = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            FileItem archive = new FileItem(nkdsPath) { Size = new FileInfo(nkdsPath).Length };
            archive.Populate();
            SourceFileSystem sfs = new SourceFileSystem(archive, null);
            FileMask mask = FileMask.CreateLocalMask($"{nkdsPath}//*", false);
            List<FileItem> allFiles = sfs.GetFiles(mask, null);

            // Assert: The folder entry exists but is non-matching (it's a container, not processable)
            FileItem folderEntry = allFiles.FirstOrDefault(f => f.Name == "Chessmaster, The (Japan)");
            Assert.NotNull(folderEntry);
            Assert.False(folderEntry.IsMatch, "CUE folder entry should be non-matching");

            // No matching files since no content was written
            Assert.DoesNotContain(allFiles, f => f.IsMatch);
        }

        [Fact]
        public void AddDataStoreFiles_GdiImageEntryName_FolderEntryIsNonMatching()
        {
            // Arrange: create a DataStore with a GDI image (no content written)
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                IImageWriter writer = ds.AddImage(setName, "Sonic Adventure (USA)", "Dreamcast", ImageFormat.Gdi);
                writer.FinalizeImage(300000, 0xAABBCCDD, 0);
                writer.Dispose();
            }

            // Act: list ALL files (including non-matching) through SourceFileSystem
            string nkdsPath = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            FileItem archive = new FileItem(nkdsPath) { Size = new FileInfo(nkdsPath).Length };
            archive.Populate();
            SourceFileSystem sfs = new SourceFileSystem(archive, null);
            FileMask mask = FileMask.CreateLocalMask($"{nkdsPath}//*", false);
            List<FileItem> allFiles = sfs.GetFiles(mask, null);

            // Assert: The folder entry exists but is non-matching (it's a container, not processable)
            FileItem folderEntry = allFiles.FirstOrDefault(f => f.Name == "Sonic Adventure (USA)");
            Assert.NotNull(folderEntry);
            Assert.False(folderEntry.IsMatch, "GDI folder entry should be non-matching");

            // No matching files since no content was written
            Assert.DoesNotContain(allFiles, f => f.IsMatch);
        }

        [Fact]
        public void AddDataStoreFiles_DuplicateCueNames_FolderEntriesAreNonMatching()
        {
            // Arrange: create a DataStore with two CUE images that have the same name
            string setName = "testset";
            long imageId1, imageId2;
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                IImageWriter writer1 = ds.AddImage(setName, "Game Title", "PS1", ImageFormat.Cue);
                writer1.FinalizeImage(100000, 0x11111111, 0);
                writer1.Dispose();

                IImageWriter writer2 = ds.AddImage(setName, "Game Title", "PS1", ImageFormat.Cue);
                writer2.FinalizeImage(200000, 0x22222222, 0);
                writer2.Dispose();

                // Get the image IDs
                List<ImageRecord> images = ds.ListImagesInSet(setName);
                imageId1 = images[0].Id;
                imageId2 = images[1].Id;
            }

            // Act: list ALL files (including non-matching) through SourceFileSystem
            string nkdsPath = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            FileItem archive = new FileItem(nkdsPath) { Size = new FileInfo(nkdsPath).Length };
            archive.Populate();
            SourceFileSystem sfs = new SourceFileSystem(archive, null);
            FileMask mask = FileMask.CreateLocalMask($"{nkdsPath}//*", false);
            List<FileItem> allFiles = sfs.GetFiles(mask, null);

            // Assert: Both folder entries exist but are non-matching
            List<FileItem> folderEntries = allFiles.Where(f => f.Name != null && f.Name.StartsWith("Game Title")).ToList();
            Assert.Equal(2, folderEntries.Count);
            Assert.All(folderEntries, f => Assert.False(f.IsMatch, "CUE folder entries should be non-matching"));

            // Duplicate names get the unambiguous {imageId} marker appended on the folder entries
            // (via DataStoreAsIso.FormatDuplicateName — routed through the same helper the listing
            // uses so the expected format can't drift from the production format).
            List<string> names = folderEntries.Select(f => f.PathFileName).OrderBy(n => n).ToList();
            Assert.Contains(Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName("Game Title", imageId1), names);
            Assert.Contains(Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName("Game Title", imageId2), names);

            // No matching files since no content was written
            Assert.DoesNotContain(allFiles, f => f.IsMatch);
        }

        // ─── Helper methods ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a SourceFileSystem pointing at the DataStore set file and lists files.
        /// </summary>
        private List<FileItem> listDataStoreFiles(string setName, string arcMask)
        {
            string nkdsPath = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            FileItem archive = new FileItem(nkdsPath) { Size = new FileInfo(nkdsPath).Length };
            archive.Populate();

            SourceFileSystem sfs = new SourceFileSystem(archive, null);
            FileMask mask = FileMask.CreateLocalMask($"{nkdsPath}//{arcMask}", false);
            return sfs.GetFiles(mask, null).Where(f => f.IsMatch).ToList();
        }
    }
}
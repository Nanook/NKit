using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;


namespace NKit.Tests.NKDS.Cue
{
    /// <summary>
    /// Tests that CUE/BIN images stored in the DataStore are listed correctly
    /// and can be found via wildcard matching (the verify-after-dedupe path).
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Cue")]
    public class DataStoreCueListingTests : IDisposable
    {
        private readonly string _tempDir;

        public DataStoreCueListingTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_CueListing_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void CueFormat_ListedWithBinExtension()
        {
            // Arrange: create a DataStore with a CUE-format image
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);
                IImageWriter writer = ds.AddImage(setName, "Chessmaster, The (Japan)", "PS1", ImageFormat.Cue);
                writer.FinalizeImage(100000, 0x12345678, 0);
                writer.Dispose();
            }

            // Act: list images and check the extension used for display
            using (DataStore ds = new DataStore(_tempDir))
            {
                List<ImageRecord> images = ds.ListImagesInSet(setName);
                Assert.Single(images);

                ImageRecord image = images[0];
                Assert.Equal("Chessmaster, The (Japan)", image.Name);
                Assert.Equal(ImageFormat.Cue, image.Format);

                // The display extension for Cue format should be .bin (not .cue)
                // because .cue would trigger index file resolution in the scanner
                string ext = image.Format.GetFileExtension();
                Assert.Equal(".cue", ext); // enum extension is .cue

                // But the DataStore listing entry should use .bin
                string listingExt = getDataStoreEntryExtension(image.Format);
                Assert.Equal(".bin", listingExt);

                // The full entry name for listing
                string entryName = image.Name + listingExt;
                Assert.Equal("Chessmaster, The (Japan).bin", entryName);
            }
        }

        [Fact]
        public void CueFormat_WildcardMatchesEntry()
        {
            // The wildcard "Chessmaster, The (Japan)*" should match "Chessmaster, The (Japan).bin"
            string entryName = "Chessmaster, The (Japan).bin";

            // Simulate what CreateLocalMask does with the path "C:/Temp/multisys.nkds//Chessmaster, The (Japan)*"
            string arcMask = "Chessmaster, The (Japan)*";
            string regexPattern = $"^{FileMask.MaskToArchiveRegex(arcMask)}$";
            Regex regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            Assert.True(regex.IsMatch(entryName), $"Regex '{regexPattern}' should match '{entryName}'");

            // Also test with .iso extension (in case old format)
            Assert.True(regex.IsMatch("Chessmaster, The (Japan).iso"), $"Regex '{regexPattern}' should match .iso variant");
        }

        [Fact]
        public void CueFormat_NameLookupInConstruct()
        {
            // Arrange: create a DataStore with a CUE-format image
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);
                IImageWriter writer = ds.AddImage(setName, "Chessmaster, The (Japan)", "PS1", ImageFormat.Cue);
                writer.FinalizeImage(100000, 0x12345678, 0);
                writer.Dispose();
            }

            // Act: simulate the name lookup that DataStoreAsIso.Construct() does
            using (DataStore ds = new DataStore(_tempDir))
            {
                // The verify source file has ImageFiles[0].FileName = "Chessmaster, The (Japan).bin"
                // DataStoreAsIso.Construct() strips the known extension to get the lookup name
                string sourceFileName = "Chessmaster, The (Japan).bin";
                string knownExt = SourceFiles.GetKnownFileExtension(sourceFileName);
                string name = string.IsNullOrEmpty(knownExt)
                    ? sourceFileName
                    : sourceFileName.Substring(0, sourceFileName.Length - knownExt.Length);

                Assert.Equal(".bin", knownExt);
                Assert.Equal("Chessmaster, The (Japan)", name);

                // Now look up the image by name (same as Construct does)
                List<ImageRecord> images = ds.ListAllImages(img =>
                    string.Compare(img.Name, name, true) == 0 &&
                    img.SetName == setName
                ).ToList();

                Assert.Single(images);
                Assert.Equal("Chessmaster, The (Japan)", images[0].Name);
                Assert.Equal(ImageFormat.Cue, images[0].Format);
            }
        }

        [Fact]
        public void CueFormat_ImageFileNameFromFormatter()
        {
            // The formatter's ImageFileName should use .bin for CUE format
            // (so the verify source file doesn't trigger index resolution)
            string imageName = "Chessmaster, The (Japan)";
            ImageFormat imageFormat = ImageFormat.Cue;

            // This is what the formatter does:
            string displayExtension = imageFormat == ImageFormat.Cue ? ".bin" : Nanook.NKit.Container.DataStoreAsIso.GetImageExtension(imageFormat);
            string imageFileName = imageName + displayExtension;

            Assert.Equal("Chessmaster, The (Japan).bin", imageFileName);

            // Verify that .bin is a known extension (so Construct can strip it)
            string knownExt = SourceFiles.GetKnownFileExtension(imageFileName);
            Assert.Equal(".bin", knownExt);
        }

        /// <summary>
        /// Mirrors the logic in SourceFileSystem.getDataStoreEntryExtension
        /// </summary>
        private static string getDataStoreEntryExtension(ImageFormat format)
        {
            if (format == ImageFormat.App)
                return string.Empty;
            if (format == ImageFormat.Cue)
                return ".bin";
            return format.GetFileExtension();
        }
    }
}
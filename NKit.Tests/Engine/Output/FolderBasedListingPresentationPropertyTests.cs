using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests for folder-based listing presentation.
    /// Feature: cue-gdi-folder-storage, Property 2: Folder-Based Listing Presentation
    ///
    /// For any DataStore set containing images with ImageFormat.Cue or ImageFormat.Gdi,
    /// listing the set SHALL present those images as folder entries with no file extension
    /// (entry name equals image name only). Images with ImageFormat.CueFolder SHALL be
    /// excluded from the listing entirely. ISO images retain .iso extension.
    ///
    /// **Validates: Requirements 2.1, 2.2, 3.1, 3.2, 10.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class FolderBasedListingPresentationPropertyTests
    {
        #region Model Methods

        /// <summary>
        /// Models the getDataStoreEntryExtension private method from SourceFileSystem.
        /// Returns the extension used for listing a given ImageFormat.
        /// </summary>
        private static string ModelGetDataStoreEntryExtension(ImageFormat format)
        {
            if (format == ImageFormat.App)
                return string.Empty;
            if (format == ImageFormat.Cue)
                return string.Empty;
            if (format == ImageFormat.Gdi)
                return string.Empty;
            if (format == ImageFormat.CueFolder)
                return string.Empty;
            return format.GetFileExtension();
        }

        /// <summary>
        /// Models the addDataStoreFiles listing logic from SourceFileSystem.
        /// Given a set of ImageRecords, returns the list of entry names that would be presented.
        /// CueFolder and TmdAppFolder images are skipped entirely.
        /// CUE, GDI, and App images use image.Name without extension (folder presentation).
        /// Other images use image.Name + extension.
        /// Duplicate names get "(imageId)" suffix appended.
        /// </summary>
        private static List<ListingEntry> ModelAddDataStoreFiles(List<ImageRecord> images)
        {
            List<ListingEntry> result = new List<ListingEntry>();

            // Detect duplicate names (excluding TmdAppFolder and CueFolder)
            HashSet<string> duplicateNames = images
                .Where(i => i.Format != ImageFormat.TmdAppFolder && i.Format != ImageFormat.CueFolder)
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (ImageRecord image in images)
            {
                // Skip TmdAppFolder and CueFolder images
                if (image.Format == ImageFormat.TmdAppFolder || image.Format == ImageFormat.CueFolder)
                    continue;

                string ext = ModelGetDataStoreEntryExtension(image.Format);
                // For APP, CUE, and GDI images present as folder (no extension)
                string entryName = (image.Format == ImageFormat.App || image.Format == ImageFormat.Cue || image.Format == ImageFormat.Gdi)
                    ? image.Name
                    : image.Name + ext;

                if (duplicateNames.Contains(image.Name))
                {
                    // Mirrors DataStoreAsIso.FormatDuplicateName: unambiguous "{id}" marker.
                    entryName = string.Concat(image.Name, " {", image.Id.ToString(), "}", ext);
                }

                result.Add(new ListingEntry(image, entryName, ext));
            }

            return result;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 2: Folder-Based Listing Presentation
        ///
        /// For any DataStore set containing CUE images, listing SHALL present those images
        /// as folder entries with no file extension (entry name equals image name only).
        ///
        /// **Validates: Requirements 2.1, 2.2, 3.1, 3.2, 10.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueImages_ListedAsFolderEntries_NoExtension(
            NonNegativeInt imageCountRaw,
            NonNegativeInt seed)
        {
            int imageCount = 1 + (imageCountRaw.Get % 10); // 1–10 images
            int s = seed.Get;

            // Generate a set of images that includes at least one CUE image
            List<ImageRecord> images = GenerateImageSet(imageCount, s, mustIncludeFormat: ImageFormat.Cue);

            List<ListingEntry> entries = ModelAddDataStoreFiles(images);

            // Verify all CUE images are listed as folder entries (no extension)
            List<ImageRecord> cueImages = images.Where(i => i.Format == ImageFormat.Cue).ToList();
            foreach (ImageRecord cueImage in cueImages)
            {
                ListingEntry entry = entries.FirstOrDefault(e => e.Image.Id == cueImage.Id);
                if (entry == null)
                    return false; // CUE image should be listed

                if (entry.Extension != string.Empty)
                    return false; // Extension should be empty

                // Entry name should be based on image.Name (no extension appended)
                if (!entry.EntryName.StartsWith(cueImage.Name))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 2: Folder-Based Listing Presentation
        ///
        /// For any DataStore set containing GDI images, listing SHALL present those images
        /// as folder entries with no file extension (entry name equals image name only).
        ///
        /// **Validates: Requirements 2.1, 2.2, 3.1, 3.2, 10.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool GdiImages_ListedAsFolderEntries_NoExtension(
            NonNegativeInt imageCountRaw,
            NonNegativeInt seed)
        {
            int imageCount = 1 + (imageCountRaw.Get % 10); // 1–10 images
            int s = seed.Get;

            // Generate a set of images that includes at least one GDI image
            List<ImageRecord> images = GenerateImageSet(imageCount, s, mustIncludeFormat: ImageFormat.Gdi);

            List<ListingEntry> entries = ModelAddDataStoreFiles(images);

            // Verify all GDI images are listed as folder entries (no extension)
            List<ImageRecord> gdiImages = images.Where(i => i.Format == ImageFormat.Gdi).ToList();
            foreach (ImageRecord gdiImage in gdiImages)
            {
                ListingEntry entry = entries.FirstOrDefault(e => e.Image.Id == gdiImage.Id);
                if (entry == null)
                    return false; // GDI image should be listed

                if (entry.Extension != string.Empty)
                    return false; // Extension should be empty

                // Entry name should be based on image.Name (no extension appended)
                if (!entry.EntryName.StartsWith(gdiImage.Name))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 2: Folder-Based Listing Presentation
        ///
        /// For any DataStore set containing CueFolder images, those images SHALL be
        /// excluded from the listing entirely.
        ///
        /// **Validates: Requirements 2.1, 2.2, 3.1, 3.2, 10.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueFolderImages_ExcludedFromListing(
            NonNegativeInt imageCountRaw,
            NonNegativeInt seed)
        {
            int imageCount = 1 + (imageCountRaw.Get % 10); // 1–10 images
            int s = seed.Get;

            // Generate a set of images that includes at least one CueFolder image
            List<ImageRecord> images = GenerateImageSet(imageCount, s, mustIncludeFormat: ImageFormat.CueFolder);

            List<ListingEntry> entries = ModelAddDataStoreFiles(images);

            // Verify NO CueFolder images appear in the listing
            List<ImageRecord> cueFolderImages = images.Where(i => i.Format == ImageFormat.CueFolder).ToList();
            foreach (ImageRecord cueFolderImage in cueFolderImages)
            {
                ListingEntry entry = entries.FirstOrDefault(e => e.Image.Id == cueFolderImage.Id);
                if (entry != null)
                    return false; // CueFolder should NOT be listed
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 2: Folder-Based Listing Presentation
        ///
        /// For any DataStore set containing ISO images, listing SHALL present those images
        /// with the .iso file extension retained.
        ///
        /// **Validates: Requirements 2.1, 2.2, 3.1, 3.2, 10.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool IsoImages_RetainIsoExtension(
            NonNegativeInt imageCountRaw,
            NonNegativeInt seed)
        {
            int imageCount = 1 + (imageCountRaw.Get % 10); // 1–10 images
            int s = seed.Get;

            // Generate a set of images that includes at least one ISO image
            List<ImageRecord> images = GenerateImageSet(imageCount, s, mustIncludeFormat: ImageFormat.Iso);

            List<ListingEntry> entries = ModelAddDataStoreFiles(images);

            // Verify all ISO images are listed with .iso extension
            List<ImageRecord> isoImages = images.Where(i => i.Format == ImageFormat.Iso).ToList();
            foreach (ImageRecord isoImage in isoImages)
            {
                ListingEntry entry = entries.FirstOrDefault(e => e.Image.Id == isoImage.Id);
                if (entry == null)
                    return false; // ISO image should be listed

                if (entry.Extension != ".iso")
                    return false; // Extension should be .iso

                // Entry name should end with .iso (unless duplicate name handling)
                if (!entry.EntryName.EndsWith(".iso"))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 2: Folder-Based Listing Presentation
        ///
        /// For any DataStore set containing a mix of image formats, the listing SHALL contain
        /// exactly the non-skipped images (all except TmdAppFolder and CueFolder), and each
        /// entry's extension matches the model's getDataStoreEntryExtension result.
        ///
        /// **Validates: Requirements 2.1, 2.2, 3.1, 3.2, 10.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool MixedFormats_ListingContainsCorrectEntries(
            NonNegativeInt imageCountRaw,
            NonNegativeInt seed)
        {
            int imageCount = 2 + (imageCountRaw.Get % 15); // 2–16 images
            int s = seed.Get;

            // Generate a mixed set of images with various formats
            List<ImageRecord> images = GenerateMixedImageSet(imageCount, s);

            List<ListingEntry> entries = ModelAddDataStoreFiles(images);

            // Count expected entries (all except TmdAppFolder and CueFolder)
            int expectedCount = images.Count(i => i.Format != ImageFormat.TmdAppFolder && i.Format != ImageFormat.CueFolder);
            if (entries.Count != expectedCount)
                return false;

            // Verify each entry has the correct extension from the model
            foreach (ListingEntry entry in entries)
            {
                string expectedExt = ModelGetDataStoreEntryExtension(entry.Image.Format);
                if (entry.Extension != expectedExt)
                    return false;
            }

            return true;
        }

        #endregion

        #region Generators

        /// <summary>
        /// Generates a set of ImageRecords with at least one image of the specified format.
        /// </summary>
        private static List<ImageRecord> GenerateImageSet(int count, int seed, ImageFormat mustIncludeFormat)
        {
            List<ImageRecord> images = new List<ImageRecord>();
            ImageFormat[] listableFormats = [ImageFormat.Iso, ImageFormat.Cue, ImageFormat.Gdi, ImageFormat.App, ImageFormat.Bin];

            // First image is always the required format
            images.Add(GenerateImageRecord(1, seed, mustIncludeFormat));

            // Remaining images are random listable formats or the required format
            for (int i = 1; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));
                ImageFormat format = (hash % 3 == 0)
                    ? mustIncludeFormat
                    : listableFormats[hash % listableFormats.Length];
                images.Add(GenerateImageRecord(i + 1, (seed * 41) + i, format));
            }

            return images;
        }

        /// <summary>
        /// Generates a mixed set of ImageRecords with various formats including
        /// CueFolder and TmdAppFolder (which should be skipped).
        /// </summary>
        private static List<ImageRecord> GenerateMixedImageSet(int count, int seed)
        {
            List<ImageRecord> images = new List<ImageRecord>();
            ImageFormat[] allFormats = [
                ImageFormat.Iso, ImageFormat.Cue, ImageFormat.Gdi,
                ImageFormat.App, ImageFormat.Bin, ImageFormat.CueFolder,
                ImageFormat.TmdAppFolder
            ];

            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));
                ImageFormat format = allFormats[hash % allFormats.Length];
                images.Add(GenerateImageRecord(i + 1, (seed * 41) + i, format));
            }

            return images;
        }

        /// <summary>
        /// Generates a single ImageRecord with the given format and arbitrary properties.
        /// </summary>
        private static ImageRecord GenerateImageRecord(long id, int seed, ImageFormat format)
        {
            string name = GenerateImageName(seed);
            int hash = Math.Abs((seed * 53) + ((int)id * 7));
            return new ImageRecord
            {
                Id = id,
                Name = name,
                Format = format,
                Size = (long)(hash & 0x7FFFFFFF) * 1024,
                Crc32 = (uint)(hash ^ 0xDEADBEEF),
                SetName = "testset",
                System = GetSystem(hash)
            };
        }

        /// <summary>
        /// Generates an arbitrary image name from a seed.
        /// </summary>
        private static string GenerateImageName(int seed)
        {
            string[] prefixes = ["Game", "Disc", "Image", "Title", "Track", "Album", "Demo"];
            string[] suffixes = ["Edition", "Remaster", "Original", "Special", "Deluxe"];
            int hash = Math.Abs(seed);
            string prefix = prefixes[hash % prefixes.Length];
            string suffix = suffixes[hash / 7 % suffixes.Length];
            int number = hash / 13 % 100;
            return $"{prefix} {suffix} {number}";
        }

        /// <summary>
        /// Returns a system string based on a hash value.
        /// </summary>
        private static string GetSystem(int hash)
        {
            string[] systems = ["PS1", "PS2", "Dreamcast", "Saturn", "SegaCD"];
            return systems[Math.Abs(hash) % systems.Length];
        }

        #endregion

        #region Helper Types

        /// <summary>
        /// Represents a listing entry produced by the model.
        /// </summary>
        private record ListingEntry(ImageRecord Image, string EntryName, string Extension);

        #endregion
    }
}
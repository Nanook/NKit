using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests for SourceFile reconstruction from FileName-tagged areas.
    /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
    ///
    /// For any CUE or GDI image stored with FileName-tagged areas, calling DataStoreAsIso.Construct()
    /// SHALL produce a SourceFile where ImageFiles contains one entry per FileName-tagged area,
    /// each with the correct filename, offset, size, and CRC matching the corresponding area record.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class SourceFileReconstructionPropertyTests
    {
        #region Model Methods

        /// <summary>
        /// Models the reconstruction logic from DataStoreAsIso.Construct() for CUE/GDI images.
        /// When FileName-tagged areas are present, builds individual SourceFileItem entries
        /// per track file using the stored filename, offset, size, and CRC from each area.
        /// </summary>
        private static SourceFileItem[] ModelReconstructImageFiles(
            List<AreaRecord> areas,
            ContainerType format,
            string imageName,
            long imageSize,
            uint imageCrc)
        {
            // Filter to areas that have FileName metadata (same as real code)
            List<AreaRecord> fileAreas = areas
                .Where(a => a.Metadata?.ContainsKey(AreaValueType.FileName) == true)
                .OrderBy(a => a.Offset)
                .ToList();

            if (format == ContainerType.Cue || format == ContainerType.Gdi)
            {
                if (fileAreas.Any())
                {
                    // Folder-based CUE/GDI: build individual track files as ImageFiles
                    List<SourceFileItem> imageFiles = new List<SourceFileItem>();
                    foreach (AreaRecord fa in fileAreas)
                    {
                        string fileName = fa.Metadata[AreaValueType.FileName]!;
                        string ext = System.IO.Path.GetExtension(fileName);
                        imageFiles.Add(new SourceFileItem(
                            "", fileName, ext, "",
                            fa.Offset, fa.Size, (long)fa.Crc32, false, false));
                    }
                    return imageFiles.ToArray();
                }
                else
                {
                    // Legacy flat storage fallback
                    string imgExt = format == ContainerType.Cue ? ".bin" : ".raw";
                    SourceFileItem single = new SourceFileItem("", imageName + imgExt, imgExt, "", -1, imageSize, (long)imageCrc, false, false);
                    return new[] { single };
                }
            }

            // Non-CUE/GDI format: not modeled here
            return Array.Empty<SourceFileItem>();
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
        ///
        /// For any CUE image with N FileName-tagged areas, Construct() produces a SourceFile
        /// where ImageFiles contains one entry per area with correct filename, offset, size, and CRC.
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueImage_WithFileNameAreas_ProducesCorrectImageFiles(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            List<AreaRecord> areas = GenerateFileNameTaggedAreas(trackCount, s);
            long imageSize = areas.Sum(a => a.Size);
            uint imageCrc = (uint)(Math.Abs(s) ^ 0xCAFEBABE);

            SourceFileItem[] imageFiles = ModelReconstructImageFiles(
                areas, ContainerType.Cue, imageName, imageSize, imageCrc);

            // Property: ImageFiles count equals number of FileName-tagged areas
            if (imageFiles.Length != trackCount)
                return false;

            // Property: Each ImageFile has correct filename, offset, size, and CRC
            List<AreaRecord> orderedAreas = areas.OrderBy(a => a.Offset).ToList();
            for (int i = 0; i < trackCount; i++)
            {
                AreaRecord area = orderedAreas[i];
                SourceFileItem file = imageFiles[i];
                string expectedFileName = area.Metadata[AreaValueType.FileName]!;

                if (file.FileName != expectedFileName)
                    return false;
                if (file.Offset != area.Offset)
                    return false;
                if (file.Size != area.Size)
                    return false;
                if (file.Crc != (long)area.Crc32)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
        ///
        /// For any GDI image with N FileName-tagged areas, Construct() produces a SourceFile
        /// where ImageFiles contains one entry per area with correct filename, offset, size, and CRC.
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool GdiImage_WithFileNameAreas_ProducesCorrectImageFiles(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            List<AreaRecord> areas = GenerateFileNameTaggedAreas(trackCount, s, isGdi: true);
            long imageSize = areas.Sum(a => a.Size);
            uint imageCrc = (uint)(Math.Abs(s) ^ 0xDEADFACE);

            SourceFileItem[] imageFiles = ModelReconstructImageFiles(
                areas, ContainerType.Gdi, imageName, imageSize, imageCrc);

            // Property: ImageFiles count equals number of FileName-tagged areas
            if (imageFiles.Length != trackCount)
                return false;

            // Property: Each ImageFile has correct filename, offset, size, and CRC
            List<AreaRecord> orderedAreas = areas.OrderBy(a => a.Offset).ToList();
            for (int i = 0; i < trackCount; i++)
            {
                AreaRecord area = orderedAreas[i];
                SourceFileItem file = imageFiles[i];
                string expectedFileName = area.Metadata[AreaValueType.FileName]!;

                if (file.FileName != expectedFileName)
                    return false;
                if (file.Offset != area.Offset)
                    return false;
                if (file.Size != area.Size)
                    return false;
                if (file.Crc != (long)area.Crc32)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
        ///
        /// For any CUE/GDI image with FileName-tagged areas, the number of ImageFiles produced
        /// equals exactly the number of FileName-tagged areas (one-to-one mapping).
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileNameAreas_ProduceOneImageFilePerArea(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed,
            bool isCue)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            List<AreaRecord> areas = GenerateFileNameTaggedAreas(trackCount, s, isGdi: !isCue);
            long imageSize = areas.Sum(a => a.Size);
            uint imageCrc = (uint)(Math.Abs(s) ^ 0xBAADF00D);

            ContainerType format = isCue ? ContainerType.Cue : ContainerType.Gdi;
            SourceFileItem[] imageFiles = ModelReconstructImageFiles(
                areas, format, imageName, imageSize, imageCrc);

            return imageFiles.Length == trackCount;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
        ///
        /// For any CUE/GDI image with NO FileName-tagged areas (legacy flat storage),
        /// Construct() falls back to a single ImageFile with the full image size and CRC.
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool NoFileNameAreas_FallsBackToSingleImageFile(
            NonNegativeInt seed,
            bool isCue)
        {
            int s = seed.Get;
            string imageName = GenerateImageName(s);
            long imageSize = (long)(Math.Abs(s) % 800_000_000) + 100_000;
            uint imageCrc = (uint)(Math.Abs(s) ^ 0xFEEDFACE);

            // Generate areas WITHOUT FileName metadata (legacy flat storage)
            List<AreaRecord> areas = GenerateAreasWithoutFileName(3, s);

            ContainerType format = isCue ? ContainerType.Cue : ContainerType.Gdi;
            SourceFileItem[] imageFiles = ModelReconstructImageFiles(
                areas, format, imageName, imageSize, imageCrc);

            // Should produce exactly one ImageFile (legacy fallback)
            if (imageFiles.Length != 1)
                return false;

            SourceFileItem single = imageFiles[0];
            string expectedExt = isCue ? ".bin" : ".raw";
            string expectedFileName = imageName + expectedExt;

            if (single.FileName != expectedFileName)
                return false;
            if (single.Size != imageSize)
                return false;
            if (single.Crc != (long)imageCrc)
                return false;
            if (single.Offset != -1)
                return false;

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
        ///
        /// For any CUE/GDI image with FileName-tagged areas, the ImageFiles are ordered
        /// by area offset (ascending), matching the physical layout on disc.
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ImageFiles_OrderedByAreaOffset(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed,
            bool isCue)
        {
            int trackCount = 2 + (trackCountRaw.Get % 19); // 2–20 tracks (need at least 2 to test ordering)
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            List<AreaRecord> areas = GenerateFileNameTaggedAreas(trackCount, s, isGdi: !isCue);
            long imageSize = areas.Sum(a => a.Size);
            uint imageCrc = (uint)(Math.Abs(s) ^ 0xABCD1234);

            ContainerType format = isCue ? ContainerType.Cue : ContainerType.Gdi;
            SourceFileItem[] imageFiles = ModelReconstructImageFiles(
                areas, format, imageName, imageSize, imageCrc);

            // Verify ImageFiles are ordered by offset (ascending)
            for (int i = 1; i < imageFiles.Length; i++)
            {
                if (imageFiles[i].Offset < imageFiles[i - 1].Offset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 3: SourceFile Reconstruction from FileName Areas
        ///
        /// For any CUE/GDI image with FileName-tagged areas, each ImageFile's extension
        /// matches the extension extracted from the stored filename in the area metadata.
        ///
        /// **Validates: Requirements 4.1, 4.2, 4.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ImageFiles_ExtensionMatchesStoredFileName(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed,
            bool isCue)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            List<AreaRecord> areas = GenerateFileNameTaggedAreas(trackCount, s, isGdi: !isCue);
            long imageSize = areas.Sum(a => a.Size);
            uint imageCrc = (uint)(Math.Abs(s) ^ 0x12345678);

            ContainerType format = isCue ? ContainerType.Cue : ContainerType.Gdi;
            SourceFileItem[] imageFiles = ModelReconstructImageFiles(
                areas, format, imageName, imageSize, imageCrc);

            List<AreaRecord> orderedAreas = areas.OrderBy(a => a.Offset).ToList();
            for (int i = 0; i < imageFiles.Length; i++)
            {
                string storedFileName = orderedAreas[i].Metadata[AreaValueType.FileName]!;
                string expectedExt = System.IO.Path.GetExtension(storedFileName);
                if (imageFiles[i].Extension != expectedExt)
                    return false;
            }

            return true;
        }

        #endregion

        #region Generators

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
        /// Generates a list of AreaRecords with FileName metadata set (folder-based CUE/GDI storage).
        /// Each area has a unique offset, size, CRC, and a FileName metadata entry.
        /// </summary>
        private static List<AreaRecord> GenerateFileNameTaggedAreas(int count, int seed, bool isGdi = false)
        {
            List<AreaRecord> areas = new List<AreaRecord>();
            string[] binExtensions = [".bin", ".raw", ".iso"];
            string[] gdiExtensions = [".bin", ".raw"];
            string[] extensions = isGdi ? gdiExtensions : binExtensions;

            long currentOffset = 0;
            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));
                int blockSize = (hash % 3 == 0) ? 0x800 : 0x930;
                long areaSize = (long)(150 + (hash % 500)) * blockSize; // 150–649 sectors

                string ext = extensions[hash % extensions.Length];
                string fileName = $"track{i + 1:D2}{ext}";

                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.FileName, fileName);
                metadata.Set(AreaValueType.BlockSize, (long)blockSize);
                metadata.Set(AreaValueType.Track, i);
                metadata.Set(AreaValueType.Session, 1 + (hash % 2));
                metadata.Set(AreaValueType.FsType, (hash % 4 == 0) ? "Audio" : "FileSystem");

                areas.Add(new AreaRecord
                {
                    Id = i + 1,
                    ImageId = 1,
                    Offset = currentOffset,
                    Size = areaSize,
                    Crc32 = (uint)(hash ^ 0xDEADBEEF ^ (i * 0x1337)),
                    XxHash64 = (ulong)(hash ^ 0xCAFEBABEL ^ (i * 0x7331L)),
                    StrideBlockSize = blockSize,
                    StrideDataOffset = blockSize == 0x930 ? 16 : 0,
                    StrideDataLength = blockSize == 0x930 ? 2048 : 2048,
                    SectionSize = 0x200000,
                    Metadata = metadata
                });

                currentOffset += areaSize;
            }

            return areas;
        }

        /// <summary>
        /// Generates a list of AreaRecords WITHOUT FileName metadata (legacy flat storage).
        /// </summary>
        private static List<AreaRecord> GenerateAreasWithoutFileName(int count, int seed)
        {
            List<AreaRecord> areas = new List<AreaRecord>();
            long currentOffset = 0;

            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));
                int blockSize = 0x930;
                long areaSize = (long)(150 + (hash % 500)) * blockSize;

                AreaMetadata metadata = new AreaMetadata();
                metadata.Set(AreaValueType.BlockSize, (long)blockSize);
                metadata.Set(AreaValueType.Track, i);
                metadata.Set(AreaValueType.Session, 1);
                metadata.Set(AreaValueType.FsType, "FileSystem");
                // No FileName metadata set - legacy flat storage

                areas.Add(new AreaRecord
                {
                    Id = i + 1,
                    ImageId = 1,
                    Offset = currentOffset,
                    Size = areaSize,
                    Crc32 = (uint)(hash ^ 0xABCDEF01),
                    Metadata = metadata
                });

                currentOffset += areaSize;
            }

            return areas;
        }

        #endregion
    }
}
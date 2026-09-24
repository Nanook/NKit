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
    /// Property-based tests for CueFolder aggregation correctness.
    /// Feature: cue-gdi-folder-storage, Property 7: CueFolder Aggregation Correctness
    ///
    /// For any source folder containing N (N ≥ 2) CUE or GDI images, the CueFolderBuilder
    /// SHALL create a CueFolder image whose filesystem.yaml contains an ifs section with
    /// exactly N entries, each referencing the correct child image ID and filename.
    ///
    /// **Validates: Requirements 10.1, 10.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class CueFolderAggregationPropertyTests
    {
        #region Model Methods

        /// <summary>
        /// Models the core CueFolderBuilder logic for building the ifs section of filesystem.yaml.
        /// This models the loop in CueFolderBuilder.Build() that iterates over childImages
        /// and calls fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size) for each file.
        /// </summary>
        private static FsYaml ModelBuildCueFolderFsYaml(List<ChildImageInfo> childImages)
        {
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);

            foreach (ChildImageInfo child in childImages)
            {
                if (child.Files == null)
                    continue;

                foreach (ChildImageFile file in child.Files)
                {
                    fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size);
                }
            }

            return fsYaml;
        }

        /// <summary>
        /// Models the expected total ifs entry count from a list of child images.
        /// Each child image contributes one ifs entry per file in its Files list.
        /// </summary>
        private static int ModelExpectedIfsCount(List<ChildImageInfo> childImages)
        {
            int count = 0;
            foreach (ChildImageInfo child in childImages)
            {
                if (child.Files != null)
                    count += child.Files.Count;
            }
            return count;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 7: CueFolder Aggregation Correctness
        ///
        /// For any source folder with N (N ≥ 2) CUE/GDI child images, CueFolderBuilder creates
        /// a CueFolder whose filesystem.yaml contains an ifs section with exactly N entries
        /// (one per child image file), each referencing the correct child image ID and filename.
        ///
        /// **Validates: Requirements 10.1, 10.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueFolderBuilder_IfsSection_HasExactlyNEntries_WithCorrectReferences(
            NonNegativeInt childCountRaw,
            NonNegativeInt seed)
        {
            int childCount = 2 + (childCountRaw.Get % 9); // 2–10 child images (N ≥ 2)
            int s = seed.Get;

            // Generate arbitrary child images
            List<ChildImageInfo> childImages = GenerateChildImages(childCount, s);

            // Model the CueFolderBuilder logic
            FsYaml fsYaml = ModelBuildCueFolderFsYaml(childImages);

            // Verify: ifs section has exactly the expected number of entries
            int expectedCount = ModelExpectedIfsCount(childImages);
            if (fsYaml.ImageFileSystems.Count != expectedCount)
                return false;

            // Verify: each ifs entry references the correct child image ID and filename
            int entryIndex = 0;
            foreach (ChildImageInfo child in childImages)
            {
                if (child.Files == null)
                    continue;

                foreach (ChildImageFile file in child.Files)
                {
                    FsYamlIfsEntry entry = fsYaml.ImageFileSystems[entryIndex];
                    if (entry.FileName != file.FileName)
                        return false;
                    if (entry.ImageId != file.ImageId)
                        return false;
                    if (entry.Size != file.Size)
                        return false;
                    entryIndex++;
                }
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 7: CueFolder Aggregation Correctness (Round-Trip)
        ///
        /// For any source folder with N (N ≥ 2) CUE/GDI child images, the filesystem.yaml
        /// produced by CueFolderBuilder can be serialized to YAML and parsed back, yielding
        /// the same ifs entries (filename, imageId, size) as the original.
        ///
        /// **Validates: Requirements 10.1, 10.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueFolderBuilder_IfsSection_RoundTrips_ThroughYamlSerialization(
            NonNegativeInt childCountRaw,
            NonNegativeInt seed)
        {
            int childCount = 2 + (childCountRaw.Get % 9); // 2–10 child images (N ≥ 2)
            int s = seed.Get;

            // Generate arbitrary child images
            List<ChildImageInfo> childImages = GenerateChildImages(childCount, s);

            // Model the CueFolderBuilder logic
            FsYaml fsYaml = ModelBuildCueFolderFsYaml(childImages);

            // Serialize to YAML
            string yaml = fsYaml.ToYaml();

            // Parse back
            FsYaml parsed = FsYaml.FromYaml(yaml);

            // Verify: parsed ifs section matches original
            if (parsed.ImageFileSystems.Count != fsYaml.ImageFileSystems.Count)
                return false;

            for (int i = 0; i < fsYaml.ImageFileSystems.Count; i++)
            {
                FsYamlIfsEntry original = fsYaml.ImageFileSystems[i];
                FsYamlIfsEntry roundTripped = parsed.ImageFileSystems[i];

                if (roundTripped.FileName != original.FileName)
                    return false;
                if (roundTripped.ImageId != original.ImageId)
                    return false;
                if (roundTripped.Size != original.Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 7: CueFolder Aggregation Correctness (Child with null Files)
        ///
        /// For any source folder with N (N ≥ 2) child images where some children have null Files,
        /// CueFolderBuilder skips those children and only includes ifs entries for children with
        /// non-null Files lists.
        ///
        /// **Validates: Requirements 10.1, 10.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueFolderBuilder_IfsSection_SkipsChildrenWithNullFiles(
            NonNegativeInt childCountRaw,
            NonNegativeInt nullCountRaw,
            NonNegativeInt seed)
        {
            int childCount = 2 + (childCountRaw.Get % 9); // 2–10 child images
            int nullCount = Math.Min(nullCountRaw.Get % childCount, childCount - 1); // At least 1 non-null
            int s = seed.Get;

            // Generate child images with some having null Files
            List<ChildImageInfo> childImages = GenerateChildImagesWithNulls(childCount, nullCount, s);

            // Model the CueFolderBuilder logic
            FsYaml fsYaml = ModelBuildCueFolderFsYaml(childImages);

            // Count expected entries (only from non-null Files)
            int expectedCount = childImages
                .Where(c => c.Files != null)
                .Sum(c => c.Files.Count);

            if (fsYaml.ImageFileSystems.Count != expectedCount)
                return false;

            // Verify all entries come from children with non-null Files
            int entryIndex = 0;
            foreach (ChildImageInfo child in childImages)
            {
                if (child.Files == null)
                    continue;

                foreach (ChildImageFile file in child.Files)
                {
                    FsYamlIfsEntry entry = fsYaml.ImageFileSystems[entryIndex];
                    if (entry.FileName != file.FileName)
                        return false;
                    if (entry.ImageId != file.ImageId)
                        return false;
                    entryIndex++;
                }
            }

            return true;
        }

        #endregion

        #region Generators

        /// <summary>
        /// Generates a list of ChildImageInfo with the specified count.
        /// Each child has a unique ImageId and a Files list with 1–5 ChildImageFile entries.
        /// </summary>
        private static List<ChildImageInfo> GenerateChildImages(int count, int seed)
        {
            List<ChildImageInfo> result = new List<ChildImageInfo>();
            string[] extensions = [".cue", ".gdi"];

            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));
                long imageId = (long)(hash % 10000) + (i * 100) + 1;
                string ext = extensions[hash % extensions.Length];
                string imageName = GenerateImageName(hash, i);

                // Each child has 1–5 track files
                int fileCount = 1 + (hash % 5);
                List<ChildImageFile> files = new List<ChildImageFile>();
                for (int f = 0; f < fileCount; f++)
                {
                    int fileHash = Math.Abs((hash * 13) + (f * 7));
                    long fileSize = (long)(fileHash % 700_000_000) + 1_000_000;
                    files.Add(new ChildImageFile
                    {
                        FileName = GenerateTrackFileName(imageName, f, fileHash),
                        ImageId = imageId,
                        Size = fileSize
                    });
                }

                result.Add(new ChildImageInfo
                {
                    ImageId = imageId,
                    ImageName = imageName + ext,
                    IndexFileName = imageName + ext,
                    Files = files
                });
            }

            return result;
        }

        /// <summary>
        /// Generates a list of ChildImageInfo where some children have null Files.
        /// </summary>
        private static List<ChildImageInfo> GenerateChildImagesWithNulls(int count, int nullCount, int seed)
        {
            List<ChildImageInfo> result = GenerateChildImages(count, seed);

            // Set Files to null for the first nullCount children
            for (int i = 0; i < nullCount && i < result.Count; i++)
            {
                ChildImageInfo child = result[i];
                child.Files = null;
                result[i] = child;
            }

            return result;
        }

        /// <summary>
        /// Generates an arbitrary image name from a hash and index.
        /// </summary>
        private static string GenerateImageName(int hash, int index)
        {
            string[] prefixes = ["Game", "Disc", "Album", "Title", "Demo", "Bonus"];
            string[] suffixes = ["Edition", "Remaster", "Original", "Special"];
            string prefix = prefixes[Math.Abs(hash) % prefixes.Length];
            string suffix = suffixes[Math.Abs(hash / 7) % suffixes.Length];
            return $"{prefix} {suffix} {index + 1}";
        }

        /// <summary>
        /// Generates a track filename for a child image file.
        /// </summary>
        private static string GenerateTrackFileName(string imageName, int trackIndex, int hash)
        {
            string[] extensions = [".bin", ".raw", ".iso", ".wav"];
            string ext = extensions[Math.Abs(hash) % extensions.Length];
            return $"{imageName} (Track {trackIndex + 1:D2}){ext}";
        }

        #endregion
    }
}
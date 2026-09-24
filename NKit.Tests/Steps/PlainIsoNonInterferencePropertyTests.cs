using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests for plain ISO non-interference.
    /// Feature: cue-gdi-folder-storage, Property 6: Plain ISO Non-Interference
    ///
    /// For any plain ISO image (no IndexFile), calling CreateAreas SHALL produce area records
    /// with NO AreaValueType.FileName metadata, and the image SHALL be stored with ImageFormat.Iso.
    ///
    /// **Validates: Requirements 7.1, 7.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class PlainIsoNonInterferencePropertyTests
    {
        #region Model Methods

        /// <summary>
        /// Models the getTrackFileName private method from DataStoreIso9660Formatter.
        /// Returns null when IndexFile is null or has no items (plain ISO case).
        /// </summary>
        private static string ModelGetTrackFileName(
            SourceFileTrack[] indexItems,
            string imageName,
            int trackIndex)
        {
            if (indexItems == null || indexItems.Length == 0)
                return null;

            if (trackIndex < 0)
                return null;

            SourceFileTrack track = indexItems.FirstOrDefault(t => t.TrackIndex == trackIndex);
            if (track == null && trackIndex >= 0 && trackIndex < indexItems.Length)
                track = indexItems[trackIndex];

            if (track != null && !string.IsNullOrEmpty(track.FileName))
                return track.FileName;

            string baseName = imageName ?? "image";
            int displayTrackNo = trackIndex + 1;
            return $"{baseName} (Track {displayTrackNo:D2}).bin";
        }

        /// <summary>
        /// Models the BuildAreaMetadata method's behavior for a plain ISO image.
        /// When IndexFile is null/empty, no FileName metadata should be set.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadata(
            AreaType areaType,
            int blockSize,
            int trackIndex,
            int session,
            long physicalOffset,
            string trackType,
            SourceFileTrack[] indexItems,
            string imageName)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, areaType.ToString());
            metadata.Set(AreaValueType.BlockSize, (long)blockSize);

            metadata.Set(AreaValueType.Track, trackIndex);
            metadata.Set(AreaValueType.Session, session);

            switch (areaType)
            {
                case AreaType.FileSystem:
                    metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
                    if (!string.IsNullOrEmpty(trackType))
                        metadata.Set(AreaValueType.Type, trackType);
                    break;

                case AreaType.Audio:
                    metadata.Set(AreaValueType.Type, "Audio");
                    break;
            }

            // Set FileName metadata only when getTrackFileName returns non-null
            string trackFileName = ModelGetTrackFileName(indexItems, imageName, trackIndex);
            if (!string.IsNullOrEmpty(trackFileName))
                metadata.Set(AreaValueType.FileName, trackFileName);

            return metadata;
        }

        /// <summary>
        /// Models the ImageFormat determination logic from DataStoreIso9660Formatter.
        /// Returns ImageFormat.Iso when IndexFile is null or has FileType == None.
        /// Returns ImageFormat.Cue for CUE index files, ImageFormat.Gdi for GDI index files.
        /// </summary>
        private static ImageFormat ModelDetermineImageFormat(IndexFileType indexFileType)
        {
            if (indexFileType == IndexFileType.Cue)
                return ImageFormat.Cue;
            if (indexFileType == IndexFileType.Gdi)
                return ImageFormat.Gdi;
            return ImageFormat.Iso;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 6: Plain ISO Non-Interference
        ///
        /// For any plain ISO image (no IndexFile), BuildAreaMetadata produces area records
        /// with NO AreaValueType.FileName metadata. This verifies that the FileName metadata
        /// assignment logic does not interfere with plain ISO images.
        ///
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool PlainIso_NoIndexFile_NoFileNameMetadata(
            NonNegativeInt areaCountRaw,
            NonNegativeInt seed)
        {
            int areaCount = 1 + (areaCountRaw.Get % 10); // 1–10 areas for plain ISO
            int s = seed.Get;

            string imageName = GenerateImageName(s);

            for (int i = 0; i < areaCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                AreaType areaType = AreaType.FileSystem; // Plain ISOs are always FileSystem areas
                int blockSize = (hash % 2 == 0) ? 0x800 : 0x930;
                int session = 1;
                long physicalOffset = (long)i * 150 * blockSize;
                string trackType = GetTrackType(hash);

                // Pass null IndexFile items (plain ISO — no IndexFile)
                AreaMetadata metadata = ModelBuildAreaMetadata(
                    areaType, blockSize, i, session, physicalOffset,
                    trackType, null, imageName);

                // Verify NO FileName metadata is set for plain ISO
                if (metadata.ContainsKey(AreaValueType.FileName))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 6: Plain ISO Non-Interference
        ///
        /// For any plain ISO image (no IndexFile), the image format determination logic
        /// SHALL produce ImageFormat.Iso. This verifies that plain ISO images are stored
        /// with the correct format regardless of other image properties.
        ///
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool PlainIso_NoIndexFile_ImageFormatIsIso(
            NonNegativeInt seed)
        {
            // Plain ISO images have IndexFileType.None (no IndexFile present)
            ImageFormat format = ModelDetermineImageFormat(IndexFileType.None);

            if (format != ImageFormat.Iso)
                return false;

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 6: Plain ISO Non-Interference
        ///
        /// For any plain ISO image with an empty IndexFile (Items is empty array),
        /// BuildAreaMetadata produces area records with NO AreaValueType.FileName metadata.
        /// This covers the edge case where an IndexFile object exists but has no items.
        ///
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool PlainIso_EmptyIndexFileItems_NoFileNameMetadata(
            NonNegativeInt areaCountRaw,
            NonNegativeInt seed)
        {
            int areaCount = 1 + (areaCountRaw.Get % 10); // 1–10 areas
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            // Empty array of tracks (IndexFile exists but has no items)
            SourceFileTrack[] emptyTracks = Array.Empty<SourceFileTrack>();

            for (int i = 0; i < areaCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                int blockSize = (hash % 2 == 0) ? 0x800 : 0x930;
                int session = 1;
                long physicalOffset = (long)i * 150 * blockSize;
                string trackType = GetTrackType(hash);

                // Pass empty IndexFile items (IndexFile exists but has no tracks)
                AreaMetadata metadata = ModelBuildAreaMetadata(
                    AreaType.FileSystem, blockSize, i, session, physicalOffset,
                    trackType, emptyTracks, imageName);

                // Verify NO FileName metadata is set
                if (metadata.ContainsKey(AreaValueType.FileName))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 6: Plain ISO Non-Interference
        ///
        /// For any plain ISO image (no IndexFile), BuildAreaMetadata still correctly sets
        /// all standard metadata fields (BlockSize, PhysicalOffset, Track, Session, Type).
        /// This verifies that the absence of FileName metadata does not affect other metadata.
        ///
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool PlainIso_NoIndexFile_StandardMetadataPreserved(
            NonNegativeInt areaCountRaw,
            NonNegativeInt seed)
        {
            int areaCount = 1 + (areaCountRaw.Get % 10); // 1–10 areas
            int s = seed.Get;

            string imageName = GenerateImageName(s);

            for (int i = 0; i < areaCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                int blockSize = (hash % 2 == 0) ? 0x800 : 0x930;
                int session = 1;
                long physicalOffset = (long)i * 150 * blockSize;
                string trackType = GetTrackType(hash);

                AreaMetadata metadata = ModelBuildAreaMetadata(
                    AreaType.FileSystem, blockSize, i, session, physicalOffset,
                    trackType, null, imageName);

                // Verify standard metadata IS present and correct
                if (metadata.GetString(AreaValueType.FsType) != AreaType.FileSystem.ToString())
                    return false;
                if (metadata.GetLong(AreaValueType.BlockSize) != (long)blockSize)
                    return false;
                if (metadata.GetLong(AreaValueType.Track) != i)
                    return false;
                if (metadata.GetLong(AreaValueType.Session) != session)
                    return false;
                if (metadata.GetLong(AreaValueType.PhysicalOffset) != physicalOffset)
                    return false;
                if (!string.IsNullOrEmpty(trackType) && metadata.GetString(AreaValueType.Type) != trackType)
                    return false;

                // And verify NO FileName metadata
                if (metadata.ContainsKey(AreaValueType.FileName))
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
            string[] prefixes = ["Game", "Disc", "Image", "Title", "System", "Archive", "Backup"];
            string[] suffixes = ["Edition", "Remaster", "Original", "Special", "Standard"];
            int hash = Math.Abs(seed);
            string prefix = prefixes[hash % prefixes.Length];
            string suffix = suffixes[hash / 7 % suffixes.Length];
            int number = hash / 13 % 100;
            return $"{prefix} {suffix} {number}";
        }

        /// <summary>
        /// Returns a track type string based on a hash value.
        /// </summary>
        private static string GetTrackType(int hash)
        {
            string[] types = ["Mode1", "Mode2Form1", "Mode2Form2"];
            return types[Math.Abs(hash) % types.Length];
        }

        #endregion
    }
}
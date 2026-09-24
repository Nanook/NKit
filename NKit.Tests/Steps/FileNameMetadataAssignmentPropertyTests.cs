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
    /// Property-based tests for FileName metadata assignment in DataStoreIso9660Formatter.
    /// Feature: cue-gdi-folder-storage, Property 1: FileName Metadata Assignment
    ///
    /// For any CUE or GDI image with N tracks (1–20) in its IndexFile, calling BuildAreaMetadata
    /// SHALL produce area records where each area's metadata contains AreaValueType.FileName set
    /// to the corresponding track filename from the IndexFile, AND all pre-existing metadata fields
    /// (BlockSize, PhysicalOffset, Track, Type, Session) remain unchanged.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class FileNameMetadataAssignmentPropertyTests
    {
        #region Model Methods

        /// <summary>
        /// Models the getTrackFileName private method from DataStoreIso9660Formatter.
        /// Returns the track filename for the given track index by looking up the corresponding
        /// SourceFileTrack in the IndexFile. Returns null when no IndexFile is present (plain ISO).
        /// When the track has no explicit filename, derives one from the image name and track number.
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

            // Find matching track by 0-based TrackIndex (mirrors the real implementation)
            SourceFileTrack track = indexItems.FirstOrDefault(t => t.TrackIndex == trackIndex);
            if (track == null && trackIndex >= 0 && trackIndex < indexItems.Length)
                track = indexItems[trackIndex];

            if (track != null && !string.IsNullOrEmpty(track.FileName))
                return track.FileName;

            // Derive filename when not explicitly set (use 1-based track number for display)
            string baseName = imageName ?? "image";
            int displayTrackNo = trackIndex + 1;
            return $"{baseName} (Track {displayTrackNo:D2}).bin";
        }

        /// <summary>
        /// Models the BuildAreaMetadata method's behavior for setting FileName metadata.
        /// Builds the full metadata for a scan area (including all pre-existing fields)
        /// and then adds FileName when applicable.
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

            // Track and Session (0-based track index stored as metadata)
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

            // Set FileName metadata for CUE/GDI tracks (after all other metadata is set)
            string trackFileName = ModelGetTrackFileName(indexItems, imageName, trackIndex);
            if (!string.IsNullOrEmpty(trackFileName))
                metadata.Set(AreaValueType.FileName, trackFileName);

            return metadata;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 1: FileName Metadata Assignment
        ///
        /// For any CUE/GDI image with N tracks (1–20) in its IndexFile, BuildAreaMetadata
        /// produces area records where each area's metadata contains AreaValueType.FileName
        /// set to the corresponding track filename from the IndexFile.
        ///
        /// **Validates: Requirements 1.1, 1.2, 1.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool BuildAreaMetadata_CueGdiImage_SetsFileNameFromIndexFile(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            // Generate arbitrary track data
            string imageName = GenerateImageName(s);
            SourceFileTrack[] tracks = GenerateTracks(trackCount, s, withExplicitFileNames: true);

            // For each track, build metadata and verify FileName is set correctly
            for (int i = 0; i < trackCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                AreaType areaType = (hash % 3 == 0) ? AreaType.Audio : AreaType.FileSystem;
                int blockSize = areaType == AreaType.Audio ? 0x930 : ((hash % 2 == 0) ? 0x930 : 0x800);
                int session = 1 + (hash % 3);
                long physicalOffset = (long)(hash & 0x7FFFFFFF) * 150;
                string trackType = areaType == AreaType.FileSystem ? GetTrackType(hash) : null;

                AreaMetadata metadata = ModelBuildAreaMetadata(
                    areaType, blockSize, tracks[i].TrackIndex, session, physicalOffset,
                    trackType, tracks, imageName);

                // Verify FileName is set to the track's filename
                string expectedFileName = tracks[i].FileName;
                string actualFileName = metadata.GetString(AreaValueType.FileName);

                if (actualFileName != expectedFileName)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 1: FileName Metadata Assignment (Derived Filenames)
        ///
        /// For any CUE/GDI image with N tracks where tracks have no explicit filename,
        /// BuildAreaMetadata derives a filename from the image name and track number
        /// in the format "{imageName} (Track {NN}).bin".
        ///
        /// **Validates: Requirements 1.1, 1.2, 1.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool BuildAreaMetadata_TracksWithNoFileName_DeriveFileNameFromImageName(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            // Generate tracks with NO explicit filenames (null/empty)
            string imageName = GenerateImageName(s);
            SourceFileTrack[] tracks = GenerateTracks(trackCount, s, withExplicitFileNames: false);

            for (int i = 0; i < trackCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                AreaType areaType = (hash % 3 == 0) ? AreaType.Audio : AreaType.FileSystem;
                int blockSize = areaType == AreaType.Audio ? 0x930 : 0x930;
                int session = 1;
                long physicalOffset = (long)i * 150 * 0x930;
                string trackType = areaType == AreaType.FileSystem ? "Mode1" : null;

                AreaMetadata metadata = ModelBuildAreaMetadata(
                    areaType, blockSize, tracks[i].TrackIndex, session, physicalOffset,
                    trackType, tracks, imageName);

                // Verify derived filename format: "{imageName} (Track {NN}).bin"
                int displayTrackNo = tracks[i].TrackIndex + 1;
                string expectedFileName = $"{imageName} (Track {displayTrackNo:D2}).bin";
                string actualFileName = metadata.GetString(AreaValueType.FileName);

                if (actualFileName != expectedFileName)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 1: FileName Metadata Assignment (Preservation)
        ///
        /// For any CUE/GDI image with N tracks, BuildAreaMetadata preserves all pre-existing
        /// metadata fields (BlockSize, PhysicalOffset, Track, Type, Session) alongside the
        /// new FileName metadata.
        ///
        /// **Validates: Requirements 1.1, 1.2, 1.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool BuildAreaMetadata_WithFileName_PreservesAllExistingMetadata(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            string imageName = GenerateImageName(s);
            SourceFileTrack[] tracks = GenerateTracks(trackCount, s, withExplicitFileNames: true);

            for (int i = 0; i < trackCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                AreaType areaType = (hash % 3 == 0) ? AreaType.Audio : AreaType.FileSystem;
                int blockSize = areaType == AreaType.Audio ? 0x930 : ((hash % 2 == 0) ? 0x930 : 0x800);
                int session = 1 + (hash % 3);
                long physicalOffset = (long)(hash & 0x7FFFFFFF) * 150;
                string trackType = areaType == AreaType.FileSystem ? GetTrackType(hash) : null;

                AreaMetadata metadata = ModelBuildAreaMetadata(
                    areaType, blockSize, tracks[i].TrackIndex, session, physicalOffset,
                    trackType, tracks, imageName);

                // Verify FileName IS present (CUE/GDI image has IndexFile)
                if (!metadata.ContainsKey(AreaValueType.FileName))
                    return false;

                // Verify pre-existing metadata is preserved
                if (metadata.GetString(AreaValueType.FsType) != areaType.ToString())
                    return false;
                if (metadata.GetLong(AreaValueType.BlockSize) != (long)blockSize)
                    return false;
                if (metadata.GetLong(AreaValueType.Track) != tracks[i].TrackIndex)
                    return false;
                if (metadata.GetLong(AreaValueType.Session) != session)
                    return false;

                // Verify type-specific metadata
                if (areaType == AreaType.FileSystem)
                {
                    if (metadata.GetLong(AreaValueType.PhysicalOffset) != physicalOffset)
                        return false;
                    if (!string.IsNullOrEmpty(trackType) && metadata.GetString(AreaValueType.Type) != trackType)
                        return false;
                }
                else if (areaType == AreaType.Audio)
                {
                    if (metadata.GetString(AreaValueType.Type) != "Audio")
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 1: FileName Metadata Assignment (No IndexFile)
        ///
        /// For any plain ISO image (no IndexFile), BuildAreaMetadata produces area records
        /// with NO AreaValueType.FileName metadata.
        ///
        /// **Validates: Requirements 1.1, 1.2, 1.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool BuildAreaMetadata_NoIndexFile_NoFileNameMetadata(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 5); // 1–5 areas for plain ISO
            int s = seed.Get;

            string imageName = GenerateImageName(s);

            for (int i = 0; i < trackCount; i++)
            {
                int hash = Math.Abs((s * 41) + (i * 13));
                int blockSize = (hash % 2 == 0) ? 0x800 : 0x930;
                int session = 1;
                long physicalOffset = (long)i * 150 * blockSize;

                // Pass null/empty IndexFile items (plain ISO)
                AreaMetadata metadata = ModelBuildAreaMetadata(
                    AreaType.FileSystem, blockSize, i, session, physicalOffset,
                    "Mode1", null, imageName);

                // Verify NO FileName metadata is set
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
            string[] prefixes = ["Game", "Disc", "Image", "Title", "Track", "Album", "Demo"];
            string[] suffixes = ["Edition", "Remaster", "Original", "Special", "Deluxe"];
            int hash = Math.Abs(seed);
            string prefix = prefixes[hash % prefixes.Length];
            string suffix = suffixes[hash / 7 % suffixes.Length];
            int number = hash / 13 % 100;
            return $"{prefix} {suffix} {number}";
        }

        /// <summary>
        /// Generates an array of SourceFileTrack objects with arbitrary properties.
        /// When withExplicitFileNames is true, tracks have explicit filenames.
        /// When false, tracks have null/empty filenames (triggering derived name generation).
        /// </summary>
        private static SourceFileTrack[] GenerateTracks(int count, int seed, bool withExplicitFileNames)
        {
            SourceFileTrack[] tracks = new SourceFileTrack[count];
            string[] extensions = [".bin", ".raw", ".iso", ".wav"];

            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));
                tracks[i] = new SourceFileTrack
                {
                    TrackIndex = i, // 0-based
                    BlockSize = (hash % 3 == 0) ? 0x800 : 0x930,
                    BasicType = (hash % 4 == 0) ? IndexTrackBasicType.Audio : IndexTrackBasicType.Mode1,
                    TrackType = (hash % 4 == 0) ? IndexTrackType.Audio : IndexTrackType.Mode1Raw,
                    FileName = withExplicitFileNames
                        ? $"track{i + 1:D2}{extensions[hash % extensions.Length]}"
                        : null
                };
            }

            return tracks;
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
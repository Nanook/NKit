using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests for index file storage and parse round-trip.
    /// Feature: cue-gdi-folder-storage, Property 4: Index File Storage and Parse Round-Trip
    ///
    /// For any valid CUE or GDI index file content stored as a loose file in the DataStore,
    /// reading it back and parsing it SHALL produce an IndexFile with the same track references
    /// (filenames, track numbers, modes) as the original.
    ///
    /// **Validates: Requirements 4.3, 4.4, 9.1, 9.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class IndexFileStorageParseRoundTripPropertyTests
    {
        #region Property Tests

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 4: Index File Storage and Parse Round-Trip (CUE)
        ///
        /// For any valid CUE index file content generated from arbitrary tracks, storing as bytes
        /// and parsing back produces an IndexFile with the same track filenames, track numbers,
        /// and modes as the original.
        ///
        /// **Validates: Requirements 4.3, 9.1**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueIndexFile_StoreThenParse_PreservesTrackReferences(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            // Generate arbitrary tracks for a CUE image
            SourceFileTrack[] originalTracks = GenerateCueTracks(trackCount, s);
            List<string> fileNames = originalTracks.Select(t => t.FileName).ToList();

            // Generate CUE content using the real ToCue method (split mode so each track has its own FILE)
            string cueContent = IndexFile.ToCue(fileNames, split: true, originalTracks);

            // Store as bytes (simulating loose file storage)
            byte[] data = Encoding.UTF8.GetBytes(cueContent);

            // Parse back (simulating reading from loose file store)
            string fileName = "game.cue";
            string extension = ".cue";
            IndexFile parsed = IndexFile.Parse(fileName, extension, data);

            if (parsed == null)
                return false;

            // Verify track count matches
            if (parsed.Items.Length != trackCount)
                return false;

            // Verify each track's references match the original
            for (int i = 0; i < trackCount; i++)
            {
                SourceFileTrack original = originalTracks[i];
                SourceFileTrack parsedTrack = parsed.Items[i];

                // Track number (1-based TrackIndex)
                if (parsedTrack.TrackIndex != original.TrackIndex)
                    return false;

                // Filename
                if (parsedTrack.FileName != original.FileName)
                    return false;

                // Track mode (BasicType)
                if (parsedTrack.BasicType != original.BasicType)
                    return false;

                // Block size
                if (parsedTrack.BlockSize != original.BlockSize)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 4: Index File Storage and Parse Round-Trip (GDI)
        ///
        /// For any valid GDI index file content generated from arbitrary tracks, storing as bytes
        /// and parsing back produces an IndexFile with the same track filenames, track numbers,
        /// and modes as the original.
        ///
        /// **Validates: Requirements 4.4, 9.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool GdiIndexFile_StoreThenParse_PreservesTrackReferences(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            // Generate arbitrary tracks for a GDI image
            SourceFileTrack[] originalTracks = GenerateGdiTracks(trackCount, s);
            List<string> fileNames = originalTracks.Select(t => t.FileName).ToList();

            // Generate GDI content using the real ToGdi method
            string gdiContent = IndexFile.ToGdi(fileNames, originalTracks);

            // Store as bytes (simulating loose file storage)
            byte[] data = Encoding.UTF8.GetBytes(gdiContent);

            // Parse back (simulating reading from loose file store)
            string fileName = "disc.gdi";
            string extension = ".gdi";
            IndexFile parsed = IndexFile.Parse(fileName, extension, data);

            if (parsed == null)
                return false;

            // Verify track count matches
            if (parsed.Items.Length != trackCount)
                return false;

            // Verify each track's references match the original
            for (int i = 0; i < trackCount; i++)
            {
                SourceFileTrack original = originalTracks[i];
                SourceFileTrack parsedTrack = parsed.Items[i];

                // Track number (1-based TrackIndex)
                if (parsedTrack.TrackIndex != original.TrackIndex)
                    return false;

                // Filename (GDI parser strips quotes, so compare unquoted)
                if (parsedTrack.FileName != original.FileName)
                    return false;

                // Track mode (BasicType: Audio vs Mode1 for GDI)
                if (parsedTrack.BasicType != original.BasicType)
                    return false;

                // Block size
                if (parsedTrack.BlockSize != original.BlockSize)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 4: Index File Storage and Parse Round-Trip
        ///
        /// For any valid CUE index file, the parsed IndexFile has FileType == IndexFileType.Cue.
        ///
        /// **Validates: Requirements 4.3, 9.1**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool CueIndexFile_ParsedFileType_IsCue(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 10); // 1–10 tracks
            int s = seed.Get;

            SourceFileTrack[] tracks = GenerateCueTracks(trackCount, s);
            List<string> fileNames = tracks.Select(t => t.FileName).ToList();
            string cueContent = IndexFile.ToCue(fileNames, split: true, tracks);
            byte[] data = Encoding.UTF8.GetBytes(cueContent);

            IndexFile parsed = IndexFile.Parse("game.cue", ".cue", data);

            return parsed != null && parsed.FileType == IndexFileType.Cue;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 4: Index File Storage and Parse Round-Trip
        ///
        /// For any valid GDI index file, the parsed IndexFile has FileType == IndexFileType.Gdi.
        ///
        /// **Validates: Requirements 4.4, 9.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool GdiIndexFile_ParsedFileType_IsGdi(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed)
        {
            int trackCount = 1 + (trackCountRaw.Get % 10); // 1–10 tracks
            int s = seed.Get;

            SourceFileTrack[] tracks = GenerateGdiTracks(trackCount, s);
            List<string> fileNames = tracks.Select(t => t.FileName).ToList();
            string gdiContent = IndexFile.ToGdi(fileNames, tracks);
            byte[] data = Encoding.UTF8.GetBytes(gdiContent);

            IndexFile parsed = IndexFile.Parse("disc.gdi", ".gdi", data);

            return parsed != null && parsed.FileType == IndexFileType.Gdi;
        }

        /// <summary>
        /// Feature: cue-gdi-folder-storage, Property 4: Index File Storage and Parse Round-Trip
        ///
        /// For any valid CUE/GDI index file, the number of parsed tracks equals the number
        /// of original tracks (no tracks lost or duplicated during round-trip).
        ///
        /// **Validates: Requirements 4.3, 4.4, 9.1, 9.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool IndexFile_RoundTrip_PreservesTrackCount(
            NonNegativeInt trackCountRaw,
            NonNegativeInt seed,
            bool isCue)
        {
            int trackCount = 1 + (trackCountRaw.Get % 20); // 1–20 tracks
            int s = seed.Get;

            if (isCue)
            {
                SourceFileTrack[] tracks = GenerateCueTracks(trackCount, s);
                List<string> fileNames = tracks.Select(t => t.FileName).ToList();
                string content = IndexFile.ToCue(fileNames, split: true, tracks);
                byte[] data = Encoding.UTF8.GetBytes(content);
                IndexFile parsed = IndexFile.Parse("game.cue", ".cue", data);
                return parsed != null && parsed.Items.Length == trackCount;
            }
            else
            {
                SourceFileTrack[] tracks = GenerateGdiTracks(trackCount, s);
                List<string> fileNames = tracks.Select(t => t.FileName).ToList();
                string content = IndexFile.ToGdi(fileNames, tracks);
                byte[] data = Encoding.UTF8.GetBytes(content);
                IndexFile parsed = IndexFile.Parse("disc.gdi", ".gdi", data);
                return parsed != null && parsed.Items.Length == trackCount;
            }
        }

        #endregion

        #region Generators

        /// <summary>
        /// Generates an array of SourceFileTrack objects suitable for CUE format.
        /// CUE tracks have 1-based TrackIndex, explicit filenames, and valid track types.
        /// </summary>
        private static SourceFileTrack[] GenerateCueTracks(int count, int seed)
        {
            SourceFileTrack[] tracks = new SourceFileTrack[count];

            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));

                // CUE supports: Audio, Mode1/2352, Mode1/2048, Mode2/2352, Mode2/2336
                IndexTrackBasicType basicType;
                IndexTrackType trackType;
                int blockSize;

                int typeChoice = hash % 5;
                switch (typeChoice)
                {
                    case 0:
                        basicType = IndexTrackBasicType.Audio;
                        trackType = IndexTrackType.Audio;
                        blockSize = 2352;
                        break;
                    case 1:
                        basicType = IndexTrackBasicType.Mode1;
                        trackType = IndexTrackType.Mode1Raw;
                        blockSize = 2352;
                        break;
                    case 2:
                        basicType = IndexTrackBasicType.Mode1;
                        trackType = IndexTrackType.Mode1;
                        blockSize = 2048;
                        break;
                    case 3:
                        basicType = IndexTrackBasicType.Mode2;
                        trackType = IndexTrackType.Mode2Raw;
                        blockSize = 2352;
                        break;
                    default:
                        basicType = IndexTrackBasicType.Mode2;
                        trackType = IndexTrackType.Mode2;
                        blockSize = 2336;
                        break;
                }

                // Generate a safe filename (no spaces, no special chars that could break parsing)
                string fileName = $"track{i + 1:D2}.bin";

                tracks[i] = new SourceFileTrack
                {
                    TrackIndex = i + 1, // CUE uses 1-based track numbers
                    BlockSize = blockSize,
                    BasicType = basicType,
                    TrackType = trackType,
                    FileName = fileName,
                    BlockIdx = 0, // Each track starts at 0 in split mode
                    Blocks = 150 + (hash % 500), // 150–649 blocks
                    ImageOffset = 0,
                    LogicalOffset = 0,
                    Session = 0
                };
            }

            return tracks;
        }

        /// <summary>
        /// Generates an array of SourceFileTrack objects suitable for GDI format.
        /// GDI tracks have 1-based TrackIndex, explicit filenames, and either Audio (mode 0) or Data (mode 4).
        /// </summary>
        private static SourceFileTrack[] GenerateGdiTracks(int count, int seed)
        {
            SourceFileTrack[] tracks = new SourceFileTrack[count];
            long currentBlockIdx = 0;

            for (int i = 0; i < count; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 19));

                // GDI only supports Audio (mode 0) and Data (mode 4)
                bool isAudio = hash % 3 == 0;
                IndexTrackBasicType basicType = isAudio ? IndexTrackBasicType.Audio : IndexTrackBasicType.Mode1;
                IndexTrackType trackType;
                int blockSize = 2352; // GDI always uses 2352

                if (isAudio)
                    trackType = IndexTrackType.Audio;
                else
                    trackType = IndexTrackType.Mode1Raw; // GDI data tracks with 2352 block size

                // Generate filename without spaces (to avoid quoting issues in round-trip)
                string fileName = $"track{i + 1:D2}.bin";

                tracks[i] = new SourceFileTrack
                {
                    TrackIndex = i + 1, // GDI uses 1-based track numbers
                    BlockSize = blockSize,
                    BasicType = basicType,
                    TrackType = trackType,
                    FileName = fileName,
                    BlockIdx = currentBlockIdx,
                    Blocks = 150 + (hash % 500),
                    LogicalOffset = currentBlockIdx * blockSize,
                    ImageOffset = currentBlockIdx * blockSize
                };

                // Advance to next track position (with gap)
                currentBlockIdx += tracks[i].Blocks + 150; // 150 block gap between tracks
            }

            return tracks;
        }

        #endregion
    }
}
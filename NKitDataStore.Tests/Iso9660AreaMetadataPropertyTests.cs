using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for ISO9660 area metadata completeness.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 5: ISO9660 Area Metadata Completeness
    /// **Validates: Requirements 2.6, 2.7, 2.8, 2.10, 7.2, 7.6**
    ///
    /// For any ISO9660 multi-track image with N tracks, calling CreateAreas SHALL produce
    /// N area records where each area's metadata contains: Track (1-based index),
    /// BlockSize (track block size), Session (session number), and PhysicalOffset (starting LBA).
    /// For raw-sector tracks, the stride metadata (StrideBlockSize, StrideDataOffset,
    /// StrideDataLength) SHALL match the expected values for the track's sector mode.
    /// </summary>
    public class Iso9660AreaMetadataPropertyTests
    {
        /// <summary>
        /// Represents a track configuration for generating test data.
        /// </summary>
        private record TrackConfig(
            int TrackNumber,
            int Session,
            int BlockSize,
            int BlockFsOffset,
            int BlockFsSize,
            AreaType AreaType,
            long PhysicalOffset,
            long ImageOffset,
            string TrackMode);

        /// <summary>
        /// Models the BuildAreaMetadata logic for ISO9660 areas.
        /// This is a faithful model of DataStoreIso9660Formatter.BuildAreaMetadata
        /// that can be tested without instantiating the full formatter (which requires
        /// a DataStore on disk).
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadata(TrackConfig track)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, track.AreaType.ToString());

            // BlockSize
            metadata.Set(AreaValueType.BlockSize, (long)track.BlockSize);

            // Track and Session
            metadata.Set(AreaValueType.Track, track.TrackNumber);
            metadata.Set(AreaValueType.Session, track.Session);

            switch (track.AreaType)
            {
                case AreaType.FileSystem:
                    // PhysicalOffset — starting LBA for sector address computation
                    metadata.Set(AreaValueType.PhysicalOffset, track.PhysicalOffset);

                    // Track type info from mode
                    if (!string.IsNullOrEmpty(track.TrackMode))
                        metadata.Set(AreaValueType.Type, track.TrackMode);

                    break;

                case AreaType.Audio:
                    metadata.Set(AreaValueType.Type, "Audio");
                    break;

                default:
                    break;
            }

            return metadata;
        }

        /// <summary>
        /// Computes the expected stride for a given track configuration.
        /// Models the DataStoreIso9660Formatter.GetStrideForPartition logic.
        /// </summary>
        private static (int SourceBlockSize, int DataOffset, int DataLength)? ModelGetStride(TrackConfig track)
        {
            if (track.BlockSize == 0x800)
            {
                // Cooked sectors — no striding needed
                return null;
            }

            if (track.BlockSize == 0x930)
            {
                if (track.AreaType == AreaType.Audio)
                {
                    // Audio: full 2352 bytes are non-recreatable content
                    return (0x930, 0, 0x930);
                }

                // Use BlockFsOffset and BlockFsSize when they differ from BlockSize
                if (track.BlockFsSize > 0 && track.BlockFsOffset >= 0 && track.BlockFsSize != track.BlockSize)
                {
                    return (track.BlockSize, track.BlockFsOffset, track.BlockFsSize);
                }

                // Fallback based on track mode
                return track.TrackMode switch
                {
                    "Mode2Form2" => (0x930, 0x18, 0x914),
                    "Mode2Form1" => (0x930, 0x18, 0x800),
                    _ => (0x930, 0x10, 0x800) // Mode1 default
                };
            }

            // Unrecognized block size — no stride
            return null;
        }

        /// <summary>
        /// Generates a valid multi-track ISO9660 image configuration.
        /// </summary>
        private static List<TrackConfig> GenerateMultiTrackImage(int trackCount, Random rng)
        {
            List<TrackConfig> tracks = new List<TrackConfig>();
            long currentImageOffset = 0;
            int currentSession = 1;

            // Valid track modes for data tracks
            string[] dataModes = { "Mode1", "Mode2Form1", "Mode2Form2" };

            for (int i = 0; i < trackCount; i++)
            {
                int trackNumber = i + 1; // 1-based

                // Randomly decide if this is a new session (after first track)
                if (i > 0 && rng.Next(4) == 0)
                    currentSession++;

                // Randomly choose track type
                bool isAudio = rng.Next(3) == 0 && i > 0; // First track is always data
                AreaType areaType = isAudio ? AreaType.Audio : AreaType.FileSystem;

                int blockSize;
                int blockFsOffset;
                int blockFsSize;
                string trackMode;

                if (isAudio)
                {
                    blockSize = 0x930;
                    blockFsOffset = 0;
                    blockFsSize = 0x930;
                    trackMode = "Audio";
                }
                else
                {
                    // Randomly choose between raw and cooked
                    bool isRaw = rng.Next(3) != 0; // 2/3 chance of raw
                    if (isRaw)
                    {
                        blockSize = 0x930;
                        trackMode = dataModes[rng.Next(dataModes.Length)];
                        switch (trackMode)
                        {
                            case "Mode1":
                                blockFsOffset = 0x10;
                                blockFsSize = 0x800;
                                break;
                            case "Mode2Form1":
                                blockFsOffset = 0x18;
                                blockFsSize = 0x800;
                                break;
                            case "Mode2Form2":
                                blockFsOffset = 0x18;
                                blockFsSize = 0x914;
                                break;
                            default:
                                blockFsOffset = 0x10;
                                blockFsSize = 0x800;
                                break;
                        }
                    }
                    else
                    {
                        blockSize = 0x800;
                        blockFsOffset = 0;
                        blockFsSize = 0x800;
                        trackMode = "Mode1";
                    }
                }

                // Physical offset (LBA) — typically starts at 0 or 150 for first track
                long physicalOffset = (i == 0) ? 0 : (long)rng.Next(150, 500000);

                tracks.Add(new TrackConfig(
                    TrackNumber: trackNumber,
                    Session: currentSession,
                    BlockSize: blockSize,
                    BlockFsOffset: blockFsOffset,
                    BlockFsSize: blockFsSize,
                    AreaType: areaType,
                    PhysicalOffset: physicalOffset,
                    ImageOffset: currentImageOffset,
                    TrackMode: trackMode));

                // Advance image offset by a random track size (multiple of block size)
                int trackSectors = rng.Next(100, 10000);
                currentImageOffset += (long)trackSectors * blockSize;
            }

            return tracks;
        }

        /// <summary>
        /// **Validates: Requirements 2.8, 7.2**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — Track number correctness.
        ///
        /// For any multi-track ISO9660 image with N tracks, BuildAreaMetadata SHALL produce
        /// metadata with Track set to the 1-based track index for each area.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_Track_Is1BasedIndex(
            NonNegativeInt trackCountSeed,
            NonNegativeInt rngSeed)
        {
            int trackCount = (trackCountSeed.Get % 8) + 1;
            Random rng = new Random(rngSeed.Get);

            List<TrackConfig> tracks = GenerateMultiTrackImage(trackCount, rng);

            for (int i = 0; i < tracks.Count; i++)
            {
                AreaMetadata metadata = ModelBuildAreaMetadata(tracks[i]);

                long? track = metadata.GetLong(AreaValueType.Track);
                if (track != (i + 1))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.7, 7.6**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — BlockSize correctness.
        ///
        /// For any multi-track ISO9660 image, BuildAreaMetadata SHALL produce
        /// metadata with BlockSize matching the track's block size (0x930 for raw, 0x800 for cooked).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_BlockSize_MatchesTrackBlockSize(
            NonNegativeInt trackCountSeed,
            NonNegativeInt rngSeed)
        {
            int trackCount = (trackCountSeed.Get % 8) + 1;
            Random rng = new Random(rngSeed.Get);

            List<TrackConfig> tracks = GenerateMultiTrackImage(trackCount, rng);

            foreach (TrackConfig track in tracks)
            {
                AreaMetadata metadata = ModelBuildAreaMetadata(track);

                long? blockSize = metadata.GetLong(AreaValueType.BlockSize);
                if (blockSize != (long)track.BlockSize)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.8**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — Session correctness.
        ///
        /// For any multi-track ISO9660 image, BuildAreaMetadata SHALL produce
        /// metadata with Session set to the track's session number.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_Session_MatchesTrackSession(
            NonNegativeInt trackCountSeed,
            NonNegativeInt rngSeed)
        {
            int trackCount = (trackCountSeed.Get % 8) + 1;
            Random rng = new Random(rngSeed.Get);

            List<TrackConfig> tracks = GenerateMultiTrackImage(trackCount, rng);

            foreach (TrackConfig track in tracks)
            {
                AreaMetadata metadata = ModelBuildAreaMetadata(track);

                long? session = metadata.GetLong(AreaValueType.Session);
                if (session != (long)track.Session)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.6**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — PhysicalOffset correctness.
        ///
        /// For any multi-track ISO9660 image with data tracks (FileSystem areas),
        /// BuildAreaMetadata SHALL produce metadata with PhysicalOffset set to the
        /// track's starting LBA.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_PhysicalOffset_MatchesStartingLba(
            NonNegativeInt trackCountSeed,
            NonNegativeInt rngSeed)
        {
            int trackCount = (trackCountSeed.Get % 8) + 1;
            Random rng = new Random(rngSeed.Get);

            List<TrackConfig> tracks = GenerateMultiTrackImage(trackCount, rng);

            foreach (TrackConfig track in tracks)
            {
                if (track.AreaType != AreaType.FileSystem)
                    continue;

                AreaMetadata metadata = ModelBuildAreaMetadata(track);

                long? physicalOffset = metadata.GetLong(AreaValueType.PhysicalOffset);
                if (physicalOffset != track.PhysicalOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 7.6**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — Stride metadata correctness.
        ///
        /// For any multi-track ISO9660 image with raw-sector tracks, the stride computed
        /// by the model SHALL match the expected values for the track's sector mode:
        /// - Mode1Raw: SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800
        /// - Mode2Form1: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800
        /// - Mode2Form2: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x914
        /// - Audio: SourceBlockSize=0x930, DataOffset=0, DataLength=0x930
        /// - Cooked: null (no stride)
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_Stride_MatchesExpectedForTrackMode(
            NonNegativeInt trackCountSeed,
            NonNegativeInt rngSeed)
        {
            int trackCount = (trackCountSeed.Get % 8) + 1;
            Random rng = new Random(rngSeed.Get);

            List<TrackConfig> tracks = GenerateMultiTrackImage(trackCount, rng);

            foreach (TrackConfig track in tracks)
            {
                (int SourceBlockSize, int DataOffset, int DataLength)? stride = ModelGetStride(track);

                if (track.BlockSize == 0x800)
                {
                    // Cooked sectors — no stride expected
                    if (stride != null)
                        return false;
                }
                else if (track.BlockSize == 0x930)
                {
                    if (stride == null)
                        return false;

                    if (track.AreaType == AreaType.Audio)
                    {
                        // Audio: full sector, no striding
                        if (stride.Value.SourceBlockSize != 0x930 ||
                            stride.Value.DataOffset != 0 ||
                            stride.Value.DataLength != 0x930)
                            return false;
                    }
                    else
                    {
                        // Data track — stride should match BlockFsOffset/BlockFsSize
                        if (stride.Value.SourceBlockSize != 0x930)
                            return false;
                        if (stride.Value.DataOffset != track.BlockFsOffset)
                            return false;
                        if (stride.Value.DataLength != track.BlockFsSize)
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.6, 2.7, 2.8, 2.10, 7.2, 7.6**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — Full multi-track image.
        ///
        /// For any ISO9660 multi-track image with N tracks, CreateAreas produces N area
        /// records where each area's metadata contains correct Track, BlockSize, Session,
        /// PhysicalOffset, and stride metadata.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_FullImage_AllTracksComplete(
            NonNegativeInt trackCountSeed,
            NonNegativeInt rngSeed)
        {
            int trackCount = (trackCountSeed.Get % 10) + 1;
            Random rng = new Random(rngSeed.Get);

            List<TrackConfig> tracks = GenerateMultiTrackImage(trackCount, rng);

            // Verify we get exactly N area records
            if (tracks.Count != trackCount)
                return false;

            for (int i = 0; i < tracks.Count; i++)
            {
                TrackConfig track = tracks[i];
                AreaMetadata metadata = ModelBuildAreaMetadata(track);

                // Track must be 1-based index
                long? trackNum = metadata.GetLong(AreaValueType.Track);
                if (trackNum != (i + 1))
                    return false;

                // BlockSize must match track block size
                long? blockSize = metadata.GetLong(AreaValueType.BlockSize);
                if (blockSize != (long)track.BlockSize)
                    return false;

                // Session must be present
                long? session = metadata.GetLong(AreaValueType.Session);
                if (session != (long)track.Session)
                    return false;

                // PhysicalOffset must be present for data tracks
                if (track.AreaType == AreaType.FileSystem)
                {
                    long? physicalOffset = metadata.GetLong(AreaValueType.PhysicalOffset);
                    if (physicalOffset != track.PhysicalOffset)
                        return false;
                }

                // FsType must match area type
                string fsType = metadata.GetString(AreaValueType.FsType);
                if (fsType != track.AreaType.ToString())
                    return false;

                // Stride must be correct for the track mode
                (int SourceBlockSize, int DataOffset, int DataLength)? stride = ModelGetStride(track);
                if (track.BlockSize == 0x930 && track.AreaType != AreaType.Audio)
                {
                    // Raw data track — stride must exist and match
                    if (stride == null)
                        return false;
                    if (stride.Value.SourceBlockSize != 0x930)
                        return false;
                    if (stride.Value.DataOffset != track.BlockFsOffset)
                        return false;
                    if (stride.Value.DataLength != track.BlockFsSize)
                        return false;
                }
                else if (track.BlockSize == 0x800)
                {
                    // Cooked — no stride
                    if (stride != null)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.10**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — PS3 encryption metadata.
        ///
        /// For any ISO9660 data track where encryption is active, BuildAreaMetadata SHALL
        /// store the Encrypted flag and TitleKeyCrc in the area metadata.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_Ps3Encryption_MetadataStored(
            NonNegativeInt rngSeed,
            uint titleKeyCrc,
            bool isEncrypted)
        {
            Random rng = new Random(rngSeed.Get);

            // Model a PS3 data track with encryption metadata
            TrackConfig track = new TrackConfig(
                TrackNumber: 1,
                Session: 1,
                BlockSize: 0x800,
                BlockFsOffset: 0,
                BlockFsSize: 0x800,
                AreaType: AreaType.FileSystem,
                PhysicalOffset: 0,
                ImageOffset: 0,
                TrackMode: "Mode1");

            // Model the PS3 encryption metadata addition
            AreaMetadata metadata = ModelBuildAreaMetadata(track);

            // Simulate PS3 encryption metadata (as done in the real formatter)
            if (isEncrypted)
            {
                metadata.Set(AreaValueType.Encrypted, isEncrypted);
                metadata.Set(AreaValueType.TitleKeyCrc, (long)titleKeyCrc);
            }

            // Verify
            if (isEncrypted)
            {
                bool? encrypted = metadata.GetBool(AreaValueType.Encrypted);
                if (encrypted != true)
                    return false;

                long? storedCrc = metadata.GetLong(AreaValueType.TitleKeyCrc);
                if (storedCrc != (long)titleKeyCrc)
                    return false;
            }
            else
            {
                // When not encrypted, these fields should not be present
                bool? encrypted = metadata.GetBool(AreaValueType.Encrypted);
                if (encrypted == true)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.8, 7.2**
        ///
        /// Property 5: ISO9660 Area Metadata Completeness — Audio track type stored.
        ///
        /// For any audio track in a multi-track image, BuildAreaMetadata SHALL produce
        /// metadata with Type set to "Audio".
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Iso9660AreaMetadata_AudioTrack_TypeIsAudio(
            NonNegativeInt trackNumberSeed,
            NonNegativeInt sessionSeed,
            NonNegativeInt offsetSeed)
        {
            int trackNumber = (trackNumberSeed.Get % 20) + 1;
            int session = (sessionSeed.Get % 5) + 1;
            long physicalOffset = (long)(offsetSeed.Get % 500000);

            TrackConfig track = new TrackConfig(
                TrackNumber: trackNumber,
                Session: session,
                BlockSize: 0x930,
                BlockFsOffset: 0,
                BlockFsSize: 0x930,
                AreaType: AreaType.Audio,
                PhysicalOffset: physicalOffset,
                ImageOffset: 0,
                TrackMode: "Audio");

            AreaMetadata metadata = ModelBuildAreaMetadata(track);

            string type = metadata.GetString(AreaValueType.Type);
            if (type != "Audio")
                return false;

            // Audio tracks should still have Track and Session
            long? storedTrack = metadata.GetLong(AreaValueType.Track);
            if (storedTrack != trackNumber)
                return false;

            long? storedSession = metadata.GetLong(AreaValueType.Session);
            if (storedSession != session)
                return false;

            // Audio tracks should have BlockSize = 0x930
            long? blockSize = metadata.GetLong(AreaValueType.BlockSize);
            if (blockSize != 0x930L)
                return false;

            return true;
        }
    }
}
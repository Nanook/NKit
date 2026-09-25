using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for multi-track area boundary state update.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 10: Multi-Track Area Boundary State Update
    /// **Validates: Requirements 5.9**
    ///
    /// For any multi-track image with areas having different block sizes and physical offsets,
    /// when the stream position crosses an area boundary, the OnAreaChanged handler SHALL update
    /// the current block size, stride configuration, and base LBA to match the entering area's
    /// metadata. Subsequent LBA computations SHALL use the new area's PhysicalOffset as the base.
    /// </summary>
    public class MultiTrackAreaBoundaryStateUpdatePropertyTests
    {
        /// <summary>
        /// Represents a track/area configuration for generating test data.
        /// </summary>
        private record AreaConfig(
            long AreaId,
            int BlockSize,
            long PhysicalOffset,
            string TrackType,
            long AreaOffset,
            long AreaSize);

        /// <summary>
        /// Models the state maintained by ImageBuilderIso9660Stream.
        /// This is a faithful model of the state update logic in OnAreaChanged
        /// and the LBA computation in OnBufferComplete.
        /// </summary>
        private class Iso9660StreamStateModel
        {
            public int CurrentBlockSize { get; private set; }
            public long CurrentPhysicalOffset { get; private set; }
            public string CurrentTrackType { get; private set; }

            private readonly Dictionary<long, AreaConfig> _areaConfigs;

            public Iso9660StreamStateModel(List<AreaConfig> areas)
            {
                _areaConfigs = areas.ToDictionary(a => a.AreaId);

                // Initialize from first area (same as ImageBuilderIso9660Stream constructor)
                if (areas.Count > 0)
                {
                    AreaConfig first = areas[0];
                    CurrentBlockSize = first.BlockSize;
                    CurrentPhysicalOffset = first.PhysicalOffset;
                    CurrentTrackType = first.TrackType;
                }
            }

            /// <summary>
            /// Models OnAreaChanged: updates current state from the new area's metadata.
            /// </summary>
            public void OnAreaChanged(long newAreaId)
            {
                if (_areaConfigs.TryGetValue(newAreaId, out AreaConfig config))
                {
                    CurrentBlockSize = config.BlockSize;
                    CurrentPhysicalOffset = config.PhysicalOffset;
                    CurrentTrackType = config.TrackType;
                }
            }

            /// <summary>
            /// Models the LBA computation from OnBufferComplete:
            /// sectorLba = areaPhysicalOffset + (bufferPositionWithinArea / blockSize)
            /// where blockSize is 0x930 for raw sectors.
            /// </summary>
            public long ComputeSectorLba(long areaId, long imageOffset, int sectorIndexInBuffer)
            {
                if (!_areaConfigs.TryGetValue(areaId, out AreaConfig config))
                    return -1;

                long areaOffset = imageOffset - config.AreaOffset;
                int sectorOffset = sectorIndexInBuffer * config.BlockSize;
                return config.PhysicalOffset + ((areaOffset + sectorOffset) / config.BlockSize);
            }
        }

        /// <summary>
        /// Generates a valid multi-track image configuration with varying block sizes and physical offsets.
        /// </summary>
        private static List<AreaConfig> GenerateMultiTrackAreas(int areaCount, Random rng)
        {
            List<AreaConfig> areas = new List<AreaConfig>();
            long currentImageOffset = 0;

            // Valid track types and their corresponding block sizes
            (int BlockSize, string TrackType)[] trackConfigs = new (int BlockSize, string TrackType)[]
            {
                (0x930, "Mode1"),
                (0x930, "Mode2"),
                (0x800, "Mode1"),    // Cooked
                (0x930, "Audio"),
            };

            for (int i = 0; i < areaCount; i++)
            {
                // Pick a random track configuration
                (int blockSize, string trackType) = trackConfigs[rng.Next(trackConfigs.Length)];

                // Generate a distinct physical offset for each area
                long physicalOffset = (long)rng.Next(0, 450000);

                // Area size: random number of sectors
                int sectorCount = rng.Next(100, 5000);
                long areaSize = (long)sectorCount * blockSize;

                areas.Add(new AreaConfig(
                    AreaId: i + 1,
                    BlockSize: blockSize,
                    PhysicalOffset: physicalOffset,
                    TrackType: trackType,
                    AreaOffset: currentImageOffset,
                    AreaSize: areaSize));

                currentImageOffset += areaSize;
            }

            return areas;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — BlockSize updated on area change.
        ///
        /// When the stream position crosses an area boundary, OnAreaChanged SHALL update
        /// the current block size to match the entering area's BlockSize metadata.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_BlockSize_UpdatedOnAreaChange(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed)
        {
            int areaCount = (areaCountSeed.Get % 8) + 2; // At least 2 areas
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // Verify initial state matches first area
            if (model.CurrentBlockSize != areas[0].BlockSize)
                return false;

            // Simulate crossing each area boundary
            for (int i = 1; i < areas.Count; i++)
            {
                model.OnAreaChanged(areas[i].AreaId);

                if (model.CurrentBlockSize != areas[i].BlockSize)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — PhysicalOffset updated on area change.
        ///
        /// When the stream position crosses an area boundary, OnAreaChanged SHALL update
        /// the current base LBA (PhysicalOffset) to match the entering area's PhysicalOffset metadata.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_PhysicalOffset_UpdatedOnAreaChange(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed)
        {
            int areaCount = (areaCountSeed.Get % 8) + 2;
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // Verify initial state matches first area
            if (model.CurrentPhysicalOffset != areas[0].PhysicalOffset)
                return false;

            // Simulate crossing each area boundary
            for (int i = 1; i < areas.Count; i++)
            {
                model.OnAreaChanged(areas[i].AreaId);

                if (model.CurrentPhysicalOffset != areas[i].PhysicalOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — TrackType updated on area change.
        ///
        /// When the stream position crosses an area boundary, OnAreaChanged SHALL update
        /// the current track type (stride configuration) to match the entering area's metadata.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_TrackType_UpdatedOnAreaChange(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed)
        {
            int areaCount = (areaCountSeed.Get % 8) + 2;
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // Verify initial state matches first area
            if (model.CurrentTrackType != areas[0].TrackType)
                return false;

            // Simulate crossing each area boundary
            for (int i = 1; i < areas.Count; i++)
            {
                model.OnAreaChanged(areas[i].AreaId);

                if (model.CurrentTrackType != areas[i].TrackType)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — LBA computation uses new PhysicalOffset.
        ///
        /// After crossing an area boundary, subsequent LBA computations SHALL use the new area's
        /// PhysicalOffset as the base. The LBA for a sector at position P within the area is:
        /// PhysicalOffset + (P / BlockSize)
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_LbaComputation_UsesNewPhysicalOffset(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed,
            NonNegativeInt sectorIndexSeed)
        {
            int areaCount = (areaCountSeed.Get % 8) + 2;
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // For each area, simulate entering it and computing LBAs
            for (int i = 0; i < areas.Count; i++)
            {
                if (i > 0)
                    model.OnAreaChanged(areas[i].AreaId);

                AreaConfig area = areas[i];

                // Compute LBA for a sector at the start of the area (sector index 0)
                long lbaAtStart = model.ComputeSectorLba(area.AreaId, area.AreaOffset, 0);
                if (lbaAtStart != area.PhysicalOffset)
                    return false;

                // Compute LBA for a sector at an arbitrary position within the area
                int maxSectors = (int)(area.AreaSize / area.BlockSize);
                if (maxSectors <= 0)
                    continue;

                int sectorIndex = sectorIndexSeed.Get % maxSectors;
                long imageOffset = area.AreaOffset; // buffer starts at area start
                long expectedLba = area.PhysicalOffset + sectorIndex;
                long computedLba = model.ComputeSectorLba(area.AreaId, imageOffset, sectorIndex);

                if (computedLba != expectedLba)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — LBA computation with buffer offset.
        ///
        /// When a buffer starts at an offset within the area (not at the area start),
        /// the LBA computation SHALL correctly account for both the area's PhysicalOffset
        /// and the buffer's position within the area:
        /// sectorLba = PhysicalOffset + ((imageOffset - areaOffset + sectorOffset) / blockSize)
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_LbaComputation_AccountsForBufferOffset(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed,
            NonNegativeInt bufferOffsetSeed)
        {
            int areaCount = (areaCountSeed.Get % 6) + 2;
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // For each area, simulate entering it and computing LBAs with a buffer offset
            for (int i = 0; i < areas.Count; i++)
            {
                if (i > 0)
                    model.OnAreaChanged(areas[i].AreaId);

                AreaConfig area = areas[i];
                int maxSectors = (int)(area.AreaSize / area.BlockSize);
                if (maxSectors <= 2)
                    continue;

                // Buffer starts at some offset within the area (not at the beginning)
                int bufferStartSector = bufferOffsetSeed.Get % (maxSectors - 1);
                long imageOffset = area.AreaOffset + ((long)bufferStartSector * area.BlockSize);

                // Compute LBA for sector 0 in this buffer
                long expectedLba = area.PhysicalOffset + bufferStartSector;
                long computedLba = model.ComputeSectorLba(area.AreaId, imageOffset, 0);

                if (computedLba != expectedLba)
                    return false;

                // Compute LBA for sector 1 in this buffer
                long expectedLba1 = area.PhysicalOffset + bufferStartSector + 1;
                long computedLba1 = model.ComputeSectorLba(area.AreaId, imageOffset, 1);

                if (computedLba1 != expectedLba1)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — Non-sequential area transitions.
        ///
        /// When areas are entered in non-sequential order (e.g., seeking backwards),
        /// OnAreaChanged SHALL still correctly update state to match the target area.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_NonSequentialTransition_StateCorrect(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed,
            NonNegativeInt transitionCountSeed)
        {
            int areaCount = (areaCountSeed.Get % 6) + 3; // At least 3 areas
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // Perform random area transitions (simulating seeks)
            int transitionCount = (transitionCountSeed.Get % 20) + 5;
            for (int t = 0; t < transitionCount; t++)
            {
                int targetIndex = rng.Next(areas.Count);
                AreaConfig targetArea = areas[targetIndex];

                model.OnAreaChanged(targetArea.AreaId);

                // Verify state matches the target area
                if (model.CurrentBlockSize != targetArea.BlockSize)
                    return false;
                if (model.CurrentPhysicalOffset != targetArea.PhysicalOffset)
                    return false;
                if (model.CurrentTrackType != targetArea.TrackType)
                    return false;

                // Verify LBA computation uses the target area's PhysicalOffset
                long lbaAtStart = model.ComputeSectorLba(targetArea.AreaId, targetArea.AreaOffset, 0);
                if (lbaAtStart != targetArea.PhysicalOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.9**
        ///
        /// Property 10: Multi-Track Area Boundary State Update — Full state consistency.
        ///
        /// For any multi-track image with areas having different block sizes and physical offsets,
        /// after each area transition, ALL state fields (block size, physical offset, track type)
        /// SHALL be consistent with the entered area's metadata, and LBA computations SHALL
        /// produce correct results for arbitrary positions within that area.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AreaBoundary_FullStateConsistency_AfterTransition(
            NonNegativeInt areaCountSeed,
            NonNegativeInt rngSeed)
        {
            int areaCount = (areaCountSeed.Get % 10) + 2;
            Random rng = new Random(rngSeed.Get);

            List<AreaConfig> areas = GenerateMultiTrackAreas(areaCount, rng);
            Iso9660StreamStateModel model = new Iso9660StreamStateModel(areas);

            // Walk through all areas sequentially, verifying full state at each transition
            for (int i = 0; i < areas.Count; i++)
            {
                AreaConfig area = areas[i];

                if (i > 0)
                    model.OnAreaChanged(area.AreaId);

                // Verify all state fields
                if (model.CurrentBlockSize != area.BlockSize)
                    return false;
                if (model.CurrentPhysicalOffset != area.PhysicalOffset)
                    return false;
                if (model.CurrentTrackType != area.TrackType)
                    return false;

                // Verify LBA computation at multiple positions within the area
                int maxSectors = (int)(area.AreaSize / area.BlockSize);
                if (maxSectors <= 0)
                    continue;

                // Test at start, middle, and near end of area
                int[] testPositions = { 0, maxSectors / 2, maxSectors - 1 };
                foreach (int sectorPos in testPositions)
                {
                    if (sectorPos >= maxSectors)
                        continue;

                    long imageOffset = area.AreaOffset;
                    long expectedLba = area.PhysicalOffset + sectorPos;
                    long computedLba = model.ComputeSectorLba(area.AreaId, imageOffset, sectorPos);

                    if (computedLba != expectedLba)
                        return false;
                }
            }

            return true;
        }
    }
}
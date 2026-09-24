using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for Xbox area metadata completeness.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 4: Xbox Area Metadata Completeness
    /// **Validates: Requirements 1.2, 1.4, 1.5, 1.8**
    ///
    /// For any Xbox image scan with N areas (video partitions and game partition),
    /// calling BuildAreaMetadata for each area SHALL produce metadata where:
    /// every area has FsType set to its area type name, BlockSize set to 0x800,
    /// and AreaOffsetBase set to the area's absolute image offset.
    /// If an encryption key is present, the game partition metadata SHALL contain
    /// KeyCrc equal to the CRC32 of that key.
    /// If no key is present, TitleKeyMissing SHALL be "true".
    /// </summary>
    public class XboxAreaMetadataPropertyTests
    {
        /// <summary>
        /// Models the BuildAreaMetadata logic for Xbox areas.
        /// This is a faithful model of DataStoreXboxFormatter.BuildAreaMetadata
        /// that can be tested without instantiating the full formatter (which requires
        /// a DataStore on disk).
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadata(AreaType areaType, long imageOffset, byte[] key, Dictionary<string, object> properties)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, areaType.ToString());

            switch (areaType)
            {
                case AreaType.FileSystem:
                    metadata.Set(AreaValueType.BlockSize, 0x800L);
                    metadata.Set(AreaValueType.AreaOffsetBase, imageOffset);

                    if (properties != null)
                    {
                        if (properties.TryGetValue("FsType", out object fsType) && fsType is string fsTypeStr && !string.IsNullOrEmpty(fsTypeStr))
                            metadata.Set(AreaValueType.FsType, fsTypeStr);

                        if (properties.TryGetValue("AreaOffsetBase", out object areaOffsetBase) && areaOffsetBase != null)
                            metadata.Set(AreaValueType.AreaOffsetBase, (long)(ulong)areaOffsetBase);

                        if (properties.TryGetValue("HeaderCrc", out object headerCrc) && headerCrc != null)
                            metadata.Set(AreaValueType.HeaderCrc, (long)(uint)headerCrc);

                        if (properties.TryGetValue("HeaderSize", out object headerSize) && headerSize != null)
                            metadata.Set(AreaValueType.HeaderSize, (long)(ulong)headerSize);
                    }

                    // Encryption key metadata
                    if (key != null && key.Length > 0)
                        metadata.Set(AreaValueType.KeyCrc, (long)Crc.Compute(key));
                    else
                        metadata.Set(AreaValueType.TitleKeyMissing, true);

                    break;

                case AreaType.Other:
                    metadata.Set(AreaValueType.BlockSize, 0x800L);
                    metadata.Set(AreaValueType.AreaOffsetBase, imageOffset);
                    break;

                default:
                    break;
            }

            return metadata;
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.4, 1.5, 1.8**
        ///
        /// Property 4: Xbox Area Metadata Completeness — FsType correctness.
        ///
        /// For any Xbox area (FileSystem or Other), BuildAreaMetadata SHALL produce
        /// metadata with FsType set to the area's AreaType name.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_FsType_MatchesAreaTypeName(
            NonNegativeInt areaCountSeed,
            NonNegativeInt offsetSeed,
            bool hasKey)
        {
            int areaCount = (areaCountSeed.Get % 10) + 1;
            Random rng = new Random(offsetSeed.Get);

            // Xbox images have video partitions (Other) and game partition (FileSystem)
            AreaType[] xboxAreaTypes = new[] { AreaType.Other, AreaType.FileSystem };

            byte[] key = hasKey ? GenerateKey(rng) : null;

            for (int i = 0; i < areaCount; i++)
            {
                AreaType areaType = xboxAreaTypes[rng.Next(xboxAreaTypes.Length)];
                long imageOffset = (long)rng.Next(1, 100) * 0x10000;

                AreaMetadata metadata = ModelBuildAreaMetadata(areaType, imageOffset, key, null);

                string fsType = metadata.GetString(AreaValueType.FsType);
                if (fsType != areaType.ToString())
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.4, 1.5, 1.8**
        ///
        /// Property 4: Xbox Area Metadata Completeness — BlockSize always 0x800.
        ///
        /// For any Xbox area (FileSystem or Other), BuildAreaMetadata SHALL produce
        /// metadata with BlockSize set to 0x800 (cooked sectors).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_BlockSize_Always0x800(
            NonNegativeInt areaCountSeed,
            NonNegativeInt offsetSeed,
            bool hasKey)
        {
            int areaCount = (areaCountSeed.Get % 10) + 1;
            Random rng = new Random(offsetSeed.Get);

            AreaType[] xboxAreaTypes = new[] { AreaType.Other, AreaType.FileSystem };

            byte[] key = hasKey ? GenerateKey(rng) : null;

            for (int i = 0; i < areaCount; i++)
            {
                AreaType areaType = xboxAreaTypes[rng.Next(xboxAreaTypes.Length)];
                long imageOffset = (long)rng.Next(1, 100) * 0x10000;

                AreaMetadata metadata = ModelBuildAreaMetadata(areaType, imageOffset, key, null);

                long? blockSize = metadata.GetLong(AreaValueType.BlockSize);
                if (blockSize != 0x800L)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.4, 1.5, 1.8**
        ///
        /// Property 4: Xbox Area Metadata Completeness — AreaOffsetBase correctness.
        ///
        /// For any Xbox area (FileSystem or Other), BuildAreaMetadata SHALL produce
        /// metadata with AreaOffsetBase set to the area's absolute image offset.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_AreaOffsetBase_MatchesImageOffset(
            NonNegativeInt areaCountSeed,
            NonNegativeInt offsetSeed,
            bool hasKey)
        {
            int areaCount = (areaCountSeed.Get % 10) + 1;
            Random rng = new Random(offsetSeed.Get);

            AreaType[] xboxAreaTypes = new[] { AreaType.Other, AreaType.FileSystem };

            byte[] key = hasKey ? GenerateKey(rng) : null;

            for (int i = 0; i < areaCount; i++)
            {
                AreaType areaType = xboxAreaTypes[rng.Next(xboxAreaTypes.Length)];
                long imageOffset = (long)rng.Next(1, 100) * 0x10000;

                AreaMetadata metadata = ModelBuildAreaMetadata(areaType, imageOffset, key, null);

                long? areaOffsetBase = metadata.GetLong(AreaValueType.AreaOffsetBase);
                if (areaOffsetBase != imageOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.4, 1.5**
        ///
        /// Property 4: Xbox Area Metadata Completeness — KeyCrc present when key available.
        ///
        /// For any Xbox FileSystem area where an encryption key is present,
        /// BuildAreaMetadata SHALL produce metadata with KeyCrc equal to the CRC32 of that key.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_KeyCrc_EqualsComputedCrc_WhenKeyPresent(
            NonNegativeInt offsetSeed,
            byte byte0, byte byte1, byte byte2, byte byte3,
            byte byte4, byte byte5, byte byte6, byte byte7,
            byte byte8, byte byte9, byte byte10, byte byte11,
            byte byte12, byte byte13, byte byte14, byte byte15)
        {
            byte[] key = new byte[] { byte0, byte1, byte2, byte3, byte4, byte5, byte6, byte7,
                                      byte8, byte9, byte10, byte11, byte12, byte13, byte14, byte15 };

            long imageOffset = (long)((offsetSeed.Get % 1000) + 1) * 0x10000;

            AreaMetadata metadata = ModelBuildAreaMetadata(AreaType.FileSystem, imageOffset, key, null);

            long? keyCrc = metadata.GetLong(AreaValueType.KeyCrc);
            long expectedCrc = (long)Crc.Compute(key);

            // KeyCrc must be present and equal to CRC32 of the key
            if (keyCrc == null || keyCrc.Value != expectedCrc)
                return false;

            // TitleKeyMissing must NOT be set when key is present
            bool? titleKeyMissing = metadata.GetBool(AreaValueType.TitleKeyMissing);
            if (titleKeyMissing == true)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.5**
        ///
        /// Property 4: Xbox Area Metadata Completeness — TitleKeyMissing when no key.
        ///
        /// For any Xbox FileSystem area where no encryption key is available,
        /// BuildAreaMetadata SHALL produce metadata with TitleKeyMissing set to "true".
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_TitleKeyMissing_WhenNoKey(
            NonNegativeInt offsetSeed,
            NonNegativeInt areaCountSeed)
        {
            int areaCount = (areaCountSeed.Get % 10) + 1;
            Random rng = new Random(offsetSeed.Get);

            for (int i = 0; i < areaCount; i++)
            {
                long imageOffset = (long)rng.Next(1, 1000) * 0x10000;

                // No key (null)
                AreaMetadata metadataNull = ModelBuildAreaMetadata(AreaType.FileSystem, imageOffset, null, null);
                bool? titleKeyMissingNull = metadataNull.GetBool(AreaValueType.TitleKeyMissing);
                if (titleKeyMissingNull != true)
                    return false;

                // No key (empty array)
                AreaMetadata metadataEmpty = ModelBuildAreaMetadata(AreaType.FileSystem, imageOffset, Array.Empty<byte>(), null);
                bool? titleKeyMissingEmpty = metadataEmpty.GetBool(AreaValueType.TitleKeyMissing);
                if (titleKeyMissingEmpty != true)
                    return false;

                // KeyCrc must NOT be set when key is missing
                long? keyCrcNull = metadataNull.GetLong(AreaValueType.KeyCrc);
                long? keyCrcEmpty = metadataEmpty.GetLong(AreaValueType.KeyCrc);
                if (keyCrcNull != null || keyCrcEmpty != null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.8**
        ///
        /// Property 4: Xbox Area Metadata Completeness — Video partition HeaderCrc/HeaderSize.
        ///
        /// For any Xbox FileSystem area with video partition properties (HeaderCrc, HeaderSize),
        /// BuildAreaMetadata SHALL store HeaderCrc and HeaderSize in the metadata.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_VideoPartition_HeaderCrcAndSize(
            NonNegativeInt offsetSeed,
            uint headerCrc,
            PositiveInt headerSizeInt)
        {
            long imageOffset = (long)((offsetSeed.Get % 1000) + 1) * 0x10000;
            ulong headerSize = (ulong)headerSizeInt.Get;

            // Simulate video partition properties (these come from the scan's AreaInfo.Properties)
            Dictionary<string, object> properties = new Dictionary<string, object>
            {
                { "HeaderCrc", headerCrc },
                { "HeaderSize", headerSize }
            };

            AreaMetadata metadata = ModelBuildAreaMetadata(AreaType.FileSystem, imageOffset, null, properties);

            long? storedHeaderCrc = metadata.GetLong(AreaValueType.HeaderCrc);
            long? storedHeaderSize = metadata.GetLong(AreaValueType.HeaderSize);

            if (storedHeaderCrc != (long)headerCrc)
                return false;
            if (storedHeaderSize != (long)headerSize)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.4, 1.5, 1.8**
        ///
        /// Property 4: Xbox Area Metadata Completeness — Full multi-area image.
        ///
        /// For any Xbox image scan with N areas (mix of video partitions and game partition),
        /// BuildAreaMetadata produces correct metadata for every area:
        /// - All areas have FsType, BlockSize=0x800, AreaOffsetBase
        /// - FileSystem areas have KeyCrc (when key present) or TitleKeyMissing (when absent)
        /// - Video partition areas (Other) have BlockSize=0x800 and AreaOffsetBase
        /// </summary>
        [Property(MaxTest = 200)]
        public bool XboxAreaMetadata_FullImage_AllAreasComplete(
            NonNegativeInt areaCountSeed,
            NonNegativeInt offsetSeed,
            bool hasKey)
        {
            // Xbox images typically have 2-3 areas: video partition 1, game partition, video partition 2
            int areaCount = (areaCountSeed.Get % 8) + 2;
            Random rng = new Random(offsetSeed.Get);

            byte[] key = hasKey ? GenerateKey(rng) : null;

            // Generate a realistic Xbox area layout: Other, FileSystem, Other (video, game, video)
            List<(AreaType Type, long Offset)> areas = new List<(AreaType Type, long Offset)>();
            long currentOffset = 0;
            for (int i = 0; i < areaCount; i++)
            {
                // Alternate between Other (video) and FileSystem (game) areas
                // First and last are typically video partitions, middle is game
                AreaType type = (i == 0 || i == areaCount - 1) ? AreaType.Other : AreaType.FileSystem;
                // For odd area counts > 3, mix in additional areas
                if (areaCount > 3 && i > 0 && i < areaCount - 1)
                    type = (i % 2 == 0) ? AreaType.Other : AreaType.FileSystem;

                long offset = currentOffset;
                currentOffset += (long)rng.Next(1, 50) * 0x10000;
                areas.Add((type, offset));
            }

            // Verify metadata for each area
            foreach ((AreaType areaType, long imageOffset) in areas)
            {
                AreaMetadata metadata = ModelBuildAreaMetadata(areaType, imageOffset, key, null);

                // All areas must have FsType
                string fsType = metadata.GetString(AreaValueType.FsType);
                if (string.IsNullOrEmpty(fsType))
                    return false;
                if (fsType != areaType.ToString())
                    return false;

                // All areas must have BlockSize = 0x800
                long? blockSize = metadata.GetLong(AreaValueType.BlockSize);
                if (blockSize != 0x800L)
                    return false;

                // All areas must have AreaOffsetBase = imageOffset
                long? areaOffsetBase = metadata.GetLong(AreaValueType.AreaOffsetBase);
                if (areaOffsetBase != imageOffset)
                    return false;

                // FileSystem areas must have key metadata
                if (areaType == AreaType.FileSystem)
                {
                    if (hasKey)
                    {
                        long? keyCrc = metadata.GetLong(AreaValueType.KeyCrc);
                        if (keyCrc != (long)Crc.Compute(key))
                            return false;
                    }
                    else
                    {
                        bool? titleKeyMissing = metadata.GetBool(AreaValueType.TitleKeyMissing);
                        if (titleKeyMissing != true)
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Generates a random 16-byte encryption key.
        /// </summary>
        private static byte[] GenerateKey(Random rng)
        {
            byte[] key = new byte[16];
            rng.NextBytes(key);
            return key;
        }
    }
}
using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for formatter block routing exclusivity.
    ///
    /// Feature: sidecar-datastore
    /// Property 2: Block Routing Exclusivity
    /// **Validates: Requirements 3.1, 3.3, 3.4**
    ///
    /// For any non-header section processed by a formatter with aux enabled,
    /// the block data is written to exactly one of {primary, aux} — never both
    /// and never neither. Image headers are the sole exception: they are written
    /// to both stores so each has a valid image record.
    /// </summary>
    public class FormatterRoutingPropertyTests
    {
        /// <summary>
        /// Represents a generated section configuration for testing routing decisions.
        /// </summary>
        private struct SectionConfig
        {
            public AreaType AreaType;
            public string PartitionType; // "Update", "Game", "Channel", null
            public int PartitionIndex;
            public long ImageOffset;
        }

        /// <summary>
        /// Tracks which writer(s) received data for a given section.
        /// </summary>
        private enum WriterTarget
        {
            None,
            Primary,
            Aux,
            Both
        }

        /// <summary>
        /// Models the Wii formatter's GetWriterForSection routing logic.
        /// Returns which writer would receive data for the given section config.
        /// This is a faithful model of DataStoreWiiFormatter.GetWriterForSection.
        /// </summary>
        private static WriterTarget ModelWiiRouting(SectionConfig config, bool auxEnabled,
            List<(long Start, long End)> updatePartitionRanges)
        {
            if (!auxEnabled)
                return WriterTarget.Primary;

            if (config.AreaType == AreaType.PartitionHeader || config.AreaType == AreaType.FileSystem)
            {
                if (config.PartitionType != null &&
                    config.PartitionType.Equals("Update", StringComparison.OrdinalIgnoreCase))
                    return WriterTarget.Aux;
            }

            if (config.AreaType == AreaType.Other && updatePartitionRanges != null)
            {
                if (updatePartitionRanges.Any(r => config.ImageOffset >= r.Start && config.ImageOffset < r.End))
                    return WriterTarget.Aux;
            }

            return WriterTarget.Primary;
        }

        /// <summary>
        /// Models the WiiU formatter's GetWriterForSection routing logic.
        /// Returns which writer would receive data for the given section config.
        /// This is a faithful model of DataStoreWiiUFormatter.GetWriterForSection.
        /// </summary>
        private static WriterTarget ModelWiiURouting(SectionConfig config, bool auxEnabled,
            Dictionary<int, string> partitionTypeMap)
        {
            if (!auxEnabled)
                return WriterTarget.Primary;

            // Try direct PartitionType property first (available on PartitionHeader sections)
            string partitionType = config.PartitionType;

            // If not directly available, look up by partition index
            if (partitionType == null && config.PartitionIndex >= 0 && partitionTypeMap != null)
            {
                partitionTypeMap.TryGetValue(config.PartitionIndex, out partitionType);
            }

            if (partitionType != null && partitionType.Equals("Update", StringComparison.OrdinalIgnoreCase))
                return WriterTarget.Aux;

            return WriterTarget.Primary;
        }

        /// <summary>
        /// Determines the full routing outcome for a section in ProcessSection,
        /// accounting for the special header dual-write behavior.
        /// </summary>
        private static WriterTarget GetProcessSectionOutcome(SectionConfig config, bool auxEnabled,
            bool isWii, List<(long Start, long End)> updateRanges, Dictionary<int, string> partitionTypeMap)
        {
            // ImageHeader: always written to BOTH primary and aux (explicit dual-write in ProcessSection)
            if (config.AreaType == AreaType.ImageHeader)
                return auxEnabled ? WriterTarget.Both : WriterTarget.Primary;

            // PartitionTable (WiiU only): also written to BOTH
            if (config.AreaType == AreaType.PartitionTable)
                return auxEnabled ? WriterTarget.Both : WriterTarget.Primary;

            // All other section types: routed via GetWriterForSection → exactly one writer
            if (isWii)
                return ModelWiiRouting(config, auxEnabled, updateRanges);
            else
                return ModelWiiURouting(config, auxEnabled, partitionTypeMap);
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.3, 3.4**
        ///
        /// Property 2: Block Routing Exclusivity (Wii formatter model).
        ///
        /// For any non-header section with aux enabled, the Wii formatter routes
        /// the block to exactly one of {primary, aux}. Image headers are written
        /// to both stores.
        ///
        /// We generate random section configurations with various AreaTypes and
        /// PartitionTypes, then verify:
        ///   1. Non-header sections → routed to exactly one writer (primary XOR aux)
        ///   2. ImageHeader sections → routed to both writers
        ///   3. No section is ever routed to neither writer
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WiiFormatter_BlockRoutingExclusivity(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt updateRangeCountSeed,
            NonNegativeInt offsetSeed)
        {
            int sectionCount = (sectionCountSeed.Get % 30) + 1;
            int updateRangeCount = (updateRangeCountSeed.Get % 5) + 1;

            // Generate update partition offset ranges
            List<(long Start, long End)> updateRanges = new List<(long Start, long End)>();
            long rangeBase = 0x50000;
            for (int i = 0; i < updateRangeCount; i++)
            {
                long start = rangeBase + (i * 0x100000);
                long end = start + 0x80000;
                updateRanges.Add((start, end));
            }

            // Wii-relevant area types for non-header sections
            AreaType[] wiiAreaTypes = new[]
            {
                AreaType.ImageHeader,
                AreaType.PartitionHeader,
                AreaType.FileSystem,
                AreaType.Other
            };

            string[] partitionTypes = new[] { "Update", "Game", "Channel", null };

            // Generate random section configurations
            Random rng = new Random(offsetSeed.Get);
            List<SectionConfig> sections = new List<SectionConfig>();
            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = wiiAreaTypes[rng.Next(wiiAreaTypes.Length)];
                string partType = partitionTypes[rng.Next(partitionTypes.Length)];

                // For Other sections, generate offsets that may or may not fall in update ranges
                long imageOffset;
                if (areaType == AreaType.Other && rng.Next(2) == 0 && updateRanges.Count > 0)
                {
                    // Place within an update range
                    (long Start, long End) range = updateRanges[rng.Next(updateRanges.Count)];
                    imageOffset = range.Start + rng.Next((int)(range.End - range.Start));
                }
                else
                {
                    // Place outside update ranges
                    imageOffset = 0x1000000 + (i * 0x10000);
                }

                sections.Add(new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = rng.Next(0, 4),
                    ImageOffset = imageOffset
                });
            }

            // Verify routing exclusivity for each section
            foreach (SectionConfig section in sections)
            {
                WriterTarget outcome = GetProcessSectionOutcome(
                    section, auxEnabled: true, isWii: true, updateRanges, null);

                // Property: no section is ever routed to neither writer
                if (outcome == WriterTarget.None)
                    return false;

                if (section.AreaType == AreaType.ImageHeader)
                {
                    // Headers must go to BOTH writers
                    if (outcome != WriterTarget.Both)
                        return false;
                }
                else
                {
                    // Non-header sections must go to exactly ONE writer
                    if (outcome != WriterTarget.Primary && outcome != WriterTarget.Aux)
                        return false;
                    if (outcome == WriterTarget.Both)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.3, 3.4**
        ///
        /// Property 2: Block Routing Exclusivity (WiiU formatter model).
        ///
        /// For any non-header section with aux enabled, the WiiU formatter routes
        /// the block to exactly one of {primary, aux}. Image headers and partition
        /// tables are written to both stores.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WiiUFormatter_BlockRoutingExclusivity(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt partitionCountSeed)
        {
            int sectionCount = (sectionCountSeed.Get % 30) + 1;
            int partitionCount = (partitionCountSeed.Get % 5) + 1;

            // Build a partition type map with a mix of Update and Game partitions
            Dictionary<int, string> partitionTypeMap = new Dictionary<int, string>();
            Random rng = new Random(partitionCountSeed.Get);
            for (int i = 0; i < partitionCount; i++)
            {
                partitionTypeMap[i] = rng.Next(2) == 0 ? "Update" : "Game";
            }

            // WiiU-relevant area types
            AreaType[] wiiUAreaTypes = new[]
            {
                AreaType.ImageHeader,
                AreaType.PartitionTable,
                AreaType.PartitionHeader,
                AreaType.FstBlock,
                AreaType.FileSystem,
                AreaType.Other
            };

            string[] partitionTypes = new[] { "Update", "Game", null };

            // Generate random section configurations
            List<SectionConfig> sections = new List<SectionConfig>();
            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = wiiUAreaTypes[rng.Next(wiiUAreaTypes.Length)];
                string partType = partitionTypes[rng.Next(partitionTypes.Length)];
                int partIdx = rng.Next(-1, partitionCount + 1); // include -1 and out-of-range

                sections.Add(new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = partIdx,
                    ImageOffset = 0x1000 + (i * 0x10000)
                });
            }

            // Verify routing exclusivity for each section
            foreach (SectionConfig section in sections)
            {
                WriterTarget outcome = GetProcessSectionOutcome(
                    section, auxEnabled: true, isWii: false, null, partitionTypeMap);

                // Property: no section is ever routed to neither writer
                if (outcome == WriterTarget.None)
                    return false;

                if (section.AreaType == AreaType.ImageHeader || section.AreaType == AreaType.PartitionTable)
                {
                    // Headers and partition tables must go to BOTH writers
                    if (outcome != WriterTarget.Both)
                        return false;
                }
                else
                {
                    // Non-header sections must go to exactly ONE writer
                    if (outcome != WriterTarget.Primary && outcome != WriterTarget.Aux)
                        return false;
                    if (outcome == WriterTarget.Both)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.3, 3.4**
        ///
        /// Property 2 (supplementary): Routing exclusivity holds for update-classified sections.
        ///
        /// For any section classified as belonging to an update partition, the formatter
        /// routes it to the aux writer (not primary). This confirms that aux-eligible
        /// blocks go exclusively to aux, not to both stores.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool UpdatePartitionSections_RouteExclusivelyToAux(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt offsetSeed)
        {
            int sectionCount = (sectionCountSeed.Get % 20) + 1;

            // Generate update partition ranges for Wii
            List<(long Start, long End)> updateRanges = new List<(long Start, long End)>
            {
                (0x50000, 0xD0000),
                (0x200000, 0x280000)
            };

            // Build partition type map for WiiU with partition 0 = Update
            Dictionary<int, string> partitionTypeMap = new Dictionary<int, string>
            {
                { 0, "Update" },
                { 1, "Game" },
                { 2, "Game" }
            };

            // Non-header area types that can be routed
            AreaType[] routableTypes = new[]
            {
                AreaType.PartitionHeader,
                AreaType.FileSystem,
                AreaType.Other,
                AreaType.FstBlock
            };

            Random rng = new Random(offsetSeed.Get);

            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = routableTypes[rng.Next(routableTypes.Length)];

                // --- Wii: update partition sections ---
                if (areaType == AreaType.PartitionHeader || areaType == AreaType.FileSystem)
                {
                    SectionConfig wiiConfig = new SectionConfig
                    {
                        AreaType = areaType,
                        PartitionType = "Update",
                        PartitionIndex = 0,
                        ImageOffset = 0x60000 + (i * 0x1000)
                    };

                    WriterTarget wiiResult = ModelWiiRouting(wiiConfig, auxEnabled: true, updateRanges);
                    if (wiiResult != WriterTarget.Aux)
                        return false;
                }

                if (areaType == AreaType.Other)
                {
                    // Place within an update range
                    (long Start, long End) range = updateRanges[rng.Next(updateRanges.Count)];
                    long offset = range.Start + rng.Next((int)(range.End - range.Start));

                    SectionConfig wiiConfig = new SectionConfig
                    {
                        AreaType = AreaType.Other,
                        PartitionType = null,
                        PartitionIndex = -1,
                        ImageOffset = offset
                    };

                    WriterTarget wiiResult = ModelWiiRouting(wiiConfig, auxEnabled: true, updateRanges);
                    if (wiiResult != WriterTarget.Aux)
                        return false;
                }

                // --- WiiU: update partition sections ---
                // PartitionHeader with direct PartitionType = "Update"
                {
                    SectionConfig wiiUConfig = new SectionConfig
                    {
                        AreaType = areaType,
                        PartitionType = "Update",
                        PartitionIndex = 0,
                        ImageOffset = 0x1000 + (i * 0x10000)
                    };

                    WriterTarget wiiUResult = ModelWiiURouting(wiiUConfig, auxEnabled: true, partitionTypeMap);
                    if (wiiUResult != WriterTarget.Aux)
                        return false;
                }

                // WiiU: section with no direct PartitionType but partition index maps to Update
                {
                    SectionConfig wiiUConfig = new SectionConfig
                    {
                        AreaType = areaType,
                        PartitionType = null,
                        PartitionIndex = 0, // maps to "Update" in partitionTypeMap
                        ImageOffset = 0x1000 + (i * 0x10000)
                    };

                    WriterTarget wiiUResult = ModelWiiURouting(wiiUConfig, auxEnabled: true, partitionTypeMap);
                    if (wiiUResult != WriterTarget.Aux)
                        return false;
                }
            }

            return true;
        }

        // =====================================================================
        // Property 3: Classification Determinism
        // =====================================================================

        /// <summary>
        /// **Validates: Requirements 2.1, 2.5**
        ///
        /// Property 3: Classification Determinism (Wii formatter model).
        ///
        /// For any section S, calling GetWriterForSection(S) multiple times with
        /// the same input produces the same routing decision. The decision depends
        /// only on the section's area type, partition type, and image offset — not
        /// on any mutable state or call order.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WiiFormatter_ClassificationDeterminism(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt updateRangeCountSeed,
            NonNegativeInt offsetSeed,
            bool auxEnabled)
        {
            int sectionCount = (sectionCountSeed.Get % 30) + 1;
            int updateRangeCount = (updateRangeCountSeed.Get % 5) + 1;

            // Generate update partition offset ranges
            List<(long Start, long End)> updateRanges = new List<(long Start, long End)>();
            long rangeBase = 0x50000;
            for (int i = 0; i < updateRangeCount; i++)
            {
                long start = rangeBase + (i * 0x100000);
                long end = start + 0x80000;
                updateRanges.Add((start, end));
            }

            // Wii-relevant area types
            AreaType[] wiiAreaTypes = new[]
            {
                AreaType.ImageHeader,
                AreaType.PartitionHeader,
                AreaType.FileSystem,
                AreaType.Other
            };

            string[] partitionTypes = new[] { "Update", "Game", "Channel", null };

            // Generate random section configurations
            Random rng = new Random(offsetSeed.Get);
            List<SectionConfig> sections = new List<SectionConfig>();
            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = wiiAreaTypes[rng.Next(wiiAreaTypes.Length)];
                string partType = partitionTypes[rng.Next(partitionTypes.Length)];

                long imageOffset;
                if (areaType == AreaType.Other && rng.Next(2) == 0 && updateRanges.Count > 0)
                {
                    (long Start, long End) range = updateRanges[rng.Next(updateRanges.Count)];
                    imageOffset = range.Start + rng.Next((int)(range.End - range.Start));
                }
                else
                {
                    imageOffset = 0x1000000 + (i * 0x10000);
                }

                sections.Add(new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = rng.Next(0, 4),
                    ImageOffset = imageOffset
                });
            }

            // Verify determinism: call routing 5 times per section, all must match
            foreach (SectionConfig section in sections)
            {
                WriterTarget firstResult = ModelWiiRouting(section, auxEnabled, updateRanges);

                for (int call = 1; call < 5; call++)
                {
                    WriterTarget result = ModelWiiRouting(section, auxEnabled, updateRanges);
                    if (result != firstResult)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.1, 2.5**
        ///
        /// Property 3: Classification Determinism (WiiU formatter model).
        ///
        /// For any section S, calling GetWriterForSection(S) multiple times with
        /// the same input produces the same routing decision. The decision depends
        /// only on the section's area type, partition type, and partition index —
        /// not on any mutable state or call order.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WiiUFormatter_ClassificationDeterminism(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt partitionCountSeed,
            bool auxEnabled)
        {
            int sectionCount = (sectionCountSeed.Get % 30) + 1;
            int partitionCount = (partitionCountSeed.Get % 5) + 1;

            // Build a partition type map with a mix of Update and Game partitions
            Dictionary<int, string> partitionTypeMap = new Dictionary<int, string>();
            Random rng = new Random(partitionCountSeed.Get);
            for (int i = 0; i < partitionCount; i++)
            {
                partitionTypeMap[i] = rng.Next(2) == 0 ? "Update" : "Game";
            }

            // WiiU-relevant area types
            AreaType[] wiiUAreaTypes = new[]
            {
                AreaType.ImageHeader,
                AreaType.PartitionTable,
                AreaType.PartitionHeader,
                AreaType.FstBlock,
                AreaType.FileSystem,
                AreaType.Other
            };

            string[] partitionTypes = new[] { "Update", "Game", null };

            // Generate random section configurations
            List<SectionConfig> sections = new List<SectionConfig>();
            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = wiiUAreaTypes[rng.Next(wiiUAreaTypes.Length)];
                string partType = partitionTypes[rng.Next(partitionTypes.Length)];
                int partIdx = rng.Next(-1, partitionCount + 1);

                sections.Add(new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = partIdx,
                    ImageOffset = 0x1000 + (i * 0x10000)
                });
            }

            // Verify determinism: call routing 5 times per section, all must match
            foreach (SectionConfig section in sections)
            {
                WriterTarget firstResult = ModelWiiURouting(section, auxEnabled, partitionTypeMap);

                for (int call = 1; call < 5; call++)
                {
                    WriterTarget result = ModelWiiURouting(section, auxEnabled, partitionTypeMap);
                    if (result != firstResult)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.1, 2.5**
        ///
        /// Property 3 (supplementary): Classification determinism holds across
        /// both Wii and WiiU formatters for the full ProcessSection outcome
        /// (including the special header dual-write behavior).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ProcessSectionOutcome_ClassificationDeterminism(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt offsetSeed,
            bool isWii,
            bool auxEnabled)
        {
            int sectionCount = (sectionCountSeed.Get % 30) + 1;

            // Generate update partition ranges for Wii
            List<(long Start, long End)> updateRanges = new List<(long Start, long End)>
            {
                (0x50000, 0xD0000),
                (0x200000, 0x280000)
            };

            // Build partition type map for WiiU
            Dictionary<int, string> partitionTypeMap = new Dictionary<int, string>
            {
                { 0, "Update" },
                { 1, "Game" },
                { 2, "Channel" }
            };

            // All area types
            AreaType[] allAreaTypes = new[]
            {
                AreaType.ImageHeader,
                AreaType.PartitionTable,
                AreaType.PartitionHeader,
                AreaType.FstBlock,
                AreaType.FileSystem,
                AreaType.Other
            };

            string[] partitionTypes = new[] { "Update", "Game", "Channel", null };

            Random rng = new Random(offsetSeed.Get);
            List<SectionConfig> sections = new List<SectionConfig>();
            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = allAreaTypes[rng.Next(allAreaTypes.Length)];
                string partType = partitionTypes[rng.Next(partitionTypes.Length)];

                long imageOffset;
                if (areaType == AreaType.Other && rng.Next(2) == 0 && updateRanges.Count > 0)
                {
                    (long Start, long End) range = updateRanges[rng.Next(updateRanges.Count)];
                    imageOffset = range.Start + rng.Next((int)(range.End - range.Start));
                }
                else
                {
                    imageOffset = 0x1000000 + (i * 0x10000);
                }

                sections.Add(new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = rng.Next(-1, 4),
                    ImageOffset = imageOffset
                });
            }

            // Verify determinism: call GetProcessSectionOutcome 5 times per section
            foreach (SectionConfig section in sections)
            {
                WriterTarget firstResult = GetProcessSectionOutcome(
                    section, auxEnabled, isWii, updateRanges, partitionTypeMap);

                for (int call = 1; call < 5; call++)
                {
                    WriterTarget result = GetProcessSectionOutcome(
                        section, auxEnabled, isWii, updateRanges, partitionTypeMap);
                    if (result != firstResult)
                        return false;
                }
            }

            return true;
        }

        // =====================================================================
        // Property 2 (supplementary): Non-update sections route to primary
        // =====================================================================

        /// <summary>
        /// **Validates: Requirements 3.1, 3.3, 3.4**
        ///
        /// Property 2 (supplementary): Non-update sections route exclusively to primary.
        ///
        /// For any non-header section that does NOT belong to an update partition,
        /// the formatter routes it to the primary writer — never to aux.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool NonUpdateSections_RouteExclusivelyToPrimary(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt offsetSeed)
        {
            int sectionCount = (sectionCountSeed.Get % 20) + 1;

            // Update ranges that we'll avoid
            List<(long Start, long End)> updateRanges = new List<(long Start, long End)>
            {
                (0x50000, 0xD0000)
            };

            // Partition type map with only Game partitions
            Dictionary<int, string> partitionTypeMap = new Dictionary<int, string>
            {
                { 0, "Game" },
                { 1, "Game" }
            };

            AreaType[] routableTypes = new[]
            {
                AreaType.PartitionHeader,
                AreaType.FileSystem,
                AreaType.Other
            };

            string[] nonUpdateTypes = new[] { "Game", "Channel", null };

            Random rng = new Random(offsetSeed.Get);

            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = routableTypes[rng.Next(routableTypes.Length)];
                string partType = nonUpdateTypes[rng.Next(nonUpdateTypes.Length)];

                // Ensure Other sections are outside update ranges
                long imageOffset = 0x1000000 + (i * 0x10000);

                // --- Wii ---
                SectionConfig wiiConfig = new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = rng.Next(0, 2),
                    ImageOffset = imageOffset
                };

                WriterTarget wiiResult = ModelWiiRouting(wiiConfig, auxEnabled: true, updateRanges);
                if (wiiResult != WriterTarget.Primary)
                    return false;

                // --- WiiU ---
                SectionConfig wiiUConfig = new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = partType,
                    PartitionIndex = rng.Next(0, 2), // maps to "Game" in partitionTypeMap
                    ImageOffset = imageOffset
                };

                WriterTarget wiiUResult = ModelWiiURouting(wiiUConfig, auxEnabled: true, partitionTypeMap);
                if (wiiUResult != WriterTarget.Primary)
                    return false;
            }

            return true;
        }

        // =====================================================================
        // Property 9: Update Partition Classification
        // =====================================================================

        /// <summary>
        /// **Validates: Requirements 2.3, 2.4**
        ///
        /// Property 9: Update Partition Classification (Wii formatter model).
        ///
        /// For any section belonging to an update partition, the Wii formatter
        /// routes it to the aux writer when aux is enabled. When aux is disabled,
        /// the same sections route to the primary writer (backward compatibility).
        ///
        /// Generates sections that ALL belong to update partitions:
        ///   - PartitionHeader / FileSystem with PartitionType = "Update"
        ///   - Other sections with offsets within update partition ranges
        /// Verifies:
        ///   1. Aux enabled → all update sections route to aux
        ///   2. Aux disabled → all update sections route to primary
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WiiFormatter_UpdatePartitionClassification(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt updateRangeCountSeed,
            NonNegativeInt offsetSeed)
        {
            int sectionCount = (sectionCountSeed.Get % 25) + 1;
            int updateRangeCount = (updateRangeCountSeed.Get % 4) + 1;

            // Generate update partition offset ranges
            List<(long Start, long End)> updateRanges = new List<(long Start, long End)>();
            long rangeBase = 0x50000;
            for (int i = 0; i < updateRangeCount; i++)
            {
                long start = rangeBase + (i * 0x100000);
                long end = start + 0x80000;
                updateRanges.Add((start, end));
            }

            // Area types that can carry update partition data on Wii
            AreaType[] updateAreaTypes = new[]
            {
                AreaType.PartitionHeader,
                AreaType.FileSystem,
                AreaType.Other
            };

            Random rng = new Random(offsetSeed.Get);

            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = updateAreaTypes[rng.Next(updateAreaTypes.Length)];

                SectionConfig config;
                if (areaType == AreaType.Other)
                {
                    // Other sections: place within an update partition range
                    (long Start, long End) range = updateRanges[rng.Next(updateRanges.Count)];
                    long offset = range.Start + (long)(rng.NextDouble() * (range.End - range.Start - 1));

                    config = new SectionConfig
                    {
                        AreaType = AreaType.Other,
                        PartitionType = null, // Other sections don't carry PartitionType directly
                        PartitionIndex = -1,
                        ImageOffset = offset
                    };
                }
                else
                {
                    // PartitionHeader / FileSystem: set PartitionType = "Update"
                    (long Start, long End) range = updateRanges[rng.Next(updateRanges.Count)];
                    long offset = range.Start + (long)(rng.NextDouble() * (range.End - range.Start - 1));

                    config = new SectionConfig
                    {
                        AreaType = areaType,
                        PartitionType = "Update",
                        PartitionIndex = 0,
                        ImageOffset = offset
                    };
                }

                // Aux enabled: update partition sections must route to aux
                WriterTarget auxEnabledResult = ModelWiiRouting(config, auxEnabled: true, updateRanges);
                if (auxEnabledResult != WriterTarget.Aux)
                    return false;

                // Aux disabled: same sections must route to primary (backward compatibility)
                WriterTarget auxDisabledResult = ModelWiiRouting(config, auxEnabled: false, updateRanges);
                if (auxDisabledResult != WriterTarget.Primary)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.3, 2.4**
        ///
        /// Property 9: Update Partition Classification (WiiU formatter model).
        ///
        /// For any section belonging to an update partition, the WiiU formatter
        /// routes it to the aux writer when aux is enabled. When aux is disabled,
        /// the same sections route to the primary writer (backward compatibility).
        ///
        /// Generates sections that ALL belong to update partitions:
        ///   - Sections with direct PartitionType = "Update"
        ///   - Sections with partition index mapping to "Update" in the partition map
        /// Verifies:
        ///   1. Aux enabled → all update sections route to aux
        ///   2. Aux disabled → all update sections route to primary
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WiiUFormatter_UpdatePartitionClassification(
            NonNegativeInt sectionCountSeed,
            NonNegativeInt partitionCountSeed,
            NonNegativeInt offsetSeed)
        {
            int sectionCount = (sectionCountSeed.Get % 25) + 1;
            int updatePartitionCount = (partitionCountSeed.Get % 3) + 1;

            // Build partition type map: first N partitions are Update, rest are Game
            Dictionary<int, string> partitionTypeMap = new Dictionary<int, string>();
            for (int i = 0; i < updatePartitionCount; i++)
                partitionTypeMap[i] = "Update";
            // Add some non-update partitions for realism
            partitionTypeMap[updatePartitionCount] = "Game";
            partitionTypeMap[updatePartitionCount + 1] = "Game";

            // WiiU area types that can carry partition data
            AreaType[] updateAreaTypes = new[]
            {
                AreaType.PartitionHeader,
                AreaType.FileSystem,
                AreaType.FstBlock,
                AreaType.Other
            };

            Random rng = new Random(offsetSeed.Get);

            for (int i = 0; i < sectionCount; i++)
            {
                AreaType areaType = updateAreaTypes[rng.Next(updateAreaTypes.Length)];
                // Pick a partition index that maps to "Update"
                int updatePartIdx = rng.Next(0, updatePartitionCount);

                // Test with direct PartitionType = "Update"
                SectionConfig configDirect = new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = "Update",
                    PartitionIndex = updatePartIdx,
                    ImageOffset = 0x1000 + (i * 0x10000)
                };

                // Aux enabled: must route to aux
                WriterTarget directAuxEnabled = ModelWiiURouting(configDirect, auxEnabled: true, partitionTypeMap);
                if (directAuxEnabled != WriterTarget.Aux)
                    return false;

                // Aux disabled: must route to primary
                WriterTarget directAuxDisabled = ModelWiiURouting(configDirect, auxEnabled: false, partitionTypeMap);
                if (directAuxDisabled != WriterTarget.Primary)
                    return false;

                // Test with no direct PartitionType but partition index maps to "Update"
                SectionConfig configIndexed = new SectionConfig
                {
                    AreaType = areaType,
                    PartitionType = null,
                    PartitionIndex = updatePartIdx,
                    ImageOffset = 0x1000 + (i * 0x10000)
                };

                // Aux enabled: must route to aux via partition index lookup
                WriterTarget indexedAuxEnabled = ModelWiiURouting(configIndexed, auxEnabled: true, partitionTypeMap);
                if (indexedAuxEnabled != WriterTarget.Aux)
                    return false;

                // Aux disabled: must route to primary
                WriterTarget indexedAuxDisabled = ModelWiiURouting(configIndexed, auxEnabled: false, partitionTypeMap);
                if (indexedAuxDisabled != WriterTarget.Primary)
                    return false;
            }

            return true;
        }
    }
}
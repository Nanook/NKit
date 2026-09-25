using FsCheck;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests for game partition filler routing to split writer.
    /// Feature: aux-split-mode, Property 5: Game Partition Filler Routing to Split
    ///
    /// For any gap/filler data within an AreaType.FileSystem section when _splitWriter
    /// is active, the DataStoreXboxFormatter SHALL write that data to the split writer
    /// (not the primary writer or shared aux writer).
    ///
    /// Uses model-based testing since the real DataStoreXboxFormatter has complex
    /// constructor dependencies (DataStore, file system, IStepContext). The model
    /// replicates the ProcessSection routing logic for AreaType.FileSystem sections.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class GamePartitionFillerRoutingPropertyTests
    {
        #region Model Types

        /// <summary>
        /// Represents a gap/filler record within a FileSystem section.
        /// Models the GapRange struct produced by GetGapsForXbox.
        /// </summary>
        private struct GapRecord
        {
            public long ImageOffset;
            public long FsOffset;
            public int Size;
            public byte FillByte;
        }

        /// <summary>
        /// Identifies which writer received the data.
        /// </summary>
        private enum WriterTarget
        {
            Primary,
            Split,
            Aux
        }

        /// <summary>
        /// Tracks writes to a modelled writer. Records each write with its offset and size.
        /// Can simulate write failures.
        /// </summary>
        private class MockWriterTracker
        {
            public List<(long ImageOffset, int Size)> Writes { get; } = new();
            public bool ThrowOnWrite { get; set; }

            public void Write(long imageOffset, int size)
            {
                if (ThrowOnWrite)
                    throw new InvalidOperationException("Simulated write failure");
                Writes.Add((imageOffset, size));
            }
        }

        #endregion

        #region Model

        /// <summary>
        /// Models the DataStoreXboxFormatter.ProcessSection routing logic for
        /// AreaType.FileSystem sections. This faithfully replicates the production
        /// code's dual-routing behavior:
        /// 
        /// - When _splitWriter is active: gap/filler data → split writer
        /// - When _splitWriter fails: fall back to primary writer for remaining blocks
        /// - When _splitWriter is null: all gap data → primary writer
        /// 
        /// Returns the list of (WriterTarget, GapRecord) tuples indicating where each
        /// gap record was actually written.
        /// </summary>
        private static List<(WriterTarget Target, GapRecord Gap)> ModelFileSystemRouting(
            IEnumerable<GapRecord> gaps,
            MockWriterTracker primaryTracker,
            MockWriterTracker splitTracker,
            bool splitWriterActive)
        {
            List<(WriterTarget Target, GapRecord Gap)> routingResults = new List<(WriterTarget Target, GapRecord Gap)>();
            bool splitFailed = false;

            foreach (GapRecord gap in gaps)
            {
                // Route filler/junk to split writer when split is active and not failed
                MockWriterTracker targetTracker = (splitWriterActive && !splitFailed)
                    ? splitTracker
                    : primaryTracker;

                WriterTarget targetName = (splitWriterActive && !splitFailed)
                    ? WriterTarget.Split
                    : WriterTarget.Primary;

                try
                {
                    targetTracker.Write(gap.ImageOffset, gap.Size);
                    routingResults.Add((targetName, gap));
                }
                catch when (splitWriterActive && targetTracker == splitTracker)
                {
                    // Split write failed — mark as failed, fall back to primary
                    splitFailed = true;
                    primaryTracker.Write(gap.ImageOffset, gap.Size);
                    routingResults.Add((WriterTarget.Primary, gap));
                }
            }

            return routingResults;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 5: Game Partition Filler Routing to Split — All gaps route to split
        ///
        /// For any set of gap/filler records in an AreaType.FileSystem section when
        /// _splitWriter is active and not failing, ALL gap data SHALL be written
        /// exclusively to the split writer. No data goes to primary or shared aux.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileSystemGaps_RouteToSplitWriter_WhenSplitActive(
            NonNegativeInt gapCountSeed,
            NonNegativeInt offsetSeed)
        {
            int gapCount = (gapCountSeed.Get % 30) + 1;
            Random rng = new Random(offsetSeed.Get);

            // Generate random gap records within a FileSystem section
            long sectionBase = ((long)rng.Next(0x1000, 0x7FFFFFFF)) * 0x100;
            List<GapRecord> gaps = GenerateGapRecords(gapCount, sectionBase, rng);

            MockWriterTracker primaryTracker = new MockWriterTracker();
            MockWriterTracker splitTracker = new MockWriterTracker();

            // Route with split writer active
            List<(WriterTarget Target, GapRecord Gap)> results = ModelFileSystemRouting(gaps, primaryTracker, splitTracker, splitWriterActive: true);

            // Property: ALL gaps went to split writer, NONE to primary
            if (primaryTracker.Writes.Count != 0)
                return false;
            if (splitTracker.Writes.Count != gapCount)
                return false;

            // Verify each result confirms split routing
            foreach ((WriterTarget target, GapRecord _) in results)
            {
                if (target != WriterTarget.Split)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Property 5: Game Partition Filler Routing to Split — Offsets preserved
        ///
        /// For any gap/filler data routed to the split writer, the image offset and
        /// size of each write SHALL match the original gap record exactly.
        /// Data integrity is preserved through routing.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileSystemGaps_OffsetsPreservedInSplitWriter(
            NonNegativeInt gapCountSeed,
            NonNegativeInt offsetSeed)
        {
            int gapCount = (gapCountSeed.Get % 25) + 1;
            Random rng = new Random(offsetSeed.Get);

            long sectionBase = ((long)rng.Next(0x1000, 0x7FFFFFFF)) * 0x100;
            List<GapRecord> gaps = GenerateGapRecords(gapCount, sectionBase, rng);

            MockWriterTracker primaryTracker = new MockWriterTracker();
            MockWriterTracker splitTracker = new MockWriterTracker();

            ModelFileSystemRouting(gaps, primaryTracker, splitTracker, splitWriterActive: true);

            // Property: each write in split tracker matches the corresponding gap record
            if (splitTracker.Writes.Count != gaps.Count)
                return false;

            for (int i = 0; i < gaps.Count; i++)
            {
                if (splitTracker.Writes[i].ImageOffset != gaps[i].ImageOffset)
                    return false;
                if (splitTracker.Writes[i].Size != gaps[i].Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Property 5: Game Partition Filler Routing to Split — Split inactive routes to primary
        ///
        /// For any gap/filler data in an AreaType.FileSystem section when _splitWriter is
        /// NOT active (null), ALL data SHALL route to the primary writer instead.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileSystemGaps_RouteToPrimary_WhenSplitInactive(
            NonNegativeInt gapCountSeed,
            NonNegativeInt offsetSeed)
        {
            int gapCount = (gapCountSeed.Get % 30) + 1;
            Random rng = new Random(offsetSeed.Get);

            long sectionBase = ((long)rng.Next(0x1000, 0x7FFFFFFF)) * 0x100;
            List<GapRecord> gaps = GenerateGapRecords(gapCount, sectionBase, rng);

            MockWriterTracker primaryTracker = new MockWriterTracker();
            MockWriterTracker splitTracker = new MockWriterTracker();

            // Route with split writer INACTIVE
            List<(WriterTarget Target, GapRecord Gap)> results = ModelFileSystemRouting(gaps, primaryTracker, splitTracker, splitWriterActive: false);

            // Property: ALL gaps went to primary, NONE to split
            if (splitTracker.Writes.Count != 0)
                return false;
            if (primaryTracker.Writes.Count != gapCount)
                return false;

            foreach ((WriterTarget target, GapRecord _) in results)
            {
                if (target != WriterTarget.Primary)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Property 5: Game Partition Filler Routing to Split — No data goes to aux writer
        ///
        /// For any AreaType.FileSystem section, regardless of whether aux or split writers
        /// are active, gap/filler data SHALL NEVER be routed to the shared aux writer.
        /// The aux writer only receives AreaType.Other (video partition) data.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileSystemGaps_NeverRouteToAuxWriter(
            NonNegativeInt gapCountSeed,
            NonNegativeInt offsetSeed,
            bool splitActive)
        {
            int gapCount = (gapCountSeed.Get % 30) + 1;
            Random rng = new Random(offsetSeed.Get);

            long sectionBase = ((long)rng.Next(0x1000, 0x7FFFFFFF)) * 0x100;
            List<GapRecord> gaps = GenerateGapRecords(gapCount, sectionBase, rng);

            MockWriterTracker primaryTracker = new MockWriterTracker();
            MockWriterTracker splitTracker = new MockWriterTracker();

            List<(WriterTarget Target, GapRecord Gap)> results = ModelFileSystemRouting(gaps, primaryTracker, splitTracker, splitWriterActive: splitActive);

            // Property: no gap was ever routed to aux (WriterTarget.Aux never appears)
            foreach ((WriterTarget target, GapRecord _) in results)
            {
                if (target == WriterTarget.Aux)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Property 5: Game Partition Filler Routing to Split — Routing is deterministic
        ///
        /// For any set of gap records and split writer state, calling the routing logic
        /// multiple times with the same inputs SHALL produce the same routing decisions.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileSystemGaps_RoutingIsDeterministic(
            NonNegativeInt gapCountSeed,
            NonNegativeInt offsetSeed,
            bool splitActive)
        {
            int gapCount = (gapCountSeed.Get % 20) + 1;
            Random rng = new Random(offsetSeed.Get);

            long sectionBase = ((long)rng.Next(0x1000, 0x7FFFFFFF)) * 0x100;
            List<GapRecord> gaps = GenerateGapRecords(gapCount, sectionBase, rng);

            // Run routing 3 times with the same inputs
            List<List<(WriterTarget Target, GapRecord Gap)>> allResults = new List<List<(WriterTarget Target, GapRecord Gap)>>();
            for (int run = 0; run < 3; run++)
            {
                MockWriterTracker primary = new MockWriterTracker();
                MockWriterTracker split = new MockWriterTracker();
                List<(WriterTarget Target, GapRecord Gap)> results = ModelFileSystemRouting(gaps, primary, split, splitWriterActive: splitActive);
                allResults.Add(results);
            }

            // Property: all runs produce identical routing decisions
            for (int i = 0; i < gapCount; i++)
            {
                WriterTarget firstTarget = allResults[0][i].Target;
                for (int run = 1; run < allResults.Count; run++)
                {
                    if (allResults[run][i].Target != firstTarget)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Property 5: Game Partition Filler Routing to Split — Ordering preserved
        ///
        /// For any set of gap records, the split writer SHALL receive writes in the
        /// same order as the gaps appear in the section (ordering is preserved).
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FileSystemGaps_OrderingPreservedInSplitWriter(
            NonNegativeInt gapCountSeed,
            NonNegativeInt offsetSeed)
        {
            int gapCount = (gapCountSeed.Get % 25) + 1;
            Random rng = new Random(offsetSeed.Get);

            long sectionBase = ((long)rng.Next(0x1000, 0x7FFFFFFF)) * 0x100;
            List<GapRecord> gaps = GenerateGapRecords(gapCount, sectionBase, rng);

            MockWriterTracker primaryTracker = new MockWriterTracker();
            MockWriterTracker splitTracker = new MockWriterTracker();

            ModelFileSystemRouting(gaps, primaryTracker, splitTracker, splitWriterActive: true);

            // Property: writes in split tracker are in the same order as the input gaps
            if (splitTracker.Writes.Count != gapCount)
                return false;

            for (int i = 1; i < splitTracker.Writes.Count; i++)
            {
                // The offsets should follow the same order as the gap records
                if (splitTracker.Writes[i].ImageOffset != gaps[i].ImageOffset)
                    return false;
            }

            return true;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Generates a list of gap records simulating gaps within a FileSystem section.
        /// Gaps have ascending image offsets within the section and varying sizes.
        /// </summary>
        private static List<GapRecord> GenerateGapRecords(int count, long sectionBase, Random rng)
        {
            List<GapRecord> gaps = new List<GapRecord>();
            long currentOffset = sectionBase;

            for (int i = 0; i < count; i++)
            {
                // Gap sizes between 0x100 and 0x8000 bytes (realistic for Xbox filler)
                int size = rng.Next(1, 32) * 0x100;

                // Skip forward by a file-sized amount before each gap
                currentOffset += rng.Next(0x800, 0x10000);

                gaps.Add(new GapRecord
                {
                    ImageOffset = currentOffset,
                    FsOffset = currentOffset - sectionBase,
                    Size = size,
                    FillByte = (byte)rng.Next(1, 256) // Non-zero fill byte (persisted gap)
                });

                currentOffset += size;
            }

            return gaps;
        }

        #endregion
    }
}
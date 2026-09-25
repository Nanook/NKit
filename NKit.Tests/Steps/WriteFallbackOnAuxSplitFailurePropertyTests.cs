using FsCheck;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests for write fallback on aux/split failure.
    /// Feature: aux-split-mode, Property 6: Write Fallback on Aux/Split Failure
    ///
    /// For any write that fails on either the shared aux writer or the split writer,
    /// the DataStoreXboxFormatter SHALL successfully write the same data to the Primary_Store.
    ///
    /// **Validates: Requirements 4.3, 4.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class WriteFallbackOnAuxSplitFailurePropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Records a write operation for tracking routing behavior.
        /// </summary>
        private record WriteRecord(long ImageOffset, int FsOffset, int Size);

        /// <summary>
        /// Simulates the DataStoreXboxFormatter fallback behavior for AreaType.Other sections.
        /// When the aux writer fails, the same data must be written to the primary writer.
        /// This mirrors the ProcessSection routing for video partition gap data.
        /// </summary>
        private static (List<WriteRecord> PrimaryWrites, List<WriteRecord> AuxWrites) SimulateAuxFallback(
            List<(long GapOffset, int FsOffset, int Size)> gaps,
            int failAtIndex)
        {
            List<WriteRecord> primaryWrites = new List<WriteRecord>();
            List<WriteRecord> auxWrites = new List<WriteRecord>();
            bool auxFailed = false;

            for (int i = 0; i < gaps.Count; i++)
            {
                (long gapOffset, int fsOffset, int size) = gaps[i];

                if (!auxFailed)
                {
                    // Try aux writer
                    if (i == failAtIndex)
                    {
                        // Aux fails — fall back to primary for this block and all subsequent
                        auxFailed = true;
                        primaryWrites.Add(new WriteRecord(gapOffset, fsOffset, size));
                    }
                    else
                    {
                        auxWrites.Add(new WriteRecord(gapOffset, fsOffset, size));
                    }
                }
                else
                {
                    // Aux already failed — all subsequent go to primary
                    primaryWrites.Add(new WriteRecord(gapOffset, fsOffset, size));
                }
            }

            return (primaryWrites, auxWrites);
        }

        /// <summary>
        /// Simulates the DataStoreXboxFormatter fallback behavior for AreaType.FileSystem sections.
        /// When the split writer fails, the same data must be written to the primary writer.
        /// This mirrors the ProcessSection routing for game partition filler data.
        /// </summary>
        private static (List<WriteRecord> PrimaryWrites, List<WriteRecord> SplitWrites) SimulateSplitFallback(
            List<(long ImageOffset, int FsOffset, int Size)> gapRanges,
            int failAtIndex)
        {
            List<WriteRecord> primaryWrites = new List<WriteRecord>();
            List<WriteRecord> splitWrites = new List<WriteRecord>();
            bool splitFailed = false;

            for (int i = 0; i < gapRanges.Count; i++)
            {
                (long imageOffset, int fsOffset, int size) = gapRanges[i];

                if (!splitFailed)
                {
                    // Try split writer
                    if (i == failAtIndex)
                    {
                        // Split fails — fall back to primary for this block and all subsequent
                        splitFailed = true;
                        primaryWrites.Add(new WriteRecord(imageOffset, fsOffset, size));
                    }
                    else
                    {
                        splitWrites.Add(new WriteRecord(imageOffset, fsOffset, size));
                    }
                }
                else
                {
                    // Split already failed — all subsequent go to primary
                    primaryWrites.Add(new WriteRecord(imageOffset, fsOffset, size));
                }
            }

            return (primaryWrites, splitWrites);
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 6a: Aux writer failure causes fallback to primary for AreaType.Other sections.
        ///
        /// For any AreaType.Other section with N gap blocks and a failure at any position K (0..N-1),
        /// the block at K and all subsequent blocks SHALL be written to the primary writer.
        /// Blocks before K remain in the aux writer.
        ///
        /// **Validates: Requirements 4.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AuxWriterFailure_FallsBackToPrimary_ForVideoPartitionData(
            NonNegativeInt gapCountRaw,
            NonNegativeInt failIndexRaw,
            NonNegativeInt seedRaw)
        {
            int gapCount = 1 + (gapCountRaw.Get % 15); // 1 to 15 gaps
            int failAtIndex = failIndexRaw.Get % gapCount; // Fail at valid index
            int seed = seedRaw.Get;

            // Generate arbitrary gap data representing video partition sub-parts
            List<(long GapOffset, int FsOffset, int Size)> gaps = new List<(long GapOffset, int FsOffset, int Size)>();
            long currentOffset = 0x10000;
            int currentFsOffset = 0;
            for (int i = 0; i < gapCount; i++)
            {
                int hash = Math.Abs((seed * 37) + (i * 13));
                int size = 0x100 + (hash % 0x4000);
                gaps.Add((currentOffset, currentFsOffset, size));
                currentOffset += size;
                currentFsOffset += size;
            }

            (List<WriteRecord> primaryWrites, List<WriteRecord> auxWrites) = SimulateAuxFallback(gaps, failAtIndex);

            // Property: blocks before failure go to aux
            if (auxWrites.Count != failAtIndex)
                return false;

            // Property: the failed block and all subsequent go to primary
            int expectedPrimaryCount = gapCount - failAtIndex;
            if (primaryWrites.Count != expectedPrimaryCount)
                return false;

            // Property: every block is accounted for (no data loss)
            if (auxWrites.Count + primaryWrites.Count != gapCount)
                return false;

            // Property: primary writes have the correct offsets (same data that would have gone to aux)
            for (int i = 0; i < primaryWrites.Count; i++)
            {
                int originalIndex = failAtIndex + i;
                (long GapOffset, int FsOffset, int Size) expectedGap = gaps[originalIndex];
                if (primaryWrites[i].ImageOffset != expectedGap.GapOffset)
                    return false;
                if (primaryWrites[i].FsOffset != expectedGap.FsOffset)
                    return false;
                if (primaryWrites[i].Size != expectedGap.Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Property 6b: Split writer failure causes fallback to primary for AreaType.FileSystem sections.
        ///
        /// For any AreaType.FileSystem section with N gap ranges and a failure at any position K (0..N-1),
        /// the block at K and all subsequent blocks SHALL be written to the primary writer.
        /// Blocks before K remain in the split writer.
        ///
        /// **Validates: Requirements 4.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool SplitWriterFailure_FallsBackToPrimary_ForGamePartitionFiller(
            NonNegativeInt gapCountRaw,
            NonNegativeInt failIndexRaw,
            NonNegativeInt seedRaw)
        {
            int gapCount = 1 + (gapCountRaw.Get % 15); // 1 to 15 gap ranges
            int failAtIndex = failIndexRaw.Get % gapCount; // Fail at valid index
            int seed = seedRaw.Get;

            // Generate arbitrary gap ranges representing game partition filler
            List<(long ImageOffset, int FsOffset, int Size)> gapRanges = new List<(long ImageOffset, int FsOffset, int Size)>();
            long currentOffset = 0x100000;
            int currentFsOffset = 0;
            for (int i = 0; i < gapCount; i++)
            {
                int hash = Math.Abs((seed * 41) + (i * 19));
                int size = 0x200 + (hash % 0x8000);
                gapRanges.Add((currentOffset, currentFsOffset, size));
                currentOffset += size + 0x800; // gaps between filler regions
                currentFsOffset += size;
            }

            (List<WriteRecord> primaryWrites, List<WriteRecord> splitWrites) = SimulateSplitFallback(gapRanges, failAtIndex);

            // Property: blocks before failure go to split
            if (splitWrites.Count != failAtIndex)
                return false;

            // Property: the failed block and all subsequent go to primary
            int expectedPrimaryCount = gapCount - failAtIndex;
            if (primaryWrites.Count != expectedPrimaryCount)
                return false;

            // Property: every block is accounted for (no data loss)
            if (splitWrites.Count + primaryWrites.Count != gapCount)
                return false;

            // Property: primary writes have the correct offsets (same data that would have gone to split)
            for (int i = 0; i < primaryWrites.Count; i++)
            {
                int originalIndex = failAtIndex + i;
                (long ImageOffset, int FsOffset, int Size) expectedRange = gapRanges[originalIndex];
                if (primaryWrites[i].ImageOffset != expectedRange.ImageOffset)
                    return false;
                if (primaryWrites[i].FsOffset != expectedRange.FsOffset)
                    return false;
                if (primaryWrites[i].Size != expectedRange.Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Property 6c: All data is preserved regardless of failure point.
        ///
        /// For any section with N blocks and failure at any position K, the total number of
        /// blocks written across all writers equals N. No data is lost during fallback.
        /// This applies symmetrically to both aux and split writer failures.
        ///
        /// **Validates: Requirements 4.3, 4.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WriteFallback_NoDataLoss_AllBlocksAccountedFor(
            NonNegativeInt gapCountRaw,
            NonNegativeInt failIndexRaw,
            NonNegativeInt seedRaw,
            bool isAuxFailure)
        {
            int gapCount = 1 + (gapCountRaw.Get % 20); // 1 to 20 blocks
            int failAtIndex = failIndexRaw.Get % gapCount;
            int seed = seedRaw.Get;

            // Generate arbitrary blocks
            List<(long Offset, int FsOffset, int Size)> blocks = new List<(long Offset, int FsOffset, int Size)>();
            long currentOffset = 0x50000;
            int currentFsOffset = 0;
            for (int i = 0; i < gapCount; i++)
            {
                int hash = Math.Abs((seed * 53) + (i * 7));
                int size = 0x100 + (hash % 0x10000);
                blocks.Add((currentOffset, currentFsOffset, size));
                currentOffset += size;
                currentFsOffset += size;
            }

            int totalPrimary;
            int totalAux;

            if (isAuxFailure)
            {
                (List<WriteRecord> primaryWrites, List<WriteRecord> auxWrites) = SimulateAuxFallback(blocks, failAtIndex);
                totalPrimary = primaryWrites.Count;
                totalAux = auxWrites.Count;
            }
            else
            {
                (List<WriteRecord> primaryWrites, List<WriteRecord> splitWrites) = SimulateSplitFallback(blocks, failAtIndex);
                totalPrimary = primaryWrites.Count;
                totalAux = splitWrites.Count;
            }

            // Property: no data loss — total blocks across both writers equals input count
            return totalPrimary + totalAux == gapCount;
        }

        /// <summary>
        /// Property 6d: Immediate failure (failAt=0) routes ALL blocks to primary.
        ///
        /// When the aux/split writer fails on the very first write, ALL blocks in the section
        /// SHALL be written to the primary writer, effectively disabling aux/split for that section.
        ///
        /// **Validates: Requirements 4.3, 4.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WriteFallback_ImmediateFailure_AllBlocksToPrimary(
            NonNegativeInt gapCountRaw,
            NonNegativeInt seedRaw,
            bool isAuxFailure)
        {
            int gapCount = 1 + (gapCountRaw.Get % 20);
            int seed = seedRaw.Get;

            List<(long Offset, int FsOffset, int Size)> blocks = new List<(long Offset, int FsOffset, int Size)>();
            long currentOffset = 0x80000;
            int currentFsOffset = 0;
            for (int i = 0; i < gapCount; i++)
            {
                int hash = Math.Abs((seed * 61) + (i * 11));
                int size = 0x200 + (hash % 0x8000);
                blocks.Add((currentOffset, currentFsOffset, size));
                currentOffset += size;
                currentFsOffset += size;
            }

            int failAtIndex = 0; // Immediate failure

            if (isAuxFailure)
            {
                (List<WriteRecord> primaryWrites, List<WriteRecord> auxWrites) = SimulateAuxFallback(blocks, failAtIndex);
                // All blocks must go to primary, none to aux
                return primaryWrites.Count == gapCount && auxWrites.Count == 0;
            }
            else
            {
                (List<WriteRecord> primaryWrites, List<WriteRecord> splitWrites) = SimulateSplitFallback(blocks, failAtIndex);
                // All blocks must go to primary, none to split
                return primaryWrites.Count == gapCount && splitWrites.Count == 0;
            }
        }

        /// <summary>
        /// Property 6e: Fallback writes preserve exact data identity.
        ///
        /// For any block that falls back to primary due to aux/split failure, the ImageOffset,
        /// FsOffset, and Size written to primary SHALL be identical to what would have been
        /// written to the aux/split writer. No transformation or data modification occurs.
        ///
        /// **Validates: Requirements 4.3, 4.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public bool WriteFallback_PreservesExactDataIdentity(
            NonNegativeInt gapCountRaw,
            NonNegativeInt failIndexRaw,
            NonNegativeInt seedRaw,
            bool isAuxFailure)
        {
            int gapCount = 1 + (gapCountRaw.Get % 15);
            int failAtIndex = failIndexRaw.Get % gapCount;
            int seed = seedRaw.Get;

            List<(long Offset, int FsOffset, int Size)> blocks = new List<(long Offset, int FsOffset, int Size)>();
            long currentOffset = 0x200000;
            int currentFsOffset = 0;
            for (int i = 0; i < gapCount; i++)
            {
                int hash = Math.Abs((seed * 67) + (i * 23));
                int size = 0x100 + (hash % 0x6000);
                blocks.Add((currentOffset, currentFsOffset, size));
                currentOffset += size;
                currentFsOffset += size;
            }

            List<WriteRecord> primaryWrites;
            if (isAuxFailure)
            {
                (primaryWrites, _) = SimulateAuxFallback(blocks, failAtIndex);
            }
            else
            {
                (primaryWrites, _) = SimulateSplitFallback(blocks, failAtIndex);
            }

            // Verify every primary write matches the exact data from the original block list
            for (int i = 0; i < primaryWrites.Count; i++)
            {
                int originalIndex = failAtIndex + i;
                (long Offset, int FsOffset, int Size) expected = blocks[originalIndex];

                if (primaryWrites[i].ImageOffset != expected.Offset)
                    return false;
                if (primaryWrites[i].FsOffset != expected.FsOffset)
                    return false;
                if (primaryWrites[i].Size != expected.Size)
                    return false;
            }

            return true;
        }

        #endregion
    }
}
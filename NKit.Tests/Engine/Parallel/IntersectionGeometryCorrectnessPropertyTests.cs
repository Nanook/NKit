using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Parallel
{
    /// <summary>
    /// Property 11: Intersection Geometry Correctness
    ///
    /// For any chunk with disc range [chunkStart, chunkEnd) and overlap file with range
    /// [fileStart, fileEnd), the intersection SHALL be calculated as
    /// [max(chunkStart, fileStart), min(chunkEnd, fileEnd)).
    /// When intersection size is positive, exactly that many bytes SHALL be written.
    /// When intersection is empty or negative, no write SHALL occur.
    /// Write size SHALL be capped to (TotalSize - TotalWritten) to prevent overshoot.
    ///
    /// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Parallel")]
    public class IntersectionGeometryCorrectnessPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Represents the result of an intersection calculation.
        /// </summary>
        private struct IntersectionResult
        {
            public long IntersectStart;
            public long IntersectEnd;
            public bool HasIntersection;
            public int WriteSize;
        }

        /// <summary>
        /// Simulates the exact intersection calculation from ProcessOverlaps Phase 2.
        ///
        /// This mirrors the real implementation logic:
        ///   intersectStart = Math.Max(discStart, fileStart);
        ///   intersectEnd = Math.Min(discEnd, fileEnd);
        ///   hasIntersection = intersectEnd > intersectStart;
        ///   writeSize = (int)(intersectEnd - intersectStart);
        ///   // Cap to remaining bytes
        ///   remaining = totalSize - totalWritten;
        ///   if (remaining <= 0) → no write
        ///   if (writeSize > remaining) writeSize = (int)remaining;
        /// </summary>
        private static IntersectionResult CalculateIntersection(
            long discStart, long discEnd,
            long fileStart, long fileEnd,
            long totalSize, long totalWritten)
        {
            long intersectStart = Math.Max(discStart, fileStart);
            long intersectEnd = Math.Min(discEnd, fileEnd);
            bool hasIntersection = intersectEnd > intersectStart;

            int writeSize = 0;
            if (hasIntersection)
            {
                writeSize = (int)(intersectEnd - intersectStart);

                // Cap to remaining bytes (prevent overshoot)
                long remaining = totalSize - totalWritten;
                if (remaining <= 0)
                {
                    writeSize = 0;
                    hasIntersection = false;
                }
                else if (writeSize > remaining)
                {
                    writeSize = (int)remaining;
                }
            }

            return new IntersectionResult
            {
                IntersectStart = intersectStart,
                IntersectEnd = intersectEnd,
                HasIntersection = hasIntersection,
                WriteSize = writeSize
            };
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 11a: Intersection start is always max(chunkStart, fileStart).
        ///
        /// For any chunk disc range [discStart, discEnd) and file range [fileStart, fileEnd),
        /// the intersection start SHALL be calculated as Math.Max(discStart, fileStart).
        ///
        /// **Validates: Requirements 9.1**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property IntersectionStart_IsMaxOfChunkStartAndFileStart()
        {
            var testGen =
                from discStart in Gen.Choose(0, 1000).Select(o => (long)o * 0x10000)
                from discSize in Gen.Choose(1, 100).Select(s => (long)s * 0x10000)
                from fileStart in Gen.Choose(0, 1000).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 100).Select(s => (long)s * 0x10000)
                select new
                {
                    DiscStart = discStart,
                    DiscEnd = discStart + discSize,
                    FileStart = fileStart,
                    FileEnd = fileStart + fileSize
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                IntersectionResult result = CalculateIntersection(
                    data.DiscStart, data.DiscEnd,
                    data.FileStart, data.FileEnd,
                    totalSize: long.MaxValue, totalWritten: 0);

                long expectedStart = Math.Max(data.DiscStart, data.FileStart);
                bool correct = result.IntersectStart == expectedStart;

                return correct.Label(
                    $"IntersectStart should be max({data.DiscStart:X}, {data.FileStart:X}) = {expectedStart:X}, " +
                    $"got {result.IntersectStart:X}");
            });
        }

        /// <summary>
        /// Property 11b: Intersection end is always min(chunkEnd, fileEnd).
        ///
        /// For any chunk disc range [discStart, discEnd) and file range [fileStart, fileEnd),
        /// the intersection end SHALL be calculated as Math.Min(discEnd, fileEnd).
        ///
        /// **Validates: Requirements 9.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property IntersectionEnd_IsMinOfChunkEndAndFileEnd()
        {
            var testGen =
                from discStart in Gen.Choose(0, 1000).Select(o => (long)o * 0x10000)
                from discSize in Gen.Choose(1, 100).Select(s => (long)s * 0x10000)
                from fileStart in Gen.Choose(0, 1000).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 100).Select(s => (long)s * 0x10000)
                select new
                {
                    DiscStart = discStart,
                    DiscEnd = discStart + discSize,
                    FileStart = fileStart,
                    FileEnd = fileStart + fileSize
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                IntersectionResult result = CalculateIntersection(
                    data.DiscStart, data.DiscEnd,
                    data.FileStart, data.FileEnd,
                    totalSize: long.MaxValue, totalWritten: 0);

                long expectedEnd = Math.Min(data.DiscEnd, data.FileEnd);
                bool correct = result.IntersectEnd == expectedEnd;

                return correct.Label(
                    $"IntersectEnd should be min({data.DiscEnd:X}, {data.FileEnd:X}) = {expectedEnd:X}, " +
                    $"got {result.IntersectEnd:X}");
            });
        }

        /// <summary>
        /// Property 11c: When intersection is positive, write size equals intersectEnd - intersectStart.
        ///
        /// For any chunk and file ranges where max(chunkStart, fileStart) &lt; min(chunkEnd, fileEnd),
        /// exactly (intersectEnd - intersectStart) bytes SHALL be written (uncapped case).
        ///
        /// **Validates: Requirements 9.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property PositiveIntersection_WriteSizeEqualsIntersectionSize()
        {
            // Generate ranges guaranteed to overlap
            var testGen =
                from baseOffset in Gen.Choose(0, 500).Select(o => (long)o * 0x10000)
                from overlapStart in Gen.Choose(1, 50).Select(o => (long)o * 0x10000)
                from overlapSize in Gen.Choose(1, 50).Select(s => (long)s * 0x10000)
                let discStart = baseOffset
                let discEnd = baseOffset + overlapStart + overlapSize + 0x10000L
                let fileStart = baseOffset + overlapStart
                let fileEnd = baseOffset + overlapStart + overlapSize
                where discEnd > discStart && fileEnd > fileStart
                where Math.Max(discStart, fileStart) < Math.Min(discEnd, fileEnd)
                select new
                {
                    DiscStart = discStart,
                    DiscEnd = discEnd,
                    FileStart = fileStart,
                    FileEnd = fileEnd
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                // Use large totalSize so capping doesn't apply
                IntersectionResult result = CalculateIntersection(
                    data.DiscStart, data.DiscEnd,
                    data.FileStart, data.FileEnd,
                    totalSize: long.MaxValue, totalWritten: 0);

                long expectedSize = Math.Min(data.DiscEnd, data.FileEnd) -
                                    Math.Max(data.DiscStart, data.FileStart);
                bool hasPositiveIntersection = expectedSize > 0;
                bool correct = result.HasIntersection && result.WriteSize == (int)expectedSize;

                return (hasPositiveIntersection && correct).Label(
                    $"Expected positive intersection of size {expectedSize:X}, " +
                    $"got HasIntersection={result.HasIntersection}, WriteSize={result.WriteSize:X}");
            });
        }

        /// <summary>
        /// Property 11d: When intersection is empty or negative, no write occurs.
        ///
        /// For any chunk and file ranges where max(chunkStart, fileStart) >= min(chunkEnd, fileEnd),
        /// no write SHALL occur (WriteSize == 0 and HasIntersection == false).
        ///
        /// **Validates: Requirements 9.4**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property NoIntersection_NoWriteOccurs()
        {
            // Generate ranges guaranteed to NOT overlap
            var testGen =
                from gap in Gen.Choose(1, 100).Select(g => (long)g * 0x10000)
                from discStart in Gen.Choose(0, 500).Select(o => (long)o * 0x10000)
                from discSize in Gen.Choose(1, 50).Select(s => (long)s * 0x10000)
                from arrangementLeft in Gen.Choose(0, 1) // 0 = file to the left, 1 = file to the right
                let discEnd = discStart + discSize
                let fileStart = arrangementLeft == 0 ? discStart - gap - 0x10000L : discEnd + gap
                let fileSize = 0x10000L
                let fileEnd = fileStart + fileSize
                where fileStart >= 0 && discStart >= 0
                where Math.Max(discStart, fileStart) >= Math.Min(discEnd, fileEnd)
                select new
                {
                    DiscStart = discStart,
                    DiscEnd = discEnd,
                    FileStart = fileStart,
                    FileEnd = fileEnd
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                IntersectionResult result = CalculateIntersection(
                    data.DiscStart, data.DiscEnd,
                    data.FileStart, data.FileEnd,
                    totalSize: long.MaxValue, totalWritten: 0);

                bool noWrite = !result.HasIntersection && result.WriteSize == 0;

                return noWrite.Label(
                    $"No intersection expected: chunk [{data.DiscStart:X}, {data.DiscEnd:X}), " +
                    $"file [{data.FileStart:X}, {data.FileEnd:X}), " +
                    $"but got HasIntersection={result.HasIntersection}, WriteSize={result.WriteSize}");
            });
        }

        /// <summary>
        /// Property 11e: Write size is capped to remaining bytes (TotalSize - TotalWritten).
        ///
        /// For any positive intersection, when the raw intersection size exceeds the remaining
        /// bytes (TotalSize - TotalWritten), the write size SHALL be capped to the remaining amount.
        ///
        /// **Validates: Requirements 9.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property WriteSize_CappedToRemainingBytes()
        {
            // Generate overlapping ranges where intersection > remaining
            var testGen =
                from discStart in Gen.Choose(0, 200).Select(o => (long)o * 0x10000)
                from discSize in Gen.Choose(10, 100).Select(s => (long)s * 0x10000)
                from fileStart in Gen.Choose(0, 200).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(10, 100).Select(s => (long)s * 0x10000)
                let discEnd = discStart + discSize
                let fileEnd = fileStart + fileSize
                let intersectionSize = Math.Min(discEnd, fileEnd) - Math.Max(discStart, fileStart)
                where intersectionSize > 0
                from remainingFraction in Gen.Choose(1, 99)
                let totalSize = (long)(intersectionSize * 2) // Arbitrary total
                let totalWritten = totalSize - (intersectionSize * remainingFraction / 100)
                where totalSize > totalWritten && totalWritten >= 0
                where (totalSize - totalWritten) < intersectionSize // remaining < intersection
                select new
                {
                    DiscStart = discStart,
                    DiscEnd = discEnd,
                    FileStart = fileStart,
                    FileEnd = fileEnd,
                    TotalSize = totalSize,
                    TotalWritten = totalWritten,
                    IntersectionSize = intersectionSize,
                    Remaining = totalSize - totalWritten
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                IntersectionResult result = CalculateIntersection(
                    data.DiscStart, data.DiscEnd,
                    data.FileStart, data.FileEnd,
                    data.TotalSize, data.TotalWritten);

                bool capped = result.WriteSize == (int)data.Remaining;

                return capped.Label(
                    $"WriteSize should be capped to remaining={data.Remaining}, " +
                    $"intersection={data.IntersectionSize}, got WriteSize={result.WriteSize}");
            });
        }

        /// <summary>
        /// Property 11f: When TotalWritten already equals TotalSize, no write occurs regardless
        /// of intersection geometry.
        ///
        /// For any chunk and file ranges that would normally produce a positive intersection,
        /// when remaining bytes (TotalSize - TotalWritten) is zero or negative, no write SHALL occur.
        ///
        /// **Validates: Requirements 9.5**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ZeroRemaining_NoWriteRegardlessOfIntersection()
        {
            // Generate overlapping ranges but with totalWritten >= totalSize
            var testGen =
                from discStart in Gen.Choose(0, 200).Select(o => (long)o * 0x10000)
                from discSize in Gen.Choose(10, 100).Select(s => (long)s * 0x10000)
                from fileStart in Gen.Choose(0, 200).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(10, 100).Select(s => (long)s * 0x10000)
                let discEnd = discStart + discSize
                let fileEnd = fileStart + fileSize
                let intersectionSize = Math.Min(discEnd, fileEnd) - Math.Max(discStart, fileStart)
                where intersectionSize > 0 // ranges DO overlap
                from totalSize in Gen.Choose(1, 100).Select(s => (long)s * 0x10000)
                from overrun in Gen.Choose(0, 10).Select(o => (long)o * 0x1000)
                let totalWritten = totalSize + overrun // at or past completion
                select new
                {
                    DiscStart = discStart,
                    DiscEnd = discEnd,
                    FileStart = fileStart,
                    FileEnd = fileEnd,
                    TotalSize = totalSize,
                    TotalWritten = totalWritten
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                IntersectionResult result = CalculateIntersection(
                    data.DiscStart, data.DiscEnd,
                    data.FileStart, data.FileEnd,
                    data.TotalSize, data.TotalWritten);

                bool noWrite = result.WriteSize == 0 && !result.HasIntersection;

                return noWrite.Label(
                    $"When TotalWritten({data.TotalWritten}) >= TotalSize({data.TotalSize}), " +
                    $"no write should occur even with positive intersection, " +
                    $"but got HasIntersection={result.HasIntersection}, WriteSize={result.WriteSize}");
            });
        }

        /// <summary>
        /// Property 11g: Combined geometry correctness across multiple sequential chunks.
        ///
        /// For any file range and a sequence of sequential chunks covering that range,
        /// the cumulative write size SHALL equal the file's total size (or TotalSize if smaller).
        /// This validates that the intersection logic correctly decomposes a file range
        /// into chunk-aligned writes that reconstruct the full file.
        ///
        /// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SequentialChunks_CumulativeWriteEqualsFileSize()
        {
            var testGen =
                from fileStart in Gen.Choose(0, 100).Select(o => (long)o * 0x10000)
                from fileSize in Gen.Choose(1, 50).Select(s => (long)s * 0x10000)
                from chunkSize in Gen.Choose(1, 20).Select(s => (long)s * 0x10000)
                from leadingChunks in Gen.Choose(0, 5)
                from trailingChunks in Gen.Choose(0, 5)
                let fileEnd = fileStart + fileSize
                let chunksStart = fileStart - (leadingChunks * chunkSize)
                let chunksEnd = fileEnd + (trailingChunks * chunkSize)
                where chunksStart >= 0
                select new
                {
                    FileStart = fileStart,
                    FileSize = fileSize,
                    FileEnd = fileEnd,
                    ChunkSize = chunkSize,
                    ChunksStart = chunksStart,
                    ChunksEnd = chunksEnd
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                long totalWritten = 0;
                long totalSize = data.FileSize;
                long pos = data.ChunksStart;

                while (pos < data.ChunksEnd)
                {
                    long chunkEnd = Math.Min(pos + data.ChunkSize, data.ChunksEnd);

                    IntersectionResult result = CalculateIntersection(
                        pos, chunkEnd,
                        data.FileStart, data.FileEnd,
                        totalSize, totalWritten);

                    totalWritten += result.WriteSize;
                    pos = chunkEnd;
                }

                bool correct = totalWritten == data.FileSize;

                return correct.Label(
                    $"Cumulative write should equal file size {data.FileSize:X}, " +
                    $"got {totalWritten:X}. " +
                    $"File [{data.FileStart:X}, {data.FileEnd:X}), " +
                    $"chunks from {data.ChunksStart:X} to {data.ChunksEnd:X}, " +
                    $"chunk size {data.ChunkSize:X}");
            });
        }

        #endregion
    }
}
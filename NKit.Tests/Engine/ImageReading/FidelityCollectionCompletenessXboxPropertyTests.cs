using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Microsoft.XBox;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Property-based tests verifying fidelity collection completeness for Xbox FstContext.
    ///
    /// Feature: filesystem-fidelity-preservation, Property 1: Fidelity collection completeness (Xbox)
    ///
    /// For any sequence of N file declarations (with arbitrary offset collision patterns),
    /// the Xbox FidelityFileList.Count SHALL equal the expected count after all insertions complete.
    ///
    /// For Xbox: same-offset same-size entries do NOT create new fidelity entries
    /// (reference already present from first insert).
    /// For Xbox: same-offset different-size entries DO create new fidelity entries.
    ///
    /// Count = number of unique offsets (first insert at each offset) + number of different-size
    /// collisions at existing offsets.
    ///
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class FidelityCollectionCompletenessXboxPropertyTests
    {
        /// <summary>
        /// Creates a minimal Xbox FstContext suitable for testing AddFile behavior.
        /// The XDvdFsHeader constructor creates the FstContext internally.
        /// </summary>
        private static FstContext CreateTestXboxFstContext(long fsSize = 0x1000000)
        {
            AreaInfo areaInfo = new AreaInfo(0, AreaType.FileSystem, 0);
            byte[] headerData = new byte[0x800]; // minimal header buffer
            XDvdFsHeader header = new XDvdFsHeader(headerData, areaInfo, fsSize);
            return header.FstContext;
        }

        /// <summary>
        /// Computes the expected fidelity count for a sequence of file declarations
        /// following Xbox FstContext rules:
        /// - First insert at a unique offset: adds to fidelity (+1)
        /// - Same offset + same size: no new fidelity entry (+0)
        /// - Same offset + different size: adds separate fidelity entry (+1)
        /// </summary>
        private static int ComputeExpectedFidelityCount(List<(long Offset, long Size)> declarations)
        {
            int count = 0;
            // Track what's been inserted: offset -> size of first entry at that offset
            Dictionary<long, long> insertedOffsets = new Dictionary<long, long>();

            foreach ((long offset, long size) in declarations)
            {
                if (!insertedOffsets.ContainsKey(offset))
                {
                    // New unique offset: first insert adds to fidelity
                    insertedOffsets[offset] = size;
                    count++;
                }
                else
                {
                    long existingSize = insertedOffsets[offset];
                    if (existingSize != size)
                    {
                        // Same offset + different size: separate fidelity entry
                        count++;
                    }
                    // Same offset + same size: no new fidelity entry
                }
            }

            return count;
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.3**
        ///
        /// Property 1: Fidelity collection completeness (Xbox variant).
        /// For any sequence of file declarations with arbitrary offset collision patterns,
        /// the Xbox FidelityFileList.Count SHALL equal the expected count after all insertions:
        ///   expected = unique_offsets + different_size_collisions
        /// (same-size collisions don't add new fidelity entries since the reference is already present).
        /// </summary>
        [Property(MaxTest = 200)]
        public Property FidelityCount_Equals_UniqueOffsets_Plus_DifferentSizeCollisions()
        {
            // Generate a list of file declarations with controlled collision patterns.
            // Use a small offset pool to force collisions.
            Gen<List<(long Offset, long Size)>> declarationGen =
                from count in Gen.Choose(1, 30)
                from poolSize in Gen.Choose(1, Math.Max(1, count / 2))
                from offsetPool in Gen.Choose(1, 100).ListOf(poolSize)
                    .Select(l => l.Distinct().Select(o => (long)o * 0x800).ToList())
                    .Where(l => l.Count > 0)
                from offsets in Gen.Elements(offsetPool.ToArray()).ListOf(count)
                from sizes in Gen.OneOf(
                    Gen.Constant(0L),
                    Gen.Choose(1, 50).Select(s => (long)s * 0x800)
                ).ListOf(count)
                select offsets.Zip(sizes, (o, s) => (Offset: o, Size: s)).ToList();

            return Prop.ForAll(declarationGen.ToArbitrary(), declarations =>
            {
                FstContext ctx = CreateTestXboxFstContext();
                FstFolder root = new FstFolder("root", null);

                foreach ((long offset, long size) in declarations)
                {
                    ctx.AddFile(root, $"file_{offset:X}_{size:X}.dat",
                        FsItemType.File, offset, size);
                }

                int expectedCount = ComputeExpectedFidelityCount(declarations);
                int actualCount = ctx.FidelityFiles.Count;

                return (actualCount == expectedCount)
                    .Label($"Expected fidelity count {expectedCount} but got {actualCount} " +
                           $"for {declarations.Count} declarations");
            });
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.3**
        ///
        /// Property 1 (supplementary): When all offsets are unique (no collisions),
        /// the fidelity count equals the total number of declarations.
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AllUniqueOffsets_FidelityCount_EqualsDeclarationCount()
        {
            // Generate declarations with strictly unique offsets (each file at a distinct offset)
            Gen<List<(long Offset, long Size)>> uniqueOffsetsGen =
                from count in Gen.Choose(1, 50)
                from sizes in Gen.Choose(0, 100).Select(s => (long)s * 0x800).ListOf(count)
                select Enumerable.Range(0, count)
                    .Select(i => (Offset: (long)(i + 1) * 0x800, Size: sizes[i]))
                    .ToList();

            return Prop.ForAll(uniqueOffsetsGen.ToArbitrary(), declarations =>
            {
                FstContext ctx = CreateTestXboxFstContext();
                FstFolder root = new FstFolder("root", null);

                foreach ((long offset, long size) in declarations)
                {
                    ctx.AddFile(root, $"file_{offset:X}_{size:X}.dat",
                        FsItemType.File, offset, size);
                }

                int actualCount = ctx.FidelityFiles.Count;

                return (actualCount == declarations.Count)
                    .Label($"Expected fidelity count {declarations.Count} (all unique offsets) " +
                           $"but got {actualCount}");
            });
        }
    }
}
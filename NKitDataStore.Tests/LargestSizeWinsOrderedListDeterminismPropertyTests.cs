using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Feature: nkfs-multi-extent-files, Property 7: First-registered-wins OrderedList behavior
    ///
    /// For any sequence of AddFile calls to FstContext where two or more files share the same
    /// FsOffset but have different FsSize values:
    /// - The OrderedList SHALL contain the FIRST-registered entry at each offset (not replaced)
    /// - Larger incoming entries MERGE as links (don't replace or create collisions)
    /// - Smaller incoming entries create COLLISION fidelity entries
    /// - Same-size incoming entries MERGE as links
    /// - The behavior is order-DEPENDENT (first insert determines the OrderedList entry's size)
    ///
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.5**
    /// </summary>
    public class LargestSizeWinsOrderedListDeterminismPropertyTests
    {
        private static readonly FsType[] ValidFsTypes = new[]
        {
            FsType.Iso9660,
            FsType.Joliet,
            FsType.RockRidge,
            FsType.Udf,
            FsType.Other
        };

        /// <summary>
        /// Creates a minimal ISO9660 FstContext suitable for testing AddFile behavior.
        /// </summary>
        private static FstContext CreateTestFstContext()
        {
            AreaInfo areaInfo = new AreaInfo(0, AreaType.FileSystem, 0);
            byte[] headerData = new byte[0x8000 * 0x40]; // minimal header buffer
            ImageHeader header = new ImageHeader(headerData, areaInfo);
            return header.FstContext;
        }

        /// <summary>
        /// Represents a single AddFile call with offset, size, and filesystem type.
        /// </summary>
        private record struct FileDeclaration(long Offset, long Size, FsType FsType, string Name);

        /// <summary>
        /// Feature: nkfs-multi-extent-files, Property 7: First-registered-wins OrderedList behavior
        ///
        /// **Validates: Requirements 11.1, 11.2, 11.3, 11.5**
        ///
        /// Generate sequences of AddFile calls where multiple files share the same FsOffset
        /// but have different FsSize values. Verify:
        /// - OrderedList contains only the FIRST-registered entry at each offset
        /// - Larger entries merge as links (don't create new fidelity entries)
        /// - Smaller entries create collision fidelity entries
        /// - One entry per unique offset in OrderedList
        /// </summary>
        [Property(MaxTest = 100)]
        public Property OrderedList_ContainsLargestAtEachOffset_AndDisplacedInFidelity()
        {
            // Generate file declarations: use a small offset pool to force collisions,
            // with varying sizes at the same offset.
            Gen<List<FileDeclaration>> declarationGen =
                from offsetCount in Gen.Choose(1, 8)
                from offsets in Gen.Choose(1, 200).Select(o => (long)o * 0x800)
                    .ListOf(offsetCount).Select(l => l.Distinct().ToList()).Where(l => l.Count > 0)
                from entriesPerOffset in Gen.Choose(2, 5).ListOf(offsets.Count)
                from seed in Gen.Choose(1, int.MaxValue)
                let declarations = GenerateDeclarations(offsets, entriesPerOffset, seed)
                where declarations.Count > 0
                select declarations;

            return Prop.ForAll(declarationGen.ToArbitrary(), declarations =>
            {
                // --- Run 1: original insertion order ---
                FstContext ctx1 = CreateTestFstContext();
                FstFolder root1 = new FstFolder(FsType.Iso9660);
                foreach (FileDeclaration decl in declarations)
                {
                    ctx1.AddFile(root1, decl.Name, decl.FsType, decl.Offset, decl.Size, FsItemType.File);
                }

                // Compute expected: FIRST-registered size at each offset
                Dictionary<long, long> expectedFirstByOffset = new Dictionary<long, long>();
                foreach (FileDeclaration decl in declarations)
                {
                    if (!expectedFirstByOffset.ContainsKey(decl.Offset))
                        expectedFirstByOffset[decl.Offset] = decl.Size;
                }

                // Verify OrderedList has exactly one entry per unique offset
                List<long> uniqueOffsets = declarations.Select(d => d.Offset).Distinct().ToList();
                bool correctCount = ctx1.FileSystem.Count == uniqueOffsets.Count;

                // Verify OrderedList has the FIRST-registered size at each offset
                bool firstRegisteredWins = true;
                foreach (long offset in uniqueOffsets)
                {
                    int idx = ctx1.FileSystem.KeyIndex(offset, out bool exists);
                    if (!exists)
                    {
                        firstRegisteredWins = false;
                        break;
                    }
                    IFsFile entry = ctx1.FileSystem[idx];
                    if (entry.FsSize != expectedFirstByOffset[offset])
                    {
                        firstRegisteredWins = false;
                        break;
                    }
                }

                // Verify fidelity count matches expected:
                // - First insert at each offset: +1
                // - Same size: merge (no new entry)
                // - Larger size: merge (no new entry)
                // - Smaller size (non-zero, different name): collision (+1)
                // - Zero size: merge (no new entry)
                int expectedFidelityCount = ComputeExpectedFidelityCount(declarations);
                bool fidelityCorrect = ctx1.FidelityFiles.Count == expectedFidelityCount;

                // Verify collision (smaller-size) entries are in fidelity
                bool collisionsInFidelity = VerifyCollisionEntriesInFidelity(declarations, ctx1);

                return correctCount
                    .Label($"OrderedList count ({ctx1.FileSystem.Count}) should equal unique offsets ({uniqueOffsets.Count})")
                    .And(firstRegisteredWins)
                    .Label("OrderedList should contain the first-registered entry at each offset")
                    .And(fidelityCorrect)
                    .Label($"FidelityFiles count ({ctx1.FidelityFiles.Count}) should equal expected ({expectedFidelityCount})")
                    .And(collisionsInFidelity)
                    .Label("All collision (smaller-size) entries should appear in FidelityFileList");
            });
        }

        /// <summary>
        /// Generates file declarations ensuring multiple files at the same offset have different sizes.
        /// </summary>
        private static List<FileDeclaration> GenerateDeclarations(
            List<long> offsets, IList<int> entriesPerOffset, int seed)
        {
            List<FileDeclaration> declarations = new List<FileDeclaration>();
            Random rng = new Random(seed);

            for (int i = 0; i < offsets.Count; i++)
            {
                long offset = offsets[i];
                int count = entriesPerOffset[i];

                // Generate distinct sizes for this offset to ensure collision with different sizes
                HashSet<long> sizes = new HashSet<long>();
                while (sizes.Count < count)
                {
                    sizes.Add((long)rng.Next(1, 500) * 0x800);
                }

                int j = 0;
                foreach (long size in sizes)
                {
                    FsType fsType = ValidFsTypes[rng.Next(ValidFsTypes.Length)];
                    declarations.Add(new FileDeclaration(offset, size, fsType,
                        $"file_{offset:X}_{size:X}_{j}"));
                    j++;
                }
            }

            return declarations;
        }

        /// <summary>
        /// Computes expected fidelity count following the first-registered-wins rules:
        /// - First insert at a unique offset: adds to fidelity (+1)
        /// - Same offset + larger size + NOT overlapping next: merges as link (+0)
        /// - Same offset + larger size + overlapping next: collision (+1)
        /// - Same offset + smaller size (non-zero, different name): collision (+1)
        /// - Same offset + same size: merges as FstLink, no new fidelity entry (+0)
        /// - Same offset + zero size: merges as link (+0)
        /// </summary>
        private static int ComputeExpectedFidelityCount(List<FileDeclaration> declarations)
        {
            int count = 0;
            // Track: offset -> first-registered size in OrderedList (simulates the OrderedList)
            SortedDictionary<long, long> orderedList = new SortedDictionary<long, long>();

            foreach (FileDeclaration decl in declarations)
            {
                if (!orderedList.ContainsKey(decl.Offset))
                {
                    // New unique offset: first insert adds to fidelity
                    orderedList[decl.Offset] = decl.Size;
                    count++;
                }
                else
                {
                    long existingSize = orderedList[decl.Offset];
                    if (decl.Size == existingSize)
                    {
                        // Same size: merge as FstLink, no new fidelity entry
                    }
                    else if (decl.Size == 0 || existingSize == 0)
                    {
                        // Zero size (either): merge
                    }
                    else if (decl.Size > existingSize)
                    {
                        // Larger: check if it overlaps the next offset in the OrderedList
                        bool largerOverlapsNext = false;
                        foreach (long nextOffset in orderedList.Keys)
                        {
                            if (nextOffset > decl.Offset)
                            {
                                largerOverlapsNext = (decl.Offset + decl.Size) > nextOffset;
                                break;
                            }
                        }
                        if (largerOverlapsNext)
                        {
                            // Goes to collision branch
                            count++;
                        }
                        // else: merge as link (first-registered size kept)
                    }
                    else
                    {
                        // Smaller + both non-zero: collision
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Verifies that all collision entries have a corresponding
        /// entry in the FidelityFileList with the same offset and size.
        /// </summary>
        private static bool VerifyCollisionEntriesInFidelity(
            List<FileDeclaration> declarations, FstContext ctx)
        {
            // Simulate the OrderedList to determine which entries are collisions
            SortedDictionary<long, long> orderedList = new SortedDictionary<long, long>();
            List<(long Offset, long Size)> expectedCollisions = new List<(long Offset, long Size)>();

            foreach (FileDeclaration decl in declarations)
            {
                if (!orderedList.ContainsKey(decl.Offset))
                {
                    orderedList[decl.Offset] = decl.Size;
                }
                else
                {
                    long existingSize = orderedList[decl.Offset];
                    if (decl.Size == existingSize || decl.Size == 0 || existingSize == 0)
                    {
                        // Merge — not a collision
                    }
                    else if (decl.Size > existingSize)
                    {
                        // Check largerOverlapsNext
                        bool largerOverlapsNext = false;
                        foreach (long nextOffset in orderedList.Keys)
                        {
                            if (nextOffset > decl.Offset)
                            {
                                largerOverlapsNext = (decl.Offset + decl.Size) > nextOffset;
                                break;
                            }
                        }
                        if (largerOverlapsNext)
                            expectedCollisions.Add((decl.Offset, decl.Size));
                    }
                    else
                    {
                        // Smaller + both non-zero: collision
                        expectedCollisions.Add((decl.Offset, decl.Size));
                    }
                }
            }

            // Verify each collision entry exists in fidelity
            List<(long FsOffset, long FsSize)> fidelityEntries = ctx.FidelityFiles.Entries
                .Select(f => (f.FsOffset, f.FsSize))
                .ToList();

            foreach ((long offset, long size) in expectedCollisions)
            {
                int idx = fidelityEntries.FindIndex(e => e.FsOffset == offset && e.FsSize == size);
                if (idx < 0)
                    return false;
                fidelityEntries.RemoveAt(idx);
            }

            return true;
        }
    }
}
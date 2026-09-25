using FsCheck;
using FsCheck.Fluent;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Operation types that can be performed on a set, used by crash recovery property tests
    /// to simulate failures during each type of mutating operation.
    /// </summary>
    public enum OperationType
    {
        /// <summary>Adding a new image to the set.</summary>
        AddImage,

        /// <summary>Soft-deleting an existing live image.</summary>
        DeleteImage,

        /// <summary>Restoring a previously deleted image.</summary>
        RestoreImage,

        /// <summary>Rolling back a previously deleted image (same as restore in this context).</summary>
        RollbackImage,

        /// <summary>Compacting the set to reclaim space from removed images.</summary>
        CompactSet
    }

    /// <summary>
    /// Crash points representing where a crash can occur during various operations.
    /// Used by crash recovery property tests to simulate failures at every intermediate step.
    /// </summary>
    public enum CrashPoint
    {
        /// <summary>Crash before AtomicCommit begins (secondary header not yet written).</summary>
        BeforeCommit,

        /// <summary>Crash after secondary header written but before primary header written.</summary>
        DuringCommitAfterSecondary,

        /// <summary>Crash after primary header written (commit completed) but before cleanup.</summary>
        DuringCommitAfterPrimary,

        /// <summary>Crash after AtomicCommit completes successfully.</summary>
        AfterCommit,

        /// <summary>Crash during File.Replace (or fallback Delete+Move) of the index file.</summary>
        DuringReplace,

        /// <summary>Crash after File.Delete of original but before File.Move of temp (fallback path).</summary>
        AfterDeleteBeforeMove,

        /// <summary>Crash during shard file replacement (CompactShard uses File.Replace).</summary>
        DuringShardReplace,

        /// <summary>Crash after shard compaction but before index commit.</summary>
        AfterShardCompactBeforeCommit,

        /// <summary>Crash during block write to shard (partial block appended).</summary>
        DuringBlockWrite,

        /// <summary>Crash during metadata section write (section appended but directory not updated).</summary>
        DuringMetadataWrite,

        /// <summary>Crash during embedded extraction (after .tmp written, before first rename).</summary>
        DuringExtractionBeforeRename,

        /// <summary>Crash during embedded extraction (after first rename, before second rename).</summary>
        DuringExtractionBetweenRenames,

        /// <summary>Crash during re-embed (after index appended to shard, before footer written).</summary>
        DuringReEmbedBeforeFooter,

        /// <summary>Crash during re-embed (after footer written, before rename completes).</summary>
        DuringReEmbedAfterFooter
    }

    /// <summary>
    /// Represents the state of a single image in a test set for crash recovery testing.
    /// </summary>
    public class ImageState
    {
        /// <summary>1-based image ID.</summary>
        public int ImageId { get; init; }

        /// <summary>Image name.</summary>
        public string Name { get; init; } = "";

        /// <summary>Number of blocks in this image (1-4).</summary>
        public int BlockCount { get; init; }

        /// <summary>Block data for this image (BlockCount * BlockSize bytes).</summary>
        public byte[] Data { get; init; } = Array.Empty<byte>();

        /// <summary>Whether this image has been soft-deleted (removed).</summary>
        public bool IsRemoved { get; init; }
    }

    /// <summary>
    /// Represents a complete set state for crash recovery testing.
    /// Contains 1-10 images with random block data and removal patterns.
    /// </summary>
    public class SetState
    {
        /// <summary>The images in this set.</summary>
        public ImageState[] Images { get; init; } = Array.Empty<ImageState>();

        /// <summary>Block size used for this set (always 65536).</summary>
        public int BlockSize { get; init; } = 65536;

        /// <summary>Shard size used for this set.</summary>
        public long ShardSize { get; init; } = 50L * 1024 * 1024 * 1024;

        /// <summary>Number of images in the set.</summary>
        public int ImageCount => Images.Length;

        /// <summary>Images that are live (not removed).</summary>
        public ImageState[] LiveImages => Images.Where(i => !i.IsRemoved).ToArray();

        /// <summary>Images that are removed.</summary>
        public ImageState[] RemovedImages => Images.Where(i => i.IsRemoved).ToArray();

        public override string ToString() =>
            $"SetState(Images={ImageCount}, Live={LiveImages.Length}, Removed={RemovedImages.Length}, " +
            $"BlockSize={BlockSize})";
    }

    /// <summary>
    /// Represents a file in an intermediate file system state (for crash simulation).
    /// </summary>
    public class IntermediateFile
    {
        /// <summary>Relative file path (e.g., "TestSet.nkds", "TestSet.nkds.compact.tmp").</summary>
        public string RelativePath { get; init; } = "";

        /// <summary>Whether this file exists in the intermediate state.</summary>
        public bool Exists { get; init; }

        /// <summary>Whether this file has valid header content.</summary>
        public bool HasValidHeader { get; init; }

        /// <summary>Whether this file has partial/corrupt content.</summary>
        public bool IsPartial { get; init; }

        /// <summary>Size of the file in bytes (0 if not exists).</summary>
        public long Size { get; init; }

        public override string ToString() =>
            $"{RelativePath}(Exists={Exists}, Valid={HasValidHeader}, Partial={IsPartial}, Size={Size})";
    }

    /// <summary>
    /// Represents a complete intermediate file system state after a simulated crash.
    /// Describes which files exist, which are missing, and which have partial content.
    /// </summary>
    public class IntermediateState
    {
        /// <summary>The crash point that produced this state.</summary>
        public CrashPoint CrashPoint { get; init; }

        /// <summary>Files present in this intermediate state.</summary>
        public IntermediateFile[] Files { get; init; } = Array.Empty<IntermediateFile>();

        /// <summary>Whether the original index file exists.</summary>
        public bool OriginalIndexExists => Files.Any(f =>
            f.RelativePath.EndsWith(".nkds") &&
            !f.RelativePath.Contains(".compact.tmp") &&
            !f.RelativePath.Contains("_") &&
            f.Exists);

        /// <summary>Whether a .compact.tmp file exists.</summary>
        public bool CompactTmpExists => Files.Any(f =>
            f.RelativePath.EndsWith(".compact.tmp") && f.Exists);

        /// <summary>Whether any shard .tmp files exist.</summary>
        public bool ShardTmpFilesExist => Files.Any(f =>
            f.RelativePath.Contains("_") &&
            f.RelativePath.EndsWith(".tmp") &&
            f.Exists);

        /// <summary>Whether any shard files have orphaned tail data.</summary>
        public bool HasOrphanedTailData { get; init; }

        /// <summary>Amount of orphaned bytes (if applicable).</summary>
        public int OrphanedBytes { get; init; }

        public override string ToString() =>
            $"IntermediateState(CrashPoint={CrashPoint}, Files={Files.Length}, " +
            $"OriginalIndex={OriginalIndexExists}, CompactTmp={CompactTmpExists}, " +
            $"ShardTmp={ShardTmpFilesExist}, Orphaned={HasOrphanedTailData})";
    }

    /// <summary>
    /// FsCheck generators for crash recovery property testing.
    ///
    /// **Validates: Requirements 9.8**
    ///
    /// Provides:
    /// - ArbSetState: random sets with 1-10 images, 1-4 blocks per image, random removal patterns
    /// - ArbCrashPoint: enum values for crash points
    /// - ArbIntermediateState: file system states representing various crash outcomes
    /// </summary>
    public static class CrashRecoveryGenerators
    {
        /// <summary>
        /// Generates a random SetState with 1-10 images, each with 1-4 blocks of 65536 bytes,
        /// and a random subset marked as removed. At least one image is always live.
        /// </summary>
        public static Gen<SetState> GenSetState()
        {
            return from imageCount in Gen.Choose(1, 10)
                   from blockCounts in Gen.ArrayOf(Gen.Choose(1, 4), imageCount)
                   from removedFlags in GenRemovalPattern(imageCount)
                   from seed in Gen.Choose(0, int.MaxValue)
                   select BuildSetState(imageCount, blockCounts, removedFlags, seed);
        }

        /// <summary>
        /// Returns an Arbitrary for SetState.
        /// </summary>
        public static Arbitrary<SetState> ArbSetState() => GenSetState().ToArbitrary();

        /// <summary>
        /// Generates a random CrashPoint enum value.
        /// </summary>
        public static Gen<CrashPoint> GenCrashPoint()
        {
            CrashPoint[] allValues = Enum.GetValues<CrashPoint>();
            return Gen.Elements(allValues);
        }

        /// <summary>
        /// Returns an Arbitrary for CrashPoint.
        /// </summary>
        public static Arbitrary<CrashPoint> ArbCrashPoint() => GenCrashPoint().ToArbitrary();

        /// <summary>
        /// Generates a random IntermediateState representing a file system state after a crash.
        /// The generated state is consistent with the crash point (e.g., if crash is BeforeCommit,
        /// the original index exists and no .compact.tmp is present).
        /// </summary>
        public static Gen<IntermediateState> GenIntermediateState()
        {
            return from crashPoint in GenCrashPoint()
                   from orphanedBytes in Gen.Choose(0, 512)
                   from hasOrphaned in Gen.Elements(true, false)
                   from shardCount in Gen.Choose(1, 4)
                   select BuildIntermediateState(crashPoint, hasOrphaned, orphanedBytes, shardCount);
        }

        /// <summary>
        /// Returns an Arbitrary for IntermediateState.
        /// </summary>
        public static Arbitrary<IntermediateState> ArbIntermediateState() => GenIntermediateState().ToArbitrary();

        /// <summary>
        /// Generates a CrashPoint filtered to only commit-related crash points
        /// (useful for testing AtomicCommit recovery specifically).
        /// </summary>
        public static Gen<CrashPoint> GenCommitCrashPoint()
        {
            return Gen.Elements(
                CrashPoint.BeforeCommit,
                CrashPoint.DuringCommitAfterSecondary,
                CrashPoint.DuringCommitAfterPrimary,
                CrashPoint.AfterCommit);
        }

        /// <summary>
        /// Generates a CrashPoint filtered to only replacement-related crash points
        /// (useful for testing File.Replace recovery specifically).
        /// </summary>
        public static Gen<CrashPoint> GenReplaceCrashPoint()
        {
            return Gen.Elements(
                CrashPoint.DuringReplace,
                CrashPoint.AfterDeleteBeforeMove,
                CrashPoint.DuringShardReplace,
                CrashPoint.AfterShardCompactBeforeCommit);
        }

        /// <summary>
        /// Generates a CrashPoint filtered to only embedded-mode crash points.
        /// </summary>
        public static Gen<CrashPoint> GenEmbeddedCrashPoint()
        {
            return Gen.Elements(
                CrashPoint.DuringExtractionBeforeRename,
                CrashPoint.DuringExtractionBetweenRenames,
                CrashPoint.DuringReEmbedBeforeFooter,
                CrashPoint.DuringReEmbedAfterFooter);
        }

        /// <summary>
        /// Generates a SetState with at least one removed image (useful for compaction tests).
        /// </summary>
        public static Gen<SetState> GenSetStateWithRemovals()
        {
            return from imageCount in Gen.Choose(2, 10)
                   from blockCounts in Gen.ArrayOf(Gen.Choose(1, 4), imageCount)
                   from removedFlags in GenRemovalPatternWithAtLeastOne(imageCount)
                   from seed in Gen.Choose(0, int.MaxValue)
                   select BuildSetState(imageCount, blockCounts, removedFlags, seed);
        }

        /// <summary>
        /// Returns an Arbitrary for SetState that always has at least one removed image.
        /// </summary>
        public static Arbitrary<SetState> ArbSetStateWithRemovals() => GenSetStateWithRemovals().ToArbitrary();

        #region Private Helpers

        /// <summary>
        /// Generates a removal pattern where at least one image is live.
        /// Each image has a 30% chance of being removed.
        /// </summary>
        private static Gen<bool[]> GenRemovalPattern(int imageCount)
        {
            if (imageCount <= 1)
                return Gen.Constant(new[] { false });

            // Generate random flags, then ensure at least one is live
            return Gen.ArrayOf(
                Gen.Frequency((3, Gen.Constant(false)), (1, Gen.Constant(true))),
                imageCount)
                .Select(flags =>
                {
                    // Ensure at least one image is live
                    if (flags.All(f => f))
                        flags[0] = false;
                    return flags;
                });
        }

        /// <summary>
        /// Generates a removal pattern where at least one image is removed AND at least one is live.
        /// </summary>
        private static Gen<bool[]> GenRemovalPatternWithAtLeastOne(int imageCount)
        {
            return Gen.ArrayOf(
                Gen.Frequency((2, Gen.Constant(false)), (1, Gen.Constant(true))),
                imageCount)
                .Select(flags =>
                {
                    // Ensure at least one is live
                    if (flags.All(f => f))
                        flags[0] = false;
                    // Ensure at least one is removed
                    if (flags.All(f => !f))
                        flags[flags.Length - 1] = true;
                    return flags;
                });
        }

        /// <summary>
        /// Builds a SetState from the generated parameters.
        /// Uses a seeded Random for deterministic block data generation.
        /// </summary>
        private static SetState BuildSetState(int imageCount, int[] blockCounts, bool[] removedFlags, int seed)
        {
            const int blockSize = 65536;
            Random rnd = new Random(seed);

            ImageState[] images = new ImageState[imageCount];
            for (int i = 0; i < imageCount; i++)
            {
                int blocks = blockCounts[i];
                byte[] data = new byte[blocks * blockSize];
                rnd.NextBytes(data);

                images[i] = new ImageState
                {
                    ImageId = i + 1,
                    Name = $"Image{i + 1}.iso",
                    BlockCount = blocks,
                    Data = data,
                    IsRemoved = removedFlags[i]
                };
            }

            return new SetState
            {
                Images = images,
                BlockSize = blockSize,
                ShardSize = 50L * 1024 * 1024 * 1024
            };
        }

        /// <summary>
        /// Builds an IntermediateState consistent with the given crash point.
        /// The file layout reflects what would exist on disk after a crash at that point.
        /// </summary>
        private static IntermediateState BuildIntermediateState(
            CrashPoint crashPoint, bool hasOrphaned, int orphanedBytes, int shardCount)
        {
            List<IntermediateFile> files = new List<IntermediateFile>();
            string setName = "TestSet";

            switch (crashPoint)
            {
                case CrashPoint.BeforeCommit:
                    // Crash before commit: original index exists, no temp files
                    // Shard may have orphaned tail data from partial write
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = hasOrphaned,
                            Size = (65536 * i) + (hasOrphaned ? orphanedBytes : 0)
                        });
                    }
                    break;

                case CrashPoint.DuringCommitAfterSecondary:
                    // Secondary header written, primary not yet written
                    // Index file exists with valid secondary but corrupt/stale primary
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true, // secondary is valid
                        IsPartial = true, // primary is stale
                        Size = 4096
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 65536 * i
                        });
                    }
                    break;

                case CrashPoint.DuringCommitAfterPrimary:
                case CrashPoint.AfterCommit:
                    // Both headers written, commit completed
                    // Clean state (or nearly clean — just needs lock release)
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 65536 * i
                        });
                    }
                    break;

                case CrashPoint.DuringReplace:
                    // During File.Replace: both original and .compact.tmp may exist
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds.compact.tmp",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 3072
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 65536 * i
                        });
                    }
                    break;

                case CrashPoint.AfterDeleteBeforeMove:
                    // Original deleted, .compact.tmp not yet moved
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = false,
                        HasValidHeader = false,
                        IsPartial = false,
                        Size = 0
                    });
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds.compact.tmp",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 3072
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 65536 * i
                        });
                    }
                    break;

                case CrashPoint.DuringShardReplace:
                    // Shard .tmp file exists alongside original shard
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 65536 * i
                        });
                        // First shard has a .tmp file (mid-replacement)
                        if (i == 1)
                        {
                            files.Add(new IntermediateFile
                            {
                                RelativePath = $"{setName}_{i:D4}.nkds.tmp",
                                Exists = true,
                                HasValidHeader = false,
                                IsPartial = false,
                                Size = 32768
                            });
                        }
                    }
                    break;

                case CrashPoint.AfterShardCompactBeforeCommit:
                    // Shards compacted (replaced) but index not yet committed
                    // Shards have new content, index still points to old offsets
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 32768 * i // smaller after compaction
                        });
                    }
                    break;

                case CrashPoint.DuringBlockWrite:
                    // Partial block appended to shard (orphaned tail)
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        bool isAffectedShard = i == shardCount; // last shard has partial write
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = isAffectedShard,
                            Size = (65536 * i) + (isAffectedShard ? orphanedBytes : 0)
                        });
                    }
                    break;

                case CrashPoint.DuringMetadataWrite:
                    // Metadata section appended but directory not updated (orphaned tail in index)
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = true, // has unreferenced metadata bytes
                        Size = 4096 + orphanedBytes
                    });
                    for (int i = 1; i <= shardCount; i++)
                    {
                        files.Add(new IntermediateFile
                        {
                            RelativePath = $"{setName}_{i:D4}.nkds",
                            Exists = true,
                            HasValidHeader = false,
                            IsPartial = false,
                            Size = 65536 * i
                        });
                    }
                    break;

                case CrashPoint.DuringExtractionBeforeRename:
                    // Embedded mode: .tmp file written but not yet renamed
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096 + (65536 * shardCount) // embedded: index + data
                    });
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds.tmp",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    break;

                case CrashPoint.DuringExtractionBetweenRenames:
                    // Embedded mode: first rename done, second not yet
                    // Original renamed to shard, .tmp exists as new index
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds.tmp",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}_{1:D4}.nkds",
                        Exists = true,
                        HasValidHeader = false,
                        IsPartial = false,
                        Size = 65536 * shardCount
                    });
                    break;

                case CrashPoint.DuringReEmbedBeforeFooter:
                    // Re-embed: index appended to shard but footer not written
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}_{1:D4}.nkds",
                        Exists = true,
                        HasValidHeader = false,
                        IsPartial = true, // has appended index but no footer
                        Size = 65536 + 4096 // shard data + appended index without footer
                    });
                    break;

                case CrashPoint.DuringReEmbedAfterFooter:
                    // Re-embed: footer written but rename not completed
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}.nkds",
                        Exists = true,
                        HasValidHeader = true,
                        IsPartial = false,
                        Size = 4096
                    });
                    files.Add(new IntermediateFile
                    {
                        RelativePath = $"{setName}_{1:D4}.nkds",
                        Exists = true,
                        HasValidHeader = false,
                        IsPartial = false,
                        Size = 65536 + 4096 + 16 // shard data + index + footer
                    });
                    break;
            }

            return new IntermediateState
            {
                CrashPoint = crashPoint,
                Files = files.ToArray(),
                HasOrphanedTailData = hasOrphaned && (
                    crashPoint == CrashPoint.BeforeCommit ||
                    crashPoint == CrashPoint.DuringBlockWrite ||
                    crashPoint == CrashPoint.DuringMetadataWrite),
                OrphanedBytes = hasOrphaned ? orphanedBytes : 0
            };
        }

        /// <summary>
        /// Generates a random OperationType enum value.
        /// </summary>
        public static Gen<OperationType> GenOperationType()
        {
            OperationType[] allValues = Enum.GetValues<OperationType>();
            return Gen.Elements(allValues);
        }

        /// <summary>
        /// Returns an Arbitrary for OperationType.
        /// </summary>
        public static Arbitrary<OperationType> ArbOperationType() => GenOperationType().ToArbitrary();

        /// <summary>
        /// Generates a CrashPoint appropriate for the given operation type.
        /// Different operations have different valid crash points.
        /// </summary>
        public static Gen<CrashPoint> GenCrashPointForOperation(OperationType operationType)
        {
            return operationType switch
            {
                OperationType.AddImage => Gen.Elements(
                    CrashPoint.BeforeCommit,
                    CrashPoint.DuringCommitAfterSecondary,
                    CrashPoint.DuringBlockWrite,
                    CrashPoint.DuringMetadataWrite),

                OperationType.DeleteImage => Gen.Elements(
                    CrashPoint.BeforeCommit,
                    CrashPoint.DuringCommitAfterSecondary,
                    CrashPoint.DuringCommitAfterPrimary,
                    CrashPoint.AfterCommit),

                OperationType.RestoreImage => Gen.Elements(
                    CrashPoint.BeforeCommit,
                    CrashPoint.DuringCommitAfterSecondary,
                    CrashPoint.DuringCommitAfterPrimary,
                    CrashPoint.AfterCommit),

                OperationType.RollbackImage => Gen.Elements(
                    CrashPoint.BeforeCommit,
                    CrashPoint.DuringCommitAfterSecondary,
                    CrashPoint.DuringCommitAfterPrimary,
                    CrashPoint.AfterCommit),

                OperationType.CompactSet => Gen.Elements(
                    CrashPoint.DuringReplace,
                    CrashPoint.AfterDeleteBeforeMove,
                    CrashPoint.DuringShardReplace,
                    CrashPoint.AfterShardCompactBeforeCommit),

                _ => GenCrashPoint()
            };
        }

        #endregion
    }
}
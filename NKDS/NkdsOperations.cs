using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using NKDS.Models;
using NKDS.Mount;
using NKDS.Validation;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System.Diagnostics;

namespace NKDS;

/// <summary>
/// Primary implementation of INkdsOperations.
/// Thin orchestration layer that delegates to DataStore for storage operations
/// and NKitProcessor for pipeline operations, adding progress/cancellation support.
/// </summary>
public sealed class NkdsOperations : INkdsOperations
{
    private readonly MountService _mountService = new();
    public OperationResult CreateSet(string dataStorePath, string setName, long shardSize, int blockSize)
    {
        Stopwatch sw = Stopwatch.StartNew();

        // Validate inputs
        (bool nameValid, string nameError) = NkdsValidation.ValidateSetName(setName);
        if (!nameValid)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = setName ?? "", Reason = nameError! }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        (bool shardValid, string shardError) = NkdsValidation.ValidateShardSize(shardSize);
        if (!shardValid)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = "shardSize", Reason = shardError! }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        (bool blockValid, string blockError) = NkdsValidation.ValidateBlockSize(blockSize);
        if (!blockValid)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = "blockSize", Reason = blockError! }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        try
        {
            using DataStore dataStore = new DataStore(dataStorePath);
            dataStore.CreateSet(setName, shardSize, blockSize);

            return new OperationResult
            {
                Success = true,
                ItemsProcessed = 1,
                ItemsFailed = 0,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = setName, Reason = ex.Message }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }
    }

    public OperationResult Remove(string dataStorePath, string setName, IReadOnlyList<long> imageIds)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
        int processed = 0;

        try
        {
            using DataStore dataStore = new DataStore(dataStorePath);

            foreach (long imageId in imageIds)
            {
                try
                {
                    GlobalImageKey key = new GlobalImageKey(setName, imageId);
                    dataStore.DeleteImage(key);
                    processed++;
                }
                catch (Exception ex)
                {
                    errors.Add(new OperationErrorEntry
                    {
                        ItemName = $"Image {imageId}",
                        Reason = ex.Message
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // DataStore-level failure (e.g., cannot open the store)
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = dataStorePath, Reason = ex.Message }],
                ItemsProcessed = processed,
                ItemsFailed = imageIds.Count - processed,
                Duration = sw.Elapsed
            };
        }

        return new OperationResult
        {
            Success = errors.Count == 0,
            Errors = errors,
            ItemsProcessed = imageIds.Count,
            ItemsFailed = errors.Count,
            Duration = sw.Elapsed
        };
    }

    public OperationResult Restore(string dataStorePath, string setName, IReadOnlyList<long> imageIds)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
        int processed = 0;

        try
        {
            using DataStore dataStore = new DataStore(dataStorePath);

            foreach (long imageId in imageIds)
            {
                try
                {
                    GlobalImageKey key = new GlobalImageKey(setName, imageId);
                    dataStore.RestoreImage(key);
                    processed++;
                }
                catch (Exception ex)
                {
                    errors.Add(new OperationErrorEntry
                    {
                        ItemName = $"Image {imageId}",
                        Reason = ex.Message
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = dataStorePath, Reason = ex.Message }],
                ItemsProcessed = processed,
                ItemsFailed = imageIds.Count - processed,
                Duration = sw.Elapsed
            };
        }

        return new OperationResult
        {
            Success = errors.Count == 0,
            Errors = errors,
            ItemsProcessed = imageIds.Count,
            ItemsFailed = errors.Count,
            Duration = sw.Elapsed
        };
    }

    public OperationResult Rollback(string dataStorePath, string setName, long targetImageId, IProgress<NKDS.Models.OperationProgress> progress = null)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            using DataStore dataStore = new DataStore(dataStorePath);
            GlobalImageKey key = new GlobalImageKey(setName, targetImageId);

            Progress<(int Percentage, string Stage)> stageProgress = progress != null
                ? new Progress<(int Percentage, string Stage)>(p =>
                    progress.Report(new NKDS.Models.OperationProgress
                    {
                        Percentage = p.Percentage,
                        CurrentItem = p.Stage,
                        ItemsProcessed = 0,
                        TotalItems = 1,
                        Elapsed = sw.Elapsed
                    }))
                : null;

            dataStore.RollbackImage(key, stageProgress);

            return new OperationResult
            {
                Success = true,
                ItemsProcessed = 1,
                ItemsFailed = 0,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = $"Image {targetImageId}", Reason = ex.Message }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }
    }

    public OperationResult GetSets(string dataStorePath)
    {
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            using DataStore dataStore = new DataStore(dataStorePath);
            List<string> setNames = dataStore.ListSetNames().ToList();

            return new OperationResult
            {
                Success = true,
                ItemsProcessed = setNames.Count,
                ItemsFailed = 0,
                Duration = sw.Elapsed,
                DuplicateItems = setNames // Reuse DuplicateItems to carry set names back to caller
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = dataStorePath, Reason = ex.Message }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }
    }

    public StatsResult GetStats(string dataStorePath, string setName, bool includePerImageStats,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<DataStoreStatistics> statistics = new List<DataStoreStatistics>();
        List<OperationErrorEntry> errors = new List<OperationErrorEntry>();

        try
        {
            using DataStore dataStore = new DataStore(dataStorePath);

            IEnumerable<string> setNames;
            if (!string.IsNullOrWhiteSpace(setName))
            {
                setNames = [setName];
            }
            else
            {
                setNames = dataStore.ListSetNames().ToList();
            }

            List<string> setList = setNames.ToList();
            int total = setList.Count;
            int processed = 0;

            foreach (string name in setList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    DataStoreStatistics stats = dataStore.GetSetStatistics(name, includePerImageStats);
                    if (stats != null)
                        statistics.Add(stats);
                    else
                        errors.Add(new OperationErrorEntry { ItemName = name, Reason = "Set not found." });
                }
                catch (Exception ex)
                {
                    errors.Add(new OperationErrorEntry { ItemName = name, Reason = ex.Message });
                }

                processed++;
                progress?.Report(new OperationProgress
                {
                    Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                    CurrentItem = name,
                    ItemsProcessed = processed,
                    TotalItems = total,
                    Elapsed = sw.Elapsed
                });
            }
        }
        catch (OperationCanceledException)
        {
            return new StatsResult
            {
                Success = false,
                SetStatistics = statistics,
                Errors = errors,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add(new OperationErrorEntry { ItemName = dataStorePath, Reason = ex.Message });
            return new StatsResult
            {
                Success = false,
                SetStatistics = statistics,
                Errors = errors,
                Duration = sw.Elapsed
            };
        }

        return new StatsResult
        {
            Success = errors.Count == 0,
            SetStatistics = statistics,
            Errors = errors,
            Duration = sw.Elapsed
        };
    }

    // --- Async methods ---

    public Task<OperationResult> AddAsync(string dataStorePath, string setName, IReadOnlyList<string> filePaths,
        IProgress<OperationProgress> progress = null,
        Action<ImageCommittedEvent> onImageCommitted = null,
        Action<ImageProgressEvent> onImageProgress = null,
        Action<string, long, string> onImageOutput = null,
        CancellationToken cancellationToken = default,
        string shardSizeText = null,
        string blockSizeText = null,
        string auxModeText = null,
        ResolvedKeyFixPaths keyFixPaths = null)
    {
        return Task.Run(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
            List<string> duplicates = new List<string>();
            int processed = 0;

            try
            {
                // Ensure DataStore directory exists
                if (!Directory.Exists(dataStorePath))
                    Directory.CreateDirectory(dataStorePath);

                // Build AppSettings for the import pipeline with all file paths as inputs
                string[] args = buildImportArgs(dataStorePath, setName, filePaths, shardSizeText, blockSizeText, auxModeText, keyFixPaths);
                AppSettings settings = new AppSettings(null, args);

                // Scan and group the input files using the NKit infrastructure
                using Log log = settings.GetLog((_, _) => { });
                if (!settings.Check(log))
                {
                    return new OperationResult
                    {
                        Success = false,
                        Errors = [new OperationErrorEntry { ItemName = dataStorePath, Reason = "No input files were specified." }],
                        ItemsProcessed = 0,
                        ItemsFailed = 1,
                        Duration = sw.Elapsed
                    };
                }

                List<SourceFile> images = SourceFiles.ScanGrouped(settings.In, settings.R, settings.Arc, true, log, cancellationToken);
                int total = images.Count;

                // Track existing image IDs so we can identify newly committed images
                HashSet<long> existingImageIds = null;
                if (onImageCommitted != null)
                {
                    try
                    {
                        using DataStore ds = new DataStore(dataStorePath);
                        existingImageIds = ds.ListImagesInSet(setName)
                            .Select(img => img.Id)
                            .ToHashSet();
                    }
                    catch
                    {
                        existingImageIds = new HashSet<long>();
                    }
                }

                // Pre-assign temporary negative IDs for progress tracking.
                // Images don't have a real ID until committed to the DataStore, but we need
                // an ID to target progress/output updates at the correct UI row.
                // We use negative IDs (starting at -1) as temporary placeholders.
                long nextTempId = -1;

                foreach (SourceFile sourceFile in images)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string itemName = DisambiguatedNameHelper.Compute(sourceFile);

                    // Per-image progress throttling state
                    float lastEmittedProgress = -1f;
                    long currentImageId = nextTempId--;

                    // Insert a placeholder row into the UI with Processing status before pipeline runs.
                    // This allows progress and output events to target the row during processing.
                    if (onImageCommitted != null)
                    {
                        onImageCommitted(new ImageCommittedEvent
                        {
                            Image = new ImageRecord
                            {
                                Id = currentImageId,
                                SetName = setName,
                                Name = itemName,
                                Size = sourceFile.Length,
                                Crc32 = 0,
                                XxHash64 = 0
                            },
                            SetName = setName,
                            SessionId = dataStorePath,
                            IsProcessing = true
                        });
                    }

                    try
                    {
                        // Task 7.4: Wire consoleLog callback to onImageOutput
                        Action<string, LogLevel> consoleLog = (msg, _) =>
                        {
                            if (onImageOutput != null && !string.IsNullOrEmpty(msg))
                                onImageOutput(setName, currentImageId, msg);
                        };

                        NKitProcessor processor = new NKitProcessor(settings, sourceFile, consoleLog);

                        // Task 7.3: Subscribe to ProgressEvent for per-image progress
                        if (onImageProgress != null || progress != null)
                        {
                            processor.ProgressEvent += (_, e) =>
                            {
                                float stepProgress = e.Progress;
                                float overallProgress = e.ProgressTotal;

                                // Throttle: only fire when delta >= 0.005 or at start/complete
                                bool isStartOrComplete = e.IsStart || e.IsComplete;
                                bool exceedsThreshold = Math.Abs(overallProgress - lastEmittedProgress) >= 0.005f;

                                if (!isStartOrComplete && !exceedsThreshold)
                                    return;

                                lastEmittedProgress = overallProgress;

                                string stepName = e.Steps != null && e.Step < e.Steps.Length ? e.Steps[e.Step] : $"Step {e.Step + 1}";

                                onImageProgress?.Invoke(new ImageProgressEvent
                                {
                                    SetName = setName,
                                    ImageId = currentImageId,
                                    StepName = stepName,
                                    StepProgress = stepProgress,
                                    OverallProgress = overallProgress,
                                    StepIndex = e.Step,
                                    StepTotal = e.StepTotal,
                                    Steps = e.Steps
                                });

                                // Also update the operation-level progress bar with per-file granularity.
                                // ProgressTotal (0.0-1.0) represents overall progress within this single file.
                                // Combine with batch position: (processed + fileProgress) / total * 100
                                progress?.Report(new OperationProgress
                                {
                                    Percentage = total > 0 ? (int)((processed + overallProgress) * 100L / total) : 0,
                                    CurrentItem = $"{itemName} \u2014 {stepName}",
                                    ItemsProcessed = processed,
                                    TotalItems = total,
                                    Elapsed = sw.Elapsed
                                });
                            };
                        }

                        NKitTaskResults result = processor.Process(cancellationToken);

                        // A re-added image already in the set is reported via ImageAlreadyExists (the
                        // dedupe pipeline rolls it back at FinalizeImage); ImageSkipped is the
                        // system-filter/no-op skip. Both mean nothing was committed, so treat both as
                        // the duplicate/skip case (otherwise the UI row stays on "Add Processing").
                        if (result.ImageAlreadyExists || result.ImageSkipped)
                        {
                            duplicates.Add(itemName);
                            bool alreadyExists = result.ImageAlreadyExists;

                            // Fire onImageCommitted distinguishing "already exists" from a plain skip.
                            if (onImageCommitted != null)
                            {
                                onImageCommitted(new ImageCommittedEvent
                                {
                                    Image = new ImageRecord
                                    {
                                        Id = currentImageId,
                                        SetName = setName,
                                        Name = itemName,
                                        Size = 0,
                                        Crc32 = 0,
                                        XxHash64 = 0
                                    },
                                    SetName = setName,
                                    SessionId = dataStorePath,
                                    WasAlreadyExists = alreadyExists,
                                    WasSkipped = !alreadyExists,
                                    Reason = alreadyExists ? "Already exists in set" : "Skipped"
                                });
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(result.ErrorMsg))
                        {
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = itemName,
                                Reason = result.ErrorMsg
                            });

                            // Task 11.1: Fire callbacks so UI can show Failed status
                            onImageOutput?.Invoke(setName, currentImageId, $"[Error] {result.ErrorMsg}");
                            onImageCommitted?.Invoke(new ImageCommittedEvent
                            {
                                Image = new ImageRecord
                                {
                                    Id = currentImageId,
                                    SetName = setName,
                                    Name = itemName,
                                    Size = 0,
                                    Crc32 = 0,
                                    XxHash64 = 0
                                },
                                SetName = setName,
                                SessionId = dataStorePath,
                                WasFailed = true,
                                Reason = result.ErrorMsg
                            });
                        }
                        else
                        {
                            // Task 7.2: Image committed successfully — find the ImageRecord and fire callback.
                            // Note on aux mode (Req 9.1): When aux store routing is active, the NKit pipeline
                            // routes update partition blocks to the aux set internally (via DedupeStep/DataStoreWiiFormatter),
                            // but the image record is always committed to the primary set identified by `setName`.
                            // We query ListImagesInSet(setName) which correctly finds the image in the primary set.
                            if (onImageCommitted != null)
                            {
                                try
                                {
                                    using DataStore ds = new DataStore(dataStorePath);
                                    ImageRecord committedImage = ds.ListImagesInSet(setName)
                                        .Where(img => !img.Removed
                                            && string.Equals(img.Name, itemName, StringComparison.OrdinalIgnoreCase)
                                            && (existingImageIds == null || !existingImageIds.Contains(img.Id)))
                                        .OrderByDescending(img => img.Id)
                                        .FirstOrDefault();

                                    if (committedImage != null)
                                    {
                                        long tempId = currentImageId;
                                        currentImageId = committedImage.Id;
                                        existingImageIds?.Add(committedImage.Id);

                                        // The image data IS committed (CRC/XxHash are correct), but the
                                        // pipeline may still have FAILED verification (e.g. a CHD source
                                        // whose stored hash cannot match the produced image). A commit that
                                        // did not verify must report a DEFINITIVE Failed status — never a
                                        // blank one. Verify was only actually attempted when the pipeline
                                        // ran a verifying step (VerifyResult != Unverified); an Unverified
                                        // result means verification was not requested, so it is not a failure.
                                        bool verifyAttempted = result.VerifyResult != VerifyResult.Unverified;
                                        bool verifyFailed = verifyAttempted && result.VerifyResult != VerifyResult.VerifySuccess;

                                        onImageCommitted(new ImageCommittedEvent
                                        {
                                            Image = committedImage,
                                            SetName = setName,
                                            SessionId = dataStorePath,
                                            TempId = tempId,
                                            IsVerified = result.VerifyResult == VerifyResult.VerifySuccess,
                                            WasFailed = verifyFailed,
                                            Reason = verifyFailed
                                                ? $"Added but verification failed ({result.VerifyResult})"
                                                : null
                                        });
                                    }
                                }
                                catch
                                {
                                    // Don't let callback failures break the batch
                                }
                            }
                        }

                        processed++;
                    }
                    catch (OperationCanceledException)
                    {
                        // Task 10.1: Notify UI that the current image was cancelled
                        if (onImageCommitted != null)
                        {
                            onImageCommitted(new ImageCommittedEvent
                            {
                                Image = new ImageRecord
                                {
                                    Id = currentImageId,
                                    SetName = setName,
                                    Name = itemName,
                                    Size = 0,
                                    Crc32 = 0,
                                    XxHash64 = 0
                                },
                                SetName = setName,
                                SessionId = dataStorePath,
                                WasSkipped = false,
                                WasCancelled = true,
                                Reason = "Operation cancelled by user"
                            });
                        }
                        throw; // Re-throw to be caught by outer handler
                    }
                    catch (HandledException ex)
                    {
                        string reason = ex.FriendlyErrorMessage.TrimEnd();
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = reason
                        });

                        // Task 11.1: Fire callbacks so UI can show Failed status
                        onImageOutput?.Invoke(setName, currentImageId, $"[Error] {reason}");
                        onImageCommitted?.Invoke(new ImageCommittedEvent
                        {
                            Image = new ImageRecord
                            {
                                Id = currentImageId,
                                SetName = setName,
                                Name = itemName,
                                Size = 0,
                                Crc32 = 0,
                                XxHash64 = 0
                            },
                            SetName = setName,
                            SessionId = dataStorePath,
                            WasFailed = true,
                            Reason = reason
                        });

                        processed++;
                    }
                    catch (Exception ex)
                    {
                        string reason = ex.Message;
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = reason
                        });

                        // Task 11.1: Fire callbacks so UI can show Failed status
                        onImageOutput?.Invoke(setName, currentImageId, $"[Error] {reason}");
                        onImageCommitted?.Invoke(new ImageCommittedEvent
                        {
                            Image = new ImageRecord
                            {
                                Id = currentImageId,
                                SetName = setName,
                                Name = itemName,
                                Size = 0,
                                Crc32 = 0,
                                XxHash64 = 0
                            },
                            SetName = setName,
                            SessionId = dataStorePath,
                            WasFailed = true,
                            Reason = reason
                        });

                        processed++;
                    }

                    progress?.Report(new OperationProgress
                    {
                        Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                        CurrentItem = itemName,
                        ItemsProcessed = processed,
                        TotalItems = total,
                        Elapsed = sw.Elapsed
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = true,
                    Errors = errors,
                    ItemsProcessed = processed,
                    ItemsFailed = errors.Count,
                    Duration = sw.Elapsed,
                    DuplicateItems = duplicates
                };
            }

            return new OperationResult
            {
                Success = errors.Count == 0,
                Errors = errors,
                ItemsProcessed = processed,
                ItemsFailed = errors.Count,
                Duration = sw.Elapsed,
                DuplicateItems = duplicates
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Scans all input paths (including archives) and returns the complete list
    /// of candidate images with disambiguated names and assigned temp IDs.
    /// Does NOT process any images. Skips unreadable archives gracefully.
    /// </summary>
    public Task<IReadOnlyList<CandidateImage>> PreScanAsync(
        string dataStorePath,
        string setName,
        IReadOnlyList<string> filePaths,
        ResolvedKeyFixPaths keyFixPaths = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            List<CandidateImage> candidates = new List<CandidateImage>();

            // Build AppSettings from file paths (reuse existing buildImportArgs pattern)
            string[] args = buildImportArgs(dataStorePath, setName, filePaths, null, null, null, keyFixPaths);
            AppSettings settings = new AppSettings(null, args);

            using Log log = settings.GetLog((_, _) => { });
            if (!settings.Check(log))
                return (IReadOnlyList<CandidateImage>)candidates;

            List<SourceFile> images;
            try
            {
                images = SourceFiles.ScanGrouped(settings.In, settings.R, settings.Arc, true, log, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Wrap unreadable archive exceptions — skip scanning entirely if the
                // top-level scan fails (e.g., corrupt archive). Return empty list.
                return (IReadOnlyList<CandidateImage>)candidates;
            }

            long nextTempId = -1;

            foreach (SourceFile sourceFile in images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Skip entries that won't be processable (archives containing archives,
                    // index-only entries, empty archives, entries with no data, etc.)
                    // Only include entries with Valid status — these are the actual images
                    // that the NKitProcessor pipeline will process.
                    if (sourceFile.Status != SourceFileResult.Valid)
                        continue;

                    string name = DisambiguatedNameHelper.Compute(sourceFile);

                    candidates.Add(new CandidateImage
                    {
                        TempId = nextTempId--,
                        DisambiguatedName = name,
                        Size = sourceFile.Length,
                        Source = sourceFile,
                        Index = candidates.Count
                    });
                }
                catch
                {
                    // Skip unreadable entries (e.g., corrupt archive members) and continue
                }
            }

            return (IReadOnlyList<CandidateImage>)candidates;
        }, cancellationToken);
    }

    /// <summary>
    /// Processes pre-scanned candidate images sequentially.
    /// Skips the internal ScanGrouped call — uses the provided CandidateImage list directly.
    /// Callbacks fire per-image for progress, status, and commit events.
    /// </summary>
    public Task<OperationResult> AddPreScannedAsync(
        string dataStorePath,
        string setName,
        IReadOnlyList<CandidateImage> candidates,
        Action<ImageCommittedEvent> onImageCommitted = null,
        Action<ImageProgressEvent> onImageProgress = null,
        Action<string, long, string> onImageOutput = null,
        Action<int, int> onItemStarted = null,
        CancellationToken cancellationToken = default,
        ResolvedKeyFixPaths keyFixPaths = null,
        string auxModeText = null)
    {
        return Task.Run(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
            List<string> duplicates = new List<string>();
            int processed = 0;
            int total = candidates.Count(c => !c.Source.IsSyntheticFolder);

            if (total == 0)
            {
                return new OperationResult
                {
                    Success = true,
                    Errors = errors,
                    ItemsProcessed = 0,
                    ItemsFailed = 0,
                    Duration = sw.Elapsed,
                    DuplicateItems = duplicates
                };
            }

            try
            {
                // Ensure DataStore directory exists
                if (!Directory.Exists(dataStorePath))
                    Directory.CreateDirectory(dataStorePath);

                // Build AppSettings for the pipeline (we need settings for NKitProcessor)
                // The -in paths are not used (we iterate candidates directly, skipping ScanGrouped),
                // but AppSettings requires at least one -in path for initialization.
                // Use dataStorePath as a dummy input — only -out, -task, and -dedupe matter.
                string[] args = buildImportArgs(dataStorePath, setName, new List<string> { dataStorePath }, null, null, auxModeText, keyFixPaths);
                AppSettings settings = new AppSettings(null, args);

                // Initialize the log — NKitProcessor and steps require settings._log to be initialized.
                // Without this, internal code paths throw NullReferenceException.
                using Log log = settings.GetLog((_, _) => { });

                // Track existing image IDs so we can identify newly committed images
                HashSet<long> existingImageIds = null;
                if (onImageCommitted != null)
                {
                    try
                    {
                        using DataStore ds = new DataStore(dataStorePath);
                        existingImageIds = ds.ListImagesInSet(setName)
                            .Select(img => img.Id)
                            .ToHashSet();
                    }
                    catch
                    {
                        existingImageIds = new HashSet<long>();
                    }
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    CandidateImage candidate = candidates[i];
                    long currentImageId = candidate.TempId;
                    string itemName = candidate.DisambiguatedName;

                    // Check for cancellation before processing each image.
                    // On cancellation, do NOT throw — instead mark remaining items as AddCancelled.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // Transition currently-pending items (from this point onward) to AddCancelled
                        for (int j = i; j < candidates.Count; j++)
                        {
                            CandidateImage remaining = candidates[j];
                            onImageCommitted?.Invoke(new ImageCommittedEvent
                            {
                                Image = new ImageRecord
                                {
                                    Id = remaining.TempId,
                                    SetName = setName,
                                    Name = remaining.DisambiguatedName,
                                    Size = 0,
                                    Crc32 = 0,
                                    XxHash64 = 0
                                },
                                SetName = setName,
                                SessionId = dataStorePath,
                                WasCancelled = true,
                                Reason = "Operation cancelled by user"
                            });
                        }

                        return new OperationResult
                        {
                            Success = false,
                            WasCancelled = true,
                            Errors = errors,
                            ItemsProcessed = processed,
                            ItemsFailed = errors.Count,
                            Duration = sw.Elapsed,
                            DuplicateItems = duplicates
                        };
                    }

                    // Fire onItemStarted for counter display (e.g., "1/65")
                    // Skip synthetic folder entries — they're processed silently (no UI row)
                    bool isSyntheticFolder = candidate.Source.IsSyntheticFolder;
                    if (!isSyntheticFolder)
                    {
                        onItemStarted?.Invoke(processed, total);

                        // Transition status to Processing (rows already exist as Pending —
                        // do NOT fire IsProcessing=true committed event, just signal status change via progress)
                        onImageProgress?.Invoke(new ImageProgressEvent
                        {
                            SetName = setName,
                            ImageId = currentImageId,
                            StepName = "Processing",
                            StepProgress = 0f,
                            OverallProgress = 0f,
                            StepIndex = 0,
                            StepTotal = 0,
                            Steps = null
                        });
                    }

                    // Per-image progress throttling state
                    float lastEmittedProgress = -1f;

                    try
                    {
                        // Wire consoleLog callback to onImageOutput
                        Action<string, LogLevel> consoleLog = (msg, _) =>
                        {
                            if (onImageOutput != null && !string.IsNullOrEmpty(msg))
                                onImageOutput(setName, currentImageId, msg);
                        };

                        NKitProcessor processor = new NKitProcessor(settings, candidate.Source, consoleLog);

                        // Subscribe to ProgressEvent for per-image progress
                        if (onImageProgress != null)
                        {
                            processor.ProgressEvent += (_, e) =>
                            {
                                float stepProgress = e.Progress;
                                float overallProgress = e.ProgressTotal;

                                // Throttle: only fire when delta >= 0.005 or at start/complete
                                bool isStartOrComplete = e.IsStart || e.IsComplete;
                                bool exceedsThreshold = Math.Abs(overallProgress - lastEmittedProgress) >= 0.005f;

                                if (!isStartOrComplete && !exceedsThreshold)
                                    return;

                                lastEmittedProgress = overallProgress;

                                string stepName = e.Steps != null && e.Step < e.Steps.Length ? e.Steps[e.Step] : $"Step {e.Step + 1}";

                                onImageProgress.Invoke(new ImageProgressEvent
                                {
                                    SetName = setName,
                                    ImageId = currentImageId,
                                    StepName = stepName,
                                    StepProgress = stepProgress,
                                    OverallProgress = overallProgress,
                                    StepIndex = e.Step,
                                    StepTotal = e.StepTotal,
                                    Steps = e.Steps
                                });
                            };
                        }

                        NKitTaskResults result = processor.Process(cancellationToken);

                        // A re-added image already in the set is reported via ImageAlreadyExists
                        // (the dedupe pipeline detects it at FinalizeImage and rolls the transaction
                        // back). ImageSkipped covers the system-filter/no-op skip. Either means the
                        // image was NOT committed, so treat both as the duplicate/skip case — otherwise
                        // the success branch below finds no committed row and the UI row stays stuck on
                        // "Add Processing".
                        if (result.ImageAlreadyExists || result.ImageSkipped)
                        {
                            // Nothing was committed: either the image already exists in the set
                            // (ImageAlreadyExists — rolled back at FinalizeImage) or it was a system
                            // filter / no-op skip (ImageSkipped). Report them distinctly so the UI can
                            // show "Already Exists" vs "Skipped".
                            duplicates.Add(itemName);
                            bool alreadyExists = result.ImageAlreadyExists;

                            onImageCommitted?.Invoke(new ImageCommittedEvent
                            {
                                Image = new ImageRecord
                                {
                                    Id = currentImageId,
                                    SetName = setName,
                                    Name = itemName,
                                    Size = 0,
                                    Crc32 = 0,
                                    XxHash64 = 0
                                },
                                SetName = setName,
                                SessionId = dataStorePath,
                                WasAlreadyExists = alreadyExists,
                                WasSkipped = !alreadyExists,
                                Reason = alreadyExists ? "Already exists in set" : "Skipped"
                            });
                        }
                        else if (!string.IsNullOrWhiteSpace(result.ErrorMsg))
                        {
                            // Pipeline reported an error — transition to Failed
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = itemName,
                                Reason = result.ErrorMsg
                            });

                            onImageOutput?.Invoke(setName, currentImageId, $"[Error] {result.ErrorMsg}");
                            onImageCommitted?.Invoke(new ImageCommittedEvent
                            {
                                Image = new ImageRecord
                                {
                                    Id = currentImageId,
                                    SetName = setName,
                                    Name = itemName,
                                    Size = 0,
                                    Crc32 = 0,
                                    XxHash64 = 0
                                },
                                SetName = setName,
                                SessionId = dataStorePath,
                                WasFailed = true,
                                Reason = result.ErrorMsg
                            });
                        }
                        else
                        {
                            // Success — find committed ImageRecord and fire callback
                            if (onImageCommitted != null)
                            {
                                try
                                {
                                    using DataStore ds = new DataStore(dataStorePath);
                                    ImageRecord committedImage = ds.ListImagesInSet(setName)
                                        .Where(img => !img.Removed
                                            && string.Equals(img.Name, itemName, StringComparison.OrdinalIgnoreCase)
                                            && (existingImageIds == null || !existingImageIds.Contains(img.Id)))
                                        .OrderByDescending(img => img.Id)
                                        .FirstOrDefault();

                                    if (committedImage != null)
                                    {
                                        existingImageIds?.Add(committedImage.Id);

                                        // The image data IS committed, but the pipeline may still have
                                        // FAILED verification (e.g. a CD-style CHD/cue that does not
                                        // round-trip through the DataStore). A commit that did not verify
                                        // must report a DEFINITIVE Failed status — never a blank one.
                                        // Unverified means verification was not requested, not a failure.
                                        bool verifyAttempted = result.VerifyResult != VerifyResult.Unverified;
                                        bool verifyFailed = verifyAttempted && result.VerifyResult != VerifyResult.VerifySuccess;

                                        onImageCommitted(new ImageCommittedEvent
                                        {
                                            Image = committedImage,
                                            SetName = setName,
                                            SessionId = dataStorePath,
                                            TempId = currentImageId,
                                            IsVerified = result.VerifyResult == VerifyResult.VerifySuccess,
                                            WasFailed = verifyFailed,
                                            Reason = verifyFailed
                                                ? $"Added but verification failed ({result.VerifyResult})"
                                                : null
                                        });
                                    }
                                }
                                catch
                                {
                                    // Don't let callback failures break the batch
                                }
                            }
                        }

                        if (!isSyntheticFolder) processed++;
                    }
                    catch (OperationCanceledException)
                    {
                        // Mid-pipeline cancellation: mark the current image as Cancelled
                        onImageCommitted?.Invoke(new ImageCommittedEvent
                        {
                            Image = new ImageRecord
                            {
                                Id = currentImageId,
                                SetName = setName,
                                Name = itemName,
                                Size = 0,
                                Crc32 = 0,
                                XxHash64 = 0
                            },
                            SetName = setName,
                            SessionId = dataStorePath,
                            WasCancelled = true,
                            Reason = "Operation cancelled by user"
                        });

                        // Transition remaining Pending items to AddCancelled
                        for (int j = i + 1; j < candidates.Count; j++)
                        {
                            CandidateImage remaining = candidates[j];
                            onImageCommitted?.Invoke(new ImageCommittedEvent
                            {
                                Image = new ImageRecord
                                {
                                    Id = remaining.TempId,
                                    SetName = setName,
                                    Name = remaining.DisambiguatedName,
                                    Size = 0,
                                    Crc32 = 0,
                                    XxHash64 = 0
                                },
                                SetName = setName,
                                SessionId = dataStorePath,
                                WasCancelled = true,
                                Reason = "Operation cancelled by user"
                            });
                        }

                        return new OperationResult
                        {
                            Success = false,
                            WasCancelled = true,
                            Errors = errors,
                            ItemsProcessed = processed,
                            ItemsFailed = errors.Count,
                            Duration = sw.Elapsed,
                            DuplicateItems = duplicates
                        };
                    }
                    catch (HandledException ex)
                    {
                        string reason = ex.FriendlyErrorMessage.TrimEnd();
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = reason
                        });

                        onImageOutput?.Invoke(setName, currentImageId, $"[Error] {reason}");
                        onImageCommitted?.Invoke(new ImageCommittedEvent
                        {
                            Image = new ImageRecord
                            {
                                Id = currentImageId,
                                SetName = setName,
                                Name = itemName,
                                Size = 0,
                                Crc32 = 0,
                                XxHash64 = 0
                            },
                            SetName = setName,
                            SessionId = dataStorePath,
                            WasFailed = true,
                            Reason = reason
                        });

                        if (!isSyntheticFolder) processed++;
                    }
                    catch (Exception ex)
                    {
                        string reason = ex.Message;
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = reason
                        });

                        onImageOutput?.Invoke(setName, currentImageId, $"[Error] {reason}");
                        onImageCommitted?.Invoke(new ImageCommittedEvent
                        {
                            Image = new ImageRecord
                            {
                                Id = currentImageId,
                                SetName = setName,
                                Name = itemName,
                                Size = 0,
                                Crc32 = 0,
                                XxHash64 = 0
                            },
                            SetName = setName,
                            SessionId = dataStorePath,
                            WasFailed = true,
                            Reason = reason
                        });

                        if (!isSyntheticFolder) processed++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected top-level failure — write full details for debugging
                string debugInfo = $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";
                try { File.WriteAllText(Path.Combine(dataStorePath, "_addPreScanned_error.txt"), debugInfo); } catch { }

                errors.Add(new OperationErrorEntry
                {
                    ItemName = dataStorePath,
                    Reason = $"{ex.GetType().Name}: {ex.Message} at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"
                });

                return new OperationResult
                {
                    Success = false,
                    Errors = errors,
                    ItemsProcessed = processed,
                    ItemsFailed = errors.Count,
                    Duration = sw.Elapsed,
                    DuplicateItems = duplicates
                };
            }

            return new OperationResult
            {
                Success = errors.Count == 0,
                Errors = errors,
                ItemsProcessed = processed,
                ItemsFailed = errors.Count,
                Duration = sw.Elapsed,
                DuplicateItems = duplicates
            };
        }, cancellationToken);
    }

    public Task<OperationResult> AddDirAsync(string dataStorePath, string setName, IReadOnlyList<string> directoryPaths,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
            int processed = 0;
            int total = directoryPaths.Count;

            try
            {
                // Ensure DataStore directory exists
                if (!Directory.Exists(dataStorePath))
                    Directory.CreateDirectory(dataStorePath);

                // Resolve shard/block sizes from existing set or use defaults
                long shardSize = 50L * 1024 * 1024 * 1024; // 50 GiB
                int blockSize = 0x10000; // 64 KB

                using (DataStore ds = new DataStore(dataStorePath))
                {
                    SetInfo existingSet = ds.GetSetInfo(setName);
                    if (existingSet != null)
                    {
                        shardSize = existingSet.ShardSize;
                        blockSize = existingSet.BlockSize;
                    }
                    else
                    {
                        ds.CreateSet(setName, shardSize, blockSize);
                    }
                }

                foreach (string inputDir in directoryPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fullDir = Path.GetFullPath(inputDir);
                    string dirName = Path.GetFileName(fullDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(dirName))
                        dirName = "unnamed";

                    try
                    {
                        if (!Directory.Exists(fullDir))
                        {
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = dirName,
                                Reason = $"Directory '{inputDir}' does not exist."
                            });
                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = dirName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        using (DataStoreFolderFormatter formatter = new DataStoreFolderFormatter(dataStorePath, dirName, shardSize, blockSize, setName))
                        {
                            List<(string FullPath, string RelativePath)> files = Directory.EnumerateFiles(fullDir, "*", SearchOption.AllDirectories)
                                .Select(f => (FullPath: f, RelativePath: Path.GetRelativePath(fullDir, f).Replace(Path.DirectorySeparatorChar, '/')))
                                .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            long totalSize = 0;
                            int fileIndex = 0;

                            using XXHash64 runningXxHasher = XXHash64.Create();

                            foreach ((string FullPath, string RelativePath) file in files)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                long fileSize = new FileInfo(file.FullPath).Length;
                                if (fileSize == 0)
                                {
                                    formatter.StoreFile(file.RelativePath, Stream.Null, 0);
                                }
                                else
                                {
                                    using FileStream stream = File.OpenRead(file.FullPath);
                                    using XxHashTeeStream teeStream = new XxHashTeeStream(stream, runningXxHasher);
                                    formatter.StoreFile(file.RelativePath, teeStream, fileSize);
                                    totalSize += fileSize;
                                }

                                fileIndex++;
                                progress?.Report(new OperationProgress
                                {
                                    Percentage = files.Count > 0 ? (int)(fileIndex * 100L / files.Count) : 100,
                                    CurrentItem = file.RelativePath,
                                    ItemsProcessed = fileIndex,
                                    TotalItems = files.Count,
                                    Elapsed = sw.Elapsed
                                });
                            }

                            runningXxHasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                            // Compute aggregate CRC32 by combining per-file CRCs
                            uint aggregateCrc = 0;
                            bool firstCrc = true;
                            foreach (FolderFileEntry entry in formatter.FileEntries)
                            {
                                if (entry.Size > 0)
                                {
                                    if (firstCrc)
                                    {
                                        aggregateCrc = entry.Crc32;
                                        firstCrc = false;
                                    }
                                    else
                                    {
                                        aggregateCrc = ~NKitDataStore.Crc.Combine(~aggregateCrc, ~entry.Crc32, entry.Size);
                                    }
                                }
                            }

                            formatter.BuildFileSystemYaml();

                            // For empty directories (zero files or all zero-byte), pass zeros
                            if (totalSize == 0)
                            {
                                formatter.FinalizeImage(0, 0, 0);
                            }
                            else
                            {
                                ulong aggregateXxHash = runningXxHasher.HashUInt64;
                                formatter.FinalizeImage(totalSize, aggregateCrc, aggregateXxHash);
                            }
                        }

                        processed++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Re-throw to be caught by outer handler
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = dirName,
                            Reason = ex.Message
                        });
                        processed++;
                    }

                    progress?.Report(new OperationProgress
                    {
                        Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                        CurrentItem = dirName,
                        ItemsProcessed = processed,
                        TotalItems = total,
                        Elapsed = sw.Elapsed
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = true,
                    Errors = errors,
                    ItemsProcessed = processed,
                    ItemsFailed = errors.Count,
                    Duration = sw.Elapsed
                };
            }

            return new OperationResult
            {
                Success = errors.Count == 0,
                Errors = errors,
                ItemsProcessed = total,
                ItemsFailed = errors.Count,
                Duration = sw.Elapsed
            };
        }, cancellationToken);
    }

    public Task<OperationResult> ExportAsync(string dataStorePath, string setName, IReadOnlyList<long> imageIds,
        string outputDirectory, string convertFormat,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default,
        ResolvedKeyFixPaths keyFixPaths = null,
        Action<ImageProgressEvent> onImageProgress = null)
    {
        return Task.Run(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
            int processed = 0;
            int total = imageIds.Count;
            long totalOutputSize = 0;

            try
            {
                // Ensure output directory exists
                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                using DataStore dataStore = new DataStore(dataStorePath);

                foreach (long imageId in imageIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string itemName = $"Image {imageId}";
                    try
                    {
                        // Look up the image name from the DataStore to use as the mask
                        ImageRecord image = dataStore.ListImagesInSet(setName)
                            .FirstOrDefault(img => img.Id == imageId);
                        string imageName = image?.Name;
                        string imageFormat = image?.Format.ToString().ToLowerInvariant() ?? "iso";

                        if (imageName == null)
                        {
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = itemName,
                                Reason = "Image not found in DataStore."
                            });
                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        itemName = imageName;

                        // === Direct directory export path ===
                        if (string.Equals(convertFormat, "dir", StringComparison.OrdinalIgnoreCase))
                        {
                            totalOutputSize += exportDirectoryImage(
                                dataStore, setName, image, outputDirectory,
                                errors, progress, processed, total, sw, cancellationToken);
                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        // Build export args: -in points to setName.nkds//imageName.ext
                        // For App/Cdn images (WiiU), the DataStore lists them without
                        // extension — pass the image name as-is (e.g. "test [tmd.3648]").
                        // For CUE/GDI images, the DataStore presents them as folders —
                        // the mask must target the index file inside: "folder/name.cue".
                        // When multiple images share the same name, addDataStoreFiles
                        // disambiguates by appending " (id)" — the mask must match.
                        string nkdsPath = Path.Combine(dataStorePath, setName + DataStore.DatabaseFileExtension);

                        // Check for duplicate names (same logic as addDataStoreFiles)
                        bool isDuplicate = dataStore.ListImagesInSet(setName)
                            .Where(i => i.Format != ImageFormat.TmdAppFolder && i.Format != ImageFormat.CueFolder)
                            .Count(i => string.Equals(i.Name, imageName, StringComparison.OrdinalIgnoreCase)) > 1;
                        string maskName = isDuplicate
                            ? Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName(imageName, imageId)
                            : imageName;

                        string mask;
                        if (image.Format == ImageFormat.App || image.Format == ImageFormat.Cdn)
                            mask = maskName;
                        else if (image.Format == ImageFormat.Cue || image.Format == ImageFormat.Gdi)
                            mask = maskName + "/" + maskName + "." + imageFormat;
                        else
                            mask = maskName + "." + imageFormat;

                        TaskType taskType = string.IsNullOrWhiteSpace(convertFormat) ? TaskType.Expand : TaskType.Convert;
                        string[] args = buildExportArgs(nkdsPath, mask, outputDirectory, taskType, convertFormat, keyFixPaths);
                        AppSettings settings = new AppSettings(null, args);

                        // Scan for the image in the DataStore
                        using Log log = settings.GetLog((_, _) => { });
                        List<SourceFile> images = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, cancellationToken)
                            .ToList();

                        if (images.Count == 0)
                        {
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = itemName,
                                Reason = "Image not found in DataStore."
                            });
                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        // Target the specific image by ID (avoid processing multiple scan matches)
                        SourceFile sourceFile = images[0];
                        sourceFile.DataStoreImageId = imageId;
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            itemName = sourceFile.Name ?? $"Image {imageId}";

                            // Per-image progress throttling state
                            float lastEmittedProgress = -1f;

                            NKitProcessor processor = new NKitProcessor(settings, sourceFile, (_, _) => { });

                            // Subscribe to ProgressEvent for per-image step progress
                            if (progress != null || onImageProgress != null)
                            {
                                processor.ProgressEvent += (_, e) =>
                                {
                                    float stepProgress = e.Progress;
                                    float overallProgress = e.ProgressTotal;

                                    bool isStartOrComplete = e.IsStart || e.IsComplete;
                                    bool exceedsThreshold = Math.Abs(overallProgress - lastEmittedProgress) >= 0.005f;

                                    if (!isStartOrComplete && !exceedsThreshold)
                                        return;

                                    lastEmittedProgress = overallProgress;

                                    string stepName = e.Steps != null && e.Step < e.Steps.Length ? e.Steps[e.Step] : $"Step {e.Step + 1}";

                                    onImageProgress?.Invoke(new ImageProgressEvent
                                    {
                                        SetName = setName,
                                        ImageId = imageId,
                                        StepName = stepName,
                                        StepProgress = stepProgress,
                                        OverallProgress = overallProgress,
                                        StepIndex = e.Step,
                                        StepTotal = e.StepTotal,
                                        Steps = e.Steps
                                    });

                                    // Update the operation-level progress bar with per-file granularity
                                    progress?.Report(new OperationProgress
                                    {
                                        Percentage = total > 0 ? (int)((processed + overallProgress) * 100L / total) : 0,
                                        CurrentItem = $"{itemName} \u2014 {stepName}",
                                        ItemsProcessed = processed,
                                        TotalItems = total,
                                        Elapsed = sw.Elapsed
                                    });
                                };
                            }

                            // Report current item name before processing starts
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 0,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });

                            NKitTaskResults result = processor.Process(cancellationToken);

                            if (!string.IsNullOrWhiteSpace(result.ErrorMsg))
                            {
                                errors.Add(new OperationErrorEntry
                                {
                                    ItemName = itemName,
                                    Reason = result.ErrorMsg
                                });
                            }
                            else
                            {
                                totalOutputSize += result.Size;
                            }
                        }

                        processed++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (HandledException ex)
                    {
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = ex.FriendlyErrorMessage.TrimEnd()
                        });
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = ex.Message
                        });
                        processed++;
                    }

                    progress?.Report(new OperationProgress
                    {
                        Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                        CurrentItem = itemName,
                        ItemsProcessed = processed,
                        TotalItems = total,
                        Elapsed = sw.Elapsed
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = true,
                    Errors = errors,
                    ItemsProcessed = processed,
                    ItemsFailed = errors.Count,
                    Duration = sw.Elapsed,
                    TotalOutputSize = totalOutputSize
                };
            }

            return new OperationResult
            {
                Success = errors.Count == 0,
                Errors = errors,
                ItemsProcessed = total,
                ItemsFailed = errors.Count,
                Duration = sw.Elapsed,
                TotalOutputSize = totalOutputSize
            };
        }, cancellationToken);
    }

    public Task<OperationResult> VerifyAsync(string dataStorePath, string setName, IReadOnlyList<long> imageIds,
        IProgress<OperationProgress> progress = null,
        Action<ImageProgressEvent> onImageProgress = null,
        Action<string, long, string> onImageOutput = null,
        Action<long, bool> onImageVerified = null,
        CancellationToken cancellationToken = default,
        ResolvedKeyFixPaths keyFixPaths = null)
    {
        return Task.Run(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<OperationErrorEntry> errors = new List<OperationErrorEntry>();
            int processed = 0;

            try
            {
                // Determine which images to verify
                List<long> idsToVerify;
                if (imageIds != null && imageIds.Count > 0)
                {
                    idsToVerify = imageIds.ToList();
                }
                else
                {
                    // Verify all images in the set
                    using DataStore ds = new DataStore(dataStorePath);
                    idsToVerify = ds.ListImagesInSet(setName)
                        .Where(img => !img.Removed)
                        .Select(img => img.Id)
                        .ToList();
                }

                int total = idsToVerify.Count;

                foreach (long imageId in idsToVerify)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string itemName = $"Image {imageId}";
                    try
                    {
                        // Look up the image name from the DataStore to use as the mask
                        string imageName = null;
                        string imageFormat = "iso";
                        ImageFormat imageFormatEnum = ImageFormat.Unknown;
                        long imageSize = 0;
                        uint imageCrc32 = 0;
                        ulong imageXxHash64 = 0;
                        using (DataStore ds = new DataStore(dataStorePath))
                        {
                            ImageRecord image = ds.ListImagesInSet(setName)
                                .FirstOrDefault(img => img.Id == imageId && !img.Removed);
                            imageName = image?.Name;
                            if (image != null)
                            {
                                imageFormat = image.Format.ToString().ToLowerInvariant();
                                imageFormatEnum = image.Format;
                                imageSize = image.Size;
                                imageCrc32 = image.Crc32;
                                imageXxHash64 = image.XxHash64;
                            }
                        }

                        if (imageName == null)
                        {
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = itemName,
                                Reason = "Image not found in DataStore."
                            });
                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        itemName = imageName;

                        // Folder image pre-checks: handle empty directories and legacy images
                        if (imageFormatEnum == ImageFormat.Folder)
                        {
                            if (imageSize == 0 && imageCrc32 == 0 && imageXxHash64 == 0)
                            {
                                // Empty directory — nothing to verify
                                onImageVerified?.Invoke(imageId, true);
                                processed++;
                                progress?.Report(new OperationProgress
                                {
                                    Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                    CurrentItem = itemName,
                                    ItemsProcessed = processed,
                                    TotalItems = total,
                                    Elapsed = sw.Elapsed
                                });
                                continue;
                            }
                            if (imageCrc32 == 0 && imageXxHash64 == 0 && imageSize > 0)
                            {
                                // Legacy image — cannot verify
                                errors.Add(new OperationErrorEntry
                                {
                                    ItemName = itemName,
                                    Reason = "Unverified (legacy image stored before checksum computation)"
                                });
                                processed++;
                                progress?.Report(new OperationProgress
                                {
                                    Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                    CurrentItem = itemName,
                                    ItemsProcessed = processed,
                                    TotalItems = total,
                                    Elapsed = sw.Elapsed
                                });
                                continue;
                            }

                            // Folder image with valid checksums — verify by reading the
                            // concatenated byte stream directly and comparing checksums.
                            // The NKitProcessor pipeline does not support Folder format,
                            // so we perform verification inline.
                            try
                            {
                                using DataStore ds2 = new DataStore(dataStorePath);
                                using IImageReader reader = ds2.OpenImageReader(new GlobalImageKey(setName, imageId));

                                // Get all distinct offset groups (each represents a stored file)
                                // ordered by offset to reconstruct the concatenated byte stream.
                                List<long> offsetGroups = reader.GetOffsets()
                                    .Where(o => o.Offset >= 0 && o.Type != BlockType.BlockPadding)
                                    .Select(o => o.OffsetStart)
                                    .Distinct()
                                    .OrderBy(o => o)
                                    .ToList();

                                using NKitDataStore.Crc crc = new NKitDataStore.Crc();
                                using XXHash64 xxHash = XXHash64.Create();

                                byte[] buffer = new byte[0x10000]; // 64KB
                                long totalRead = 0;

                                foreach (long offsetStart in offsetGroups)
                                {
                                    using Stream stream = reader.OpenStream(offsetStart);
                                    int bytesRead;
                                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        crc.Sum(buffer, 0, bytesRead);
                                        xxHash.TransformBlock(buffer, 0, bytesRead, null, 0);
                                        totalRead += bytesRead;
                                    }
                                }

                                xxHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                                uint computedCrc = crc.Value;
                                ulong computedXxHash = xxHash.HashUInt64;

                                if (computedCrc == imageCrc32 && computedXxHash == imageXxHash64)
                                {
                                    onImageVerified?.Invoke(imageId, true);
                                }
                                else
                                {
                                    errors.Add(new OperationErrorEntry
                                    {
                                        ItemName = itemName,
                                        Reason = $"Verification failed: CRC32 {(computedCrc != imageCrc32 ? "mismatch" : "ok")}, XxHash64 {(computedXxHash != imageXxHash64 ? "mismatch" : "ok")}"
                                    });
                                    onImageVerified?.Invoke(imageId, false);
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add(new OperationErrorEntry
                                {
                                    ItemName = itemName,
                                    Reason = $"I/O error during verification: {ex.Message}"
                                });
                                onImageVerified?.Invoke(imageId, false);
                            }

                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        // Build verify args: -in points to setName.nkds//imageName.ext
                        // For App/Cdn images (WiiU), the DataStore lists them without
                        // extension — pass the image name as-is (e.g. "test [tmd.3648]").
                        // For CUE/GDI images, the DataStore presents them as folders —
                        // the mask must target the index file inside: "folder/name.cue".
                        // When multiple images share the same name, addDataStoreFiles
                        // disambiguates by appending " (id)" — the mask must match.
                        string nkdsPath = Path.Combine(dataStorePath, setName + DataStore.DatabaseFileExtension);

                        // Check for duplicate names (same logic as addDataStoreFiles)
                        bool isDuplicate;
                        using (DataStore dsCheck = new DataStore(dataStorePath))
                        {
                            isDuplicate = dsCheck.ListImagesInSet(setName)
                                .Where(i => i.Format != ImageFormat.TmdAppFolder && i.Format != ImageFormat.CueFolder)
                                .Count(i => string.Equals(i.Name, imageName, StringComparison.OrdinalIgnoreCase)) > 1;
                        }
                        string maskName = isDuplicate
                            ? Nanook.NKit.Container.DataStoreAsIso.FormatDuplicateName(imageName, imageId)
                            : imageName;

                        string mask;
                        if (imageFormatEnum == ImageFormat.App || imageFormatEnum == ImageFormat.Cdn)
                            mask = maskName;
                        else if (imageFormatEnum == ImageFormat.Cue || imageFormatEnum == ImageFormat.Gdi)
                            mask = maskName + "/" + maskName + "." + imageFormat;
                        else
                            mask = maskName + "." + imageFormat;

                        System.Diagnostics.Trace.WriteLine($"[Verify] Image {imageId}: name='{imageName}', nkdsPath='{nkdsPath}', exists={File.Exists(nkdsPath)}, mask='{mask}'");
                        string[] args = buildVerifyArgs(nkdsPath, mask, keyFixPaths);
                        System.Diagnostics.Trace.WriteLine($"[Verify] Args: {string.Join(" ", args)}");
                        AppSettings settings = new AppSettings(null, args);

                        using Log log = settings.GetLog((_, _) => { });
                        List<SourceFile> images = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, cancellationToken)
                            .ToList();

                        if (images.Count == 0)
                        {
                            errors.Add(new OperationErrorEntry
                            {
                                ItemName = itemName,
                                Reason = "Image not found in DataStore."
                            });
                            processed++;
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });
                            continue;
                        }

                        // Target the specific image by ID
                        SourceFile sourceFile = images[0];
                        sourceFile.DataStoreImageId = imageId;
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            itemName = sourceFile.Name ?? $"Image {imageId}";

                            // Per-image progress throttling state
                            float lastEmittedProgress = -1f;

                            // Wire console output to onImageOutput callback
                            Action<string, LogLevel> consoleLog = (msg, _) =>
                            {
                                if (onImageOutput != null && !string.IsNullOrEmpty(msg))
                                    onImageOutput(setName, imageId, msg);
                            };

                            NKitProcessor processor = new NKitProcessor(settings, sourceFile, consoleLog);

                            // Subscribe to ProgressEvent for per-image step progress
                            if (onImageProgress != null || progress != null)
                            {
                                processor.ProgressEvent += (_, e) =>
                                {
                                    float stepProgress = e.Progress;
                                    float overallProgress = e.ProgressTotal;

                                    bool isStartOrComplete = e.IsStart || e.IsComplete;
                                    bool exceedsThreshold = Math.Abs(overallProgress - lastEmittedProgress) >= 0.005f;

                                    if (!isStartOrComplete && !exceedsThreshold)
                                        return;

                                    lastEmittedProgress = overallProgress;

                                    string stepName = e.Steps != null && e.Step < e.Steps.Length ? e.Steps[e.Step] : $"Step {e.Step + 1}";

                                    onImageProgress?.Invoke(new ImageProgressEvent
                                    {
                                        SetName = setName,
                                        ImageId = imageId,
                                        StepName = stepName,
                                        StepProgress = stepProgress,
                                        OverallProgress = overallProgress,
                                        StepIndex = e.Step,
                                        StepTotal = e.StepTotal,
                                        Steps = e.Steps
                                    });

                                    // Also update the operation-level progress bar with per-file granularity.
                                    progress?.Report(new OperationProgress
                                    {
                                        Percentage = total > 0 ? (int)((processed + overallProgress) * 100L / total) : 0,
                                        CurrentItem = $"{itemName} \u2014 {stepName}",
                                        ItemsProcessed = processed,
                                        TotalItems = total,
                                        Elapsed = sw.Elapsed
                                    });
                                };
                            }

                            // Report current item name before processing starts
                            progress?.Report(new OperationProgress
                            {
                                Percentage = total > 0 ? (int)(processed * 100L / total) : 0,
                                CurrentItem = itemName,
                                ItemsProcessed = processed,
                                TotalItems = total,
                                Elapsed = sw.Elapsed
                            });

                            NKitTaskResults result = processor.Process(cancellationToken);

                            if (!string.IsNullOrWhiteSpace(result.ErrorMsg) || result.VerifyResult != VerifyResult.VerifySuccess)
                            {
                                string reason = !string.IsNullOrWhiteSpace(result.ErrorMsg)
                                    ? result.ErrorMsg
                                    : $"Verification failed: {result.VerifyResult}";
                                errors.Add(new OperationErrorEntry
                                {
                                    ItemName = itemName,
                                    Reason = reason
                                });

                                // Notify UI of verify failure
                                onImageVerified?.Invoke(imageId, false);
                            }
                            else
                            {
                                // Notify UI of verify success
                                onImageVerified?.Invoke(imageId, true);
                            }
                        }

                        processed++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (HandledException ex)
                    {
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = ex.FriendlyErrorMessage.TrimEnd()
                        });
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new OperationErrorEntry
                        {
                            ItemName = itemName,
                            Reason = ex.Message
                        });
                        processed++;
                    }

                    progress?.Report(new OperationProgress
                    {
                        Percentage = total > 0 ? (int)(processed * 100L / total) : 100,
                        CurrentItem = itemName,
                        ItemsProcessed = processed,
                        TotalItems = total,
                        Elapsed = sw.Elapsed
                    });
                }

                return new OperationResult
                {
                    Success = errors.Count == 0,
                    Errors = errors,
                    ItemsProcessed = total,
                    ItemsFailed = errors.Count,
                    Duration = sw.Elapsed
                };
            }
            catch (OperationCanceledException)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = true,
                    Errors = errors,
                    ItemsProcessed = processed,
                    ItemsFailed = errors.Count,
                    Duration = sw.Elapsed
                };
            }
        }, cancellationToken);
    }

    public Task<OperationResult> CompactAsync(string dataStorePath, string setName,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<OperationErrorEntry> errors = new List<OperationErrorEntry>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new OperationProgress
                {
                    Percentage = 0,
                    CurrentItem = setName,
                    ItemsProcessed = 0,
                    TotalItems = 1,
                    Elapsed = sw.Elapsed
                });

                using DataStore dataStore = new DataStore(dataStorePath);
                dataStore.CompactSet(setName, new Progress<(int Percentage, string Stage)>(p =>
                {
                    progress?.Report(new OperationProgress
                    {
                        Percentage = p.Percentage,
                        CurrentItem = $"{setName} - [{p.Stage}]",
                        ItemsProcessed = 0,
                        TotalItems = 1,
                        Elapsed = sw.Elapsed
                    });
                }));

                progress?.Report(new OperationProgress
                {
                    Percentage = 100,
                    CurrentItem = setName,
                    ItemsProcessed = 1,
                    TotalItems = 1,
                    Elapsed = sw.Elapsed
                });
            }
            catch (OperationCanceledException)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = true,
                    Errors = errors,
                    ItemsProcessed = 0,
                    ItemsFailed = 0,
                    Duration = sw.Elapsed
                };
            }
            catch (Exception ex)
            {
                errors.Add(new OperationErrorEntry
                {
                    ItemName = setName,
                    Reason = ex.Message
                });
            }

            return new OperationResult
            {
                Success = errors.Count == 0,
                Errors = errors,
                ItemsProcessed = errors.Count == 0 ? 1 : 0,
                ItemsFailed = errors.Count,
                Duration = sw.Elapsed
            };
        }, cancellationToken);
    }

    // --- Direct directory export ---

    /// <summary>
    /// Reads filesystem data for an image using NkFs-first fallback.
    /// Tries filesystem.nkfs first (converting to FsYaml), then falls back to filesystem.yaml.
    /// Same pattern as FolderImageProcessor.ReadFsYaml().
    /// </summary>
    private static FsYaml ReadFsYaml(DataStore dataStore, GlobalImageKey key)
    {
        // Try NkFs first
        byte[] data = null;
        try { data = dataStore.ReadFile(key, DataStore.FileSystemNkfsRootPath); } catch { }
        if (data != null)
        {
            try { return NkFs.FromBytes(data).ToFsYaml(); } catch { }
        }

        // Fall back to YAML
        try { data = dataStore.ReadFile(key, DataStore.FileSystemYamlRootPath); } catch { }
        if (data == null)
        {
            try { data = dataStore.ReadFile(key, DataStore.FileSystemYamlName); } catch { }
        }

        if (data != null)
            return FsYaml.FromBytes(data);

        return null;
    }

    /// <summary>
    /// Exports a single directory image by reading its filesystem metadata and writing all files
    /// to the output directory. Returns total bytes written.
    /// </summary>
    private long exportDirectoryImage(
        DataStore dataStore, string setName, ImageRecord image,
        string outputDirectory, List<OperationErrorEntry> errors,
        IProgress<OperationProgress> progress, int processed, int total,
        Stopwatch sw, CancellationToken cancellationToken)
    {
        long bytesWritten = 0;
        string imageName = image.Name;

        // 1. Read filesystem metadata using NkFs-first pattern
        GlobalImageKey key = new GlobalImageKey(setName, image.Id);
        FsYaml fsYaml = ReadFsYaml(dataStore, key);
        if (fsYaml == null)
        {
            errors.Add(new OperationErrorEntry
            { ItemName = imageName, Reason = "Image has no filesystem.nkfs or filesystem.yaml." });
            return 0;
        }

        // 2. Create root output folder
        string rootPath = Path.Combine(outputDirectory, sanitizeFileName(imageName));
        try
        {
            Directory.CreateDirectory(rootPath);
        }
        catch (Exception ex)
        {
            errors.Add(new OperationErrorEntry
            { ItemName = imageName, Reason = $"Cannot create output folder: {ex.Message}" });
            return 0;
        }

        // 3. If no filesystem entries, we're done (empty folder)
        if (fsYaml.FileSystems.Count == 0)
            return 0;

        // 4. Open reader and traverse the tree depth-first, creating directories and writing files
        using IImageReader reader = dataStore.OpenImageReader(key);

        foreach (FsYamlNode fsRoot in fsYaml.FileSystems)
        {
            // Write children directly into rootPath (the root node name is the image name)
            writeNodeChildren(reader, fsRoot, rootPath, ref bytesWritten,
                errors, imageName, progress, processed, total, sw, cancellationToken);
        }

        return bytesWritten;
    }

    /// <summary>
    /// Recursively writes all children of a directory node to disk.
    /// </summary>
    private void writeNodeChildren(
        IImageReader reader, FsYamlNode dirNode, string currentPath,
        ref long bytesWritten, List<OperationErrorEntry> errors, string imageName,
        IProgress<OperationProgress> progress, int processed, int total,
        Stopwatch sw, CancellationToken cancellationToken)
    {
        if (dirNode.Children == null)
            return;

        foreach (FsYamlNode child in dirNode.Children)
        {
            string childPath = Path.Combine(currentPath, sanitizeFileName(child.Name));

            if (child.IsDirectory)
            {
                Directory.CreateDirectory(childPath);
                writeNodeChildren(reader, child, childPath, ref bytesWritten,
                    errors, imageName, progress, processed, total, sw, cancellationToken);
            }
            else
            {
                // File node — check cancellation, open stream and write to disk
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using Stream stream = reader.OpenStream(child.Offset);
                    using FileStream fileStream = new FileStream(childPath,
                        FileMode.Create, FileAccess.Write, FileShare.None);

                    copyExactBytes(stream, fileStream, child.Size);
                    bytesWritten += child.Size;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors.Add(new OperationErrorEntry
                    { ItemName = imageName, Reason = $"Failed to write '{child.Name}': {ex.Message}" });
                    return; // Stop processing this image on file write failure
                }
            }
        }
    }

    /// <summary>
    /// Sanitizes a file/folder name by replacing filesystem-illegal characters with underscore.
    /// </summary>
    private static string sanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            if (name.Contains(c))
                name = name.Replace(c, '_');
        }
        return name;
    }

    /// <summary>
    /// Copies exactly the specified number of bytes from source to destination.
    /// Throws <see cref="EndOfStreamException"/> if source ends before <paramref name="byteCount"/> bytes are read.
    /// </summary>
    private static void copyExactBytes(Stream source, Stream destination, long byteCount)
    {
        byte[] buffer = new byte[81920]; // 80KB buffer
        long remaining = byteCount;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = source.Read(buffer, 0, toRead);
            if (read == 0)
                throw new EndOfStreamException(
                    $"Unexpected end of stream. Expected {byteCount} bytes, got {byteCount - remaining}.");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    // --- Helper methods for building NKit pipeline arguments ---

    /// <summary>
    /// Appends system-specific key and fix file override arguments to the argument list.
    /// These override any values from the NKit config file (existing SystemSettings behavior).
    /// Only non-null/non-empty paths are appended.
    /// </summary>
    private static void appendKeyFixArgs(List<string> args, ResolvedKeyFixPaths keyFixPaths)
    {
        if (keyFixPaths == null)
            return;

        if (!string.IsNullOrWhiteSpace(keyFixPaths.WiiFixInfoPath))
        {
            args.Add("-wii:fixInfo");
            args.Add(keyFixPaths.WiiFixInfoPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.WiiFixFilesPath))
        {
            args.Add("-wii:fixFiles");
            args.Add(keyFixPaths.WiiFixFilesPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.GameCubeFixInfoPath))
        {
            args.Add("-gamecube:fixInfo");
            args.Add(keyFixPaths.GameCubeFixInfoPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.GameCubeFixFilesPath))
        {
            args.Add("-gamecube:fixFiles");
            args.Add(keyFixPaths.GameCubeFixFilesPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.Ps3FixInfoPath))
        {
            args.Add("-ps3:fixInfo");
            args.Add(keyFixPaths.Ps3FixInfoPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.Ps3FixFilesPath))
        {
            args.Add("-ps3:fixFiles");
            args.Add(keyFixPaths.Ps3FixFilesPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.Ps3KeysPath))
        {
            args.Add("-ps3:keys");
            args.Add(keyFixPaths.Ps3KeysPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.WiiUKeysPath))
        {
            args.Add("-wiiu:keys");
            args.Add(keyFixPaths.WiiUKeysPath);
        }

        if (!string.IsNullOrWhiteSpace(keyFixPaths.DreamcastFixInfoPath))
        {
            args.Add("-dreamcast:fixInfo");
            args.Add(keyFixPaths.DreamcastFixInfoPath);
        }
    }

    private static string[] buildImportArgs(string dataStorePath, string setName, IReadOnlyList<string> filePaths) => buildImportArgs(dataStorePath, setName, filePaths, null, null, null, null);

    private static string[] buildImportArgs(string dataStorePath, string setName, IReadOnlyList<string> filePaths, string shardSizeText, string blockSizeText, string auxModeText, ResolvedKeyFixPaths keyFixPaths = null)
    {
        // Build dedupe param: setName:shardSize:blockSize:auxMode (trailing empty parts trimmed)
        string dedupeParam = setName;
        if (!string.IsNullOrWhiteSpace(shardSizeText) || !string.IsNullOrWhiteSpace(blockSizeText) || !string.IsNullOrWhiteSpace(auxModeText))
        {
            string shard = shardSizeText ?? "";
            string block = blockSizeText ?? "";
            string aux = auxModeText ?? "";
            string[] parts = new[] { setName, shard, block, aux };
            int lastNonEmpty = -1;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    lastNonEmpty = i;
                    break;
                }
            }
            dedupeParam = string.Join(":", parts, 0, lastNonEmpty + 1);
        }

        List<string> args = new List<string>
        {
            "nkds",
            "-task", TaskType.Dedupe.ToString(),
            "-out", dataStorePath,
            "-dedupe", dedupeParam
        };

        // Disable NKit config file loading — NKDS passes all required settings
        // (keys, fixInfo, fixFiles) directly via command-line override arguments.
        args.Add("-cfg");
        args.Add("n");

        foreach (string filePath in filePaths)
        {
            args.Add("-in");
            args.Add(filePath);
        }

        appendKeyFixArgs(args, keyFixPaths);

        return args.ToArray();
    }

    private static string[] buildExportArgs(string nkdsFilePath, string mask, string outputDirectory, TaskType taskType, string convertFormat, ResolvedKeyFixPaths keyFixPaths = null)
    {
        // nkdsFilePath: full path to the .nkds index file (e.g. C:\TEMP\WII\Wii.nkds)
        // mask: image name wildcard (e.g. "Example Game*")
        List<string> args = new List<string>
        {
            "nkds",
            "-task", taskType.ToString(),
            "-out", outputDirectory,
            "-in", nkdsFilePath + "//" + mask
        };

        // Disable NKit config file loading — NKDS passes all required settings
        // (keys, fixInfo, fixFiles) directly via command-line override arguments.
        args.Add("-cfg");
        args.Add("n");

        if (!string.IsNullOrWhiteSpace(convertFormat))
        {
            args.Add("-convert");
            args.Add(convertFormat);
        }

        appendKeyFixArgs(args, keyFixPaths);

        return args.ToArray();
    }

    private static string[] buildVerifyArgs(string nkdsFilePath, string mask, ResolvedKeyFixPaths keyFixPaths = null)
    {
        // nkdsFilePath: full path to the .nkds index file (e.g. C:\TEMP\WII\Wii.nkds)
        // mask: image name or wildcard (e.g. "Example Game (USA).iso")
        List<string> args = new List<string>
        {
            "nkds",
            "-task", TaskType.Verify.ToString(),
            "-v", "Y",
            "-in", nkdsFilePath + "//" + mask
        };

        // Disable NKit config file loading — NKDS passes all required settings
        // (keys, fixInfo, fixFiles) directly via command-line override arguments.
        args.Add("-cfg");
        args.Add("n");

        appendKeyFixArgs(args, keyFixPaths);

        return args.ToArray();
    }

    // --- Mount operations (delegated to MountService) ---

    public Task<OperationResult> MountAsync(string dataStorePath, string setName, string mountPoint, MountOptions options,
        CancellationToken cancellationToken = default) => _mountService.MountAsync(dataStorePath, setName, mountPoint, options, cancellationToken);

    public OperationResult Unmount(string mountPoint) => _mountService.Unmount(mountPoint);

    public IReadOnlyList<ActiveMount> GetActiveMounts() => _mountService.GetActiveMounts();

    public void Dispose() => _mountService.Dispose();
}
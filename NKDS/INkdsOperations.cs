using NKDS.Models;

namespace NKDS;

/// <summary>
/// Primary API for all NKDS DataStore operations.
/// Thread-safe for concurrent calls targeting different sets.
/// </summary>
public interface INkdsOperations : IDisposable
{
    // Short-lived operations (synchronous)
    OperationResult CreateSet(string dataStorePath, string setName, long shardSize, int blockSize);
    OperationResult Remove(string dataStorePath, string setName, IReadOnlyList<long> imageIds);
    OperationResult Restore(string dataStorePath, string setName, IReadOnlyList<long> imageIds);
    OperationResult Rollback(string dataStorePath, string setName, long targetImageId, IProgress<NKDS.Models.OperationProgress> progress = null);
    OperationResult GetSets(string dataStorePath);
    StatsResult GetStats(string dataStorePath, string setName, bool includePerImageStats,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default);

    // Long-running operations (async with progress + cancellation)
    Task<IReadOnlyList<CandidateImage>> PreScanAsync(string dataStorePath, string setName, IReadOnlyList<string> filePaths,
        ResolvedKeyFixPaths keyFixPaths = null, CancellationToken cancellationToken = default);
    Task<OperationResult> AddPreScannedAsync(string dataStorePath, string setName, IReadOnlyList<CandidateImage> candidates,
        Action<ImageCommittedEvent> onImageCommitted = null,
        Action<ImageProgressEvent> onImageProgress = null,
        Action<string, long, string> onImageOutput = null,
        Action<int, int> onItemStarted = null,
        CancellationToken cancellationToken = default,
        ResolvedKeyFixPaths keyFixPaths = null,
        string auxModeText = null);
    Task<OperationResult> AddAsync(string dataStorePath, string setName, IReadOnlyList<string> filePaths,
        IProgress<OperationProgress> progress = null,
        Action<ImageCommittedEvent> onImageCommitted = null,
        Action<ImageProgressEvent> onImageProgress = null,
        Action<string, long, string> onImageOutput = null,
        CancellationToken cancellationToken = default,
        string shardSizeText = null,
        string blockSizeText = null,
        string auxModeText = null,
        ResolvedKeyFixPaths keyFixPaths = null);
    Task<OperationResult> AddDirAsync(string dataStorePath, string setName, IReadOnlyList<string> directoryPaths,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> ExportAsync(string dataStorePath, string setName, IReadOnlyList<long> imageIds,
        string outputDirectory, string convertFormat,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default,
        ResolvedKeyFixPaths keyFixPaths = null,
        Action<ImageProgressEvent> onImageProgress = null);
    Task<OperationResult> VerifyAsync(string dataStorePath, string setName, IReadOnlyList<long> imageIds,
        IProgress<OperationProgress> progress = null,
        Action<ImageProgressEvent> onImageProgress = null,
        Action<string, long, string> onImageOutput = null,
        Action<long, bool> onImageVerified = null,
        CancellationToken cancellationToken = default,
        ResolvedKeyFixPaths keyFixPaths = null);
    Task<OperationResult> CompactAsync(string dataStorePath, string setName,
        IProgress<OperationProgress> progress = null, CancellationToken cancellationToken = default);

    // Mount operations
    Task<OperationResult> MountAsync(string dataStorePath, string setName, string mountPoint, MountOptions options,
        CancellationToken cancellationToken = default);
    OperationResult Unmount(string mountPoint);
    IReadOnlyList<ActiveMount> GetActiveMounts();
}
using NKDS.DatVerification;
using NKDS.Mount;
using NkdsUi.ViewModels;

namespace NkdsUi.Services;

/// <summary>
/// Application-level composition root. Creates and holds all service instances
/// using explicit constructor calls (no reflection, AOT-compatible).
/// Instantiated once in Application.OnFrameworkInitializationCompleted.
/// Implements IDisposable with reverse-order disposal and partial-construction safety.
/// </summary>
public sealed class ServiceRegistry : IDisposable
{
    // Services are created in dependency order.
    // Each is exposed as a typed read-only property so consumers can access
    // services without casting and compiled bindings remain compatible.

    public IConfigService ConfigService { get; }
    public IDataStoreService DataStoreService { get; }
    public IBlockComparisonService BlockComparisonService { get; }
    public IHashCacheService HashCacheService { get; }
    public ISimilarityGroupService SimilarityGroupService { get; }
    public IThresholdGroupingService ThresholdGroupingService { get; }
    public IStatsCalculationService StatsCalculationService { get; }
    public IPlatformFsHostFactory PlatformFsHostFactory { get; }
    public IDatVerificationService DatVerificationService { get; }
    public SessionManager SessionManager { get; }
    public FileHandleReleaseStrategy FileHandleReleaseStrategy { get; }
    public ExclusiveAccessManager ExclusiveAccessManager { get; }
    public ErrorNotificationService ErrorNotificationService { get; }
    public IFileAssociationService FileAssociationService { get; }
    public SharedObservableState SharedState { get; }

    public ServiceRegistry()
    {
        try
        {
            // 1. ConfigService — no dependencies
            ConfigService = new ConfigService();

            // 2. DataStoreService — no dependencies
            DataStoreService = new DataStoreService();

            // 3. BlockComparisonService — depends on IDataStoreService
            BlockComparisonService = new BlockComparisonService(DataStoreService);

            // 4. HashCacheService — depends on IDataStoreService
            HashCacheService = new HashCacheService(DataStoreService);

            // 5. SimilarityGroupService — no dependencies
            SimilarityGroupService = new SimilarityGroupService();

            // 6. ThresholdGroupingService — no dependencies
            ThresholdGroupingService = new ThresholdGroupingService();

            // 7. StatsCalculationService — no dependencies
            StatsCalculationService = new StatsCalculationService();

            // 8. PlatformFsHostFactory — static factory, platform-detected
            PlatformFsHostFactory = NKDS.Mount.PlatformFsHostFactory.CreateForCurrentPlatform();

            // 9. DatVerificationService — no dependencies
            DatVerificationService = new DatVerificationService();

            // 10. SessionManager — depends on IDataStoreService
            SessionManager = new SessionManager(DataStoreService);

            // 11. FileHandleReleaseStrategy — no dependencies
            FileHandleReleaseStrategy = new FileHandleReleaseStrategy();

            // 12. ExclusiveAccessManager — depends on SessionManager, FileHandleReleaseStrategy
            ExclusiveAccessManager = new ExclusiveAccessManager(SessionManager, FileHandleReleaseStrategy);

            // 13. ErrorNotificationService — no dependencies
            ErrorNotificationService = new ErrorNotificationService();

            // Wire ErrorNotificationService to services created before it
            ((ConfigService)ConfigService).SetErrorNotification(ErrorNotificationService);
            ((DataStoreService)DataStoreService).SetErrorNotification(ErrorNotificationService);
            ((HashCacheService)HashCacheService).SetErrorNotification(ErrorNotificationService);
            ((StatsCalculationService)StatsCalculationService).SetErrorNotification(ErrorNotificationService);

            // 14. FileAssociationService — platform-detected factory, no dependencies
            FileAssociationService = CreateFileAssociationService();

            // 15. SharedObservableState — no dependencies
            SharedState = new SharedObservableState();
        }
        catch
        {
            // Partial-construction safety: dispose any services already created
            // before propagating the exception to the caller.
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Disposes all owned services in reverse order of construction.
    /// Uses safe-cast pattern (as IDisposable) for services that may or may not
    /// implement IDisposable, and null-conditional for nullable references
    /// (handles partial construction where later services may be null).
    /// </summary>
    public void Dispose()
    {
        // Reverse order: 15 → 1
        (SharedState as IDisposable)?.Dispose();
        (FileAssociationService as IDisposable)?.Dispose();
        (ErrorNotificationService as IDisposable)?.Dispose();
        (ExclusiveAccessManager as IDisposable)?.Dispose();
        (FileHandleReleaseStrategy as IDisposable)?.Dispose();
        SessionManager?.Dispose();
        (DatVerificationService as IDisposable)?.Dispose();
        (StatsCalculationService as IDisposable)?.Dispose();
        (PlatformFsHostFactory as IDisposable)?.Dispose();
        (ThresholdGroupingService as IDisposable)?.Dispose();
        (SimilarityGroupService as IDisposable)?.Dispose();
        (HashCacheService as IDisposable)?.Dispose();
        (BlockComparisonService as IDisposable)?.Dispose();
        (DataStoreService as IDisposable)?.Dispose();
        (ConfigService as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Creates the platform-appropriate IFileAssociationService implementation.
    /// Returns WindowsFileAssociationService on Windows, LinuxFileAssociationService on Linux,
    /// and NullFileAssociationService on all other platforms.
    /// </summary>
    private static IFileAssociationService CreateFileAssociationService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsFileAssociationService();
        if (OperatingSystem.IsLinux())
            return new LinuxFileAssociationService();
        return new NullFileAssociationService();
    }
}
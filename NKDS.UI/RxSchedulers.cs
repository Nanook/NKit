using ReactiveUI.Primitives.Concurrency;

namespace NkdsUi;

/// <summary>
/// Provides access to common Rx schedulers used throughout the application.
/// Wraps ReactiveUI.Primitives.Concurrency.AvaloniaScheduler for convenient access.
/// Supports override for unit testing via <see cref="SetSchedulerForTest"/>.
/// </summary>
internal static class RxSchedulers
{
    private static ISequencer? _testOverride;

    /// <summary>
    /// Scheduler that dispatches work to the Avalonia UI thread.
    /// In test mode, returns the override scheduler (typically <see cref="ImmediateSequencer"/>).
    /// </summary>
    public static ISequencer MainThreadScheduler => _testOverride ?? AvaloniaScheduler.Instance;

    /// <summary>
    /// Overrides the MainThreadScheduler for unit testing. Pass null to reset.
    /// </summary>
    internal static void SetSchedulerForTest(ISequencer? scheduler) => _testOverride = scheduler;
}
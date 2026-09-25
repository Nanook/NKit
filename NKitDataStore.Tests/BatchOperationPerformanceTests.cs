using NkdsUi;
using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Signals;
using System.Diagnostics;

namespace NKitDataStore.Tests;

/// <summary>
/// Performance assertion tests for batch operations on ImageListViewModel.
/// Validates that bulk operations complete within acceptable time bounds.
///
/// **Validates: Requirements 5.3, 5.4, 7.4**
/// </summary>
public class BatchOperationPerformanceTests : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public BatchOperationPerformanceTests()
    {
        EnsureReactiveUIInitialized();
        RxSchedulers.SetSchedulerForTest(ImmediateSequencer.Instance);
    }

    public void Dispose() => RxSchedulers.SetSchedulerForTest(null);

    private static void EnsureReactiveUIInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _initialized = true;
        }
    }

    private static ImageRowViewModel CreateRow(long id, string name, long size)
    {
        ImageRecord record = new ImageRecord
        {
            Id = id,
            Name = name,
            Size = size,
            SetName = "TestSet",
            Format = ImageFormat.Iso
        };
        return new ImageRowViewModel(record, "test-session");
    }

    private static ImageListViewModel CreateImageListViewModel()
    {
        ReplaySignal<IReadOnlyList<ImageSessionModel>> sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        StubDataStoreService stubService = new StubDataStoreService(sessionsSubject);
        return new ImageListViewModel(stubService);
    }

    /// <summary>
    /// Test that inserting 1000 rows via InsertRowsBatch completes within 100ms.
    /// Requirement 5.3: Support inserting 1000 rows without blocking UI thread > 100ms.
    /// </summary>
    [Fact]
    public void InsertRowsBatch_1000Rows_CompletesWithin100ms()
    {
        using ImageListViewModel vm = CreateImageListViewModel();

        List<ImageRowViewModel> rows = new List<ImageRowViewModel>(1000);
        for (int i = 0; i < 1000; i++)
        {
            rows.Add(CreateRow(i + 1, $"image_{i:D4}.iso", (i + 1) * 1024L));
        }

        // Warm up: insert a small batch first to JIT-compile paths
        List<ImageRowViewModel> warmupRows = new List<ImageRowViewModel>
        {
            CreateRow(10001, "warmup.iso", 1024)
        };
        vm.InsertRowsBatch(warmupRows);

        // Measure the actual 1000-row batch insert
        Stopwatch sw = Stopwatch.StartNew();
        vm.InsertRowsBatch(rows);
        sw.Stop();

        Assert.Equal(1001, vm.FilteredImages.Count); // 1000 + 1 warmup
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"InsertRowsBatch of 1000 rows took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    /// <summary>
    /// Test that inserting 1000 rows via InsertRowsBatch with sorting active completes within 100ms.
    /// Requirement 5.3: Support inserting 1000 rows without blocking UI thread > 100ms.
    /// </summary>
    [Fact]
    public void InsertRowsBatch_1000Rows_WithSort_CompletesWithin100ms()
    {
        using ImageListViewModel vm = CreateImageListViewModel();

        // Activate a sort (Name ascending) to exercise sorted insertion path
        vm.SortCommand.Execute("Name").Subscribe();

        List<ImageRowViewModel> rows = new List<ImageRowViewModel>(1000);
        for (int i = 0; i < 1000; i++)
        {
            rows.Add(CreateRow(i + 1, $"image_{i:D4}.iso", (i + 1) * 1024L));
        }

        // Warm up
        List<ImageRowViewModel> warmupRows = new List<ImageRowViewModel>
        {
            CreateRow(10001, "warmup.iso", 1024)
        };
        vm.InsertRowsBatch(warmupRows);

        // Measure
        Stopwatch sw = Stopwatch.StartNew();
        vm.InsertRowsBatch(rows);
        sw.Stop();

        Assert.Equal(1001, vm.FilteredImages.Count);
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"InsertRowsBatch of 1000 sorted rows took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    /// <summary>
    /// Test that reconciling 1000 images via Reconcile completes within 200ms.
    /// Requirement 7.4: Complete a Reconcile operation for 1000 images within 200ms.
    /// </summary>
    [Fact]
    public void Reconcile_1000Images_CompletesWithin200ms()
    {
        using ImageListViewModel vm = CreateImageListViewModel();

        // Pre-populate with 500 existing rows (simulates partial overlap scenario)
        List<ImageRowViewModel> existingRows = new List<ImageRowViewModel>(500);
        for (int i = 0; i < 500; i++)
        {
            existingRows.Add(CreateRow(i + 1, $"existing_{i:D4}.iso", (i + 1) * 1024L));
        }
        vm.InsertRowsBatch(existingRows);

        // Create authoritative image list of 1000 images:
        // - 500 overlap with existing (some with changed data)
        // - 500 are new additions
        List<ImageRecord> authoritativeImages = new List<ImageRecord>(1000);
        for (int i = 0; i < 1000; i++)
        {
            authoritativeImages.Add(new ImageRecord
            {
                Id = i + 1,
                Name = $"reconciled_{i:D4}.iso", // Changed name for existing rows
                Size = (i + 1) * 2048L,
                SetName = "TestSet",
                Format = ImageFormat.Iso
            });
        }

        // Warm up
        vm.Reconcile("WarmupSet", Array.Empty<ImageRecord>());

        // Measure
        Stopwatch sw = Stopwatch.StartNew();
        vm.Reconcile("TestSet", authoritativeImages);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Reconcile of 1000 images took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    /// <summary>
    /// Test that reconciling 1000 images with all-new rows (worst case: all inserts) completes within 200ms.
    /// Requirement 7.4: Complete a Reconcile operation for 1000 images within 200ms.
    /// </summary>
    [Fact]
    public void Reconcile_1000NewImages_CompletesWithin200ms()
    {
        using ImageListViewModel vm = CreateImageListViewModel();

        // Create authoritative image list of 1000 entirely new images
        List<ImageRecord> authoritativeImages = new List<ImageRecord>(1000);
        for (int i = 0; i < 1000; i++)
        {
            authoritativeImages.Add(new ImageRecord
            {
                Id = i + 1,
                Name = $"new_image_{i:D4}.iso",
                Size = (i + 1) * 1024L,
                SetName = "TestSet",
                Format = ImageFormat.Iso
            });
        }

        // Warm up
        vm.Reconcile("WarmupSet", Array.Empty<ImageRecord>());

        // Measure
        Stopwatch sw = Stopwatch.StartNew();
        vm.Reconcile("TestSet", authoritativeImages);
        sw.Stop();

        Assert.Equal(1000, vm.FilteredImages.Count);
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Reconcile of 1000 new images took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    /// <summary>
    /// Test that RemoveRowsBatch for 1000 rows completes within 100ms.
    /// Requirement 5.3: Batch operations should not block UI thread > 100ms for 1000 rows.
    /// </summary>
    [Fact]
    public void RemoveRowsBatch_1000Rows_CompletesWithin100ms()
    {
        using ImageListViewModel vm = CreateImageListViewModel();

        // Pre-populate with 1000 rows
        List<ImageRowViewModel> rows = new List<ImageRowViewModel>(1000);
        for (int i = 0; i < 1000; i++)
        {
            rows.Add(CreateRow(i + 1, $"image_{i:D4}.iso", (i + 1) * 1024L));
        }
        vm.InsertRowsBatch(rows);
        Assert.Equal(1000, vm.FilteredImages.Count);

        // Measure removal of all 1000 rows
        Stopwatch sw = Stopwatch.StartNew();
        vm.RemoveRowsBatch(_ => true);
        sw.Stop();

        Assert.Equal(0, vm.FilteredImages.Count);
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"RemoveRowsBatch of 1000 rows took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    /// <summary>
    /// Minimal stub for IDataStoreService used in performance tests.
    /// </summary>
    private sealed class StubDataStoreService : IDataStoreService
    {
        private readonly IObservable<IReadOnlyList<ImageSessionModel>> _sessions;

        public StubDataStoreService(IObservable<IReadOnlyList<ImageSessionModel>> sessions)
        {
            _sessions = sessions;
        }

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessions;
        public IObservable<IReadOnlyList<ImageRecord>> AllImages => new ReplaySignal<IReadOnlyList<ImageRecord>>(1);
        public IObservable<string> ErrorMessages => new ReplaySignal<string>(1);
        public int SessionCount => 0;

        public Task<bool> OpenDirectoryAsync(string directoryPath) => Task.FromResult(false);
        public Task<bool> OpenFileAsync(string filePath) => Task.FromResult(false);
        public void CloseSession(string sessionId) { }
        public bool IsAlreadyOpen(string path) => false;
        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null) => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public DataStore GetActiveDataStore() => null;
        public string GetActiveDataStorePath() => null;
    }
}
using NKDS.Models;
using NKDS.Mount;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace NKitDataStore.Tests;

#region Mock Infrastructure

/// <summary>
/// Mock IPlatformFsHost whose Run() blocks on a ManualResetEventSlim so tests can control
/// when it "unmounts". Optionally throws an exception to simulate host errors.
/// </summary>
internal sealed class MockPlatformFsHost : IPlatformFsHost
{
    private readonly ManualResetEventSlim _gate = new(false);
    private readonly Exception _runException;

    /// <summary>Captured parameters from the last Run() call.</summary>
    public string LastDataStorePath { get; private set; }
    public string LastMountPoint { get; private set; }
    public string LastSetName { get; private set; }

    /// <summary>If set, the host exposes a DataStorePaths property for reflection-based wiring.</summary>
    public string[] DataStorePaths { get; set; }

    public bool WasDisposed { get; private set; }
    public bool RunWasCalled { get; private set; }

    public MockPlatformFsHost(Exception runException = null)
    {
        _runException = runException;
    }

    public void Run(string dataStorePath, string mountPoint, string setName = null,
        bool showImage = true, bool showFileSystem = true, bool showSystem = false,
        bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null,
        int maxFileSystemYamlSizeKiB = 512)
    {
        RunWasCalled = true;
        LastDataStorePath = dataStorePath;
        LastMountPoint = mountPoint;
        LastSetName = setName;

        if (_runException != null)
            throw _runException;

        // Block until signaled (simulates the host event loop)
        _gate.Wait();
    }

    /// <summary>Signal the Run() method to return (simulates external unmount).</summary>
    public void SignalUnmount() => _gate.Set();

    public string GetErrorMessage(Exception ex) => $"MockError: {ex.Message}";

    public void FinalizeDatabases(string dataStorePath, string setName = null) { }

    public void Dispose()
    {
        WasDisposed = true;
        _gate.Set(); // Unblock Run() if still waiting
    }
}

/// <summary>
/// Mock IPlatformFsHost that does NOT have a DataStorePaths property.
/// Used to test that reflection-based wiring gracefully handles missing properties.
/// </summary>
internal sealed class MockPlatformFsHostNoDataStorePaths : IPlatformFsHost
{
    private readonly ManualResetEventSlim _gate = new(false);

    public bool RunWasCalled { get; private set; }

    public void Run(string dataStorePath, string mountPoint, string setName = null,
        bool showImage = true, bool showFileSystem = true, bool showSystem = false,
        bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null,
        int maxFileSystemYamlSizeKiB = 512)
    {
        RunWasCalled = true;
        _gate.Wait();
    }

    public void SignalUnmount() => _gate.Set();

    public string GetErrorMessage(Exception ex) => ex.Message;

    public void FinalizeDatabases(string dataStorePath, string setName = null) { }

    public void Dispose() => _gate.Set();
}

/// <summary>
/// Mock factory that returns a supported host. Tracks created hosts for test assertions.
/// </summary>
internal sealed class MockPlatformFsHostFactory : IPlatformFsHostFactory
{
    private readonly Func<IPlatformFsHost> _creator;

    public bool IsSupported { get; }
    public string UnsupportedReason { get; }
    public List<IPlatformFsHost> CreatedHosts { get; } = new();

    public MockPlatformFsHostFactory(Func<IPlatformFsHost> creator, bool isSupported = true, string unsupportedReason = null)
    {
        _creator = creator;
        IsSupported = isSupported;
        UnsupportedReason = unsupportedReason;
    }

    public IPlatformFsHost Create()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException(UnsupportedReason ?? "Not supported");

        IPlatformFsHost host = _creator();
        CreatedHosts.Add(host);
        return host;
    }
}

#endregion

#region 1. MountOrchestrator Validation Tests

public class MountOrchestratorValidationTests
{
    private static MountOrchestrator createOrchestrator(IPlatformFsHostFactory factory = null)
    {
        factory ??= new MockPlatformFsHostFactory(() => new MockPlatformFsHost(), isSupported: true);
        return new MountOrchestrator(factory);
    }

    [Fact]
    public async Task MountAsync_NullDataStorePaths_ReturnsError()
    {
        using MountOrchestrator orch = createOrchestrator();
        MountRequest request = new MountRequest
        {
            DataStorePaths = null!,
            MountPoint = @"Z:\"
        };

        OperationResult result = await orch.MountAsync(request);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Reason.Contains("DataStore path"));
    }

    [Fact]
    public async Task MountAsync_EmptyDataStorePaths_ReturnsError()
    {
        using MountOrchestrator orch = createOrchestrator();
        MountRequest request = new MountRequest
        {
            DataStorePaths = Array.Empty<string>(),
            MountPoint = @"Z:\"
        };

        OperationResult result = await orch.MountAsync(request);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Reason.Contains("DataStore path"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MountAsync_NullOrEmptyMountPoint_ReturnsError(string mountPoint)
    {
        using MountOrchestrator orch = createOrchestrator();
        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store.nkds" },
            MountPoint = mountPoint!
        };

        OperationResult result = await orch.MountAsync(request);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Reason.Contains("Mount point"));
    }

    [Fact]
    public async Task MountAsync_DuplicateMountPoint_ReturnsError()
    {
        using MountOrchestrator orch = createOrchestrator();
        MountRequest request1 = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store1.nkds" },
            MountPoint = @"Z:\"
        };
        MountRequest request2 = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store2.nkds" },
            MountPoint = @"Z:\"
        };

        OperationResult result1 = await orch.MountAsync(request1);
        Assert.True(result1.Success);

        OperationResult result2 = await orch.MountAsync(request2);
        Assert.False(result2.Success);
        Assert.Contains(result2.Errors, e => e.Reason.Contains("already in use"));
    }

    [Fact]
    public async Task MountAsync_UnsupportedFactory_ReturnsError()
    {
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(
            () => throw new PlatformNotSupportedException(),
            isSupported: false,
            unsupportedReason: "Dokan not installed");

        using MountOrchestrator orch = new MountOrchestrator(factory);
        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store.nkds" },
            MountPoint = @"Z:\"
        };

        OperationResult result = await orch.MountAsync(request);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Reason.Contains("Dokan not installed"));
    }

    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() => new MountOrchestrator(null!));
}

#endregion

#region 2. MountOrchestrator Set Name Normalization

public class MountOrchestratorSetNameNormalizationTests
{
    [Theory]
    [InlineData("All")]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("aLl")]
    [InlineData("alL")]
    [InlineData("ALl")]
    [InlineData("aLL")]
    public void NormalizeSetName_AllCaseInsensitive_ReturnsNull(string setName)
    {
        string result = MountOrchestrator.NormalizeSetName(setName);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("MySet")]
    [InlineData("GameCube")]
    [InlineData("Wii")]
    [InlineData("Favorites")]
    [InlineData("all games")]
    [InlineData("Allx")]
    [InlineData("xAll")]
    public void NormalizeSetName_SpecificSetName_PassedThroughUnchanged(string setName)
    {
        string result = MountOrchestrator.NormalizeSetName(setName);
        Assert.Equal(setName, result);
    }

    [Fact]
    public void NormalizeSetName_Null_ReturnsNull()
    {
        string result = MountOrchestrator.NormalizeSetName(null);
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeSetName_EmptyString_ReturnedUnchanged()
    {
        // Empty string is not "All", so it should pass through
        string result = MountOrchestrator.NormalizeSetName("");
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeSetName_WhitespaceOnly_ReturnedUnchanged()
    {
        string result = MountOrchestrator.NormalizeSetName("   ");
        Assert.Equal("   ", result);
    }
}

#endregion

#region 3. MountOrchestrator State Events

public class MountOrchestratorStateEventTests
{
    [Fact]
    public async Task MountAsync_HostRunCompletesNormally_EmitsMountingThenMounted()
    {
        ConcurrentQueue<(string MountPoint, MountState State)> states = new ConcurrentQueue<(string MountPoint, MountState State)>();
        MockPlatformFsHost host = new MockPlatformFsHost();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);
        orch.StateChanged += (_, e) => states.Enqueue((e.MountPoint, e.State));

        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store.nkds" },
            MountPoint = @"Z:\"
        };

        OperationResult result = await orch.MountAsync(request);
        Assert.True(result.Success);

        // Give the background thread time to start and emit Mounted
        SpinWait.SpinUntil(() => states.Any(s => s.State == MountState.Mounted), TimeSpan.FromSeconds(5));

        List<(string MountPoint, MountState State)> stateList = states.ToList();
        Assert.Contains(stateList, s => s.State == MountState.Mounting);
        Assert.Contains(stateList, s => s.State == MountState.Mounted);

        // Mounting should come before Mounted (using queue preserves insertion order)
        int mountingIndex = stateList.FindIndex(s => s.State == MountState.Mounting);
        int mountedIndex = stateList.FindIndex(s => s.State == MountState.Mounted);
        Assert.True(mountingIndex < mountedIndex, "Mounting should be emitted before Mounted");
    }

    [Fact]
    public async Task MountAsync_HostRunThrows_EmitsMountingThenError()
    {
        ConcurrentQueue<(string MountPoint, MountState State, string Error)> states = new ConcurrentQueue<(string MountPoint, MountState State, string Error)>();
        MockPlatformFsHost host = new MockPlatformFsHost(runException: new InvalidOperationException("Dokan init failed"));
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);
        orch.StateChanged += (_, e) => states.Enqueue((e.MountPoint, e.State, e.ErrorMessage));

        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store.nkds" },
            MountPoint = @"Z:\"
        };

        OperationResult result = await orch.MountAsync(request);
        Assert.True(result.Success); // MountAsync returns success because the thread was started

        // Give the background thread time to throw and emit Error
        SpinWait.SpinUntil(() => states.Any(s => s.State == MountState.Error), TimeSpan.FromSeconds(5));

        List<(string MountPoint, MountState State, string Error)> stateList = states.ToList();
        Assert.Contains(stateList, s => s.State == MountState.Mounting);
        Assert.Contains(stateList, s => s.State == MountState.Error);

        (string MountPoint, MountState State, string Error) errorState = stateList.First(s => s.State == MountState.Error);
        Assert.Contains("Dokan init failed", errorState.Error);
    }

    [Fact]
    public async Task Unmount_ActiveMount_EmitsUnmountingThenUnmounted()
    {
        ConcurrentQueue<(string MountPoint, MountState State)> states = new ConcurrentQueue<(string MountPoint, MountState State)>();
        MockPlatformFsHost host = new MockPlatformFsHost();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);
        orch.StateChanged += (_, e) => states.Enqueue((e.MountPoint, e.State));

        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store.nkds" },
            MountPoint = @"Z:\"
        };

        await orch.MountAsync(request);

        // Wait for mount to be active
        SpinWait.SpinUntil(() => states.Any(s => s.State == MountState.Mounted), TimeSpan.FromSeconds(5));

        // Clear states to isolate unmount events
        while (states.TryDequeue(out _)) { }

        OperationResult unmountResult = orch.Unmount(@"Z:\");
        Assert.True(unmountResult.Success);

        List<(string MountPoint, MountState State)> stateList = states.ToList();
        Assert.Contains(stateList, s => s.State == MountState.Unmounting);
        Assert.Contains(stateList, s => s.State == MountState.Unmounted);
    }
}

#endregion

#region 4. MountOrchestrator DataStorePaths Wiring

public class MountOrchestratorDataStorePathsWiringTests
{
    [Fact]
    public async Task SetDataStorePaths_PrimaryPathPassedToRun()
    {
        MockPlatformFsHost host = new MockPlatformFsHost();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);

        string[] paths = new[] { @"C:\data\store1.nkds", @"C:\data\store2.nkds" };
        MountRequest request = new MountRequest
        {
            DataStorePaths = paths,
            MountPoint = @"Z:\"
        };

        await orch.MountAsync(request);

        // Give the background thread time to start
        SpinWait.SpinUntil(() => host.RunWasCalled, TimeSpan.FromSeconds(5));

        // The first DataStorePath (primary) should be passed to Run()
        Assert.True(host.RunWasCalled);
        Assert.Equal(paths[0], host.LastDataStorePath);
    }

    [Fact]
    public async Task SetDataStorePaths_HostLacksProperty_NoErrorOccurs()
    {
        MockPlatformFsHostNoDataStorePaths host = new MockPlatformFsHostNoDataStorePaths();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);

        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { @"C:\data\store.nkds" },
            MountPoint = @"Z:\"
        };

        // Should not throw even though host doesn't have DataStorePaths property
        OperationResult result = await orch.MountAsync(request);
        Assert.True(result.Success);

        // Give the background thread time to start
        SpinWait.SpinUntil(() => host.RunWasCalled, TimeSpan.FromSeconds(5));
        Assert.True(host.RunWasCalled);
    }

    [Fact]
    public async Task SetDataStorePaths_MultiplePaths_PrimaryPassedToRun()
    {
        MockPlatformFsHost host = new MockPlatformFsHost();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);

        string[] paths = new[]
        {
            @"C:\data\store1.nkds",
            @"D:\backup\store2.nkds",
            @"E:\archive\store3.nkds"
        };
        MountRequest request = new MountRequest
        {
            DataStorePaths = paths,
            MountPoint = @"Z:\"
        };

        await orch.MountAsync(request);

        // Give the background thread time to start
        SpinWait.SpinUntil(() => host.RunWasCalled, TimeSpan.FromSeconds(5));

        // The primary path (first) should be passed to Run()
        Assert.True(host.RunWasCalled);
        Assert.Equal(paths[0], host.LastDataStorePath);
    }

    [Fact]
    public async Task SetDataStorePaths_SingleNkdsFile_PassedAsDataStorePath()
    {
        MockPlatformFsHost host = new MockPlatformFsHost();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);

        string nkdsFilePath = @"C:\data\MyCollection.nkds";
        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { nkdsFilePath },
            MountPoint = @"Z:\"
        };

        await orch.MountAsync(request);

        SpinWait.SpinUntil(() => host.RunWasCalled, TimeSpan.FromSeconds(5));

        // The .nkds file path should be passed directly to Run()
        Assert.True(host.RunWasCalled);
        Assert.Equal(nkdsFilePath, host.LastDataStorePath);
    }

    [Fact]
    public async Task SetDataStorePaths_DirectoryPath_PassedAsDataStorePath()
    {
        MockPlatformFsHost host = new MockPlatformFsHost();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => host);

        using MountOrchestrator orch = new MountOrchestrator(factory);

        string directoryPath = @"C:\data\MyCollection";
        MountRequest request = new MountRequest
        {
            DataStorePaths = new[] { directoryPath },
            MountPoint = @"Z:\"
        };

        await orch.MountAsync(request);

        SpinWait.SpinUntil(() => host.RunWasCalled, TimeSpan.FromSeconds(5));

        // Directory path should be passed directly to Run()
        Assert.Equal(directoryPath, host.LastDataStorePath);
    }
}

#endregion

#region 5. PlatformFsHostFactory Tests

public class PlatformFsHostFactoryTests
{
    [Fact]
    public void CreateForCurrentPlatform_OnWindows_ReturnsSupportedFactory()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Skip on non-Windows

        IPlatformFsHostFactory factory = PlatformFsHostFactory.CreateForCurrentPlatform();

        Assert.True(factory.IsSupported);
        Assert.Null(factory.UnsupportedReason);
    }

    [Fact]
    public void SupportedFactory_Create_ReturnsNonNullHost()
    {
        MockPlatformFsHost mockHost = new MockPlatformFsHost();
        PlatformFsHostFactory factory = new PlatformFsHostFactory(() => mockHost);

        Assert.True(factory.IsSupported);

        IPlatformFsHost host = factory.Create();
        Assert.NotNull(host);
        Assert.Same(mockHost, host);
    }

    [Fact]
    public void UnsupportedFactory_Create_ThrowsPlatformNotSupportedException()
    {
        PlatformFsHostFactory factory = new PlatformFsHostFactory("FUSE not available on this platform");

        Assert.False(factory.IsSupported);
        Assert.Equal("FUSE not available on this platform", factory.UnsupportedReason);

        Assert.Throws<PlatformNotSupportedException>(() => factory.Create());
    }

    [Fact]
    public void UnsupportedFactory_IsSupported_ReturnsFalse()
    {
        PlatformFsHostFactory factory = new PlatformFsHostFactory("Not supported");

        Assert.False(factory.IsSupported);
    }

    [Fact]
    public void SupportedFactory_IsSupported_ReturnsTrue()
    {
        PlatformFsHostFactory factory = new PlatformFsHostFactory(() => new MockPlatformFsHost());

        Assert.True(factory.IsSupported);
        Assert.Null(factory.UnsupportedReason);
    }
}

#endregion

#region 6. MountOrchestrator Lifecycle

public class MountOrchestratorLifecycleTests
{
    [Fact]
    public async Task Dispose_UnmountsAllActiveMounts()
    {
        List<MockPlatformFsHost> hosts = new List<MockPlatformFsHost>();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() =>
        {
            MockPlatformFsHost h = new MockPlatformFsHost();
            hosts.Add(h);
            return h;
        });

        MountOrchestrator orch = new MountOrchestrator(factory);

        await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store1.nkds" },
            MountPoint = @"X:\"
        });
        await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store2.nkds" },
            MountPoint = @"Y:\"
        });

        // Wait for both hosts to start running
        SpinWait.SpinUntil(() => hosts.Count == 2 && hosts.All(h => h.RunWasCalled), TimeSpan.FromSeconds(5));

        orch.Dispose();

        // All hosts should have been disposed
        Assert.All(hosts, h => Assert.True(h.WasDisposed));
    }

    [Fact]
    public async Task GetActiveMounts_ReturnsCorrectSnapshot()
    {
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => new MockPlatformFsHost());

        using MountOrchestrator orch = new MountOrchestrator(factory);

        await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store1.nkds" },
            MountPoint = @"X:\",
            SetName = "GameCube"
        });
        await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store2.nkds" },
            MountPoint = @"Y:\",
            SetName = null
        });

        IReadOnlyList<ActiveMount> activeMounts = orch.GetActiveMounts();

        Assert.Equal(2, activeMounts.Count);

        ActiveMount mountX = activeMounts.FirstOrDefault(m => m.MountPoint == @"X:\");
        Assert.NotNull(mountX);
        Assert.Equal("GameCube", mountX.SetName);
        Assert.Equal(@"C:\store1.nkds", mountX.DataStorePath);

        ActiveMount mountY = activeMounts.FirstOrDefault(m => m.MountPoint == @"Y:\");
        Assert.NotNull(mountY);
        Assert.Equal("All", mountY.SetName); // null set name displays as "All"
        Assert.Equal(@"C:\store2.nkds", mountY.DataStorePath);
    }

    [Fact]
    public void Unmount_NonExistentMountPoint_ReturnsError()
    {
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => new MockPlatformFsHost());
        using MountOrchestrator orch = new MountOrchestrator(factory);

        OperationResult result = orch.Unmount(@"Z:\nonexistent");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Reason.Contains("No active mount found"));
    }

    [Fact]
    public void Unmount_EmptyMountPoint_ReturnsError()
    {
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => new MockPlatformFsHost());
        using MountOrchestrator orch = new MountOrchestrator(factory);

        OperationResult result = orch.Unmount("");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Reason.Contains("Mount point"));
    }

    [Fact]
    public async Task Dispose_ThenMountAsync_ThrowsObjectDisposedException()
    {
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => new MockPlatformFsHost());
        MountOrchestrator orch = new MountOrchestrator(factory);
        orch.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await orch.MountAsync(new MountRequest
            {
                DataStorePaths = new[] { @"C:\store.nkds" },
                MountPoint = @"Z:\"
            }));
    }

    [Fact]
    public async Task MountAsync_DuplicateMountPoint_CaseInsensitive_ReturnsError()
    {
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => new MockPlatformFsHost());
        using MountOrchestrator orch = new MountOrchestrator(factory);

        OperationResult result1 = await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store.nkds" },
            MountPoint = @"Z:\"
        });
        Assert.True(result1.Success);

        // Same mount point, different case
        OperationResult result2 = await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store2.nkds" },
            MountPoint = @"z:\"
        });
        Assert.False(result2.Success);
        Assert.Contains(result2.Errors, e => e.Reason.Contains("already in use"));
    }

    [Fact]
    public async Task Dispose_EmitsUnmountingAndUnmountedForEachMount()
    {
        ConcurrentQueue<(string MountPoint, MountState State)> states = new ConcurrentQueue<(string MountPoint, MountState State)>();
        MockPlatformFsHostFactory factory = new MockPlatformFsHostFactory(() => new MockPlatformFsHost());

        MountOrchestrator orch = new MountOrchestrator(factory);
        orch.StateChanged += (_, e) => states.Enqueue((e.MountPoint, e.State));

        await orch.MountAsync(new MountRequest
        {
            DataStorePaths = new[] { @"C:\store.nkds" },
            MountPoint = @"X:\"
        });

        // Wait for mount to be active
        SpinWait.SpinUntil(() => states.Any(s => s.State == MountState.Mounted), TimeSpan.FromSeconds(5));

        // Clear states to isolate dispose events
        while (states.TryDequeue(out _)) { }

        orch.Dispose();

        List<(string MountPoint, MountState State)> stateList = states.ToList();
        Assert.Contains(stateList, s => s.MountPoint == @"X:\" && s.State == MountState.Unmounting);
        Assert.Contains(stateList, s => s.MountPoint == @"X:\" && s.State == MountState.Unmounted);
    }
}

#endregion
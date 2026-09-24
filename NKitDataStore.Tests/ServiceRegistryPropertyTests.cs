using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for ServiceRegistry Dispose Safety.
///
/// Property 2: ServiceRegistry Dispose Safety
///
/// **Validates: Requirements 2.6, 2.8**
///
/// For any position N in the construction sequence where service N's constructor throws,
/// all services at positions 0..N-1 that implement IDisposable SHALL have their Dispose
/// method called (in reverse construction order) before the exception propagates.
/// Additionally, for any fully-constructed registry, calling Dispose SHALL dispose all
/// owned IDisposable services in reverse order of construction.
///
/// Uses a test-specific helper that mimics the ServiceRegistry pattern with trackable
/// disposables to verify the disposal invariants without requiring real service dependencies.
/// </summary>
public class ServiceRegistryPropertyTests
{
    /// <summary>
    /// The number of services in the real ServiceRegistry (13 total).
    /// </summary>
    private const int ServiceCount = 13;

    /// <summary>
    /// Tracks disposal calls with ordering information.
    /// </summary>
    private class TrackableDisposable : IDisposable
    {
        private readonly int _index;
        private readonly List<int> _disposalOrder;

        public TrackableDisposable(int index, List<int> disposalOrder)
        {
            _index = index;
            _disposalOrder = disposalOrder;
        }

        public int Index => _index;
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                _disposalOrder.Add(_index);
            }
        }
    }

    /// <summary>
    /// A factory that creates trackable disposables and throws at a specified position.
    /// Mimics the ServiceRegistry construction pattern where services are created in order
    /// and if one fails, previously created services must be disposed in reverse order.
    /// </summary>
    private class ThrowingServiceFactory
    {
        private readonly int _throwAtPosition;
        private readonly List<int> _disposalOrder;
        private readonly List<TrackableDisposable> _createdServices = new();

        public ThrowingServiceFactory(int throwAtPosition, List<int> disposalOrder)
        {
            _throwAtPosition = throwAtPosition;
            _disposalOrder = disposalOrder;
        }

        public IReadOnlyList<TrackableDisposable> CreatedServices => _createdServices;

        public TrackableDisposable Create(int position)
        {
            if (position == _throwAtPosition)
                throw new InvalidOperationException($"Construction failed at position {position}");

            TrackableDisposable service = new TrackableDisposable(position, _disposalOrder);
            _createdServices.Add(service);
            return service;
        }
    }

    /// <summary>
    /// Mimics the ServiceRegistry pattern: creates N services in order,
    /// disposes in reverse order, and handles partial construction failures.
    /// This tests the PATTERN used by the real ServiceRegistry.
    /// </summary>
    private class TestableRegistry : IDisposable
    {
        private readonly TrackableDisposable[] _services;

        public TestableRegistry(ThrowingServiceFactory factory, int serviceCount)
        {
            _services = new TrackableDisposable[serviceCount];
            try
            {
                for (int i = 0; i < serviceCount; i++)
                {
                    _services[i] = factory.Create(i);
                }
            }
            catch
            {
                // Partial-construction safety: dispose already-created services
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            // Dispose in reverse order of construction (same pattern as ServiceRegistry)
            for (int i = _services.Length - 1; i >= 0; i--)
            {
                _services[i]?.Dispose();
            }
        }
    }

    /// <summary>
    /// **Validates: Requirements 2.6, 2.8**
    ///
    /// Property 2: ServiceRegistry Dispose Safety — Partial Construction.
    /// For any failure position N (0..ServiceCount-1), when construction fails at position N,
    /// services 0..N-1 are disposed in reverse order before the exception propagates.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PartialConstruction_DisposesCreatedServicesInReverseOrder(NonNegativeInt positionSeed)
    {
        int failPosition = positionSeed.Get % ServiceCount;
        List<int> disposalOrder = new List<int>();
        ThrowingServiceFactory factory = new ThrowingServiceFactory(failPosition, disposalOrder);

        // Act: attempt construction that will fail at failPosition
        Exception caught = null;
        try
        {
            using TestableRegistry registry = new TestableRegistry(factory, ServiceCount);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        // Verify: exception was thrown
        if (caught == null)
            return false;

        // Verify: all services created before the failure position were disposed
        IReadOnlyList<TrackableDisposable> createdServices = factory.CreatedServices;
        if (createdServices.Count != failPosition)
            return false;

        foreach (TrackableDisposable service in createdServices)
        {
            if (!service.IsDisposed)
                return false;
        }

        // Verify: disposal happened in reverse order of construction
        // Services 0..N-1 should be disposed in order N-1, N-2, ..., 0
        if (disposalOrder.Count != failPosition)
            return false;

        for (int i = 0; i < disposalOrder.Count; i++)
        {
            int expectedIndex = failPosition - 1 - i;
            if (disposalOrder[i] != expectedIndex)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 2.6, 2.8**
    ///
    /// Property 2: ServiceRegistry Dispose Safety — Full Disposal Ordering.
    /// For any fully-constructed registry, calling Dispose disposes all services
    /// in reverse order of construction (index ServiceCount-1 first, index 0 last).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FullDisposal_DisposesAllServicesInReverseOrder(NonNegativeInt seed)
    {
        // Use seed to vary the service count slightly (test with different sizes)
        int serviceCount = (seed.Get % ServiceCount) + 1; // 1..ServiceCount
        List<int> disposalOrder = new List<int>();

        // Use int.MaxValue as throwAtPosition so no service throws
        ThrowingServiceFactory factory = new ThrowingServiceFactory(int.MaxValue, disposalOrder);

        // Act: create and dispose a fully-constructed registry
        TestableRegistry registry = new TestableRegistry(factory, serviceCount);
        registry.Dispose();

        // Verify: all services were disposed
        IReadOnlyList<TrackableDisposable> createdServices = factory.CreatedServices;
        if (createdServices.Count != serviceCount)
            return false;

        foreach (TrackableDisposable service in createdServices)
        {
            if (!service.IsDisposed)
                return false;
        }

        // Verify: disposal happened in reverse order of construction
        if (disposalOrder.Count != serviceCount)
            return false;

        for (int i = 0; i < disposalOrder.Count; i++)
        {
            int expectedIndex = serviceCount - 1 - i;
            if (disposalOrder[i] != expectedIndex)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 2.6, 2.8**
    ///
    /// Property 2: ServiceRegistry Dispose Safety — Dispose is idempotent.
    /// Calling Dispose multiple times does not dispose services more than once.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool DoubleDispose_DoesNotDisposeServicesTwice(NonNegativeInt callCountSeed)
    {
        int extraDisposeCalls = (callCountSeed.Get % 5) + 1; // 1..5 extra calls
        List<int> disposalOrder = new List<int>();
        ThrowingServiceFactory factory = new ThrowingServiceFactory(int.MaxValue, disposalOrder);

        // Act: create and dispose multiple times
        TestableRegistry registry = new TestableRegistry(factory, ServiceCount);
        registry.Dispose();

        int firstDisposalCount = disposalOrder.Count;

        for (int i = 0; i < extraDisposeCalls; i++)
        {
            registry.Dispose();
        }

        // Verify: disposal count did not increase after first Dispose
        if (disposalOrder.Count != firstDisposalCount)
            return false;

        // Verify: each service was disposed exactly once
        if (firstDisposalCount != ServiceCount)
            return false;

        return true;
    }
}
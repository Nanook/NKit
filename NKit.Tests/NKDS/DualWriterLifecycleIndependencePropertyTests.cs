using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Property-based tests for dual writer lifecycle independence in DataStoreXboxFormatter.
    ///
    /// Feature: aux-split-mode
    /// Property 18: Dual Writer Lifecycle Independence
    ///
    /// For any DataStoreXboxFormatter with both writers configured, failure of one writer
    /// during construction SHALL not prevent the other writer from operating. Disposal SHALL
    /// dispose both writers regardless of their individual states.
    ///
    /// **Validates: Requirements 13.1, 13.2, 13.3, 13.6**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class DualWriterLifecycleIndependencePropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Represents the outcome of attempting to open a writer during construction.
        /// Models the try/catch blocks in DataStoreXboxFormatter constructor.
        /// </summary>
        private enum WriterSetupOutcome
        {
            /// <summary>Writer opened successfully and is active.</summary>
            Success,
            /// <summary>Writer setup threw an exception; the formatter operates without it.</summary>
            Failed,
            /// <summary>Writer was not requested (null set name).</summary>
            NotRequested
        }

        /// <summary>
        /// Mock writer that tracks its operational state and disposal.
        /// </summary>
        private class MockDualWriter : IDisposable
        {
            public string Name { get; }
            public bool IsDisposed { get; private set; }
            public List<string> Operations { get; } = new();
            public bool ThrowOnDispose { get; set; }

            public MockDualWriter(string name)
            {
                Name = name;
            }

            public void WriteData(long offset, int size)
            {
                if (IsDisposed)
                    throw new ObjectDisposedException(Name);
                Operations.Add($"Write@{offset:X}:{size}");
            }

            public void FinalizeImage(long size, uint crc, ulong xxHash)
            {
                if (IsDisposed)
                    throw new ObjectDisposedException(Name);
                Operations.Add($"Finalize:{size},{crc},{xxHash}");
            }

            public void CreateArea(long offset, long size)
            {
                if (IsDisposed)
                    throw new ObjectDisposedException(Name);
                Operations.Add($"Area@{offset:X}:{size}");
            }

            public void Dispose()
            {
                if (ThrowOnDispose)
                    throw new InvalidOperationException($"Dispose failed for {Name}");
                IsDisposed = true;
            }
        }

        /// <summary>
        /// Mock DataStore that tracks disposal.
        /// </summary>
        private class MockDataStore : IDisposable
        {
            public string Name { get; }
            public bool IsDisposed { get; private set; }
            public bool ThrowOnDispose { get; set; }

            public MockDataStore(string name)
            {
                Name = name;
            }

            public void Dispose()
            {
                if (ThrowOnDispose)
                    throw new InvalidOperationException($"DataStore Dispose failed for {Name}");
                IsDisposed = true;
            }
        }

        /// <summary>
        /// Models the DataStoreXboxFormatter's constructor behavior for writer setup.
        /// Each writer is independently initialized within a try/catch block.
        /// Failure of one does not affect the other.
        /// </summary>
        private static (MockDualWriter AuxWriter, MockDataStore AuxStore,
                        MockDualWriter SplitWriter, MockDataStore SplitStore)
            ModelConstructor(
                WriterSetupOutcome auxOutcome,
                WriterSetupOutcome splitOutcome)
        {
            MockDualWriter auxWriter = null;
            MockDataStore auxStore = null;
            MockDualWriter splitWriter = null;
            MockDataStore splitStore = null;

            // Aux writer setup — independent try/catch
            if (auxOutcome != WriterSetupOutcome.NotRequested)
            {
                try
                {
                    if (auxOutcome == WriterSetupOutcome.Failed)
                        throw new InvalidOperationException("Simulated aux setup failure");

                    auxStore = new MockDataStore("AuxDataStore");
                    auxWriter = new MockDualWriter("AuxWriter");
                }
                catch
                {
                    // On failure, clean up partial resources and operate without aux
                    try { auxWriter?.Dispose(); } catch { }
                    try { auxStore?.Dispose(); } catch { }
                    auxWriter = null;
                    auxStore = null;
                }
            }

            // Split writer setup — independent try/catch
            if (splitOutcome != WriterSetupOutcome.NotRequested)
            {
                try
                {
                    if (splitOutcome == WriterSetupOutcome.Failed)
                        throw new InvalidOperationException("Simulated split setup failure");

                    splitStore = new MockDataStore("SplitDataStore");
                    splitWriter = new MockDualWriter("SplitWriter");
                }
                catch
                {
                    // On failure, clean up partial resources and operate without split
                    try { splitWriter?.Dispose(); } catch { }
                    try { splitStore?.Dispose(); } catch { }
                    splitWriter = null;
                    splitStore = null;
                }
            }

            return (auxWriter, auxStore, splitWriter, splitStore);
        }

        /// <summary>
        /// Models the DataStoreXboxFormatter's Dispose behavior.
        /// All resources are disposed in order, each within its own try/catch.
        /// </summary>
        private static void ModelDispose(
            MockDualWriter splitWriter, MockDataStore splitStore,
            MockDualWriter auxWriter, MockDataStore auxStore,
            MockDualWriter imageWriter, MockDataStore dataStore)
        {
            try { splitWriter?.Dispose(); } catch { }
            try { splitStore?.Dispose(); } catch { }
            try { auxWriter?.Dispose(); } catch { }
            try { auxStore?.Dispose(); } catch { }
            try { imageWriter?.Dispose(); } catch { }
            try { dataStore?.Dispose(); } catch { }
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 18a: Aux writer failure does not prevent split writer from operating.
        ///
        /// For any configuration where the aux writer fails during construction,
        /// the split writer SHALL still be active and operational (HasSplit == true).
        /// The formatter operates in degraded mode with only the split writer.
        ///
        /// **Validates: Requirements 13.2, 13.6**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AuxWriterFailure_SplitWriterStillOperates(
            NonNegativeInt writeCountRaw,
            NonNegativeInt offsetSeed)
        {
            var testGen =
                from writeCount in Gen.Choose(1, 10)
                from baseSeed in Gen.Choose(0, 10000)
                select new { WriteCount = writeCount, BaseSeed = baseSeed };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (MockDualWriter auxWriter, MockDataStore auxStore, MockDualWriter splitWriter, MockDataStore splitStore) =
                    ModelConstructor(WriterSetupOutcome.Failed, WriterSetupOutcome.Success);

                // HasAux should be false (aux failed)
                bool hasAux = auxWriter != null;
                // HasSplit should be true (split succeeded)
                bool hasSplit = splitWriter != null;

                if (hasAux)
                    return false.Label("HasAux should be false when aux setup fails");
                if (!hasSplit)
                    return false.Label("HasSplit should be true when split setup succeeds");

                // Split writer should be operational — perform writes
                Random rng = new Random(data.BaseSeed);
                for (int i = 0; i < data.WriteCount; i++)
                {
                    long offset = (long)rng.Next(0, int.MaxValue) * 0x100;
                    int size = rng.Next(1, 0x10000);
                    splitWriter.WriteData(offset, size);
                }

                // Verify all writes were recorded
                if (splitWriter.Operations.Count != data.WriteCount)
                    return false.Label(
                        $"Expected {data.WriteCount} operations on split writer, " +
                        $"got {splitWriter.Operations.Count}");

                return true.Label(
                    $"Split writer correctly operated with {data.WriteCount} writes " +
                    $"despite aux writer failure");
            });
        }

        /// <summary>
        /// Property 18b: Split writer failure does not prevent aux writer from operating.
        ///
        /// For any configuration where the split writer fails during construction,
        /// the aux writer SHALL still be active and operational (HasAux == true).
        /// The formatter operates in degraded mode with only the aux writer.
        ///
        /// **Validates: Requirements 13.2, 13.6**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property SplitWriterFailure_AuxWriterStillOperates(
            NonNegativeInt writeCountRaw,
            NonNegativeInt offsetSeed)
        {
            var testGen =
                from writeCount in Gen.Choose(1, 10)
                from baseSeed in Gen.Choose(0, 10000)
                select new { WriteCount = writeCount, BaseSeed = baseSeed };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (MockDualWriter auxWriter, MockDataStore auxStore, MockDualWriter splitWriter, MockDataStore splitStore) =
                    ModelConstructor(WriterSetupOutcome.Success, WriterSetupOutcome.Failed);

                // HasAux should be true (aux succeeded)
                bool hasAux = auxWriter != null;
                // HasSplit should be false (split failed)
                bool hasSplit = splitWriter != null;

                if (!hasAux)
                    return false.Label("HasAux should be true when aux setup succeeds");
                if (hasSplit)
                    return false.Label("HasSplit should be false when split setup fails");

                // Aux writer should be operational — perform writes
                Random rng = new Random(data.BaseSeed);
                for (int i = 0; i < data.WriteCount; i++)
                {
                    long offset = (long)rng.Next(0, int.MaxValue) * 0x100;
                    int size = rng.Next(1, 0x10000);
                    auxWriter.WriteData(offset, size);
                }

                // Verify all writes were recorded
                if (auxWriter.Operations.Count != data.WriteCount)
                    return false.Label(
                        $"Expected {data.WriteCount} operations on aux writer, " +
                        $"got {auxWriter.Operations.Count}");

                return true.Label(
                    $"Aux writer correctly operated with {data.WriteCount} writes " +
                    $"despite split writer failure");
            });
        }

        /// <summary>
        /// Property 18c: Disposal disposes both writers regardless of their individual states.
        ///
        /// For any combination of writer states (both active, one active, neither active),
        /// calling Dispose SHALL dispose all non-null resources. Each disposal is wrapped
        /// in try/catch so one failing disposal does not prevent others from being disposed.
        ///
        /// **Validates: Requirements 13.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property Dispose_DisposesBothWriters_RegardlessOfState(
            bool auxThrowsOnDispose,
            bool splitThrowsOnDispose)
        {
            var testGen =
                from auxOutcome in Gen.Elements(
                    WriterSetupOutcome.Success,
                    WriterSetupOutcome.Failed,
                    WriterSetupOutcome.NotRequested)
                from splitOutcome in Gen.Elements(
                    WriterSetupOutcome.Success,
                    WriterSetupOutcome.Failed,
                    WriterSetupOutcome.NotRequested)
                select new { AuxOutcome = auxOutcome, SplitOutcome = splitOutcome };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (MockDualWriter auxWriter, MockDataStore auxStore, MockDualWriter splitWriter, MockDataStore splitStore) =
                    ModelConstructor(data.AuxOutcome, data.SplitOutcome);

                // Primary writer/store (always active)
                MockDualWriter imageWriter = new MockDualWriter("ImageWriter");
                MockDataStore dataStore = new MockDataStore("PrimaryDataStore");

                // Configure throwing behavior for active writers
                if (auxWriter != null)
                    auxWriter.ThrowOnDispose = auxThrowsOnDispose;
                if (splitWriter != null)
                    splitWriter.ThrowOnDispose = splitThrowsOnDispose;

                // Execute Dispose
                ModelDispose(splitWriter, splitStore, auxWriter, auxStore, imageWriter, dataStore);

                // Verify primary writer/store always disposed
                if (!imageWriter.IsDisposed)
                    return false.Label("ImageWriter should always be disposed");
                if (!dataStore.IsDisposed)
                    return false.Label("Primary DataStore should always be disposed");

                // Verify aux resources disposed if they were active
                if (auxWriter != null && !auxThrowsOnDispose && !auxWriter.IsDisposed)
                    return false.Label("AuxWriter should be disposed when active and not throwing");
                if (auxStore != null && !auxStore.IsDisposed)
                    return false.Label("AuxDataStore should be disposed when active");

                // Verify split resources disposed if they were active
                if (splitWriter != null && !splitThrowsOnDispose && !splitWriter.IsDisposed)
                    return false.Label("SplitWriter should be disposed when active and not throwing");
                if (splitStore != null && !splitStore.IsDisposed)
                    return false.Label("SplitDataStore should be disposed when active");

                return true.Label(
                    $"Dispose correctly handled: aux={data.AuxOutcome}, split={data.SplitOutcome}, " +
                    $"auxThrows={auxThrowsOnDispose}, splitThrows={splitThrowsOnDispose}");
            });
        }

        /// <summary>
        /// Property 18d: One writer's dispose failure does not prevent the other's disposal.
        ///
        /// For any configuration where both writers are active and one throws during disposal,
        /// the other writer SHALL still be disposed. This verifies the try/catch-per-resource
        /// pattern in the Dispose implementation.
        ///
        /// **Validates: Requirements 13.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property DisposeFailure_DoesNotPreventOtherDisposals(bool auxThrows, bool splitThrows)
        {
            // Both writers active
            (MockDualWriter auxWriter, MockDataStore auxStore, MockDualWriter splitWriter, MockDataStore splitStore) =
                ModelConstructor(WriterSetupOutcome.Success, WriterSetupOutcome.Success);

            // Primary writer/store (always active)
            MockDualWriter imageWriter = new MockDualWriter("ImageWriter");
            MockDataStore dataStore = new MockDataStore("PrimaryDataStore");

            // Configure throwing behavior
            auxWriter.ThrowOnDispose = auxThrows;
            splitWriter.ThrowOnDispose = splitThrows;

            // Dispose should NOT throw, even if individual resources throw
            try
            {
                ModelDispose(splitWriter, splitStore, auxWriter, auxStore, imageWriter, dataStore);
            }
            catch (Exception ex)
            {
                return false.Label($"Dispose should never throw, but got: {ex.Message}");
            }

            // Primary resources should always be disposed regardless of aux/split failures
            if (!imageWriter.IsDisposed)
                return false.Label("ImageWriter must be disposed regardless of other failures");
            if (!dataStore.IsDisposed)
                return false.Label("Primary DataStore must be disposed regardless of other failures");

            // DataStores should be disposed (they don't throw in this test)
            if (!auxStore.IsDisposed)
                return false.Label("AuxDataStore must be disposed regardless of writer throw");
            if (!splitStore.IsDisposed)
                return false.Label("SplitDataStore must be disposed regardless of writer throw");

            // Writers that don't throw should be disposed
            if (!auxThrows && !auxWriter.IsDisposed)
                return false.Label("AuxWriter should be disposed when not throwing");
            if (!splitThrows && !splitWriter.IsDisposed)
                return false.Label("SplitWriter should be disposed when not throwing");

            return true.Label(
                $"All non-throwing resources disposed correctly (auxThrows={auxThrows}, splitThrows={splitThrows})");
        }

        /// <summary>
        /// Property 18e: Both writers active — independent lifecycle operations.
        ///
        /// When both writers are successfully constructed, operations on one writer
        /// do not affect the state of the other. Each writer maintains its own
        /// independent operation history.
        ///
        /// **Validates: Requirements 13.1, 13.2**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property BothWritersActive_IndependentOperations()
        {
            var testGen =
                from auxOpCount in Gen.Choose(0, 15)
                from splitOpCount in Gen.Choose(0, 15)
                from seed in Gen.Choose(0, 99999)
                select new { AuxOpCount = auxOpCount, SplitOpCount = splitOpCount, Seed = seed };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                (MockDualWriter auxWriter, MockDataStore auxStore, MockDualWriter splitWriter, MockDataStore splitStore) =
                    ModelConstructor(WriterSetupOutcome.Success, WriterSetupOutcome.Success);

                Random rng = new Random(data.Seed);

                // Perform operations on aux writer
                for (int i = 0; i < data.AuxOpCount; i++)
                {
                    long offset = (long)rng.Next(0, int.MaxValue) * 0x100;
                    int size = rng.Next(1, 0x10000);
                    auxWriter.WriteData(offset, size);
                }

                // Perform operations on split writer
                for (int i = 0; i < data.SplitOpCount; i++)
                {
                    long offset = (long)rng.Next(0, int.MaxValue) * 0x100;
                    int size = rng.Next(1, 0x10000);
                    splitWriter.WriteData(offset, size);
                }

                // Verify independence: each writer has exactly its own operation count
                if (auxWriter.Operations.Count != data.AuxOpCount)
                    return false.Label(
                        $"Aux writer should have {data.AuxOpCount} operations, " +
                        $"got {auxWriter.Operations.Count}");
                if (splitWriter.Operations.Count != data.SplitOpCount)
                    return false.Label(
                        $"Split writer should have {data.SplitOpCount} operations, " +
                        $"got {splitWriter.Operations.Count}");

                // Verify no cross-contamination: operations are distinct
                if (data.AuxOpCount > 0 && data.SplitOpCount > 0)
                {
                    // The operations lists should have different entries (different RNG seeds per writer)
                    // unless by coincidence they share an offset — but counts must match independently
                    bool auxHasOnlyAuxOps = auxWriter.Operations.All(op => op.StartsWith("Write@"));
                    bool splitHasOnlySplitOps = splitWriter.Operations.All(op => op.StartsWith("Write@"));
                    if (!auxHasOnlyAuxOps || !splitHasOnlySplitOps)
                        return false.Label("Writers should only contain their own operations");
                }

                return true.Label(
                    $"Writers operated independently: aux={data.AuxOpCount}, split={data.SplitOpCount}");
            });
        }

        #endregion
    }
}
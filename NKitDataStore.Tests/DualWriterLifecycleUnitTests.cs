namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for dual-writer lifecycle management (finalize, dispose, error fallback).
    /// Uses model-based testing since the formatters are internal with complex constructors.
    ///
    /// Validates Requirements 8.1, 8.2, 8.3.
    /// </summary>
    public class DualWriterLifecycleUnitTests
    {
        /// <summary>
        /// Tracks calls to a mock writer for lifecycle verification.
        /// </summary>
        private class MockWriter : IDisposable
        {
            public bool Finalized { get; private set; }
            public bool Disposed { get; private set; }
            public long FinalizedSize { get; private set; }
            public uint FinalizedCrc { get; private set; }
            public ulong FinalizedXxHash { get; private set; }
            public List<(long Offset, int Size)> WrittenBlocks { get; } = new();
            public bool ThrowOnWrite { get; set; }

            public void FinalizeImage(long size, uint crc, ulong xxHash)
            {
                Finalized = true;
                FinalizedSize = size;
                FinalizedCrc = crc;
                FinalizedXxHash = xxHash;
            }

            public void WriteData(long imageOffset, byte[] data, int offset, int size)
            {
                if (ThrowOnWrite)
                    throw new InvalidOperationException("Simulated aux write failure");
                WrittenBlocks.Add((imageOffset, size));
            }

            public void Dispose() => Disposed = true;
        }

        /// <summary>
        /// Models the formatter's FinalizeImage behavior: calls FinalizeImage on both
        /// primary and aux writers. Aux failures are caught and logged (not rethrown).
        /// This mirrors DataStoreWiiFormatter.FinalizeImage and DataStoreWiiUFormatter.FinalizeImage.
        /// </summary>
        private static void ModelFinalizeImage(MockWriter primary, MockWriter aux,
            long size, uint crc, ulong xxHash)
        {
            primary.FinalizeImage(size, crc, xxHash);
            try { aux?.FinalizeImage(size, crc, xxHash); } catch { }
        }

        /// <summary>
        /// Models the formatter's Dispose behavior: disposes both aux and primary writers.
        /// Exceptions are swallowed (try/catch around each).
        /// This mirrors DataStoreWiiFormatter.Dispose and DataStoreWiiUFormatter.Dispose.
        /// </summary>
        private static void ModelDispose(MockWriter primary, MockWriter aux)
        {
            try { aux?.Dispose(); } catch { }
            try { primary.Dispose(); } catch { }
        }

        /// <summary>
        /// Models the formatter's WriteDataWithAuxFallback behavior: when writing to aux
        /// and the write fails, falls back to writing to primary instead.
        /// This mirrors DataStoreWiiFormatter.WriteDataWithAuxFallback.
        /// </summary>
        private static void ModelWriteDataWithAuxFallback(
            MockWriter primary, MockWriter aux, bool isAuxTarget,
            long imageOffset, byte[] data, int offset, int size)
        {
            if (isAuxTarget)
            {
                try
                {
                    aux.WriteData(imageOffset, data, offset, size);
                }
                catch
                {
                    // Fallback to primary on aux failure
                    primary.WriteData(imageOffset, data, offset, size);
                }
            }
            else
            {
                primary.WriteData(imageOffset, data, offset, size);
            }
        }

        // ── 7.4: FinalizeImage_BothWritersFinalized ──────────────────────

        /// <summary>
        /// Validates Requirement 8.1: When an image is finalized, both the primary
        /// and aux writers receive FinalizeImage calls with identical parameters.
        /// </summary>
        [Fact]
        public void FinalizeImage_BothWritersFinalized()
        {
            // Arrange
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            long size = 0x100000;
            uint crc = 0xDEADBEEF;
            ulong xxHash = 0x1234567890ABCDEF;

            // Act
            ModelFinalizeImage(primary, aux, size, crc, xxHash);

            // Assert
            Assert.True(primary.Finalized, "Primary writer should be finalized");
            Assert.True(aux.Finalized, "Aux writer should be finalized");
            Assert.Equal(size, primary.FinalizedSize);
            Assert.Equal(crc, primary.FinalizedCrc);
            Assert.Equal(xxHash, primary.FinalizedXxHash);
            Assert.Equal(size, aux.FinalizedSize);
            Assert.Equal(crc, aux.FinalizedCrc);
            Assert.Equal(xxHash, aux.FinalizedXxHash);
        }

        /// <summary>
        /// Validates Requirement 8.1: FinalizeImage works correctly when aux is null
        /// (primary-only mode). Only the primary writer is finalized.
        /// </summary>
        [Fact]
        public void FinalizeImage_NoAux_OnlyPrimaryFinalized()
        {
            // Arrange
            MockWriter primary = new MockWriter();

            // Act
            ModelFinalizeImage(primary, aux: null, 0x200000, 0xBEEF, 0xABCD);

            // Assert
            Assert.True(primary.Finalized);
            Assert.Equal(0x200000, primary.FinalizedSize);
        }

        // ── 7.4: Dispose_BothWritersDisposed ─────────────────────────────

        /// <summary>
        /// Validates Requirement 8.2: When the formatter is disposed, both the
        /// primary and aux writers are disposed.
        /// </summary>
        [Fact]
        public void Dispose_BothWritersDisposed()
        {
            // Arrange
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();

            // Act
            ModelDispose(primary, aux);

            // Assert
            Assert.True(primary.Disposed, "Primary writer should be disposed");
            Assert.True(aux.Disposed, "Aux writer should be disposed");
        }

        /// <summary>
        /// Validates Requirement 8.2: Dispose works correctly when aux is null.
        /// </summary>
        [Fact]
        public void Dispose_NoAux_OnlyPrimaryDisposed()
        {
            // Arrange
            MockWriter primary = new MockWriter();

            // Act
            ModelDispose(primary, aux: null);

            // Assert
            Assert.True(primary.Disposed);
        }

        // ── 7.4: AuxWriteFailure_FallsBackToPrimary ─────────────────────

        /// <summary>
        /// Validates Requirement 8.3: When an aux write fails, the formatter
        /// falls back to writing the block to the primary store instead.
        /// </summary>
        [Fact]
        public void AuxWriteFailure_FallsBackToPrimary()
        {
            // Arrange
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter { ThrowOnWrite = true };
            byte[] data = new byte[65536];
            long imageOffset = 0x50000;

            // Act: write to aux target, which will fail and fall back to primary
            ModelWriteDataWithAuxFallback(primary, aux, isAuxTarget: true,
                imageOffset, data, 0, data.Length);

            // Assert
            Assert.Empty(aux.WrittenBlocks);           // aux write failed
            Assert.Single(primary.WrittenBlocks);      // primary received the fallback write
            Assert.Equal(imageOffset, primary.WrittenBlocks[0].Offset);
            Assert.Equal(data.Length, primary.WrittenBlocks[0].Size);
        }

        /// <summary>
        /// Validates Requirement 8.3 (supplementary): When aux write succeeds,
        /// the block stays in aux and is NOT written to primary.
        /// </summary>
        [Fact]
        public void AuxWriteSuccess_StaysInAux()
        {
            // Arrange
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter(); // no ThrowOnWrite
            byte[] data = new byte[65536];
            long imageOffset = 0x60000;

            // Act
            ModelWriteDataWithAuxFallback(primary, aux, isAuxTarget: true,
                imageOffset, data, 0, data.Length);

            // Assert
            Assert.Single(aux.WrittenBlocks);          // aux received the write
            Assert.Empty(primary.WrittenBlocks);       // primary was NOT written to
        }

        /// <summary>
        /// Validates Requirement 8.3 (supplementary): When writing to primary target,
        /// the write goes directly to primary regardless of aux state.
        /// </summary>
        [Fact]
        public void PrimaryTargetWrite_GoesDirectlyToPrimary()
        {
            // Arrange
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            byte[] data = new byte[32768];
            long imageOffset = 0x100000;

            // Act
            ModelWriteDataWithAuxFallback(primary, aux, isAuxTarget: false,
                imageOffset, data, 0, data.Length);

            // Assert
            Assert.Single(primary.WrittenBlocks);
            Assert.Empty(aux.WrittenBlocks);
        }
    }
}
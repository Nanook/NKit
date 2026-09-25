using System.Diagnostics;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Encapsulates the rename-based state machine for transitioning between embedded
    /// and separate modes during writes/compaction. Each step is a rename or append —
    /// never an in-place truncate of the main file. Handles crash recovery by detecting
    /// which files exist on startup.
    /// </summary>
    internal class EmbeddedIndexCommitter
    {
        private readonly string _setBasePath;

        /// <summary>
        /// Creates a new EmbeddedIndexCommitter for the specified set base path.
        /// </summary>
        /// <param name="setBasePath">Full path to the embedded file (e.g. "path/to/test.nkds").</param>
        public EmbeddedIndexCommitter(string setBasePath)
        {
            _setBasePath = setBasePath ?? throw new ArgumentNullException(nameof(setBasePath));
        }

        /// <summary>
        /// The embedded file path (e.g. "path/to/test.nkds").
        /// </summary>
        public string EmbeddedPath => _setBasePath;

        /// <summary>
        /// Temporary path for the extracted index during the commit sequence (e.g. "path/to/test.nkds.tmp").
        /// </summary>
        public string IndexTmpPath => _setBasePath + ".tmp";

        /// <summary>
        /// The shard file path used during separate-mode phase (e.g. "path/to/test_0000.nkds").
        /// </summary>
        public string ShardPath => GetShardPath(_setBasePath);

        /// <summary>
        /// Temporary shard path used during the re-embed rename sequence (e.g. "path/to/test_0000.nkds.tmp").
        /// </summary>
        public string ShardTmpPath => GetShardPath(_setBasePath) + ".tmp";

        /// <summary>
        /// Minimum valid index size: two 256-byte headers (primary + secondary).
        /// </summary>
        private static readonly int MinIndexSize = FileHeader.HeaderSize * 2;

        /// <summary>
        /// Extracts from embedded mode to separate mode for writing.
        /// After this method completes, the set looks like a normal separate-mode set:
        /// test.nkds = standalone index, test_0000.nkds = plain shard (block data only).
        /// </summary>
        /// <param name="shardBoundary">The byte offset where block data ends and the index begins.</param>
        /// <returns>Tuple of (indexPath, shardPath) for the now-separate-mode set.</returns>
        public (string indexPath, string shardPath) ExtractToSeparateMode(long shardBoundary)
        {
            // Step 1: Write the binary index (bytes from shardBoundary to end-12) to test.nkds.tmp
            using (FileStream source = new FileStream(EmbeddedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long fileSize = source.Length;
                long indexSize = fileSize - EmbeddedFooter.FooterSize - shardBoundary;

                if (indexSize < MinIndexSize)
                    throw new InvalidDataException(
                        $"Embedded index too small ({indexSize} bytes). Minimum valid index is {MinIndexSize} bytes (two {FileHeader.HeaderSize}-byte headers).");

                using (FileStream dest = new FileStream(IndexTmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.Seek(shardBoundary, SeekOrigin.Begin);
                    CopyBytes(source, dest, indexSize);
                    dest.Flush(flushToDisk: true);
                }
            }

            return FinishExtraction(shardBoundary);
        }

        /// <summary>
        /// Extracts the embedded index to separate mode using a known index size.
        /// Use this overload when the embedded footer may be stale (e.g., after Remove
        /// operations that grew the index without updating the footer).
        /// </summary>
        /// <param name="shardBoundary">The byte offset where block data ends and the index begins.</param>
        /// <param name="knownIndexSize">The actual index size from the header's FileEndOffset.</param>
        /// <returns>Tuple of (indexPath, shardPath) for the now-separate-mode set.</returns>
        public (string indexPath, string shardPath) ExtractToSeparateMode(long shardBoundary, long knownIndexSize)
        {
            if (knownIndexSize < MinIndexSize)
                throw new InvalidDataException(
                    $"Embedded index too small ({knownIndexSize} bytes). Minimum valid index is {MinIndexSize} bytes (two {FileHeader.HeaderSize}-byte headers).");

            using (FileStream source = new FileStream(EmbeddedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                using (FileStream dest = new FileStream(IndexTmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.Seek(shardBoundary, SeekOrigin.Begin);
                    CopyBytes(source, dest, knownIndexSize);
                    dest.Flush(flushToDisk: true);
                }
            }

            return FinishExtraction(shardBoundary);
        }

        private (string indexPath, string shardPath) FinishExtraction(long shardBoundary)
        {
            // Step 2: Rename test.nkds → test_0000.nkds
            MoveWithRetry(EmbeddedPath, ShardPath);

            // Step 3: Rename test.nkds.tmp → test.nkds (now a standalone index)
            // If crash here, looks like normal separate-mode set
            File.Move(IndexTmpPath, EmbeddedPath);

            // Step 4: Truncate test_0000.nkds to shardBoundary (remove old index+footer)
            // Retry in case file handles from the previous owner haven't been fully released yet
            for (int attempt = 0; attempt <= 10; attempt++)
            {
                try
                {
                    using (FileStream shard = new FileStream(ShardPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    {
                        shard.SetLength(shardBoundary);
                        shard.Flush(flushToDisk: true);
                    }
                    break;
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(100);
                }
            }

            return (EmbeddedPath, ShardPath);
        }

        /// <summary>
        /// Re-embeds from separate mode back to embedded mode after writing.
        /// Appends the index content and EmbeddedFooter to the shard file, then renames
        /// to produce the final embedded file.
        /// </summary>
        /// <param name="indexPath">Path to the standalone index file.</param>
        /// <param name="shardPath">Path to the plain shard file (block data only).</param>
        public void ReEmbed(string indexPath, string shardPath)
        {
            // Step 1: Read index content
            byte[] indexContent;
            using (FileStream indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (indexStream.Length < MinIndexSize)
                    throw new InvalidDataException(
                        $"Index file too small ({indexStream.Length} bytes). Minimum valid index is {MinIndexSize} bytes (two {FileHeader.HeaderSize}-byte headers).");

                indexContent = new byte[indexStream.Length];
                int totalRead = 0;
                while (totalRead < indexContent.Length)
                {
                    int read = indexStream.Read(indexContent, totalRead, indexContent.Length - totalRead);
                    if (read == 0)
                        throw new IOException($"Unexpected end of index file at {totalRead} bytes (expected {indexContent.Length}).");
                    totalRead += read;
                }
            }

            // Step 2: Append index content + EmbeddedFooter directly to the shard file
            using (FileStream shardStream = new FileStream(shardPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                shardStream.Seek(0, SeekOrigin.End);
                shardStream.Write(indexContent, 0, indexContent.Length);

                EmbeddedFooter footer = new EmbeddedFooter(indexContent.Length);
                byte[] footerBytes = footer.Serialize();
                shardStream.Write(footerBytes, 0, footerBytes.Length);
                shardStream.Flush(flushToDisk: true);
            }

            // Step 3: Rename shard (now contains blocks + index + footer) → final embedded file
            // Delete the old index file first, then rename shard to the embedded path
            if (File.Exists(EmbeddedPath))
            {
                try { File.Delete(EmbeddedPath); }
                catch (IOException) { MoveWithRetry(EmbeddedPath, EmbeddedPath + ".old"); }
            }
            MoveWithRetry(shardPath, EmbeddedPath);

            // Clean up any leftover .old file
            try { File.Delete(EmbeddedPath + ".old"); } catch { }
        }

        /// <summary>
        /// Detects intermediate crash states and recovers.
        /// Returns the mode the set is in after recovery.
        /// </summary>
        public EmbeddedRecoveryResult TryRecover()
        {
            bool embeddedExists = File.Exists(EmbeddedPath);
            bool shardExists = File.Exists(ShardPath);
            bool shardTmpExists = File.Exists(ShardTmpPath);
            bool indexTmpExists = File.Exists(IndexTmpPath);

            // Case: test.nkds.tmp exists alongside test.nkds → extraction Step 1 completed
            // but Step 2 (rename embedded → shard) was not done. Delete the .tmp and
            // treat the original embedded file as authoritative. (Requirement 15.3)
            if (indexTmpExists && embeddedExists)
            {
                Trace.TraceWarning(
                    $"[EmbeddedIndexCommitter.TryRecover] Index .tmp '{IndexTmpPath}' exists alongside embedded file '{EmbeddedPath}'. " +
                    $"Extraction was interrupted before first rename. Deleting .tmp and treating original as authoritative.");
                File.Delete(IndexTmpPath);
                indexTmpExists = false;
                // Re-check state after cleanup
                embeddedExists = File.Exists(EmbeddedPath);
                shardExists = File.Exists(ShardPath);
                shardTmpExists = File.Exists(ShardTmpPath);
            }

            // Case: test.nkds.tmp exists + test_0000.nkds exists + test.nkds missing →
            // extraction Step 2 completed (embedded renamed to shard) but Step 3
            // (rename .tmp → index) was not done. Complete the rename to reach
            // separate mode. The oversized shard (still containing stale index+footer)
            // will be truncated downstream by TruncateOrphanedTailData. (Requirement 15.4)
            if (indexTmpExists && !embeddedExists && shardExists)
            {
                Trace.TraceWarning(
                    $"[EmbeddedIndexCommitter.TryRecover] Index .tmp '{IndexTmpPath}' and shard '{ShardPath}' exist " +
                    $"but embedded file '{EmbeddedPath}' is missing. Extraction Step 2 completed but Step 3 did not. " +
                    $"Completing rename of .tmp to index path to reach separate mode.");
                File.Move(IndexTmpPath, EmbeddedPath);
                return EmbeddedRecoveryResult.InSeparateMode;
            }

            // Case: test_0000.nkds.tmp exists
            // Separate-mode guard: a "test_0000.nkds.tmp" is ambiguous. In EMBEDDED mode it is a
            // re-embed artifact (handled below). In SEPARATE mode it is the deferred-rename temp
            // produced by shard compaction of shard 0, and must NOT be treated as an embedded
            // re-embed — doing so would append the index to it and rename it to the embedded file,
            // destroying the compacted shard AND the standalone index. We recognise separate mode
            // by: a standalone index file (test.nkds exists and is NOT an embedded file) AND a
            // real shard (test_0000.nkds) present. In that case leave the temp for the separate
            // mode shard-.tmp recovery in BinaryDataStoreDataAccess to promote/delete.
            bool separateModeShardTemp =
                shardTmpExists &&
                embeddedExists &&
                shardExists &&
                !EndsWithFooterMagic(EmbeddedPath);

            if (shardTmpExists && !separateModeShardTemp)
            {
                if (EndsWithFooterMagic(ShardTmpPath))
                {
                    // Re-embed was complete (footer appended), rename was interrupted
                    // Rename test_0000.nkds.tmp → test.nkds
                    if (embeddedExists)
                        File.Delete(EmbeddedPath);
                    File.Move(ShardTmpPath, EmbeddedPath);
                    return EmbeddedRecoveryResult.RecoveredToEmbedded;
                }
                else
                {
                    // Append was interrupted — re-append index from test.nkds
                    if (embeddedExists)
                    {
                        // Read index from test.nkds (the standalone index file)
                        byte[] indexContent;
                        using (FileStream indexStream = new FileStream(EmbeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            indexContent = new byte[indexStream.Length];
                            int totalRead = 0;
                            while (totalRead < indexContent.Length)
                            {
                                int read = indexStream.Read(indexContent, totalRead, indexContent.Length - totalRead);
                                if (read == 0)
                                    throw new IOException("Unexpected end of index file during recovery.");
                                totalRead += read;
                            }
                        }

                        using (FileStream shardStream = new FileStream(ShardTmpPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                        {
                            // The .tmp file without footer magic contains only block data
                            // (or block data with a partial/corrupt append). Append the full
                            // index + footer to make it a valid embedded file.
                            shardStream.Seek(0, SeekOrigin.End);
                            shardStream.Write(indexContent, 0, indexContent.Length);

                            EmbeddedFooter footer = new EmbeddedFooter(indexContent.Length);
                            byte[] footerBytes = footer.Serialize();
                            shardStream.Write(footerBytes, 0, footerBytes.Length);
                            shardStream.Flush(flushToDisk: true);
                        }

                        // Now rename .tmp → test.nkds and delete old index
                        File.Delete(EmbeddedPath);
                        File.Move(ShardTmpPath, EmbeddedPath);
                        return EmbeddedRecoveryResult.RecoveredToEmbedded;
                    }
                    else
                    {
                        // No index file to recover from — cannot complete recovery
                        // This is an edge case; treat as no file found
                        return EmbeddedRecoveryResult.NoFileFound;
                    }
                }
            }

            // Case: test.nkds + test_0000.nkds both exist → could be mid-write separate mode,
            // or a crash during ReEmbed (index/footer appended to shard but rename not completed)
            if (embeddedExists && shardExists)
            {
                if (EndsWithFooterMagic(ShardPath))
                {
                    // Scenario (Req 15.2): ReEmbed completed writing index+footer to shard,
                    // but the rename from shard → embedded path didn't happen.
                    // The shard is a valid embedded file. Complete the rename.
                    File.Delete(EmbeddedPath);
                    MoveWithRetry(ShardPath, EmbeddedPath);
                    return EmbeddedRecoveryResult.RecoveredToEmbedded;
                }

                // Check if the embedded path is a standalone index (no footer magic).
                // If so, we may be in a ReEmbed crash where the index was appended to the
                // shard but the footer was not yet written (Req 15.1).
                if (!EndsWithFooterMagic(EmbeddedPath))
                {
                    // EmbeddedPath is a standalone index file (separate mode index).
                    // Check if the shard has the full index appended but no footer:
                    // shard_size == shard_boundary + index_size, where shard_boundary is unknown.
                    // We can detect this by checking if the shard file size > 0 and the index
                    // file is valid. If the shard has extra bytes matching the index size,
                    // we re-append the footer to complete the re-embed.
                    long indexFileSize = new FileInfo(EmbeddedPath).Length;
                    long shardFileSize = new FileInfo(ShardPath).Length;

                    if (indexFileSize >= MinIndexSize && shardFileSize > indexFileSize)
                    {
                        // The shard is larger than the index — it likely has block data + appended index.
                        // Compute the presumed shard boundary: shardFileSize - indexFileSize
                        // Verify by checking if the bytes at that offset match the index file content.
                        long presumedShardBoundary = shardFileSize - indexFileSize;

                        if (VerifyIndexAppendedToShard(ShardPath, EmbeddedPath, presumedShardBoundary, indexFileSize))
                        {
                            // The full index was appended but footer is missing. Append the footer.
                            using (FileStream shardStream = new FileStream(ShardPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                            {
                                shardStream.Seek(0, SeekOrigin.End);
                                EmbeddedFooter footer = new EmbeddedFooter(indexFileSize);
                                byte[] footerBytes = footer.Serialize();
                                shardStream.Write(footerBytes, 0, footerBytes.Length);
                                shardStream.Flush(flushToDisk: true);
                            }

                            // Now the shard is a complete embedded file. Complete the rename.
                            File.Delete(EmbeddedPath);
                            MoveWithRetry(ShardPath, EmbeddedPath);
                            return EmbeddedRecoveryResult.RecoveredToEmbedded;
                        }
                    }

                    // Could not confirm index was fully appended. Treat as normal separate mode
                    // and let the caller's orphaned tail truncation handle any partial append.
                    return EmbeddedRecoveryResult.InSeparateMode;
                }

                // EmbeddedPath has footer magic (is itself an embedded file) but shard also exists.
                // This shouldn't normally happen. Treat as separate mode for safety.
                return EmbeddedRecoveryResult.InSeparateMode;
            }

            // Case: only test.nkds exists
            if (embeddedExists)
            {
                if (EndsWithFooterMagic(EmbeddedPath))
                {
                    return EmbeddedRecoveryResult.AlreadyEmbedded;
                }
                else
                {
                    // File exists but no footer magic — could be a standalone index (separate mode)
                    // or a corrupted file. Treat as separate mode (the caller will handle).
                    return EmbeddedRecoveryResult.InSeparateMode;
                }
            }

            // No relevant files found
            return EmbeddedRecoveryResult.NoFileFound;
        }

        /// <summary>
        /// Derives the shard path from the base path by inserting "_0000" before the extension.
        /// For example: "path/to/test.nkds" → "path/to/test_0000.nkds"
        /// </summary>
        internal static string GetShardPath(string basePath)
        {
            string directory = Path.GetDirectoryName(basePath) ?? string.Empty;
            string nameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
            string extension = Path.GetExtension(basePath);
            string shardFileName = $"{nameWithoutExt}_0000{extension}";
            return string.IsNullOrEmpty(directory)
                ? shardFileName
                : Path.Combine(directory, shardFileName);
        }

        /// <summary>
        /// Checks if the file ends with the EmbeddedFooter magic bytes.
        /// </summary>
        private static bool EndsWithFooterMagic(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < EmbeddedFooter.FooterSize)
                return false;

            Span<byte> magicBuffer = stackalloc byte[4];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-4, SeekOrigin.End);
            int read = stream.Read(magicBuffer);
            if (read < 4)
                return false;

            return EmbeddedFooter.IsMagicValid(magicBuffer);
        }

        /// <summary>
        /// Verifies that the index file content was fully appended to the shard at the given offset.
        /// Compares the first and last chunks of the appended region against the index file content
        /// to confirm the append was complete (not partial).
        /// </summary>
        private static bool VerifyIndexAppendedToShard(string shardPath, string indexPath, long appendOffset, long indexSize)
        {
            // Quick sanity check: the shard must be at least appendOffset + indexSize bytes
            FileInfo shardInfo = new FileInfo(shardPath);
            if (shardInfo.Length < appendOffset + indexSize)
                return false;

            // Compare the first N bytes and last N bytes of the appended region
            // against the index file to confirm the full index was written
            const int verifyChunkSize = 512;
            int firstChunk = (int)Math.Min(verifyChunkSize, indexSize);
            int lastChunk = (int)Math.Min(verifyChunkSize, indexSize);

            byte[] shardBuffer = new byte[Math.Max(firstChunk, lastChunk)];
            byte[] indexBuffer = new byte[Math.Max(firstChunk, lastChunk)];

            using FileStream shardStream = new FileStream(shardPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // Verify first chunk
            shardStream.Seek(appendOffset, SeekOrigin.Begin);
            int shardRead = shardStream.Read(shardBuffer, 0, firstChunk);
            indexStream.Seek(0, SeekOrigin.Begin);
            int indexRead = indexStream.Read(indexBuffer, 0, firstChunk);

            if (shardRead != firstChunk || indexRead != firstChunk)
                return false;
            if (!shardBuffer.AsSpan(0, firstChunk).SequenceEqual(indexBuffer.AsSpan(0, firstChunk)))
                return false;

            // Verify last chunk (if index is larger than one chunk)
            if (indexSize > verifyChunkSize)
            {
                long lastChunkOffset = indexSize - lastChunk;
                shardStream.Seek(appendOffset + lastChunkOffset, SeekOrigin.Begin);
                shardRead = shardStream.Read(shardBuffer, 0, lastChunk);
                indexStream.Seek(lastChunkOffset, SeekOrigin.Begin);
                indexRead = indexStream.Read(indexBuffer, 0, lastChunk);

                if (shardRead != lastChunk || indexRead != lastChunk)
                    return false;
                if (!shardBuffer.AsSpan(0, lastChunk).SequenceEqual(indexBuffer.AsSpan(0, lastChunk)))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Copies a specified number of bytes from source to destination stream.
        /// </summary>
        private static void CopyBytes(Stream source, Stream destination, long count)
        {
            byte[] buffer = new byte[81920]; // 80 KB copy buffer
            long remaining = count;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = source.Read(buffer, 0, toRead);
                if (read == 0)
                    throw new IOException($"Unexpected end of stream. Expected {remaining} more bytes.");
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        /// <summary>
        /// Moves (renames) a file with retry logic to handle transient file locking on Windows.
        /// After a FileStream is disposed, the OS may not immediately release the handle.
        /// This retries the move a few times with short delays to accommodate that.
        /// </summary>
        private static void MoveWithRetry(string source, string destination, int maxRetries = 5, int delayMs = 50)
        {
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // If destination exists and is locked, rename it out of the way first
                    if (File.Exists(destination))
                    {
                        string tombstone = destination + ".old";
                        try { File.Delete(tombstone); } catch { }
                        try { File.Move(destination, tombstone); } catch { }
                        try { File.Delete(tombstone); } catch { }
                    }
                    File.Move(source, destination);
                    return;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    Thread.Sleep(delayMs);
                }
            }
        }
    }

    /// <summary>
    /// Result of the embedded index recovery process.
    /// </summary>
    internal enum EmbeddedRecoveryResult
    {
        /// <summary>Normal embedded file, no recovery needed.</summary>
        AlreadyEmbedded,

        /// <summary>Recovered and re-embedded successfully.</summary>
        RecoveredToEmbedded,

        /// <summary>Left in separate mode (mid-write), can continue normally.</summary>
        InSeparateMode,

        /// <summary>No set files found.</summary>
        NoFileFound
    }
}
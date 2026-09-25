using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Steps.Shared
{
    public struct FolderFileEntry
    {
        public string RelativePath;
        public long OffsetStart;
        public long Size;
        public ulong XxHash64;
        public uint Crc32;
    }

    public class DataStoreFolderFormatter : IDisposable
    {
        private readonly IImageWriter _imageWriter;
        private readonly IDataStore _dataStore;
        private readonly List<FolderFileEntry> _fileEntries;
        private long _currentOffset;

        public string ImageFileName { get; }

        /// <summary>
        /// Exposes the tracked file entries so callers (e.g., TmdAppFolderBuilder) can build
        /// custom filesystem.yaml with both fs and ifs sections.
        /// </summary>
        public IReadOnlyList<FolderFileEntry> FileEntries => _fileEntries;

        /// <summary>
        /// The cumulative offset after all stored files.
        /// </summary>
        public long CurrentOffset => _currentOffset;

        public DataStoreFolderFormatter(string dedupePath, string imageName, long shardSize,
            int blockSize, string setName, ImageFormat format = ImageFormat.Folder, string system = "Directories")
        {
            if (string.IsNullOrEmpty(dedupePath))
                throw new ArgumentNullException(nameof(dedupePath));
            if (string.IsNullOrEmpty(imageName))
                throw new ArgumentNullException(nameof(imageName));
            if (string.IsNullOrEmpty(setName))
                throw new ArgumentNullException(nameof(setName));

            if (!Directory.Exists(dedupePath))
                Directory.CreateDirectory(dedupePath);

            _dataStore = new DataStore(dedupePath);
            if (_dataStore.GetSetInfo(setName) == null)
                _dataStore.CreateSet(setName, shardSize, blockSize);

            ImageFileName = imageName;
            _fileEntries = new List<FolderFileEntry>();
            _currentOffset = 0;

            _imageWriter = _dataStore.AddImage(setName, imageName, system, format);
        }

        public void StoreFile(string relativePath, Stream fileData, long fileSize)
        {
            long offsetStart = _currentOffset;

            if (fileSize == 0)
            {
                // Zero-byte files: no blocks to write, just track the entry.
                // Writing a zero-byte stream would create an offset record at the current offset,
                // and a second zero-byte file would collide on the same offset (UNIQUE constraint).
                _fileEntries.Add(new FolderFileEntry
                {
                    RelativePath = relativePath,
                    OffsetStart = offsetStart,
                    Size = 0,
                    XxHash64 = 0,
                    Crc32 = 0
                });
                return;
            }

            using (Crc crc = new Crc())
            using (XXHash64 xxHash = XXHash64.Create())
            using (Stream writeStream = _imageWriter.BeginWriteStream(_currentOffset, BlockType.File))
            {
                byte[] buffer = new byte[0x10000]; // 64KB buffer
                int bytesRead;
                long totalRead = 0;
                while ((bytesRead = fileData.Read(buffer, 0, buffer.Length)) > 0)
                {
                    writeStream.Write(buffer, 0, bytesRead);
                    crc.Sum(buffer, 0, bytesRead);
                    xxHash.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalRead += bytesRead;
                }
                xxHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                // Guard against truncated/stalled source streams (e.g. a network-mounted
                // drive that drops the connection mid-read). A Read() returning 0 signals
                // EOF, but if we have not received the expected number of bytes the source
                // ended early. Storing the short data anyway would record the FULL expected
                // Size alongside a Crc32/XxHash64 computed over only the bytes we actually
                // received — an image that looks correct by size/CRC yet fails verification.
                // Throwing here lets the caller (NkdsOperations) record a per-image error and
                // roll the un-finalized image back cleanly instead of persisting corruption.
                if (totalRead != fileSize)
                    throw new IOException(
                        $"Truncated read for '{relativePath}': expected {fileSize} bytes but the source " +
                        $"stream ended after {totalRead} bytes. The source may be an unreliable or " +
                        $"disconnected stream (e.g. a network drive). Image not stored.");

                _fileEntries.Add(new FolderFileEntry
                {
                    RelativePath = relativePath,
                    OffsetStart = offsetStart,
                    Size = fileSize,
                    XxHash64 = xxHash.HashUInt64,
                    Crc32 = crc.Value
                });
            }

            _currentOffset += fileSize;
        }

        public void BuildFileSystemYaml()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            foreach (FolderFileEntry entry in _fileEntries)
            {
                root.AddFileByPath(entry.RelativePath, entry.OffsetStart, entry.Size, entry.XxHash64, entry.Crc32);
            }

            byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(fsYaml).ToBytes();
            _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
        }

        /// <summary>
        /// Writes a pre-built FsYaml as the filesystem.yaml for this image.
        /// Used by TmdAppFolderBuilder to write a custom yaml with both fs and ifs sections.
        /// </summary>
        public void WriteFileSystemYaml(FsYaml fsYaml)
        {
            byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(fsYaml).ToBytes();
            _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
        }

        public void FinalizeImage(long totalSize, uint crc, ulong xxHash) => _imageWriter?.FinalizeImage(totalSize, crc, xxHash);

        public bool AlreadyExists => _imageWriter?.AlreadyExists ?? false;

        public void Dispose()
        {
            (_imageWriter as IDisposable)?.Dispose();
            _dataStore?.Dispose();
        }
    }
}
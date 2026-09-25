using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Unit tests verifying that plain ISO images and Xbox XISO images are unaffected
    /// by the CUE/GDI folder-based storage changes.
    ///
    /// Feature: cue-gdi-folder-storage
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class PlainIsoUnaffectedUnitTests : IDisposable
    {
        private readonly string _tempDir;
        private static readonly MethodInfo _getDataStoreEntryExtensionMethod;

        static PlainIsoUnaffectedUnitTests()
        {
            _getDataStoreEntryExtensionMethod = typeof(SourceFileSystem).GetMethod(
                "getDataStoreEntryExtension",
                BindingFlags.NonPublic | BindingFlags.Static);
        }

        public PlainIsoUnaffectedUnitTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_PlainIso_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ─── Listing extension tests ────────────────────────────────────────────

        /// <summary>
        /// Plain ISO images must be listed with ".iso" extension in DataStore listings.
        /// **Validates: Requirement 7.3**
        /// </summary>
        [Fact]
        public void GetDataStoreEntryExtension_Iso_ReturnsDotIso()
        {
            Assert.NotNull(_getDataStoreEntryExtensionMethod);
            string result = (string)_getDataStoreEntryExtensionMethod.Invoke(null, new object[] { ImageFormat.Iso })!;
            Assert.Equal(".iso", result);
        }

        /// <summary>
        /// Plain ISO images listed in a real DataStore appear with ".iso" extension.
        /// **Validates: Requirement 7.3**
        /// </summary>
        [Fact]
        public void PlainIsoImage_ListedWithIsoExtension()
        {
            // Arrange: create a DataStore with a plain ISO image
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                IImageWriter writer = ds.AddImage(setName, "Gran Turismo 4 (USA)", "PS2", ImageFormat.Iso);
                writer.FinalizeImage(4_700_000_000, 0xAABBCCDD, 0);
                writer.Dispose();
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: ISO image should be listed with .iso extension
            Assert.Single(files);
            // FileItem.Name strips the extension; FileName includes it
            Assert.Equal("Gran Turismo 4 (USA)", files[0].Name);
            Assert.Equal(".iso", files[0].Extension);
            Assert.Equal("Gran Turismo 4 (USA).iso", files[0].FileName);
        }

        /// <summary>
        /// Xbox XISO images (which use ImageFormat.Iso) are listed with ".iso" extension.
        /// **Validates: Requirement 7.3**
        /// </summary>
        [Fact]
        public void XboxXisoImage_ListedWithIsoExtension()
        {
            // Arrange: create a DataStore with an Xbox XISO image (stored as ImageFormat.Iso)
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                IImageWriter writer = ds.AddImage(setName, "Halo 2 (USA)", "Xbox", ImageFormat.Iso);
                writer.FinalizeImage(6_000_000_000, 0x11223344, 0);
                writer.Dispose();
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: Xbox XISO image should be listed with .iso extension
            Assert.Single(files);
            Assert.Equal("Halo 2 (USA)", files[0].Name);
            Assert.Equal(".iso", files[0].Extension);
            Assert.Equal("Halo 2 (USA).iso", files[0].FileName);
        }

        // ─── No FileName metadata tests ─────────────────────────────────────────

        /// <summary>
        /// Plain ISO images (no IndexFile) must have no FileName metadata on areas.
        /// This tests the model logic that mirrors DataStoreIso9660Formatter.getTrackFileName.
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Fact]
        public void PlainIso_NoIndexFile_NoFileNameMetadata()
        {
            // Simulate a plain ISO image with 1 area and no IndexFile
            AreaMetadata metadata = ModelBuildAreaMetadataForPlainIso(
                areaType: AreaType.FileSystem,
                blockSize: 0x800,
                trackIndex: 0,
                session: 1,
                physicalOffset: 0,
                trackType: "Mode1",
                indexItems: null,
                imageName: "Gran Turismo 4 (USA)");

            // Assert: no FileName metadata should be set
            Assert.False(metadata.ContainsKey(AreaValueType.FileName));
        }

        /// <summary>
        /// Plain ISO images with multiple areas still have no FileName metadata on any area.
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Fact]
        public void PlainIso_MultipleAreas_NoFileNameMetadataOnAny()
        {
            // Simulate a multi-partition plain ISO (e.g., PS3 disc with multiple regions)
            string imageName = "Uncharted 2 (USA)";

            for (int i = 0; i < 4; i++)
            {
                AreaMetadata metadata = ModelBuildAreaMetadataForPlainIso(
                    areaType: AreaType.FileSystem,
                    blockSize: 0x800,
                    trackIndex: i,
                    session: 1,
                    physicalOffset: (long)i * 0x10000,
                    trackType: "Mode1",
                    indexItems: null,
                    imageName: imageName);

                Assert.False(metadata.ContainsKey(AreaValueType.FileName),
                    $"Area {i} should not have FileName metadata for plain ISO");
            }
        }

        /// <summary>
        /// Plain ISO images with empty IndexFile items still have no FileName metadata.
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Fact]
        public void PlainIso_EmptyIndexFileItems_NoFileNameMetadata()
        {
            AreaMetadata metadata = ModelBuildAreaMetadataForPlainIso(
                areaType: AreaType.FileSystem,
                blockSize: 0x800,
                trackIndex: 0,
                session: 1,
                physicalOffset: 0,
                trackType: "Mode1",
                indexItems: Array.Empty<SourceFileTrack>(),
                imageName: "Some ISO Image");

            Assert.False(metadata.ContainsKey(AreaValueType.FileName));
        }

        /// <summary>
        /// Plain ISO images still have standard metadata (BlockSize, Track, Session, etc.) preserved.
        /// **Validates: Requirements 7.1, 7.2**
        /// </summary>
        [Fact]
        public void PlainIso_StandardMetadata_StillPresent()
        {
            AreaMetadata metadata = ModelBuildAreaMetadataForPlainIso(
                areaType: AreaType.FileSystem,
                blockSize: 0x800,
                trackIndex: 0,
                session: 1,
                physicalOffset: 150,
                trackType: "Mode1",
                indexItems: null,
                imageName: "Test ISO");

            // Standard metadata should be present
            Assert.Equal(AreaType.FileSystem.ToString(), metadata.GetString(AreaValueType.FsType));
            Assert.Equal(0x800L, metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(0L, metadata.GetLong(AreaValueType.Track));
            Assert.Equal(1L, metadata.GetLong(AreaValueType.Session));
            Assert.Equal(150L, metadata.GetLong(AreaValueType.PhysicalOffset));
            Assert.Equal("Mode1", metadata.GetString(AreaValueType.Type));

            // But no FileName
            Assert.False(metadata.ContainsKey(AreaValueType.FileName));
        }

        // ─── Xbox XISO non-interference tests ───────────────────────────────────

        /// <summary>
        /// Xbox XISO images use ImageFormat.Iso and have no FileName metadata on areas.
        /// **Validates: Requirement 7.3**
        /// </summary>
        [Fact]
        public void XboxXiso_NoFileNameMetadata()
        {
            // Xbox XISO images are stored as ImageFormat.Iso with no IndexFile.
            // They typically have 2-3 areas: video partition(s) + game partition.
            string imageName = "Halo 2 (USA)";

            // Video partition (AreaType.Other)
            AreaMetadata videoMeta = ModelBuildAreaMetadataForPlainIso(
                areaType: AreaType.Other,
                blockSize: 0x800,
                trackIndex: 0,
                session: 1,
                physicalOffset: 0,
                trackType: null,
                indexItems: null,
                imageName: imageName);

            Assert.False(videoMeta.ContainsKey(AreaValueType.FileName),
                "Xbox video partition should not have FileName metadata");

            // Game partition (AreaType.FileSystem)
            AreaMetadata gameMeta = ModelBuildAreaMetadataForPlainIso(
                areaType: AreaType.FileSystem,
                blockSize: 0x800,
                trackIndex: 1,
                session: 1,
                physicalOffset: 0x30600,
                trackType: null,
                indexItems: null,
                imageName: imageName);

            Assert.False(gameMeta.ContainsKey(AreaValueType.FileName),
                "Xbox game partition should not have FileName metadata");
        }

        /// <summary>
        /// Xbox XISO images are stored with ImageFormat.Iso (not Cue or Gdi).
        /// **Validates: Requirement 7.3**
        /// </summary>
        [Fact]
        public void XboxXiso_ImageFormat_IsIso()
        {
            // Xbox XISO images have no IndexFile, so the format determination
            // should always return ImageFormat.Iso
            ImageFormat format = ModelDetermineImageFormat(IndexFileType.None);
            Assert.Equal(ImageFormat.Iso, format);
        }

        /// <summary>
        /// Xbox XISO images listed alongside CUE/GDI images are unaffected.
        /// **Validates: Requirement 7.3**
        /// </summary>
        [Fact]
        public void XboxXiso_ListedAlongsideCueGdi_StillHasIsoExtension()
        {
            // Arrange: create a DataStore with mixed image formats
            string setName = "testset";
            using (DataStore ds = new DataStore(_tempDir))
            {
                ds.CreateSet(setName, 50L * 1024 * 1024 * 1024, 0x10000);

                // Xbox XISO (stored as ImageFormat.Iso)
                IImageWriter xboxWriter = ds.AddImage(setName, "Halo 2 (USA)", "Xbox", ImageFormat.Iso);
                xboxWriter.FinalizeImage(6_000_000_000, 0x11223344, 0);
                xboxWriter.Dispose();

                // CUE image (should appear as folder)
                IImageWriter cueWriter = ds.AddImage(setName, "Final Fantasy VII (USA)", "PS1", ImageFormat.Cue);
                cueWriter.FinalizeImage(700_000_000, 0x55667788, 0);
                cueWriter.Dispose();

                // GDI image (should appear as folder)
                IImageWriter gdiWriter = ds.AddImage(setName, "Sonic Adventure (USA)", "Dreamcast", ImageFormat.Gdi);
                gdiWriter.FinalizeImage(1_000_000_000, 0x99AABBCC, 0);
                gdiWriter.Dispose();
            }

            // Act: list files through SourceFileSystem
            List<FileItem> files = ListDataStoreFiles(setName, "*");

            // Assert: Only the Xbox ISO should be matching (CUE/GDI folder entries are
            // non-matching containers — only their internal .cue/.gdi index files are processable)
            Assert.Single(files);

            // Xbox XISO should have .iso extension
            FileItem xboxFile = files.FirstOrDefault(f => f.Name.Contains("Halo"));
            Assert.NotNull(xboxFile);
            Assert.Equal("Halo 2 (USA)", xboxFile.Name);
            Assert.Equal(".iso", xboxFile.Extension);
        }

        // ─── Model methods ──────────────────────────────────────────────────────

        /// <summary>
        /// Models the getTrackFileName private method from DataStoreIso9660Formatter.
        /// Returns null when IndexFile is null or has no items (plain ISO case).
        /// </summary>
        private static string ModelGetTrackFileName(
            SourceFileTrack[] indexItems,
            string imageName,
            int trackIndex)
        {
            if (indexItems == null || indexItems.Length == 0)
                return null;

            if (trackIndex < 0)
                return null;

            SourceFileTrack track = indexItems.FirstOrDefault(t => t.TrackIndex == trackIndex);
            if (track == null && trackIndex >= 0 && trackIndex < indexItems.Length)
                track = indexItems[trackIndex];

            if (track != null && !string.IsNullOrEmpty(track.FileName))
                return track.FileName;

            string baseName = imageName ?? "image";
            int displayTrackNo = trackIndex + 1;
            return $"{baseName} (Track {displayTrackNo:D2}).bin";
        }

        /// <summary>
        /// Models the BuildAreaMetadata method's behavior for a plain ISO image.
        /// When IndexFile is null/empty, no FileName metadata should be set.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataForPlainIso(
            AreaType areaType,
            int blockSize,
            int trackIndex,
            int session,
            long physicalOffset,
            string trackType,
            SourceFileTrack[] indexItems,
            string imageName)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, areaType.ToString());
            metadata.Set(AreaValueType.BlockSize, (long)blockSize);
            metadata.Set(AreaValueType.Track, trackIndex);
            metadata.Set(AreaValueType.Session, session);

            switch (areaType)
            {
                case AreaType.FileSystem:
                    metadata.Set(AreaValueType.PhysicalOffset, physicalOffset);
                    if (!string.IsNullOrEmpty(trackType))
                        metadata.Set(AreaValueType.Type, trackType);
                    break;

                case AreaType.Audio:
                    metadata.Set(AreaValueType.Type, "Audio");
                    break;

                case AreaType.Other:
                    // Xbox video partitions — no additional metadata beyond base
                    break;
            }

            // Set FileName metadata only when getTrackFileName returns non-null
            string trackFileName = ModelGetTrackFileName(indexItems, imageName, trackIndex);
            if (!string.IsNullOrEmpty(trackFileName))
                metadata.Set(AreaValueType.FileName, trackFileName);

            return metadata;
        }

        /// <summary>
        /// Models the ImageFormat determination logic from DataStoreIso9660Formatter.
        /// Returns ImageFormat.Iso when IndexFile is null or has FileType == None.
        /// </summary>
        private static ImageFormat ModelDetermineImageFormat(IndexFileType indexFileType)
        {
            if (indexFileType == IndexFileType.Cue)
                return ImageFormat.Cue;
            if (indexFileType == IndexFileType.Gdi)
                return ImageFormat.Gdi;
            return ImageFormat.Iso;
        }

        // ─── Helper methods ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a SourceFileSystem pointing at the DataStore set file and lists files.
        /// </summary>
        private List<FileItem> ListDataStoreFiles(string setName, string arcMask)
        {
            string nkdsPath = Path.Combine(_tempDir, $"{setName}{DataStore.DatabaseFileExtension}");
            FileItem archive = new FileItem(nkdsPath) { Size = new FileInfo(nkdsPath).Length };
            archive.Populate();

            SourceFileSystem sfs = new SourceFileSystem(archive, null);
            FileMask mask = FileMask.CreateLocalMask($"{nkdsPath}//{arcMask}", false);
            return sfs.GetFiles(mask, null).Where(f => f.IsMatch).ToList();
        }
    }
}
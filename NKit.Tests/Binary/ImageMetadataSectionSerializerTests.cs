using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKit.Tests.Binary.Generators;
using NKitDataStore;
using NKitDataStore.Binary.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for Image_Metadata_Section serialization round-trip.
    ///
    /// Feature: binary-index-format
    /// Property 2: Image_Metadata_Section serialization round-trip
    /// **Validates: Requirements 3.2, 15.1, 16.2, 16.6**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class ImageMetadataSectionSerializerTests
    {
        /// <summary>
        /// Generates a tuple of (AreaRecord[], FileRecord[]) with bounded counts.
        /// </summary>
        private static Arbitrary<(AreaRecord[] Areas, FileRecord[] Files)> AreasAndFiles()
        {
            Gen<(AreaRecord[] Areas, FileRecord[] Files)> gen = Gen.Choose(0, 20).SelectMany(areaCount =>
                Gen.Choose(0, 20).SelectMany(fileCount =>
                    Gen.ArrayOf(BinaryIndexGenerators.ValidAreaRecord(), areaCount).SelectMany(areas =>
                        Gen.ArrayOf(BinaryIndexGenerators.ValidFileRecord(), fileCount).Select(files =>
                            (Areas: areas, Files: files)
                        ))));

            return Arb.From(gen);
        }

        /// <summary>
        /// **Validates: Requirements 3.2, 15.1, 16.2, 16.6**
        ///
        /// Property 2: Image_Metadata_Section serialization round-trip.
        /// For any valid collection of AreaRecord objects (with arbitrary Offset, Size,
        /// stride values, SectionSize, Crc32, XxHash64, and Metadata key-value pairs)
        /// and FileRecord objects (with arbitrary Name up to 512 UTF-8 characters, FileId,
        /// Offset, Size, UncompressedSize, and IsSystem flag), serializing to an
        /// Image_Metadata_Section and deserializing back SHALL produce collections with
        /// identical values for all fields, where AreaMetadata equality means identical
        /// key-value pairs and FileRecord Name preserves UTF-8 encoding byte-for-byte.
        /// </summary>
        [Property(MaxTest = 100, Arbitrary = new[] { typeof(ImageMetadataSectionSerializerTests) })]
        public bool ImageMetadataSectionRoundTrip(
            (AreaRecord[] Areas, FileRecord[] Files) input)
        {
            (AreaRecord[] areas, FileRecord[] files) = input;

            // Serialize
            (byte[] serialized, int _) = ImageMetadataSectionSerializer.Serialize(areas, files);

            // Deserialize
            (List<AreaRecord> deserializedAreas, List<FileRecord> deserializedFiles) = ImageMetadataSectionSerializer.Deserialize(serialized);

            // Verify area count matches
            if (deserializedAreas.Count != areas.Length)
                return false;

            // Verify file count matches
            if (deserializedFiles.Count != files.Length)
                return false;

            // Verify each AreaRecord field
            for (int i = 0; i < areas.Length; i++)
            {
                if (!AreaRecordsEqual(areas[i], deserializedAreas[i]))
                    return false;
            }

            // Verify each FileRecord field
            for (int i = 0; i < files.Length; i++)
            {
                if (!FileRecordsEqual(files[i], deserializedFiles[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies that an empty collection of areas and files round-trips correctly.
        /// </summary>
        [Fact]
        public void EmptyCollectionsRoundTrip()
        {
            AreaRecord[] areas = Array.Empty<AreaRecord>();
            FileRecord[] files = Array.Empty<FileRecord>();

            (byte[] serialized, int _) = ImageMetadataSectionSerializer.Serialize(areas, files);
            (List<AreaRecord> deserializedAreas, List<FileRecord> deserializedFiles) = ImageMetadataSectionSerializer.Deserialize(serialized);

            Assert.Empty(deserializedAreas);
            Assert.Empty(deserializedFiles);
        }

        private static bool AreaRecordsEqual(AreaRecord expected, AreaRecord actual)
        {
            return expected.Offset == actual.Offset
                && expected.Size == actual.Size
                && expected.StrideBlockSize == actual.StrideBlockSize
                && expected.StrideDataOffset == actual.StrideDataOffset
                && expected.StrideDataLength == actual.StrideDataLength
                && expected.SectionSize == actual.SectionSize
                && expected.Crc32 == actual.Crc32
                && expected.XxHash64 == actual.XxHash64
                && AreaMetadataEqual(expected.Metadata, actual.Metadata);
        }

        private static bool AreaMetadataEqual(AreaMetadata expected, AreaMetadata actual)
        {
            if (expected == null && actual == null) return true;
            if (expected == null || actual == null) return false;

            // Compare by converting to blob (deterministic serialization)
            byte[] expectedBlob = expected.ToBlob();
            byte[] actualBlob = actual.ToBlob();
            return expectedBlob.SequenceEqual(actualBlob);
        }

        private static bool FileRecordsEqual(FileRecord expected, FileRecord actual)
        {
            return expected.Name == actual.Name
                && expected.FileId == actual.FileId
                && expected.Offset == actual.Offset
                && expected.Size == actual.Size
                && expected.UncompressedSize == actual.UncompressedSize
                && expected.IsSystem == actual.IsSystem;
        }
    }
}
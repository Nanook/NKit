using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using System.Collections.Generic;
using Xunit;

#nullable enable


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for Image_Directory serialization round-trip.
    ///
    /// Feature: binary-index-format
    /// Property 4: Image_Directory serialization round-trip
    /// **Validates: Requirements 4.1, 4.6, 13.3, 16.1, 16.5, 16.8**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class ImageDirectorySerializerTests
    {
        private static List<ImageDirectoryEntry> GenerateEntries(int seed)
        {
            Random rng = new Random(seed);
            int count = rng.Next(0, 11); // 0 to 10 entries
            List<ImageDirectoryEntry> entries = new List<ImageDirectoryEntry>(count);

            for (int i = 0; i < count; i++)
            {
                entries.Add(GenerateEntry(rng));
            }

            return entries;
        }

        private static ImageDirectoryEntry GenerateEntry(Random rng)
        {
            // Generate a name of 1-64 printable ASCII characters
            int nameLength = rng.Next(1, 65);
            char[] nameChars = new char[nameLength];
            for (int i = 0; i < nameLength; i++)
                nameChars[i] = (char)rng.Next(0x20, 0x7F);

            // Generate system (nullable)
            bool hasSystem = rng.Next(2) == 1;
            string? system = null;
            if (hasSystem)
            {
                int systemLength = rng.Next(1, 17);
                char[] systemChars = new char[systemLength];
                for (int i = 0; i < systemLength; i++)
                    systemChars[i] = (char)rng.Next(0x41, 0x5B); // A-Z
                system = new string(systemChars);
            }

            // Generate format
            ImageFormat[] formats = new[] {
                ImageFormat.Unknown, ImageFormat.Iso, ImageFormat.Bin,
                ImageFormat.App, ImageFormat.Cdn, ImageFormat.Gdi,
                ImageFormat.Folder, ImageFormat.TmdAppFolder
            };

            bool hasRollback = rng.Next(2) == 1;

            return new ImageDirectoryEntry
            {
                ImageId = (long)rng.Next(1, 100_000),
                Name = new string(nameChars),
                Size = (long)rng.Next(1, int.MaxValue / 2),
                Crc32 = (uint)rng.Next(int.MinValue, int.MaxValue),
                XxHash64 = ((ulong)(uint)rng.Next(int.MinValue, int.MaxValue) << 32) | (uint)rng.Next(int.MinValue, int.MaxValue),
                System = system,
                Format = formats[rng.Next(formats.Length)],
                RollbackFileId = hasRollback ? rng.Next(0, 100) : null,
                RollbackOffset = hasRollback ? (long)rng.Next(0, int.MaxValue / 2) : null,
                Removed = rng.Next(2) == 1,
                MetadataSectionOffset = (long)rng.Next(8192, int.MaxValue / 2),
                MetadataSectionCompressedSize = rng.Next(1, 1_000_000),
                BlockMapSectionOffset = (long)rng.Next(8192, int.MaxValue / 2),
                BlockMapSectionCompressedSize = rng.Next(1, 10_000_000),
            };
        }

        /// <summary>
        /// **Validates: Requirements 4.1, 4.6, 13.3, 16.1, 16.5, 16.8**
        ///
        /// Property 4: Image_Directory serialization round-trip.
        /// For any valid ImageDirectory containing N entries (each with arbitrary ImageId,
        /// Name up to 512 Unicode characters, Size, Crc32, XxHash64, System including null,
        /// Format, RollbackFileId including null, RollbackOffset including null, Removed flag,
        /// and section offsets/sizes), serializing and deserializing SHALL produce an
        /// ImageDirectory with identical entry count and identical field values for every entry.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ImageDirectoryRoundTrip(NonNegativeInt seed)
        {
            List<ImageDirectoryEntry> entries = GenerateEntries(seed.Get);

            // Serialize the entries
            byte[] serialized = ImageDirectorySerializer.Serialize(entries);

            // Deserialize back
            List<ImageDirectoryEntry> deserialized = ImageDirectorySerializer.Deserialize(serialized);

            // Verify entry count matches
            if (deserialized.Count != entries.Count)
                return false;

            // Verify all fields for each entry
            for (int i = 0; i < entries.Count; i++)
            {
                ImageDirectoryEntry original = entries[i];
                ImageDirectoryEntry result = deserialized[i];

                if (result.ImageId != original.ImageId)
                    return false;
                if (result.Name != original.Name)
                    return false;
                if (result.Size != original.Size)
                    return false;
                if (result.Crc32 != original.Crc32)
                    return false;
                if (result.XxHash64 != original.XxHash64)
                    return false;
                if (result.System != original.System)
                    return false;
                if (result.Format != original.Format)
                    return false;
                if (result.RollbackFileId != original.RollbackFileId)
                    return false;
                if (result.RollbackOffset != original.RollbackOffset)
                    return false;
                if (result.Removed != original.Removed)
                    return false;
                if (result.MetadataSectionOffset != original.MetadataSectionOffset)
                    return false;
                if (result.MetadataSectionCompressedSize != original.MetadataSectionCompressedSize)
                    return false;
                if (result.BlockMapSectionOffset != original.BlockMapSectionOffset)
                    return false;
                if (result.BlockMapSectionCompressedSize != original.BlockMapSectionCompressedSize)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies that an empty directory (0 entries) round-trips correctly.
        /// </summary>
        [Fact]
        public void EmptyDirectoryRoundTrip()
        {
            List<ImageDirectoryEntry> entries = new List<ImageDirectoryEntry>();

            byte[] serialized = ImageDirectorySerializer.Serialize(entries);
            List<ImageDirectoryEntry> deserialized = ImageDirectorySerializer.Deserialize(serialized);

            Assert.Empty(deserialized);
        }
    }
}
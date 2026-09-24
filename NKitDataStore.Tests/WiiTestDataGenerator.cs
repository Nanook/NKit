using Nanook.NKit.Nintendo.WiiGc;
using System.Security.Cryptography;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Helper class to generate test data with real Wii hashes and encryption.
    /// Uses the actual WiiHashGenerator to create valid test data.
    /// </summary>
    internal class WiiTestDataGenerator
    {
        // Test title key (16 bytes) - deterministic for testing
        public static byte[] TestTitleKey { get; } = new byte[]
        {
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
            0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10
        };

        // Test H3 table (empty for now - will be populated by hash generator)
        private byte[] _h3Table;

        public WiiTestDataGenerator()
        {
            // Allocate H3 table (can hold hashes for multiple 2MiB groups)
            // Each entry is 20 bytes, need space for at least 1 group for tests
            _h3Table = new byte[1024]; // Enough for 51 groups (1024/20)

            // Initialize to zeros - will be populated by WiiHashGenerator
            Array.Clear(_h3Table, 0, _h3Table.Length);
        }


        /// <summary>
        /// Generates clean data for a single Wii block (0x7C00 bytes).
        /// </summary>
        public byte[] GenerateCleanBlockData(int blockIndex, DataPattern pattern)
        {
            byte[] data = new byte[WiiConsts.WiiSectorFsSize]; // 0x7C00

            switch (pattern)
            {
                case DataPattern.Sequential:
                    // Each byte is (blockIndex + byteIndex) % 256
                    for (int i = 0; i < data.Length; i++)
                        data[i] = (byte)((blockIndex + i) % 256);
                    break;

                case DataPattern.BlockRepeating:
                    // Each block has a repeating pattern based on its index
                    byte pattern_byte = (byte)(blockIndex % 256);
                    for (int i = 0; i < data.Length; i++)
                        data[i] = (byte)((pattern_byte + (i % 16)) % 256);
                    break;

                case DataPattern.Zeros:
                    // All zeros (already initialized)
                    break;

                case DataPattern.Random:
                    // Deterministic "random" based on block index
                    Random rng = new Random(blockIndex);
                    rng.NextBytes(data);
                    break;

                case DataPattern.AlternatingBytes:
                    // Alternating 0x55, 0xAA pattern
                    for (int i = 0; i < data.Length; i++)
                        data[i] = (byte)(((i + blockIndex) % 2) == 0 ? 0x55 : 0xAA);
                    break;
            }

            return data;
        }

        /// <summary>
        /// Extracts clean data from an encrypted block (for verification).
        /// </summary>
        public byte[] ExtractCleanDataFromEncryptedBlock(byte[] encryptedGroup, int blockOffset)
        {
            // Validate we have at least a full group
            if (encryptedGroup.Length < WiiConsts.WiiGroupSize)
                throw new ArgumentException($"Encrypted data must be at least {WiiConsts.WiiGroupSize} bytes (full group)");

            // Decrypt the FULL group
            byte[] decrypted = new byte[WiiConsts.WiiGroupSize];

            WiiSecurity sec = new WiiSecurity((int)WiiConsts.WiiGroupSize);
            sec.Populate(
                key: TestTitleKey,
                enc: encryptedGroup,
                dec: decrypted,
                size: (int)WiiConsts.WiiGroupSize,
                isEnc: true,
                isEncHeader: false,
                hashRebuild: false,
                areaOffset: 0,
                h3Table: null,
                flags: null,
                hashesRestored: false
            );

            sec.Decrypt();

            // Extract clean data from the specific block (skip hash area)
            byte[] cleanData = new byte[WiiConsts.WiiSectorFsSize];
            int dataOffset = blockOffset + WiiConsts.WiiSectorHashSize;
            Array.Copy(decrypted, dataOffset, cleanData, 0, WiiConsts.WiiSectorFsSize);

            return cleanData;
        }

        /// <summary>
        /// Validates that a block's hashes are correct.
        /// NOTE: This validates the entire GROUP, not just one block,
        /// because WiiSecurity requires full group size for validation.
        /// </summary>
        public bool ValidateBlockHashes(byte[] encryptedGroup, int blockOffset)
        {
            // Validate we have a full group
            if (encryptedGroup.Length < WiiConsts.WiiGroupSize)
                return false;

            byte[] decrypted = new byte[WiiConsts.WiiGroupSize];

            // Decrypt the FULL group
            WiiSecurity sec = new WiiSecurity((int)WiiConsts.WiiGroupSize);
            sec.Populate(
                key: TestTitleKey,
                enc: encryptedGroup,
                dec: decrypted,
                size: (int)WiiConsts.WiiGroupSize,
                isEnc: true,
                isEncHeader: false,
                hashRebuild: false,
                areaOffset: 0,
                h3Table: _h3Table, // Pass the H3 table for validation
                flags: null,
                hashesRestored: false
            );

            sec.Decrypt();

            // Validate the entire group's hashes
            bool isCreatable;
            return sec.IsValid(hashRecalculateIfDirty: false, out isCreatable);
        }

        /// <summary>
        /// Gets the H3 hash for a specific group.
        /// </summary>
        public byte[] GetH3Hash(int groupIndex)
        {
            byte[] hash = new byte[20];
            Array.Copy(_h3Table, groupIndex * 20, hash, 0, 20);
            return hash;
        }

        /// <summary>
        /// Calculates the SHA1 hash of data (for verification).
        /// </summary>
        public static byte[] CalculateSHA1(byte[] data)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                return sha1.ComputeHash(data);
            }
        }

        /// <summary>
        /// Calculates the CRC32 of data (for verification).
        /// </summary>
        public static uint CalculateCRC32(byte[] data) => TestHashUtil.ComputeCrc32(data);
    }

    /// <summary>
    /// Patterns for generating test data.
    /// </summary>
    public enum DataPattern
    {
        /// <summary>Sequential bytes: 0, 1, 2, ... 255, 0, 1, ...</summary>
        Sequential,

        /// <summary>Each block has a repeating pattern based on block index.</summary>
        BlockRepeating,

        /// <summary>All zeros.</summary>
        Zeros,

        /// <summary>Deterministic random based on block index.</summary>
        Random,

        /// <summary>Alternating 0x55, 0xAA pattern.</summary>
        AlternatingBytes
    }
}
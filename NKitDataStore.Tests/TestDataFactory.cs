using Nanook.NKit;
using Nanook.NKit.Nintendo.WiiGc;
using System.Security.Cryptography;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Test helper utilities to create fake headers, generate hashed/checked blocks
    /// and assemble strided source blocks (Wii-style) for unit tests.
    /// This intentionally avoids depending on internal BlockKey types and returns
    /// plain data structures (crc, xxhash) that tests can use to compare expectations.
    /// </summary>
    public static class TestDataFactory
    {
        private static readonly Random Rng = new Random(12345);

        public static byte[] CreateRandomData(int size)
        {
            byte[] data = new byte[size];
            Rng.NextBytes(data);
            return data;
        }

        public static byte[] CreateZeroData(int size) => new byte[size];

        public static uint ComputeCrc32(byte[] data, int offset = 0, int? length = null) => Crc.Compute(data, offset, length ?? (data.Length - offset));

        public static ulong ComputeXxHash64(byte[] data, int offset = 0, int? length = null) => XXHash64.Compute(data, offset, length ?? (data.Length - offset));

        /// <summary>
        /// Splits clean data into logical blocks of given blockSize and returns per-block checksums.
        /// </summary>
        public static List<(int Index, int Size, uint Crc32, ulong XxHash64, byte[] Data)> ComputeBlockChecksums(byte[] cleanData, int blockSize)
        {
            List<(int, int, uint, ulong, byte[])> list = new List<(int, int, uint, ulong, byte[])>();
            int offset = 0;
            int idx = 0;
            while (offset < cleanData.Length)
            {
                int take = Math.Min(blockSize, cleanData.Length - offset);
                byte[] buf = new byte[take];
                Array.Copy(cleanData, offset, buf, 0, take);
                uint crc = ComputeCrc32(buf);
                ulong xx = ComputeXxHash64(buf);
                list.Add((idx, take, crc, xx, buf));
                offset += take;
                idx++;
            }
            return list;
        }

        /// <summary>
        /// Assemble a byte array representing concatenated strided source blocks.
        /// For each entry in cleanBlocks (size = dataLength), creates a source block of strideBlockSize, writes the optional hash
        /// at the beginning of the block (hashProvider can return null/empty to leave zeros) and copies the clean block at dataOffset.
        /// Remaining bytes are zero-filled.
        /// </summary>
        public static byte[] CreateStridedSourceBlocks(List<byte[]> cleanBlocks, int strideBlockSize, int dataOffset, int dataLength, Func<int, byte[]> hashProvider)
        {
            if (dataLength > strideBlockSize - dataOffset)
                throw new ArgumentException("dataLength does not fit within stride block at given dataOffset");

            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < cleanBlocks.Count; i++)
                {
                    byte[] block = new byte[strideBlockSize];
                    // optional hash placed at start
                    if (hashProvider != null)
                    {
                        byte[] h = hashProvider(i);
                        if (h != null && h.Length > 0)
                        {
                            Array.Copy(h, 0, block, 0, Math.Min(h.Length, strideBlockSize));
                        }
                    }

                    // copy clean data
                    byte[] clean = cleanBlocks[i] ?? new byte[dataLength];
                    Array.Copy(clean, 0, block, dataOffset, Math.Min(clean.Length, dataLength));

                    ms.Write(block, 0, block.Length);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// AES-CBC encrypt/decrypt helper. When encrypt==true it encrypts source->dest, otherwise decrypts.
        /// Uses NoPadding (caller must provide full-block multiples as needed).
        /// </summary>
        public static byte[] AesCbcTransform(byte[] input, byte[] key, byte[] iv, bool encrypt = true)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                using (MemoryStream ms = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(ms, transform, CryptoStreamMode.Write))
                {
                    cs.Write(input, 0, input.Length);
                    cs.FlushFinalBlock();
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Create a vector of clean blocks (byte[]) filled with a reproducible pattern for testing.
        /// </summary>
        public static List<byte[]> CreateSequentialCleanBlocks(int blockCount, int dataLength, byte start = 0)
        {
            List<byte[]> list = new List<byte[]>();
            for (int i = 0; i < blockCount; i++)
            {
                byte[] b = new byte[dataLength];
                for (int j = 0; j < dataLength; j++)
                    b[j] = (byte)((start + i + j) & 0xFF);
                list.Add(b);
            }
            return list;
        }

        /// <summary>
        /// Convenience that creates strided source blocks from sequential clean data and returns both the source bytes
        /// and the computed per-clean-block checksums for verification.
        /// </summary>
        public static (byte[] SourceBlocks, List<(uint Crc32, ulong XxHash64)> Checksums) CreateStridedSourceAndChecksums(int blockCount, int strideBlockSize, int dataOffset, int dataLength, Func<int, byte[]> hashProvider)
        {
            List<byte[]> cleanBlocks = CreateSequentialCleanBlocks(blockCount, dataLength);
            List<(uint, ulong)> checks = new List<(uint, ulong)>();
            foreach (byte[] cb in cleanBlocks)
            {
                checks.Add((ComputeCrc32(cb), ComputeXxHash64(cb)));
            }
            byte[] src = CreateStridedSourceBlocks(cleanBlocks, strideBlockSize, dataOffset, dataLength, hashProvider);
            return (src, checks);
        }

        // ------------------- Wii-specific helpers using NKit classes -------------------

        /// <summary>
        /// Build a Wii encrypted group buffer using NKit's WiiSecurity.
        /// cleanSectorFsData: list of byte[] representing the filesystem portion (0x7C00) for each sector in the group.
        /// Returns the encrypted group bytes (length = WiiConsts.WiiGroupSize).
        /// </summary>
        public static byte[] CreateWiiEncryptedGroupFromFsSectors(List<byte[]> cleanSectorFsData, byte[] key, byte[] h3Table)
        {
            if (cleanSectorFsData == null)
                throw new ArgumentNullException(nameof(cleanSectorFsData));

            int groupSize = (int)WiiConsts.WiiGroupSize;
            int sectorSize = (int)WiiConsts.WiiSectorSize;
            int sectorHashSize = (int)WiiConsts.WiiSectorHashSize;
            int fsSize = sectorSize - sectorHashSize; // typically 0x7C00

            if (cleanSectorFsData.Count > groupSize / sectorSize)
                throw new ArgumentException("Too many sectors for a single Wii group");

            // Prepare buffers
            byte[] dec = new byte[groupSize];
            byte[] enc = new byte[groupSize];

            // Copy clean FS data into decrypted buffer at each sector's fs offset
            for (int i = 0; i < cleanSectorFsData.Count; i++)
            {
                byte[] fs = cleanSectorFsData[i] ?? new byte[fsSize];
                int destOff = (i * sectorSize) + sectorHashSize;
                Array.Copy(fs, 0, dec, destOff, Math.Min(fs.Length, fsSize));
            }

            // create security and populate; ask it to rebuild hashes (hashRebuild = true)
            WiiSecurity sec = new WiiSecurity(groupSize);
            // BitState flags: sized like other code (bytes = (sectors/8)+1)
            int scrubBytes = (WiiConsts.WiiSectors / 8) + 1;
            BitState flags = new BitState(scrubBytes);
            // default key if not supplied
            byte[] useKey = key ?? WiiConsts.NKitWipeTitleKey;

            sec.Populate(useKey, enc, dec, groupSize, false, false, true, 0, h3Table, flags, false);
            // Mark dirty to signal hashes need generation
            sec.MarkDirty();
            // Recalculate hashes and then encrypt
            bool creatable;
            sec.IsValid(true, out creatable); // forces hash rebuild
            sec.Encrypt();

            // After Encrypt the internal encrypted buffer is available via ensureEncrypted call. But we provided 'enc' buffer and Populate/Encrypt operate on it.
            // Return enc
            return enc;
        }

        /// <summary>
        /// Create N contiguous strided encrypted blocks for Wii by composing groups.
        /// Each group will contain up to (WiiGroupSize/sectorSize) sectors. The input cleanBlocks contain filesystem-sized data per sector.
        /// </summary>
        public static byte[] CreateWiiEncryptedStridedSource(List<byte[]> cleanBlocks)
        {
            if (cleanBlocks == null)
                throw new ArgumentNullException(nameof(cleanBlocks));

            int sectorSize = (int)WiiConsts.WiiSectorSize;
            int sectorFsSize = sectorSize - (int)WiiConsts.WiiSectorHashSize;
            int sectorsPerGroup = (int)WiiConsts.WiiGroupSize / sectorSize;

            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < cleanBlocks.Count; i += sectorsPerGroup)
                {
                    List<byte[]> slice = cleanBlocks.Skip(i).Take(sectorsPerGroup).Select(b => b ?? new byte[sectorFsSize]).ToList();
                    byte[] group = CreateWiiEncryptedGroupFromFsSectors(slice, null, null);
                    ms.Write(group, 0, group.Length);
                }
                return ms.ToArray();
            }
        }
    }
}
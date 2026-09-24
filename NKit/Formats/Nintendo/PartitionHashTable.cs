using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Nanook.NKit.Nintendo
{
    internal class PartitionHashTables
    {
        private const int _hashLen = 20;

        public PartitionHashTables(int sectorSize, int hashSize, int h0Offset, int h1Offset, int h2Offset, int h0Count, int h1Count, int h2Count, SystemType system)
        {
            int h0 = -1;
            int h1 = -1;
            int h2 = -1;

            if (system == SystemType.Wii)
            {
                int sectors = h1Count * h2Count;
                H0 = new PartitionHashTable[sectors]; //64 tables of h0 31 hashes (1 per 0x400 of data) : + 1 for hash block
                H1 = new PartitionHashTable[sectors / h1Count]; //8 sectors of hashed h0 table hashes
                H2 = new PartitionHashTable[sectors / (h1Count * h2Count)]; //1 table of hashed h1 table hashes
                for (int i = 0; i < sectors; i++) //per sector
                {
                    int offset = i * sectorSize;
                    H0[++h0] = new PartitionHashTable(i, h0Count, offset + h0Offset, offset + hashSize, hashSize, hashSize, 0); //datasize is same as size
                    if (i % (sectors / H1.Length) == 0)
                        H1[++h1] = new PartitionHashTable(i, h1Count, offset + h1Offset, offset + h0Offset, h0Count * _hashLen, sectorSize, 1);
                    else
                        H1[h1].DupeHashOffsets.Add(offset + h1Offset);
                    if (i % (sectors / H2.Length) == 0)
                        H2[++h2] = new PartitionHashTable(i, h2Count, offset + h2Offset, offset + h1Offset, h1Count * _hashLen, sectorSize, h1Count);
                    else
                        H2[h2].DupeHashOffsets.Add(offset + h2Offset);
                }
            }
            else if (system == SystemType.WiiU)
            {
                int sectors = h0Count * h1Count; //we only process 1 full H2 block at a time (16MiB rather than 256MiB)
                H0 = new PartitionHashTable[sectors / h0Count]; //16 tables of h0 16 hashes
                H1 = new PartitionHashTable[sectors / (h0Count * h1Count)]; //1 table of hashed h0 table hashes
                H2 = new PartitionHashTable[sectors / (h0Count * h1Count /** h2Count*/)]; //1 table or which we must check the correct hash (1 of 16)
                for (int i = 0; i < sectors; i++) //per sector
                {
                    int offset = i * sectorSize;
                    if (i % (sectors / H0.Length) == 0)
                        H0[++h0] = new PartitionHashTable(i, h0Count, offset + h0Offset, offset + hashSize, sectorSize - hashSize, sectorSize, 1);
                    else
                        H0[h0].DupeHashOffsets.Add(offset + h0Offset);
                    if (i % (sectors / H1.Length) == 0)
                        H1[++h1] = new PartitionHashTable(i, h1Count, offset + h1Offset, offset, h0Count * _hashLen, sectorSize, h0Count);
                    else
                        H1[h1].DupeHashOffsets.Add(offset + h1Offset);
                    if (i % (sectors / H2.Length) == 0)
                        H2[++h2] = new PartitionHashTable(i, h2Count, offset + h2Offset, offset + h1Offset, h1Count * _hashLen, sectorSize, h0Count * h1Count /** h2Count*/);
                    else
                        H2[h2].DupeHashOffsets.Add(offset + h2Offset);
                }
            }
            else
                throw new HandledException("Unknown PartitionHashTable type {0}", system.ToString());
        }

        public PartitionHashTable[] H0 { get; }
        public PartitionHashTable[] H1 { get; }
        public PartitionHashTable[] H2 { get; }
    }


    internal class PartitionHashTable
    {
        private readonly int[] _hashOffsets;
        private readonly int[] _dataOffsets;
        private const int _hashLen = 20;
        private readonly int _hashesLen;

        public int HashCount { get; }
        private readonly int _dataLength;

        public int Sector { get; }
        public int HashSectorSpan { get; } //amount of sectors to increment Sectors by for each hash

        internal PartitionHashTable(int sectorIdx, int hashCount, int hashOffset, int dataOffsets, int dataLength, int sectorSize, int sectorSpan)
        {
            if (sectorSpan == 0)
                sectorSpan = 1;
            this.DupeHashOffsets = new List<int>();
            Sector = sectorIdx;
            HashSectorSpan = sectorSpan;
            _hashOffsets = new int[hashCount];
            _dataOffsets = new int[hashCount];
            for (int i = 0; i < hashCount; i++)
            {
                _hashOffsets[i] = hashOffset;
                _dataOffsets[i] = dataOffsets;
                hashOffset += _hashLen;
                dataOffsets += sectorSpan * sectorSize;
            }
            _hashesLen = hashCount * _hashLen;
            HashCount = hashCount;
            _dataLength = dataLength;
        }

        public override string ToString() => $"Idx:{Sector}, HashOffset:{_hashOffsets[0]:X}, Hashes:{HashCount}, DataOffset:{_dataOffsets[0]:X}";
        public List<int> DupeHashOffsets { get; }

        public bool IsValid(byte[] data, int groupOffset, int hashIndex, SHA1 sha) => data.Equals(groupOffset + _hashOffsets[hashIndex], sha.ComputeHash(data, groupOffset + _dataOffsets[hashIndex], _dataLength), 0, _hashLen);
        public bool IsValid(byte[] data, int groupOffset, int hashIndex, byte[] sha1) => data.Equals(groupOffset + _hashOffsets[hashIndex], sha1, 0, _hashLen);

        public bool IsClear(byte[] data, int groupOffset, int hashIndex) => data.Equals(groupOffset + _hashOffsets[hashIndex], _hashLen, 0);

        public void CopyAll(byte[] data, int groupOffset, int size)
        {
            foreach (int off in DupeHashOffsets)
            {
                if (off >= size)
                    break;
                Array.Copy(data, groupOffset + _hashOffsets[0], data, groupOffset + off, _hashesLen);
            }
        }

        public void Set(byte[] data, int groupOffset, int hashIndex, byte[] sha1) => Array.Copy(sha1, 0, data, groupOffset + _hashOffsets[hashIndex], _hashLen);

        public void Clear(byte[] data, int groupOffset, int hashIndex) => Array.Clear(data, groupOffset + _hashOffsets[hashIndex], _hashLen);

        public bool IsAllValid(byte[] data, int groupOffset, SHA1 sha, int size, bool testFiller, out bool creatable)
        {
            bool valid = true;
            creatable = true;
            for (int i = 0; valid && i < this.HashCount; i++)
            {
                if (_dataOffsets[i] < size)
                {
                    if (!this.IsValid(data, groupOffset, i, sha))
                        valid = false;
                }
                else if (testFiller)
                {
                    if (!this.IsValid(data, groupOffset, i, sha))
                        creatable = false;
                }
                else if (!this.IsClear(data, groupOffset, i)) //!test filler
                    creatable = false;
            }

            if (valid)
            {
                foreach (int off in DupeHashOffsets)
                {
                    if (off >= size)
                        break;
                    if (!data.Equals(_hashOffsets[0], data, off, _hashesLen))
                    {
                        valid = false;
                        break;
                    }
                }
            }
            if (!valid)
                creatable = false;
            return valid;
        }

        public void GenerateAll(byte[] data, int groupOffset, SHA1 sha, int size, bool genFiller)
        {
            for (int i = 0; i < this.HashCount; i++)
            {
                if (_dataOffsets[i] < size || genFiller)
                    this.Set(data, groupOffset, i, this.HashData(data, groupOffset, i, sha));
                else if (!genFiller)
                    this.Clear(data, groupOffset, i);
            }

            this.CopyAll(data, groupOffset, size);
        }

        public void GenerateMissing(byte[] data, int groupOffset, SHA1 sha, Func<int, bool> gen)
        {
            bool firstGenned = false;
            for (int i = 0; i < this.HashCount; i++)
            {
                bool g = gen(_dataOffsets[i]);
                if (g && i == 0)
                    firstGenned = true;
                if (gen(_dataOffsets[i]) && firstGenned) //only update if the source of the hash table (index 0)
                    this.Set(data, groupOffset, i, this.HashData(data, groupOffset, i, sha));
            }

            foreach (int off in DupeHashOffsets)
            {
                if (gen(off))
                    Array.Copy(data, groupOffset + _hashOffsets[0], data, groupOffset + off, _hashesLen);
            }
        }

        public byte[] HashData(byte[] data, int groupOffset, int hashIndex, SHA1 sha) => sha.ComputeHash(data, groupOffset + _dataOffsets[hashIndex], _dataLength);

        public byte[] HashHashes(byte[] data, int groupOffset, SHA1 sha) => sha.ComputeHash(data, groupOffset + _hashOffsets[0], _hashesLen);

        internal bool CompareDupeHashes(byte[] data, int groupOffset, int size)
        {
            foreach (int off in DupeHashOffsets)
            {
                if (off >= size)
                    break;
                if (!data.Equals(_hashOffsets[0], data, groupOffset + off, _hashesLen))
                    return false;
            }
            return true;
        }

        internal PartitionHashTable(int hashCount, int hashOffset)
        {
            HashCount = hashCount;
            _hashOffsets = new int[hashCount];
            for (int i = 0; i < hashCount; i++)
            {
                _hashOffsets[i] = hashOffset;
                hashOffset += _hashLen;
            }

        }

    }

}
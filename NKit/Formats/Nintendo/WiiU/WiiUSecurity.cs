using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nanook.NKit.Nintendo.WiiU
{
    /// <summary>
    /// Class to manage the encryption and hash state to ensure the minimal amount of hashing and encryption happens per group
    /// </summary>
    internal class WiiUSecurity
    {
        private byte[] _h3Table;
        private byte[] _h3Value;
        private int _hashesMatchData; //-1=not set, 0=false, 1=true 
        private int _hashesAreValid; //-1=not set, 0=false, 1=true
        private int _hashesMatchH3; //-1=not set, 0=false, 1=true
        private byte[] _enc;
        private byte[] _dec;
        private bool _hasEnc; //true if encrypted data is set on populate and decrypted data has not been modified
        private bool _hasDec; //true if decrypted data is set or we have decrypted the encrypted data
        private bool _isDirty; //we have decrypted data and it has been modified
        private readonly WiiSecuritySector[] _sectors;
        private long _areaOffset;
        private int _size;
        private byte[] _key;
        private int _usedSectors;
        private int _h2Index;
        private bool _hasHashes;
        private int _sectorSize;
        private ImageHeader _header;
        private ContentHeader _cntHeader;
        private PartitionType _type;
        private AreaType _areaType;
        private bool _isFs;
        private static readonly PartitionHashTables[] _HTables;
        private PartitionHashTables _hTable;
        private byte[] _hashlessIv;

        static WiiUSecurity()
        {
            _HTables = new PartitionHashTables[2];
            _HTables[0] = new PartitionHashTables(WiiUConsts.DefaultSectorSize, WiiUConsts.HashSize, WiiUConsts.H0Offset, WiiUConsts.H1Offset, WiiUConsts.H2Offset, WiiUConsts.H0Count, WiiUConsts.H1Count, WiiUConsts.H2Count, SystemType.WiiU);
            _HTables[1] = new PartitionHashTables(WiiUConsts.DefaultSectorSize << 1, WiiUConsts.HashSize, WiiUConsts.H0Offset, WiiUConsts.H1Offset, WiiUConsts.H2Offset, WiiUConsts.H0Count, WiiUConsts.H1Count, WiiUConsts.H2Count, SystemType.WiiU);
        }
        public WiiUSecurity(ImageHeader header)
        {
            _header = header;
            _sectorSize = header.BlockSizeHashed; // _hasHashes ? header.BlockSizeHashed : header.BlockSize;
            _sectors = new WiiSecuritySector[WiiUConsts.H0Count * WiiUConsts.H1Count]; //(Header.BlockSizeHashed / _sectorSize)]; // ; //*2 if no hashes (size is then 0x8000 not 0x10000)

            for (int i = 0; i < _sectors.Length; i++)
                _sectors[i] = new WiiSecuritySector(i, _sectorSize, WiiUConsts.HashSize); //set hashes to false, apply later on
        }

        public void Decrypt() => ensureDecrypted();
        public void Encrypt() => ensureEncrypted();

        public void Populate(ContentHeader cntHeader, SiData siData, PartitionType type, AreaType areaType, byte[] enc, byte[] dec, int size, bool isEnc, bool forceDecryptedHashRebuild, long areaOffset, byte[] hashlessIv = null)
        {
            _type = type;
            _areaType = areaType;
            _isFs = _areaType == AreaType.FileSystem || (_areaType == AreaType.Other && (cntHeader?.RepeatedApp ?? false));
            _h3Table = cntHeader?.H3Hashes;
            _cntHeader = cntHeader;
            _hasHashes = cntHeader?.HasHashes ?? false;
            _areaOffset = areaOffset;
            _hashlessIv = hashlessIv;

            if (!_hasHashes)
            {
                forceDecryptedHashRebuild = false;
                _h2Index = 0;
            }
            else if (_cntHeader.RepeatedApp) //has _cntHeader and hashes
                _h2Index = (int)(_areaOffset % _cntHeader.SizePaddedToBlock / _header.H2BlockSize) % WiiUConsts.H2Count; //reset the offset for repeated "Other" data
            else //has _cntHeader and hashes not repeat
                _h2Index = (int)(_areaOffset / _header.H2BlockSize) % WiiUConsts.H2Count;

            _hashesMatchData = -1;
            _hashesAreValid = -1;
            _hashesMatchH3 = -1;

            if (enc.Length != dec.Length)
                throw new HandledException("Enc and Dec data must be of equal length");

            int s = Math.Min(size, dec.Length);
            if (_hasHashes && s % _header.BlockSize != 0)
                throw new HandledException(string.Format("Group size is not a multiple of 0x{0}", _header.BlockSize.ToString("X4")));

            if (enc.Length < _header.BlockSize)
                throw new HandledException(string.Format("Data must have a length of at least 0x{0}", _header.BlockSize.ToString("X8")));

            _size = s;
            _enc = enc;
            _dec = dec;
            _key = WiiUSecurityContext.GetActiveEncryptionKey(_type, _areaType, false, _header.Key, siData?.KeyTitle);

            _hasEnc = isEnc;
            _hasDec = !isEnc; //dec array isDirty if isEnc
            _isDirty = forceDecryptedHashRebuild; //dirty if rebuilding
            _usedSectors = (s / _sectorSize) + (s % _sectorSize == 0 ? 0 : 1);

            _hTable = _HTables[_sectorSize == WiiUConsts.DefaultSectorSize ? 0 : 1];

            for (int i = 0; i < _sectors.Length; i++)
            {
                _sectors[i].Populate();
                _sectors[i].IsUsed = i < _usedSectors;
                _sectors[i].Aes.Key = _key;
            }

            if (!isEnc && _hasHashes)
            {
                if (forceDecryptedHashRebuild)
                    recalculateDirtyHashes();
            }
        }
        internal void MarkDirty()
        {
            _isDirty = true;
            _hasEnc = false; //invalidate encrypted data
        }

        internal void MarkDecrypted() => _hasDec = true;

        public int UsedSectors => _usedSectors;
        public int UnusedSectors => _sectors.Length - _usedSectors;
        public int UsedSize => _size;
        public int SectorOffset(int sectorIndex) => _sectors[sectorIndex].Offset;
        public int SectorFsOffset(int sectorIndex) => _sectors[sectorIndex].FsOffset;

        private bool validate(out bool creatable)
        {
            creatable = false;
            ensureDecrypted();
            if (!_hasHashes)
                return true;

            if (_hashesMatchData == -1 || _hashesAreValid == -1)
            {
                bool anyInvalid = false;
                creatable = true;
                bool outCreatable = creatable;
                Parallel.ForEach(_hTable.H0, h0 =>  //can parallel
                {
                    if (!anyInvalid && _sectors[h0.Sector].IsUsed) //would be dirty read of old data
                    {
                        SHA1 sha = _sectors[h0.Sector].Sha;
                        if (!h0.IsAllValid(_dec, 0, sha, _size, false, out bool cr))
                            anyInvalid = true;
                        if (!cr)
                            outCreatable = false;
                        else
                        {
                            //post hashes data if not nulls
                            if (!_dec.Equals(_sectors[h0.Sector].Offset + WiiUConsts.H2Offset + WiiUConsts.H2Len, 0x40, 0))
                                outCreatable = false;
                            else
                            {
                                for (int i = 0; outCreatable && i < h0.DupeHashOffsets.Count; i++)
                                {
                                    if (!_dec.Equals(h0.DupeHashOffsets[i] + WiiUConsts.H2Offset + WiiUConsts.H2Len, 0x40, 0))
                                        outCreatable = false;
                                }
                            }
                        }
                    }
                });
                creatable = outCreatable;

                if (!anyInvalid)
                {
                    foreach (PartitionHashTable h1 in _hTable.H1)
                    {
                        if (!anyInvalid && _sectors[h1.Sector].IsUsed) //would be dirty read of old data
                        {
                            SHA1 sha = _sectors[h1.Sector].Sha;
                            if (!h1.IsAllValid(_dec, 0, sha, _size, false, out bool cr))
                                anyInvalid = true;
                            if (!cr)
                                creatable = false;
                        }

                        if (anyInvalid)
                            break;
                    }
                }

                if (!anyInvalid) //test the correct H2 entry. If the h2 table hash matches the H3 then the other hashes are correct also
                {
                    PartitionHashTable h2 = _hTable.H2[0];
                    if (!h2.IsValid(_dec, 0, _h2Index, _hTable.H1[0].HashHashes(_dec, 0, _sectors[_hTable.H1[0].Sector].Sha)))
                        anyInvalid = true;
                }

                if (anyInvalid)
                    creatable = false;

                _hashesMatchData = _hashesAreValid = !anyInvalid ? 1 : 0;
            }

            if (_hashesAreValid == 1)
            {
                PartitionHashTable h2 = _hTable.H2[0];
                _h3Value = h2.HashHashes(_dec, 0, _sectors[h2.Sector].Sha);
                int hashIdx = (int)(_areaOffset / _header.H3BlockSize);
                if (_cntHeader != null && _cntHeader.RepeatedApp)
                    hashIdx = (int)(_areaOffset % _cntHeader.SizePaddedToBlock / _header.H3BlockSize); //reset the offset for repeated "Other" data
                _hashesMatchH3 = hashIdx <= _h3Table.Length - 20 && _h3Table.Equals(hashIdx * 20, _h3Value, 0, 20) ? 1 : 0;
            }

            return _hashesMatchData == 1 && _hashesAreValid == 1 && _hashesMatchH3 == 1;
        }


        private void recalculateDirtyHashes()
        {
            if (!_hasHashes || !_isDirty)
                return;

            //create all h0 hashes
            Parallel.ForEach(_hTable.H0, h0 =>
            {
                h0.GenerateAll(_dec, 0, _sectors[h0.Sector].Sha, _size, false);
                Array.Clear(_dec, _sectors[h0.Sector].Offset + WiiUConsts.H2Offset + WiiUConsts.H2Len, 0x40);
                foreach (int off in h0.DupeHashOffsets)
                    Array.Clear(_dec, off + WiiUConsts.H2Offset + WiiUConsts.H2Len, 0x40);
            });

            //create all h1 hashes
            foreach (PartitionHashTable h1 in _hTable.H1)
                h1.GenerateAll(_dec, 0, _sectors[h1.Sector].Sha, _size, false);

            //H2 can't be calculated
            _isDirty = false;
        }

        public bool IsValid(bool hashRecalculateIfDirty, out bool isCreatable)
        {
            bool dirty = _isDirty;
            ensureDecrypted();
            if (hashRecalculateIfDirty && dirty)
                recalculateDirtyHashes();

            return validate(out isCreatable);
        }

        private byte[] ensureEncrypted()
        {
            if (!_hasEnc) //if false we must have decrypted data
            {
                if (!_hasHashes && (!_isFs || _type == PartitionType.Game))
                {
                    byte[] iv;

                    if (_hashlessIv != null)
                        iv = _hashlessIv;
                    else
                    {
                        iv = new byte[16];
                        if (_type == PartitionType.Game && _cntHeader != null)
                            iv[1] = (byte)_cntHeader.Index;
                    }
                    EncryptHashless(_dec, _enc, 0, _size, _key, iv); //WipePartition header / fst
                }
                else
                {
                    //ensureHashCache(); //calc hashes if dirty
                    Parallel.ForEach(_sectors, b => encrypt(b));
                    _isDirty = false;
                    _hasEnc = true;
                }
            }
            return _enc;
        }

        private byte[] ensureDecrypted()
        {
            if (_hasEnc && !_hasDec) //_hasEnc only true when the data is not Populated as so and not dirty
            {
                if (!_hasHashes && (!_isFs || _type == PartitionType.Game))
                {
                    byte[] iv = new byte[16];
                    if (_type == PartitionType.Game && _cntHeader != null)
                        iv[1] = (byte)_cntHeader.Index;
                    DecryptHashless(_enc, _dec, 0, _size, _key, iv); //WipePartition header / fst
                }
                else
                {
                    //Parallel.ForEach(_sectors, b => decrypt(b));
                    foreach (WiiSecuritySector s in _sectors)
                        decrypt(s);
                }
                _hasDec = true;
            }
            return _dec;
        }

        private void encrypt(WiiSecuritySector b)
        {
            if (!b.IsUsed)
                return;

            byte[] iv = b.Iv;

            if (_hasHashes)
            {
                Array.Clear(iv, 0, 16);
                b.Aes.IV = iv; //takes as copy

                Array.Copy(_dec, b.Offset + (b.Index % WiiUConsts.H0Count * 0x14), iv, 0, 0x10); //copy the decrypted bytes now in case _enc == _dec

                using (ICryptoTransform cryptor = b.Aes.CreateEncryptor())
                    cryptor.TransformBlock(_dec, b.Offset, _header.HashesSize, _enc, b.Offset);

                b.Aes.IV = iv;

                using (ICryptoTransform cryptor = b.Aes.CreateEncryptor())
                    cryptor.TransformBlock(_dec, b.FsOffset, _header.BlockFsSizeHashed, _enc, b.FsOffset);
            }
            else
            {
                Array.Clear(iv, 0, 8);
                ulong l = (ulong)(_areaOffset + b.Offset);
                if (_cntHeader != null && _cntHeader.RepeatedApp)
                    l = (ulong)((_areaOffset % _cntHeader.Size) + b.Offset); //reset the offset for repeated "Other" data
                iv.WriteUInt64B(8, l >> 16);
                b.Aes.IV = iv;
                using (ICryptoTransform cryptor = b.Aes.CreateEncryptor())
                    cryptor.TransformBlock(_dec, b.Offset, _sectorSize, _enc, b.Offset);
            }
        }

        private void decrypt(WiiSecuritySector b)
        {
            if (!b.IsUsed)
                return;

            byte[] iv = b.Iv;

            if (_hasHashes)
            {
                Array.Clear(iv, 0, 16);
                b.Aes.IV = iv;

                using (ICryptoTransform cryptor = b.Aes.CreateDecryptor())
                    cryptor.TransformBlock(_enc, b.Offset, _header.HashesSize, _dec, b.Offset);

                Array.Copy(_dec, b.Offset + (b.Index % WiiUConsts.H0Count * 0x14), iv, 0, 0x10);
                b.Aes.IV = iv;

                using (ICryptoTransform cryptor = b.Aes.CreateDecryptor())
                    cryptor.TransformBlock(_enc, b.FsOffset, _header.BlockFsSizeHashed, _dec, b.FsOffset);
            }
            else
            {
                Array.Clear(iv, 0, 16);
                ulong l = (ulong)(_areaOffset + b.Offset);
                if (_cntHeader != null && _cntHeader.RepeatedApp)
                    l = (ulong)((_areaOffset % _cntHeader.Size) + b.Offset); //reset the offset for repeated "Other" data
                iv.WriteUInt64B(8, l >> 16);
                b.Aes.IV = iv;
                using (ICryptoTransform cryptor = b.Aes.CreateDecryptor())
                    cryptor.TransformBlock(_enc, b.Offset, _sectorSize, _dec, b.Offset);
            }
        }

        internal string DebugState()
        {
            StringBuilder sb = new StringBuilder();
            foreach (WiiSecuritySector b in _sectors)
                sb.AppendLine(b.ToString());

            return sb.ToString();
        }

        public static byte[] DecryptPartitionTable(byte[] enc, byte[] dec, int size, byte[] key) => DecryptHashless(enc, dec, 0, size, key, new byte[0x10]);

        public static byte[] DecryptFst(byte[] enc, byte[] dec, int size, byte[] key) => DecryptHashless(enc, dec, 0, size, key, new byte[0x10]);

        public static byte[] EncryptPartitionTable(byte[] enc, byte[] dec, int size, byte[] key) => EncryptHashless(dec, enc, 0, size, key, new byte[0x10]);

        public static byte[] EncryptFst(byte[] enc, byte[] dec, int size, byte[] key) => EncryptHashless(dec, enc, 0, size, key, new byte[0x10]);

        internal static byte[] DecryptHashless(byte[] enc, byte[] dec, int offset, int size, byte[] key, byte[] iv)
        {
            // AES-CBC requires block-aligned byte counts. CDN content sizes in TMD records can
            // be non-16-byte-aligned (the encrypted bytes on disk are always padded to 16, but
            // the TMD reports the true content length). Round up so TransformBlock doesn't throw.
            int alignedSize = (size + 15) & ~15;

            byte[] d = dec;
            int dOff = offset;
            if (d == null)
            {
                d = new byte[alignedSize];
                dOff = 0;
            }

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = iv;

                using (ICryptoTransform crypt = aes.CreateDecryptor())
                    crypt.TransformBlock(enc, offset, alignedSize, d, dOff);
            }
            return d;
        }
        internal static byte[] EncryptHashless(byte[] dec, byte[] enc, int offset, int size, byte[] key, byte[] iv)
        {
            byte[] e = enc;
            int eOff = offset;
            if (e == null)
            {
                e = new byte[size];
                eOff = 0;
            }

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = iv;

                using (ICryptoTransform crypt = aes.CreateEncryptor())
                    crypt.TransformBlock(dec, offset, size, e, eOff);
            }
            return e;
        }

        //Creates an IV that can be used to seek in to a large encrypted area. enc and dec must be populated and correct
        internal static byte[] CreateSeekIv(byte[] enc, byte[] dec, int offset, byte[] key, byte[] iv)
        {
            byte[] seekIv = DecryptHashless(enc, null, offset, 0x10, key, new byte[0x10]);
            for (int i = 0; i < seekIv.Length; i++)
                seekIv[i] ^= (byte)dec[i];

            if (seekIv.Equals(0, iv, 0, seekIv.Length))
                return null;

            return seekIv;
        }
    }
}

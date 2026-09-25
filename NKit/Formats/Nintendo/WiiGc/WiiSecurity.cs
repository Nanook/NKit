using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nanook.NKit.Nintendo.WiiGc
{
    /// <summary>
    /// Class to manage the encryption and hash state to ensure the minimal amount of hashing and encryption happens per group
    /// </summary>
    internal class WiiSecurity
    {

        private byte[] _h3Table;
        private byte[] _h3Value;
        private int _hashesMatchData; //-1=not set, 0=false, 1=true 
        private int _hashesMatchH3; //-1=not set, 0=false, 1=true
        private int _hashesCreatable;
        private bool _hashesRestored;
        private int _groupIdx;
        private byte[] _enc;
        private byte[] _dec;
        private int _encOffset;
        private int _decOffset;
        private bool _hasEnc; //true if encrypted data is set on populate and decrypted data has not been modified
        private bool _hasDec; //true if decrypted data is set or we have decrypted the encrypted data
        private readonly int _maxSize;
        private bool _isDirty; //we have decrypted data and it has been modified
        public readonly WiiSecuritySector[] Sectors;
        private long _areaOffset;
        private int _size;
        private int _usedSectors;
        private BitState _scrubbed;

        private readonly static PartitionHashTables _hTable; //stateless

        static WiiSecurity()
        {
            _hTable = new PartitionHashTables(WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.H0Offset, WiiConsts.H1Offset, WiiConsts.H2Offset, WiiConsts.H0Count, WiiConsts.H1Count, WiiConsts.H2Count, SystemType.Wii);
        }

        public WiiSecurity(int maxSize)
        {
            _groupIdx = -1;
            if (maxSize % WiiConsts.WiiSectorSize != 0)
                throw new HandledException("Max group size is not a multiple of 0x" + WiiConsts.WiiSectorSize.ToString("X4"));

            _maxSize = maxSize;
            Sectors = new WiiSecuritySector[maxSize / WiiConsts.WiiSectorSize];

            for (int i = 0; i < Sectors.Length; i++)
                Sectors[i] = new WiiSecuritySector(i, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize);
        }

        public void ForceDecrypt()
        {
            _hasEnc = true;
            _hasDec = false;
            Decrypt();
        }
        public void Decrypt() => ensureDecrypted();
        public void Encrypt() => ensureEncrypted();
        public void Populate(byte[] key, byte[] enc, byte[] dec, int size, bool isEnc, bool isEncHeader, bool hashRebuild, long areaOffset, byte[] h3Table, BitState flags, bool hashesRestored) => this.Populate(key, enc, 0, dec, 0, size, isEnc, isEncHeader, hashRebuild, areaOffset, h3Table, flags, hashesRestored);
        public void Populate(byte[] key, byte[] enc, int encOffset, byte[] dec, int decOffset, int size, bool isEnc, bool isEncHeader, bool hashRebuild, long areaOffset, byte[] h3Table, BitState flags, bool hashesRestored)
        {
            _scrubbed = flags ?? new BitState(WiiConsts.WiiSectors + 1);
            _hashesRestored = hashesRestored;

            _h3Table = h3Table;
            _areaOffset = areaOffset;
            _hashesMatchData = -1;
            _hashesMatchH3 = -1;
            _hashesCreatable = -1;

            ////if (enc.Length != dec.Length)
            //    throw new HandledException("Enc and Dec data must be of equal length");

            int s = Math.Min(size, dec.Length);
            if (s % WiiConsts.WiiSectorSize != 0)
                throw new HandledException(string.Format("Group size is not a multiple of 0x{0}", WiiConsts.WiiSectorSize.ToString("X4")));

            if (enc.Length < WiiConsts.WiiGroupSize)
                throw new HandledException(string.Format("Data must have a length of at least 0x{0}", _maxSize.ToString("X8")));

            _groupIdx = (int)(areaOffset / WiiConsts.WiiGroupSize);

            _size = s;
            _enc = enc;
            _dec = dec;
            _encOffset = encOffset;
            _decOffset = decOffset;

            _hasDec = !(_hasEnc = isEnc); //dec array isDirty if isEnc
            _isDirty = !hashesRestored && (hashRebuild || (scrubbedAll && scrubbedHashes)); //not scrubbed and dirty if rebuilding
            _usedSectors = s / WiiConsts.WiiSectorSize;

            for (int i = 0; i < Sectors.Length; i++)
            {
                Sectors[i].Populate();
                Sectors[i].IsUsed = i < _usedSectors;
                Sectors[i].Aes.Key = key ?? new byte[0x10];
            }

            bool recalcUnusedHashes = false;
            if (_size < _maxSize) //blank any unused sectors. Decrypting will leave them intact (hashes will have been calculated below, markeed dirty above)
            {
                Array.Clear(_enc, _encOffset + s, _maxSize - (_encOffset + s));
                Array.Clear(_dec, _decOffset + s, _maxSize - (_decOffset + s));
                recalcUnusedHashes = true;
            }

            if (!isEnc)
            {
                if (isEncHeader)
                {
                    Parallel.ForEach(Sectors, b =>
                    {
                        if (b.IsUsed)
                        {
                            b.Aes.IV = new byte[16];
                            using (ICryptoTransform cryptor = b.Aes.CreateDecryptor())
                                cryptor.TransformBlock(_dec, _decOffset + b.Offset, WiiConsts.WiiSectorHashSize, _dec, _decOffset + b.Offset);
                        }
                    });
                }
                else if (_isDirty)
                {
                    recalculateDirtyHashes();
                    recalcUnusedHashes = false; //re just recreated them all
                }
            }
            if (recalcUnusedHashes)
                buildUnusedHashes(_decOffset);
        }

        private bool scrubbedHashes => _scrubbed[WiiConsts.WiiSectors];
        private bool scrubbedAll
        {
            get
            {
                for (int i = 0; i < _scrubbed.Bytes.Length - 1; i++)
                {
                    if (_scrubbed.Bytes[i] != 0xff)
                        return false;
                }
                return true;
            }
        }

        internal void MarkDirty()
        {
            _hashesCreatable = -1;
            _hashesMatchData = -1;
            _isDirty = true;
            _hasEnc = false; //invalidate encrypted data
            _hasDec = true;
        }

        public int GroupIndex => _groupIdx;
        public int UsedSectors => _usedSectors;
        public int UnusedSectors => Sectors.Length - _usedSectors;
        public int UsedSize => _size;

        private bool validate(out bool creatable)
        {
            creatable = false;
            ensureDecrypted();

            if (!_scrubbed.IsClear()) //can be creatable if all used sectors are fully scrubbed
            {
                if (scrubbedHashes)
                {
                    creatable = true;
                    for (int i = 0; creatable && i < _usedSectors; i++)
                    {
                        if (!_scrubbed[i])
                            creatable = false;
                    }
                }
                return false; //not valid
            }

            if (_hashesMatchData == -1)
            {
                bool anyInvalid = false;
                creatable = true;
                bool outCreatable = creatable;

                Parallel.ForEach(_hTable.H0, h0 =>  //wii has 1 H0 per sector - scrubbing and IsUsed can be easily checked
                {
                    WiiSecuritySector sec = Sectors[h0.Sector];
                    if (!h0.IsAllValid(_dec, _decOffset, sec.Sha, (int)WiiConsts.WiiGroupSize, true, out bool cr))
                        anyInvalid = true;

                    //ensure the blank areas are blank - some customs fail this - Mario Kart Black
                    if (!anyInvalid && (
                        !_dec.Equals(sec.Offset + WiiConsts.H0Offset + WiiConsts.H0Len, WiiConsts.H1Offset - (WiiConsts.H0Offset + WiiConsts.H0Len), 0) ||
                        !_dec.Equals(sec.Offset + WiiConsts.H1Offset + WiiConsts.H1Len, WiiConsts.H2Offset - (WiiConsts.H1Offset + WiiConsts.H1Len), 0) ||
                        !_dec.Equals(sec.Offset + WiiConsts.H2Offset + WiiConsts.H2Len, WiiConsts.WiiSectorHashSize - (WiiConsts.H2Offset + WiiConsts.H2Len), 0)))
                        outCreatable = false;
                });
                creatable = outCreatable;

                if (!anyInvalid)
                {
                    foreach (PartitionHashTable h1 in _hTable.H1)
                    {
                        if (!anyInvalid) //would be dirty read of old data
                        {
                            SHA1 sha = Sectors[h1.Sector].Sha;
                            if (!h1.IsAllValid(_dec, _decOffset, sha, (int)WiiConsts.WiiGroupSize, true, out bool cr))
                            {
                                anyInvalid = true;
                                break;
                            }
                        }
                    }
                }

                if (!anyInvalid)
                {
                    foreach (PartitionHashTable h2 in _hTable.H2)
                    {
                        if (!anyInvalid) //would be dirty read of old data
                        {
                            SHA1 sha = Sectors[h2.Sector].Sha;
                            if (!h2.IsAllValid(_dec, _decOffset, sha, (int)WiiConsts.WiiGroupSize, true, out bool cr))
                            {
                                anyInvalid = true;
                                break;
                            }
                        }
                    }
                }

                if (anyInvalid)
                    creatable = false;

                _hashesMatchData = !anyInvalid ? 1 : 0;
                _hashesCreatable = creatable ? 1 : 0;
            }


            _h3Value = _hTable.H2[0].HashHashes(_dec, _decOffset, Sectors[_hTable.H2[0].Sector].Sha);
            if (_h3Table != null)
                _hashesMatchH3 = _h3Value.Equals(0, _h3Table, _groupIdx * 20, 20) ? 1 : 0;
            creatable = _hashesCreatable == 1;

            return _hashesMatchData == 1 && _hashesMatchH3 == 1;
        }

        //it's important to copy these carefully when preserving customs, hashes are not always consistent
        private void buildUnusedHashes(int groupOffset)
        {
            foreach (PartitionHashTable h0 in _hTable.H0)
                h0.GenerateMissing(_dec, groupOffset, Sectors[h0.Sector].Sha, off => off >= _size); //set full size

            foreach (PartitionHashTable h1 in _hTable.H1)
                h1.GenerateMissing(_dec, groupOffset, Sectors[h1.Sector].Sha, off => off >= _size); //set full size

            foreach (PartitionHashTable h2 in _hTable.H2)
                h2.GenerateMissing(_dec, groupOffset, Sectors[h2.Sector].Sha, off => off >= _size); //set full size
        }

        private void recalculateDirtyHashes()
        {
            if (!_isDirty)
                return;

            if (scrubbedAll) //wipe the hashes as they may be stale
            {
                for (int i = 0; i < _usedSectors; i++)
                    Array.Clear(_dec, _decOffset + Sectors[i].Offset, WiiConsts.WiiSectorHashSize);
                _hashesMatchH3 = 0;
            }
            else
            {
                //create all h0 hashes
                Parallel.ForEach(_hTable.H0, h0 =>
                {
                    h0.GenerateAll(_dec, _decOffset, Sectors[h0.Sector].Sha, (int)WiiConsts.WiiGroupSize, true);
                    int off = _decOffset + Sectors[h0.Sector].Offset;
                    Array.Clear(_dec, off + WiiConsts.H0Offset + WiiConsts.H0Len, WiiConsts.H1Offset - (WiiConsts.H0Offset + WiiConsts.H0Len)); //0x26c 31 20 byte hashes
                    Array.Clear(_dec, off + WiiConsts.H1Offset + WiiConsts.H1Len, WiiConsts.H2Offset - (WiiConsts.H1Offset + WiiConsts.H1Len)); //0xA0 8 20 byte hashes
                    Array.Clear(_dec, off + WiiConsts.H2Offset + WiiConsts.H2Len, WiiConsts.WiiSectorHashSize - (WiiConsts.H2Offset + WiiConsts.H2Len));
                });

                //create all h1 hashes
                foreach (PartitionHashTable h1 in _hTable.H1)
                    h1.GenerateAll(_dec, _decOffset, Sectors[h1.Sector].Sha, (int)WiiConsts.WiiGroupSize, true);

                PartitionHashTable h2 = _hTable.H2[0];
                h2.GenerateAll(_dec, _decOffset, Sectors[h2.Sector].Sha, (int)WiiConsts.WiiGroupSize, true);

                _h3Value = h2.HashHashes(_dec, _decOffset, Sectors[h2.Sector].Sha);
                if (_h3Table != null)
                    _hashesMatchH3 = _h3Value.Equals(0, _h3Table, _groupIdx * 20, 20) ? 1 : 0;
            }

            _isDirty = false;
        }

        public void UpdateH3Entry()
        {
            ensureEncrypted();
            if (_h3Table != null)
                Array.Copy(_h3Value, 0, _h3Table, _groupIdx * 20, 20);
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
                Parallel.ForEach(Sectors, b => encrypt(b));
                _isDirty = false;
                _hasEnc = true;
            }
            return _enc;
        }

        private byte[] ensureDecrypted()
        {
            if (_hasEnc && !_hasDec) //_hasEnc only true when the data is not Populated as so and not dirty
            {
                Parallel.ForEach(Sectors, b => decrypt(b));
                if (_usedSectors != Sectors.Length)
                    buildUnusedHashes(_decOffset);
                _hasDec = true;
            }
            return _dec;
        }

        private void encrypt(WiiSecuritySector b)
        {
            if (!b.IsUsed)
                return;

            byte[] iv = b.Iv;
            Array.Clear(iv, 0, 16);
            b.Aes.IV = iv; //takes as copy

            if (!_scrubbed[b.Index] || !scrubbedHashes)
            {
                using (ICryptoTransform cryptor = b.Aes.CreateEncryptor())
                    cryptor.TransformBlock(_dec, _decOffset + b.Offset, WiiConsts.WiiSectorHashSize, _enc, _encOffset + b.Offset);
            }
            else
                _enc.Clear(_encOffset + b.Offset, WiiConsts.WiiSectorHashSize, _dec[_decOffset + b.Offset]);

            Array.Copy(_enc, _encOffset + b.Offset + 0x3d0, iv, 0, 16);
            b.Aes.IV = iv;

            if (!_scrubbed[b.Index])
            {
                using (ICryptoTransform cryptor = b.Aes.CreateEncryptor())
                    cryptor.TransformBlock(_dec, _decOffset + b.FsOffset, WiiConsts.WiiSectorFsSize, _enc, _encOffset + b.FsOffset);
            }
            else
                _enc.Clear(_encOffset + b.FsOffset, WiiConsts.WiiSectorFsSize, _dec[_decOffset + b.FsOffset]);
        }

        private void decrypt(WiiSecuritySector b)
        {
            if (!b.IsUsed)
                return;

            byte[] iv = b.Iv;
            Array.Clear(iv, 0, 16);
            b.Aes.IV = iv; //takes a copy

            if (!_scrubbed[b.Index] || !scrubbedHashes)
            {
                using (ICryptoTransform cryptor = b.Aes.CreateDecryptor())
                    cryptor.TransformBlock(_enc, _encOffset + b.Offset, WiiConsts.WiiSectorHashSize, _dec, _decOffset + b.Offset);
            }
            else
                _dec.Clear(_decOffset + b.Offset, WiiConsts.WiiSectorHashSize, _enc[_encOffset + b.Offset]);

            Array.Copy(_enc, _encOffset + b.Offset + 0x3d0, iv, 0, 16); //get iv from encrypted header
            b.Aes.IV = iv;

            if (!_scrubbed[b.Index])
            {
                using (ICryptoTransform cryptor = b.Aes.CreateDecryptor())
                    cryptor.TransformBlock(_enc, _encOffset + b.FsOffset, WiiConsts.WiiSectorFsSize, _dec, _decOffset + b.FsOffset);
            }
            else
                _dec.Clear(b.FsOffset, WiiConsts.WiiSectorFsSize, _enc[b.FsOffset]);
        }

        internal string DebugState()
        {
            StringBuilder sb = new StringBuilder();
            foreach (WiiSecuritySector b in Sectors)
                sb.AppendLine(b.ToString());

            return sb.ToString();
        }

    }
}
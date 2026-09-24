using System;
using System.Security.Cryptography;

namespace Nanook.NKit.Nintendo.WiiU
{
    /// <summary>
    /// Class to manage the encryption and hash state to ensure the minimal amount of hashing and encryption happens per group
    /// </summary>
    internal class WiiUSecurityHashless : IDisposable
    {
        private ImageHeader _header;
        private ICryptoTransform _blockCrypt;

        public WiiUSecurityHashless(ImageHeader header, int contentIdx, bool encrypt) : this(header, PartitionType.Other, contentIdx, null, encrypt)
        {
        }

        public WiiUSecurityHashless(ImageHeader header, PartitionType type, int contentIdx, byte[] titleKey, bool encrypt)
        {
            _header = header;
            Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = WiiUSecurityContext.GetActiveEncryptionKey(type, false, _header.Key, titleKey);
            byte[] iv = new byte[16];
            iv[1] = (byte)contentIdx;
            aes.IV = iv;
            _blockCrypt = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        }

        public int Process(byte[] enc, int encOffset, byte[] dec, int decOffset, int size) => _blockCrypt.TransformBlock(enc, encOffset, size, dec, decOffset);

        public void Dispose()
        {
            try { if (_blockCrypt != null) _blockCrypt.Dispose(); } catch { }
            _blockCrypt = null;
        }
    }

}
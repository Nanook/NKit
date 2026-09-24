using NKitDataStore;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal static class WiiUSecurityContext
    {
        public static SiData BuildSiData(int appIndex, byte[] tmd, byte[] tik, byte[] cert, ImageFormat format, ImageHeader header, ref byte[] currentKey)
        {
            if (tmd == null && tik == null && cert == null)
                return null;

            try
            {
                SiData si = new SiData()
                {
                    AppIndex = appIndex,
                    FileCert = cert,
                    FileTicket = tik,
                    FileTmd = tmd,
                    IsComplete = true
                };

                try { if (tmd != null) si.TmdInfo = new TmdInfo(tmd); } catch { }

                if (tik != null && si.TmdInfo != null)
                {
                    si.IvTitle = tik.Read(WiiUConsts.TicketTitleIdOffset, 0x10);
                    si.IvTitle.WriteUInt64B(8, 0);

                    // For DataStore reconstructions, we may already have the decrypted KeyTitle in this.Key via applyTitleKey().
                    // Prefer it over re-decrypting from the ticket ONLY for folder-style APP images where Key is the Title Key.
                    // For full ISO images, the builder Key property holds the disc key; the title key must be derived using the common key.
                    if (format != ImageFormat.Iso && format != ImageFormat.Bin && currentKey != null && currentKey.Length == 16)
                        si.KeyTitle = (byte[])currentKey.Clone();
                    else
                    {
                        byte[] commonKey = (si.TmdInfo?.IsRetail ?? true) ? (header?.KeyCommon ?? WiiUConsts.KeyCommon) : (header?.KeyCommonDev ?? WiiUConsts.KeyCommonDev);
                        si.KeyTitle = WiiUSecurity.DecryptHashless(tik, null, WiiUConsts.TicketKeyOffset, 0x10, commonKey, si.IvTitle);
                    }
                }

                try { if (si.TmdInfo != null) si.TitleId = si.TmdInfo.TitleId; } catch { }
                try { if (si.KeyTitle != null && currentKey == null) currentKey = si.KeyTitle; } catch { }
                return si;
            }
            catch { return null; }
        }

        public static ContentHeader CreateSyntheticForApp(int contentIndex, long size, bool hasHashes, ImageHeader header)
        {
            return new ContentHeader
            {
                HasHashes = hasHashes,
                HasEncryption = true,
                Index = contentIndex,
                BlockSize = hasHashes ? header.BlockSizeHashed : header.BlockSize,
                BlockFsOffset = hasHashes ? header.HashesSize : 0,
                BlockFsSize = hasHashes ? header.BlockFsSizeHashed : header.BlockSize,
                Size = size,
            };
        }

        public static void ReadContentFiles(IEnumerable<IFsFile> files, Func<long, Stream> openStream, long baseImage, Action<byte[]> setTmd, Action<byte[]> setTik, Action<byte[]> setCert)
        {
            if (files == null)
                return;

            foreach (IFsFile f in files)
            {
                try
                {
                    string name = (f?.Name ?? string.Empty).ToLowerInvariant();
                    Action<byte[]> setter = name.EndsWith(WiiUConsts.ExtTmd) ? setTmd : name.EndsWith(WiiUConsts.ExtTik) ? setTik : name.EndsWith(WiiUConsts.ExtCert) ? setCert : null;
                    if (setter == null)
                        continue;

                    using (Stream fsStream = openStream(baseImage + f.FsOffset))
                        setter(fsStream.ReadBytes((int)f.FsSize));
                }
                catch { }
            }
        }

        public static byte[] GetActiveEncryptionKey(
            PartitionType partitionType,
            AreaType areaType,
            bool isFlatContainer,
            byte[] imageHeaderKey,
            byte[] titleKey)
        {
            if (isFlatContainer || (partitionType == PartitionType.Game &&
                areaType != AreaType.PartitionHeader &&
                areaType != AreaType.PartitionTable &&
                areaType != AreaType.ImageHeader))
            {
                return titleKey ?? imageHeaderKey;
            }
            return imageHeaderKey;
        }

        public static byte[] GetActiveEncryptionKey(
            PartitionType partitionType,
            bool isFlatContainer,
            byte[] imageHeaderKey,
            byte[] titleKey) => GetActiveEncryptionKey(partitionType, AreaType.Other, isFlatContainer, imageHeaderKey, titleKey);
    }
}
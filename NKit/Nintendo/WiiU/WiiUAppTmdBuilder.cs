using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nanook.NKit.Nintendo.WiiU
{
    /// <summary>
    /// Builds encrypted NUS/TmdApp content files (.app, .h3) and an updated TMD.
    ///
    /// Reference: wiiu_decrypt.py (ihaveamac/wiiu-things), WiiUBrew title-metadata wiki.
    ///
    /// Hash-tree structure (per 0x10000-byte chunk):
    ///   [0x000..0x140) H0: 16 × SHA-1 of each 0xFC0-byte sub-block of this chunk's data
    ///   [0x140..0x280) H1: 16 × SHA-1 of H0 tables — one per chunk in the H1 group (16 chunks)
    ///   [0x280..0x3C0) H2: 16 × SHA-1 of H1 tables — one per H1 group (16 groups = 256 chunks)
    ///   [0x3C0..0x400) reserved zeros
    ///   [0x400..0x10000) 0xFC00 bytes plaintext data
    ///
    ///   H1 group  = 16 consecutive chunks; all share the same H1 and H2 tables.
    ///   H2 group  = 16 H1 groups = 256 chunks; all share the same H2 table.
    ///   H3 entry  = SHA-1(H2 table of one H2 group) — H3Count = ceil(chunks / 4096)
    ///   H3 group  = 16 H2 groups = 256 H1 groups = 4096 chunks (H2Full = H0Count³).
    ///
    /// Unused slots in partial groups are filled with SHA-1 of zero-data so the full
    /// 0x140-byte H1/H2 tables are always non-trivially populated (matching retail CDN).
    ///
    /// Hashless IV: 2-byte big-endian content index, padded to 16 bytes with zeros.
    /// TMD hash for hashless = SHA-1(encrypted bytes truncated to fsSize).
    /// </summary>
    internal sealed class WiiUAppTmdBuilder
    {
        public const long ContentSplitSize = 0x1FFF0000L;

        static WiiUAppTmdBuilder() { }

        private readonly SiData _siData;
        private readonly Dictionary<int, byte[]> _contentHashes = new();
        private readonly Dictionary<int, byte[]> _h3Tables = new();

        internal WiiUAppTmdBuilder(SiData siData) => _siData = siData;

        internal byte[] FileTmd    => _siData.FileTmd;
        internal byte[] FileTicket => _siData.FileTicket;
        internal byte[] FileCert   => _siData.FileCert;

        // ── Hashless ─────────────────────────────────────────────────────────────────

        internal byte[] EncryptHashless(int contentIndex, Stream input, long fsSize, Stream output)
        {
            // CDN/NUS hashless content IV = content index as big-endian uint16 in bytes [0..1], rest zeros.
            // This matches jNUSLib's DefaultNUSDataProcessor.readDecryptedContentToStream.
            byte[] iv = new byte[0x10];
            iv[0] = (byte)(contentIndex >> 8);
            iv[1] = (byte)(contentIndex & 0xFF);

            int alignedSize = (int)((fsSize + 15) & ~15L);
            byte[] dec = new byte[alignedSize];
            for (int pos = 0, rem = (int)fsSize; rem > 0;)
            {
                int n = input.Read(dec, pos, rem);
                if (n == 0) break;
                pos += n; rem -= n;
            }

            byte[] enc = WiiUSecurity.EncryptHashless(dec, null, 0, alignedSize, _siData.KeyTitle, iv);
            output.Write(enc, 0, alignedSize);

            // TMD hash for hashless content = SHA-1 of the full AES-aligned decrypted buffer.
            // jNUSLib's processNonHashedStream "fallback" SHA-1 hashes up to c.getEncryptedFileSize()
            // = alignedSize bytes, so we must match that (not just fsSize).
            using SHA1 sha = SHA1.Create();
            byte[] hash = sha.ComputeHash(dec, 0, alignedSize);
            _contentHashes[contentIndex] = hash;
            return hash;
        }

        // ── Hashed ───────────────────────────────────────────────────────────────────

        internal void EncryptHashed(int contentIndex, long fsSize, Stream input,
                                    IReadOnlyList<Stream> appStreams, out byte[] h3Table)
        {
            // Mirrors WipeWiiUStep.updateH2Entries:
            //
            // Per 16-MiB H2 block (H0Count × H1Count = 256 chunks):
            //   Populate(forceDecryptedHashRebuild=true) → WiiUSecurity fills H0 + H1 into dec
            //   Cache the block's dec buffer
            //
            // After H2Count blocks (or at content end):
            //   H2[i] = SHA-1(cachedBlock[i], H1Offset, H1Len)  — one per cached block
            //   Broadcast full H2 table into every sector of every cached block
            //   H3 entry = SHA-1(H2 table)
            //   Per cached block: Populate(rebuild=false) + Encrypt() → output

            int chunksPerH2Block = WiiUConsts.H0Count * WiiUConsts.H1Count;   // 256 chunks = 16 MiB
            int h2BlockSize      = chunksPerH2Block * WiiUConsts.HashedChunkSize;

            long totalChunks  = (fsSize + WiiUConsts.HashedDataSize - 1) / WiiUConsts.HashedDataSize;
            int  totalH2Blocks = (int)((totalChunks + chunksPerH2Block - 1) / chunksPerH2Block);
            int  h3Count       = (totalH2Blocks + WiiUConsts.H2Count - 1) / WiiUConsts.H2Count;
            if (h3Count < 1) h3Count = 1;
            byte[] h3Raw = new byte[h3Count * WiiUConsts.Sha1HashLen];

            ImageHeader   header    = new ImageHeader(null);
            ContentHeader cntHeader = WiiUSecurityContext.CreateSyntheticForApp(contentIndex, fsSize, true, header);
            WiiUSecurity  security  = new WiiUSecurity(header);
            SiData        siData    = new SiData() { KeyTitle = _siData.KeyTitle };

            // enc scratch buffer — same size as one H2 block, reused per Encrypt() call
            byte[] enc = new byte[h2BlockSize];

            // H2 set: up to H2Count cached dec blocks
            var h2Blocks  = new byte[WiiUConsts.H2Count][];
            var h2Sizes   = new long[WiiUConsts.H2Count];
            int h2InSet   = 0;
            int h3Idx     = 0;
            long fsOffset = 0;
            int  appIdx   = 0;
            long appFsBytes = 0;

            using SHA1 sha1 = SHA1.Create();

            for (int bi = 0; bi < totalH2Blocks; bi++)
            {
                long chunksInBlock = Math.Min(chunksPerH2Block, totalChunks - (long)bi * chunksPerH2Block);
                int  blockBytes    = (int)(chunksInBlock * WiiUConsts.HashedChunkSize);

                // ── Read data into dec buffer (hash area zeros, data at +HashSize) ───────
                byte[] dec = new byte[h2BlockSize]; // always full-size; Populate checks enc.Length
                for (int ci = 0; ci < chunksInBlock; ci++)
                {
                    int toRead = (int)Math.Min(WiiUConsts.HashedDataSize, fsSize - fsOffset);
                    for (int got = 0; got < toRead;)
                    {
                        int n = input.Read(dec, ci * WiiUConsts.HashedChunkSize + WiiUConsts.HashSize + got, toRead - got);
                        if (n == 0) break;
                        got += n;
                    }
                    fsOffset += toRead;
                }

                // ── Populate(forceDecryptedHashRebuild=true): fills H0 + H1 into dec ────
                security.Populate(cntHeader, siData, PartitionType.Game, AreaType.FileSystem,
                    enc, dec, blockBytes, false, true, (long)bi * h2BlockSize);

                h2Blocks[h2InSet] = dec;
                h2Sizes[h2InSet]  = blockBytes;
                h2InSet++;

                bool isLastInSet = h2InSet == WiiUConsts.H2Count || bi == totalH2Blocks - 1;
                if (!isLastInSet)
                    continue;

                // ── Build H2 table: H2[i] = SHA-1(cachedBlock[i], H1Offset, H1Len) ──────
                // Written into block 0's H2 area (H2Offset), matching the wipe pattern.
                for (int i = 0; i < h2InSet; i++)
                    Array.Copy(sha1.ComputeHash(h2Blocks[i], WiiUConsts.H1Offset, WiiUConsts.H1Len),
                               0, h2Blocks[0], WiiUConsts.H2Offset + i * WiiUConsts.Sha1HashLen,
                               WiiUConsts.Sha1HashLen);

                // ── Broadcast H2 table into every sector of every cached block ────────────
                for (int i = 0; i < h2InSet; i++)
                    for (int h = 0; h < h2Sizes[i]; h += WiiUConsts.HashedChunkSize)
                        Array.Copy(h2Blocks[0], WiiUConsts.H2Offset,
                                   h2Blocks[i], h + WiiUConsts.H2Offset,
                                   WiiUConsts.H2Len);

                // ── H3 entry = SHA-1(H2 table) ────────────────────────────────────────────
                Array.Copy(sha1.ComputeHash(h2Blocks[0], WiiUConsts.H2Offset, WiiUConsts.H2Len),
                           0, h3Raw, h3Idx * WiiUConsts.Sha1HashLen, WiiUConsts.Sha1HashLen);
                h3Idx++;

                // ── Populate(rebuild=false) + Encrypt() per cached block → output ─────────
                for (int i = 0; i < h2InSet; i++)
                {
                    security.Populate(cntHeader, siData, PartitionType.Game, AreaType.FileSystem,
                        enc, h2Blocks[i], (int)h2Sizes[i], false, false,
                        (long)(bi - h2InSet + 1 + i) * h2BlockSize);
                    security.Encrypt();

                    int chunksOut = (int)(h2Sizes[i] / WiiUConsts.HashedChunkSize);
                    for (int ci = 0; ci < chunksOut; ci++)
                    {
                        if (appFsBytes >= ContentSplitSize) { appIdx++; appFsBytes = 0; }
                        appStreams[appIdx].Write(enc, ci * WiiUConsts.HashedChunkSize, WiiUConsts.HashedChunkSize);
                        appFsBytes += WiiUConsts.HashedDataSize;
                    }
                }

                // Reset for next H3 set
                h2InSet = 0;
                Array.Clear(enc, 0, enc.Length);
            }

            h3Table = h3Raw;
            _h3Tables[contentIndex]      = h3Raw;
            _contentHashes[contentIndex] = sha1.ComputeHash(h3Raw);
        }
        // ── Finalise TMD ──────────────────────────────────────────────────────────────

        internal void FinaliseTmd()
        {
            TmdInfo ti = new TmdInfo(_siData.FileTmd);
            foreach (KeyValuePair<int, byte[]> kv in _contentHashes)
            {
                int off = ti.TmdContentOffset + ti.TmdContentItemLength * kv.Key + 0x10;
                _siData.FileTmd.Write(off, kv.Value, 20);
            }
            using SHA256 sha256 = SHA256.Create();
            foreach (ContentGroup cg in ti.ContentGroups)
                _siData.FileTmd.Write(cg.GroupOffset + 0x4,
                    sha256.ComputeHash(_siData.FileTmd, cg.ContentOffset, cg.ContentSize), 0x20);
            _siData.FileTmd.Write(WiiUConsts.TmdContentInfoHashOffset,
                sha256.ComputeHash(_siData.FileTmd, ti.TmdHeaderSize, ti.TmdContentOffset - ti.TmdHeaderSize), 0x20);
        }

        internal byte[] GetH3Table(int contentIndex)
            => _h3Tables.TryGetValue(contentIndex, out byte[] h3) ? h3 : null;

        public static int SplitCount(long fsSize)
            => (int)((fsSize + ContentSplitSize - 1) / ContentSplitSize);
    }
}

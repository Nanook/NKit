using Nanook.NKit;
using Nanook.NKit.Nintendo.WiiU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace NKit.Tests.Integration
{
    /// <summary>
    /// End-to-end validation of <see cref="WiiUAppTmdBuilder"/> via NKit's own scan pipeline.
    ///
    /// A synthetic AppTmd folder is built from scratch using random content:
    ///   • one hashless .app  (FST-style, content index 0)
    ///   • one hashed   .app  (data-style, content index 1)
    ///   • title.tmd, title.tik, title.cert  (fake-signed)
    ///
    /// The test then scans the output folder with NKit and asserts:
    ///   (a) SignedStatus = FakeSigned  (garbage RSA sig → null-byte exploit detected)
    ///   (b) HashRoot = Valid           (H3 hash root matches TMD content hash)
    ///   (c) HashesValid + IsCreatable  (H0/H1/H2 chain correct in every sector)
    /// </summary>
    [Trait("Area", "Integration")]
    [Trait("Speed", "Slow")]
    public class ConvertFolderAppTmdIntegrationTests
    {
        // Synthetic title ID in the 0005000010xxxxxx game range
        private const ulong TitleId = 0x0005000010ABCDEF;

        [Fact]
        public void ScanPackedAppTmd_HashesValidAndFakeSigned()
        {
            string outDir  = Path.Combine(Path.GetTempPath(), $"nkit_apptmd_{Guid.NewGuid():N}");
            string scanDir = Path.Combine(Path.GetTempPath(), $"nkit_scan_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(scanDir);

            try
            {
                // ── 1. Build a synthetic TMD + ticket + cert ──────────────────────────
                // Two contents:
                //   Index 0 — hashless (AppType.Fst-like), FST header so NKit's probe recognises it
                const int dataPerBlock  = 0xFC00;
                //   Index 1 — hashed, 3 chunks of arbitrary data
                //
                // The title key is all-zeros. NKit's zero-key fallback probe in Image.Setup()
                // will recognise the FST and accept the content without needing a real common key.

                // Hashless content 0: minimal WiiU FST binary starting with "FST\0"
                byte[] hashlessData = buildMinimalFst(contentCount: 2);

                // Hashed content 1: arbitrary data
                byte[] hashedData = new byte[3 * dataPerBlock];
                new Random(7).NextBytes(hashedData);

                // Build TMD
                byte[] tmd = buildTmd(TitleId, new[]
                {
                    (ContentId: 0x00000000L, Index: 0, Hashed: false, Size: (long)hashlessData.Length),
                    (ContentId: 0x00000001L, Index: 1, Hashed: true,  Size: (long)hashedData.Length),
                });

                // All-zeros title key: encrypt with common key so ticket is valid,
                // but the raw content encryption uses zeros directly.
                byte[] zeroTitleKey  = new byte[16]; // all zeros
                byte[] encTitleKey   = TmdInfo.EncryptKey(zeroTitleKey, TitleId, WiiUConsts.KeyCommon);

                SiData siData = new SiData(encTitleKey, tmd, null, null, null);
                siData.Complete(new ImageHeader(null));
                Assert.NotNull(siData.KeyTitle);

                var builder = new WiiUAppTmdBuilder(siData);

                // ── 2. Encrypt and write content files ────────────────────────────────
                // Hashless content (index 0)
                using (var ms = new MemoryStream(hashlessData))
                using (var fs = File.Create(Path.Combine(outDir, "00000000.app")))
                    builder.EncryptHashless(0, ms, hashlessData.Length, fs);

                // Hashed content (index 1)
                var splitStreams = new List<Stream> { File.Create(Path.Combine(outDir, "00000001.app")) };
                try
                {
                    using var ms = new MemoryStream(hashedData);
                    builder.EncryptHashed(1, hashedData.Length, ms, splitStreams, out byte[] h3Table);
                    File.WriteAllBytes(Path.Combine(outDir, "00000001.h3"), h3Table);
                }
                finally
                {
                    foreach (var s in splitStreams) s.Dispose();
                }

                // Finalise TMD and write title files
                builder.FinaliseTmd();
                File.WriteAllBytes(Path.Combine(outDir, "title.tmd"),  siData.FileTmd);
                File.WriteAllBytes(Path.Combine(outDir, "title.tik"),  siData.FileTicket);
                File.WriteAllBytes(Path.Combine(outDir, "title.cert"), siData.FileCert);

                // ── 3. Scan with NKit ─────────────────────────────────────────────────
                var presets = new SystemPresetSettings
                {
                    Task    = TaskType.Scan,
                    V       = Verify.N,
                    ScanOut = scanDir,
                    Out     = scanDir,
                    R       = false,
                    Arc     = true,
                };
                presets.In.Add(Path.Combine(outDir, "title.tmd"));

                AppSettings settings = new AppSettings(presets);

                using var cancel = new System.Threading.CancellationTokenSource();
                using Log log = settings.GetLog((msg, _) => { /* Console.WriteLine(msg); */ });

                var allFiles = SourceFiles.Scan(
                    settings.In, settings.R, settings.Arc,
                    validOnly: false, log: log, cancel: cancel.Token).ToList();

                SourceFile file = allFiles.OrderBy(f => f.Name).FirstOrDefault();
                Assert.True(file != null,
                    $"SourceFiles.Scan returned {allFiles.Count} results. " +
                    $"outDir files: {string.Join(", ", Directory.EnumerateFiles(outDir).Select(Path.GetFileName))}. " +
                    $"Status(es): {string.Join(", ", allFiles.Select(f => f.Status.ToString()))}");
                Assert.Equal(SourceImageType.TmdApp, file.ImageType);

                NKitProcessor processor = new NKitProcessor(settings, file, (_, _) => { });
                NKitTaskResults results = processor.Process(TestContext.Current.CancellationToken);

                Assert.True(string.IsNullOrEmpty(results.ErrorMsg),
                    $"Scan failed: {results.ErrorMsg}");
                Assert.NotNull(results.Scan);

                // ── 4. Assert (a): FakeSigned ─────────────────────────────────────────
                ScanArea fstBlock = results.Scan.Areas
                    .FirstOrDefault(a => a.Type == AreaType.FstBlock);
                Assert.True(fstBlock != null,
                    $"No FstBlock area found. Areas: {string.Join(", ", results.Scan?.Areas.Select(a => a.Type.ToString()) ?? new[]{"null scan"})}. " +
                    $"ErrorMsg: {results.ErrorMsg ?? "(none)"}");

                string signedProp = fstBlock.AreaInfo.Properties["Signed"]?.ToXmlValue() ?? "";
                Assert.Equal("FakeSigned", signedProp);

                // ── 5. Assert (b) + (c): HashRoot=Valid, sections HashesValid+Creatable ─
                var fsAreas = results.Scan.Areas
                    .Where(a => a.Type == AreaType.FileSystem)
                    .ToList();
                Assert.NotEmpty(fsAreas);

                foreach (ScanArea area in fsAreas)
                {
                    bool isHashed = (int)(uint)area.AreaInfo.Properties["HashSize"] != 0;
                    if (!isHashed)
                        continue; // hashless content has no HashRoot property

                    string hashRoot = area.AreaInfo.Properties["HashRoot"]?.ToXmlValue() ?? "";
                    Assert.Equal("Valid", hashRoot);

                    foreach (ScanSection sec in area.Sections)
                    {
                        Assert.True(sec.HashesValid,
                            $"Section at image offset 0x{sec.ImageOffset:X} HashesValid");
                        Assert.True(sec.IsCreatable,
                            $"Section at image offset 0x{sec.ImageOffset:X} IsCreatable");
                    }
                }
            }
            finally
            {
                try { Directory.Delete(outDir,  recursive: true); } catch { }
                try { Directory.Delete(scanDir, recursive: true); } catch { }
            }
        }

        // ── FST builder helper ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal WiiU FST binary with <paramref name="contentCount"/> content entries,
        /// padded to a full 0x8000-byte block. NKit's FstBlock parser reads:
        ///   +0x00  uint32 magic (ignored)
        ///   +0x04  uint32 multiplier (block size shift — we use 0 = 1-byte blocks → offsets in bytes)
        ///   +0x08  uint32 contentCount
        ///   +0x0C  byte   hashDisabled (1 = no hash verification)
        /// Followed by contentCount × 0x20-byte content records (all zeros is fine for a test FST).
        /// The first 4 bytes are "FST\0" so NKit's key-probe passes.
        /// </summary>
        private static byte[] buildMinimalFst(int contentCount)
        {
            const int fstRecLen = 0x20;
            int dataSize = (1 + contentCount) * fstRecLen; // header record + N content records
            int padded   = ((dataSize + 0x7FFF) / 0x8000) * 0x8000;
            byte[] fst   = new byte[Math.Max(padded, 0x8000)];

            // FST magic
            fst[0] = 0x46; fst[1] = 0x53; fst[2] = 0x54; fst[3] = 0x00; // "FST\0"
            // multiplier = 0 (byte-addressed offsets)
            fst.WriteUInt32B(0x04, 0);
            // content count
            fst.WriteUInt32B(0x08, (uint)contentCount);
            // hashDisabled = 1
            fst[0x0C] = 1;
            // Content records at offset (1+i)*0x20 — all zeros is sufficient for the probe
            return fst;
        }

        // ── TMD builder helper ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal WiiU v1 TMD binary with fake-zero RSA signature.
        /// Content hash fields are initialised to zeros; <see cref="WiiUAppTmdBuilder.FinaliseTmd"/>
        /// fills them in after encryption.
        /// </summary>
        private static byte[] buildTmd(
            ulong titleId,
            IEnumerable<(long ContentId, int Index, bool Hashed, long Size)> contents)
        {
            var contentList = contents.ToList();
            int  contentCount   = contentList.Count;
            int  tmdContentOff  = 0xB04;           // WiiU v1 content records start here
            int  contentItemLen = 0x30;            // 48 bytes per content record (WiiU v1)
            int  totalLen       = tmdContentOff + contentCount * contentItemLen;

            byte[] tmd = new byte[totalLen];

            // Signature type 0x00010004 = RSA-2048 SHA-256  (stays at zero → fake-signed)
            tmd.WriteUInt32B(0x000, 0x00010004u);
            // sig bytes [0x004..0x103] left as zeros → null-byte fake signature

            // Issuer at 0x140
            byte[] issuer = System.Text.Encoding.ASCII.GetBytes("Root-CA00000003-CP0000000b");
            Array.Copy(issuer, 0, tmd, 0x140, issuer.Length);

            tmd[0x180] = 1;  // version = 1
            tmd.WriteUInt64B(0x18C, titleId);
            tmd.WriteUInt32B(0x194, 0x00000001);  // title type
            // access rights, title version, content count, boot index
            tmd.WriteUInt16B(0x1DC, 0);       // title version
            tmd.WriteUInt16B(0x1DE, (ushort)contentCount);
            tmd.WriteUInt16B(0x1E0, 0);       // boot index

            // info-hash at 0x1E4 — left zeros for now; FinaliseTmd will patch it
            // ContentInfo group at 0x204: first group covers all contents
            tmd.WriteUInt16B(0x204, 0);                            // offset = 0
            tmd.WriteUInt16B(0x206, (ushort)contentCount);         // count
            // group SHA-256 hash at 0x208 — left zeros; FinaliseTmd patches

            // Content records
            for (int i = 0; i < contentList.Count; i++)
            {
                var (contentId, index, hashed, size) = contentList[i];
                int off = tmdContentOff + i * contentItemLen;
                tmd.WriteUInt32B(off + 0x00, (uint)contentId);
                tmd.WriteUInt16B(off + 0x04, (ushort)index);
                // type: 0x0001 = encrypted; 0x0003 = encrypted+hashed
                tmd.WriteUInt16B(off + 0x06, (ushort)(hashed ? 0x0003 : 0x0001));
                tmd.WriteUInt64B(off + 0x08, (ulong)size);
                // hash at off+0x10 — left zeros; FinaliseTmd patches
            }

            return tmd;
        }
    }
}

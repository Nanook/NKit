using Nanook.NKit.Nintendo.WiiU;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Converts a WUA (.wua ZArchive) or Loadiine folder (code/content/meta on disk)
    /// into an encrypted NUS/TmdApp installable title:
    ///
    ///   title.cert  — certificate chain (CA, CP, XS)
    ///   title.tik   — ticket containing the encrypted title key
    ///   title.tmd   — title metadata with per-content SHA-1 hashes
    ///   xxxxxxxx.app — AES-128-CBC encrypted content file(s)
    ///   xxxxxxxx.h3  — H3 hash table for each hashed content
    ///
    /// The source already contains a signed tmd/tik/cert.  If any are missing they are
    /// synthesised (fake-signed, same as NUSPacker) using the existing SiData helpers.
    ///
    /// File bytes are accumulated per-section in <see cref="Process"/> (one
    /// <see cref="AreaType.Other"/> area per file), then encrypted and written to disk
    /// in <see cref="ProcessResults"/> once all sections have been received.
    /// </summary>
    internal class ConvertFolderAppTmdStep : StepBase, IStep
    {
        private static readonly System.Text.RegularExpressions.Regex _titleFolderRegex =
            new System.Text.RegularExpressions.Regex(@"^[0-9a-fA-F]{16}_v[0-9]+$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private IStepContext _context;
        private readonly string _outName;
        private int _appFiles;

        // Accumulated file data from sections: relPath → MemoryStream of raw bytes
        private readonly Dictionary<string, MemoryStream> _fileData = new();
        // Ordered list of relative paths (in section arrival order)
        private readonly List<string> _filePaths = new();

        internal override bool ContractIsLossy    => true;  // no original disc structure preserved
        internal override bool ContractIsExpand   => false;
        internal override bool ContractIsFix      => false;
        internal override bool ContractReqPatch   => false;
        internal override bool ContractReqChk     => false;
        internal override bool ContractFullScan   => true;
        internal override OutputType ContractOutputType => OutputType.FolderIndex;
        internal override bool ContractCanCrc     => false;
        internal override bool ContractCanHash    => false;
        internal override string ComponentTag     => LogScopes.StepConvertFolderAppTmd;

        public override string ProposedName() => _outName;

        internal ConvertFolderAppTmdStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _context   = context;
            _appFiles  = 0;
            _fileData.Clear();
            _filePaths.Clear();
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.Type != AreaType.Other)
                return;

            string relPath = section.AreaInfo?.Properties?["FileName"]?.ToXmlValue() ?? "";
            if (string.IsNullOrEmpty(relPath))
                return;

            if (!_fileData.TryGetValue(relPath, out MemoryStream ms))
            {
                ms = new MemoryStream();
                _fileData[relPath] = ms;
                _filePaths.Add(relPath);
            }

            ms.Seek(section.AreaOffset, SeekOrigin.Begin);
            ms.Write(section.Decrypted, 0, (int)section.Size);
        }

        public void Patched(ISection section) { }

        public override void ProcessResults()
        {
            // Build the virtual file dictionary from accumulated section data
            var files = new Dictionary<string, (long Size, Func<Stream> Open)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string relPath in _filePaths)
            {
                if (!_fileData.TryGetValue(relPath, out MemoryStream ms)) continue;
                byte[] data = ms.ToArray();
                files[relPath] = (data.Length, () => new MemoryStream(data, writable: false));
            }

            string outPath   = _context.WritePath;
            Directory.CreateDirectory(outPath);

            convertFiles(files, outPath);

            base.ProcessingComplete();
            Context.Result.ProgressSummary = $"Wrote {_appFiles} app file{_appFiles.s()}";
            // Assign scan directly — base.ProcessResults() validates OutStream has an index
            // file which doesn't apply here (we write directly to disk, not via OutStream).
            if (Context.StepInfo.FullScan)
                Context.Result.Scan = Context.Scan;
        }
        // ── Core conversion ──────────────────────────────────────────────────────────

        /// <summary>
        /// Given a flat dictionary of { relPath → (size, opener) } representing the
        /// decrypted game files, locates the tmd/tik/cert, builds SiData, then
        /// encrypts every .app content and writes the installable tree to
        /// <paramref name="outPath"/>.
        /// </summary>
        private void convertFiles(
            Dictionary<string, (long Size, Func<Stream> Open)> files,
            string outPath)
        {
            // ── 2. Read tmd / tik / cert ─────────────────────────────────────────────
            // Support multiple naming conventions:
            //   Standard NUS:   title.tmd / title.tik / title.cert
            //   CDN flat:       tmd.0 / tik / cetk  (extensionless, or various names)
            //   Loadiine-style: content/title.tmd
            // If no TMD is present (raw Loadiine / WUA disc extract without CDN files)
            // synthesise a minimal stub from the title ID in meta/meta.xml or code/app.xml.
            byte[] tmd  = readEntry(files, "content/title.tmd")
                       ?? readEntry(files, "title.tmd")
                       ?? readEntryStartsWith(files, "tmd")   // tmd.0, tmd.1 etc.
                       ?? synthesiseTmdFromXml(files);

            if (tmd == null)
                throw new HandledException("ConvertFolderAppTmd: no tmd found and could not determine title ID from meta/meta.xml or code/app.xml");
            byte[] tik  = readEntry(files, "content/title.tik")
                       ?? readEntry(files, "title.tik")
                       ?? readEntry(files, "tik")
                       ?? readEntry(files, "cetk");
            byte[] cert = readEntry(files, "content/title.cert")
                       ?? readEntry(files, "title.cert")
                       ?? readEntry(files, "cert");

            // Locate the first hashless content — its app file provides the FST bytes
            // needed for key derivation when a ticket is absent.
            TmdInfo tmdInfo = new TmdInfo(tmd);
            Content fstContent = tmdInfo.Content.FirstOrDefault(c => !c.Type.HasFlag(AppContentType.Hashed))
                              ?? tmdInfo.Content.FirstOrDefault();

            // App files may use .app extension or be extensionless (CDN flat layout).
            byte[] fstBytes = fstContent == null ? null
                           : (readEntry(files, $"content/{fstContent.ContentId:x8}.app")
                           ?? readEntry(files, $"{fstContent.ContentId:x8}.app")
                           ?? readEntry(files, $"{fstContent.ContentId:x8}"));

            // Derive the title key: use a zero raw key so the encrypted form in the ticket
            // is deterministic and the console can always decrypt the .app files.
            // This replaces the PBKDF2-based brute-force approach — a zero raw key encrypted
            // with the common key is simpler and equally valid for fake-signed content.
            byte[] key = TmdInfo.EncryptKey(new byte[16], tmdInfo.TitleId, WiiUConsts.KeyCommon);

            // SiData.setup() generates a ticket+cert when absent, using the pre-generated key.
            SiData siData = new SiData(key, tmd, tik, cert, fstBytes);
            siData.Complete(new ImageHeader(null));

            if (siData.KeyTitle == null)
                throw new HandledException(
                    $"ConvertFolderAppTmd: could not determine title key for {tmdInfo.TitleId:X16}");

            var builder = new WiiUAppTmdBuilder(siData);

            // ── Check source type ─────────────────────────────────────────────────────
            if (tmdInfo.Content.Length == 0)
            {
                // Disc-extract Loadiine/WUA: no pre-built .app files.
                // Pack each top-level directory (code/, content/, meta/) as a hashed content
                // and build a WiiU FST binary as the hashless content 0.
                packFromDiscExtract(files, tmdInfo, outPath);
                return;
            }

            // ── 3. Output cert / tik / tmd stubs (tmd re-emitted after finalise) ─────
            string outName = tmdInfo.TitleId.ToString("X16");
            // Record the title-ID-based name so the framework renames WritePath to it.
            _context.Result.FinalName = $"{outName}_v{tmdInfo.TitleVersion}";

            writeFile(outPath, "title.cert", siData.FileCert);
            writeFile(outPath, "title.tik",  siData.FileTicket);
            // tmd written last (after hash finalisation)

            // ── 4. Encrypt each content ──────────────────────────────────────────────
            foreach (Content content in tmdInfo.Content.OrderBy(c => c.Index))
            {
                string appName    = $"{content.ContentId:x8}.app";
                string srcRelPath = $"content/{appName}";
                if (!files.ContainsKey(srcRelPath))
                    srcRelPath = appName; // flat layout fallback

                bool hashed = content.Type.HasFlag(AppContentType.Hashed);

                if (hashed)
                {
                    encryptHashedContent(files, srcRelPath, content, builder, outPath);
                }
                else
                {
                    // Try .app extension, then extensionless (CDN flat layout)
                    if (!files.ContainsKey(srcRelPath))
                        srcRelPath = $"{content.ContentId:x8}";
                    if (!files.ContainsKey(srcRelPath))
                        srcRelPath = $"content/{content.ContentId:x8}";

                    if (!files.TryGetValue(srcRelPath, out var entry))
                        throw new HandledException(
                            $"ConvertFolderAppTmd: missing source app '{srcRelPath}'");

                    string destPath = Path.Combine(outPath, appName);
                    using Stream input  = entry.Open();
                    using Stream output = File.Create(destPath);
                    builder.EncryptHashless(content.Index, input, entry.Size, output);
                    _appFiles++;
                }
            }

            // ── 5. Finalise TMD and write H3 files ───────────────────────────────────
            builder.FinaliseTmd();

            foreach (Content content in tmdInfo.Content.Where(c => c.Type.HasFlag(AppContentType.Hashed)))
            {
                byte[] h3 = builder.GetH3Table(content.Index);
                if (h3 != null)
                    writeFile(outPath, $"{content.ContentId:x8}.h3", h3);
            }

            writeFile(outPath, "title.tmd", siData.FileTmd);
        }

        // ── Disc-extract Loadiine / WUA packing ──────────────────────────────────────

        /// <summary>
        /// Packs a disc-extract Loadiine/WUA source (raw game files, no pre-built .app files)
        /// into NUS AppTmd format.
        ///
        /// Content layout:
        ///   Index 0 — FST binary (hashless, AppType.Fst)
        ///   Index 1 — code/app.xml (hashless, AppType.Code) — OS reads at boot
        ///   Index 2 — code/cos.xml (hashless, AppType.Code) — OS reads at boot
        ///   Index 3 — meta/meta.xml (hashed, AppType.Files) — metadata shown in menu
        ///   Index 4 — meta/ boot visuals + icon (hashed, AppType.Files)
        ///   Index 5 — meta/bootMovie.h264 (hashed, AppType.Files)
        ///   Index 6 — meta/bootLogoTex.tga (hashed, AppType.Files)
        ///   Index 7 — meta/Manual.bfma (hashed, AppType.Files)
        ///   Index 8 — RPX executable (hashless, AppType.Code)
        ///   Index 9 — remaining content/ + other code/ files (hashed, AppType.Files)
        ///
        /// This matches the per-file content structure produced by disc→AppTmd conversion
        /// (ConvertWiiUAppTmdStep), which installs and runs correctly on hardware.
        /// </summary>
        private void packFromDiscExtract(
            Dictionary<string, (long Size, Func<Stream> Open)> files,
            TmdInfo tmdInfo,
            string outPath)
        {
            ulong titleId = tmdInfo.TitleId;

            // ── Derive title key ─────────────────────────────────────────────────────
            // Use a zero raw key — the encrypted form is stored in the generated ticket,
            // so the console always has what it needs to decrypt the .app files.
            byte[] encKey = TmdInfo.EncryptKey(new byte[16], titleId, WiiUConsts.KeyCommon);

            const int GRP_CODE  = 0x0000;
            const int GRP_FILES = 0x0400;
            // Content groupId for content/ data — lower 16 bits of the title type word
            int contentGroupId = (int)((titleId >> 8) & 0xFFFF);

            // ── Partition files by folder ─────────────────────────────────────────────
            // code/ non-RPX: hashless, one content each (app.xml, cos.xml, etc.)
            var codeFiles = files.Keys
                .Where(k => k.StartsWith("code/", StringComparison.OrdinalIgnoreCase)
                         && !k.EndsWith(".rpx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // code/ RPX files: hashless, one content each (usually exactly one)
            var rpxFiles = files.Keys
                .Where(k => k.StartsWith("code/", StringComparison.OrdinalIgnoreCase)
                         && k.EndsWith(".rpx", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(k => files[k].Size)  // largest first (primary RPX)
                .ToList();

            // meta/ files: hashed, one content each, sorted by name
            var metaFiles = files.Keys
                .Where(k => k.StartsWith("meta/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // content/ and everything else: single hashed content
            var contentFiles = files.Keys
                .Where(k => !k.StartsWith("code/", StringComparison.OrdinalIgnoreCase)
                         && !k.StartsWith("meta/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ── Assign content indices ────────────────────────────────────────────────
            // [0] = FST
            // [1..codeFiles.Count] = hashless code files (non-RPX)
            // [codeFiles.Count+1 .. +rpxFiles.Count] = hashless RPX files
            // [codeFiles.Count+rpxFiles.Count+1 .. +metaFiles.Count] = hashed meta files
            // [last] = single hashed content/ content
            int idx = 0;

            // ── Build FST ────────────────────────────────────────────────────────────
            var fstBuilder = new WiiUFstBuilder();
            fstBuilder.AddContent(idx++, AppType.Fst, 0, 0, 0);

            // code/ non-RPX (hashless)
            var codeContents = new List<(int Idx, string Path, long Size)>();
            foreach (var path in codeFiles)
            {
                long sz = files[path].Size;
                fstBuilder.AddContent(idx, AppType.Code, sz, 0, GRP_CODE);
                fstBuilder.AddFile(path, 0, sz, idx);
                codeContents.Add((idx, path, sz));
                idx++;
            }

            // code/ RPX (hashless)
            var rpxContents = new List<(int Idx, string Path, long Size)>();
            foreach (var path in rpxFiles)
            {
                long sz = files[path].Size;
                fstBuilder.AddContent(idx, AppType.Code, sz, 0, GRP_CODE);
                fstBuilder.AddFile(path, 0, sz, idx);
                rpxContents.Add((idx, path, sz));
                idx++;
            }

            // meta/ files — each gets its own hashed content; files aligned to 32 bytes within content
            const long fsAlign = 32L; // FST multiplier=32 → offsets stored as byteOffset/32
            var metaContents = new List<(int Idx, string Path, long Size)>();
            foreach (var path in metaFiles)
            {
                long sz = files[path].Size;
                fstBuilder.AddContent(idx, AppType.Files, sz, 0, GRP_FILES);
                fstBuilder.AddFile(path, 0, sz, idx);  // single file per content, starts at offset 0
                metaContents.Add((idx, path, sz));
                idx++;
            }

            // content/ — all files in one hashed content, 32-byte aligned
            int contentIdx = idx++;
            long cOff = 0;
            var contentAligned = new List<(string Path, long AlignedOff, long Size)>();
            fstBuilder.AddContent(contentIdx, AppType.Files, 0, titleId, contentGroupId);
            foreach (var path in contentFiles)
            {
                long sz    = files[path].Size;
                long aOff  = ((cOff + fsAlign - 1) / fsAlign) * fsAlign;
                fstBuilder.AddFile(path, aOff, sz, contentIdx);
                contentAligned.Add((path, aOff, sz));
                cOff = aOff + sz;
            }
            long contentTotalSz = cOff;
            fstBuilder.SetFsSize(contentIdx, contentTotalSz);

            // ── Size helpers ──────────────────────────────────────────────────────────
            static long hashless(long n) => n > 0 ? ((n + 0x7FFF) / 0x8000) * 0x8000 : 0;
            static long hashedSz(long n) => n > 0 ? ((n + 0xFC00 - 1) / 0xFC00) * 0x10000 : 0;

            // ── FST first pass — get size for content[0] rawSz ───────────────────────
            byte[] fstBytes = fstBuilder.ToArray(32);

            fstBuilder.SetEncryptedSize(0, hashless(fstBytes.Length));
            foreach (var (i, _, sz) in codeContents)
                fstBuilder.SetEncryptedSize(i, Math.Max(hashless(sz), 0x8000));
            foreach (var (i, _, sz) in rpxContents)
                fstBuilder.SetEncryptedSize(i, Math.Max(hashless(sz), 0x8000));
            foreach (var (i, _, sz) in metaContents)
                fstBuilder.SetEncryptedSize(i, hashedSz(sz));
            fstBuilder.SetEncryptedSize(contentIdx, hashedSz(contentTotalSz));

            // Second pass bakes rawSz values into the FST binary
            byte[] fstBytesWithSizes = fstBuilder.ToArray(32);

            // ── Build TMD ────────────────────────────────────────────────────────────
            int totalContents = idx; // FST + code + rpx + meta + content
            byte[] newTmd = TmdInfo.SynthesiseTmd(titleId, tmdInfo.TitleVersion, totalContents,
                sysVersion: tmdInfo.SysVersion != 0 ? (ulong)tmdInfo.SysVersion : 0x000500101000400AUL,
                groupId:    tmdInfo.GroupId    != 0 ? (ushort)tmdInfo.GroupId
                                                    : (ushort)((titleId >> 8) & 0xFFFF));

            // content[0] FST — hashless
            TmdInfo.WriteContentRecord(newTmd, 0, 0, 0, 0x2001, hashless(fstBytesWithSizes.Length));
            // code non-RPX — hashless
            foreach (var (i, _, sz) in codeContents)
                TmdInfo.WriteContentRecord(newTmd, i, (uint)i, i, 0x2001, hashless(sz));
            // RPX — hashless
            foreach (var (i, _, sz) in rpxContents)
                TmdInfo.WriteContentRecord(newTmd, i, (uint)i, i, 0x2001, hashless(sz));
            // meta — hashed
            foreach (var (i, _, sz) in metaContents)
                TmdInfo.WriteContentRecord(newTmd, i, (uint)i, i, 0x2003, hashedSz(sz));
            // content/ — hashed
            TmdInfo.WriteContentRecord(newTmd, contentIdx, (uint)contentIdx, contentIdx, 0x2003, hashedSz(contentTotalSz));

            SiData newSiData = new SiData(encKey, newTmd, null, null, null);
            newSiData.Complete(new ImageHeader(null));
            var newBuilder = new WiiUAppTmdBuilder(newSiData);

            // ── Write FST (content 0) ─────────────────────────────────────────────────
            long fstPadded = hashless(fstBytesWithSizes.Length);
            using (var fstMs = new MemoryStream((int)fstPadded))
            {
                fstMs.Write(fstBytesWithSizes, 0, fstBytesWithSizes.Length);
                fstMs.Position = 0;
                using var fstOut = File.Create(Path.Combine(outPath, "00000000.app"));
                newBuilder.EncryptHashless(0, fstMs, fstPadded, fstOut);
            }
            int appCount = 1;

            // ── Write hashless code contents ──────────────────────────────────────────
            void writeHashless(int i, string path, long sz) {
                if (sz == 0) return;
                using var inS  = files[path].Open();
                long padded    = Math.Max(hashless(sz), 0x8000);
                using var ms2  = new MemoryStream((int)padded);
                byte[] buf     = new byte[sz]; inS.Read(buf, 0, (int)sz); ms2.Write(buf, 0, (int)sz);
                ms2.Position   = 0;
                using var cOut = File.Create(Path.Combine(outPath, $"{i:x8}.app"));
                newBuilder.EncryptHashless(i, ms2, padded, cOut);
                appCount++;
            }

            foreach (var (i, path, sz) in codeContents) writeHashless(i, path, sz);
            foreach (var (i, path, sz) in rpxContents)  writeHashless(i, path, sz);

            // ── Write hashed meta contents (one file each) ────────────────────────────
            foreach (var (i, path, sz) in metaContents)
            {
                if (sz == 0 || !files.ContainsKey(path)) continue;
                var items = new List<(string, long, long)> { (path, 0L, sz) };
                writeHashedAligned(i, items, sz);
            }

            // ── Write single hashed content/ content ──────────────────────────────────
            writeHashedAligned(contentIdx, contentAligned, contentTotalSz);

            // ── Hashed writer (shared by meta and content/) ───────────────────────────
            void writeHashedAligned(int i, IReadOnlyList<(string Path, long AlignedOff, long Size)> items, long totalSz)
            {
                if (totalSz == 0 || items.Count == 0) return;
                var streams = new List<Lazy<Stream>>();
                long cursor = 0;
                foreach (var (path, off, sz) in items) {
                    if (!files.ContainsKey(path)) continue;
                    long gap = off - cursor;
                    if (gap > 0) { long g = gap; streams.Add(new Lazy<Stream>(() => new ZeroStream(g))); }
                    streams.Add(new Lazy<Stream>(() => files[path].Open()));
                    cursor = off + sz;
                }
                if (cursor < totalSz) { long tail = totalSz - cursor; streams.Add(new Lazy<Stream>(() => new ZeroStream(tail))); }
                using var concat = new ConcatStream(streams, totalSz);
                int splits = WiiUAppTmdBuilder.SplitCount(totalSz);
                var outStreams = new List<Stream>();
                try {
                    for (int s = 0; s < splits; s++) {
                        outStreams.Add(File.Create(Path.Combine(outPath, s == 0 ? $"{i:x8}.app" : $"{i:x8}.app.{s}")));
                        appCount++;
                    }
                    newBuilder.EncryptHashed(i, totalSz, concat, outStreams, out byte[] h3);
                    File.WriteAllBytes(Path.Combine(outPath, $"{i:x8}.h3"), h3);
                } finally { foreach (var s in outStreams) try { s.Dispose(); } catch { } }
            }

            // ── Finalise ─────────────────────────────────────────────────────────────
            newBuilder.FinaliseTmd();
            // Use the source image name (WUA/Loadiine filename) as the output folder name
            // so the result is identifiable. Fall back to titleId if no source name is available.
            _context.Result.FinalName = !string.IsNullOrEmpty(_outName) ? _outName
                                                                         : $"{titleId:X16}_v{tmdInfo.TitleVersion}";
            writeFile(outPath, "title.cert", newSiData.FileCert);
            writeFile(outPath, "title.tik",  newSiData.FileTicket);
            writeFile(outPath, "title.tmd",  newSiData.FileTmd);
            _appFiles = appCount;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to build a minimal TMD stub from title metadata in
        /// <c>meta/meta.xml</c> or <c>code/app.xml</c>.  Both files use a
        /// <c>&lt;title_id&gt;</c> element containing a 16-hex string.
        /// Returns <c>null</c> if neither file is present or parseable.
        /// </summary>
        private static byte[] synthesiseTmdFromXml(
            Dictionary<string, (long Size, Func<Stream> Open)> files)
        {
            ulong titleId = 0;
            int   titleVersion = 0;

            foreach (string xmlPath in new[] { "meta/meta.xml", "code/app.xml" })
            {
                byte[] data = readEntry(files, xmlPath);
                if (data == null) continue;
                try
                {
                    using var ms = new MemoryStream(data);
                    XDocument doc = XDocument.Load(ms);
                    string tidStr = doc.Root?.Element("title_id")?.Value?.Trim();
                    if (tidStr != null && ulong.TryParse(tidStr,
                            System.Globalization.NumberStyles.HexNumber, null, out ulong id))
                    {
                        titleId = id;
                        // Also try to read title_version from the same file
                        string verStr = doc.Root?.Element("title_version")?.Value?.Trim();
                        if (verStr != null && int.TryParse(verStr, out int ver))
                            titleVersion = ver;
                        break;
                    }
                }
                catch { }
            }

            if (titleId == 0)
                return null;

            return TmdInfo.SynthesiseTmd(titleId, titleVersion);
        }

        private void encryptHashedContent(
            Dictionary<string, (long Size, Func<Stream> Open)> files,
            string srcRelPath,
            Content content,
            WiiUAppTmdBuilder builder,
            string outPath)
        {
            // Also try extensionless CDN flat layout
            if (!files.ContainsKey(srcRelPath))
                srcRelPath = $"{content.ContentId:x8}";
            if (!files.ContainsKey(srcRelPath))
                srcRelPath = $"content/{content.ContentId:x8}";

            if (!files.TryGetValue(srcRelPath, out var entry))
                throw new HandledException(
                    $"ConvertFolderAppTmd: missing source app '{srcRelPath}'");

            long fsSize    = entry.Size;
            int splitCount = WiiUAppTmdBuilder.SplitCount(fsSize);

            // Open all output split streams up front
            var outStreams = new List<FileStream>(splitCount);
            try
            {
                for (int i = 0; i < splitCount; i++)
                {
                    // Multi-split naming follows NUSPacker: 00000001.app, 00000001.app.1, …
                    string splitName = i == 0
                        ? $"{content.ContentId:x8}.app"
                        : $"{content.ContentId:x8}.app.{i}";
                    outStreams.Add(File.Create(Path.Combine(outPath, splitName)));
                    _appFiles++;
                }

                using Stream input = entry.Open();
                builder.EncryptHashed(content.Index, fsSize, input, outStreams, out _);
            }
            finally
            {
                foreach (FileStream s in outStreams)
                    try { s.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Renames the output folder to <c>{titleId}_{version}</c> if it doesn't already
        /// match, creating the new path if necessary, and returns the resolved path.
        /// </summary>
        private static byte[] readEntry(
            Dictionary<string, (long Size, Func<Stream> Open)> files,
            string relPath)
        {
            if (!files.TryGetValue(relPath, out var entry))
                return null;
            using Stream s = entry.Open();
            byte[] buf = new byte[entry.Size];
            int read = 0, remaining = (int)entry.Size;
            while (remaining > 0)
            {
                int n = s.Read(buf, read, remaining);
                if (n == 0) break;
                read += n; remaining -= n;
            }
            return buf;
        }

        /// <summary>
        /// Returns the bytes of the first file whose key starts with <paramref name="prefix"/>
        /// (case-insensitive). Used to find CDN files like "tmd.0", "tmd.1".
        /// </summary>
        private static byte[] readEntryStartsWith(
            Dictionary<string, (long Size, Func<Stream> Open)> files,
            string prefix)
        {
            string key = files.Keys
                .FirstOrDefault(k => Path.GetFileName(k).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                  && !Path.GetFileName(k).EndsWith(".h3", StringComparison.OrdinalIgnoreCase));
            return key == null ? null : readEntry(files, key);
        }

        private static void writeFile(string dir, string name, byte[] data)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        // ── ConcatStream ─────────────────────────────────────────────────────────────

        /// <summary>A read-only stream of <paramref name="length"/> zero bytes.</summary>
        private sealed class ZeroStream : Stream
        {
            private long _remaining;
            public ZeroStream(long length) { _remaining = length; }
            public override bool CanRead  => true;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin g) => throw new NotSupportedException();
            public override void SetLength(long v)           => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Min(count, _remaining);
                if (n <= 0) return 0;
                Array.Clear(buffer, offset, n);
                _remaining -= n;
                return n;
            }
        }

        /// <summary>Concatenates multiple lazily-opened streams into a single readable stream.</summary>
        private sealed class ConcatStream : Stream
        {
            private readonly List<Lazy<Stream>> _streams;
            private readonly long _length;
            private int _idx;
            private Stream _current;
            private long _position;

            public ConcatStream(List<Lazy<Stream>> streams, long length)
            {
                _streams = streams; _length = length;
                _idx = 0;
                _current = _streams.Count > 0 ? _streams[0].Value : Stream.Null;
            }

            public override bool CanRead  => true;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => _length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int total = 0;
                while (count > 0 && _idx < _streams.Count)
                {
                    int n = _current.Read(buffer, offset + total, count);
                    if (n == 0)
                    {
                        _current.Dispose();
                        _idx++;
                        if (_idx < _streams.Count)
                            _current = _streams[_idx].Value;
                        else break;
                    }
                    else { total += n; count -= n; _position += n; }
                }
                return total;
            }

            public override void Flush() { }
            public override long Seek(long o, SeekOrigin g) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) try { _current?.Dispose(); } catch { }
                base.Dispose(disposing);
            }
        }
    }
}

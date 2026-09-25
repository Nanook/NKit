using Nanook.NKit.Dats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nanook.NKit
{
    internal class NKitVerify
    {
        private DatManager _datManager;
        private ILogScope _log;

        public string VerifyType { get; internal set; }

        public NKitVerify(DatManager datManager, ILogScope log = null)
        {
            _datManager = datManager;
            _log = log;
        }

        // Scoped [Out] [Verify] logger, or null when Detail is off / no log — lets every decision
        // point cheaply guard with `if (vlog != null)` before building any string.
        private ILogScope verifyLog()
        {
            ILogScope s = _log?.ScopeFor(LogScopes.Out);
            return (s != null && s.IsEnabled(LogLevel.Detail)) ? s : null;
        }

        // Every verify line leads with the STEP being evaluated (e.g. "Verify-Image", "Fix-WiiGc")
        // so, in a multi-step chain where Verify runs once per step, each line is attributable to
        // the step that produced it — one step reporting "no checksums" and another reporting a
        // compare is then unambiguous.
        private static void vLog(ILogScope scope, string subject, string message)
        {
            scope?.Log(LogLevel.Detail,
                LogScopes.Tag(LogScopes.Verify) + (string.IsNullOrEmpty(subject) ? "" : subject + ": ") + message);
        }

        public void Initialise(IEnumerable<IStepInfo> stepInfos, bool hasDats)
        {
            ILogScope vlog = verifyLog();
            IStepInfo si = find(stepInfos, null, a => a.VerifyMethod != VerifyMethod.NoVerify);

            if (si == null)
            {
                this.VerifyType = VerifyMethod.NoVerify.ToString();
                if (vlog != null)
                    vLog(vlog, "-", $"init: no verifying step in chain -> {this.VerifyType}");
            }
            else
            {
                IParts parts = (si.VerifyMethod == VerifyMethod.InChecksums || si.VerifyMethod == VerifyMethod.DataStore) ? si.SrcParts :
                                (si.VerifyMethod == VerifyMethod.DatMatch ? si.DatMatch :
                                null);

                if (vlog != null)
                    vLog(vlog, si.Name, $"init: method {si.VerifyMethod} hasDats {(hasDats ? "y" : "n")}"
                        + $" srcParts {parts?.Length.ToString() ?? "-"}"
                        + $" srcHashes [{describeChecksums(parts)}]"
                        + $" datMatch {(si.DatMatch != null ? "y" : "n")}");

                si.VerifyChecksums = RequiredChecksums(hasDats, parts, si, out string chkString, vlog);

                this.VerifyType = chkString;
                if (vlog != null)
                    vLog(vlog, si.Name, $"init: selected checksums [{string.Join("+", si.VerifyChecksums.Select(a => a.ToString()))}] -> VerifyType '{chkString}'");
            }
        }

        // Compact "Crc32(4),XxHash(8)" style description of the checksums present on the first part,
        // for logging what inputs a decision saw. Null-safe and Detail-only (never on a hot path).
        private static string describeChecksums(IParts parts)
        {
            Checksums c = (parts != null && parts.Length > 0) ? parts[0].Checksums : null;
            if (c == null)
                return "";
            List<string> present = new List<string>();
            foreach (ChecksumType t in new[] { ChecksumType.Crc32, ChecksumType.Md5, ChecksumType.Sha1, ChecksumType.XxHash })
                if (c.Exists(t))
                    present.Add(t.ToString());
            return string.Join(",", present);
        }

        // Render the actual values of the compared checksum types across all parts, e.g.
        // "{Crc32=9A2C5CC5,XxHash=1C41F7E359B73D2F}" — Detail-only diagnostics of what a compare saw.
        private static string describeValues(IParts parts, ChecksumType[] types)
        {
            if (parts == null || types == null)
                return "(none)";
            List<string> partStrs = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                Checksums c = parts[i].Checksums;
                List<string> kv = new List<string>();
                foreach (ChecksumType t in types)
                    kv.Add($"{t}={(c != null && c.Exists(t) ? c[t].ToHexString() : "-")}");
                partStrs.Add($"[{parts[i].Size}:{string.Join(",", kv)}]");
            }
            return string.Join(" ", partStrs);
        }

        internal static ChecksumType[] RequiredChecksums(bool hasDats, IParts srcParts, IStepInfo si, out string checksumString, ILogScope vlog = null)
        {
            List<ChecksumType> ret = new List<ChecksumType>();
            StringBuilder chkString = new StringBuilder();
            bool hasCrc;
            ChecksumType? chk;
            switch (si.VerifyMethod)
            {
                case VerifyMethod.InChecksums:
                    chk = srcParts.GetHashType(true, out hasCrc);
                    if (chk != null)
                    {
                        ret.Add(chk.Value);
                        chkString.Append(chk.ToString());
                    }
                    if (hasCrc)
                        ret.Add(ChecksumType.Crc32);
                    if (vlog != null)
                        vLog(vlog, si.Name, $"req[InChecksums]: srcPreferredHash {chk?.ToString() ?? "none"} srcHasCrc {(hasCrc ? "y" : "n")}");
                    break;
                case VerifyMethod.DataStore:
                    ret.Add(ChecksumType.XxHash);
                    chkString.Append(ChecksumType.XxHash.ToString());
                    ret.Add(ChecksumType.Crc32);
                    if (vlog != null)
                        vLog(vlog, si.Name, $"req[DataStore]: fixed XxHash+Crc32");
                    break;
                case VerifyMethod.DatMatch:
                case VerifyMethod.DatLookup:
                    chk = si.DatMatch?.GetHashType(false, out _);
                    bool defaulted = false;
                    if (chk != null || hasDats)
                    {
                        if (chk == null)
                        {
                            chk = ChecksumType.Md5;
                            defaulted = true;
                        }
                        ret.Add(chk.Value);
                        chkString.Append(chk.ToString());
                    }
                    ret.Add(ChecksumType.Crc32);
                    if (vlog != null)
                        vLog(vlog, si.Name, $"req[{si.VerifyMethod}]: datMatchHash {chk?.ToString() ?? "none"}{(defaulted ? " (defaulted, hasDats)" : "")} hasDats {(hasDats ? "y" : "n")}");
                    break;
                case VerifyMethod.InScanCompare:
                case VerifyMethod.ScanCompare:
                    ret.Add(ChecksumType.Crc32);
                    if (vlog != null)
                        vLog(vlog, si.Name, $"req[{si.VerifyMethod}]: Crc32 (scan compare)");
                    break;
                default:
                    if (vlog != null)
                        vLog(vlog, si.Name, $"req[{si.VerifyMethod}]: no checksums");
                    break;
            }

            if (ret.Count == 1 && ret[0] == ChecksumType.Crc32)
                chkString.Append($"{ChecksumType.Crc32}Only");
            else if (ret.Count >= 1 && ret.Last() == ChecksumType.Crc32)
                chkString.Append($"+{ChecksumType.Crc32}");

            if (si.VerifyMethod == VerifyMethod.NoVerify)
                checksumString = si.VerifyMethod.ToString();
            else
                checksumString = $"{si.VerifyMethod} [{chkString}]";

            return ret.ToArray();
        }

        public uint sumCrc(IParts parts)
        {
            if (parts == null || parts.Length == 0)
                return 0;

            // If the parts collection exposes a global CRC (calculated during streaming),
            // prefer that value because it represents the CRC computed over the
            // streamed image data.
            if (parts is IPartsGlobalHash global && global.GlobalCrc != 0)
                return global.GlobalCrc;

            uint crc = parts[0].Checksums.Crc;
            for (int x = 1; x < parts.Length; x++)
                crc = ~Nanook.NKit.Crc.Combine(~crc, ~parts[x].Checksums.Crc, parts[x].Size);
            return crc;
        }

        public VerifyResult Verify(NKitStepResult lastProcessed, IEnumerable<NKitStepResult> results, long inSize, bool imageSkipped)
        {
            NKitStepResult[] rs = results.ToArray();
            bool isFinal = lastProcessed == rs.Last();
            IStepInfo lastSi = lastProcessed.StepInfo;
            ChecksumType[] compared = lastProcessed.StepInfo.VerifyChecksums;
            ChecksumType? skipped = null;

            VerifyResult vr = VerifyResult.Unverified;
            VerifyMethod vm = lastSi.VerifyMethod;
            string postfix = "";

            ILogScope vlog = verifyLog();
            if (vlog != null)
                vLog(vlog, lastSi.Name, $"verify: method {lastSi.VerifyMethod} isFinal {(isFinal ? "y" : "n")}"
                    + $" isFix {(lastSi.IsFix ? "y" : "n")} fullScan {(lastSi.FullScan ? "y" : "n")} imageSkipped {(imageSkipped ? "y" : "n")}"
                    + $" compare [{(compared == null ? "" : string.Join("+", compared.Select(a => a.ToString())))}]"
                    + $" hasScan {(lastProcessed.Scan != null ? "y" : "n")} srcParts {lastSi.SrcParts?.Length.ToString() ?? "-"} datMatch {(lastSi.DatMatch != null ? "y" : "n")}");

            if (lastSi.VerifyMethod == VerifyMethod.NoVerify || imageSkipped)
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: branch [NoVerify/Skipped] ({(imageSkipped ? "imageSkipped" : "method=NoVerify")})");
                if (!lastSi.IsFix || lastSi.FullScan)
                {
                    if (lastProcessed.Scan != null)
                    {
                        lastProcessed.MatchedDatItem = datLookup(_datManager, new Parts(new Checksums() { Crc = lastProcessed.Scan.Crc, Size = lastProcessed.Scan.Size }), [ChecksumType.Crc32], out _);
                        lastProcessed.ResultCrc = lastProcessed.Scan.Crc;
                        lastProcessed.ResultSize = lastProcessed.Scan.Size;
                    }
                    else if (lastProcessed == rs[0] && (lastSi.SrcParts?[0].Checksums.Exists(ChecksumType.Crc32) ?? false))
                    {
                        lastProcessed.ResultCrc = sumCrc(lastSi.SrcParts);
                        if (lastSi.SrcParts != null)
                            lastProcessed.ResultSize = lastSi.SrcParts.Sum(a => a.Size);
                    }
                    if (lastProcessed.ResultSize == null) //set when skipped and not blanked by end of function
                        lastProcessed.ResultSize = inSize;
                }
                if (imageSkipped)
                    vm = VerifyMethod.NoVerify;
            }
            else if (lastSi.VerifyMethod == VerifyMethod.InChecksums || lastSi.VerifyMethod == VerifyMethod.DataStore)
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: branch [{lastSi.VerifyMethod}] (compare produced parts vs source parts)");
                IParts parts = GetStepChecksums(lastProcessed, isFinal);
                IParts srcParts = lastSi.SrcParts;
                if (srcParts != null && parts != null && srcParts.Length != parts.Length)
                {
                    if (vlog != null)
                        vLog(vlog, lastSi.Name, $"verify: part-count mismatch src {srcParts.Length} vs produced {parts.Length} -> joining to compare as one");
                    if (srcParts.Length == 1 && parts.Length > 1)
                        parts = joinParts(parts);
                    else if (parts.Length == 1 && srcParts.Length > 1)
                        srcParts = joinParts(srcParts);
                }

                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: src {describeValues(srcParts, compared)} vs produced {describeValues(parts, compared)}");
                vr = compareParts(srcParts, parts, compared, out skipped) ? VerifyResult.VerifySuccess : VerifyResult.VerifyFailed;
                if (vlog != null)
                {
                    // Distinguish a genuine mismatch from "no produced checksums" — the latter is the
                    // deliberate CPU-saving path (checksums not always computed) and the step's real
                    // outcome is resolved elsewhere (scan/dat), NOT a verify failure of this compare.
                    string note = parts == null
                        ? "no produced checksums (not computed — outcome resolved via scan/dat)"
                        : $"compared [{(compared == null ? "" : string.Join("+", compared.Select(a => a.ToString())))}]{(skipped != null ? $" skipped {skipped}" : "")}";
                    vLog(vlog, lastSi.Name, $"verify: {note} -> compareParts {(vr == VerifyResult.VerifySuccess ? "match" : "no-match")}");
                }
                if (compared.Length != 0)
                    postfix = $" [{string.Join("+", compared.Select(a => a.ToString()))}]";

                List<ChecksumType?> dmChk = new List<ChecksumType?>() { parts.GetHashType(false, out bool hasCrc) };
                if (hasCrc && dmChk.Count == 1 && dmChk[0] != ChecksumType.Crc32)
                    dmChk.Add(ChecksumType.Crc32);
                lastProcessed.MatchedDatItem = datLookup(_datManager, parts, dmChk.Where(a => a != null).Select(a => a.Value).ToArray(), out _);
                lastProcessed.ResultCrc = sumCrc(parts);
                lastProcessed.ResultSize = parts.Sum(a => a.Size);
            }
            else if (lastSi.VerifyMethod == VerifyMethod.DatMatch && lastSi.DatMatch != null)
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: branch [DatMatch] (compare produced parts vs supplied dat match, {lastSi.DatMatch.Length} parts)");
                IParts parts = GetStepChecksums(lastProcessed, isFinal);
                IParts srcParts = lastSi.DatMatch;
                if (srcParts != null && parts != null && srcParts.Length != parts.Length)
                {
                    if (srcParts.Length == 1 && parts.Length > 1)
                        parts = joinParts(parts);
                    else if (parts.Length == 1 && srcParts.Length > 1)
                        srcParts = joinParts(srcParts);
                }

                if (compared.Length != 0)
                    postfix = $" [{string.Join("+", compared.Select(a => a.ToString()))}]";

                if (compareParts(srcParts, parts, compared, out skipped))
                {
                    vr = VerifyResult.VerifySuccess;
                    if (compared != null)
                    {
                        if (compared.Length > 1 && skipped != null)
                            postfix = $" [{compared[1]}-Missing{skipped}]";
                        else if (compared.Length != 0)
                            postfix = $" [{string.Join("+", compared.Select(a => a.ToString()))}]";
                        if ((lastSi.DatMatch?.Length ?? 0) > 1)
                            postfix += $" [{lastSi.DatMatch.Length} parts]";
                    }
                }
                else
                    vr = VerifyResult.VerifyFailed;
                lastProcessed.MatchedDatItem = vr == VerifyResult.VerifySuccess ? lastSi.DatMatch : datLookup(_datManager, lastProcessed.InFileParts, compared, out _);
                lastProcessed.ResultCrc = sumCrc(lastProcessed.InFileParts);
                lastProcessed.ResultSize = parts.Sum(a => a.Size);
            }
            else if (lastSi.VerifyMethod == VerifyMethod.DatLookup || (lastSi.VerifyMethod == VerifyMethod.DatMatch && lastSi.DatMatch == null))
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: branch [DatLookup]{(lastSi.VerifyMethod == VerifyMethod.DatMatch ? " (DatMatch with no supplied match -> lookup)" : "")}"
                        + $" scanOnly {(!lastSi.IsFix || lastSi.FullScan ? "y" : "n")}");
                if (!lastSi.IsFix || lastSi.FullScan)
                {
                    IParts parts = GetStepChecksums(lastProcessed, isFinal);
                    lastProcessed.MatchedDatItem = datLookup(_datManager, parts, compared, out skipped);
                    if (compared != null)
                    {
                        if (compared.Length > 1 && skipped != null)
                            postfix = $" [{compared[1]}-Missing{skipped}]";
                        else if (compared.Length != 0)
                            postfix = $" [{string.Join("+", compared.Select(a => a.ToString()))}]";
                        if ((lastProcessed.MatchedDatItem?.Length ?? 0) > 1)
                            postfix += $" [{lastProcessed.MatchedDatItem.Bins.Length} parts]";
                    }
                    lastProcessed.ResultCrc = lastProcessed.Scan.Crc;
                    lastProcessed.ResultSize = lastProcessed.Scan.Size;
                }
                vr = lastProcessed.MatchedDatItem != null ? VerifyResult.VerifySuccess : VerifyResult.VerifyFailed;
            }
            else if (lastSi.VerifyMethod == VerifyMethod.ScanCompare) //from matched loaded scan
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: branch [ScanCompare] (loaded scan vs produced scan)");
                vr = ScanParser.Equal(lastSi.SrcScan, lastProcessed.Scan) ? VerifyResult.VerifySuccess : VerifyResult.VerifyFailed;
                lastProcessed.MatchedDatItem = datLookup(_datManager, new Parts(new Checksums() { Crc = lastProcessed.Scan.Crc, Size = lastProcessed.Scan.Size }), compared, out _);
                lastProcessed.ResultCrc = lastProcessed.Scan.Crc;
                lastProcessed.ResultSize = lastProcessed.Scan.Size;
            }
            else if (lastSi.VerifyMethod == VerifyMethod.InScanCompare) //from previous processing
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: branch [InScanCompare] (earlier full-scan vs produced scan)");
                Scan cmp = find(rs, lastProcessed, a => a.StepInfo.FullScan && a.Scan != null)?.Scan; //last scan created that's before current
                vr = ScanParser.Equal(cmp, lastProcessed.Scan) ? VerifyResult.VerifySuccess : VerifyResult.VerifyFailed;
                lastProcessed.MatchedDatItem = datLookup(_datManager, new Parts(new Checksums() { Crc = lastProcessed.Scan.Crc, Size = lastProcessed.Scan.Size }), compared, out _);
                lastProcessed.ResultCrc = lastProcessed.Scan.Crc;
                lastProcessed.ResultSize = lastProcessed.Scan.Size;
            }

            lastProcessed.ChkCompared = compared;
            lastProcessed.VerifyResult = vr;
            lastProcessed.VerifyType = vm.ToString() + postfix;

            if (!lastSi.FullScan && !lastSi.IsFix)
            {
                if (vlog != null)
                    vLog(vlog, lastSi.Name, $"verify: not fullScan and not fix -> ResultCrc/Size blanked (verify CRC not reported for this step)");
                lastProcessed.ResultCrc = 0;
                lastProcessed.ResultSize = 0;
            }

            if (vlog != null)
                vLog(vlog, lastSi.Name, $"verify: result {vr} type '{lastProcessed.VerifyType}'"
                    + $" datMatch {(lastProcessed.MatchedDatItem != null ? "y" : "n")}"
                    + $" resultCrc {((lastProcessed.ResultCrc ?? 0) == 0 ? "-" : lastProcessed.ResultCrc.Value.ToString("X8"))}");

            return vr;
        }

        private IParts joinParts(IParts parts)
        {
            if (parts == null)
                return null;
            if (parts.Length <= 1)
                return parts;

            Checksums chk = new Checksums() { Crc = sumCrc(parts), Size = parts.Sum(a => a.Size) };

            // If the IParts collection exposes global hashes (captured during sequential processing),
            // use those to allow verification of non-combinable hashes like XxHash.
            if (parts is IPartsGlobalHash global && global.HasGlobalHashes)
            {
                if (global.GlobalXxHash != 0) chk.XxHash = global.GlobalXxHash;
                if (global.GlobalMd5 != null) chk.Md5 = global.GlobalMd5;
                if (global.GlobalSha1 != null) chk.Sha1 = global.GlobalSha1;
            }

            return new Parts(chk);
        }

        private DatItem datLookup(DatManager datMgr, IParts find, ChecksumType[] compare, out ChecksumType? skipped)
        {
            ChecksumType? skip = null;
            DatItem di = null;
            if (find != null)
                di = datMgr.FindMatch(a => compareParts(find, a, compare, out skip));
            skipped = skip;
            return di;
        }

        private bool compareParts(IParts a, IParts b, ChecksumType[] compare, out ChecksumType? skipped)
        {
            skipped = null;
            if (a == null || b == null)
                return false;
            ChecksumType? skip = null;
            bool found = a.Length > 0;
            for (int i = 0; found && i < a.Length; i++)
            {
                Checksums srcChk = a[i].Checksums;
                found = b.FirstOrDefault(x =>
                {
                    int c = 0;
                    bool cMatch = compare.All(chk =>
                    {
                        if (!srcChk.Exists(chk) || !x.Checksums.Exists(chk)) //if a hash doesn't exist then accept crc only
                        {
                            if (chk == ChecksumType.Crc32)
                                return false;
                            skip = chk;
                            return true;
                        }
                        else
                        {
                            c++;
                            return srcChk[chk].Length == x.Checksums[chk].Length && srcChk[chk].Equals(0, x.Checksums[chk], 0, srcChk[chk].Length);
                        }
                    }) && c > 0;
                    return cMatch;
                }) != null;
            }
            skipped = skip;
            return found;
        }

        public static IParts GetStepChecksums(NKitStepResult result, bool isFinalStep)
        {
            IParts parts = null;
            //if final step then use OutChecksums if available - otherwise In
            if (isFinalStep && result.StepInfo.CreateOutChecksum)
                parts = result.OutFileParts;
            else if (result.StepInfo.CreateInChecksum) //favour In when main Verify task. Out when not main
                parts = result.InFileParts;
            else if (result.StepInfo.CreateOutChecksum)
                parts = result.OutFileParts;
            else if (result.StepInfo.CreateInChecksum || result.StepInfo.CreateOutChecksum)
                throw new HandledException($"Verify method set to {result.StepInfo.VerifyMethod} and CreateInChecksum / CreateOutChecksum not enabled");

            if (parts != null && parts.Length == 1)
                parts[0].Checksums.MergeSafe(result.StepInfo.SrcParts?[0]?.Checksums);
            return parts;
        }

        private static T find<T>(IEnumerable<T> results, T start, Predicate<T> match) where T : class
        {
            bool tst = start == null;
            foreach (T result in results.Reverse())
            {
                if (!tst)
                    tst = start == result;
                else if (match(result))
                    return result;
            }
            return null;
        }

    }
}
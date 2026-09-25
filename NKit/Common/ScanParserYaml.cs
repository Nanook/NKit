using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit
{
    /// <summary>Scan YAML verbosity. <see cref="Verbose"/> writes every offset (a faithful superset
    /// of the XML). <see cref="Compact"/> writes only the two anchors per line — <c>ImageOffset</c>
    /// (raw) and <c>AreaFsOffset</c> (fs-space) — from which every other offset is recomputed on
    /// read using the area block geometry.</summary>
    public enum ScanYamlMode { Compact, Verbose }

    /// <summary>On-disk scan serialization format. <see cref="Xml"/> is the legacy
    /// <c>&lt;NKitScan Version="1.0"&gt;</c> document; <see cref="YamlCompact"/> and
    /// <see cref="YamlVerbose"/> are the query-friendly <c>NKitScan: {Version: 2.0}</c> YAML
    /// (compact = two anchors per line, verbose = every offset). Reading is format-agnostic
    /// (the content is sniffed), so the format only affects what is written.</summary>
    public enum ScanFormat { Xml, YamlCompact, YamlVerbose }

    /// <summary>
    /// YAML representation of a <see cref="Scan"/>, designed to be easy to query and diff. Each
    /// element stays on ONE flow-mapping line. The structure fixes the XML's query pain points:
    /// <list type="bullet">
    /// <item>Area metadata is folded INTO the Area line as a nested <c>Info: {}</c> (not a detached
    /// sibling).</item>
    /// <item>A file and its trailing gap are ONE <c>Items</c> entry (the model already pairs them),
    /// with the gap nested as <c>Gap: {}</c>.</item>
    /// <item>The whole-item (section-spanning) summary is a nested <c>Full: {Size, CRC[, XxHash]}</c>
    /// present only on the FIRST part — so "list the filesystem" = items whose part has a
    /// <c>Full</c>.</item>
    /// <item>A non-uniform gap's / Other-section's internal breakdown is a nested <c>Parts: []</c>
    /// list on the owner — one consistent shape (no separate, re-parenting element).</item>
    /// </list>
    /// Values are formatted identically to the XML (hex X8/X9/X16, bool true/false, DataType tokens)
    /// so the two encode the same facts and round-trip to the identical <see cref="Scan"/> model.
    /// </summary>
    internal class ScanParserYaml
    {
        private ScanParserYaml() { }

        private const string ScanVersion = "2.0";

        // ════════════════════════════════════════════════════════════════════════
        //  READ
        // ════════════════════════════════════════════════════════════════════════
        //
        // The new YAML structure differs from the XML (folded Info, File+Gap+Parts as one item,
        // nested Full/Combined), so ParseYaml translates it into the XML shape that the existing,
        // battle-tested ScanParser.Parse consumes — reusing ALL of ScanParser's model construction
        // and (brittle, per-system) getAreaProperties typing with zero duplication.
        //
        // In Compact mode the offsets that were dropped are recomputed here purely by subtraction of
        // the two anchors (ImageOffset raw, AreaFsOffset fs) against their parent — no block geometry
        // is needed, because one raw + one fs anchor per line is a lossless minimal set.

        public static Scan ParseYaml(string filePathName)
        {
            using (Stream s = File.OpenRead(filePathName))
                return ParseYaml(s, Path.GetFileNameWithoutExtension(filePathName));
        }

        public static Scan ParseYaml(Stream yamlData, string name)
        {
            string yaml;
            using (StreamReader sr = new StreamReader(yamlData, Encoding.UTF8))
                yaml = sr.ReadToEnd();

            byte[] bytes = Encoding.UTF8.GetBytes(YamlToXml(yaml));
            using (MemoryStream ms = new MemoryStream(bytes))
                return ScanParser.Parse(ms, name);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  FORMAT-DISPATCHING FACADE
        // ════════════════════════════════════════════════════════════════════════
        // These let call sites work in terms of a single ScanFormat and stay agnostic of whether the
        // scan on disk is legacy XML or the new YAML. Writing picks the serializer from the format;
        // reading sniffs the content so either format loads regardless of how it was written.

        /// <summary>The on-disk file extension (without leading dot) for a scan in the given format:
        /// <c>nkit</c> for XML, <c>nkit.yaml</c> for the YAML formats — so YAML scans are recognised
        /// as YAML by editors and tools while both remain "nkit" scans. This is the single source of
        /// truth for the scan extension; callers must NOT pre/append their own ".yaml".</summary>
        public static string ScanExtension(ScanFormat format)
            => format == ScanFormat.Xml ? "nkit" : NKit.NKitTask.ScanExt;

        /// <summary>Serialize a scan to text in the requested <see cref="ScanFormat"/>.</summary>
        public static string Serialize(Scan scan, ScanFormat format)
        {
            switch (format)
            {
                case ScanFormat.YamlCompact:
                    return ResultToYaml(scan, ScanYamlMode.Compact);
                case ScanFormat.YamlVerbose:
                    return ResultToYaml(scan, ScanYamlMode.Verbose);
                default:
                    return ScanParser.ResultToString(scan);
            }
        }

        /// <summary>Detect whether scan text is the new YAML (<c>NKitScan:</c>) or the legacy XML
        /// (<c>&lt;?xml</c> / <c>&lt;NKitScan</c>) by its first non-whitespace content.</summary>
        public static bool IsYaml(string scanText)
        {
            if (string.IsNullOrEmpty(scanText))
                return false;
            int i = 0;
            while (i < scanText.Length && char.IsWhiteSpace(scanText[i]))
                i++;
            // XML always opens with '<'; the YAML document opens with the "NKitScan:" mapping key.
            return i < scanText.Length && scanText[i] != '<';
        }

        /// <summary>Parse a scan from a stream regardless of format — sniffs XML vs YAML.</summary>
        public static Scan Parse(Stream scanData, string name)
        {
            string text;
            using (StreamReader sr = new StreamReader(scanData, Encoding.UTF8))
                text = sr.ReadToEnd();
            return Parse(text, name);
        }

        /// <summary>Parse a scan from text regardless of format — sniffs XML vs YAML.</summary>
        public static Scan Parse(string scanText, string name)
        {
            if (IsYaml(scanText))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(YamlToXml(scanText));
                using (MemoryStream ms = new MemoryStream(bytes))
                    return ScanParser.Parse(ms, name);
            }
            byte[] xb = Encoding.UTF8.GetBytes(scanText);
            using (MemoryStream ms = new MemoryStream(xb))
                return ScanParser.Parse(ms, name);
        }

        /// <summary>Translate the new-structure scan YAML into the XML document ScanParser.Parse
        /// consumes. Emits the FULL attribute set the XML reader expects (deriving the offsets that
        /// Compact mode omitted). Grammar-driven: each line is a recognised element.</summary>
        internal static string YamlToXml(string yaml)
        {
            StringBuilder xml = new StringBuilder(yaml.Length + 0x1000);
            xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");

            bool scanOpen = false, areaOpen = false, sectionOpen = false;

            // Context needed for offset derivation.
            long areaImageOffset = 0;
            long secImageOffset = 0, secAreaFsOffset = 0;
            bool areaIsFs = false;                 // BlockSize != BlockFsSize (has AreaFsOffset anchors)
            string curFileName = null;             // current file, to attribute continuation parts / gap
            long fileFirstAreaFs = 0;              // first-part AreaFsOffset of the current file (for Position)
            long gapFirstAreaFs = 0;               // first-part AreaFsOffset of the current gap
            string curGapName = null;

            // Parts blocks (indentation-delimited, since the reader is line-oriented). A block is
            // opened by a "Parts:" header at some indent and stays active for every deeper-indented
            // entry until a line at that indent-or-shallower appears.
            //   inSectionParts : Parts attach to the current <Section> (non-FS Other area).
            //   gapPartsOpen   : Parts attach to the current (still-open) <Gap> element.
            bool inSectionParts = false, gapPartsOpen = false;
            bool gapAwaitingParts = false;         // a gap was emitted OPEN, expecting a Parts: block
            int partsIndent = -1;                  // indent of the active Parts: header (-1 = none)

            // A pending <Area>: the Area line no longer folds Info, so we defer writing <Area> until
            // we know whether an Info: line follows (the XML needs <AreaInfo> emitted BEFORE <Area>).
            List<KeyValuePair<string, string>> pendingArea = null;

            void CloseGapParts() { if (gapPartsOpen) { xml.Append("      </Gap>\n"); gapPartsOpen = false; } }
            void EndPartsBlocks() { CloseGapParts(); inSectionParts = false; partsIndent = -1; }
            void FlushArea()
            {
                if (pendingArea == null) return;
                List<KeyValuePair<string, string>> m = pendingArea; pendingArea = null;
                xml.Append("  <Area");
                WriteAttr(xml, "ImageOffset", Get(m, "ImageOffset"));
                WriteAttr(xml, "Type", Get(m, "Type"));
                WriteAttr(xml, "Size", Get(m, "Size"));
                WriteAttr(xml, "CRC", Get(m, "CRC"));
                string decCrc = Get(m, "DecryptedCRC");
                if (decCrc != null) WriteAttr(xml, "DecryptedCRC", decCrc);
                xml.Append(">\n");
                areaOpen = true;
                areaIsFs = Get(m, "Fs") == "true";
            }
            void CloseSection() { EndPartsBlocks(); if (sectionOpen) { xml.Append("    </Section>\n"); sectionOpen = false; } }
            void CloseArea() { CloseSection(); FlushArea(); if (areaOpen) { xml.Append("  </Area>\n"); areaOpen = false; } }

            foreach (string raw in yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                // Indentation (before trimming) delimits Parts blocks.
                int indent = 0;
                while (indent < raw.Length && raw[indent] == ' ') indent++;
                if (indent >= raw.Length)
                    continue;

                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("- "))
                    line = line.Substring(2).TrimStart();

                // A Parts block ends once we reach a line at its indent or shallower.
                if (partsIndent >= 0 && indent <= partsIndent)
                    EndPartsBlocks();

                int colon = line.IndexOf(':');
                string head = colon < 0 ? line : line.Substring(0, colon);
                string rest = colon < 0 ? "" : line.Substring(colon + 1).Trim();

                // List headers carry no element.
                if ((head == "Areas" || head == "Sections" || head == "Items") && rest.Length == 0)
                {
                    EndPartsBlocks();
                    if (head == "Sections" || head == "Items")
                        FlushArea(); // an area with no Info: goes straight to its children
                    continue;
                }
                // A block-style "Parts:" header: attaches to the current <Gap> (FS breakdown) when
                // the item just emitted was a gap left open for parts, else to the <Section> (Other).
                if (head == "Parts" && rest.Length == 0)
                {
                    partsIndent = indent;
                    if (gapAwaitingParts)
                    {
                        gapPartsOpen = true;         // route following parts under the open <Gap>
                        gapAwaitingParts = false;
                    }
                    else
                        inSectionParts = true;       // section-level breakdown
                    continue;
                }

                switch (head)
                {
                    case "NKitScan":
                        {
                            List<KeyValuePair<string, string>> m = ParseFlowMapping(rest);
                            xml.Append("<NKitScan");
                            foreach (KeyValuePair<string, string> kv in m)
                                if (kv.Key != "Mode") // Mode is a YAML-only hint; the XML reader ignores Version too
                                    WriteAttr(xml, kv.Key, kv.Value);
                            xml.Append(">\n");
                            scanOpen = true;
                            break;
                        }
                    case "Area":
                        {
                            CloseArea();
                            List<KeyValuePair<string, string>> m = ParseFlowMapping(rest);
                            areaImageOffset = ParseHex(Get(m, "ImageOffset"));
                            // Defer <Area> emission; Info: (if any) arrives on the NEXT line and must be
                            // written as <AreaInfo> BEFORE <Area>. Fs flag is read at flush time.
                            pendingArea = m;
                            areaIsFs = Get(m, "Fs") == "true"; // needed if the very next line is a Section
                            break;
                        }
                    case "Info":
                        {
                            // Area Info on its own line — emit <AreaInfo> now (before the deferred <Area>).
                            xml.Append("  <AreaInfo");
                            foreach (KeyValuePair<string, string> kv in ParseFlowMapping(rest))
                                WriteAttr(xml, kv.Key, kv.Value);
                            xml.Append(" />\n");
                            FlushArea();
                            break;
                        }
                    case "Section":
                        {
                            FlushArea();
                            CloseSection();
                            List<KeyValuePair<string, string>> m = ParseFlowMapping(rest);
                            secImageOffset = ParseHex(Get(m, "ImageOffset"));
                            long secAreaOff = secImageOffset - areaImageOffset;
                            string afo = Get(m, "AreaFsOffset");
                            // Section fs-space start: from the anchor when fs, else == raw area offset.
                            secAreaFsOffset = (areaIsFs && afo != null) ? ParseHex(afo) : secAreaOff;

                            xml.Append("    <Section");
                            WriteAttr(xml, "ImageOffset", Get(m, "ImageOffset"));
                            WriteAttr(xml, "AreaOffset", Hex(secAreaOff, 9));
                            WriteAttr(xml, "Size", Get(m, "Size"));
                            string fsSize = Get(m, "FsSize");
                            if (fsSize != null) WriteAttr(xml, "FsSize", fsSize);
                            WriteAttr(xml, "CRC", Get(m, "CRC"));
                            string dcrc = Get(m, "DecryptedCRC");
                            if (dcrc != null) WriteAttr(xml, "DecryptedCRC", dcrc);
                            string xxh = Get(m, "XxHash");
                            if (xxh != null) WriteAttr(xml, "XxHash", xxh);
                            string valid = Get(m, "Valid");
                            if (valid != null) WriteAttr(xml, "Valid", valid);
                            string creat = Get(m, "Creatable");
                            if (creat != null) WriteAttr(xml, "Creatable", creat);
                            string state = Get(m, "State");
                            if (state != null) WriteAttr(xml, "State", state);
                            string seekIv = Get(m, "SeekIv");
                            if (seekIv != null) WriteAttr(xml, "SeekIv", seekIv);
                            string sdt = Get(m, "DataType"); // non-FS single-item section
                            if (sdt != null) WriteAttr(xml, "DataType", sdt);
                            xml.Append(">\n");
                            sectionOpen = true;
                            curFileName = curGapName = null;
                            break;
                        }
                    case "Item":
                        {
                            // A gap-with-Parts is a BLOCK mapping: "- Item: {gap fields}" then a sibling
                            // "Parts:" block. The <Gap> is left OPEN so the following parts nest in it.
                            List<KeyValuePair<string, string>> gm = ParseFlowMapping(rest);
                            gapAwaitingParts = true;
                            EmitItem(xml, gm, ref curFileName, ref fileFirstAreaFs, ref gapFirstAreaFs, ref curGapName,
                                     secImageOffset, secAreaFsOffset, areaImageOffset, areaIsFs, true);
                            break;
                        }
                    default:
                        {
                            // A bare flow mapping "{...}" — a Parts entry (Info) when inside a Parts block,
                            // otherwise a normal one-line File/Gap item.
                            if (!line.StartsWith("{"))
                                break;
                            List<KeyValuePair<string, string>> m = ParseFlowMapping(line);
                            if (inSectionParts || gapPartsOpen)
                                EmitInfo(xml, m, secImageOffset, secAreaFsOffset, areaImageOffset, areaIsFs);
                            else
                                EmitItem(xml, m, ref curFileName, ref fileFirstAreaFs, ref gapFirstAreaFs, ref curGapName,
                                         secImageOffset, secAreaFsOffset, areaImageOffset, areaIsFs);
                            break;
                        }
                }
            }

            EndPartsBlocks();
            CloseArea();
            if (scanOpen)
                xml.Append("</NKitScan>\n");
            return xml.ToString();
        }

        // Emit a <File>/<Gap> item (with optional nested <Gap> and its <Info> parts) into XML,
        // computing every offset the XML reader expects.
        private static void EmitItem(StringBuilder xml, List<KeyValuePair<string, string>> m,
            ref string curFileName, ref long fileFirstAreaFs, ref long gapFirstAreaFs, ref string curGapName,
            long secImg, long secAreaFs, long areaImg, bool isFs, bool gapLeaveOpen = false)
        {
            if (Get(m, "File") != null)
            {
                string name = Get(m, "File");
                // First part of a file == no Position on the line (OffsetInItem == 0).
                bool firstPart = Get(m, "Position") == null;
                if (firstPart || curFileName != name)
                {
                    curFileName = name;
                    fileFirstAreaFs = isFs ? ParseHex(Get(m, "AreaFsOffset")) : (ParseHex(Get(m, "ImageOffset")) - areaImg);
                }
                EmitData(xml, m, true, name, secImg, secAreaFs, areaImg, isFs, fileFirstAreaFs);
                // A file's trailing gap is its OWN item line (handled by the Gap branch below).
            }
            else if (Get(m, "Gap") != null)
            {
                string gname = Get(m, "Gap");
                bool gFirst = Get(m, "Position") == null;
                if (gFirst || curGapName != gname)
                {
                    curGapName = gname;
                    gapFirstAreaFs = isFs ? ParseHex(Get(m, "AreaFsOffset")) : (ParseHex(Get(m, "ImageOffset")) - areaImg);
                }
                // When a Parts block follows, leave <Gap> OPEN; the caller closes it after the parts.
                EmitData(xml, m, false, gname, secImg, secAreaFs, areaImg, isFs, gapFirstAreaFs, gapLeaveOpen);
            }
        }

        // Emit a <File> or <Gap> element. When it has nested Parts, leave it OPEN (caller closes).
        private static void EmitData(StringBuilder xml, List<KeyValuePair<string, string>> m, bool file, string name,
            long secImg, long secAreaFs, long areaImg, bool isFs, long itemFirstAreaFs, bool leaveOpen = false)
        {
            long img = ParseHex(Get(m, "ImageOffset"));
            long areaOff = img - areaImg;
            long secOff = img - secImg;
            long areaFs = isFs ? ParseHex(Get(m, "AreaFsOffset")) : areaOff;
            long secFs = isFs ? (areaFs - secAreaFs) : secOff;
            // Position within the item. It is written on the line whenever non-zero (it is NOT
            // derivable across section boundaries), so read it verbatim when present and only fall
            // back to the fs-space distance from the item's first part when it is absent (== 0).
            string posStr = Get(m, "Position");
            long position = posStr != null
                ? ParseHex(posStr)
                : (isFs ? (areaFs - itemFirstAreaFs) : (areaOff - itemFirstAreaFs));

            string tag = file ? "File" : "Gap";
            xml.Append("      <").Append(tag);
            WriteAttr(xml, "ImageOffset", Hex(img, 9));
            WriteAttr(xml, "AreaOffset", Hex(areaOff, 9));
            WriteAttr(xml, "SectionOffset", Hex(secOff, 8));
            if (isFs)
            {
                WriteAttr(xml, "AreaFsOffset", Hex(areaFs, 9));
                WriteAttr(xml, "SectionFsOffset", Hex(secFs, 8));
            }
            WriteAttr(xml, "Position", Hex(position, 9));
            WriteAttr(xml, isFs ? "FsSize" : "Size", Get(m, isFs ? "FsSize" : "Size"));
            WriteAttr(xml, "CRC", Get(m, "CRC"));

            // Full → FullSize/FullCRC (+ XxHash for files). Present only on the first part.
            string fullFlow = Get(m, "Full");
            bool fullWroteXx = false;
            if (fullFlow != null)
            {
                List<KeyValuePair<string, string>> full = ParseFlowMapping(fullFlow);
                WriteAttr(xml, "FullSize", Get(full, "Size"));
                WriteAttr(xml, "FullCRC", Get(full, "CRC"));
                string fxx = Get(full, "XxHash");
                if (fxx != null) { WriteAttr(xml, "XxHash", fxx); fullWroteXx = true; }
            }
            // A single-part gap with DataType Other carries its own XxHash on the line. It can
            // coexist with the Full summary (which, for gaps, never carries an XxHash), so emit it
            // whenever Full did not already write one.
            string ownXx = Get(m, "XxHash");
            if (!file && !fullWroteXx && ownXx != null)
                WriteAttr(xml, "XxHash", ownXx);

            WriteAttr(xml, "DataType", Get(m, "DataType"));
            WriteAttr(xml, tag, name);

            string fsList = Get(m, "FS");
            if (file && fsList != null)
                WriteAttr(xml, "FS", fsList);

            string combFlow = Get(m, "Combined");
            if (file && combFlow != null)
            {
                List<KeyValuePair<string, string>> c = ParseFlowMapping(combFlow);
                WriteAttr(xml, "SplitIndex", Get(c, "Index"));
                WriteAttr(xml, "SplitParts", Get(c, "Parts"));
                WriteAttr(xml, "SplitPosition", Get(c, "Position"));
                WriteAttr(xml, "CombinedSize", Get(c, "Size"));
                WriteAttr(xml, "CombinedCRC", Get(c, "CRC"));
                WriteAttr(xml, "CombinedXxHash", Get(c, "XxHash"));
            }

            xml.Append(leaveOpen ? ">\n" : " />\n");
        }

        // Emit an <Info> (gap/section breakdown part).
        private static void EmitInfo(StringBuilder xml, List<KeyValuePair<string, string>> m,
            long secImg, long secAreaFs, long areaImg, bool isFs)
        {
            long img = ParseHex(Get(m, "ImageOffset"));
            long areaOff = img - areaImg;
            long secOff = img - secImg;
            long areaFs = isFs ? ParseHex(Get(m, "AreaFsOffset")) : areaOff;
            long secFs = isFs ? (areaFs - secAreaFs) : secOff;

            xml.Append("        <Info");
            WriteAttr(xml, "ImageOffset", Hex(img, 9));
            WriteAttr(xml, "AreaOffset", Hex(areaOff, 9));
            WriteAttr(xml, "SectionOffset", Hex(secOff, 9));
            if (isFs)
            {
                WriteAttr(xml, "AreaFsOffset", Hex(areaFs, 9));
                WriteAttr(xml, "SectionFsOffset", Hex(secFs, 8));
            }
            WriteAttr(xml, isFs ? "FsSize" : "Size", Get(m, isFs ? "FsSize" : "Size"));
            string crc = Get(m, "CRC");
            if (crc != null) WriteAttr(xml, "CRC", crc);
            string xx = Get(m, "XxHash");
            if (xx != null) WriteAttr(xml, "XxHash", xx);
            WriteAttr(xml, "DataType", Get(m, "DataType"));
            xml.Append(" />\n");
        }

        private static void WriteAttr(StringBuilder xml, string key, string val)
        {
            if (val == null)
                return;
            xml.Append(' ').Append(key).Append("=\"");
            foreach (char c in val)
            {
                switch (c)
                {
                    case '&': xml.Append("&amp;"); break;
                    case '<': xml.Append("&lt;"); break;
                    case '>': xml.Append("&gt;"); break;
                    case '"': xml.Append("&quot;"); break;
                    default: xml.Append(c); break;
                }
            }
            xml.Append('"');
        }

        private static string Get(List<KeyValuePair<string, string>> m, string key)
        {
            foreach (KeyValuePair<string, string> kv in m)
                if (kv.Key == key)
                    return kv.Value;
            return null;
        }

        private static long ParseHex(string v) => v == null ? 0 : long.Parse(v, System.Globalization.NumberStyles.HexNumber);

        /// <summary>Parse a flow mapping "{a: 1, b: {c: 2}, d: [..]}" into ordered top-level pairs.
        /// Nested {} and [] are captured verbatim as the value (for a later recursive parse).</summary>
        private static List<KeyValuePair<string, string>> ParseFlowMapping(string flow)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(flow))
                return result;

            string s = flow.Trim();
            if (s.StartsWith("{") && s.EndsWith("}"))
                s = s.Substring(1, s.Length - 2);

            int i = 0, n = s.Length;
            while (i < n)
            {
                while (i < n && (s[i] == ' ' || s[i] == ',')) i++;
                if (i >= n) break;

                int keyStart = i;
                while (i < n && s[i] != ':') i++;
                string key = s.Substring(keyStart, i - keyStart).Trim();
                if (i < n) i++; // ':'
                while (i < n && s[i] == ' ') i++;

                string val;
                if (i < n && (s[i] == '{' || s[i] == '['))
                {
                    // Nested map/sequence — capture verbatim, balancing brackets (quote-aware).
                    char open = s[i], close = open == '{' ? '}' : ']';
                    int depth = 0, start = i; bool inQ = false;
                    for (; i < n; i++)
                    {
                        char c = s[i];
                        if (inQ) { if (c == '\\') i++; else if (c == '"') inQ = false; continue; }
                        if (c == '"') inQ = true;
                        else if (c == open || (open == '{' && c == '[') || (open == '[' && c == '{')) { }
                        if (c == '{' || c == '[') depth++;
                        else if (c == '}' || c == ']') { depth--; if (depth == 0) { i++; break; } }
                    }
                    val = s.Substring(start, i - start);
                }
                else if (i < n && s[i] == '"')
                {
                    i++;
                    StringBuilder sb = new StringBuilder();
                    while (i < n && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < n)
                        {
                            char e = s[i + 1];
                            sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e);
                            i += 2;
                        }
                        else sb.Append(s[i++]);
                    }
                    if (i < n) i++;
                    val = sb.ToString();
                }
                else
                {
                    int valStart = i;
                    while (i < n && s[i] != ',') i++;
                    val = s.Substring(valStart, i - valStart).Trim();
                }

                result.Add(new KeyValuePair<string, string>(key, val));
            }
            return result;
        }

        /// <summary>Split a flow sequence "[{..}, {..}]" into its element strings.</summary>
        private static List<string> SplitFlowSequence(string seq)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(seq)) return list;
            string s = seq.Trim();
            if (s.StartsWith("[") && s.EndsWith("]"))
                s = s.Substring(1, s.Length - 2);

            int i = 0, n = s.Length, depth = 0, start = -1; bool inQ = false;
            for (; i < n; i++)
            {
                char c = s[i];
                if (inQ) { if (c == '\\') i++; else if (c == '"') inQ = false; continue; }
                if (c == '"') { inQ = true; if (start < 0) start = i; continue; }
                if (c == '{' || c == '[') { if (depth == 0) start = i; depth++; }
                else if (c == '}' || c == ']') { depth--; if (depth == 0 && start >= 0) { list.Add(s.Substring(start, i - start + 1)); start = -1; } }
            }
            return list;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  WRITE
        // ════════════════════════════════════════════════════════════════════════

        public static string ResultToYaml(Scan scan) => ResultToYaml(scan, ScanYamlMode.Compact);

        public static string ResultToYaml(Scan scan, ScanYamlMode mode)
        {
            bool verbose = mode == ScanYamlMode.Verbose;
            StringBuilder sb = new StringBuilder(0x400 * 0x400);

            // Header — Version + Mode drive the reader; then the scan Properties.
            List<string> hdr = new List<string> { Kv("Version", ScanVersion), Kv("Mode", mode.ToString()) };
            foreach (string key in scan.Properties.Keys)
            {
                object val = scan.Properties[key];
                if (val != null)
                    hdr.Add(Kv(key, ToYamlValue(val)));
            }
            sb.Append("NKitScan: ").Append(Flow(hdr)).Append('\n');

            if (scan.Areas.Count == 0)
                return sb.ToString();

            sb.Append("Areas:\n");
            foreach (ScanArea sra in scan.Areas)
            {
                AreaInfo ai = sra.AreaInfo;
                bool fs = ai.BlockSize != ai.BlockFsSize;

                // ── Area line (with folded Info: {}) ──
                List<string> area = new List<string>
                {
                    Kv("No", ai.AreaNo.ToString()),
                    Kv("ImageOffset", Hex(sra.ImageOffset, 9)),
                    Kv("Type", sra.Type.ToString()),
                    Kv("Size", Hex(sra.Size, 9)),
                    Kv("CRC", Hex(sra.Crc, 8)),
                };
                if (ai.IsEncrypted)
                    area.Add(Kv("DecryptedCRC", Hex(sra.CrcDecrypted, 8)));
                if (fs)
                    area.Add(Kv("Fs", "true")); // area uses fs-space offsets (hash gaps) — drives the reader
                sb.Append("  - Area: ").Append(Flow(area)).Append('\n');

                // Area Info on its OWN line (still part of the area — a sibling mapping key of the
                // list item, indented to 4 spaces like Sections:), so the area header stays short.
                if ((ai.Properties?.Keys?.Length ?? 0) != 0)
                {
                    List<string> info = new List<string> { Kv("Type", scan.SystemType.ToString()) };
                    foreach (string key in ai.Properties.Keys)
                    {
                        object val = ai.Properties[key];
                        if (val != null)
                            info.Add(Kv(key, ToYamlValue(val)));
                    }
                    sb.Append("    Info: ").Append(Flow(info)).Append('\n');
                }

                if (sra.Sections.LastOrDefault() == null)
                    continue;

                sb.Append("    Sections:\n");
                foreach (ScanSection srs in sra.Sections)
                {
                    sb.Append("      - Section: ").Append(Flow(SectionFields(srs, verbose))).Append('\n');
                    ISectionItem lastItm = srs.Items?.LastOrDefault();

                    if (sra.Type == AreaType.FileSystem && lastItm != null)
                    {
                        sb.Append("        Items:\n");
                        foreach (ISectionItem it in srs.Items)
                        {
                            foreach (ItemLine line in ItemFields(it, ai, fs, verbose))
                            {
                                if (line.Parts == null)
                                {
                                    // Normal item — a one-line flow mapping sequence element.
                                    sb.Append("          - ").Append(Flow(line.Fields)).Append('\n');
                                }
                                else
                                {
                                    // A gap WITH a non-uniform breakdown. YAML can't hang a nested
                                    // block sequence off a flow-mapping element, so this item is a
                                    // block mapping: the gap's fields stay on one line under Item:,
                                    // and Parts is a sibling block — one aligned line per part.
                                    sb.Append("          - Item: ").Append(Flow(line.Fields)).Append('\n');
                                    sb.Append("            Parts:\n");
                                    foreach (List<string> p in line.Parts)
                                        sb.Append("              - ").Append(Flow(p)).Append('\n');
                                }
                            }
                        }
                    }
                    else if (sra.Type == AreaType.Other && srs.Items.Count == 1 && (lastItm?.GapInfo.Count ?? 0) > 1)
                    {
                        // Non-FS section whose own data is a non-uniform breakdown — Parts on the section.
                        sb.Append("        Parts:\n");
                        foreach (ISectionData gd in lastItm.GapInfo)
                            sb.Append("          - ").Append(Flow(PartFields(gd, ai, fs, verbose))).Append('\n');
                    }
                }
            }

            return sb.ToString();
        }

        // ── Field builders ───────────────────────────────────────────────────────

        private static List<string> SectionFields(ScanSection srs, bool verbose)
        {
            AreaInfo ai = srs.ParentArea.AreaInfo;
            bool fs = ai.BlockSize != ai.BlockFsSize;
            List<string> f = new List<string> { Kv("ImageOffset", Hex(srs.ImageOffset, 9)) };
            // AreaFsOffset anchor (fs-space section start). Both anchors let the reader derive the rest.
            f.Add(Kv("AreaFsOffset", Hex(Buffer.OffsetToFsOffset(srs.AreaOffset, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize), 9)));
            if (verbose)
                f.Add(Kv("AreaOffset", Hex(srs.AreaOffset, 9)));
            f.Add(Kv("Size", Hex(srs.Size, 8)));
            if (fs)
                f.Add(Kv("FsSize", Hex(srs.FsSize, 8)));
            f.Add(Kv("CRC", Hex(srs.Crc, 8)));
            if (ai.IsEncrypted)
                f.Add(Kv("DecryptedCRC", Hex(srs.CrcDecrypted, 8)));
            f.Add(Kv("XxHash", Hex(srs.XxHash, 16)));

            if (srs.ParentArea.Type != AreaType.FileSystem && srs.Items.Count == 1)
                f.Add(Kv("DataType", ScanParser.DataTypeToString(srs.Items[0].Gap, false, false)));

            if (fs)
            {
                f.Add(Kv("Valid", srs.HashesValid ? "true" : "false"));
                f.Add(Kv("Creatable", srs.IsCreatable ? "true" : "false"));
            }
            if (srs.State != null && !srs.State.IsClear())
                f.Add(Kv("State", srs.State.ToString()));
            if (srs.SeekIv != null)
                f.Add(Kv("SeekIv", srs.SeekIv.ToHexString()));
            return f;
        }

        /// <summary>One emitted item: its own flow-mapping line, plus (for a non-uniform gap) a
        /// nested list of <c>Parts</c> lines emitted underneath as their own aligned rows.</summary>
        private sealed class ItemLine
        {
            public List<string> Fields;            // the flow-mapping fields for this item's line
            public List<List<string>> Parts;       // null unless a non-uniform gap breakdown follows
        }

        /// <summary>Build the item line(s) for one <see cref="ISectionItem"/>. A file and its
        /// trailing gap are emitted as SEPARATE lines (each led by its own ImageOffset) so the
        /// section layout reads top-to-bottom with the offsets aligned. Whole-item summary is a
        /// nested <c>Full: {}</c> on the first part; split-file info a nested <c>Combined: {}</c>;
        /// a non-uniform gap's breakdown becomes a nested <c>Parts:</c> block (one aligned line
        /// per part) rather than an inline array.</summary>
        private static List<ItemLine> ItemFields(ISectionItem item, AreaInfo ai, bool fs, bool verbose)
        {
            List<ItemLine> lines = new List<ItemLine>();
            if (item.File != null)
            {
                List<string> f = new List<string>();
                AppendData(f, item, item.File, item.FsFile, true, ai, fs, verbose);
                lines.Add(new ItemLine { Fields = f });

                if (item.Gap != null)
                    lines.Add(GapLine(item, ai, fs, verbose));
            }
            else if (item.Gap != null)
            {
                lines.Add(GapLine(item, ai, fs, verbose));
            }
            return lines;
        }

        /// <summary>Build a gap's line, attaching its non-uniform breakdown as a Parts block.</summary>
        private static ItemLine GapLine(ISectionItem item, AreaInfo ai, bool fs, bool verbose)
        {
            List<string> g = new List<string>();
            AppendData(g, item, item.Gap, item.FsFile, false, ai, fs, verbose);

            List<List<string>> parts = null;
            if (item.Gap != null && item.Gap.DataType == DataType.Other && item.GapInfo.Count > 1)
            {
                parts = new List<List<string>>();
                foreach (ISectionData gd in item.GapInfo)
                    parts.Add(PartFields(gd, ai, fs, verbose));
                // Reader hint: a Parts block follows, so the <Gap> must be left open. Placed before
                // the trailing name (AppendData emits the Gap name last) so the name stays last.
                if (g.Count > 0)
                    g.Insert(g.Count - 1, Kv("HasParts", "true"));
                else
                    g.Add(Kv("HasParts", "true"));
            }
            return new ItemLine { Fields = g, Parts = parts };
        }

        /// <summary>Emit the fields of a File or Gap part. Leads with the item name (<c>File:</c> /
        /// <c>Gap:</c>) so a query keys on it, then the two offset anchors, sizes, CRC, DataType,
        /// and (first part only) the whole-item Full/Combined summaries.</summary>
        private static void AppendData(List<string> f, ISectionItem item, ISectionData itm, IFsFile fsFile, bool file, AreaInfo ai, bool fs, bool verbose)
        {
            // ImageOffset leads every item so the offsets align in a vertical column; the variable
            // File/Gap name is emitted LAST (jagged tail), keeping all the numeric columns aligned.
            f.Add(Kv("ImageOffset", Hex(itm.ImageOffset, 9)));
            if (fs)
                f.Add(Kv("AreaFsOffset", Hex(Buffer.OffsetToFsOffset(itm.AreaOffset, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize), 9)));

            if (verbose)
            {
                if (item.AreaOffset != item.ImageOffset)
                    f.Add(Kv("AreaOffset", Hex(itm.AreaOffset, 9)));
                f.Add(Kv("SectionOffset", Hex(itm.Offset, 8)));
                if (fs)
                    f.Add(Kv("SectionFsOffset", Hex(itm.FsOffset, 8)));
            }
            // Position (OffsetInItem) is NOT derivable across section boundaries — a file's first
            // part can live in a prior section — so it is always emitted, even in compact mode.
            if (itm.OffsetInItem != 0)
                f.Add(Kv("Position", Hex(itm.OffsetInItem, 9)));

            f.Add(Kv(fs ? "FsSize" : "Size", Hex(itm.FsSize, 8)));
            f.Add(Kv("CRC", Hex(itm.Crc, 8)));

            bool writeXxHash = !file && item.Gap.DataType == DataType.Other && item.GapInfo.Count == 0;

            // Whole-item summary (first part only) — the "unique filesystem row" marker.
            if (fsFile != null && itm.OffsetInItem == 0)
            {
                List<string> full = new List<string>
                {
                    Kv("Size", Hex(file ? fsFile.FsSize : fsFile.PostGapSize, 9)),
                    Kv("CRC", Hex(file ? fsFile.Crc : fsFile.GapCrc, 8)),
                };
                if (file)
                    full.Add(Kv("XxHash", Hex(fsFile.XxHash, 16)));
                f.Add("Full: " + Flow(full));
            }
            if (writeXxHash)
                f.Add(Kv("XxHash", Hex(item.Gap.XxHash, 16)));

            f.Add(Kv("DataType", ScanParser.DataTypeToString(itm, file, fsFile?.IsSystemFile ?? false)));

            if (file && item.FileSystems.Count != 0)
                f.Add(Kv("FS", string.Join(", ", item.FileSystems)));
            if (file && fsFile != null && itm.OffsetInItem == 0 && fsFile.SplitParts != null)
            {
                List<string> comb = new List<string>
                {
                    Kv("Index", fsFile.SplitIndex.ToString()),
                    Kv("Parts", fsFile.SplitParts.Parts.Count.ToString()),
                    Kv("Position", Hex(fsFile.SplitParts.Parts[fsFile.SplitIndex].OffsetInFile, 9)),
                    Kv("Size", Hex(fsFile.SplitParts.Size, 9)),
                    Kv("CRC", Hex(fsFile.SplitParts.Crc, 8)),
                    Kv("XxHash", Hex(fsFile.SplitParts.XxHash, 16)),
                };
                f.Add("Combined: " + Flow(comb));
            }

            // Name last — the jagged, variable-length tail.
            f.Add(Kv(file ? "File" : "Gap", fsFile?.FullName ?? ""));
        }

        private static List<string> PartFields(ISectionData data, AreaInfo ai, bool fs, bool verbose)
        {
            List<string> f = new List<string> { Kv("ImageOffset", Hex(data.ImageOffset, 9)) };
            if (fs)
                f.Add(Kv("AreaFsOffset", Hex(Buffer.OffsetToFsOffset(data.AreaOffset, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize), 9)));
            if (verbose)
            {
                if (data.AreaOffset != data.ImageOffset)
                    f.Add(Kv("AreaOffset", Hex(data.AreaOffset, 9)));
                f.Add(Kv("SectionOffset", Hex(data.Offset, 9)));
                if (fs)
                    f.Add(Kv("SectionFsOffset", Hex(data.FsOffset, 8)));
            }
            f.Add(Kv(fs ? "FsSize" : "Size", Hex(data.FsSize, 8)));
            if (data.DataType == DataType.Other)
            {
                f.Add(Kv("CRC", Hex(data.Crc, 8)));
                f.Add(Kv("XxHash", Hex(data.XxHash, 16)));
            }
            f.Add(Kv("DataType", ScanParser.DataTypeToString(data, false, false)));
            return f;
        }

        // ── Emit primitives ───────────────────────────────────────────────────────

        private static string Hex(long v, int width) => v.ToString("X" + width);
        private static string Hex(uint v, int width) => v.ToString("X" + width);
        private static string Hex(ulong v, int width) => v.ToString("X" + width);

        private static string Kv(string key, string value) => key + ": " + YamlScalar(value);
        private static string Flow(List<string> fields) => "{" + string.Join(", ", fields) + "}";

        private static string ToYamlValue(object s)
        {
            switch (s)
            {
                case bool b: return b ? "true" : "false";
                case uint ui: return ui.ToString("X8");
                case ulong ul: return ul.ToString("X9");
                case int i: return i.ToString();
                case long l: return l.ToString();
                case System.TimeSpan ts: return ts.ToString("hh\\:mm\\:ss\\.fff");
                default: return (s ?? "").ToString();
            }
        }

        private static string YamlScalar(string v)
        {
            if (v == null || v.Length == 0)
                return "\"\"";

            bool needQuote = false;
            if (v[0] == ' ' || v[v.Length - 1] == ' ')
                needQuote = true;
            else if ("!&*?|>%@`\"'#,[]{}:-".IndexOf(v[0]) >= 0)
                needQuote = true;
            else
            {
                foreach (char c in v)
                {
                    if (c == ',' || c == '{' || c == '}' || c == '[' || c == ']' || c == '"' || c == '\n' || c == '\t' || c == '\r')
                    { needQuote = true; break; }
                }
                if (!needQuote && (v.Contains(": ") || v.Contains(" #")))
                    needQuote = true;
            }

            if (!needQuote)
                return v;

            StringBuilder sb = new StringBuilder(v.Length + 4);
            sb.Append('"');
            foreach (char c in v)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
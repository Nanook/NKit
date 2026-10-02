// Self-contained WUA inspector — no NKit types needed.
// Parses the ZArchive footer and file tree directly.

static uint  U32BE(byte[] b, int o) => ((uint)b[o]<<24)|((uint)b[o+1]<<16)|((uint)b[o+2]<<8)|b[o+3];
static ulong U64BE(byte[] b, int o) { ulong v=0; for(int i=0;i<8;i++) v=(v<<8)|b[o+i]; return v; }
static ushort U16BE(byte[] b, int o) => (ushort)(((ushort)b[o]<<8)|b[o+1]);

static void Inspect(string path)
{
    const int FooterSize = 144;
    const int BlockSize  = 0x10000; // 64 KiB
    const int EntriesPerRecord = 16;
    const uint Magic   = 0x169F52D6u;
    const uint Version = 0x61BF3A01u;

    var info = new FileInfo(path);
    Console.WriteLine($"\n=== {info.Name} ({info.Length / 1024.0 / 1024.0:F1} MB on disk) ===");

    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    long fileSize = fs.Length;

    // Read footer
    var footer = new byte[FooterSize];
    fs.Seek(fileSize - FooterSize, SeekOrigin.Begin);
    fs.ReadExactly(footer);

    uint magic   = U32BE(footer, 140);
    uint version = U32BE(footer, 136);
    ulong total  = U64BE(footer, 128);
    Console.WriteLine($"Magic=0x{magic:X8}  Version=0x{version:X8}  Valid={magic==Magic && version==Version}  TotalSize={total}");

    long compOff  = (long)U64BE(footer,  0), compSize  = (long)U64BE(footer,  8);
    long recOff   = (long)U64BE(footer, 16), recSize   = (long)U64BE(footer, 24);
    long namesOff = (long)U64BE(footer, 32), namesSize = (long)U64BE(footer, 40);
    long treeOff  = (long)U64BE(footer, 48), treeSize  = (long)U64BE(footer, 56);
    Console.WriteLine($"CompData: offset={compOff} size={compSize/1024.0/1024:F1} MB");

    // Compute block count and sample some block compressed sizes
    int recCount = (int)(recSize / (8 + 2 * EntriesPerRecord));
    var recBuf = new byte[recSize];
    fs.Seek(recOff, SeekOrigin.Begin);
    fs.ReadExactly(recBuf);

    int totalBlocks = recCount * EntriesPerRecord; // approximate — last record may be partial
    long sumCompSizes = 0;
    int nonTrivial = 0;
    for (int r = 0; r < recCount; r++)
    {
        int roff = r * (8 + 2 * EntriesPerRecord);
        for (int b = 0; b < EntriesPerRecord; b++)
        {
            ushort sz1 = U16BE(recBuf, roff + 8 + b * 2); // stored as size-1
            int sz = sz1 + 1;
            sumCompSizes += sz;
            if (sz < BlockSize) nonTrivial++;
        }
    }
    // Actual block count from compSize
    Console.WriteLine($"OffsetRecords: {recCount} records → ≤{totalBlocks} blocks");
    Console.WriteLine($"Avg compressed block size: {sumCompSizes / (double)totalBlocks / 1024:F1} KB  (blocks < 64KB: {nonTrivial}/{totalBlocks})");

    // Read name table
    var nameBuf = new byte[namesSize];
    fs.Seek(namesOff, SeekOrigin.Begin);
    fs.ReadExactly(nameBuf);

    // Read file tree
    var treeBuf = new byte[treeSize];
    fs.Seek(treeOff, SeekOrigin.Begin);
    fs.ReadExactly(treeBuf);
    int entryCount = (int)(treeSize / 16);

    // Decode name table
    string GetName(int nameOff)
    {
        int len;
        int start;
        if ((nameBuf[nameOff] & 0x80) == 0)
        {
            len   = nameBuf[nameOff];
            start = nameOff + 1;
        }
        else
        {
            len   = (nameBuf[nameOff] & 0x7F) | (nameBuf[nameOff+1] << 7);
            start = nameOff + 2;
        }
        return System.Text.Encoding.Latin1.GetString(nameBuf, start, len);
    }

    // Decode entries
    (bool IsFile, string Name, int NameOff, long FileOffset, long FileSize, int ChildStart, int ChildCount)[] entries
        = new (bool, string, int, long, long, int, int)[entryCount];
    for (int i = 0; i < entryCount; i++)
    {
        int eoff = i * 16;
        uint nameAndType = U32BE(treeBuf, eoff);
        bool isFile = (nameAndType & 0x80000000u) != 0;
        int  nameOff = (int)(nameAndType & 0x7FFFFFFFu);
        string name = GetName(nameOff);
        if (isFile)
        {
            uint offLow  = U32BE(treeBuf, eoff+4);
            uint sizeLow = U32BE(treeBuf, eoff+8);
            uint high    = U32BE(treeBuf, eoff+12);
            long foff  = offLow  | (((long)(high & 0xFFFF)) << 32);
            long fsize = sizeLow | (((long)((high >> 16) & 0xFFFF)) << 32);
            entries[i] = (true, name, nameOff, foff, fsize, 0, 0);
        }
        else
        {
            int cs = (int)U32BE(treeBuf, eoff+4);
            int cc = (int)U32BE(treeBuf, eoff+8);
            entries[i] = (false, name, nameOff, 0, 0, cs, cc);
        }
    }

    // Enumerate all files recursively from entry 0
    var allFiles = new List<(string path, long size)>();
    void Walk(int idx, string prefix)
    {
        var e = entries[idx];
        for (int i = e.ChildStart; i < e.ChildStart + e.ChildCount; i++)
        {
            string p = prefix.Length == 0 ? entries[i].Name : prefix + "/" + entries[i].Name;
            if (entries[i].IsFile)
                allFiles.Add((p, entries[i].FileSize));
            else
                Walk(i, p);
        }
    }
    Walk(0, "");

    long totalUncomp = allFiles.Sum(f => f.size);
    Console.WriteLine($"Files: {allFiles.Count}  Uncompressed total: {totalUncomp / 1024.0 / 1024:F1} MB");
    Console.WriteLine($"Compression ratio: {(double)info.Length / totalUncomp * 100:F1}% of uncompressed  ({(1.0 - (double)info.Length / totalUncomp) * 100:F1}% space saved)");

    // Root dirs
    var rootDirs = entries[0].ChildCount > 0
        ? Enumerable.Range(entries[0].ChildStart, entries[0].ChildCount)
            .Where(i => !entries[i].IsFile).Select(i => entries[i].Name).ToList()
        : new List<string>();
    Console.WriteLine($"Root dirs: {string.Join(", ", rootDirs)}");

    // Group by depth-2 folder (title/code, title/content, title/meta)
    var byDir = allFiles
        .GroupBy(f => { var p = f.path.Split('/'); return p.Length > 1 ? p[1] : "(root)"; })
        .OrderBy(g => g.Key);

    Console.WriteLine();
    foreach (var group in byDir)
        Console.WriteLine($"  {group.Key,-12}  {group.Count(),4} files   {group.Sum(f=>f.size)/1024.0/1024,8:F1} MB");

    Console.WriteLine("\nFirst 15 files:");
    foreach (var (p, sz) in allFiles.Take(15))
        Console.WriteLine($"  {p}  ({sz:N0} B)");
}

Inspect(@"D:\NKitFiles\WiiU_Iso\Cocoto Magic Circus 2 (EU).wua");
Inspect(@"D:\NKitFiles\WiiU_WUA_Out\Cocoto Magic Circus 2 (Europe) (En,Fr,De,Es,It,Nl,Pt)_3.wua");
Console.WriteLine();

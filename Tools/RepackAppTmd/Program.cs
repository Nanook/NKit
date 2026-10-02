// Commands:
//   repack   <src-folder> <out-folder> [swap:<index>=<appfile>]
//   dump-fst <00000000.app> <title.tik> <out.txt>

if (args.Length < 3) { PrintUsage(); return 1; }

if (args[0] == "dump-fst")
{
    if (args.Length < 4) { PrintUsage(); return 1; }
    Nanook.NKit.DumpFst.Run(args[1], args[2], args[3]);
    return 0;
}

// repack (default, args[0] may be "repack" or the src path)
int argOff = args[0] == "repack" ? 1 : 0;
string src  = args[argOff];
string out_ = args[argOff + 1];

var swaps = new Dictionary<int, string>();
for (int i = argOff + 2; i < args.Length; i++)
{
    if (args[i].StartsWith("swap:", StringComparison.OrdinalIgnoreCase))
    {
        string val = args[i].Substring(5);
        int eq = val.IndexOf('=');
        if (eq > 0 && int.TryParse(val[..eq], out int idx))
            swaps[idx] = val[(eq + 1)..].Trim('"');
    }
}

if (!Directory.Exists(src)) { Console.WriteLine($"Source folder not found: {src}"); return 1; }

try { Nanook.NKit.RepackAppTmd.Run(src, out_, swaps); return 0; }
catch (Exception ex) { Console.WriteLine($"ERROR: {ex.Message}\n{ex.StackTrace}"); return 1; }

void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  RepackAppTmd repack   <src-folder> <out-folder> [swap:<idx>=<file>]");
    Console.WriteLine("  RepackAppTmd dump-fst <00000000.app> <title.tik> <out.txt>");
}

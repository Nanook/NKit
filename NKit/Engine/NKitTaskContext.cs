using Nanook.NKit.Dats;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nanook.NKit
{
    internal class NKitTaskContext
    {
        //ReqPatch   - Source image requires patching. nkit.iso/nkit.gcz / fix etc
        //ReqChk     - Generate checksums - e.g. to write in to nkit headers
        //FullScan   - Has the can output a full scan
        //IsLossy    - Is lossy output
        //IsExpand   - Output is full 
        //OutputType - Image or multi track
        //CanCrc     - Is able to calculate crc (even by combining)
        //CanHash    - Is able to calculate hash (in full continous write)

        // pcEngine is a plain-ISO / dual-format (iso/cue) iso9660 filesystem system, treated
        // identically to CDi/SegaCD/Saturn everywhere else (dedupe formatter, config defaults,
        // DataStoreAsIso). It belongs in the full iso9660-FS set so it gets Extract/Wipe and the
        // folderindex CUE/GDI routing like its peers.
        private const string _IsoFsSystems = "xbox/xbox360/ps1/ps2/ps3/psp/dreamcast/saturn/segacd/cdi/pcEngine/default";
        private const string _IsoSystems = _IsoFsSystems;
        private const string _AllSystems = "*"; // $"wii/gamecube/wiiu/{isoSystems}";
        private const string _ImgIdx = "image/folderindex";

        private enum StepDetail { Name, Config, ReqPatch, ReqChk, FullScan, IsLossy, IsFix, IsExpand, OutputType, CanCrc, CanHash }
        private static string[][] _StepDetail = new[]
        {
            new[] { "Convert-WiiGc-Lossless", "rvz[nkit]/wbfs[nkit]/ciso[nkit]", "n",      "y",    "y",      "n",     "n",   "n",      "image",       "n",    "n" }, //Writes header on completion so no CRC/Hash - CRC could be supported
            new[] { "Convert-WiiGc-Lossy",    "wbfs/ciso",                       "n",      "n",    "y",      "y",     "n",   "n",      "image",       "n",    "n" },
            new[] { "Convert-WiiU-Wux",       "wux",                             "n",      "n",    "y",      "n",     "n",   "n",      "image",       "n",    "n" },
            new[] { "Convert-WiiU-AppTmd",    "apptmd",                          "n",      "n",    "n",      "y",     "n",   "n",      "folderindex", "n",    "n" },
            new[] { "Expand-WiiU-AppTmd",     "apptmd",                          "n",      "n",    "n",      "n",     "n",   "y",      "folderindex", "y",    "y" },
            new[] { "Convert-Iso-CsoZso",     "cso/zso",                         "n",      "y",    "y",      "n",     "n",   "n",      "image",       "n",    "n" },
            new[] { "Convert-Iso-DecIso",     "deciso",                          "n",      "n",    "y",      "n",     "n",   "y",      "image",       "y",    "y" },
            new[] { "Convert-GdRom-CueGdi",   "gdromcue/gdromgdi",               "n",      "n",    "y",      "n",     "n",   "y",      "folderindex", "y",    "y" },
            new[] { "Convert-Iso-CueToc",     "cue/toc",                         "n",      "n",    "y",      "n",     "n",   "y",      "folderindex", "y",    "y" },
            new[] { "Convert-XBox-Xiso",      "xiso",                            "n",      "n",    "y",      "y",     "n",   "n",      "image",       "y",    "y" },
            new[] { "Convert-Image",          "iso",                             "n",      "n",    "y",      "n",     "n",   "y",      "image",       "y",    "y" },

            new[] { "Fix-WiiGc",              "iso",                             "n",      "n",    "n",      "n",     "y",   "y",      "image",       "y",    "n" },
            new[] { "Fix-Ps3",                "iso",                             "n",      "n",    "y",      "n",     "y",   "y",      "image",       "y",    "y" },

            new[] { "FixExtract-WiiGc",       "files",                           "n",      "n",    "n",      "n",     "n",   "n",      "files",       "n",    "n" },
            new[] { "FixExtract-Ps3",         "files",                           "n",      "n",    "y",      "n",     "n",   "n",      "files",       "n",    "n" },

            new[] { "Extract-WiiGc",          "folderfiles",                     "n",      "n",    "n",      "y",     "n",   "n",      "folderfiles", "n",    "n" },
            new[] { "Extract-WiiU",           "folderfiles",                     "n",      "n",    "n",      "y",     "n",   "n",      "folderfiles", "n",    "n" },
            new[] { "Extract-XBox",           "folderfiles",                     "n",      "n",    "n",      "y",     "n",   "n",      "folderfiles", "n",    "n" },
            new[] { "Extract-Iso",            "folderfiles",                     "n",      "n",    "n",      "y",     "n",   "n",      "folderfiles", "n",    "n" },

            new[] { "Dedupe-Image",           "files",                           "n",      "y",    "y",      "n",     "n",   "y",      "filestore",   "y",    "y" },

            new[] { "Expand-Image-Patch",     "iso",                             "y",      "n",    "y",      "n",     "n",   "y",      "image",       "y",    "n" },
            new[] { "Expand-GdRom-CueGdi",    "gdromcue/gdromgdi",               "n",      "n",    "y",      "n",     "n",   "y",      "folderindex", "y",    "y" },
            new[] { "Expand-Iso-CueToc",      "cue/toc",                         "n",      "n",    "y",      "n",     "n",   "y",      "folderindex", "y",    "y" },
            new[] { "Expand-Image",           "iso",                             "n",      "n",    "y",      "n",     "n",   "y",      "image",       "y",    "y" },
            new[] { "Expand-XBox",            "iso/xiso",                        "n",      "n",    "n",      "n",     "n",   "y",      "image",       "y",    "y" },

            new[] { "Scan-Image",             "scan",                            "n",      "n",    "y",      "n",     "n",   "y",      "scan",        "y",    "y" },
            new[] { "Scan-IsoGdChd",          "chdgdromcue/chdgdromgdi",         "n",      "n",    "y",      "n",     "n",   "y",      "folderindex", "y",    "y" },

            new[] { "Verify-Image",           "none",                            "n",      "n",    "y",      "n",     "n",   "y",      "none",        "y",    "y" },
            new[] { "Verify-IsoGdChd",        "chdgdromcue/chdgdromgdi",         "n",      "n",    "y",      "n",     "n",   "y",      "folderindex", "y",    "y" },

            new[] { "Wipe-Iso",               "none",                            "n",      "n",    "n",      "y",     "n",   "n",      "image",       "n",    "n" },
            new[] { "Wipe-WiiGc",             "none",                            "n",      "n",    "n",      "y",     "n",   "n",      "image",       "n",    "n" },
            new[] { "Wipe-WiiU",              "none",                            "n",      "n",    "n",      "y",     "n",   "n",      "image",       "n",    "n" },
            new[] { "Wipe-WiiU-AppTmd",       "none",                            "n",      "n",    "n",      "y",     "n",   "n",      "folderindex", "n",    "n" },
            new[] { "Wipe-IsoGdChd",          "chdgdromcue/chdgdromgdi",         "n",      "n",    "n",      "y",     "n",   "n",      "folderindex", "n",    "n" },
            new[] { "Wipe-IsoCueTocGdi",      "chdgdromcue/chdgdromgdi",         "n",      "n",    "n",      "y",     "n",   "n",      "folderindex", "n",    "n" },

            new[] { "NotSet-NotSupported",    "none",                            "n",      "n",    "n",      "n",     "n",   "n",      "none",        "n",    "n" },
        };
        private enum StepDef { StepType, Task, System, SrcType, Config, ReqPatch, PrmV, SrcCrcHash, InNKitScan, Dats, DatItem }
        private static string[][] _StepsDefs = new[]
        {
            //Wii / GameCube - Fix Then Convert experimental
            //new[] { @"Fix-WiiGc             (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
            //          Convert-WiiGc-Lossless(scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)                      
            //          Verify-Image          (scan:Y, vfy:datLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Fix",             "wii/gamecube", imgIdx,        "iso/",                            "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },

            //////////////////////
            // DEDUPE                                                                                             Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   wii gamecube
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DataStore,          ichk:Y, ochk:N, write:N, del:Y)", "Dedupe",          "wii/gamecube", "image",       "",                                "n",   "y/datLookup",   "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Dedupe",          "wii/gamecube", "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated rows — a decoded NKitAsIso source has
            //     _info.IsNkit=false so ReqPatch is never y; it takes the single-step Dedupe-Image
            //     rows above. (The Expand-Image-Patch pre-pass was for legacy raw NKit, which no
            //     longer reaches the Image un-decoded.)
            //   wiiu
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DataStore,          ichk:Y, ochk:N, write:N, del:Y)", "Dedupe",          "wiiu",         "image",       "",                                "n",   "y/datLookup",   "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Dedupe",          "wiiu",         "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   wiiu (folderindex / files mode)
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DataStore,          ichk:Y, ochk:N, write:N, del:Y)", "Dedupe",          "wiiu",         "folderindex", "",                                "n",   "y/datLookup",   "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Dedupe",          "wiiu",         "folderindex", "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   xbox / xbox360
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DataStore,          ichk:Y, ochk:N, write:N, del:Y)", "Dedupe",          "xbox/xbox360", "image",       "",                                "n",   "y/datLookup",   "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Dedupe",          "xbox/xbox360", "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   iso9660 systems (image)
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DataStore,          ichk:Y, ochk:N, write:N, del:Y)", "Dedupe",          _IsoSystems,    "image",       "",                                "n",   "y/datLookup",   "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Dedupe",          _IsoSystems,    "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   iso9660 systems (folderindex / CUE/GDI)
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DataStore,          ichk:Y, ochk:N, write:N, del:Y)", "Dedupe",          _IsoSystems,    "folderindex", "",                                "n",   "y/datLookup",   "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Dedupe-Image             (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Dedupe",          _IsoSystems,    "folderindex", "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Dedupe",          _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // FIX EXTRACT                                                                                        Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   wii gamecube
            new[] { @"FixExtract-WiiGc         (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "FixExtract",      "wii/gamecube", "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated row — decoded NKitAsIso is ReqPatch=n and
            //     takes the single-step FixExtract-WiiGc row above (no Expand-Image-Patch pre-pass).
            //   ps3
            new[] { @"FixExtract-Ps3           (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "FixExtract",      "ps3",          "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "FixExtract",      _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // EXTRACT                                                                                            Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   wii gamecube
            new[] { @"Extract-WiiGc            (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Extract",         "wii/gamecube", "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated row — decoded NKitAsIso is ReqPatch=n and
            //     takes the single-step Extract-WiiGc row above (no Expand-Image-Patch pre-pass).
            //   wiiu
            new[] { @"Extract-WiiU             (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Extract",         "wiiu",         _ImgIdx,        "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   xbox — single-step: archived XBox streams from the archive (BufferStream), so
            //   Extract-XBox walks the file system in one pass (no Expand-XBox pre-pass).
            new[] { @"Extract-XBox             (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Extract",         "xbox/xbox360", "image",       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   everything else
            new[] { @"Extract-Iso              (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Extract",         _IsoFsSystems,  _ImgIdx,       "",                                "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Extract",         _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //////////////////////
            // FIX                                                                                                Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   gamecube - ONE step: GcFixAsIso returns fully-corrected data at read time (header
            //   brute-forced to the fixfile header CRC, junk streamed), so the single Fix step
            //   reads the corrected image, verifies and creates the scan in one forward-only pass.
            new[] { @"Fix-WiiGc                (scan:Y, vfy:datLookup,          ichk:Y, ochk:N, write:Y, del:N)", "Fix",             "gamecube",     "image",       "iso/",                            "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Fix-WiiGc                (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Fix",             "gamecube",     "image",       "iso/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     gamecube patch (nkit.iso/gcz) - NKitAsIso returns corrected data at read; still one step.
            new[] { @"Fix-WiiGc                (scan:Y, vfy:datLookup,          ichk:Y, ochk:N, write:Y, del:N)", "Fix",             "gamecube",     "image",       "iso/",                            "y",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Fix-WiiGc                (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Fix",             "gamecube",     "image",       "iso/",                            "y",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   wii - TWO steps: the Fix step writes the file, but the update-partition brute-force
            //   + region/age calcs happen on the COMPLETED on-disc file, so verify/scan cannot run
            //   until a subsequent step reads the finished file. Scan is created by the Verify step.
            new[] { @"Fix-WiiGc                (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:datLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Fix",             "wii",          "image",       "iso/",                            "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Fix-WiiGc                (scan:N, vfy:datLookup,          ichk:N, ochk:N, write:Y, del:N)", "Fix",             "wii",          "image",       "iso/",                            "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Fix-WiiGc                (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Fix",             "wii",          "image",       "iso/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     wii patch (nkit.iso/gcz): no dedicated rows and NO Expand pre-pass — WiiFixAsIso
            //     wraps the (decoded or raw) nkit and the Fix step reads the corrected image
            //     directly, so it is ReqPatch=n and takes the single-step Fix-WiiGc / two-step
            //     Fix-WiiGc -> Verify-Image rows above. The two steps appear ONLY when a verify is
            //     required: Wii must fix to disk first, then re-read the completed file to verify
            //     (unlike GameCube, which fixes and verifies in one pass).
            //   ps3
            new[] { @"Fix-Ps3                  (scan:Y, vfy:InChecksums,        ichk:N, ochk:Y, write:Y, del:N)", "Fix",             "ps3",          "image",       "sfb",                             "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Fix-Ps3                  (scan:Y, vfy:ScanCompare,        ichk:N, ochk:Y, write:Y, del:N)", "Fix",             "ps3",          "image",       "sfb",                             "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Fix-Ps3                  (scan:Y, vfy:DatMatch,           ichk:N, ochk:Y, write:Y, del:N)", "Fix",             "ps3",          "image",       "sfb",                             "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Fix-Ps3                  (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:N)", "Fix",             "ps3",          "image",       "sfb",                             "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Fix-Ps3                  (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:N)", "Fix",             "ps3",          "image",       "sfb",                             "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Fix-Ps3                  (scan:Y, vfy:NoVerify,           ichk:N, ochk:Y, write:Y, del:N)", "Fix",             "ps3",          "image",       "sfb",                             "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Fix",             _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // CONVERT                                                                                            Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   wii gamecube - lossless
            new[] { @"Convert-WiiGc-Lossless   (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:InScanCompare,      ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "wii/gamecube", "image",       "rvz[nkit]/wbfs[nkit]/ciso[nkit]", "n",   "y",             "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Convert-WiiGc-Lossless   (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "wii/gamecube", "image",       "rvz[nkit]/wbfs[nkit]/ciso[nkit]", "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-WiiGc-Lossless   (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Convert",         "wii/gamecube", "image",       "rvz[nkit]/wbfs[nkit]/ciso[nkit]", "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated rows — decoded NKitAsIso is ReqPatch=n and
            //     converts single-step (Convert-WiiGc-Lossless -> Verify-Image) via the rows above.
            //   wii gamecube - lossy
            new[] { @"Convert-WiiGc-Lossy      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:N, del:Y)", "Convert",         "wii/gamecube", "image",       "wbfs/ciso",                       "n",   "y",             "y/n", "y",   "y/n", "y/n" },
            new[] { @"Convert-WiiGc-Lossy      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "wii/gamecube", "image",       "wbfs/ciso",                       "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-WiiGc-Lossy      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Convert",         "wii/gamecube", "image",       "wbfs/ciso",                       "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz) - lossy: no dedicated rows — decoded NKitAsIso is ReqPatch=n
            //     and converts single-step (Convert-WiiGc-Lossy -> Verify-Image) via the rows above.
            //   wiiu - image
            new[] { @"Convert-WiiU-Wux         (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:InScanCompare,      ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "wiiu",         "image",       "wux",                             "n",   "y",             "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Convert-WiiU-Wux         (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "wiiu",         "image",       "wux",                             "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-WiiU-Wux         (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Convert",         "wiiu",         "image",       "wux",                             "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   wiiu - apptmd - lossy 
            new[] { @"Convert-WiiU-AppTmd      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Convert",         "wiiu",         _ImgIdx,       "apptmd",                          "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   wiiu - apptmd - not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Convert",         "wiiu",         "folderindex", "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   ps3 - decrypt
            new[] { @"Convert-Iso-DecIso       (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:InScanCompare,      ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "ps3",          "image",       "deciso",                          "n",   "y",             "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Convert-Iso-DecIso       (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "ps3",          "image",       "deciso",                          "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-Iso-DecIso       (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Convert",         "ps3",          "image",       "deciso",                          "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   dreamcast
            new[] { @"Convert-GdRom-CueGdi     (scan:Y, vfy:NoVerify,           ichk:N, ochk:Y, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Convert",         "dreamcast",    "folderindex", "gdromgdi/gdromcue",               "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-GdRom-CueGdi     (scan:Y, vfy:NoVerify,           ichk:N, ochk:Y, write:Y, del:N)", "Convert",         "dreamcast",    "folderindex", "gdromgdi/gdromcue",               "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   xbox
            new[] { @"Convert-XBox-Xiso        (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Convert",         "xbox",         "image",       "xiso",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   generic folderindex
            new[] { @"Convert-Iso-CueToc       (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Convert-Iso-CueToc       (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Convert",         _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Convert-Iso-CueToc       (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Convert-Iso-CueToc       (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Convert-Iso-CueToc       (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "folderindex", "cue/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-Iso-CueToc       (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Convert",         _AllSystems,    "folderindex", "cue/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   generic image                                                                                                                                          
            new[] { @"Convert-Iso-CsoZso       (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:InScanCompare,      ichk:Y, ochk:N, write:N, del:Y)", "Convert",         _IsoSystems,    "image",       "cso/cso2/zso",                    "n",   "y",             "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Convert-Iso-CsoZso       (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Convert",         _IsoSystems,    "image",       "cso/cso2/zso",                    "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-Iso-CsoZso       (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:Y, del:N)", "Convert",         _IsoSystems,    "image",       "cso/cso2/zso",                    "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   generic image
            new[] { @"Convert-Image            (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "image",       "iso/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Convert-Image            (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Convert",         _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Convert-Image            (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Convert-Image            (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Convert-Image            (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Convert",         _AllSystems,    "image",       "iso/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-Image            (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Convert",         _AllSystems,    "image",       "iso/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated rows — a decoded NKitAsIso source is
            //     ReqPatch=n and converts to iso single-step via the Convert-Image rows above.
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Convert",         _AllSystems,    _ImgIdx,        "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // EXPAND                                                                                             Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   wiiu - apptmd
            new[] { @"Expand-WiiU-AppTmd       (scan:N, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "wiiu",         "image/folderindex", "apptmd",                    "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-WiiU-AppTmd       (scan:N, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Expand",          "wiiu",         "image/folderindex", "apptmd",                    "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-WiiU-AppTmd       (scan:N, vfy:DatMatch,           ichk:N, ochk:N, write:Y, del:Y)", "Expand",          "wiiu",         "image/folderindex", "apptmd",                    "n",   "y",             "n",   "n",   "y",   "y" },
            new[] { @"Expand-WiiU-AppTmd       (scan:N, vfy:DatLookup,          ichk:N, ochk:N, write:Y, del:N)", "Expand",          "wiiu",         "image/folderindex", "apptmd",                    "n",   "y",             "n",   "n",   "y",   "n" },
            new[] { @"Expand-WiiU-AppTmd       (scan:N, vfy:DatLookup,          ichk:N, ochk:N, write:Y, del:N)", "Expand",          "wiiu",         "image/folderindex", "apptmd",                    "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-WiiU-AppTmd       (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "wiiu",         "image/folderindex", "apptmd",                    "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   dreamcast
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:N, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Expand",          "dreamcast",    "folderindex", "chdgdromcue",                     "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "chdgdromcue",                     "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   xbox / xbox360 - xiso output
            new[] { @"Expand-XBox              (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "xiso",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-XBox              (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Expand",          "xbox/xbox360", "image",       "xiso",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-XBox              (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "xiso",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Expand-XBox              (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "xiso",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Expand-XBox              (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "xiso",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-XBox              (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "xbox/xbox360", "image",       "xiso",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   xbox / xbox360 - iso output (default)
            new[] { @"Expand-XBox              (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "iso/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-XBox              (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Expand",          "xbox/xbox360", "image",       "iso/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-XBox              (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Expand-XBox              (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Expand-XBox              (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          "xbox/xbox360", "image",       "iso/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-XBox              (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "xbox/xbox360", "image",       "iso/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   generic folderindex
            new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   generic image
            new[] { @"Expand-Image             (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-Image             (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-Image             (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Expand-Image             (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Expand-Image             (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-Image             (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            ////   dreamcast
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:InChecksums,        ichk:N, ochk:Y, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "chdgdromcue",                     "n",   "y/datLookup",   "y/n", "y/n", "y",   "y/n" },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "chdgdromcue",                     "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:InChecksums,        ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:ScanCompare,        ichk:N, ochk:Y, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "n",   "y",   "y/n", "y/n" },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:DatMatch,           ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "n",   "n",   "y",   "y"   },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "y",             "n",   "n",   "y",   "n"   },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            //new[] { @"Expand-GdRom-CueGdi      (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          "dreamcast",    "folderindex", "gdromcue",                        "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            ////   generic folderindex
            //new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:InChecksums,        ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            //new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:ScanCompare,        ichk:N, ochk:Y, write:Y, del:N)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            //new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:DatMatch,           ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            //new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            //new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            //new[] { @"Expand-Iso-CueToc        (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "folderindex", "cue/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            ////   generic image
            //new[] { @"Expand-Image             (scan:Y, vfy:InChecksums,        ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "y",   "y/n", "y/n", "y/n" },
            //new[] { @"Expand-Image             (scan:Y, vfy:ScanCompare,        ichk:N, ochk:Y, write:Y, del:N)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "y",   "y/n", "y/n" },
            //new[] { @"Expand-Image             (scan:Y, vfy:DatMatch,           ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "y"   },
            //new[] { @"Expand-Image             (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "y",             "n",   "n",   "y",   "n"   },
            //new[] { @"Expand-Image             (scan:Y, vfy:DatLookup,          ichk:N, ochk:Y, write:Y, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            //new[] { @"Expand-Image             (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "image",       "iso/",                            "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz)
            new[] { @"Expand-Image-Patch       (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:N, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "y",   "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Expand-Image-Patch       (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:N, del:N)", "Expand",          _AllSystems,    "image",       "iso/",                            "y",   "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Expand-Image-Patch       (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:N, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "y",   "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Expand-Image-Patch       (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:N, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "y",   "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Expand-Image-Patch       (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:Y)", "Expand",          _AllSystems,    "image",       "iso/",                            "y",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Expand-Image-Patch       (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Expand",          _AllSystems,    "image",       "iso/",                            "y",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Expand",          _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // SCAN                                                                                               Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   dreamcast gdrom
            new[] { @"Scan-Image               (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:N, del:N)", "Scan",            "dreamcast",    "folderindex", "chdgdromcue",                     "y/n", "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Convert-GdRom-CueGdi     (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Scan-Image               (scan:Y, vfy:ScanCompare,        ichk:Y, ochk:N, write:N, del:N)", "Scan",            "dreamcast",    "folderindex", "chdgdromcue",                     "y/n", "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Convert-GdRom-CueGdi     (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Scan-Image               (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:N, del:N)", "Scan",            "dreamcast",    "folderindex", "chdgdromcue",                     "y/n", "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Convert-GdRom-CueGdi     (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Scan-Image               (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:N)", "Scan",            "dreamcast",    "folderindex", "chdgdromcue",                     "y/n", "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Convert-GdRom-CueGdi     (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Scan-Image               (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:N)", "Scan",            "dreamcast",    "folderindex", "chdgdromcue",                     "y/n", "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Convert-GdRom-CueGdi     (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Scan-Image               (scan:Y, vfy:NoVerify,           ichk:Y, ochk:N, write:N, del:N)", "Scan",            "dreamcast",    "folderindex", "chdgdromcue",                     "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated row — decoded NKitAsIso is ReqPatch=n and
            //     scans single-step via the generic Scan-Image rows below.
            //   xbox archive: no dedicated rows — archived XBox streams from the archive (via
            //   DefaultAsIso + BufferStream) and takes the generic single-step Scan-Image rows
            //   below. The old Expand-XBox pre-pass is gone (CalculateConfig no longer emits 'arc').
            //   generic folderimage and image
            new[] { @"Scan-Image               (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "scan",                            "y/n", "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Scan-Image               (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "scan",                            "y/n", "y",             "n",   "y",   "y/n", "y/n" },
            new[] { @"Scan-Image               (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "scan",                            "y/n", "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Scan-Image               (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "scan",                            "y/n", "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Scan-Image               (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "scan",                            "n",   "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Scan-Image               (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "scan",                            "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Scan",            _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // VERIFY                                                                                             Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   dreamcast gdrom
            new[] { @"Verify-IsoGdChd          (scan:N, vfy:InChecksums,        ichk:Y, ochk:N, write:N, del:N)", "Verify",          "dreamcast",    "folderindex", "chdgdromnone",                    "y/n", "y",             "y",   "y/n", "y/n", "y/n" },
            new[] { @"Convert-GdRom-CueGdi     (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)
                      Verify-Image             (scan:Y, vfy:ScanCompare,        ichk:N, ochk:Y, write:N, del:N)", "Verify",          "dreamcast",    "folderindex", "chdgdromnone",                    "y/n", "y",             "y/n", "y",   "y/n", "y/n" },
            new[] { @"Verify-IsoGdChd          (scan:N, vfy:DatMatch,           ichk:N, ochk:Y, write:N, del:N)", "Verify",          "dreamcast",    "folderindex", "chdgdromnone",                    "y/n", "y",             "n",   "n",   "y",   "y"   },
            new[] { @"Verify-IsoGdChd          (scan:N, vfy:DatLookup,          ichk:N, ochk:Y, write:N, del:N)", "Verify",          "dreamcast",    "folderindex", "chdgdromnone",                    "y/n", "y",             "n",   "n",   "y",   "n"   },
            new[] { @"Verify-IsoGdChd          (scan:N, vfy:DatLookup,          ichk:N, ochk:Y, write:N, del:N)", "Verify",          "dreamcast",    "folderindex", "chdgdromnone",                    "y/n", "datLookup",     "y/n", "y/n", "y",   "y/n" },
            new[] { @"Verify-IsoGdChd          (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Verify",          "dreamcast",    "folderindex", "chdgdromnone",                    "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated row — decoded NKitAsIso is ReqPatch=n and
            //     verifies single-step via the generic Verify-Image rows below.
            //   generic folderimage and image
            new[] { @"Verify-Image             (scan:Y, vfy:InChecksums,        ichk:Y, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "none",                           "y/n", "y/n",           "y",   "y/n", "y/n", "y/n" },
            new[] { @"Verify-Image             (scan:Y, vfy:ScanCompare,        ichk:N, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "none",                           "y/n", "y/n",           "n",   "y",   "y/n", "y/n" },
            new[] { @"Verify-Image             (scan:Y, vfy:DatMatch,           ichk:Y, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "none",                           "y/n", "y/n",           "n",   "n",   "y",   "y"   },
            new[] { @"Verify-Image             (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "none",                           "y/n", "y/n",           "n",   "n",   "y",   "n"   },
            new[] { @"Verify-Image             (scan:Y, vfy:DatLookup,          ichk:Y, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "none",                           "n",   "n/datLookup",   "y/n", "y/n", "y",   "y/n" },
            new[] { @"Verify-Image             (scan:Y, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "none",                           "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Verify",          _AllSystems,    _ImgIdx,        "*",                              "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },

            //////////////////////
            // WIPE                                                                                               Task,              System,         SrcType,       Config,                            Ptch,  PrmV,            Chk,   Scan,  Dats,  DatItem
            //   Wii/Gc/WiiU
            new[] { @"Wipe-WiiGc               (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Wipe",            "wii/gamecube", "image",       "*",                               "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Wipe-WiiU                (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Wipe",            "wiiu",         "image",       "*",                               "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Wipe-WiiU-AppTmd         (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Wipe",            "wiiu",         "folderindex", "*",                               "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //     patch (nkit.iso/gcz): no dedicated row — decoded NKitAsIso is ReqPatch=n and
            //     wipes single-step via the Wipe-WiiGc row above.
            //   xbox archive: no dedicated row — archived XBox streams from the archive
            //   (BufferStream), so Wipe-Iso operates in one pass (no Expand-XBox pre-pass). It
            //   takes the generic image Wipe-Iso row below.
            //   generic folderimage and image
            new[] { @"Wipe-IsoGdChd            (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Wipe",            _IsoFsSystems,  "folderindex", "chdgdromcue",                     "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Wipe-IsoCueTocGdi        (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Wipe",            _IsoFsSystems,  "folderindex", "*",                               "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            new[] { @"Wipe-Iso                 (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:Y, del:N)", "Wipe",            _IsoFsSystems,  "image",       "*",                               "n",   "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
            //   not supported
            new[] { @"NotSet-NotSupported      (scan:N, vfy:NoVerify,           ichk:N, ochk:N, write:N, del:N)", "Wipe",            _AllSystems,    _ImgIdx,       "*",                               "y/n", "n/y/datLookup", "y/n", "y/n", "y/n", "y/n" },
        };

        public NKitTaskContext(AppSettings appSettings, SourceFile sourceFile, Action<string, LogLevel> consoleLog, bool dynamicConsole = false)
        {
            this.AppSettings = appSettings;
            this.Steps = new List<NKitStepContext>();
            this.HostLog = this.AppSettings.GetLog(consoleLog, dynamicConsole);
            this.Log = this.HostLog;
            this.TaskType = this.AppSettings.TaskType;
            this.Steps.Add(new NKitStepContext(this, 0)); //add a first step for the initial image to use
            this.Steps[0].Initialise(sourceFile); //enough to allow first time image reading
        }

        public string SourceImageName => this.Steps[0].SourceFile.Name;

        /// <summary>1-based index of this image within the host's batch, or 0 when unknown.
        /// Supplied by the host (the CLI/UI loops the scanned images); the library only knows one
        /// image at a time. Carried on the Title summary line as the X of the <c>X/Y</c> counter.</summary>
        public int ImageIndex { get; internal set; }

        /// <summary>Total images in the host's batch, or 0 when unknown. The Y of <c>X/Y</c>.</summary>
        public int ImageTotal { get; internal set; }

        public List<NKitStepContext> Steps { get; private set; }
        public SystemType SystemType { get; private set; }
        public TaskType TaskType { get; private set; }
        public DatManager DatManager => this.AppSettings.DatManager;
        public AppSettings AppSettings { get; private set; }
        public SystemSettings Settings { get; private set; }
        public bool VerifyEnabled { get; private set; }
        public bool ScanEnabled { get; private set; }
        public ILogScope Log { get; internal set; }

        // The concrete host Log (owns the bus, progress dot-stream, file summary). The public Log
        // above exposes it as ILogScope for logging; HostLog is for host-only concerns (Progress).
        // Both point at the same instance. A2: this is the internal seam that keeps the host bits
        // reachable while call sites see only ILogScope.
        internal Log HostLog { get; set; }
        public CancellationToken? CancelToken { get; internal set; }

        internal void Initialise(SystemType systemType)
        {
            this.SystemType = systemType;
            this.Settings = AppSettings[this.SystemType];
            setStep();
            this.Settings.Initialise(this.SystemType, this.TaskType, this.Log, this.DatManager);
            this.AppSettings.DatManager.Register(this.SystemType, this.Settings.Dat); //register the expanded dat for this image
            this.AppSettings.DatManager.LoadDats(this.SystemType);
        }

        private void setStep()
        {
            //used to handle convertswap
        }

        private NKitStepContext addTask(IStepInfo info, string stepConfig)
        {
            bool fillStep0 = this.Steps.Count == 1 && this.Steps[0].Step == null;
            NKitStepContext stepContext = fillStep0 ? this.Steps[0] : new NKitStepContext(this, this.Steps.Count);
            stepContext.Setup(this.SystemType, info, stepConfig);

            if (!fillStep0)
                this.Steps.Add(stepContext);
            return stepContext;
        }

        internal void CreateSteps(IStepsImageInfo imageInfo)
        {
            DatItem datMatch = this.DatManager?.FindByName(this.Steps[0].SourceFile.Name);
            Scan inScan = this.AppSettings[this.SystemType]?.Lookup.GetNKitScan(this.Steps[0].SourceFile.ImageFiles[0].FileName);

            // Normalise ALL source facts once, before the lookup. SourceProfile absorbs the former
            // config-string selection (#1), the dual-format split (#2), and the whole CalculateConfig
            // per-system switch (#6-#10). The table below is then a pure lookup with no post-hoc
            // input substitution. See NKitVault "TaskContext Firewall Simplification".
            SourceProfile profile = SourceProfile.From(this.TaskType, this.SystemType, this.Steps[0].SourceFile, this.Steps[0].ImageInfo, this.Settings);

            logCalcConfig(profile);

            CreateSteps(imageInfo, profile.SrcType, datMatch, inScan, profile.Config, profile.TaskConfig, this.DatManager.ItemsCount() != 0);
        }

        // [Config] [CalcConfig] Detail: the derived out-format token + key inputs that produced it.
        // Moved out of the (now deleted) CalculateConfig; the token is the _StepsDefs discriminator, so
        // when the lookup fails this line shows exactly HOW the config was derived.
        private void logCalcConfig(SourceProfile profile)
        {
            ILogScope cfgScope = this.Log?.ScopeFor(LogScopes.Config);
            if (cfgScope != null && cfgScope.IsEnabled(LogLevel.Detail))
                cfgScope.Log(LogLevel.Detail,
                    $"{LogScopes.Tag(LogScopes.CalcConfig)}task {this.TaskType} system {this.SystemType} configString '{profile.TaskConfig ?? ""}'"
                    + $" isGdRom {(profile.IsGdRom ? "y" : "n")} srcImageType {this.Steps?[0]?.SourceFile?.ImageType}"
                    + $" folderIndex {(profile.IsFolderIndex ? "y" : "n")}"
                    + $" -> config '{profile.Config}'");
        }

        internal void CreateSteps(IStepsImageInfo imageInfo, string srcType, DatItem datMatch, Scan inScan, string config, string taskConfig, bool hasDats)
        {
            string steps = getStepsValue(imageInfo, srcType, config, datMatch, inScan, hasDats);

            if (steps != null)
            {
                MatchCollection mc = Regex.Matches(steps, @"([a-z0-9-]+) *\(scan:([YN]), +vfy:([a-z>]+), +ichk:([YN]), +ochk:([YN]), +write:([YN]), +del:([YN]) *\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in mc)
                {
                    StepInfo info = new StepInfo()
                    {
                        Name = m.Groups[1].Value,
                        StepType = (TaskType)Enum.Parse(typeof(TaskType), m.Groups[1].Value.Split('-')[0], true),
                        CreateScan = m.Groups[2].Value.ToLower() == "y",
                        // #3 (was a post-lookup swap): the effective verify method is DERIVED at the
                        // decision point from the source fact, not mutated afterwards. The table reuses
                        // the InChecksums rows for DataStore sources; resolveVerifyMethod maps them.
                        VerifyMethod = resolveVerifyMethod((VerifyMethod)Enum.Parse(typeof(VerifyMethod), m.Groups[3].Value, true), this.Steps[0].SourceFile?.IsDataStore == true),
                        CreateInChecksum = m.Groups[4].Value.ToLower() == "y",
                        CreateOutChecksum = m.Groups[5].Value.ToLower() == "y",
                        WriteImage = m.Groups[6].Value.ToLower() == "y",
                        DeleteSourceCandidate = m.Groups[7].Value.ToLower() == "y",
                        DatMatch = datMatch,
                        SrcScan = inScan,
                        ImageConfig = config,
                        SrcParts = new Parts(imageInfo.Checksums)
                    };

                    string[] detail = _StepDetail.FirstOrDefault(a => a[(int)StepDetail.Name] == info.Name);
                    if (detail != null)
                    {
                        info.ReqPatch = detail[(int)StepDetail.ReqPatch] == "y";
                        info.ReqChk = detail[(int)StepDetail.ReqChk] == "y";
                        info.FullScan = detail[(int)StepDetail.FullScan] == "y";
                        info.IsLossy = detail[(int)StepDetail.IsLossy] == "y";
                        info.IsFix = detail[(int)StepDetail.IsFix] == "y";
                        info.IsExpand = detail[(int)StepDetail.IsExpand] == "y";
                        info.OutputType = (OutputType)Enum.Parse(typeof(OutputType), detail[(int)StepDetail.OutputType], true);
                        info.CanCrc = detail[(int)StepDetail.CanCrc] == "y";
                        info.CanHash = detail[(int)StepDetail.CanHash] == "y";
                        info.Config = detail[(int)StepDetail.Config];
                    }

                    // #4 (was a post-lookup override): the checksum DIRECTION is a pure consequence of
                    // the verify method, not an authored value — derive it so no row can disagree.
                    applyVerifyChecksumDirection(info);

                    addTask(info, resolveStepConfig(info, taskConfig));
                }

                this.VerifyEnabled = this.Steps.Any(a => a.StepInfo.VerifyMethod != VerifyMethod.NoVerify);
                this.ScanEnabled = this.Steps.Any(a => a.StepInfo.CreateScan);
            }
        }

        // #3 — resolve the EFFECTIVE verify method from the table's method + the source fact, at the
        // decision point (was a post-lookup mutation). A DataStore source reuses the table's InChecksums
        // rows but must verify against the stored DataStore values instead. Every other method passes
        // through unchanged. Pairs with applyVerifyChecksumDirection (which derives ochk/ichk from the
        // resolved method) so the whole verify decision is fact-driven, never substituted afterwards.
        internal static VerifyMethod resolveVerifyMethod(VerifyMethod tableMethod, bool isDataStore)
            => tableMethod == VerifyMethod.InChecksums && isDataStore ? VerifyMethod.DataStore : tableMethod;

        // #4 — derive the checksum direction from the verify method (was a look-up-then-override).
        // A DataStore verify compares the produced OUTPUT against the stored values, so it calculates
        // output checksums (ochk) rather than input checksums (ichk). Deriving this means the table
        // never has to author (and never can contradict) the direction.
        private static void applyVerifyChecksumDirection(StepInfo info)
        {
            if (info.VerifyMethod == VerifyMethod.DataStore)
            {
                info.CreateOutChecksum = true;
                info.CreateInChecksum = false;
            }
        }

        // #5 — the per-step-kind config token (was an inline "iso" hack). Named so the branch reads as
        // intent: an Expand/Wipe/Fix that outputs an Image but whose configured expand format is a
        // folder-index format (cue) must fall back to iso for the image write.
        private string resolveStepConfig(StepInfo info, string taskConfig)
        {
            if (info.StepType == TaskType.Convert || info.StepType == TaskType.Extract)
                return taskConfig;
            // Wipe outputs the SAME formats as Expand (it is expand-plus-content/key-wiping), so it
            // resolves its output extension identically to Expand/Fix.
            if (info.StepType == TaskType.Expand || info.StepType == TaskType.Fix || info.StepType == TaskType.Wipe)
            {
                if (info.OutputType == OutputType.FolderIndex)
                    return this.Settings.ExpandIndexExtension;
                if (info.OutputType == OutputType.Image && this.Settings.ExpandExtension == "cue")
                    return "iso"; // image output can't take a folder-index (cue) format
                return this.Settings.ExpandExtension;
            }
            if (info.StepType == TaskType.Scan)
                return NKitTask.ScanExt;
            return "";
        }

        // Config is calculated from image information to allow the step-table lookup. The per-system
        // switch (workarounds #6-#10) now lives in SourceProfile.CalculateConfig — this thin shim
        // computes the folder-index fact from local state and delegates, so the test harness (which
        // supplies its own pre-selected configString) exercises the exact same logic.
        internal string CalculateConfig(string configString, bool isGdRom)
        {
            bool isFolderIndex = this.Steps?[0]?.SourceFile?.IndexFile != null
                              || (this.Steps?[0]?.ImageInfo?.IsFolderIndex ?? false);
            return SourceProfile.CalculateConfig(this.TaskType, this.SystemType, configString, isGdRom, isFolderIndex, this.Steps?[0]?.SourceFile);
        }

        private string getStepsValue(IStepsImageInfo imageInfo, string srcType, string config, DatItem datMatch, Scan inScan, bool hasDats)
        {
            string s = _StepsDefs.Where(a => eq(a, StepDef.Task, this.TaskType.ToString().ToLower()))
                                 .Where(a => eq(a, StepDef.System, this.SystemType.ToString().ToLower()))
                                 .FirstOrDefault(a => eq(a, StepDef.SrcType, srcType.ToLower())
                                                   && eq(a, StepDef.Config, config.ToLower())
                                                   && eq(a, StepDef.ReqPatch, imageInfo.ReqPatch)
                                                   && eq(a, StepDef.PrmV, this.Settings.V.ToString().ToLower())
                                                   && eq(a, StepDef.SrcCrcHash, imageInfo.Checksums.Count != 0)
                                                   && eq(a, StepDef.InNKitScan, inScan != null)
                                                   && eq(a, StepDef.Dats, hasDats)
                                                   && eq(a, StepDef.DatItem, datMatch != null)
                                                 )?[(int)StepDef.StepType];

            if (s == null)
                throw new HandledException($"No processing options found for :: Task: {this.TaskType.ToString().ToLower()}, System: {this.SystemType.ToString().ToLower()}, SrcType: {srcType.ToLower()}, OutFormat: {config.ToLower()}, ReqPatch: {(imageInfo.ReqPatch ? "y" : "n")}, PrmV: {this.Settings.V.ToString().ToLower()}, SrcCrcHash: {(imageInfo.Checksums.Count != 0 ? "y" : "n")}, InNKitScan: {(inScan != null ? "y" : "n")}, Dats: {(this.DatManager.ItemsCount() != 0 ? "y" : "n")}, DatItem: {(datMatch != null ? "y" : "n")}");
#if DEBUG
            Debug.WriteLine($"Found: {Regex.Replace(s.Replace("\r\n", "#"), "  ", "")} :: Task: {this.TaskType.ToString().ToLower()}, System: {this.SystemType.ToString().ToLower()}, SrcType: {srcType.ToLower()}, Config: {config.ToLower()}, ReqPatch: {(imageInfo.ReqPatch ? "y" : "n")}, PrmV: {this.Settings.V.ToString().ToLower()}, SrcCrcHash: {(imageInfo.Checksums.Count != 0 ? "y" : "n")}, InNKitScan: {(inScan != null ? "y" : "n")}, Dats: {(this.DatManager.ItemsCount() != 0 ? "y" : "n")}, DatItem: {(datMatch != null ? "y" : "n")}");
#endif
            this.Log.Trace(() => $"Found: {Regex.Replace(s.Replace("\r\n", "#"), "  ", "")} :: Task: {this.TaskType.ToString().ToLower()}, System: {this.SystemType.ToString().ToLower()}, SrcType: {srcType.ToLower()}, OutFormat: {config.ToLower()}, ReqPatch: {(imageInfo.ReqPatch ? "y" : "n")}, PrmV: {this.Settings.V.ToString().ToLower()}, SrcCrcHash: {(imageInfo.Checksums.Count != 0 ? "y" : "n")}, InNKitScan: {(inScan != null ? "y" : "n")}, Dats: {(this.DatManager.ItemsCount() != 0 ? "y" : "n")}, DatItem: {(datMatch != null ? "y" : "n")}");

            // Detail (NKitLog): "why did this task pick these steps" — the step chain chosen plus the
            // discriminators that selected it. Promoted from the legacy Debug line above (which the
            // Log adapter mirrors at Trace) to Detail on the [Task] scope, so it shows at the file's
            // default Detail level and is attributable to the task-selection stage.
            ILogScope taskScope = this.Log?.ScopeFor(LogScopes.Params); // "[Params]" tag
            if (taskScope != null && taskScope.IsEnabled(LogLevel.Detail))
            {
                string steps = Regex.Replace(s.Replace("\r\n", "#"), "  ", "");
                taskScope.Log(LogLevel.Detail,
                    $"Step chain [{steps}] :: Task:{this.TaskType.ToString().ToLower()} System:{this.SystemType.ToString().ToLower()} SrcType:{srcType.ToLower()} OutFormat:{config.ToLower()} ReqPatch:{(imageInfo.ReqPatch ? "y" : "n")} PrmV:{this.Settings.V.ToString().ToLower()} SrcCrcHash:{(imageInfo.Checksums.Count != 0 ? "y" : "n")} InNKitScan:{(inScan != null ? "y" : "n")} Dats:{(this.DatManager.ItemsCount() != 0 ? "y" : "n")} DatItem:{(datMatch != null ? "y" : "n")}");
            }
            return s;
        }

        private bool eq(string[] vals, StepDef col, string val)
        {
            if (vals[(int)col] == "*")
                return true;
            bool res = vals[(int)col].ToLower().Split('/').Contains(val);
            return res;
        }
        private bool eq(string[] vals, StepDef col, bool val) => vals[(int)col].ToLower().Split('/').Contains(val ? "y" : "n");

        // ── Capability derivation (drives TaskCapabilities / interactive CLI) ────────────────
        // A (task, system) is SUPPORTED when at least one row in _StepsDefs matches that task and
        // system with a step chain that is NOT the NotSet-NotSupported sentinel. The System column
        // may be a '/'-group or the '*' wildcard, resolved via the same eq() matching used at run
        // time. This reads the private table without exposing it.
        internal static bool IsTaskSupportedInternal(TaskType task, SystemType system)
        {
            string t = task.ToString().ToLower();
            string sys = system.ToString().ToLower();
            foreach (string[] row in _StepsDefs)
            {
                if (!colMatch(row, StepDef.Task, t)) continue;
                if (!colMatch(row, StepDef.System, sys)) continue;
                string stepType = row[(int)StepDef.StepType];
                if (stepType.IndexOf("NotSet-NotSupported", StringComparison.OrdinalIgnoreCase) < 0)
                    return true;
            }
            return false;
        }

        // Static equivalent of eq() for the derivation (no instance state needed).
        private static bool colMatch(string[] vals, StepDef col, string val)
        {
            string cell = vals[(int)col];
            if (cell == "*") return true;
            return cell.ToLower().Split('/').Contains(val);
        }

    }

}
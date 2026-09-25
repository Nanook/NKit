using Nanook.NKit;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskStepsNotSupportedTests
    {
        private const string _NotSupportedResult = "NotSet-NotSupported";

        //                  System,      srcFormats,                                                      convert
        [Theory]
        [InlineData("001", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "isodec")]
        [InlineData("002", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "gcz")]
        [InlineData("003", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "wia")]
        [InlineData("004", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "nkitiso")]
        [InlineData("005", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "nkitgcz")]
        [InlineData("006", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "cue")]
        [InlineData("007", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "gdi")]
        [InlineData("008", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "wux")]
        [InlineData("009", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "app")]
        [InlineData("010", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "cso")]
        [InlineData("011", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "zso")]
        [InlineData("012", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "deciso")]
        [InlineData("013", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "dax")]
        [InlineData("014", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "jso")]
        [InlineData("015", "gamecube", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "chd")]
        [InlineData("016", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "isodec")]
        [InlineData("017", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "gcz")]
        [InlineData("018", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "wia")]
        [InlineData("019", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "nkitiso")]
        [InlineData("020", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "nkitgcz")]
        [InlineData("021", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "cue")]
        [InlineData("022", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "gdi")]
        [InlineData("023", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "wux")]
        [InlineData("024", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "app")]
        [InlineData("025", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "cso")]
        [InlineData("026", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "zso")]
        [InlineData("027", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "deciso")]
        [InlineData("028", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "dax")]
        [InlineData("029", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "jso")]
        [InlineData("030", "wii", ".iso,.iso.dec,.gcz,.ciso,.wbfs,.wia,.rvz,.nkit.iso,.nkit.gcz", "chd")]
        [InlineData("031", "wiiu", ".iso,.wud,.wux", "isodec")]
        [InlineData("032", "wiiu", ".iso,.wud,.wux", "gcz")]
        [InlineData("033", "wiiu", ".iso,.wud,.wux", "ciso")]
        [InlineData("034", "wiiu", ".iso,.wud,.wux", "wbfs")]
        [InlineData("035", "wiiu", ".iso,.wud,.wux", "wia")]
        [InlineData("036", "wiiu", ".iso,.wud,.wux", "rvz")]
        [InlineData("037", "wiiu", ".iso,.wud,.wux", "nkitiso")]
        [InlineData("038", "wiiu", ".iso,.wud,.wux", "nkitgcz")]
        [InlineData("039", "wiiu", ".iso,.wud,.wux", "cue")]
        [InlineData("040", "wiiu", ".iso,.wud,.wux", "gdi")]
        [InlineData("041", "wiiu", ".iso,.wud,.wux", "cso")]
        [InlineData("042", "wiiu", ".iso,.wud,.wux", "zso")]
        [InlineData("043", "wiiu", ".iso,.wud,.wux", "deciso")]
        [InlineData("044", "wiiu", ".iso,.wud,.wux", "dax")]
        [InlineData("045", "wiiu", ".iso,.wud,.wux", "jso")]
        [InlineData("046", "wiiu", ".iso,.wud,.wux", "chd")]
        [InlineData("047", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "isodec")]
        [InlineData("048", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gcz")]
        [InlineData("049", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "ciso")]
        [InlineData("050", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wbfs")]
        [InlineData("051", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wia")]
        [InlineData("052", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "rvz")]
        [InlineData("053", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitiso")]
        [InlineData("054", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitgcz")]
        [InlineData("055", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wux")]
        [InlineData("056", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "app")]
        [InlineData("057", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "cue")]
        [InlineData("044", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gdi")]
        [InlineData("045", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "dax")]
        [InlineData("046", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "jso")]
        [InlineData("046", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "chd")]
        [InlineData("047", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "isodec")]
        [InlineData("048", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gcz")]
        [InlineData("049", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "ciso")]
        [InlineData("050", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wbfs")]
        [InlineData("051", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wia")]
        [InlineData("052", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "rvz")]
        [InlineData("053", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitiso")]
        [InlineData("054", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitgcz")]
        [InlineData("055", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wux")]
        [InlineData("056", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "app")]
        [InlineData("057", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "cue")]
        [InlineData("058", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gdi")]
        [InlineData("059", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "dax")]
        [InlineData("060", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "jso")]
        [InlineData("061", "ps1", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "chd")]
        [InlineData("062", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "isodec")]
        [InlineData("063", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gcz")]
        [InlineData("064", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "ciso")]
        [InlineData("065", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wbfs")]
        [InlineData("066", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wia")]
        [InlineData("067", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "rvz")]
        [InlineData("068", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitiso")]
        [InlineData("069", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitgcz")]
        [InlineData("070", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wux")]
        [InlineData("071", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "app")]
        [InlineData("072", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "cue")]
        [InlineData("073", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gdi")]
        [InlineData("074", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "deciso")]
        [InlineData("075", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "dax")]
        [InlineData("076", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "jso")]
        [InlineData("077", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "chd")]
        [InlineData("078", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "isodec")]
        [InlineData("079", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gcz")]
        [InlineData("080", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "ciso")]
        [InlineData("081", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wbfs")]
        [InlineData("082", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wia")]
        [InlineData("083", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "rvz")]
        [InlineData("084", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitiso")]
        [InlineData("085", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "nkitgcz")]
        [InlineData("086", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "wux")]
        [InlineData("087", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "app")]
        [InlineData("088", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "cue")]
        [InlineData("089", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "gdi")]
        [InlineData("090", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "dax")]
        [InlineData("091", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "jso")]
        [InlineData("092", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso", "chd")]

        public void ConvertNotSupported_ImageTests(string idx, string system, string srcFormats, string convert) => convertNotSupported(system, srcFormats, convert);

        //                  System,      srcFormats,                                                      convert
        [Theory]
        [InlineData("001", "ps2", ".cue", "iso")]
        [InlineData("002", "ps2", ".cue", "isodec")]
        [InlineData("003", "ps2", ".cue", "gcz")]
        [InlineData("004", "ps2", ".cue", "ciso")]
        [InlineData("005", "ps2", ".cue", "wbfs")]
        [InlineData("006", "ps2", ".cue", "wia")]
        [InlineData("007", "ps2", ".cue", "rvz")]
        [InlineData("008", "ps2", ".cue", "nkitiso")]
        [InlineData("009", "ps2", ".cue", "nkitgcz")]
        [InlineData("010", "ps2", ".cue", "wux")]
        [InlineData("011", "ps2", ".cue", "app")]
        [InlineData("012", "ps2", ".cue", "gdi")]
        [InlineData("013", "ps2", ".cue", "cso")]
        [InlineData("014", "ps2", ".cue", "zso")]
        [InlineData("015", "ps2", ".cue", "deciso")]
        [InlineData("016", "ps2", ".cue", "jso")]
        [InlineData("017", "ps2", ".cue", "dax")]
        [InlineData("018", "ps2", ".cue", "chd")]
        [InlineData("019", "ps1", ".cue", "iso")]
        [InlineData("020", "ps1", ".cue", "isodec")]
        [InlineData("021", "ps1", ".cue", "gcz")]
        [InlineData("022", "ps1", ".cue", "ciso")]
        [InlineData("023", "ps1", ".cue", "wbfs")]
        [InlineData("024", "ps1", ".cue", "wia")]
        [InlineData("025", "ps1", ".cue", "rvz")]
        [InlineData("026", "ps1", ".cue", "nkitiso")]
        [InlineData("027", "ps1", ".cue", "nkitgcz")]
        [InlineData("028", "ps1", ".cue", "wux")]
        [InlineData("029", "ps1", ".cue", "app")]
        [InlineData("030", "ps1", ".cue", "gdi")]
        [InlineData("031", "ps1", ".cue", "cso")]
        [InlineData("032", "ps1", ".cue", "zso")]
        [InlineData("033", "ps1", ".cue", "deciso")]
        [InlineData("034", "ps1", ".cue", "jso")]
        [InlineData("035", "ps1", ".cue", "dax")]
        [InlineData("036", "ps1", ".cue", "chd")]
        [InlineData("037", "dreamcast", ".cue,.gdi", "iso")]
        [InlineData("038", "dreamcast", ".cue,.gdi", "isodec")]
        [InlineData("039", "dreamcast", ".cue,.gdi", "gcz")]
        [InlineData("040", "dreamcast", ".cue,.gdi", "ciso")]
        [InlineData("041", "dreamcast", ".cue,.gdi", "wbfs")]
        [InlineData("042", "dreamcast", ".cue,.gdi", "wia")]
        [InlineData("043", "dreamcast", ".cue,.gdi", "rvz")]
        [InlineData("044", "dreamcast", ".cue,.gdi", "nkitiso")]
        [InlineData("045", "dreamcast", ".cue,.gdi", "nkitgcz")]
        [InlineData("046", "dreamcast", ".cue,.gdi", "wux")]
        [InlineData("047", "dreamcast", ".cue,.gdi", "app")]
        [InlineData("048", "dreamcast", ".cue,.gdi", "cso")]
        [InlineData("049", "dreamcast", ".cue,.gdi", "zso")]
        [InlineData("050", "dreamcast", ".cue,.gdi", "deciso")]
        [InlineData("051", "dreamcast", ".cue,.gdi", "jso")]
        [InlineData("052", "dreamcast", ".cue,.gdi", "dax")]
        [InlineData("053", "dreamcast", ".cue,.gdi", "chd")]
        [InlineData("054", "wiiu", ".tmd", "iso")]
        [InlineData("055", "wiiu", ".tmd", "isodec")]
        [InlineData("056", "wiiu", ".tmd", "gcz")]
        [InlineData("057", "wiiu", ".tmd", "ciso")]
        [InlineData("058", "wiiu", ".tmd", "wbfs")]
        [InlineData("059", "wiiu", ".tmd", "wia")]
        [InlineData("060", "wiiu", ".tmd", "rvz")]
        [InlineData("061", "wiiu", ".tmd", "nkitiso")]
        [InlineData("062", "wiiu", ".tmd", "nkitgcz")]
        [InlineData("063", "wiiu", ".tmd", "wux")]
        [InlineData("064", "wiiu", ".tmd", "cue")]
        [InlineData("065", "wiiu", ".tmd", "gdi")]
        [InlineData("066", "wiiu", ".tmd", "cso")]
        [InlineData("067", "wiiu", ".tmd", "zso")]
        [InlineData("068", "wiiu", ".tmd", "deciso")]
        [InlineData("069", "wiiu", ".tmd", "jso")]
        [InlineData("070", "wiiu", ".tmd", "dax")]
        [InlineData("071", "wiiu", ".tmd", "chd")]
        public void ConvertNotSupported_FolderIndexTests(string idx, string system, string srcFormats, string convert) => convertNotSupported(system, srcFormats, convert);

        private static void convertNotSupported(string system, string srcFormats, string convert)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

            string taskType = "convert";
            string cfg = system == "dreamcast" && (convert == "cue" || convert == "gdi") ? "gdrom" : convert;

            foreach (string srcFormat in srcFormats.Split(','))
            {
                bool reqPatch = false; // Convert never sets ReqPatch (single-pass)

                for (int v = 0; v < vfy.Length; v++)
                {
                    IParts parts = new Parts(0); //none for extract

                    NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, "", convert, reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);

                    Assert.Single(task.Steps);
                    Assert.Equal(_NotSupportedResult, task.Steps[0].StepInfo.Name);
                }
            }
        }

        //                  System,      srcFormats
        [Theory]
        [InlineData("001", "wiiu", ".iso,.wud,.wux,.tmd")]
        [InlineData("002", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso")]
        [InlineData("003", "ps3", ".iso,.cso,.zso,.dec.iso,.dax,.jso")]
        [InlineData("004", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso,.cue")]
        [InlineData("005", "ps1", ".iso,.cue,.chd")]
        [InlineData("006", "dreamcast", ".cue,.gdi,.chd")]
        [InlineData("007", "segacd", ".iso,.cue,.chd")]
        [InlineData("008", "saturn", ".iso,.cue,.chd")]
        [InlineData("009", "pcengine", ".iso,.cue,.chd")]
        [InlineData("010", "cdi", ".iso,.cue,.chd")]
        [InlineData("011", "default", ".iso,.cue,.chd")]
        internal static void FixNotSupported(string idx, string system, string srcFormats)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

            string taskType = "fix";
            string cfg = "";

            foreach (string srcFormat in srcFormats.Split(','))
            {
                // Fix sets ReqPatch only for a raw nkit Wii source (none appear in these
                // not-supported lists, so this is false here).
                bool reqPatch = (srcFormat == ".nkit.iso" || srcFormat == ".nkit.gcz") && system == "wii";

                for (int v = 0; v < vfy.Length; v++)
                {
                    IParts parts = new Parts(0); //none for extract

                    NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, "", "", reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);

                    Assert.Single(task.Steps);
                    Assert.Equal(_NotSupportedResult, task.Steps[0].StepInfo.Name);
                }
            }
        }

        //                  System,      srcFormats
        [Theory]
        [InlineData("001", "wiiu", ".iso,.wud,.wux")]
        [InlineData("002", "psp", ".iso,.cso,.zso,.dec.iso,.dax,.jso,.chd")]
        [InlineData("003", "ps2", ".iso,.cso,.zso,.dec.iso,.dax,.jso,.chd")]
        [InlineData("004", "ps2", ".cue,.chd")]
        [InlineData("005", "ps1", ".cue,.chd")]
        [InlineData("006", "dreamcast", ".cue,.gdi,.chd")]
        [InlineData("007", "wiiu", ".tmd,.app")]
        internal static void FixExtractNotSupported(string idx, string system, string srcFormats)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

            string taskType = "fixextract";
            string cfg = "";

            foreach (string srcFormat in srcFormats.Split(','))
            {
                bool reqPatch = false; // FixExtract never sets ReqPatch (single-pass)

                for (int v = 0; v < vfy.Length; v++)
                {
                    IParts parts = new Parts(0); //none for extract

                    NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, "", "", reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);

                    Assert.Single(task.Steps);
                    Assert.Equal(_NotSupportedResult, task.Steps[0].StepInfo.Name);
                }
            }
        }

        //                  System,      srcFormats
        //[Theory]
        //[InlineData("001", "wiiu",       ".tmd")]
        //[InlineData("002", "psp",        ".iso,.cso,.zso,.dec.iso,.dax,.jso,.chd")]
        //[InlineData("003", "ps3",        ".iso,.cso,.zso,.dec.iso,.dax,.jso,.chd")]
        //[InlineData("004", "ps2",        ".iso,.cso,.zso,.dec.iso,.dax,.jso,.chd")]
        //[InlineData("005", "ps2",        ".cue,.chd")]
        //[InlineData("006", "ps1",        ".cue,.chd")]
        //[InlineData("007", "dreamcast",  ".cue,.gdi")]
        //internal static void DedupeNotSupported(string idx, string system, string srcFormats)
        //{
        //    TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

        //    string taskType = "dedupe";
        //    string cfg = "";

        //    foreach (string srcFormat in srcFormats.Split(','))
        //    {
        //        bool reqPatch = srcFormat == ".nkit.iso" || srcFormat == ".nkit.gcz";

        //        for (int v = 0; v < vfy.Length; v++)
        //        {
        //            IParts parts = new Parts(0); //none for extract

        //            NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, "", "", reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);

        //            Assert.Single(task.Steps);
        //            Assert.Equal(NotSupportedResult, task.Steps[0].StepInfo.Name);
        //        }
        //    }
        //}

        // ===================== DataStore (.nkds) not-supported coverage =====================
        // A .nkds source is a deduped DataStore (srcInfo carries "ds"; "idxds" = folderindex/CD shape,
        // "ds" = image/DVD shape). These (task, system, shape) combinations route to NotSupported,
        // mirroring the .chd/.cue/.iso not-supported rows above but modelling a real DataStore source.

        //                  System,        srcInfo,   convert   (Fix supports only wii/gamecube + ps3-sfb)
        [Theory]
        [InlineData("001", "ps1", "idxds", "iso")]
        [InlineData("002", "ps2", "idxds", "iso")]
        [InlineData("003", "ps2", "ds", "iso")]
        [InlineData("004", "ps3", "ds", "iso")]
        [InlineData("005", "psp", "ds", "iso")]
        [InlineData("006", "dreamcast", "idxds", "iso")]
        [InlineData("007", "saturn", "idxds", "iso")]
        [InlineData("008", "segacd", "idxds", "iso")]
        [InlineData("009", "cdi", "idxds", "iso")]
        [InlineData("010", "pcEngine", "idxds", "iso")]
        [InlineData("011", "xbox", "ds", "iso")]
        [InlineData("012", "xbox360", "ds", "iso")]
        [InlineData("013", "default", "idxds", "iso")]
        [InlineData("014", "default", "ds", "iso")]
        [InlineData("015", "wiiu", "ds", "iso")]
        public void FixNotSupported_DataStore(string idx, string system, string srcInfo, string convert) => dataStoreNotSupported("fix", system, srcInfo, convert);

        //                  System,        srcInfo,   (FixExtract supports only wii/gamecube + ps3)
        [Theory]
        [InlineData("001", "ps1", "idxds", "")]
        [InlineData("002", "ps2", "idxds", "")]
        [InlineData("003", "ps2", "ds", "")]
        [InlineData("004", "psp", "ds", "")]
        [InlineData("005", "dreamcast", "idxds", "")]
        [InlineData("006", "saturn", "idxds", "")]
        [InlineData("007", "segacd", "idxds", "")]
        [InlineData("008", "cdi", "idxds", "")]
        [InlineData("009", "pcEngine", "idxds", "")]
        [InlineData("010", "xbox", "ds", "")]
        [InlineData("011", "xbox360", "ds", "")]
        [InlineData("012", "default", "idxds", "")]
        [InlineData("013", "default", "ds", "")]
        [InlineData("014", "wiiu", "ds", "")]
        public void FixExtractNotSupported_DataStore(string idx, string system, string srcInfo, string convert) => dataStoreNotSupported("fixExtract", system, srcInfo, convert);

        //                  System,        srcInfo,   convert   (wii/gamecube/wiiu don't support cso/zso)
        [Theory]
        [InlineData("001", "wii", "ds", "cso")]
        [InlineData("002", "wii", "ds", "zso")]
        [InlineData("003", "gamecube", "ds", "cso")]
        [InlineData("004", "gamecube", "ds", "zso")]
        [InlineData("005", "wiiu", "ds", "cso")]
        [InlineData("006", "wiiu", "ds", "zso")]
        public void ConvertNotSupported_DataStore(string idx, string system, string srcInfo, string convert) => dataStoreNotSupported("convert", system, srcInfo, convert);

        private static void dataStoreNotSupported(string taskType, string system, string srcInfo, string convert)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();
            string cfg = convert;
            foreach (TaskStepVerifySettings v in vfy)
            {
                IParts parts = new Parts(0);
                NKitTaskContext task = TaskStepsShared.Process(taskType, system, ".nkds", srcInfo, convert, false, v.PrmV, parts, v.InNKitScan, v.Dats, v.DatItem, cfg);
                Assert.Single(task.Steps);
                Assert.Equal(_NotSupportedResult, task.Steps[0].StepInfo.Name);
            }
        }
    }
}
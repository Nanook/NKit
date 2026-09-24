using Nanook.NKit;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskStepsWipeTests
    {
        //                  System,      srcFormat,   srcInfo,    wipe,                 Cfg,          outType        Result
        [Theory]
        [InlineData("001", "dreamcast", ".cue", "", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("002", "dreamcast", ".cue", "", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("003", "dreamcast", ".gdi", "", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("004", "dreamcast", ".chd", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("005", "dreamcast", ".chd", "idx", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("006", "wiiu", ".tmd", "", "", "none", "folderindex", "Wipe-WiiU-AppTmd(M,V:NoVerify,C:WipeWiiUStep)")]
        [InlineData("007", "wiiu", ".iso", "", "", "none", "image", "Wipe-WiiU(M,V:NoVerify,C:WipeWiiUStep)")]
        [InlineData("008", "wiiu", ".wux", "", "", "none", "image", "Wipe-WiiU(M,V:NoVerify,C:WipeWiiUStep)")]
        [InlineData("009", "ps3", ".dec.iso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("010", "ps3", ".cso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("011", "ps3", ".cso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("012", "ps3", ".zso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("013", "ps3", ".zso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("014", "ps3", ".iso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("015", "ps3", ".chd", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("016", "ps2", ".cso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("017", "ps2", ".cso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("018", "ps2", ".zso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("019", "ps2", ".zso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("020", "ps2", ".cue", "", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("021", "ps2", ".iso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("022", "ps2", ".chd", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("023", "ps2", ".chd", "idx", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("024", "ps1", ".cso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("025", "ps1", ".cso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("026", "ps1", ".zso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("027", "ps1", ".zso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("028", "ps1", ".cue", "", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("029", "ps1", ".iso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("030", "ps1", ".chd", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("031", "ps1", ".chd", "idx", "", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("032", "psp", ".cso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("033", "psp", ".cso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("034", "psp", ".zso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("035", "psp", ".zso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("036", "psp", ".jso", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("037", "psp", ".dax", "nkit", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("038", "psp", ".iso", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("039", "psp", ".chd", "", "", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("040", "gamecube", ".iso", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("041", "gamecube", ".iso.dec", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("042", "gamecube", ".gcz", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("043", "gamecube", ".ciso", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("044", "gamecube", ".ciso", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("045", "gamecube", ".wbfs", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("046", "gamecube", ".wbfs", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("047", "gamecube", ".wia", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("048", "gamecube", ".rvz", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("049", "gamecube", ".rvz", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("050", "gamecube", ".nkit.iso", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("051", "gamecube", ".nkit.gcz", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("052", "wii", ".iso", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("053", "wii", ".iso.dec", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("054", "wii", ".gcz", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("055", "wii", ".ciso", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("056", "wii", ".ciso", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("057", "wii", ".wbfs", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("058", "wii", ".wbfs", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("059", "wii", ".wia", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("060", "wii", ".rvz", "", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("061", "wii", ".rvz", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("062", "wii", ".nkit.iso", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("063", "wii", ".nkit.gcz", "nkit", "", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("064", "saturn", ".chd", "idx", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("065", "segacd", ".chd", "idx", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("066", "cdi", ".chd", "idx", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("067", "xbox", ".chd", "", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("068", "xbox360", ".chd", "", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("069", "default", ".chd", "idx", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("070", "default", ".chd", "", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("071", "wii", ".chd", "", "none", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("072", "gamecube", ".chd", "", "none", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("073", "wiiu", ".chd", "", "none", "none", "image", "Wipe-WiiU(M,V:NoVerify,C:WipeWiiUStep)")]
        [InlineData("074", "ps1", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("075", "ps2", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("076", "ps2", ".nkds", "ds", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("077", "ps3", ".nkds", "ds", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("078", "psp", ".nkds", "ds", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("079", "dreamcast", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("080", "saturn", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("081", "segacd", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("082", "cdi", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("083", "xbox", ".nkds", "ds", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("084", "xbox360", ".nkds", "ds", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("085", "default", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("086", "default", ".nkds", "ds", "none", "none", "image", "Wipe-Iso(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("087", "wii", ".nkds", "ds", "none", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("088", "gamecube", ".nkds", "ds", "none", "none", "image", "Wipe-WiiGc(M,V:NoVerify,C:WipeWiiGcStep)")]
        [InlineData("089", "wiiu", ".nkds", "ds", "none", "none", "image", "Wipe-WiiU(M,V:NoVerify,C:WipeWiiUStep)")]
        [InlineData("090", "pcEngine", ".chd", "idx", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        [InlineData("091", "pcEngine", ".nkds", "idxds", "none", "none", "folderindex", "Wipe-IsoCueTocGdi(M,V:NoVerify,C:WipeIsoStep)")]
        public void WipeTest(string idx, string system, string srcFormat, string srcInfo, string wipe, string cfg, string outType, string resultString)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

            for (int v = 0; v < vfy.Length; v++)
            {
                string taskType = "wipe";
                // ReqPatch is only set by the WiiGc reader for the Expand task.
                // Wipe reads the corrected nkit image in a single pass, so ReqPatch is never set.
                bool reqPatch = false;
                bool nkitHeader = srcInfo.Contains("nkit");
                bool srcCrcHash = nkitHeader;

                IParts parts = new Parts(0); //none for extract

                NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, srcInfo, wipe, reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);
                TaskStepResult[] results = TaskStepsShared.GetResults(resultString);
                int i = 0;

                Assert.Equal(results.Length, task.Steps.Count);
                Assert.Equal(1, results.Count(a => a.IsMain));
                foreach (NKitStepContext step in task.Steps)
                {
                    NKitVerify.RequiredChecksums(vfy[v].Dats, parts, step.StepInfo, out string chkString);
                    Assert.Equal(results[i].Name, step.StepInfo.Name);
                    if (step.StepInfo.Name != "NotSet-NotSupported")
                    {
                        if (results[i].IsMain)
                        {
                            Assert.Equal(cfg.ToLower(), step.StepInfo.ImageConfig.ToLower());
                            if (step.StepInfo.WriteImage)
                                Assert.Equal(outType.ToLower(), step.StepInfo.OutputType.ToString().ToLower());
                        }
                        if (results[i].IsVerify)
                            Assert.Equal(results[i].VerifyType, chkString);
                        Assert.Equal(results[i].ClassName, step.Step.GetType().Name);
                    }
                    i++;
                }
            }
        }

    }
}
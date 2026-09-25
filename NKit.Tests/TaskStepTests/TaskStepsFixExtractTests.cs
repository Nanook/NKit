using Nanook.NKit;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskStepsFixExtractTests
    {
        //                  System,      srcFormat,   NKitHeader, srcType,              Cfg,          outType        Result
        [Theory]
        [InlineData("001", "gamecube", ".iso", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("002", "gamecube", ".iso.dec", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("003", "gamecube", ".gcz", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("004", "gamecube", ".ciso", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("005", "gamecube", ".ciso", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("006", "gamecube", ".wbfs", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("007", "gamecube", ".wbfs", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("008", "gamecube", ".wia", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("009", "gamecube", ".rvz", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("010", "gamecube", ".rvz", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("011", "gamecube", ".nkit.iso", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("012", "gamecube", ".nkit.gcz", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("013", "wii", ".iso", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("014", "wii", ".iso.dec", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("015", "wii", ".gcz", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("016", "wii", ".ciso", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("017", "wii", ".ciso", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("018", "wii", ".wbfs", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("019", "wii", ".wbfs", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("020", "wii", ".wia", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("021", "wii", ".rvz", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("022", "wii", ".rvz", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("023", "wii", ".nkit.iso", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("024", "wii", ".nkit.gcz", "nkit", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("025", "ps3", ".dec.iso", "", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("026", "ps3", ".cso", "", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("027", "ps3", ".cso", "nkit", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("028", "ps3", ".zso", "", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("029", "ps3", ".zso", "nkit", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("030", "ps3", ".iso", "", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("031", "ps3", ".chd", "", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("032", "wii", ".chd", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("033", "gamecube", ".chd", "", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("034", "ps3", ".nkds", "ds", "", "", "files", "FixExtract-Ps3(M,V:NoVerify,C:FixExtractPs3Step)")]
        [InlineData("035", "wii", ".nkds", "ds", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        [InlineData("036", "gamecube", ".nkds", "ds", "", "", "files", "FixExtract-WiiGc(M,V:NoVerify,C:FixExtractWiiGcStep)")]
        public void FixExtractTest(string idx, string system, string srcFormat, string srcInfo, string srcType, string cfg, string outType, string resultString)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

            for (int v = 0; v < vfy.Length; v++)
            {
                string taskType = "fixextract";
                // ReqPatch is only set by the WiiGc reader for the Expand task.
                // FixExtract reads the corrected nkit image in a single pass, so ReqPatch is never set.
                bool reqPatch = false;
                bool nkitHeader = srcInfo.Contains("nkit");
                bool srcCrcHash = nkitHeader;

                IParts parts = new Parts(0); //none for extract

                NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, srcInfo, srcType, reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);
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
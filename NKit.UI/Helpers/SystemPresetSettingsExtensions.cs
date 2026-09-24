using Nanook.NKit;

namespace NKit.Ui.Helpers
{
    public static class SystemPresettargetExtensions
    {
        public static void SetFrom(this SystemPresetSettings target, SystemPresetSettings source)
        {
            target.System = source.System;
            target.Task = source.Task;
            target.Out = source.Out;
            target.ScanIn = source.ScanIn;
            target.ScanOut = source.ScanOut;
            target.Tmp = source.Tmp;
            target.R = source.R;
            target.Arc = source.Arc;
            target.V = source.V;
            target.ConsoleLevel = source.ConsoleLevel;
            target.LogOutLevel = source.LogOutLevel;
            target.LogOut = source.LogOut;
            target.Results = source.Results;
            target.ResultsOut = source.ResultsOut;
            target.BaseInPath = source.BaseInPath;
            target.FixInfo = source.FixInfo;
            target.FixFiles = source.FixFiles;
            target.Dat = source.Dat;
            target.Keys = source.Keys;
            target.Convert = source.Convert;
            target.Extract = source.Extract;
            target.Dedupe = source.Dedupe;
            target.OutAsDatMatch = source.OutAsDatMatch;
            target.DeleteProcessed = source.DeleteProcessed;
            target.SkipIfCompleted = source.SkipIfCompleted;
        }
    }
}
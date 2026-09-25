using System.Collections.Generic;

namespace Nanook.NKit
{
    public class SystemPresetSettings
    {
        public SystemPresetSettings()
        {
            this.In = new List<string>();
        }
        public SystemType System { get; set; }
        public TaskType Task { get; set; }
        public List<string> In { get; }

        public string Out { get; set; }
        public string ScanIn { get; set; }
        public string ScanOut { get; set; }
        public string Tmp { get; set; }
        public bool R { get; set; }
        public bool Arc { get; set; }
        public Verify V { get; set; }
        public LogLevel ConsoleLevel { get; set; }
        public LogLevel LogOutLevel { get; set; }
        public string LogOut { get; set; }
        public bool Results { get; set; }
        public string ResultsOut { get; set; }



        public string BaseInPath { get; set; }
        public string Dedupe { get; set; }
        public string OgmrYamlPath { get; set; }
        public string FixInfo { get; set; }

        public string Dat { get; set; }

        public string Keys { get; set; }

        // Dat collection properties for flattened library configuration
        public string RedumpDatsPath { get; set; }
        public string NoIntroDatsPath { get; set; }
        public string TosecDatsPath { get; set; }

        public string Convert { get; set; }
        public string Extract { get; set; }

        public bool OutAsDatMatch { get; set; }
        public bool DeleteProcessed { get; set; }
        public bool SkipIfCompleted { get; set; }

        public string FixFiles { get; set; }

        internal SystemSettings ToSystemSettings()
        {
            Dictionary<string, string> rootParams = new Dictionary<string, string>();
            Dictionary<string, string> sysParams = new Dictionary<string, string>();

            rootParams.Add("in", string.Join("\0", this.In));
            rootParams.Add("out", this.Out);
            rootParams.Add("scanIn", this.ScanIn);
            rootParams.Add("scanOut", this.ScanOut);
            rootParams.Add("tmp", this.Tmp);
            rootParams.Add("r", this.R ? "y" : "n");
            rootParams.Add("arc", this.Arc ? "y" : "n");
            rootParams.Add("v", this.V.ToString());
            rootParams.Add("task", this.Task.ToString());
            rootParams.Add("logOutLevel", this.LogOutLevel.ToString());
            rootParams.Add("logOut", this.LogOut);
            rootParams.Add("results", this.Results ? "y" : "n");
            rootParams.Add("resultsOut", this.ResultsOut);
            rootParams.Add("consoleLevel", this.ConsoleLevel.ToString());
            rootParams.Add("system", System.ToString());
            rootParams.Add("outAsDatMatch", this.OutAsDatMatch ? "y" : "n");
            rootParams.Add("deleteProcessed", this.DeleteProcessed ? "y" : "n");
            rootParams.Add("skipIfCompleted", this.SkipIfCompleted ? "y" : "n");

            sysParams.Add("baseInPath", this.BaseInPath);
            sysParams.Add("dedupe", this.Dedupe);
            sysParams.Add("ogmr", this.OgmrYamlPath);
            sysParams.Add("fixInfo", this.FixInfo);
            sysParams.Add("fixFiles", this.FixFiles);
            sysParams.Add("dat", this.Dat);
            sysParams.Add("keys", this.Keys);
            sysParams.Add("convert", this.Convert);
            sysParams.Add("extract", this.Extract);

            // Add dat collections to root params since they're global
            rootParams.Add("redumpDatsPath", this.RedumpDatsPath);
            rootParams.Add("noIntroDatsPath", this.NoIntroDatsPath);
            rootParams.Add("tosecDatsPath", this.TosecDatsPath);

            return new SystemSettings(rootParams, this.System.ToString(), sysParams, new Dictionary<string, string>(), null);
        }
    }
}
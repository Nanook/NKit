using System.Collections.Generic;
using System.Linq;
using Xunit.Sdk;

namespace NKit.Tests
{
    public class ProcessingTestItem : IXunitSerializable
    {
        public ProcessingTestItem()
        {
            this.Values = new Dictionary<string, string>();
            this.Index = 0;
            this.BasePath = "";
            this.InputBasePath = "";
            this.TestName = "";
        }

        public string BasePath { get; set; }
        /// <summary>Separate base path for resolving input file paths (may differ from BasePath
        /// when input files are on a read-only mount and output must go elsewhere).</summary>
        public string InputBasePath { get; set; }
        public string TestName { get; set; }
        public int Index { get; set; }
        public Dictionary<string, string> Values { get; set; }

        public void Deserialize(IXunitSerializationInfo info)
        {
            this.BasePath = info.GetValue<string>("BasePath");
            this.InputBasePath = info.GetValue<string>("InputBasePath") ?? this.BasePath;
            this.TestName = info.GetValue<string>("TestName");
            this.Index = info.GetValue<int>("Index");
            this.Values.Clear();
            foreach (string name in info.GetValue<string>("Keys").Split('\t'))
                this.Values.Add(name, info.GetValue<string>(name));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue("BasePath", this.BasePath);
            info.AddValue("InputBasePath", this.InputBasePath);
            info.AddValue("TestName", this.TestName);
            info.AddValue("Index", this.Index);
            info.AddValue("Keys", string.Join('\t', this.Values.Keys)); //workaround - info doesn't expose the keys :(
            foreach (KeyValuePair<string, string> kv in this.Values)
                info.AddValue(kv.Key, kv.Value, typeof(string));
        }

        public override string ToString() //publish unit test name for VS
        {
            string nameKey = this.Values?.Keys?.FirstOrDefault(a => string.Compare("name", a, true) == 0);
            return this.Index.ToString("D3") + (nameKey == null ? "" : (" : " + this.Values[nameKey]));
        }
    }
}
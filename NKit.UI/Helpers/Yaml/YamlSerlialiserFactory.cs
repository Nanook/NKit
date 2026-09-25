using NKit.Ui.Models;
using System.IO;

namespace NKit.Ui.Helpers.Yaml
{
    internal static class YamlSerlialiserFactory
    {
        public static string Serialize(UiConfiguration config)
        {
            using StringWriter writer = new StringWriter();
            AotYamlSerializer.Serialize(writer, config);
            return writer.ToString();
        }

        public static UiConfiguration Deserialize(string yaml)
        {
            using StringReader reader = new StringReader(yaml);
            return AotYamlSerializer.Deserialize(reader);
        }
    }
}
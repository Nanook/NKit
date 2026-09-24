using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace NKit.Ui.Helpers.Yaml
{
    internal sealed class BooleanYamlTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) =>
            type == typeof(bool);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            => parser.Consume<Scalar>().Value == "y" ? true : false;

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer) => emitter.Emit(new Scalar(null, (bool)value ? "y" : "n"));
    }
}
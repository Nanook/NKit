using System;
using System.ComponentModel;
//using Newtonsoft.Json;

namespace CUETools.Codecs.Flake
{
    //[JsonObject(MemberSerialization.OptIn)]
    internal class DecoderSettings : IAudioDecoderSettings
    {
        // ======= IAudioDecoderSettings implementation =======
        [Browsable(false)]
        public string Extension => "flac";

        [Browsable(false)]
        public string Name => "cuetools";

        [Browsable(false)]
        public Type DecoderType => typeof(AudioDecoder);

        [Browsable(false)]
        public int Priority => 2;

        public IAudioDecoderSettings Clone() => MemberwiseClone() as IAudioDecoderSettings;
        // ======= End IAudioDecoderSettings implementation =======

        public DecoderSettings()
        {
            this.Init();
        }
    }
}

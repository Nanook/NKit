using System;
using System.Collections.Generic;

namespace CUETools.Codecs.Flake
{
    internal interface IAudioSource
    {
        IAudioDecoderSettings Settings { get; }

        AudioPCMConfig PCM { get; }
        string Path { get; }

        TimeSpan Duration { get; }
        long Length { get; }
        long Position { get; set; }
        long Remaining { get; }

        int Read(AudioBuffer buffer, int maxLength);
        void Close();
    }

    internal interface IAudioTitle
    {
        List<TimeSpan> Chapters { get; }
        AudioPCMConfig PCM { get; }
        string Codec { get; }
        string Language { get; }
        int StreamId { get; }
        //IAudioSource Open { get; }
    }

    internal interface IAudioTitleSet
    {
        List<IAudioTitle> AudioTitles { get; }
    }

    internal static class IAudioTitleExtensions
    {
        public static TimeSpan GetDuration(this IAudioTitle title)
        {
            List<TimeSpan> chapters = title.Chapters;
            return chapters[chapters.Count - 1];
        }


        public static string GetRateString(this IAudioTitle title)
        {
            int sr = title.PCM.SampleRate;
            if (sr % 1000 == 0) return $"{sr / 1000}KHz";
            if (sr % 100 == 0) return $"{sr / 100}.{sr / 100 % 10}KHz";
            return $"{sr}Hz";
        }

        public static string GetFormatString(this IAudioTitle title)
        {
            switch (title.PCM.ChannelCount)
            {
                case 1: return "mono";
                case 2: return "stereo";
                default: return "multi-channel";
            }
        }
    }

    internal class SingleAudioTitle : IAudioTitle
    {
        public SingleAudioTitle(IAudioSource source) { this._source = source; }
        public List<TimeSpan> Chapters => new List<TimeSpan> { TimeSpan.Zero, _source.Duration };
        public AudioPCMConfig PCM => _source.PCM;
        public string Codec => _source.Settings.Extension;
        public string Language => "";
        public int StreamId => 0;
        IAudioSource _source;
    }

    internal class SingleAudioTitleSet : IAudioTitleSet
    {
        public SingleAudioTitleSet(IAudioSource source) { this._source = source; }
        public List<IAudioTitle> AudioTitles => new List<IAudioTitle> { new SingleAudioTitle(_source) };
        IAudioSource _source;
    }
}

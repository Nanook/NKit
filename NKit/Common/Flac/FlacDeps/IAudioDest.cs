namespace CUETools.Codecs.Flake
{
    internal interface IAudioDest
    {
        //IAudioEncoderSettings Settings { get; }

        string Path { get; }
        long FinalSampleCount { set; }

        void Write(AudioBuffer buffer);
        void Close();
        void Delete();
    }
}

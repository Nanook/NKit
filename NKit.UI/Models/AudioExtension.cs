using Nanook.NKit.Configuration;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NKit.Ui.Models
{
    public static class AudioExtension
    {
        // Use centralized configuration constants for consistency
        public static string Bin = ConfigSettingsConstants.AudioExtensionBin;
        public static string Wav = ConfigSettingsConstants.AudioExtensionWav;
        public static string Flac = ConfigSettingsConstants.AudioExtensionFlac;
        public static string Raw = ConfigSettingsConstants.AudioExtensionRaw;  // Add Raw back for UI compatibility

        public static ObservableCollection<string> GetAudioExtensions()
        {
            try
            {
                // Use centralized configuration if available
                IReadOnlyList<string> extensions = ConfigSettingsRanges.GetAudioExtensions();
                return new ObservableCollection<string>(extensions);
            }
            catch
            {
                // Fallback to hardcoded values if centralized config fails
                return new ObservableCollection<string>
                {
                    Bin,
                    Flac,
                    Wav,
                    Raw  // Include Raw in fallback
                };
            }
        }
    }
}
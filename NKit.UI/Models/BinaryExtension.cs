using Nanook.NKit.Configuration;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NKit.Ui.Models
{
    public static class BinaryExtension
    {
        // Use centralized configuration constants for consistency
        public static string Bin = ConfigSettingsConstants.BinaryExtensionBin;
        public static string Img = ConfigSettingsConstants.BinaryExtensionImg;
        public static string Raw = ConfigSettingsConstants.AudioExtensionRaw;  // Keep Raw for UI compatibility - not all contexts use ISO

        public static ObservableCollection<string> GetBinaryExtensions()
        {
            try
            {
                // Use centralized configuration if available, but add Raw for UI compatibility
                List<string> extensions = ConfigSettingsRanges.GetBinaryExtensions().ToList();

                // Add Raw if it's not already there (for UI compatibility)
                if (!extensions.Contains(ConfigSettingsConstants.AudioExtensionRaw))
                {
                    extensions.Add(ConfigSettingsConstants.AudioExtensionRaw);
                }

                return new ObservableCollection<string>(extensions);
            }
            catch
            {
                // Fallback to hardcoded values if centralized config fails
                return new ObservableCollection<string>
                {
                    Bin,
                    Img,
                    Raw,
                };
            }
        }
    }
}
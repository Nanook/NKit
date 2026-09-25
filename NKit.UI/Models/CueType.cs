using Nanook.NKit.Configuration;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NKit.Ui.Models
{
    public static class CueType
    {
        // Use centralized configuration constants for consistency
        public static string Joined = ConfigSettingsConstants.CueTypeJoined;
        public static string Split = ConfigSettingsConstants.CueTypeSplit;

        public static ObservableCollection<string> GetCueTypes()
        {
            try
            {
                // Use centralized configuration if available
                IReadOnlyList<string> cueTypes = ConfigSettingsRanges.GetCueTypes();
                return new ObservableCollection<string>(cueTypes);
            }
            catch
            {
                // Fallback to hardcoded values if centralized config fails
                return new ObservableCollection<string>
                {
                    Split,    // Keep Split first for UI consistency
                    Joined
                };
            }
        }
    }
}
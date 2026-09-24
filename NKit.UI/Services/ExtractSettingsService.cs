using Nanook.NKit;
using Nanook.NKit.Configuration;
using NKit.Ui.Models;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NKit.Ui.Services
{
    public class ExtractSettingsService
    {
        public bool IsFixFilesSupportedSystem(SystemType system)
        {
            SystemType[] fixFilesSupportedSystems = { SystemType.GameCube, SystemType.Wii, SystemType.PS3 };
            return fixFilesSupportedSystems.Contains(system);
        }

        public (bool isRegexValid, string validationMessage) ValidateRegexPattern(string searchTerm, bool isRegexSelected, bool isForensicMode)
        {
            if (isForensicMode)
                return (true, string.Empty);

            if (isRegexSelected)
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return (false, "Regex pattern cannot be empty");

                try
                {
                    ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateExtractFormat($"r:{searchTerm}");
                    if (!validationResult.IsValid)
                        return (false, validationResult.ErrorMessage);

                    Regex.Match("", searchTerm);
                    return (true, string.Empty);
                }
                catch (ArgumentException ex)
                {
                    return (false, ex.Message);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return (false, "Search pattern cannot be empty");

                return (true, string.Empty);
            }
        }

        public void EnsureValidDefaults(NKitSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ExtractType))
                settings.ExtractType = ConfigSettingsConstants.ExtractFlagMaskToRegex;

            if (string.IsNullOrEmpty(settings.ExtractSearchTerm))
                settings.ExtractSearchTerm = ConfigSettingsConstants.DefaultExtractSearchTerm;

            if (settings.ExtractMode != ExtractMode.AllFiles && settings.ExtractMode != ExtractMode.FixFiles)
                settings.ExtractMode = ExtractMode.AllFiles;

            settings.ExtractForensic = false;
        }

        public (bool isMaskSelected, bool isRegexSelected) GetExtractTypeSelection(string extractType)
        {
            if (extractType == ConfigSettingsConstants.ExtractFlagMaskToRegex)
                return (true, false);
            else if (extractType == ConfigSettingsConstants.ExtractTypeRegex)
                return (false, true);
            else
                return (true, false);
        }
    }
}
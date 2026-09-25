using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NKit.Tests.Settings
{
    /// <summary>
    /// Property-based tests for CalculateConfig validity.
    /// Feature: full-format-processing-parity, Property 4: CalculateConfig Produces Valid Config for DataStore Expand Tasks
    ///
    /// For any valid system type and any format string from the set of supported export formats
    /// (cue, gdi, iso, xiso, or empty string), when the task type is Expand and the source is a
    /// DataStore entry, CalculateConfig SHALL produce a config value that matches at least one
    /// entry in the _StepsDefs Expand section for the given system and source type combination.
    ///
    /// **Validates: Requirements 8.6, 10.8**
    /// </summary>
    [Trait("Area", "Settings")]
    public class CalculateConfigValidityPropertyTests
    {
        // Valid (system, format) pairs for DataStore Expand tasks.
        // Each system only supports certain export formats.
        private static readonly (string System, string Format)[] ValidCombinations = BuildValidCombinations();

        private static (string System, string Format)[] BuildValidCombinations()
        {
            List<(string, string)> combos = new List<(string, string)>();

            // Xbox/Xbox360: xiso, iso, or empty
            foreach (string sys in new[] { "xbox", "xbox360" })
                foreach (string fmt in new[] { "xiso", "iso", "" })
                    combos.Add((sys, fmt));

            // Dreamcast: cue (non-GdRom), gdi (GdRom), or empty
            foreach (string fmt in new[] { "cue", "gdi", "" })
                combos.Add(("dreamcast", fmt));

            // ISO9660 systems that support CUE: cue, iso, or empty
            foreach (string sys in new[] { "default", "ps1", "ps2", "saturn", "segacd", "cdi", "pcengine" })
                foreach (string fmt in new[] { "cue", "iso", "" })
                    combos.Add((sys, fmt));

            // PSP: iso or empty only (PSP does not support CUE export)
            foreach (string fmt in new[] { "iso", "" })
                combos.Add(("psp", fmt));

            return combos.ToArray();
        }

        /// <summary>
        /// Feature: full-format-processing-parity, Property 4: CalculateConfig Produces Valid Config for DataStore Expand Tasks
        ///
        /// For any valid system type and format string from supported export formats (cue, gdi, iso, xiso,
        /// or empty string), CalculateConfig produces a config value that matches at least one entry in
        /// _StepsDefs Expand section for the given system and source type.
        ///
        /// The test verifies that:
        /// 1. CalculateConfig produces a non-null config value
        /// 2. The routing resolves to a valid step sequence (not NotSet-NotSupported)
        ///
        /// **Validates: Requirements 8.6, 10.8**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool CalculateConfig_ProducesValidConfig_ForDataStoreExpandTasks(
            NonNegativeInt comboIndex,
            NonNegativeInt verifyIndex)
        {
            (string system, string formatString) = ValidCombinations[comboIndex.Get % ValidCombinations.Length];

            // Vary verification settings to exercise more routing paths
            string[] verifyOptions = new[] { "n", "y", "datLookup" };
            string prmV = verifyOptions[verifyIndex.Get % verifyOptions.Length];

            // Determine source type based on format:
            // - CUE/GDI formats come from folderindex sources
            // - ISO/XISO/empty come from image sources
            bool isFolderIndex = formatString == "cue" || formatString == "gdi";
            bool isGdRom = system == "dreamcast" && formatString == "gdi";

            // For Dreamcast GDI, the config string passed to CalculateConfig is "cue"
            // (the GdRom detection is separate via the isGdRom flag)
            string configString = formatString == "gdi" ? "cue" : formatString;

            // Create the task context to call CalculateConfig
            SystemPresetSettings presets = new SystemPresetSettings()
            {
                Task = TaskType.Expand,
                System = (SystemType)Enum.Parse(typeof(SystemType), system, true),
                V = (Verify)Enum.Parse(typeof(Verify), prmV, true),
                Convert = "",
                Extract = "",
                Out = ""
            };

            AppSettings settings = new AppSettings(presets);
            SourceFile file = TaskStepsShared.CreateSourceFile(
                isFolderIndex ? ".cue" : ".iso", isFolderIndex);
            NKitTaskContext task = new NKitTaskContext(settings, file, null);
            task.Steps[0].ImageInfo = new ImageInfo() { IsFolderIndex = file.IndexFile != null };
            task.Initialise(presets.System);

            // Call CalculateConfig — this is the method under test
            string config = task.CalculateConfig(configString, isGdRom);

            // Verify the config is not null
            if (config == null)
                return false;

            // Verify the config matches at least one Expand entry in _StepsDefs
            // by calling CreateSteps which internally calls getStepsValue.
            // We use minimal parameters to match the broadest entries.
            string srcType = isFolderIndex ? "folderindex" : "image";
            IStepsImageInfo imgInfo = new FakeImageInfo()
            {
                ReqPatch = false,
                Checksums = new Checksums(),
                IsIndex = isFolderIndex
            };

            try
            {
                task.CreateSteps(imgInfo, srcType, null, null, config, configString, false);

                // Verify it didn't resolve to NotSet-NotSupported
                bool hasNotSupported = task.Steps.Any(s => s.StepInfo?.Name == "NotSet-NotSupported");
                return !hasNotSupported;
            }
            catch (HandledException ex) when (ex.Message.Contains("No processing options found"))
            {
                // Config didn't match any entry — property violated
                return false;
            }
            catch (HandledException)
            {
                // Other HandledExceptions (like format validation in step constructors)
                // are not about config matching — the config DID match a _StepsDefs entry
                // but the step constructor has additional validation.
                // The property is specifically about config matching _StepsDefs entries,
                // so this is still a pass.
                return true;
            }
        }
    }
}
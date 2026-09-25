using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Property-based tests for Expand/Verify routing for DataStore folderindex sources.
    /// Feature: full-format-processing-parity, Property 3: Expand/Verify Routing for DataStore Folderindex Sources
    ///
    /// For any valid ISO9660 system type (ps1, ps2, saturn, segacd, cdi, default, pcengine, dreamcast)
    /// and a folderindex source from the DataStore, the NKitTaskContext routing SHALL resolve to a valid
    /// step sequence (Expand-Iso-CueToc for non-Dreamcast CUE, Expand-GdRom-CueGdi for Dreamcast GDI)
    /// and SHALL NOT fall through to NotSet-NotSupported.
    ///
    /// **Validates: Requirements 1.3, 2.3, 8.1, 8.2, 8.7, 10.7**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class ExpandVerifyFolderindexRoutingPropertyTests
    {
        // Valid ISO9660 system types that support folderindex sources
        private static readonly string[] Iso9660Systems = new[]
        {
            "Ps1", "Ps2", "Saturn", "SegaCd", "Cdi", "Default", "PcEngine", "Dreamcast"
        };

        // Config strings that represent DataStore export scenarios for folderindex sources
        // "cue" = explicit CUE export, "" = native expand (no format specified)
        private static readonly string[] FolderindexConfigStrings = new[] { "cue", "" };

        /// <summary>
        /// Feature: full-format-processing-parity, Property 3: Expand/Verify Routing for DataStore Folderindex Sources (Non-Dreamcast CUE)
        ///
        /// For any non-Dreamcast ISO9660 system type with a folderindex source from the DataStore,
        /// the Expand task routing SHALL resolve to Expand-Iso-CueToc and SHALL NOT fall through
        /// to NotSet-NotSupported.
        ///
        /// **Validates: Requirements 1.3, 2.3, 8.1, 8.7, 10.7**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ExpandRouting_NonDreamcastFolderindex_ResolvesToExpandIsoCueToc(
            NonNegativeInt systemIndexRaw,
            NonNegativeInt configIndexRaw,
            NonNegativeInt verifyComboIndexRaw)
        {
            // Select a non-Dreamcast ISO9660 system
            string[] nonDreamcastSystems = Iso9660Systems.Where(s => s != "Dreamcast").ToArray();
            string system = nonDreamcastSystems[systemIndexRaw.Get % nonDreamcastSystems.Length];
            string configString = FolderindexConfigStrings[configIndexRaw.Get % FolderindexConfigStrings.Length];

            // Vary verification settings to cover different routing paths
            TaskStepVerifySettings[] combos = TaskStepsShared.VerifyCombos();
            TaskStepVerifySettings combo = combos[verifyComboIndexRaw.Get % combos.Length];

            string srcFormat = ".cue"; // folderindex source
            string srcInfo = ""; // no special info
            bool reqPatch = false;
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            // cfg="" means non-GdRom for CalculateConfig
            string cfg = "";

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", system, srcFormat, srcInfo, configString,
                reqPatch, combo.PrmV, parts, combo.InNKitScan, combo.Dats, combo.DatItem, cfg);

            // Verify: routing resolved to Expand-Iso-CueToc (not NotSet-NotSupported)
            string mainStepName = task.Steps[0].StepInfo.Name;
            return mainStepName == "Expand-Iso-CueToc";
        }

        /// <summary>
        /// Feature: full-format-processing-parity, Property 3: Expand/Verify Routing for DataStore Folderindex Sources (Dreamcast GDI)
        ///
        /// For Dreamcast system with a GDI folderindex source from the DataStore (GdRom),
        /// the Expand task routing SHALL resolve to Expand-GdRom-CueGdi and SHALL NOT fall through
        /// to NotSet-NotSupported.
        ///
        /// **Validates: Requirements 2.3, 8.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ExpandRouting_DreamcastGdiFolderindex_ResolvesToExpandGdRomCueGdi(
            NonNegativeInt verifyComboIndexRaw)
        {
            // Dreamcast GDI (GdRom) scenario
            string system = "Dreamcast";
            string configString = "cue"; // GdRom expand uses "cue" as the format part

            TaskStepVerifySettings[] combos = TaskStepsShared.VerifyCombos();
            TaskStepVerifySettings combo = combos[verifyComboIndexRaw.Get % combos.Length];

            string srcFormat = ".gdi"; // GDI folderindex source
            string srcInfo = "";
            bool reqPatch = false;
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            // cfg="gdromcue" signals GdRom to CalculateConfig
            string cfg = "gdromcue";

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", system, srcFormat, srcInfo, configString,
                reqPatch, combo.PrmV, parts, combo.InNKitScan, combo.Dats, combo.DatItem, cfg);

            // Verify: routing resolved to Expand-GdRom-CueGdi (not NotSet-NotSupported)
            string mainStepName = task.Steps[0].StepInfo.Name;
            return mainStepName == "Expand-GdRom-CueGdi";
        }

        /// <summary>
        /// Feature: full-format-processing-parity, Property 3: Expand/Verify Routing for DataStore Folderindex Sources (Dreamcast non-GdRom CUE)
        ///
        /// For Dreamcast system with a non-GdRom CUE folderindex source from the DataStore,
        /// the Expand task routing SHALL resolve to Expand-Iso-CueToc and SHALL NOT fall through
        /// to NotSet-NotSupported.
        ///
        /// **Validates: Requirements 8.1, 8.7, 10.7**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ExpandRouting_DreamcastNonGdRomCueFolderindex_ResolvesToExpandIsoCueToc(
            NonNegativeInt configIndexRaw,
            NonNegativeInt verifyComboIndexRaw)
        {
            // Dreamcast non-GdRom CUE scenario
            string system = "Dreamcast";
            string configString = FolderindexConfigStrings[configIndexRaw.Get % FolderindexConfigStrings.Length];

            TaskStepVerifySettings[] combos = TaskStepsShared.VerifyCombos();
            TaskStepVerifySettings combo = combos[verifyComboIndexRaw.Get % combos.Length];

            string srcFormat = ".cue"; // CUE folderindex source (non-GdRom)
            string srcInfo = "";
            bool reqPatch = false;
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            // cfg="" means non-GdRom for CalculateConfig
            string cfg = "";

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", system, srcFormat, srcInfo, configString,
                reqPatch, combo.PrmV, parts, combo.InNKitScan, combo.Dats, combo.DatItem, cfg);

            // Verify: routing resolved to Expand-Iso-CueToc (not NotSet-NotSupported)
            string mainStepName = task.Steps[0].StepInfo.Name;
            return mainStepName == "Expand-Iso-CueToc";
        }

        /// <summary>
        /// Feature: full-format-processing-parity, Property 3: Expand/Verify Routing for DataStore Folderindex Sources (Verify)
        ///
        /// For any valid ISO9660 system type with a folderindex source from the DataStore,
        /// the Verify task routing SHALL resolve to Verify-Image and SHALL NOT fall through
        /// to NotSet-NotSupported.
        ///
        /// **Validates: Requirements 1.3, 10.7**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool VerifyRouting_Iso9660Folderindex_ResolvesToVerifyImage(
            NonNegativeInt systemIndexRaw,
            NonNegativeInt verifyComboIndexRaw)
        {
            // Select any ISO9660 system (including Dreamcast)
            string system = Iso9660Systems[systemIndexRaw.Get % Iso9660Systems.Length];

            TaskStepVerifySettings[] combos = TaskStepsShared.VerifyCombos();
            TaskStepVerifySettings combo = combos[verifyComboIndexRaw.Get % combos.Length];

            string srcFormat = ".cue"; // folderindex source
            string srcInfo = "";
            bool reqPatch = false;
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            // For Verify, cfg is not used for GdRom detection in the same way
            // Verify uses config="none" regardless
            string cfg = "";

            NKitTaskContext task = TaskStepsShared.Process(
                "verify", system, srcFormat, srcInfo, "",
                reqPatch, combo.PrmV, parts, combo.InNKitScan, combo.Dats, combo.DatItem, cfg);

            // Verify: routing resolved to Verify-Image (not NotSet-NotSupported)
            string mainStepName = task.Steps[0].StepInfo.Name;
            return mainStepName == "Verify-Image";
        }
    }
}
using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Property-based tests for Xbox Expand routing correctness.
    /// Feature: full-format-processing-parity, Property 5: Xbox Expand Routing for DataStore Sources
    ///
    /// For any Xbox or Xbox360 image from the DataStore with a config value of "xiso" or "iso"
    /// (or empty), the NKitTaskContext routing SHALL resolve to the Expand-XBox step sequence
    /// and SHALL NOT fall through to NotSet-NotSupported or resolve to the generic Expand-Image step.
    ///
    /// **Validates: Requirements 6.1, 6.4, 8.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class XboxExpandRoutingPropertyTests
    {
        private static readonly string[] XboxSystems = new[] { "xbox", "xbox360" };
        private static readonly string[] XboxConfigs = new[] { "xiso", "iso", "" };

        /// <summary>
        /// Feature: full-format-processing-parity, Property 5: Xbox Expand Routing for DataStore Sources
        ///
        /// For any Xbox or Xbox360 system type with config values "xiso", "iso", or empty string,
        /// the Expand routing resolves to Expand-XBox step sequence and does NOT fall through to
        /// NotSet-NotSupported or resolve to generic Expand-Image.
        ///
        /// **Validates: Requirements 6.1, 6.4, 8.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool XboxExpandRouting_AlwaysResolvesToExpandXBox(NonNegativeInt systemIndex, NonNegativeInt configIndex)
        {
            string system = XboxSystems[systemIndex.Get % XboxSystems.Length];
            string configString = XboxConfigs[configIndex.Get % XboxConfigs.Length];

            // Use the shared test infrastructure to process the routing
            // Xbox images are always "image" source type (not folderindex)
            string srcFormat = ".iso";
            string srcInfo = "";
            string prmV = "n";
            bool reqPatch = false;
            bool inScan = false;
            bool dats = false;
            bool datItem = false;

            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", system, srcFormat, srcInfo, configString,
                reqPatch, prmV, parts, inScan, dats, datItem, null);

            // Verify the routing resolved to Expand-XBox
            // 1. Must have at least one step
            if (task.Steps.Count == 0)
                return false;

            // 2. The main step must be Expand-XBox (not Expand-Image or NotSet-NotSupported)
            NKitStepContext mainStep = task.Steps.FirstOrDefault(s => s.StepInfo.Name == "Expand-XBox");
            if (mainStep == null)
                return false;

            // 3. Must NOT contain NotSet-NotSupported
            bool hasNotSupported = task.Steps.Any(s => s.StepInfo.Name == "NotSet-NotSupported");
            if (hasNotSupported)
                return false;

            // 4. Must NOT contain generic Expand-Image
            bool hasExpandImage = task.Steps.Any(s => s.StepInfo.Name == "Expand-Image");
            if (hasExpandImage)
                return false;

            return true;
        }

        /// <summary>
        /// Feature: full-format-processing-parity, Property 5: Xbox Expand Routing for DataStore Sources
        /// (Verification parameter variation)
        ///
        /// For any Xbox or Xbox360 system type with config values "xiso", "iso", or empty string,
        /// and any verification parameter setting (n, y, datLookup), the Expand routing resolves
        /// to Expand-XBox step sequence regardless of verification settings.
        ///
        /// **Validates: Requirements 6.1, 6.4, 8.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public bool XboxExpandRouting_AllVerifySettings_ResolvesToExpandXBox(
            NonNegativeInt systemIndex,
            NonNegativeInt configIndex,
            NonNegativeInt verifyIndex,
            bool inScan,
            bool dats,
            bool datItem)
        {
            string system = XboxSystems[systemIndex.Get % XboxSystems.Length];
            string configString = XboxConfigs[configIndex.Get % XboxConfigs.Length];
            string[] verifyOptions = new[] { "n", "y", "datLookup" };
            string prmV = verifyOptions[verifyIndex.Get % verifyOptions.Length];

            // Xbox images are always "image" source type
            string srcFormat = ".iso";
            string srcInfo = "";
            bool reqPatch = false;

            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", system, srcFormat, srcInfo, configString,
                reqPatch, prmV, parts, inScan, dats, datItem, null);

            // Verify the routing resolved to Expand-XBox
            if (task.Steps.Count == 0)
                return false;

            // The main step must be Expand-XBox
            NKitStepContext mainStep = task.Steps.FirstOrDefault(s => s.StepInfo.Name == "Expand-XBox");
            if (mainStep == null)
                return false;

            // Must NOT contain NotSet-NotSupported
            if (task.Steps.Any(s => s.StepInfo.Name == "NotSet-NotSupported"))
                return false;

            // Must NOT contain generic Expand-Image
            if (task.Steps.Any(s => s.StepInfo.Name == "Expand-Image"))
                return false;

            return true;
        }
    }
}
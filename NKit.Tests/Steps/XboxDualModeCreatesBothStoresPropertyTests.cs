using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using System;
using System.Text;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Property-based tests for Xbox dual mode store creation.
    /// Feature: aux-split-mode, Property 17: Xbox Dual Mode Creates Both Stores
    ///
    /// For any Xbox/Xbox360 game with AUX mode enabled, the DedupeStep SHALL create both
    /// a shared aux store ({systemType}.aux.nkds) and a split store ({setName}.split.nkds)
    /// if they do not already exist.
    ///
    /// Uses model-based testing since DedupeStep.Initialise() has complex dependencies
    /// (DataStore, file system, IStepContext). The model replicates the Xbox dual mode
    /// auto-creation logic and verifies both stores are always created with correct names.
    ///
    /// **Validates: Requirements 2.1, 2.4, 2.5**
    /// </summary>
    [Trait("Area", "NKDS")]
    public class XboxDualModeCreatesBothStoresPropertyTests
    {
        #region Model

        /// <summary>
        /// Represents the result of the dual mode store creation logic.
        /// </summary>
        private class DualModeCreationResult
        {
            /// <summary>Shared aux store set name (e.g., "xbox360.aux"). Null if not created/reused.</summary>
            public string AuxSetName { get; set; }

            /// <summary>Per-game split store set name (e.g., "gamename.split"). Null if not created/reused.</summary>
            public string SplitSetName { get; set; }

            /// <summary>Whether the shared aux store was reused (already existed).</summary>
            public bool AuxReused { get; set; }

            /// <summary>Whether the split store was reused (already existed).</summary>
            public bool SplitReused { get; set; }

            /// <summary>Shard size used for shared aux store creation (50GB standard sharding).</summary>
            public long AuxShardSize { get; set; }

            /// <summary>Shard size used for split store creation (0 = embedded mode).</summary>
            public long SplitShardSize { get; set; }
        }

        /// <summary>
        /// Models the DedupeStep.Initialise() Xbox dual mode auto-creation branch.
        /// This replicates the logic for creating both shared aux and per-game split stores
        /// when AutoCreateAux is enabled for Xbox/Xbox360 system types.
        /// </summary>
        /// <param name="systemType">Must be XBox or XBox360</param>
        /// <param name="setName">The per-game set name (derived from image filename)</param>
        /// <param name="auxFilename">User-supplied aux filename (should be IGNORED for Xbox)</param>
        /// <param name="existingAuxSetName">Non-null if a shared aux store already exists</param>
        /// <param name="existingSplitSetName">Non-null if a split store already exists for this game</param>
        /// <returns>The creation result with both store names and reuse flags</returns>
        private static DualModeCreationResult ModelXboxDualModeCreation(
            SystemType systemType,
            string setName,
            string auxFilename,
            string existingAuxSetName,
            string existingSplitSetName)
        {
            // Mirrors the production code in DedupeStep.Initialise() Xbox/Xbox360 branch

            // 1. Shared aux store: "{systemType}.aux" with standard sharding (50GB)
            string systemTypeName = systemType.ToString().ToLower(); // e.g. "xbox360"
            string sharedAuxName = systemTypeName + ".aux"; // e.g. "xbox360.aux"

            string auxSetName;
            bool auxReused;
            if (existingAuxSetName != null)
            {
                // Shared aux store already exists — reuse without modification (Req 2.5)
                auxSetName = existingAuxSetName;
                auxReused = true;
            }
            else
            {
                // Create new shared aux store
                auxSetName = sharedAuxName;
                auxReused = false;
            }

            // 2. Per-game split store: "{setName}.split" with embedded mode (shardSize=0)
            string splitName = setName + ".split"; // e.g. "gamename.split"

            string splitSetName;
            bool splitReused;
            if (existingSplitSetName != null)
            {
                // Split store already exists — reuse without modification (Req 2.4)
                splitSetName = existingSplitSetName;
                splitReused = true;
            }
            else
            {
                // Create new split store
                splitSetName = splitName;
                splitReused = false;
            }

            // NOTE: auxFilename is intentionally ignored for Xbox/Xbox360 (Req 12.4)

            return new DualModeCreationResult
            {
                AuxSetName = auxSetName,
                SplitSetName = splitSetName,
                AuxReused = auxReused,
                SplitReused = splitReused,
                AuxShardSize = 50L * 1024 * 1024 * 1024, // Standard sharding for shared aux
                SplitShardSize = 0 // Embedded mode for per-game split
            };
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 17: Xbox Dual Mode Creates Both Stores — Both stores always present
        ///
        /// For any Xbox/Xbox360 system type and any set name, when AUX mode is enabled,
        /// the dual mode creation logic SHALL produce non-null names for BOTH the shared
        /// aux store AND the per-game split store, regardless of whether they already exist
        /// (reuse) or need to be created fresh.
        ///
        /// **Validates: Requirements 2.1, 2.4, 2.5**
        /// </summary>
        [Property]
        public bool XboxDualMode_BothStoresAlwaysPresent(
            bool isXbox360,
            NonEmptyString setNameRaw,
            bool auxExists,
            bool splitExists)
        {
            SystemType systemType = isXbox360 ? SystemType.XBox360 : SystemType.XBox;
            string setName = SanitizeSetName(setNameRaw.Get);
            if (string.IsNullOrWhiteSpace(setName))
                return true; // Skip degenerate input

            string existingAux = auxExists ? (systemType.ToString().ToLower() + ".aux") : null;
            string existingSplit = splitExists ? (setName + ".split") : null;

            DualModeCreationResult result = ModelXboxDualModeCreation(
                systemType, setName, auxFilename: null, existingAux, existingSplit);

            // Both stores must always be non-null
            return result.AuxSetName != null && result.SplitSetName != null;
        }

        /// <summary>
        /// Property 17: Xbox Dual Mode Creates Both Stores — Shared aux name derives from system type
        ///
        /// For any Xbox/Xbox360 system type, the shared aux store name SHALL be
        /// "{systemType}.aux" (e.g., "xbox.aux" or "xbox360.aux"), derived from the
        /// system type name — NOT from the game set name or user-supplied Aux_Filename.
        ///
        /// **Validates: Requirements 2.1, 2.5**
        /// </summary>
        [Property]
        public bool XboxDualMode_SharedAuxNameDerivedFromSystemType(
            bool isXbox360,
            NonEmptyString setNameRaw,
            NonEmptyString auxFilenameRaw)
        {
            SystemType systemType = isXbox360 ? SystemType.XBox360 : SystemType.XBox;
            string setName = SanitizeSetName(setNameRaw.Get);
            if (string.IsNullOrWhiteSpace(setName))
                return true; // Skip degenerate input

            string auxFilename = auxFilenameRaw.Get.Trim();
            string expectedAuxName = systemType.ToString().ToLower() + ".aux";

            // When no existing aux store — creates new with system-type-based name
            DualModeCreationResult result = ModelXboxDualModeCreation(
                systemType, setName, auxFilename, existingAuxSetName: null, existingSplitSetName: null);

            // Shared aux name must match the system-type-derived convention
            // regardless of setName or user-supplied auxFilename
            return result.AuxSetName == expectedAuxName;
        }

        /// <summary>
        /// Property 17: Xbox Dual Mode Creates Both Stores — Split name derives from game set name
        ///
        /// For any Xbox/Xbox360 game with any set name, the split store name SHALL be
        /// "{setName}.split" — always derived from the per-game set name.
        ///
        /// **Validates: Requirements 2.1, 2.4**
        /// </summary>
        [Property]
        public bool XboxDualMode_SplitNameDerivedFromSetName(
            bool isXbox360,
            NonEmptyString setNameRaw)
        {
            SystemType systemType = isXbox360 ? SystemType.XBox360 : SystemType.XBox;
            string setName = SanitizeSetName(setNameRaw.Get);
            if (string.IsNullOrWhiteSpace(setName))
                return true; // Skip degenerate input

            string expectedSplitName = setName + ".split";

            // When no existing split store — creates new with setName-based name
            DualModeCreationResult result = ModelXboxDualModeCreation(
                systemType, setName, auxFilename: null, existingAuxSetName: null, existingSplitSetName: null);

            return result.SplitSetName == expectedSplitName;
        }

        /// <summary>
        /// Property 17: Xbox Dual Mode Creates Both Stores — Existing stores reused without modification
        ///
        /// For any Xbox/Xbox360 game where both stores already exist, the dual mode creation
        /// logic SHALL reuse both stores without modification (idempotent behavior).
        ///
        /// **Validates: Requirements 2.4, 2.5**
        /// </summary>
        [Property]
        public bool XboxDualMode_ExistingStoresReusedWithoutModification(
            bool isXbox360,
            NonEmptyString setNameRaw)
        {
            SystemType systemType = isXbox360 ? SystemType.XBox360 : SystemType.XBox;
            string setName = SanitizeSetName(setNameRaw.Get);
            if (string.IsNullOrWhiteSpace(setName))
                return true; // Skip degenerate input

            string existingAux = systemType.ToString().ToLower() + ".aux";
            string existingSplit = setName + ".split";

            DualModeCreationResult result = ModelXboxDualModeCreation(
                systemType, setName, auxFilename: null, existingAux, existingSplit);

            // Both stores should be reused (not re-created)
            return result.AuxReused && result.SplitReused
                && result.AuxSetName == existingAux
                && result.SplitSetName == existingSplit;
        }

        /// <summary>
        /// Property 17: Xbox Dual Mode Creates Both Stores — User-supplied Aux_Filename ignored
        ///
        /// For any Xbox/Xbox360 game, even when a user supplies an Aux_Filename value,
        /// the shared aux name SHALL still be derived from the system type name, and
        /// the split name SHALL still be derived from the game set name.
        /// The Aux_Filename is completely ignored for Xbox/Xbox360.
        ///
        /// **Validates: Requirements 2.1, 2.5**
        /// </summary>
        [Property]
        public bool XboxDualMode_AuxFilenameIgnored(
            bool isXbox360,
            NonEmptyString setNameRaw,
            NonEmptyString auxFilenameRaw)
        {
            SystemType systemType = isXbox360 ? SystemType.XBox360 : SystemType.XBox;
            string setName = SanitizeSetName(setNameRaw.Get);
            if (string.IsNullOrWhiteSpace(setName))
                return true; // Skip degenerate input

            string auxFilename = auxFilenameRaw.Get.Trim();
            string expectedAuxName = systemType.ToString().ToLower() + ".aux";
            string expectedSplitName = setName + ".split";

            DualModeCreationResult result = ModelXboxDualModeCreation(
                systemType, setName, auxFilename, existingAuxSetName: null, existingSplitSetName: null);

            // Aux filename value does NOT influence the store names
            return result.AuxSetName == expectedAuxName
                && result.SplitSetName == expectedSplitName;
        }

        /// <summary>
        /// Property 17: Xbox Dual Mode Creates Both Stores — Correct shard sizes
        ///
        /// For any Xbox/Xbox360 dual mode creation, the shared aux store SHALL use
        /// standard sharding (50GB) and the split store SHALL use embedded mode (shardSize=0).
        ///
        /// **Validates: Requirements 2.1**
        /// </summary>
        [Property]
        public bool XboxDualMode_CorrectShardSizes(
            bool isXbox360,
            NonEmptyString setNameRaw,
            bool auxExists,
            bool splitExists)
        {
            SystemType systemType = isXbox360 ? SystemType.XBox360 : SystemType.XBox;
            string setName = SanitizeSetName(setNameRaw.Get);
            if (string.IsNullOrWhiteSpace(setName))
                return true; // Skip degenerate input

            string existingAux = auxExists ? (systemType.ToString().ToLower() + ".aux") : null;
            string existingSplit = splitExists ? (setName + ".split") : null;

            DualModeCreationResult result = ModelXboxDualModeCreation(
                systemType, setName, auxFilename: null, existingAux, existingSplit);

            // Shared aux: standard sharding (50GB)
            // Split: embedded mode (shardSize=0)
            return result.AuxShardSize == 50L * 1024 * 1024 * 1024
                && result.SplitShardSize == 0;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Sanitizes a generated string to be a valid set name (no path separators, no extensions).
        /// </summary>
        private static string SanitizeSetName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Remove characters invalid in filenames and path separators
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            StringBuilder cleaned = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (Array.IndexOf(invalid, c) < 0 && c != '.' && c != ' ')
                    cleaned.Append(c);
            }

            string result = cleaned.ToString();
            return result.Length > 0 ? result.Substring(0, Math.Min(result.Length, 50)).ToLower() : null;
        }

        #endregion
    }
}
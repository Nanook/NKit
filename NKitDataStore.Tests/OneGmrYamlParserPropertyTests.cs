using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Ogmr;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for OgmrYamlParser.
    ///
    /// Feature: nkds-1gmr-command, Property 5: YAML round-trip structural integrity
    /// **Validates: Requirements 2.1, 2.4, 2.5, 2.6**
    /// </summary>
    public class OgmrYamlParserPropertyTests
    {
        /// <summary>
        /// Pool of valid game names (non-empty, no newlines, no YAML-breaking characters).
        /// </summary>
        private static readonly string[] GameNames = new[]
        {
            "Game Alpha", "Zelda Twilight Princess", "Mario Kart Wii",
            "Metroid Prime 3", "Super Smash Bros Brawl", "Donkey Kong Country Returns",
            "Wii Sports Resort", "Animal Crossing City Folk", "Fire Emblem Radiant Dawn",
            "Xenoblade Chronicles", "Kirby Return to Dream Land", "Pikmin 2 New Play Control",
            "007 GoldenEye", "Star Fox Assault", "F-Zero GX",
            "Sonic Colors", "Resident Evil 4 Wii Edition", "No More Heroes",
            "Red Steel 2", "Monster Hunter Tri"
        };

        /// <summary>
        /// Pool of simple alphanumeric mask patterns that are valid regex literals.
        /// </summary>
        private static readonly string[] MaskPatterns = new[]
        {
            "GameAlpha", "ZeldaTP", "MarioKart", "MetroidPrime3",
            "SmashBros", "DonkeyKong", "WiiSports", "AnimalCrossing",
            "FireEmblem", "Xenoblade", "Kirby", "Pikmin2",
            "GoldenEye007", "StarFox", "FZeroGX",
            "SonicColors", "ResidentEvil4", "NoMoreHeroes",
            "RedSteel2", "MonsterHunter"
        };

        /// <summary>
        /// Serializes game entries to the 1GMR YAML format.
        /// </summary>
        private static string SerializeToYaml(string[] names, int[][] maskIndices)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("games:");
            for (int i = 0; i < names.Length; i++)
            {
                sb.AppendLine($"  - name: {names[i]}");
                sb.AppendLine("    masks:");
                foreach (int maskIdx in maskIndices[i])
                {
                    string pattern = MaskPatterns[maskIdx % MaskPatterns.Length];
                    sb.AppendLine($"      - '{pattern}'");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// **Validates: Requirements 2.1, 2.4, 2.5, 2.6**
        ///
        /// Property 5: YAML round-trip structural integrity.
        /// For any valid list of game entries (each with a non-empty name and at least one
        /// non-empty mask string), serializing to the 1GMR YAML format and parsing back
        /// SHALL produce a List&lt;GameEntry&gt; where each entry's Name matches the original
        /// and each entry has the same number of masks as the original.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool YamlRoundTrip_PreservesNamesAndMaskCounts(
            NonNegativeInt entryCountSeed,
            NonNegativeInt nameSeed1,
            NonNegativeInt nameSeed2,
            NonNegativeInt nameSeed3,
            NonNegativeInt maskSeed1,
            NonNegativeInt maskSeed2,
            NonNegativeInt maskSeed3)
        {
            // Generate 1-5 entries
            int entryCount = (entryCountSeed.Get % 5) + 1;

            // Select names from the pool using seeds
            int[] nameSeeds = { nameSeed1.Get, nameSeed2.Get, nameSeed3.Get, nameSeed1.Get + nameSeed2.Get, nameSeed2.Get + nameSeed3.Get };
            string[] names = new string[entryCount];
            for (int i = 0; i < entryCount; i++)
                names[i] = GameNames[nameSeeds[i] % GameNames.Length];

            // Generate mask indices for each entry (1-3 masks per entry)
            int[] maskSeeds = { maskSeed1.Get, maskSeed2.Get, maskSeed3.Get, maskSeed1.Get + maskSeed3.Get, maskSeed2.Get + maskSeed1.Get };
            int[][] maskIndices = new int[entryCount][];
            for (int i = 0; i < entryCount; i++)
            {
                int maskCount = (maskSeeds[i] % 3) + 1; // 1-3 masks
                maskIndices[i] = new int[maskCount];
                for (int j = 0; j < maskCount; j++)
                    maskIndices[i][j] = (maskSeeds[i] + (j * 7)) % MaskPatterns.Length;
            }

            // Serialize to YAML
            string yaml = SerializeToYaml(names, maskIndices);

            // Write to a temp file and parse back
            string tempFile = Path.Combine(Path.GetTempPath(), $"1gmr_pbt_{Guid.NewGuid()}.yaml");
            try
            {
                File.WriteAllText(tempFile, yaml);

                // Parse back
                List<GameEntry> parsed = OgmrYamlParser.Parse(tempFile);

                // Verify entry count matches
                if (parsed.Count != entryCount)
                    return false;

                // Verify each entry's name and mask count
                for (int i = 0; i < entryCount; i++)
                {
                    if (parsed[i].Name != names[i])
                        return false;

                    if (parsed[i].CompiledMasks.Length != maskIndices[i].Length)
                        return false;
                }

                return true;
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
    }
}
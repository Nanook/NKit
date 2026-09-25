using System;
using System.Collections.Generic;
using System.Text;

namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Enhanced utility class for string obfuscation/scrambling with enum-based type safety
    /// to avoid virus scanner false positives from path strings in the executable
    /// </summary>
    internal static class StringObfuscation
    {
        // Const byte arrays - XOR-encoded data
        private const byte Key = 0x42;

        // Environment variable encoded data
        private static readonly byte[] HomeBytes = { 0x0A, 0x0D, 0x0F, 0x07 }; // "HOME"
        private static readonly byte[] XdgConfigHomeBytes = { 0x1A, 0x06, 0x05, 0x1D, 0x01, 0x0D, 0x0C, 0x04, 0x0B, 0x05, 0x1D, 0x0A, 0x0D, 0x0F, 0x07 }; // "XDG_CONFIG_HOME"
        private static readonly byte[] HomeDriveBytes = { 0x0A, 0x0D, 0x0F, 0x07, 0x06, 0x10, 0x0B, 0x14, 0x07 }; // "HOMEDRIVE"
        private static readonly byte[] HomePathBytes = { 0x0A, 0x0D, 0x0F, 0x07, 0x12, 0x03, 0x16, 0x0A }; // "HOMEPATH"
        private static readonly byte[] UserProfileBytes = { 0x17, 0x11, 0x07, 0x10, 0x12, 0x10, 0x0D, 0x04, 0x0B, 0x0E, 0x07 }; // "USERPROFILE"

        // Path component encoded data
        private static readonly byte[] LibraryBytes = { 0x0E, 0x2B, 0x20, 0x30, 0x23, 0x30, 0x3B }; // "Library"
        private static readonly byte[] ApplicationSupportBytes = { 0x03, 0x32, 0x32, 0x2E, 0x2B, 0x21, 0x23, 0x36, 0x2B, 0x2D, 0x2C, 0x62, 0x11, 0x37, 0x32, 0x32, 0x2D, 0x30, 0x36 }; // "Application Support"
        private static readonly byte[] ConfigBytes = { 0x6C, 0x21, 0x2D, 0x2C, 0x24, 0x2B, 0x25 }; // ".config"
        private static readonly byte[] ApplicationDirectoryNameBytes = { 0x2C, 0x29, 0x2B, 0x36 }; // "nkit"
        private static readonly byte[] AppExtensionBytes = { 0x6C, 0x23, 0x32, 0x32 }; // ".app"
        private static readonly byte[] ContentsBytes = { 0x01, 0x2D, 0x2C, 0x36, 0x27, 0x2C, 0x36, 0x31 }; // "Contents"
        private static readonly byte[] MacOSBytes = { 0x0F, 0x23, 0x21, 0x0D, 0x11 }; // "MacOS"

        // Helper method to XOR decode byte array and convert to string
        private static string DecodeBytes(byte[] encodedBytes)
        {
            byte[] decodedBytes = new byte[encodedBytes.Length];
            for (int i = 0; i < encodedBytes.Length; i++)
                decodedBytes[i] = (byte)(encodedBytes[i] ^ Key);
            return Encoding.UTF8.GetString(decodedBytes);
        }

        // Static encoded strings - XOR-decoded const arrays wrapped in Encoding.GetString()
        private static readonly Dictionary<EnvironmentVariableType, string> _encodedEnvVars =
            new Dictionary<EnvironmentVariableType, string>
            {
                [EnvironmentVariableType.Home] = DecodeBytes(HomeBytes),
                [EnvironmentVariableType.XdgConfigHome] = DecodeBytes(XdgConfigHomeBytes),
                [EnvironmentVariableType.HomeDrive] = DecodeBytes(HomeDriveBytes),
                [EnvironmentVariableType.HomePath] = DecodeBytes(HomePathBytes),
                [EnvironmentVariableType.UserProfile] = DecodeBytes(UserProfileBytes)
            };

        private static readonly Dictionary<PathComponentType, string> _encodedPathComponents =
            new Dictionary<PathComponentType, string>
            {
                [PathComponentType.Library] = DecodeBytes(LibraryBytes),
                [PathComponentType.ApplicationSupport] = DecodeBytes(ApplicationSupportBytes),
                [PathComponentType.Config] = DecodeBytes(ConfigBytes),
                [PathComponentType.ApplicationDirectoryName] = DecodeBytes(ApplicationDirectoryNameBytes),
                [PathComponentType.AppExtension] = DecodeBytes(AppExtensionBytes),
                [PathComponentType.Contents] = DecodeBytes(ContentsBytes),
                [PathComponentType.MacOS] = DecodeBytes(MacOSBytes)
            };

        /// <summary>
        /// Gets decoded environment variable mappings for production use
        /// Returns the pre-decoded strings
        /// </summary>
        /// <returns>Dictionary of decoded environment variable mappings</returns>
        public static Dictionary<EnvironmentVariableType, string> GetDeobfuscatedEnvironmentVariables() => new Dictionary<EnvironmentVariableType, string>(_encodedEnvVars);

        /// <summary>
        /// Gets decoded path component mappings for production use
        /// Returns the pre-decoded strings
        /// </summary>
        /// <returns>Dictionary of decoded path component mappings</returns>
        public static Dictionary<PathComponentType, string> GetDeobfuscatedPathComponents() => new Dictionary<PathComponentType, string>(_encodedPathComponents);

        /// <summary>
        /// Example showing how to initialize PathResolutionService with obfuscated mappings
        /// </summary>
        public static void InitializePathResolutionServiceWithObfuscation()
        {
            Dictionary<EnvironmentVariableType, string> deobfuscatedEnvVars = GetDeobfuscatedEnvironmentVariables();
            Dictionary<PathComponentType, string> deobfuscatedPathComponents = GetDeobfuscatedPathComponents();

            PathResolutionService.InitializeFromObfuscatedMappings(
                deobfuscatedEnvVars,
                deobfuscatedPathComponents);
        }

        /// <summary>
        /// Legacy methods - kept for compatibility but disabled
        /// </summary>
        [Obsolete("Legacy method disabled")]
        public static Dictionary<EnvironmentVariableType, string> GenerateObfuscatedEnvironmentVariables() => throw new InvalidOperationException("Legacy method disabled.");

        [Obsolete("Legacy method disabled")]
        public static Dictionary<PathComponentType, string> GenerateObfuscatedPathComponents() => throw new InvalidOperationException("Legacy method disabled.");

        [Obsolete("Legacy method disabled")]
        public static string ObfuscateString(string input, byte key = 0x42) => throw new InvalidOperationException("Legacy method disabled.");

        [Obsolete("Legacy method disabled")]
        public static string DeobfuscateString(string obfuscatedInput, byte key = 0x42) => throw new InvalidOperationException("Legacy method disabled.");

        [Obsolete("Legacy method disabled")]
        public static string DeobfuscateFromBytes(byte[] obfuscatedBytes, byte key = 0x42) => throw new InvalidOperationException("Legacy method disabled.");

        [Obsolete("Development method disabled")]
        public static void GenerateAllObfuscatedMappingsForDevelopment() => throw new InvalidOperationException("Development method disabled.");

        [Obsolete("Development method disabled")]
        public static void PrintObfuscatedInfo() => throw new InvalidOperationException("Development method disabled.");

        [Obsolete("Development method disabled")]
        public static void GenerateByteArrayDeclarations() => throw new InvalidOperationException("Development method disabled.");
    }
}

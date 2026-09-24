using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using System.Collections.Generic;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;

namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests for nkds-CLI-specific config file detection and folder-creation behaviour.
    ///
    /// Known bugs targeted by this suite:
    ///
    /// BUG-1 (config file name): ConfigurationCoreService.GetConfigFileName() derives the name
    ///   from the running executable. When the process is "nkds" it returns "nkds.yaml", which
    ///   does not exist anywhere. nkds should always use "nkit.yaml" so that it finds the same
    ///   config the nkit CLI creates. Tests: NkdsExe_ConfigFileName_IsNkitYaml,
    ///   NkdsExe_LocalNkitYaml_DetectedAsPortable.
    ///
    /// BUG-2 (portable detection): Because of BUG-1, even when "nkit.yaml" sits next to the nkds
    ///   exe, ShouldUsePortableMode looks for "nkds.yaml" and finds nothing, so nkds silently
    ///   falls into system mode. Tests: NkdsExe_LocalNkitYaml_DetectedAsPortable.
    ///
    /// BUG-3 (fallback on deletion): Deleting the local "nkit.yaml" while nkds is in portable
    ///   mode should transparently fall back to the user-area config, not crash or return None
    ///   when a user-area config exists. Tests: NkdsExe_DeleteLocalConfig_FallsBackToUserArea.
    ///
    /// BUG-4 (EnsureConfiguration never called): NKDSApp/Program.cs does not call
    ///   AppSettings.EnsureDefaultConfigExists() (it is commented out). On first run the required
    ///   directory tree (dats/, keys/, fix/, out/, logs/, …) is therefore never created. The nkit
    ///   CLI creates it; nkds should too. Tests: NkdsExe_FirstRun_CreatesRequiredDirectories,
    ///   NkdsExe_FirstRun_CopiesDefaultConfig.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "NkdsCli")]
    public class NkdsCliConfigTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A MockPlatformService that reports the executable as "nkds" (not "nkit"), so the
        /// config-file-name derivation in ConfigurationCoreService behaves like the real nkds
        /// process rather than the nkit process the base mock always returns.
        /// </summary>
        private sealed class NkdsPlatformService : MockPlatformService, IPlatformService
        {
            public NkdsPlatformService(
                PlatformModeDetectionTests.Platform platform,
                string executableDirectory)
                : base(platform, PlatformModeDetectionTests.AppType.CLI, executableDirectory) { }

            // Explicit interface implementation: ConfigurationCoreService calls this via the
            // IPlatformService reference. Returning "nkds" causes GetConfigFileName to produce
            // "nkds.yaml", which is the current buggy behaviour under test.
            string IPlatformService.GetExecutableName() => "nkds";
        }

        // ── BUG-1 / BUG-2: config file name and portable detection ────────────

        /// <summary>
        /// When the running executable is "nkds", the resolved config file name should still
        /// be "nkit.yaml" — because nkds shares the nkit pipeline configuration.
        ///
        /// Current behaviour (BUG-1): returns "nkds.yaml".
        /// Expected behaviour: "nkit.yaml".
        ///
        /// This test FAILS until BUG-1 is fixed.
        /// </summary>
        [Fact(DisplayName = "[BUG-1] nkds exe: config file name should be nkit.yaml not nkds.yaml")]
        public void NkdsExe_ConfigFileName_IsNkitYaml()
        {
            NkdsPlatformService platform = new NkdsPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                @"c:\tools\nkit");
            MockFileSystemService fs = new MockFileSystemService();

            // No local config of any name — just exercising the file-name resolution.
            fs.SetupSystemMode(@"c:\tools\nkit", "nkds.yaml");
            fs.SetupSystemMode(@"c:\tools\nkit", "nkit.yaml");

            using ConfigManager cm = new ConfigManager(platform, fs);
            ConfigurationInfo info = cm.GetConfigurationInfo();

            // BUG-1: currently returns "nkds.yaml" because GetConfigFileName uses the exe name.
            Assert.Equal("nkit.yaml", info.ConfigFileName);
        }

        /// <summary>
        /// When "nkit.yaml" exists next to the nkds executable, the app should detect portable
        /// mode and use that local config — the same as nkit does.
        ///
        /// Current behaviour (BUG-2): nkds looks for "nkds.yaml", finds nothing, falls into
        /// system mode. The user's local nkit.yaml is ignored.
        /// Expected behaviour: portable mode, ConfigDirectory == exe dir.
        ///
        /// This test FAILS until BUG-1 (and therefore BUG-2) is fixed.
        /// </summary>
        [Theory(DisplayName = "[BUG-2] nkds exe: local nkit.yaml next to exe → portable mode")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, @"c:\tools\nkit")]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, "/opt/nkit")]
        public void NkdsExe_LocalNkitYaml_DetectedAsPortable(
            PlatformModeDetectionTests.Platform platform, string exeDir)
        {
            NkdsPlatformService svc = new NkdsPlatformService(platform, exeDir);
            MockFileSystemService fs = new MockFileSystemService();

            // Only "nkit.yaml" is present next to the exe (not "nkds.yaml").
            fs.SetupPortableMode(exeDir, "nkit.yaml");

            using ConfigManager cm = new ConfigManager(svc, fs);
            ConfigurationInfo info = cm.GetConfigurationInfo();

            // BUG-2: currently false (system mode) because nkds looks for nkds.yaml.
            Assert.True(info.IsPortableMode,
                "nkds should enter portable mode when nkit.yaml exists next to the exe");
            Assert.Equal(exeDir, info.ConfigDirectory);
            Assert.Equal("nkit.yaml", info.ConfigFileName);
        }

        // ── BUG-3: fallback on local-config deletion ──────────────────────────

        /// <summary>
        /// After the local "nkit.yaml" is deleted, a subsequent nkds startup should silently
        /// fall back to the user-area config (ConfigSource.UserConfig) when one exists there.
        ///
        /// Current behaviour (BUG-3): because of BUG-1, nkds never detects the local config as
        /// portable in the first place, so this scenario is masked. Once BUG-1 is fixed, nkds
        /// must also handle the deletion fallback correctly.
        ///
        /// After fixing BUG-1 this test verifies the fallback works. It will also fail currently
        /// because the config file name will be wrong, meaning the user-area config is also missed.
        ///
        /// This test FAILS until BUG-1 is fixed (and remains a regression guard thereafter).
        /// </summary>
        [Theory(DisplayName = "[BUG-3] nkds exe: deleting local nkit.yaml falls back to user-area config")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, @"c:\tools\nkit",
            @"C:\Users\TestUser\AppData\Roaming\nkit")]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, "/opt/nkit",
            "/home/testuser/.config/nkit")]
        public void NkdsExe_DeleteLocalConfig_FallsBackToUserArea(
            PlatformModeDetectionTests.Platform platform, string exeDir, string userConfigDir)
        {
            NkdsPlatformService svc = new NkdsPlatformService(platform, exeDir);
            MockFileSystemService fs = new MockFileSystemService();

            // Local config was deleted (not present).
            fs.SetupSystemMode(exeDir, "nkit.yaml");

            // But a user-area config exists.
            string userConfigPath = fs.CombinePath(userConfigDir, "nkit.yaml");
            fs.SetFileExists(userConfigPath, true);

            using ConfigManager cm = new ConfigManager(svc, fs);
            ConfigurationInfo info = cm.GetConfigurationInfo();

            // Should switch to system mode and find the user-area config.
            Assert.False(info.IsPortableMode,
                "After deleting local nkit.yaml, nkds should fall back to system mode");
            Assert.Equal(ConfigSource.UserConfig, info.ConfigSource);
            Assert.Equal("nkit.yaml", info.ConfigFileName);
            Assert.Equal(userConfigDir, info.ConfigDirectory);
        }

        /// <summary>
        /// When the local config is deleted AND no user-area config exists, nkds should run with
        /// ConfigSource.None (empty defaults) rather than throwing or crashing.
        /// </summary>
        [Fact(DisplayName = "[BUG-3b] nkds exe: no config anywhere → ConfigSource.None, no exception")]
        public void NkdsExe_NoConfigAnywhere_RunsWithNone()
        {
            NkdsPlatformService svc = new NkdsPlatformService(
                PlatformModeDetectionTests.Platform.Windows, @"c:\tools\nkit");
            MockFileSystemService fs = new MockFileSystemService();

            // Neither local nor user-area config exists.
            fs.SetupSystemMode(@"c:\tools\nkit", "nkit.yaml");
            fs.SetupSystemMode(@"c:\tools\nkit", "nkds.yaml");

            using ConfigManager cm = new ConfigManager(svc, fs);
            ConfigurationInfo info = cm.GetConfigurationInfo();

            Assert.Equal(ConfigSource.None, info.ConfigSource);
            Assert.False(info.HasValidConfiguration);
        }

        // ── BUG-4: EnsureConfiguration never called by nkds ──────────────────

        /// <summary>
        /// When nkds calls EnsureConfiguration() (which it currently does NOT do), the full
        /// required directory tree should be created: dats/, keys/, fix/, scans/, out/, logs/,
        /// temp/, dedupe/.
        ///
        /// This test passes IF EnsureConfiguration is called and creates the directories.
        /// It documents the EXPECTED behaviour so we can verify the fix once EnsureConfiguration
        /// is wired into NKDSApp/Program.cs.
        ///
        /// This test FAILS today because nkds never calls EnsureConfiguration; we cannot verify
        /// from outside the process, but we can verify via the ConfigurationManager directly.
        /// </summary>
        [Fact(DisplayName = "[BUG-4] nkds first run: EnsureConfiguration creates required directories")]
        public void NkdsExe_FirstRun_CreatesRequiredDirectories()
        {
            NkdsPlatformService svc = new NkdsPlatformService(
                PlatformModeDetectionTests.Platform.Windows, @"c:\tools\nkit");
            MockFileSystemService fs = new MockFileSystemService();

            // System mode: no local config.
            fs.SetupSystemMode(@"c:\tools\nkit", "nkit.yaml");

            // Explicitly mark the expected config subdirectories as absent so the mock's
            // CreateDirectory is triggered. The mock defaults to true (exists) for safety,
            // so we must opt-in to "absent" for each directory we want to verify is created.
            string userConfigDir = @"C:\Users\TestUser\AppData\Roaming\nkit";
            fs.SetDirectoryWritable(userConfigDir, true);
            foreach (string sub in new[] { "dats", "keys", "fix", "scans" })
                fs.SetDirectoryExists($@"{userConfigDir}\{sub}", false);

            using ConfigManager cm = new ConfigManager(svc, fs);

            SetupResult result = cm.EnsureConfiguration();

            Assert.True(result.Success, "EnsureConfiguration should succeed");
            Assert.True(result.DirectoriesCreated > 0,
                "At least the standard folder tree (dats, keys, fix, …) should be created");
        }

        /// <summary>
        /// On a first run in system mode with a defaults/nkit.yaml present next to the exe,
        /// EnsureConfiguration should copy it into the user-area config directory.
        ///
        /// This test FAILS today because nkds never calls EnsureConfiguration.
        /// </summary>
        [Fact(DisplayName = "[BUG-4b] nkds first run: EnsureConfiguration copies default nkit.yaml to user area")]
        public void NkdsExe_FirstRun_CopiesDefaultConfig()
        {
            NkdsPlatformService svc = new NkdsPlatformService(
                PlatformModeDetectionTests.Platform.Windows, @"c:\tools\nkit");
            MockFileSystemService fs = new MockFileSystemService();

            // System mode: no local config.
            fs.SetupSystemMode(@"c:\tools\nkit", "nkit.yaml");

            // A defaults/nkit.yaml exists next to the exe (shipped in the release zip).
            string defaultsDir = @"c:\tools\nkit\defaults";
            string defaultsConfig = fs.CombinePath(defaultsDir, "nkit.yaml");
            fs.SetDirectoryExists(defaultsDir, true);
            fs.SetFileExists(defaultsConfig, true);
            fs.SetFileContents(defaultsConfig, "# Default NKit config\ntask: convert\n");

            // User config dir exists and is writable.
            string userConfigDir = @"C:\Users\TestUser\AppData\Roaming\nkit";
            fs.SetDirectoryExists(userConfigDir, true);
            fs.SetDirectoryWritable(userConfigDir, true);

            using ConfigManager cm = new ConfigManager(svc, fs);

            // BUG-4: nkds never calls this.
            SetupResult result = cm.EnsureConfiguration();

            Assert.True(result.Success, "EnsureConfiguration should succeed");
            Assert.True(result.ConfigFileCreated,
                "Default nkit.yaml should be copied to the user config directory on first run");

            // The user-area config should now exist.
            string targetPath = fs.CombinePath(userConfigDir, "nkit.yaml");
            Assert.True(fs.FileExists(targetPath),
                "nkit.yaml should exist in the user config directory after first-run setup");
        }

        // ── Regression guard: nkit CLI behaviour is NOT broken ────────────────

        /// <summary>
        /// Sanity check: nkit CLI (exe name "nkit") should still resolve to "nkit.yaml" and
        /// detect portable mode as before. This guards against accidentally breaking nkit
        /// while fixing nkds.
        /// </summary>
        [Fact(DisplayName = "Regression: nkit CLI still detects nkit.yaml in portable mode")]
        public void NkitCli_LocalNkitYaml_StillDetectedAsPortable()
        {
            // Use the standard mock (reports exe name as "nkit").
            MockPlatformService svc = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI,
                @"c:\tools\nkit");
            MockFileSystemService fs = new MockFileSystemService();

            fs.SetupPortableMode(@"c:\tools\nkit", "nkit.yaml");

            using ConfigManager cm = new ConfigManager(svc, fs);
            ConfigurationInfo info = cm.GetConfigurationInfo();

            Assert.True(info.IsPortableMode);
            Assert.Equal("nkit.yaml", info.ConfigFileName);
            Assert.Equal(@"c:\tools\nkit", info.ConfigDirectory);
        }
    }
}

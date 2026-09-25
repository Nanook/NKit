using NkdsUi.Services;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for ConfigService window state get/set behavior.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    public class ConfigServiceWindowStateTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _configPath;
        private readonly string _legacyPath;

        public ConfigServiceWindowStateTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds-winstate-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _configPath = Path.Combine(_tempDir, "nkds-ui.json");
            _legacyPath = Path.Combine(_tempDir, "export-format-preferences.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        /// <summary>
        /// Creates a ConfigService that operates in an isolated temp directory.
        /// </summary>
        private ConfigService CreateFreshService() => new ConfigService(_configPath, _legacyPath);

        // --- Requirement 5.3: Initial WindowState is null when no config file exists ---

        [Fact]
        public void WindowState_NoConfigFile_ReturnsNull()
        {
            ConfigService service = CreateFreshService();

            Assert.Null(service.WindowState);
        }

        // --- Requirement 5.1, 5.2: SetWindowState then WindowState returns correct values ---

        [Fact]
        public void SetWindowState_ThenGet_ReturnsCorrectValues()
        {
            ConfigService service = CreateFreshService();

            service.SetWindowState(1400, 900, 100, 50);

            WindowStateConfig state = service.WindowState;

            Assert.NotNull(state);
            Assert.Equal(1400, state.Width);
            Assert.Equal(900, state.Height);
            Assert.Equal(100, state.X);
            Assert.Equal(50, state.Y);
        }

        // --- Requirement 5.1: SetWindowState overwrites previous state ---

        [Fact]
        public void SetWindowState_OverwritesPreviousState()
        {
            ConfigService service = CreateFreshService();

            service.SetWindowState(800, 600, 0, 0);
            service.SetWindowState(1920, 1080, 200, 150);

            WindowStateConfig state = service.WindowState;

            Assert.NotNull(state);
            Assert.Equal(1920, state.Width);
            Assert.Equal(1080, state.Height);
            Assert.Equal(200, state.X);
            Assert.Equal(150, state.Y);
        }

        // --- Requirement 5.2: Window state persists across ConfigService instances ---

        [Fact]
        public void WindowState_PersistsAcrossInstances()
        {
            // Create first service and set window state
            ConfigService service1 = CreateFreshService();
            service1.SetWindowState(1600, 1000, 75, 25);

            // Create a new service instance from the same config path
            ConfigService service2 = new ConfigService(_configPath, _legacyPath);

            WindowStateConfig state = service2.WindowState;

            Assert.NotNull(state);
            Assert.Equal(1600, state.Width);
            Assert.Equal(1000, state.Height);
            Assert.Equal(75, state.X);
            Assert.Equal(25, state.Y);
        }
    }
}
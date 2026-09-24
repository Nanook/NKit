using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for WindowDecorationMode persistence round-trip.
///
/// Feature: nkds-settings-panel, Property 4: WindowDecorationMode Persistence Round-Trip
/// </summary>
public sealed class WindowDecorationModeRoundTripPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public WindowDecorationModeRoundTripPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WinDecorPropTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// **Validates: Requirements 7.3, 8.1**
    ///
    /// Property 4: WindowDecorationMode Persistence Round-Trip.
    /// For any valid WindowDecorationMode value, persisting it via SetWindowDecorationMode
    /// and then loading it via GetWindowDecorationMode SHALL return the same value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WindowDecorationMode_RoundTrip_ReturnsPersistedValue(int modeSeed)
    {
        // Generate a valid WindowDecorationMode from the enum values
        WindowDecorationMode[] validModes = (WindowDecorationMode[])Enum.GetValues(typeof(WindowDecorationMode));
        WindowDecorationMode mode = validModes[Math.Abs(modeSeed) % validModes.Length];

        string configPath = Path.Combine(_tempDir, $"cfg-wdm-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-wdm-{Guid.NewGuid():N}.json");

        try
        {
            ConfigService service = new ConfigService(configPath, legacyPath);

            // Set the mode
            service.SetWindowDecorationMode(mode);

            // Get the mode back
            WindowDecorationMode retrieved = service.GetWindowDecorationMode();

            return retrieved == mode;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(configPath + ".tmp"))
                File.Delete(configPath + ".tmp");
        }
    }

    /// <summary>
    /// **Validates: Requirements 7.3, 8.1**
    ///
    /// Property 4: WindowDecorationMode Persistence Round-Trip (across service instances).
    /// For any valid WindowDecorationMode value, persisting it via SetWindowDecorationMode
    /// and then creating a new ConfigService instance pointing to the same file SHALL
    /// return the same value via GetWindowDecorationMode.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WindowDecorationMode_RoundTrip_SurvivesReload(int modeSeed)
    {
        // Generate a valid WindowDecorationMode from the enum values
        WindowDecorationMode[] validModes = (WindowDecorationMode[])Enum.GetValues(typeof(WindowDecorationMode));
        WindowDecorationMode mode = validModes[Math.Abs(modeSeed) % validModes.Length];

        string configPath = Path.Combine(_tempDir, $"cfg-wdm-reload-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-wdm-reload-{Guid.NewGuid():N}.json");

        try
        {
            // First service instance: set the mode
            ConfigService service1 = new ConfigService(configPath, legacyPath);
            service1.SetWindowDecorationMode(mode);

            // Second service instance: load from the same file
            ConfigService service2 = new ConfigService(configPath, legacyPath);
            WindowDecorationMode retrieved = service2.GetWindowDecorationMode();

            return retrieved == mode;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(configPath + ".tmp"))
                File.Delete(configPath + ".tmp");
        }
    }
}
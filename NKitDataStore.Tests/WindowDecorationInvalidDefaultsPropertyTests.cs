using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;
using System.Text.Json;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for invalid configuration defaults.
///
/// Feature: nkds-settings-panel, Property 5: Invalid Configuration Defaults
/// </summary>
public sealed class WindowDecorationInvalidDefaultsPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public WindowDecorationInvalidDefaultsPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WinDecorInvalidDefaults_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// **Validates: Requirements 8.3, 8.4**
    ///
    /// Property 5: Invalid Configuration Defaults.
    /// For any string that is not a recognized WindowDecorationMode value (including null,
    /// empty string, corrupted JSON, or arbitrary text), loading the configuration SHALL
    /// return ClientSideDecorations as the default mode.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool InvalidWindowDecorationMode_ReturnsClientSideDecorations(NonEmptyString arbitraryValue)
    {
        // Filter out valid enum values — we only want invalid strings
        string value = arbitraryValue.Get;
        if (value == nameof(WindowDecorationMode.ClientSideDecorations) ||
            value == nameof(WindowDecorationMode.NativeTitleBar))
            return true; // Skip valid values — property is vacuously true for them

        string configPath = Path.Combine(_tempDir, $"cfg-invalid-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-invalid-{Guid.NewGuid():N}.json");

        try
        {
            // Write a valid JSON config with an invalid windowDecorationMode value
            string json = $"{{\"windowDecorationMode\": {JsonSerializer.Serialize(value)}}}";
            File.WriteAllText(configPath, json);

            ConfigService service = new ConfigService(configPath, legacyPath);
            WindowDecorationMode result = service.GetWindowDecorationMode();

            return result == WindowDecorationMode.ClientSideDecorations;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    /// <summary>
    /// **Validates: Requirements 8.3, 8.4**
    ///
    /// Property 5: Invalid Configuration Defaults (null value).
    /// When the windowDecorationMode field is null in the config file,
    /// GetWindowDecorationMode SHALL return ClientSideDecorations.
    /// </summary>
    [Fact]
    public void NullWindowDecorationMode_ReturnsClientSideDecorations()
    {
        string configPath = Path.Combine(_tempDir, $"cfg-null-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-null-{Guid.NewGuid():N}.json");

        try
        {
            string json = "{\"windowDecorationMode\": null}";
            File.WriteAllText(configPath, json);

            ConfigService service = new ConfigService(configPath, legacyPath);
            WindowDecorationMode result = service.GetWindowDecorationMode();

            Assert.Equal(WindowDecorationMode.ClientSideDecorations, result);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    /// <summary>
    /// **Validates: Requirements 8.3, 8.4**
    ///
    /// Property 5: Invalid Configuration Defaults (empty string).
    /// When the windowDecorationMode field is an empty string in the config file,
    /// GetWindowDecorationMode SHALL return ClientSideDecorations.
    /// </summary>
    [Fact]
    public void EmptyStringWindowDecorationMode_ReturnsClientSideDecorations()
    {
        string configPath = Path.Combine(_tempDir, $"cfg-empty-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-empty-{Guid.NewGuid():N}.json");

        try
        {
            string json = "{\"windowDecorationMode\": \"\"}";
            File.WriteAllText(configPath, json);

            ConfigService service = new ConfigService(configPath, legacyPath);
            WindowDecorationMode result = service.GetWindowDecorationMode();

            Assert.Equal(WindowDecorationMode.ClientSideDecorations, result);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    /// <summary>
    /// **Validates: Requirements 8.3, 8.4**
    ///
    /// Property 5: Invalid Configuration Defaults (corrupted JSON).
    /// When the config file contains unparseable JSON, GetWindowDecorationMode
    /// SHALL return ClientSideDecorations.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CorruptedJson_ReturnsClientSideDecorations(byte[] randomBytes)
    {
        string configPath = Path.Combine(_tempDir, $"cfg-corrupt-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-corrupt-{Guid.NewGuid():N}.json");

        try
        {
            byte[] content = randomBytes ?? Array.Empty<byte>();
            File.WriteAllBytes(configPath, content);

            ConfigService service = new ConfigService(configPath, legacyPath);
            WindowDecorationMode result = service.GetWindowDecorationMode();

            return result == WindowDecorationMode.ClientSideDecorations;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    /// <summary>
    /// **Validates: Requirements 8.3, 8.4**
    ///
    /// Property 5: Invalid Configuration Defaults (missing config file).
    /// When the config file does not exist, GetWindowDecorationMode
    /// SHALL return ClientSideDecorations.
    /// </summary>
    [Fact]
    public void MissingConfigFile_ReturnsClientSideDecorations()
    {
        string configPath = Path.Combine(_tempDir, $"cfg-missing-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-missing-{Guid.NewGuid():N}.json");

        // Don't create the file — it should not exist
        ConfigService service = new ConfigService(configPath, legacyPath);
        WindowDecorationMode result = service.GetWindowDecorationMode();

        Assert.Equal(WindowDecorationMode.ClientSideDecorations, result);
    }

    /// <summary>
    /// **Validates: Requirements 8.3, 8.4**
    ///
    /// Property 5: Invalid Configuration Defaults (whitespace-only strings).
    /// For any whitespace-only string as the windowDecorationMode value,
    /// GetWindowDecorationMode SHALL return ClientSideDecorations.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WhitespaceOnlyValue_ReturnsClientSideDecorations(PositiveInt lengthSeed)
    {
        string configPath = Path.Combine(_tempDir, $"cfg-ws-{Guid.NewGuid():N}.json");
        string legacyPath = Path.Combine(_tempDir, $"legacy-ws-{Guid.NewGuid():N}.json");

        try
        {
            // Generate a whitespace-only string of varying length (1-20 chars)
            int length = 1 + (lengthSeed.Get % 20);
            char[] whitespaceChars = new[] { ' ', '\t', '\n', '\r' };
            Random rng = new Random(lengthSeed.Get);
            string wsValue = new string(Enumerable.Range(0, length)
                .Select(_ => whitespaceChars[rng.Next(whitespaceChars.Length)])
                .ToArray());

            string json = $"{{\"windowDecorationMode\": {JsonSerializer.Serialize(wsValue)}}}";
            File.WriteAllText(configPath, json);

            ConfigService service = new ConfigService(configPath, legacyPath);
            WindowDecorationMode result = service.GetWindowDecorationMode();

            return result == WindowDecorationMode.ClientSideDecorations;
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }
}
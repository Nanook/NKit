using System.Text.Json.Serialization;

namespace NkdsUi.Services;

/// <summary>
/// AOT-compatible JSON serialization context for legacy format preferences migration.
/// Retained for use by <see cref="ConfigService.MigrateLegacyPreferences"/>.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class FormatPreferencesJsonContext : JsonSerializerContext;
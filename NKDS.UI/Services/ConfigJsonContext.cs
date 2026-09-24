using NkdsUi.Models;
using System.Text.Json.Serialization;

namespace NkdsUi.Services;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(FormatOptionsEntry))]
[JsonSerializable(typeof(KeysAndFixPathsConfig))]
[JsonSerializable(typeof(Dictionary<string, double>))]
internal partial class ConfigJsonContext : JsonSerializerContext;
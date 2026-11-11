using System.Text.Json.Serialization;

namespace DualPlayer.Core.Settings;

/// <summary>App-wide settings. Empty at v1; features add properties and bump <see cref="AppSettingsFile.CurrentVersion"/>.</summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = AppSettingsFile.CurrentVersion;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

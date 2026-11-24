using System.Text.Json.Serialization;

namespace DualPlayer.Core.Settings;

/// <summary>Remembered per-device offsets (ms, positive = sound heard later), keyed by OS endpoint id.</summary>
public sealed record DeviceOffsets
{
    public int SchemaVersion { get; init; } = DeviceOffsetsFile.CurrentVersion;

    public Dictionary<string, int> Offsets { get; init; } = [];
}

/// <summary>Definition of <c>device-offsets.json</c> (ADR-0006).</summary>
public static class DeviceOffsetsFile
{
    public const int CurrentVersion = 1;

    public static StateFileDefinition<DeviceOffsets> Definition { get; } = new(
        "device-offsets.json",
        CurrentVersion,
        DeviceOffsetsJsonContext.Default.DeviceOffsets,
        static () => new DeviceOffsets(),
        []);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DeviceOffsets))]
internal sealed partial class DeviceOffsetsJsonContext : JsonSerializerContext;

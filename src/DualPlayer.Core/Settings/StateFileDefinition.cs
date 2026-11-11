using System.Text.Json.Serialization.Metadata;

namespace DualPlayer.Core.Settings;

/// <summary>
/// Describes one persisted JSON file: its name, current schema version, source-generated type info,
/// default value and the ordered upgrader chain from version 0.
/// </summary>
public sealed class StateFileDefinition<T> where T : class
{
    public StateFileDefinition(
        string fileName,
        int currentVersion,
        JsonTypeInfo<T> typeInfo,
        Func<T> createDefault,
        IReadOnlyList<IStateUpgrader> upgraders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(currentVersion);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(createDefault);
        ArgumentNullException.ThrowIfNull(upgraders);

        FileName = fileName;
        CurrentVersion = currentVersion;
        TypeInfo = typeInfo;
        CreateDefault = createDefault;
        Upgraders = upgraders;
    }

    public string FileName { get; }

    public int CurrentVersion { get; }

    public JsonTypeInfo<T> TypeInfo { get; }

    public Func<T> CreateDefault { get; }

    public IReadOnlyList<IStateUpgrader> Upgraders { get; }
}

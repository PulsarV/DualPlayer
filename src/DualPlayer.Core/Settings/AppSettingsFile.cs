using System.Text.Json.Nodes;

namespace DualPlayer.Core.Settings;

/// <summary>Definition of <c>settings.json</c>: version, default and the upgrader chain.</summary>
public static class AppSettingsFile
{
    public const int CurrentVersion = 1;

    public static StateFileDefinition<AppSettings> Definition { get; } = new(
        "settings.json",
        CurrentVersion,
        SettingsJsonContext.Default.AppSettings,
        static () => new AppSettings(),
        [new V0ToV1()]);

    /// <summary>v0 = a file written before versioning existed; v1 only adds <c>schemaVersion</c>.</summary>
    private sealed class V0ToV1 : IStateUpgrader
    {
        public int FromVersion => 0;

        public JsonObject Upgrade(JsonObject document)
        {
            document[JsonFileStateStore.SchemaVersionProperty] = 1;
            return document;
        }
    }
}

using System.Text.Json.Nodes;

namespace DualPlayer.Core.Settings;

/// <summary>Lifts one state file document from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.</summary>
public interface IStateUpgrader
{
    int FromVersion { get; }

    JsonObject Upgrade(JsonObject document);
}

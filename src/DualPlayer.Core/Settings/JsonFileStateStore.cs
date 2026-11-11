using System.Text.Json;
using System.Text.Json.Nodes;

namespace DualPlayer.Core.Settings;

/// <summary>
/// <see cref="IStateStore"/> over JSON files in one directory. Every file carries <c>schemaVersion</c>;
/// writes go to a temp file that is then renamed over the target, so an interrupted write never
/// corrupts the previous file.
/// </summary>
public sealed class JsonFileStateStore : IStateStore
{
    public const string SchemaVersionProperty = "schemaVersion";

    private readonly string _directory;

    public JsonFileStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public Result<T> Load<T>(StateFileDefinition<T> definition) where T : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        var path = PathFor(definition.FileName);
        if (!File.Exists(path))
        {
            return Result<T>.Success(definition.CreateDefault());
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject document)
            {
                return Result<T>.Failure("state.invalid", $"{definition.FileName} is not a JSON object.");
            }

            var version = ReadVersion(document);
            if (version > definition.CurrentVersion)
            {
                return Result<T>.Failure(
                    "state.too-new",
                    $"{definition.FileName} has schemaVersion {version}, newer than supported {definition.CurrentVersion}.");
            }

            for (; version < definition.CurrentVersion; version++)
            {
                var upgrader = definition.Upgraders.FirstOrDefault(u => u.FromVersion == version);
                if (upgrader is null)
                {
                    return Result<T>.Failure(
                        "state.no-upgrader",
                        $"No upgrader lifts {definition.FileName} from schemaVersion {version}.");
                }

                document = upgrader.Upgrade(document);
            }

            document[SchemaVersionProperty] = definition.CurrentVersion;
            var value = document.Deserialize(definition.TypeInfo);
            return value is null
                ? Result<T>.Failure("state.invalid", $"{definition.FileName} deserialized to null.")
                : Result<T>.Success(value);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Result<T>.Failure("state.unreadable", $"{definition.FileName}: {ex.Message}");
        }
    }

    public Result Save<T>(StateFileDefinition<T> definition, T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(value);
        var path = PathFor(definition.FileName);
        var tempPath = path + ".tmp";

        try
        {
            var document = JsonSerializer.SerializeToNode(value, definition.TypeInfo) as JsonObject
                ?? throw new InvalidOperationException($"{typeof(T).Name} must serialize to a JSON object.");
            document[SchemaVersionProperty] = definition.CurrentVersion;

            Directory.CreateDirectory(_directory);
            File.WriteAllText(tempPath, document.ToJsonString(WriteOptions));
            File.Move(tempPath, path, overwrite: true);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("state.unwritable", $"{definition.FileName}: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    private static int ReadVersion(JsonObject document) =>
        document[SchemaVersionProperty] is JsonValue v && v.TryGetValue(out int version) ? version : 0;
}

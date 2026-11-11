using DualPlayer.Core.Settings;

namespace DualPlayer.Core.Tests.Settings;

public sealed class JsonFileStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, AppSettingsFile.Definition.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_loads_the_default()
    {
        var result = new JsonFileStateStore(_directory).Load(AppSettingsFile.Definition);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SchemaVersion.ShouldBe(AppSettingsFile.CurrentVersion);
    }

    [Fact]
    public void Saved_v1_settings_reload()
    {
        var store = new JsonFileStateStore(_directory);

        store.Save(AppSettingsFile.Definition, new AppSettings()).IsSuccess.ShouldBeTrue();
        var reloaded = store.Load(AppSettingsFile.Definition);

        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.ShouldBe(new AppSettings { SchemaVersion = 1 });
        File.ReadAllText(SettingsPath).ShouldContain("\"schemaVersion\": 1");
    }

    [Fact]
    public void V0_file_is_lifted_to_v1_through_the_upgrader_chain()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{}");

        var result = new JsonFileStateStore(_directory).Load(AppSettingsFile.Definition);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SchemaVersion.ShouldBe(1);
    }

    [Fact]
    public void Newer_schema_version_is_an_error_not_a_crash()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 99 }""");

        var result = new JsonFileStateStore(_directory).Load(AppSettingsFile.Definition);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("state.too-new");
    }

    [Fact]
    public void Interrupted_write_leaves_the_previous_file_intact()
    {
        var store = new JsonFileStateStore(_directory);
        store.Save(AppSettingsFile.Definition, new AppSettings()).IsSuccess.ShouldBeTrue();
        var before = File.ReadAllText(SettingsPath);

        // A crash between writing the temp file and the rename leaves a partial temp file behind.
        File.WriteAllText(SettingsPath + ".tmp", "{ \"schemaVer");

        File.ReadAllText(SettingsPath).ShouldBe(before);
        var reloaded = store.Load(AppSettingsFile.Definition);
        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.SchemaVersion.ShouldBe(1);

        // The next save replaces the stale temp file and succeeds.
        store.Save(AppSettingsFile.Definition, new AppSettings()).IsSuccess.ShouldBeTrue();
        File.Exists(SettingsPath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Corrupt_file_is_an_error_not_a_crash()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ not json");

        var result = new JsonFileStateStore(_directory).Load(AppSettingsFile.Definition);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("state.unreadable");
    }
}

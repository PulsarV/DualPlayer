using DualPlayer.Core.Settings;

namespace DualPlayer.Core.Tests.Settings;

public sealed class JsonFileStateStoreDurabilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Saving_flushes_the_temp_file_to_disk_before_it_is_renamed()
    {
        var flushes = new List<bool>();
        FlushRecordingStream? stream = null;
        var store = new JsonFileStateStore(_directory, tempPath =>
        {
            Directory.CreateDirectory(_directory);
            return stream = new FlushRecordingStream(tempPath, flushes);
        });

        store.Save(AppSettingsFile.Definition, new AppSettings()).IsSuccess.ShouldBeTrue();

        stream.ShouldNotBeNull("the store must write the temp file through the supplied stream");
        flushes.ShouldContain(true, "the temp file must be flushed to disk before the rename");
        File.ReadAllText(Path.Combine(_directory, AppSettingsFile.Definition.FileName)).ShouldContain("\"schemaVersion\"");
    }

    private sealed class FlushRecordingStream(string path, List<bool> flushes)
        : FileStream(path, FileMode.Create, FileAccess.Write)
    {
        public override void Flush(bool flushToDisk)
        {
            flushes.Add(flushToDisk);
            base.Flush(flushToDisk);
        }
    }
}

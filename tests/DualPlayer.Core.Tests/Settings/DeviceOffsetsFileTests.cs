using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Settings;

public sealed class DeviceOffsetsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "device-offsets.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Definition_is_v1_named_device_offsets_json_with_an_empty_default()
    {
        DeviceOffsetsFile.Definition.FileName.ShouldBe("device-offsets.json");
        DeviceOffsetsFile.Definition.CurrentVersion.ShouldBe(1);
        DeviceOffsetsFile.Definition.Upgraders.ShouldBeEmpty();
        DeviceOffsetsFile.Definition.CreateDefault().Offsets.ShouldBeEmpty();
    }

    [Fact]
    public void Offsets_round_trip_through_the_store_in_the_documented_shape()
    {
        var store = new JsonFileStateStore(_directory);
        var offsets = new DeviceOffsetsBuilder().With("{0.0.0.00000000}.{abc}", -40).With("dev2", 120).Build();

        store.Save(DeviceOffsetsFile.Definition, offsets).IsSuccess.ShouldBeTrue();
        var reloaded = store.Load(DeviceOffsetsFile.Definition);

        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.Offsets["{0.0.0.00000000}.{abc}"].ShouldBe(-40);
        reloaded.Value.Offsets["dev2"].ShouldBe(120);
        var text = File.ReadAllText(FilePath);
        text.ShouldContain("\"schemaVersion\": 1");
        text.ShouldContain("\"offsets\"");
    }

    [Fact]
    public void Schema_version_2_is_state_too_new()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "schemaVersion": 2, "offsets": {} }""");

        var result = new JsonFileStateStore(_directory).Load(DeviceOffsetsFile.Definition);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("state.too-new");
    }

    [Fact]
    public void Offset_pressed_then_abandoned_without_shutdown_is_remembered_by_a_fresh_store_after_1_s()
    {
        var time = new FakeTimeProvider();
        var book = new OffsetBook(new JsonFileStateStore(_directory), time, NullLogger<OffsetBook>.Instance);
        book.Load();

        book.Step("dev1", 10);
        book.Step("dev1", 10);
        time.Advance(TimeSpan.FromSeconds(1));
        // Session abandoned: no dispose, no orderly shutdown.

        var next = new OffsetBook(new JsonFileStateStore(_directory), new FakeTimeProvider(), NullLogger<OffsetBook>.Instance);
        next.Load().ShouldBeEmpty();
        next.Get("dev1").ShouldBe(20);
    }

    [Fact]
    public void Offset_pressed_then_disposed_within_a_second_is_remembered_by_a_fresh_store()
    {
        var time = new FakeTimeProvider();
        var book = new OffsetBook(new JsonFileStateStore(_directory), time, NullLogger<OffsetBook>.Instance);
        book.Load();

        book.Step("dev1", 10);
        time.Advance(TimeSpan.FromMilliseconds(300));
        book.Dispose();
        time.Advance(TimeSpan.FromSeconds(5));

        var next = new OffsetBook(new JsonFileStateStore(_directory), new FakeTimeProvider(), NullLogger<OffsetBook>.Instance);
        next.Load().ShouldBeEmpty();
        next.Get("dev1").ShouldBe(10);
    }

    [Fact]
    public void A_null_offsets_object_in_the_file_warns_state_invalid_and_starts_every_offset_at_zero()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "schemaVersion": 1, "offsets": null }""");
        var book = new OffsetBook(new JsonFileStateStore(_directory), new FakeTimeProvider(), NullLogger<OffsetBook>.Instance);

        var warnings = book.Load();

        warnings.Single().Code.ShouldBe("state.invalid");
        book.Get("dev1").ShouldBe(0);
    }

    [Fact]
    public void Value_900_and_15_in_the_file_load_as_500_and_20_with_an_offsets_clamped_warning_each()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "schemaVersion": 1, "offsets": { "a": 900, "b": 15, "c": 30 } }""");
        var book = new OffsetBook(new JsonFileStateStore(_directory), new FakeTimeProvider(), NullLogger<OffsetBook>.Instance);

        var warnings = book.Load();

        book.Get("a").ShouldBe(500);
        book.Get("b").ShouldBe(20);
        book.Get("c").ShouldBe(30);
        warnings.Count.ShouldBe(2);
        warnings.ShouldAllBe(w => w.Code == "offsets.clamped");
        warnings.ShouldContain(w => w.Message.Contains("a", StringComparison.Ordinal));
        warnings.ShouldContain(w => w.Message.Contains("b", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_without_the_offsets_key_warns_state_invalid_and_starts_at_zero_like_offsets_null()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "schemaVersion": 1 }""");
        using var book = new OffsetBook(new JsonFileStateStore(_directory), new FakeTimeProvider(), NullLogger<OffsetBook>.Instance);

        var warning = book.Load().ShouldHaveSingleItem();

        warning.Code.ShouldBe("state.invalid");
        book.Get("dev-1").ShouldBe(0);
    }

    [Theory]
    [InlineData(int.MaxValue, 500)]
    [InlineData(int.MinValue, -500)]
    [InlineData(int.MaxValue - 4, 500)]
    [InlineData(int.MinValue + 4, -500)]
    public void A_huge_stored_offset_clamps_to_the_bound_on_its_own_side(int stored, int expected)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, $$"""{ "schemaVersion": 1, "offsets": { "dev-1": {{stored}} } }""");
        using var book = new OffsetBook(new JsonFileStateStore(_directory), new FakeTimeProvider(), NullLogger<OffsetBook>.Instance);

        book.Load();

        book.Get("dev-1").ShouldBe(expected);
    }
}

using DualPlayer.Core.Settings;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DualPlayer.Core.Tests.Sync;

public sealed class OffsetBookTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly IStateStore _store = Substitute.For<IStateStore>();

    private OffsetBook CreateBook(DeviceOffsets? stored = null)
    {
        _store.Load(DeviceOffsetsFile.Definition)
            .Returns(Result<DeviceOffsets>.Success(stored ?? new DeviceOffsets()));
        _store.Save(DeviceOffsetsFile.Definition, Arg.Any<DeviceOffsets>()).Returns(Result.Success());
        var book = new OffsetBook(_store, _time, NullLogger<OffsetBook>.Instance);
        book.Load();
        return book;
    }

    private int Saves => _store.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IStateStore.Save));

    [Fact]
    public void An_unknown_device_starts_at_zero()
    {
        CreateBook().Get("dev").ShouldBe(0);
    }

    [Fact]
    public void Each_press_moves_the_offset_by_10_ms_later_or_earlier()
    {
        var book = CreateBook();

        book.Step("dev", 10).ShouldBe(new OffsetStepResult(10, AtLimit: false));
        book.Step("dev", 10).OffsetMs.ShouldBe(20);
        book.Step("dev", -10).OffsetMs.ShouldBe(10);
        book.Step("dev", -10);
        book.Step("dev", -10).ShouldBe(new OffsetStepResult(-10, AtLimit: false));
        book.Get("dev").ShouldBe(-10);
    }

    [Fact]
    public void Offsets_are_kept_per_device()
    {
        var book = CreateBook();

        book.Step("a", 10);

        book.Get("b").ShouldBe(0);
    }

    [Fact]
    public void Pressing_later_at_plus_500_stays_at_the_limit_and_says_so()
    {
        var book = CreateBook(new DeviceOffsetsBuilder().With("dev", 500).Build());

        book.Step("dev", 10).ShouldBe(new OffsetStepResult(500, AtLimit: true));
        book.Get("dev").ShouldBe(500);
    }

    [Fact]
    public void Pressing_earlier_at_minus_500_stays_at_the_limit_and_says_so()
    {
        var book = CreateBook(new DeviceOffsetsBuilder().With("dev", -500).Build());

        book.Step("dev", -10).ShouldBe(new OffsetStepResult(-500, AtLimit: true));
    }

    [Fact]
    public void Reaching_the_limit_by_a_press_is_not_yet_at_limit()
    {
        var book = CreateBook(new DeviceOffsetsBuilder().With("dev", 490).Build());

        book.Step("dev", 10).ShouldBe(new OffsetStepResult(500, AtLimit: false));
    }

    [Fact]
    public void Remembered_offset_is_returned_after_load_AC_12()
    {
        CreateBook(new DeviceOffsetsBuilder().With("dev", -70).Build()).Get("dev").ShouldBe(-70);
    }

    [Fact]
    public void Several_presses_within_a_second_cause_one_save_one_second_after_the_last()
    {
        var book = CreateBook();

        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromMilliseconds(600));
        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Saves.ShouldBe(0);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        Saves.ShouldBe(1);
        _store.Received(1).Save(
            DeviceOffsetsFile.Definition,
            Arg.Is<DeviceOffsets>(o => o.Offsets["dev"] == 20));
    }

    [Fact]
    public void Disposing_with_a_save_still_waiting_writes_it_once_and_the_timer_does_not_write_again()
    {
        var book = CreateBook();

        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromMilliseconds(400));
        book.Dispose();
        _time.Advance(TimeSpan.FromSeconds(5));

        Saves.ShouldBe(1);
        _store.Received(1).Save(
            DeviceOffsetsFile.Definition,
            Arg.Is<DeviceOffsets>(o => o.Offsets["dev"] == 10));
    }

    [Fact]
    public void Disposing_with_nothing_pending_does_not_save()
    {
        var book = CreateBook();

        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromSeconds(1));
        book.Dispose();

        Saves.ShouldBe(1);
    }

    [Fact]
    public void A_press_at_the_limit_does_not_schedule_a_save()
    {
        var book = CreateBook(new DeviceOffsetsBuilder().With("dev", 500).Build());

        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromSeconds(2));

        Saves.ShouldBe(0);
    }

    [Fact]
    public void A_failed_save_is_reported_and_the_in_memory_offset_is_kept()
    {
        var book = CreateBook();
        _store.Save(DeviceOffsetsFile.Definition, Arg.Any<DeviceOffsets>())
            .Returns(Result.Failure("state.unreadable", "disk full"));
        Error? reported = null;
        book.SaveFailed += e => reported = e;

        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromSeconds(1));

        reported.ShouldNotBeNull();
        book.Get("dev").ShouldBe(10);
    }

    [Theory]
    [InlineData("state.unreadable")]
    [InlineData("state.invalid")]
    [InlineData("state.too-new")]
    public void An_unreadable_file_starts_every_offset_at_zero_warns_and_is_not_overwritten_before_a_press(string code)
    {
        _store.Load(DeviceOffsetsFile.Definition).Returns(Result<DeviceOffsets>.Failure(code, "bad"));
        _store.Save(DeviceOffsetsFile.Definition, Arg.Any<DeviceOffsets>()).Returns(Result.Success());
        var book = new OffsetBook(_store, _time, NullLogger<OffsetBook>.Instance);

        var warnings = book.Load();
        _time.Advance(TimeSpan.FromSeconds(5));

        warnings.Single().Code.ShouldBe(code);
        book.Get("dev").ShouldBe(0);
        Saves.ShouldBe(0);

        book.Step("dev", 10);
        _time.Advance(TimeSpan.FromSeconds(1));

        Saves.ShouldBe(1);
    }

    [Fact]
    public void Out_of_range_and_off_grid_values_are_corrected_with_a_warning_per_device()
    {
        var book = CreateBook(new DeviceOffsetsBuilder().With("a", 900).With("b", 15).With("c", -900).Build());

        var warnings = book.Load();

        book.Get("a").ShouldBe(500);
        book.Get("b").ShouldBe(20);
        book.Get("c").ShouldBe(-500);
        warnings.Count.ShouldBe(3);
        warnings.ShouldAllBe(w => w.Code == "offsets.clamped");
    }

    [Fact]
    public void A_clamped_offset_is_reported_in_the_cli_md_line_format()
    {
        var store = Substitute.For<IStateStore>();
        store.Load(DeviceOffsetsFile.Definition).Returns(Result<DeviceOffsets>.Success(new DeviceOffsets { Offsets = new Dictionary<string, int> { ["dev-x"] = 900 } }));
        using var book = new OffsetBook(store, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OffsetBook>.Instance);

        book.Load().ShouldHaveSingleItem().Message.ShouldBe("device dev-x offset 900 -> 500");
    }
}

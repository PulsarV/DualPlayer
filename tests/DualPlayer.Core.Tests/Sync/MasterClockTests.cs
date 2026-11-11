using DualPlayer.Core.Sync;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

public sealed class MasterClockTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void A_new_clock_is_stopped_at_zero()
    {
        var clock = new MasterClock(_time);

        clock.IsRunning.ShouldBeFalse();
        clock.PositionAt(_time.GetTimestamp()).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_started_clock_advances_with_the_time_provider()
    {
        var clock = new MasterClock(_time);
        clock.Start();

        _time.Advance(TimeSpan.FromSeconds(3));

        clock.IsRunning.ShouldBeTrue();
        clock.PositionAt(_time.GetTimestamp()).ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_stopped_clock_holds_its_position_for_any_later_timestamp()
    {
        var clock = new MasterClock(_time);
        clock.Start();
        _time.Advance(TimeSpan.FromSeconds(2));
        clock.StopAt(_time.GetTimestamp());

        _time.Advance(TimeSpan.FromSeconds(10));

        clock.IsRunning.ShouldBeFalse();
        clock.PositionAt(_time.GetTimestamp()).ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Jumping_a_running_clock_moves_the_position_and_keeps_it_running()
    {
        var clock = new MasterClock(_time);
        clock.Start();
        _time.Advance(TimeSpan.FromSeconds(1));

        clock.JumpTo(TimeSpan.FromSeconds(100));
        _time.Advance(TimeSpan.FromSeconds(2));

        clock.IsRunning.ShouldBeTrue();
        clock.PositionAt(_time.GetTimestamp()).ShouldBe(TimeSpan.FromSeconds(102));
    }

    [Fact]
    public void Jumping_a_stopped_clock_moves_the_position_and_keeps_it_stopped()
    {
        var clock = new MasterClock(_time);

        clock.JumpTo(TimeSpan.FromSeconds(50));
        _time.Advance(TimeSpan.FromSeconds(5));

        clock.IsRunning.ShouldBeFalse();
        clock.PositionAt(_time.GetTimestamp()).ShouldBe(TimeSpan.FromSeconds(50));
    }

    [Fact]
    public void Restarting_after_a_stop_resumes_from_the_stop_position()
    {
        var clock = new MasterClock(_time);
        clock.Start();
        _time.Advance(TimeSpan.FromSeconds(2));
        clock.StopAt(_time.GetTimestamp());
        _time.Advance(TimeSpan.FromSeconds(10));

        clock.Start();
        _time.Advance(TimeSpan.FromSeconds(1));

        clock.PositionAt(_time.GetTimestamp()).ShouldBe(TimeSpan.FromSeconds(3));
    }
}

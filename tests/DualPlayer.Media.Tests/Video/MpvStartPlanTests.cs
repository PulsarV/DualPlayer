using DualPlayer.Media.Video;

namespace DualPlayer.Media.Tests.Video;

/// <summary>Where a start seeks to so the picture can be up when the start reaches it (1 tick = 1 ms).</summary>
public sealed class MpvStartPlanTests
{
    private const long Frequency = 1000;

    [Fact]
    public void A_start_further_away_than_the_seek_takes_seeks_to_the_requested_position()
    {
        MpvStartPlan.SeekTarget(TimeSpan.FromSeconds(10), start: 1300, now: 1000, seekBudget: TimeSpan.FromMilliseconds(100), speed: 1.0, Frequency)
            .ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_start_due_now_seeks_to_where_the_content_will_be_once_the_seek_is_done()
    {
        MpvStartPlan.SeekTarget(TimeSpan.FromSeconds(10), start: 1000, now: 1000, seekBudget: TimeSpan.FromMilliseconds(100), speed: 1.0, Frequency)
            .ShouldBe(TimeSpan.FromMilliseconds(10_100));
    }

    [Fact]
    public void A_start_already_past_seeks_ahead_by_the_time_since_it_plus_the_seek_at_the_playback_speed()
    {
        MpvStartPlan.SeekTarget(TimeSpan.FromSeconds(10), start: 950, now: 1000, seekBudget: TimeSpan.FromMilliseconds(100), speed: 1.002, Frequency)
            .ShouldBe(TimeSpan.FromMilliseconds(10_150.3));
    }

    [Fact]
    public void The_seek_budget_is_the_measured_seek_time_with_headroom_within_bounds()
    {
        MpvStartPlan.SeekBudget(TimeSpan.FromMilliseconds(54)).ShouldBe(TimeSpan.FromMilliseconds(101));
        MpvStartPlan.SeekBudget(TimeSpan.Zero).ShouldBe(TimeSpan.FromMilliseconds(50));
        MpvStartPlan.SeekBudget(TimeSpan.FromSeconds(5)).ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_measured_seek_time_follows_new_measurements_smoothly()
    {
        MpvStartPlan.NextSeekTime(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(50))
            .ShouldBe(TimeSpan.FromMilliseconds(190));
    }
}

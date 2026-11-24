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
    public void The_seek_budget_is_the_seek_time_tail_with_headroom_within_bounds()
    {
        MpvStartPlan.SeekBudget(TimeSpan.FromMilliseconds(56)).ShouldBe(TimeSpan.FromMilliseconds(90));
        MpvStartPlan.SeekBudget(TimeSpan.Zero).ShouldBe(TimeSpan.FromMilliseconds(50));
        MpvStartPlan.SeekBudget(TimeSpan.FromSeconds(5)).ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_seek_time_tail_is_the_slowest_of_the_last_eight_seeks_not_their_mean()
    {
        // Target PC, 1080p HEVC: seek times swing 4..240 ms with the keyframe distance; a mean underestimates the slow ones.
        TimeSpan[] measured = [Ms(400), Ms(13), Ms(51), Ms(88), Ms(136), Ms(136), Ms(37), Ms(37), Ms(240)];

        MpvStartPlan.SeekTimeTail(measured).ShouldBe(Ms(240));
        MpvStartPlan.SeekTimeTail(measured[..8]).ShouldBe(Ms(400));
        MpvStartPlan.SeekTimeTail([.. measured[1..], Ms(20)]).ShouldBe(Ms(240));
        MpvStartPlan.SeekTimeTail([.. measured[2..], Ms(20), Ms(20)]).ShouldBe(Ms(240));
    }

    [Fact]
    public void Before_any_seek_was_measured_the_tail_covers_a_cold_hevc_seek()
    {
        // A cold first seek took ~525 ms on the target PC; the first budget must hold it.
        MpvStartPlan.SeekTimeTail([]).ShouldBe(MpvStartPlan.InitialSeekTime);
        MpvStartPlan.InitialSeekTime.ShouldBeGreaterThanOrEqualTo(Ms(600));
        MpvStartPlan.SeekBudget(MpvStartPlan.SeekTimeTail([])).ShouldBeGreaterThan(Ms(525));
        MpvStartPlan.SeekBudget(MpvStartPlan.SeekTimeTail([])).ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_restart_in_time_for_its_frame_unpauses()
    {
        MpvStartPlan.AtRestart(due: 1100, now: 1050, replans: 0, Frequency).ShouldBe(StartAction.Unpause);
        MpvStartPlan.AtRestart(due: 1100, now: 1100, replans: 0, Frequency).ShouldBe(StartAction.Unpause);
    }

    [Fact]
    public void A_restart_a_few_milliseconds_past_its_frame_still_unpauses_because_a_reseek_would_cost_more()
    {
        MpvStartPlan.AtRestart(due: 1100, now: 1105, replans: 0, Frequency).ShouldBe(StartAction.Unpause);
    }

    [Fact]
    public void A_seek_that_overran_its_budget_is_replanned_instead_of_unpausing_late()
    {
        // Target PC: a seek took 240 ms of a 127 ms budget, the unpause came 86 ms late.
        MpvStartPlan.AtRestart(due: 1127, now: 1240, replans: 0, Frequency).ShouldBe(StartAction.Replan);
        MpvStartPlan.AtRestart(due: 1100, now: 1106, replans: 2, Frequency).ShouldBe(StartAction.Replan);
    }

    [Fact]
    public void After_three_replans_a_late_start_unpauses_late_and_leaves_the_rest_to_the_picture_loop()
    {
        MpvStartPlan.MaxReplans.ShouldBe(3);
        MpvStartPlan.AtRestart(due: 1100, now: 1200, replans: 3, Frequency).ShouldBe(StartAction.UnpauseLate);
    }

    [Fact]
    public void A_replan_seeks_to_the_frame_due_once_a_fresh_budget_has_passed()
    {
        // Content 10 s was due at 1000; the late restart came at 1240. The new seek goes to where the start is 1240 +
        // budget later, so whenever that seek is done in budget the picture resumes on its due frame.
        var budget = MpvStartPlan.SeekBudget(MpvStartPlan.SeekTimeTail([Ms(240)]));

        MpvStartPlan.SeekTarget(TimeSpan.FromSeconds(10), start: 1000, now: 1240, budget, speed: 1.0, Frequency)
            .ShouldBe(TimeSpan.FromSeconds(10) + Ms(240) + budget);
        budget.ShouldBeGreaterThan(Ms(240));
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
}

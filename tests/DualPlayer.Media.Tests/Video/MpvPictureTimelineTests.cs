using DualPlayer.Core.Playback;
using DualPlayer.Media.Video;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// The mapping from mpv's reports to the picture on screen, on a 1 kHz timestamp base (1 tick = 1 ms). mpv reports a
/// frame (<c>time-pos</c>) when it hands the frame to its video output, which shows it later, at the frame's display
/// time on mpv's schedule.
/// </summary>
public sealed class MpvPictureTimelineTests
{
    private const long Frequency = 1000;

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static MpvPictureTimeline PlayingFrom(TimeSpan shown, long resumedAt)
    {
        var timeline = new MpvPictureTimeline(Frequency);
        timeline.Reset(paused: true);
        timeline.Restarted(shown, at: resumedAt - 500);
        timeline.Resumed(resumedAt).ShouldBe(new PresentedPicture(shown, resumedAt));
        return timeline;
    }

    [Fact]
    public void A_frame_handed_over_ahead_of_its_display_time_is_not_published_before_that_time()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);

        // Frame 1 (40 ms) is handed over at once on resume; it is due 40 ms after the resume.
        timeline.Queued(Ms(40), at: 1001).ShouldBeNull();
    }

    [Fact]
    public void A_frame_is_published_with_its_display_time_once_a_later_report_finds_it_on_screen()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.Queued(Ms(40), at: 1001);

        // Frame 2 (80 ms) is handed over 60 ms ahead of its display at 1080; frame 1 has been up since 1040.
        timeline.Queued(Ms(80), at: 1020).ShouldBeNull();
        timeline.Queued(Ms(120), at: 1060).ShouldBe(new PresentedPicture(Ms(40), 1040));
        timeline.Queued(Ms(160), at: 1100).ShouldBe(new PresentedPicture(Ms(80), 1080));
    }

    [Fact]
    public void A_frame_handed_over_after_its_display_time_is_shown_no_earlier_than_it_was_handed_over()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);

        // A stall: frame 1, due at 1040, reaches the video output only at 1070.
        timeline.Queued(Ms(40), at: 1070).ShouldBe(new PresentedPicture(Ms(40), 1070));
    }

    [Fact]
    public void The_schedule_runs_at_the_applied_speed_from_the_moment_mpv_applied_it()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);

        timeline.SpeedApplied(2.0, at: 1100);

        // At 1100 the schedule is at 100 ms; at double speed 300 ms is due 100 ms of real time later.
        timeline.Queued(Ms(300), at: 1150).ShouldBeNull();
        timeline.Queued(Ms(400), at: 1210).ShouldBe(new PresentedPicture(Ms(300), 1200));
    }

    [Fact]
    public void While_seeking_the_reported_target_stands_in_until_the_restart_shows_the_frame_mpv_reports_then()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.SeekStarted();

        timeline.Queued(Ms(10_000), at: 1100).ShouldBe(new PresentedPicture(Ms(10_000), 1100));
        timeline.Restarted(Ms(10_016), at: 1150).ShouldBe(new PresentedPicture(Ms(10_016), 1150));
        timeline.UnpauseDue.ShouldBeNull();

        // Playing on from the restart frame shown at 1150.
        timeline.Queued(Ms(10_056), at: 1160).ShouldBeNull();
        timeline.Queued(Ms(10_096), at: 1200).ShouldBe(new PresentedPicture(Ms(10_056), 1190));
    }

    [Fact]
    public void A_late_repeat_of_the_restart_frame_is_not_published_again_as_a_later_picture()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.SeekStarted();
        timeline.Restarted(Ms(10_016), at: 1150);

        // mpv's coalesced time-pos for the restart frame arrives after the restart: it is the frame already published.
        timeline.Queued(Ms(10_016), at: 1155).ShouldBeNull();
        timeline.Queued(Ms(10_056), at: 1160).ShouldBeNull();
        timeline.Queued(Ms(10_096), at: 1200).ShouldBe(new PresentedPicture(Ms(10_056), 1190));
    }

    [Fact]
    public void A_restart_whose_frame_could_not_be_read_falls_back_to_the_seek_target()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.SeekStarted();
        timeline.Queued(Ms(10_000), at: 1100);

        timeline.Restarted(null, at: 1150).ShouldBe(new PresentedPicture(Ms(10_000), 1150));
    }

    [Fact]
    public void A_restart_whose_frame_could_not_be_read_falls_back_to_the_last_reported_position()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.Queued(Ms(40), at: 1001);
        timeline.SeekStarted();

        timeline.Restarted(null, at: 1150).ShouldBe(new PresentedPicture(Ms(40), 1150));
    }

    [Fact]
    public void Presentation_delay_samples_are_counted_with_the_median_of_the_ones_used()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.PresentationDelays.ShouldBe(new PresentationDelayStats(0, 0, null));

        // The first three after the resume are skipped (stale), the rest are used.
        foreach (var delay in new[] { 90.0, 90.0, 90.0, 10.0, 30.0, 20.0 })
        {
            timeline.PresentationDelay(Ms(delay));
        }

        timeline.PresentationDelays.ShouldBe(new PresentationDelayStats(6, 3, 20.0));
        timeline.Reset(paused: true);
        timeline.PresentationDelays.ShouldBe(new PresentationDelayStats(0, 0, null));
    }

    [Fact]
    public void While_paused_a_reported_frame_is_the_one_on_screen()
    {
        var timeline = new MpvPictureTimeline(Frequency);
        timeline.Reset(paused: true);

        timeline.Queued(TimeSpan.Zero, at: 500).ShouldBe(new PresentedPicture(TimeSpan.Zero, 500));
    }

    [Fact]
    public void A_pause_publishes_the_last_frame_handed_over_at_its_display_time_and_a_resume_starts_from_it()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.Queued(Ms(40), at: 1001);
        timeline.Queued(Ms(80), at: 1020);

        timeline.Paused(at: 1030).ShouldBe(new PresentedPicture(Ms(80), 1080));
        timeline.Queued(Ms(80), at: 1500).ShouldBe(new PresentedPicture(Ms(80), 1500));

        timeline.Resumed(2000).ShouldBe(new PresentedPicture(Ms(80), 2000));
        timeline.Queued(Ms(120), at: 2001).ShouldBeNull();
        timeline.Queued(Ms(160), at: 2060).ShouldBe(new PresentedPicture(Ms(120), 2040));
    }

    [Fact]
    public void A_planned_start_holds_the_restart_frame_until_the_start_reaches_it()
    {
        var timeline = new MpvPictureTimeline(Frequency);
        timeline.Reset(paused: true);
        timeline.Restarted(TimeSpan.Zero, at: 500);

        // Start: content 10 s shown at 2000. The seek lands on the first frame at or after its target.
        timeline.PlanStart(Ms(10_000), start: 2000);
        timeline.SeekStarted();
        timeline.Queued(Ms(10_000), at: 1001).ShouldBe(new PresentedPicture(Ms(10_000), 2000));
        timeline.Restarted(Ms(10_016), at: 1060).ShouldBe(new PresentedPicture(Ms(10_016), 2016));

        timeline.UnpauseDue.ShouldBe(2016);

        // mpv reports the restart frame once more after the restart; it is still held for the start.
        timeline.Queued(Ms(10_016), at: 1061).ShouldBe(new PresentedPicture(Ms(10_016), 2016));
        timeline.Resumed(2016).ShouldBe(new PresentedPicture(Ms(10_016), 2016));
        timeline.UnpauseDue.ShouldBeNull();
    }

    [Fact]
    public void A_planned_start_the_restart_comes_too_late_for_is_due_at_once_and_shown_as_late()
    {
        var timeline = new MpvPictureTimeline(Frequency);
        timeline.Reset(paused: true);
        timeline.Restarted(TimeSpan.Zero, at: 500);
        timeline.PlanStart(Ms(10_000), start: 1000);
        timeline.SeekStarted();

        timeline.Restarted(Ms(10_016), at: 1100).ShouldBe(new PresentedPicture(Ms(10_016), 1100));

        timeline.UnpauseDue.ShouldBe(1016);
    }

    [Fact]
    public void A_restart_from_before_the_planned_starts_seek_does_not_start_it()
    {
        var timeline = new MpvPictureTimeline(Frequency);
        timeline.Reset(paused: true);
        timeline.PlanStart(Ms(10_000), start: 2000);

        // The load's own first frame comes up before the start's seek has begun.
        timeline.Restarted(TimeSpan.Zero, at: 600).ShouldBe(new PresentedPicture(TimeSpan.Zero, 600));

        timeline.UnpauseDue.ShouldBeNull();
    }

    [Fact]
    public void A_pause_drops_a_planned_start()
    {
        var timeline = new MpvPictureTimeline(Frequency);
        timeline.Reset(paused: true);
        timeline.Restarted(TimeSpan.Zero, at: 500);
        timeline.PlanStart(Ms(10_000), start: 2000);

        timeline.Paused(at: 600);
        timeline.SeekStarted();

        timeline.Restarted(Ms(10_016), at: 1060).ShouldBe(new PresentedPicture(Ms(10_016), 1060));
        timeline.UnpauseDue.ShouldBeNull();
    }

    [Fact]
    public void A_resume_before_the_seek_has_restarted_waits_for_the_restart_frame()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.Paused(at: 1100);
        timeline.SeekStarted();

        timeline.Resumed(1200).ShouldBeNull();
        timeline.Restarted(Ms(5000), at: 1300).ShouldBe(new PresentedPicture(Ms(5000), 1300));
        timeline.Queued(Ms(5040), at: 1301).ShouldBeNull();
        timeline.Queued(Ms(5080), at: 1350).ShouldBe(new PresentedPicture(Ms(5040), 1340));
    }

    [Fact]
    public void The_median_of_the_recent_presentation_delays_moves_the_display_time()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);

        // Presentation feedback (past the samples that may still be about flips before the resume): the display shows
        // frames 37 ms after their display time; one sample is an outlier.
        foreach (var delay in new[] { 0.0, 0.0, 0.0, 37.0, 37.0, -4.0, 37.0, 36.0 })
        {
            timeline.PresentationDelay(Ms(delay));
        }

        timeline.Queued(Ms(40), at: 1001).ShouldBeNull();
        timeline.Queued(Ms(80), at: 1078).ShouldBe(new PresentedPicture(Ms(40), 1077));
    }

    [Fact]
    public void Presentation_feedback_from_before_a_resume_or_restart_is_not_applied_to_the_frames_after_it()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.PresentationDelay(Ms(5));
        timeline.Paused(at: 1100);

        // Redraws while paused measure against the old schedule; feedback lags up to the swapchain depth plus one flips.
        timeline.PresentationDelay(Ms(400));
        timeline.Resumed(2000);
        foreach (var stale in new[] { 115.0, 95.0, 95.0 })
        {
            timeline.PresentationDelay(Ms(stale));
        }

        timeline.Queued(Ms(40), at: 2001).ShouldBeNull();
        timeline.Queued(Ms(80), at: 2041).ShouldBe(new PresentedPicture(Ms(40), 2040));
    }

    [Fact]
    public void Without_presentation_feedback_the_display_time_is_mpvs_schedule()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.PresentationDelay(Ms(30));

        timeline.PresentationDelay(null);

        timeline.Queued(Ms(40), at: 1001).ShouldBeNull();
        timeline.Queued(Ms(80), at: 1041).ShouldBe(new PresentedPicture(Ms(40), 1040));
    }

    [Fact]
    public void A_new_file_forgets_the_schedule_and_the_presentation_delays_but_keeps_the_speed()
    {
        var timeline = PlayingFrom(TimeSpan.Zero, resumedAt: 1000);
        timeline.SpeedApplied(2.0, at: 1000);
        timeline.PresentationDelay(Ms(30));

        timeline.Reset(paused: false);
        timeline.Restarted(TimeSpan.Zero, at: 3000).ShouldBe(new PresentedPicture(TimeSpan.Zero, 3000));

        timeline.Queued(Ms(80), at: 3001).ShouldBeNull();
        timeline.Queued(Ms(160), at: 3041).ShouldBe(new PresentedPicture(Ms(80), 3040));
    }
}

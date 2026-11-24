using DualPlayer.Media.Tests.Video;

namespace DualPlayer.Media.Tests.Video;

public sealed class SpikeMathTests
{
    [Fact]
    public void Percentile_uses_nearest_rank_on_unsorted_input()
    {
        double[] values = [50, 10, 30, 20, 40, 60, 70, 80, 90, 100];

        SpikeMath.Percentile(values, 50).ShouldBe(50);
        SpikeMath.Percentile(values, 95).ShouldBe(100);
        SpikeMath.Percentile(values, 10).ShouldBe(10);
    }

    [Fact]
    public void Percentile_of_nothing_is_zero()
    {
        SpikeMath.Percentile([], 99).ShouldBe(0);
    }

    [Fact]
    public void Summarize_works_on_absolute_errors_and_keeps_the_maximum()
    {
        var summary = SpikeMath.Summarize([-5, 1, 2, 3, 12]);

        summary.Count.ShouldBe(5);
        summary.P50.ShouldBe(3);
        summary.Max.ShouldBe(12);
        SpikeMath.Summarize([-30, 1]).Max.ShouldBe(30);
    }

    [Fact]
    public void RecoveryTime_is_the_start_of_the_final_stretch_within_tolerance()
    {
        (double AtMs, double ErrorMs)[] samples =
        [
            (0, 80), (100, 40), (200, 25), (300, 10), (400, -5), (500, 18), (600, 2),
        ];

        SpikeMath.RecoveryTimeMs(samples, 20).ShouldBe(300);
    }

    [Fact]
    public void RecoveryTime_restarts_when_the_error_leaves_tolerance_again()
    {
        (double AtMs, double ErrorMs)[] samples = [(0, 5), (100, 50), (200, 5), (300, 5)];

        SpikeMath.RecoveryTimeMs(samples, 20).ShouldBe(200);
    }

    [Fact]
    public void RecoveryTime_is_null_when_the_last_sample_is_still_out_or_there_are_none()
    {
        SpikeMath.RecoveryTimeMs([(0, 5), (100, 50)], 20).ShouldBeNull();
        SpikeMath.RecoveryTimeMs([], 20).ShouldBeNull();
    }

    [Fact]
    public void RecoveryTime_does_not_count_samples_taken_while_the_picture_was_held_for_a_start()
    {
        // The held frame is stamped with the planned start, so its error is ~0 by construction while the screen still
        // shows the paused picture: recovery starts at the first sample after the actual resume.
        (double AtMs, double ErrorMs, bool Held)[] samples =
        [
            (0, 0.1, true), (100, 0.0, true), (230, 0.2, true), (240, 3, false), (300, 25, false), (400, 4, false), (500, 2, false),
        ];

        SpikeMath.HeldAwareRecoveryTimeMs(samples, 20).ShouldBe(400);
    }

    [Fact]
    public void RecoveryTime_is_null_when_the_picture_was_still_held_at_the_end()
    {
        SpikeMath.HeldAwareRecoveryTimeMs([(0, 0.0, true), (100, 0.0, true)], 20).ShouldBeNull();
    }

    [Fact]
    public void The_presentation_delay_line_shows_the_sample_count_and_median_and_makes_no_feedback_visible()
    {
        SpikeMath.PresentationDelayText(1200, 1100, 12.5).ShouldBe("presentation-delay samples: 1200 (used 1100), median 12.5 ms");
        SpikeMath.PresentationDelayText(0, 0, null).ShouldBe(
            "presentation-delay samples: 0 (used 0), median n/a - mpv gave no presentation feedback (property missing or not provided by the output); the display schedule ran uncorrected");
    }

    [Theory]
    [InlineData(20.0, true)]
    [InlineData(20.1, false)]
    public void Steady_play_passes_when_no_picture_was_more_than_20_ms_from_due(double maxMs, bool pass)
    {
        SpikeMath.SteadyVerdict(maxMs).ShouldBe(pass ? SpikeVerdict.Pass : SpikeVerdict.Fail);
    }

    [Fact]
    public void Recovery_passes_within_one_second_and_fails_when_later_or_never()
    {
        SpikeMath.RecoveryVerdict([100, 1000]).ShouldBe(SpikeVerdict.Pass);
        SpikeMath.RecoveryVerdict([100, 1001]).ShouldBe(SpikeVerdict.Fail);
        SpikeMath.RecoveryVerdict([100, null]).ShouldBe(SpikeVerdict.Fail);
        SpikeMath.RecoveryVerdict([]).ShouldBe(SpikeVerdict.NotMeasured);
    }

    [Fact]
    public void Seek_overruns_of_a_start_are_listed_with_budget_and_time_taken_and_nothing_when_none()
    {
        SpikeMath.OverrunsText([]).ShouldBe(string.Empty);
        SpikeMath.OverrunsText([(127, 240.4), (320, 331)]).ShouldBe(" (overran: 240 ms of 127 ms budget; 331 ms of 320 ms budget)");
    }

    [Fact]
    public void Late_or_dropped_passes_at_one_frame_per_minute_and_fails_above()
    {
        SpikeMath.PerMinute(10, TimeSpan.FromMinutes(10)).ShouldBe(1.0);
        SpikeMath.LateDroppedVerdict(10, TimeSpan.FromMinutes(10)).ShouldBe(SpikeVerdict.Pass);
        SpikeMath.LateDroppedVerdict(11, TimeSpan.FromMinutes(10)).ShouldBe(SpikeVerdict.Fail);
        SpikeMath.LateDroppedVerdict(0, TimeSpan.Zero).ShouldBe(SpikeVerdict.NotMeasured);
    }
}

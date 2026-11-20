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
    public void Late_or_dropped_passes_at_one_frame_per_minute_and_fails_above()
    {
        SpikeMath.PerMinute(10, TimeSpan.FromMinutes(10)).ShouldBe(1.0);
        SpikeMath.LateDroppedVerdict(10, TimeSpan.FromMinutes(10)).ShouldBe(SpikeVerdict.Pass);
        SpikeMath.LateDroppedVerdict(11, TimeSpan.FromMinutes(10)).ShouldBe(SpikeVerdict.Fail);
        SpikeMath.LateDroppedVerdict(0, TimeSpan.Zero).ShouldBe(SpikeVerdict.NotMeasured);
    }
}

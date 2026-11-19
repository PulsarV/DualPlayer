using DualPlayer.Core.Sync;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

/// <summary>The one-step re-alignment decisions of an output: the step path after consecutive readings and the once-per-second fallback.</summary>
public sealed class OutputSyncTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Sample = TimeSpan.FromMilliseconds(100);

    private readonly FakeTimeProvider _time = new();
    private readonly MasterClock _clock;
    private readonly OutputSync _sync;

    public OutputSyncTests()
    {
        _clock = new MasterClock(_time);
        _clock.Start();
        _sync = new OutputSync(_clock, Tolerance);
    }

    /// <summary>One 100 ms sample whose content is <paramref name="driftMs"/> away from the master clock.</summary>
    private TimeSpan Read(double driftMs, bool correct = true)
    {
        _time.Advance(Sample);
        var now = _time.GetTimestamp();
        return _sync.AddReading(_clock.PositionAt(now) + TimeSpan.FromMilliseconds(driftMs), now, Sample, correct);
    }

    [Fact]
    public void Two_readings_beyond_the_threshold_do_not_re_align_and_the_third_removes_the_latest_drift()
    {
        Read(-30);
        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
        Read(-30);
        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
        Read(-29);

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.FromMilliseconds(-29));
    }

    [Fact]
    public void A_reading_back_within_the_threshold_starts_the_count_again()
    {
        Read(-30);
        Read(-30);
        Read(-2);
        Read(-30);
        Read(-30);

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Readings_beyond_the_threshold_on_alternating_sides_never_count_as_a_step()
    {
        for (var i = 0; i < 6; i++)
        {
            Read(i % 2 == 0 ? -30 : 30);
            _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
        }
    }

    [Fact]
    public void A_reading_between_the_tolerance_and_the_re_alignment_threshold_is_left_to_the_rate_correction()
    {
        _sync.RealignThreshold.ShouldBe(TimeSpan.FromMilliseconds(10.5));
        for (var i = 0; i < 5; i++)
        {
            Read(10.4);
        }

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_step_just_over_the_threshold_is_still_re_aligned_when_the_rate_correction_shrinks_it_under_the_threshold()
    {
        Read(10.55);
        Read(10.53);
        Read(10.50); // no longer beyond the threshold, still beyond the tolerance

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.FromMilliseconds(10.50));
    }

    [Fact]
    public void A_blocked_step_is_kept_and_removed_as_soon_as_re_aligning_is_allowed()
    {
        Read(40);
        Read(40);
        Read(40);
        _sync.TakeStepRealignment(mayRealign: false).ShouldBe(TimeSpan.Zero); // a recovery window or an earlier re-alignment still on its way

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public void A_re_alignment_clears_the_count_so_the_same_error_is_not_removed_twice()
    {
        Read(-30);
        Read(-30);
        Read(-30);
        _sync.TakeStepRealignment(mayRealign: true).ShouldNotBe(TimeSpan.Zero);

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
        _sync.CloseInterval(mayRealign: true).Realignment.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_reset_clears_the_count()
    {
        Read(-30);
        Read(-30);
        _sync.Reset();
        Read(-30);

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Readings_taken_while_the_output_is_being_aligned_do_not_count_towards_a_step()
    {
        Read(-30, correct: false);
        Read(-30, correct: false);
        Read(-30, correct: false);

        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void The_interval_close_still_re_aligns_a_step_seen_by_fewer_readings_than_the_step_path_needs()
    {
        for (var i = 0; i < 8; i++)
        {
            Read(0);
        }

        Read(60);
        Read(60); // two readings: the step path has not fired, the interval's average is 12 ms
        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);

        var interval = _sync.CloseInterval(mayRealign: true);

        interval.AverageDrift.ShouldBe(TimeSpan.FromMilliseconds(12));
        interval.Realignment.ShouldBe(TimeSpan.FromMilliseconds(60));
        Read(60); // the device has not applied it yet
        _sync.TakeStepRealignment(mayRealign: true).ShouldBe(TimeSpan.Zero);
    }
}

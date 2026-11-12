using DualPlayer.Core.Diagnostics;
using Shouldly;

namespace DualPlayer.Core.Tests.Diagnostics;

public class ExcursionTrackerTests
{
    private static TimeSpan At(int s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Drift_within_tolerance_emits_no_excursion()
    {
        var tracker = new ExcursionTracker("dev-A");
        for (var s = 1; s <= 5; s++)
        {
            tracker.Observe(At(s), 9.9, inRecoveryWindow: false).ShouldBeNull();
        }

        tracker.Finish(At(6)).ShouldBeNull();
    }

    [Fact]
    public void Stretch_beyond_tolerance_emits_one_excursion_with_device_peak_duration_and_start()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(1), 2, false).ShouldBeNull();
        tracker.Observe(At(2), 11, false).ShouldBeNull();
        tracker.Observe(At(3), -14.5, false).ShouldBeNull();
        tracker.Observe(At(4), 12, false).ShouldBeNull();

        var excursion = tracker.Observe(At(5), 3, false);

        excursion.ShouldNotBeNull();
        excursion.DeviceId.ShouldBe("dev-A");
        excursion.Start.ShouldBe(At(2));
        excursion.Duration.ShouldBe(At(3));
        excursion.PeakMs.ShouldBe(-14.5, 0.001); // signed drift of largest magnitude: + = ahead, - = behind
        tracker.Observe(At(6), 3, false).ShouldBeNull();
        tracker.Finish(At(7)).ShouldBeNull();
    }

    [Fact]
    public void Two_separate_stretches_emit_two_excursions()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(1), 20, false);
        var first = tracker.Observe(At(2), 0, false);
        tracker.Observe(At(10), 30, false);
        var second = tracker.Observe(At(12), 0, false);

        first.ShouldNotBeNull();
        first.Start.ShouldBe(At(1));
        second.ShouldNotBeNull();
        second.Start.ShouldBe(At(10));
        second.PeakMs.ShouldBe(30, 0.001);
    }

    [Fact]
    public void Drift_beyond_tolerance_inside_a_recovery_window_opens_no_excursion()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(1), 80, inRecoveryWindow: true).ShouldBeNull();
        tracker.Observe(At(2), 40, inRecoveryWindow: true).ShouldBeNull();

        tracker.Finish(At(3)).ShouldBeNull();
    }

    [Fact]
    public void Excursion_still_open_when_the_run_ends_is_emitted_once_up_to_the_end()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(100), 15, false).ShouldBeNull();
        tracker.Observe(At(101), 18, false).ShouldBeNull();

        var excursion = tracker.Finish(At(105));

        excursion.ShouldNotBeNull();
        excursion.Start.ShouldBe(At(100));
        excursion.Duration.ShouldBe(At(5));
        excursion.PeakMs.ShouldBe(18, 0.001);
        tracker.Finish(At(106)).ShouldBeNull();
    }

    [Fact]
    public void An_output_behind_beyond_tolerance_has_a_negative_peak()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(1), -12, false).ShouldBeNull();
        tracker.Observe(At(2), -14.2, false).ShouldBeNull();
        tracker.Observe(At(3), -11, false).ShouldBeNull();

        var excursion = tracker.Observe(At(4), 0, false);

        excursion.ShouldNotBeNull();
        excursion.PeakMs.ShouldBe(-14.2, 0.001);
    }

    [Fact]
    public void An_output_ahead_beyond_tolerance_keeps_a_positive_peak()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(1), 12, false);
        tracker.Observe(At(2), 14.2, false);

        var excursion = tracker.Observe(At(3), 0, false);

        excursion.ShouldNotBeNull();
        excursion.PeakMs.ShouldBe(14.2, 0.001);
    }

    [Fact]
    public void An_excursion_still_open_at_the_end_reports_a_negative_peak_when_the_output_was_behind()
    {
        var tracker = new ExcursionTracker("dev-A");
        tracker.Observe(At(1), -25, false);

        tracker.Finish(At(2))!.PeakMs.ShouldBe(-25, 0.001);
    }
}

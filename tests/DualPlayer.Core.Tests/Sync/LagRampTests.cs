using DualPlayer.Core.Sync;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

public sealed class LagRampTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Fact]
    public void A_ten_millisecond_change_takes_five_seconds_at_two_thousand_ppm()
    {
        var ramp = new LagRamp();
        ramp.SetAtOnce(TimeSpan.Zero);
        ramp.Retarget(TimeSpan.FromMilliseconds(10));

        ramp.IsMoving.ShouldBeTrue();
        ramp.RatePpm.ShouldBe(2000, 1e-6);
        for (var i = 0; i < 4; i++)
        {
            ramp.Advance(Second);
        }

        ramp.Current.TotalMilliseconds.ShouldBe(8, 1e-6);
        ramp.IsMoving.ShouldBeTrue();
        ramp.Advance(Second);
        ramp.Current.ShouldBe(TimeSpan.FromMilliseconds(10));
        ramp.IsMoving.ShouldBeFalse();
        ramp.RatePpm.ShouldBe(0);
    }

    [Fact]
    public void The_ramp_never_overshoots_the_target_within_one_step()
    {
        var ramp = new LagRamp();
        ramp.Retarget(TimeSpan.FromMilliseconds(1));
        ramp.Advance(TimeSpan.FromSeconds(10));

        ramp.Current.ShouldBe(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void A_decreasing_lag_ramps_down_at_a_negative_rate()
    {
        var ramp = new LagRamp();
        ramp.SetAtOnce(TimeSpan.FromMilliseconds(100));
        ramp.Retarget(TimeSpan.FromMilliseconds(90));

        ramp.RatePpm.ShouldBe(-2000, 1e-6);
        ramp.Advance(Second);
        ramp.Current.TotalMilliseconds.ShouldBe(98, 1e-6);
    }

    [Fact]
    public void Retargeting_during_a_ramp_continues_from_the_current_lag()
    {
        var ramp = new LagRamp();
        ramp.Retarget(TimeSpan.FromMilliseconds(10));
        ramp.Advance(Second); // 2 ms
        ramp.Retarget(TimeSpan.FromMilliseconds(-4));

        ramp.Current.TotalMilliseconds.ShouldBe(2, 1e-6);
        ramp.RatePpm.ShouldBe(-2000, 1e-6);
        ramp.Advance(Second);
        ramp.Current.TotalMilliseconds.ShouldBe(0, 1e-6);
    }

    [Fact]
    public void Setting_at_once_jumps_current_and_target_and_stops_the_ramp()
    {
        var ramp = new LagRamp();
        ramp.Retarget(TimeSpan.FromMilliseconds(10));
        ramp.Advance(Second);
        ramp.SetAtOnce(TimeSpan.FromMilliseconds(50));

        ramp.Current.ShouldBe(TimeSpan.FromMilliseconds(50));
        ramp.Target.ShouldBe(TimeSpan.FromMilliseconds(50));
        ramp.IsMoving.ShouldBeFalse();
    }

    [Fact]
    public void A_ramp_given_a_longer_move_spreads_its_change_over_it_and_arrives_at_its_end_AC_14()
    {
        var ramp = new LagRamp();
        ramp.SetAtOnce(TimeSpan.Zero);
        ramp.Retarget(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(10));

        ramp.RatePpm.ShouldBe(1000, 1e-6);
        for (var i = 0; i < 9; i++)
        {
            ramp.Advance(Second);
        }

        ramp.Current.TotalMilliseconds.ShouldBe(9, 1e-6);
        ramp.Advance(Second);
        ramp.Current.ShouldBe(TimeSpan.FromMilliseconds(10));
        ramp.IsMoving.ShouldBeFalse();
    }

    [Fact]
    public void A_ramp_given_too_short_a_move_still_never_exceeds_two_thousand_ppm_AC_14()
    {
        var ramp = new LagRamp();
        ramp.SetAtOnce(TimeSpan.Zero);
        ramp.Retarget(TimeSpan.FromMilliseconds(-10), TimeSpan.FromSeconds(1));

        ramp.RatePpm.ShouldBe(-2000, 1e-6);
    }
}

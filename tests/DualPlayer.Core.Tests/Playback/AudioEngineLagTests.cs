using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>Per-output lags join the pre-roll and later lag targets ramp or jump on the engine control loop (AC-01, AC-14; ADR-0005).</summary>
public sealed class AudioEngineLagTests
{
    private static readonly TimeSpan Lag = TimeSpan.FromMilliseconds(200);

    private static EngineRig Rig() => new([new RigDevice(48_000), new RigDevice(48_000)]);

    /// <summary>The film position the device is playing right now, from the frame values the fake decoder wrote.</summary>
    private static double HeardSeconds(EngineRig rig, int device)
    {
        var d = rig.Devices[device];
        var frame = (int)d.PlayedFramesNow;
        return Math.Round(d.Rendered[frame] * 1000.0) / 48_000;
    }

    [Fact]
    public void An_output_with_a_start_lag_hears_the_film_that_much_behind_the_other_output()
    {
        using var rig = Rig();
        rig.Start(new EngineOptions { InitialLags = new Dictionary<string, TimeSpan> { ["dev-2"] = Lag } });
        rig.Render(100);

        var gapMs = (HeardSeconds(rig, 0) - HeardSeconds(rig, 1)) * 1000;

        gapMs.ShouldBe(Lag.TotalMilliseconds, 10, "dev-2 must trail dev-1 by its start lag");
    }

    [Fact]
    public void Without_lags_both_outputs_hear_the_same_film_position()
    {
        using var rig = Rig();
        rig.Start();
        rig.Render(100);

        ((HeardSeconds(rig, 0) - HeardSeconds(rig, 1)) * 1000).ShouldBe(0, 10);
    }

    [Fact]
    public void A_ramped_lag_target_moves_the_lag_at_up_to_2000_ppm()
    {
        using var rig = Rig();
        rig.Start();
        rig.Render(50);
        var control = (IOutputLagControl)rig.Engine;

        control.SetLagTargets(new Dictionary<string, TimeSpan> { ["dev-2"] = TimeSpan.FromMilliseconds(20) }, LagChangeMode.Ramp).IsSuccess.ShouldBeTrue();
        rig.Render(300); // 3 s -> at most 6 ms

        control.IsRamping.ShouldBeTrue();
        control.CurrentLag("dev-2")!.Value.TotalMilliseconds.ShouldBeInRange(4, 6.5);
        control.CurrentLag("dev-1")!.Value.ShouldBe(TimeSpan.Zero);

        rig.Render(1000);

        control.IsRamping.ShouldBeFalse();
        control.CurrentLag("dev-2")!.Value.ShouldBe(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void Applied_speeds_are_each_output_ramp_and_drift_correction_as_its_controller_runs_them_AC_14_AC_17()
    {
        using var rig = Rig();
        rig.Start();
        rig.Render(50);
        var control = (IOutputLagControl)rig.Engine;

        control.SetLagTargets(new Dictionary<string, TimeSpan> { ["dev-2"] = TimeSpan.FromMilliseconds(20) }, LagChangeMode.Ramp).IsSuccess.ShouldBeTrue();

        var speeds = control.AppliedSpeeds();
        speeds.Keys.ShouldBe(["dev-1", "dev-2"], ignoreOrder: true);
        speeds["dev-2"].RampPpm.ShouldBe(2000, 1e-9);
        speeds["dev-2"].TotalPpm.ShouldBe(speeds["dev-2"].RampPpm + speeds["dev-2"].CorrectionPpm, 1e-9);
        speeds["dev-1"].RampPpm.ShouldBe(0);

        rig.Render(1500);

        control.AppliedSpeeds()["dev-2"].RampPpm.ShouldBe(0, "the ramp ended at its target");
    }

    [Fact]
    public void An_at_once_lag_target_is_reached_immediately_without_a_ramp()
    {
        using var rig = Rig();
        rig.Start();
        rig.Render(50);
        var control = (IOutputLagControl)rig.Engine;

        control.SetLagTargets(new Dictionary<string, TimeSpan> { ["dev-2"] = Lag }, LagChangeMode.AtOnce).IsSuccess.ShouldBeTrue();
        rig.Render(1);

        control.CurrentLag("dev-2").ShouldBe(Lag);
        control.IsRamping.ShouldBeFalse();
    }

    [Fact]
    public void A_lag_target_for_an_output_not_in_the_run_is_ignored()
    {
        using var rig = Rig();
        rig.Start();
        rig.Render(50);
        var control = (IOutputLagControl)rig.Engine;

        control.SetLagTargets(new Dictionary<string, TimeSpan> { ["dev-9"] = Lag }, LagChangeMode.AtOnce).IsSuccess.ShouldBeTrue();
        rig.Render(1);

        control.CurrentLag("dev-9").ShouldBeNull();
        control.CurrentLag("dev-1").ShouldBe(TimeSpan.Zero);
    }

    private static readonly TimeSpan RampTarget = TimeSpan.FromMilliseconds(200);

    private static void StartRampOnSecondOutput(EngineRig rig, IOutputLagControl control)
    {
        rig.Start();
        rig.Render(50);
        control.SetLagTargets(new Dictionary<string, TimeSpan> { ["dev-2"] = RampTarget }, LagChangeMode.Ramp).IsSuccess.ShouldBeTrue();
        rig.Render(100); // 1 s -> about 2 ms of 200 ms
        control.IsRamping.ShouldBeTrue();
    }

    [Fact]
    public void A_seek_during_a_ramp_sets_the_lag_target_at_once()
    {
        using var rig = Rig();
        var control = (IOutputLagControl)rig.Engine;
        StartRampOnSecondOutput(rig, control);

        rig.Seek(TimeSpan.FromSeconds(5));
        rig.Render(300);

        control.IsRamping.ShouldBeFalse();
        control.CurrentLag("dev-2").ShouldBe(RampTarget);
        ((HeardSeconds(rig, 0) - HeardSeconds(rig, 1)) * 1000).ShouldBe(RampTarget.TotalMilliseconds, 10, "the seek re-aligns to the target lag");
    }

    [Fact]
    public void A_resume_during_a_ramp_sets_the_lag_target_at_once()
    {
        using var rig = Rig();
        var control = (IOutputLagControl)rig.Engine;
        StartRampOnSecondOutput(rig, control);

        rig.Pause();
        rig.Render(20);
        rig.Resume();
        rig.Render(300);

        control.IsRamping.ShouldBeFalse();
        control.CurrentLag("dev-2").ShouldBe(RampTarget);
        ((HeardSeconds(rig, 0) - HeardSeconds(rig, 1)) * 1000).ShouldBe(RampTarget.TotalMilliseconds, 10, "the resume re-aligns to the target lag");
    }

    [Fact]
    public void The_frame_zero_instant_is_the_timestamp_at_which_the_slowest_output_hears_the_first_frame()
    {
        using var rig = Rig();
        var control = (IOutputLagControl)rig.Engine;
        control.FrameZeroTimestamp.ShouldBeNull();
        var before = rig.Time.GetTimestamp();
        var commandAt = rig.Elapsed;

        rig.Start(new EngineOptions { InitialLags = new Dictionary<string, TimeSpan> { ["dev-2"] = Lag } });

        var frameZero = control.FrameZeroTimestamp;
        frameZero.ShouldNotBeNull();
        var sinceCommand = rig.Time.GetElapsedTime(before, frameZero.Value);
        (commandAt + sinceCommand).TotalMilliseconds.ShouldBe(rig.ClockStart.TotalMilliseconds, EngineRig.Step.TotalMilliseconds);
    }
}

using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T74: a picture-delay move logs the speeds really applied, signed as cli.md says, and starts from the in-flight delay (AC-14, AC-17).</summary>
public sealed class PlaybackSessionPictureDelayMoveSpeedsTests
{
    private static SessionRig PlayingRig(Func<IOutputLagControl, IOutputLagControl>? wrapLags = null)
    {
        // dev-1: Bluetooth, 240 ms assumed, lag 0; dev-2: wired, lag 240. PD = 240.
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, wrapLags: wrapLags);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_move_to_a_smaller_picture_delay_logs_a_positive_picture_speed_equal_to_the_renderer_speed_AC_14_AC_17()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        // Smaller delay: the picture runs faster than real time; cli.md: positive = faster than real time.
        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        rig.Renderer.Speed.ShouldBeGreaterThan(1.0);
        move.PicturePpm.ShouldBe((rig.Renderer.Speed - 1.0) * 1e6, 1);
        move.PicturePpm.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_move_to_a_larger_picture_delay_logs_a_negative_picture_speed_equal_to_the_renderer_speed_AC_14_AC_17()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, -10)).IsSuccess.ShouldBeTrue();

        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        rig.Renderer.Speed.ShouldBeLessThan(1.0);
        move.PicturePpm.ShouldBe((rig.Renderer.Speed - 1.0) * 1e6, 1);
        move.PicturePpm.ShouldBeLessThan(0);
    }

    [Fact]
    public void A_press_that_retargets_a_running_move_logs_the_in_flight_delay_as_the_start_AC_14_AC_17()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100); // 1 s into a 5 s move 240 -> 230 ms: about 238 ms in flight.

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        var second = rig.Events.OfType<PictureDelayMoved>().Last();
        second.ToMs.ShouldBe(220);
        ((double)second.FromMs).ShouldBe(238.0, 1.0);
        // From, To and Duration agree with the speed: the change covered over the duration is the speed logged.
        var covered = (second.ToMs - (double)second.FromMs) / second.Duration.TotalMilliseconds * 1e6;
        second.PicturePpm.ShouldBe(-covered, 1000);
    }

    [Fact]
    public void A_move_logs_each_output_ramp_and_total_speed_as_the_engine_applies_them_even_beyond_2000_ppm_AC_14_AC_17()
    {
        var applied = new Dictionary<string, OutputSpeed>(StringComparer.Ordinal)
        {
            ["dev-1"] = new(RampPpm: 0, CorrectionPpm: -30),
            ["dev-2"] = new(RampPpm: -2500, CorrectionPpm: 120),
        };
        using var rig = PlayingRig(inner => new AppliedSpeedLags(inner, applied));

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        // Not recomputed from the move and not clamped: what the engine's drift controllers run at.
        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        move.Ramps.Select(r => r.DeviceId).ShouldBe(["dev-1", "dev-2"]);
        var dev2 = move.Ramps.Single(r => r.DeviceId == "dev-2");
        dev2.RampPpm.ShouldBe(-2500);
        dev2.CorrectionPpm.ShouldBe(120);
        dev2.TotalPpm.ShouldBe(-2380);
        var dev1 = move.Ramps.Single(r => r.DeviceId == "dev-1");
        dev1.RampPpm.ShouldBe(0);
        dev1.TotalPpm.ShouldBe(-30);
    }

    [Fact]
    public void A_move_logs_the_speeds_the_real_engine_applies_its_ramp_plus_its_drift_correction_AC_14_AC_17()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        // dev-2's lag 240 -> 230 ms over the 5 s move: the engine ramps it at 2000 ppm, shrinking.
        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        var dev2 = move.Ramps.Single(r => r.DeviceId == "dev-2");
        dev2.RampPpm.ShouldBe(-2000, 1);
        dev2.TotalPpm.ShouldBe(dev2.RampPpm + dev2.CorrectionPpm, 1e-9);
        Math.Abs(dev2.CorrectionPpm).ShouldBeLessThanOrEqualTo(DriftController.MaxCorrectionPpm);
    }

    [Fact]
    public void A_move_logs_the_picture_speed_the_renderer_really_applied_not_the_one_it_was_asked_for_AC_14_AC_17()
    {
        using var rig = PlayingRig();
        rig.Renderer.AppliesSpeed = _ => 1.0025; // the player runs at +2500 ppm whatever it is asked

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        move.PicturePpm.ShouldBe(2500, 1e-6);
    }

    /// <summary>The engine's lag control, reporting scripted applied speeds (a drift controller that ran beyond 2000 ppm).</summary>
    private sealed class AppliedSpeedLags(IOutputLagControl inner, IReadOnlyDictionary<string, OutputSpeed> applied) : IOutputLagControl
    {
        public long? FrameZeroTimestamp => inner.FrameZeroTimestamp;

        public long? ClockStartTimestamp => inner.ClockStartTimestamp;

        public bool IsRamping => inner.IsRamping;

        public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default) => inner.SetLagTargets(targets, mode, moveDuration);

        public TimeSpan? CurrentLag(string deviceId) => inner.CurrentLag(deviceId);

        public long? FirstAudioTimestamp(string deviceId) => inner.FirstAudioTimestamp(deviceId);

        public IReadOnlyDictionary<string, OutputPositionError> PositionErrors() => inner.PositionErrors();

        public IReadOnlyDictionary<string, OutputSpeed> AppliedSpeeds() => applied;
    }
}

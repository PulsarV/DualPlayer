using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T25: the shipped session keeps the picture on clock − picture delay, measured at each snapshot's own time.</summary>
public sealed class PlaybackSessionPictureSteeringTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    // dev-1: Bluetooth headphones, 40 + 200 = 240 ms assumed; dev-2: wired, 0 ms. Picture delay 240 ms.
    private static SessionRig PlayingRig(Action<FakeVideoRenderer>? arrange = null)
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        arrange?.Invoke(rig.Renderer);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    // What a camera would measure: the frame on screen now against the master clock minus the picture delay. The slowest
    // output hears frame 0 at the engine's frame-zero instant, so that is when the picture is due at position 0.
    private static double TrueErrorMs(SessionRig rig)
    {
        var frameZero = rig.Engine.Engine.FrameZeroTimestamp ?? throw new InvalidOperationException("no run");
        var due = rig.Engine.Time.GetElapsedTime(frameZero);
        return (rig.Renderer.ShownNow - due).TotalMilliseconds;
    }

    [Fact]
    public void A_picture_whose_clock_drifts_from_the_sound_is_steered_and_stays_within_20_ms_AC_01_AC_17()
    {
        // 0.15 % drift: 30 ms off after 20 s when nobody steers it; the loop may steer by up to 0.2 %.
        using var rig = PlayingRig(r => r.RateError = -0.0015);

        var worst = 0.0;
        for (var i = 0; i < 20; i++)
        {
            rig.Engine.Render(100);
            worst = Math.Max(worst, Math.Abs(TrueErrorMs(rig)));
        }

        worst.ShouldBeLessThanOrEqualTo(20, "the session never steered the drifting picture");
        Math.Abs(rig.Renderer.Speed - 1.0).ShouldBeGreaterThan(0, "the picture speed was never changed");
    }

    [Fact]
    public void A_picture_that_falls_150_ms_behind_is_back_within_20_ms_in_under_a_second_AC_14_AC_17()
    {
        using var rig = PlayingRig();
        Math.Abs(TrueErrorMs(rig)).ShouldBeLessThanOrEqualTo(20);

        rig.Renderer.Jump(TimeSpan.FromMilliseconds(-150));
        rig.Engine.Render(100);

        Math.Abs(TrueErrorMs(rig)).ShouldBeLessThanOrEqualTo(20, "the late picture was not sought back");
    }

    [Fact]
    public void Logged_picture_timing_compares_each_snapshot_with_the_due_position_at_the_snapshot_time_AC_17()
    {
        // Every snapshot is 40 ms old, as mpv's are between frames; the picture itself is exactly on time.
        using var rig = PlayingRig(r => r.SnapshotAge = TimeSpan.FromMilliseconds(40));
        rig.Engine.Render(300);

        Math.Abs(TrueErrorMs(rig)).ShouldBeLessThanOrEqualTo(2);
        var reported = rig.Events.OfType<PictureTimingReported>().TakeLast(3).ToList();
        reported.ShouldNotBeEmpty();
        reported.ShouldAllBe(t => Math.Abs(t.ErrorMs) <= 2, "the logged error is skewed by the snapshot's age");
    }

    [Fact]
    public void Pause_with_an_old_snapshot_freezes_at_the_freeze_point_without_running_past_it_AC_04()
    {
        using var rig = PlayingRig(r => r.SnapshotAge = TimeSpan.FromMilliseconds(40));

        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        var freeze = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Pause).Clock;

        var furthest = TimeSpan.Zero;
        for (var i = 0; i < 40; i++)
        {
            rig.Engine.Render(1);
            furthest = TimeSpan.FromTicks(Math.Max(furthest.Ticks, rig.Renderer.ShownNow.Ticks));
        }

        rig.Renderer.IsPlaying.ShouldBeFalse();
        rig.Renderer.ShownNow.ShouldBe(freeze);
        (furthest - freeze).TotalMilliseconds.ShouldBeLessThanOrEqualTo(10, "the picture ran past the freeze point and jumped back");
    }
}

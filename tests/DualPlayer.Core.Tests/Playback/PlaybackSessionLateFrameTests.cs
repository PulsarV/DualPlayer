using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T30: a frame is late when it is presented more than 20 ms after its due time (spec §6).</summary>
public sealed class PlaybackSessionLateFrameTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    // 25 frames per second; dev-1 Bluetooth headphones (240 ms assumed), dev-2 wired. Picture delay 240 ms.
    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Renderer.FrameDuration = TimeSpan.FromMilliseconds(40);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_picture_on_time_counts_no_late_frames_AC_17()
    {
        using var rig = PlayingRig();

        rig.Engine.Render(200);

        rig.Session.Counters.Late.ShouldBe(0);
    }

    [Fact]
    public void Frames_presented_30_ms_after_due_are_each_counted_late_once_AC_17()
    {
        using var rig = PlayingRig();
        rig.Session.Counters.Late.ShouldBe(0);

        // 30 ms behind: steered back at 0.2 %, so it stays more than 20 ms late for about 5 s; one second is 25 frames.
        rig.Renderer.Jump(TimeSpan.FromMilliseconds(-30));
        rig.Engine.Render(100);

        rig.Session.Counters.Late.ShouldBeInRange(22, 27);
    }

    [Fact]
    public void Frames_15_ms_after_due_are_not_late_AC_17()
    {
        using var rig = PlayingRig();

        rig.Renderer.Jump(TimeSpan.FromMilliseconds(-15));
        rig.Engine.Render(100);

        rig.Session.Counters.Late.ShouldBe(0);
    }

    [Fact]
    public void A_deliberate_picture_delay_move_is_not_lateness_AC_14_AC_17()
    {
        using var rig = PlayingRig();

        // The slowest output 50 ms earlier: the picture delay moves 50 ms at 0.2 % (about 25 s), due follows it.
        for (var i = 0; i < 5; i++)
        {
            rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        }

        rig.Engine.Render(500);

        rig.Session.Counters.Late.ShouldBe(0);
    }
}

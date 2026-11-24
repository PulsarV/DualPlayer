using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T51: a seek past the end leaves a seek record; a seek or resume during a move logs the delay the film aligned at.</summary>
public sealed class PlaybackSessionSeekRecordTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    // dev-1 is heard 240 ms late, dev-2 is wired: the picture delay is 240 ms; a press on output 1 moves it to 230.
    private static SessionRig PlayingRig(double totalSeconds = 600)
    {
        var rig = new SessionRig(TwoDevices, null, totalSeconds);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_seek_past_the_end_publishes_one_seek_picture_delay_record_AC_17()
    {
        using var rig = PlayingRig(totalSeconds: 30);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<PictureDelayChanged>().Count(e => e.Cause == PictureDelayCause.Seek).ShouldBe(1);
    }

    [Fact]
    public void A_seek_during_a_picture_delay_move_records_the_target_delay_AC_17()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100); // part way: ~238 ms of the 240 -> 230 move
        rig.Events.OfType<PictureDelayChanged>().ShouldContain(e => e.Cause == PictureDelayCause.Offset && e.NewPictureDelayMs == 230);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();

        var seek = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Seek);
        seek.NewPictureDelayMs.ShouldBe(230);
    }

    [Fact]
    public void A_resume_during_a_picture_delay_move_records_the_target_delay_AC_17()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100); // part way through the move
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();

        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();

        var resume = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Resume);
        resume.NewPictureDelayMs.ShouldBe(230);
    }
}

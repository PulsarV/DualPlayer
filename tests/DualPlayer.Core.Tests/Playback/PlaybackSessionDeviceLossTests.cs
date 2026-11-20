using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionDeviceLossTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    // dev-1: Bluetooth headphones, 40 + 200 = 240 ms assumed, lag 0; dev-2: wired, 0 ms assumed, lag 240. PD = 240.
    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void Losing_the_slowest_output_keeps_the_rest_playing_follows_the_new_largest_latency_and_shows_a_note_AC_15()
    {
        using var rig = PlayingRig();

        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.Barrier();

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Events.OfType<OutputLost>().ShouldHaveSingleItem().ShouldBe(new OutputLost(1, "dev-1", rig.Events.OfType<OutputLost>().Single().Clock, true) { OldPictureDelayMs = 240, NewPictureDelayMs = 0 });
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Output 1 lost");
        rig.Events.OfType<PictureDelayChanged>().ShouldContain(e => e.Cause == PictureDelayCause.DeviceLost && e.OldPictureDelayMs == 240 && e.NewPictureDelayMs == 0);
        rig.Renderer.IsPlaying.ShouldBeTrue();
        rig.Renderer.Speed.ShouldBe(1.002, 1e-9);

        rig.Engine.Render(300);
        rig.Engine.Devices[1].IsLost.ShouldBeFalse();
        rig.Engine.Engine.CurrentLag("dev-2")!.Value.TotalMilliseconds.ShouldBeLessThan(240);
    }

    [Fact]
    public void Losing_an_output_that_is_not_the_slowest_leaves_the_picture_delay_and_shows_a_note_AC_15()
    {
        using var rig = PlayingRig();

        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Events.OfType<OutputLost>().ShouldHaveSingleItem().OutputNumber.ShouldBe(2);
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Output 2 lost");
        rig.Events.OfType<PictureDelayChanged>().ShouldNotContain(e => e.Cause == PictureDelayCause.DeviceLost && e.NewPictureDelayMs != 240);
        rig.Renderer.Speed.ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void An_offset_key_for_a_lost_output_is_ignored_AC_15()
    {
        using var rig = PlayingRig();
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();

        rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10));

        rig.Events.OfType<OffsetChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void Offset_keys_keep_their_output_numbers_after_a_loss_AC_15()
    {
        using var rig = PlayingRig();
        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.Barrier();

        rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10));
        rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10));

        rig.Events.OfType<OffsetChanged>().Count().ShouldBe(2);
        rig.Events.OfType<OffsetChanged>().ShouldAllBe(e => e.OutputNumber == 2 && e.DeviceId == "dev-2");

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10));

        rig.Events.OfType<OffsetChanged>().Count().ShouldBe(2);
    }

    [Fact]
    public void A_second_loss_follows_the_largest_latency_of_the_outputs_still_playing_AC_15()
    {
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.FromMilliseconds(20), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-3", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(3))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.Barrier();
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();

        var changes = rig.Events.OfType<PictureDelayChanged>().Where(e => e.Cause == PictureDelayCause.DeviceLost).ToList();
        changes.Count.ShouldBe(2);
        changes[0].OldPictureDelayMs.ShouldBe(240);
        changes[0].NewPictureDelayMs.ShouldBe(220);
        changes[1].OldPictureDelayMs.ShouldBe(220);
        changes[1].NewPictureDelayMs.ShouldBe(0);
        rig.Session.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public void Losing_the_last_output_ends_the_film_with_the_picture_frozen_and_a_note_AC_15b()
    {
        using var rig = PlayingRig();
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();

        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();

        rig.Engine.Summary!.EndReason.ShouldBe(RunEndReason.AllDevicesLost);
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        rig.Events.OfType<OutputLost>().Last().OthersContinue.ShouldBeFalse();
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Output 1 lost - last output, film ended");
        rig.Renderer.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void A_loss_with_others_continuing_carries_the_old_and_new_picture_delay_AC_15()
    {
        using var rig = PlayingRig();

        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.Barrier();

        var lost = rig.Events.OfType<OutputLost>().ShouldHaveSingleItem();
        lost.OldPictureDelayMs.ShouldBe(240);
        lost.NewPictureDelayMs.ShouldBe(0);
    }
}

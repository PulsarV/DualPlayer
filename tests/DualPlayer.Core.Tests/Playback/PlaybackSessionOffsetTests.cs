using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionOffsetTests
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

    // The master clock runs at 1x in rig time, so the picture delay changes by (clock travelled - picture travelled).
    private static (TimeSpan Elapsed, TimeSpan Picture) Mark(SessionRig rig) => (rig.Engine.Elapsed, rig.Renderer.Presented!.Position);

    private static double PictureDelayMs(SessionRig rig, (TimeSpan Elapsed, TimeSpan Picture) since, double delayAtMarkMs)
    {
        var now = Mark(rig);
        return delayAtMarkMs + ((now.Elapsed - since.Elapsed) - (now.Picture - since.Picture)).TotalMilliseconds;
    }

    private static double LagMs(SessionRig rig, string device) => rig.Engine.Engine.CurrentLag(device)!.Value.TotalMilliseconds;

    [Fact]
    public void Pause_during_an_offset_ramp_publishes_each_output_lag_the_engine_applies_and_never_a_negative_one_AC_17()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);
        LagMs(rig, "dev-2").ShouldBeGreaterThan(237.9);

        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();

        var change = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Pause);
        foreach (var output in change.Outputs.Where(o => !o.IsLost))
        {
            output.LagMs.ShouldBeGreaterThanOrEqualTo(0);
            ((double)output.LagMs).ShouldBe(LagMs(rig, output.DeviceId), 1, $"{output.DeviceId}: published lag must be the applied one");
            output.AssumedMs.ShouldBe(output.ReportedMs + output.TypeDefaultMs - output.OffsetMs, $"{output.DeviceId}: assumed = reported + default - offset, even mid-ramp");
        }
    }

    [Fact]
    public void Every_picture_delay_record_keeps_the_true_assumed_latency_while_a_pause_mid_ramp_carries_the_applied_lag_AC_17()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();

        var records = rig.Events.OfType<PictureDelayChanged>().ToList();
        records.ShouldContain(e => e.Cause == PictureDelayCause.Pause);
        foreach (var record in records)
        {
            foreach (var output in record.Outputs.Where(o => !o.IsLost))
            {
                output.AssumedMs.ShouldBe(output.ReportedMs + output.TypeDefaultMs - output.OffsetMs, $"{record.Cause} {output.DeviceId}: assumed");
            }
        }

        var pause = records.Last(e => e.Cause == PictureDelayCause.Pause);
        var slow = pause.Outputs.Single(o => o.DeviceId == "dev-2");
        slow.AssumedMs.ShouldBe(0);
        ((double)slow.LagMs).ShouldBe(LagMs(rig, "dev-2"), 1, "pause record carries the applied lag");
        slow.LagMs.ShouldBeGreaterThan(pause.NewPictureDelayMs - 1);
    }

    [Fact]
    public void A_later_press_on_the_slowest_output_lowers_the_picture_delay_and_the_other_output_follows_gradually_AC_10_AC_14()
    {
        using var rig = PlayingRig();
        LagMs(rig, "dev-2").ShouldBe(240, 1);
        rig.Renderer.Speed.ShouldBe(1.0);

        var pressed = Mark(rig);
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();
        rig.Renderer.Speed.ShouldBe(1.002, 1e-9);
        rig.Events.OfType<PictureTimingReported>().ShouldNotBeEmpty();

        rig.Events.OfType<OffsetChanged>().ShouldContain(e => e.OutputNumber == 1 && e.DeviceId == "dev-1" && e.OldOffsetMs == 0 && e.NewOffsetMs == 10);
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Output 1: +10 ms");
        rig.Events.OfType<PictureDelayChanged>().ShouldContain(e => e.Cause == PictureDelayCause.Offset && e.OldPictureDelayMs == 240 && e.NewPictureDelayMs == 230);

        // Not at once: 1 s later at most 0.2 % (2 ms) has been travelled.
        rig.Engine.Render(100);
        LagMs(rig, "dev-2").ShouldBeGreaterThan(237.9);
        LagMs(rig, "dev-2").ShouldBeLessThan(240.0);
        PictureDelayMs(rig, pressed, 240).ShouldBe(238, 0.5);
        PictureDelayMs(rig, pressed, 240).ShouldBe(LagMs(rig, "dev-2"), 1);
        rig.Renderer.Speed.ShouldBe(1.002, 1e-9);

        rig.Engine.Render(700);
        LagMs(rig, "dev-2").ShouldBe(230, 1);
        LagMs(rig, "dev-1").ShouldBe(0, 1);
        PictureDelayMs(rig, pressed, 240).ShouldBe(230, 0.5);
        rig.Renderer.Speed.ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void An_earlier_press_on_the_fast_output_ramps_only_its_own_lag_AC_10()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.ChangeOffset(2, -10)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(800);

        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Output 2: -10 ms");
        LagMs(rig, "dev-2").ShouldBe(230, 1);
        LagMs(rig, "dev-1").ShouldBe(0, 1);
        rig.Events.OfType<PictureDelayChanged>().ShouldNotContain(e => e.Cause == PictureDelayCause.Offset && e.NewPictureDelayMs != 240);
    }

    [Fact]
    public void A_press_at_the_limit_keeps_the_offset_and_shows_the_limit_note_AC_11()
    {
        using var rig = PlayingRig();
        for (var i = 0; i < 50; i++)
        {
            rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10)).IsSuccess.ShouldBeTrue();
        }

        rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10));

        rig.Events.OfType<OffsetChanged>().Count().ShouldBe(50);
        rig.Events.OfType<OffsetChanged>().Last().NewOffsetMs.ShouldBe(500);
        rig.Events.OfType<NoteShown>().Last().Text.ShouldBe("Output 2: offset cannot go beyond ±500 ms");
    }

    [Fact]
    public void A_press_while_paused_is_remembered_and_applied_at_once_when_playback_continues_AC_10_AC_14()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<OffsetChanged>().ShouldContain(e => e.NewOffsetMs == 10);
        rig.Events.OfType<NoteShown>().ShouldContain(e => e.Text == "Output 1: +10 ms");
        LagMs(rig, "dev-2").ShouldBe(240, 1);

        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();
        LagMs(rig, "dev-2").ShouldBe(230, 1);
    }

    [Fact]
    public void A_press_whose_corrected_output_moves_further_than_the_picture_stretches_the_whole_move_so_both_arrive_together_AC_14()
    {
        // dev-1: 240 ms assumed (slowest), dev-2: 235 ms, dev-3: 0 ms. PD = 240; lags 0 / 5 / 240.
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.FromMilliseconds(235), "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Probe.Set("dev-3", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(3))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        // Output 1 gets +50 ms offset (assumed 240 -> 190): dev-2 becomes the slowest, PD 240 -> 235 (5 ms), dev-1's lag 0 -> 45 ms (45 ms).
        var pressed = Mark(rig);
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 50)).IsSuccess.ShouldBeTrue();

        // The move lasts 45 ms / 0.2 % = 22.5 s; after 5 s nothing is at its target yet and no rate exceeds 0.2 %.
        rig.Engine.Render(500);
        Math.Abs(rig.Renderer.Speed - 1.0).ShouldBeGreaterThan(0);
        Math.Abs(rig.Renderer.Speed - 1.0).ShouldBeLessThanOrEqualTo(0.002 + 1e-9);
        LagMs(rig, "dev-1").ShouldBeLessThan(45 - 1);

        // Past the end of the picture move the corrected output has arrived, and the picture is back at 1x.
        rig.Engine.Render(2000);
        rig.Renderer.Speed.ShouldBe(1.0, 1e-9);
        LagMs(rig, "dev-1").ShouldBe(45, 1);
        PictureDelayMs(rig, pressed, 240).ShouldBe(235, 0.5);
    }

    [Fact]
    public void A_long_stretched_move_keeps_every_unchanged_output_within_ten_ms_of_the_picture_and_ends_all_together_AC_14()
    {
        // dev-1: 120 ms assumed (slowest), dev-2: 60 ms, dev-3: 0 ms. PD = 120; lags 0 / 60 / 120.
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(120), "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.FromMilliseconds(60), "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Probe.Set("dev-3", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(3))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        LagMs(rig, "dev-2").ShouldBe(60, 1);
        LagMs(rig, "dev-3").ShouldBe(120, 1);

        // Output 3 gets -180 ms (assumed 0 -> 180): PD 120 -> 180 (60 ms), dev-3's lag 120 -> 0 (120 ms), so the move
        // lasts 120 ms / 0.2 % = 60 s; dev-1 (0 -> 60) and dev-2 (60 -> 120) move with the picture over the whole of it.
        var pressed = Mark(rig);
        rig.Engine.Call(() => rig.Session.ChangeOffset(3, -180)).IsSuccess.ShouldBeTrue();

        for (var second = 5; second <= 55; second += 5)
        {
            rig.Engine.Render(500);
            var pictureDelay = PictureDelayMs(rig, pressed, 120);
            (LagMs(rig, "dev-1") - pictureDelay).ShouldBe(0 - 120, 10, $"dev-1 against the picture at {second} s");
            (LagMs(rig, "dev-2") - pictureDelay).ShouldBe(60 - 120, 10, $"dev-2 against the picture at {second} s");
            Math.Abs(rig.Renderer.Speed - 1.0).ShouldBeGreaterThan(0);
            Math.Abs(rig.Renderer.Speed - 1.0).ShouldBeLessThanOrEqualTo(0.002 + 1e-9);
        }

        // Half-way the unchanged outputs are half-way too, not already at their targets.
        LagMs(rig, "dev-2").ShouldBeLessThan(119);

        // 1 s before the end the corrected output still has about 2 ms to go; it arrives by the render where the
        // picture returns to 1x.
        rig.Engine.Render(400);
        LagMs(rig, "dev-3").ShouldBeGreaterThan(1.5);
        var renders = 0;
        while (Math.Abs(rig.Renderer.Speed - 1.0) > 1e-9 && renders < 300)
        {
            rig.Engine.Render(1);
            renders++;
        }

        rig.Renderer.Speed.ShouldBe(1.0, 1e-9);
        LagMs(rig, "dev-3").ShouldBe(0, 0.5);
        LagMs(rig, "dev-1").ShouldBe(60, 0.5);
        LagMs(rig, "dev-2").ShouldBe(120, 0.5);
        PictureDelayMs(rig, pressed, 120).ShouldBe(180, 1);
    }

    [Fact]
    public void A_key_for_an_output_that_is_not_in_the_run_is_ignored_AC_10()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.ChangeOffset(3, 10));

        rig.Events.OfType<OffsetChanged>().ShouldBeEmpty();
        rig.Events.OfType<NoteShown>().ShouldBeEmpty();
    }
}

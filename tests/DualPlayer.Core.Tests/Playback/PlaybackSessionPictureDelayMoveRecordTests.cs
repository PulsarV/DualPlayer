using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T65: each picture-delay move publishes one record with its duration and the speeds it ran at (AC-14, AC-17).</summary>
public sealed class PlaybackSessionPictureDelayMoveRecordTests
{
    [Fact]
    public void An_offset_change_publishes_one_move_record_with_the_picture_and_each_ramp_within_2000_ppm_AC_14_AC_17()
    {
        // dev-1: Bluetooth, 240 ms assumed, lag 0; dev-2: wired, 0 ms assumed, lag 240. PD = 240.
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue();

        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        move.Cause.ShouldBe(PictureDelayCause.Offset);
        move.FromMs.ShouldBe(240);
        move.ToMs.ShouldBe(230);
        move.Duration.TotalSeconds.ShouldBe(5.0, 0.001);
        Math.Abs(move.PicturePpm).ShouldBe(2000, 1);
        move.Ramps.Select(r => r.DeviceId).ShouldBe(["dev-1", "dev-2"]);
        move.Ramps.ShouldAllBe(r => Math.Abs(r.RampPpm) <= 2000 + 1e-6);
        Math.Abs(move.Ramps.Single(r => r.DeviceId == "dev-2").RampPpm).ShouldBe(2000, 1);
        move.Ramps.Single(r => r.DeviceId == "dev-1").RampPpm.ShouldBe(0, 1e-6);
    }

    [Fact]
    public void A_move_stretched_by_a_corrected_output_records_the_stretched_duration_and_the_slower_picture_speed_AC_14()
    {
        // dev-1 240 ms assumed, dev-2 235 ms, dev-3 0 ms. +50 ms on output 1: PD 240 -> 235, dev-1's lag 0 -> 45 ms.
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.FromMilliseconds(235), "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Probe.Set("dev-3", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(3))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 50)).IsSuccess.ShouldBeTrue();

        var move = rig.Events.OfType<PictureDelayMoved>().ShouldHaveSingleItem();
        move.Duration.TotalSeconds.ShouldBe(22.5, 0.001);
        Math.Abs(move.PicturePpm).ShouldBe(5.0 / 22_500 * 1e6, 1);
        Math.Abs(move.Ramps.Single(r => r.DeviceId == "dev-1").RampPpm).ShouldBe(2000, 1);
        move.Ramps.ShouldAllBe(r => Math.Abs(r.RampPpm) <= 2000 + 1e-6);
    }

    [Fact]
    public void The_latency_line_writes_the_contract_device_type_name_not_the_enum_name_AC_13()
    {
        var log = new CapturingLogger<PlaybackSession>();
        using var rig = new SessionRig([new RigDevice(48_000)], null, 600, log: log);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(1))).IsSuccess.ShouldBeTrue();

        var line = log.Messages.First(m => m.StartsWith("Latency for dev-1", StringComparison.Ordinal));
        line.ShouldContain("type bluetooth-headphones");
        line.ShouldNotContain("BluetoothHeadphones");
    }
}

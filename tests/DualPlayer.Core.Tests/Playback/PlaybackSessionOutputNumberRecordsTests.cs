using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionOutputNumberRecordsTests
{
    [Fact]
    public void After_output_1_is_lost_a_picture_delay_change_names_outputs_2_and_3_AC_17()
    {
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.FromMilliseconds(20), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-3", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(3))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.Barrier();
        rig.Engine.Call(() => rig.Session.ChangeOffset(2, 10));
        rig.Engine.Render(300);

        var change = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Offset);
        change.Outputs.Select(o => o.OutputNumber).ShouldBe([1, 2, 3]);
        change.Outputs.Select(o => o.DeviceId).ShouldBe(["dev-1", "dev-2", "dev-3"]);
        change.Outputs.Select(o => o.IsLost).ShouldBe([true, false, false]);
        foreach (var moved in rig.Events.OfType<PictureDelayMoved>().Where(m => m.Cause == PictureDelayCause.Offset))
        {
            moved.Ramps.Select(r => r.OutputNumber).ShouldBe([2, 3]);
        }
    }

    [Fact]
    public void A_failed_probe_says_type_other_in_the_source_and_in_the_warning_AC_13()
    {
        var log = new CapturingLogger<PlaybackSession>();
        using var rig = new SessionRig([new RigDevice(48_000)], null, 600, log: log);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(1))).IsSuccess.ShouldBeTrue();

        var source = rig.Events.OfType<LatencyEstimated>().First().Estimate.Outputs.Single().Source;
        source.ShouldNotBeNull().ShouldContain("type other", Case.Sensitive);
        source.ShouldNotContain("type Other", Case.Sensitive);
        var warning = log.Messages.First(m => m.StartsWith("Latency probe failed", StringComparison.Ordinal));
        warning.ShouldContain("type other", Case.Sensitive);
        warning.ShouldNotContain("type Other", Case.Sensitive);
    }
}

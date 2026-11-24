using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T44 (R10): "lip-synced again" needs the picture within 20 ms and every live output within 10 ms (spec §6 recovery row, AC-17).</summary>
public sealed class PlaybackSessionLipSyncedOutputsTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    // dev-1: Bluetooth headphones, 40 + 200 = 240 ms assumed; dev-2: wired, 0 ms. Picture delay 240 ms.
    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static LipSyncedAgain? SeekRecord(SessionRig rig) => rig.Events.OfType<LipSyncedAgain>().LastOrDefault(e => e.Command == "seek");

    [Fact]
    public void An_output_that_stays_over_10_ms_off_after_a_seek_delays_lip_synced_again_until_it_is_within_10_ms_AC_17()
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-2", 30);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50); // half a second on the fake clock (inside the 1 s recovery budget): the picture is within 20 ms, dev-2 is not within 10 ms

        SeekRecord(rig).ShouldBeNull("lip-synced again was reported while an output was still 30 ms off");

        rig.ForceOutputError("dev-2", 4);
        for (var i = 0; i < 40 && SeekRecord(rig) is null; i++)
        {
            rig.Engine.Render(1);
        }

        var again = SeekRecord(rig).ShouldNotBeNull("lip-synced again was never reported once the output was within 10 ms");
        again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(500, "reported before the output was within 10 ms");
        again.AfterCommandMs.ShouldBeLessThanOrEqualTo(1000, "reported long after the output was within 10 ms");
    }

    [Fact]
    public void Lip_synced_again_carries_every_outputs_position_error_each_within_10_ms_AC_17()
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-1", 6.5);
        rig.ForceOutputError("dev-2", -4.25);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(150);

        var again = SeekRecord(rig).ShouldNotBeNull();
        again.AfterCommandMs.ShouldBeLessThanOrEqualTo(1000, "outputs within 10 ms must not hold the record back");
        again.OutputErrorsMs.Keys.ShouldBe(["dev-1", "dev-2"], ignoreOrder: true);
        again.OutputErrorsMs.Values.ShouldAllBe(e => Math.Abs(e) <= 10);
        again.OutputErrorsMs["dev-1"].ShouldBe(6.5, 1e-9);
        again.OutputErrorsMs["dev-2"].ShouldBe(-4.25, 1e-9);
    }

    [Fact]
    public void A_lost_output_neither_delays_lip_synced_again_nor_appears_in_its_output_errors_AC_15_AC_17()
    {
        using var rig = PlayingRig();
        rig.ForceOutputError("dev-2", 30);
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(150);

        var again = SeekRecord(rig).ShouldNotBeNull("the lost output held lip-synced again back");
        again.OutputErrorsMs.Keys.ShouldBe(["dev-1"]);
    }
}

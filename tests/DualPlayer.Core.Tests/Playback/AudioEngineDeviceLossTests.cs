using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Tests.Fakes;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>AC-06 with three outputs: each further loss behaves like the first one (test-plan "each further loss with three outputs").</summary>
public sealed class AudioEngineDeviceLossTests
{
    private const double ToleranceMs = 10;

    [Fact]
    public void With_three_outputs_losing_two_in_turn_the_third_keeps_playing_in_tolerance_without_underruns_until_it_is_lost_too()
    {
        using var rig = new EngineRig([new RigDevice(48_000, 100), new RigDevice(44_100, -150), new RigDevice(48_000, 50, TimeSpan.FromMilliseconds(10))]);
        rig.Start();
        rig.Render(500);

        rig.Devices[1].SimulateLoss();
        rig.Barrier();
        rig.Render(500);
        rig.Devices[0].SimulateLoss();
        rig.Barrier();
        var recordsAtSecondLoss = rig.RecordsOf("dev-3").Count;
        rig.Render(2_500);

        var lost = rig.Events.OfType<DeviceLost>().ToList();
        lost.Select(e => e.DeviceId).ShouldBe(["dev-2", "dev-1"]);
        lost.ShouldAllBe(e => e.OthersContinue);
        lost[0].ClockPosition.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        lost[1].ClockPosition.ShouldBeGreaterThan(lost[0].ClockPosition);
        rig.Summary.ShouldBeNull("the run must continue while one device plays");
        rig.RecordsOf("dev-3").Count.ShouldBeGreaterThanOrEqualTo(recordsAtSecondLoss + 24, "the survivor keeps getting a record every second");
        rig.RecordsOf("dev-2").Count.ShouldBeLessThanOrEqualTo(6);
        rig.RecordsOf("dev-1").Count.ShouldBeLessThanOrEqualTo(11);
        var survivor = rig.RecordsOf("dev-3");
        survivor.Where(r => r.ClockPosition > TimeSpan.FromSeconds(2)).Max(r => Math.Abs(r.DriftMs)).ShouldBeLessThanOrEqualTo(ToleranceMs);
        survivor.ShouldAllBe(r => r.Underruns == 0);

        rig.Devices[2].SimulateLoss();
        rig.WaitFor(() => rig.Summary is not null).ShouldBeTrue();
        var summary = rig.Summary.ShouldNotBeNull();
        summary.EndReason.ShouldBe(RunEndReason.AllDevicesLost);
        summary.Outputs.Single(o => o.DeviceId == "dev-3").Underruns.ShouldBe(0);
        summary.Outputs.Single(o => o.DeviceId == "dev-3").Played.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(30));
        summary.Outputs.SelectMany(o => o.DeviceLostEvents).Select(e => e.DeviceId).ShouldBe(["dev-2", "dev-1", "dev-3"], ignoreOrder: true);
    }
}

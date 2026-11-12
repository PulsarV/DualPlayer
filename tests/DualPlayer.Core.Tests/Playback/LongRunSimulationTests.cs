using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Tests.Fakes;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// AC-07 through the real <see cref="DualPlayer.Core.Playback.AudioEngine"/>: three hours of fake time with devices at
/// different sample rates whose clocks run at +/-200 ppm, a decoder with mismatched block sizes (1536 and 1024 frames) and a
/// start latency, sampled by the engine at its real 100 ms cadence.
/// </summary>
public sealed class LongRunSimulationTests
{
    private const double ToleranceMs = 10;
    private static readonly TimeSpan ThreeHours = TimeSpan.FromHours(3);

    [Fact]
    public void Two_outputs_at_plus_and_minus_200_ppm_stay_within_tolerance_for_three_hours_with_no_underruns_and_no_corrections()
    {
        using var rig = new EngineRig(
            [
                new RigDevice(Rate: 48_000, Ppm: 200, StartLatency: TimeSpan.FromMilliseconds(20)),
                new RigDevice(Rate: 44_100, Ppm: -200, StartLatency: TimeSpan.FromMilliseconds(5)),
            ],
            totalSeconds: ThreeHours.TotalSeconds + 120,
            keepRendered: false,
            barrierEvery: 10);
        rig.Start();

        rig.Render((int)(ThreeHours / EngineRig.Step));
        rig.Stop(RunEndReason.Stop);

        var records = rig.Records;
        var seconds = (int)ThreeHours.TotalSeconds;
        foreach (var id in new[] { "dev-1", "dev-2" })
        {
            rig.RecordsOf(id).Count.ShouldBeGreaterThanOrEqualTo(seconds - 5, $"{id} sync records");
        }

        var steady = records.Where(r => r.ClockPosition > TimeSpan.FromSeconds(2)).ToList();
        steady.Max(r => Math.Abs(r.DriftMs)).ShouldBeLessThanOrEqualTo(ToleranceMs);
        records.ShouldAllBe(r => r.Underruns == 0);
        rig.Events.OfType<Excursion>().ShouldBeEmpty();
        rig.Events.OfType<DeviceLost>().ShouldBeEmpty();

        var summary = rig.Summary.ShouldNotBeNull();
        summary.EndReason.ShouldBe(RunEndReason.Stop);
        summary.Outputs.Count.ShouldBe(2);
        summary.Outputs.ShouldAllBe(o => o.Corrections == 0);
        summary.Outputs.ShouldAllBe(o => o.Underruns == 0);
        summary.Outputs.ShouldAllBe(o => o.MaxDriftMs <= ToleranceMs);
        summary.Outputs.ShouldAllBe(o => o.Played >= ThreeHours - TimeSpan.FromSeconds(2));
        summary.Outputs.Single(o => o.DeviceId == "dev-1").ClockMismatchPpm.ShouldBe(200, 5);
        summary.Outputs.Single(o => o.DeviceId == "dev-2").ClockMismatchPpm.ShouldBe(-200, 5);
    }
}

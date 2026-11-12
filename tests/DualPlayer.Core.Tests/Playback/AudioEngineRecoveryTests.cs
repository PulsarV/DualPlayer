using DualPlayer.Core.Tests.Fakes;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// Spec §6 recovery time through the real engine (AC-07, AC-09): after a start, a resume or a seek every output is within
/// 10 ms of the master clock at most one second later and stays there. The fakes make it hard: devices at 48 kHz and
/// 44.1 kHz whose clocks run +/-300 ppm, a decoder with mismatched block sizes (1536 and 1024 frames), a device that
/// reports its position late, a position that steps while paused and a seek whose decoder lands before the target.
/// </summary>
public sealed class AudioEngineRecoveryTests
{
    private const double ToleranceMs = 10;
    private static readonly TimeSpan Recovery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Settled = TimeSpan.FromSeconds(31);

    private static void AssertWithinToleranceFromRecoveryOn(EngineRig rig, TimeSpan commandAt, TimeSpan positionAtCommand)
    {
        for (var d = 0; d < rig.Devices.Count; d++)
        {
            rig.MaxDriftMs(d, commandAt, positionAtCommand, Recovery, Settled)
                .ShouldBeLessThanOrEqualTo(ToleranceMs, $"dev-{d + 1} drift between {Recovery.TotalSeconds} s and {Settled.TotalSeconds} s after the command");
        }
    }

    [Theory]
    [InlineData(300, 15)]
    [InlineData(-300, 50)]
    [InlineData(300, 120)]
    [InlineData(-300, 15)]
    public void After_a_start_with_a_device_reporting_its_position_late_every_output_is_within_tolerance_one_second_later(double ppm, int latencyMs)
    {
        using var rig = new EngineRig(
            [new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm, TimeSpan.FromMilliseconds(latencyMs))],
            observe: true);

        rig.Start();
        rig.Render(3_200);

        AssertWithinToleranceFromRecoveryOn(rig, TimeSpan.Zero, TimeSpan.Zero);
    }

    [Theory]
    [InlineData(300, 15)]
    [InlineData(-300, -15)]
    [InlineData(300, -15)]
    [InlineData(-300, 40)]
    public void After_a_resume_with_a_stepped_device_position_every_output_is_within_tolerance_one_second_later(double ppm, int stepMs)
    {
        using var rig = new EngineRig([new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm)], observe: true);
        rig.Start();
        rig.Render(3_000);
        rig.Pause();
        rig.Render(500);
        rig.Devices[1].StepPosition(stepMs * 44_100L / 1000);

        rig.Resume();
        var resumedAt = rig.Elapsed;
        var position = rig.Position;
        rig.Render(3_200);

        AssertWithinToleranceFromRecoveryOn(rig, resumedAt, position);
    }

    [Theory]
    [InlineData(300, 60, 0)]
    [InlineData(-300, 60, 37)]
    [InlineData(300, -10, 37)]
    [InlineData(-300, 10, 80)]
    [InlineData(300, -60, 0)]
    public void After_a_seek_whose_decoder_lands_before_the_target_every_output_is_within_tolerance_one_second_later(
        double ppm, int offsetSeconds, int landsEarlyMs)
    {
        using var rig = new EngineRig(
            [new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm)],
            seekLandsEarly: [TimeSpan.FromMilliseconds(landsEarlyMs), TimeSpan.FromMilliseconds(landsEarlyMs / 2)],
            observe: true);
        rig.Start();
        rig.Render(7_000);
        var target = TimeSpan.FromSeconds(70 + offsetSeconds);
        var commandAt = rig.Elapsed;

        rig.Seek(target);
        rig.Render(3_300);

        AssertWithinToleranceFromRecoveryOn(rig, commandAt, target);
    }
}

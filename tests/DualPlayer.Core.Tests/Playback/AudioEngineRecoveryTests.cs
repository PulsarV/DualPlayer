using DualPlayer.Core.Diagnostics;
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
        rig.Render(3_200);
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

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    public void After_a_seek_whose_first_block_lands_45_ms_after_the_target_the_played_audio_stays_within_tolerance(double ppm)
    {
        using var rig = new EngineRig(
            [new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm)],
            seekLandsLate: [TimeSpan.FromMilliseconds(45), TimeSpan.Zero],
            observe: true);
        rig.Start();
        rig.Render(7_000);
        var target = TimeSpan.FromSeconds(130);
        var commandAt = rig.Elapsed;

        rig.Seek(target);
        rig.Render(3_300);

        AssertWithinToleranceFromRecoveryOn(rig, commandAt, target);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    public void After_a_start_whose_stream_begins_45_ms_into_the_file_the_played_audio_stays_within_tolerance(double ppm)
    {
        using var rig = new EngineRig(
            [new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm)],
            startLandsLate: [TimeSpan.FromMilliseconds(45), TimeSpan.Zero],
            observe: true);

        rig.Start();
        rig.Render(3_200);

        AssertWithinToleranceFromRecoveryOn(rig, TimeSpan.Zero, TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void After_an_output_that_hung_during_a_seek_recovers_it_plays_within_tolerance_and_its_records_tell_the_truth(double ppm)
    {
        using var rig = new EngineRig([new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm)], observe: true);
        rig.Start();
        rig.Render(1_000);
        rig.Devices[1].Hang();
        rig.Render(300); // past the one second after which an output counts as stalled
        rig.Seek(TimeSpan.FromSeconds(120));
        rig.Render(100);

        rig.Devices[1].Unhang();
        var recoveredAt = rig.Elapsed;
        var position = rig.Position;
        var recordsBefore = rig.RecordsOf("dev-2").Count;
        rig.Render(3_200);

        // what the hung output actually plays, from its rendered samples, not from the engine's own records
        rig.MaxDriftMs(1, recoveredAt, position, Recovery, Settled)
            .ShouldBeLessThanOrEqualTo(ToleranceMs, "dev-2 played drift after it recovered");
        rig.MaxDriftMs(0, recoveredAt, position, TimeSpan.Zero, Settled)
            .ShouldBeLessThanOrEqualTo(ToleranceMs, "dev-1 must be unaffected");

        // the records published after recovery agree with the played audio: no 0 ms while the output is off
        var after = rig.RecordsOf("dev-2").Skip(recordsBefore).ToArray();
        after.ShouldNotBeEmpty();
        var late = after.Where(r => r.ClockPosition >= position + Recovery).ToArray();
        late.ShouldNotBeEmpty();
        late.ShouldAllBe(r => Math.Abs(r.DriftMs) <= ToleranceMs);
        var real = rig.MaxDriftMs(1, recoveredAt, position, TimeSpan.Zero, Recovery);
        var reported = after.Where(r => r.ClockPosition < position + Recovery).Select(r => Math.Abs(r.DriftMs)).DefaultIfEmpty(0).Max();
        if (real > 100)
        {
            reported.ShouldBeGreaterThan(ToleranceMs, $"the output played {real:F0} ms off right after recovery but its records reported at most {reported:F2} ms");
        }

        rig.Summary.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void After_an_output_that_hung_for_three_seconds_recovers_without_a_seek_it_is_aligned_once_with_no_underruns(double ppm)
    {
        RigDevice[] devices = [new RigDevice(48_000, ppm), new RigDevice(44_100, -ppm)];

        // control run: the same playback without the hang, to count what a healthy run corrects anyway
        int Corrections(bool hang, out TimeSpan recoveredAt, out TimeSpan position, out long underrunsAtRecovery, out EngineRig rig)
        {
            rig = new EngineRig(devices, observe: true);
            rig.Start();
            rig.Render(1_000);
            if (hang)
            {
                rig.Devices[1].Hang();
                rig.Render(300);
                rig.Devices[1].Unhang();
            }
            else
            {
                rig.Render(300);
            }

            recoveredAt = rig.Elapsed;
            position = rig.Position;
            underrunsAtRecovery = rig.RecordsOf("dev-2").LastOrDefault()?.Underruns ?? 0;
            rig.Render(3_200);
            rig.Stop(RunEndReason.Stop);
            return rig.Summary!.Outputs.Single(o => o.DeviceId == "dev-2").Corrections;
        }

        var baseline = Corrections(false, out _, out _, out _, out var control);
        control.Dispose();
        var withHang = Corrections(true, out var recoveredAt, out var position, out var underrunsAtRecovery, out var rig);
        using (rig)
        {
            // what the recovered output actually plays, from its rendered samples
            rig.MaxDriftMs(1, recoveredAt, position, Recovery, Settled)
                .ShouldBeLessThanOrEqualTo(ToleranceMs, "dev-2 played drift after it recovered");
            rig.MaxDriftMs(0, recoveredAt, position, TimeSpan.Zero, Settled)
                .ShouldBeLessThanOrEqualTo(ToleranceMs, "dev-1 must be unaffected");

            var second = rig.Summary!.Outputs.Single(o => o.DeviceId == "dev-2");
            var first = rig.Summary.Outputs.Single(o => o.DeviceId == "dev-1");
            (second.Underruns - underrunsAtRecovery).ShouldBe(0, "dev-2 underruns after recovery");
            first.Underruns.ShouldBe(0, "the healthy output must never underrun");
            (withHang - baseline).ShouldBeLessThanOrEqualTo(1, "corrections dev-2 needed after the hang beyond a run without it");
        }
    }
}

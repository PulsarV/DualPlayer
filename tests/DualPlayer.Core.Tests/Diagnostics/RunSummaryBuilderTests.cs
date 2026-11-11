using DualPlayer.Core.Diagnostics;
using Shouldly;

namespace DualPlayer.Core.Tests.Diagnostics;

public class RunSummaryBuilderTests
{
    private static TimeSpan At(int s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Summary_lists_every_output_with_played_duration_mismatch_corrections_and_underruns()
    {
        var b = new RunSummaryBuilder();
        b.OutputStarted("dev-A", At(0));
        b.OutputStarted("dev-B", At(0));
        b.RecordDrift("dev-A", 1.5, inRecoveryWindow: false);
        b.RecordClockMismatch("dev-A", 35.0);
        b.RecordClockMismatch("dev-B", -12.0);
        b.RecordCorrection("dev-B");
        b.RecordCorrection("dev-B");
        b.RecordUnderruns("dev-B", 4);

        var summary = b.Build(RunEndReason.FileEnd, At(112));

        summary.EndReason.ShouldBe(RunEndReason.FileEnd);
        summary.ClockPosition.ShouldBe(At(112));
        summary.Outputs.Count.ShouldBe(2);
        var a = summary.Outputs.Single(o => o.DeviceId == "dev-A");
        a.Played.ShouldBe(At(112));
        a.ClockMismatchPpm.ShouldBe(35.0, 0.001);
        a.Corrections.ShouldBe(0);
        a.Underruns.ShouldBe(0);
        a.DeviceLostEvents.ShouldBeEmpty();
        var bOut = summary.Outputs.Single(o => o.DeviceId == "dev-B");
        bOut.ClockMismatchPpm.ShouldBe(-12.0, 0.001);
        bOut.Corrections.ShouldBe(2);
        bOut.Underruns.ShouldBe(4);
    }

    [Fact]
    public void Largest_steady_state_drift_excludes_recovery_windows_and_uses_magnitude()
    {
        var b = new RunSummaryBuilder();
        b.OutputStarted("dev-A", At(0));
        b.RecordDrift("dev-A", 90, inRecoveryWindow: true);
        b.RecordDrift("dev-A", 3, false);
        b.RecordDrift("dev-A", -7.5, false);
        b.RecordDrift("dev-A", 4, false);

        var summary = b.Build(RunEndReason.Stop, At(10));

        summary.Outputs.Single().MaxDriftMs.ShouldBe(7.5, 0.001);
    }

    [Fact]
    public void Lost_output_stops_played_duration_at_the_loss_and_carries_its_device_lost_event()
    {
        var b = new RunSummaryBuilder();
        b.OutputStarted("dev-A", At(0));
        b.OutputStarted("dev-B", At(0));
        var lost = new DeviceLost(1, "dev-B", At(40), OthersContinue: true);
        b.RecordDeviceLost(lost);

        var summary = b.Build(RunEndReason.Interrupt, At(100));

        var bOut = summary.Outputs.Single(o => o.DeviceId == "dev-B");
        bOut.Played.ShouldBe(At(40));
        bOut.DeviceLostEvents.ShouldBe([lost]);
        summary.Outputs.Single(o => o.DeviceId == "dev-A").Played.ShouldBe(At(100));
    }

    [Fact]
    public void Summary_carries_the_end_reason()
    {
        var b = new RunSummaryBuilder();
        b.OutputStarted("dev-A", At(0));

        b.Build(RunEndReason.AllDevicesLost, At(5)).EndReason.ShouldBe(RunEndReason.AllDevicesLost);
    }
}

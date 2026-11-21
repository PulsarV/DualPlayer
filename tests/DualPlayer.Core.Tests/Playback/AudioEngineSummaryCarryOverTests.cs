using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T48: a run that continues the previous one carries that run's summary over only when it plays the same file.</summary>
public sealed class AudioEngineSummaryCarryOverTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private static readonly OutputAssignment[] Assignments = [new(1, DeviceRef.ByNumber(1)), new(2, DeviceRef.ByNumber(2))];

    // Plays "film.mkv" for about 20 s, stops it, then plays a continuing run of file for 0.3 s and stops that too.
    private static RunSummary ContinuingRunSummary(string file)
    {
        using var rig = new EngineRig(TwoDevices);
        rig.Start();
        rig.Render(2_000);
        rig.Stop(RunEndReason.FileChange);
        rig.Summary.ShouldNotBeNull().Outputs.ShouldAllBe(o => o.Played >= TimeSpan.FromSeconds(19), "the first run played about 20 s");

        rig.Engine.StartRun(file, Assignments, new EngineOptions { ContinuesPreviousRun = true }).IsSuccess.ShouldBeTrue();
        rig.CommandDone(TimeSpan.Zero, paused: true); // the new run holds 00:00 through its pre-roll
        rig.Render(30);
        rig.Stop(RunEndReason.Stop);

        var summaries = rig.Events.OfType<RunSummary>().ToList();
        summaries.Count.ShouldBe(2);
        return summaries[1];
    }

    [Fact]
    public void A_continuing_run_of_another_file_does_not_carry_the_previous_files_summary_AC_06()
    {
        var summary = ContinuingRunSummary("other.mkv");

        summary.Outputs.ShouldNotBeEmpty();
        summary.Outputs.ShouldAllBe(o => o.Played < TimeSpan.FromSeconds(1), "the 20 s belong to film.mkv, not to other.mkv");
    }

    [Fact]
    public void A_continuing_run_of_the_same_file_carries_the_previous_summary_over_AC_06()
    {
        var summary = ContinuingRunSummary("film.mkv");

        summary.Outputs.ShouldNotBeEmpty();
        summary.Outputs.ShouldAllBe(o => o.Played >= TimeSpan.FromSeconds(19), "the return run continues film.mkv's played time");
    }
}

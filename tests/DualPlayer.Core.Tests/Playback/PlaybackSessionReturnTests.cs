using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T41: a return into the film after the end is a seek with an engine start position: no audio of the film's opening is
/// heard first, and the run summary at the next end or exit covers the file's playback since it was opened (AC-05, AC-06).
/// The fake decoder gives frame n the value n / 1000 (silence is 0), so a sample's value names the film position it came from.
/// </summary>
public sealed class PlaybackSessionReturnTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig EndedRig()
    {
        var rig = new SessionRig(TwoDevices, null, 30);
        rig.Renderer.Duration = TimeSpan.FromSeconds(30);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(500); // five seconds of the film
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50);
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        return rig;
    }

    private static List<RunSummary> Summaries(SessionRig rig) => [.. rig.Engine.Events.OfType<RunSummary>()];

    [Fact]
    public void A_backward_seek_at_the_end_lets_no_audio_from_the_start_of_the_film_reach_any_output_AC_05()
    {
        using var rig = EndedRig();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Engine.Devices.Count.ShouldBe(2, "the returning run's outputs");
        foreach (var device in rig.Engine.Devices)
        {
            var audible = device.Rendered.Where(v => v > 0).ToArray();
            audible.ShouldNotBeEmpty("the returning run played nothing");
            // Target 20 s = value 960; a sample below 19.8 s (950) is the film's opening, or anywhere before the target.
            audible.Min().ShouldBeGreaterThan(950f, $"audio of value {audible.Min():F3} (film position {audible.Min():F3} s) was heard before the target (20 s)");
        }
    }

    [Fact]
    public void The_run_summary_after_a_return_into_the_film_is_one_and_covers_the_playback_since_the_file_was_opened_AC_06()
    {
        using var rig = EndedRig();
        var atEnd = Summaries(rig);
        atEnd.Count.ShouldBe(1, "the seek past the end prints the summary of the end");
        atEnd[0].EndReason.ShouldBe(RunEndReason.SeekPastEnd);
        var before = atEnd[0].Outputs.Select(o => o.Played).ToArray();
        before.ShouldAllBe(p => p > TimeSpan.FromSeconds(4)); // five seconds were played before the seek

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200); // two more seconds from the target
        rig.Engine.Call(() =>
        {
            rig.Session.Exit();
            return true;
        });
        rig.Engine.AdvanceUntil(() => Summaries(rig).Count >= 2).ShouldBeTrue("the exit published no run summary");
        rig.Engine.Render(20);

        var summaries = Summaries(rig);
        summaries.Count.ShouldBe(2, "exactly one more summary after the return: " + string.Join(", ", summaries.Select(s => s.EndReason)));
        var final = summaries[1];
        for (var i = 0; i < final.Outputs.Count; i++)
        {
            // Carried over: the five seconds before the end plus the two after the return; not only the two after it.
            final.Outputs[i].Played.ShouldBeGreaterThan(before[i] + TimeSpan.FromSeconds(1.5), $"output {i + 1}: the summary covers only the stretch after the return");
        }
    }
}

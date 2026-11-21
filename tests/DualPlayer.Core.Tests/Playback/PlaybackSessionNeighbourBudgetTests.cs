using System.Text.RegularExpressions;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionNeighbourBudgetTests : IDisposable
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-budget-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public void One_page_down_over_slow_neighbours_answers_within_the_three_second_budget_AC_09b()
    {
        var probe = new SlowPictureProbe(TimeSpan.FromSeconds(2));
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe);
        probe.Time = rig.Engine.Time;
        foreach (var name in new[] { "Episode-02.mkv", "Episode-03.mkv", "Episode-04.mkv", "Episode-05.mkv" })
        {
            File(name);
        }

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        var key = rig.Engine.Time.GetUtcNow();
        rig.Engine.Call(() => rig.Session.NextFile());
        var elapsed = rig.Engine.Time.GetUtcNow() - key;

        elapsed.ShouldBeLessThanOrEqualTo(Budget + TimeSpan.FromMilliseconds(250), "the key was answered later than the 3 s budget");
        var note = rig.Events.OfType<NoteShown>().Select(e => e.Text).Single();
        note.ShouldStartWith("Next file not found within 3 s (checked ", Case.Sensitive);
        rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("running out of time is not the no-next-file outcome");
        rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem("only the probe that had its full limit is a skip");
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Probed.Count.ShouldBeLessThanOrEqualTo(2, "the scan kept probing after the deadline");
    }

    [Fact]
    public void A_probe_whose_limit_was_cut_by_the_budget_and_timed_out_is_not_reported_as_a_skipped_file_AC_09b()
    {
        var probe = new SlowPictureProbe(TimeSpan.FromSeconds(5));
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe);
        probe.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        File("Episode-04.mkv");

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileSkipped>().Count().ShouldBe(1, "a probe cut short by the budget was reported as a skipped file");
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldNotContain(t => t.Contains("cannot be played on this PC", StringComparison.Ordinal));
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldContain(t => t.StartsWith("Next file not found within 3 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_slow_track_listing_keeps_one_press_within_the_budget_AC_09b()
    {
        var probe = new SlowPictureProbe(TimeSpan.FromSeconds(2));
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe);
        probe.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        File("Episode-03.mkv");

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Decoder.BeforeList = () =>
        {
            var end = rig.Engine.Time.GetTimestamp() + (long)(2.5 * rig.Engine.Time.TimestampFrequency);
            while (rig.Engine.Time.GetTimestamp() < end)
            {
                Thread.Sleep(1);
            }
        };

        var key = rig.Engine.Time.GetUtcNow();
        rig.Engine.Call(() => rig.Session.NextFile());
        var elapsed = rig.Engine.Time.GetUtcNow() - key;

        elapsed.ShouldBeLessThanOrEqualTo(Budget + TimeSpan.FromMilliseconds(250), "the track listing ran past the budget");
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldContain(t => t.StartsWith("Next file not found within 3 s", StringComparison.Ordinal));
    }
}

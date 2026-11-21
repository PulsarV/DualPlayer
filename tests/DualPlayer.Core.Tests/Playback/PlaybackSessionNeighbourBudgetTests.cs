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
        var note = rig.Events.OfType<NoteShown>().Select(e => e.Text).Single(t => t.StartsWith("No next file (skipped ", StringComparison.Ordinal));
        var count = int.Parse(Regex.Match(note, @"skipped (\d+);").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        count.ShouldBe(rig.Events.OfType<FileSkipped>().Count());
        count.ShouldBeGreaterThan(0);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Probed.Count.ShouldBeLessThanOrEqualTo(2, "the scan kept probing after the deadline");
    }
}

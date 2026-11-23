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
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
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
        var timedOut = rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem("one press, one budget-expiry event");
        timedOut.Next.ShouldBeTrue();
        timedOut.Checked.ShouldBe(1, "one file was judged (skipped) before the budget ran out");
        note.ShouldBe("Next file not found within 3 s (checked 1)");
    }

    [Fact]
    public void One_page_up_over_slow_neighbours_publishes_one_previous_direction_timeout_with_the_checked_count_AC_09b()
    {
        var probe = new SlowPictureProbe(TimeSpan.FromSeconds(2));
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        foreach (var name in new[] { "Episode-01.mkv", "Episode-02.mkv", "Episode-03.mkv", "Episode-04.mkv" })
        {
            File(name);
        }

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-05.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        var key = rig.Engine.Time.GetUtcNow();
        rig.Engine.Call(() => rig.Session.PreviousFile());
        var elapsed = rig.Engine.Time.GetUtcNow() - key;

        elapsed.ShouldBeLessThanOrEqualTo(Budget + TimeSpan.FromMilliseconds(250), "the key was answered later than the 3 s budget");
        var timedOut = rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem("one press, one budget-expiry event");
        timedOut.Next.ShouldBeFalse("Page Up searches backwards");
        timedOut.Checked.ShouldBe(1);
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldHaveSingleItem().ShouldBe("Previous file not found within 3 s (checked 1)");
        rig.Events.OfType<NoNeighbour>().ShouldBeEmpty();
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void A_probe_whose_limit_was_cut_by_the_budget_and_timed_out_is_not_reported_as_a_skipped_file_AC_09b()
    {
        var probe = new SlowPictureProbe(TimeSpan.FromSeconds(5));
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
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
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        File("Episode-03.mkv");

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Decoder.BeforeList = () =>
        {
            var end = rig.Engine.Time.GetTimestamp() + (long)(2.5 * rig.Engine.Time.TimestampFrequency);
            rig.Engine.Time.SpinUntil(end);
        };

        var key = rig.Engine.Time.GetUtcNow();
        rig.Engine.Call(() => rig.Session.NextFile());
        var elapsed = rig.Engine.Time.GetUtcNow() - key;

        elapsed.ShouldBeLessThanOrEqualTo(Budget + TimeSpan.FromMilliseconds(250), "the track listing ran past the budget");
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldContain(t => t.StartsWith("Next file not found within 3 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_track_listing_that_blocks_past_the_budget_is_cut_off_at_the_budget_and_reports_zero_checked_AC_09b()
    {
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
        File("Episode-02.mkv");
        File("Episode-03.mkv");

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        // The listing spends fake time only up to the budget plus the 50 ms slack the press gives a worker (registered with the rig as
        // its deadline, so the rig never moves the clock past it, however late the session thread wakes), then blocks in real time
        // until the test lets it go: a listing that never comes back.
        var release = new ManualResetEventSlim(); // not disposed: the abandoned worker may still be waiting on it
        using var releaseOnExit = new Defer(release.Set);
        rig.Decoder.BeforeList = () =>
        {
            var end = rig.Engine.Time.GetTimestamp() + (long)((Budget + TimeSpan.FromMilliseconds(50)).TotalSeconds * rig.Engine.Time.TimestampFrequency);
            rig.Engine.Time.SpinUntil(end);
            release.Wait(TimeSpan.FromSeconds(30));
        };

        var key = rig.Engine.Time.GetUtcNow();
        rig.Engine.Call(() => rig.Session.NextFile());
        var elapsed = rig.Engine.Time.GetUtcNow() - key;

        elapsed.ShouldBeLessThanOrEqualTo(Budget + TimeSpan.FromMilliseconds(250), "the press waited for a listing that never came back within the budget");
        var timedOut = rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem();
        timedOut.Next.ShouldBeTrue();
        timedOut.Checked.ShouldBe(0, "no file was judged: the first listing never returned");
        rig.Events.OfType<FileSkipped>().ShouldBeEmpty("a listing that ran out of time is not a skipped file");
        rig.Events.OfType<NoNeighbour>().ShouldBeEmpty();
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
    }

    private sealed class Defer(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

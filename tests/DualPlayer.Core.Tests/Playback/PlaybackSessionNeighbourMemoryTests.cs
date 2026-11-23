using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T88 (review S1, S8, S9): the neighbour scan counts its 3 s budget from when the session starts the command; a file is
/// remembered as timed out only when its own listing or probe ran its full, uncut limit; a remembered file is keyed by
/// path + size + mtime and is retried last, so one that recovers plays; a judged file keeps its verdict and reason.
/// </summary>
public sealed class PlaybackSessionNeighbourMemoryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-memory-").FullName;

    private readonly List<ScriptedProbe> _probes = [];

    public void Dispose()
    {
        foreach (var probe in _probes)
        {
            probe.ReleaseWorkers(); // a fake held on its real-time gate by a test that failed before it released it
        }

        Directory.Delete(_folder, recursive: true);
    }

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private (SessionRig Rig, ScriptedProbe Probe) PlayingRig(string first = "Episode-01.mkv")
    {
        var probe = new ScriptedProbe();
        _probes.Add(probe);
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File(first) })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return (rig, probe);
    }

    // A press that relies on the workers of the one before it having ended (a cut check is checked again only once its worker
    // has ended) waits for them first, by the session's worker map, not by a real-time sleep.
    private static void Press(SessionRig rig)
    {
        rig.Engine.Call(() => rig.Session.NextFile());
        RigLoops.WaitForNeighbourWorkers(rig.Session);
    }

    [Fact]
    public void A_healthy_neighbour_whose_check_the_press_budget_cut_is_checked_again_and_plays_on_the_next_press_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv"); // E2 hangs its full 2 s; E3 is left 1 s of the press, but needs 1.5 s
        probe.Slow("Episode-03.mkv", TimeSpan.FromMilliseconds(1500));

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot judge E3 in the budget it had left");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv", "a healthy file cut by the budget was reported as skipped");

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press did not play the next file").File.ShouldBe(healthy, "a healthy file the budget cut was remembered as timed out and stepped over");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv");
    }

    [Fact]
    public void A_press_that_waited_behind_a_slow_file_change_still_checks_files_and_plays_the_next_one_AC_07()
    {
        var (rig, _) = PlayingRig();
        using var _ = rig;
        var next = File("Episode-02.mkv");
        var keyAt = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(400); // the key waits 4 s behind a file change, longer than the whole 3 s scan budget

        rig.Engine.Call(() => rig.Session.NextFile(keyAt)).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<NeighbourSearchTimedOut>().ShouldBeEmpty("the scan budget was counted from the key's arrival, so nothing was checked");
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldNotContain(t => t.StartsWith("Next file not found within 3 s", StringComparison.Ordinal));
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(next);
    }

    [Fact]
    public void A_remembered_file_that_answers_in_time_on_a_later_press_plays_AC_07()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        var only = File("Episode-02.mkv");
        probe.Hang("Episode-02.mkv"); // its own full limit runs out: remembered

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-02.mkv"); // the disk woke up

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a file that recovered was never given another chance").File.ShouldBe(only);
    }

    [Fact]
    public void A_remembered_file_with_a_new_modification_time_is_checked_in_its_own_place_not_last_AC_07()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        var second = File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");

        Press(rig); // E2 times out on its own, E3 is cut by the budget
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();

        System.IO.File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddHours(1)); // the copy finished: a different file now
        probe.Ok("Episode-02.mkv");
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(second);
        probe.Probed.Select(Path.GetFileName).ShouldBe(["Episode-02.mkv"], "a changed file was still treated as the remembered one and checked last");
    }

    [Fact]
    public void A_file_skipped_for_its_picture_keeps_that_reason_and_is_not_probed_again_on_the_next_press_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.NotHardwareDecodableAfterLimit("Episode-02.mkv"); // ran its whole 2 s limit, then judged: not decodable
        probe.Hang("Episode-03.mkv"); // cut by the 1 s left of the press

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a file already judged was probed again and spent budget");
        var reasons = rig.Events.OfType<FileSkipped>().Where(e => e.File == "Episode-02.mkv").Select(e => e.Reason).ToList();
        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(r => r == "not-hardware-decodable", "a skipped file was later reported with another reason");
    }

    // Both outputs are lost one after the other, which ends the film (AC-15b); the devices are still listed, so a return retries them.
    private static void LoseEveryOutput(SessionRig rig)
    {
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();
        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void A_file_judged_not_hardware_decodable_is_not_probed_again_after_every_output_was_lost_and_the_return_into_the_film_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.NotHardwareDecodableAfterLimit("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        LoseEveryOutput(rig);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a return into the same film forgot the verdict and spent budget on the file again");
    }

    [Fact]
    public void A_file_change_whose_load_fails_keeps_the_judged_files_and_they_are_not_probed_again_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.NotHardwareDecodableAfterLimit("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-03.mkv");
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse(); // plays E3, which does not load
        rig.Renderer.LoadError = null;
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.PreviousFile()); // from E3: E2 first

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a failed file change forgot the verdict and spent budget on the file again");
    }

    [Fact]
    public void A_only_neighbour_whose_check_runs_out_its_limit_on_every_press_ends_the_search_timed_out_not_with_no_neighbour_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        probe.Hang("Episode-02.mkv"); // runs its full 2 s limit and ends on cancel, every time

        Press(rig);
        Press(rig);

        rig.Events.OfType<NeighbourSearchTimedOut>().Select(e => e.Checked).ShouldBe([1, 1], "a probe that ran out its full limit is counted as checked on every press");
        rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("a file that was never judged was reported as no neighbour");
    }

    [Fact]
    public void A_file_remembered_as_timed_out_is_still_checked_last_after_a_file_change_whose_load_fails_AC_09b()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig); // E2 runs out its limit: remembered; E3 is cut
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-03.mkv");
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse(); // plays E3, which does not load
        rig.Renderer.LoadError = null;
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.PreviousFile()); // from E3: E1 first, E2 last

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a failed file change forgot the timed-out memory");
    }

    [Fact]
    public void A_file_remembered_as_timed_out_is_still_checked_last_after_every_output_was_lost_and_the_return_AC_09b()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig); // E2 runs out its limit: remembered; E3 is cut
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-03.mkv");
        LoseEveryOutput(rig);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a return into the same film forgot the timed-out memory");
    }

    [Fact]
    public void A_cut_healthy_neighbour_whose_probe_ends_on_cancel_plays_on_a_press_queued_right_after_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv"); // E2 hangs its full 2 s; E3 is left 1 s of the press
        probe.SlowPastItsLimit("Episode-03.mkv", TimeSpan.FromMilliseconds(1500)); // needs 1.5 s, ends only once cancelled

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile()); // queued at once: no wait for the first press's workers

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press did not play the next file").File.ShouldBe(healthy, "a cut worker that ended on cancel was kept, so the next press stepped past its healthy file");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv", "a healthy file cut by the budget was reported as skipped");
    }

    [Fact]
    public void A_cut_healthy_neighbour_whose_adapter_probe_has_returned_but_is_still_winding_down_plays_on_a_press_queued_right_after_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv"); // E2 hangs its full 2 s; E3 is left 1 s of the press
        probe.AdapterShaped("Episode-03.mkv", TimeSpan.FromMilliseconds(1500)); // needs 1.5 s: returns video.probe_timeout at its cut limit, but its worker is busy for a moment after

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile()); // queued at once: no wait for the first press's workers

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press did not play the next file").File.ShouldBe(healthy, "the adapter's still-running worker made the session step past its healthy file");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv", "a healthy file cut by the budget was reported as skipped");
    }

    [Fact]
    public void A_cut_neighbour_whose_worker_ends_inside_the_50_ms_grace_plays_on_a_press_queued_right_after_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        using var releaseOnExit = new Defer(probe.ReleaseWorkers); // a held worker must not outlive a failed test
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv");
        probe.SlowPastItsLimit("Episode-03.mkv", TimeSpan.FromMilliseconds(1500), windDown: TimeSpan.FromMilliseconds(20));

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a wind-down inside the grace was kept").File.ShouldBe(healthy);
    }

    [Fact]
    public void A_cut_neighbour_whose_worker_outlasts_the_50_ms_grace_is_stepped_past_neither_judged_nor_remembered_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        using var releaseOnExit = new Defer(probe.ReleaseWorkers); // a held worker must not outlive a failed test
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        var fourth = File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv");
        probe.SlowPastItsLimitOutlastingTheGrace("Episode-03.mkv", TimeSpan.FromMilliseconds(1500));

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a worker still running after the grace did not make the press step past its file").File.ShouldBe(fourth);
        rig.Events.OfType<FileSkipped>().Where(e => e.File == "Episode-03.mkv").ShouldAllBe(e => e.Reason == "unreadable", "a file whose worker outlasted the grace was judged, not just stepped past");
        probe.ReleaseWorkers(); // the wind-down ends
        RigLoops.WaitForNeighbourWorkers(rig.Session);
        rig.Engine.Call(() => rig.Session.PreviousFile()); // from E4: E3 first, probed again
        rig.Events.OfType<FileChanged>().Last().File.ShouldBe(healthy, "a file whose worker outlasted the grace was remembered");
    }

    [Fact]
    public void A_cut_neighbour_whose_adapter_worker_ends_inside_the_50_ms_grace_plays_on_a_press_queued_right_after_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv");
        probe.AdapterShaped("Episode-03.mkv", TimeSpan.FromMilliseconds(1500), windDown: TimeSpan.FromMilliseconds(20));

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a wind-down inside the grace was kept").File.ShouldBe(healthy);
    }

    [Fact]
    public void A_cut_neighbour_whose_adapter_worker_outlasts_the_50_ms_grace_is_stepped_past_neither_judged_nor_remembered_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        var fourth = File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv");
        probe.AdapterShapedOutlastingTheGrace("Episode-03.mkv", TimeSpan.FromMilliseconds(1500));

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a worker still running after the grace did not make the press step past its file").File.ShouldBe(fourth);
        rig.Events.OfType<FileSkipped>().Where(e => e.File == "Episode-03.mkv").ShouldAllBe(e => e.Reason == "unreadable", "a file whose worker outlasted the grace was judged, not just stepped past");
        probe.ReleaseWorkers(); // the wind-down ends
        rig.Engine.Call(() => rig.Session.PreviousFile());
        rig.Events.OfType<FileChanged>().Last().File.ShouldBe(healthy, "a file whose worker outlasted the grace was remembered");
    }

    private sealed class Defer(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    /// <summary>
    /// Watches how often the session's control thread reads the rig clock while it waits in a give-up grace, so a fake can hold a
    /// worker's end until the session has looked at the clock (and kept looking) rather than race it. The session thread is the one
    /// that cancelled the worker's token (the cancel callback runs on it, see <see cref="WatchThisThread"/>); a read counts only when
    /// it is made by the grace wait (<c>AwaitGrace</c>, found by its frame as the rig finds <c>ChangeFile</c>), not by the sync and
    /// health ticks the same thread runs between commands.
    /// </summary>
    private sealed class GraceClockReads(CountingTimeProvider time)
    {
        private readonly ManualResetEventSlim _enough = new();
        private int _thread;
        private long _from;
        private int _wanted;
        private int _reads;

        public void WatchThisThread() => Volatile.Write(ref _thread, Environment.CurrentManagedThreadId);

        /// <summary>
        /// Waits (real time) until the session thread has read the clock in its grace, at or after <paramref name="from"/>,
        /// <paramref name="count"/> times from now on. False when <paramref name="abandon"/> was set first (the test is over).
        /// </summary>
        public bool WaitForReads(long from, int count, ManualResetEventSlim abandon, TimeSpan failAfter)
        {
            Volatile.Write(ref _from, from);
            Volatile.Write(ref _wanted, count);
            Volatile.Write(ref _reads, 0);
            _enough.Reset();
            time.BeforeRead = OnRead;
            try
            {
                var which = WaitHandle.WaitAny([_enough.WaitHandle, abandon.WaitHandle], failAfter);
                return which == WaitHandle.WaitTimeout
                    ? throw new TimeoutException("The session never polled the clock in its give-up grace.")
                    : which == 0;
            }
            finally
            {
                time.BeforeRead = null;
            }
        }

        private void OnRead()
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref _thread)
                && new System.Diagnostics.StackTrace(false).GetFrames().Any(f => f.GetMethod()?.Name == "AwaitGrace")
                && time.GetTimestamp() >= Volatile.Read(ref _from)
                && Interlocked.Increment(ref _reads) >= Volatile.Read(ref _wanted))
            {
                _enough.Set();
            }
        }
    }

    /// <summary>Per-file behaviour on the session's fake time; a file with none probes fine at once.</summary>
    private sealed class ScriptedProbe : IPictureProbe
    {
        // The session's grace is 50 ms; a worker that must outlast it spends this much fake time (grace + 10 ms) after the cancel.
        private static readonly TimeSpan OutlastsTheGrace = TimeSpan.FromMilliseconds(60);

        // A wind-down inside the grace ends only after the session has polled the clock this many times, in the grace, at or after the wind-down's end.
        private const int PollsInsideTheGrace = 3;

        // A failure bound for the real-time waits below, never a pass condition.
        private static readonly TimeSpan FailAfter = TimeSpan.FromSeconds(30);

        private readonly Lock _gate = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly HashSet<string> _outlasting = new(StringComparer.Ordinal);
        private readonly HashSet<string> _heldBusy = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (TimeSpan Cost, string? Code)> _behaviours = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pastLimit = new(StringComparer.Ordinal);
        private readonly HashSet<string> _adapterShaped = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _busyUntil = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TimeSpan> _windDowns = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _busyUntilFake = new(StringComparer.Ordinal);

        public TimeProvider? Time { get; set; }

        public List<string> Probed { get; } = [];

        /// <summary>
        /// Shaped like the production adapter: it spends its limit on fake time (or until cancelled), then returns
        /// <c>video.probe_timeout</c> at once (when its limit is shorter than its cost), but its own worker is still running (<see cref="IsBusy"/> is true) for a short real
        /// time afterwards. Real time, not fake: fake time stands still once the probe has returned, so the grace must wait on the
        /// adapter's answer, not on the clock.
        /// </summary>
        public void AdapterShaped(string name, TimeSpan cost)
        {
            Set(name, cost, null);
            lock (_gate)
            {
                _adapterShaped.Add(name);
            }
        }

        /// <summary>
        /// As <see cref="AdapterShaped"/>, but the worker's wind-down after the answer is <paramref name="windDown"/> of FAKE time spent
        /// inside a waiting scope (a helper thread holds the scope), so the rig moves the clock during the session's give-up grace.
        /// </summary>
        public void AdapterShaped(string name, TimeSpan cost, TimeSpan windDown)
        {
            AdapterShaped(name, cost);
            lock (_gate)
            {
                _windDowns[name] = windDown;
            }
        }

        /// <summary>
        /// As <see cref="SlowPastItsLimit(string, TimeSpan)"/>, but after a cancel the probe takes <paramref name="windDown"/> of FAKE
        /// time (inside a waiting scope) to end, instead of 200 ms of real time. The wind-down starts once the session has begun polling
        /// its grace and ends only after the session, still waiting in the grace, has polled the clock at or after its end
        /// several times: a session whose grace is shorter than the wind-down leaves at the first such poll, with the worker still running.
        /// </summary>
        public void SlowPastItsLimit(string name, TimeSpan cost, TimeSpan windDown)
        {
            SlowPastItsLimit(name, cost);
            lock (_gate)
            {
                _windDowns[name] = windDown;
            }
        }

        /// <summary>
        /// As <see cref="SlowPastItsLimit(string, TimeSpan, TimeSpan)"/>, but the probe's wind-down outlasts the session's grace: after
        /// the cancel it spends grace + 10 ms of fake time, so the clock is past the grace's end whenever the session reads it, then
        /// holds the worker on a real-time gate until <see cref="ReleaseWorkers"/>. How late the session thread wakes cannot matter.
        /// </summary>
        public void SlowPastItsLimitOutlastingTheGrace(string name, TimeSpan cost)
        {
            SlowPastItsLimit(name, cost);
            lock (_gate)
            {
                _outlasting.Add(name);
            }
        }

        /// <summary>
        /// As <see cref="AdapterShaped(string, TimeSpan)"/>, but the adapter's worker outlasts the session's grace: after the answer it
        /// spends grace + 10 ms of fake time on its own thread and stays busy (<see cref="IsBusy"/>) until <see cref="ReleaseWorkers"/>.
        /// </summary>
        public void AdapterShapedOutlastingTheGrace(string name, TimeSpan cost)
        {
            AdapterShaped(name, cost);
            lock (_gate)
            {
                _outlasting.Add(name);
            }
        }

        /// <summary>Lets the workers held by <see cref="SlowPastItsLimitOutlastingTheGrace"/> and <see cref="AdapterShapedOutlastingTheGrace"/> end.</summary>
        public void ReleaseWorkers() => _release.Set();

        public bool IsBusy(string path)
        {
            lock (_gate)
            {
                var name = Path.GetFileName(path);
                if (_heldBusy.Contains(name) && !_release.IsSet)
                {
                    return true;
                }

                if (_busyUntilFake.TryGetValue(name, out var untilFake))
                {
                    // Busy for the fake wind-down AND for a short real time: the rig moves fake time faster than real, so a grace
                    // cut short by the clock (too short a slack) ends while the worker is still running.
                    return Time!.GetTimestamp() < untilFake || (_busyUntil.TryGetValue(name, out var real) && Environment.TickCount64 < real);
                }

                return _busyUntil.TryGetValue(name, out var until) && Environment.TickCount64 < until;
            }
        }

        /// <summary>Takes its whole limit, then fails as a probe that gave up.</summary>
        public void Hang(string name) => Set(name, TimeSpan.MaxValue, "video.probe_timeout");

        /// <summary>Takes its whole limit, then fails as a picture this PC cannot decode.</summary>
        public void NotHardwareDecodableAfterLimit(string name) => Set(name, TimeSpan.MaxValue, "video.not_hardware_decodable");

        /// <summary>Succeeds after <paramref name="cost"/>, but fails after the limit when the limit is shorter.</summary>
        public void Slow(string name, TimeSpan cost) => Set(name, cost, null);

        public void Ok(string name) => Set(name, TimeSpan.Zero, null);

        /// <summary>
        /// Succeeds after <paramref name="cost"/> whatever its limit; a cancel stops it, and it then takes a moment to close the file
        /// (real time, while fake time stands still), so it is still running when the session gives up on it.
        /// </summary>
        public void SlowPastItsLimit(string name, TimeSpan cost)
        {
            Set(name, cost, null);
            lock (_gate)
            {
                _pastLimit.Add(name);
            }
        }

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
        {
            (TimeSpan Cost, string? Code) behaviour;
            bool pastLimit;
            bool adapterShaped;
            lock (_gate)
            {
                Probed.Add(path);
                adapterShaped = _adapterShaped.Contains(Path.GetFileName(path));
                behaviour = _behaviours.GetValueOrDefault(Path.GetFileName(path), (TimeSpan.Zero, null));
                pastLimit = _pastLimit.Contains(Path.GetFileName(path));
            }

            if (adapterShaped)
            {
                // A limit that covers its cost: the probe succeeds. A shorter one: it spends the limit, answers timeout, and its worker winds down.
                var fits = behaviour.Cost <= timeLimit;
                var answerAfter = fits ? behaviour.Cost : timeLimit;
                var clock = Time!;
                clock.SpinUntil(clock.GetTimestamp() + (long)(answerAfter.TotalSeconds * clock.TimestampFrequency), cancel);
                if (fits && !cancel.IsCancellationRequested)
                {
                    return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
                }

                TimeSpan? fakeWindDown;
                bool outlasting;
                lock (_gate)
                {
                    outlasting = _outlasting.Contains(Path.GetFileName(path));
                    if (outlasting)
                    {
                        _heldBusy.Add(Path.GetFileName(path));
                    }

                    // An outlasting worker is busy until the test releases it; its fake wind-down below only moves the clock past the grace.
                    fakeWindDown = outlasting ? OutlastsTheGrace : _windDowns.TryGetValue(Path.GetFileName(path), out var w) ? w : null;
                    if (fakeWindDown is null)
                    {
                        _busyUntil[Path.GetFileName(path)] = Environment.TickCount64 + 60; // its worker winds down after the answer
                    }
                    else
                    {
                        _busyUntilFake[Path.GetFileName(path)] = clock.GetTimestamp() + (long)(fakeWindDown.Value.TotalSeconds * clock.TimestampFrequency);
                        _busyUntil[Path.GetFileName(path)] = Environment.TickCount64 + (long)Math.Min(fakeWindDown.Value.TotalMilliseconds, 20);
                    }
                }

                if (fakeWindDown is { } windDown)
                {
                    // The worker's wind-down runs on its own thread, in a waiting scope, so fake time moves while it lasts.
                    var until = clock.GetTimestamp() + (long)(windDown.TotalSeconds * clock.TimestampFrequency);
                    _ = Task.Factory.StartNew(() => clock.SpinUntil(until, CancellationToken.None), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                return Result<PictureInfo>.Failure("video.probe_timeout", "The probe ran out its limit.");
            }

            if (pastLimit)
            {
                var time = Time!;
                var watch = new GraceClockReads((CountingTimeProvider)time);
                using var watching = cancel.CanBeCanceled && !cancel.IsCancellationRequested ? cancel.Register(watch.WatchThisThread) : default;
                // Never spends fake time past the session's give-up (its limit + the 50 ms slack): the rig moves fake time while this
                // thread waits for the CPU, so the deadline is the scope's own; then it waits in real time for the cancel.
                var spend = TimeSpan.FromTicks(Math.Min(behaviour.Cost.Ticks, (timeLimit + TimeSpan.FromMilliseconds(50)).Ticks));
                time.SpinUntil(time.GetTimestamp() + (long)(spend.TotalSeconds * time.TimestampFrequency), cancel);
                if (spend < behaviour.Cost && !cancel.IsCancellationRequested)
                {
                    cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                }

                if (cancel.IsCancellationRequested)
                {
                    TimeSpan windDown;
                    bool winds;
                    bool outlasts;
                    lock (_gate)
                    {
                        winds = _windDowns.TryGetValue(Path.GetFileName(path), out windDown);
                        outlasts = _outlasting.Contains(Path.GetFileName(path));
                    }

                    if (outlasts)
                    {
                        // The clock stands still from the cancel until this thread spends fake time: let the session read it for its
                        // grace first (its second read in the grace follows the one that fixes the grace's end), so the grace ends
                        // exactly 50 ms after the instant this wind-down starts from.
                        watch.WaitForReads(0, 2, _release, FailAfter);
                        time.SpinUntil(time.GetTimestamp() + (long)(OutlastsTheGrace.TotalSeconds * time.TimestampFrequency), CancellationToken.None);
                        _release.Wait(FailAfter); // closes the file only when the test says so
                    }
                    else if (winds)
                    {
                        // Wind-down inside the grace: the worker may end only once the session, still waiting in its grace, has polled
                        // the clock at or after the wind-down's end several times. A session whose grace is shorter than the wind-down
                        // stops waiting at the first such read, so it never polls that often and its worker is still kept.
                        watch.WaitForReads(0, 2, _release, FailAfter);
                        var windDownEnd = time.GetTimestamp() + (long)(windDown.TotalSeconds * time.TimestampFrequency);
                        time.SpinUntil(windDownEnd, CancellationToken.None); // closes the file after the cancel, on fake time
                        watch.WaitForReads(windDownEnd, PollsInsideTheGrace, _release, FailAfter);
                    }
                    else
                    {
                        Thread.Sleep(200); // closes the file after the cancel
                    }

                    return Result<PictureInfo>.Failure("video.probe_timeout", "The probe was cancelled.");
                }

                return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
            }

            var spent = behaviour.Cost < timeLimit ? behaviour.Cost : timeLimit;
            var end = Time!.GetTimestamp() + (long)(spent.TotalSeconds * Time.TimestampFrequency);
            Time.SpinUntil(end, cancel);

            if (behaviour.Cost <= timeLimit && behaviour.Code is null)
            {
                return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
            }

            return Result<PictureInfo>.Failure(behaviour.Code ?? "video.probe_timeout", "The probe gave up.");
        }

        private void Set(string name, TimeSpan cost, string? code)
        {
            lock (_gate)
            {
                _behaviours[name] = (cost, code);
            }
        }
    }
}
